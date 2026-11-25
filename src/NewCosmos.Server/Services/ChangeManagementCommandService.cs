using System.Globalization;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Requests;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.ChangeManagement;
using NewCosmos.Services.Domain.SocialAssistance;

namespace NewCosmos.Server.Services;

/// <summary>
/// 保障对象动态管理写编排：
/// 户主变更 / 成员变更 / 户主死亡 / 渐退确认 / 核验停止。
/// 业务判定全部交 Core（IChangeService / IGracePeriodService）；本类只做参数校验、
/// 数据范围与状态闸、成员行落库（成员变更需要先落行再调 Core 重新认定）。
/// 成员行与 Core 调用在同一环境事务内，任一步失败整体回滚。
/// </summary>
public static class ChangeManagementCommandService
{
    // ───────────────────────────── 户主变更 ─────────────────────────────

    public static async Task<Result<ChangeOperationResultDto>> HeadChangeAsync(
        IDatabaseService database,
        DataScopeFilterBuilder dataScope,
        IChangeService changes,
        IFamilyMemberService familyMembers,
        ILoggerService logger,
        int userId,
        string operatorName,
        long applicationId,
        HeadChangeRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var scopeFailure = await EnsureInScopeAsync(
                database, dataScope, logger, userId, applicationId, ct);
            if (scopeFailure is not null)
            {
                return Result.Failure<ChangeOperationResultDto>(
                    scopeFailure.ErrorCode!, scopeFailure.Message!);
            }

            if (request.NewHeadMemberId <= 0)
            {
                return Result.Failure<ChangeOperationResultDto>(
                    ErrorCodes.VALIDATION_FAILED, "请选择新户主");
            }

            if (string.IsNullOrWhiteSpace(request.ChangeReason))
            {
                return Result.Failure<ChangeOperationResultDto>(
                    ErrorCodes.VALIDATION_FAILED, "请填写变更原因");
            }

            var appResult = await LoadChangeableApplicationAsync(database, applicationId, ct);
            if (appResult.IsFailure)
            {
                return Result.Failure<ChangeOperationResultDto>(
                    appResult.ErrorCode!, appResult.Message!);
            }

            var application = appResult.Value!;

            var membersResult = await familyMembers.GetByApplicationIdAsync(applicationId, ct);
            if (membersResult.IsFailure)
            {
                return Result.Failure<ChangeOperationResultDto>(
                    membersResult.ErrorCode!, membersResult.Message!);
            }

            var members = membersResult.Value ?? [];
            var headResolution = HouseholdHeadResolver.Resolve(application, members);
            var oldHead = headResolution.Head;
            var newHead = members.FirstOrDefault(m => m.Id == request.NewHeadMemberId);

            if (newHead is null || string.Equals(
                    newHead.MemberCategory, MemberCategoryConstants.SUPPORT, StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure<ChangeOperationResultDto>(
                    ErrorCodes.FAMILY_MEMBER_NOT_FOUND, "新户主必须是本户非赡养共同生活成员");
            }

            if (oldHead is not null && oldHead.Id > 0 && newHead.Id == oldHead.Id)
            {
                return Result.Failure<ChangeOperationResultDto>(
                    ErrorCodes.VALIDATION_FAILED, "新户主不能是当前户主本人");
            }

            if (string.IsNullOrWhiteSpace(newHead.IdCard))
            {
                return Result.Failure<ChangeOperationResultDto>(
                    ErrorCodes.VALIDATION_FAILED, "新户主缺少身份证号，无法建立新档案");
            }

            var result = await changes.ExecuteHeadChangeAsync(new HeadChangeContext
            {
                ApplicationId = applicationId,
                OldHeadMemberId = oldHead?.Id ?? 0,
                NewHeadMemberId = newHead.Id,
                OldHeadName = oldHead?.Name ?? application.ApplicantName ?? string.Empty,
                NewHeadName = newHead.Name,
                ChangeReason = request.ChangeReason.Trim(),
                OperatorName = operatorName
            }, ct);

            return MapChangeResult(result, "户主变更失败");
        }
        catch (Exception ex)
        {
            logger.Error($"户主变更异常: ApplicationId={applicationId}, {ex.Message}");
            return Result.Failure<ChangeOperationResultDto>(
                ErrorCodes.DB_QUERY_ERROR, ex.Message);
        }
    }

    // ───────────────────────────── 家庭成员变更 ─────────────────────────────

    public static async Task<Result<ChangeOperationResultDto>> MemberChangeAsync(
        IDatabaseService database,
        DataScopeFilterBuilder dataScope,
        IChangeService changes,
        IFamilyMemberService familyMembers,
        IIncomeCalculationService incomeCalculation,
        ILoggerService logger,
        int userId,
        string operatorName,
        long applicationId,
        MemberChangeRequest request,
        CancellationToken ct = default)
    {
        var additions = request.Additions?.ToList() ?? [];
        var removals = request.Removals?.ToList() ?? [];
        var modifications = request.Modifications?.ToList() ?? [];

        if (string.IsNullOrWhiteSpace(request.ChangeReason))
        {
            return Result.Failure<ChangeOperationResultDto>(
                ErrorCodes.VALIDATION_FAILED, "请填写变更原因");
        }

        if (additions.Count == 0 && removals.Count == 0 && modifications.Count == 0)
        {
            return Result.Failure<ChangeOperationResultDto>(
                ErrorCodes.VALIDATION_FAILED, "未检测到家庭成员变化（新增、减员或属性变更）");
        }

        try
        {
            var scopeFailure = await EnsureInScopeAsync(
                database, dataScope, logger, userId, applicationId, ct);
            if (scopeFailure is not null)
            {
                return Result.Failure<ChangeOperationResultDto>(
                    scopeFailure.ErrorCode!, scopeFailure.Message!);
            }

            var appResult = await LoadChangeableApplicationAsync(database, applicationId, ct);
            if (appResult.IsFailure)
            {
                return Result.Failure<ChangeOperationResultDto>(
                    appResult.ErrorCode!, appResult.Message!);
            }

            var application = appResult.Value!;

            await using var tx = await database.BeginTransactionScopeAsync(ct);

            var membersResult = await familyMembers.GetByApplicationIdAsync(applicationId, ct);
            if (membersResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeOperationResultDto>(
                    membersResult.ErrorCode!, membersResult.Message!);
            }

            var oldMembers = membersResult.Value ?? [];
            var oldHeadResolution = HouseholdHeadResolver.Resolve(application, oldMembers);
            var oldHead = oldHeadResolution.Head;
            var oldHouseholdCount = oldMembers.Count(m => !IsSupport(m));
            var oldFamilySize = application.FamilySize > 0 ? application.FamilySize : oldHouseholdCount;

            var validation = ValidateMemberOperations(
                oldMembers, oldHead, additions, removals, modifications);
            if (validation is not null)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeOperationResultDto>(
                    validation.ErrorCode!, validation.Message!);
            }

            var entries = new List<MemberChangeEntry>();
            var summaryParts = new List<string>();

            foreach (var removal in removals)
            {
                var member = oldMembers.First(m => m.Id == removal.MemberId);
                var eventDate = ParseDate(removal.EventDate) ?? DateTime.Today;
                TimeSpan? eventTime = null;
                if (MemberChangeReasonConstants.IsDeath(removal.ReasonCode))
                {
                    if (!TryParseTime(removal.EventTime, out var parsedTime))
                    {
                        await tx.RollbackAsync(ct);
                        return Result.Failure<ChangeOperationResultDto>(
                            ErrorCodes.VALIDATION_FAILED,
                            $"减员 {member.Name} 选择「{removal.ReasonName}」时必须填写死亡确切时间");
                    }

                    eventTime = parsedTime;
                    if (eventDate.Date + parsedTime > DateTime.Now)
                    {
                        await tx.RollbackAsync(ct);
                        return Result.Failure<ChangeOperationResultDto>(
                            ErrorCodes.VALIDATION_FAILED,
                            $"减员 {member.Name} 的死亡时间不能晚于当前时间");
                    }
                }

                var deleteResult = await familyMembers.DeleteAsync(member.Id, ct);
                if (deleteResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<ChangeOperationResultDto>(
                        deleteResult.ErrorCode!, deleteResult.Message!);
                }

                summaryParts.Add($"减员 {member.Name}（{removal.ReasonName}）");
                entries.Add(new MemberChangeEntry
                {
                    Direction = DictionaryConstants.ChangeType.MEMBER_REMOVE,
                    MemberId = member.Id,
                    Name = member.Name,
                    IdCard = member.IdCard,
                    RelationshipToHead = member.RelationshipToHead,
                    MemberCategory = member.MemberCategory,
                    ReasonCode = removal.ReasonCode.Trim(),
                    ReasonName = removal.ReasonName.Trim(),
                    EventDate = eventDate,
                    EventTime = eventTime,
                    Remark = removal.Remark ?? string.Empty
                });
            }

            foreach (var modification in modifications)
            {
                var member = oldMembers.First(m => m.Id == modification.MemberId);
                var merged = BuildUpdateRequest(member, modification);
                var updateResult = await familyMembers.UpdateAsync(merged, ct);
                if (updateResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<ChangeOperationResultDto>(
                        updateResult.ErrorCode!, updateResult.Message!);
                }

                summaryParts.Add($"属性变更 {member.Name}（{modification.Reason.Trim()}）");
                entries.Add(new MemberChangeEntry
                {
                    Direction = DictionaryConstants.ChangeType.MEMBER_ATTRIBUTE,
                    MemberId = member.Id,
                    Name = member.Name,
                    IdCard = member.IdCard,
                    RelationshipToHead = string.IsNullOrWhiteSpace(modification.RelationshipToHead)
                        ? member.RelationshipToHead
                        : modification.RelationshipToHead.Trim(),
                    MemberCategory = member.MemberCategory,
                    ReasonCode = "Attribute",
                    ReasonName = modification.Reason.Trim(),
                    EventDate = DateTime.Today,
                    Remark = modification.Remark ?? string.Empty
                });
            }

            foreach (var addition in additions)
            {
                var birthDate = ParseDate(addition.BirthDate)
                                 ?? IdCardValidator.ExtractBirthDate(addition.IdCard);
                var gender = string.IsNullOrWhiteSpace(addition.Gender)
                    ? IdCardValidator.ExtractGender(addition.IdCard) ?? string.Empty
                    : NormalizeGender(addition.Gender);
                var age = addition.Age ?? IdCardValidator.ExtractAgeBasic(addition.IdCard);
                var eventDate = ParseDate(addition.EventDate) ?? DateTime.Today;

                var createRequest = new FamilyMemberCreateRequest
                {
                    ApplicationId = applicationId,
                    Name = addition.Name.Trim(),
                    IdCard = addition.IdCard.Trim().ToUpperInvariant(),
                    Relation = addition.RelationshipToHead.Trim(),
                    Gender = gender,
                    Age = age,
                    BirthDate = birthDate,
                    HealthStatus = addition.HealthStatus ?? string.Empty,
                    Phone = addition.Phone ?? string.Empty,
                    IsHouseholdHead = false,
                    MemberCategory = MemberCategoryConstants.SHARED_LIVING,
                    WorkCapacity = string.Empty,
                    CreatedBy = operatorName
                };

                var addResult = await familyMembers.AddAsync(createRequest, ct);
                if (addResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<ChangeOperationResultDto>(
                        addResult.ErrorCode!, addResult.Message!);
                }

                summaryParts.Add($"新增 {createRequest.Name}（{addition.ReasonName.Trim()}）");
                entries.Add(new MemberChangeEntry
                {
                    Direction = DictionaryConstants.ChangeType.MEMBER_ADD,
                    MemberId = addResult.Value,
                    Name = createRequest.Name,
                    IdCard = createRequest.IdCard,
                    RelationshipToHead = createRequest.Relation,
                    MemberCategory = MemberCategoryConstants.SHARED_LIVING,
                    ReasonCode = addition.ReasonCode.Trim(),
                    ReasonName = addition.ReasonName.Trim(),
                    EventDate = eventDate,
                    Remark = addition.Remark ?? string.Empty
                });
            }

            var refreshedResult = await familyMembers.GetByApplicationIdAsync(applicationId, ct);
            if (refreshedResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeOperationResultDto>(
                    refreshedResult.ErrorCode!, refreshedResult.Message!);
            }

            var refreshed = refreshedResult.Value ?? [];
            var household = refreshed.Where(m => !IsSupport(m)).ToList();
            var refreshedHead = HouseholdHeadResolver.Resolve(application, household);
            var newFamilySize = household.Count + (refreshedHead.IsVirtual ? 1 : 0);
            if (newFamilySize <= 0)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeOperationResultDto>(
                    ErrorCodes.VALIDATION_FAILED, "变更后家庭共同生活成员不能为空");
            }

            var changeType = additions.Count > 0 && removals.Count == 0 && modifications.Count == 0
                ? DictionaryConstants.ChangeType.MEMBER_ADD
                : removals.Count > 0 && additions.Count == 0 && modifications.Count == 0
                    ? DictionaryConstants.ChangeType.MEMBER_REMOVE
                    : DictionaryConstants.ChangeType.MEMBER_MODIFY;

            var totalAnnualIncome = application.TotalAnnualIncome;
            var context = new MemberChangeContext
            {
                ApplicationId = applicationId,
                ChangeType = changeType,
                ChangeSummary = string.Join("；", summaryParts),
                ChangeReason = request.ChangeReason.Trim(),
                Entries = entries,
                OldFamilySize = oldFamilySize,
                OldClassification = application.ClassificationResult,
                OldIsEligible = application.IsEligible,
                OldGuaranteeAmount = application.TotalGuaranteeAmount,
                OldHouseholdMonthlyGuaranteeAmount = application.HouseholdMonthlyGuaranteeAmount,
                OldClassifiedSubsidyAmount = application.ClassifiedSubsidyAmount,
                OldCaregiverSubsidyAmount = application.CaregiverSubsidyAmount,
                OldTotalFamilyIncome = application.TotalFamilyIncome,
                OldPerCapitaIncome = application.PerCapitaIncome,
                OldRigidExpenditure = application.RigidExpenditure,
                OldTotalAnnualIncome = application.TotalAnnualIncome,
                OldPerCapitaAnnualIncome = application.PerCapitaAnnualIncome,
                OldComponents = new IncomeComponentValues
                {
                    WorkIncomeTotal = application.WorkIncomeTotal,
                    BusinessIncomeTotal = application.BusinessIncomeTotal,
                    PropertyIncomeTotal = application.PropertyIncomeTotal,
                    TransferIncomeTotal = application.TransferIncomeTotal,
                    OtherIncomeTotal = application.OtherIncomeTotal,
                    RigidExpenditure = application.RigidExpenditure,
                    AlimonyIncome = application.AlimonyIncome,
                    LandIncomeTotal = application.LandIncomeTotal,
                    SubsidyTotal = application.SubsidyTotal
                },
                NewTotalAnnualIncome = totalAnnualIncome,
                NewPerCapitaAnnualIncome = incomeCalculation.CalculatePerCapitaAnnual(
                    totalAnnualIncome, newFamilySize),
                NewTotalFamilyIncome = incomeCalculation.MonthlyFromAnnual(totalAnnualIncome),
                NewPerCapitaIncome = incomeCalculation.PerCapitaMonthly(
                    totalAnnualIncome, newFamilySize),
                NewRigidExpenditure = application.RigidExpenditure,
                NewFamilySize = newFamilySize,
                OperatorName = operatorName
            };

            var changeResult = await changes.ExecuteMemberChangeAsync(context, ct);
            if (changeResult.IsFailure || changeResult.Value is null || !changeResult.Value.IsSuccess)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeOperationResultDto>(
                    changeResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    changeResult.Message ?? "家庭成员变更失败");
            }

            await tx.CommitAsync(ct);
            return Result.Success(MapChangeResultValue(changeResult.Value));
        }
        catch (Exception ex)
        {
            logger.Error($"家庭成员变更异常: ApplicationId={applicationId}, {ex.Message}");
            return Result.Failure<ChangeOperationResultDto>(
                ErrorCodes.DB_QUERY_ERROR, ex.Message);
        }
    }

    // ───────────────────────────── 户主死亡变更 ─────────────────────────────

    public static async Task<Result<ChangeOperationResultDto>> HouseholdDeathAsync(
        IDatabaseService database,
        DataScopeFilterBuilder dataScope,
        IChangeService changes,
        IFamilyMemberService familyMembers,
        ILoggerService logger,
        int userId,
        string operatorName,
        long applicationId,
        HouseholdDeathRequest request,
        CancellationToken ct = default)
    {
        var deathDate = ParseDate(request.DeathDate);
        if (deathDate is null)
        {
            return Result.Failure<ChangeOperationResultDto>(
                ErrorCodes.VALIDATION_FAILED, "请选择死亡日期");
        }

        if (!TryParseTime(request.DeathTime, out var deathTime))
        {
            return Result.Failure<ChangeOperationResultDto>(
                ErrorCodes.VALIDATION_FAILED, "请选择死亡确切时间");
        }

        var deathAt = deathDate.Value.Date + deathTime;
        if (deathAt > DateTime.Now)
        {
            return Result.Failure<ChangeOperationResultDto>(
                ErrorCodes.VALIDATION_FAILED, "死亡时间不能晚于当前时间");
        }

        if (string.IsNullOrWhiteSpace(request.DeathReason))
        {
            return Result.Failure<ChangeOperationResultDto>(
                ErrorCodes.VALIDATION_FAILED, "请选择死亡原因");
        }

        try
        {
            var scopeFailure = await EnsureInScopeAsync(
                database, dataScope, logger, userId, applicationId, ct);
            if (scopeFailure is not null)
            {
                return Result.Failure<ChangeOperationResultDto>(
                    scopeFailure.ErrorCode!, scopeFailure.Message!);
            }

            var appResult = await LoadChangeableApplicationAsync(database, applicationId, ct);
            if (appResult.IsFailure)
            {
                return Result.Failure<ChangeOperationResultDto>(
                    appResult.ErrorCode!, appResult.Message!);
            }

            var application = appResult.Value!;
            var membersResult = await familyMembers.GetByApplicationIdAsync(applicationId, ct);
            if (membersResult.IsFailure)
            {
                return Result.Failure<ChangeOperationResultDto>(
                    membersResult.ErrorCode!, membersResult.Message!);
            }

            var members = membersResult.Value ?? [];
            var headResolution = HouseholdHeadResolver.Resolve(
                application, members, request.DeceasedHeadMemberId ?? 0);
            var deceasedHead = headResolution.Head;
            if (deceasedHead is null)
            {
                return Result.Failure<ChangeOperationResultDto>(
                    ErrorCodes.FAMILY_MEMBER_NOT_FOUND, "未找到死亡原户主成员记录");
            }

            var eligible = members
                .Where(m => m.Id != deceasedHead.Id && !IsSupport(m))
                .ToList();

            var singlePersonHousehold = eligible.Count == 0;
            long newHeadMemberId = 0;
            FamilyMember? newHead = null;

            if (!singlePersonHousehold)
            {
                newHeadMemberId = request.NewHeadMemberId ?? 0;
                newHead = eligible.FirstOrDefault(m => m.Id == newHeadMemberId);
                if (newHead is null)
                {
                    return Result.Failure<ChangeOperationResultDto>(
                        ErrorCodes.VALIDATION_FAILED, "请选择新户主");
                }
            }

            var context = new HouseholdDeathContext
            {
                ApplicationId = applicationId,
                DeceasedHeadMemberId = deceasedHead.Id,
                DeceasedHeadName = deceasedHead.Name,
                DeceasedHeadIdCard = deceasedHead.IdCard,
                NewHeadMemberId = newHeadMemberId,
                NewHeadName = newHead?.Name ?? string.Empty,
                NewHeadIdCard = newHead?.IdCard ?? string.Empty,
                DeathDate = deathDate.Value.Date,
                DeathTime = deathTime,
                DeathReason = request.DeathReason.Trim(),
                DeathCertificateNo = request.DeathCertificateNo?.Trim() ?? string.Empty,
                OperatorName = operatorName
            };

            var result = singlePersonHousehold
                ? await changes.ExecuteHouseholdHeadDeathStopOnlyAsync(context, ct)
                : await changes.ExecuteHouseholdDeathAsync(context, ct);

            return MapChangeResult(result, "户主死亡变更失败");
        }
        catch (Exception ex)
        {
            logger.Error($"户主死亡变更异常: ApplicationId={applicationId}, {ex.Message}");
            return Result.Failure<ChangeOperationResultDto>(
                ErrorCodes.DB_QUERY_ERROR, ex.Message);
        }
    }

    // ───────────────────────────── 渐退确认 ─────────────────────────────

    public static async Task<Result<GraceConfirmResultDto>> GraceConfirmAsync(
        IDatabaseService database,
        DataScopeFilterBuilder dataScope,
        IGracePeriodService gracePeriods,
        ILoggerService logger,
        int userId,
        long applicationId,
        GraceConfirmRequest request,
        CancellationToken ct = default)
    {
        if (!GracePeriodConstants.IsValidMonths(request.Months))
        {
            return Result.Failure<GraceConfirmResultDto>(
                ErrorCodes.VALIDATION_FAILED, "渐退期月数必须在 1–12 之间");
        }

        try
        {
            var scopeFailure = await EnsureInScopeAsync(
                database, dataScope, logger, userId, applicationId, ct);
            if (scopeFailure is not null)
            {
                return Result.Failure<GraceConfirmResultDto>(
                    scopeFailure.ErrorCode!, scopeFailure.Message!);
            }

            var appResult = await LoadChangeableApplicationAsync(database, applicationId, ct);
            if (appResult.IsFailure)
            {
                return Result.Failure<GraceConfirmResultDto>(
                    appResult.ErrorCode!, appResult.Message!);
            }

            var activeResult = await gracePeriods.GetActiveAsync(applicationId, ct);
            if (activeResult.IsFailure)
            {
                return Result.Failure<GraceConfirmResultDto>(
                    activeResult.ErrorCode!, activeResult.Message!);
            }

            var active = activeResult.Value;
            if (active is null)
            {
                return Result.Failure<GraceConfirmResultDto>(
                    ErrorCodes.VALIDATION_FAILED,
                    "当前无进行中的渐退期。渐退资格须在经济状况复核 Step5 分类判定后确认，本页不能凭空创建渐退期。");
            }

            var startDate = active.StartDate
                            ?? gracePeriods.CalculatePeriod(request.Months).GracePeriodStartDate;
            var endDate = startDate.AddMonths(request.Months).AddDays(-1);

            var activateResult = await gracePeriods.ActivateAsync(
                applicationId,
                request.Months,
                startDate,
                endDate,
                active.OriginalClassification,
                active.OriginalGuaranteeAmount,
                active.GraceGrantAmount,
                ct);
            if (activateResult.IsFailure)
            {
                return Result.Failure<GraceConfirmResultDto>(
                    activateResult.ErrorCode!, activateResult.Message!);
            }

            return Result.Success(new GraceConfirmResultDto(
                true, request.Months, startDate, endDate));
        }
        catch (Exception ex)
        {
            logger.Error($"渐退确认异常: ApplicationId={applicationId}, {ex.Message}");
            return Result.Failure<GraceConfirmResultDto>(
                ErrorCodes.DB_QUERY_ERROR, ex.Message);
        }
    }

    // ───────────────────────────── 核验停止 ─────────────────────────────

    public static async Task<Result<ChangeOperationResultDto>> VerifyStopAsync(
        IDatabaseService database,
        DataScopeFilterBuilder dataScope,
        IChangeService changes,
        ILoggerService logger,
        int userId,
        string operatorName,
        long applicationId,
        VerifyStopRequest request,
        CancellationToken ct = default)
    {
        var stopDate = ParseDate(request.StopDate) ?? DateTime.Today;
        if (stopDate > DateTime.Today)
        {
            return Result.Failure<ChangeOperationResultDto>(
                ErrorCodes.VALIDATION_FAILED, "停止日期不能晚于今天");
        }

        try
        {
            var scopeFailure = await EnsureInScopeAsync(
                database, dataScope, logger, userId, applicationId, ct);
            if (scopeFailure is not null)
            {
                return Result.Failure<ChangeOperationResultDto>(
                    scopeFailure.ErrorCode!, scopeFailure.Message!);
            }

            var appResult = await LoadChangeableApplicationAsync(database, applicationId, ct);
            if (appResult.IsFailure)
            {
                return Result.Failure<ChangeOperationResultDto>(
                    appResult.ErrorCode!, appResult.Message!);
            }

            var result = await changes.ExecuteVerifyStopAsync(new VerifyStopContext
            {
                ApplicationId = applicationId,
                StopDate = stopDate,
                OperatorName = operatorName
            }, ct);

            return MapChangeResult(result, "核验停止失败");
        }
        catch (Exception ex)
        {
            logger.Error($"核验停止异常: ApplicationId={applicationId}, {ex.Message}");
            return Result.Failure<ChangeOperationResultDto>(
                ErrorCodes.DB_QUERY_ERROR, ex.Message);
        }
    }

    // ───────────────────────────── 共用 ─────────────────────────────

    private static async Task<Result?> EnsureInScopeAsync(
        IDatabaseService database,
        DataScopeFilterBuilder dataScope,
        ILoggerService logger,
        int userId,
        long applicationId,
        CancellationToken ct)
    {
        if (applicationId <= 0)
        {
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "申请 ID 无效");
        }

        var scopeResult = await GracePeriodQueryService.IsApplicationInScopeAsync(
            database, dataScope, logger, userId, applicationId, ct);
        if (scopeResult.IsFailure)
        {
            return Result.Failure(scopeResult.ErrorCode!, scopeResult.Message!);
        }

        if (!scopeResult.Value)
        {
            return Result.Failure(ErrorCodes.NOT_FOUND, "申请档案不存在或不在数据范围内");
        }

        return null;
    }

    /// <summary>
    /// 写接口状态闸：只有已审批/已完成（在保）档案可办理变更；
    /// 与旧版 ChangeViewModel.EnsureChangeableStatusAsync 同口径。
    /// </summary>
    private static async Task<Result<Application>> LoadChangeableApplicationAsync(
        IDatabaseService database,
        long applicationId,
        CancellationToken ct)
    {
        var appResult = await database.QuerySingleAsync<Application>(
            $"SELECT {ApplicationColumns.Full} FROM nc_biz_applications " +
            "WHERE id = $1 AND deleted_at IS NULL",
            ct,
            applicationId);
        if (appResult.IsFailure)
        {
            return Result.Failure<Application>(
                appResult.ErrorCode!, appResult.Message!);
        }

        var application = appResult.Value;
        if (application is null)
        {
            return Result.Failure<Application>(
                ErrorCodes.APPLICATION_NOT_FOUND, "申请不存在");
        }

        var status = (application.Status ?? string.Empty).Trim();
        if (!string.Equals(status, ApplicationStatusCodes.APPROVED, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(status, ApplicationStatusCodes.COMPLETED, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<Application>(
                ErrorCodes.INVALID_TRANSITION,
                $"档案当前状态为{ApplicationQueryService.ToStatusText(status)}，仅已审批/已完成档案可办理动态管理业务");
        }

        return Result.Success(application);
    }

    private static Result? ValidateMemberOperations(
        IReadOnlyList<FamilyMember> members,
        FamilyMember? head,
        IReadOnlyList<MemberAddRequest> additions,
        IReadOnlyList<MemberRemoveRequest> removals,
        IReadOnlyList<MemberModifyRequest> modifications)
    {
        var duplicateIdCards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var addition in additions)
        {
            var name = addition.Name?.Trim() ?? string.Empty;
            var idCard = addition.IdCard?.Trim() ?? string.Empty;
            if (name.Length == 0 || idCard.Length == 0)
            {
                return Result.Failure(
                    ErrorCodes.VALIDATION_FAILED, "新增成员必须填写姓名与身份证号");
            }

            if (!IdCardValidator.IsValid(idCard))
            {
                return Result.Failure(
                    ErrorCodes.INVALID_ID_CARD, $"新增成员 {name} 的身份证号不合法");
            }

            if (members.Any(m => string.Equals(m.IdCard, idCard, StringComparison.OrdinalIgnoreCase))
                || !duplicateIdCards.Add(idCard))
            {
                return Result.Failure(
                    ErrorCodes.DUPLICATE_ID_CARD, $"新增成员 {name} 的身份证号已在本户或本次新增清单中存在");
            }

            if (string.IsNullOrWhiteSpace(addition.RelationshipToHead))
            {
                return Result.Failure(
                    ErrorCodes.VALIDATION_FAILED, $"新增成员 {name} 未选择与户主关系");
            }

            if (IsHeadRelationship(addition.RelationshipToHead))
            {
                return Result.Failure(
                    ErrorCodes.VALIDATION_FAILED,
                    $"新增成员 {name} 不能直接登记为户主，请使用「户主变更」办理");
            }

            if (string.IsNullOrWhiteSpace(addition.ReasonCode)
                || string.IsNullOrWhiteSpace(addition.ReasonName))
            {
                return Result.Failure(
                    ErrorCodes.VALIDATION_FAILED, $"新增成员 {name} 未登记增员原因");
            }
        }

        foreach (var removal in removals)
        {
            var member = members.FirstOrDefault(m => m.Id == removal.MemberId);
            if (member is null)
            {
                return Result.Failure(
                    ErrorCodes.FAMILY_MEMBER_NOT_FOUND, "待减员成员不存在或已删除");
            }

            if (IsSupport(member))
            {
                return Result.Failure(
                    ErrorCodes.VALIDATION_FAILED,
                    $"成员 {member.Name} 是赡养抚养扶养人，请在赡养人维护中处理");
            }

            if (head is not null && member.Id == head.Id)
            {
                return Result.Failure(
                    ErrorCodes.MEMBER_IS_HEAD, "户主不能直接减员，请先办理户主变更");
            }

            if (string.IsNullOrWhiteSpace(removal.ReasonCode)
                || string.IsNullOrWhiteSpace(removal.ReasonName))
            {
                return Result.Failure(
                    ErrorCodes.VALIDATION_FAILED, $"减员成员 {member.Name} 未登记减员原因");
            }
        }

        foreach (var modification in modifications)
        {
            var member = members.FirstOrDefault(m => m.Id == modification.MemberId);
            if (member is null)
            {
                return Result.Failure(
                    ErrorCodes.FAMILY_MEMBER_NOT_FOUND, "待修改成员不存在或已删除");
            }

            if (IsSupport(member))
            {
                return Result.Failure(
                    ErrorCodes.VALIDATION_FAILED,
                    $"成员 {member.Name} 是赡养抚养扶养人，请在赡养人维护中处理");
            }

            if (string.IsNullOrWhiteSpace(modification.Reason))
            {
                return Result.Failure(
                    ErrorCodes.VALIDATION_FAILED, $"成员 {member.Name} 未填写属性变更原因");
            }

            if (modification.RelationshipToHead is not null)
            {
                var isHead = (head is not null && member.Id == head.Id)
                             || ChangeManagementQueryService.IsHeadMember(member);
                if (!isHead && IsHeadRelationship(modification.RelationshipToHead))
                {
                    return Result.Failure(
                        ErrorCodes.VALIDATION_FAILED,
                        $"成员 {member.Name} 不能通过属性变更登记为户主，请使用「户主变更」办理");
                }

                if (isHead && !IsHeadRelationship(modification.RelationshipToHead))
                {
                    return Result.Failure(
                        ErrorCodes.VALIDATION_FAILED,
                        $"户主 {member.Name} 与户主关系不能通过属性变更修改");
                }
            }

            if (!HasAttributeChange(member, modification))
            {
                return Result.Failure(
                    ErrorCodes.VALIDATION_FAILED, $"成员 {member.Name} 未检测到属性变化");
            }
        }

        return null;
    }

    private static bool HasAttributeChange(FamilyMember member, MemberModifyRequest modification)
    {
        static bool Diff(string current, string? next)
            => next is not null && !string.Equals(current ?? string.Empty, next.Trim(), StringComparison.Ordinal);

        return Diff(member.RelationshipToHead, modification.RelationshipToHead)
               || Diff(member.HealthStatus, modification.HealthStatus)
               || Diff(member.EmploymentStatus, modification.EmploymentStatus)
               || Diff(member.WorkCapacity, modification.WorkCapacity)
               || Diff(member.Phone, modification.Phone)
               || Diff(member.WorkUnit, modification.WorkUnit)
               || Diff(member.EducationLevel, modification.EducationLevel)
               || Diff(member.MaritalStatus, modification.MaritalStatus)
               || Diff(member.PoliticalStatus, modification.PoliticalStatus);
    }

    private static FamilyMemberUpdateRequest BuildUpdateRequest(
        FamilyMember member,
        MemberModifyRequest modification)
        => new()
        {
            MemberId = member.Id,
            Name = member.Name,
            IdCard = member.IdCard,
            Relation = ValueOrCurrent(member.RelationshipToHead, modification.RelationshipToHead),
            Gender = member.Gender,
            Age = NewCosmos.Server.Services.ChangeManagementQueryService.ResolveAge(member),
            BirthDate = member.BirthDate == default ? null : member.BirthDate,
            Ethnicity = member.Ethnicity,
            Phone = ValueOrCurrent(member.Phone, modification.Phone),
            HukouType = member.HukouType,
            HukouAddress = member.HukouAddress,
            HomeProvince = member.HomeProvince,
            HomeCity = member.HomeCity,
            HomeDistrict = member.HomeDistrict,
            HomeTown = member.HomeTown,
            HomeVillage = member.HomeVillage,
            HomeAddress = member.HomeAddress,
            HukouProvince = member.HukouProvince,
            HukouCity = member.HukouCity,
            HukouDistrict = member.HukouDistrict,
            HukouTown = member.HukouTown,
            MaritalStatus = ValueOrCurrent(member.MaritalStatus, modification.MaritalStatus),
            EducationLevel = ValueOrCurrent(member.EducationLevel, modification.EducationLevel),
            PoliticalStatus = ValueOrCurrent(member.PoliticalStatus, modification.PoliticalStatus),
            HealthStatus = ValueOrCurrent(member.HealthStatus, modification.HealthStatus),
            HasSevereIllness = member.HasSevereIllness,
            HasDisability = member.IsDisabled || member.IsSevereDisability,
            DisabilityType = member.DisabilityType,
            DisabilityLevel = member.DisabilityLevel,
            DisabilityCertificateNo = member.DisabilityCertificateNo,
            DiseaseCategory = member.DiseaseCategory,
            DiseaseName = member.DiseaseName,
            SecondaryDiseaseName = member.SecondaryDisease,
            DiseaseCode = member.DiseaseCode,
            IsSevereDisease = member.IsSevereDisease,
            MedicalSelfPaid12m = member.MedicalSelfPaid12m,
            IsLaborExempt = member.IsLaborExempt,
            IsHouseholdHead = member.IsApplicant || member.IsHouseholdHead,
            MemberCategory = member.MemberCategory,
            EmploymentStatus = ValueOrCurrent(member.EmploymentStatus, modification.EmploymentStatus),
            WorkUnit = ValueOrCurrent(member.WorkUnit, modification.WorkUnit),
            MainIncomeSource = member.MainIncomeSource,
            AnnualIncome = member.AnnualIncome,
            WorkCapacity = ValueOrCurrent(member.WorkCapacity, modification.WorkCapacity),
            MonthlyIncomeCapacity = member.MonthlyIncomeCapacity,
            FamilySize = member.SupporterFamilySize,
            PersonType = member.PersonType,
            AnnualSupportFee = member.AnnualSupportFee,
            MonthlySupportFee = member.MonthlySupportFee,
            SupportMonths = member.SupportMonths,
            IsSupportAbility = member.IsSupportAbility,
            UpdatedBy = string.Empty
        };

    private static string ValueOrCurrent(string current, string? next)
        => next is null ? current : next.Trim();

    private static string NormalizeGender(string? value) => value?.Trim() switch
    {
        "Male" or "M" or "男" => "男",
        "Female" or "F" or "女" => "女",
        var other => other ?? string.Empty
    };

    private static bool IsSupport(FamilyMember member)
        => string.Equals(
            member.MemberCategory, MemberCategoryConstants.SUPPORT,
            StringComparison.OrdinalIgnoreCase);

    private static bool IsHeadRelationship(string? relationship)
        => (relationship ?? string.Empty).Trim() switch
        {
            "Head" or "户主" or "本人" or "本人/户主" => true,
            _ => false
        };

    private static Result<ChangeOperationResultDto> MapChangeResult(
        Result<ChangeResult> result,
        string fallbackMessage)
    {
        if (result.IsFailure || result.Value is null || !result.Value.IsSuccess)
        {
            return Result.Failure<ChangeOperationResultDto>(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? fallbackMessage);
        }

        return Result.Success(MapChangeResultValue(result.Value));
    }

    private static ChangeOperationResultDto MapChangeResultValue(ChangeResult value)
        => new(
            value.ChangeId,
            value.IsSuccess,
            value.OldClassification,
            ClassificationConstants.ConvertFromCode(value.OldClassification),
            value.NewClassification,
            ClassificationConstants.ConvertFromCode(value.NewClassification),
            value.OldGuaranteeAmount,
            value.NewGuaranteeAmount,
            value.TriggeredGracePeriod,
            value.TriggeredStop,
            value.Rebuilt,
            value.NewApplicationId);

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParseExact(
            value.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? date.Date
            : null;
    }

    private static bool TryParseTime(string? value, out TimeSpan time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var formats = new[] { @"hh\:mm", @"hh\:mm\:ss", @"h\:mm", @"h\:mm\:ss" };
        return TimeSpan.TryParseExact(
            value.Trim(),
            formats,
            CultureInfo.InvariantCulture,
            out time);
    }
}
