using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;

namespace NewCosmos.Services.Domain.SocialAssistance;

/// <summary>
/// 快速核验展示构建器：把 QuickVerifyBundle 的 10 组台账转为页面/报告共用的展示模型。
/// 【同源】标题、明细拼接、同名风险提示、姓名来源、命中统计只在这里实现一次，
/// 页面与报告不重复拼装；零命中分组保留在结果中（是否隐藏由前端决定，默认隐藏以复刻旧版）。
/// 【只读】不写库、不改 bundle。
/// </summary>
public static class QuickVerifyVerificationPresenter
{
    /// <summary>
    /// 构建展示结果。调用方需保证 bundle 已成功返回（整体失败由服务层上抛）。
    /// </summary>
    public static QuickVerifyVerificationView Build(QuickVerifyBundle bundle)
    {
        var groups = new List<QuickVerifyVerificationGroup>
        {
            BuildGroup("contractLand", "🌾 土地·承包台账（外部）", bundle.ContractLand,
                r => new QuickVerifyVerificationItem
                {
                    Title = string.IsNullOrWhiteSpace(r.PlotName)
                        ? $"地块 {r.PlotCode}"
                        : $"{r.PlotName} · {r.PlotCode}",
                    Detail = $"承包方 {r.ContractorName}｜发包方 {r.EmployerName}｜数据年份 {r.DataYear}",
                    Value = $"{QuickVerifyFormat.Area(r.LandArea)} 亩"
                }),
            BuildGroup("historyLand", "🌾 土地·历史申请确权", bundle.HistoryLand,
                r => new QuickVerifyVerificationItem
                {
                    Title = $"{r.MemberName} · {r.PlotCode}",
                    Detail = $"{r.LandPlotInfo}｜{r.LandUsage}｜来源档案 #{r.ApplicationId}",
                    Value = $"{QuickVerifyFormat.Area(r.LandArea)} 亩"
                }),
            BuildGroup("subsidies", "🌱 种植补贴（最新年段）", bundle.Subsidies, BuildSubsidyItem),
            BuildGroup("securityIdentities", "🛡 历史保障身份", bundle.SecurityIdentities,
                r => new QuickVerifyVerificationItem
                {
                    Title = $"{r.SourceDisplay} · {r.Name}",
                    Detail = BuildIdentityDetail(r),
                    Value = $"成员 {r.FamilyMemberCount} 人"
                }),
            BuildGroup("assets", "🚗 资产（车辆/房产/金融）", bundle.Assets,
                r => new QuickVerifyVerificationItem
                {
                    Title = $"{r.Category} · {r.Title}",
                    Detail = r.Detail,
                    Value = $"{QuickVerifyFormat.Money(r.Amount)} 元"
                }),
            BuildGroup("bankFlows", "🏦 银行流水（最新批次）", bundle.BankFlows,
                r => new QuickVerifyVerificationItem
                {
                    Title = $"{r.PersonName} · 余额 {QuickVerifyFormat.Money(r.TotalBalance)} 元",
                    Detail = $"12个月最高流水 {QuickVerifyFormat.Money(r.MaxFlowAmount)} 元" +
                             (string.IsNullOrWhiteSpace(r.BankSummary) ? string.Empty : $"｜{r.BankSummary}"),
                    Badge = r.IsExceed ? "超阈值" : string.Empty,
                    Value = r.BatchNo
                }),
            BuildGroup("assetReports", "📄 经济状况核对报告", bundle.AssetReports,
                r => new QuickVerifyVerificationItem
                {
                    Title = r.FileName,
                    Detail = $"报告日期 {QuickVerifyFormat.Date(r.ReportDate)}｜版本 v{r.Version}",
                    Badge = r.IsValid ? string.Empty : "文件名存疑"
                }),
            BuildGroup("deaths", "🕯 死亡记录", bundle.Deaths,
                r => new QuickVerifyVerificationItem
                {
                    Title = $"{r.MemberName} · {r.RelationshipToHead}",
                    Detail = $"死亡日期 {QuickVerifyFormat.Date(r.DeathDate)}" +
                             (string.IsNullOrWhiteSpace(r.DeathReason) ? string.Empty : $"｜{r.DeathReason}")
                }),
            BuildGroup("tempReliefs", "⛑ 临时救助", bundle.TempReliefs,
                r => new QuickVerifyVerificationItem
                {
                    Title = $"{r.ApplicantName} · {r.ReliefType}",
                    Detail = $"申请 {QuickVerifyFormat.Date(r.ApplyDate)}｜状态 {r.Status}",
                    Value = $"{QuickVerifyFormat.Money(r.ConfirmAmount)} 元"
                }),
            BuildGroup("elderlyBenefits", "🧓 高龄津贴", bundle.ElderlyBenefits,
                r => new QuickVerifyVerificationItem
                {
                    Title = $"{r.Name} · {r.Category}",
                    Detail = $"受理 {QuickVerifyFormat.Date(r.ApplyDate)}｜状态 {r.Status}",
                    Value = $"{QuickVerifyFormat.Money(r.IssueAmount)} 元/月"
                })
        };

        var hitGroups = groups.Count(g => g.HasItems);
        // 姓名展示与旧版摘要口径一致：姓名 + 来源；无姓名时明确"未匹配到姓名"
        var nameDisplay = string.IsNullOrWhiteSpace(bundle.QueryName)
            ? "未匹配到姓名"
            : string.IsNullOrWhiteSpace(bundle.NameSource)
                ? bundle.QueryName
                : $"{bundle.QueryName}（{bundle.NameSource}）";

        return new QuickVerifyVerificationView
        {
            IdCard = bundle.IdCard,
            IdCardMasked = DataMasker.MaskIdCard(bundle.IdCard),
            QueryName = bundle.QueryName,
            NameSource = bundle.NameSource,
            NameDisplay = nameDisplay,
            // 旧版摘要含"无数据分组已隐藏"；页面可切换显示空组，故改为中性表述
            Summary = $"身份证 {DataMasker.MaskIdCard(bundle.IdCard)} · 匹配姓名 {nameDisplay} · " +
                      $"{hitGroups} 组有数据（只读核验，不写入任何数据）",
            HasAnyRecord = hitGroups > 0,
            HitGroupCount = hitGroups,
            Groups = groups,
            LandImportTotal = ResolveLandImportTotal(bundle, out var landSource),
            LandImportSource = landSource,
            SubsidyImportTotal = ResolveSubsidyImportTotal(bundle),
            SubsidyImportCount = bundle.Subsidies.IsFailure ? 0 : bundle.Subsidies.Count
        };
    }

    /// <summary>
    /// 种植补贴行：面积型/金额型互推（与「导入农业补贴」同口径），
    /// 面积缺省按 金额÷单价 回算，金额缺省按 面积×单价 折算；单价取 SubsidyPriceConstants 当前价。
    /// </summary>
    private static QuickVerifyVerificationItem BuildSubsidyItem(SubsidyRow r)
    {
        var price = SubsidyPriceConstants.GetPrice(r.SubsidyType);
        var area = r.Area > 0
            ? r.Area
            : price > 0 && r.Amount > 0 ? Math.Round(r.Amount / price, 2) : 0m;
        var amount = r.Amount > 0
            ? r.Amount
            : price > 0 && r.Area > 0 ? Math.Round(r.Area * price, 2) : 0m;
        var areaNote = r.Area <= 0 && area > 0 ? "（金额÷单价回算）" : string.Empty;
        var amountNote = r.Amount <= 0 && amount > 0 ? "（按单价折算）" : string.Empty;

        return new QuickVerifyVerificationItem
        {
            Title = $"{r.SubsidyType} · {r.Name}",
            Detail = $"面积 {QuickVerifyFormat.Area(area)} 亩{areaNote}",
            Value = $"{QuickVerifyFormat.Money(amount)} 元{amountNote}"
        };
    }

    private static string BuildIdentityDetail(SecurityIdentityRow r)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(r.Relationship)) parts.Add($"关系 {r.Relationship}");
        if (!string.IsNullOrWhiteSpace(r.Phone)) parts.Add($"电话 {r.Phone}");
        if (!string.IsNullOrWhiteSpace(r.Address)) parts.Add(r.Address);
        return string.Join("｜", parts);
    }

    private static QuickVerifyVerificationGroup BuildGroup<T>(
        string key,
        string title,
        QuickVerifyGroup<T> group,
        Func<T, QuickVerifyVerificationItem> mapper)
    {
        // 失败组保留原因、绝不伪装无数据（§7）；零命中组保留空列表供前端显示空态
        var items = group.IsFailure
            ? new List<QuickVerifyVerificationItem>()
            : group.Items.Select(mapper).ToList();

        return new QuickVerifyVerificationGroup
        {
            Key = key,
            Title = title,
            IsFailure = group.IsFailure,
            FailureReason = group.FailureReason,
            MatchNote = group.MatchNote,
            Count = items.Count,
            CountText = group.IsFailure || items.Count == 0 ? string.Empty : $"共 {items.Count} 条",
            HasItems = items.Count > 0,
            Items = items
        };
    }

    /// <summary>土地带入合计：承包台账优先，其次历史确权（与旧版导入顺序一致）。</summary>
    private static decimal ResolveLandImportTotal(QuickVerifyBundle bundle, out string source)
    {
        var total = bundle.ContractLand.IsFailure
            ? 0m
            : bundle.ContractLand.Items.Sum(r => r.LandArea);
        source = "承包台账";

        if (total <= 0)
        {
            total = bundle.HistoryLand.IsFailure
                ? 0m
                : bundle.HistoryLand.Items.Sum(r => r.LandArea);
            source = "历史确权";
        }

        if (total <= 0)
        {
            source = string.Empty;
            return 0m;
        }

        return total;
    }

    /// <summary>补贴带入合计：金额型取金额，面积型按分类单价折算（逐笔舍入后求和，与旧版一致）。</summary>
    private static decimal ResolveSubsidyImportTotal(QuickVerifyBundle bundle)
    {
        if (bundle.Subsidies.IsFailure)
            return 0m;

        decimal sum = 0m;
        foreach (var row in bundle.Subsidies.Items)
        {
            if (row.Amount > 0)
                sum += row.Amount;
            else if (row.Area > 0)
                sum += Math.Round(row.Area * SubsidyPriceConstants.GetPrice(row.SubsidyType), 2);
        }
        return sum;
    }
}
