namespace NewCosmos.Server.Services.Reprint;

/// <summary>
/// 补打域注册表：按域键解析 Provider；域顺序固定为 <see cref="ReprintDomains.All"/>。
/// </summary>
public sealed class ReprintProviderRegistry
{
    private readonly Dictionary<string, IReprintProvider> _byDomain;

    public ReprintProviderRegistry(IEnumerable<IReprintProvider> providers)
    {
        _byDomain = providers.ToDictionary(provider => provider.DomainKey, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>全部域（固定展示顺序；缺失的域自动跳过）。</summary>
    public IReadOnlyList<IReprintProvider> All => ReprintDomains.All
        .Select(domain => _byDomain.GetValueOrDefault(domain))
        .OfType<IReprintProvider>()
        .ToList();

    /// <summary>按域键解析；未支持返回 null。</summary>
    public IReprintProvider? Resolve(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return null;
        return _byDomain.GetValueOrDefault(domain.Trim());
    }
}
