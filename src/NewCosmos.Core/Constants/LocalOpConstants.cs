namespace NewCosmos.Constants;

/// <summary>
/// 本机操作（LocalOp）指令白名单：服务端入队校验与 Agent 执行器分发共用同一份常量
/// （业务常量铁律：常量只在 Core 定义一处，Server / Agent 引用，禁止各端各写一份字面量）。
/// 新增 op 必须同时：① 加入 <see cref="KnownOps"/>；② Agent <c>LocalOpExecutor</c> 增加分发分支；
/// ③ 前端（网络接入 / 设置-公文字体）接入调用。
/// </summary>
public static class LocalOpConstants
{
    /// <summary>公文字体检查（扫随包字体目录与系统已装字体比对）。</summary>
    public const string OpFontCheck = "font.check";

    /// <summary>公文字体一键安装（复制到用户字体目录 + 写 HKCU 注册表，免管理员）。</summary>
    public const string OpFontInstall = "font.install";

    /// <summary>ZeroTier 状态检测（info + listnetworks 解析）。</summary>
    public const string OpZeroTierStatus = "zerotier.status";

    /// <summary>ZeroTier 静默安装（随包 MSI，提权 UAC）。</summary>
    public const string OpZeroTierInstall = "zerotier.install";

    /// <summary>加入 ZeroTier 网络（可选 Moon）。</summary>
    public const string OpZeroTierJoin = "zerotier.join";

    /// <summary>退出 ZeroTier 网络。</summary>
    public const string OpZeroTierLeave = "zerotier.leave";

    /// <summary>
    /// 已知 op 白名单（区分大小写，与 Agent <c>switch (op)</c> 的序数比较语义一致）。
    /// 服务端在入队前校验：未知 op 直接 400，不落 <c>nc_biz_print_jobs</c>。
    /// </summary>
    public static readonly IReadOnlyCollection<string> KnownOps = new[]
    {
        OpFontCheck,
        OpFontInstall,
        OpZeroTierStatus,
        OpZeroTierInstall,
        OpZeroTierJoin,
        OpZeroTierLeave,
    };

    /// <summary>op 是否为已知指令（服务端下发前的白名单校验；调用方负责先 Trim）。</summary>
    public static bool IsKnownOp(string? op) =>
        !string.IsNullOrWhiteSpace(op) && KnownOps.Contains(op);
}
