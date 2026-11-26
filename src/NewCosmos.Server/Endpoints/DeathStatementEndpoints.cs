using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.DeathStatement;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.ArchiveManagement;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 死亡情况说明接口（村内死亡申报 → 身份八源聚合 → 数据提醒 → 联动 → 文书出具）。
/// <para>
/// 权限：读 <see cref="PermissionCodes.ARCHIVE_VIEW"/>；写 <see cref="PermissionCodes.ARCHIVE_MANAGE"/>；
/// 打印/保存 <see cref="PermissionCodes.PRINT_LOWINCOMEPROOF"/>。
/// 业务口径全部复用 Core <see cref="IDeathStatementService"/> 与
/// <see cref="DeathStatementFieldAssembler"/>，端点只做范围解析、契约映射与打印入队。
/// </para>
/// </summary>
public static class DeathStatementEndpoints
{
    private const string Prefix = "/api/death-statements";
    private const string OutputCategory = "死亡情况说明";

    public static IEndpointRouteBuilder MapDeathStatementEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet($"{Prefix}/scope", GetScopeAsync);
        app.MapGet($"{Prefix}/search", SearchAsync);
        app.MapGet($"{Prefix}/context", GetContextAsync);
        app.MapPost($"{Prefix}/count-recent", CountRecentAsync);
        app.MapPost($"{Prefix}/declare", DeclareAsync);
        app.MapPost($"{Prefix}/{{id:long}}/revoke", RevokeAsync);
        app.MapPost($"{Prefix}/{{id:long}}/archive", ArchiveAsync);
        app.MapGet($"{Prefix}/linkage-tasks", GetLinkageTasksAsync);
        app.MapGet($"{Prefix}/linkage-count", GetLinkageCountAsync);
        app.MapPost($"{Prefix}/jobs", CreateJobAsync);
        return app;
    }

    // ───────────────────────────── 范围 / 查询 ─────────────────────────────

    /// <summary>GET /api/death-statements/scope → 当前账号本村/本镇范围与降级说明。</summary>
    private static async Task<IResult> GetScopeAsync(
        HttpContext context,
        IDeathStatementService deathStatements,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var scopeResult = await deathStatements.ResolveScopeAsync(ct);
        if (scopeResult.IsFailure)
        {
            return MapResult(context, scopeResult);
        }

        var scope = scopeResult.Value;
        var degradedReason = scope.IsTownScope
            ? $"当前组织未绑定村/社区，已按全镇范围查询：{scope.TownName}。如需按村口径，请到「经办机构管理」补齐区划。"
            : null;

        return Results.Json(new DeathStatementScopeResponse(
            Village: scope.VillageName,
            Town: scope.TownName,
            Degraded: scope.IsTownScope,
            DegradedReason: degradedReason,
            VillageId: scope.VillageId,
            VillageCoreName: scope.VillageCoreName,
            TownId: scope.TownId,
            ScopeLabel: scope.ScopeLabel,
            OrgName: scope.OrgName,
            OrgPhone: scope.OrgPhone));
    }

    /// <summary>
    /// GET /api/death-statements/search?keyword=&amp;from=&amp;to=&amp;showAll=
    /// 三源搜索：申报台账 + 档案死亡登记候选 + 历史导入台账候选（仅关键字非空时）。
    /// 日期默认最近 12 个月（与旧版页面默认一致）。
    /// </summary>
    private static async Task<IResult> SearchAsync(
        HttpContext context,
        IDeathStatementService deathStatements,
        INewPermissionService permissions,
        CancellationToken ct,
        string? keyword,
        DateTime? from,
        DateTime? to,
        bool? showAll)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var scopeResult = await deathStatements.ResolveScopeAsync(ct);
        if (scopeResult.IsFailure)
        {
            return MapResult(context, scopeResult);
        }

        var fromDate = (from ?? DateTime.Today.AddMonths(-12)).Date;
        var toDate = (to ?? DateTime.Today).Date;
        if (toDate < fromDate)
        {
            return InvalidRequest(context, "结束日期不能早于开始日期");
        }

        var result = await deathStatements.SearchAsync(
            scopeResult.Value, keyword, fromDate, toDate, showAll ?? false, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(new DeathStatementListResponse(result.Value));
    }

    /// <summary>
    /// GET /api/death-statements/context?declarationId=&amp;idCard=
    /// 旧版 DeathStatementContext：死者信息 + 身份八源 + 13 条提醒 + 联动卡片。
    /// declarationId 优先；仅 idCard 时按手工录入口径聚合。
    /// </summary>
    private static async Task<IResult> GetContextAsync(
        HttpContext context,
        IDeathStatementService deathStatements,
        INewPermissionService permissions,
        CancellationToken ct,
        long? declarationId,
        string? idCard)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var scopeResult = await deathStatements.ResolveScopeAsync(ct);
        if (scopeResult.IsFailure)
        {
            return MapResult(context, scopeResult);
        }

        var result = await deathStatements.BuildContextAsync(
            scopeResult.Value, declarationId, idCard, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(result.Value);
    }

    /// <summary>POST /api/death-statements/count-recent body {idCard, deathDate} → {count}（±3 天重复申报窗口）。</summary>
    private static async Task<IResult> CountRecentAsync(
        DeathRecentCountRequest? request,
        HttpContext context,
        IDeathStatementService deathStatements,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.IdCard))
        {
            return InvalidRequest(context, "请填写死者身份证号");
        }

        if (!DateTime.TryParse(request.DeathDate, out var deathDate))
        {
            return InvalidRequest(context, "死亡日期格式不正确");
        }

        var result = await deathStatements.CountRecentValidAsync(
            request.IdCard.Trim(), deathDate, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(new DeathRecentCountResponse(result.Value));
    }

    // ───────────────────────────── 申报状态机 ─────────────────────────────

    /// <summary>
    /// POST /api/death-statements/declare
    /// body 兼容 Web 前端字段（personName/reporterType/discovererName/businessMatters/disposalType…）
    /// 与旧版 <see cref="DeathDeclareRequest"/> 字段名（name/declaredByName/reporterName…）。
    /// → {id, declarationId, status}。
    /// </summary>
    private static async Task<IResult> DeclareAsync(
        DeathDeclareApiRequest? request,
        HttpContext context,
        IDeathStatementService deathStatements,
        INewPermissionService permissions,
        ILoggerService logger,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_MANAGE, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (request is null)
        {
            return InvalidRequest(context, "请求体不能为空");
        }

        var scopeResult = await deathStatements.ResolveScopeAsync(ct);
        if (scopeResult.IsFailure)
        {
            return MapResult(context, scopeResult);
        }

        var scope = scopeResult.Value;
        var declare = ToDeclareRequest(request);

        // 前端不传 applicationId：按身份证补关联档案（旧版 VM 从已汇总上下文带入）。
        if (declare.ApplicationId is null && !string.IsNullOrWhiteSpace(declare.IdCard))
        {
            var archiveContext = await deathStatements.BuildContextAsync(
                scope, null, declare.IdCard, ct);
            if (archiveContext.IsSuccess)
            {
                declare.ApplicationId = archiveContext.Value.ApplicationId;
            }
            else
            {
                logger.Warn($"死亡申报补关联档案失败（application_id 留空）: {archiveContext.Message}");
            }
        }

        // 服务端以当前组织范围覆盖村/镇归属，并执行申报前置校验（含遗体处置/矛盾纠纷硬门）。
        var result = await deathStatements.DeclareAsync(scope, declare, ct);
        if (result.IsFailure)
        {
            return MapResult(context, result);
        }

        var info = result.Value;
        return Results.Json(new DeathDeclareResponse(
            info.Id, info.Id, DeathDeclarationConstants.StatusPending, info.RecentValidCount));
    }

    /// <summary>POST /api/death-statements/{id}/revoke body {reason} → 204。</summary>
    private static async Task<IResult> RevokeAsync(
        long id,
        DeathRevokeRequest? request,
        HttpContext context,
        IDeathStatementService deathStatements,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_MANAGE, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (id <= 0)
        {
            return InvalidRequest(context, "申报 ID 无效");
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Reason))
        {
            return InvalidRequest(context, "请填写作废原因");
        }

        var result = await deathStatements.RevokeAsync(id, request.Reason.Trim(), ct);
        return result.IsFailure ? MapResult(context, result) : Results.NoContent();
    }

    /// <summary>POST /api/death-statements/{id}/archive → 204。</summary>
    private static async Task<IResult> ArchiveAsync(
        long id,
        HttpContext context,
        IDeathStatementService deathStatements,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_MANAGE, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (id <= 0)
        {
            return InvalidRequest(context, "申报 ID 无效");
        }

        var result = await deathStatements.ArchiveAsync(id, ct);
        return result.IsFailure ? MapResult(context, result) : Results.NoContent();
    }

    // ───────────────────────────── 联动待办 ─────────────────────────────

    /// <summary>GET /api/death-statements/linkage-tasks?domains=ElderlyStop,College → {items}。</summary>
    private static async Task<IResult> GetLinkageTasksAsync(
        HttpContext context,
        IDeathStatementService deathStatements,
        INewPermissionService permissions,
        CancellationToken ct,
        string? domains)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var scopeResult = await deathStatements.ResolveScopeAsync(ct);
        if (scopeResult.IsFailure)
        {
            return MapResult(context, scopeResult);
        }

        var onlyDomains = string.IsNullOrWhiteSpace(domains)
            ? null
            : domains.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(domain => domain.Length > 0)
                .ToArray();

        var result = await deathStatements.GetLinkageTasksAsync(scopeResult.Value, onlyDomains, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(new DeathLinkageTasksResponse(result.Value));
    }

    /// <summary>GET /api/death-statements/linkage-count → {count}（首页横幅；失败按 0 不显示）。</summary>
    private static async Task<IResult> GetLinkageCountAsync(
        HttpContext context,
        IDeathStatementService deathStatements,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var scopeResult = await deathStatements.ResolveScopeAsync(ct);
        if (scopeResult.IsFailure)
        {
            return MapResult(context, scopeResult);
        }

        var result = await deathStatements.GetLinkageTaskCountAsync(scopeResult.Value, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(new DeathLinkageCountResponse(result.Value));
    }

    // ───────────────────────────── 文书任务 ─────────────────────────────

    /// <summary>
    /// POST /api/death-statements/jobs
    /// body: { declarationId, mode:"preview|print|save", copies?, isDuplex?, printerBindingId?, printerName? }
    /// 模板按 <see cref="DocumentTemplateNames.DeathStatement"/>（证明_死亡情况说明，docx）解析；
    /// 字段由 <see cref="DeathStatementFieldAssembler"/> 装配；单 part 的 TemplateBatch 任务。
    /// ⚠️ 三联打印（村留存/镇政府/卫生院三份不同推送单位）本期未做，仅支持单份；
    /// 后续如需三联，应生成三个 part、逐份注入 PUSH_UNIT，而不是用份数复制。
    /// </summary>
    private static async Task<IResult> CreateJobAsync(
        DeathStatementJobRequest? request,
        HttpContext context,
        IDeathStatementService deathStatements,
        INewPermissionService permissions,
        ArchivePrintQueueService queue,
        PrinterBindingService printers,
        IDatabaseService database,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.PRINT_LOWINCOMEPROOF, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetCurrentUser() is not { } user)
        {
            return Unauthorized(context);
        }

        if (request is null || request.DeclarationId is not > 0)
        {
            return InvalidRequest(context, "declarationId 必须为正整数");
        }

        var mode = (request.Mode ?? "preview").Trim().ToLowerInvariant();
        if (mode is not ("preview" or "print" or "save"))
        {
            return InvalidRequest(context, "mode 只能是 preview / print / save");
        }

        var scopeResult = await deathStatements.ResolveScopeAsync(ct);
        if (scopeResult.IsFailure)
        {
            return MapResult(context, scopeResult);
        }
        var scope = scopeResult.Value;

        // 上下文（含身份八源/提醒/联动）同时是文书字段的数据源；申报不在本村/镇范围时显式失败。
        var contextResult = await deathStatements.BuildContextAsync(
            scope, request.DeclarationId, null, ct);
        if (contextResult.IsFailure)
        {
            return MapResult(context, contextResult);
        }
        var statement = contextResult.Value;

        var templateResult = await LoadTemplateAsync(database, ct);
        if (templateResult.IsFailure)
        {
            return MapResult(context, templateResult);
        }
        var template = templateResult.Value;

        var fields = DeathStatementFieldAssembler.BuildStatementFields(BuildFieldInput(
            statement, scope, OperatorName(user)));

        // 目标打印机：与既有批量文书一致，preview/print/save 都必须固化到已绑定 Agent 设备，
        // 否则新认领协议不会领取任务。
        var targetResult = await printers.ResolveTargetAsync(
            user.Id, request.PrinterBindingId, request.PrinterName, ct);
        if (targetResult.IsFailure)
        {
            return MapResult(context, targetResult);
        }
        var target = targetResult.Value;
        if (target is null || target.DeviceId <= 0 || string.IsNullOrWhiteSpace(target.PrinterName))
        {
            return InvalidRequest(context,
                "当前账号未绑定打印机，请先在「我的打印机」中生成绑定码并完成绑定");
        }

        var (_, extension) = ResolveFileType(template.FileType);
        var period = DateTime.Now.ToString("yyyyMM");
        var fileName = BuildFileName(template.Name, statement.Name, extension);

        var batch = new PrintBatchDefinition
        {
            Mode = mode,
            Category = OutputCategory,
            Period = period,
            Parts =
            [
                new PrintBatchPart
                {
                    TemplateId = template.Id,
                    TemplateName = template.Name,
                    FileName = fileName,
                    Fields = fields,
                    TableRows = []
                }
            ]
        };

        var jobRequest = new PrintJobRequest
        {
            BusinessType = PrintBusinessTypes.DeathStatement,
            BusinessId = request.DeclarationId,
            Classification = PrintBusinessTypes.DeathStatement,
            TemplateId = null,
            TemplateName = template.Name,
            ApplicantName = statement.Name,
            ApplicantIdCard = statement.IdCard,
            PrinterName = target.PrinterName,
            TargetUserId = target.UserId,
            TargetDeviceId = target.DeviceId,
            TargetPrinter = target.PrinterName,
            Copies = Math.Clamp(request.Copies ?? 1, 1, 99),
            IsDuplex = request.IsDuplex ?? false,
            Fields = new Dictionary<string, string>(StringComparer.Ordinal),
            TableRows = [],
            RequestedBy = user.Id,
            RequestedByName = OperatorName(user),
            JobKind = PrintJobConstants.JobKindTemplateBatch,
            Batch = batch
        };

        var enqueueResult = await queue.EnqueueAsync(jobRequest, ct);
        if (enqueueResult.IsFailure)
        {
            return MapResult(context, enqueueResult);
        }

        var job = enqueueResult.Value;
        return Results.Json(new DeathStatementJobResponse(
            job.Id,
            job.JobNo,
            job.Status,
            job.JobKind,
            mode,
            template.Id,
            template.Name,
            fileName));
    }

    /// <summary>采集上下文 → 装配输入（Web 无表单层，表单侧全部取已落库申报/上下文值）。</summary>
    private static DeathStatementFieldInput BuildFieldInput(
        DeathStatementContext context, VillageScope scope, string operatorName)
        => new()
        {
            Name = context.Name,
            IdCard = context.IdCard,
            DeathReason = context.DeathReason,
            DeathCertNo = context.DeathCertNo,
            DeathPlace = context.DeathPlace,
            Relation = context.Relation,
            DeclaredRelation = context.DeclaredRelation,
            DeclaredVillage = string.IsNullOrWhiteSpace(context.VillageName) ? scope.VillageName : context.VillageName,
            // 档案无性别时按身份证推导（与旧版 ApplyContext 同口径）。
            DeathGender = string.IsNullOrWhiteSpace(context.Gender)
                ? (IdCardValidator.ExtractGender(context.IdCard) ?? string.Empty)
                : context.Gender,
            DeathEthnicity = context.Ethnicity,
            ReporterName = context.ReporterName,
            ReporterRelation = context.ReporterRelation,
            ReporterPhone = context.ReporterPhone,
            SituationDesc = context.SituationDesc,
            RemainsArrangement = context.RemainsArrangement,
            BusinessAgent = context.BusinessAgent,
            BusinessMatter = context.BusinessMatter,
            DeclaredByName = context.DeclaredByName,
            DeclaredByIdCard = context.DeclaredByIdCard,
            DeclaredByPhone = context.DeclaredByPhone,
            DeclaredByType = context.DeclaredByType,
            DeclaredAtText = context.DeclaredAtText,
            CertType = context.CertType,
            CertIssueOrg = context.CertIssueOrg,
            DispositionBranch = ResolveDispositionBranch(context),
            CremationPlace = context.CremationPlace,
            CremationCertNo = context.CremationCertNo,
            BurialPlace = context.BurialPlace,
            FamilySituation = context.FamilySituation,
            DisputeStatus = context.DisputeStatus,
            DisputeDesc = context.DisputeDesc,
            Context = context,
            Scope = scope,
            // 单份出件默认推送单位（旧版 CurrentPushUnit：镇政府联优先）
            DefaultPushUnit = BuildDefaultPushUnit(scope),
            OperatorName = operatorName
        };

    /// <summary>遗体处置分支回填：结构化字段命中 → 对应分支；仅自由文本 → 其他；全空为空。</summary>
    private static string ResolveDispositionBranch(DeathStatementContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.CremationDateText)
            || !string.IsNullOrWhiteSpace(context.CremationPlace)
            || !string.IsNullOrWhiteSpace(context.CremationCertNo))
            return DeathDeclarationConstants.DispositionCremation;

        if (!string.IsNullOrWhiteSpace(context.BurialDateText)
            || !string.IsNullOrWhiteSpace(context.BurialPlace))
            return DeathDeclarationConstants.DispositionBurial;

        if (!string.IsNullOrWhiteSpace(context.RemainsArrangement))
            return DeathDeclarationConstants.DispositionOther;

        return string.Empty;
    }

    /// <summary>单份默认推送单位：镇政府联优先，其次村留存（与旧版 CurrentPushUnit 同口径）。</summary>
    private static string BuildDefaultPushUnit(VillageScope scope)
    {
        var town = scope.TownName?.Trim() ?? string.Empty;
        if (town.Length > 0) return $"{town}人民政府";

        var village = scope.VillageName?.Trim() ?? string.Empty;
        return village.Length > 0 ? $"{village}村民委员会（本委存档）" : string.Empty;
    }

    /// <summary>按模板名精确解析《证明_死亡情况说明》（docx）。</summary>
    private static async Task<Result<DeathStatementTemplateRow>> LoadTemplateAsync(
        IDatabaseService database, CancellationToken ct)
    {
        var result = await database.QuerySingleAsync<DeathStatementTemplateRow>(
            @"SELECT id, name, file_type
              FROM nc_biz_templates
              WHERE name = $1
              ORDER BY id
              LIMIT 1",
            ct, DocumentTemplateNames.DeathStatement);

        if (result.IsFailure)
        {
            return Result.Failure<DeathStatementTemplateRow>(result.ErrorCode!, result.Message!);
        }

        if (result.Value is not { Id: > 0 } template)
        {
            return Result.Failure<DeathStatementTemplateRow>(
                ErrorCodes.NOT_FOUND,
                $"模板不存在：{DocumentTemplateNames.DeathStatement}（请到「模板管理」上传并确认名称）");
        }

        if (!string.Equals(NormalizeWordType(template.FileType), TemplateFileTypes.Word, StringComparison.Ordinal))
        {
            return Result.Failure<DeathStatementTemplateRow>(
                ErrorCodes.NOT_FOUND,
                $"模板类型不支持：{DocumentTemplateNames.DeathStatement}（{template.FileType ?? "-"}，需要 docx）");
        }

        return Result.Success(template);
    }

    private static string NormalizeWordType(string? fileType)
    {
        try
        {
            return TemplateFileTypes.Normalize(fileType);
        }
        catch (NotSupportedException)
        {
            return string.Empty;
        }
    }

    private static (string FileType, string Extension) ResolveFileType(string? fileType)
    {
        try
        {
            var normalized = TemplateFileTypes.Normalize(fileType);
            return (normalized, TemplateFileTypes.ToExtension(normalized));
        }
        catch (NotSupportedException)
        {
            return (TemplateFileTypes.Word, ".docx");
        }
    }

    private static string BuildFileName(string templateName, string personName, string extension)
    {
        var safeTemplate = string.IsNullOrWhiteSpace(templateName) ? "死亡情况说明" : templateName.Trim();
        if (safeTemplate.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
        {
            safeTemplate = Path.GetFileNameWithoutExtension(safeTemplate);
        }

        var safePerson = string.IsNullOrWhiteSpace(personName) ? "未命名" : personName.Trim();
        var raw = $"{safeTemplate}_{safePerson}";
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            raw = raw.Replace(invalid, '_');
        }
        return $"{raw}{extension}";
    }

    /// <summary>
    /// Web 前端契约 → 旧版 DeathDeclareRequest。
    /// 兼容两套字段名：前端（personName/reporterType/reporterName=申报人/discovererName=报告人/
    /// businessMatters/disposalType…）与旧版（name/declaredByName/reporterName=报告人…）。
    /// </summary>
    private static DeathDeclareRequest ToDeclareRequest(DeathDeclareApiRequest request)
    {
        // 前端形态判定：有 personName / discovererName / disposalType / businessMatters 数组任一即是。
        var frontendShape = !string.IsNullOrWhiteSpace(request.PersonName)
            || !string.IsNullOrWhiteSpace(request.DiscovererName)
            || !string.IsNullOrWhiteSpace(request.DisposalType)
            || request.BusinessMatters is { Count: > 0 };

        var declarantName = FirstNonEmpty(
            request.DeclaredByName, frontendShape ? request.ReporterName : null);
        var declarantIdCard = FirstNonEmpty(
            request.DeclaredByIdCard, frontendShape ? request.ReporterIdCard : null);
        var declarantPhone = FirstNonEmpty(
            request.DeclaredByPhone, frontendShape ? request.ReporterPhone : null);
        var declarantType = FirstNonEmpty(
            request.DeclaredByType, frontendShape ? request.ReporterType : null);
        var declarantRelation = FirstNonEmpty(
            request.DeclaredRelation, frontendShape ? request.ReporterRelation : null);

        // 前端 reporterName 是申报人、discovererName 是报告人；旧版 reporterName 是报告人。
        var reporterName = frontendShape
            ? FirstNonEmpty(request.DiscovererName, declarantName)
            : request.ReporterName;
        var reporterRelation = frontendShape
            ? FirstNonEmpty(request.DiscovererRelation, declarantRelation)
            : request.ReporterRelation;
        var reporterPhone = frontendShape
            ? FirstNonEmpty(request.DiscovererPhone, declarantPhone)
            : request.ReporterPhone;

        var deathDateTime = ParseDateTime(request.DeathDate);
        var deathTime = ParseTimeSpan(request.DeathTime) ?? deathDateTime?.TimeOfDay ?? TimeSpan.Zero;

        var businessMatter = request.BusinessMatters is { Count: > 0 }
            ? string.Join(DeathDeclarationConstants.BusinessMatterSeparator,
                request.BusinessMatters.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()))
            : (request.BusinessMatter?.Trim() ?? string.Empty);

        return new DeathDeclareRequest
        {
            Name = FirstNonEmpty(request.PersonName, request.Name) ?? string.Empty,
            IdCard = request.IdCard?.Trim() ?? string.Empty,
            DeathDate = deathDateTime?.Date ?? default,
            DeathTime = deathTime,
            DeathReason = request.DeathReason?.Trim() ?? string.Empty,
            DeathCertNo = FirstNonEmpty(request.DeathCertificateNo, request.DeathCertNo) ?? string.Empty,
            DeathPlace = request.DeathPlace?.Trim() ?? string.Empty,
            Relation = declarantRelation ?? string.Empty,
            DeclaredByName = declarantName ?? string.Empty,
            DeclaredByIdCard = declarantIdCard ?? string.Empty,
            DeclaredByPhone = declarantPhone ?? string.Empty,
            DeclaredByType = declarantType ?? string.Empty,
            ReporterName = reporterName ?? string.Empty,
            ReporterRelation = reporterRelation ?? string.Empty,
            ReporterPhone = reporterPhone ?? string.Empty,
            SituationDesc = FirstNonEmpty(request.SceneDescription, request.SituationDesc) ?? string.Empty,
            RemainsArrangement = FirstNonEmpty(request.DisposalNote, request.RemainsArrangement) ?? string.Empty,
            BusinessAgent = request.BusinessAgent?.Trim() ?? string.Empty,
            BusinessMatter = businessMatter,
            CertType = request.CertType?.Trim() ?? string.Empty,
            CertIssueOrg = request.CertIssueOrg?.Trim() ?? string.Empty,
            DispositionBranch = FirstNonEmpty(request.DisposalType, request.DispositionBranch) ?? string.Empty,
            CremationDate = ParseDate(request.CremationDate),
            CremationPlace = request.CremationPlace?.Trim() ?? string.Empty,
            CremationCertNo = request.CremationCertNo?.Trim() ?? string.Empty,
            BurialDate = ParseDate(request.BurialDate),
            BurialPlace = request.BurialPlace?.Trim() ?? string.Empty,
            FamilySituation = request.FamilySituation?.Trim() ?? string.Empty,
            DisputeStatus = FirstNonEmpty(request.DisputeType, request.DisputeStatus) ?? string.Empty,
            DisputeDesc = FirstNonEmpty(request.DisputeNote, request.DisputeDesc) ?? string.Empty,
            ApplicationId = request.ApplicationId,
            VillageId = request.VillageId,
            VillageName = FirstNonEmpty(request.Village, request.VillageName) ?? string.Empty,
            TownId = request.TownId,
            OperatorName = request.OperatorName?.Trim() ?? string.Empty,
            Channel = request.Channel?.Trim() ?? string.Empty,
            Remark = request.Remark?.Trim() ?? string.Empty
        };
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static DateTime? ParseDateTime(string? text)
        => DateTime.TryParse(text, out var value) ? value : null;

    private static DateTime? ParseDate(string? text)
        => DateTime.TryParse(text, out var value) ? value.Date : null;

    private static TimeSpan? ParseTimeSpan(string? text)
        => TimeSpan.TryParse(text, out var value) ? value : null;

    private static string OperatorName(UserDto user)
        => string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName;

    // ───────────────────────────── Result → HTTP ─────────────────────────────

    private static IResult MapResult(HttpContext context, Result result)
    {
        if (result.ErrorCode is ErrorCodes.RECORD_NOT_FOUND
            or ErrorCodes.NOT_FOUND
            or ErrorCodes.APPLICATION_NOT_FOUND)
        {
            return ApiErrors.Write(
                context, StatusCodes.Status404NotFound,
                ApiErrorCodes.NotFound, result.Message ?? "记录不存在");
        }

        if (result.ErrorCode is ErrorCodes.VALIDATION_FAILED
            or ErrorCodes.REQUIRED_FIELD_MISSING
            or ErrorCodes.INVALID_ID_CARD)
        {
            return ApiErrors.Write(
                context, StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError, result.Message ?? "请求参数校验失败");
        }

        return ApiErrors.FromResult(context, result);
    }

    private static IResult InvalidRequest(HttpContext context, string message)
        => ApiErrors.Write(
            context, StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError, message);

    private static IResult Unauthorized(HttpContext context)
        => ApiErrors.Write(
            context, StatusCodes.Status401Unauthorized,
            ApiErrorCodes.AuthRequired, "请先登录");

    // ───────────────────────────── 契约 ─────────────────────────────

    private sealed class DeathStatementTemplateRow
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? FileType { get; set; }
    }
}

/// <summary>GET /api/death-statements/scope 响应（village/town 为展示名；id 字段供前端跳转/筛选）。</summary>
public sealed record DeathStatementScopeResponse(
    string Village,
    string Town,
    bool Degraded,
    string? DegradedReason,
    int VillageId,
    string VillageCoreName,
    int? TownId,
    string ScopeLabel,
    string OrgName,
    string OrgPhone);

/// <summary>列表类响应包装（与既有端点 items 约定一致）。</summary>
public sealed record DeathStatementListResponse(IReadOnlyList<DeathSearchItem> Items);

public sealed record DeathLinkageTasksResponse(IReadOnlyList<DeathLinkageTaskItem> Items);

public sealed record DeathLinkageCountResponse(int Count);

/// <summary>POST /api/death-statements/count-recent 请求体（deathDate 兼容日期或日期时间字符串）。</summary>
public sealed record DeathRecentCountRequest(string? IdCard, string? DeathDate);

public sealed record DeathRecentCountResponse(int Count);

/// <summary>POST /api/death-statements/declare 响应。</summary>
public sealed record DeathDeclareResponse(
    long Id,
    long DeclarationId,
    string Status,
    int RecentValidCount);

/// <summary>
/// POST /api/death-statements/declare 请求体（两套字段名兼容）。
/// Web 前端：personName / reporterType / reporterName(申报人) / discovererName(报告人) /
/// businessMatters / disposalType / disposalNote / disputeType…
/// 旧版：name / declaredByName / declaredByType / reporterName(报告人) / businessMatter / disputeStatus…
/// </summary>
public sealed class DeathDeclareApiRequest
{
    // ── 前端字段 ──
    public string? PersonName { get; set; }
    public string? DeathDate { get; set; }
    public string? DeathTime { get; set; }
    public string? DeathCertificateNo { get; set; }
    public string? ReporterType { get; set; }
    public string? ReporterName { get; set; }
    public string? ReporterIdCard { get; set; }
    public string? ReporterPhone { get; set; }
    public string? ReporterRelation { get; set; }
    public string? DiscovererName { get; set; }
    public string? DiscovererRelation { get; set; }
    public string? DiscovererPhone { get; set; }
    public string? Village { get; set; }
    public List<string>? BusinessMatters { get; set; }
    public string? SceneDescription { get; set; }
    public string? DisposalType { get; set; }
    public string? DisposalNote { get; set; }
    public string? DisputeType { get; set; }
    public string? DisputeNote { get; set; }

    // ── 旧版字段（与前端同义但不同名）──
    public string? Name { get; set; }
    public string? IdCard { get; set; }
    public string? DeathReason { get; set; }
    public string? DeathCertNo { get; set; }
    public string? DeathPlace { get; set; }
    public string? Relation { get; set; }
    public string? DeclaredByName { get; set; }
    public string? DeclaredByIdCard { get; set; }
    public string? DeclaredByPhone { get; set; }
    public string? DeclaredByType { get; set; }
    public string? DeclaredRelation { get; set; }
    public string? BusinessMatter { get; set; }
    public string? SituationDesc { get; set; }
    public string? RemainsArrangement { get; set; }
    public string? DisputeStatus { get; set; }
    public string? DisputeDesc { get; set; }

    // ── 共用字段 ──
    public string? CertType { get; set; }
    public string? CertIssueOrg { get; set; }
    public string? DispositionBranch { get; set; }
    public string? CremationDate { get; set; }
    public string? CremationPlace { get; set; }
    public string? CremationCertNo { get; set; }
    public string? BurialDate { get; set; }
    public string? BurialPlace { get; set; }
    public string? FamilySituation { get; set; }
    public string? BusinessAgent { get; set; }
    public string? OperatorName { get; set; }
    public string? Channel { get; set; }
    public string? Remark { get; set; }
    public string? VillageName { get; set; }
    public long? ApplicationId { get; set; }
    public int? VillageId { get; set; }
    public int? TownId { get; set; }
}

/// <summary>POST /api/death-statements/{id}/revoke 请求体。</summary>
public sealed record DeathRevokeRequest(string? Reason);

/// <summary>POST /api/death-statements/jobs 请求体。</summary>
public sealed record DeathStatementJobRequest(
    long? DeclarationId,
    string? Mode,
    int? Copies,
    bool? IsDuplex,
    long? PrinterBindingId,
    string? PrinterName);

/// <summary>POST /api/death-statements/jobs 响应（单份 TemplateBatch 任务信息）。</summary>
public sealed record DeathStatementJobResponse(
    long Id,
    string JobNo,
    string Status,
    string JobKind,
    string Mode,
    long TemplateId,
    string TemplateName,
    string FileName);
