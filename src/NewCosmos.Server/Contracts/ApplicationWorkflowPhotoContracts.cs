namespace NewCosmos.Server.Contracts;

#region 业务申请工作流 · 档案照片

/// <summary>
/// 工作流「照片」对话框的单张照片。
/// <see cref="DataUrl"/> 已由服务端拼成可直接用于 img src 的 data URL；
/// <see cref="Format"/> 为归一化后的格式名（jpeg/webp/png），空值按 jpeg。
/// </summary>
public sealed record ApplicationWorkflowPhotoDto(
    long Id,
    string Category,
    string PhotoName,
    string Format,
    string DataUrl);

public sealed record ApplicationWorkflowPhotoListResponse(
    IReadOnlyList<ApplicationWorkflowPhotoDto> Photos);

#endregion
