using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.Recovery;

namespace NewCosmos.Server.Services;

/// <summary>
/// 后补追缴《追缴资金办理单》打印数据装配（Server 侧单点）。
/// <para>
/// 口径与旧版 <c>RecoveryPrintFieldBuilder</c> 一致：编号取库内已分配值（补打同号，存量记录首次打印时补分配），
/// 填报单位取当前登录用户组织名称（查不到回退用户名），金额统一 N2。
/// 只做数据装配，不引用 Office、不生成 PDF。
/// </para>
/// </summary>
public sealed class RecoveryPrintDataService(
    IRecoveryService recovery,
    IDatabaseService database,
    ILoggerService logger)
{
    /// <summary>追缴文书模板名称（nc_biz_templates.name 精确匹配）。</summary>
    public const string TemplateName = "档案_追缴资金办理单";

    /// <summary>模板分类（nc_biz_templates.categories）与批量任务分类。</summary>
    public const string Category = "追缴";

    /// <summary>
    /// 按追缴记录装配打印字段。
    /// </summary>
    /// <param name="recordId">追缴记录 ID（nc_biz_recovery_records.id）</param>
    /// <param name="organizationId">当前登录用户组织 ID（为空时直接用回退名称）</param>
    /// <param name="fallbackUnitName">组织名称取不到时的回退值（通常为登录用户名）</param>
    /// <param name="ct">取消令牌</param>
    public async Task<Result<RecoveryPrintData>> BuildAsync(
        long recordId,
        int? organizationId,
        string? fallbackUnitName,
        CancellationToken ct = default)
    {
        var recordResult = await recovery.GetRecoveryRecordByIdAsync(recordId, ct);
        if (recordResult.IsFailure)
        {
            return Result.Failure<RecoveryPrintData>(recordResult.ErrorCode!, recordResult.Message!);
        }

        if (recordResult.Value is null)
        {
            return Result.Failure<RecoveryPrintData>(ErrorCodes.RECORD_NOT_FOUND, "追缴记录不存在");
        }

        var record = recordResult.Value;

        // 编号取库内已分配值：补打/多次打印同号；失败不阻断打印，仅留空并记日志（与旧版一致）。
        var codeResult = await recovery.GetOrAssignRecoveryCodeAsync(recordId, ct);
        if (codeResult.IsFailure)
        {
            logger.Warn(
                $"追缴编号获取失败（文书编号将为空）: RecordId={recordId} {codeResult.ErrorCode} {codeResult.Message}");
        }

        var code = codeResult.IsSuccess ? codeResult.Value ?? string.Empty : string.Empty;
        var unit = await ResolveReportUnitAsync(organizationId, fallbackUnitName, ct);
        var period = DateTime.Now.ToString("yyyyMM");

        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [FieldKeys.RECOVERY_UNIT] = unit,
            [FieldKeys.RECOVERY_CODE] = code,
            [FieldKeys.RECOVERY_SEQ] = "1",
            [FieldKeys.RECOVERY_PERSON_NAME] = record.PersonName ?? string.Empty,
            [FieldKeys.RECOVERY_ID_CARD] = record.IdCard ?? string.Empty,
            [FieldKeys.RECOVERY_SOURCE_TYPE] =
                RecoveryConstants.GetSourceTypeDisplayName(record.SourceType ?? string.Empty),
            [FieldKeys.RECOVERY_AMOUNT] = record.RecoveryAmount.ToString("N2"),
            [FieldKeys.RECOVERED_AMOUNT] = record.RecoveredAmount.ToString("N2"),
            [FieldKeys.RECOVERY_REPORT_DATE] = DateTime.Now.ToString("yyyy年MM月dd日"),
            [FieldKeys.RECOVERY_RANGE] = $"{record.StartMonth} 至 {record.EndMonth}"
        };

        return Result.Success(new RecoveryPrintData
        {
            RecordId = record.Id,
            PersonName = record.PersonName ?? string.Empty,
            IdCard = record.IdCard,
            Period = period,
            Fields = fields
        });
    }

    /// <summary>填报单位 = 当前用户组织名称；组织缺失/查询失败回退用户名。</summary>
    private async Task<string> ResolveReportUnitAsync(
        int? organizationId,
        string? fallbackUnitName,
        CancellationToken ct)
    {
        if (organizationId is > 0)
        {
            var result = await database.QuerySingleAsync<string?>(
                "SELECT name FROM nc_sys_organizations WHERE id = $1",
                ct,
                organizationId.Value);
            if (result.IsFailure)
            {
                logger.Warn(
                    $"追缴填报单位查询失败，回退用户名: OrgId={organizationId} {result.ErrorCode} {result.Message}");
            }
            else if (!string.IsNullOrWhiteSpace(result.Value))
            {
                return result.Value.Trim();
            }
        }

        return string.IsNullOrWhiteSpace(fallbackUnitName) ? string.Empty : fallbackUnitName.Trim();
    }
}

/// <summary>追缴《追缴资金办理单》打印数据（模板 240 字段 + 批量输出元数据）。</summary>
public sealed class RecoveryPrintData
{
    public long RecordId { get; init; }

    public string PersonName { get; init; } = string.Empty;

    public string? IdCard { get; init; }

    /// <summary>输出周期（yyyyMM，批量任务 Period 与文件名前缀）。</summary>
    public string Period { get; init; } = string.Empty;

    public Dictionary<string, string> Fields { get; init; } = new(StringComparer.Ordinal);
}
