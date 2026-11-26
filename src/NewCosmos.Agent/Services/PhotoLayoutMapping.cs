using System.Drawing;
using NewCosmos.Constants;

namespace NewCosmos.Agent.Services;

/// <summary>
/// 照片版式映射（单位：1/100 英寸，与 <see cref="System.Drawing.Printing.PrintPageEventArgs"/> 一致）。
/// <para>
/// 版式代码/证件尺寸的唯一来源是 Core 的 <see cref="PhotoLayoutConstants"/>：
/// Agent 只做「点 → 1/100 英寸」换算和 A4 可打印区布局，不在此重抄版式常量。
/// </para>
/// </summary>
internal static class PhotoLayoutMapping
{
    private const float PointsToHundredthsInch = 100f / 72f;

    /// <summary>
    /// 规范化版式：未知/空值按类别回落（身份证 → 证件原寸，其余 → 整页），
    /// 返回值使用 <see cref="PhotoLayoutConstants"/> 中的标准大写代码。
    /// </summary>
    public static string ResolveLayout(string? layout, string? category)
    {
        var value = layout?.Trim();
        if (!string.IsNullOrEmpty(value))
        {
            foreach (var item in PhotoLayoutConstants.All)
            {
                if (string.Equals(item.Code, value, StringComparison.OrdinalIgnoreCase))
                    return item.Code;
            }
        }

        return PhotoLayoutConstants.DefaultForCategory(category?.Trim().ToUpperInvariant());
    }

    /// <summary>
    /// 计算照片在纸上的目标包围盒（居中）：
    /// <list type="bullet">
    /// <item>ID_CARD：PhotoLayoutConstants.GetBox 的证件原寸（242×153 点 ≈ 85.4×54mm），在可打印区居中；</item>
    /// <item>HALF_PAGE：A4 可打印区上半部分，照片在其中居中；</item>
    /// <item>FULL_PAGE/未知：A4 可打印区整块，照片等比缩放铺满（不裁剪照片）。</item>
    /// </list>
    /// </summary>
    public static RectangleF GetTargetBox(string? layout, RectangleF printableArea)
    {
        if (printableArea.Width <= 0 || printableArea.Height <= 0)
            return RectangleF.Empty;

        return layout switch
        {
            PhotoLayoutConstants.ID_CARD => GetIdCardBox(printableArea),
            PhotoLayoutConstants.HALF_PAGE => new RectangleF(
                printableArea.X,
                printableArea.Y,
                printableArea.Width,
                printableArea.Height / 2f),
            _ => printableArea
        };
    }

    /// <summary>版式显示名（日志/提示用，避免在各处重复 switch）。</summary>
    public static string Describe(string? layout) => PhotoLayoutConstants.GetDisplayName(layout);

    private static RectangleF GetIdCardBox(RectangleF printableArea)
    {
        var (maxWidthPoints, maxHeightPoints) = PhotoLayoutConstants.GetBox(PhotoLayoutConstants.ID_CARD);
        var width = Math.Min(maxWidthPoints * PointsToHundredthsInch, printableArea.Width);
        var height = Math.Min(maxHeightPoints * PointsToHundredthsInch, printableArea.Height);

        return new RectangleF(
            printableArea.X + (printableArea.Width - width) / 2f,
            printableArea.Y + (printableArea.Height - height) / 2f,
            width,
            height);
    }
}
