using NewCosmos.Constants;

namespace NewCosmos.Services.Domain.SocialAssistance;

/// <summary>异地协助申请人准入判定结果。</summary>
public enum RemoteAssistanceGate
{
    /// <summary>本地库无档案：允许，无标记。</summary>
    Allowed,

    /// <summary>本地档案已停保：允许，标记为「户籍迁出」。</summary>
    AllowedHukouMovedOut,

    /// <summary>本地已在享（Approved/Completed）：拒绝。</summary>
    BlockedActive,

    /// <summary>本地存在其它状态档案（草稿/已提交/不予受理等）：拒绝。</summary>
    BlockedLocalArchive
}

/// <summary>
/// 异地协助申请人准入规则（纯逻辑，便于单测）。
/// 设计口径（方案 A）：
/// 1. 本地在享 → 拒绝（走保障对象动态管理，不重复建协助记录）；
/// 2. 本地仅停保（Stopped）→ 允许并标记「户籍迁出」；
/// 3. 本地存在草稿/已提交/不予受理等其它状态 → 拒绝（本地档案尚未闭环）；
/// 4. 本地无档案 → 允许（由调用方继续查历史导入库）。
/// </summary>
public static class RemoteAssistanceRules
{
    public static RemoteAssistanceGate Resolve(IEnumerable<string>? localStatuses)
    {
        var statuses = (localStatuses ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (statuses.Contains(ApplicationStatusCodes.APPROVED)
            || statuses.Contains(ApplicationStatusCodes.COMPLETED))
        {
            return RemoteAssistanceGate.BlockedActive;
        }

        if (statuses.Contains(ApplicationStatusCodes.STOPPED))
        {
            return RemoteAssistanceGate.AllowedHukouMovedOut;
        }

        return statuses.Count > 0
            ? RemoteAssistanceGate.BlockedLocalArchive
            : RemoteAssistanceGate.Allowed;
    }
}
