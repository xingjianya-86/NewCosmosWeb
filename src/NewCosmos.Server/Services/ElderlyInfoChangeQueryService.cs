using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.ElderlyBenefits;

namespace NewCosmos.Server.Services;

/// <summary>
/// 高龄年度认证·信息变更查询。Server 侧按当前用户数据范围过滤 records，
/// 明细用一次批量查询补齐；不使用 Core 的无范围列表。
/// </summary>
public sealed class ElderlyInfoChangeQueryService(
    IDatabaseService database,
    ElderlyCertificationQueryService certificationQuery,
    IElderlyInfoChangeService infoChangeService,
    ILoggerService logger)
{
    private const string RecordTable = "nc_biz_elderly_info_change_records";
    private const string DetailTable = "nc_biz_elderly_info_change_details";

    private const string RecordColumns = @"
        r.id, r.change_no, r.campaign_id, r.task_id, r.application_id, r.id_card, r.name,
        r.change_type, r.reason, r.status, r.source_type, r.source_file, r.source_row,
        r.effective_month, r.applied_at, r.applied_by, r.reviewed_at, r.reviewed_by,
        r.review_opinion, r.created_at, r.created_by, r.updated_at, r.deleted_at";

    private const string DetailColumns = @"
        d.id, d.change_id, d.field_key, d.field_label, d.old_value, d.new_value, d.created_at";

    public async Task<Result<ElderlyInfoChangeListResponse>> GetPagedAsync(
        int userId,
        string? keyword,
        string? status,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var pageIndex = Math.Max(page, 1);
        var effectivePageSize = Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, 200);

        var conditions = new List<string> { "r.deleted_at IS NULL" };
        var parameters = new List<object?>();

        if (string.IsNullOrWhiteSpace(status))
        {
            parameters.Add(ElderlyInfoChangeConstants.ReviewListStatuses);
            conditions.Add("r.status = ANY($1::text[])");
        }
        else
        {
            if (!ElderlyInfoChangeConstants.StatusOptions.Any(o => o.Key == status))
            {
                return Result.Failure<ElderlyInfoChangeListResponse>(
                    ErrorCodes.VALIDATION_FAILED, "变更状态不合法");
            }

            parameters.Add(status);
            conditions.Add("r.status = $1");
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            parameters.Add($"%{keyword.Trim()}%");
            conditions.Add(
                $"(r.name ILIKE ${parameters.Count} OR r.id_card ILIKE ${parameters.Count} OR r.change_no ILIKE ${parameters.Count})");
        }

        var scope = await certificationQuery.BuildTaskScopeAsync(
            userId, "t", parameters.Count + 1, ct);
        if (!scope.IsEmpty)
        {
            conditions.Add(
                "r.task_id IS NOT NULL AND EXISTS (" +
                "SELECT 1 FROM nc_biz_elderly_cert_tasks t " +
                "WHERE t.id = r.task_id AND t.deleted_at IS NULL AND (" + scope.Sql + "))");
            parameters.AddRange(scope.Parameters);
        }

        var where = " WHERE " + string.Join(" AND ", conditions);
        var countResult = await database.ExecuteScalarAsync<long?>(
            $"SELECT COUNT(*) FROM {RecordTable} r{where}",
            ct,
            parameters.ToArray());
        if (countResult.IsFailure)
        {
            return Result.Failure<ElderlyInfoChangeListResponse>(
                countResult.ErrorCode!, countResult.Message!);
        }

        var totalCount = countResult.Value ?? 0;
        if (totalCount == 0)
        {
            return Result.Success(new ElderlyInfoChangeListResponse(
                Array.Empty<ElderlyInfoChangeRecordDto>(),
                pageIndex,
                effectivePageSize,
                0,
                0));
        }

        var listParameters = new List<object?>(parameters)
        {
            effectivePageSize,
            (long)(pageIndex - 1) * effectivePageSize
        };
        var limitIndex = parameters.Count + 1;
        var offsetIndex = parameters.Count + 2;

        var listSql =
            $@"SELECT {RecordColumns}
               FROM {RecordTable} r{where}
               ORDER BY r.created_at DESC NULLS LAST, r.id DESC
               LIMIT ${limitIndex} OFFSET ${offsetIndex}";

        var rowsResult = await database.QueryAsync<ElderlyInfoChangeRecord>(
            listSql, ct, listParameters.ToArray());
        if (rowsResult.IsFailure)
        {
            return Result.Failure<ElderlyInfoChangeListResponse>(
                rowsResult.ErrorCode!, rowsResult.Message!);
        }

        var records = rowsResult.Value ?? new List<ElderlyInfoChangeRecord>();
        await LoadDetailsAsync(records, ct);

        var items = records.Select(MapRecord).ToList();
        var totalPages = (int)((totalCount + effectivePageSize - 1) / effectivePageSize);
        return Result.Success(new ElderlyInfoChangeListResponse(
            items, pageIndex, effectivePageSize, totalCount, totalPages));
    }

    public async Task<Result<IReadOnlyList<ElderlyInfoChangeRecordDto>>> GetByTaskAsync(
        int userId,
        long taskId,
        CancellationToken ct = default)
    {
        var scoped = await certificationQuery.GetScopedTaskAsync(userId, taskId, ct);
        if (scoped.IsFailure)
        {
            return Result.Failure<IReadOnlyList<ElderlyInfoChangeRecordDto>>(
                scoped.ErrorCode!, scoped.Message!);
        }

        var result = await infoChangeService.GetByTaskAsync(taskId, ct);
        if (result.IsFailure)
        {
            return Result.Failure<IReadOnlyList<ElderlyInfoChangeRecordDto>>(
                result.ErrorCode!, result.Message!);
        }

        var records = result.Value ?? new List<ElderlyInfoChangeRecord>();
        return Result.Success<IReadOnlyList<ElderlyInfoChangeRecordDto>>(
            records.Select(MapRecord).ToList());
    }

    /// <summary>创建变更前校验任务在数据范围内。</summary>
    public Task<Result<ElderlyCertTask>> GetScopedRecordTaskAsync(
        int userId,
        long taskId,
        CancellationToken ct = default) =>
        certificationQuery.GetScopedTaskAsync(userId, taskId, ct);

    /// <summary>按变更单ID取记录，并做任务数据范围校验；审批/驳回前调用。</summary>
    public async Task<Result<ElderlyInfoChangeRecord>> GetScopedRecordAsync(
        int userId,
        long changeId,
        CancellationToken ct = default)
    {
        if (changeId <= 0)
        {
            return Result.Failure<ElderlyInfoChangeRecord>(
                ErrorCodes.VALIDATION_FAILED, "变更单ID不合法");
        }

        var sql =
            $@"SELECT {RecordColumns}
               FROM {RecordTable} r
               WHERE r.id = $1 AND r.deleted_at IS NULL
               LIMIT 1";
        var result = await database.QueryAsync<ElderlyInfoChangeRecord>(sql, ct, changeId);
        if (result.IsFailure)
        {
            return Result.Failure<ElderlyInfoChangeRecord>(
                result.ErrorCode!, result.Message!);
        }

        var record = result.Value?.FirstOrDefault();
        if (record?.TaskId is not > 0)
        {
            return Result.Failure<ElderlyInfoChangeRecord>(
                ApiErrorCodes.NotFound, "变更单不存在或无权查看");
        }

        var scoped = await certificationQuery.GetScopedTaskAsync(userId, record.TaskId.Value, ct);
        if (scoped.IsFailure)
        {
            return Result.Failure<ElderlyInfoChangeRecord>(
                ApiErrorCodes.NotFound, "变更单不存在或无权查看");
        }

        await LoadDetailsAsync(new List<ElderlyInfoChangeRecord> { record }, ct);
        return Result.Success(record);
    }

    private async Task LoadDetailsAsync(
        IReadOnlyList<ElderlyInfoChangeRecord> records,
        CancellationToken ct)
    {
        var ids = records.Select(r => r.Id).ToArray();
        if (ids.Length == 0) return;

        var sql =
            $@"SELECT {DetailColumns}
               FROM {DetailTable} d
               WHERE d.change_id = ANY($1::bigint[])
               ORDER BY d.id";
        var result = await database.QueryAsync<ElderlyInfoChangeDetail>(sql, ct, ids);
        if (result.IsFailure)
        {
            logger.Warn($"加载信息变更明细失败: {result.ErrorCode} {result.Message}");
            return;
        }

        var map = (result.Value ?? new List<ElderlyInfoChangeDetail>())
            .GroupBy(d => d.ChangeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var record in records)
        {
            record.Details = map.TryGetValue(record.Id, out var details)
                ? details
                : new List<ElderlyInfoChangeDetail>();
        }
    }

    private static ElderlyInfoChangeRecordDto MapRecord(ElderlyInfoChangeRecord record)
    {
        var details = record.Details
            .Select(detail => new ElderlyInfoChangeDetailDto(
                detail.Id,
                detail.FieldKey,
                detail.Label,
                ElderlyInfoChangeConstants.GetRisk(detail.FieldKey),
                detail.RiskName,
                detail.OldValue ?? string.Empty,
                detail.NewValue ?? string.Empty))
            .ToList();

        return new ElderlyInfoChangeRecordDto(
            record.Id,
            record.ChangeNo,
            record.CampaignId,
            record.TaskId,
            record.ApplicationId,
            record.IdCard,
            record.Name,
            record.ChangeType,
            record.RiskName,
            record.Reason ?? string.Empty,
            record.Status,
            record.StatusName,
            record.SourceType ?? string.Empty,
            record.SourceDisplay,
            record.SourceFile ?? string.Empty,
            record.SourceRow,
            record.EffectiveMonth ?? string.Empty,
            record.AppliedAt,
            record.AppliedBy ?? string.Empty,
            record.ReviewedAt,
            record.ReviewedBy ?? string.Empty,
            record.ReviewOpinion ?? string.Empty,
            record.CreatedAt,
            record.CreatedBy ?? string.Empty,
            record.UpdatedAt,
            details);
    }
}
