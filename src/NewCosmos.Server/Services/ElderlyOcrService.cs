using System.Net.Http.Headers;
using System.Text.Json;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Services.Core;

namespace NewCosmos.Server.Services;

/// <summary>
/// 高龄登记身份证 OCR：服务端转发到内网 OCR 服务（deploy/ocr/ocr_api.py）。
/// <para>
/// 地址只从环境变量 <c>NC_OCR_SERVICE_URL</c> 读取；未配置一律返回
/// <see cref="ErrorCodes.OCR_SERVICE_UNREACHABLE"/>（端点映射 503），不得静默成功。
/// </para>
/// <para>
/// 响应契约与 Core <c>RemoteOcrRecognizeClient</c> 一致：<c>ok/fields/lines</c>；
/// fields 为空但 lines 有值时用 <see cref="IdCardOcrParser"/> 从文本行兜底抽取姓名/证号。
/// </para>
/// </summary>
public sealed class ElderlyOcrService(ILoggerService logger)
{
    public const string BaseUrlEnvironmentVariable = "NC_OCR_SERVICE_URL";

    /// <summary>与旧客户端一致的识别超时上限（大图推理可达数分钟，见 OcrConstants）。</summary>
    private static readonly TimeSpan RecognizeTimeout =
        TimeSpan.FromSeconds(OcrConstants.RecognizeTimeoutSeconds);

    /// <summary>Singleton 共享 HttpClient（按需求固定 60s 超时）。</summary>
    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10)
        })
        {
            Timeout = RecognizeTimeout
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NewCosmos-Server/1.0");
        return client;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ResolveBaseUrl());

    public async Task<Result<ElderlyOcrResponse>> RecognizeIdCardAsync(
        Stream image, CancellationToken ct = default)
    {
        var baseUrl = ResolveBaseUrl();
        if (baseUrl is null)
        {
            return Result.Failure<ElderlyOcrResponse>(
                ErrorCodes.OCR_SERVICE_UNREACHABLE,
                "识别服务地址未配置（环境变量 NC_OCR_SERVICE_URL）");
        }

        try
        {
            using var content = new MultipartFormDataContent();
            var streamContent = new StreamContent(image);
            streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(streamContent, "file", "idcard.bin");
            content.Add(new StringContent("ID_CARD"), "category");

            using var response = await Http.PostAsync($"{baseUrl}/ocr/recognize", content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                var imageInvalid = response.StatusCode is
                    System.Net.HttpStatusCode.BadRequest
                    or System.Net.HttpStatusCode.RequestEntityTooLarge
                    or System.Net.HttpStatusCode.UnsupportedMediaType
                    or System.Net.HttpStatusCode.UnprocessableEntity;

                logger.Warn($"高龄身份证 OCR 失败 HTTP {(int)response.StatusCode}: {Truncate(body, 200)}");
                return Result.Failure<ElderlyOcrResponse>(
                    imageInvalid ? ErrorCodes.OCR_IMAGE_INVALID : ErrorCodes.OCR_RECOGNIZE_FAILED,
                    imageInvalid
                        ? "图片无法识别（格式或大小不符），请换一张图片"
                        : "识别服务处理失败，请稍后重试");
            }

            var parsed = ParseResponse(body);
            if (parsed is null)
            {
                logger.Warn("高龄身份证 OCR 响应解析失败（格式不符）");
                return Result.Failure<ElderlyOcrResponse>(
                    ErrorCodes.OCR_RECOGNIZE_FAILED, "识别服务返回数据异常");
            }

            var fields = parsed.Value.Fields;
            var lines = parsed.Value.Lines;

            if (!fields.Values.Any(v => !string.IsNullOrWhiteSpace(v)) && lines.Count > 0)
            {
                var fallback = IdCardOcrParser.Parse(lines);
                if (!string.IsNullOrWhiteSpace(fallback.Name))
                {
                    fields[OcrFieldKeys.Name] = fallback.Name.Trim();
                }

                if (!string.IsNullOrWhiteSpace(fallback.IdCard))
                {
                    fields[OcrFieldKeys.IdCard] = fallback.IdCard.Trim();
                }
            }

            var name = GetField(fields, OcrFieldKeys.Name);
            var idCard = GetField(fields, OcrFieldKeys.IdCard);
            var address = GetField(fields, OcrFieldKeys.Address);

            var (gender, birthDate) = ExtractFromIdCard(idCard);

            logger.LogBusiness("高龄身份证 OCR 识别完成",
                ("FieldCount", fields.Count(kv => !string.IsNullOrWhiteSpace(kv.Value))),
                ("LineCount", lines.Count));

            return Result.Success(new ElderlyOcrResponse(
                name, idCard, gender, birthDate, address, fields));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Result.Failure<ElderlyOcrResponse>(ErrorCodes.CANCELLED, "操作已取消");
        }
        catch (TaskCanceledException)
        {
            logger.Warn($"高龄身份证 OCR 超时（{RecognizeTimeout.TotalSeconds:F0}s）");
            return Result.Failure<ElderlyOcrResponse>(
                ErrorCodes.OCR_TIMEOUT, "识别服务响应超时，请稍后重试");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "高龄身份证 OCR 请求失败");
            return Result.Failure<ElderlyOcrResponse>(
                ErrorCodes.OCR_SERVICE_UNREACHABLE,
                "识别服务未连接（请确认内网/ZeroTier 在线或联系管理员）");
        }
    }

    /// <summary>解析 ok/fields/lines；ok=false 或 JSON 非法返回 null 由调用方显式失败。</summary>
    private (Dictionary<string, string> Fields, List<string> Lines)? ParseResponse(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (!root.TryGetProperty(OcrConstants.ResponseKeys.Ok, out var ok)
                || ok.ValueKind != JsonValueKind.True)
            {
                return null;
            }

            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            if (root.TryGetProperty(OcrConstants.ResponseKeys.Fields, out var fieldsElement)
                && fieldsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in fieldsElement.EnumerateObject())
                {
                    fields[property.Name] = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? string.Empty
                        : string.Empty;
                }
            }

            var lines = new List<string>();
            if (root.TryGetProperty(OcrConstants.ResponseKeys.Lines, out var linesElement)
                && linesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in linesElement.EnumerateArray())
                {
                    var text = item.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        lines.Add(text);
                    }
                }
            }

            return (fields, lines);
        }
        catch (JsonException ex)
        {
            logger.Warn($"高龄身份证 OCR 响应 JSON 解析失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>从身份证号推导性别/出生日期（校验码通过优先，失败时按 18 位格式尽力解析）。</summary>
    private static (string Gender, DateTime? BirthDate) ExtractFromIdCard(string? idCard)
    {
        var value = (idCard ?? string.Empty).Trim();
        if (value.Length != 18)
        {
            return (string.Empty, null);
        }

        var birthDate = IdCardValidator.IsValid(value)
            ? IdCardValidator.ExtractBirthDate(value)
            : IdCardValidator.ExtractBirthDateBasic(value);

        var gender = string.Empty;
        if (char.IsDigit(value[16]))
        {
            gender = (value[16] - '0') % 2 == 1 ? "男" : "女";
        }

        return (gender, birthDate);
    }

    private static string GetField(IReadOnlyDictionary<string, string> fields, string key)
        => fields.TryGetValue(key, out var value) ? (value ?? string.Empty).Trim() : string.Empty;

    /// <summary>
    /// 解析服务地址：仅接受 http(s) 绝对地址；未配置/非法一律视为未配置（返回 null）。
    /// 图片含公民 PII，只允许内网端点，不做外网兜底（同 Core RemoteOcrRecognizeClient §12 边界）。
    /// </summary>
    private static string? ResolveBaseUrl()
    {
        var url = Environment.GetEnvironmentVariable(BaseUrlEnvironmentVariable)?.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme is not ("http" or "https")) return null;
        return url;
    }

    private static string Truncate(string text, int max)
        => text.Length <= max ? text : text[..max] + "…";
}
