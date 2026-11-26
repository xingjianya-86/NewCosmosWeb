namespace NewCosmos.Server.Contracts;

#region 数据中心总览

/// <summary>数据库连接与容量状态。</summary>
public sealed record DatabaseStatusDto(
    bool Connected,
    string DatabaseName,
    string Version,
    int ActiveConnections,
    long SizeBytes,
    string SizeText,
    int TotalTables,
    long TotalRecords,
    int ActiveUsers,
    DateTime CheckedAt);

/// <summary>表大小 / 行数统计。</summary>
public sealed record TableSizeDto(
    string TableName,
    string TableComment,
    long SizeBytes,
    string SizeText,
    long RowCount);

public sealed record DataCenterOverviewResponse(
    DatabaseStatusDto Database,
    IReadOnlyList<TableSizeDto> Tables);

/// <summary>Schema 状态（结构校验结果）。</summary>
public sealed record SchemaStatusDto(
    bool IsInitialized,
    string StatusText,
    string StatusSummary,
    string StatusIcon,
    string StatusColor,
    string CurrentVersion,
    string InitializedAt,
    string InitializedBy,
    int TotalRequiredTables,
    int TotalExistingTables,
    IReadOnlyList<string> MissingTables,
    IReadOnlyDictionary<string, List<string>> MissingColumns,
    int TotalMissingColumns,
    bool HasMissingTables,
    bool HasMissingColumns);

public sealed record SchemaFixResponse(
    int TotalFixed,
    int FailedCount,
    int SkippedCount,
    string Summary,
    IReadOnlyList<string> FailedItems);

/// <summary>备份文件。</summary>
public sealed record BackupFileDto(
    string FileName,
    long SizeBytes,
    string SizeText,
    DateTime CreatedAt);

public sealed record CleanupLogsResponse(
    int DeletedCount,
    string Message);

/// <summary>单表软删除统计。</summary>
public sealed record SoftDeleteTableDto(
    string TableName,
    int SoftDeleted,
    int Kept,
    int ToDelete);

/// <summary>档案链一致性异常：应停未停的旧档案。</summary>
public sealed record ChainIssueDto(
    long OldApplicationId,
    string OldName,
    string OldStatus,
    long NewApplicationId,
    string ChainType);

public sealed record SoftDeleteCensusResponse(
    IReadOnlyList<SoftDeleteTableDto> Tables,
    int TotalSoftDeleted,
    int TotalKept,
    int TotalToDelete,
    IReadOnlyList<ChainIssueDto> ChainIssues,
    DateTime DetectedAt);

public sealed record SoftDeleteCleanupResponse(
    string BackupFile,
    int DeletedRows,
    int RestoredRows,
    int DeletedArchives,
    string Message);

/// <summary>登录端版本（来自 nc_sys_user_sessions）。</summary>
public sealed record ClientVersionDto(
    string DeviceName,
    string DeviceKind,
    string AppVersion,
    string UserName,
    string FullName,
    DateTime LoginAt,
    DateTime LastSeenAt,
    bool Online);

public sealed record ClientVersionResponse(
    IReadOnlyList<ClientVersionDto> Items,
    int OnlineCount,
    string Summary);

#endregion

#region 后台任务

public sealed record DataCenterJobDto(
    string JobId,
    string Kind,
    string Title,
    string State,
    int Percent,
    string Message,
    DateTime StartedAt,
    DateTime? FinishedAt,
    string? Error,
    IReadOnlyList<string> Logs);

public sealed record StartJobResponse(string JobId, string Message);

#endregion
