using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ElderlyBenefits;
using NewCosmos.Services.System;

namespace NewCosmos.Server.Services;

/// <summary>
/// 高龄津贴登记写服务：表单回填、判类/补发评估、导入名册查询、创建/更新/确认/删除。
/// <para>
/// 业务口径（判类、月标准、补发分段）一律复用 Core：
/// <see cref="IElderlyApplicationService"/>、<see cref="ElderlyPaybackCalculator"/>；
/// 本类只做表单校验、请求字段到实体的装配与区划名称/ID 互转，禁止重抄金额公式。
/// </para>
/// <para>
/// 与旧桌面端（legacy ElderlyApplicationFormViewModel）保持一致：
/// 非特殊情况补发直接采信 Core 评估结果；特殊情况按人工起止月逐月重算分段。
/// </para>
/// </summary>
public sealed class ElderlyApplicationWriteService(
    IElderlyApplicationService elderlyService,
    IRegionService regionService,
    ILoggerService logger)
{
    /// <summary>补发分段逐月取标准时使用的四个年龄档（顺序与 Core 评估一致）。</summary>
    private static readonly string[] SegmentCategories =
    {
        ElderlyBenefitConstants.CatOtherElderly,
        ElderlyBenefitConstants.CatLowSubsidy,
        ElderlyBenefitConstants.Cat90To99,
        ElderlyBenefitConstants.Cat100Plus
    };

    // ───────────────────────────── 表单回填 ─────────────────────────────

    public async Task<Result<ElderlyApplicationFormResponse>> GetFormAsync(long id, CancellationToken ct = default)
    {
        var applicationResult = await elderlyService.GetByIdAsync(id, ct);
        if (applicationResult.IsFailure)
        {
            return Result.Failure<ElderlyApplicationFormResponse>(
                applicationResult.ErrorCode!, applicationResult.Message!);
        }

        if (applicationResult.Value is null)
        {
            return Result.Failure<ElderlyApplicationFormResponse>(
                ErrorCodes.RECORD_NOT_FOUND, "登记记录不存在或已删除");
        }

        var application = applicationResult.Value;

        var hukou = await ResolveRegionNamesAsync(
            application.HukouCityId, application.HukouCountyId,
            application.HukouTownId, application.HukouVillageId, ct);
        var family = await ResolveRegionNamesAsync(
            application.FamilyCityId, application.FamilyCountyId,
            application.FamilyTownId, application.FamilyVillageId, ct);

        var segmentsResult = await elderlyService.GetSegmentsAsync(id, ct);
        if (segmentsResult.IsFailure)
        {
            return Result.Failure<ElderlyApplicationFormResponse>(
                segmentsResult.ErrorCode!, segmentsResult.Message!);
        }

        return Result.Success(new ElderlyApplicationFormResponse
        {
            Application = application,
            HukouProvince = hukou.Province,
            HukouCity = hukou.City,
            HukouCounty = hukou.County,
            HukouTown = hukou.Town,
            HukouVillage = hukou.Village,
            FamilyProvince = family.Province,
            FamilyCity = family.City,
            FamilyCounty = family.County,
            FamilyTown = family.Town,
            FamilyVillage = family.Village,
            Segments = segmentsResult.Value ?? new List<ElderlyPaybackSegment>()
        });
    }

    // ───────────────────────────── 评估 ─────────────────────────────

    /// <summary>按身份证判类 + 自动补发计算（受理日期缺省当天）。</summary>
    public async Task<Result<ElderlyEvaluateResult>> EvaluateAsync(
        string? idCard, DateTime applyDate, CancellationToken ct = default)
    {
        var normalized = (idCard ?? string.Empty).Trim();
        if (!IdCardValidator.IsValid(normalized))
        {
            return Result.Failure<ElderlyEvaluateResult>(
                ErrorCodes.VALIDATION_FAILED, "身份证号码无效");
        }

        return await elderlyService.EvaluateAsync(normalized, applyDate, ct);
    }

    /// <summary>
    /// 特殊情况补发试算：按人工起止月逐月取年龄档月标准并切段。
    /// 身份档优先取 Core 评估结果（评估失败时按「无高补贴身份」降级，月标准缺失仍显式失败）。
    /// </summary>
    public async Task<Result<ElderlyPaybackResult>> EvaluateSpecialPaybackAsync(
        string? idCard, string? startMonth, string? endMonth, CancellationToken ct = default)
    {
        var normalized = (idCard ?? string.Empty).Trim();
        if (!IdCardValidator.IsValid(normalized))
        {
            return Result.Failure<ElderlyPaybackResult>(
                ErrorCodes.VALIDATION_FAILED, "身份证号码无效");
        }

        if (!TryParseMonth(startMonth, out var start))
        {
            return Result.Failure<ElderlyPaybackResult>(
                ErrorCodes.VALIDATION_FAILED, "补发起算月格式应为 yyyy-MM（如 2026-03）");
        }

        if (!TryParseMonth(endMonth, out var end))
        {
            return Result.Failure<ElderlyPaybackResult>(
                ErrorCodes.VALIDATION_FAILED, "补发止算月格式应为 yyyy-MM（如 2026-08）");
        }

        if (start > end)
        {
            return Result.Failure<ElderlyPaybackResult>(
                ErrorCodes.VALIDATION_FAILED, "补发起算月不能晚于止算月");
        }

        var birthDate = IdCardValidator.ExtractBirthDate(normalized);
        if (birthDate is null)
        {
            return Result.Failure<ElderlyPaybackResult>(
                ErrorCodes.VALIDATION_FAILED, "身份证号码无效，无法提取出生日期");
        }

        var identityFlag = ElderlyBenefitConstants.IdentityNone;
        var evaluateResult = await elderlyService.EvaluateAsync(normalized, start, ct);
        if (evaluateResult.IsSuccess && evaluateResult.Value is not null)
        {
            identityFlag = evaluateResult.Value.IdentityFlag;
        }
        else
        {
            // 特殊情况可能早于满80周岁或跨越配置缺口：身份只影响 80-89 档高低标准，
            // 评估失败不中断试算，但月标准读取失败仍会在下一步显式返回。
            logger.Warn($"特殊情况补发试算：身份评估未成功，按无高补贴身份计算（{evaluateResult.ErrorCode}）");
        }

        var standards = await LoadStandardMapAsync(ct);
        var segments = BuildSegmentsFromRange(birthDate.Value, start, end, identityFlag, standards);
        if (segments.Any(s => s.MonthlyAmount <= 0))
        {
            return Result.Failure<ElderlyPaybackResult>(
                ErrorCodes.CONFIG_NOT_FOUND, "部分年龄档位的补贴标准未配置，请先在标准配置管理中维护");
        }

        return Result.Success(new ElderlyPaybackResult
        {
            StartMonth = ToMonthString(start),
            EndMonth = ToMonthString(end),
            TotalMonths = segments.Sum(s => s.Months),
            TotalAmount = segments.Sum(s => s.SegmentAmount),
            Segments = segments
        });
    }

    // ───────────────────────────── 导入名册 ─────────────────────────────

    public async Task<Result<ElderlyImportedSearchItem?>> GetImportedByIdCardAsync(
        string? idCard, CancellationToken ct = default)
    {
        var normalized = (idCard ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return Result.Failure<ElderlyImportedSearchItem?>(
                ErrorCodes.VALIDATION_FAILED, "请填写身份证号");
        }

        return await elderlyService.GetImportedByIdCardAsync(normalized, ct);
    }

    /// <summary>导入名册关键字查询；空关键字直接返回空列表，最多 50 条。</summary>
    public async Task<Result<List<ElderlyImportedSearchItem>>> SearchImportedAsync(
        string? keyword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return Result.Success(new List<ElderlyImportedSearchItem>());
        }

        var result = await elderlyService.SearchImportedLibraryAsync(keyword.Trim(), ct);
        if (result.IsFailure)
        {
            return Result.Failure<List<ElderlyImportedSearchItem>>(
                result.ErrorCode!, result.Message!);
        }

        return Result.Success((result.Value ?? new List<ElderlyImportedSearchItem>())
            .Take(50)
            .ToList());
    }

    // ───────────────────────────── 写操作 ─────────────────────────────

    public async Task<Result<ElderlyApplicationCreatedDto>> CreateAsync(
        int userId,
        string operatorName,
        ElderlyApplicationFormRequest request,
        CancellationToken ct = default)
    {
        if (Validate(request) is { } validation)
        {
            return Result.Failure<ElderlyApplicationCreatedDto>(
                ErrorCodes.VALIDATION_FAILED, validation);
        }

        var idCard = Trim(request.IdCard);

        var existsResult = await elderlyService.CheckIdCardExistsAsync(idCard, null, ct);
        if (existsResult.IsFailure)
        {
            return Result.Failure<ElderlyApplicationCreatedDto>(
                existsResult.ErrorCode!, existsResult.Message!);
        }

        if (existsResult.Value)
        {
            return Result.Failure<ElderlyApplicationCreatedDto>(
                ErrorCodes.DUPLICATE_ID_CARD, "该身份证号已登记");
        }

        var applyDate = (request.ApplyDate ?? DateTime.Today).Date;

        var evaluateResult = await EvaluateAsync(idCard, applyDate, ct);
        if (evaluateResult.IsFailure)
        {
            return Result.Failure<ElderlyApplicationCreatedDto>(
                evaluateResult.ErrorCode!, evaluateResult.Message!);
        }

        var evaluation = evaluateResult.Value;
        var category = evaluation.Category;
        var monthlyAmount = evaluation.MonthlyAmount;

        if (request.IsCategoryManual && !string.IsNullOrWhiteSpace(request.Category))
        {
            category = Trim(request.Category);
            var amountResult = await elderlyService.GetMonthlyAmountAsync(category, ct);
            if (amountResult.IsFailure)
            {
                return Result.Failure<ElderlyApplicationCreatedDto>(
                    amountResult.ErrorCode!, amountResult.Message!);
            }

            monthlyAmount = amountResult.Value;
        }

        var paybackResult = await BuildPaybackAsync(request, evaluation, idCard, ct);
        if (paybackResult.IsFailure)
        {
            return Result.Failure<ElderlyApplicationCreatedDto>(
                paybackResult.ErrorCode!, paybackResult.Message!);
        }

        var payback = paybackResult.Value;

        var entity = await BuildEntityAsync(
            request, evaluation, category, monthlyAmount, payback, applyDate, operatorName, ct);

        var createResult = await elderlyService.CreateAsync(entity, payback.Segments, ct);
        if (createResult.IsFailure)
        {
            return Result.Failure<ElderlyApplicationCreatedDto>(
                createResult.ErrorCode!, createResult.Message!);
        }

        var createdId = createResult.Value;
        var applicationNo = string.Empty;
        var createdResult = await elderlyService.GetByIdAsync(createdId, ct);
        if (createdResult.IsSuccess && createdResult.Value is not null)
        {
            applicationNo = createdResult.Value.ApplicationNo;
        }

        logger.LogBusiness("高龄登记创建成功",
            ("UserId", userId),
            ("ApplicationId", createdId),
            ("ApplicationNo", applicationNo));

        return Result.Success(new ElderlyApplicationCreatedDto(createdId, applicationNo));
    }

    public async Task<Result> UpdateAsync(
        long id,
        string operatorName,
        ElderlyApplicationFormRequest request,
        CancellationToken ct = default)
    {
        var existingResult = await elderlyService.GetByIdAsync(id, ct);
        if (existingResult.IsFailure)
        {
            return Result.Failure(existingResult.ErrorCode!, existingResult.Message!);
        }

        if (existingResult.Value is null)
        {
            return Result.Failure(ErrorCodes.RECORD_NOT_FOUND, "登记记录不存在或已删除");
        }

        var existing = existingResult.Value;

        if (Validate(request) is { } validation)
        {
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, validation);
        }

        var idCard = Trim(request.IdCard);

        var existsResult = await elderlyService.CheckIdCardExistsAsync(idCard, id, ct);
        if (existsResult.IsFailure)
        {
            return Result.Failure(existsResult.ErrorCode!, existsResult.Message!);
        }

        if (existsResult.Value)
        {
            return Result.Failure(ErrorCodes.DUPLICATE_ID_CARD, "该身份证号已登记");
        }

        var applyDate = (request.ApplyDate ?? existing.ApplyDate ?? DateTime.Today).Date;

        var evaluateResult = await EvaluateAsync(idCard, applyDate, ct);
        if (evaluateResult.IsFailure)
        {
            return Result.Failure(evaluateResult.ErrorCode!, evaluateResult.Message!);
        }

        var evaluation = evaluateResult.Value;
        var category = evaluation.Category;
        var monthlyAmount = evaluation.MonthlyAmount;

        if (request.IsCategoryManual && !string.IsNullOrWhiteSpace(request.Category))
        {
            category = Trim(request.Category);
            var amountResult = await elderlyService.GetMonthlyAmountAsync(category, ct);
            if (amountResult.IsFailure)
            {
                return Result.Failure(amountResult.ErrorCode!, amountResult.Message!);
            }

            monthlyAmount = amountResult.Value;
        }

        var paybackResult = await BuildPaybackAsync(request, evaluation, idCard, ct);
        if (paybackResult.IsFailure)
        {
            return Result.Failure(paybackResult.ErrorCode!, paybackResult.Message!);
        }

        var payback = paybackResult.Value;

        var entity = await BuildEntityAsync(
            request, evaluation, category, monthlyAmount, payback, applyDate, operatorName, ct);

        entity.Id = id;
        entity.ApplicationNo = existing.ApplicationNo;
        entity.Status = existing.Status;
        entity.CreatedAt = existing.CreatedAt;
        entity.CreatedBy = existing.CreatedBy;
        entity.UpdatedBy = operatorName;

        var updateResult = await elderlyService.UpdateAsync(entity, payback.Segments, ct);
        if (updateResult.IsFailure)
        {
            return updateResult;
        }

        logger.LogBusiness("高龄登记更新成功",
            ("ApplicationId", id),
            ("ApplicationNo", entity.ApplicationNo));

        return Result.Success();
    }

    public Task<Result> ConfirmAsync(long id, string operatorName, CancellationToken ct = default)
        => elderlyService.ConfirmAsync(id, operatorName, ct);

    public Task<Result> DeleteAsync(long id, CancellationToken ct = default)
        => elderlyService.DeleteAsync(id, ct);

    // ───────────────────────────── 校验 ─────────────────────────────

    /// <summary>与旧桌面端 Validate 同口径；返回 null 表示通过。</summary>
    private static string? Validate(ElderlyApplicationFormRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return "请填写申请人姓名";
        if (string.IsNullOrWhiteSpace(request.IdCard)) return "请填写身份证号码";
        if (!IdCardValidator.IsValid(Trim(request.IdCard))) return "身份证号码无效";
        if (string.IsNullOrWhiteSpace(request.Phone)) return "请填写联系电话";
        if (string.IsNullOrWhiteSpace(request.HukouCity)) return "请选择户籍城市";
        if (string.IsNullOrWhiteSpace(request.HukouCounty)) return "请选择户籍区县";
        if (string.IsNullOrWhiteSpace(request.HukouTown)) return "请选择户籍乡镇";
        if (string.IsNullOrWhiteSpace(request.HukouVillage)) return "请选择户籍村/社区";
        if (string.IsNullOrWhiteSpace(request.BankName)) return "请填写社保卡开户行";
        if (string.IsNullOrWhiteSpace(request.BankAccount)) return "请填写社保卡账号";

        if (!request.IsSpecialCase) return null;

        if (string.IsNullOrWhiteSpace(request.SpecialReason))
        {
            return "请填写特殊情况原因说明";
        }

        var hasStart = !string.IsNullOrWhiteSpace(request.PaybackStartMonth);
        var hasEnd = !string.IsNullOrWhiteSpace(request.PaybackEndMonth);
        if (hasStart != hasEnd)
        {
            return "特殊情况：补发起算月与止算月需同时填写，或同时留空表示无补发";
        }

        if (!hasStart) return null;

        if (!TryParseMonth(request.PaybackStartMonth, out var start)
            || !TryParseMonth(request.PaybackEndMonth, out var end))
        {
            return "补发起止月格式应为 yyyy-MM（如 2026-03）";
        }

        return start > end ? "补发起算月不能晚于止算月" : null;
    }

    // ───────────────────────────── 补发 ─────────────────────────────

    private async Task<Result<ElderlyPaybackResult>> BuildPaybackAsync(
        ElderlyApplicationFormRequest request,
        ElderlyEvaluateResult evaluation,
        string idCard,
        CancellationToken ct)
    {
        if (!request.IsSpecialCase)
        {
            var automatic = evaluation.Payback ?? new ElderlyPaybackResult();
            return Result.Success(new ElderlyPaybackResult
            {
                StartMonth = automatic.StartMonth,
                EndMonth = automatic.EndMonth,
                TotalMonths = automatic.TotalMonths,
                TotalAmount = automatic.TotalAmount,
                Segments = automatic.Segments ?? new List<ElderlyPaybackSegment>()
            });
        }

        // 特殊情况：起止月都空 = 明确无补发
        if (string.IsNullOrWhiteSpace(request.PaybackStartMonth)
            && string.IsNullOrWhiteSpace(request.PaybackEndMonth))
        {
            return Result.Success(new ElderlyPaybackResult());
        }

        if (!TryParseMonth(request.PaybackStartMonth, out var start)
            || !TryParseMonth(request.PaybackEndMonth, out var end))
        {
            return Result.Failure<ElderlyPaybackResult>(
                ErrorCodes.VALIDATION_FAILED, "补发起止月格式应为 yyyy-MM（如 2026-03）");
        }

        if (start > end)
        {
            return Result.Failure<ElderlyPaybackResult>(
                ErrorCodes.VALIDATION_FAILED, "补发起算月不能晚于止算月");
        }

        var birthDate = evaluation.BirthDate ?? IdCardValidator.ExtractBirthDate(idCard);
        if (birthDate is null)
        {
            return Result.Failure<ElderlyPaybackResult>(
                ErrorCodes.VALIDATION_FAILED, "身份证号码无效，无法提取出生日期");
        }

        var standards = await LoadStandardMapAsync(ct);
        var segments = BuildSegmentsFromRange(
            birthDate.Value, start, end, evaluation.IdentityFlag, standards);
        if (segments.Any(s => s.MonthlyAmount <= 0))
        {
            return Result.Failure<ElderlyPaybackResult>(
                ErrorCodes.CONFIG_NOT_FOUND, "部分年龄档位的补贴标准未配置，请先在标准配置管理中维护");
        }

        return Result.Success(new ElderlyPaybackResult
        {
            StartMonth = ToMonthString(start),
            EndMonth = ToMonthString(end),
            TotalMonths = segments.Sum(s => s.Months),
            TotalAmount = segments.Sum(s => s.SegmentAmount),
            Segments = segments
        });
    }

    /// <summary>
    /// 逐月按年龄档取月标准并切段（与旧版 BuildSegmentsFromRange 算法一致；
    /// 年龄/类别判定仍走 Core 的 ElderlyPaybackCalculator）。
    /// </summary>
    private static List<ElderlyPaybackSegment> BuildSegmentsFromRange(
        DateTime birthDate,
        DateTime start,
        DateTime end,
        string? identityFlag,
        IReadOnlyDictionary<string, decimal> standardMap)
    {
        var segments = new List<ElderlyPaybackSegment>();
        ElderlyPaybackSegment? current = null;
        var cursor = start;

        while (cursor <= end)
        {
            var age = ElderlyPaybackCalculator.GetAgeAtMonth(birthDate, cursor);
            var category = ElderlyPaybackCalculator.GetCategoryForAge(age, identityFlag);
            var amount = standardMap.GetValueOrDefault(category);

            if (current is null || current.CategoryCode != category || current.MonthlyAmount != amount)
            {
                current = new ElderlyPaybackSegment
                {
                    CategoryCode = category,
                    MonthlyAmount = amount,
                    SegmentStartMonth = ToMonthString(cursor)
                };
                segments.Add(current);
            }

            current.SegmentEndMonth = ToMonthString(cursor);
            current.Months++;
            current.SegmentAmount = current.MonthlyAmount * current.Months;
            cursor = cursor.AddMonths(1);
        }

        return segments;
    }

    /// <summary>预取四个年龄档月标准（缺配置按 0 处理，由调用方校验实际使用段是否有效）。</summary>
    private async Task<Dictionary<string, decimal>> LoadStandardMapAsync(CancellationToken ct)
    {
        var map = new Dictionary<string, decimal>(SegmentCategories.Length);
        foreach (var category in SegmentCategories)
        {
            var result = await elderlyService.GetMonthlyAmountAsync(category, ct);
            if (result.IsFailure)
            {
                logger.Warn($"读取高龄月标准失败（{category}），本次分段按 0 处理: {result.ErrorCode}");
            }

            map[category] = result.IsSuccess ? result.Value : 0m;
        }

        return map;
    }

    // ───────────────────────────── 实体装配 ─────────────────────────────

    private async Task<ElderlyApplication> BuildEntityAsync(
        ElderlyApplicationFormRequest request,
        ElderlyEvaluateResult evaluation,
        string category,
        decimal monthlyAmount,
        ElderlyPaybackResult payback,
        DateTime applyDate,
        string operatorName,
        CancellationToken ct)
    {
        var hukouVillage = Trim(request.HukouVillage);
        var hukou = await ResolveRegionIdsAsync(
            request.HukouCity, request.HukouCounty, request.HukouTown, hukouVillage, ct);
        var family = await ResolveRegionIdsAsync(
            request.FamilyCity, request.FamilyCounty, request.FamilyTown, request.FamilyVillage, ct);

        var hukouAddress = JoinRegionText(
            request.HukouCity, request.HukouCounty, request.HukouTown, hukouVillage);
        var familyAddress = JoinRegionText(
            request.FamilyCity, request.FamilyCounty, request.FamilyTown,
            request.FamilyVillage, request.DetailAddress);

        var isSpecial = request.IsSpecialCase;

        return new ElderlyApplication
        {
            Name = Trim(request.Name),
            IdCard = Trim(request.IdCard),
            Gender = string.IsNullOrWhiteSpace(evaluation.Gender) ? Trim(request.Gender) : evaluation.Gender,
            BirthDate = evaluation.BirthDate ?? request.BirthDate,
            Phone = Trim(request.Phone),

            HukouAddress = hukouAddress,
            HukouVillage = hukouVillage,
            HukouCityId = hukou.CityId,
            HukouCountyId = hukou.CountyId,
            HukouTownId = hukou.TownId,
            HukouVillageId = hukou.VillageId,
            HukouDetailAddress = Trim(request.HukouDetailAddress),

            FamilyAddress = familyAddress,
            FamilyCityId = family.CityId,
            FamilyCountyId = family.CountyId,
            FamilyTownId = family.TownId,
            FamilyVillageId = family.VillageId,
            DetailAddress = Trim(request.DetailAddress),

            BankName = Trim(request.BankName),
            BankAccount = Trim(request.BankAccount),
            AgentName = Trim(request.AgentName),
            AgentRelation = Trim(request.AgentRelation),
            AgentReceiveName = Trim(request.AgentReceiveName),
            AgentReceiveRelation = Trim(request.AgentReceiveRelation),
            AgentReceiveBankName = Trim(request.AgentReceiveBankName),
            AgentReceiveBankAccount = Trim(request.AgentReceiveBankAccount),
            AgentReceiveReason = Trim(request.AgentReceiveReason),

            Category = category,
            IdentityFlag = evaluation.IdentityFlag,
            IdentitySource = evaluation.IdentitySource,
            IsCategoryManual = request.IsCategoryManual,
            IssueStartMonth = evaluation.IssueStartMonth,
            IssueAmount = monthlyAmount,

            PaybackStartMonth = payback.StartMonth,
            PaybackEndMonth = payback.EndMonth,
            AutoStartMonth = isSpecial ? Trim(request.PaybackStartMonth) : payback.StartMonth,
            AutoEndMonth = isSpecial ? Trim(request.PaybackEndMonth) : payback.EndMonth,
            PaybackMonths = payback.TotalMonths,
            PaybackAmount = payback.TotalAmount,
            PaybackReason = Trim(request.PaybackReason),
            IsSpecialCase = isSpecial,
            SpecialReason = Trim(request.SpecialReason),

            Status = ElderlyBenefitConstants.StatusDraft,
            ApplyDate = applyDate,
            CreatedBy = operatorName,
            UpdatedBy = operatorName
        };
    }

    // ───────────────────────────── 区划 ─────────────────────────────

    private sealed record RegionNames(string Province, string City, string County, string Town, string Village)
    {
        public static RegionNames Empty { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
    }

    private readonly record struct RegionIds(int? CityId, int? CountyId, int? TownId, int? VillageId)
    {
        public static RegionIds Empty => new(null, null, null, null);
    }

    /// <summary>按 ID 反查四级名称；省份由城市 ProvinceId 反查，解析不到回落默认省。</summary>
    private async Task<RegionNames> ResolveRegionNamesAsync(
        int? cityId, int? countyId, int? townId, int? villageId, CancellationToken ct)
    {
        var province = DefaultValuesConstants.HOME_PROVINCE;
        var city = string.Empty;
        var county = string.Empty;
        var town = string.Empty;
        var village = string.Empty;

        if (cityId is > 0)
        {
            var cityResult = await regionService.GetCityByIdAsync(cityId.Value, ct);
            if (cityResult.IsSuccess && cityResult.Value is not null)
            {
                city = cityResult.Value.CityName;
                province = await ResolveProvinceNameAsync(cityResult.Value.ProvinceId, ct);
            }
        }

        if (countyId is > 0)
        {
            var countyResult = await regionService.GetCountyByIdAsync(countyId.Value, ct);
            if (countyResult.IsSuccess && countyResult.Value is not null)
            {
                county = countyResult.Value.CountyName;
            }
        }

        if (townId is > 0)
        {
            var townResult = await regionService.GetTownByIdAsync(townId.Value, ct);
            if (townResult.IsSuccess && townResult.Value is not null)
            {
                town = townResult.Value.TownName;
            }
        }

        if (villageId is > 0)
        {
            var villageResult = await regionService.GetVillageByIdAsync(villageId.Value, ct);
            if (villageResult.IsSuccess && villageResult.Value is not null)
            {
                village = villageResult.Value.VillageName;
            }
        }

        return new RegionNames(province, city, county, town, village);
    }

    private async Task<string> ResolveProvinceNameAsync(int? provinceId, CancellationToken ct)
    {
        if (provinceId is > 0)
        {
            var provincesResult = await regionService.GetProvincesAsync(ct);
            if (provincesResult.IsSuccess)
            {
                var province = provincesResult.Value?
                    .FirstOrDefault(p => p.Id == provinceId.Value);
                if (!string.IsNullOrWhiteSpace(province?.ProvinceName))
                {
                    return province!.ProvinceName;
                }
            }
            else
            {
                logger.Warn($"读取省份列表失败（省份名称回落默认值）: {provincesResult.ErrorCode}");
            }
        }

        return DefaultValuesConstants.HOME_PROVINCE;
    }

    /// <summary>
    /// 按名称逐级查区划 ID（与旧版 ResolveRegionIdsAsync 同口径：查不到只留空 ID，不影响地址文本落库）。
    /// </summary>
    private async Task<RegionIds> ResolveRegionIdsAsync(
        string? cityName, string? countyName, string? townName, string? villageName,
        CancellationToken ct)
    {
        var cityText = (cityName ?? string.Empty).Trim();
        if (cityText.Length == 0)
        {
            return RegionIds.Empty;
        }

        var citiesResult = await regionService.GetCitiesAsync(ct);
        if (citiesResult.IsFailure)
        {
            logger.Warn($"读取城市列表失败（区划 ID 留空）: {citiesResult.ErrorCode}");
            return RegionIds.Empty;
        }

        var city = citiesResult.Value?
            .FirstOrDefault(c => string.Equals(c.CityName, cityText, StringComparison.Ordinal));
        if (city is null)
        {
            return RegionIds.Empty;
        }

        int? countyId = null;
        int? townId = null;
        int? villageId = null;

        var countyText = (countyName ?? string.Empty).Trim();
        var county = await FindCountyAsync(cityText, countyText, ct);
        if (county is not null)
        {
            countyId = county.Id;

            var townText = (townName ?? string.Empty).Trim();
            var town = await FindTownAsync(county.Id, townText, ct);
            if (town is not null)
            {
                townId = town.Id;

                var villageText = (villageName ?? string.Empty).Trim();
                var village = await FindVillageAsync(town.Id, villageText, ct);
                if (village is not null)
                {
                    villageId = village.Id;
                }
            }
        }

        return new RegionIds(city.Id, countyId, townId, villageId);
    }

    private async Task<RegionCounty?> FindCountyAsync(string cityName, string countyName, CancellationToken ct)
    {
        if (countyName.Length == 0) return null;

        var result = await regionService.GetCountiesByCityAsync(cityName, ct);
        if (result.IsFailure)
        {
            logger.Warn($"读取县区列表失败（区划 ID 留空）: {result.ErrorCode}");
            return null;
        }

        return result.Value?
            .FirstOrDefault(c => string.Equals(c.CountyName, countyName, StringComparison.Ordinal));
    }

    private async Task<RegionTown?> FindTownAsync(int countyId, string townName, CancellationToken ct)
    {
        if (townName.Length == 0) return null;

        var result = await regionService.GetTownsByCountyIdAsync(countyId, ct);
        if (result.IsFailure)
        {
            logger.Warn($"读取乡镇列表失败（区划 ID 留空）: {result.ErrorCode}");
            return null;
        }

        return result.Value?
            .FirstOrDefault(t => string.Equals(t.TownName, townName, StringComparison.Ordinal));
    }

    private async Task<RegionVillage?> FindVillageAsync(int townId, string villageName, CancellationToken ct)
    {
        if (villageName.Length == 0) return null;

        var result = await regionService.GetVillagesByTownIdAsync(townId, ct);
        if (result.IsFailure)
        {
            logger.Warn($"读取村/社区列表失败（区划 ID 留空）: {result.ErrorCode}");
            return null;
        }

        return result.Value?
            .FirstOrDefault(v => string.Equals(v.VillageName, villageName, StringComparison.Ordinal));
    }

    private static string JoinRegionText(params string?[] parts)
        => string.Concat(parts
            .Select(p => (p ?? string.Empty).Trim())
            .Where(p => p.Length > 0));

    // ───────────────────────────── 月份工具 ─────────────────────────────

    /// <summary>请求字段统一去空白（JSON 显式 null 也按空串处理，避免 NRE）。</summary>
    private static string Trim(string? value) => (value ?? string.Empty).Trim();

    private static bool TryParseMonth(string? month, out DateTime value)
    {
        if (DateTime.TryParseExact(
                (month ?? string.Empty).Trim(),
                "yyyy-MM",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var parsed))
        {
            value = new DateTime(parsed.Year, parsed.Month, 1);
            return true;
        }

        value = default;
        return false;
    }

    private static string ToMonthString(DateTime value) => value.ToString("yyyy-MM");
}
