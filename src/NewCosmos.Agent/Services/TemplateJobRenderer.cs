using NewCosmos.Agent.Models;
using NewCosmos.Constants;
using NewCosmos.Models.Options;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Templates;
using NewCosmos.Services.Utilities;

namespace NewCosmos.Agent.Services;

/// <summary>模板渲染/打印（不依赖数据库：模板字节由服务端下发并落临时文件）。</summary>
public interface ITemplateJobRenderer
{
    /// <summary>把 templateFileType / templateFileName 归一化为 docx / xlsx。</summary>
    string ResolveFileType(string? templateFileType, string? templateFileName);

    Task RenderPdfAsync(
        string templatePath,
        string fileType,
        IReadOnlyDictionary<string, string> fields,
        IReadOnlyList<Dictionary<string, string>> tableRows,
        IReadOnlyList<PrintImagePayload>? images,
        string? templateConfigJson,
        string? tableStartMarker,
        string? tableEndMarker,
        string pdfPath,
        CancellationToken ct);

    Task RenderSourceAsync(
        string templatePath,
        string fileType,
        IReadOnlyDictionary<string, string> fields,
        IReadOnlyList<Dictionary<string, string>> tableRows,
        IReadOnlyList<PrintImagePayload>? images,
        string? templateConfigJson,
        string? tableStartMarker,
        string? tableEndMarker,
        string outputPath,
        CancellationToken ct);

    Task<byte[]> ExportPdfFromSourceAsync(
        string sourcePath,
        string fileType,
        string pdfPath,
        CancellationToken ct);

    /// <summary>用 Office COM 打印渲染后的源文档（支持指定打印机/份数/双面）。</summary>
    Task PrintSourceAsync(
        string sourcePath,
        string fileType,
        string? printerName,
        int copies,
        bool isDuplex,
        CancellationToken ct);

    /// <summary>
    /// 模板索引槽每页容量（FAMILY_MEMBER_NAME_1..N → N）；0 表示不是名册类模板。
    /// 行数超过容量时必须拆页，否则第 N+1 行起会被静默丢弃。
    /// </summary>
    int GetIndexSlotCapacity(string? templateConfigJson);
}

/// <summary>
/// 复用 NewCosmos.Office 的 WordEngine / ExcelEngine：LoadFromFile → 替换字段/表格 → Save → ExportPdf。
/// 不调用 TemplateEngineFactory.CreateFromTemplateIdAsync（该路径依赖模板数据库）。
/// 字段映射规则与 PrintExecuteService 保持一致：服务端 fields/tableRows 使用 FieldKeys，
/// 由模板 config_json 的 fields[].fieldKey/placeholder 与 tables[].columns 映射到模板占位符。
/// </summary>
public sealed class TemplateJobRenderer : ITemplateJobRenderer
{
    private readonly ILoggerService _logger;
    private readonly PerformanceOptions _perfOptions;
    private readonly StorageOptions _storageOptions;
    private OfficeProviderInfo? _office;

    public TemplateJobRenderer(ILoggerService logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _perfOptions = AgentRuntime.CreatePerformanceOptions();
        _storageOptions = AgentRuntime.CreateStorageOptions();
    }

    public string ResolveFileType(string? templateFileType, string? templateFileName)
    {
        var type = (templateFileType ?? string.Empty).Trim().ToLowerInvariant();
        if (type.Contains("wordprocessingml") || type is "docx" or "word")
            return "docx";
        if (type.Contains("spreadsheetml") || type is "xlsx" or "excel")
            return "xlsx";

        var extension = Path.GetExtension(templateFileName ?? string.Empty).TrimStart('.').ToLowerInvariant();
        if (extension is "docx" or "word") return "docx";
        if (extension is "xlsx" or "excel") return "xlsx";

        throw new BusinessException(
            ErrorCodes.FILE_FORMAT_ERROR,
            $"不支持的模板类型: type={templateFileType ?? "-"}, file={templateFileName ?? "-"}（仅支持 docx/xlsx）");
    }

    public async Task RenderPdfAsync(
        string templatePath,
        string fileType,
        IReadOnlyDictionary<string, string> fields,
        IReadOnlyList<Dictionary<string, string>> tableRows,
        IReadOnlyList<PrintImagePayload>? images,
        string? templateConfigJson,
        string? tableStartMarker,
        string? tableEndMarker,
        string pdfPath,
        CancellationToken ct)
    {
        EnsureOffice(fileType);
        using var engine = CreateEngine(fileType);
        engine.LoadFromFile(templatePath);
        ApplyPayload(engine, fields, tableRows, images, templateConfigJson, tableStartMarker, tableEndMarker);

        var pdfBytes = await engine.ExportPdfAsync(ct).ConfigureAwait(false);
        var directory = Path.GetDirectoryName(pdfPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(pdfPath, pdfBytes, ct).ConfigureAwait(false);
    }

    public async Task RenderSourceAsync(
        string templatePath,
        string fileType,
        IReadOnlyDictionary<string, string> fields,
        IReadOnlyList<Dictionary<string, string>> tableRows,
        IReadOnlyList<PrintImagePayload>? images,
        string? templateConfigJson,
        string? tableStartMarker,
        string? tableEndMarker,
        string outputPath,
        CancellationToken ct)
    {
        EnsureOffice(fileType);
        using var engine = CreateEngine(fileType);
        engine.LoadFromFile(templatePath);
        ApplyPayload(engine, fields, tableRows, images, templateConfigJson, tableStartMarker, tableEndMarker);
        await engine.SaveToFileAsync(outputPath, ct).ConfigureAwait(false);
    }

    public async Task<byte[]> ExportPdfFromSourceAsync(
        string sourcePath,
        string fileType,
        string pdfPath,
        CancellationToken ct)
    {
        EnsureOffice(fileType);
        using var engine = CreateEngine(fileType);
        return await engine.ExportPdfFromFileAsync(sourcePath, pdfPath, ct).ConfigureAwait(false);
    }

    public async Task PrintSourceAsync(
        string sourcePath,
        string fileType,
        string? printerName,
        int copies,
        bool isDuplex,
        CancellationToken ct)
    {
        EnsureOffice(fileType);
        using var engine = CreateEngine(fileType);
        engine.PrinterName = printerName ?? string.Empty;
        engine.IsDuplex = isDuplex;
        await engine.PrintFromFileAsync(sourcePath, Math.Clamp(copies, 1, 99), ct).ConfigureAwait(false);
    }

    private ITemplateEngine CreateEngine(string fileType)
    {
        return fileType switch
        {
            "docx" => new WordEngine(_logger, _perfOptions, _storageOptions),
            "xlsx" => new ExcelEngine(_logger, _perfOptions, _storageOptions),
            _ => throw new NotSupportedException($"不支持的模板类型: {fileType}")
        };
    }

    public int GetIndexSlotCapacity(string? templateConfigJson)
    {
        var config = ParseConfig(templateConfigJson);
        if (config?.Fields is not { Count: > 0 })
        {
            return 0;
        }

        const string prefix = "FAMILY_MEMBER_NAME_";
        var capacity = 0;
        foreach (var field in config.Fields)
        {
            var key = field?.FieldKey;
            if (string.IsNullOrWhiteSpace(key)
                || !key.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (int.TryParse(key[prefix.Length..], out var index) && index > capacity)
            {
                capacity = index;
            }
        }

        return capacity;
    }

    private void ApplyPayload(
        ITemplateEngine engine,
        IReadOnlyDictionary<string, string>? fields,
        IReadOnlyList<Dictionary<string, string>>? tableRows,
        IReadOnlyList<PrintImagePayload>? images,
        string? templateConfigJson,
        string? tableStartMarker,
        string? tableEndMarker)
    {
        var config = ParseConfig(templateConfigJson);
        var plainFields = ToDictionary(fields);
        var rows = tableRows?.Where(row => row is { Count: > 0 }).ToList()
                   ?? new List<Dictionary<string, string>>();

        var placeholderFields = BuildPlaceholderFields(plainFields, rows, config);

        var configTables = config is { Tables: { Count: > 0 } tables }
            ? tables
                .Where(t => t != null && !string.IsNullOrWhiteSpace(t.StartMarker))
                .ToList()
            : new List<TemplateTableMapping>();

        if (configTables.Count > 0)
        {
            // 与 PrintExecuteService 顺序一致：先单值字段，再表格（表格标记不在 config.Fields 中），最后图片。
            if (placeholderFields.Count > 0)
                engine.ReplaceFields(placeholderFields);

            foreach (var table in configTables)
            {
                var tableRowsForEngine = rows
                    .Select(row => MapRowToPlaceholders(row, table.Columns))
                    .Where(row => row.Count > 0)
                    .ToList();
                if (tableRowsForEngine.Count == 0) continue;

                TryReplaceTable(engine, table.StartMarker, table.EndMarker, tableRowsForEngine);
            }

            ApplyImages(engine, images);
            return;
        }

        // 无 config 表格配置：兼容服务端显式下发标记，或按首行首键推断（legacy 约定）。
        if (rows.Count > 0)
        {
            var start = FirstNonEmpty(tableStartMarker, InferMarker(rows));
            var end = FirstNonEmpty(tableEndMarker, start);
            if (!string.IsNullOrWhiteSpace(start))
                TryReplaceTable(engine, start!, end!, rows.Select(ToDictionary).ToList());
            else
                _logger.Warn("[JobRender] 任务含 tableRows 但无表格配置/标记，已跳过表格填充");
        }

        if (placeholderFields.Count > 0)
            engine.ReplaceFields(placeholderFields);

        ApplyImages(engine, images);
    }

    /// <summary>
    /// 图片占位符内嵌（照片）：key 用模板占位符原文（与 config_json 一致），不是 FieldKey。
    /// 只处理 Server 下发的 PHOTO_*；SIGNATURE_* 签名系统未设计，跳过不报错。
    /// </summary>
    private void ApplyImages(ITemplateEngine engine, IReadOnlyList<PrintImagePayload>? images)
    {
        if (images is not { Count: > 0 }) return;

        var map = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var image in images)
        {
            if (image == null) continue;
            if (!string.IsNullOrEmpty(image.FieldKey)
                && image.FieldKey.StartsWith("SIGNATURE_", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var placeholder = image.Placeholder?.Trim();
            if (string.IsNullOrEmpty(placeholder) || string.IsNullOrEmpty(image.Base64)) continue;
            if (map.ContainsKey(placeholder)) continue;

            try
            {
                var bytes = Convert.FromBase64String(image.Base64);
                if (bytes.Length == 0) continue;
                map[placeholder] = bytes;
            }
            catch (FormatException ex)
            {
                _logger.Warn($"[JobRender] 图片 Base64 损坏，跳过占位符 {placeholder}: {ex.Message}");
            }
        }

        if (map.Count == 0) return;

        try
        {
            engine.ReplaceImageFields(map);
            _logger.Info($"[JobRender] 图片占位符已内嵌: {map.Count} 个");
        }
        catch (Exception ex)
        {
            // 图片失败不阻断整份文书（Excel 引擎本就不支持图片替换）。
            _logger.Warn($"[JobRender] 图片内嵌失败（继续渲染）: {ex.Message}");
        }
    }

    private void TryReplaceTable(
        ITemplateEngine engine,
        string? startMarker,
        string? endMarker,
        List<Dictionary<string, string>> rows)
    {
        if (rows.Count == 0 || string.IsNullOrWhiteSpace(startMarker)) return;

        try
        {
            engine.ReplaceTableByPlaceholder(startMarker!, endMarker ?? startMarker, rows);
        }
        catch (Exception ex)
        {
            // 表格填充失败不阻断整份文书（字段仍可渲染），但必须留痕。
            _logger.Warn($"[JobRender] 表格填充失败（继续渲染字段）: Marker={startMarker}, {ex.Message}");
        }
    }

    /// <summary>复制 PrintExecuteService 的映射口径：索引槽展平 → config.Fields 映射 → 格式化/默认值。</summary>
    private static Dictionary<string, string> BuildPlaceholderFields(
        Dictionary<string, string> fields,
        IReadOnlyList<Dictionary<string, string>> tableRows,
        TemplateConfig? config)
    {
        var flattened = FlattenTableRowsToFields(fields, tableRows, config);
        var mapped = MapFieldsToPlaceholders(flattened, config?.Fields, config?.IndexShifts);

        // 兼容服务端直接下发占位符键（历史/测试数据）：不覆盖已映射值。
        foreach (var (key, value) in fields)
        {
            if (!IsPlaceholderKey(key) || mapped.ContainsKey(key)) continue;
            mapped[key] = value;
        }

        return mapped;
    }

    private static Dictionary<string, string> FlattenTableRowsToFields(
        Dictionary<string, string> fields,
        IReadOnlyList<Dictionary<string, string>> tableRows,
        TemplateConfig? config)
    {
        var result = new Dictionary<string, string>(fields, StringComparer.Ordinal);
        if (config?.Fields is not { Count: > 0 }) return result;

        // 索引槽展平（tableRows 为空时跳过，月报批量把数据直接放在 fields 里）。
        if (tableRows.Count > 0)
        {
            var maxIndexByBase = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var field in config.Fields)
            {
                if (!TryGetIndexedKey(field?.FieldKey, out var baseKey, out var index)) continue;
                if (!maxIndexByBase.TryGetValue(baseKey, out var existing) || index > existing)
                    maxIndexByBase[baseKey] = index;
            }

            for (var i = 0; i < tableRows.Count; i++)
            {
                var suffix = i + 1;
                foreach (var (key, value) in tableRows[i])
                {
                    if (maxIndexByBase.ContainsKey(key))
                        result[$"{key}_{suffix}"] = value;
                }
            }
        }

        // 与旧版 PrintService.FillTemplateEngineAsync 一致：config 里声明了、fields 里没有的字段，
        // 一律补 DefaultValue ?? ""。这样模板中未使用的行/槽位会被清空，不会残留 {占位符}。
        foreach (var mapping in config.Fields)
        {
            if (string.IsNullOrEmpty(mapping?.FieldKey)) continue;
            if (!result.ContainsKey(mapping.FieldKey))
                result[mapping.FieldKey] = mapping.DefaultValue ?? string.Empty;
        }

        return result;
    }

    private static Dictionary<string, string> MapFieldsToPlaceholders(
        Dictionary<string, string> fieldValues,
        List<TemplateFieldMapping>? mappings,
        IReadOnlyList<IndexShiftRule>? indexShifts)
    {
        if (mappings is not { Count: > 0 } || fieldValues.Count == 0)
            return new Dictionary<string, string>(fieldValues, StringComparer.Ordinal);

        var projected = TemplateFieldResolver.Project(fieldValues, indexShifts);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var mapping in mappings)
        {
            if (mapping == null || string.IsNullOrWhiteSpace(mapping.Placeholder)) continue;

            // 签名/照片是图片占位符，不能当作文本替换（由 ApplyImages 单独内嵌）。
            if (IsImageFieldKey(mapping.FieldKey)) continue;

            if (!string.IsNullOrWhiteSpace(mapping.FieldKey)
                && projected.TryGetValue(mapping.FieldKey, out var rawValue))
            {
                result[mapping.Placeholder] = ApplyFormat(rawValue, mapping);
            }
            else if (!string.IsNullOrEmpty(mapping.DefaultValue))
            {
                result[mapping.Placeholder] = mapping.DefaultValue;
            }
        }

        return result;
    }

    private static Dictionary<string, string> MapRowToPlaceholders(
        Dictionary<string, string> row,
        List<TemplateFieldMapping>? columns)
    {
        if (columns is not { Count: > 0 })
            return new Dictionary<string, string>(row, StringComparer.Ordinal);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var column in columns)
        {
            if (column == null || string.IsNullOrWhiteSpace(column.Placeholder)) continue;
            if (!string.IsNullOrWhiteSpace(column.FieldKey) && row.TryGetValue(column.FieldKey, out var value))
                result[column.Placeholder] = value ?? string.Empty;
        }
        return result;
    }

    private static bool TryGetIndexedKey(string? fieldKey, out string baseKey, out int index)
    {
        baseKey = string.Empty;
        index = 0;
        if (string.IsNullOrWhiteSpace(fieldKey)) return false;

        var position = fieldKey.LastIndexOf('_');
        if (position <= 0 || position >= fieldKey.Length - 1) return false;
        if (!int.TryParse(fieldKey[(position + 1)..], out index)) return false;

        baseKey = fieldKey[..position];
        return true;
    }

    private static bool IsPlaceholderKey(string key)
        => !string.IsNullOrWhiteSpace(key) && key.StartsWith('{') && key.EndsWith('}');

    private static bool IsImageFieldKey(string? fieldKey)
        => !string.IsNullOrEmpty(fieldKey)
           && (fieldKey.StartsWith("SIGNATURE_", StringComparison.OrdinalIgnoreCase)
               || fieldKey.StartsWith("PHOTO_", StringComparison.OrdinalIgnoreCase));

    private static string ApplyFormat(string value, TemplateFieldMapping mapping)
    {
        if (string.IsNullOrEmpty(mapping.Format)) return value ?? string.Empty;
        var formatted = TemplateFieldMapping.ApplyFormat(value, mapping.Format);
        return string.IsNullOrEmpty(formatted) ? string.Empty : formatted;
    }

    private static string? InferMarker(IReadOnlyList<Dictionary<string, string>> tableRows)
    {
        foreach (var row in tableRows)
        {
            if (row == null) continue;
            foreach (var key in row.Keys)
            {
                if (!string.IsNullOrWhiteSpace(key)) return key;
            }
        }
        return null;
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static Dictionary<string, string> ToDictionary(IReadOnlyDictionary<string, string>? source)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (source == null) return result;

        foreach (var (key, value) in source)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            result[key] = value ?? string.Empty;
        }
        return result;
    }

    private TemplateConfig? ParseConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            return TemplateConfig.FromJson(json);
        }
        catch (Exception ex)
        {
            _logger.Warn($"[JobRender] 模板 configJson 解析失败，按无映射处理: {ex.Message}");
            return null;
        }
    }

    private void EnsureOffice(string fileType)
    {
        _office ??= DetectOfficeSafe();

        var available = fileType == "docx" ? _office.HasWord : _office.HasExcel;
        if (available) return;

        var app = fileType == "docx" ? "Word" : "Excel";
        throw new BusinessException(
            ErrorCodes.OFFICE_NOT_INSTALLED,
            $"未检测到可用的 Microsoft Office {app}，无法渲染/打印该任务。请安装并激活 Office 桌面版（WPS 不在本链路支持范围）。");
    }

    private OfficeProviderInfo DetectOfficeSafe()
    {
        try
        {
            return OfficeProviderDetector.Detect();
        }
        catch (Exception ex)
        {
            _logger.Warn($"[JobRender] Office 探测失败（按未安装处理）: {ex.Message}");
            return new OfficeProviderInfo { Provider = OfficeProvider.None };
        }
    }
}
