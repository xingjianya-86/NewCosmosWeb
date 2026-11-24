using NewCosmos.Models.Results;
using NewCosmos.Services.Core;

namespace NewCosmos.Server.Infrastructure;

/// <summary>
/// 服务端不提供按 YAML 重建表结构的能力。
/// 迁移由 Migrations/*.sql 与 NC_AUTO_MIGRATE 负责；这里返回成功以满足 Core 的接口依赖。
/// </summary>
public sealed class NoopSchemaSyncService : ISchemaSyncService
{
    public Task<Result> SyncTableSchemaAsync(
        string tableName,
        bool allowDrop = false,
        IProgress<ProgressContext>? progress = null,
        CancellationToken ct = default)
    {
        return Task.FromResult(Result.Success());
    }
}
