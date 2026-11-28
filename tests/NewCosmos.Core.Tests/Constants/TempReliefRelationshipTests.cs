using NewCosmos.Constants;
using NewCosmos.Helpers;
using Xunit;

namespace NewCosmos.Core.Tests;

/// <summary>
/// 家庭关系显示词表统一（2026-11-28）：全仓唯一词表 = <c>DictValueMapper.ToFormRelationship</c>
/// （Head→本人/户主、Son→子/婿、Daughter→女/媳、Parent→父母/岳父母/公婆…）。
/// <para>
/// 锁点：临时救助委托同一套词表（原先自维护一份「户主/儿子/女儿」）；
/// <c>RelationHead</c> 是**展示值**；旧显示值与字典 key 都能归一到 key ——
/// 否则「拿显示文本判断户主」的文书去重与前端过滤会在换词表时静默失效。
/// </para>
/// </summary>
public class TempReliefRelationshipTests
{
    [Theory]
    [InlineData("Head", "本人/户主")]
    [InlineData("Son", "子/婿")]
    [InlineData("Daughter", "女/媳")]
    [InlineData("Spouse", "配偶")]
    [InlineData("Grandchild", "孙子女/外孙子女")]
    [InlineData("Parent", "父母/岳父母/公婆")]
    [InlineData("Grandparent", "祖父母/外祖父母")]
    [InlineData("Sibling", "兄弟姐妹")]
    [InlineData("Other", "其他")]
    public void 临时救助关系显示_与统一词表一致(string key, string expected)
        => Assert.Equal(expected, TempReliefConstants.GetRelationshipDisplayName(key));

    [Theory]
    // 临时救助 relation 列落库的是中文显示值（旧词表「户主/儿子/女儿」）
    [InlineData("户主")]
    [InlineData("本人/户主")]
    [InlineData("本人")]
    [InlineData("Head")]
    public void 户主的旧显示值与key都归一为Head(string value)
        => Assert.Equal(
            DictionaryConstants.FamilyRelationship.HEAD,
            DictValueMapper.ToCoreRelationship(value));

    [Theory]
    [InlineData("儿子")]
    [InlineData("子/婿")]
    [InlineData("Son")]
    public void 儿子的旧显示值与key都归一为Son(string value)
        => Assert.Equal(
            DictionaryConstants.FamilyRelationship.SON,
            DictValueMapper.ToCoreRelationship(value));

    [Theory]
    [InlineData("女儿")]
    [InlineData("女/媳")]
    [InlineData("Daughter")]
    public void 女儿的旧显示值与key都归一为Daughter(string value)
        => Assert.Equal(
            DictionaryConstants.FamilyRelationship.DAUGHTER,
            DictValueMapper.ToCoreRelationship(value));

    /// <summary>RelationHead 仅作展示：必须与统一词表的 Head 显示值一致，不得再是旧词表「户主」。</summary>
    [Fact]
    public void RelationHead_是展示值且等于统一词表()
        => Assert.Equal(
            TempReliefConstants.RelationHead,
            DictValueMapper.ToFormRelationship(DictionaryConstants.FamilyRelationship.HEAD));

    /// <summary>未知中文历史值原样保留（不吞掉用户手填的自由文本），未知 ASCII key 退化为「其他」。</summary>
    [Fact]
    public void 关系显示_未知值不吞中文历史值()
    {
        Assert.Equal("患病史", TempReliefConstants.GetRelationshipDisplayName("患病史"));
        Assert.Equal("其他", TempReliefConstants.GetRelationshipDisplayName("UnknownKey"));
    }
}
