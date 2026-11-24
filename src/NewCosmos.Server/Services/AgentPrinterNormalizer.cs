using System.Text.Json;
using NewCosmos.Server.Contracts;

namespace NewCosmos.Server.Services;

/// <summary>Agent 上报打印机列表的校验与序列化。名称是绑定校验的唯一依据。</summary>
public static class AgentPrinterNormalizer
{
    public const int MaxPrinters = 200;
    public const int MaxNameLength = 300;

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static bool TryNormalize(
        IReadOnlyCollection<AgentPrinterReport>? source,
        out List<AgentPrinterReport> normalized,
        out string? error)
    {
        normalized = [];
        error = null;

        if (source is null)
        {
            return true;
        }

        if (source.Count > MaxPrinters)
        {
            error = $"打印机数量不能超过 {MaxPrinters} 台";
            return false;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var printer in source)
        {
            var name = printer.Name?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                error = "打印机名称不能为空";
                return false;
            }

            if (name.Length > MaxNameLength)
            {
                error = $"打印机名称不能超过 {MaxNameLength} 个字符";
                return false;
            }

            if (!seen.Add(name))
            {
                continue;
            }

            normalized.Add(new AgentPrinterReport(name, printer.IsDefault));
        }

        return true;
    }

    public static string Serialize(IReadOnlyCollection<AgentPrinterReport> printers)
        => JsonSerializer.Serialize(printers, JsonOptions);

    public static bool TryReadNames(string? printersJson, out List<string> names)
    {
        names = [];
        if (string.IsNullOrWhiteSpace(printersJson))
        {
            return true;
        }

        try
        {
            var printers = JsonSerializer.Deserialize<List<AgentPrinterReport>>(printersJson, JsonOptions);
            if (printers is null)
            {
                return false;
            }

            names = printers
                .Select(printer => printer.Name?.Trim())
                .Where(name => !string.IsNullOrEmpty(name))
                .Select(name => name!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
