using System.Text.RegularExpressions;
using NewCosmos.Constants;
using NewCosmos.Models.Options;
using NewCosmos.Models.Results;
using NewCosmos.Server.Configuration;
using NewCosmos.Server.Contracts;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Server.Services.DataCenter;

/// <summary>
/// 数据中心查询与维护服务：数据库状态/统计、表大小、Schema、日志清理、软删除检测与清理、登录端版本。
/// 业务算法严格对齐 PC <c>DatabaseManagementService</c>；差异仅在文件路径来自 <see cref="DataCenterOptions"/>。
/// </summary>
public sealed class DataCenterService(
    IDatabaseService database,
    ISchemaService schema,
    IDatabaseBackupService backup,
    DataCenterOptions options,
    ILoggerService logger)
{
    private static readonly string[] ApplicationDependentTables =
    [
        "nc_biz_alimony_incomes", "nc_biz_application_logs", "nc_biz_archives", "nc_biz_breeding_incomes",
        "nc_biz_business_incomes", "nc_biz_capability_assessments", "nc_biz_caregivers", "nc_biz_change_records",
        "nc_biz_employment_costs", "nc_biz_family_members", "nc_biz_financial_assets", "nc_biz_forest_lands",
        "nc_biz_grace_periods", "nc_biz_guardians", "nc_biz_household_surveys", "nc_biz_kinship_filings",
        "nc_biz_labor_incomes", "nc_biz_land_confirmations", "nc_biz_land_incomes", "nc_biz_land_registrations",
        "nc_biz_machineries", "nc_biz_near_relative_links", "nc_biz_other_incomes", "nc_biz_properties",
        "nc_biz_property_incomes", "nc_biz_rigid_expenditures", "nc_biz_self_care_assessments", "nc_biz_subsidies",
        "nc_biz_subsidy_incomes", "nc_biz_supporters", "nc_biz_transfer_incomes", "nc_biz_vehicles"
    ];

    private static readonly string[] ElderlyDependentTables = ["nc_biz_elderly_payback_segments"];

    private static readonly string[] TempReliefDependentTables =
    [
        "nc_biz_temp_relief_members", "nc_biz_temp_relief_diseases",
        "nc_biz_temp_relief_accidents", "nc_biz_temp_relief_educations"
    ];

    private static readonly string[] SpecialSoftDeleteTables =
    [
        "nc_biz_applications", "nc_biz_family_members", "nc_biz_elderly_applications",
        "nc_biz_temp_relief_applications", "nc_biz_change_records"
    ];

    private static readonly Dictionary<string, int> LogRetention = new(StringComparer.OrdinalIgnoreCase)
    {
        ["app"] = 30,
        ["biz"] = 180,
        ["sec"] = 365,
        ["err"] = 90,
        ["perf"] = 30
    };

    // ───────────────────────── 状态 / 统计 ─────────────────────────

    public async Task<DataCenterOverviewResponse> GetOverviewAsync(CancellationToken ct)
    {
        var tables = await GetTableSizesAsync(ct);

        var yamlTables = schema.GetAllYamlTableNames().ToArray();
        var statusSql = """
            SELECT version() AS version,
                   current_database() AS db_name,
                   (SELECT count(*) FROM pg_stat_activity WHERE datname = current_database()) AS active_connections,
                   (SELECT COALESCE(SUM(pg_total_relation_size(c.oid)), 0)::bigint
                      FROM pg_class c
                      JOIN pg_namespace n ON n.oid = c.relnamespace
                     WHERE n.nspname = 'public' AND c.relkind = 'r'
                       AND c.relname = ANY($1)) AS size_bytes,
                   (SELECT COALESCE(SUM(c.reltuples), 0)::bigint
                      FROM pg_class c
                      JOIN pg_namespace n ON n.oid = c.relnamespace
                     WHERE n.nspname = 'public' AND c.relkind = 'r'
                       AND c.relname = ANY($1)) AS total_records,
                   (SELECT COUNT(*) FROM nc_sys_users WHERE is_active = true) AS active_users
            """;

        var row = await database.QuerySingleAsync<OverviewRow>(statusSql, ct, new object[] { yamlTables });
        if (row.IsFailure || row.Value is null)
        {
            throw new InvalidOperationException(row.Message ?? "获取数据库状态失败");
        }

        var value = row.Value;
        var dto = new DatabaseStatusDto(
            Connected: true,
            DatabaseName: value.DbName,
            Version: value.Version,
            ActiveConnections: (int)value.ActiveConnections,
            SizeBytes: value.SizeBytes,
            SizeText: FormatSize(value.SizeBytes),
            TotalTables: yamlTables.Length,
            TotalRecords: value.TotalRecords,
            ActiveUsers: (int)value.ActiveUsers,
            CheckedAt: DateTime.Now);

        return new DataCenterOverviewResponse(dto, tables);
    }

    public async Task<List<TableSizeDto>> GetTableSizesAsync(CancellationToken ct)
    {
        var yamlTables = schema.GetAllYamlTableNames();
        if (yamlTables.Count == 0)
        {
            return [];
        }

        const string sql = """
            SELECT c.relname AS table_name,
                   pg_total_relation_size(c.oid) AS size_bytes,
                   c.reltuples::bigint AS row_count
              FROM pg_class c
              JOIN pg_namespace n ON n.oid = c.relnamespace
             WHERE n.nspname = 'public' AND c.relkind = 'r'
               AND c.relname = ANY($1)
             ORDER BY pg_total_relation_size(c.oid) DESC
            """;

        var result = await database.QueryAsync<TableSizeRow>(sql, ct, new object[] { yamlTables.ToArray() });
        if (result.IsFailure)
        {
            throw new InvalidOperationException(result.Message ?? "获取表大小失败");
        }

        return (result.Value ?? [])
            .Select(item => new TableSizeDto(
                item.TableName,
                schema.GetTableSchema(item.TableName)?.Comment ?? string.Empty,
                item.SizeBytes,
                FormatSize(item.SizeBytes),
                item.RowCount))
            .ToList();
    }

    // ───────────────────────── Schema ─────────────────────────

    public async Task<SchemaStatusDto> GetSchemaStatusAsync(CancellationToken ct)
    {
        var result = await schema.GetSchemaStatusAsync(ct);
        if (result.IsFailure || result.Value is null)
        {
            throw new InvalidOperationException(result.Message ?? "获取 Schema 状态失败");
        }

        var status = result.Value;
        return new SchemaStatusDto(
            status.IsInitialized,
            status.StatusText,
            status.StatusSummary,
            status.StatusIcon,
            status.StatusColor,
            status.CurrentVersion,
            status.InitializedAt,
            status.InitializedBy,
            status.TotalRequiredTables,
            status.TotalExistingTables,
            status.MissingTables,
            status.MissingColumns,
            status.TotalMissingColumns,
            status.HasMissingTables,
            status.HasMissingColumns);
    }

    public async Task<int> InitializeSchemaAsync(string initializedBy, CancellationToken ct)
    {
        var before = await schema.GetSchemaStatusAsync(ct);
        var missing = before.IsSuccess && before.Value is not null ? before.Value.TotalMissingTables : 0;

        var result = await schema.InitializeAllTablesAsync(initializedBy, ct);
        if (result.IsFailure)
        {
            throw new InvalidOperationException(result.Message ?? "初始化 Schema 失败");
        }

        logger.LogSecurity("数据中心-初始化缺失表", ("MissingBefore", missing), ("Operator", initializedBy));
        return missing;
    }

    public async Task<SchemaFixResponse> FixSchemaAsync(CancellationToken ct)
    {
        var validation = await schema.ValidateAllSchemasAsync(ct);
        if (validation.IsFailure || validation.Value is null)
        {
            throw new InvalidOperationException(validation.Message ?? "校验 Schema 失败");
        }

        var differences = validation.Value.Differences
            .Where(d => d.Severity == NewCosmos.Models.Results.SeverityLevel.Error)
            .ToList();

        if (differences.Count == 0)
        {
            return new SchemaFixResponse(0, 0, 0, "未发现需要修复的结构差异", []);
        }

        var result = await schema.FixSchemaDifferencesAsync(differences, ct);
        if (result.IsFailure || result.Value is null)
        {
            throw new InvalidOperationException(result.Message ?? "修复 Schema 失败");
        }

        var fix = result.Value;
        logger.LogSecurity("数据中心-修复缺失列",
            ("Fixed", fix.TotalFixed), ("Failed", fix.FailedCount), ("Skipped", fix.SkippedCount));
        return new SchemaFixResponse(
            fix.TotalFixed, fix.FailedCount, fix.SkippedCount, fix.Summary, fix.FailedItems);
    }

    // ───────────────────────── 备份 / 日志 ─────────────────────────

    public Task<List<BackupFileDto>> GetBackupFilesAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.Run(() =>
        {
            if (!Directory.Exists(options.BackupRoot))
            {
                return new List<BackupFileDto>();
            }

            return Directory.GetFiles(options.BackupRoot, "*.sql")
                .Select(path => new FileInfo(path))
                .Where(file => file.Exists)
                .OrderByDescending(file => file.LastWriteTime)
                .Select(file => new BackupFileDto(
                    file.Name,
                    file.Length,
                    FormatSize(file.Length),
                    file.LastWriteTime))
                .ToList();
        }, ct);
    }

    public Task<int> CleanupOldLogsAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!Directory.Exists(options.LogRoot))
        {
            return Task.FromResult(0);
        }

        var today = DateTime.Today;
        var deleted = 0;
        foreach (var file in Directory.GetFiles(options.LogRoot, "*.log"))
        {
            ct.ThrowIfCancellationRequested();

            var fileName = Path.GetFileName(file);
            var prefix = fileName.Split('_')[0];
            if (!LogRetention.TryGetValue(prefix, out var retentionDays))
            {
                continue;
            }

            var info = new FileInfo(file);
            // 不删当前正在写入的文件（今天仍有修改）。
            if (info.LastWriteTime >= today)
            {
                continue;
            }

            if (info.LastWriteTime >= DateTime.Now.AddDays(-retentionDays))
            {
                continue;
            }

            try
            {
                info.Delete();
                deleted++;
            }
            catch (Exception ex)
            {
                logger.Warn($"删除日志失败: {fileName} {ex.Message}");
            }
        }

        logger.LogBusiness("数据中心-清理过期日志", ("DeletedCount", deleted));
        return Task.FromResult(deleted);
    }

    // ───────────────────────── 软删除 ─────────────────────────

    public async Task<SoftDeleteCensusResponse> DetectSoftDeleteAsync(CancellationToken ct)
    {
        var tablesResult = await GetSoftDeleteTablesAsync(ct);
        var stats = new List<SoftDeleteTableDto>();

        foreach (var table in tablesResult)
        {
            ct.ThrowIfCancellationRequested();
            var count = await database.ExecuteScalarAsync<int?>(
                $"SELECT count(*) FROM {table} WHERE deleted_at IS NOT NULL", ct);
            if (count.IsFailure)
            {
                throw new InvalidOperationException(count.Message ?? $"统计 {table} 软删除失败");
            }

            var softDeleted = count.Value ?? 0;
            if (softDeleted == 0)
            {
                continue;
            }

            var toDelete = await CountToDeleteAsync(table, softDeleted, ct);
            stats.Add(new SoftDeleteTableDto(table, softDeleted, softDeleted - toDelete, toDelete));
        }

        var chain = await database.QueryAsync<ChainRow>("""
            SELECT p.id AS old_application_id,
                   p.applicant_name AS old_name,
                   p.status AS old_status,
                   c.id AS new_application_id,
                   c.chain_type AS chain_type
              FROM nc_biz_applications p
              JOIN nc_biz_applications c ON c.original_application_id = p.id AND c.deleted_at IS NULL
             WHERE p.deleted_at IS NULL AND p.status <> $1
               AND c.chain_type IN ('CategoryRebuild', 'HeadChange', 'HouseholdDeath')
             ORDER BY p.id
            """, ct, ApplicationStatusCodes.STOPPED);
        if (chain.IsFailure)
        {
            throw new InvalidOperationException(chain.Message ?? "检查档案链失败");
        }

        var chainIssues = (chain.Value ?? [])
            .Select(item => new ChainIssueDto(
                item.OldApplicationId, item.OldName, item.OldStatus,
                item.NewApplicationId, item.ChainType))
            .ToList();

        return new SoftDeleteCensusResponse(
            stats,
            stats.Sum(item => item.SoftDeleted),
            stats.Sum(item => item.Kept),
            stats.Sum(item => item.ToDelete),
            chainIssues,
            DateTime.Now);
    }

    public async Task<SoftDeleteCleanupResponse> CleanupSoftDeletedAsync(CancellationToken ct)
    {
        var backupResult = await backup.BackupDatabaseAsync(options.BackupRoot, ct);
        if (backupResult.IsFailure)
        {
            throw new InvalidOperationException(
                $"清理前备份失败，已中止：{backupResult.Message}");
        }

        var backupFile = backupResult.Value;
        var deletedRows = 0;
        var restoredRows = 0;
        var deletedArchives = 0;

        await using var tx = await database.BeginTransactionScopeAsync(ct);

        var restoreMembers = await database.ExecuteNonQueryAsync("""
            UPDATE nc_biz_family_members fm SET deleted_at = NULL
              FROM nc_biz_applications a
             WHERE fm.application_id = a.id AND fm.deleted_at IS NOT NULL
               AND a.deleted_at IS NULL AND a.status = $1
            """, ct, ApplicationStatusCodes.STOPPED);
        EnsureWriteSucceeded(restoreMembers, "恢复停保档案成员");
        restoredRows += restoreMembers.Value;

        var deleteOrphanMembers = await database.ExecuteNonQueryAsync("""
            DELETE FROM nc_biz_family_members fm USING nc_biz_applications a
             WHERE fm.application_id = a.id AND fm.deleted_at IS NOT NULL
               AND a.deleted_at IS NULL AND a.status <> $1
            """, ct, ApplicationStatusCodes.STOPPED);
        EnsureWriteSucceeded(deleteOrphanMembers, "删除冗余软删成员");
        deletedRows += deleteOrphanMembers.Value;

        var appIdsResult = await database.QueryAsync<long>("""
            SELECT a.id FROM nc_biz_applications a
             WHERE a.deleted_at IS NOT NULL AND a.status <> $1
               AND NOT EXISTS (SELECT 1 FROM nc_biz_change_records cr
                                WHERE cr.application_id = a.id
                                   OR cr.original_application_id = a.id
                                   OR cr.new_application_id = a.id)
               AND NOT EXISTS (SELECT 1 FROM nc_biz_applications c
                                WHERE c.original_application_id = a.id AND c.deleted_at IS NULL)
            """, ct, ApplicationStatusCodes.STOPPED);
        EnsureQuerySucceeded(appIdsResult, "查询待删救助档案");
        var appIds = (appIdsResult.Value ?? []).ToArray();
        if (appIds.Length > 0)
        {
            foreach (var table in ApplicationDependentTables)
            {
                var deleted = await database.ExecuteNonQueryAsync(
                    $"DELETE FROM {table} WHERE application_id = ANY($1)", ct, appIds);
                EnsureWriteSucceeded(deleted, $"删除 {table} 明细");
                deletedRows += deleted.Value;
            }

            var deletedApps = await database.ExecuteNonQueryAsync(
                "DELETE FROM nc_biz_applications WHERE id = ANY($1)", ct, appIds);
            EnsureWriteSucceeded(deletedApps, "删除救助档案");
            deletedRows += deletedApps.Value;
            deletedArchives += deletedApps.Value;
        }

        var elderlyIds = await GetIdsToDeleteAsync("nc_biz_elderly_applications", ct);
        if (elderlyIds.Length > 0)
        {
            foreach (var table in ElderlyDependentTables)
            {
                var deleted = await database.ExecuteNonQueryAsync(
                    $"DELETE FROM {table} WHERE application_id = ANY($1)", ct, elderlyIds);
                EnsureWriteSucceeded(deleted, $"删除高龄 {table} 明细");
                deletedRows += deleted.Value;
            }

            var deletedElderly = await database.ExecuteNonQueryAsync(
                "DELETE FROM nc_biz_elderly_applications WHERE id = ANY($1)", ct, elderlyIds);
            EnsureWriteSucceeded(deletedElderly, "删除高龄档案");
            deletedRows += deletedElderly.Value;
            deletedArchives += deletedElderly.Value;
        }

        var tempIds = await GetIdsToDeleteAsync("nc_biz_temp_relief_applications", ct);
        if (tempIds.Length > 0)
        {
            foreach (var table in TempReliefDependentTables)
            {
                var deleted = await database.ExecuteNonQueryAsync(
                    $"DELETE FROM {table} WHERE application_id = ANY($1)", ct, tempIds);
                EnsureWriteSucceeded(deleted, $"删除临时救助 {table} 明细");
                deletedRows += deleted.Value;
            }

            var deletedTemp = await database.ExecuteNonQueryAsync(
                "DELETE FROM nc_biz_temp_relief_applications WHERE id = ANY($1)", ct, tempIds);
            EnsureWriteSucceeded(deletedTemp, "删除临时救助档案");
            deletedRows += deletedTemp.Value;
            deletedArchives += deletedTemp.Value;
        }

        var deleteDetails = await database.ExecuteNonQueryAsync(
            "DELETE FROM nc_biz_change_details WHERE change_id IN (SELECT id FROM nc_biz_change_records WHERE deleted_at IS NOT NULL)", ct);
        EnsureWriteSucceeded(deleteDetails, "删除变更明细");

        var deleteSnapshots = await database.ExecuteNonQueryAsync(
            "DELETE FROM nc_biz_change_snapshots WHERE change_id IN (SELECT id FROM nc_biz_change_records WHERE deleted_at IS NOT NULL)", ct);
        EnsureWriteSucceeded(deleteSnapshots, "删除变更快照");

        var deleteChanges = await database.ExecuteNonQueryAsync(
            "DELETE FROM nc_biz_change_records WHERE deleted_at IS NOT NULL", ct);
        EnsureWriteSucceeded(deleteChanges, "删除变更记录");
        deletedRows += deleteChanges.Value;

        var allSoftTables = await GetSoftDeleteTablesAsync(ct);
        foreach (var table in allSoftTables)
        {
            if (SpecialSoftDeleteTables.Contains(table))
            {
                continue;
            }

            var deleted = await database.ExecuteNonQueryAsync(
                $"DELETE FROM {table} WHERE deleted_at IS NOT NULL", ct);
            EnsureWriteSucceeded(deleted, $"清理 {table} 软删行");
            deletedRows += deleted.Value;
        }

        await tx.CommitAsync(ct);

        logger.LogBusiness("数据中心-软删除清理完成",
            ("DeletedRows", deletedRows), ("RestoredRows", restoredRows),
            ("DeletedArchives", deletedArchives), ("Backup", Path.GetFileName(backupFile)));

        return new SoftDeleteCleanupResponse(
            backupFile, deletedRows, restoredRows, deletedArchives,
            $"清理完成：删除 {deletedRows} 行，恢复 {restoredRows} 行，删除档案 {deletedArchives} 个");
    }

    // ───────────────────────── 登录端版本 ─────────────────────────

    public async Task<ClientVersionResponse> GetClientsAsync(CancellationToken ct)
    {
        var result = await database.QueryAsync<ClientRow>("""
            SELECT s.device_name, s.device_kind, s.app_version,
                   u.username, u.full_name,
                   s.login_at, s.last_seen_at,
                   (s.last_seen_at >= now() - interval '5 minutes') AS online
              FROM nc_sys_user_sessions s
              JOIN nc_sys_users u ON u.id = s.user_id
             WHERE s.status = 'active' AND s.expires_at > now()
             ORDER BY s.last_seen_at DESC
             LIMIT 50
            """, ct);

        if (result.IsFailure)
        {
            throw new InvalidOperationException(result.Message ?? "读取客户端版本失败");
        }

        var items = (result.Value ?? [])
            .Select(item => new ClientVersionDto(
                item.DeviceName ?? "—",
                item.DeviceKind ?? "web",
                item.AppVersion ?? "—",
                item.Username ?? string.Empty,
                item.FullName ?? string.Empty,
                item.LoginAt,
                item.LastSeenAt,
                item.Online))
            .ToList();

        var online = items.Count(item => item.Online);
        return new ClientVersionResponse(items, online, $"在线 {online} 台 / 共 {items.Count} 台");
    }

    // ───────────────────────── 内部 ─────────────────────────

    private async Task<long[]> GetIdsToDeleteAsync(string table, CancellationToken ct)
    {
        var result = await database.QueryAsync<long>(
            $"SELECT id FROM {table} WHERE deleted_at IS NOT NULL AND status <> $1",
            ct, ApplicationStatusCodes.STOPPED);
        EnsureQuerySucceeded(result, $"查询 {table} 待删档案");
        return (result.Value ?? []).ToArray();
    }

    private async Task<List<string>> GetSoftDeleteTablesAsync(CancellationToken ct)
    {
        var result = await database.QueryAsync<string>(
            "SELECT table_name FROM information_schema.columns WHERE column_name = 'deleted_at' AND table_schema = 'public' ORDER BY table_name",
            ct);
        if (result.IsFailure)
        {
            throw new InvalidOperationException(result.Message ?? "读取软删除表清单失败");
        }

        return (result.Value ?? [])
            .Where(IsSafeTableName)
            .Where(name => !name.Contains("_bak_", StringComparison.Ordinal))
            .ToList();
    }

    private async Task<int> CountToDeleteAsync(string table, int softDeleted, CancellationToken ct)
    {
        Result<int?> result;
        switch (table)
        {
            case "nc_biz_applications":
                result = await database.ExecuteScalarAsync<int?>("""
                    SELECT count(*) FROM nc_biz_applications a
                     WHERE a.deleted_at IS NOT NULL AND a.status <> $1
                       AND NOT EXISTS (SELECT 1 FROM nc_biz_change_records cr
                                        WHERE cr.application_id = a.id
                                           OR cr.original_application_id = a.id
                                           OR cr.new_application_id = a.id)
                       AND NOT EXISTS (SELECT 1 FROM nc_biz_applications c
                                        WHERE c.original_application_id = a.id AND c.deleted_at IS NULL)
                    """, ct, ApplicationStatusCodes.STOPPED);
                break;

            case "nc_biz_elderly_applications":
            case "nc_biz_temp_relief_applications":
                result = await database.ExecuteScalarAsync<int?>(
                    $"SELECT count(*) FROM {table} WHERE deleted_at IS NOT NULL AND status <> $1",
                    ct, ApplicationStatusCodes.STOPPED);
                break;

            case "nc_biz_family_members":
                result = await database.ExecuteScalarAsync<int?>("""
                    SELECT count(*) FROM nc_biz_family_members fm
                      JOIN nc_biz_applications a ON a.id = fm.application_id
                     WHERE fm.deleted_at IS NOT NULL
                       AND NOT (a.deleted_at IS NULL AND a.status = $1)
                    """, ct, ApplicationStatusCodes.STOPPED);
                break;

            default:
                return softDeleted;
        }

        if (result.IsFailure)
        {
            throw new InvalidOperationException(result.Message ?? $"统计 {table} 待删行失败");
        }

        return result.Value ?? 0;
    }

    private static bool IsSafeTableName(string name) =>
        !string.IsNullOrEmpty(name) && name.Length <= 64 &&
        Regex.IsMatch(name, "^nc_[a-z0-9_]+$");

    private static void EnsureWriteSucceeded(Result result, string operation)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"{operation}失败，已回滚本次清理：{result.Message}");
        }
    }

    private static void EnsureQuerySucceeded<T>(Result<T> result, string operation)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"{operation}失败，已中止本次清理：{result.Message}");
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024L * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F1} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
    }

    private sealed class OverviewRow
    {
        public string Version { get; set; } = string.Empty;
        public string DbName { get; set; } = string.Empty;
        public long ActiveConnections { get; set; }
        public long SizeBytes { get; set; }
        public long TotalRecords { get; set; }
        public long ActiveUsers { get; set; }
    }

    private sealed class TableSizeRow
    {
        public string TableName { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public long RowCount { get; set; }
    }

    private sealed class ChainRow
    {
        public long OldApplicationId { get; set; }
        public string OldName { get; set; } = string.Empty;
        public string OldStatus { get; set; } = string.Empty;
        public long NewApplicationId { get; set; }
        public string ChainType { get; set; } = string.Empty;
    }

    private sealed class ClientRow
    {
        public string? DeviceName { get; set; }
        public string? DeviceKind { get; set; }
        public string? AppVersion { get; set; }
        public string? Username { get; set; }
        public string? FullName { get; set; }
        public DateTime LoginAt { get; set; }
        public DateTime LastSeenAt { get; set; }
        public bool Online { get; set; }
    }
}
