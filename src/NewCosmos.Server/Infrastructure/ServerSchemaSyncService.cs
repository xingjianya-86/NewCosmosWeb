using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Results;
using NewCosmos.Models.Schema;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Server.Infrastructure;

/// <summary>
/// 服务端表结构同步：以 Resources/Schema 下 YAML 为唯一权威来源，但**永不 DROP**。
/// <list type="bullet">
/// <item>表不存在：按 YAML 定义 CREATE TABLE + 索引/注释；</item>
/// <item>表已存在：无论 allowDrop 真假，一律增量修复（只补列/索引/约束/注释，不删列不删表）；</item>
/// <item>allowDrop=true 且表已存在：忽略 DROP 重建请求（记安全日志后走增量同步），生产环境重建一律走迁移脚本。</item>
/// </list>
/// 与 Core 的 <c>SchemaSyncService</c>（DROP + CREATE）刻意区分：服务端不允许误删生产数据。
/// </summary>
public sealed class ServerSchemaSyncService(ISchemaService schemaService, ILoggerService logger)
    : ISchemaSyncService
{
    private const int TotalSteps = 3;

    public async Task<Result> SyncTableSchemaAsync(
        string tableName,
        bool allowDrop = false,
        IProgress<ProgressContext>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tableName))
        {
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "表名不能为空");
        }

        tableName = tableName.Trim();

        // 动态表名必须白名单：后续 DDL 直接拼接表名。
        if (!TableNameValidator.IsValid(tableName))
        {
            logger.LogSecurity("拒绝同步非法表名", ("Table", tableName), ("AllowDrop", allowDrop));
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, $"无效的表名: {tableName}");
        }

        // 服务端永不 DROP：allowDrop 仅是调用方的"整表重建"意图，这里一律降级为增量同步。
        try
        {
            var table = schemaService.GetTableSchema(tableName);
            if (table is null)
            {
                logger.Error($"YAML Schema 中未找到表定义: {tableName}");
                return Result.Failure(
                    ErrorCodes.FILE_NOT_FOUND,
                    $"YAML Schema 中未找到表 {tableName} 的定义，无法同步表结构");
            }

            progress?.Report(new ProgressContext
            {
                CurrentStep = 1,
                TotalSteps = TotalSteps,
                CurrentStepName = $"检查表: {tableName}"
            });

            var exists = await schemaService.TableExistsAsync(tableName, ct);
            if (exists.IsFailure)
            {
                logger.Error($"检查表是否存在失败: {tableName} {exists.ErrorCode} {exists.Message}");
                return Result.Failure(exists.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, exists.Message);
            }

            if (!exists.Value)
            {
                return await CreateMissingTableAsync(table, progress, ct);
            }

            if (allowDrop)
            {
                // 调用方（Core 字典/标准/区划 SyncSchemaAsync）传了 allowDrop:true，
                // 但服务端拒绝 DROP：留痕后按增量修复执行，保证"同步表结构"按钮可用且不丢数据。
                logger.LogSecurity("服务端忽略 DROP 重建请求，改为增量同步", ("Table", tableName));
            }

            return await FixExistingTableAsync(tableName, progress, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"同步表结构异常: {tableName}");
            return Result.FromException(ex);
        }
    }

    /// <summary>表不存在：按 YAML 定义直接创建（CREATE TABLE IF NOT EXISTS + 索引 + 注释）。</summary>
    private async Task<Result> CreateMissingTableAsync(
        TableSchema table,
        IProgress<ProgressContext>? progress,
        CancellationToken ct)
    {
        progress?.Report(new ProgressContext
        {
            CurrentStep = 2,
            TotalSteps = TotalSteps,
            CurrentStepName = $"创建表: {table.Name}"
        });

        var sql = schemaService.GenerateCreateTableSql(table, includeForeignKeys: true);
        var result = await schemaService.ExecuteCreateTableAsync(sql, ct);
        if (result.IsFailure)
        {
            logger.Error($"按 YAML 创建表失败: {table.Name} {result.ErrorCode} {result.Message}");
            return result;
        }

        progress?.Report(new ProgressContext
        {
            CurrentStep = 3,
            TotalSteps = TotalSteps,
            CurrentStepName = $"表已创建: {table.Name}"
        });

        logger.LogSecurity("按 YAML 创建表", ("Table", table.Name));
        logger.LogBusiness("同步表结构-新建表",
            ("Table", table.Name),
            ("Columns", table.Columns.Count),
            ("Indexes", table.Indexes.Count));
        return Result.Success();
    }

    /// <summary>
    /// 表已存在：仅做增量修复。FixSchemaDifferencesAsync 自身会跳过 ExtraTable/ExtraColumn，
    /// 这里再按表名/差异类型过滤一次，确保不会删除已有列或表。
    /// </summary>
    private async Task<Result> FixExistingTableAsync(
        string tableName,
        IProgress<ProgressContext>? progress,
        CancellationToken ct)
    {
        progress?.Report(new ProgressContext
        {
            CurrentStep = 2,
            TotalSteps = TotalSteps,
            CurrentStepName = $"校验结构差异: {tableName}"
        });

        var validation = await schemaService.ValidateAllSchemasAsync(ct);
        if (validation.IsFailure || validation.Value is null)
        {
            logger.Error($"校验表结构失败: {tableName} {validation.ErrorCode} {validation.Message}");
            return Result.Failure(
                validation.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                validation.Message ?? $"校验表结构失败: {tableName}");
        }

        var differences = validation.Value.Differences
            .Where(d => string.Equals(d.TableName, tableName, StringComparison.OrdinalIgnoreCase))
            // 只修复错误/警告；Info（如多余列提示）不处理。
            .Where(d => d.Severity is SeverityLevel.Error or SeverityLevel.Warning)
            // 双保险：绝不删除多余表/列（FixSchemaDifferencesAsync 也会跳过）。
            .Where(d => d.Type is not (DifferenceType.ExtraTable or DifferenceType.ExtraColumn))
            .ToList();

        if (differences.Count == 0)
        {
            progress?.Report(new ProgressContext
            {
                CurrentStep = 3,
                TotalSteps = TotalSteps,
                CurrentStepName = $"结构已一致: {tableName}"
            });

            logger.Info($"表结构无需修复: {tableName}");
            return Result.Success();
        }

        progress?.Report(new ProgressContext
        {
            CurrentStep = 3,
            TotalSteps = TotalSteps,
            CurrentStepName = $"增量修复: {tableName}（{differences.Count} 项）"
        });

        var fixResult = await schemaService.FixSchemaDifferencesAsync(differences, ct);
        if (fixResult.IsFailure || fixResult.Value is null)
        {
            logger.Error($"修复表结构失败: {tableName} {fixResult.ErrorCode} {fixResult.Message}");
            return Result.Failure(
                fixResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                fixResult.Message ?? $"修复表结构失败: {tableName}");
        }

        var fix = fixResult.Value;
        logger.LogSecurity("增量修复表结构",
            ("Table", tableName),
            ("Differences", differences.Count),
            ("Fixed", fix.TotalFixed),
            ("Failed", fix.FailedCount),
            ("Skipped", fix.SkippedCount));
        logger.LogBusiness("同步表结构-增量修复",
            ("Table", tableName),
            ("Fixed", fix.TotalFixed),
            ("Failed", fix.FailedCount));

        if (fix.FailedCount > 0)
        {
            return Result.Failure(
                "SCHEMA_SYNC_PARTIAL_FAILURE",
                $"表 {tableName} 结构修复失败 {fix.FailedCount} 项: {string.Join("；", fix.FailedItems)}");
        }

        return Result.Success();
    }
}
