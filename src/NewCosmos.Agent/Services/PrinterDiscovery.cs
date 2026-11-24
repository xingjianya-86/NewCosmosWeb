using System.Drawing.Printing;
using NewCosmos.Agent.Models;
using NewCosmos.Services.Core;

namespace NewCosmos.Agent.Services;

/// <summary>
/// 本机打印机探测。优先使用 System.Drawing.Printing（简单、无 WMI 依赖）；
/// 任何失败都只记录警告并返回空列表，绝不让心跳/注册崩溃。
/// </summary>
public static class PrinterDiscovery
{
    public static List<AgentPrinterInfo> GetInstalledPrinters(ILoggerService logger)
    {
        var result = new List<AgentPrinterInfo>();

        string? defaultPrinter = null;
        try
        {
            defaultPrinter = new PrinterSettings().PrinterName;
        }
        catch (Exception ex)
        {
            logger.Warn($"[Printer] 读取默认打印机失败: {ex.Message}");
        }

        try
        {
            var names = new List<string>();
            foreach (string? name in PrinterSettings.InstalledPrinters)
            {
                if (!string.IsNullOrWhiteSpace(name))
                    names.Add(name.Trim());
            }

            foreach (var name in names
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(n => n, StringComparer.CurrentCulture))
            {
                result.Add(new AgentPrinterInfo
                {
                    Name = name,
                    IsDefault = !string.IsNullOrWhiteSpace(defaultPrinter)
                                && string.Equals(name, defaultPrinter.Trim(), StringComparison.OrdinalIgnoreCase)
                });
            }

            // 默认打印机可能不在 InstalledPrinters 集合中（异常驱动/离线队列）——补一条，避免丢失默认标记
            if (!string.IsNullOrWhiteSpace(defaultPrinter)
                && result.All(p => !p.IsDefault))
            {
                result.Insert(0, new AgentPrinterInfo { Name = defaultPrinter.Trim(), IsDefault = true });
            }
        }
        catch (Exception ex)
        {
            logger.Warn($"[Printer] 枚举打印机失败，本次上报空列表: {ex.Message}");
            return new List<AgentPrinterInfo>();
        }

        if (result.Count == 0)
            logger.Warn("[Printer] 未发现本机打印机（可能未安装打印驱动或运行在无打印服务的环境）");

        return result;
    }
}
