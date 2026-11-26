using System.Globalization;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.ElderlyBenefits;
using NewCosmos.Services.Domain.Recovery;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.Services.Domain.TempRelief;
using NewCosmos.Services.Utilities;

namespace NewCosmos.Server.Services;

/// <summary>
/// 档案制作中心 + 档案输出（Server 端）。
/// <para>
/// 职责：
/// ① 档案制作页只读数据装配（FamilyApplication / AssetVerification / TempRelief / Recovery）；
/// ② 按业务分类列出可输出模板（分类解析唯一走 <see cref="ArchiveCategoryResolver"/>）；
/// ③ 生成 TemplateBatch 打印任务（字段装配复用各业务既有 Builder/DataService）；
/// ④ 完成归档（各业务域幂等 CompleteArchive）。
/// </para>
/// <para>
/// 本服务不重算收入/分类/保障金公式，不生成 PDF，只做“取数 → 装配 → 入队”。
/// 模板适用性（土地/赡养/近亲属/变动事实）本期不在此实现；后续由 TemplateApplicability
/// 与 ChangeDocumentApplicabilityService 补入，当前返回分类命中集合。
/// </para>
/// </summary>
public sealed class ArchiveProductionDataService(
    ApplicationFormService forms,
    IApplicationService applications,
    ArchivePrintDataService archivePrintData,
    AssetCheckPrintDataService assetChecks,
    ITempReliefService tempRelief,
    IRecoveryService recovery,
    RecoveryPrintDataService recoveryPrintData,
    IElderlyApplicationService elderly,
    IBusinessTimelineService timeline,
    ArchivePrintQueueService queue,
    PrinterBindingService printers,
    IDatabaseService database)
{
    private static readonly string[] OutputModes = ["preview", "print", "save"];

    // ───────────────────────────── 1. 档案制作数据 ─────────────────────────────

    public async Task<Result<ArchiveProductionResponse>> GetProductionAsync(
        string businessType,
        long businessId,
        int userId,
        CancellationToken ct = default)
    {
        if (businessId <= 0)
        {
            return Result.Failure<ArchiveProductionResponse>(
                ErrorCodes.VALIDATION_FAILED, "businessId 必须为正整数");
        }

        return NormalizeBusinessType(businessType) switch
        {
            PrintBusinessTypes.FamilyApplication => await BuildFamilyProductionAsync(businessId, userId, ct),
            PrintBusinessTypes.AssetVerification => await BuildAssetProductionAsync(businessId, ct),
            PrintBusinessTypes.TempRelief => await BuildTempReliefProductionAsync(businessId, ct),
            PrintBusinessTypes.Recovery => await BuildRecoveryProductionAsync(businessId, ct),
            _ => Result.Failure<ArchiveProductionResponse>(
                ErrorCodes.VALIDATION_FAILED, $"不支持的业务类型: {businessType}")
        };
    }

    private async Task<Result<ArchiveProductionResponse>> BuildFamilyProductionAsync(
        long applicationId,
        int userId,
        CancellationToken ct)
    {
        // GetFormAsync 先过数据范围（越权/不存在统一 404），再读主表做摘要。
        var formResult = await forms.GetFormAsync(applicationId, userId, ct);
        if (formResult.IsFailure || formResult.Value is null)
        {
            return Result.Failure<ArchiveProductionResponse>(
                formResult.ErrorCode ?? ErrorCodes.APPLICATION_NOT_FOUND,
                formResult.Message ?? "加载申请表单失败");
        }

        var appResult = await applications.GetByIdAsync(applicationId, ct);
        if (appResult.IsFailure || appResult.Value is null)
        {
            return Result.Failure<ArchiveProductionResponse>(
                appResult.ErrorCode ?? ErrorCodes.APPLICATION_NOT_FOUND,
                appResult.Message ?? "申请不存在");
        }

        var form = formResult.Value;
        var app = appResult.Value;
        var classification = app.ClassificationResult ?? string.Empty;

        var summary = new List<ArchiveProductionSummaryItem>
        {
            new("业务类型", "社会救助申请"),
            new("申请人", app.ApplicantName ?? string.Empty),
            new("身份证", app.ApplicantIdCard ?? string.Empty),
            new("家庭人口", app.FamilySize.ToString(CultureInfo.InvariantCulture)),
            new("分类", DisplayClassification(classification))
        };

        var basic = form.Basic;
        var economy = form.Economy;
        var familyAddress = AddressResolver.BuildAddressFromText(
            app.District, app.Town, app.Community, app.Address);
        var hukouAddress = FirstNonEmpty(
            app.HukouAddress,
            JoinParts(
                basic?.HukouProvince, basic?.HukouCity, basic?.HukouDistrict,
                basic?.HukouTown, basic?.HukouVillage, basic?.HukouDetailAddress));

        var groups = new List<ArchiveProductionSidebarGroup>
        {
            CreateGroup("基本信息",
                ("申请人姓名", FirstNonEmpty(basic?.ApplicantName, app.ApplicantName)),
                ("身份证号", FirstNonEmpty(basic?.ApplicantIdCard, app.ApplicantIdCard)),
                ("联系电话", FirstNonEmpty(basic?.Phone, app.ApplicantPhone)),
                ("申请原因", FirstNonEmpty(basic?.ApplicationReason, app.ApplicationReason)),
                ("户籍类型", FirstNonEmpty(basic?.HukouType, app.HukouType)),
                ("性别", FirstNonEmpty(basic?.Gender, app.Gender)),
                ("民族", FirstNonEmpty(basic?.Ethnicity, app.Ethnicity)),
                ("婚姻状况", FirstNonEmpty(basic?.MaritalStatus, app.MaritalStatus)),
                ("文化程度", FirstNonEmpty(basic?.EducationLevel, app.EducationLevel)),
                ("申请状态", FirstNonEmpty(form.StatusText, app.Status))),

            CreateGroup("地址信息",
                ("家庭地址", familyAddress),
                ("社区", app.Community),
                ("乡镇", app.Town),
                ("区县", app.District),
                ("户籍地址", hukouAddress)),

            CreateGroup("经济信息",
                ("家庭人数", app.FamilySize.ToString(CultureInfo.InvariantCulture)),
                ("家庭总收入", FormatAmount(economy?.TotalAnnualIncome ?? app.TotalAnnualIncome)),
                ("人均收入", FormatAmount(economy?.PerCapitaAnnualIncome ?? app.PerCapitaAnnualIncome)),
                ("务工收入", FormatAmount(economy?.WorkIncome ?? app.WorkIncomeTotal)),
                ("经营收入", FormatAmount(economy?.BusinessIncome ?? app.BusinessIncomeTotal)),
                ("财产性收入", FormatAmount(economy?.PropertyIncome ?? app.PropertyIncomeTotal)),
                ("转移性收入", FormatAmount(economy?.TransferIncome ?? app.TransferIncomeTotal)),
                ("农业补贴", FormatAmount(economy?.SubsidyTotal ?? app.SubsidyTotal)),
                ("刚性支出", FormatAmount(economy?.RigidExpenditure ?? app.RigidExpenditure)),
                ("赡养费收入", FormatAmount(economy?.AlimonyIncome ?? app.AlimonyIncome))),

            CreateGroup("经办人信息",
                ("经办人", app.UpdatedBy),
                ("区县", app.District),
                ("乡镇", app.Town))
        };

        // 分类结果组：仅主表已有分类结果时展示（与旧版一致）。
        if (!string.IsNullOrWhiteSpace(classification))
        {
            groups.Insert(3, CreateGroup("分类结果",
                ("分类结果", DisplayClassification(classification)),
                ("补贴类型", app.ClassifiedSubsidyType),
                ("补贴金额", FormatAmount(app.ClassifiedSubsidyAmount)),
                ("保障金总额", FormatAmount(app.TotalGuaranteeAmount))));
        }

        return Result.Success(new ArchiveProductionResponse(
            PrintBusinessTypes.FamilyApplication,
            applicationId,
            "社会救助申请",
            FirstNonEmpty(form.StatusText, app.Status),
            summary,
            groups,
            classification,
            string.Empty));
    }

    private async Task<Result<ArchiveProductionResponse>> BuildAssetProductionAsync(
        long checkId,
        CancellationToken ct)
    {
        var payloadResult = await assetChecks.BuildAsync(checkId, ct);
        if (payloadResult.IsFailure || payloadResult.Value is null)
        {
            return Result.Failure<ArchiveProductionResponse>(
                payloadResult.ErrorCode ?? ErrorCodes.RECORD_NOT_FOUND,
                payloadResult.Message ?? "核对记录不存在");
        }

        var payload = payloadResult.Value;
        var fields = payload.Fields;
        var statusResult = await database.ExecuteScalarAsync<string?>(
            "SELECT status FROM nc_biz_asset_checks WHERE id = $1 AND deleted_at IS NULL",
            ct,
            checkId);
        var status = statusResult.IsSuccess ? statusResult.Value : null;
        var statusText = string.IsNullOrWhiteSpace(status)
            ? string.Empty
            : AssetCheckStatusConstants.GetDescription(status);

        var summary = new List<ArchiveProductionSummaryItem>
        {
            new("业务类型", "家庭经济状况核对"),
            new("申请人", GetField(fields, FieldKeys.APPLICANT_NAME)),
            new("身份证", GetField(fields, FieldKeys.APPLICANT_ID_CARD)),
            new("核查状态", statusText)
        };

        var groups = new List<ArchiveProductionSidebarGroup>
        {
            CreateGroup("基本信息",
                ("申请人姓名", GetField(fields, FieldKeys.APPLICANT_NAME)),
                ("身份证号", GetField(fields, FieldKeys.APPLICANT_ID_CARD)),
                ("证件类型", GetField(fields, FieldKeys.APPLICANT_ID_TYPE)),
                ("联系电话", GetField(fields, FieldKeys.CONTACT_PHONE)),
                ("申请原因", GetField(fields, FieldKeys.APPLICATION_REASON)),
                ("申请日期", GetField(fields, FieldKeys.APPLICATION_DATE)),
                ("核查状态", statusText)),

            CreateGroup("地址信息",
                ("家庭地址", GetField(fields, FieldKeys.FAMILY_ADDRESS)),
                ("详细地址", GetField(fields, FieldKeys.FAMILY_DETAIL_ADDRESS)),
                ("社区", GetField(fields, FieldKeys.COMMUNITY)),
                ("区县", GetField(fields, FieldKeys.DISTRICT)),
                ("乡镇", GetField(fields, FieldKeys.TOWN))),

            CreateGroup("经办人信息",
                ("经办人", GetField(fields, FieldKeys.OPERATOR_NAME)),
                ("经办单位", GetField(fields, FieldKeys.OPERATOR_UNIT)),
                ("经办单位电话", GetField(fields, FieldKeys.OPERATOR_UNIT_PHONE)),
                ("民政助理", GetField(fields, FieldKeys.CIVIL_ASSISTANT_NAME)),
                ("区县民政局", GetField(fields, FieldKeys.DISTRICT_CIVIL_BUREAU)),
                ("区县民政局电话", GetField(fields, FieldKeys.DISTRICT_CIVIL_BUREAU_PHONE)))
        };

        // 代理人组：仅确实存在代理关系时展示。
        if (string.Equals(GetField(fields, FieldKeys.IS_AGENT), "是", StringComparison.Ordinal))
        {
            groups.Insert(2, CreateGroup("代理人信息",
                ("是否委托代理", GetField(fields, FieldKeys.IS_AGENT)),
                ("代理人姓名", GetField(fields, FieldKeys.AGENT_NAME)),
                ("代理人身份证", GetField(fields, FieldKeys.AGENT_ID_CARD)),
                ("代理人证件类型", FirstNonEmpty(
                    GetField(fields, FieldKeys.AGENT_CERT_TYPE),
                    GetField(fields, FieldKeys.AGENT_ID_TYPE))),
                ("代理关系", GetField(fields, FieldKeys.AGENT_RELATION))));
        }

        // 其他字段（人数/日期/编号等）按语义无法归入上面的组，统一放入“其他信息”，不丢字段。
        var knownKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            FieldKeys.APPLICANT_NAME, FieldKeys.APPLICANT_ID_CARD, FieldKeys.APPLICANT_ID_TYPE,
            FieldKeys.CONTACT_PHONE, FieldKeys.APPLICATION_REASON, FieldKeys.APPLICATION_DATE,
            FieldKeys.FAMILY_ADDRESS, FieldKeys.FAMILY_DETAIL_ADDRESS, FieldKeys.COMMUNITY,
            FieldKeys.DISTRICT, FieldKeys.TOWN, FieldKeys.OPERATOR_NAME, FieldKeys.OPERATOR_UNIT,
            FieldKeys.OPERATOR_UNIT_PHONE, FieldKeys.CIVIL_ASSISTANT_NAME,
            FieldKeys.DISTRICT_CIVIL_BUREAU, FieldKeys.DISTRICT_CIVIL_BUREAU_PHONE,
            FieldKeys.IS_AGENT, FieldKeys.AGENT_NAME, FieldKeys.AGENT_ID_CARD,
            FieldKeys.AGENT_CERT_TYPE, FieldKeys.AGENT_ID_TYPE, FieldKeys.AGENT_RELATION
        };
        var otherFields = fields
            .Where(pair => !knownKeys.Contains(pair.Key))
            .Select(pair => (Label: pair.Key, Value: (string?)pair.Value))
            .ToArray();
        if (otherFields.Length > 0)
        {
            groups.Add(CreateGroup("其他信息", otherFields));
        }

        return Result.Success(new ArchiveProductionResponse(
            PrintBusinessTypes.AssetVerification,
            checkId,
            "家庭经济状况核对",
            statusText,
            summary,
            groups,
            ClassificationConstants.AssetVerification,
            string.Empty));
    }

    private async Task<Result<ArchiveProductionResponse>> BuildTempReliefProductionAsync(
        long applicationId,
        CancellationToken ct)
    {
        var appResult = await tempRelief.GetByIdAsync(applicationId, ct);
        if (appResult.IsFailure || appResult.Value is null)
        {
            return Result.Failure<ArchiveProductionResponse>(
                appResult.ErrorCode ?? ErrorCodes.APPLICATION_NOT_FOUND,
                appResult.Message ?? "临时救助申请不存在");
        }

        var app = appResult.Value;
        var statusText = TempReliefConstants.GetStatusName(app.Status);
        var categoryText = TempReliefConstants.GetCategoryDisplayName(
            app.ReliefType, app.PopulationStatus, app.PaymentChannel);

        var summary = new List<ArchiveProductionSummaryItem>
        {
            new("业务类型", "临时救助"),
            new("申请人", app.ApplicantName ?? string.Empty),
            new("身份证", app.ApplicantIdCard ?? string.Empty),
            new("家庭人口", app.FamilySize?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
            new("分类", categoryText)
        };

        var groups = new List<ArchiveProductionSidebarGroup>
        {
            CreateGroup("基本信息",
                ("申请编号", app.ApplicationNo),
                ("申请人姓名", app.ApplicantName),
                ("身份证号", app.ApplicantIdCard),
                ("性别", app.Gender),
                ("年龄", app.Age?.ToString(CultureInfo.InvariantCulture)),
                ("联系电话", app.Phone),
                ("救助类型", TempReliefConstants.GetReliefTypeName(app.ReliefType)),
                ("人员状态", TempReliefConstants.GetPopulationStatusName(app.PopulationStatus)),
                ("支付渠道", TempReliefConstants.GetPaymentChannelName(
                    TempReliefConstants.ResolvePaymentChannel(app.ReliefType, app.PaymentChannel))),
                ("申请状态", statusText)),

            CreateGroup("地址信息",
                ("家庭地址", app.FamilyAddress),
                ("户籍地址", app.HukouAddress),
                ("乡镇", app.Town),
                ("村", app.Village)),

            CreateGroup("救助信息",
                ("家庭人口", app.FamilySize?.ToString(CultureInfo.InvariantCulture)),
                ("家庭类别", app.FamilyCategory),
                ("政策享受", app.PolicyEnjoyed),
                ("困难类型", app.DifficultyType),
                ("困难原因", app.DifficultyReason),
                ("确认金额", FormatAmount(app.ConfirmAmount)),
                ("公示开始", FormatDate(app.PublicizeStartDate)),
                ("公示结束", FormatDate(app.PublicizeEndDate)),
                ("上报单位", app.ReportUnit),
                ("上报时间", FormatDate(app.ReportTime))),

            CreateGroup("经办人信息",
                ("受理人", app.AcceptancePerson),
                ("上报单位", app.ReportUnit))
        };

        return Result.Success(new ArchiveProductionResponse(
            PrintBusinessTypes.TempRelief,
            applicationId,
            "临时救助",
            statusText,
            summary,
            groups,
            app.ReliefType,
            string.Empty));
    }

    private async Task<Result<ArchiveProductionResponse>> BuildRecoveryProductionAsync(
        long recordId,
        CancellationToken ct)
    {
        var recordResult = await recovery.GetRecoveryRecordByIdAsync(recordId, ct);
        if (recordResult.IsFailure || recordResult.Value is null)
        {
            return Result.Failure<ArchiveProductionResponse>(
                recordResult.ErrorCode ?? ErrorCodes.RECORD_NOT_FOUND,
                recordResult.Message ?? "追缴记录不存在");
        }

        var record = recordResult.Value;
        var statusText = record.StatusDisplay;

        var summary = new List<ArchiveProductionSummaryItem>
        {
            new("业务类型", "后补追缴"),
            new("追缴人员", record.PersonName ?? string.Empty),
            new("身份证", record.IdCard ?? string.Empty),
            new("应追缴金额", FormatAmount(record.RecoveryAmount)),
            new("状态", statusText)
        };

        var groups = new List<ArchiveProductionSidebarGroup>
        {
            CreateGroup("追缴人员",
                ("姓名", record.PersonName),
                ("身份证", record.IdCard),
                ("来源类型", RecoveryConstants.GetSourceTypeDisplayName(record.SourceType ?? string.Empty)),
                ("追缴理由", record.RecoveryReason)),

            CreateGroup("追缴信息",
                ("应追缴金额", FormatAmount(record.RecoveryAmount)),
                ("已追缴金额", FormatAmount(record.RecoveredAmount)),
                ("追缴月数", record.RecoveryMonths.ToString(CultureInfo.InvariantCulture)),
                ("起始月", record.StartMonth),
                ("终止月", record.EndMonth),
                ("金额来源", RecoveryConstants.GetAmountSourceDisplayName(record.AmountSource)),
                ("金额说明", record.AmountNote)),

            CreateGroup("状态信息",
                ("状态", statusText),
                ("录入模式", string.Equals(record.InputMode, RecoveryConstants.INPUT_MODE_MANUAL, StringComparison.Ordinal)
                    ? "手工录入"
                    : "搜索录入"),
                ("创建人", record.CreatedBy),
                ("创建时间", FormatDateTime(record.CreatedAt)))
        };

        return Result.Success(new ArchiveProductionResponse(
            PrintBusinessTypes.Recovery,
            recordId,
            "后补追缴",
            statusText,
            summary,
            groups,
            RecoveryPrintDataService.Category,
            string.Empty));
    }

    // ───────────────────────────── 2. 输出模板 ─────────────────────────────

    public async Task<Result<List<ArchiveOutputTemplateResponse>>> GetOutputTemplatesAsync(
        string businessType,
        long? businessId,
        string? classification,
        string? operationOverride,
        CancellationToken ct = default)
    {
        var normalized = NormalizeBusinessType(businessType);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Result.Failure<List<ArchiveOutputTemplateResponse>>(
                ErrorCodes.VALIDATION_FAILED, "businessType 不能为空");
        }

        // 未显式传分类时按业务主键补一次（FamilyApplication 的停保/新增、TempRelief 的大额/小额
        // 都决定分类集，前端漏传时不至于退化成错误模板集）。
        var resolvedClassification = classification;
        if (string.IsNullOrWhiteSpace(resolvedClassification) && businessId is > 0)
        {
            resolvedClassification = await TryResolveClassificationAsync(normalized, businessId.Value, ct);
        }

        var categories = ArchiveCategoryResolver.GetRecordCategories(
            normalized, resolvedClassification, operationOverride);

        // 注意：本期只做分类命中集合；土地二选一/赡养人/近亲属/变动事实等适用性
        // 后续由 TemplateApplicability + ChangeDocumentApplicabilityService 补入。
        var templatesResult = await queue.ListTemplatesAsync(categories, ct);
        if (templatesResult.IsFailure)
        {
            return Result.Failure<List<ArchiveOutputTemplateResponse>>(
                templatesResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                templatesResult.Message ?? "加载模板失败");
        }

        var templates = SortTemplates(templatesResult.Value ?? [])
            .Select(template => new ArchiveOutputTemplateResponse(
                template.Id,
                template.Name,
                NormalizeFileType(template.FileType),
                template.DocType ?? string.Empty,
                template.SortOrder,
                DefaultSelected: true))
            .ToList();

        return Result.Success(templates);
    }

    // ───────────────────────────── 3. 生成输出任务 ─────────────────────────────

    public async Task<Result<ArchiveOutputJobResponse>> CreateOutputJobAsync(
        ArchiveOutputJobRequest request,
        UserDto user,
        CancellationToken ct = default)
    {
        var normalized = NormalizeBusinessType(request.BusinessType);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Result.Failure<ArchiveOutputJobResponse>(
                ErrorCodes.VALIDATION_FAILED, "businessType 不能为空");
        }

        if (request.BusinessId is not > 0)
        {
            return Result.Failure<ArchiveOutputJobResponse>(
                ErrorCodes.VALIDATION_FAILED, "businessId 必须为正整数");
        }

        var mode = (request.Mode ?? string.Empty).Trim().ToLowerInvariant();
        if (!OutputModes.Contains(mode, StringComparer.Ordinal))
        {
            return Result.Failure<ArchiveOutputJobResponse>(
                ErrorCodes.VALIDATION_FAILED, "mode 只能是 preview / print / save");
        }

        var templateIds = (request.TemplateIds ?? [])
            .Where(id => id > 0)
            .Distinct()
            .ToList();
        if (templateIds.Count == 0)
        {
            return Result.Failure<ArchiveOutputJobResponse>(
                ErrorCodes.VALIDATION_FAILED, "至少选择一个模板");
        }

        var templatesResult = await LoadTemplatesByIdsAsync(templateIds, ct);
        if (templatesResult.IsFailure || templatesResult.Value is null)
        {
            return Result.Failure<ArchiveOutputJobResponse>(
                templatesResult.ErrorCode ?? ErrorCodes.VALIDATION_FAILED,
                templatesResult.Message ?? "模板不存在");
        }

        var contextResult = await BuildOutputContextAsync(
            normalized,
            request.BusinessId.Value,
            request.Classification,
            request.OperationOverride,
            user,
            ct);
        if (contextResult.IsFailure || contextResult.Value is null)
        {
            return Result.Failure<ArchiveOutputJobResponse>(
                contextResult.ErrorCode ?? ErrorCodes.VALIDATION_FAILED,
                contextResult.Message ?? "业务数据装配失败");
        }

        // 目标打印机：优先前端选择的绑定，其次默认绑定；与证明打印一致，任务必须固化目标。
        var targetResult = await printers.ResolveTargetAsync(
            user.Id, request.PrinterBindingId, request.PrinterName, ct);
        if (targetResult.IsFailure)
        {
            return Result.Failure<ArchiveOutputJobResponse>(
                targetResult.ErrorCode ?? ErrorCodes.VALIDATION_FAILED,
                targetResult.Message ?? "解析打印机失败");
        }

        var target = targetResult.Value;
        if (target is null || target.DeviceId <= 0 || string.IsNullOrWhiteSpace(target.PrinterName))
        {
            return Result.Failure<ArchiveOutputJobResponse>(
                ErrorCodes.VALIDATION_FAILED,
                "当前账号未绑定打印机，请先在「我的打印机」中生成绑定码并完成绑定");
        }

        var context = contextResult.Value;
        var period = DateTime.Now.ToString("yyyyMM", CultureInfo.InvariantCulture);
        var orderedTemplates = SortTemplates(templatesResult.Value);

        var batch = new PrintBatchDefinition
        {
            Mode = mode,
            Category = context.Category,
            Period = period
        };

        foreach (var template in orderedTemplates)
        {
            var (_, extension) = ResolveFileType(template.FileType);
            batch.Parts.Add(new PrintBatchPart
            {
                TemplateId = template.Id,
                TemplateName = template.Name,
                FileName = BuildFileName(template.Name, extension),
                Fields = new Dictionary<string, string>(context.Fields, StringComparer.Ordinal),
                TableRows = context.TableRows
                    .Select(row => new Dictionary<string, string>(row, StringComparer.Ordinal))
                    .ToList()
            });
        }

        var jobRequest = new PrintJobRequest
        {
            BusinessType = normalized,
            BusinessId = request.BusinessId.Value,
            Classification = context.Classification,
            TemplateId = null,
            TemplateName = null,
            ApplicantName = context.ApplicantName,
            ApplicantIdCard = context.ApplicantIdCard,
            PrinterName = target.PrinterName,
            TargetUserId = target.UserId,
            TargetDeviceId = target.DeviceId,
            TargetPrinter = target.PrinterName,
            Copies = Math.Clamp(request.Copies ?? 1, 1, 99),
            IsDuplex = request.IsDuplex ?? true,
            Fields = new Dictionary<string, string>(StringComparer.Ordinal),
            TableRows = [],
            RequestedBy = user.Id,
            RequestedByName = OperatorName(user),
            JobKind = PrintJobConstants.JobKindTemplateBatch,
            Batch = batch
        };

        var enqueueResult = await queue.EnqueueAsync(jobRequest, ct);
        if (enqueueResult.IsFailure || enqueueResult.Value is null)
        {
            return Result.Failure<ArchiveOutputJobResponse>(
                enqueueResult.ErrorCode ?? ErrorCodes.UNKNOWN_ERROR,
                enqueueResult.Message ?? "打印任务入队失败");
        }

        var job = enqueueResult.Value;
        return Result.Success(new ArchiveOutputJobResponse(
            job.Id,
            job.JobNo,
            job.Status,
            job.JobKind,
            orderedTemplates
                .Select(template => new ArchiveOutputJobTemplate(template.Id, template.Name))
                .ToList()));
    }

    private async Task<Result<List<PrintTemplateMetaRow>>> LoadTemplatesByIdsAsync(
        IReadOnlyCollection<long> templateIds,
        CancellationToken ct)
    {
        var ids = templateIds.Distinct().ToArray();
        var result = await database.QueryAsync<PrintTemplateMetaRow>(
            @"SELECT id, name, doc_type, file_type, sort_order
              FROM nc_biz_templates
              WHERE id = ANY($1::bigint[])",
            ct,
            ids);

        if (result.IsFailure)
        {
            return Result.Failure<List<PrintTemplateMetaRow>>(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "加载模板失败");
        }

        var byId = (result.Value ?? []).ToDictionary(template => template.Id);
        var missing = ids.Where(id => !byId.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
        {
            return Result.Failure<List<PrintTemplateMetaRow>>(
                ErrorCodes.VALIDATION_FAILED,
                $"模板不存在: {string.Join(", ", missing)}");
        }

        return Result.Success(ids.Select(id => byId[id]).ToList());
    }

    private async Task<Result<BusinessOutputContext>> BuildOutputContextAsync(
        string businessType,
        long businessId,
        string? classification,
        string? operationOverride,
        UserDto user,
        CancellationToken ct)
    {
        return businessType switch
        {
            PrintBusinessTypes.FamilyApplication =>
                await BuildFamilyOutputContextAsync(businessId, classification, user.Id, ct),
            PrintBusinessTypes.AssetVerification =>
                await BuildAssetOutputContextAsync(businessId, classification, ct),
            PrintBusinessTypes.TempRelief =>
                await BuildTempReliefOutputContextAsync(businessId, classification, operationOverride, ct),
            PrintBusinessTypes.Recovery =>
                await BuildRecoveryOutputContextAsync(businessId, classification, user, ct),
            PrintBusinessTypes.ElderlyBenefits =>
                await BuildElderlyOutputContextAsync(businessId, classification, operationOverride, ct),
            _ => Result.Failure<BusinessOutputContext>(
                ErrorCodes.VALIDATION_FAILED, $"不支持的业务类型: {businessType}")
        };
    }

    private async Task<Result<BusinessOutputContext>> BuildFamilyOutputContextAsync(
        long applicationId,
        string? classification,
        int userId,
        CancellationToken ct)
    {
        var printResult = await archivePrintData.BuildAsync(applicationId, userId, ct);
        if (printResult.IsFailure || printResult.Value is null)
        {
            return Result.Failure<BusinessOutputContext>(
                printResult.ErrorCode ?? ErrorCodes.APPLICATION_NOT_FOUND,
                printResult.Message ?? "申请数据装配失败");
        }

        var data = printResult.Value;
        var resolvedClassification = FirstNonEmpty(classification, data.Classification);
        return Result.Success(new BusinessOutputContext(
            data.Fields,
            data.TableRows,
            data.ApplicantName,
            data.ApplicantIdCard,
            resolvedClassification,
            resolvedClassification));
    }

    private async Task<Result<BusinessOutputContext>> BuildAssetOutputContextAsync(
        long checkId,
        string? classification,
        CancellationToken ct)
    {
        var payloadResult = await assetChecks.BuildAsync(checkId, ct);
        if (payloadResult.IsFailure || payloadResult.Value is null)
        {
            return Result.Failure<BusinessOutputContext>(
                payloadResult.ErrorCode ?? ErrorCodes.RECORD_NOT_FOUND,
                payloadResult.Message ?? "核对记录不存在");
        }

        var payload = payloadResult.Value;
        return Result.Success(new BusinessOutputContext(
            payload.Fields,
            payload.TableRows,
            GetField(payload.Fields, FieldKeys.APPLICANT_NAME),
            GetField(payload.Fields, FieldKeys.APPLICANT_ID_CARD),
            FirstNonEmpty(classification, ClassificationConstants.AssetVerification),
            ClassificationConstants.AssetVerification));
    }

    private async Task<Result<BusinessOutputContext>> BuildTempReliefOutputContextAsync(
        long applicationId,
        string? classification,
        string? operationOverride,
        CancellationToken ct)
    {
        var appResult = await tempRelief.GetByIdAsync(applicationId, ct);
        if (appResult.IsFailure || appResult.Value is null)
        {
            return Result.Failure<BusinessOutputContext>(
                appResult.ErrorCode ?? ErrorCodes.APPLICATION_NOT_FOUND,
                appResult.Message ?? "临时救助申请不存在");
        }

        var app = appResult.Value;
        var membersResult = await tempRelief.GetMembersByApplicationIdAsync(applicationId, ct);
        if (membersResult.IsFailure)
        {
            return Result.Failure<BusinessOutputContext>(
                membersResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                membersResult.Message ?? "加载家庭成员失败");
        }

        var members = membersResult.Value ?? [];
        var (acceptanceDate, investigationDate) =
            await TempReliefPrintDataBuilder.ResolveScheduleAsync(timeline, app);

        var phoneResult = await tempRelief.GetReportUnitPhoneAsync(app.ReportUnit, ct);
        var contactPhone = phoneResult.IsSuccess ? phoneResult.Value ?? string.Empty : string.Empty;

        var fields = TempReliefPrintDataBuilder.BuildSingleFields(
            app, members, contactPhone, acceptanceDate, investigationDate);

        var resolvedClassification = FirstNonEmpty(classification, app.ReliefType);
        var category = ArchiveCategoryResolver
            .GetRecordCategories(PrintBusinessTypes.TempRelief, resolvedClassification, operationOverride)
            .FirstOrDefault() ?? resolvedClassification;

        var tableRows = members
            .Where(member => !string.IsNullOrWhiteSpace(member.MemberName))
            .Select(member =>
            {
                var row = new Dictionary<string, string>(StringComparer.Ordinal);
                Put(row, FieldKeys.FAMILY_MEMBER_NAME, member.MemberName);
                Put(row, FieldKeys.FAMILY_MEMBER_ID_CARD, member.IdCard);
                Put(row, FieldKeys.FAMILY_MEMBER_RELATION, member.Relation);
                return row;
            })
            .Where(row => row.Count > 0)
            .ToList();

        return Result.Success(new BusinessOutputContext(
            fields,
            tableRows,
            app.ApplicantName ?? string.Empty,
            app.ApplicantIdCard ?? string.Empty,
            resolvedClassification,
            category));
    }

    private async Task<Result<BusinessOutputContext>> BuildRecoveryOutputContextAsync(
        long recordId,
        string? classification,
        UserDto user,
        CancellationToken ct)
    {
        var printResult = await recoveryPrintData.BuildAsync(
            recordId,
            user.OrganizationId,
            FirstNonEmpty(user.FullName, user.Username),
            ct);
        if (printResult.IsFailure || printResult.Value is null)
        {
            return Result.Failure<BusinessOutputContext>(
                printResult.ErrorCode ?? ErrorCodes.RECORD_NOT_FOUND,
                printResult.Message ?? "追缴数据装配失败");
        }

        var data = printResult.Value;
        return Result.Success(new BusinessOutputContext(
            data.Fields,
            [],
            data.PersonName,
            data.IdCard ?? string.Empty,
            FirstNonEmpty(classification, RecoveryPrintDataService.Category),
            RecoveryPrintDataService.Category));
    }

    private async Task<Result<BusinessOutputContext>> BuildElderlyOutputContextAsync(
        long applicationId,
        string? classification,
        string? operationOverride,
        CancellationToken ct)
    {
        var appResult = await elderly.GetByIdAsync(applicationId, ct);
        if (appResult.IsFailure || appResult.Value is null)
        {
            return Result.Failure<BusinessOutputContext>(
                appResult.ErrorCode ?? ErrorCodes.APPLICATION_NOT_FOUND,
                appResult.Message ?? "普惠高龄登记不存在");
        }

        var app = appResult.Value;
        var fields = ElderlyPrintDataBuilder.BuildSingleFields(app);
        var defaultClassification = string.Equals(
            app.Status, ElderlyBenefitConstants.StatusStopped, StringComparison.Ordinal)
            ? "Stop"
            : "新增";
        var resolvedClassification = FirstNonEmpty(classification, defaultClassification);
        var category = ArchiveCategoryResolver
            .GetRecordCategories(PrintBusinessTypes.ElderlyBenefits, resolvedClassification, operationOverride)
            .FirstOrDefault() ?? resolvedClassification;

        return Result.Success(new BusinessOutputContext(
            fields,
            [],
            app.Name,
            app.IdCard,
            resolvedClassification,
            category));
    }

    // ───────────────────────────── 4. 完成归档 ─────────────────────────────

    public async Task<Result> CompleteArchiveAsync(
        string businessType,
        long businessId,
        UserDto user,
        CancellationToken ct = default)
    {
        if (businessId <= 0)
        {
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "businessId 必须为正整数");
        }

        switch (NormalizeBusinessType(businessType))
        {
            case PrintBusinessTypes.FamilyApplication:
                return await applications.CompleteArchiveAsync(
                    businessId, OperatorName(user), ct);

            case PrintBusinessTypes.TempRelief:
                return await tempRelief.CompleteArchiveAsync(businessId, ct);

            case PrintBusinessTypes.ElderlyBenefits:
                return await elderly.CompleteArchiveAsync(businessId, ct);

            case PrintBusinessTypes.Recovery:
                var recoveryResult = await recovery.UpdateRecoveryRecordStatusAsync(
                    businessId, RecoveryConstants.STATUS_PRINTED, ct);
                return recoveryResult.IsFailure
                    ? Result.Failure(
                        recoveryResult.ErrorCode ?? ErrorCodes.UNKNOWN_ERROR,
                        recoveryResult.Message ?? "追缴记录状态更新失败")
                    : Result.Success();

            case PrintBusinessTypes.AssetVerification:
                return Result.Failure(
                    ErrorCodes.VALIDATION_FAILED, "家庭经济状况核对无归档语义，不支持完成归档");

            default:
                return Result.Failure(
                    ErrorCodes.VALIDATION_FAILED, $"不支持的业务类型: {businessType}");
        }
    }

    // ───────────────────────────── 工具 ─────────────────────────────

    private async Task<string?> TryResolveClassificationAsync(
        string businessType,
        long businessId,
        CancellationToken ct)
    {
        switch (businessType)
        {
            case PrintBusinessTypes.FamilyApplication:
                var appResult = await applications.GetByIdAsync(businessId, ct);
                return appResult.IsSuccess ? appResult.Value?.ClassificationResult : null;

            case PrintBusinessTypes.TempRelief:
                var tempResult = await tempRelief.GetByIdAsync(businessId, ct);
                return tempResult.IsSuccess ? tempResult.Value?.ReliefType : null;

            case PrintBusinessTypes.Recovery:
                return RecoveryPrintDataService.Category;

            case PrintBusinessTypes.AssetVerification:
                return ClassificationConstants.AssetVerification;

            case PrintBusinessTypes.ElderlyBenefits:
                var elderlyResult = await elderly.GetByIdAsync(businessId, ct);
                if (!elderlyResult.IsSuccess || elderlyResult.Value is null)
                {
                    return null;
                }

                return string.Equals(
                    elderlyResult.Value.Status,
                    ElderlyBenefitConstants.StatusStopped,
                    StringComparison.Ordinal)
                    ? "Stop"
                    : "新增";

            default:
                return null;
        }
    }

    private static List<PrintTemplateMetaRow> SortTemplates(IEnumerable<PrintTemplateMetaRow> templates)
        => templates
            .OrderBy(template => GetDocTypeRank(template.DocType))
            .ThenBy(template => template.SortOrder)
            .ThenBy(template => template.Name, StringComparer.Ordinal)
            .ToList();

    /// <summary>排序位：封面 → 目录 → 其他（同组内按 sort_order、名称）。</summary>
    private static int GetDocTypeRank(string? docType)
    {
        if (string.Equals(docType, TemplateDocTypes.Cover, StringComparison.Ordinal))
        {
            return 0;
        }

        return string.Equals(docType, TemplateDocTypes.Directory, StringComparison.Ordinal)
            ? 1
            : 2;
    }

    private static string NormalizeBusinessType(string? businessType)
    {
        var trimmed = businessType?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return string.Empty;
        }

        if (string.Equals(trimmed, PrintBusinessTypes.FamilyApplication, StringComparison.OrdinalIgnoreCase))
        {
            return PrintBusinessTypes.FamilyApplication;
        }

        if (string.Equals(trimmed, PrintBusinessTypes.AssetVerification, StringComparison.OrdinalIgnoreCase))
        {
            return PrintBusinessTypes.AssetVerification;
        }

        if (string.Equals(trimmed, PrintBusinessTypes.TempRelief, StringComparison.OrdinalIgnoreCase))
        {
            return PrintBusinessTypes.TempRelief;
        }

        if (string.Equals(trimmed, PrintBusinessTypes.Recovery, StringComparison.OrdinalIgnoreCase))
        {
            return PrintBusinessTypes.Recovery;
        }

        if (string.Equals(trimmed, PrintBusinessTypes.ElderlyBenefits, StringComparison.OrdinalIgnoreCase))
        {
            return PrintBusinessTypes.ElderlyBenefits;
        }

        return trimmed;
    }

    private static string DisplayClassification(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return string.Empty;
        }

        var fullName = ClassificationConstants.ConvertToFullName(code.Trim());
        return string.IsNullOrWhiteSpace(fullName) ? code.Trim() : fullName;
    }

    private static string GetField(Dictionary<string, string> fields, string key)
        => fields.TryGetValue(key, out var value) ? value ?? string.Empty : string.Empty;

    private static void Put(Dictionary<string, string> target, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target[key] = value.Trim();
        }
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string JoinParts(params string?[] parts)
        => string.Concat(parts
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim()));

    private static string FormatAmount(decimal? value)
        => value?.ToString("N2", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string FormatAmount(decimal value)
        => value.ToString("N2", CultureInfo.InvariantCulture);

    private static string FormatDate(DateTime? value)
        => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string FormatDateTime(DateTime? value)
        => value?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;

    private static ArchiveProductionSidebarGroup CreateGroup(
        string title,
        params (string Label, string? Value)[] fields)
        => new(
            title,
            fields.Select(field => new ArchiveProductionSidebarField(
                field.Label,
                field.Value ?? string.Empty,
                !string.IsNullOrWhiteSpace(field.Value))).ToList());

    private static (string FileType, string Extension) ResolveFileType(string? fileType)
    {
        try
        {
            var normalized = TemplateFileTypes.Normalize(fileType);
            return (normalized, TemplateFileTypes.ToExtension(normalized));
        }
        catch (NotSupportedException)
        {
            var fallback = string.IsNullOrWhiteSpace(fileType)
                ? TemplateFileTypes.Word
                : fileType.Trim().ToLowerInvariant();
            return (fallback, ".docx");
        }
    }

    private static string NormalizeFileType(string? fileType)
    {
        try
        {
            return TemplateFileTypes.Normalize(fileType);
        }
        catch (NotSupportedException)
        {
            return fileType?.Trim().ToLowerInvariant() ?? string.Empty;
        }
    }

    private static string BuildFileName(string? name, string extension)
    {
        var safe = string.IsNullOrWhiteSpace(name) ? "档案文书" : name.Trim();
        if (safe.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)
            || safe.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            safe = Path.GetFileNameWithoutExtension(safe);
        }

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            safe = safe.Replace(invalid, '_');
        }

        return safe + extension;
    }

    private static string OperatorName(UserDto user)
        => string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName!;

    /// <summary>一次输出任务装配完成后的业务主字段快照。</summary>
    private sealed record BusinessOutputContext(
        Dictionary<string, string> Fields,
        List<Dictionary<string, string>> TableRows,
        string ApplicantName,
        string ApplicantIdCard,
        string Classification,
        string Category);
}

#region 档案制作 / 输出契约

/// <summary>GET /api/archives/production 响应。</summary>
public sealed record ArchiveProductionResponse(
    string BusinessType,
    long BusinessId,
    string Title,
    string StatusText,
    IReadOnlyList<ArchiveProductionSummaryItem> Summary,
    IReadOnlyList<ArchiveProductionSidebarGroup> SidebarGroups,
    string Classification,
    string OperationOverride);

/// <summary>制作页顶部摘要项。</summary>
public sealed record ArchiveProductionSummaryItem(string Label, string Value);

/// <summary>制作页右侧分组。</summary>
public sealed record ArchiveProductionSidebarGroup(
    string Title,
    IReadOnlyList<ArchiveProductionSidebarField> Fields);

/// <summary>制作页右侧字段（autoFilled = 该字段由业务数据自动带出）。</summary>
public sealed record ArchiveProductionSidebarField(string Label, string Value, bool AutoFilled);

/// <summary>GET /api/archives/output/templates 列表项。</summary>
public sealed record ArchiveOutputTemplateResponse(
    long Id,
    string Name,
    string FileType,
    string DocType,
    int SortOrder,
    bool DefaultSelected);

/// <summary>POST /api/archives/output/jobs 请求体。</summary>
public sealed record ArchiveOutputJobRequest(
    string? BusinessType,
    long? BusinessId,
    string? Classification,
    string? OperationOverride,
    List<long>? TemplateIds,
    string? Mode,
    int? Copies,
    bool? IsDuplex,
    long? PrinterBindingId,
    string? PrinterName);

/// <summary>POST /api/archives/output/jobs 响应中的模板项。</summary>
public sealed record ArchiveOutputJobTemplate(long Id, string Name);

/// <summary>POST /api/archives/output/jobs 响应。</summary>
public sealed record ArchiveOutputJobResponse(
    long Id,
    string JobNo,
    string Status,
    string JobKind,
    IReadOnlyList<ArchiveOutputJobTemplate> Templates);

/// <summary>POST /api/archives/output/complete 请求体。</summary>
public sealed record ArchiveOutputCompleteRequest(string? BusinessType, long? BusinessId);

#endregion
