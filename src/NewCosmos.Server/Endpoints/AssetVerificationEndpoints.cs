using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Requests;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.AssetVerification;
using NewCosmos.Services.Domain.Reporting;
using NewCosmos.Services.System;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 家庭经济状况核对只读接口：首页统计、任务列表、详情、月度审核。
/// 所有端点先校验登录会话与 ASSET_VIEW 权限，再在查询服务内叠加数据范围。
/// </summary>
public static class AssetVerificationEndpoints
{
    public static IEndpointRouteBuilder MapAssetVerificationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/asset-verification/summary", GetSummaryAsync);
        app.MapGet("/api/asset-verification/tasks", ListTasksAsync);
        app.MapGet("/api/asset-verification/tasks/{id:long}", GetDetailAsync);
        app.MapGet("/api/asset-verification/history", GetHistoryAsync);
        app.MapGet("/api/asset-verification/monthly", ListMonthlyAsync);
        app.MapGet("/api/asset-verification/operator-context", GetOperatorContextAsync);
        app.MapPost("/api/asset-verification/checks", CreateCheckAsync);
        return app;
    }

    private static async Task<IResult> GetSummaryAsync(
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ASSET_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is null)
        {
            return Unauthorized(context);
        }

        var summary = await CreateQueryService(context).GetSummaryAsync(ct);
        return Results.Json(summary);
    }

    private static async Task<IResult> ListTasksAsync(
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct,
        int? page,
        int? pageSize,
        string? status,
        string? keyword,
        string? dateFrom,
        string? dateTo)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ASSET_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        if (!TryReadDate(dateFrom, out var from) || !TryReadDate(dateTo, out var to))
        {
            return InvalidRequest(context, "日期格式应为 YYYY-MM-DD");
        }

        if (from is { } fromValue && to is { } toValue && fromValue > toValue)
        {
            return InvalidRequest(context, "开始日期不能晚于结束日期");
        }

        var result = await CreateQueryService(context).ListTasksAsync(
            userId,
            page ?? 1,
            pageSize ?? AssetVerificationQueryService.DefaultPageSize,
            status,
            keyword,
            from,
            to?.AddDays(1),
            ct);

        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> GetDetailAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ASSET_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var result = await CreateQueryService(context).GetDetailAsync(userId, id, ct);
        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(result.Value);
    }

    /// <summary>
    /// 按身份证号查询历史核查记录（快速核对页“查询历史”）。
    /// 先复用 Core 的历史判定，再按申请日期倒序取最近 20 条任务。
    /// </summary>
    private static async Task<IResult> GetHistoryAsync(
        HttpContext context,
        INewPermissionService permissions,
        IAssetVerificationService verification,
        IDatabaseService database,
        CancellationToken ct,
        string? idCard)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ASSET_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is null)
        {
            return Unauthorized(context);
        }

        var normalized = (idCard ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return InvalidRequest(context, "请提供身份证号");
        }

        var history = await verification.GetHistoryByIdCardAsync(normalized, ct);
        if (history is null)
        {
            return Results.Json(new { tasks = Array.Empty<object>() });
        }

        const string sql = @"
            SELECT id, batch_id, applicant_name, application_date, family_address, status
            FROM nc_biz_asset_checks
            WHERE applicant_id_card = $1 AND deleted_at IS NULL
            ORDER BY application_date DESC, id DESC
            LIMIT 20";

        var rowsResult = await database.QueryAsync<AssetHistoryRow>(sql, ct, normalized);
        if (rowsResult.IsFailure)
        {
            return ApiErrors.FromResult(context, rowsResult);
        }

        var tasks = (rowsResult.Value ?? [])
            .Select(row => new
            {
                id = row.Id,
                batchId = row.BatchId ?? string.Empty,
                applicantName = row.ApplicantName ?? string.Empty,
                applicationDate = row.ApplicationDate,
                familyAddress = row.FamilyAddress ?? string.Empty,
                status = row.Status ?? string.Empty,
                statusText = string.IsNullOrWhiteSpace(row.Status)
                    ? string.Empty
                    : AssetCheckStatusConstants.GetDescription(row.Status),
            })
            .ToList();

        return Results.Json(new { tasks });
    }

    private static async Task<IResult> ListMonthlyAsync(
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct,
        int? year,
        int? month,
        int? page,
        int? pageSize,
        string? status,
        string? keyword)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ASSET_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        if (year is < 1900 or > 9999)
        {
            return InvalidRequest(context, "年份参数无效");
        }

        if (month is < 1 or > 12)
        {
            return InvalidRequest(context, "月份参数应为 1-12");
        }

        var result = await CreateQueryService(context).ListMonthlyAsync(
            userId,
            year,
            month,
            page ?? 1,
            pageSize ?? AssetVerificationQueryService.DefaultPageSize,
            status,
            keyword,
            ct);

        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> GetOperatorContextAsync(
        HttpContext context,
        INewPermissionService permissions,
        IDatabaseService database,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ASSET_CREATE, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var result = await LoadOperatorContextAsync(database, userId, ct);
        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> CreateCheckAsync(
        QuickAssetCheckCreateRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        IAssetVerificationService verification,
        IDatabaseService database,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ASSET_CREATE, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        if (request is null)
        {
            return InvalidRequest(context, "请求体不能为空");
        }

        var operatorResult = await LoadOperatorContextAsync(database, userId, ct);
        if (operatorResult.IsFailure)
        {
            return ApiErrors.FromResult(context, operatorResult);
        }

        var operatorInfo = operatorResult.Value;
        if (operatorInfo.TownId is null or <= 0)
        {
            return InvalidRequest(context, "当前账号未配置经办乡镇，请先在“经办机构管理”补齐区划");
        }

        var applicants = (request.Applicants ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.Name) || !string.IsNullOrWhiteSpace(item.IdCard))
            .ToList();
        if (applicants.Count == 0)
        {
            return InvalidRequest(context, "请至少录入一名申请人信息");
        }

        var head = applicants.FirstOrDefault(item => item.IsHead) ?? applicants[0];
        head.IsHead = true;
        var headIdCard = (head.IdCard ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(head.Name))
        {
            return InvalidRequest(context, "请填写申请人姓名");
        }
        if (headIdCard.Length != 18)
        {
            return InvalidRequest(context, "申请人身份证号格式错误：必须为 18 位");
        }

        foreach (var applicant in applicants)
        {
            if (string.IsNullOrWhiteSpace(applicant.Name) || string.IsNullOrWhiteSpace(applicant.IdCard))
            {
                return InvalidRequest(context, "授权人姓名和身份证号不能为空");
            }
            if (applicant.IdCard.Trim().Length != 18)
            {
                return InvalidRequest(context, $"授权人“{applicant.Name}”身份证号格式错误：必须为 18 位");
            }
            if (string.IsNullOrWhiteSpace(applicant.Relationship))
            {
                return InvalidRequest(context, $"授权人“{applicant.Name}”未选择家庭关系");
            }
        }

        var community = (request.Community ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(community))
        {
            return InvalidRequest(context, "请选择村社区");
        }

        var village = operatorInfo.Villages.FirstOrDefault(item =>
                string.Equals(item.Code, community, StringComparison.OrdinalIgnoreCase))
            ?? operatorInfo.Villages.FirstOrDefault(item =>
                string.Equals(item.Name, community, StringComparison.Ordinal));
        if (village is null)
        {
            return InvalidRequest(context, "村社区不在当前经办乡镇范围内");
        }

        var addressDetail = (request.AddressDetail ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(addressDetail))
        {
            return InvalidRequest(context, "请填写详细地址");
        }

        var reason = ToApplicationReasonCode(request.ApplicationReason);
        if (string.IsNullOrWhiteSpace(reason))
        {
            return InvalidRequest(context, "请选择申请原因");
        }

        if (request.IsAgent
            && (string.IsNullOrWhiteSpace(request.AgentName)
                || string.IsNullOrWhiteSpace(request.AgentIdCard)
                || string.IsNullOrWhiteSpace(request.AgentRelationship)))
        {
            return InvalidRequest(context, "已勾选委托代理，请填写代理人姓名、身份证号和关系");
        }

        var applicationDate = TryParseDate(request.ApplicationDate) ?? DateTime.Today;
        var duplicateResult = await verification.CheckDuplicateAsync(
            headIdCard, applicationDate.Year, applicationDate.Month, ct);
        if (duplicateResult.IsFailure)
        {
            return ApiErrors.FromResult(context, duplicateResult);
        }
        if (duplicateResult.Value && !request.OverwriteExisting)
        {
            return ApiErrors.Write(
                context,
                StatusCodes.Status409Conflict,
                "ASSET_CHECK_DUPLICATE",
                $"申请人“{head.Name}”在本业务月度已有核查记录，是否覆盖提交？");
        }

        var batchId = Guid.NewGuid().ToString("N");
        var familyAddress = AddressResolver.BuildAddressFromText(
            operatorInfo.CountyName,
            operatorInfo.TownName,
            village.Name,
            addressDetail);

        var submitRequest = new QuickAssetCheckSubmitRequest
        {
            ApplicationDate = applicationDate,
            FamilyAddress = familyAddress,
            ApplicationReason = reason,
            Community = village.Code,
            ContactPhone = (request.ContactPhone ?? string.Empty).Trim(),
            BatchId = batchId,
            VerificationYear = applicationDate.Year,
            VerificationMonth = applicationDate.Month,
            IsAgent = request.IsAgent,
            AgentName = (request.AgentName ?? string.Empty).Trim(),
            AgentIdType = string.IsNullOrWhiteSpace(request.AgentIdType)
                ? IdTypeConstants.ResidentIdCardCode
                : request.AgentIdType.Trim(),
            AgentIdCard = (request.AgentIdCard ?? string.Empty).Trim(),
            AgentRelationship = (request.AgentRelationship ?? string.Empty).Trim(),
            OperatorUserId = userId,
            OperatorName = operatorInfo.OperatorName,
            OperatorAccount = operatorInfo.OperatorAccount,
            OperatorUnitName = operatorInfo.UnitName,
            Applicants = applicants.Select(item => new AssetCheckItemDto
            {
                BatchId = batchId,
                ApplicantName = (item.Name ?? string.Empty).Trim(),
                ApplicantIdType = string.IsNullOrWhiteSpace(item.IdType)
                    ? IdTypeConstants.ResidentIdCardCode
                    : item.IdType.Trim(),
                ApplicantIdCard = (item.IdCard ?? string.Empty).Trim(),
                Relationship = ToRelationshipCode(item.Relationship),
                IsHead = item.IsHead,
                ContactPhone = (request.ContactPhone ?? string.Empty).Trim(),
                IsAgent = request.IsAgent,
                AgentName = (request.AgentName ?? string.Empty).Trim(),
                AgentIdCard = (request.AgentIdCard ?? string.Empty).Trim(),
                AgentRelationship = (request.AgentRelationship ?? string.Empty).Trim(),
            }).ToList(),
        };

        var submitResult = await verification.SubmitQuickVerificationAsync(
            submitRequest, request.OverwriteExisting, ct);
        if (submitResult.IsFailure)
        {
            return ApiErrors.FromResult(context, submitResult);
        }

        return Results.Json(new
        {
            batchId,
            count = submitResult.Value,
        });
    }

    private static async Task<Result<OperatorContext>> LoadOperatorContextAsync(
        IDatabaseService database,
        int userId,
        CancellationToken ct)
    {
        const string operatorSql = @"
            SELECT u.full_name AS operator_name,
                   u.username AS operator_account,
                   COALESCE(o.name, '') AS unit_name,
                   o.town_id,
                   o.county_id,
                   COALESCE(t.town_name, '') AS town_name,
                   COALESCE(c.county_name, '') AS county_name
            FROM nc_sys_users u
            LEFT JOIN nc_sys_organizations o ON o.id = u.organization_id
            LEFT JOIN nc_regions_towns t ON t.id = o.town_id
            LEFT JOIN nc_regions_counties c ON c.id = o.county_id
            WHERE u.id = $1
            LIMIT 1";

        var operatorResult = await database.QuerySingleAsync<OperatorRow>(operatorSql, ct, userId);
        if (operatorResult.IsFailure)
        {
            return Result.Failure<OperatorContext>(
                operatorResult.ErrorCode!, operatorResult.Message!);
        }

        var row = operatorResult.Value;
        if (row is null)
        {
            return Result.Failure<OperatorContext>(
                ErrorCodes.RECORD_NOT_FOUND, "当前登录用户不存在");
        }

        var villages = new List<VillageOption>();
        if (row.TownId is > 0)
        {
            const string villageSql = @"
                SELECT id, village_name, village_code
                FROM nc_regions_villages
                WHERE town_id = $1 AND is_active = TRUE
                ORDER BY sort_order, id";
            var villageResult = await database.QueryAsync<VillageRow>(villageSql, ct, row.TownId.Value);
            if (villageResult.IsFailure)
            {
                return Result.Failure<OperatorContext>(
                    villageResult.ErrorCode!, villageResult.Message!);
            }

            villages = (villageResult.Value ?? [])
                .Select(item => new VillageOption
                {
                    Id = item.Id,
                    Name = item.VillageName,
                    Code = item.VillageCode,
                })
                .ToList();
        }

        return Result.Success(new OperatorContext
        {
            TownId = row.TownId,
            CountyId = row.CountyId,
            TownName = row.TownName,
            CountyName = row.CountyName,
            UnitName = row.UnitName,
            OperatorName = row.OperatorName,
            OperatorAccount = row.OperatorAccount,
            Villages = villages,
        });
    }

    private static DateTime? TryParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return DateTime.TryParseExact(
            raw.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : null;
    }

    private static string ToRelationshipCode(string? value) => value?.Trim() switch
    {
        "本人/户主" or "本人" or "户主" or "Head" => "Head",
        "配偶" or "Spouse" => "Spouse",
        "儿子" or "子/婿" or "Son" => "Son",
        "女儿" or "女/媳" or "Daughter" => "Daughter",
        "孙子" or "Grandson" => "Grandson",
        "孙女" or "Granddaughter" => "Granddaughter",
        "父亲" or "Father" => "Father",
        "母亲" or "Mother" => "Mother",
        "兄弟" or "Brother" => "Brother",
        "姐妹" or "Sister" => "Sister",
        "其他" or "Other" => "Other",
        var other => other ?? string.Empty,
    };

    private static string ToApplicationReasonCode(string? value) => value?.Trim() switch
    {
        "收入低" or "LowIncome" => "LowIncome",
        "疾病" or "Illness" => "Illness",
        "灾害" or "Disaster" => "Disaster",
        "残疾" or "Disability" => "Disability",
        "教育" or "Education" => "Education",
        "失业" or "Unemployment" => "Unemployment",
        "失地" or "LandLoss" => "LandLoss",
        "事故" or "Accident" => "Accident",
        "其他" or "Other" => "Other",
        var other => other ?? string.Empty,
    };

    private sealed class OperatorRow
    {
        public string OperatorName { get; set; } = string.Empty;
        public string OperatorAccount { get; set; } = string.Empty;
        public string UnitName { get; set; } = string.Empty;
        public int? TownId { get; set; }
        public int? CountyId { get; set; }
        public string TownName { get; set; } = string.Empty;
        public string CountyName { get; set; } = string.Empty;
    }

    private sealed class VillageRow
    {
        public int Id { get; set; }
        public string VillageName { get; set; } = string.Empty;
        public string VillageCode { get; set; } = string.Empty;
    }

    private sealed class AssetHistoryRow
    {
        public long Id { get; set; }
        public string? BatchId { get; set; }
        public string? ApplicantName { get; set; }
        public DateTime ApplicationDate { get; set; }
        public string? FamilyAddress { get; set; }
        public string? Status { get; set; }
    }

    private sealed class VillageOption
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
    }

    private sealed class OperatorContext
    {
        public int? TownId { get; set; }
        public int? CountyId { get; set; }
        public string TownName { get; set; } = string.Empty;
        public string CountyName { get; set; } = string.Empty;
        public string UnitName { get; set; } = string.Empty;
        public string OperatorName { get; set; } = string.Empty;
        public string OperatorAccount { get; set; } = string.Empty;
        public List<VillageOption> Villages { get; set; } = [];
    }

    public sealed class QuickAssetCheckCreateRequest
    {
        public string? ApplicationDate { get; set; }
        public string? Community { get; set; }
        public string? AddressDetail { get; set; }
        public string? ContactPhone { get; set; }
        public string? ApplicationReason { get; set; }
        public bool IsAgent { get; set; }
        public string? AgentName { get; set; }
        public string? AgentIdType { get; set; }
        public string? AgentIdCard { get; set; }
        public string? AgentRelationship { get; set; }
        public bool OverwriteExisting { get; set; }
        public List<QuickAssetCheckApplicantRequest>? Applicants { get; set; }
    }

    public sealed class QuickAssetCheckApplicantRequest
    {
        public string? Name { get; set; }
        public string? IdType { get; set; }
        public string? IdCard { get; set; }
        public string? Relationship { get; set; }
        public bool IsHead { get; set; }
    }

    /// <summary>
    /// Program.cs 未注册本模块查询服务（共享文件不在本批改动范围），
    /// 依赖均已注册，这里按请求从 RequestServices 解析后组合。
    /// </summary>
    private static AssetVerificationQueryService CreateQueryService(HttpContext context)
    {
        var services = context.RequestServices;
        return new AssetVerificationQueryService(
            services.GetRequiredService<IDatabaseService>(),
            services.GetRequiredService<IStatisticsService>(),
            services.GetRequiredService<DataScopeFilterBuilder>());
    }

    private static bool TryReadDate(string? raw, out DateTime? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (DateTime.TryParseExact(
                raw.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    private static IResult InvalidRequest(HttpContext context, string message)
        => ApiErrors.Write(
            context,
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            message);

    private static IResult Unauthorized(HttpContext context)
        => ApiErrors.Write(
            context,
            StatusCodes.Status401Unauthorized,
            ApiErrorCodes.AuthRequired,
            "请先登录");
}
