using System.Globalization;
using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ElderlyBenefits;

namespace NewCosmos.Server.Services;

/// <summary>
/// 高龄年度认证写操作：新建批次、生成名单、手工改认证结果、暂缓/排除/待停发、
/// 逐人停发（复用现有 ElderlyApplicationService.StopAsync，不做批量停发）。
/// 所有针对具体批次/任务的操作都先用 QueryService 做数据范围校验，越权按 NOT_FOUND。
/// </summary>
public sealed class ElderlyCertificationWriteService(
    IElderlyCertificationService certificationService,
    IElderlyApplicationService applicationService,
    ElderlyCertificationQueryService queryService,
    ILoggerService logger)
{
    public async Task<Result<long>> CreateCampaignAsync(
        ElderlyCertCampaignCreateRequest request,
        string operatorName,
        CancellationToken ct = default)
    {
        if (request.Year <= 0)
        {
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "认证年度不能为空");
        }

        if (request.PeriodStart == default || request.PeriodEnd == default)
        {
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "认证起止日期不能为空");
        }

        return await certificationService.CreateCampaignAsync(
            request.Year,
            request.Round,
            request.Name,
            request.PeriodStart.Date,
            request.PeriodEnd.Date,
            request.RequireEvidence,
            operatorName,
            ct);
    }

    /// <summary>
    /// 生成/刷新名单快照。Core 的名单口径是全县在享 + 名册 Active，
    /// Server 只校验当前账号能否看到该批次，不对 Core 的名单范围做二次裁剪。
    /// </summary>
    public async Task<Result<int>> GenerateRosterAsync(
        int userId,
        long campaignId,
        string operatorName,
        CancellationToken ct = default)
    {
        var visible = await queryService.GetVisibleCampaignAsync(userId, campaignId, ct);
        if (visible.IsFailure)
        {
            return Result.Failure<int>(visible.ErrorCode!, visible.Message!);
        }

        return await certificationService.GenerateRosterAsync(campaignId, operatorName, ct);
    }

    public async Task<Result> UpdateResultAsync(
        int userId,
        long taskId,
        ElderlyCertResultUpdateRequest request,
        string operatorName,
        CancellationToken ct = default)
    {
        var scoped = await queryService.GetScopedTaskAsync(userId, taskId, ct);
        if (scoped.IsFailure)
        {
            return Result.Failure(scoped.ErrorCode!, scoped.Message!);
        }

        var task = scoped.Value;
        if (task.CertStatus == ElderlyCertificationConstants.CertStatusStopped
            || task.StopDecision == ElderlyCertificationConstants.StopDecisionApproved)
        {
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "该任务已停发，不能修改认证结果");
        }

        var certStatus = (request.CertStatus ?? string.Empty).Trim();
        if (certStatus.Length == 0)
        {
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "请选择认证状态");
        }

        return await certificationService.UpdateTaskResultAsync(
            taskId,
            certStatus,
            Normalize(request.VerifyMethod),
            request.VerifiedAt?.Date,
            Normalize(request.VerifiedBy),
            Normalize(request.ResultNote),
            request.DeathDate?.Date,
            operatorName,
            ct);
    }

    public async Task<Result> SetStopDecisionAsync(
        int userId,
        long taskId,
        ElderlyCertStopDecisionRequest request,
        string operatorName,
        CancellationToken ct = default)
    {
        var scoped = await queryService.GetScopedTaskAsync(userId, taskId, ct);
        if (scoped.IsFailure)
        {
            return Result.Failure(scoped.ErrorCode!, scoped.Message!);
        }

        var decision = (request.Decision ?? string.Empty).Trim();
        if (decision.Length == 0)
        {
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "请选择停发决定");
        }

        return await certificationService.SetStopDecisionAsync(
            taskId,
            decision,
            Normalize(request.Note),
            request.FollowUpDate?.Date,
            operatorName,
            ct);
    }

    /// <summary>
    /// 逐人停发：任务必须在本用户数据范围且已建档（ApplicationId 非空）；
    /// 先调现有停发服务，成功后再回写认证任务。失败不回写，避免“未停发却标记已停发”。
    /// </summary>
    public async Task<Result> StopAsync(
        int userId,
        long taskId,
        ElderlyCertStopRequest request,
        string operatorName,
        CancellationToken ct = default)
    {
        var scoped = await queryService.GetScopedTaskAsync(userId, taskId, ct);
        if (scoped.IsFailure)
        {
            return Result.Failure(scoped.ErrorCode!, scoped.Message!);
        }

        var task = scoped.Value;
        if (task.CertStatus == ElderlyCertificationConstants.CertStatusStopped
            || task.StopDecision == ElderlyCertificationConstants.StopDecisionApproved)
        {
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "该任务已停发");
        }

        if (task.ApplicationId is not > 0)
        {
            return Result.Failure(
                ErrorCodes.VALIDATION_FAILED,
                "该人员名册未建档（无高龄档案 ID），不能直接停发");
        }

        if (!TryBuildStopValues(request, out var values, out var validation))
        {
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, validation!);
        }

        var applicationId = task.ApplicationId.Value;
        var stopResult = await applicationService.StopAsync(
            applicationId,
            values.ReasonText,
            operatorName,
            values.DueStopDate,
            values.DeathDate,
            values.IsRecover,
            values.RecoverStartMonth,
            values.RecoverEndMonth,
            values.RecoverAmount,
            values.Remark,
            ct);
        if (stopResult.IsFailure)
        {
            return stopResult;
        }

        var markResult = await certificationService.MarkStoppedAsync(
            taskId,
            applicationId,
            values.ReasonKey,
            values.DueStopDate,
            DateTime.Today,
            operatorName,
            ct);
        if (markResult.IsFailure)
        {
            // 停发档案已生效，这里失败会导致“已停发但认证任务未回写”的不一致状态。
            logger.Error(
                $"高龄认证停发回写失败: TaskId={taskId}, ApplicationId={applicationId}, " +
                $"{markResult.ErrorCode} {markResult.Message}");
            return Result.Failure(
                markResult.ErrorCode!,
                $"停发办理已成功，但认证任务回写失败，请人工核对：{markResult.Message}");
        }

        return Result.Success();
    }

    // ───────────────────────────── 停发参数校验 ─────────────────────────────

    private sealed record StopValues(
        string ReasonKey,
        string ReasonText,
        DateTime? DueStopDate,
        DateTime? DeathDate,
        bool IsRecover,
        string RecoverStartMonth,
        string RecoverEndMonth,
        decimal RecoverAmount,
        string Remark);

    private static bool TryBuildStopValues(
        ElderlyCertStopRequest? request,
        out StopValues values,
        out string? error)
    {
        values = default!;
        error = null;

        if (request is null)
        {
            error = "请求体不能为空";
            return false;
        }

        var reasonKey = (request.StopReason ?? string.Empty).Trim();
        if (reasonKey.Length == 0)
        {
            error = "请选择停发原因";
            return false;
        }

        // 与取消备案表五项保持一致；NotLocated 在认证停发里归到 CERTIFICATION_FAIL + 备注，
        // 不允许前端直接传 NOT_LOCATED 造成模板/报表口径分裂。
        if (!ElderlyBenefitConstants.StopReasonPickerOptions.Contains(reasonKey, StringComparer.Ordinal))
        {
            error = "停发原因不合法";
            return false;
        }

        string reasonText;
        if (string.Equals(reasonKey, ElderlyBenefitConstants.StopReasonOther, StringComparison.Ordinal))
        {
            var other = (request.OtherReasonText ?? string.Empty).Trim();
            if (other.Length == 0)
            {
                error = "请填写“其他”停发原因说明";
                return false;
            }

            reasonText = other;
        }
        else
        {
            reasonText = reasonKey;
        }

        if (string.Equals(reasonKey, ElderlyBenefitConstants.StopReasonDeceased, StringComparison.Ordinal)
            && request.DeathDate is null)
        {
            error = "请选择死亡日期";
            return false;
        }

        var start = string.Empty;
        var end = string.Empty;
        var amount = 0m;
        if (request.IsRecover)
        {
            if (!TryReadMonth(request.RecoverStartMonth, out start))
            {
                error = "追缴起算月格式应为 yyyy-MM";
                return false;
            }

            if (!TryReadMonth(request.RecoverEndMonth, out end))
            {
                error = "追缴止算月格式应为 yyyy-MM";
                return false;
            }

            if (string.CompareOrdinal(start, end) > 0)
            {
                error = "追缴止算月不能早于起算月";
                return false;
            }

            amount = request.RecoverAmount ?? 0m;
            if (amount < 0)
            {
                error = "追缴金额不能为负数";
                return false;
            }
        }

        values = new StopValues(
            reasonKey,
            reasonText,
            (request.DueStopDate ?? DateTime.Today).Date,
            request.DeathDate?.Date,
            request.IsRecover,
            start,
            end,
            amount,
            Normalize(request.Remark) ?? string.Empty);
        return true;
    }

    private static bool TryReadMonth(string? raw, out string month)
    {
        month = string.Empty;
        var value = (raw ?? string.Empty).Trim();
        if (!DateTime.TryParseExact(
                value,
                "yyyy-MM",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return false;
        }

        month = parsed.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        return true;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
