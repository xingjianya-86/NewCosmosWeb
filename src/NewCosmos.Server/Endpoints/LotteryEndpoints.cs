using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using NewCosmos.Constants;
using NewCosmos.Models.Lottery;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services.Lottery;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.Lottery;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 彩票助手（Web）：开奖数据同步/查询、机选、购彩记录与验证、Worker 训练/预测任务。
/// <para>
/// 业务口径全部来自 Core <see cref="ILotteryDataService"/> / <see cref="ILotteryPurchaseService"/> /
/// <see cref="LotteryPrizeRules"/>；本端点只做权限、HTTP 契约映射与 Worker 编排。
/// 全部接口要求 <see cref="PermissionCodes.LOTTERY_ACCESS"/>。
/// </para>
/// </summary>
public static class LotteryEndpoints
{
    private const string Prefix = "/api/lottery";
    private const int DefaultPageSize = 30;
    private const int MaxPageSize = 100;
    private const int DefaultPredictCount = 5;
    private const int MaxPredictCount = 20;

    public static IEndpointRouteBuilder MapLotteryEndpoints(this IEndpointRouteBuilder app)
    {
        // 汇总 / 开奖
        app.MapGet($"{Prefix}/summary", GetSummaryAsync);
        app.MapGet($"{Prefix}/draws", GetDrawsAsync);
        app.MapGet($"{Prefix}/draws/latest", GetLatestDrawAsync);
        app.MapGet($"{Prefix}/draws/count", GetDrawCountAsync);
        app.MapGet($"{Prefix}/draws/{{drawNumber}}", GetDrawAsync);

        // 同步 / 机选（POST /sync、POST /quick-pick 为旧版别名，保留兼容）
        app.MapPost($"{Prefix}/sync-history", SyncAsync);
        app.MapPost($"{Prefix}/sync", SyncAsync);
        app.MapGet($"{Prefix}/quick-pick", QuickPickGetAsync);
        app.MapPost($"{Prefix}/quick-pick", QuickPickAsync);

        // 购彩记录
        app.MapGet($"{Prefix}/purchases", GetPurchasesAsync);
        app.MapGet($"{Prefix}/purchases/summary", GetPurchaseSummaryAsync);
        app.MapPost($"{Prefix}/purchases/verify", VerifyAsync);

        // Worker 代理：健康 / 模型 / 任务
        app.MapGet($"{Prefix}/worker/health", GetWorkerHealthAsync);
        app.MapGet($"{Prefix}/worker/models", GetWorkerModelsAsync);
        app.MapPost($"{Prefix}/worker/jobs/train", ProxyTrainAsync);
        app.MapPost($"{Prefix}/worker/jobs/predict", ProxyPredictAsync);
        app.MapGet($"{Prefix}/worker/jobs", ListWorkerJobsAsync);
        app.MapGet($"{Prefix}/worker/jobs/{{jobId}}", GetWorkerJobAsync);
        app.MapPost($"{Prefix}/worker/jobs/{{jobId}}/cancel", CancelWorkerJobAsync);

        // 旧版别名（Web 早期版本使用，保留避免破坏已发布页面）
        app.MapGet($"{Prefix}/model-status", GetModelStatusAsync);
        app.MapPost($"{Prefix}/train", TrainAsync);
        app.MapPost($"{Prefix}/predict", PredictAsync);
        app.MapGet($"{Prefix}/jobs/{{jobId}}", GetJobAsync);

        return app;
    }

    // ───────────────────────────── 汇总 ─────────────────────────────

    private static async Task<IResult> GetSummaryAsync(
        string? type,
        HttpContext context,
        INewPermissionService permissions,
        ILotteryDataService data,
        ILotteryPurchaseService purchases,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, ct) is { } denied)
            return denied;
        if (!TryParseLotteryType(type, out var lotteryType))
            return InvalidRequest(context, "type 必须为 SSQ 或 DLT");

        var latestResult = await data.GetLatestDrawAsync(lotteryType, ct);
        if (latestResult.IsFailure)
            return MapResult(context, latestResult);

        var countResult = await data.GetDrawCountAsync(lotteryType, ct);
        if (countResult.IsFailure)
            return MapResult(context, countResult);

        var recentResult = await data.GetRecentDrawsAsync(lotteryType, 10, ct);
        if (recentResult.IsFailure)
            return MapResult(context, recentResult);

        var purchaseResult = await purchases.GetPurchaseSummaryAsync(lotteryType, ct);
        if (purchaseResult.IsFailure)
            return MapResult(context, purchaseResult);

        return Results.Json(new LotterySummaryResponse(
            latestResult.Value is null ? null : ToDrawDto(latestResult.Value),
            countResult.Value,
            recentResult.Value.Select(ToDrawDto).ToList(),
            ToPurchaseSummaryDto(purchaseResult.Value)));
    }

    // ───────────────────────────── 开奖 ─────────────────────────────

    private static async Task<IResult> GetDrawsAsync(
        string? type,
        int? page,
        int? pageSize,
        HttpContext context,
        INewPermissionService permissions,
        ILotteryDataService data,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, ct) is { } denied)
            return denied;
        if (!TryParseLotteryType(type, out var lotteryType))
            return InvalidRequest(context, "type 必须为 SSQ 或 DLT");

        var safePage = page is > 0 ? page.Value : 1;
        var safePageSize = pageSize is > 0 ? Math.Min(pageSize.Value, MaxPageSize) : DefaultPageSize;

        var listResult = await data.GetDrawListAsync(lotteryType, safePage, safePageSize, ct);
        if (listResult.IsFailure)
            return MapResult(context, listResult);

        var countResult = await data.GetDrawCountAsync(lotteryType, ct);
        if (countResult.IsFailure)
            return MapResult(context, countResult);

        return Results.Json(new LotteryDrawListResponse(
            listResult.Value.Select(ToDrawDto).ToList(),
            countResult.Value,
            safePage,
            safePageSize));
    }

    private static async Task<IResult> GetLatestDrawAsync(
        string? type,
        HttpContext context,
        INewPermissionService permissions,
        ILotteryDataService data,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, ct) is { } denied)
            return denied;
        if (!TryParseLotteryType(type, out var lotteryType))
            return InvalidRequest(context, "type 必须为 SSQ 或 DLT");

        var result = await data.GetLatestDrawAsync(lotteryType, ct);
        if (result.IsFailure)
            return MapResult(context, result);

        return Results.Json(result.Value is null ? null : ToDrawDto(result.Value));
    }

    private static async Task<IResult> GetDrawCountAsync(
        string? type,
        HttpContext context,
        INewPermissionService permissions,
        ILotteryDataService data,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, ct) is { } denied)
            return denied;
        if (!TryParseLotteryType(type, out var lotteryType))
            return InvalidRequest(context, "type 必须为 SSQ 或 DLT");

        var result = await data.GetDrawCountAsync(lotteryType, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(new LotteryCountResponse(result.Value));
    }

    private static async Task<IResult> GetDrawAsync(
        string drawNumber,
        string? type,
        HttpContext context,
        INewPermissionService permissions,
        ILotteryDataService data,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, ct) is { } denied)
            return denied;
        if (!TryParseLotteryType(type, out var lotteryType))
            return InvalidRequest(context, "type 必须为 SSQ 或 DLT");

        var result = await data.GetDrawByNumberAsync(lotteryType, drawNumber, ct);
        if (result.IsFailure)
            return MapResult(context, result);

        return result.Value is null
            ? ApiErrors.Write(context, StatusCodes.Status404NotFound, ApiErrorCodes.NotFound,
                $"开奖记录不存在：{drawNumber}")
            : Results.Json(ToDrawDto(result.Value));
    }

    // ───────────────────────────── 同步 / 机选 ─────────────────────────────

    private static async Task<IResult> SyncAsync(
        LotteryTypeRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        ILotteryDataService data,
        ILotteryPurchaseService purchases,
        ILoggerService logger,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, ct) is { } denied)
            return denied;
        if (!TryParseLotteryType(request?.Type, out var lotteryType))
            return InvalidRequest(context, "type 必须为 SSQ 或 DLT");

        // 增量起点 = 本地最新开奖日期（已存在的最新一期会重新 upsert 以刷新奖级）；
        // Full=true 时从最早一期全量抓取，用于修复历史销售额/奖池。
        DateTime? sinceDate = null;
        if (request?.Full != true)
        {
            var latestResult = await data.GetLatestDrawAsync(lotteryType, ct);
            if (latestResult.IsFailure)
                return MapResult(context, latestResult);

            sinceDate = latestResult.Value?.DrawDate.Date;
        }
        var syncResult = await data.SyncHistoryAsync(lotteryType, sinceDate, ct);
        if (syncResult.IsFailure)
            return MapResult(context, syncResult);

        var verifyResult = await purchases.VerifyAndRewardAsync(lotteryType, ct);
        if (verifyResult.IsFailure)
            return MapResult(context, verifyResult);

        logger.LogBusiness("彩票开奖同步完成",
            ("LotteryType", lotteryType.ToString()),
            ("NewCount", syncResult.Value),
            ("Verified", verifyResult.Value.TotalVerified),
            ("Hit", verifyResult.Value.HitCount));

        return Results.Json(new LotterySyncResponse(
            syncResult.Value,
            ToVerificationDto(verifyResult.Value)));
    }

    private static async Task<IResult> QuickPickAsync(
        LotteryQuickPickRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, ct) is { } denied)
            return denied;
        if (!TryParseLotteryType(request?.Type, out var lotteryType))
            return InvalidRequest(context, "type 必须为 SSQ 或 DLT");

        var count = request?.Count ?? 1;
        if (count is < 1 or > MaxPredictCount)
            return InvalidRequest(context, $"count 必须在 1-{MaxPredictCount} 之间");

        return QuickPickResult(lotteryType, count);
    }

    private static async Task<IResult> QuickPickGetAsync(
        string? type,
        int? count,
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, ct) is { } denied)
            return denied;
        if (!TryParseLotteryType(type, out var lotteryType))
            return InvalidRequest(context, "type 必须为 SSQ 或 DLT");

        var safeCount = count ?? 1;
        if (safeCount is < 1 or > MaxPredictCount)
            return InvalidRequest(context, $"count 必须在 1-{MaxPredictCount} 之间");

        return QuickPickResult(lotteryType, safeCount);
    }

    private static IResult QuickPickResult(LotteryType lotteryType, int count)
    {
        var predictions = LotteryNumberGenerator.Generate(lotteryType, count);
        return Results.Json(new LotteryQuickPickResponse(
            predictions.Select(ToPredictionDto).ToList()));
    }

    // ───────────────────────────── 购彩记录 ─────────────────────────────

    private static async Task<IResult> GetPurchasesAsync(
        string? type,
        string? filter,
        int? page,
        int? pageSize,
        HttpContext context,
        INewPermissionService permissions,
        ILotteryPurchaseService purchases,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, ct) is { } denied)
            return denied;
        if (!TryParseLotteryType(type, out var lotteryType))
            return InvalidRequest(context, "type 必须为 SSQ 或 DLT");

        var purchaseFilter = ParseFilter(filter);
        var safePage = page is > 0 ? page.Value : 1;
        var safePageSize = pageSize is > 0 ? Math.Min(pageSize.Value, MaxPageSize) : DefaultPageSize;

        var listResult = await purchases.GetPurchasesAsync(
            lotteryType, purchaseFilter, safePage, safePageSize, ct);
        if (listResult.IsFailure)
            return MapResult(context, listResult);

        var countResult = await purchases.GetPurchaseCountAsync(lotteryType, purchaseFilter, ct);
        if (countResult.IsFailure)
            return MapResult(context, countResult);

        return Results.Json(new LotteryPurchaseListResponse(
            listResult.Value.Select(ToPurchaseDto).ToList(),
            countResult.Value,
            safePage,
            safePageSize));
    }

    private static async Task<IResult> GetPurchaseSummaryAsync(
        string? type,
        HttpContext context,
        INewPermissionService permissions,
        ILotteryPurchaseService purchases,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, ct) is { } denied)
            return denied;
        if (!TryParseLotteryType(type, out var lotteryType))
            return InvalidRequest(context, "type 必须为 SSQ 或 DLT");

        var result = await purchases.GetPurchaseSummaryAsync(lotteryType, ct);
        if (result.IsFailure)
            return MapResult(context, result);

        var summary = result.Value;
        return Results.Json(new LotteryPurchaseSummaryResponse(
            summary.TotalCount,
            summary.HitCount,
            summary.PendingCount,
            summary.TotalPrizeAmount,
            summary.BestPrizeLevel,
            summary.BestPrizeDescription));
    }

    private static async Task<IResult> VerifyAsync(
        LotteryTypeRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        ILotteryPurchaseService purchases,
        ILoggerService logger,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, ct) is { } denied)
            return denied;
        if (!TryParseLotteryType(request?.Type, out var lotteryType))
            return InvalidRequest(context, "type 必须为 SSQ 或 DLT");

        var result = await purchases.VerifyAndRewardAsync(lotteryType, ct);
        if (result.IsFailure)
            return MapResult(context, result);

        logger.LogBusiness("彩票购彩验证",
            ("LotteryType", lotteryType.ToString()),
            ("Verified", result.Value.TotalVerified),
            ("Hit", result.Value.HitCount),
            ("Pending", result.Value.PendingCount));

        return Results.Json(ToVerificationDto(result.Value));
    }

    // ───────────────────────────── 模型 / 任务 ─────────────────────────────

    private static async Task<IResult> GetModelStatusAsync(
        string? type,
        HttpContext context,
        INewPermissionService permissions,
        LotteryWorkerClient worker,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, ct) is { } denied)
            return denied;
        if (!TryParseLotteryType(type, out var lotteryType))
            return InvalidRequest(context, "type 必须为 SSQ 或 DLT");

        var modelsResult = await worker.GetModelsAsync(ct);
        if (modelsResult.IsFailure)
            return MapResult(context, modelsResult);

        var workerType = ToWorkerType(lotteryType);
        var matches = modelsResult.Value
            .Where(model => string.Equals(model.LotteryType, workerType, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(model.LotteryType, lotteryType.ToString(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        var model = matches.FirstOrDefault(item => item.Window == LotteryConstants.LSTM_WINDOW)
                    ?? matches.OrderByDescending(item => item.TrainedAt).FirstOrDefault();

        return Results.Json(new LotteryModelStatusResponse(
            model is not null,
            model?.Window ?? LotteryConstants.LSTM_WINDOW,
            model?.TrainedAt));
    }

    private static async Task<IResult> TrainAsync(
        LotteryTrainRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        LotteryWorkerClient worker,
        ILoggerService logger,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, ct) is { } denied)
            return denied;
        if (!TryParseLotteryType(request?.Type, out var lotteryType))
            return InvalidRequest(context, "type 必须为 SSQ 或 DLT");

        var window = request?.Window ?? LotteryConstants.LSTM_WINDOW;
        if (window is < 10 or > 500)
            return InvalidRequest(context, "window 必须在 10-500 之间");

        var result = await worker.EnqueueTrainAsync(lotteryType, window, ct);
        if (result.IsFailure)
            return MapResult(context, result);

        logger.LogBusiness("彩票训练任务入队",
            ("LotteryType", lotteryType.ToString()),
            ("Window", window),
            ("JobId", result.Value.JobId));

        return Results.Json(new LotteryJobEnqueueResponse(result.Value.JobId, result.Value.Status));
    }

    private static async Task<IResult> PredictAsync(
        LotteryPredictRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        LotteryWorkerClient worker,
        ILoggerService logger,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, ct) is { } denied)
            return denied;
        if (!TryParseLotteryType(request?.Type, out var lotteryType))
            return InvalidRequest(context, "type 必须为 SSQ 或 DLT");

        var count = request?.Count ?? DefaultPredictCount;
        if (count is < 1 or > MaxPredictCount)
            return InvalidRequest(context, $"count 必须在 1-{MaxPredictCount} 之间");

        var result = await worker.EnqueuePredictAsync(
            lotteryType, count, LotteryConstants.LSTM_WINDOW, ct);
        if (result.IsFailure)
            return MapResult(context, result);

        logger.LogBusiness("彩票预测任务入队",
            ("LotteryType", lotteryType.ToString()),
            ("Count", count),
            ("JobId", result.Value.JobId));

        return Results.Json(new LotteryJobEnqueueResponse(result.Value.JobId, result.Value.Status));
    }

    private static async Task<IResult> GetJobAsync(
        string jobId,
        string? type,
        HttpContext context,
        INewPermissionService permissions,
        LotteryWorkerClient worker,
        ILotteryPurchaseService purchases,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, ct) is { } denied)
            return denied;
        if (string.IsNullOrWhiteSpace(jobId))
            return InvalidRequest(context, "jobId 不能为空");

        var hasQueryType = TryParseLotteryType(type, out var queryType);

        var jobResult = await worker.GetJobAsync(jobId, ct);
        if (jobResult.IsFailure)
            return MapResult(context, jobResult);

        var job = jobResult.Value;
        var lotteryType = ResolveJobLotteryType(job, hasQueryType ? queryType : null);

        IReadOnlyList<LotteryPredictionDto>? predictions = null;
        int? savedCount = null;

        // 预测任务成功：解析结果并幂等入库（重复轮询 savedCount=0）。
        if (string.Equals(job.Status, "succeeded", StringComparison.OrdinalIgnoreCase)
            && string.Equals(job.Type, "predict", StringComparison.OrdinalIgnoreCase))
        {
            var parseResult = ParsePredictions(lotteryType, job.Result);
            if (parseResult.IsFailure)
                return MapResult(context, parseResult);

            var algorithmName = ResolveAlgorithmName(parseResult.Value.AlgorithmCode);
            var saveResult = await purchases.SaveFromPredictionsAsync(
                lotteryType,
                parseResult.Value.Predictions,
                algorithmName,
                job.JobId,
                ct);
            if (saveResult.IsFailure)
                return MapResult(context, saveResult);

            predictions = parseResult.Value.Predictions.Select(ToPredictionDto).ToList();
            savedCount = saveResult.Value;
        }

        return Results.Json(new LotteryJobResponse(
            job.JobId,
            lotteryType.ToString(),
            job.Status,
            job.Progress,
            job.Message,
            job.Result,
            job.Error,
            job.LogTail is { Count: > 0 } ? string.Join('\n', job.LogTail) : null,
            predictions,
            savedCount));
    }

    // ───────────────────────────── Worker 代理 ─────────────────────────────

    private static async Task<IResult> GetWorkerHealthAsync(
        HttpContext context,
        INewPermissionService permissions,
        LotteryWorkerClient worker,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, ct) is { } denied)
            return denied;

        var result = await worker.GetHealthAsync(ct);
        return result.IsFailure ? MapResult(context, result) : Results.Json(result.Value);
    }

    private static async Task<IResult> GetWorkerModelsAsync(
        HttpContext context,
        INewPermissionService permissions,
        LotteryWorkerClient worker,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, ct) is { } denied)
            return denied;

        var result = await worker.GetModelsRawAsync(ct);
        return result.IsFailure ? MapResult(context, result) : Results.Json(result.Value);
    }

    private static async Task<IResult> ProxyTrainAsync(
        [FromBody] JsonElement? request,
        HttpContext context,
        INewPermissionService permissions,
        LotteryWorkerClient worker,
        ILoggerService logger,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, ct) is { } denied)
            return denied;

        if (request is not { ValueKind: JsonValueKind.Object } body)
            return InvalidRequest(context, "请求体必须为 JSON 对象，且包含 lotteryType");

        if (!ValidateWorkerJobBody(body, isPredict: false, out var message))
            return InvalidRequest(context, message);

        var workerResult = await worker.PostJobRawAsync("/jobs/train", body, ct);
        if (workerResult.IsFailure)
            return MapResult(context, workerResult);

        logger.LogBusiness("彩票训练任务入队", ("RequestBody", body.GetRawText()));
        return Results.Json(workerResult.Value, statusCode: StatusCodes.Status202Accepted);
    }

    private static async Task<IResult> ProxyPredictAsync(
        [FromBody] JsonElement? request,
        HttpContext context,
        INewPermissionService permissions,
        LotteryWorkerClient worker,
        ILoggerService logger,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, ct) is { } denied)
            return denied;

        if (request is not { ValueKind: JsonValueKind.Object } body)
            return InvalidRequest(context, "请求体必须为 JSON 对象，且包含 lotteryType");

        if (!ValidateWorkerJobBody(body, isPredict: true, out var message))
            return InvalidRequest(context, message);

        var workerResult = await worker.PostJobRawAsync("/jobs/predict", body, ct);
        if (workerResult.IsFailure)
            return MapResult(context, workerResult);

        logger.LogBusiness("彩票预测任务入队", ("RequestBody", body.GetRawText()));
        return Results.Json(workerResult.Value, statusCode: StatusCodes.Status202Accepted);
    }

    private static async Task<IResult> ListWorkerJobsAsync(
        int? limit,
        string? status,
        HttpContext context,
        INewPermissionService permissions,
        LotteryWorkerClient worker,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, ct) is { } denied)
            return denied;

        var result = await worker.ListJobsRawAsync(Math.Clamp(limit ?? 50, 1, 200), status, ct);
        return result.IsFailure ? MapResult(context, result) : Results.Json(result.Value);
    }

    private static async Task<IResult> GetWorkerJobAsync(
        string jobId,
        string? type,
        HttpContext context,
        INewPermissionService permissions,
        LotteryWorkerClient worker,
        ILotteryPurchaseService purchases,
        ILoggerService logger,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, ct) is { } denied)
            return denied;
        if (string.IsNullOrWhiteSpace(jobId))
            return InvalidRequest(context, "jobId 不能为空");

        var jobResult = await worker.GetJobRawAsync(jobId, ct);
        if (jobResult.IsFailure)
            return MapResult(context, jobResult);

        var job = jobResult.Value;
        var hasQueryType = TryParseLotteryType(type, out var queryType);
        var lotteryType = ResolveJobLotteryType(job, hasQueryType ? queryType : null);
        var jobStatus = TryGetString(job, "status");
        var jobType = TryGetString(job, "type");

        // 预测任务成功：解析结果并幂等入库（重复轮询 savedCount=0）。
        if (string.Equals(jobStatus, "succeeded", StringComparison.OrdinalIgnoreCase)
            && string.Equals(jobType, "predict", StringComparison.OrdinalIgnoreCase)
            && job.TryGetProperty("result", out var resultElement)
            && resultElement.ValueKind != JsonValueKind.Null)
        {
            var parseResult = ParsePredictions(lotteryType, resultElement);
            if (parseResult.IsFailure)
                return MapResult(context, parseResult);

            var algorithmName = ResolveAlgorithmName(parseResult.Value.AlgorithmCode);
            var saveResult = await purchases.SaveFromPredictionsAsync(
                lotteryType,
                parseResult.Value.Predictions,
                algorithmName,
                jobId,
                ct);
            if (saveResult.IsFailure)
                return MapResult(context, saveResult);

            if (saveResult.Value > 0)
            {
                logger.LogBusiness("彩票预测结果入库",
                    ("JobId", jobId),
                    ("LotteryType", lotteryType.ToString()),
                    ("Count", saveResult.Value));
            }

            var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(job.GetRawText())
                          ?? new Dictionary<string, JsonElement>();
            payload["savedCount"] = JsonSerializer.SerializeToElement(saveResult.Value);
            return Results.Json(payload);
        }

        return Results.Json(job);
    }

    private static async Task<IResult> CancelWorkerJobAsync(
        string jobId,
        HttpContext context,
        INewPermissionService permissions,
        LotteryWorkerClient worker,
        ILoggerService logger,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, ct) is { } denied)
            return denied;
        if (string.IsNullOrWhiteSpace(jobId))
            return InvalidRequest(context, "jobId 不能为空");

        var result = await worker.CancelJobRawAsync(jobId, ct);
        if (result.IsFailure)
            return MapResult(context, result);

        logger.LogBusiness("彩票任务取消", ("JobId", jobId));
        return Results.Json(result.Value);
    }

    /// <summary>
    /// 只校验不重写：任务请求体必须原样转发给 Worker。彩种缺省由 Worker 决定（ssq），
    /// 显式传入时必须为 ssq/dlt；window/count 做边界校验，避免 Worker 返回 422 英文错误。
    /// </summary>
    private static bool ValidateWorkerJobBody(JsonElement body, bool isPredict, out string message)
    {
        message = string.Empty;

        if (body.TryGetProperty("lotteryType", out var lotteryType))
        {
            var rawType = lotteryType.ValueKind == JsonValueKind.String
                ? lotteryType.GetString()?.Trim().ToLowerInvariant()
                : null;
            if (rawType is not ("ssq" or "dlt"))
            {
                message = "lotteryType 必须为 ssq 或 dlt";
                return false;
            }
        }

        if (body.TryGetProperty("window", out var window)
            && window.ValueKind != JsonValueKind.Null)
        {
            if (window.ValueKind != JsonValueKind.Number
                || !window.TryGetInt32(out var windowValue)
                || windowValue is < 10 or > 500)
            {
                message = "window 必须是 10-500 之间的整数";
                return false;
            }
        }

        if (isPredict
            && body.TryGetProperty("count", out var count)
            && count.ValueKind != JsonValueKind.Null)
        {
            if (count.ValueKind != JsonValueKind.Number
                || !count.TryGetInt32(out var countValue)
                || countValue is < 1 or > MaxPredictCount)
            {
                message = $"count 必须是 1-{MaxPredictCount} 之间的整数";
                return false;
            }
        }

        return true;
    }

    private static LotteryType ResolveJobLotteryType(JsonElement job, LotteryType? fallback)
    {
        if (job.TryGetProperty("params", out var parameters)
            && parameters.ValueKind == JsonValueKind.Object
            && TryGetString(parameters, "lotteryType") is { } raw
            && TryParseLotteryType(raw, out var parsed))
        {
            return parsed;
        }

        return fallback ?? LotteryType.SSQ;
    }

    // ───────────────────────────── 映射 ─────────────────────────────

    private static LotteryDrawDto ToDrawDto(LotteryDraw draw) => new(
        draw.DrawNumber,
        draw.DrawDate,
        draw.GetRedNumbers(),
        draw.GetBlueNumbers(),
        draw.SalesAmount,
        draw.PoolMoney,
        draw.PrizeDescription);

    private static LotteryPredictionDto ToPredictionDto(PredictionResult prediction) => new(
        prediction.GetPredictedReds(),
        prediction.GetPredictedBlues(),
        prediction.ConfidenceScore,
        string.IsNullOrWhiteSpace(prediction.AlgorithmVersion) ? "lstm" : prediction.AlgorithmVersion,
        string.IsNullOrWhiteSpace(prediction.AlgorithmName)
            ? LotteryConstants.LSTM_ALGORITHM_NAME
            : prediction.AlgorithmName);

    private static LotteryPurchaseDto ToPurchaseDto(UserPurchaseRecord record) => new(
        record.Id,
        record.LotteryType.ToString(),
        record.DrawNumber,
        record.GetRedNumbers(),
        record.GetBlueNumbers(),
        record.GetActualRedNumbers(),
        record.GetActualBlueNumbers(),
        record.Algorithm,
        record.PurchaseDate,
        record.IsVerified,
        record.IsHit,
        record.HitRedCount,
        record.HitBlueCount,
        record.PrizeLevel,
        record.PrizeLevelText,
        record.PrizeAmount,
        record.StatusText);

    private static LotteryPurchaseSummaryResponse ToPurchaseSummaryDto(LotteryPurchaseSummary summary) => new(
        summary.TotalCount,
        summary.HitCount,
        summary.PendingCount,
        summary.TotalPrizeAmount,
        summary.BestPrizeLevel,
        summary.BestPrizeDescription);

    private static LotteryVerificationDto ToVerificationDto(VerificationSummary summary) => new(
        summary.TotalVerified,
        summary.HitCount,
        summary.PendingCount,
        summary.TotalPrizeAmount,
        summary.BestPrizeLevel,
        summary.BestPrizeDescription,
        summary.EncouragementMessage);

    private sealed record ParsedPredictions(
        List<PredictionResult> Predictions,
        string AlgorithmCode);

    private static Result<ParsedPredictions> ParsePredictions(
        LotteryType lotteryType,
        JsonElement? result)
    {
        if (result is null || result.Value.ValueKind != JsonValueKind.Object)
            return Result.Failure<ParsedPredictions>(
                ErrorCodes.LOTTERY_WORKER_ERROR, "预测任务结果缺失或格式无效");

        if (!result.Value.TryGetProperty("predictions", out var predictionsElement)
            || predictionsElement.ValueKind != JsonValueKind.Array)
        {
            return Result.Failure<ParsedPredictions>(
                ErrorCodes.LOTTERY_WORKER_ERROR, "预测任务结果缺少 predictions");
        }

        var algorithmCode = TryGetString(result.Value, "algorithm") ?? "lstm";
        var predictions = new List<PredictionResult>();

        foreach (var item in predictionsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var reds = ReadIntArray(item, "red_numbers");
            var blues = ReadIntArray(item, "blue_numbers");

            var prediction = new PredictionResult
            {
                LotteryType = lotteryType,
                AlgorithmVersion = algorithmCode,
                AlgorithmName = ResolveAlgorithmName(algorithmCode),
                ConfidenceScore = ReadDecimal(item, "confidence")
            };
            prediction.SetPredictedReds(reds);
            prediction.SetPredictedBlues(blues);
            predictions.Add(prediction);
        }

        if (predictions.Count == 0)
            return Result.Failure<ParsedPredictions>(
                ErrorCodes.LOTTERY_WORKER_ERROR, "预测任务结果中没有任何号码");

        return Result.Success(new ParsedPredictions(predictions, algorithmCode));
    }

    private static int[] ReadIntArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var array)
            || array.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<int>();
        }

        var numbers = new List<int>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var value))
                numbers.Add(value);
            else if (item.ValueKind == JsonValueKind.String
                     && int.TryParse(item.GetString(), NumberStyles.Integer,
                         CultureInfo.InvariantCulture, out var parsed))
                numbers.Add(parsed);
        }

        return numbers.ToArray();
    }

    private static decimal ReadDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return 0m;

        var raw = property.ValueKind switch
        {
            JsonValueKind.Number => property.ToString(),
            JsonValueKind.String => property.GetString(),
            _ => null
        };

        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0m;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.ToString(),
            _ => null
        };
    }

    private static LotteryType ResolveJobLotteryType(LotteryWorkerJob job, LotteryType? fallback)
    {
        if (job.Params is { } parameters
            && parameters.ValueKind == JsonValueKind.Object
            && TryGetString(parameters, "lotteryType") is { } raw
            && TryParseLotteryType(raw, out var parsed))
        {
            return parsed;
        }

        return fallback ?? LotteryType.SSQ;
    }

    private static string ResolveAlgorithmName(string? algorithmCode)
    {
        if (string.IsNullOrWhiteSpace(algorithmCode))
            return LotteryConstants.LSTM_ALGORITHM_NAME;

        return algorithmCode.Trim().ToLowerInvariant() switch
        {
            "lstm" => LotteryConstants.LSTM_ALGORITHM_NAME,
            "random" => LotteryNumberGenerator.RandomAlgorithmName,
            _ => algorithmCode.Trim()
        };
    }

    private static string ToWorkerType(LotteryType lotteryType)
        => lotteryType == LotteryType.DLT ? "dlt" : "ssq";

    private static bool TryParseLotteryType(string? raw, out LotteryType lotteryType)
    {
        lotteryType = LotteryType.SSQ;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        return Enum.TryParse(raw.Trim(), ignoreCase: true, out lotteryType)
               && Enum.IsDefined(lotteryType);
    }

    private static LotteryPurchaseFilter ParseFilter(string? raw)
    {
        return Enum.TryParse<LotteryPurchaseFilter>(raw?.Trim(), ignoreCase: true, out var filter)
               && Enum.IsDefined(filter)
            ? filter
            : LotteryPurchaseFilter.All;
    }

    private static async Task<IResult?> DenyAsync(
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
        => await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.LOTTERY_ACCESS, ct);

    private static IResult MapResult(HttpContext context, Result result)
    {
        var code = result.ErrorCode ?? string.Empty;
        var message = string.IsNullOrWhiteSpace(result.Message) ? "操作失败" : result.Message;

        switch (code)
        {
            case ErrorCodes.VALIDATION_FAILED:
            case ErrorCodes.LOTTERY_DATA_INVALID:
                return ApiErrors.Write(
                    context, StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, message);

            case ErrorCodes.NOT_FOUND:
            case ErrorCodes.RECORD_NOT_FOUND:
            case ErrorCodes.LOTTERY_JOB_NOT_FOUND:
                return ApiErrors.Write(
                    context, StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, message);

            case ErrorCodes.LOTTERY_MODEL_NOT_FOUND:
                return ApiErrors.Write(
                    context, StatusCodes.Status409Conflict, ApiErrorCodes.ValidationError, message);

            case ErrorCodes.LOTTERY_QUEUE_FULL:
                return ApiErrors.Write(
                    context, StatusCodes.Status429TooManyRequests, ApiErrorCodes.ValidationError, message);

            case ErrorCodes.LOTTERY_WORKER_UNREACHABLE:
                return ApiErrors.Write(
                    context,
                    StatusCodes.Status503ServiceUnavailable,
                    ApiErrorCodes.InternalError,
                    "彩票预测服务未启动");

            case ErrorCodes.LOTTERY_DEPENDENCY_MISSING:
                return ApiErrors.Write(
                    context, StatusCodes.Status503ServiceUnavailable, ApiErrorCodes.InternalError, message);

            case ErrorCodes.LOTTERY_SYNC_FAILED:
            case ErrorCodes.LOTTERY_WORKER_ERROR:
                return ApiErrors.Write(
                    context, StatusCodes.Status502BadGateway, ApiErrorCodes.InternalError, message);

            default:
                return ApiErrors.FromResult(context, result);
        }
    }

    private static IResult InvalidRequest(HttpContext context, string message)
        => ApiErrors.Write(
            context, StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, message);
}
