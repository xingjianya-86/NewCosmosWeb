using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Results;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Services.Domain.SevereDisease;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// AI 重病判定端点（Web P0 接线，2026-11-27）。判定口径唯一权威在 Core：
/// 三级管线 <see cref="ISevereDiseaseAdviceService.JudgeAsync"/>（确定性 → 缓存 → LLM）、
/// 自动勾选闸门 <see cref="ISevereDiseaseAdviceService.ShouldAutoCheck"/>、
/// 建议文案拼装点 <see cref="AiJudgeConstants.FormatSuggestion"/> /
/// <see cref="SevereDiseaseByCostRule.FormatSuggestion"/>。
/// 本端点只做：权限校验 → 名称兜底 → 调 Core → 错误映射 → 组 JSON，不重抄判定规则。
/// 判定请求只含疾病名称/编码与费用金额，不含 applicationId，因此不做数据范围校验。
/// </summary>
public static class AiJudgeEndpoints
{
    private const string Prefix = "/api/ai";

    /// <summary>与 <see cref="Icd10Endpoints"/> 完全一致的共享读权限。</summary>
    private static readonly string[] ReadPermissions =
    [
        PermissionCodes.INCOME_VIEW,
        PermissionCodes.TEMP_VIEW
    ];

    public static IEndpointRouteBuilder MapAiJudgeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost($"{Prefix}/severity", JudgeSeverityAsync);
        app.MapPost($"{Prefix}/severity/cost", JudgeSeverityByCostAsync);
        return app;
    }

    /// <summary>
    /// POST /api/ai/severity，body <c>{ name?, code, userRejected }</c>。
    /// 名称为空时由 ICD-10 字典按编码兜底带名；仍带不出名 → 400。
    /// 返回判定字段 + <c>autoEligible/autoChecked/suggestion</c>。
    /// </summary>
    private static async Task<IResult> JudgeSeverityAsync(
        AiSeverityRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        ISevereDiseaseAdviceService advice,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequireAnyPermissionAsync(
            context, permissions, ReadPermissions, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (request is null)
        {
            return ApiErrors.Write(
                context, StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError, "请求体不能为空");
        }

        var code = (request.Code ?? string.Empty).Trim();
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            if (code.Length == 0
                || !Icd10Catalog.TryGetName(code, out var resolved)
                || string.IsNullOrWhiteSpace(resolved))
            {
                return ApiErrors.Write(
                    context, StatusCodes.Status400BadRequest,
                    ApiErrorCodes.ValidationError, "疾病名称为空，无法判定（请先由疾病编码带出名称）");
            }

            name = resolved.Trim();
        }

        var result = await advice.JudgeAsync(name, code, ct);
        if (result.IsFailure)
        {
            return MapAiResult(context, result);
        }

        var judgment = result.Value;
        var autoChecked = ISevereDiseaseAdviceService.ShouldAutoCheck(judgment, request.UserRejected);
        // 与桌面 ApplyApplicantJudgment 同口径：非 severe 不出建议条，文案一律留空（不制造噪音）。
        var suggestion = judgment.Verdict == SevereDiseaseJudgment.VerdictSevere
            ? AiJudgeConstants.FormatSuggestion(judgment, autoChecked)
            : string.Empty;

        return Results.Json(new AiSeverityResponse(
            judgment.Verdict,
            judgment.MatchedItem,
            judgment.Confidence,
            judgment.Reason,
            judgment.Source,
            judgment.PolicyVersion,
            judgment.Model,
            judgment.AutoEligible,
            autoChecked,
            suggestion));
    }

    /// <summary>
    /// POST /api/ai/severity/cost，body <c>{ selfPaid12m, isRural, userRejected }</c>。
    /// 阈值 ≤0（未配置/无效）时静默降级：<c>severe=false, suggestion=""</c>，不报错、不硬编码回退值。
    /// </summary>
    private static async Task<IResult> JudgeSeverityByCostAsync(
        AiSeverityCostRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        ISevereDiseaseAdviceService advice,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequireAnyPermissionAsync(
            context, permissions, ReadPermissions, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (request is null)
        {
            return ApiErrors.Write(
                context, StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError, "请求体不能为空");
        }

        var isRural = request.IsRural;
        var thresholdResult = await advice.GetCostThresholdAsync(isRural, ct);
        if (thresholdResult.IsFailure)
        {
            return MapAiResult(context, thresholdResult);
        }

        var threshold = thresholdResult.Value ?? 0m;
        if (threshold <= 0m)
        {
            // 未配置/配置无效：降级人工（SevereDiseaseByCostRule 纪律，禁止硬编码回退值）。
            return Results.Json(new AiSeverityCostResponse(false, threshold, false, string.Empty));
        }

        if (!SevereDiseaseByCostRule.IsSevereByCost(request.SelfPaid12m, threshold))
        {
            return Results.Json(new AiSeverityCostResponse(false, threshold, false, string.Empty));
        }

        // 与 AI 同闸门：用户否决过则只出建议、不自动勾（只勾不取消）。
        var autoChecked = !request.UserRejected;
        var suggestion = SevereDiseaseByCostRule.FormatSuggestion(
            request.SelfPaid12m!.Value, threshold, isRural, autoChecked);
        return Results.Json(new AiSeverityCostResponse(true, threshold, autoChecked, suggestion));
    }

    /// <summary>
    /// AI 判定错误映射（<c>ApiErrors.Map</c> 无 AI_* 条目，故本端点自带）：
    /// 服务不可达 503 / 超时 504 / 判定失败 502 / 取消 408；其余（DB/配置类）回落通用映射。
    /// </summary>
    private static IResult MapAiResult(HttpContext context, Result result)
    {
        var status = result.ErrorCode switch
        {
            ErrorCodes.AI_SERVICE_UNREACHABLE => StatusCodes.Status503ServiceUnavailable,
            ErrorCodes.AI_TIMEOUT => StatusCodes.Status504GatewayTimeout,
            ErrorCodes.AI_JUDGE_FAILED => StatusCodes.Status502BadGateway,
            ErrorCodes.CANCELLED => StatusCodes.Status408RequestTimeout,
            _ => 0
        };

        if (status == 0)
        {
            return ApiErrors.FromResult(context, result);
        }

        return ApiErrors.Write(
            context,
            status,
            string.IsNullOrEmpty(result.ErrorCode) ? ApiErrorCodes.InternalError : result.ErrorCode,
            result.Message);
    }
}

/// <summary>POST /api/ai/severity 请求体。</summary>
public sealed record AiSeverityRequest(string? Name, string? Code, bool UserRejected);

/// <summary>POST /api/ai/severity/cost 请求体（自付单位：元）。</summary>
public sealed record AiSeverityCostRequest(decimal? SelfPaid12m, bool IsRural, bool UserRejected);

/// <summary>
/// /api/ai/severity 响应：判定字段 + 自动勾选闸门结论 + 建议文案
/// （suggestion 仅 verdict=severe 时非空，拼装点在 Core）。
/// </summary>
public sealed record AiSeverityResponse(
    string Verdict,
    string MatchedItem,
    double Confidence,
    string Reason,
    string Source,
    string PolicyVersion,
    string Model,
    bool AutoEligible,
    bool AutoChecked,
    string Suggestion);

/// <summary>
/// /api/ai/severity/cost 响应：threshold≤0 或未超阈值时 severe=false 且 suggestion 为空。
/// </summary>
public sealed record AiSeverityCostResponse(bool Severe, decimal Threshold, bool AutoChecked, string Suggestion);
