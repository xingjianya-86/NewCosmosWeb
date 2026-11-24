using NewCosmos.Services.Core;
using NewCosmos.Services.Utilities;

namespace NewCosmos.Agent.Services;

/// <summary>Office/WPS 探测包装：失败不抛出，返回 None 并记录原因。</summary>
public static class OfficeProbe
{
    public static OfficeProviderInfo Detect(ILoggerService logger)
    {
        try
        {
            var info = OfficeProviderDetector.Detect();
            logger.Info(
                $"[Office] Provider={info.Provider}, Word={info.WordProgId ?? "-"}, " +
                $"Excel={info.ExcelProgId ?? "-"}, Version={info.Version ?? "-"}");
            return info;
        }
        catch (Exception ex)
        {
            logger.Warn($"[Office] 探测失败（按未安装处理）: {ex.Message}");
            return new OfficeProviderInfo { Provider = OfficeProvider.None };
        }
    }

    public static string Describe(OfficeProviderInfo info)
    {
        var word = info.HasWord ? $"可用（{info.WordProgId}）" : "不可用";
        var excel = info.HasExcel ? $"可用（{info.ExcelProgId}）" : "不可用";
        var msOffice = OfficeProviderDetector.IsOfficeInstalled() ? "已安装" : "未安装";
        return $"Microsoft Office: {msOffice}；Word: {word}；Excel: {excel}；Provider: {info.Provider}"
               + (string.IsNullOrWhiteSpace(info.Version) ? string.Empty : $"；版本: {info.Version}");
    }
}
