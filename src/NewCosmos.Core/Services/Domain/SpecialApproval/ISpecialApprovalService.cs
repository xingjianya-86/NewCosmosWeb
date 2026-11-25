using NewCosmos.Constants;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.SpecialApproval;

/// <summary>
/// 一事一议 / 特殊审批服务。
/// <para>
/// 数据落点沿用旧版两张既有表，不新增列：
/// 申报内容写 <c>nc_biz_special_approval_forms</c>；会议审议结果写
/// <c>nc_biz_special_approvals</c>，并把申请主表的
/// <c>is_special_approval / special_approval_id</c> 回写。
/// </para>
/// <para>
/// 资源 id 统一指申报表 id（<c>nc_biz_special_approval_forms.id</c>）；
/// 会议审议 id 以 <see cref="SpecialApprovalRecord.SpecialApprovalId"/> 返回。
/// 一个申请只允许一份未删除的申报表。
/// </para>
/// </summary>
public interface ISpecialApprovalService
{
    /// <summary>新建申报表草稿。</summary>
    Task<Result<SpecialApprovalRecord>> CreateAsync(
        SpecialApprovalSaveRequest request,
        string operatorName,
        CancellationToken ct = default);

    /// <summary>按申报表 id 读取（含最近一次会议审议结果）。</summary>
    Task<Result<SpecialApprovalRecord>> GetByIdAsync(long id, CancellationToken ct = default);
    /// <summary>按申请 id 读取最近一份申报表（含会议审议结果）。</summary>
    Task<Result<SpecialApprovalRecord>> GetByApplicationIdAsync(long applicationId, CancellationToken ct = default);

    /// <summary>更新草稿/已提交未审议的申报内容。</summary>
    Task<Result<SpecialApprovalRecord>> UpdateAsync(
        long id,
        SpecialApprovalSaveRequest request,
        string operatorName,
        CancellationToken ct = default);

    /// <summary>提交申报（Draft → Submitted）。</summary>
    Task<Result<SpecialApprovalRecord>> SubmitAsync(
        long id,
        string operatorName,
        CancellationToken ct = default);

    /// <summary>
    /// 会议审议（通过/驳回）：写 <c>nc_biz_special_approvals</c>，更新申报表状态；
    /// 通过时回写申请分类与一事一议标记，驳回时按申请状态机转 Refused。
    /// </summary>
    Task<Result<SpecialApprovalRecord>> ReviewAsync(
        long id,
        SpecialApprovalReviewRequest request,
        string operatorName,
        CancellationToken ct = default);

    /// <summary>列出一个申请下的一事一议申报记录（按创建时间倒序）。</summary>
    Task<Result<List<SpecialApprovalRecord>>> ListByApplicationAsync(
        long applicationId,
        CancellationToken ct = default);
}

/// <summary>一事一议申报表 + 最近一次会议审议结果的合并读取模型。</summary>
public sealed class SpecialApprovalRecord
{
    public long Id { get; set; }
    public long ApplicationId { get; set; }
    public long? SpecialApprovalId { get; set; }
    public string FormNo { get; set; } = string.Empty;

    // ── 申报表（nc_biz_special_approval_forms）──
    public string BasicSituation { get; set; } = string.Empty;
    public string SpecialMatters { get; set; } = string.Empty;
    public string TemplateKey { get; set; } = string.Empty;
    public string ReportUnit { get; set; } = string.Empty;
    public string HandlerName { get; set; } = string.Empty;
    public DateTime? FormDate { get; set; }
    public string AuditResult { get; set; } = string.Empty;
    public DateTime? AuditDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // ── 会议审议（nc_biz_special_approvals，最近一条）──
    public DateTime? MeetingDate { get; set; }
    public string MeetingMembers { get; set; } = string.Empty;
    public string OverrideClassification { get; set; } = string.Empty;
    public string ApproverName { get; set; } = string.Empty;
    public string ApproverTitle { get; set; } = string.Empty;
    public string ApproverOrganization { get; set; } = string.Empty;
    public string SpecialReason { get; set; } = string.Empty;
    public string SpecialCircumstances { get; set; } = string.Empty;
    public string ReviewCreatedBy { get; set; } = string.Empty;
    public DateTime? ReviewedAt { get; set; }

    public string StatusText => SpecialApprovalConstants.GetStatusDescription(Status);
    public bool IsSubmitted => Status == SpecialApprovalConstants.StatusSubmitted;
    public bool IsReviewed =>
        Status is SpecialApprovalConstants.StatusApproved
            or SpecialApprovalConstants.StatusRejected;
    public bool? Approved => IsReviewed
        ? Status == SpecialApprovalConstants.StatusApproved
        : null;
}

/// <summary>申报表保存请求（新建/更新共用）。</summary>
public sealed class SpecialApprovalSaveRequest
{
    /// <summary>新建时必填；更新时忽略。</summary>
    public long ApplicationId { get; set; }

    public string BasicSituation { get; set; } = string.Empty;
    public string SpecialMatters { get; set; } = string.Empty;
    public string TemplateKey { get; set; } = string.Empty;
    public string ReportUnit { get; set; } = string.Empty;
    public string HandlerName { get; set; } = string.Empty;
    public DateTime? FormDate { get; set; }
    public string AuditResult { get; set; } = string.Empty;
    public DateTime? AuditDate { get; set; }
}

/// <summary>会议审议请求（与前端 review 请求体一一对应）。</summary>
public sealed class SpecialApprovalReviewRequest
{
    public bool Approved { get; set; }

    /// <summary>指定救助分类代码；审议通过时必填且必须是有效施保分类。</summary>
    public string? Classification { get; set; }

    public DateTime? MeetingDate { get; set; }
    public string? Attendees { get; set; }
    public string? ApproverName { get; set; }
    public string? ApproverPosition { get; set; }
    public string? ApproverUnit { get; set; }
}
