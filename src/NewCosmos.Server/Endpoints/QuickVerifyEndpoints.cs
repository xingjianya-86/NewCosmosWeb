using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 低收入人口救助帮扶 · 快速核验 / 模拟测算（只读）。
/// 权限统一 INCOME_VIEW；全部业务计算在 Core 完成，Server 只做契约映射，前端只展示。
/// </summary>
public static class QuickVerifyEndpoints
{
    public static IEndpointRouteBuilder MapQuickVerifyEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/social-assistance/quick-verify", GetVerifyAsync);
        app.MapGet("/api/social-assistance/quick-verify/standards", GetStandardsAsync);
        app.MapPost("/api/social-assistance/quick-verify/simulate", SimulateAsync);
        app.MapPost("/api/social-assistance/quick-verify/report", BuildReportAsync);
        return app;
    }

    /// <summary>按身份证（+可选姓名）核验 10 组台账；name 为空时由 Core 从人员库反查。</summary>
    private static async Task<IResult> GetVerifyAsync(
        HttpContext context,
        INewPermissionService permissions,
        IQuickVerifySimulationService simulation,
        CancellationToken ct,
        string? idCard,
        string? name)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.INCOME_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var result = await simulation.VerifyViewAsync(idCard ?? string.Empty, name, ct);
        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(ToResponse(result.Value!));
    }

    /// <summary>读取测算所需标准（低保档 / 边缘上限 / 赡养费默认值 / 土地单价）。</summary>
    private static async Task<IResult> GetStandardsAsync(
        HttpContext context,
        INewPermissionService permissions,
        IQuickVerifySimulationService simulation,
        CancellationToken ct,
        string? hukou,
        int? year)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.INCOME_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var result = await simulation.GetStandardsAsync(hukou, year, ct);
        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(ToResponse(result.Value!));
    }

    /// <summary>收入模拟测算：赡养费（默认标准×20%）、土地三用途、种植补贴 → 年值口径 → 标准区间。</summary>
    private static async Task<IResult> SimulateAsync(
        QuickVerifySimulateRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        IQuickVerifySimulationService simulation,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.INCOME_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (request is null)
        {
            return InvalidRequest(context, "请求体不能为空");
        }

        var result = await simulation.SimulateAsync(ToInput(request), ct);
        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(ToResponse(result.Value!));
    }

    /// <summary>生成三段式简易报告（文本由 Core 生成；页面只做复制/浏览器打印）。</summary>
    private static async Task<IResult> BuildReportAsync(
        QuickVerifyReportRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        IQuickVerifySimulationService simulation,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.INCOME_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (request is null)
        {
            return InvalidRequest(context, "请求体不能为空");
        }

        var result = await simulation.BuildReportAsync(
            ToInput(request), request.IncludeIncome, request.IncludeVerification, ct);
        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(ToResponse(result.Value!));
    }

    #region 契约映射

    private static QuickVerifySimulationInput ToInput(QuickVerifySimulateRequest request) => new()
    {
        Name = (request.Name ?? string.Empty).Trim(),
        IdCard = (request.IdCard ?? string.Empty).Trim(),
        HukouType = (request.Hukou ?? string.Empty).Trim(),
        FamilySizeExcludingHead = request.FamilySizeExcludingHead,
        Children = request.Children,
        PerChildMonthlyFee = request.PerChildMonthlyFee,
        LandSelfArea = request.LandSelfArea,
        LandSubleaseArea = request.LandSubleaseArea,
        LandContractArea = request.LandContractArea,
        SubsidyAnnual = request.SubsidyAnnual
    };

    private static QuickVerifyResponse ToResponse(QuickVerifyVerificationView view) => new(
        view.IdCard,
        view.IdCardMasked,
        view.QueryName,
        view.NameSource,
        view.NameDisplay,
        view.Summary,
        view.HasAnyRecord,
        view.HitGroupCount,
        view.Groups.Select(ToResponse).ToList(),
        new QuickVerifyImportSuggestionDto(
            view.LandImportTotal,
            view.LandImportSource,
            view.SubsidyImportTotal,
            view.SubsidyImportCount));

    private static QuickVerifyGroupDto ToResponse(QuickVerifyVerificationGroup group) => new(
        group.Key,
        group.Title,
        group.IsFailure,
        group.FailureReason,
        group.MatchNote,
        group.HasItems,
        group.Count,
        group.CountText,
        group.Items.Select(ToResponse).ToList());

    private static QuickVerifyItemDto ToResponse(QuickVerifyVerificationItem item) => new(
        item.Title,
        item.Detail,
        item.Value,
        item.Badge);

    private static QuickVerifyStandardsResponse ToResponse(QuickVerifyStandards standards) => new(
        standards.Hukou,
        standards.HukouText,
        standards.Year,
        standards.YearNote,
        standards.StandardType,
        standards.StandardSource,
        standards.StandardAvailable,
        standards.StandardError,
        standards.SubsistenceStandardMonthly,
        standards.EdgeStandardMonthly,
        standards.PerChildMonthlyFee,
        standards.SupportFeeRatio,
        standards.LowIncomeMultiplier,
        standards.SelfFarmUnitPrice,
        standards.SubleaseUnitPrice,
        standards.ContractUnitPrice,
        standards.UnitPriceText,
        standards.StandardText,
        standards.EdgeText,
        standards.PerChildFeeText);

    private static QuickVerifySimulationResponse ToResponse(QuickVerifySimulationResult result) => new(
        result.Name,
        result.IdCard,
        result.Hukou,
        result.HukouText,
        result.FamilySize,
        result.Children,
        result.PerChildMonthlyFee,
        result.AlimonyAnnual,
        result.LandAnnual,
        result.SubsidyAnnual,
        result.AnnualFamilyIncome,
        result.PerCapitaAnnual,
        result.PerCapitaMonthly,
        result.SubsistenceStandardMonthly,
        result.EdgeStandardMonthly,
        result.LandSelfArea,
        result.LandSubleaseArea,
        result.LandContractArea,
        result.SelfFarmUnitPrice,
        result.SubleaseUnitPrice,
        result.ContractUnitPrice,
        result.ZoneKind,
        result.ZoneText,
        result.AnnualIncomeText,
        result.PerCapitaAnnualText,
        result.PerCapitaMonthlyText,
        result.StandardText,
        result.EdgeText,
        result.PerChildFeeText,
        result.Lines
            .Select(line => new QuickVerifySimulationLineDto(line.Label, line.Value))
            .ToList());

    private static QuickVerifyReportResponse ToResponse(QuickVerifyReport report) => new(
        report.Text,
        report.Length,
        report.IncomeIncluded,
        report.VerificationIncluded,
        report.VerificationError);

    #endregion

    private static IResult InvalidRequest(HttpContext context, string message)
        => ApiErrors.Write(
            context,
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            message);
}
