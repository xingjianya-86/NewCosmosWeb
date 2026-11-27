using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ElderlyBenefits;

namespace NewCosmos.Server.Services;

/// <summary>
/// 高龄年度认证·信息变更写服务：创建、审批通过、驳回。
/// 业务校验/事务/白名单全部在 Core；Server 只做数据范围与 DTO 映射。
/// </summary>
public sealed class ElderlyInfoChangeWriteService(
    IElderlyInfoChangeService infoChangeService,
    ElderlyInfoChangeQueryService queryService,
    ILoggerService logger)
{
    public async Task<Result<ElderlyInfoChangeRecordDto>> CreateAsync(
        int userId,
        long taskId,
        ElderlyInfoChangeCreateRequest request,
        string operatorName,
        CancellationToken ct = default)
    {
        if (request is null || request.Details.Count == 0)
        {
            return Result.Failure<ElderlyInfoChangeRecordDto>(
                ErrorCodes.VALIDATION_FAILED, "至少填写一个变更字段");
        }

        var scoped = await queryService.GetScopedRecordTaskAsync(userId, taskId, ct);
        if (scoped.IsFailure)
        {
            return Result.Failure<ElderlyInfoChangeRecordDto>(
                scoped.ErrorCode!, scoped.Message!);
        }

        var details = request.Details
            .Select(detail => new ElderlyInfoChangeDetailInput
            {
                FieldKey = detail.FieldKey,
                OldValue = detail.OldValue,
                NewValue = detail.NewValue
            })
            .ToList();

        var createResult = await infoChangeService.CreateAsync(
            taskId,
            request.Reason,
            request.SourceType,
            request.SourceFile,
            request.SourceRow,
            request.EffectiveMonth,
            details,
            operatorName,
            ct);
        if (createResult.IsFailure)
        {
            return Result.Failure<ElderlyInfoChangeRecordDto>(
                createResult.ErrorCode!, createResult.Message!);
        }

        var recordResult = await queryService.GetScopedRecordAsync(userId, createResult.Value, ct);
        if (recordResult.IsFailure)
        {
            // 创建已成功；回读失败只返回最小信息，避免前端误以为未创建。
            logger.Warn($"信息变更创建成功但回读失败: ChangeId={createResult.Value} {recordResult.ErrorCode}");
            return Result.Success(new ElderlyInfoChangeRecordDto(
                createResult.Value,
                string.Empty,
                null,
                taskId,
                null,
                string.Empty,
                string.Empty,
                ElderlyInfoChangeConstants.RiskLow,
                string.Empty,
                request.Reason ?? string.Empty,
                ElderlyInfoChangeConstants.StatusPending,
                ElderlyInfoChangeConstants.GetStatusName(ElderlyInfoChangeConstants.StatusPending),
                request.SourceType ?? ElderlyInfoChangeConstants.SourceManual,
                "手工登记",
                request.SourceFile ?? string.Empty,
                request.SourceRow,
                request.EffectiveMonth ?? string.Empty,
                null,
                string.Empty,
                null,
                string.Empty,
                string.Empty,
                null,
                operatorName,
                null,
                Array.Empty<ElderlyInfoChangeDetailDto>()));
        }

        return Result.Success(MapRecord(recordResult.Value));
    }

    public async Task<Result> ApproveAsync(
        int userId,
        long changeId,
        ElderlyInfoChangeApproveRequest request,
        string operatorName,
        CancellationToken ct = default)
    {
        var scoped = await queryService.GetScopedRecordAsync(userId, changeId, ct);
        if (scoped.IsFailure)
        {
            return Result.Failure(scoped.ErrorCode!, scoped.Message!);
        }

        return await infoChangeService.ApproveAsync(
            changeId,
            request?.ReviewOpinion,
            request?.EffectiveMonth,
            operatorName,
            ct);
    }

    public async Task<Result> RejectAsync(
        int userId,
        long changeId,
        ElderlyInfoChangeRejectRequest request,
        string operatorName,
        CancellationToken ct = default)
    {
        var scoped = await queryService.GetScopedRecordAsync(userId, changeId, ct);
        if (scoped.IsFailure)
        {
            return Result.Failure(scoped.ErrorCode!, scoped.Message!);
        }

        if (request is null || string.IsNullOrWhiteSpace(request.ReviewOpinion))
        {
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "驳回时请填写审批意见");
        }

        return await infoChangeService.RejectAsync(
            changeId,
            request.ReviewOpinion,
            operatorName,
            ct);
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
