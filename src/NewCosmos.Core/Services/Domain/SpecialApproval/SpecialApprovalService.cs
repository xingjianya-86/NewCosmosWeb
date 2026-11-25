using NewCosmos.Constants;
using NewCosmos.Models.Requests;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.SocialAssistance;

namespace NewCosmos.Services.Domain.SpecialApproval;

/// <summary>
/// 一事一议 / 特殊审批服务实现。
/// <para>
/// 列事实来自 <c>WideTableColumns.SpecialApprovalFormsColumns</c> 与
/// <c>SpecialApprovalsColumns</c>（仅使用旧版 Schema 与 Web WideTableColumns 都存在的列）：
/// 申报内容在 nc_biz_special_approval_forms，会议审议结果在 nc_biz_special_approvals；
/// 表结构没有审批结果列，通过/驳回由申报表 status = Approved/Rejected 表达。
/// </para>
/// <para>所有 SQL 均使用 $1..$n 位置参数、明确列名与事务；不新增数据库列。</para>
/// </summary>
public class SpecialApprovalService : BaseService, ISpecialApprovalService
{
    protected override string ServiceName => "SpecialApprovalService";

    private readonly IDatabaseService _db;
    private readonly IApplicationService _applicationService;

    public SpecialApprovalService(
        IDatabaseService db,
        IApplicationService applicationService,
        ILoggerService logger) : base(logger)
    {
        _db = db;
        _applicationService = applicationService;
    }

    /// <summary>
    /// 申报表 + 最近一条会议审议结果。用 LATERAL 一次取回，避免列表 N+1。
    /// 仅查询两张表在旧版 Schema 与 Web WideTableColumns 中共有的列。
    /// </summary>
    private const string RecordSelectSql = @"
        SELECT f.id, f.application_id, f.form_no, f.basic_situation, f.special_matters,
               f.template_key, f.report_unit, f.handler_name, f.form_date,
               f.audit_result, f.audit_date, f.status, f.created_by,
               f.created_at, f.updated_at,
               sa.id AS special_approval_id,
               sa.meeting_date, sa.meeting_members,
               sa.override_classification,
               sa.approver_name, sa.approver_title, sa.approver_organization,
               sa.special_reason, sa.special_circumstances,
               sa.created_by AS review_created_by,
               sa.created_at AS reviewed_at
        FROM nc_biz_special_approval_forms f
        LEFT JOIN LATERAL (
            SELECT sa.id, sa.meeting_date, sa.meeting_members,
                   sa.override_classification,
                   sa.approver_name, sa.approver_title, sa.approver_organization,
                   sa.special_reason, sa.special_circumstances,
                   sa.created_by, sa.created_at
            FROM nc_biz_special_approvals sa
            WHERE sa.application_id = f.application_id
              AND sa.deleted_at IS NULL
            ORDER BY sa.id DESC
            LIMIT 1
        ) sa ON TRUE";

    public async Task<Result<SpecialApprovalRecord>> CreateAsync(
        SpecialApprovalSaveRequest request,
        string operatorName,
        CancellationToken ct = default)
    {
        if (request is null)
            return Fail<SpecialApprovalRecord>(ErrorCodes.VALIDATION_FAILED, "请求体不能为空");
        if (request.ApplicationId <= 0)
            return Fail<SpecialApprovalRecord>(ErrorCodes.VALIDATION_FAILED, "申请 ID 无效");
        if (string.IsNullOrWhiteSpace(request.SpecialMatters))
            return Fail<SpecialApprovalRecord>(ErrorCodes.REQUIRED_FIELD_MISSING, "请填写一事一议说明情况");

        var actor = NormalizeActor(operatorName);

        try
        {
            var appResult = await LoadApplicationAsync(request.ApplicationId, ct);
            if (appResult.IsFailure)
                return Fail<SpecialApprovalRecord>(appResult.ErrorCode, appResult.Message);
            var app = appResult.Value;

            await using var tx = await _db.BeginTransactionScopeAsync(ct);

            var existing = await LoadFormBriefByApplicationAsync(request.ApplicationId, ct);
            if (existing is not null)
                return Fail<SpecialApprovalRecord>(
                    ErrorCodes.SPECIAL_APPROVAL_ALREADY_EXISTS, "该申请已有一事一议申报表");

            var formNo = SpecialApprovalConstants.BuildFormNo(
                app.ApplicationNo, Random.Shared.Next(1, 9999));

            const string insertSql = @"
                INSERT INTO nc_biz_special_approval_forms
                    (application_id, form_no, basic_situation, special_matters, template_key,
                     report_unit, handler_name, form_date, audit_result, audit_date,
                     status, created_by, created_at, updated_at)
                VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,NOW(),NOW())
                RETURNING id";

            var insertResult = await _db.ExecuteScalarAsync<long?>(insertSql, ct,
                request.ApplicationId,
                formNo,
                request.BasicSituation ?? string.Empty,
                request.SpecialMatters,
                request.TemplateKey ?? string.Empty,
                request.ReportUnit ?? string.Empty,
                request.HandlerName ?? string.Empty,
                request.FormDate,
                request.AuditResult ?? string.Empty,
                request.AuditDate,
                SpecialApprovalConstants.StatusDraft,
                actor);

            if (insertResult.IsFailure)
            {
                return insertResult.ErrorCode == ErrorCodes.DB_UNIQUE_VIOLATION
                    ? Fail<SpecialApprovalRecord>(
                        ErrorCodes.SPECIAL_APPROVAL_ALREADY_EXISTS, "该申请已有一事一议申报表")
                    : Fail<SpecialApprovalRecord>(insertResult.ErrorCode, insertResult.Message);
            }

            if (insertResult.Value is not > 0)
                return Fail<SpecialApprovalRecord>(ErrorCodes.DB_QUERY_ERROR, "创建申报表失败");

            var formId = insertResult.Value!.Value;
            await tx.CommitAsync(ct);

            LogInfo($"一事一议申报表已创建: FormId={formId}, ApplicationId={request.ApplicationId}");
            return await GetByIdAsync(formId, ct);
        }
        catch (Exception ex)
        {
            LogException(ex, "创建一事一议申报表");
            return Result.FromException<SpecialApprovalRecord>(ex);
        }
    }

    public async Task<Result<SpecialApprovalRecord>> GetByIdAsync(long id, CancellationToken ct = default)
    {
        if (id <= 0)
            return Fail<SpecialApprovalRecord>(ErrorCodes.VALIDATION_FAILED, "申报表 ID 无效");

        var result = await _db.QuerySingleAsync<SpecialApprovalRecord>(
            RecordSelectSql + " WHERE f.id = $1 AND f.deleted_at IS NULL LIMIT 1", ct, id);

        if (result.IsFailure)
            return Fail<SpecialApprovalRecord>(result.ErrorCode, result.Message);
        if (result.Value is not null)
            return Result.Success(result.Value);

        // 兼容：前端若拿 nc_biz_applications.special_approval_id（会议审议 id）
        // 直接访问 /special-approvals/{id}，按会议记录反查所属申请再取申报表。
        var applicationIdResult = await _db.ExecuteScalarAsync<long?>(
            "SELECT application_id FROM nc_biz_special_approvals " +
            "WHERE id = $1 AND deleted_at IS NULL LIMIT 1", ct, id);

        if (applicationIdResult.IsFailure)
            return Fail<SpecialApprovalRecord>(
                applicationIdResult.ErrorCode, applicationIdResult.Message);

        if (applicationIdResult.Value is not > 0)
            return Fail<SpecialApprovalRecord>(
                ErrorCodes.SPECIAL_APPROVAL_FORM_NOT_FOUND, "未找到一事一议申报表");

        return await GetByApplicationIdAsync(applicationIdResult.Value!.Value, ct);
    }

    public async Task<Result<SpecialApprovalRecord>> GetByApplicationIdAsync(
        long applicationId,
        CancellationToken ct = default)
    {
        if (applicationId <= 0)
            return Fail<SpecialApprovalRecord>(ErrorCodes.VALIDATION_FAILED, "申请 ID 无效");

        var result = await _db.QuerySingleAsync<SpecialApprovalRecord>(
            RecordSelectSql + @"
            WHERE f.application_id = $1 AND f.deleted_at IS NULL
            ORDER BY f.id DESC
            LIMIT 1", ct, applicationId);

        if (result.IsFailure)
            return Fail<SpecialApprovalRecord>(result.ErrorCode, result.Message);
        if (result.Value is null)
            return Fail<SpecialApprovalRecord>(
                ErrorCodes.SPECIAL_APPROVAL_FORM_NOT_FOUND, "未找到一事一议申报表");

        return Result.Success(result.Value);
    }

    public async Task<Result<List<SpecialApprovalRecord>>> ListByApplicationAsync(
        long applicationId,
        CancellationToken ct = default)
    {
        if (applicationId <= 0)
            return Result.Failure<List<SpecialApprovalRecord>>(
                ErrorCodes.VALIDATION_FAILED, "申请 ID 无效");

        var result = await _db.QueryAsync<SpecialApprovalRecord>(
            RecordSelectSql + @"
            WHERE f.application_id = $1 AND f.deleted_at IS NULL
            ORDER BY f.created_at DESC
            LIMIT 20", ct, applicationId);

        return result.IsFailure
            ? Result.Failure<List<SpecialApprovalRecord>>(result.ErrorCode, result.Message)
            : Result.Success(result.Value ?? new List<SpecialApprovalRecord>());
    }

    public async Task<Result<SpecialApprovalRecord>> UpdateAsync(
        long id,
        SpecialApprovalSaveRequest request,
        string operatorName,
        CancellationToken ct = default)
    {
        if (request is null)
            return Fail<SpecialApprovalRecord>(ErrorCodes.VALIDATION_FAILED, "请求体不能为空");
        if (string.IsNullOrWhiteSpace(request.SpecialMatters))
            return Fail<SpecialApprovalRecord>(ErrorCodes.REQUIRED_FIELD_MISSING, "请填写一事一议说明情况");

        try
        {
            await using var tx = await _db.BeginTransactionScopeAsync(ct);

            var form = await LoadFormBriefAsync(id, ct);
            if (form is null)
                return Fail<SpecialApprovalRecord>(
                    ErrorCodes.SPECIAL_APPROVAL_FORM_NOT_FOUND, "未找到一事一议申报表");
            if (IsFinalStatus(form.Status))
                return Fail<SpecialApprovalRecord>(
                    ErrorCodes.SPECIAL_APPROVAL_REVIEW_DONE, "申报表已完成会议审议，无法修改");

            const string updateSql = @"
                UPDATE nc_biz_special_approval_forms SET
                    basic_situation = $1, special_matters = $2, template_key = $3,
                    report_unit = $4, handler_name = $5, form_date = $6,
                    audit_result = $7, audit_date = $8, updated_at = NOW()
                WHERE id = $9 AND deleted_at IS NULL";

            var updateResult = await _db.ExecuteNonQueryAsync(updateSql, ct,
                request.BasicSituation ?? string.Empty,
                request.SpecialMatters,
                request.TemplateKey ?? string.Empty,
                request.ReportUnit ?? string.Empty,
                request.HandlerName ?? string.Empty,
                request.FormDate,
                request.AuditResult ?? string.Empty,
                request.AuditDate,
                id);

            if (updateResult.IsFailure)
                return Fail<SpecialApprovalRecord>(updateResult.ErrorCode, updateResult.Message);
            if (updateResult.Value == 0)
                return Fail<SpecialApprovalRecord>(
                    ErrorCodes.SPECIAL_APPROVAL_FORM_NOT_FOUND, "未找到一事一议申报表");

            await tx.CommitAsync(ct);

            LogInfo($"一事一议申报表已更新: FormId={id}, Operator={NormalizeActor(operatorName)}");
            return await GetByIdAsync(id, ct);
        }
        catch (Exception ex)
        {
            LogException(ex, "更新一事一议申报表");
            return Result.FromException<SpecialApprovalRecord>(ex);
        }
    }

    public async Task<Result<SpecialApprovalRecord>> SubmitAsync(
        long id,
        string operatorName,
        CancellationToken ct = default)
    {
        try
        {
            await using var tx = await _db.BeginTransactionScopeAsync(ct);

            var form = await LoadFormBriefAsync(id, ct);
            if (form is null)
                return Fail<SpecialApprovalRecord>(
                    ErrorCodes.SPECIAL_APPROVAL_FORM_NOT_FOUND, "未找到一事一议申报表");
            if (IsFinalStatus(form.Status))
                return Fail<SpecialApprovalRecord>(
                    ErrorCodes.SPECIAL_APPROVAL_REVIEW_DONE, "申报表已完成会议审议，无法重复提交");
            if (form.Status != SpecialApprovalConstants.StatusDraft)
                return Fail<SpecialApprovalRecord>(
                    ErrorCodes.SPECIAL_APPROVAL_ALREADY_SUBMITTED, "申报表已提交，无法重复提交");

            const string sql = @"
                UPDATE nc_biz_special_approval_forms SET
                    status = $1, updated_at = NOW()
                WHERE id = $2 AND deleted_at IS NULL AND status = $3";

            var result = await _db.ExecuteNonQueryAsync(sql, ct,
                SpecialApprovalConstants.StatusSubmitted,
                id,
                SpecialApprovalConstants.StatusDraft);

            if (result.IsFailure)
                return Fail<SpecialApprovalRecord>(result.ErrorCode, result.Message);
            if (result.Value == 0)
                return Fail<SpecialApprovalRecord>(
                    ErrorCodes.CONCURRENCY_CONFLICT, "申报表状态已变化，请刷新后重试");

            await tx.CommitAsync(ct);

            LogInfo($"一事一议申报表已提交: FormId={id}, ApplicationId={form.ApplicationId}");
            return await GetByIdAsync(id, ct);
        }
        catch (Exception ex)
        {
            LogException(ex, "提交一事一议申报表");
            return Result.FromException<SpecialApprovalRecord>(ex);
        }
    }

    public async Task<Result<SpecialApprovalRecord>> ReviewAsync(
        long id,
        SpecialApprovalReviewRequest request,
        string operatorName,
        CancellationToken ct = default)
    {
        if (request is null)
            return Fail<SpecialApprovalRecord>(ErrorCodes.VALIDATION_FAILED, "请求体不能为空");

        var actor = NormalizeActor(operatorName);

        try
        {
            await using var tx = await _db.BeginTransactionScopeAsync(ct);

            var form = await LoadFormBriefAsync(id, ct);
            if (form is null)
                return Fail<SpecialApprovalRecord>(
                    ErrorCodes.SPECIAL_APPROVAL_FORM_NOT_FOUND, "未找到一事一议申报表");
            if (IsFinalStatus(form.Status))
                return Fail<SpecialApprovalRecord>(
                    ErrorCodes.SPECIAL_APPROVAL_REVIEW_DONE, "该申报表已完成会议审议");

            var appResult = await LoadApplicationAsync(form.ApplicationId, ct);
            if (appResult.IsFailure)
                return Fail<SpecialApprovalRecord>(appResult.ErrorCode, appResult.Message);
            var app = appResult.Value;

            var classification = string.Empty;
            if (request.Approved)
            {
                classification = ClassificationConstants.NormalizeCodeByHukou(
                    (request.Classification ?? string.Empty).Trim(),
                    app.HukouType ?? string.Empty);

                if (!SpecialApprovalConstants.IsValidOverrideClassification(classification))
                    return Fail<SpecialApprovalRecord>(
                        ErrorCodes.INVALID_CLASSIFICATION, "请选择有效的指定救助分类");
            }

            var meetingDate = request.MeetingDate ?? DateTime.Today;

            const string insertApprovalSql = @"
                INSERT INTO nc_biz_special_approvals
                    (application_id, meeting_date, meeting_members,
                     special_reason, special_circumstances, override_classification,
                     approver_name, approver_title, approver_organization,
                     created_by, created_at, updated_at)
                VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,NOW(),NOW())
                RETURNING id";

            var insertResult = await _db.ExecuteScalarAsync<long?>(insertApprovalSql, ct,
                form.ApplicationId,
                meetingDate,
                request.Attendees ?? string.Empty,
                form.SpecialMatters ?? string.Empty,
                form.SpecialMatters ?? string.Empty,
                classification,
                request.ApproverName ?? string.Empty,
                request.ApproverPosition ?? string.Empty,
                request.ApproverUnit ?? string.Empty,
                actor);

            if (insertResult.IsFailure)
                return Fail<SpecialApprovalRecord>(insertResult.ErrorCode, insertResult.Message);
            if (insertResult.Value is not > 0)
                return Fail<SpecialApprovalRecord>(ErrorCodes.DB_QUERY_ERROR, "保存会议审议结果失败");

            var specialApprovalId = insertResult.Value!.Value;
            var formStatus = request.Approved
                ? SpecialApprovalConstants.StatusApproved
                : SpecialApprovalConstants.StatusRejected;

            const string updateFormSql = @"
                UPDATE nc_biz_special_approval_forms SET
                    status = $1, audit_result = $2, audit_date = $3, updated_at = NOW()
                WHERE id = $4 AND deleted_at IS NULL";

            var updateFormResult = await _db.ExecuteNonQueryAsync(updateFormSql, ct,
                formStatus,
                request.Approved ? "同意纳入" : "不同意纳入",
                meetingDate,
                id);

            if (updateFormResult.IsFailure)
                return Fail<SpecialApprovalRecord>(updateFormResult.ErrorCode, updateFormResult.Message);
            if (updateFormResult.Value == 0)
                return Fail<SpecialApprovalRecord>(
                    ErrorCodes.SPECIAL_APPROVAL_FORM_NOT_FOUND, "未找到一事一议申报表");

            if (request.Approved)
            {
                // 与旧版一致：只回写指定分类与一事一议标记，不迁移申请状态；
                // 申请仍交回档案制作 → 完成归档管线。
                const string updateAppSql = @"
                    UPDATE nc_biz_applications SET
                        classification_result = $1, is_eligible = TRUE,
                        is_special_approval = TRUE, special_approval_id = $2,
                        updated_by = $3, updated_at = NOW()
                    WHERE id = $4 AND deleted_at IS NULL";

                var updateAppResult = await _db.ExecuteNonQueryAsync(updateAppSql, ct,
                    classification, specialApprovalId, actor, form.ApplicationId);

                if (updateAppResult.IsFailure)
                    return Fail<SpecialApprovalRecord>(updateAppResult.ErrorCode, updateAppResult.Message);
                if (updateAppResult.Value == 0)
                    return Fail<SpecialApprovalRecord>(
                        ErrorCodes.APPLICATION_NOT_FOUND, "申请不存在");
            }
            else
            {
                // 驳回：走 Core 申请状态机（Draft/Submitted → Refused），不在此另写流转。
                var refuseResult = await _applicationService.ApproveAsync(
                    new ApplicationApproveRequest
                    {
                        ApplicationId = form.ApplicationId,
                        Approved = false,
                        ApprovalComment = "一事一议审议驳回",
                        ApprovedBy = actor
                    }, ct);

                if (refuseResult.IsFailure)
                    return Fail<SpecialApprovalRecord>(refuseResult.ErrorCode, refuseResult.Message);
            }

            await tx.CommitAsync(ct);

            LogInfo($"一事一议审议完成: FormId={id}, ApplicationId={form.ApplicationId}, " +
                    $"Approved={request.Approved}, SpecialApprovalId={specialApprovalId}");
            return await GetByIdAsync(id, ct);
        }
        catch (Exception ex)
        {
            LogException(ex, "一事一议会议审议");
            return Result.FromException<SpecialApprovalRecord>(ex);
        }
    }

    // ───────────────────────────── 内部读取 ─────────────────────────────

    private async Task<Result<ApplicationBrief>> LoadApplicationAsync(long applicationId, CancellationToken ct)
    {
        const string sql = @"
            SELECT id, application_no, hukou_type, status
            FROM nc_biz_applications
            WHERE id = $1 AND deleted_at IS NULL
            LIMIT 1";

        var result = await _db.QuerySingleAsync<ApplicationBrief>(sql, ct, applicationId);
        if (result.IsFailure)
            return Result.Failure<ApplicationBrief>(result.ErrorCode, result.Message);
        if (result.Value is null)
            return Result.Failure<ApplicationBrief>(ErrorCodes.APPLICATION_NOT_FOUND, "申请不存在");

        return Result.Success(result.Value);
    }

    private async Task<FormBrief?> LoadFormBriefAsync(long id, CancellationToken ct)
    {
        const string sql = @"
            SELECT id, application_id, status, special_matters
            FROM nc_biz_special_approval_forms
            WHERE id = $1 AND deleted_at IS NULL
            LIMIT 1";

        var result = await _db.QuerySingleAsync<FormBrief>(sql, ct, id);
        if (result.IsFailure)
            throw new InvalidOperationException($"读取一事一议申报表失败: {result.Message}");

        return result.Value;
    }

    private async Task<FormBrief?> LoadFormBriefByApplicationAsync(long applicationId, CancellationToken ct)
    {
        const string sql = @"
            SELECT id, application_id, status, special_matters
            FROM nc_biz_special_approval_forms
            WHERE application_id = $1 AND deleted_at IS NULL
            ORDER BY id DESC
            LIMIT 1";

        var result = await _db.QuerySingleAsync<FormBrief>(sql, ct, applicationId);
        if (result.IsFailure)
            throw new InvalidOperationException($"读取一事一议申报表失败: {result.Message}");

        return result.Value;
    }

    private static bool IsFinalStatus(string? status)
        => status is SpecialApprovalConstants.StatusApproved or SpecialApprovalConstants.StatusRejected;

    private static string NormalizeActor(string? operatorName)
        => string.IsNullOrWhiteSpace(operatorName) ? "System" : operatorName.Trim();

    private static Result<T> Fail<T>(string? errorCode, string? message)
        => Result.Failure<T>(
            string.IsNullOrWhiteSpace(errorCode) ? ErrorCodes.UNKNOWN_ERROR : errorCode,
            string.IsNullOrWhiteSpace(message) ? "操作失败" : message);

    private sealed class ApplicationBrief
    {
        public long Id { get; set; }
        public string ApplicationNo { get; set; } = string.Empty;
        public string HukouType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    private sealed class FormBrief
    {
        public long Id { get; set; }
        public long ApplicationId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string SpecialMatters { get; set; } = string.Empty;
    }
}
