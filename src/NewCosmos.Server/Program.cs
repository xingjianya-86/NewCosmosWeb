using System.IO.Compression;
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using NewCosmos.Models.Options;
using NewCosmos.Server.Configuration;
using NewCosmos.Server.Endpoints;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.AssetVerification;
using NewCosmos.Services.Domain.ArchiveManagement;
using NewCosmos.Services.Domain.BankFlow;
using NewCosmos.Services.Domain.ChangeManagement;
using NewCosmos.Services.Domain.ElderlyBenefits;
using NewCosmos.Services.Domain.NearRelative;
using NewCosmos.Services.Domain.Recovery;
using NewCosmos.Services.Domain.Reporting;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.Services.Domain.SpecialApproval;
using NewCosmos.Services.Domain.TempRelief;
using NewCosmos.Services.Domain.UserManagement;
using NewCosmos.Services.System;
using NewCosmos.Services.UserManagement;
using NewCosmos.Services.Utilities;

var serverOptions = ServerEnvironmentOptions.Load();

var builder = WebApplication.CreateBuilder(args);

// 默认监听 8787（Web 开发代理与 .2 nginx 约定）；ASPNETCORE_URLS / Urls / --urls 优先。
if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS"))
    && string.IsNullOrWhiteSpace(builder.Configuration["urls"]))
{
    builder.WebHost.UseUrls("http://0.0.0.0:8787");
}

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = serverOptions.MaxRequestBodyBytes;
});

builder.Services.AddSingleton(serverOptions);
builder.Services.AddSingleton(serverOptions.Database);
builder.Services.AddSingleton(serverOptions.Storage);
builder.Services.AddSingleton(serverOptions.App);
builder.Services.AddSingleton(serverOptions.Performance);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    // 生产环境 nginx 与本机 API 同机；仅信任 loopback 代理，避免外部伪造 X-Forwarded-For。
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(serverOptions.CorsOrigins)
        .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
        .WithHeaders("Authorization", "Content-Type", "X-NC-Device", "X-NC-Device-Key")
        .WithExposedHeaders("traceId"));
});

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
        ["application/json; charset=utf-8"]);
});
builder.Services.Configure<BrotliCompressionProviderOptions>(
    options => options.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(
    options => options.Level = CompressionLevel.Fastest);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition =
        System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
});
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

// Core 服务：数据库、日志、会话、用户。
builder.Services.AddSingleton<ILoggerService, LoggerService>();
builder.Services.AddSingleton<ISessionGuard, RequestSessionGuard>();
builder.Services.AddSingleton<ISchemaSyncService, NoopSchemaSyncService>();
builder.Services.AddSingleton<PostgreSqlDatabaseService>();
builder.Services.AddSingleton<IDatabaseService>(
    provider => provider.GetRequiredService<PostgreSqlDatabaseService>());
builder.Services.AddSingleton<IUserSessionService, UserSessionService>();
builder.Services.AddScoped<IUserService, UserService>();
// 行政区划：无状态缓存 + 单例 DB 依赖，沿用 Core 其它 System 服务的 Singleton 生命周期。
builder.Services.AddSingleton<IRegionService, RegionService>();

// 权限与数据范围：服务端强制校验，不信任前端。
builder.Services.AddSingleton<INewPermissionService, NewPermissionService>();
builder.Services.AddSingleton<IDataScopeService, DataScopeService>();

// 业务统计/查询服务：全部复用 Core，禁止在 Server 重抄口径。
builder.Services.AddSingleton<ISeedMergeService, SeedMergeService>();
builder.Services.AddSingleton<IStandardConfigService, StandardConfigService>();
builder.Services.AddSingleton<ElderlyCategoryService>();
builder.Services.AddSingleton<IHolidayService, HolidayService>();
builder.Services.AddSingleton<IBusinessTimelineService, BusinessTimelineService>();
builder.Services.AddSingleton<IStatisticsService, StatisticsService>();
builder.Services.AddSingleton<IGracePeriodService, GracePeriodService>();
builder.Services.AddSingleton<IElderlyApplicationService, ElderlyApplicationService>();
builder.Services.AddSingleton<ICollegeStudentService, CollegeStudentService>();
builder.Services.AddSingleton<IAssetVerificationService, AssetVerificationService>();
// 低收入人口救助帮扶子页面：字典/组织/人员检索等底层依赖。
builder.Services.AddSingleton<IDictionaryService, DictionaryService>();
builder.Services.AddSingleton<IDictCacheService, DictCacheService>();
builder.Services.AddSingleton<IOrganizationService, OrganizationService>();
builder.Services.AddSingleton<IPersonSearchService, PersonSearchService>();
builder.Services.AddSingleton<IBankFlowQueryService, BankFlowQueryService>();
builder.Services.AddSingleton<INearRelativeService, NearRelativeService>();
builder.Services.AddSingleton<ITempReliefService, TempReliefService>();
builder.Services.AddSingleton<IChangeService, ChangeService>();
builder.Services.AddSingleton<IQuickVerifyService, QuickVerifyService>();
builder.Services.AddSingleton<IQuickVerifySimulationService, QuickVerifySimulationService>();
// 月报服务依赖 Scoped 的 IUserService，必须 Scoped（Singleton 无法消费 Scoped）。
builder.Services.AddScoped<IMonthlyReportService, MonthlyReportService>();

// 后补追缴：业务口径全部在 Core；Server 只做端点/打印装配。
builder.Services.AddScoped<IRecoveryService, RecoveryService>();
builder.Services.AddScoped<IRecoveryHistoryProvider, RecoveryHistoryProvider>();
builder.Services.AddScoped<RecoveryPrintDataService>();

// 低收入人口认定表单：复用 Core 业务服务，禁止在 Server 重抄收入/分类/保障金公式。
builder.Services.AddSingleton<IPdfVerificationService, PdfVerificationService>();
builder.Services.AddSingleton<ICapabilityAssessmentService, CapabilityAssessmentService>();
builder.Services.AddSingleton<IGuaranteeAmountService, GuaranteeAmountService>();
builder.Services.AddSingleton<IIncomeCalculationService, IncomeCalculationService>();
builder.Services.AddSingleton<IOtherDifficultyLedgerService, OtherDifficultyLedgerService>();
builder.Services.AddSingleton<IApplicationStatusService, ApplicationStatusService>();
builder.Services.AddSingleton<IClassificationService, ClassificationService>();
builder.Services.AddSingleton<IFamilyMemberService, FamilyMemberService>();
builder.Services.AddSingleton<ISupporterService, SupporterService>();
builder.Services.AddSingleton<ICaregiverService, CaregiverService>();
builder.Services.AddSingleton<ISubsidyDataService, SubsidyDataService>();
builder.Services.AddSingleton<ILandContractService, LandContractService>();
builder.Services.AddSingleton<IHouseholdSurveyService, HouseholdSurveyService>();
builder.Services.AddSingleton<IVisitRecordService, VisitRecordService>();
builder.Services.AddSingleton<IEconomicDetailService, EconomicDetailService>();
builder.Services.AddSingleton<IApplicationService, ApplicationService>();
builder.Services.AddSingleton<IImportedArchiveService, ImportedArchiveService>();
builder.Services.AddSingleton<ISpecialApprovalService, SpecialApprovalService>();
builder.Services.AddScoped<ApplicationFormService>();

// Server 侧聚合服务。
builder.Services.AddScoped<PrinterBindingService>();
builder.Services.AddScoped<AgentDeviceService>();
// 档案制作页推送打印：数据装配 + Server 侧队列网关（不引用 Office，不在 Server 生成 PDF）。
builder.Services.AddScoped<ArchivePrintDataService>();
builder.Services.AddScoped<ArchivePrintQueueService>();
// 档案查询/证明打印：字段装配 + 证明单位模板映射（证明打印任务复用上面的打印队列网关）。
builder.Services.AddScoped<ArchiveProofDataService>();
builder.Services.AddScoped<ProofUnitTemplateService>();
builder.Services.AddScoped<ArchivePhotoService>();
// 照片中心：跨四业务域的照片补全/查询/导出/打印（Core 版照片服务，与上面 Server 旧照片服务并存）。
builder.Services.AddScoped<NewCosmos.Services.Domain.ArchiveManagement.IArchivePhotoService,
    NewCosmos.Services.Domain.ArchiveManagement.ArchivePhotoService>();
builder.Services.AddScoped<PhotoPrintService>();
builder.Services.AddScoped<TemplateAdminService>();
builder.Services.AddScoped<PublicityService>();
builder.Services.AddScoped<ArchiveProductionDataService>();
// 死亡情况说明：本村范围、身份八源、提醒、联动、申报台账。
builder.Services.AddSingleton<DeathIdentityAggregator>();
builder.Services.AddSingleton<IDeathStatementService, DeathStatementService>();
// 资产核查推送打印：按 nc_biz_asset_checks.id 装配授权书/核查报告字段。
builder.Services.AddScoped<AssetCheckPrintDataService>();
builder.Services.AddScoped<ApplicationQueryService>();
builder.Services.AddScoped<ApplicationWorkflowQueryService>();
builder.Services.AddScoped<DataScopeFilterBuilder>();
builder.Services.AddSingleton<DatabaseMigrator>();

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseForwardedHeaders();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    if (context.Request.Path.StartsWithSegments("/api/auth", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.Headers.CacheControl = "no-store";
    }
    await next();
});
app.UseResponseCompression();
app.UseCors();
app.UseMiddleware<SessionAuthenticationMiddleware>();

app.MapAuthEndpoints();
app.MapPrinterEndpoints();
app.MapAgentEndpoints();
app.MapPrintJobEndpoints();
app.MapApplicationEndpoints();
app.MapDashboardEndpoints();
app.MapSocialAssistanceEndpoints();
app.MapCollegeStudentEndpoints();
app.MapNearRelativeEndpoints();
app.MapQuickVerifyEndpoints();
app.MapTempReliefEndpoints();
app.MapIcd10Endpoints();
app.MapRecoveryEndpoints();
app.MapArchiveEndpoints();
app.MapArchivePhotoEndpoints();
app.MapPhotoEndpoints();
app.MapTemplateAdminEndpoints();
app.MapPublicityEndpoints();
app.MapArchiveOutputEndpoints();
app.MapDeathStatementEndpoints();
app.MapChangeManagementEndpoints();
app.MapMonthlyReportEndpoints();
app.MapApplicationWorkflowEndpoints();
app.MapApplicationWorkflowPhotoEndpoints();
app.MapApplicationFormEndpoints();
app.MapSpecialApprovalEndpoints();
app.MapElderlyEndpoints();
app.MapAssetVerificationEndpoints();
app.MapAssetMonthlyAuditEndpoints();
app.MapAssetMonthlyReportEndpoints();
app.MapSystemEndpoints();
app.MapRegionEndpoints();

if (serverOptions.AutoMigrate)
{
    await app.Services.GetRequiredService<DatabaseMigrator>().RunAsync();
}

app.Run();

/// <summary>供集成测试引用。</summary>
public partial class Program;
