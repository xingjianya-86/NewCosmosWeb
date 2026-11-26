using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Services.Database;

namespace NewCosmos.Server.Services.Reprint;

/// <summary>
/// 补打留痕读写：查询历史打印记录、读取原样 PDF、接收 Agent 回传的 PDF/源文件字节。
/// <para>
/// 留痕主表 <c>nc_biz_print_records</c> 无 job_id / file_name 列，Agent 回传的幂等键
/// 编码在 <c>remark</c>：<c>补打留痕|job={jobId}|file={fileName}</c>；
/// 同一 job + 模板 + 文件名先删后插，重复回传不会产生重复留痕。
/// </para>
/// </summary>
public sealed class ReprintPrintRecordService(IDatabaseService database)
{
    /// <summary>按业务域 + 业务 ID 查询打印留痕（倒序）。</summary>
    public async Task<Result<List<ReprintHistoryItem>>> GetHistoryAsync(
        string domain,
        long businessId,
        int limit = 50,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(domain) || businessId <= 0)
        {
            return Result.Failure<List<ReprintHistoryItem>>(
                ErrorCodes.VALIDATION_FAILED, "domain 与 id 不能为空");
        }

        if (limit <= 0) limit = 50;
        if (limit > 200) limit = 200;

        const string sql = @"SELECT id, template_name, created_at, operator_name, status,
                                    COALESCE(printer_name, '') AS PrinterName,
                                    COALESCE(remark, '') AS Remark,
                                    COALESCE(batch_no, '') AS BatchNo,
                                    COALESCE(OCTET_LENGTH(pdf_data), 0) AS PdfSize
                             FROM nc_biz_print_records
                             WHERE business_type = $1 AND business_id = $2
                             ORDER BY created_at DESC
                             LIMIT $3";

        var result = await database.QueryAsync<ReprintHistoryRow>(sql, ct, domain.Trim(), businessId, limit);
        if (result.IsFailure)
        {
            return Result.Failure<List<ReprintHistoryItem>>(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "打印留痕查询失败");
        }

        return Result.Success((result.Value ?? [])
            .Select(row => new ReprintHistoryItem(
                row.Id,
                row.TemplateName ?? string.Empty,
                row.PrinterName ?? string.Empty,
                row.CreatedAt,
                row.OperatorName ?? string.Empty,
                row.Status ?? string.Empty,
                row.Remark ?? string.Empty,
                row.BatchNo ?? string.Empty,
                row.PdfSize > 0,
                row.PdfSize))
            .ToList());
    }

    /// <summary>
    /// 原样补打：按留痕倒序索引读取历史 PDF 字节。
    /// 优先 pdf_data；历史数据仅有 pdf_path 且服务端文件仍存在时兜底读取。
    /// </summary>
    public async Task<Result<ReprintOriginalPdf>> GetOriginalAsync(
        string domain,
        long businessId,
        int index = 0,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(domain) || businessId <= 0)
        {
            return Result.Failure<ReprintOriginalPdf>(
                ErrorCodes.VALIDATION_FAILED, "domain 与 id 不能为空");
        }

        if (index < 0) index = 0;

        const string sql = @"SELECT id, template_name, pdf_data, COALESCE(pdf_path, '') AS PdfPath, created_at
                             FROM nc_biz_print_records
                             WHERE business_type = $1 AND business_id = $2
                             ORDER BY created_at DESC
                             OFFSET $3 LIMIT 1";

        var result = await database.QuerySingleAsync<ReprintOriginalRow>(sql, ct, domain.Trim(), businessId, index);
        if (result.IsFailure)
        {
            return Result.Failure<ReprintOriginalPdf>(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "打印留痕查询失败");
        }

        var row = result.Value;
        if (row is null)
        {
            return Result.Failure<ReprintOriginalPdf>(
                ErrorCodes.NOT_FOUND, "打印留痕不存在");
        }

        if (row.PdfData is { Length: > 0 })
        {
            return Result.Success(new ReprintOriginalPdf(
                row.PdfData, $"{row.TemplateName ?? "reprint"}_{row.Id}.pdf", row.TemplateName ?? string.Empty));
        }

        // 兜底：老留痕只落 pdf_path（Server 与 Windows 输出目录同机部署时可用）。
        if (!string.IsNullOrWhiteSpace(row.PdfPath) && File.Exists(row.PdfPath))
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(row.PdfPath, ct);
                if (bytes.Length > 0)
                {
                    return Result.Success(new ReprintOriginalPdf(
                        bytes, Path.GetFileName(row.PdfPath), row.TemplateName ?? string.Empty));
                }
            }
            catch (IOException ex)
            {
                return Result.Failure<ReprintOriginalPdf>(
                    ErrorCodes.FILE_READ_ERROR, $"读取历史 PDF 失败: {ex.Message}");
            }
        }

        return Result.Failure<ReprintOriginalPdf>(
            ErrorCodes.NOT_FOUND,
            "该留痕没有 PDF 数据（可能仅保存了源文件），无法原样补打，请改用模板补打");
    }

    /// <summary>
    /// Agent 回传留痕：同一 job + 模板 + 文件名幂等写入。
    /// pdf_data/source_data 存 bytea；status=Completed；batch_no 取 job.batch_no。
    /// </summary>
    public async Task<Result<int>> SaveAgentRecordsAsync(
        PrintJob job,
        IReadOnlyList<AgentPrintRecordInput> records,
        CancellationToken ct = default)
    {
        if (records.Count == 0)
        {
            return Result.Failure<int>(ErrorCodes.VALIDATION_FAILED, "records 不能为空");
        }

        var names = records
            .Select(record => record.TemplateName?.Trim())
            .OfType<string>()
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var templateMapResult = await ResolveTemplateIdsAsync(job, names, ct);
        if (templateMapResult.IsFailure || templateMapResult.Value is null)
        {
            return Result.Failure<int>(
                templateMapResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                templateMapResult.Message ?? "模板名称解析失败");
        }

        var templateMap = templateMapResult.Value;
        var businessId = job.BusinessId ?? 0;
        var written = 0;

        foreach (var record in records)
        {
            var templateName = string.IsNullOrWhiteSpace(record.TemplateName)
                ? job.TemplateName ?? string.Empty
                : record.TemplateName.Trim();
            var fileName = string.IsNullOrWhiteSpace(record.FileName) ? string.Empty : record.FileName.Trim();
            var remark = BuildRemark(job.Id, fileName);

            var pdfResult = DecodeBase64(record.PdfBase64, "pdfBase64");
            if (pdfResult.IsFailure)
            {
                return Result.Failure<int>(pdfResult.ErrorCode!, pdfResult.Message!);
            }

            var sourceResult = DecodeBase64(record.SourceBase64, "sourceBase64");
            if (sourceResult.IsFailure)
            {
                return Result.Failure<int>(sourceResult.ErrorCode!, sourceResult.Message!);
            }

            var pdf = pdfResult.Value ?? [];
            var source = sourceResult.Value ?? [];
            var pdfSize = record.PdfSize is > 0 ? record.PdfSize.Value : pdf.LongLength;
            var sourceSize = record.SourceSize is > 0 ? record.SourceSize.Value : source.LongLength;

            var templateId = ResolveTemplateId(job, templateName, templateMap);

            // 幂等：同一 job + 模板 + 文件名先删后插（remark 内含 jobId + fileName）。
            const string deleteSql = @"DELETE FROM nc_biz_print_records
                                       WHERE business_type = $1
                                         AND COALESCE(business_id, 0) = $2
                                         AND template_name = $3
                                         AND remark = $4";
            var deleteResult = await database.ExecuteNonQueryAsync(
                deleteSql, ct, job.BusinessType, businessId, templateName, remark);
            if (deleteResult.IsFailure)
            {
                return Result.Failure<int>(
                    deleteResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    deleteResult.Message ?? "打印留痕去重失败");
            }

            const string insertSql = @"INSERT INTO nc_biz_print_records
                    (batch_no, business_type, business_id, template_id, template_name,
                     pdf_data, pdf_size, source_data, source_type, source_size,
                     file_path, pdf_path, printer_name, copies, operator_id, operator_name,
                     applicant_id_card, status, remark, created_at)
                    VALUES ($1, $2, $3, $4, $5,
                            $6, $7, $8, $9, $10,
                            '', '', $11, $12, $13, $14,
                            $15, $16, $17, NOW())";

            var insertResult = await database.ExecuteNonQueryAsync(
                insertSql, ct,
                job.BatchNo ?? string.Empty,
                job.BusinessType,
                businessId,
                templateId.ToString(),
                templateName,
                pdf,
                pdfSize,
                source,
                record.SourceType ?? string.Empty,
                sourceSize,
                job.PrinterName ?? job.TargetPrinter ?? string.Empty,
                job.Copies,
                job.RequestedBy,
                job.RequestedByName ?? string.Empty,
                job.ApplicantIdCard ?? string.Empty,
                PrintJobConstants.StatusCompleted,
                remark);

            if (insertResult.IsFailure)
            {
                return Result.Failure<int>(
                    insertResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    insertResult.Message ?? "打印留痕写入失败");
            }

            written++;
        }

        return Result.Success(written);
    }

    /// <summary>Agent 回传幂等/溯源标记（写入 remark）。</summary>
    public static string BuildRemark(long jobId, string? fileName)
        => string.IsNullOrWhiteSpace(fileName)
            ? $"补打留痕|job={jobId}"
            : $"补打留痕|job={jobId}|file={fileName.Trim()}";

    private async Task<Result<Dictionary<string, long>>> ResolveTemplateIdsAsync(
        PrintJob job,
        IReadOnlyList<string> names,
        CancellationToken ct)
    {
        var map = new Dictionary<string, long>(StringComparer.Ordinal);

        // 单模板任务直接命中，无需回查模板表。
        if (job.TemplateId is > 0 && !string.IsNullOrWhiteSpace(job.TemplateName))
        {
            map[job.TemplateName] = job.TemplateId.Value;
        }

        if (names.Count == 0)
        {
            return Result.Success(map);
        }

        var result = await database.QueryAsync<ReprintTemplateIdRow>(
            @"SELECT id, name FROM nc_biz_templates WHERE name = ANY($1::text[])",
            ct,
            names.ToArray());

        if (result.IsFailure)
        {
            return Result.Failure<Dictionary<string, long>>(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "模板名称解析失败");
        }

        foreach (var row in result.Value ?? [])
        {
            if (!string.IsNullOrWhiteSpace(row.Name))
            {
                map.TryAdd(row.Name, row.Id);
            }
        }

        return Result.Success(map);
    }

    private static long ResolveTemplateId(
        PrintJob job,
        string templateName,
        IReadOnlyDictionary<string, long> templateMap)
    {
        if (templateMap.TryGetValue(templateName, out var id) && id > 0)
        {
            return id;
        }

        return job.TemplateId is > 0 ? job.TemplateId.Value : 0;
    }

    private static Result<byte[]> DecodeBase64(string? base64, string field)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return Result.Success(Array.Empty<byte>());
        }

        var payload = base64.Trim();
        if (payload.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = payload.IndexOf(',');
            if (comma >= 0) payload = payload[(comma + 1)..];
        }

        var buffer = new byte[payload.Length];
        if (!Convert.TryFromBase64String(payload, buffer, out var written))
        {
            return Result.Failure<byte[]>(
                ErrorCodes.VALIDATION_FAILED, $"{field} 不是合法的 Base64 字符串");
        }

        return Result.Success(buffer[..written]);
    }

    private sealed class ReprintHistoryRow
    {
        public long Id { get; set; }
        public string? TemplateName { get; set; }
        public string? PrinterName { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? OperatorName { get; set; }
        public string? Status { get; set; }
        public string? Remark { get; set; }
        public string? BatchNo { get; set; }
        public long PdfSize { get; set; }
    }

    private sealed class ReprintOriginalRow
    {
        public long Id { get; set; }
        public string? TemplateName { get; set; }
        public byte[]? PdfData { get; set; }
        public string? PdfPath { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    private sealed class ReprintTemplateIdRow
    {
        public long Id { get; set; }
        public string? Name { get; set; }
    }
}

/// <summary>打印留痕列表项（补打历史）。</summary>
public sealed record ReprintHistoryItem(
    long Id,
    string TemplateName,
    string PrinterName,
    DateTime CreatedAt,
    string OperatorName,
    string Status,
    string Remark,
    string BatchNo,
    bool HasPdf,
    long PdfSize);

/// <summary>原样补打 PDF 字节。</summary>
public sealed record ReprintOriginalPdf(byte[] Data, string FileName, string TemplateName);
