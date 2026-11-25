using System.Globalization;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Requests;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.Services.System;

namespace NewCosmos.Server.Services;

using ApplicationEntity = NewCosmos.Models.Entities.Application;

/// <summary>
/// 低收入人口认定五步表单聚合服务。
/// <para>
/// 职责边界：本服务只做“表单 ↔ Core 服务”的编排与映射，不重抄任何业务公式——
/// 收入汇总走 <see cref="IIncomeCalculationService"/>，分类/保障金走
/// <see cref="IClassificationService"/> 与 <see cref="IGuaranteeAmountService"/>，
/// 主表/成员/赡养人/调查的持久化分别走对应 Core 服务。
/// </para>
/// <para>
/// 数据范围：GET/PUT/classify/complete/delete 均先按当前登录用户构造
/// nc_biz_applications 数据范围条件；越权与不存在统一返回 404（不泄露存在性）。
/// </para>
/// </summary>
public sealed class ApplicationFormService(
    IApplicationService applications,
    IClassificationService classification,
    IIncomeCalculationService incomeCalculation,
    IGuaranteeAmountService guaranteeAmountService,
    IFamilyMemberService familyMembers,
    ISupporterService supporters,
    ICaregiverService caregivers,
    IHouseholdSurveyService householdSurveys,
    IVisitRecordService visitRecords,
    IEconomicDetailService economicDetails,
    ISubsidyDataService subsidyData,
    ILandContractService landContracts,
    DataScopeFilterBuilder dataScope,
    IDatabaseService database,
    IRegionService regions,
    ILoggerService logger)
{
    private static readonly CultureInfo DateCulture = CultureInfo.InvariantCulture;

    // ───────────────────────────── 读取 ─────────────────────────────

    public async Task<Result<ApplicationFormDto>> GetFormAsync(
        long applicationId,
        int userId,
        CancellationToken ct = default)
    {
        var appResult = await LoadInScopeAsync(applicationId, userId, ct);
        if (appResult.IsFailure)
        {
            return Result.Failure<ApplicationFormDto>(appResult.ErrorCode, appResult.Message);
        }

        var app = appResult.Value;

        var membersResult = await familyMembers.GetByApplicationIdAsync(applicationId, ct);
        if (membersResult.IsFailure)
        {
            return Result.Failure<ApplicationFormDto>(membersResult.ErrorCode!, membersResult.Message!);
        }

        var supportersResult = await supporters.GetByApplicationIdAsync(applicationId, ct);
        if (supportersResult.IsFailure)
        {
            return Result.Failure<ApplicationFormDto>(supportersResult.ErrorCode!, supportersResult.Message!);
        }

        var surveyResult = await householdSurveys.GetByApplicationIdAsync(applicationId, ct);
        if (surveyResult.IsFailure)
        {
            return Result.Failure<ApplicationFormDto>(surveyResult.ErrorCode!, surveyResult.Message!);
        }

        var caregiversResult = await caregivers.GetByApplicationIdAsync(applicationId, ct);
        if (caregiversResult.IsFailure)
        {
            return Result.Failure<ApplicationFormDto>(caregiversResult.ErrorCode!, caregiversResult.Message!);
        }

        var visitsResult = await visitRecords.GetListByApplicationIdAsync(applicationId, ct);
        if (visitsResult.IsFailure)
        {
            return Result.Failure<ApplicationFormDto>(visitsResult.ErrorCode!, visitsResult.Message!);
        }

        var economicDetailsResult = await economicDetails.LoadAllAsync(applicationId, ct);
        if (economicDetailsResult.IsFailure)
        {
            return Result.Failure<ApplicationFormDto>(
                economicDetailsResult.ErrorCode!, economicDetailsResult.Message!);
        }

        // 赡养行由 supporters 块承载，members 只返回户主 + 共同生活成员，避免前端重复展示。
        var members = (membersResult.Value ?? [])
            .Where(m => !IsSupportMember(m))
            .ToList();
        // 年赡养费乘数 = 申请表家庭人口；主表 family_size 尚未回填时按成员行现算。
        var familySize = ResolveApplicationFamilySize(app.FamilySize, members);

        // 地址分级列（户籍/现住）在主表只有 ID 或合并文本；先统一反解，成员回填可复用。
        var basicDto = await ToBasicDtoAsync(app, ct);

        var dto = new ApplicationFormDto(
            app.Id,
            app.ApplicationNo ?? string.Empty,
            app.Status ?? string.Empty,
            ApplicationQueryService.ToStatusText(app.Status),
            app.CurrentStep,
            basicDto,
            members.Select(m => ToMemberDto(m, basicDto)).ToList(),
            (supportersResult.Value ?? [])
                .Select(supporter => ToSupporterDto(supporter, familySize))
                .ToList(),
            ToEconomyDto(app),
            ToSurveyDto(surveyResult.Value),
            ToClassificationDto(app),
            ToEconomicDetailsDto(economicDetailsResult.Value),
            caregiversResult.Value ?? [],
            visitsResult.Value ?? []);

        return Result.Success(dto);
    }

    // ───────────────────────────── 新建草稿 ─────────────────────────────

    public async Task<Result<ApplicationFormSaveResult>> CreateAsync(
        ApplicationFormSaveRequest request,
        int userId,
        string operatorName,
        CancellationToken ct = default)
    {
        var members = request.Members ?? [];
        var familySize = ResolveFamilySize(members);
        var alimonyAnnual = request.Supporters is { } createSupporters
            ? ResolveAlimonyAnnual(createSupporters, familySize)
            : 0m;
        var totals = CalculateIncome(request.Economy, familySize, alimonyAnnual);

        var application = new ApplicationEntity();
        if (request.Basic is { } basic)
        {
            ApplyBasic(application, basic);
            // 主表只有 hukou_*_id 与 city_id/county_id/... 等 ID 列：按名称解析后一并落库。
            await ApplyAddressIdsAsync(application, basic, ct);
        }
        ApplyHeadMemberToApplication(application, members);
        ApplyEconomy(application, request.Economy, familySize, alimonyAnnual, totals);
        ApplyEconomicDetailsTotals(application, request.EconomicDetails, request.Economy, familySize);

        if (string.IsNullOrWhiteSpace(application.ApplicantName)
            || string.IsNullOrWhiteSpace(application.ApplicantIdCard))
        {
            return Result.Failure<ApplicationFormSaveResult>(
                ErrorCodes.VALIDATION_FAILED, "申请人姓名和身份证号不能为空");
        }

        application.Status = ApplicationStatusCodes.DRAFT;
        application.CurrentStep = request.CurrentStep is { } requestedStep
            ? Math.Clamp(requestedStep, WorkflowSteps.ENTRY_START, WorkflowSteps.PENDING_ARCHIVE)
            : WorkflowSteps.ENTRY_START;
        application.CreatedBy = operatorName;
        application.UpdatedBy = operatorName;
        // Core CreateAsync 以 CreatedAt==default 判定“使用 DateTime.Now”，
        // 实体默认的 DateTime.UtcNow 会造成创建时间口径与存量记录不一致。
        application.CreatedAt = default;

        long createdId = 0;
        var saveResult = await applications.ExecuteInTransactionForResultAsync(async token =>
        {
            var createResult = await applications.CreateAsync(application, token);
            if (createResult.IsFailure)
            {
                return ToFailure(createResult);
            }

            createdId = createResult.Value;

            // Core CreateAsync 的 INSERT 不写 updated_by；补一条窄更新，保证审计字段从建档起完整。
            var touchResult = await database.ExecuteNonQueryAsync(
                "UPDATE nc_biz_applications SET updated_by = $1 WHERE id = $2 AND deleted_at IS NULL",
                token,
                operatorName,
                createdId);
            if (touchResult.IsFailure)
            {
                return ToFailure(touchResult);
            }

            var related = await SaveRelatedAsync(createdId, request, familySize, operatorName, token);
            if (related.IsFailure)
            {
                return related;
            }

            // 创建也必须落在当前用户数据范围内：范围外直接回滚，避免越权建档。
            var inScope = await LoadInScopeAsync(createdId, userId, token);
            return inScope.IsFailure
                ? Result.Failure(inScope.ErrorCode!, inScope.Message!)
                : Result.Success();
        }, ct);

        if (saveResult.IsFailure)
        {
            logger.Warn(
                $"低收入表单新建失败: Operator={operatorName}, {saveResult.ErrorCode} {saveResult.Message}");
            return Result.Failure<ApplicationFormSaveResult>(saveResult.ErrorCode, saveResult.Message);
        }

        var applicationNo = string.Empty;
        var reloadResult = await applications.GetByIdAsync(createdId, ct);
        if (reloadResult.HasValue)
        {
            applicationNo = reloadResult.Value.ApplicationNo ?? string.Empty;
        }
        else
        {
            logger.Warn($"低收入表单新建成功但读取申请编号失败: ApplicationId={createdId}");
        }

        logger.LogBusiness("低收入表单新建草稿",
            ("ApplicationId", createdId), ("Operator", operatorName));

        return Result.Success(new ApplicationFormSaveResult(createdId, applicationNo));
    }

    // ───────────────────────────── 保存草稿 ─────────────────────────────

    public async Task<Result<ApplicationFormSaveResult>> UpdateAsync(
        long applicationId,
        ApplicationFormSaveRequest request,
        int userId,
        string operatorName,
        CancellationToken ct = default)
    {
        var appResult = await LoadInScopeAsync(applicationId, userId, ct);
        if (appResult.IsFailure)
        {
            return Result.Failure<ApplicationFormSaveResult>(appResult.ErrorCode, appResult.Message);
        }

        var application = appResult.Value;
        if (!IsDraft(application.Status))
        {
            return Result.Failure<ApplicationFormSaveResult>(
                ErrorCodes.VALIDATION_FAILED, "仅草稿状态的申请可以保存");
        }

        if (request.Basic is { } basic)
        {
            ApplyBasic(application, basic);
            await ApplyAddressIdsAsync(application, basic, ct);
        }
        if (request.Members is { } updateMembers)
        {
            ApplyHeadMemberToApplication(application, updateMembers);
        }

        var familySize = request.Members is { } memberList
            ? ResolveFamilySize(memberList)
            : application.FamilySize;
        // 未携带成员块且主表 family_size 为空（历史/外部建档）时，从已落库成员行补算，
        // 避免赡养年费乘数退化成 1。
        if (familySize <= 0)
        {
            var storedMembers = await familyMembers.GetByApplicationIdAsync(applicationId, ct);
            if (storedMembers.IsFailure)
            {
                return Result.Failure<ApplicationFormSaveResult>(
                    storedMembers.ErrorCode!, storedMembers.Message!);
            }

            var shared = (storedMembers.Value ?? []).Where(m => !IsSupportMember(m)).ToList();
            familySize = ResolveApplicationFamilySize(0, shared);
        }
        // 赡养收入只由 supporters 块派生；未传 supporters 时沿用主表现值。
        var alimonyAnnual = request.Supporters is { } updateSupporters
            ? ResolveAlimonyAnnual(updateSupporters, familySize)
            : application.AlimonyIncome;
        var totals = request.Economy is { } economy
            ? CalculateIncome(economy, familySize, alimonyAnnual)
            : null;
        ApplyEconomy(application, request.Economy, familySize, alimonyAnnual, totals);
        ApplyEconomicDetailsTotals(application, request.EconomicDetails, request.Economy, familySize);

        if (string.IsNullOrWhiteSpace(application.ApplicantName)
            || string.IsNullOrWhiteSpace(application.ApplicantIdCard))
        {
            return Result.Failure<ApplicationFormSaveResult>(
                ErrorCodes.VALIDATION_FAILED, "申请人姓名和身份证号不能为空");
        }

        // 草稿步骤进度以 1..5 为界；归档（6）只能走 complete 接口。
        if (request.CurrentStep is { } requestedStep)
        {
            application.CurrentStep = Math.Clamp(
                requestedStep, WorkflowSteps.ENTRY_START, WorkflowSteps.PENDING_ARCHIVE);
        }

        application.UpdatedBy = operatorName;

        var saveResult = await applications.ExecuteInTransactionForResultAsync(async token =>
        {
            var updateResult = await applications.UpdateAsync(application, token);
            if (updateResult.IsFailure)
            {
                return updateResult;
            }

            return await SaveRelatedAsync(applicationId, request, familySize, operatorName, token);
        }, ct);

        if (saveResult.IsFailure)
        {
            logger.Warn(
                $"低收入表单保存失败: ApplicationId={applicationId}, Operator={operatorName}, " +
                $"{saveResult.ErrorCode} {saveResult.Message}");
            return Result.Failure<ApplicationFormSaveResult>(saveResult.ErrorCode, saveResult.Message);
        }

        logger.LogBusiness("低收入表单保存草稿",
            ("ApplicationId", applicationId), ("Operator", operatorName));

        return Result.Success(new ApplicationFormSaveResult(applicationId, application.ApplicationNo ?? string.Empty));
    }

    // ───────────────────────────── 分类判定 ─────────────────────────────

    public async Task<Result<ApplicationFormDto>> ClassifyAsync(
        long applicationId,
        int userId,
        string operatorName,
        CancellationToken ct = default)
    {
        var appResult = await LoadInScopeAsync(applicationId, userId, ct);
        if (appResult.IsFailure)
        {
            return Result.Failure<ApplicationFormDto>(appResult.ErrorCode, appResult.Message);
        }

        var application = appResult.Value;

        // 重新加载成员/赡养人，避免使用前端缓存的旧快照参与判定。
        var membersResult = await familyMembers.GetByApplicationIdAsync(applicationId, ct);
        if (membersResult.IsFailure)
        {
            return Result.Failure<ApplicationFormDto>(
                membersResult.ErrorCode!, membersResult.Message!);
        }

        var supportersResult = await supporters.GetByApplicationIdAsync(applicationId, ct);
        if (supportersResult.IsFailure)
        {
            return Result.Failure<ApplicationFormDto>(
                supportersResult.ErrorCode!, supportersResult.Message!);
        }

        var classificationResult = await classification.DetermineClassificationAsync(
            application,
            membersResult.Value ?? [],
            supportersResult.Value ?? [],
            [],
            ct);

        if (classificationResult.IsFailure)
        {
            return Result.Failure<ApplicationFormDto>(
                classificationResult.ErrorCode!, classificationResult.Message!);
        }

        var result = classificationResult.Value;

        // 照料护理费只对特困人员有意义；其余分类按 0 写入（能力鉴定服务无记录时本身也返回 0）。
        var caregiverSubsidy = ClassificationConstants.IsCodeDestitute(result.Classification)
            ? await classification.CalculateCareAllowanceAsync(applicationId, ct)
            : 0m;
        var classifiedSubsidy = result.ClassifiedSubsidy?.TotalAmount ?? 0m;
        var totalGuarantee = guaranteeAmountService.CalculateTotalGuaranteeAmount(
            result.GuaranteeAmount, classifiedSubsidy, caregiverSubsidy);

        // 只写主表已有列；IneligibleReason/DeterminationBasis 在主表没有对应列，
        // 在本次响应中返回给前端展示，不回写数据库（详见子任务报告）。
        application.ClassificationResult = result.Classification;
        application.IsEligible = result.IsEligible;
        application.HouseholdMonthlyGuaranteeAmount = result.GuaranteeAmount;
        application.ClassifiedSubsidyType = result.ClassifiedSubsidy?.Types ?? string.Empty;
        application.ClassifiedSubsidyAmount = classifiedSubsidy;
        application.CaregiverSubsidyAmount = caregiverSubsidy;
        application.TotalGuaranteeAmount = totalGuarantee;
        application.IsInGracePeriod = result.ShouldTriggerGracePeriod;

        var inGrace = result.ShouldTriggerGracePeriod && result.GracePeriod is { IsEligible: true };
        application.GracePeriodMonths = inGrace ? result.GracePeriod!.Months : null;
        application.GracePeriodStartDate = inGrace ? result.GracePeriod!.StartDate : null;
        application.GracePeriodEndDate = inGrace ? result.GracePeriod!.EndDate : null;
        application.UpdatedBy = operatorName;

        var updateResult = await applications.UpdateAsync(application, ct);
        if (updateResult.IsFailure)
        {
            logger.Warn(
                $"低收入表单分类写回失败: ApplicationId={applicationId}, " +
                $"{updateResult.ErrorCode} {updateResult.Message}");
            return Result.Failure<ApplicationFormDto>(
                updateResult.ErrorCode!, updateResult.Message!);
        }

        logger.LogBusiness("低收入表单分类判定",
            ("ApplicationId", applicationId),
            ("Classification", result.Classification ?? string.Empty),
            ("Operator", operatorName));

        var classificationDto = new ApplicationFormClassificationDto(
            result.Classification ?? string.Empty,
            ClassificationConstants.ConvertFromCode(result.Classification ?? string.Empty),
            result.IsEligible,
            result.IneligibleReason ?? string.Empty,
            result.DeterminationBasis ?? string.Empty,
            result.GuaranteeAmount,
            classifiedSubsidy,
            caregiverSubsidy,
            totalGuarantee,
            result.ShouldTriggerGracePeriod,
            inGrace ? result.GracePeriod!.Months : null,
            inGrace ? FormatDate(result.GracePeriod!.EndDate) : string.Empty);

        // 返回完整表单（含服务端刚重算的收入汇总），前端可直接 applyForm 刷新只读合计；
        // IneligibleReason/DeterminationBasis 无主表列，用本次判定结果覆盖回填块。
        var formResult = await GetFormAsync(applicationId, userId, ct);
        if (formResult.IsFailure)
        {
            return Result.Failure<ApplicationFormDto>(formResult.ErrorCode, formResult.Message);
        }

        return Result.Success(formResult.Value with { Classification = classificationDto });
    }

    // ───────────────────────────── 归档完成 ─────────────────────────────

    public async Task<Result<ApplicationFormCompleteResult>> CompleteAsync(
        long applicationId,
        int userId,
        string operatorName,
        CancellationToken ct = default)
    {
        var appResult = await LoadInScopeAsync(applicationId, userId, ct);
        if (appResult.IsFailure)
        {
            return Result.Failure<ApplicationFormCompleteResult>(appResult.ErrorCode, appResult.Message);
        }

        if (string.IsNullOrWhiteSpace(appResult.Value.ClassificationResult))
        {
            return Result.Failure<ApplicationFormCompleteResult>(
                ErrorCodes.VALIDATION_FAILED, "请先完成分类判定再归档");
        }

        var completeResult = await applications.CompleteArchiveAsync(applicationId, operatorName, ct);
        if (completeResult.IsFailure)
        {
            return Result.Failure<ApplicationFormCompleteResult>(
                completeResult.ErrorCode!, completeResult.Message!);
        }

        logger.LogBusiness("低收入表单归档完成",
            ("ApplicationId", applicationId), ("Operator", operatorName));

        return Result.Success(new ApplicationFormCompleteResult(
            ApplicationStatusCodes.APPROVED, WorkflowSteps.ARCHIVED));
    }

    // ───────────────────────────── 回访补录 ─────────────────────────────

    /// <summary>
    /// 补录一条回访。申请必须存在且在当前用户数据范围内；applicant_id_card 由
    /// IVisitRecordService.AddAsync 从主表同源带出，不接受前端传入。
    /// </summary>
    public async Task<Result<ApplicationVisitCreatedResult>> AddVisitAsync(
        long applicationId,
        ApplicationVisitCreateRequest request,
        int userId,
        string operatorName,
        CancellationToken ct = default)
    {
        var appResult = await LoadInScopeAsync(applicationId, userId, ct);
        if (appResult.IsFailure)
        {
            return Result.Failure<ApplicationVisitCreatedResult>(appResult.ErrorCode, appResult.Message);
        }

        var visitAt = ParseDate(request.VisitAt);
        if (visitAt is null)
        {
            if (!string.IsNullOrWhiteSpace(request.VisitAt))
            {
                return Result.Failure<ApplicationVisitCreatedResult>(
                    ErrorCodes.VALIDATION_FAILED, "回访时间格式无效");
            }

            visitAt = DateTime.Today;
        }

        if (string.IsNullOrWhiteSpace(request.VisitResult))
        {
            return Result.Failure<ApplicationVisitCreatedResult>(
                ErrorCodes.VALIDATION_FAILED, "回访情况不能为空");
        }

        var record = new VisitRecord
        {
            ApplicationId = applicationId,
            VisitAt = visitAt.Value,
            VisitBy = string.IsNullOrWhiteSpace(request.VisitBy)
                ? operatorName
                : request.VisitBy.Trim(),
            VisitResult = request.VisitResult.Trim(),
            VisitRemark = request.VisitRemark ?? string.Empty
        };

        var addResult = await visitRecords.AddAsync(record, operatorName, ct);
        if (addResult.IsFailure)
        {
            logger.Warn(
                $"低收入表单回访补录失败: ApplicationId={applicationId}, Operator={operatorName}, " +
                $"{addResult.ErrorCode} {addResult.Message}");
            return Result.Failure<ApplicationVisitCreatedResult>(
                addResult.ErrorCode!, addResult.Message!);
        }

        logger.LogBusiness("低收入表单回访补录",
            ("ApplicationId", applicationId),
            ("VisitRecordId", addResult.Value),
            ("Operator", operatorName));

        return Result.Success(new ApplicationVisitCreatedResult(addResult.Value));
    }

    // ───────────────────────────── Step3 导入 ─────────────────────────────

    /// <summary>
    /// Step3 补贴导入：按主表申请人 + 共同生活成员（排除赡养行）的身份证查询年度种植补贴。
    /// 只读，不写库；前端把结果放入 economicDetails.subsidies 后随 PUT /form 保存。
    /// </summary>
    public async Task<Result<List<SubsidyImportRecord>>> GetSubsidyImportsAsync(
        long applicationId,
        int userId,
        CancellationToken ct = default)
    {
        var appResult = await LoadInScopeAsync(applicationId, userId, ct);
        if (appResult.IsFailure)
        {
            return Result.Failure<List<SubsidyImportRecord>>(appResult.ErrorCode, appResult.Message);
        }

        var idCards = new List<string>();
        var seenIdCards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddDistinct(idCards, seenIdCards, appResult.Value.ApplicantIdCard);

        var membersResult = await familyMembers.GetByApplicationIdAsync(applicationId, ct);
        if (membersResult.IsFailure)
        {
            return Result.Failure<List<SubsidyImportRecord>>(
                membersResult.ErrorCode!, membersResult.Message!);
        }

        foreach (var member in membersResult.Value ?? [])
        {
            // 赡养人只在 Step3 赡养块维护，不属于共同生活成员，不参与补贴导入。
            if (IsSupportMember(member))
            {
                continue;
            }

            AddDistinct(idCards, seenIdCards, member.IdCard);
        }

        return await subsidyData.GetSubsidiesByIdCardsAsync(idCards, ct);
    }

    /// <summary>
    /// Step3 土地确权导入：按主表申请人 + 共同生活成员（排除赡养行）姓名查询确权地块。
    /// 确权底表只有承包人姓名、没有身份证列，故这里只能按姓名匹配。
    /// 只读，不写库；前端把结果放入 economicDetails 后随 PUT /form 保存。
    /// </summary>
    public async Task<Result<List<LandConfirmationRecord>>> GetLandConfirmationImportsAsync(
        long applicationId,
        int userId,
        CancellationToken ct = default)
    {
        var appResult = await LoadInScopeAsync(applicationId, userId, ct);
        if (appResult.IsFailure)
        {
            return Result.Failure<List<LandConfirmationRecord>>(appResult.ErrorCode, appResult.Message);
        }

        var names = new List<string>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        AddDistinct(names, seenNames, appResult.Value.ApplicantName);

        var membersResult = await familyMembers.GetByApplicationIdAsync(applicationId, ct);
        if (membersResult.IsFailure)
        {
            return Result.Failure<List<LandConfirmationRecord>>(
                membersResult.ErrorCode!, membersResult.Message!);
        }

        foreach (var member in membersResult.Value ?? [])
        {
            if (IsSupportMember(member))
            {
                continue;
            }

            AddDistinct(names, seenNames, member.Name);
        }

        return await landContracts.GetRecordsByNamesAsync(names, ct);
    }

    // ───────────────────────────── 删除草稿 ─────────────────────────────

    public async Task<Result> DeleteAsync(
        long applicationId,
        int userId,
        string operatorName,
        CancellationToken ct = default)
    {
        var appResult = await LoadInScopeAsync(applicationId, userId, ct);
        if (appResult.IsFailure)
        {
            return Result.Failure(appResult.ErrorCode, appResult.Message);
        }

        if (!IsDraft(appResult.Value.Status))
        {
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "仅草稿状态的申请可以删除");
        }

        var deleteResult = await applications.DeleteAsync(applicationId, ct);
        if (deleteResult.IsFailure)
        {
            return deleteResult;
        }

        logger.LogBusiness("低收入表单删除草稿",
            ("ApplicationId", applicationId), ("Operator", operatorName));

        return Result.Success();
    }

    // ───────────────────────────── 数据范围 ─────────────────────────────

    /// <summary>
    /// 读取申请并强制当前用户数据范围。越权与不存在都返回 NOT_FOUND（404）。
    /// </summary>
    private async Task<Result<ApplicationEntity>> LoadInScopeAsync(
        long applicationId,
        int userId,
        CancellationToken ct)
    {
        var scope = await dataScope.BuildAsync(
            userId, DataScopeTarget.Applications, "a", 2, ct);

        var sql = "SELECT COUNT(*) FROM nc_biz_applications a " +
                  "WHERE a.id = $1 AND a.deleted_at IS NULL";
        var parameters = new List<object?> { applicationId };
        if (!scope.IsEmpty)
        {
            sql += " AND " + scope.Sql;
            parameters.AddRange(scope.Parameters);
        }

        var countResult = await database.ExecuteScalarAsync<long?>(sql, ct, parameters.ToArray());
        if (countResult.IsFailure)
        {
            return Result.Failure<ApplicationEntity>(countResult.ErrorCode!, countResult.Message!);
        }

        if ((countResult.Value ?? 0) == 0)
        {
            return Result.Failure<ApplicationEntity>(ApiErrorCodes.NotFound, "申请不存在");
        }

        var appResult = await applications.GetByIdAsync(applicationId, ct);
        if (appResult.IsFailure)
        {
            return Result.Failure<ApplicationEntity>(appResult.ErrorCode!, appResult.Message!);
        }

        if (appResult.Value is null)
        {
            return Result.Failure<ApplicationEntity>(ApiErrorCodes.NotFound, "申请不存在");
        }

        return Result.Success(appResult.Value);
    }

    // ───────────────────────────── 关联数据保存 ─────────────────────────────

    /// <summary>
    /// 主表保存后的关联数据编排：成员 → 户主行同步 → 赡养人 → 入户调查。
    /// 调用方已处于事务中，任一失败由外层事务整体回滚。
    /// </summary>
    private async Task<Result> SaveRelatedAsync(
        long applicationId,
        ApplicationFormSaveRequest request,
        int familySize,
        string operatorName,
        CancellationToken ct)
    {
        var memberIdMap = new Dictionary<long, long>();
        if (request.Members is { } memberDtos)
        {
            var saveMembers = await SaveMembersAsync(applicationId, memberDtos, request.Basic, ct);
            if (saveMembers.IsFailure)
            {
                return Result.Failure(saveMembers.ErrorCode, saveMembers.Message);
            }

            memberIdMap = saveMembers.Value;
        }

        // 户主行由主表单向维护（成员表不参与全删全插），每次保存后同步一次，幂等。
        var headResult = await familyMembers.UpsertHouseholdHeadFromApplicationAsync(applicationId, ct);
        if (headResult.IsFailure)
        {
            return headResult;
        }

        if (request.Supporters is { } supporterDtos)
        {
            var saveSupporters = await SaveSupportersAsync(
                applicationId, supporterDtos, familySize, ct);
            if (saveSupporters.IsFailure)
            {
                return saveSupporters;
            }
        }

        if (request.Survey is { } surveyDto)
        {
            var saveSurvey = await SaveSurveyAsync(applicationId, surveyDto, ct);
            if (saveSurvey.IsFailure)
            {
                return saveSurvey;
            }
        }

        if (request.EconomicDetails is { } detailsDto)
        {
            var saveDetails = await SaveEconomicDetailsAsync(
                applicationId, detailsDto, memberIdMap, ct);
            if (saveDetails.IsFailure)
            {
                return saveDetails;
            }
        }

        if (request.Caregivers is { } caregiverDtos)
        {
            var saveCaregivers = await SaveCaregiversAsync(applicationId, caregiverDtos, memberIdMap, ct);
            if (saveCaregivers.IsFailure)
            {
                return saveCaregivers;
            }
        }

        return Result.Success();
    }

    /// <summary>
    /// 经济明细整批重写。表单暂无土地确权组，先读取旧组再原样回写，
    /// 避免 SaveAllAsync 的“全删全插”把确权记录/共有人清空。
    /// </summary>
    private async Task<Result> SaveEconomicDetailsAsync(
        long applicationId,
        ApplicationFormEconomicDetailsDto dto,
        IReadOnlyDictionary<long, long> memberIdMap,
        CancellationToken ct)
    {
        var existingResult = await economicDetails.LoadAllAsync(applicationId, ct);
        if (existingResult.IsFailure)
        {
            return Result.Failure(existingResult.ErrorCode!, existingResult.Message!);
        }

        var landGroups = existingResult.Value.LandConfirmationGroups ?? [];

        // 明细行统一归到当前申请；成员行全删全插换了 id，明细的 member_id 同步迁移。
        long MapMember(long memberId)
            => memberId > 0 && memberIdMap.TryGetValue(memberId, out var newId) ? newId : memberId;

        foreach (var item in dto.LaborIncomes ?? [])
        {
            item.ApplicationId = applicationId;
            item.MemberId = MapMember(item.MemberId);
        }
        foreach (var item in dto.BusinessIncomes ?? [])
        {
            item.ApplicationId = applicationId;
            item.MemberId = MapMember(item.MemberId);
        }
        foreach (var item in dto.PropertyIncomes ?? [])
        {
            item.ApplicationId = applicationId;
            item.MemberId = MapMember(item.MemberId);
        }
        foreach (var item in dto.TransferIncomes ?? [])
        {
            item.ApplicationId = applicationId;
            item.MemberId = MapMember(item.MemberId);
        }
        foreach (var item in dto.RigidExpenditures ?? [])
        {
            item.ApplicationId = applicationId;
            item.MemberId = MapMember(item.MemberId);
        }
        foreach (var item in dto.FamilyProperties ?? [])
        {
            item.ApplicationId = applicationId;
            item.MemberId = MapMember(item.MemberId);
        }
        foreach (var item in dto.Vehicles ?? [])
        {
            item.ApplicationId = applicationId;
            item.MemberId = MapMember(item.MemberId);
        }
        foreach (var item in dto.Machineries ?? [])
        {
            item.ApplicationId = applicationId;
            item.MemberId = MapMember(item.MemberId);
        }
        foreach (var item in dto.OtherIncomes ?? []) item.ApplicationId = applicationId;
        foreach (var item in dto.Subsidies ?? []) item.ApplicationId = applicationId;
        foreach (var item in dto.BreedingIncomes ?? []) item.ApplicationId = applicationId;
        foreach (var item in dto.FinancialAssets ?? []) item.ApplicationId = applicationId;
        foreach (var item in dto.LandRegistrations ?? []) item.ApplicationId = applicationId;

        var saveResult = await economicDetails.SaveAllAsync(
            applicationId,
            dto.LaborIncomes ?? [],
            dto.BusinessIncomes ?? [],
            dto.PropertyIncomes ?? [],
            dto.TransferIncomes ?? [],
            dto.OtherIncomes ?? [],
            dto.Subsidies ?? [],
            dto.BreedingIncomes ?? [],
            dto.RigidExpenditures ?? [],
            dto.FamilyProperties ?? [],
            dto.Vehicles ?? [],
            dto.Machineries ?? [],
            dto.FinancialAssets ?? [],
            dto.LandRegistrations ?? [],
            landGroups,
            ct);

        return saveResult.IsFailure
            ? Result.Failure(saveResult.ErrorCode!, saveResult.Message!)
            : Result.Success();
    }

    private async Task<Result> SaveCaregiversAsync(
        long applicationId,
        List<Caregiver> caregiverDtos,
        IReadOnlyDictionary<long, long> memberIdMap,
        CancellationToken ct)
    {
        // 不直接回写请求实体：Caregiver 的 Selected* 绑定属性 setter 会联动清空
        // EmploymentStatus/MainIncomeSource（GET 回传的 null 选项），这里只复制落库列。
        var list = new List<Caregiver>(caregiverDtos.Count);
        foreach (var dto in caregiverDtos)
        {
            if (dto is null)
            {
                continue;
            }

            list.Add(new Caregiver
            {
                ApplicationId = applicationId,
                // 共同生活成员保存时全删全插会换新 id，按旧→新映射迁移被照料人引用；
                // 户主行不重建，其旧 id 继续有效（映射表里没有则原样保留）。
                CaredMemberId = dto.CaredMemberId > 0
                                 && memberIdMap.TryGetValue(dto.CaredMemberId, out var newMemberId)
                    ? newMemberId
                    : dto.CaredMemberId,
                Name = dto.Name ?? string.Empty,
                IdCard = (dto.IdCard ?? string.Empty).Trim(),
                Phone = dto.Phone ?? string.Empty,
                Relationship = ToCoreRelationship(dto.Relationship),
                Gender = NormalizeGender(dto.Gender),
                Age = dto.Age,
                Ethnicity = dto.Ethnicity ?? string.Empty,
                MaritalStatus = dto.MaritalStatus ?? string.Empty,
                HukouType = dto.HukouType ?? string.Empty,
                EducationLevel = dto.EducationLevel ?? string.Empty,
                PoliticalStatus = dto.PoliticalStatus ?? string.Empty,
                HealthStatus = dto.HealthStatus ?? string.Empty,
                EmploymentStatus = dto.EmploymentStatus ?? string.Empty,
                MainIncomeSource = dto.MainIncomeSource ?? string.Empty,
                WorkUnit = dto.WorkUnit ?? string.Empty,
                Position = dto.Position ?? string.Empty,
                Address = dto.Address ?? string.Empty,
                BankName = dto.BankName ?? string.Empty,
                BankAccount = dto.BankAccount ?? string.Empty
            });
        }

        var saveResult = await caregivers.SaveAsync(applicationId, list, ct);
        return saveResult.IsFailure
            ? Result.Failure(saveResult.ErrorCode!, saveResult.Message!)
            : Result.Success();
    }

    /// <summary>
    /// 全删全插共同生活成员，返回“旧成员 id → 新成员 id”映射，供照料人 cared_member_id 迁移。
    /// </summary>
    private async Task<Result<Dictionary<long, long>>> SaveMembersAsync(
        long applicationId,
        List<ApplicationFormMemberDto> memberDtos,
        ApplicationFormBasicDto? basic,
        CancellationToken ct)
    {
        // 先读取旧成员，用于保留表单未建模的列（家庭住址、婚姻/学历/政治面貌、疾病分类等），
        // 避免“全删全插”把这些字段静默清零。
        var existingResult = await familyMembers.GetByApplicationIdAsync(applicationId, ct);
        if (existingResult.IsFailure)
        {
            return Result.Failure<Dictionary<long, long>>(
                existingResult.ErrorCode!, existingResult.Message!);
        }

        var existingById = new Dictionary<long, FamilyMember>();
        var existingByIdCard = new Dictionary<string, FamilyMember>(StringComparer.OrdinalIgnoreCase);
        foreach (var existing in existingResult.Value ?? [])
        {
            if (IsSupportMember(existing))
            {
                continue;
            }

            existingById[existing.Id] = existing;
            if (!string.IsNullOrWhiteSpace(existing.IdCard))
            {
                existingByIdCard.TryAdd(existing.IdCard.Trim(), existing);
            }
        }

        var deleteResult = await familyMembers.DeleteNonSupportByApplicationIdAsync(applicationId, ct);
        if (deleteResult.IsFailure)
        {
            return Result.Failure<Dictionary<long, long>>(
                deleteResult.ErrorCode!, deleteResult.Message!);
        }

        var seenIdCards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var requests = new List<FamilyMemberCreateRequest>(memberDtos.Count);
        var applicantIdCard = (basic?.ApplicantIdCard ?? string.Empty).Trim();

        foreach (var member in memberDtos)
        {
            var category = MemberCategoryHelper.Normalize(
                member.MemberCategory, member.IsApplicant, member.Relation);

            // 赡养行由 SupporterService 独占写入；户主行由 UpsertHouseholdHead 维护。
            if (string.Equals(category, MemberCategoryConstants.SUPPORT, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (member.IsApplicant
                || member.IsHouseholdHead
                || string.Equals(category, MemberCategoryConstants.HOUSEHOLD_HEAD, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(member.Name) || string.IsNullOrWhiteSpace(member.IdCard))
            {
                continue;
            }

            var idCard = member.IdCard.Trim();

            // 即使前端漏标 isHouseholdHead/memberCategory，身份证与主表申请人一致的成员也归户主行，
            // 不能再插入一份（会与 UpsertHouseholdHead 的户主行撞身份证唯一校验）。
            if (applicantIdCard.Length > 0
                && string.Equals(idCard, applicantIdCard, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!seenIdCards.Add(idCard))
            {
                logger.Info($"低收入表单跳过重复身份证成员: ApplicationId={applicationId}");
                continue;
            }

            FamilyMember? existing = null;
            if (member.Id > 0)
            {
                existingById.TryGetValue(member.Id, out existing);
            }
            if (existing is null)
            {
                existingByIdCard.TryGetValue(idCard, out existing);
            }

            var createRequest = ToMemberCreateRequest(applicationId, member, basic);
            PreserveUnmodeledMemberFields(createRequest, existing);
            requests.Add(createRequest);
        }

        if (requests.Count == 0)
        {
            return Result.Success(new Dictionary<long, long>());
        }

        var addResult = await familyMembers.AddRangeAsync(requests, ct);
        if (addResult.IsFailure)
        {
            return Result.Failure<Dictionary<long, long>>(
                addResult.ErrorCode!, addResult.Message!);
        }

        // 旧行已删除，按身份证把旧 id 映射到新插入的 id（户主行未重建，不在此表）。
        var newIdByIdCard = addResult.Value;
        var idMap = new Dictionary<long, long>();
        foreach (var (oldId, oldMember) in existingById)
        {
            var idCard = (oldMember.IdCard ?? string.Empty).Trim();
            if (idCard.Length > 0 && newIdByIdCard.TryGetValue(idCard, out var newId))
            {
                idMap[oldId] = newId;
            }
        }

        return Result.Success(idMap);
    }

    /// <summary>
    /// 保存赡养/抚养/扶养义务人。年赡养费是只读派生值：
    /// <c>round(月费 × 月数 × 申请表家庭人口, 2)</c>，前端传来的 annualSupportFee 一律忽略；
    /// 月数落在 nc_biz_family_members.support_months（supporter 行，member_category='Support'），
    /// 家庭人口乘数取申请表 family_size（由成员行解析），赡养人自家人口 family_size 列沿用旧值。
    /// </summary>
    private async Task<Result> SaveSupportersAsync(
        long applicationId,
        List<ApplicationFormSupporterDto> supporterDtos,
        int familySize,
        CancellationToken ct)
    {
        var existingResult = await supporters.GetByApplicationIdAsync(applicationId, ct);
        if (existingResult.IsFailure)
        {
            return Result.Failure(existingResult.ErrorCode!, existingResult.Message!);
        }

        var existingList = existingResult.Value ?? [];
        var existingById = existingList.ToDictionary(x => x.Id);
        var existingByIdCard = new Dictionary<string, Supporter>(StringComparer.OrdinalIgnoreCase);
        foreach (var existing in existingList)
        {
            if (!string.IsNullOrWhiteSpace(existing.IdCard))
            {
                existingByIdCard.TryAdd(existing.IdCard.Trim(), existing);
            }
        }

        // 部分保存（只传 supporters、不带 members）时 familySize 可能为 0：从已落库成员行补算。
        if (familySize <= 0)
        {
            var membersResult = await familyMembers.GetByApplicationIdAsync(applicationId, ct);
            if (membersResult.IsFailure)
            {
                return Result.Failure(membersResult.ErrorCode!, membersResult.Message!);
            }

            var shared = (membersResult.Value ?? []).Where(m => !IsSupportMember(m)).ToList();
            familySize = ResolveApplicationFamilySize(0, shared);
        }

        var multiplier = familySize > 0 ? familySize : 1;
        var list = new List<Supporter>(supporterDtos.Count);

        foreach (var dto in supporterDtos)
        {
            var id = dto.Id > 0 && existingById.ContainsKey(dto.Id) ? dto.Id : 0;
            var idCard = (dto.IdCard ?? string.Empty).Trim();
            if (id == 0 && idCard.Length > 0 && existingByIdCard.TryGetValue(idCard, out var byCard))
            {
                id = byCard.Id;
            }

            Supporter? existing = null;
            if (id > 0)
            {
                existingById.TryGetValue(id, out existing);
            }

            // 月数：本次有值优先；否则沿用旧行；旧行也没有则按旧版默认 12。
            var months = dto.SupportMonths is > 0
                ? dto.SupportMonths.Value
                : existing is { SupportMonths: > 0 } ? existing.SupportMonths : 12;
            var monthly = ResolveMonthlySupportFee(dto);

            var supporter = new Supporter
            {
                Id = id,
                ApplicationId = applicationId,
                Name = dto.Name ?? string.Empty,
                IdCard = idCard,
                Relationship = ToCoreRelationship(dto.Relation),
                Gender = NormalizeGender(dto.Gender),
                Age = dto.Age,
                MonthlySupportFee = monthly,
                IsSupportAbility = dto.IsSupportAbility,
                SupportMonths = months,
                SupporterFamilySize = 1
            };

            if (existing is not null)
            {
                // 表单未建模的赡养人字段（地址、自家人口、收入能力等）沿用旧值；
                // 本次显式传入的新增字段优先（GET 回传同一份快照，等价于原样保留）。
                supporter.SupporterFamilySize = existing.SupporterFamilySize > 0
                    ? existing.SupporterFamilySize
                    : 1;
                supporter.AnnualIncome = existing.AnnualIncome;
                supporter.Ethnicity = existing.Ethnicity;
                supporter.HukouType = existing.HukouType;
                supporter.MaritalStatus = existing.MaritalStatus;
                supporter.EducationLevel = existing.EducationLevel;
                supporter.PoliticalStatus = existing.PoliticalStatus;
                supporter.HealthStatus = existing.HealthStatus;
                supporter.WorkUnit = existing.WorkUnit;
                supporter.EmploymentStatus = existing.EmploymentStatus;
                supporter.MainIncomeSource = existing.MainIncomeSource;
                supporter.WorkCapacity = existing.WorkCapacity;
                supporter.HomeProvince = existing.HomeProvince;
                supporter.HomeCity = existing.HomeCity;
                supporter.HomeDistrict = existing.HomeDistrict;
                supporter.HomeTown = existing.HomeTown;
                supporter.HomeVillage = existing.HomeVillage;
                supporter.HukouProvince = existing.HukouProvince;
                supporter.HukouCity = existing.HukouCity;
                supporter.HukouDistrict = existing.HukouDistrict;
                supporter.HukouTown = existing.HukouTown;
            }

            if (!string.IsNullOrWhiteSpace(dto.PersonType))
            {
                supporter.PersonType = dto.PersonType.Trim();
            }
            else if (existing is not null)
            {
                supporter.PersonType = existing.PersonType;
            }

            if (!string.IsNullOrWhiteSpace(dto.Phone))
            {
                supporter.Phone = dto.Phone.Trim();
            }
            else if (existing is not null)
            {
                supporter.Phone = existing.Phone;
            }

            if (!string.IsNullOrWhiteSpace(dto.Address))
            {
                supporter.HomeAddress = dto.Address.Trim();
            }
            else if (existing is not null)
            {
                supporter.HomeAddress = existing.HomeAddress;
            }

            supporter.MonthlyIncomeCapacity = dto.MonthlyIncomeCapacity
                ?? existing?.MonthlyIncomeCapacity
                ?? 0m;

            // 年费最后赋值：避免 ObservableObject 的月费/月数/人数联动覆盖本次公式结果。
            supporter.CalcFamilySize = multiplier;
            supporter.AnnualSupportFee = CalculateAnnualSupportFee(monthly, months, multiplier);
            list.Add(supporter);
        }

        var saveResult = await supporters.SaveAsync(applicationId, list, ct);
        return saveResult.IsFailure
            ? Result.Failure(saveResult.ErrorCode!, saveResult.Message!)
            : Result.Success();
    }

    private async Task<Result> SaveSurveyAsync(
        long applicationId,
        ApplicationFormSurveyDto dto,
        CancellationToken ct)
    {
        var existingResult = await householdSurveys.GetByApplicationIdAsync(applicationId, ct);
        if (existingResult.IsFailure)
        {
            return Result.Failure(existingResult.ErrorCode!, existingResult.Message!);
        }

        var surveyDate = ParseDate(dto.SurveyDate);
        var hasContent = surveyDate is not null
            || !string.IsNullOrWhiteSpace(dto.SurveyorName)
            || !string.IsNullOrWhiteSpace(dto.SurveyorOrganization)
            || !string.IsNullOrWhiteSpace(dto.ApplicationReason)
            || !string.IsNullOrWhiteSpace(dto.ApplicationReasonDetail)
            || !string.IsNullOrWhiteSpace(dto.SurveyNotes)
            || !string.IsNullOrWhiteSpace(dto.RespondentName)
            || !string.IsNullOrWhiteSpace(dto.RespondentRelation)
            || !string.IsNullOrWhiteSpace(dto.SurveyConclusion);

        // 空调查块不落库：避免把 0001-01-01 写成调查日期，影响批次锚点。
        if (!hasContent)
        {
            return Result.Success();
        }

        var existing = existingResult.Value;
        var survey = new HouseholdSurvey
        {
            ApplicationId = applicationId,
            SurveyDate = surveyDate ?? DateTime.Today,
            SurveyorName = dto.SurveyorName ?? string.Empty,
            SurveyorOrganization = dto.SurveyorOrganization ?? string.Empty,
            ApplicationReason = dto.ApplicationReason ?? string.Empty,
            ApplicationReasonDetail = dto.ApplicationReasonDetail ?? string.Empty,
            SurveyNotes = dto.SurveyNotes ?? string.Empty,
            RespondentName = string.IsNullOrWhiteSpace(dto.RespondentName)
                ? existing?.RespondentName ?? string.Empty
                : dto.RespondentName.Trim(),
            RespondentRelation = string.IsNullOrWhiteSpace(dto.RespondentRelation)
                ? existing?.RespondentRelation ?? string.Empty
                : dto.RespondentRelation.Trim(),
            SurveyConclusion = string.IsNullOrWhiteSpace(dto.SurveyConclusion)
                ? string.IsNullOrWhiteSpace(existing?.SurveyConclusion)
                    ? "属实"
                    : existing!.SurveyConclusion
                : dto.SurveyConclusion.Trim()
        };

        var saveResult = await householdSurveys.SaveAsync(applicationId, survey, ct);
        return saveResult.IsFailure
            ? Result.Failure(saveResult.ErrorCode!, saveResult.Message!)
            : Result.Success();
    }

    // ───────────────────────────── 主表字段映射 ─────────────────────────────

    private static void ApplyBasic(ApplicationEntity application, ApplicationFormBasicDto basic)
    {
        application.ApplicantName = (basic.ApplicantName ?? string.Empty).Trim();
        application.ApplicantIdCard = (basic.ApplicantIdCard ?? string.Empty).Trim();
        application.ApplicantPhone = basic.Phone ?? string.Empty;
        application.Gender = NormalizeGender(basic.Gender);
        application.Ethnicity = basic.Ethnicity ?? string.Empty;
        application.HukouType = basic.HukouType ?? string.Empty;
        application.HukouAddress = ComposeHukouAddress(basic);

        // AddressInfo 不落库，但分类判定的户主快照会读取，故仍在内存中填好。
        application.HukouAddressInfo = new AddressInfo
        {
            Province = basic.HukouProvince ?? string.Empty,
            City = basic.HukouCity ?? string.Empty,
            District = basic.HukouDistrict ?? string.Empty,
            Town = basic.HukouTown ?? string.Empty,
            Community = basic.HukouVillage ?? string.Empty,
            Detail = basic.HukouDetailAddress ?? string.Empty
        };

        application.Province = basic.Province ?? string.Empty;
        application.City = basic.City ?? string.Empty;
        application.District = basic.District ?? string.Empty;
        application.Town = basic.Town ?? string.Empty;
        application.Community = basic.Village ?? string.Empty;
        application.Village = basic.Village ?? string.Empty;
        application.Address = basic.Address ?? string.Empty;

        application.ResidentialAddressInfo = new AddressInfo
        {
            Province = application.Province,
            City = application.City,
            District = application.District,
            Town = application.Town,
            Community = application.Community,
            Detail = application.Address
        };

        application.BankName = basic.BankName ?? string.Empty;
        application.BankAccount = basic.BankAccount ?? string.Empty;
        application.DisabilityCardNo = basic.DisabilityCardNo ?? string.Empty;
        application.ApplicationReason = basic.ApplicationReason ?? string.Empty;
        application.ApplicationReasonDetail = basic.ApplicationReasonDetail ?? string.Empty;

        // Step1 扩展字段（户主本人）。成员块若同时携带户主行，ApplyHeadMemberToApplication
        // 会在其后覆盖健康/疾病/残疾三项，保证两处编辑口径一致。
        application.EducationLevel = ToCoreEducationLevel(basic.EducationLevel);
        application.MaritalStatus = ToCoreMaritalStatus(basic.MaritalStatus);
        application.PoliticalStatus = ToCorePoliticalStatus(basic.PoliticalStatus);
        application.HealthStatus = ToCoreHealthStatus(basic.HealthStatus);
        // 疾病口径（与 Core ClassificationService 的主表快照一致）：
        // disease_name = 一级分类 DiseaseCategory，secondary_disease_name = 二级疾病。
        // DiseaseName 仍兼容旧前端（只传 diseaseName 表示二级疾病）。
        application.DiseaseCode = basic.DiseaseCode ?? string.Empty;
        application.DiseaseName = basic.DiseaseCategory ?? string.Empty;
        application.SecondaryDiseaseName = FirstNonEmpty(basic.SecondaryDiseaseName, basic.DiseaseName);
        application.IsSevereDisease = basic.IsSevereDisease;
        application.DisabilityType = ToCoreDisabilityType(basic.DisabilityType);
        application.DisabilityLevel = ToCoreDisabilityLevel(basic.DisabilityLevel);
        application.MedicalSelfPaid12m = basic.MedicalSelfPaid12m;

        // basic.Remark 在主表没有对应列（ApplicationColumns.Full 已对 pg_dump 校验），
        // 契约保留字段以兼容前端模型，但服务端不持久化。
    }

    /// <summary>
    /// 户主成员行不参与成员表全删全插，其健康/残疾/疾病信息以主表为权威列。
    /// 前端 Step1 不含这些字段，Step2 户主卡编辑，保存时回写主表，分类判定与
    /// UpsertHouseholdHeadFromApplicationAsync 才能取到。
    /// </summary>
    private static void ApplyHeadMemberToApplication(
        ApplicationEntity application,
        IEnumerable<ApplicationFormMemberDto> members)
    {
        var list = members as IReadOnlyCollection<ApplicationFormMemberDto> ?? members.ToList();
        if (list.Count == 0)
        {
            return;
        }

        var applicantIdCard = (application.ApplicantIdCard ?? string.Empty).Trim();
        var head = list.FirstOrDefault(m => m.IsApplicant)
                   ?? list.FirstOrDefault(m => m.IsHouseholdHead)
                   ?? list.FirstOrDefault(m => string.Equals(
                       MemberCategoryHelper.Normalize(m.MemberCategory, m.IsApplicant, m.Relation),
                       MemberCategoryConstants.HOUSEHOLD_HEAD,
                       StringComparison.OrdinalIgnoreCase))
                   ?? list.FirstOrDefault(m => applicantIdCard.Length > 0
                       && string.Equals((m.IdCard ?? string.Empty).Trim(), applicantIdCard,
                           StringComparison.OrdinalIgnoreCase));

        if (head is null)
        {
            return;
        }

        application.HealthStatus = ToCoreHealthStatus(head.HealthStatus);
        application.DisabilityType = ToCoreDisabilityType(head.DisabilityType);
        application.DisabilityLevel = ToCoreDisabilityLevel(head.DisabilityLevel);
        // 户主卡疾病与 Step1 同源：一级→disease_name，二级→secondary_disease_name。
        // Step1 是权威：只有主表为空时才用户主卡补空，避免户主行旧值覆盖 Step1 新值。
        if (string.IsNullOrWhiteSpace(application.DiseaseName)
            && !string.IsNullOrWhiteSpace(head.DiseaseCategory))
        {
            application.DiseaseName = head.DiseaseCategory.Trim();
        }
        var headSecondary = FirstNonEmpty(head.SecondaryDiseaseName, head.DiseaseName);
        if (string.IsNullOrWhiteSpace(application.SecondaryDiseaseName) && headSecondary.Length > 0)
        {
            application.SecondaryDiseaseName = headSecondary;
        }
        if (string.IsNullOrWhiteSpace(application.DiseaseCode)
            && !string.IsNullOrWhiteSpace(head.DiseaseCode))
        {
            application.DiseaseCode = head.DiseaseCode.Trim();
        }
        if (string.IsNullOrWhiteSpace(application.DiseaseName)
            && string.IsNullOrWhiteSpace(application.SecondaryDiseaseName)
            && string.IsNullOrWhiteSpace(application.DiseaseCode))
        {
            application.IsSevereDisease = head.IsSevereDisease;
        }

        // Step2 户主卡与 Step1 共用同一人：户主卡有值的新增字段回写主表，
        // 避免“只在成员卡修改”时因户主行不参与全删全插而丢失。
        if (!string.IsNullOrWhiteSpace(head.EducationLevel))
        {
            application.EducationLevel = head.EducationLevel.Trim();
        }
        if (!string.IsNullOrWhiteSpace(head.MaritalStatus))
        {
            application.MaritalStatus = head.MaritalStatus.Trim();
        }
        if (!string.IsNullOrWhiteSpace(head.PoliticalStatus))
        {
            application.PoliticalStatus = head.PoliticalStatus.Trim();
        }
        if (head.MedicalSelfPaid12m is { } medicalSelfPaid)
        {
            application.MedicalSelfPaid12m = medicalSelfPaid;
        }
    }

    private static void ApplyEconomy(
        ApplicationEntity application,
        ApplicationFormEconomyDto? economy,
        int familySize,
        decimal alimonyAnnual,
        IncomeTotals? totals)
    {
        application.FamilySize = familySize;
        // 赡养年值只认 supporters 派生值（旧版口径），前端 economy.AlimonyIncome 一律忽略。
        application.AlimonyIncome = alimonyAnnual;

        if (economy is null || totals is null)
        {
            return;
        }

        application.WorkIncomeTotal = economy.WorkIncome ?? 0m;
        application.BusinessIncomeTotal = economy.BusinessIncome ?? 0m;
        application.PropertyIncomeTotal = economy.PropertyIncome ?? 0m;
        application.TransferIncomeTotal = economy.TransferIncome ?? 0m;
        application.OtherIncomeTotal = economy.OtherIncome ?? 0m;
        application.LandIncomeTotal = economy.LandIncome ?? 0m;
        application.SubsidyTotal = economy.SubsidyTotal ?? 0m;
        application.RigidExpenditure = economy.RigidExpenditure ?? 0m;

        application.FamilyLandArea = economy.FamilyLandArea ?? 0m;
        application.SelfFarmedLandArea = economy.SelfFarmedLandArea ?? 0m;
        application.SubleasedLandArea = economy.SubleasedLandArea ?? 0m;
        application.ContractedLandArea = economy.ContractedLandArea ?? 0m;

        application.TotalAnnualIncome = totals.TotalAnnualIncome;
        application.TotalFamilyIncome = totals.TotalFamilyIncome;
        application.PerCapitaAnnualIncome = totals.PerCapitaAnnualIncome;
        application.PerCapitaIncome = totals.PerCapitaIncome;
    }

    private IncomeTotals CalculateIncome(
        ApplicationFormEconomyDto? economy,
        int familySize,
        decimal alimonyAnnual)
    {
        if (economy is null)
        {
            return new IncomeTotals(0m, 0m, 0m, 0m);
        }

        var totalAnnual = incomeCalculation.CalculateAnnualFamilyIncome(
            economy.WorkIncome ?? 0m,
            economy.BusinessIncome ?? 0m,
            economy.PropertyIncome ?? 0m,
            economy.TransferIncome ?? 0m,
            economy.OtherIncome ?? 0m,
            alimonyAnnual,
            economy.LandIncome ?? 0m,
            economy.SubsidyTotal ?? 0m,
            economy.RigidExpenditure ?? 0m);

        return new IncomeTotals(
            totalAnnual,
            incomeCalculation.MonthlyFromAnnual(totalAnnual),
            incomeCalculation.CalculatePerCapitaAnnual(totalAnnual, familySize),
            incomeCalculation.PerCapitaMonthly(totalAnnual, familySize));
    }

    /// <summary>
    /// 明细口径覆盖主表汇总：工资/经营/财产/转移/其他/补贴/刚性支出取明细求和，
    /// 赡养与土地年值仍取本次 economy（未传则沿用主表现有值），再按 Core 公式重算年/月/人均。
    /// 13 个明细列表全空时不重算，避免把用户手填的 economy 汇总清零。
    /// </summary>
    private void ApplyEconomicDetailsTotals(
        ApplicationEntity application,
        ApplicationFormEconomicDetailsDto? details,
        ApplicationFormEconomyDto? economy,
        int familySize)
    {
        if (details is null || !HasAnyEconomicDetail(details))
        {
            return;
        }

        var workIncome = incomeCalculation.CalculateLaborIncome(details.LaborIncomes ?? []);
        var businessIncome = incomeCalculation.CalculateBusinessNetIncome(details.BusinessIncomes ?? []);
        var propertyIncome = (details.PropertyIncomes ?? []).Sum(x => x.Amount);
        var transferIncome = (details.TransferIncomes ?? []).Sum(x => x.TotalAmount);
        var otherIncome = (details.OtherIncomes ?? []).Sum(x => x.Amount);
        var subsidyIncome = incomeCalculation.CalculateSubsidyIncome(details.Subsidies ?? []);
        var rigidExpenditure = incomeCalculation.CalculateRigidExpenditure(
            details.RigidExpenditures ?? []);

        var alimonyAnnual = application.AlimonyIncome;
        var landIncome = economy?.LandIncome ?? application.LandIncomeTotal;

        var totalAnnual = incomeCalculation.CalculateAnnualFamilyIncome(
            workIncome,
            businessIncome,
            propertyIncome,
            transferIncome,
            otherIncome,
            alimonyAnnual,
            landIncome,
            subsidyIncome,
            rigidExpenditure);

        application.WorkIncomeTotal = workIncome;
        application.BusinessIncomeTotal = businessIncome;
        application.PropertyIncomeTotal = propertyIncome;
        application.TransferIncomeTotal = transferIncome;
        application.OtherIncomeTotal = otherIncome;
        application.SubsidyTotal = subsidyIncome;
        application.RigidExpenditure = rigidExpenditure;

        application.TotalAnnualIncome = totalAnnual;
        application.TotalFamilyIncome = incomeCalculation.MonthlyFromAnnual(totalAnnual);
        application.PerCapitaAnnualIncome = incomeCalculation.CalculatePerCapitaAnnual(totalAnnual, familySize);
        application.PerCapitaIncome = incomeCalculation.PerCapitaMonthly(totalAnnual, familySize);
    }

    private static bool HasAnyEconomicDetail(ApplicationFormEconomicDetailsDto details)
        => (details.LaborIncomes?.Count ?? 0) > 0
           || (details.BusinessIncomes?.Count ?? 0) > 0
           || (details.PropertyIncomes?.Count ?? 0) > 0
           || (details.TransferIncomes?.Count ?? 0) > 0
           || (details.OtherIncomes?.Count ?? 0) > 0
           || (details.Subsidies?.Count ?? 0) > 0
           || (details.BreedingIncomes?.Count ?? 0) > 0
           || (details.RigidExpenditures?.Count ?? 0) > 0
           || (details.FamilyProperties?.Count ?? 0) > 0
           || (details.Vehicles?.Count ?? 0) > 0
           || (details.Machineries?.Count ?? 0) > 0
           || (details.FinancialAssets?.Count ?? 0) > 0
           || (details.LandRegistrations?.Count ?? 0) > 0;

    // ───────────────────────────── 成员/赡养人映射 ─────────────────────────────

    /// <summary>
    /// 成员回填。疾病口径：成员表 disease_name 存一级分类、secondary_disease 存二级疾病
    /// （见 FamilyMemberService.UpsertHouseholdHeadFromApplicationAsync 的主表→成员同步）；
    /// 旧数据只有 disease_category/disease_name 时按旧语义兜底，避免历史值丢失。
    /// 成员表没有 hukou_village 列，户籍村/社区不单独回填。
    /// </summary>
    private static ApplicationFormMemberDto ToMemberDto(
        FamilyMember member,
        ApplicationFormBasicDto? basic)
    {
        // 新数据：disease_category 与 disease_name 同为一级、secondary_disease 为二级；
        // 旧数据：disease_category=一级、disease_name=二级、secondary_disease 为空。
        // 两者都按“一级→DiseaseCategory、二级→DiseaseName/SecondaryDiseaseName”归一，不反向。
        var category = string.IsNullOrWhiteSpace(member.DiseaseCategory)
            ? (member.DiseaseName ?? string.Empty).Trim()
            : member.DiseaseCategory.Trim();
        var secondary = (member.SecondaryDisease ?? string.Empty).Trim();
        if (secondary.Length == 0
            && !string.IsNullOrWhiteSpace(member.DiseaseCategory)
            && !string.IsNullOrWhiteSpace(member.DiseaseName)
            && !string.Equals(member.DiseaseCategory.Trim(), member.DiseaseName.Trim(),
                StringComparison.Ordinal))
        {
            // 旧版行：disease_category=一级、disease_name=二级；仅在两者不同时兜底。
            secondary = member.DiseaseName.Trim();
        }

        return new(
            member.Id,
            member.Name ?? string.Empty,
            member.IdCard ?? string.Empty,
            ToFormRelationship(member.RelationshipToHead),
            member.Gender ?? string.Empty,
            FormatDate(member.BirthDate),
            member.Age,
            member.Ethnicity ?? string.Empty,
            member.Phone ?? string.Empty,
            member.HukouType ?? string.Empty,
            ToFormHealthStatus(member.HealthStatus),
            ToFormEmploymentStatus(member.EmploymentStatus),
            member.WorkUnit ?? string.Empty,
            member.MainIncomeSource ?? string.Empty,
            member.AnnualIncome,
            member.MonthlyIncomeCapacity,
            member.IsHouseholdHead,
            member.IsApplicant,
            member.MemberCategory ?? string.Empty,
            member.IsDisabled,
            ToFormDisabilityType(member.DisabilityType),
            ToFormDisabilityLevel(member.DisabilityLevel),
            secondary,
            member.IsSevereDisease,
            member.IsLaborExempt,
            ToFormEducationLevel(member.EducationLevel),
            ToFormMaritalStatus(member.MaritalStatus),
            ToFormPoliticalStatus(member.PoliticalStatus),
            member.HukouAddress ?? string.Empty,
            member.HomeAddress ?? string.Empty,
            category,
            member.MedicalSelfPaid12m,
            member.DisabilityCertificateNo ?? string.Empty,
            member.PersonType ?? string.Empty,
            member.IsSingleRescue,
            member.SelfCareAbility ?? string.Empty,
            member.DiseaseCode ?? string.Empty,
            secondary,
            member.HomeProvince ?? string.Empty,
            member.HomeCity ?? string.Empty,
            member.HomeDistrict ?? string.Empty,
            member.HomeTown ?? string.Empty,
            member.HomeVillage ?? string.Empty,
            // 共同生活成员的户籍分级在保存时按申请人户籍写入；主表有值时以成员列为准，
            // 空值回落到申请人户籍，避免户主行同步后显示空白。
            FirstNonEmpty(member.HukouProvince, basic?.HukouProvince),
            FirstNonEmpty(member.HukouCity, basic?.HukouCity),
            FirstNonEmpty(member.HukouDistrict, basic?.HukouDistrict),
            FirstNonEmpty(member.HukouTown, basic?.HukouTown));
    }

    /// <summary>
    /// 赡养人回填。年费 = 月费 × 月数 × 申请表家庭人口（familySize 参数）；
    /// 旧数据年费 &gt; 0、月费 = 0（且仍有赡养能力）时按旧版回退月费 = 年费 / 12、月数 = 12，
    /// 避免打开卡片就显示 0 导致保存后赡养收入被清零。
    /// </summary>
    private static ApplicationFormSupporterDto ToSupporterDto(Supporter supporter, int familySize)
    {
        var monthly = supporter.MonthlySupportFee;
        var months = supporter.SupportMonths > 0 ? supporter.SupportMonths : 12;

        if (supporter.IsSupportAbility && monthly <= 0 && supporter.AnnualSupportFee > 0)
        {
            monthly = Math.Round(supporter.AnnualSupportFee / 12m, 2);
            months = 12;
        }

        return new ApplicationFormSupporterDto(
            supporter.Id,
            supporter.Name ?? string.Empty,
            supporter.IdCard ?? string.Empty,
            ToFormRelationship(supporter.Relationship),
            supporter.Gender ?? string.Empty,
            supporter.Age,
            supporter.AnnualSupportFee,
            monthly,
            supporter.IsSupportAbility,
            supporter.PersonType ?? string.Empty,
            supporter.MonthlyIncomeCapacity,
            months,
            familySize > 0 ? familySize : 1,
            supporter.Phone ?? string.Empty,
            supporter.HomeAddress ?? string.Empty);
    }

    private static FamilyMemberCreateRequest ToMemberCreateRequest(
        long applicationId,
        ApplicationFormMemberDto member,
        ApplicationFormBasicDto? basic)
    {
        var healthStatus = ToCoreHealthStatus(member.HealthStatus);
        var disabilityType = ToCoreDisabilityType(member.DisabilityType);
        var disabilityLevel = ToCoreDisabilityLevel(member.DisabilityLevel);

        var createRequest = new FamilyMemberCreateRequest
        {
            ApplicationId = applicationId,
            Name = (member.Name ?? string.Empty).Trim(),
            IdCard = (member.IdCard ?? string.Empty).Trim(),
            Relation = ToCoreRelationship(member.Relation),
            Gender = NormalizeGender(member.Gender),
            Age = member.Age,
            BirthDate = ParseDate(member.BirthDate),
            Ethnicity = member.Ethnicity ?? string.Empty,
            Phone = member.Phone ?? string.Empty,
            HukouType = member.HukouType ?? string.Empty,
            HealthStatus = healthStatus,
            // is_severe_disability 列（CreateRequest 字段历史命名为 HasSevereIllness）
            HasSevereIllness = ClassificationConstants.DisabilityLevel.IsSevereForAssistance(
                disabilityLevel, disabilityType),
            HasDisability = member.HasDisability
                || disabilityType.Length > 0
                || disabilityLevel.Length > 0,
            DisabilityType = disabilityType,
            DisabilityLevel = disabilityLevel,
            // 成员表 disease_name=一级分类（DiseaseCategory），secondary_disease=二级疾病；
            // DiseaseName 字段继续承载二级，兼容旧前端（旧前端只传 diseaseName）。
            DiseaseName = member.DiseaseCategory ?? string.Empty,
            SecondaryDiseaseName = FirstNonEmpty(member.SecondaryDiseaseName, member.DiseaseName),
            DiseaseCode = member.DiseaseCode ?? string.Empty,
            IsSevereDisease = member.IsSevereDisease,
            IsLaborExempt = member.IsLaborExempt,
            EducationLevel = ToCoreEducationLevel(member.EducationLevel),
            MaritalStatus = ToCoreMaritalStatus(member.MaritalStatus),
            PoliticalStatus = ToCorePoliticalStatus(member.PoliticalStatus),
            DiseaseCategory = member.DiseaseCategory ?? string.Empty,
            MedicalSelfPaid12m = member.MedicalSelfPaid12m,
            DisabilityCertificateNo = member.DisabilityCertificateNo ?? string.Empty,
            PersonType = member.PersonType ?? string.Empty,
            HomeProvince = member.HomeProvince ?? string.Empty,
            HomeCity = member.HomeCity ?? string.Empty,
            HomeDistrict = member.HomeDistrict ?? string.Empty,
            HomeTown = member.HomeTown ?? string.Empty,
            HomeVillage = member.HomeVillage ?? string.Empty,
            HomeAddress = member.HomeAddress ?? string.Empty,
            IsHouseholdHead = false,
            MemberCategory = MemberCategoryConstants.SHARED_LIVING,
            EmploymentStatus = ToCoreEmploymentStatus(member.EmploymentStatus),
            WorkUnit = member.WorkUnit ?? string.Empty,
            MainIncomeSource = member.MainIncomeSource ?? string.Empty,
            AnnualIncome = member.AnnualIncome ?? 0m,
            MonthlyIncomeCapacity = member.MonthlyIncomeCapacity ?? 0m,
            IsSupportAbility = true
        };

        // 成员户籍分级：优先成员自己的级联值，缺失时按旧版沿用申请人户籍（共同生活成员口径）。
        var memberHukouAddress = (member.HukouAddress ?? string.Empty).Trim();
        createRequest.HukouProvince = FirstNonEmpty(member.HukouProvince, basic?.HukouProvince);
        createRequest.HukouCity = FirstNonEmpty(member.HukouCity, basic?.HukouCity);
        createRequest.HukouDistrict = FirstNonEmpty(member.HukouDistrict, basic?.HukouDistrict);
        createRequest.HukouTown = FirstNonEmpty(member.HukouTown, basic?.HukouTown);
        // 成员表无 hukou_village 列，village 只在完整地址文本里体现。
        var hukouDetail = memberHukouAddress.Length > 0
            ? memberHukouAddress
            : (basic?.HukouDetailAddress ?? string.Empty).Trim();
        createRequest.HukouAddress = ComposeRegionAddress(
            new[]
            {
                createRequest.HukouProvince, createRequest.HukouCity,
                createRequest.HukouDistrict, createRequest.HukouTown
            },
            hukouDetail);

        return createRequest;
    }

    /// <summary>
    /// 表单未建模的成员列从旧行沿用，避免全删全插导致数据静默丢失。
    /// 表单已建模的列（姓名/身份证/健康/残疾/疾病/就业/收入等）仍以本次请求为准。
    /// </summary>
    private static void PreserveUnmodeledMemberFields(
        FamilyMemberCreateRequest request,
        FamilyMember? existing)
    {
        if (existing is null)
        {
            return;
        }

        // 表单已建模的扩展字段只在本次为空时回填旧值；有值则以请求为准。
        if (string.IsNullOrWhiteSpace(request.MaritalStatus))
        {
            request.MaritalStatus = existing.MaritalStatus;
        }
        if (string.IsNullOrWhiteSpace(request.EducationLevel))
        {
            request.EducationLevel = existing.EducationLevel;
        }
        if (string.IsNullOrWhiteSpace(request.PoliticalStatus))
        {
            request.PoliticalStatus = existing.PoliticalStatus;
        }
        if (string.IsNullOrWhiteSpace(request.DiseaseCategory))
        {
            request.DiseaseCategory = existing.DiseaseCategory;
        }
        if (string.IsNullOrWhiteSpace(request.DisabilityCertificateNo))
        {
            request.DisabilityCertificateNo = existing.DisabilityCertificateNo;
        }
        if (string.IsNullOrWhiteSpace(request.PersonType))
        {
            request.PersonType = existing.PersonType;
        }
        request.MedicalSelfPaid12m ??= existing.MedicalSelfPaid12m;

        // 表单已建模的列：本次为空才沿用旧值，有值以请求为准（否则新增的疾病二级/编码会被旧值覆盖）。
        if (string.IsNullOrWhiteSpace(request.SecondaryDiseaseName))
        {
            request.SecondaryDiseaseName = existing.SecondaryDisease;
        }
        if (string.IsNullOrWhiteSpace(request.DiseaseCode))
        {
            request.DiseaseCode = existing.DiseaseCode;
        }

        // 表单未建模的列仍原样沿用，避免全删全插静默丢失。
        request.WorkCapacity = existing.WorkCapacity;
        request.FamilySize = existing.FamilySize;
        request.AnnualSupportFee = existing.AnnualSupportFee;
        request.MonthlySupportFee = existing.MonthlySupportFee;
        request.SupportMonths = existing.SupportMonths;
        request.IsSupportAbility = existing.IsSupportAbility;

        if (string.IsNullOrWhiteSpace(request.HukouProvince))
        {
            request.HukouProvince = existing.HukouProvince;
        }
        if (string.IsNullOrWhiteSpace(request.HukouCity))
        {
            request.HukouCity = existing.HukouCity;
        }
        if (string.IsNullOrWhiteSpace(request.HukouDistrict))
        {
            request.HukouDistrict = existing.HukouDistrict;
        }
        if (string.IsNullOrWhiteSpace(request.HukouTown))
        {
            request.HukouTown = existing.HukouTown;
        }
        if (string.IsNullOrWhiteSpace(request.HukouAddress))
        {
            request.HukouAddress = existing.HukouAddress;
        }

        // 现住址已建模：逐级按“本次为空则沿用旧值”，否则新增的级联选择会被旧值覆盖。
        if (string.IsNullOrWhiteSpace(request.HomeProvince))
        {
            request.HomeProvince = existing.HomeProvince;
        }
        if (string.IsNullOrWhiteSpace(request.HomeCity))
        {
            request.HomeCity = existing.HomeCity;
        }
        if (string.IsNullOrWhiteSpace(request.HomeDistrict))
        {
            request.HomeDistrict = existing.HomeDistrict;
        }
        if (string.IsNullOrWhiteSpace(request.HomeTown))
        {
            request.HomeTown = existing.HomeTown;
        }
        if (string.IsNullOrWhiteSpace(request.HomeVillage))
        {
            request.HomeVillage = existing.HomeVillage;
        }
        if (string.IsNullOrWhiteSpace(request.HomeAddress))
        {
            request.HomeAddress = existing.HomeAddress;
        }

        if (request.BirthDate is null && existing.BirthDate.Year >= 1900)
        {
            request.BirthDate = existing.BirthDate;
        }
        request.Age ??= existing.Age;
        if (string.IsNullOrWhiteSpace(request.Ethnicity))
        {
            request.Ethnicity = existing.Ethnicity;
        }
        if (string.IsNullOrWhiteSpace(request.Phone))
        {
            request.Phone = existing.Phone;
        }
        if (string.IsNullOrWhiteSpace(request.HukouType))
        {
            request.HukouType = existing.HukouType;
        }
    }

    // ───────────────────────────── DTO 映射 ─────────────────────────────

    /// <summary>
    /// 基本信息回填。地址分级在主表并非全量文本列：
    /// 户籍只有 hukou_address + hukou_*_id，现住有 province/city/district/town/community + *_id。
    /// 先按 ID 取名称，再按 nc_regions_* 名称从合并地址里拆分补齐；拆不出就整段留在详细地址。
    /// </summary>
    private async Task<ApplicationFormBasicDto> ToBasicDtoAsync(
        ApplicationEntity app,
        CancellationToken ct)
    {
        var hukou = await ResolveStoredAddressAsync(
            app.HukouCityId,
            app.HukouCountyId,
            app.HukouTownId,
            app.HukouVillageId,
            string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
            app.HukouAddress,
            ct);

        var home = await ResolveStoredAddressAsync(
            app.CityId,
            app.CountyId,
            app.TownId,
            app.VillageId,
            app.Province,
            app.City,
            app.District,
            app.Town,
            FirstNonEmpty(app.Community, app.Village),
            app.Address,
            ct);

        // disease_name=一级分类、secondary_disease_name=二级疾病（与 Core 主表快照一致）。
        var diseaseCategory = app.DiseaseName ?? string.Empty;
        var secondaryDisease = app.SecondaryDiseaseName ?? string.Empty;

        return new ApplicationFormBasicDto(
            app.HukouType ?? string.Empty,
            app.ApplicantName ?? string.Empty,
            app.ApplicantIdCard ?? string.Empty,
            NormalizeGender(app.Gender),
            app.Ethnicity ?? string.Empty,
            app.ApplicantPhone ?? string.Empty,
            hukou.Province,
            hukou.City,
            hukou.District,
            hukou.Town,
            hukou.Village,
            hukou.Detail,
            home.Province,
            home.City,
            home.District,
            home.Town,
            home.Village,
            home.Detail,
            app.BankName ?? string.Empty,
            app.BankAccount ?? string.Empty,
            app.DisabilityCardNo ?? string.Empty,
            app.ApplicationReason ?? string.Empty,
            app.ApplicationReasonDetail ?? string.Empty,
            string.Empty,
            ToFormEducationLevel(app.EducationLevel),
            ToFormMaritalStatus(app.MaritalStatus),
            ToFormPoliticalStatus(app.PoliticalStatus),
            ToFormHealthStatus(app.HealthStatus),
            secondaryDisease,
            ToFormDisabilityType(app.DisabilityType),
            ToFormDisabilityLevel(app.DisabilityLevel),
            app.MedicalSelfPaid12m,
            app.DiseaseCode ?? string.Empty,
            diseaseCategory,
            secondaryDisease,
            app.IsSevereDisease);
    }

    private static ApplicationFormEconomyDto ToEconomyDto(ApplicationEntity app)
        => new(
            app.WorkIncomeTotal,
            app.BusinessIncomeTotal,
            app.PropertyIncomeTotal,
            app.TransferIncomeTotal,
            app.OtherIncomeTotal,
            app.AlimonyIncome,
            app.LandIncomeTotal,
            app.SubsidyTotal,
            app.RigidExpenditure,
            app.FamilyLandArea,
            app.SelfFarmedLandArea,
            app.SubleasedLandArea,
            app.ContractedLandArea,
            app.TotalAnnualIncome,
            app.TotalFamilyIncome,
            app.PerCapitaAnnualIncome,
            app.PerCapitaIncome);

    private static ApplicationFormSurveyDto ToSurveyDto(HouseholdSurvey? survey)
        => survey is null
            ? new ApplicationFormSurveyDto(
                string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
                string.Empty, string.Empty, string.Empty)
            : new ApplicationFormSurveyDto(
                FormatDate(survey.SurveyDate),
                survey.SurveyorName ?? string.Empty,
                survey.SurveyorOrganization ?? string.Empty,
                survey.ApplicationReason ?? string.Empty,
                survey.ApplicationReasonDetail ?? string.Empty,
                survey.SurveyNotes ?? string.Empty,
                survey.RespondentName ?? string.Empty,
                survey.RespondentRelation ?? string.Empty,
                survey.SurveyConclusion ?? string.Empty);

    private static ApplicationFormEconomicDetailsDto ToEconomicDetailsDto(EconomicDetailData data)
        => new(
            data.LaborIncomes,
            data.BusinessIncomes,
            data.PropertyIncomes,
            data.TransferIncomes,
            data.OtherIncomes,
            data.Subsidies,
            data.BreedingIncomes,
            data.RigidExpenditures,
            data.FamilyProperties,
            data.Vehicles,
            data.Machineries,
            data.FinancialAssets,
            data.LandRegistrations);

    private static ApplicationFormClassificationDto ToClassificationDto(ApplicationEntity app)
    {
        var classificationResult = app.ClassificationResult ?? string.Empty;
        return new ApplicationFormClassificationDto(
            classificationResult,
            ClassificationConstants.ConvertFromCode(classificationResult),
            app.IsEligible,
            // 主表无 ineligible_reason / determination_basis 列；这两个值只在 classify 响应中返回。
            string.Empty,
            string.Empty,
            app.HouseholdMonthlyGuaranteeAmount,
            app.ClassifiedSubsidyAmount,
            app.CaregiverSubsidyAmount,
            app.TotalGuaranteeAmount,
            app.IsInGracePeriod,
            app.GracePeriodMonths,
            FormatDate(app.GracePeriodEndDate));
    }

    // ───────────────────────────── 小工具 ─────────────────────────────

    private sealed record IncomeTotals(
        decimal TotalAnnualIncome,
        decimal TotalFamilyIncome,
        decimal PerCapitaAnnualIncome,
        decimal PerCapitaIncome);

    private static bool IsDraft(string? status)
        => string.Equals(status, ApplicationStatusCodes.DRAFT, StringComparison.OrdinalIgnoreCase);

    private static bool IsSupportMember(FamilyMember member)
        => string.Equals(
            MemberCategoryHelper.Normalize(
                member.MemberCategory, member.IsApplicant, member.RelationshipToHead),
            MemberCategoryConstants.SUPPORT,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>去空格、去空值、去重后追加，供导入接口拼身份证/姓名清单。</summary>
    private static void AddDistinct(List<string> values, HashSet<string> seen, string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        if (seen.Add(trimmed))
        {
            values.Add(trimmed);
        }
    }

    /// <summary>
    /// 家庭人数：非赡养成员数（含户主）；若表单未携带户主行则 +1。
    /// 最低 1，避免主表 family_size=0 影响分类/人均口径。
    /// </summary>
    private static int ResolveFamilySize(IEnumerable<ApplicationFormMemberDto> members)
    {
        var list = members as IReadOnlyCollection<ApplicationFormMemberDto> ?? members.ToList();
        if (list.Count == 0)
        {
            return 1;
        }

        var sharedCount = 0;
        var hasHouseholdHead = false;
        foreach (var member in list)
        {
            var category = MemberCategoryHelper.Normalize(
                member.MemberCategory, member.IsApplicant, member.Relation);
            if (string.Equals(category, MemberCategoryConstants.SUPPORT, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            sharedCount++;
            if (member.IsHouseholdHead
                || member.IsApplicant
                || string.Equals(category, MemberCategoryConstants.HOUSEHOLD_HEAD, StringComparison.OrdinalIgnoreCase))
            {
                hasHouseholdHead = true;
            }
        }

        if (sharedCount == 0)
        {
            return 1;
        }

        return hasHouseholdHead ? sharedCount : sharedCount + 1;
    }

    /// <summary>
    /// 年赡养费口径复用 Core <see cref="Supporter"/> 的联动公式：
    /// 月费 × 月数 × 申请表家庭人口（CalcFamilySize），结果舍入到分。
    /// </summary>
    private static decimal CalculateAnnualSupportFee(decimal monthlySupportFee, int supportMonths, int familySize)
    {
        var months = supportMonths > 0 ? supportMonths : 12;
        var size = familySize > 0 ? familySize : 1;
        var supporter = new Supporter
        {
            MonthlySupportFee = monthlySupportFee,
            SupportMonths = months,
            SupporterFamilySize = size,
            CalcFamilySize = size
        };
        return supporter.AnnualSupportFee;
    }

    /// <summary>
    /// 月赡养费：以本次请求为准；旧客户端只传年费时按旧版回退 月费 = 年费 / 12。
    /// </summary>
    private static decimal ResolveMonthlySupportFee(ApplicationFormSupporterDto dto)
    {
        if (dto.MonthlySupportFee is { } monthly)
        {
            return monthly > 0 ? monthly : 0m;
        }

        if (dto.AnnualSupportFee is { } annual && annual > 0)
        {
            return Math.Round(annual / 12m, 2);
        }

        return 0m;
    }

    /// <summary>
    /// 赡养年收入 = Σ(有赡养能力者的年赡养费)，逐笔年费先舍入、再求和（legacy
    /// IncomeCalculationService.CalculateAlimonyAnnual 口径）。前端 economy.AlimonyIncome 不参与。
    /// </summary>
    private static decimal ResolveAlimonyAnnual(
        IEnumerable<ApplicationFormSupporterDto>? supporterDtos,
        int familySize)
    {
        if (supporterDtos is null)
        {
            return 0m;
        }

        var multiplier = familySize > 0 ? familySize : 1;
        decimal total = 0m;
        foreach (var dto in supporterDtos)
        {
            if (!dto.IsSupportAbility)
            {
                continue;
            }

            var months = dto.SupportMonths is > 0 ? dto.SupportMonths.Value : 12;
            total += CalculateAnnualSupportFee(ResolveMonthlySupportFee(dto), months, multiplier);
        }

        return total;
    }

    /// <summary>
    /// 申请表家庭人口（含户主）：主表 family_size 已有值优先；否则按共同生活成员行现算，
    /// 无户主行时 +1（与前端 Step2「共同生活成员数 + 1」同口径）。
    /// </summary>
    private static int ResolveApplicationFamilySize(
        int storedFamilySize,
        IReadOnlyCollection<FamilyMember> members)
    {
        if (storedFamilySize > 0)
        {
            return storedFamilySize;
        }

        if (members.Count == 0)
        {
            return 1;
        }

        var hasHouseholdHead = members.Any(member =>
            member.IsHouseholdHead
            || member.IsApplicant
            || string.Equals(
                MemberCategoryHelper.Normalize(
                    member.MemberCategory, member.IsApplicant, member.RelationshipToHead),
                MemberCategoryConstants.HOUSEHOLD_HEAD,
                StringComparison.OrdinalIgnoreCase));

        return hasHouseholdHead ? members.Count : members.Count + 1;
    }

    private static string ComposeHukouAddress(ApplicationFormBasicDto basic)
        => ComposeRegionAddress(
            new[]
            {
                basic.HukouProvince, basic.HukouCity, basic.HukouDistrict,
                basic.HukouTown, basic.HukouVillage
            },
            basic.HukouDetailAddress);

    /// <summary>
    /// 五级名称 + 详细地址拼成完整地址；逐级去重，详细地址里已含的层级不再前置，
    /// 避免“林口县林口县…”这类重复。
    /// </summary>
    private static string ComposeRegionAddress(IEnumerable<string?> parts, string? detail)
    {
        var text = (detail ?? string.Empty).Trim();
        var prefix = string.Concat(
            parts
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim())
                .Where(x => !text.Contains(x, StringComparison.Ordinal)));

        return prefix + text;
    }

    // ───────────────────────── 地址解析（名称 ↔ ID / 文本拆分） ─────────────────────────

    private sealed record ResolvedFormAddress(
        string Province,
        string City,
        string District,
        string Town,
        string Village,
        string Detail);

    private sealed record ResolvedRegionIds(
        int? CityId,
        int? CountyId,
        int? TownId,
        int? VillageId);

    /// <summary>
    /// 保存：按五级名称解析主表 ID 列（nc_regions_* 主键）。
    /// 区划数据不可用或市级匹配不到时返回 null，调用方保留原 ID，避免一次查询失败清空历史值。
    /// </summary>
    private async Task<ResolvedRegionIds?> ResolveRegionIdsAsync(
        string? province,
        string? city,
        string? district,
        string? town,
        string? village,
        CancellationToken ct)
    {
        var cityName = (city ?? string.Empty).Trim();
        if (cityName.Length == 0)
        {
            return null;
        }

        var provincesResult = await regions.GetProvincesAsync(ct);
        var provinceEntity = provincesResult.Value?
            .FirstOrDefault(p => RegionNameEquals(p.ProvinceName, province));

        RegionCity? cityEntity;
        if (provinceEntity is not null)
        {
            var citiesResult = await regions.GetCitiesByProvinceIdAsync(provinceEntity.Id, ct);
            cityEntity = citiesResult.Value?
                .FirstOrDefault(c => RegionNameEquals(c.CityName, cityName));
        }
        else
        {
            // 历史数据可能只有市/县没有省：按市名做一次全国匹配。
            var cityResult = await regions.GetCityByNameAsync(cityName, ct);
            cityEntity = cityResult.HasValue && cityResult.Value is { } found ? found : null;
        }

        if (cityEntity is null)
        {
            return null;
        }

        int? countyId = null;
        int? townId = null;
        int? villageId = null;

        var districtName = (district ?? string.Empty).Trim();
        if (districtName.Length > 0)
        {
            var countiesResult = await regions.GetCountiesByCityIdAsync(cityEntity.Id, ct);
            var county = countiesResult.Value?
                .FirstOrDefault(c => RegionNameEquals(c.CountyName, districtName));

            if (county is not null)
            {
                countyId = county.Id;

                var townName = (town ?? string.Empty).Trim();
                if (townName.Length > 0)
                {
                    var townsResult = await regions.GetTownsByCountyIdAsync(county.Id, ct);
                    var townEntity = townsResult.Value?
                        .FirstOrDefault(t => RegionNameEquals(t.TownName, townName));

                    if (townEntity is not null)
                    {
                        townId = townEntity.Id;

                        var villageName = (village ?? string.Empty).Trim();
                        if (villageName.Length > 0)
                        {
                            var villagesResult = await regions.GetVillagesByTownIdAsync(townEntity.Id, ct);
                            villageId = villagesResult.Value?
                                .FirstOrDefault(v => RegionNameEquals(v.VillageName, villageName))?.Id;
                        }
                    }
                }
            }
        }

        return new ResolvedRegionIds(cityEntity.Id, countyId, townId, villageId);
    }

    /// <summary>
    /// 保存：Basic 的户籍/现住名称解析成主表 ID 列。
    /// nc_biz_applications 实际列：hukou_city_id/hukou_county_id/hukou_town_id/hukou_village_id、
    /// city_id/county_id/town_id/village_id（没有 hukou_province_id，也没有 home_* 列）。
    /// </summary>
    private async Task ApplyAddressIdsAsync(
        ApplicationEntity application,
        ApplicationFormBasicDto basic,
        CancellationToken ct)
    {
        var hukou = await ResolveRegionIdsAsync(
            basic.HukouProvince, basic.HukouCity, basic.HukouDistrict,
            basic.HukouTown, basic.HukouVillage, ct);

        if (hukou is not null)
        {
            application.HukouCityId = hukou.CityId;
            application.HukouCountyId = hukou.CountyId;
            application.HukouTownId = hukou.TownId;
            application.HukouVillageId = hukou.VillageId;
        }
        else if (!HasAnyRegionName(
                     basic.HukouProvince, basic.HukouCity, basic.HukouDistrict,
                     basic.HukouTown, basic.HukouVillage))
        {
            // 用户清空了户籍地址：同步清空 ID，避免回填出旧地址。
            application.HukouCityId = null;
            application.HukouCountyId = null;
            application.HukouTownId = null;
            application.HukouVillageId = null;
        }

        var home = await ResolveRegionIdsAsync(
            basic.Province, basic.City, basic.District, basic.Town, basic.Village, ct);

        if (home is not null)
        {
            application.CityId = home.CityId;
            application.CountyId = home.CountyId;
            application.TownId = home.TownId;
            application.VillageId = home.VillageId;
        }
        else if (!HasAnyRegionName(
                     basic.Province, basic.City, basic.District, basic.Town, basic.Village))
        {
            application.CityId = null;
            application.CountyId = null;
            application.TownId = null;
            application.VillageId = null;
        }
    }

    private static bool HasAnyRegionName(params string?[] values)
        => values.Any(v => !string.IsNullOrWhiteSpace(v));

    /// <summary>
    /// 回填：把主表存量地址还原成五级名称 + 详细地址。
    /// 1) 有 ID 的先按 ID 取名称；2) 名称仍缺、地址文本非空时按 nc_regions_* 名称拆分；
    /// 3) 拆不出就把原文整段放进 Detail（不丢原始地址）。
    /// </summary>
    private async Task<ResolvedFormAddress> ResolveStoredAddressAsync(
        int? cityId,
        int? countyId,
        int? townId,
        int? villageId,
        string? provinceName,
        string? cityName,
        string? districtName,
        string? townName,
        string? villageName,
        string? fullAddress,
        CancellationToken ct)
    {
        var province = (provinceName ?? string.Empty).Trim();
        var city = (cityName ?? string.Empty).Trim();
        var district = (districtName ?? string.Empty).Trim();
        var town = (townName ?? string.Empty).Trim();
        var village = (villageName ?? string.Empty).Trim();
        var address = (fullAddress ?? string.Empty).Trim();

        if (cityId is { } cityIdValue)
        {
            var cityResult = await regions.GetCityByIdAsync(cityIdValue, ct);
            if (cityResult.HasValue && cityResult.Value is { } cityEntity)
            {
                if (city.Length == 0)
                {
                    city = (cityEntity.CityName ?? string.Empty).Trim();
                }
                if (province.Length == 0 && cityEntity.ProvinceId is { } provinceId)
                {
                    province = await GetProvinceNameAsync(provinceId, ct);
                }
            }
        }

        if (countyId is { } countyIdValue && district.Length == 0)
        {
            var countyResult = await regions.GetCountyByIdAsync(countyIdValue, ct);
            if (countyResult.HasValue && countyResult.Value is { } countyEntity)
            {
                district = (countyEntity.CountyName ?? string.Empty).Trim();
            }
        }

        if (townId is { } townIdValue && town.Length == 0)
        {
            var townResult = await regions.GetTownByIdAsync(townIdValue, ct);
            if (townResult.HasValue && townResult.Value is { } townEntity)
            {
                town = (townEntity.TownName ?? string.Empty).Trim();
            }
        }

        if (villageId is { } villageIdValue && village.Length == 0)
        {
            var villageResult = await regions.GetVillageByIdAsync(villageIdValue, ct);
            if (villageResult.HasValue && villageResult.Value is { } villageEntity)
            {
                village = (villageEntity.VillageName ?? string.Empty).Trim();
            }
        }

        var detail = address;
        if (address.Length > 0)
        {
            // 主表 hukou_address 是“五级 + 详细”的合并串，即使层级已由 ID 还原也要剥掉前缀，
            // 否则详细地址会重复带上省/市/县。拆不出（只剩门牌等）时保留原文。
            var parsed = await ParseAddressTextAsync(address, ct);
            if (parsed is not null)
            {
                province = province.Length > 0 ? province : parsed.Province;
                city = city.Length > 0 ? city : parsed.City;
                district = district.Length > 0 ? district : parsed.District;
                town = town.Length > 0 ? town : parsed.Town;
                village = village.Length > 0 ? village : parsed.Village;
                detail = parsed.Detail;
            }
        }

        return new ResolvedFormAddress(province, city, district, town, village, detail);
    }

    /// <summary>
    /// 按 nc_regions_* 名称从完整地址文本中逐级拆分（省→市→县→镇→村）。
    /// 省匹配不到时返回 null，调用方保留整段原文。剩余文本作为详细地址。
    /// </summary>
    private async Task<ResolvedFormAddress?> ParseAddressTextAsync(string text, CancellationToken ct)
    {
        var working = text.Trim();
        if (working.Length == 0)
        {
            return null;
        }

        var provincesResult = await regions.GetProvincesAsync(ct);
        var province = MatchRegionByName(provincesResult.Value, text, x => x.ProvinceName);
        if (province is null)
        {
            return null;
        }

        var citiesResult = await regions.GetCitiesByProvinceIdAsync(province.Id, ct);
        var city = MatchRegionByName(citiesResult.Value, text, x => x.CityName);
        if (city is null)
        {
            working = RemoveFirstOccurrence(working, province.ProvinceName);
            return new ResolvedFormAddress(
                province.ProvinceName.Trim(), string.Empty, string.Empty, string.Empty, string.Empty,
                NormalizeDetail(working, text));
        }

        var countiesResult = await regions.GetCountiesByCityIdAsync(city.Id, ct);
        var county = MatchRegionByName(countiesResult.Value, text, x => x.CountyName);

        RegionTown? townEntity = null;
        RegionVillage? villageEntity = null;
        if (county is not null)
        {
            var townsResult = await regions.GetTownsByCountyIdAsync(county.Id, ct);
            townEntity = MatchRegionByName(townsResult.Value, text, x => x.TownName);

            if (townEntity is not null)
            {
                var villagesResult = await regions.GetVillagesByTownIdAsync(townEntity.Id, ct);
                villageEntity = MatchRegionByName(villagesResult.Value, text, x => x.VillageName);
            }
        }

        foreach (var name in new[]
                 {
                     province.ProvinceName, city.CityName, county?.CountyName,
                     townEntity?.TownName, villageEntity?.VillageName
                 })
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                working = RemoveFirstOccurrence(working, name.Trim());
            }
        }

        return new ResolvedFormAddress(
            province.ProvinceName.Trim(),
            city.CityName.Trim(),
            (county?.CountyName ?? string.Empty).Trim(),
            (townEntity?.TownName ?? string.Empty).Trim(),
            (villageEntity?.VillageName ?? string.Empty).Trim(),
            NormalizeDetail(working, text));
    }

    /// <summary>名称匹配：优先取文本中出现的最长名称，避免“林口县/林口镇”互相误配。</summary>
    private static T? MatchRegionByName<T>(
        IEnumerable<T>? options,
        string text,
        Func<T, string?> nameSelector)
        where T : class
    {
        if (options is null || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        T? matched = null;
        var matchedLength = 0;
        foreach (var option in options)
        {
            var name = nameSelector(option)?.Trim();
            if (string.IsNullOrEmpty(name) || !text.Contains(name, StringComparison.Ordinal))
            {
                continue;
            }

            if (name.Length > matchedLength)
            {
                matched = option;
                matchedLength = name.Length;
            }
        }

        return matched;
    }

    private static string RemoveFirstOccurrence(string text, string part)
    {
        var index = text.IndexOf(part, StringComparison.Ordinal);
        return index < 0 ? text : text.Remove(index, part.Length);
    }

    /// <summary>剥离层级名称后剩余的详细地址；没剥离出任何内容时返回空串。</summary>
    private static string NormalizeDetail(string working, string original)
    {
        var detail = working.Trim(' ', '\t', ',', '，', '/', '\\', ';', '；', '-', '—', '、');
        return string.Equals(detail, original.Trim(), StringComparison.Ordinal) ? string.Empty : detail;
    }

    private async Task<string> GetProvinceNameAsync(int provinceId, CancellationToken ct)
    {
        var provincesResult = await regions.GetProvincesAsync(ct);
        return (provincesResult.Value?
            .FirstOrDefault(p => p.Id == provinceId)?.ProvinceName ?? string.Empty).Trim();
    }

    /// <summary>名称相等：先精确，再忽略行政区划后缀（兼容“林口”/“林口县”）。</summary>
    private static bool RegionNameEquals(string? left, string? right)
    {
        var a = (left ?? string.Empty).Trim();
        var b = (right ?? string.Empty).Trim();
        if (a.Length == 0 || b.Length == 0)
        {
            return false;
        }

        if (string.Equals(a, b, StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(NormalizeRegionName(a), NormalizeRegionName(b), StringComparison.Ordinal);
    }

    private static string NormalizeRegionName(string value)
        => value.Trim().TrimEnd('省', '市', '区', '县', '镇', '乡', '村');

    // ── 旧版字典 key ↔ 中文显示值（表单只显示中文，落库保留 key） ────────────────
    private static string ToFormEducationLevel(string? value) => value?.Trim() switch
    {
        "Illiterate" => "文盲/半文盲",
        "Primary" => "小学",
        "JuniorHigh" => "初中",
        "SeniorHigh" => "高中/中专",
        "College" => "大专",
        "Bachelor" => "本科",
        "Postgraduate" => "研究生及以上",
        var other => other ?? string.Empty
    };

    private static string ToCoreEducationLevel(string? value) => value?.Trim() switch
    {
        "文盲/半文盲" => "Illiterate",
        "小学" => "Primary",
        "初中" => "JuniorHigh",
        "高中/中专" => "SeniorHigh",
        "大专" => "College",
        "本科" => "Bachelor",
        "研究生及以上" => "Postgraduate",
        var other => other ?? string.Empty
    };

    private static string ToFormMaritalStatus(string? value) => value?.Trim() switch
    {
        "Single" => "未婚",
        "Married" => "已婚",
        "Widowed" => "丧偶",
        "Divorced" => "离婚",
        "Other" => "其他",
        var other => other ?? string.Empty
    };

    private static string ToCoreMaritalStatus(string? value) => value?.Trim() switch
    {
        "未婚" => "Single",
        "已婚" => "Married",
        "丧偶" => "Widowed",
        "离婚" => "Divorced",
        "其他" => "Other",
        var other => other ?? string.Empty
    };

    private static string ToFormPoliticalStatus(string? value) => value?.Trim() switch
    {
        "CPCMember" => "中共党员",
        "CYLMembers" or "CYLCadre" => "共青团员",
        "Masses" => "群众",
        "DemocraticParty" => "民主党派",
        "Other" => "其他",
        var other => other ?? string.Empty
    };

    private static string ToCorePoliticalStatus(string? value) => value?.Trim() switch
    {
        "中共党员" => "CPCMember",
        "共青团员" => "CYLMembers",
        "群众" => "Masses",
        "民主党派" => "DemocraticParty",
        "其他" => "Other",
        var other => other ?? string.Empty
    };

    private static string ToFormEmploymentStatus(string? value) => value?.Trim() switch
    {
        "Employed" => "在职",
        "Unemployed" => "失业",
        "Farming" => "务农",
        "Student" => "学生",
        "Retired" => "退休",
        "Other" => "其他",
        var other => other ?? string.Empty
    };

    private static string ToCoreEmploymentStatus(string? value) => value?.Trim() switch
    {
        "在职" => "Employed",
        "失业" => "Unemployed",
        "务农" => "Farming",
        "学生" => "Student",
        "退休" => "Retired",
        "其他" => "Other",
        var other => other ?? string.Empty
    };

    private static string ToFormRelationship(string? value) => value?.Trim() switch
    {
        "Head" => "本人/户主",
        "Spouse" => "配偶",
        "Son" => "子/婿",
        "Daughter" => "女/媳",
        "Grandchild" => "孙子女/外孙子女",
        "Parent" => "父母/岳父母/公婆",
        "Grandparent" => "祖父母/外祖父母",
        "Sibling" => "兄弟姐妹",
        "Other" => "其他",
        var other => other ?? string.Empty
    };

    private static string ToCoreRelationship(string? value) => value?.Trim() switch
    {
        "本人/户主" => "Head",
        "配偶" => "Spouse",
        "子/婿" => "Son",
        "女/媳" => "Daughter",
        "孙子女/外孙子女" => "Grandchild",
        "父母/岳父母/公婆" => "Parent",
        "祖父母/外祖父母" => "Grandparent",
        "兄弟姐妹" => "Sibling",
        "其他" => "Other",
        var other => other ?? string.Empty
    };
    /// <summary>历史数据存在 Male/Female 英文代码，表单统一输出/保存为中文。</summary>
    private static string NormalizeGender(string? value) => value?.Trim() switch
    {
        "Male" or "M" or "男" => "男",
        "Female" or "F" or "女" => "女",
        var other => other ?? string.Empty
    };

    private static string FormatDate(DateTime? value)
        => value is { Year: >= 1900 } date
            ? date.ToString("yyyy-MM-dd", DateCulture)
            : string.Empty;

    // 前端下拉使用中文显示值（Step2 选项），Core 判定使用字典 key；在 API 边界双向换算。
    // 已是 key 的值原样透传，兼容导入/历史数据。

    private static string ToCoreHealthStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim() switch
        {
            "健康" or "健康或良好" => HealthStatusConstants.HEALTHY,
            "一般" or "一般或较弱" or "患病" or "残疾" => HealthStatusConstants.FAIR_OR_WEAK,
            "重病" => HealthStatusConstants.SEVERE_DISEASE,
            "重残" => HealthStatusConstants.SEVERE_DISABILITY,
            "重病且重残" => HealthStatusConstants.SEVERE_DISEASE_AND_DISABILITY,
            _ => value.Trim()
        };
    }

    private static string ToFormHealthStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim() switch
        {
            HealthStatusConstants.HEALTHY or "健康或良好" => "健康",
            HealthStatusConstants.FAIR_OR_WEAK or "一般或较弱" => "一般",
            HealthStatusConstants.SEVERE_DISEASE => "重病",
            HealthStatusConstants.SEVERE_DISABILITY => "重残",
            HealthStatusConstants.SEVERE_DISEASE_AND_DISABILITY => "重病且重残",
            _ => value.Trim()
        };
    }

    private static string ToCoreDisabilityType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim() switch
        {
            "视力" => DisabilityConstants.TYPE_VISION,
            "听力" => DisabilityConstants.TYPE_HEARING,
            "言语" => DisabilityConstants.TYPE_SPEECH,
            "肢体" => DisabilityConstants.TYPE_PHYSICAL,
            "智力" => DisabilityConstants.TYPE_INTELLECTUAL,
            "精神" => DisabilityConstants.TYPE_MENTAL,
            "多重" => DisabilityConstants.TYPE_MULTIPLE,
            _ => value.Trim()
        };
    }

    private static string ToFormDisabilityType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim() switch
        {
            DisabilityConstants.TYPE_VISION => "视力",
            DisabilityConstants.TYPE_HEARING => "听力",
            DisabilityConstants.TYPE_SPEECH => "言语",
            DisabilityConstants.TYPE_PHYSICAL => "肢体",
            DisabilityConstants.TYPE_INTELLECTUAL => "智力",
            DisabilityConstants.TYPE_MENTAL => "精神",
            DisabilityConstants.TYPE_MULTIPLE => "多重",
            _ => value.Trim()
        };
    }

    private static string ToCoreDisabilityLevel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim() switch
        {
            "一级" => DisabilityConstants.LEVEL1,
            "二级" => DisabilityConstants.LEVEL2,
            "三级" => DisabilityConstants.LEVEL3,
            "四级" => DisabilityConstants.LEVEL4,
            _ => value.Trim()
        };
    }

    private static string ToFormDisabilityLevel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim() switch
        {
            DisabilityConstants.LEVEL1 => "一级",
            DisabilityConstants.LEVEL2 => "二级",
            DisabilityConstants.LEVEL3 => "三级",
            DisabilityConstants.LEVEL4 => "四级",
            _ => value.Trim()
        };
    }

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParse(
            value.Trim(),
            DateCulture,
            DateTimeStyles.None,
            out var date)
            ? date
            : null;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static Result ToFailure(Result source)
        => Result.Failure(
            string.IsNullOrEmpty(source.ErrorCode) ? ErrorCodes.UNKNOWN_ERROR : source.ErrorCode,
            string.IsNullOrEmpty(source.Message) ? "保存失败" : source.Message);
}
