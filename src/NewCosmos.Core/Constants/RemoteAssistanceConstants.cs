namespace NewCosmos.Constants;

/// <summary>
/// 异地协助（application_type=RemoteAssistance）业务常量。
/// 方案 A 口径：仅用于**本地库没有在享档案**的人员；本地档案已停保（Stopped）时
/// 允许办理，但标记为「户籍迁出」（停保通常即迁出本地）。
/// </summary>
public static class RemoteAssistanceConstants
{
    /// <summary>标记值：原本地档案已停保，按户籍迁出处理。</summary>
    public const string MarkerHukouMovedOut = "HukouMovedOut";

    /// <summary>标记显示名。</summary>
    public const string MarkerHukouMovedOutDisplay = "户籍迁出";

    /// <summary>标记码转显示名（无标记返回 null）。</summary>
    public static string? MarkerDisplay(string? marker)
        => string.Equals(marker, MarkerHukouMovedOut, StringComparison.Ordinal)
            ? MarkerHukouMovedOutDisplay
            : null;
}
