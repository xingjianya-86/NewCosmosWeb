using NewCosmos.Constants;

namespace NewCosmos.Helpers;

/// <summary>
/// 字典字段 Core key ↔ 中文显示值 的**唯一换算点**（全仓只此一份，禁止再各自维护 switch 副本）。
/// <para>
/// 口径：落库/接口契约统一为字典 <c>item_key</c>（英文，权威定义见 <c>nc_dict_items</c>），
/// 展示（表单、文书、报表）统一为 <c>item_value</c>（中文）。
/// </para>
/// <para>
/// 覆盖字段：身体状况、残疾类型/等级、就业状况、婚姻状况、文化程度、政治面貌、民族、
/// 家庭关系、性别、户口性质、申请原因。
/// </para>
/// <para>
/// 通用规则：
/// <list type="bullet">
/// <item>空值/空白 → 空字符串；</item>
/// <item>大小写不敏感（兼容 healthy / SEVEREILLNESS 之类）；</item>
/// <item>历史中文旧值（务工、个体经营、本人/户主…）一并归一到 key；</item>
/// <item>未命中的未知值**原样保留**，不猜测、不吞掉（个别字段见方法注释的特殊兜底）。</item>
/// </list>
/// </para>
/// <para>改任何一张表必须同步前端 <c>web/src/views/applications/form/options.ts</c>。</para>
/// </summary>
public static class DictValueMapper
{
    // ── 身体状况 ────────────────────────────────────────────────────────────

    /// <summary>Core key / 旧字典显示值 / 历史中文值 → 表单中文档位（词表与 nc_dict_items.HealthStatuses 一致）。</summary>
    public static string ToFormHealthStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Trim();
        return text.ToLowerInvariant() switch
        {
            // 健康/一般两档采用**字典 item_value**，直接引用 HealthStatusConstants.*_DISPLAY，
            // 与 DictDisplayHelper.GetHealthStatusDisplay、PageDefaultValues.HealthOptions 同源；
            // 历史短词「健康/一般」一并归一。
            "healthy" or "健康" or "健康或良好" => HealthStatusConstants.HEALTHY_DISPLAY,
            "weak" or "fair" or "一般" or "一般或较弱" => HealthStatusConstants.FAIR_OR_WEAK_DISPLAY,
            "severeillness" or "severedisease" or "重病" => HealthStatusConstants.SEVERE_DISEASE_DISPLAY,
            "severedisability" or "重残" => HealthStatusConstants.SEVERE_DISABILITY_DISPLAY,
            "severeillnessanddisability" or "重病且重残"
                => HealthStatusConstants.SEVERE_DISEASE_AND_DISABILITY_DISPLAY,
            "other" or "其他" => "其他",
            _ => text
        };
    }

    /// <summary>表单中文档位 / 旧字典显示值 → Core key；未知历史值原样保留。</summary>
    public static string ToCoreHealthStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Trim();
        return text.ToLowerInvariant() switch
        {
            "healthy" or "健康" or "健康或良好" => HealthStatusConstants.HEALTHY,
            "weak" or "fair" or "一般" or "一般或较弱" => HealthStatusConstants.FAIR_OR_WEAK,
            "severeillness" or "severedisease" or "重病" => HealthStatusConstants.SEVERE_DISEASE,
            "severedisability" or "重残" => HealthStatusConstants.SEVERE_DISABILITY,
            "severeillnessanddisability" or "重病且重残"
                => HealthStatusConstants.SEVERE_DISEASE_AND_DISABILITY,
            "other" or "其他" => "Other",
            _ => text
        };
    }

    // ── 残疾类型 / 等级 ─────────────────────────────────────────────────────

    /// <summary>残疾类型 key（Vision…）→ 中文显示值（视力…）。</summary>
    public static string ToFormDisabilityType(string? value) => value?.Trim() switch
    {
        "Vision" or "视力" => "视力",
        "Hearing" or "听力" => "听力",
        "Speech" or "言语" => "言语",
        "Physical" or "肢体" => "肢体",
        "Intellectual" or "智力" => "智力",
        "Mental" or "精神" => "精神",
        "Multiple" or "多重" => "多重",
        var other => other ?? string.Empty
    };

    /// <summary>中文残疾类型 → key（Level*/一级…两套都认）。</summary>
    public static string ToCoreDisabilityType(string? value) => value?.Trim() switch
    {
        "视力" => DisabilityConstants.TYPE_VISION,
        "听力" => DisabilityConstants.TYPE_HEARING,
        "言语" => DisabilityConstants.TYPE_SPEECH,
        "肢体" => DisabilityConstants.TYPE_PHYSICAL,
        "智力" => DisabilityConstants.TYPE_INTELLECTUAL,
        "精神" => DisabilityConstants.TYPE_MENTAL,
        "多重" => DisabilityConstants.TYPE_MULTIPLE,
        var other => other ?? string.Empty
    };

    /// <summary>残疾等级 key（Level1…）→ 中文（一级…）。</summary>
    public static string ToFormDisabilityLevel(string? value) => value?.Trim() switch
    {
        DisabilityConstants.LEVEL1 => "一级",
        DisabilityConstants.LEVEL2 => "二级",
        DisabilityConstants.LEVEL3 => "三级",
        DisabilityConstants.LEVEL4 => "四级",
        var other => other ?? string.Empty
    };

    /// <summary>中文残疾等级 → key。</summary>
    public static string ToCoreDisabilityLevel(string? value) => value?.Trim() switch
    {
        "一级" => DisabilityConstants.LEVEL1,
        "二级" => DisabilityConstants.LEVEL2,
        "三级" => DisabilityConstants.LEVEL3,
        "四级" => DisabilityConstants.LEVEL4,
        var other => other ?? string.Empty
    };

    // ── 就业状况 ────────────────────────────────────────────────────────────

    /// <summary>就业状况 key → 中文；历史中文旧值（务工/个体经营/无业…）一并归一。</summary>
    public static string ToFormEmploymentStatus(string? value) => value?.Trim() switch
    {
        "Employed" or "在职" or "务工" or "个体经营" or "灵活就业" or "自谋职业" => "在职",
        "Unemployed" or "失业" or "无业" or "待业" => "失业",
        "Farming" or "务农" or "农业" => "务农",
        "Student" or "学生" or "在校学生" => "学生",
        "Retired" or "退休" or "离退休" => "退休",
        "Other" or "其他" => "其他",
        var other => other ?? string.Empty
    };

    /// <summary>中文就业状况 → key。</summary>
    public static string ToCoreEmploymentStatus(string? value) => value?.Trim() switch
    {
        "Employed" or "在职" or "务工" or "个体经营" or "灵活就业" or "自谋职业" => "Employed",
        "Unemployed" or "失业" or "无业" or "待业" => "Unemployed",
        "Farming" or "务农" or "农业" => "Farming",
        "Student" or "学生" or "在校学生" => "Student",
        "Retired" or "退休" or "离退休" => "Retired",
        "Other" or "其他" => "Other",
        var other => other ?? string.Empty
    };

    // ── 婚姻状况 ────────────────────────────────────────────────────────────

    /// <summary>婚姻状况 key → 中文。</summary>
    public static string ToFormMaritalStatus(string? value) => value?.Trim() switch
    {
        "Single" or "未婚" => "未婚",
        "Married" or "已婚" => "已婚",
        "Widowed" or "丧偶" => "丧偶",
        "Divorced" or "离婚" => "离婚",
        "Other" or "其他" => "其他",
        var other => other ?? string.Empty
    };

    /// <summary>中文婚姻状况 → key。</summary>
    public static string ToCoreMaritalStatus(string? value) => value?.Trim() switch
    {
        "Single" or "未婚" => "Single",
        "Married" or "已婚" => "Married",
        "Widowed" or "丧偶" => "Widowed",
        "Divorced" or "离婚" => "Divorced",
        "Other" or "其他" => "Other",
        var other => other ?? string.Empty
    };

    // ── 文化程度 ────────────────────────────────────────────────────────────

    /// <summary>文化程度 key → 中文。</summary>
    public static string ToFormEducationLevel(string? value) => value?.Trim() switch
    {
        "Illiterate" or "文盲" or "文盲/半文盲" => "文盲/半文盲",
        "Primary" or "小学" => "小学",
        "JuniorHigh" or "初中" => "初中",
        "SeniorHigh" or "高中" or "中专" or "高中/中专" => "高中/中专",
        "College" or "专科" or "大专" => "大专",
        "Bachelor" or "本科" => "本科",
        "Postgraduate" or "研究生" or "研究生及以上" => "研究生及以上",
        var other => other ?? string.Empty
    };

    /// <summary>中文文化程度 → key。</summary>
    public static string ToCoreEducationLevel(string? value) => value?.Trim() switch
    {
        "Illiterate" or "文盲" or "文盲/半文盲" => "Illiterate",
        "Primary" or "小学" => "Primary",
        "JuniorHigh" or "初中" => "JuniorHigh",
        "SeniorHigh" or "高中" or "中专" or "高中/中专" => "SeniorHigh",
        "College" or "专科" or "大专" => "College",
        "Bachelor" or "本科" => "Bachelor",
        "Postgraduate" or "研究生" or "研究生及以上" => "Postgraduate",
        var other => other ?? string.Empty
    };

    // ── 政治面貌 ────────────────────────────────────────────────────────────

    /// <summary>政治面貌 key → 中文。</summary>
    public static string ToFormPoliticalStatus(string? value) => value?.Trim() switch
    {
        "CPCMember" or "中共党员" => "中共党员",
        "CYLMembers" or "CYLCadre" or "共青团员" => "共青团员",
        "Masses" or "群众" => "群众",
        "DemocraticParty" or "民主党派" => "民主党派",
        "Other" or "其他" => "其他",
        var other => other ?? string.Empty
    };

    /// <summary>中文政治面貌 → key（历史 key CYLCadre 归一为 CYLMembers）。</summary>
    public static string ToCorePoliticalStatus(string? value) => value?.Trim() switch
    {
        "CPCMember" or "中共党员" => "CPCMember",
        "CYLMembers" or "CYLCadre" or "共青团员" => "CYLMembers",
        "Masses" or "群众" => "Masses",
        "DemocraticParty" or "民主党派" => "DemocraticParty",
        "Other" or "其他" => "Other",
        var other => other ?? string.Empty
    };

    // ── 民族 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 民族 key → 中文。历史前端 key "Mongolian" 归一为字典 key "Mongol" 的显示值；
    /// Daur/Xibe 是旧前端用过、字典未收录的 key，仅做中文回显兼容。
    /// </summary>
    public static string ToFormEthnicity(string? value) => value?.Trim() switch
    {
        "Han" or "汉族" => "汉族",
        "Mongol" or "Mongolian" or "蒙古族" => "蒙古族",
        "Hui" or "回族" => "回族",
        "Tibetan" or "藏族" => "藏族",
        "Uyghur" or "维吾尔族" => "维吾尔族",
        "Miao" or "苗族" => "苗族",
        "Yi" or "彝族" => "彝族",
        "Zhuang" or "壮族" => "壮族",
        "Bouyei" or "布依族" => "布依族",
        "Korean" or "朝鲜族" => "朝鲜族",
        "Manchu" or "满族" => "满族",
        "Dong" or "侗族" => "侗族",
        "Yao" or "瑶族" => "瑶族",
        "Bai" or "白族" => "白族",
        "Tujia" or "土家族" => "土家族",
        "Hani" or "哈尼族" => "哈尼族",
        "Dai" or "傣族" => "傣族",
        "Li" or "黎族" => "黎族",
        "Other" or "其他" => "其他",
        "Daur" or "达斡尔族" => "达斡尔族",
        "Xibe" or "锡伯族" => "锡伯族",
        var other => other ?? string.Empty
    };

    /// <summary>中文民族 → key（Mongolian 归一为 Mongol）。</summary>
    public static string ToCoreEthnicity(string? value) => value?.Trim() switch
    {
        "Han" or "汉族" => "Han",
        "Mongol" or "Mongolian" or "蒙古族" => "Mongol",
        "Hui" or "回族" => "Hui",
        "Tibetan" or "藏族" => "Tibetan",
        "Uyghur" or "维吾尔族" => "Uyghur",
        "Miao" or "苗族" => "Miao",
        "Yi" or "彝族" => "Yi",
        "Zhuang" or "壮族" => "Zhuang",
        "Bouyei" or "布依族" => "Bouyei",
        "Korean" or "朝鲜族" => "Korean",
        "Manchu" or "满族" => "Manchu",
        "Dong" or "侗族" => "Dong",
        "Yao" or "瑶族" => "Yao",
        "Bai" or "白族" => "Bai",
        "Tujia" or "土家族" => "Tujia",
        "Hani" or "哈尼族" => "Hani",
        "Dai" or "傣族" => "Dai",
        "Li" or "黎族" => "Li",
        "Other" or "其他" => "Other",
        "Daur" or "达斡尔族" => "Daur",
        "Xibe" or "锡伯族" => "Xibe",
        var other => other ?? string.Empty
    };

    // ── 家庭关系 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 家庭关系 key → 旧版中文显示值。
    /// 未知**纯 ASCII** 代码不直接回显，退化为“其他”；未知中文历史值原样保留。
    /// </summary>
    public static string ToFormRelationship(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Trim();
        return text switch
        {
            "Head" or "户主" or "本人" or "本人/户主" => "本人/户主",
            "Spouse" or "Husband" or "Wife" or "配偶" => "配偶",
            "Son" or "Child" or "子" or "儿子" or "子女" or "子/婿" => "子/婿",
            "Daughter" or "女" or "女儿" or "女/媳" => "女/媳",
            "Grandchild" or "孙子女" or "孙子女/外孙子女" => "孙子女/外孙子女",
            "Parent" or "Father" or "Mother" or "父母" or "父母/岳父母/公婆" => "父母/岳父母/公婆",
            "Grandparent" or "祖父母" or "祖父母/外祖父母" => "祖父母/外祖父母",
            "Sibling" or "Brother" or "Sister" or "兄弟姐妹" => "兄弟姐妹",
            "Other" or "其他" => "其他",
            _ => text.All(char.IsAscii) ? "其他" : text
        };
    }

    /// <summary>中文家庭关系 → key（含历史别名 本人/本人/户主 → Head）。</summary>
    public static string ToCoreRelationship(string? value) => value?.Trim() switch
    {
        "Head" or "户主" or "本人" or "本人/户主" => "Head",
        "Spouse" or "Husband" or "Wife" or "配偶" => "Spouse",
        "Son" or "Child" or "子" or "儿子" or "子女" or "子/婿" => "Son",
        "Daughter" or "女" or "女儿" or "女/媳" => "Daughter",
        "Grandchild" or "孙子女" or "孙子女/外孙子女" => "Grandchild",
        "Parent" or "Father" or "Mother" or "父母" or "父母/岳父母/公婆" => "Parent",
        "Grandparent" or "祖父母" or "祖父母/外祖父母" => "Grandparent",
        "Sibling" or "Brother" or "Sister" or "兄弟姐妹" => "Sibling",
        "Other" or "其他" => "Other",
        var other => other ?? string.Empty
    };

    // ── 性别 ────────────────────────────────────────────────────────────────

    /// <summary>历史数据存在 Male/Female 英文代码，表单统一输出/保存为中文。</summary>
    public static string ToFormGender(string? value) => value?.Trim() switch
    {
        "Male" or "M" or "男" => "男",
        "Female" or "F" or "女" => "女",
        var other => other ?? string.Empty
    };

    // ── 户口性质 ────────────────────────────────────────────────────────────

    /// <summary>户口性质 key → 中文。</summary>
    public static string ToFormHukouType(string? value) => value?.Trim() switch
    {
        "Rural" or "农村户口" => "农村户口",
        "Urban" or "城市户口" => "城市户口",
        var other => other ?? string.Empty
    };

    /// <summary>中文户口性质 → key。</summary>
    public static string ToCoreHukouType(string? value) => value?.Trim() switch
    {
        "Rural" or "农村户口" => "Rural",
        "Urban" or "城市户口" => "Urban",
        var other => other ?? string.Empty
    };

    // ── 申请原因 ────────────────────────────────────────────────────────────

    /// <summary>申请原因 key → 中文。</summary>
    public static string ToFormApplicationReason(string? value) => value?.Trim() switch
    {
        "Illness" or "因病" => "因病",
        "Disaster" or "因灾" => "因灾",
        "Disability" or "因残" => "因残",
        "Education" or "因学" => "因学",
        "LowIncome" or "收入低" => "收入低",
        "Unemployment" or "失业" => "失业",
        "LandLoss" or "失地" => "失地",
        "Accident" or "因意外事故" => "因意外事故",
        "Other" or "其他" => "其他",
        var other => other ?? string.Empty
    };

    /// <summary>中文申请原因 → key。</summary>
    public static string ToCoreApplicationReason(string? value) => value?.Trim() switch
    {
        "Illness" or "因病" => "Illness",
        "Disaster" or "因灾" => "Disaster",
        "Disability" or "因残" => "Disability",
        "Education" or "因学" => "Education",
        "LowIncome" or "收入低" => "LowIncome",
        "Unemployment" or "失业" => "Unemployment",
        "LandLoss" or "失地" => "LandLoss",
        "Accident" or "因意外事故" => "Accident",
        "Other" or "其他" => "Other",
        var other => other ?? string.Empty
    };
}
