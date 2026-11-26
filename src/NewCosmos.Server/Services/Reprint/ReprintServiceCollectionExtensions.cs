using Microsoft.Extensions.DependencyInjection;
using NewCosmos.Server.Services.Reprint;

namespace NewCosmos.Server.Services;

/// <summary>
/// 历史补打中心服务注册（Program.cs 只需一行 <c>builder.Services.AddReprintServices();</c>）。
/// 依赖既有注册：ApplicationFormService / ArchivePrintDataService / ArchivePrintQueueService /
/// PrinterBindingService / ITempReliefService / IElderlyApplicationService / IAssetVerificationService /
/// IApplicationService / IBusinessTimelineService / IDatabaseService / ILoggerService。
/// </summary>
public static class ReprintServiceCollectionExtensions
{
    public static IServiceCollection AddReprintServices(this IServiceCollection services)
    {
        services.AddScoped<IReprintProvider, SocialAssistanceReprintProvider>();
        services.AddScoped<IReprintProvider, TempReliefReprintProvider>();
        services.AddScoped<IReprintProvider, ElderlyReprintProvider>();
        services.AddScoped<IReprintProvider, AssetVerificationReprintProvider>();
        services.AddScoped<IReprintProvider, DynamicRecordReprintProvider>();

        services.AddScoped<ReprintProviderRegistry>();
        services.AddScoped<ReprintJobService>();
        services.AddScoped<ReprintPrintRecordService>();

        return services;
    }
}
