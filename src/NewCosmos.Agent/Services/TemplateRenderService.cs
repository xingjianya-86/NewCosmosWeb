using System.Text.Json;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Templates;
using NewCosmos.Services.Utilities;

namespace NewCosmos.Agent.Services;

/// <summary>
/// CLI 单次模板渲染：模板 + JSON 数据 → PDF（真实 Office COM，失败给明确原因）。
/// 数据格式（flat 或带 fields/tableRows 两种）：
///   { "{户主姓名}": "张三", "{家庭住址}": "..." }
///   { "fields": { ... }, "tableRows": [ { "{成员姓名}": "李四" } ],
///     "tableStartMarker": "{成员姓名}", "tableEndMarker": "{成员姓名}" }
/// </summary>
public static class TemplateRenderService
{
    public static async Task<int> RunAsync(
        string templatePath,
        string dataArgument,
        string outputPath,
        ILoggerService logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(templatePath))
        {
            AgentConsole.Error("缺少 --template <path>");
            return 2;
        }
        if (string.IsNullOrWhiteSpace(dataArgument))
        {
            AgentConsole.Error("缺少 --data <json 文件或 JSON 文本>");
            return 2;
        }
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            AgentConsole.Error("缺少 --out <pdf>");
            return 2;
        }

        var fullTemplate = Path.GetFullPath(Environment.ExpandEnvironmentVariables(templatePath));
        if (!File.Exists(fullTemplate))
        {
            AgentConsole.Error($"模板文件不存在: {fullTemplate}");
            return 3;
        }

        var extension = Path.GetExtension(fullTemplate).ToLowerInvariant();
        if (extension is not (".docx" or ".xlsx"))
        {
            AgentConsole.Error($"不支持的模板扩展名: {extension}（当前仅支持 .docx / .xlsx）");
            return 3;
        }

        RenderPayload payload;
        try
        {
            var json = LoadDataJson(dataArgument);
            payload = ParsePayload(json);
        }
        catch (Exception ex)
        {
            AgentConsole.Error($"读取/解析 --data 失败: {ex.Message}");
            return 3;
        }

        var fullOut = Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputPath));
        if (!string.Equals(Path.GetExtension(fullOut), ".pdf", StringComparison.OrdinalIgnoreCase))
            fullOut += ".pdf";

        var outDir = Path.GetDirectoryName(fullOut);
        if (!string.IsNullOrEmpty(outDir))
            Directory.CreateDirectory(outDir);

        // 真实渲染前先探测 MS Office：OfficeComWorker 只使用 Microsoft Office 的 ProgID，
        // WPS 即便已安装也不走本链路（避免给出"检测到 WPS 但渲染失败"的误导）。
        OfficeProviderInfo office;
        try
        {
            office = OfficeProviderDetector.Detect();
        }
        catch (Exception ex)
        {
            logger.Warn($"[Render] Office 探测异常: {ex.Message}");
            office = new OfficeProviderInfo { Provider = OfficeProvider.None };
        }

        var needsWord = extension is ".docx";
        var hasRequiredOffice = needsWord ? office.HasWord : office.HasExcel;
        var isMsOffice = office.Provider is OfficeProvider.Office or OfficeProvider.Both;

        if (!hasRequiredOffice || !isMsOffice)
        {
            AgentConsole.Error(
                $"未检测到可用的 Microsoft Office（{(needsWord ? "Word" : "Excel")}）。" +
                $"当前探测: Provider={office.Provider}, Word={office.WordProgId ?? "-"}, Excel={office.ExcelProgId ?? "-"}。" +
                "请安装 Microsoft Office（WPS 不在本链路支持范围），并确认固定 PC 上已激活。");
            logger.Warn("[Render] Microsoft Office 不可用，已中止渲染");
            return 4;
        }

        AgentConsole.Info($"检测到 Office: {OfficeProbe.Describe(office)}");
        AgentConsole.Info($"渲染模板: {fullTemplate}");
        AgentConsole.Info($"字段 {payload.Fields.Count} 个" +
                          (payload.TableRows.Count > 0 ? $"，表格行 {payload.TableRows.Count} 条" : string.Empty));

        var storage = AgentRuntime.CreateStorageOptions();
        var perf = AgentRuntime.CreatePerformanceOptions();

        ITemplateEngine? engine = null;
        try
        {
            engine = needsWord
                ? new WordEngine(logger, perf, storage)
                : new ExcelEngine(logger, perf, storage);

            engine.LoadFromFile(fullTemplate);

            if (payload.Fields.Count > 0)
                engine.ReplaceFields(payload.Fields);

            if (payload.TableRows.Count > 0)
            {
                if (string.IsNullOrWhiteSpace(payload.TableStartMarker)
                    || string.IsNullOrWhiteSpace(payload.TableEndMarker))
                {
                    AgentConsole.Error("data 含 tableRows 时必须同时提供 tableStartMarker / tableEndMarker（模板中的表格定位标记）");
                    return 2;
                }

                engine.ReplaceTableByPlaceholder(
                    payload.TableStartMarker,
                    payload.TableEndMarker,
                    payload.TableRows);
            }

            var pdf = await engine.ExportPdfAsync(ct).ConfigureAwait(false);
            await File.WriteAllBytesAsync(fullOut, pdf, ct).ConfigureAwait(false);

            AgentConsole.Success($"渲染完成: {fullOut}（{pdf.Length:N0} bytes）");
            logger.Info($"[Render] 渲染完成: Template={fullTemplate}, Out={fullOut}, Bytes={pdf.Length}");
            return 0;
        }
        catch (OperationCanceledException)
        {
            AgentConsole.Warn("渲染已取消");
            return 130;
        }
        catch (BusinessException ex)
        {
            AgentConsole.Error($"渲染失败 [{ex.ErrorCode}]: {ex.Message}");
            logger.Error($"[Render] 渲染失败 [{ex.ErrorCode}]: {ex.Message}");
            return 5;
        }
        catch (Exception ex)
        {
            AgentConsole.Error($"渲染失败: {ex.Message}");
            logger.LogError(ex, "[Render] 渲染异常");
            return 5;
        }
        finally
        {
            engine?.Dispose();
        }
    }

    private static string LoadDataJson(string dataArgument)
    {
        var candidate = Environment.ExpandEnvironmentVariables(dataArgument);
        if (File.Exists(candidate))
            return File.ReadAllText(candidate);

        // 允许直接传 JSON 文本（便于冒烟）
        return dataArgument;
    }

    private static RenderPayload ParsePayload(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("data 根节点必须是 JSON 对象");

        var payload = new RenderPayload();

        if (root.TryGetProperty("fields", out var fieldsElement) && fieldsElement.ValueKind == JsonValueKind.Object)
        {
            payload.Fields = ReadStringDictionary(fieldsElement);
        }
        else
        {
            foreach (var property in root.EnumerateObject())
            {
                if (IsMetadataKey(property.Name)) continue;
                if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array) continue;
                payload.Fields[property.Name] = ToText(property.Value);
            }
        }

        if (root.TryGetProperty("tableRows", out var rowsElement) && rowsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rowsElement.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("tableRows 的每一项都必须是 JSON 对象");
                payload.TableRows.Add(ReadStringDictionary(row));
            }
        }

        payload.TableStartMarker = ReadOptionalString(root, "tableStartMarker");
        payload.TableEndMarker = ReadOptionalString(root, "tableEndMarker");

        return payload;
    }

    private static Dictionary<string, string> ReadStringDictionary(JsonElement element)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            result[property.Name] = ToText(property.Value);
        return result;
    }

    private static string? ReadOptionalString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool IsMetadataKey(string name) =>
        name is "tableRows" or "tableStartMarker" or "tableEndMarker";

    private static string ToText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
        _ => value.GetRawText()
    };

    private sealed class RenderPayload
    {
        public Dictionary<string, string> Fields { get; set; } = new(StringComparer.Ordinal);

        public List<Dictionary<string, string>> TableRows { get; } = new();

        public string? TableStartMarker { get; set; }

        public string? TableEndMarker { get; set; }
    }
}
