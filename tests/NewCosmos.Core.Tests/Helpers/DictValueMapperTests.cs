using NewCosmos.Constants;
using NewCosmos.Helpers;
using Xunit;

namespace NewCosmos.Core.Tests;

/// <summary>
/// 字典字段 Core key ↔ 中文显示值 换算（DictValueMapper，全仓唯一换算点）。
/// 锁点：英文 Core key / 历史中文旧值 / 大小写与空白容错 / 未知历史值原样保留。
/// </summary>
public class DictValueMapperTests
{
    // ── 身体状况（词表 = nc_dict_items.HealthStatuses.item_value：健康或良好 / 一般或较弱 …）──

    [Theory]
    [InlineData("Healthy", "健康或良好")]
    [InlineData("healthy", "健康或良好")]
    [InlineData("  Healthy  ", "健康或良好")]
    [InlineData("健康或良好", "健康或良好")]
    [InlineData("健康", "健康或良好")]
    [InlineData("Weak", "一般或较弱")]
    [InlineData("一般或较弱", "一般或较弱")]
    [InlineData("一般", "一般或较弱")]
    [InlineData("SevereIllness", "重病")]
    [InlineData("SevereIllnessAndDisability", "重病且重残")]
    [InlineData("SevereDisability", "重残")]
    [InlineData("Other", "其他")]
    [InlineData("其他", "其他")]
    [InlineData("患病", "患病")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void 健康状况_转中文(string? input, string expected)
        => Assert.Equal(expected, DictValueMapper.ToFormHealthStatus(input));

    [Theory]
    [InlineData("健康", HealthStatusConstants.HEALTHY)]
    [InlineData("健康或良好", HealthStatusConstants.HEALTHY)]
    [InlineData("一般", HealthStatusConstants.FAIR_OR_WEAK)]
    [InlineData("一般或较弱", HealthStatusConstants.FAIR_OR_WEAK)]
    [InlineData("重病", HealthStatusConstants.SEVERE_DISEASE)]
    [InlineData("重残", HealthStatusConstants.SEVERE_DISABILITY)]
    [InlineData("重病且重残", HealthStatusConstants.SEVERE_DISEASE_AND_DISABILITY)]
    [InlineData("其他", "Other")]
    [InlineData("Healthy", HealthStatusConstants.HEALTHY)]
    [InlineData("SEVEREILLNESS", HealthStatusConstants.SEVERE_DISEASE)]
    [InlineData("患病", "患病")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void 健康状况_转CoreKey(string? input, string expected)
        => Assert.Equal(expected, DictValueMapper.ToCoreHealthStatus(input));

    // ── 残疾类型 / 等级 ──

    [Theory]
    [InlineData("Vision", "视力")]
    [InlineData("Physical", "肢体")]
    [InlineData("Multiple", "多重")]
    [InlineData("视力", "视力")]
    [InlineData("", "")]
    public void 残疾类型_转中文(string input, string expected)
        => Assert.Equal(expected, DictValueMapper.ToFormDisabilityType(input));

    [Theory]
    [InlineData("肢体", DisabilityConstants.TYPE_PHYSICAL)]
    [InlineData("Physical", DisabilityConstants.TYPE_PHYSICAL)]
    [InlineData("", "")]
    public void 残疾类型_转CoreKey(string input, string expected)
        => Assert.Equal(expected, DictValueMapper.ToCoreDisabilityType(input));

    [Theory]
    [InlineData("Level1", "一级")]
    [InlineData("Level4", "四级")]
    [InlineData("一级", "一级")]
    public void 残疾等级_转中文(string input, string expected)
        => Assert.Equal(expected, DictValueMapper.ToFormDisabilityLevel(input));

    [Theory]
    [InlineData("一级", DisabilityConstants.LEVEL1)]
    [InlineData("Level2", DisabilityConstants.LEVEL2)]
    public void 残疾等级_转CoreKey(string input, string expected)
        => Assert.Equal(expected, DictValueMapper.ToCoreDisabilityLevel(input));

    // ── 就业状况（含历史中文旧值）──

    [Theory]
    [InlineData("Employed", "在职")]
    [InlineData("务工", "在职")]
    [InlineData("个体经营", "在职")]
    [InlineData("Unemployed", "失业")]
    [InlineData("无业", "失业")]
    [InlineData("Farming", "务农")]
    [InlineData("Student", "学生")]
    [InlineData("Retired", "退休")]
    public void 就业状况_转中文含历史旧值(string input, string expected)
        => Assert.Equal(expected, DictValueMapper.ToFormEmploymentStatus(input));

    [Theory]
    [InlineData("在职", "Employed")]
    [InlineData("务农", "Farming")]
    [InlineData("Employed", "Employed")]
    public void 就业状况_转CoreKey(string input, string expected)
        => Assert.Equal(expected, DictValueMapper.ToCoreEmploymentStatus(input));

    // ── 婚姻 / 文化 / 政治 ──

    [Theory]
    [InlineData("Married", "已婚")]
    [InlineData("已婚", "已婚")]
    [InlineData("Widowed", "丧偶")]
    public void 婚姻状况_转中文(string input, string expected)
        => Assert.Equal(expected, DictValueMapper.ToFormMaritalStatus(input));

    [Theory]
    [InlineData("已婚", "Married")]
    [InlineData("Married", "Married")]
    public void 婚姻状况_转CoreKey(string input, string expected)
        => Assert.Equal(expected, DictValueMapper.ToCoreMaritalStatus(input));

    [Theory]
    [InlineData("Primary", "小学")]
    [InlineData("小学", "小学")]
    [InlineData("SeniorHigh", "高中/中专")]
    [InlineData("中专", "高中/中专")]
    public void 文化程度_转中文(string input, string expected)
        => Assert.Equal(expected, DictValueMapper.ToFormEducationLevel(input));

    [Theory]
    [InlineData("小学", "Primary")]
    [InlineData("Primary", "Primary")]
    public void 文化程度_转CoreKey(string input, string expected)
        => Assert.Equal(expected, DictValueMapper.ToCoreEducationLevel(input));

    [Theory]
    [InlineData("Masses", "群众")]
    [InlineData("群众", "群众")]
    [InlineData("CYLCadre", "共青团员")]
    public void 政治面貌_转中文(string input, string expected)
        => Assert.Equal(expected, DictValueMapper.ToFormPoliticalStatus(input));

    [Theory]
    [InlineData("群众", "Masses")]
    [InlineData("CYLCadre", "CYLMembers")]
    public void 政治面貌_转CoreKey(string input, string expected)
        => Assert.Equal(expected, DictValueMapper.ToCorePoliticalStatus(input));

    // ── 民族 ──

    [Theory]
    [InlineData("Han", "汉族")]
    [InlineData("Mongolian", "蒙古族")]
    [InlineData("Daur", "达斡尔族")]
    public void 民族_转中文(string input, string expected)
        => Assert.Equal(expected, DictValueMapper.ToFormEthnicity(input));

    [Theory]
    [InlineData("汉族", "Han")]
    [InlineData("Mongolian", "Mongol")]
    public void 民族_转CoreKey(string input, string expected)
        => Assert.Equal(expected, DictValueMapper.ToCoreEthnicity(input));

    // ── 家庭关系 ──

    [Theory]
    [InlineData("Head", "本人/户主")]
    [InlineData("本人/户主", "本人/户主")]
    [InlineData("本人", "本人/户主")]
    [InlineData("Son", "子/婿")]
    [InlineData("Spouse", "配偶")]
    public void 家庭关系_转中文(string input, string expected)
        => Assert.Equal(expected, DictValueMapper.ToFormRelationship(input));

    [Theory]
    [InlineData("本人/户主", "Head")]
    [InlineData("本人", "Head")]
    [InlineData("Head", "Head")]
    [InlineData("子/婿", "Son")]
    public void 家庭关系_转CoreKey(string input, string expected)
        => Assert.Equal(expected, DictValueMapper.ToCoreRelationship(input));

    [Fact]
    public void 家庭关系_未知ASCII代码退化为其他_未知中文原样保留()
    {
        Assert.Equal("其他", DictValueMapper.ToFormRelationship("UnknownKey"));
        Assert.Equal("患病史", DictValueMapper.ToFormRelationship("患病史"));
    }

    // ── 性别 / 户口 / 申请原因 ──

    [Theory]
    [InlineData("Male", "男")]
    [InlineData("M", "男")]
    [InlineData("Female", "女")]
    [InlineData("男", "男")]
    public void 性别_归一为中文(string input, string expected)
        => Assert.Equal(expected, DictValueMapper.ToFormGender(input));

    [Theory]
    [InlineData("Rural", "农村户口")]
    [InlineData("Urban", "城市户口")]
    public void 户口性质_转中文(string input, string expected)
        => Assert.Equal(expected, DictValueMapper.ToFormHukouType(input));

    [Theory]
    [InlineData("农村户口", "Rural")]
    [InlineData("Rural", "Rural")]
    public void 户口性质_转CoreKey(string input, string expected)
        => Assert.Equal(expected, DictValueMapper.ToCoreHukouType(input));

    [Theory]
    [InlineData("LowIncome", "收入低")]
    [InlineData("Illness", "因病")]
    public void 申请原因_转中文(string input, string expected)
        => Assert.Equal(expected, DictValueMapper.ToFormApplicationReason(input));

    [Theory]
    [InlineData("收入低", "LowIncome")]
    [InlineData("LowIncome", "LowIncome")]
    public void 申请原因_转CoreKey(string input, string expected)
        => Assert.Equal(expected, DictValueMapper.ToCoreApplicationReason(input));

    // ── 通用容错 ──

    /// <summary>
    /// 健康状况词表必须与字典口径同源：表单、月报、文书、字典显示四处必须是同一套词，
    /// 否则同一个人在申请表单显示「健康或良好」、在月报显示「健康」。
    /// </summary>
    [Fact]
    public void 健康状况词表与字典口径同源()
    {
        Assert.Equal(HealthStatusConstants.HEALTHY_DISPLAY, PageDefaultValues.HealthOptions[0]);
        Assert.Equal(HealthStatusConstants.FAIR_OR_WEAK_DISPLAY, PageDefaultValues.HealthOptions[1]);
        Assert.Equal(HealthStatusConstants.SEVERE_DISEASE_DISPLAY, PageDefaultValues.HealthOptions[2]);
        Assert.Equal(HealthStatusConstants.SEVERE_DISABILITY_DISPLAY, PageDefaultValues.HealthOptions[3]);
        Assert.Equal(
            HealthStatusConstants.SEVERE_DISEASE_AND_DISABILITY_DISPLAY,
            PageDefaultValues.HealthOptions[4]);

        Assert.Equal(
            HealthStatusConstants.HEALTHY_DISPLAY,
            DictValueMapper.ToFormHealthStatus(HealthStatusConstants.HEALTHY));
        Assert.Equal(
            HealthStatusConstants.FAIR_OR_WEAK_DISPLAY,
            DictValueMapper.ToFormHealthStatus(HealthStatusConstants.FAIR_OR_WEAK));
        Assert.Equal(
            HealthStatusConstants.SEVERE_DISEASE_DISPLAY,
            DictValueMapper.ToFormHealthStatus(HealthStatusConstants.SEVERE_DISEASE));

        // 历史表单短词归一到字典口径（不许再回出「健康」「一般」）
        Assert.Equal(HealthStatusConstants.HEALTHY_DISPLAY, DictValueMapper.ToFormHealthStatus("健康"));
        Assert.Equal(HealthStatusConstants.FAIR_OR_WEAK_DISPLAY, DictValueMapper.ToFormHealthStatus("一般"));
    }

    [Fact]
    public void 空值与空白_统一返回空字符串()
    {
        Assert.Equal(string.Empty, DictValueMapper.ToFormHealthStatus(null));
        Assert.Equal(string.Empty, DictValueMapper.ToFormHealthStatus("   "));
        Assert.Equal(string.Empty, DictValueMapper.ToCoreMaritalStatus(null));
        Assert.Equal(string.Empty, DictValueMapper.ToFormRelationship("  "));
    }
}
