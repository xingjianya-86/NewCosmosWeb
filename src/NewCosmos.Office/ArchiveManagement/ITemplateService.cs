using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.ArchiveManagement;

/// <summary>
/// Office 侧只读模板访问契约（从 legacy ITemplateService 裁剪）。
/// Agent 当前从服务端下发/本地文件取模板，故这里只保留渲染链路真正用到的读取方法；
/// 模板的增删改仍归服务端 NewCosmos.Server。
/// </summary>
public interface ITemplateService
{
    /// <summary>按 Id 取模板元数据。Success(null)=无此行；Failure=查询失败（禁止把故障伪装成"模板不存在"）。</summary>
    Task<Result<Template?>> GetByIdAsync(long id, CancellationToken ct = default);

    /// <summary>取模板文件字节。Success(空)=无此行或文件列为空；Failure=查询失败（禁止洗成空数组）。</summary>
    Task<Result<byte[]>> GetFileDataAsync(long id, CancellationToken ct = default);

    /// <summary>按分类取模板元数据（推送打印解析模板用）。</summary>
    Task<List<Template>> GetByCategoriesAsync(string[] categories, CancellationToken ct = default);
}
