using System.Security.Cryptography;
using System.Text.Json;
using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Server.Services;

/// <summary>
/// 证明接收单位模板映射服务（Server 侧）。
/// <para>
/// 模板文件本体存 <c>nc_biz_templates</c>，本服务只负责「接收单位 → 专属模板」映射的维护。
/// 口径与旧版 <c>ProofUnitTemplateService</c> 一致：单位名唯一；新增时事务内先建模板再写映射；
/// 删除只删映射，保留模板文件。模板分类固定为「证明文件」。
/// </para>
/// </summary>
public sealed class ProofUnitTemplateService(IDatabaseService database, ILoggerService logger)
{
    /// <summary>单位证明模板分类标签（存 nc_biz_templates.categories）。</summary>
    public const string ProofCategory = "证明文件";

    /// <summary>模板文档类型（nc_biz_templates.doc_type）。</summary>
    public const string ProofDocType = "证明文件";

    public async Task<Result<List<ProofUnitTemplateItem>>> GetAllAsync(CancellationToken ct = default)
    {
        const string sql = @"SELECT u.id, u.unit_name, u.template_id, u.created_at, u.updated_at,
                                    COALESCE(t.name, '') AS template_name,
                                    COALESCE(t.file_type, '') AS file_type,
                                    COALESCE(t.config_json::text, '') AS config_json
                             FROM nc_config_proof_unit_templates u
                             LEFT JOIN nc_biz_templates t ON u.template_id = t.id
                             ORDER BY u.unit_name";

        var result = await database.QueryAsync<ProofUnitTemplateItem>(sql, ct);
        return result.IsFailure
            ? Result.Failure<List<ProofUnitTemplateItem>>(result.ErrorCode!, result.Message!)
            : Result.Success(result.Value ?? []);
    }

    public async Task<Result<ProofUnitTemplateItem>> GetByIdAsync(long id, CancellationToken ct = default)
    {
        const string sql = @"SELECT u.id, u.unit_name, u.template_id, u.created_at, u.updated_at,
                                    COALESCE(t.name, '') AS template_name,
                                    COALESCE(t.file_type, '') AS file_type,
                                    COALESCE(t.config_json::text, '') AS config_json
                             FROM nc_config_proof_unit_templates u
                             LEFT JOIN nc_biz_templates t ON u.template_id = t.id
                             WHERE u.id = $1";

        var result = await database.QuerySingleAsync<ProofUnitTemplateItem?>(sql, ct, id);
        if (result.IsFailure)
        {
            return Result.Failure<ProofUnitTemplateItem>(result.ErrorCode!, result.Message!);
        }

        return result.Value is null
            ? Result.Failure<ProofUnitTemplateItem>(ErrorCodes.RECORD_NOT_FOUND, "单位模板不存在")
            : Result.Success(result.Value);
    }

    /// <summary>
    /// 新增单位模板：事务内插入 <c>nc_biz_templates</c>（name=证明_{unitName}）与映射。
    /// </summary>
    public async Task<Result<long>> CreateAsync(
        string? unitName,
        string? fileType,
        byte[]? fileData,
        string? fileName,
        string? configJson,
        int? operatorId,
        CancellationToken ct = default)
    {
        var name = unitName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "请输入接收单位名称");
        }

        if (fileData is not { Length: > 0 })
        {
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "请选择模板文件");
        }

        var typeResult = ResolveFileType(fileType, fileName);
        if (typeResult.IsFailure)
        {
            return Result.Failure<long>(typeResult.ErrorCode!, typeResult.Message!);
        }

        var configError = ValidateConfigJson(configJson);
        if (configError is not null)
        {
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, configError);
        }

        var normalizedType = typeResult.Value;

        var existsResult = await database.ExecuteScalarAsync<long?>(
            "SELECT id FROM nc_config_proof_unit_templates WHERE unit_name = $1 LIMIT 1",
            ct, name);
        if (existsResult.IsFailure)
        {
            return Result.Failure<long>(existsResult.ErrorCode!, existsResult.Message!);
        }

        if (existsResult.Value is > 0)
        {
            return Result.Failure<long>(
                ErrorCodes.VALIDATION_FAILED, $"单位 [{name}] 已配置证明模板，请直接修改");
        }

        var templateName = $"证明_{name}";
        var fileHash = ComputeSha256(fileData);

        await using var tx = await database.BeginTransactionScopeAsync(ct);

        const string insertTemplateSql = @"INSERT INTO nc_biz_templates
                (name, file_type, file_data, file_hash, categories, doc_type, sort_order, config_json, created_at, updated_at)
                VALUES ($1, $2, $3, $4, $5::text[], $6, 0, NULLIF($7, '')::jsonb, NOW(), NOW())
                RETURNING id";

        object?[] templateArgs =
        [
            templateName,
            normalizedType,
            fileData,
            fileHash,
            new[] { ProofCategory },
            ProofDocType,
            configJson ?? string.Empty
        ];

        var templateResult = await database.QuerySingleAsync<long>(insertTemplateSql, ct, templateArgs);
        if (templateResult.IsFailure)
        {
            await tx.RollbackAsync(ct);
            return Result.Failure<long>(templateResult.ErrorCode!, templateResult.Message!);
        }

        const string insertMappingSql = @"INSERT INTO nc_config_proof_unit_templates
                (unit_name, template_id, created_by, updated_by, created_at)
                VALUES ($1, $2, $3, $3, NOW())
                RETURNING id";

        var mappingResult = await database.QuerySingleAsync<long>(
            insertMappingSql, ct, name, templateResult.Value, operatorId);
        if (mappingResult.IsFailure)
        {
            await tx.RollbackAsync(ct);
            return Result.Failure<long>(mappingResult.ErrorCode!, mappingResult.Message!);
        }

        await tx.CommitAsync(ct);

        logger.Info($"新增证明单位模板: {name} (TemplateId={templateResult.Value})");
        return Result.Success(mappingResult.Value);
    }

    /// <summary>
    /// 更新单位模板：可改单位名；<paramref name="fileData"/> 非空时替换模板文件（字节/哈希/类型/名称）。
    /// </summary>
    public async Task<Result> UpdateAsync(
        long id,
        string? unitName,
        string? fileType,
        byte[]? fileData,
        string? fileName,
        string? configJson,
        int? operatorId,
        CancellationToken ct = default)
    {
        var currentResult = await GetByIdAsync(id, ct);
        if (currentResult.IsFailure)
        {
            return Result.Failure(currentResult.ErrorCode!, currentResult.Message!);
        }

        var current = currentResult.Value;
        var newName = string.IsNullOrWhiteSpace(unitName) ? current.UnitName : unitName.Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "请输入接收单位名称");
        }

        if (!string.Equals(newName, current.UnitName, StringComparison.Ordinal))
        {
            var existsResult = await database.ExecuteScalarAsync<long?>(
                "SELECT id FROM nc_config_proof_unit_templates WHERE unit_name = $1 AND id <> $2 LIMIT 1",
                ct, newName, id);
            if (existsResult.IsFailure)
            {
                return Result.Failure(existsResult.ErrorCode!, existsResult.Message!);
            }

            if (existsResult.Value is > 0)
            {
                return Result.Failure(
                    ErrorCodes.VALIDATION_FAILED, $"单位 [{newName}] 已配置证明模板");
            }
        }

        string? normalizedType = null;
        if (fileData is { Length: > 0 })
        {
            var typeResult = ResolveFileType(fileType, fileName);
            if (typeResult.IsFailure)
            {
                return Result.Failure(typeResult.ErrorCode!, typeResult.Message!);
            }

            normalizedType = typeResult.Value;
        }

        if (configJson is not null)
        {
            var configError = ValidateConfigJson(configJson);
            if (configError is not null)
            {
                return Result.Failure(ErrorCodes.VALIDATION_FAILED, configError);
            }
        }

        await using var tx = await database.BeginTransactionScopeAsync(ct);

        if (fileData is { Length: > 0 } && normalizedType is not null)
        {
            const string updateTemplateSql = @"UPDATE nc_biz_templates
                    SET name = $1, file_type = $2, file_data = $3, file_hash = $4,
                        config_json = NULLIF($5, '')::jsonb, updated_at = NOW()
                    WHERE id = $6";

            var templateResult = await database.ExecuteNonQueryAsync(
                updateTemplateSql, ct,
                $"证明_{newName}", normalizedType, fileData, ComputeSha256(fileData),
                configJson ?? current.ConfigJson, current.TemplateId);
            if (templateResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure(templateResult.ErrorCode!, templateResult.Message!);
            }

            if (templateResult.Value <= 0)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure(
                    ErrorCodes.RECORD_NOT_FOUND, "单位模板关联的模板文件不存在，无法替换");
            }
        }
        else if (configJson is not null)
        {
            const string updateConfigSql = @"UPDATE nc_biz_templates
                    SET config_json = NULLIF($1, '')::jsonb, updated_at = NOW()
                    WHERE id = $2";

            var configResult = await database.ExecuteNonQueryAsync(
                updateConfigSql, ct, configJson, current.TemplateId);
            if (configResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure(configResult.ErrorCode!, configResult.Message!);
            }

            if (configResult.Value <= 0)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure(
                    ErrorCodes.RECORD_NOT_FOUND, "单位模板关联的模板文件不存在，无法更新字段映射");
            }
        }

        const string updateMappingSql = @"UPDATE nc_config_proof_unit_templates
                SET unit_name = $1, updated_by = $2, updated_at = NOW()
                WHERE id = $3";

        var mappingResult = await database.ExecuteNonQueryAsync(
            updateMappingSql, ct, newName, operatorId, id);
        if (mappingResult.IsFailure)
        {
            await tx.RollbackAsync(ct);
            return Result.Failure(mappingResult.ErrorCode!, mappingResult.Message!);
        }

        await tx.CommitAsync(ct);

        logger.Info($"更新证明单位模板: {id} -> {newName}");
        return Result.Success();
    }

    /// <summary>删除单位模板映射，保留 <c>nc_biz_templates</c> 中的模板文件。</summary>
    public async Task<Result> DeleteAsync(long id, CancellationToken ct = default)
    {
        var result = await database.ExecuteNonQueryAsync(
            "DELETE FROM nc_config_proof_unit_templates WHERE id = $1", ct, id);
        if (result.IsFailure)
        {
            return Result.Failure(result.ErrorCode!, result.Message!);
        }

        if (result.Value <= 0)
        {
            return Result.Failure(ErrorCodes.RECORD_NOT_FOUND, "单位模板不存在");
        }

        logger.Info($"删除证明单位模板: {id}");
        return Result.Success();
    }

    /// <summary>
    /// 归一化文件类型：优先按扩展名（.docx/.xlsx）判定；请求显式传 fileType 时要求与扩展名一致。
    /// </summary>
    private static Result<string> ResolveFileType(string? fileType, string? fileName)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        if (extension is not (".docx" or ".xlsx"))
        {
            return Result.Failure<string>(
                ErrorCodes.VALIDATION_FAILED, "模板文件仅支持 .docx 或 .xlsx");
        }

        var fromExtension = extension == ".xlsx"
            ? TemplateFileTypes.Excel
            : TemplateFileTypes.Word;

        if (string.IsNullOrWhiteSpace(fileType))
        {
            return Result.Success(fromExtension);
        }

        string normalized;
        try
        {
            normalized = TemplateFileTypes.Normalize(fileType);
        }
        catch (NotSupportedException)
        {
            return Result.Failure<string>(
                ErrorCodes.VALIDATION_FAILED, "fileType 仅支持 docx / xlsx");
        }

        if (!string.Equals(normalized, fromExtension, StringComparison.Ordinal))
        {
            return Result.Failure<string>(
                ErrorCodes.VALIDATION_FAILED, "fileType 与模板文件扩展名不一致");
        }

        return Result.Success(normalized);
    }

    private static string? ValidateConfigJson(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
        {
            return null;
        }

        try
        {
            using var _ = JsonDocument.Parse(configJson);
            return null;
        }
        catch (JsonException)
        {
            return "字段映射 configJson 不是合法 JSON";
        }
    }

    private static string ComputeSha256(byte[] data)
        => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}

/// <summary>单位模板展示项（含模板名称与类型）。</summary>
public sealed class ProofUnitTemplateItem
{
    public long Id { get; set; }

    public string UnitName { get; set; } = string.Empty;

    public long TemplateId { get; set; }

    public string TemplateName { get; set; } = string.Empty;

    public string FileType { get; set; } = string.Empty;

    public string ConfigJson { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}
