using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Services.Core;
using NewCosmos.Services.Import;

namespace NewCosmos.Server.Services;

/// <summary>
/// 高龄年度认证两段式导入的预览会话（单例）。
///
/// 存储：Excel/ZIP 落 <c>Path.GetTempPath()/nc-elderly-cert/{previewId}</c>，不写 Web 根目录、
/// 不把 ZIP 存数据库；会话只保存在内存（服务重启即失效，前端收到 PREVIEW_EXPIRED 后重传）。
/// 过期：24 小时；每次预览/应用顺带清理过期会话与孤儿目录。
/// 并发：ConcurrentDictionary；应用时先原子 TryRemove 会话，避免同一预览被并发应用两次。
/// </summary>
public sealed class ElderlyCertificationImportSessionService(
    IElderlyCertificationImportService importService,
    ILoggerService logger)
{
    /// <summary>Excel 名单上限 20MB。</summary>
    public const long MaxExcelBytes = 20L * 1024 * 1024;

    /// <summary>单个图片 ZIP 上限 200MB。</summary>
    public const long MaxZipBytes = 200L * 1024 * 1024;

    /// <summary>multipart 请求体上限：Excel + ZIP + 表单头开销（约 224MB）。</summary>
    public const long MaxRequestBodyBytes = 224L * 1024 * 1024;

    private const string RootFolderName = "nc-elderly-cert";
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(24);

    private static readonly string[] ExcelExtensions = { ".xlsx", ".xls" };
    private static readonly string[] ZipExtensions = { ".zip" };

    private readonly ConcurrentDictionary<long, PreviewSession> _sessions = new();
    private long _nextPreviewId;
    private long _lastSweepUtcTicks;

    /// <summary>
    /// 上传 Excel + ZIP 并生成预览。调用方（端点）必须已校验当前用户对 campaignId 的数据范围。
    /// </summary>
    public async Task<Result<ElderlyCertImportPreviewResponse>> PreviewAsync(
        int userId,
        long campaignId,
        IFormFile? excel,
        IFormFile? zip,
        CancellationToken ct = default)
    {
        if (campaignId <= 0)
        {
            return Result.Failure<ElderlyCertImportPreviewResponse>(
                ErrorCodes.VALIDATION_FAILED, "请选择认证批次");
        }

        if (excel is null || excel.Length <= 0)
        {
            return Result.Failure<ElderlyCertImportPreviewResponse>(
                ErrorCodes.VALIDATION_FAILED, "请选择 Excel 名单文件（字段名 excel）");
        }

        if (zip is null || zip.Length <= 0)
        {
            return Result.Failure<ElderlyCertImportPreviewResponse>(
                ErrorCodes.VALIDATION_FAILED, "请选择图片 ZIP 文件（字段名 zip）");
        }

        var excelExtension = Path.GetExtension(excel.FileName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(excelExtension)
            || !ExcelExtensions.Contains(excelExtension, StringComparer.OrdinalIgnoreCase))
        {
            return Result.Failure<ElderlyCertImportPreviewResponse>(
                ErrorCodes.VALIDATION_FAILED, "Excel 仅支持 .xlsx / .xls 文件");
        }

        if (excel.Length > MaxExcelBytes)
        {
            return Result.Failure<ElderlyCertImportPreviewResponse>(
                ErrorCodes.VALIDATION_FAILED, "Excel 文件不能超过 20MB");
        }

        var zipExtension = Path.GetExtension(zip.FileName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(zipExtension)
            || !ZipExtensions.Contains(zipExtension, StringComparer.OrdinalIgnoreCase))
        {
            return Result.Failure<ElderlyCertImportPreviewResponse>(
                ErrorCodes.VALIDATION_FAILED, "图片压缩包仅支持 .zip 文件");
        }

        if (zip.Length > MaxZipBytes)
        {
            return Result.Failure<ElderlyCertImportPreviewResponse>(
                ErrorCodes.VALIDATION_FAILED, "图片 ZIP 不能超过 200MB");
        }

        var previewId = Interlocked.Increment(ref _nextPreviewId);
        var directory = BuildPreviewDirectory(previewId);
        var excelPath = Path.Combine(
            directory, "roster" + excelExtension.ToLowerInvariant());
        var zipPath = Path.Combine(directory, "evidence.zip");

        // 进程重启后 previewId 从 1 重新计数，可能命中上一进程残留的同名目录。
        if (Directory.Exists(directory))
        {
            TryDeleteDirectory(directory);
        }

        Directory.CreateDirectory(directory);

        try
        {
            var excelSave = await SaveUploadAsync(excel, excelPath, isExcel: true, excelExtension, ct);
            if (!excelSave.Ok)
            {
                TryDeleteDirectory(directory);
                return Result.Failure<ElderlyCertImportPreviewResponse>(
                    ErrorCodes.VALIDATION_FAILED, excelSave.Error!);
            }

            var zipSave = await SaveUploadAsync(zip, zipPath, isExcel: false, zipExtension, ct);
            if (!zipSave.Ok)
            {
                TryDeleteDirectory(directory);
                return Result.Failure<ElderlyCertImportPreviewResponse>(
                    ErrorCodes.VALIDATION_FAILED, zipSave.Error!);
            }

            var previewResult = await importService.PreviewAsync(campaignId, excelPath, zipPath, ct);
            if (previewResult.IsFailure)
            {
                TryDeleteDirectory(directory);
                return Result.Failure<ElderlyCertImportPreviewResponse>(
                    previewResult.ErrorCode!, previewResult.Message!);
            }

            var preview = previewResult.Value;
            var session = new PreviewSession(
                previewId,
                userId,
                campaignId,
                directory,
                excelPath,
                zipPath,
                preview.ExcelHash,
                preview.ZipHash,
                DateTime.UtcNow,
                preview);

            _sessions[previewId] = session;
            SweepExpiredSessions();

            return Result.Success(MapPreview(previewId, preview));
        }
        catch (Exception ex)
        {
            TryDeleteDirectory(directory);
            logger.LogError(ex, "高龄认证导入预览");
            return Result.Failure<ElderlyCertImportPreviewResponse>(
                ErrorCodes.UNKNOWN_ERROR, $"预览失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 按 previewId 取会话的批次 ID（供端点在 apply 前再做一次数据范围校验）。
    /// 同时校验会话归属用户、24h 有效期；过期/不存在返回 false。
    /// </summary>
    public bool TryGetSessionCampaignId(long previewId, int userId, out long campaignId)
    {
        campaignId = 0;
        if (previewId <= 0 || !_sessions.TryGetValue(previewId, out var session))
        {
            return false;
        }

        if (session.UserId != userId)
        {
            return false;
        }

        if (DateTime.UtcNow - session.CreatedAtUtc > SessionLifetime)
        {
            if (_sessions.TryRemove(previewId, out var expired))
            {
                TryDeleteDirectory(expired.Directory);
            }

            return false;
        }

        campaignId = session.CampaignId;
        return true;
    }

    /// <summary>
    /// 应用预览。应用前校验会话归属、24h 有效期、临时文件是否仍在、Excel/ZIP 哈希是否变化；
    /// 成功后删除临时目录与会话；Core 应用失败时把会话放回，允许修参数后重试。
    /// </summary>
    public async Task<Result<ElderlyCertImportApplyResult>> ApplyAsync(
        int userId,
        ElderlyCertImportApplyRequest? request,
        string operatorName,
        CancellationToken ct = default)
    {
        if (request is null || request.PreviewId <= 0)
        {
            return Result.Failure<ElderlyCertImportApplyResult>(
                ErrorCodes.VALIDATION_FAILED, "previewId 不能为空");
        }

        // 先原子摘除：同一预览并发 apply 时，第二个请求直接按过期处理，避免重复落库。
        if (!_sessions.TryRemove(request.PreviewId, out var session))
        {
            return PreviewExpired();
        }

        try
        {
            if (session.UserId != userId)
            {
                // 不暴露“该 previewId 属于别人”，按过期处理且不归还会话。
                TryDeleteDirectory(session.Directory);
                return PreviewExpired();
            }

            if (DateTime.UtcNow - session.CreatedAtUtc > SessionLifetime
                || !Directory.Exists(session.Directory)
                || !File.Exists(session.ExcelPath)
                || !File.Exists(session.ZipPath))
            {
                TryDeleteDirectory(session.Directory);
                return PreviewExpired();
            }

            var excelHash = await ComputeFileHashAsync(session.ExcelPath, ct);
            var zipHash = await ComputeFileHashAsync(session.ZipPath, ct);
            if (!string.Equals(excelHash, session.ExcelHash, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(zipHash, session.ZipHash, StringComparison.OrdinalIgnoreCase))
            {
                // 文件已变化，预览结论不再可信：删除会话，强制重新预览。
                TryDeleteDirectory(session.Directory);
                return Result.Failure<ElderlyCertImportApplyResult>(
                    ElderlyCertificationApiCodes.ImportFileChanged,
                    "Excel/ZIP 在预览后已变化，请重新上传并预览");
            }

            var options = new ElderlyCertImportApplyOptions
            {
                CampaignId = session.CampaignId,
                ExcelPath = session.ExcelPath,
                ZipPath = session.ZipPath,
                OperatorName = operatorName,
                AllowAdverseOverwrite = request.AllowAdverseOverwrite,
                ExcelHash = session.ExcelHash,
                ZipHash = session.ZipHash,
                RowNumbers = request.RowNumbers is { Count: > 0 }
                    ? new HashSet<int>(request.RowNumbers)
                    : null
            };

            var applyResult = await importService.ApplyAsync(session.Preview, options, ct);
            if (applyResult.IsFailure)
            {
                // 落库失败（如批次已关闭/状态冲突）可保留临时文件供用户调整后重试。
                _sessions.TryAdd(request.PreviewId, session);
                return applyResult;
            }

            TryDeleteDirectory(session.Directory);
            return applyResult;
        }
        catch (Exception ex)
        {
            _sessions.TryAdd(request.PreviewId, session);
            logger.LogError(ex, "高龄认证导入应用");
            return Result.Failure<ElderlyCertImportApplyResult>(
                ErrorCodes.UNKNOWN_ERROR, $"应用导入失败：{ex.Message}");
        }
    }

    // ───────────────────────────── 映射 ─────────────────────────────

    private static ElderlyCertImportPreviewResponse MapPreview(
        long previewId,
        ElderlyCertImportPreview preview)
    {
        var rows = preview.Rows
            .Select(row => new ElderlyCertImportRowDto(
                row.RowNo,
                row.IdCard ?? string.Empty,
                row.Name ?? string.Empty,
                row.Village ?? string.Empty,
                row.CertStatus ?? string.Empty,
                row.CertStatusName,
                row.VerifyMethod ?? string.Empty,
                ToDateOnly(row.VerifiedAt),
                row.ExpectedEvidenceCount,
                row.MatchedEvidenceCount,
                row.EvidenceStatus ?? string.Empty,
                row.EvidenceStatusName,
                row.MatchStatus ?? string.Empty,
                row.MatchStatusName,
                row.ErrorMessage ?? string.Empty,
                row.TaskId))
            .ToList();

        var unmatched = preview.UnmatchedEntries
            .Select(entry => new ElderlyCertUnmatchedEntryDto(
                entry.EntryName ?? string.Empty,
                entry.FileName ?? string.Empty,
                entry.Length,
                entry.Extension ?? string.Empty))
            .ToList();

        return new ElderlyCertImportPreviewResponse(
            previewId,
            preview.CampaignId,
            preview.BatchNo ?? string.Empty,
            preview.TotalRows,
            preview.MatchedRows,
            preview.UnknownRows,
            preview.DuplicateRows,
            preview.NameMismatchRows,
            preview.InvalidRows,
            preview.ConflictRows,
            preview.EvidenceTotal,
            preview.EvidenceMatched,
            preview.EvidenceMissing,
            preview.EvidenceUnmatched,
            preview.SummaryText,
            rows,
            unmatched,
            preview.Warnings.ToList(),
            preview.Errors.ToList());
    }

    private static Result<ElderlyCertImportApplyResult> PreviewExpired()
        => Result.Failure<ElderlyCertImportApplyResult>(
            ElderlyCertificationApiCodes.PreviewExpired,
            "预览已过期或临时文件已清理，请重新上传并预览");

    private static DateOnly? ToDateOnly(DateTime? value)
        => value is { Year: >= 1900 } date ? DateOnly.FromDateTime(date) : null;

    // ───────────────────────────── 临时文件 ─────────────────────────────

    private static string BuildPreviewDirectory(long previewId)
    {
        var root = Path.Combine(Path.GetTempPath(), RootFolderName);
        return Path.Combine(root, previewId.ToString(CultureInfo.InvariantCulture));
    }

    private static async Task<(bool Ok, string? Error)> SaveUploadAsync(
        IFormFile file,
        string path,
        bool isExcel,
        string extension,
        CancellationToken ct)
    {
        await using var input = file.OpenReadStream();

        // 只读文件头判断真实格式，防止把 .exe/.rar 改扩展名混进来；随后落盘时保留已读头。
        var header = new byte[8];
        var read = await input.ReadAsync(header.AsMemory(), ct);
        if (!HasValidHeader(header, read, isExcel, extension))
        {
            return (false, isExcel
                ? "Excel 文件内容不是有效的 .xlsx / .xls 格式"
                : "ZIP 文件内容不是有效的压缩包");
        }

        await using var output = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await output.WriteAsync(header.AsMemory(0, read), ct);
        await input.CopyToAsync(output, 64 * 1024, ct);
        return (true, null);
    }

    private static bool HasValidHeader(byte[] header, int read, bool isExcel, string extension)
    {
        if (read < 4)
        {
            return false;
        }

        const byte pk1 = 0x50; // 'P'
        const byte pk2 = 0x4B; // 'K'
        var isZip = header[0] == pk1 && header[1] == pk2;

        if (!isExcel)
        {
            return isZip;
        }

        if (string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return isZip;
        }

        // .xls（OLE2 复合文档）：D0 CF 11 E0 A1 B1 1A E1
        return read >= 8
               && header[0] == 0xD0
               && header[1] == 0xCF
               && header[2] == 0x11
               && header[3] == 0xE0
               && header[4] == 0xA1
               && header[5] == 0xB1
               && header[6] == 0x1A
               && header[7] == 0xE1;
    }

    private static async Task<string> ComputeFileHashAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException ex)
        {
            logger.Error($"清理高龄认证导入临时目录失败: {directory} {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.Error($"清理高龄认证导入临时目录无权限: {directory} {ex.Message}");
        }
    }

    // ───────────────────────────── 过期清理 ─────────────────────────────

    private void SweepExpiredSessions()
    {
        var now = DateTime.UtcNow;
        var lastTicks = Interlocked.Read(ref _lastSweepUtcTicks);
        if (now.Ticks - lastTicks < TimeSpan.FromMinutes(10).Ticks)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _lastSweepUtcTicks, now.Ticks, lastTicks) != lastTicks)
        {
            return;
        }

        foreach (var pair in _sessions)
        {
            if (now - pair.Value.CreatedAtUtc <= SessionLifetime)
            {
                continue;
            }

            if (_sessions.TryRemove(pair.Key, out var expired))
            {
                TryDeleteDirectory(expired.Directory);
            }
        }

        // 服务重启后内存会话丢失，但临时目录可能残留；按目录创建时间清理 24h 以上的孤儿目录。
        try
        {
            var root = Path.Combine(Path.GetTempPath(), RootFolderName);
            if (!Directory.Exists(root))
            {
                return;
            }

            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                try
                {
                    var createdAt = Directory.GetCreationTimeUtc(directory);
                    if (now - createdAt > SessionLifetime)
                    {
                        TryDeleteDirectory(directory);
                    }
                }
                catch (IOException)
                {
                    // 单项清理失败不影响其它目录。
                }
            }
        }
        catch (Exception ex)
        {
            logger.Error($"清理高龄认证导入孤儿目录失败: {ex.Message}");
        }
    }

    /// <summary>一次预览会话（不可变；应用时通过 TryRemove 串行化）。</summary>
    private sealed class PreviewSession(
        long previewId,
        int userId,
        long campaignId,
        string directory,
        string excelPath,
        string zipPath,
        string excelHash,
        string zipHash,
        DateTime createdAtUtc,
        ElderlyCertImportPreview preview)
    {
        public long PreviewId { get; } = previewId;
        public int UserId { get; } = userId;
        public long CampaignId { get; } = campaignId;
        public string Directory { get; } = directory;
        public string ExcelPath { get; } = excelPath;
        public string ZipPath { get; } = zipPath;
        public string ExcelHash { get; } = excelHash ?? string.Empty;
        public string ZipHash { get; } = zipHash ?? string.Empty;
        public DateTime CreatedAtUtc { get; } = createdAtUtc;
        public ElderlyCertImportPreview Preview { get; } = preview;
    }
}
