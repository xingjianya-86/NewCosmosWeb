using NewCosmos.Helpers;
using NewCosmos.Models.Results;
using Xunit;

namespace NewCosmos.Core.Tests;

/// <summary>
/// CHANGELOG.md 解析（2026-11-15「更新日志随包内置」）：
/// 与发版脚本 Get-ChangelogSection 同口径分节，输出带随包来源的升级日志条目。
/// </summary>
public class ChangelogMarkdownParserTests
{
    private const string Sample = """
        # 更新日志

        > 本文件是**升级日志的唯一素材来源**（引用头部：解析应忽略）。

        ---

        ## [未发布]

        ### 新增
        - 功能甲
        - 功能乙

        ### 修复
        - 修复丙

        ---

        ## [1.2.20261109] - 2026-11-09

        ### 变更
        - 历史版本说明

        ## [1.1.20260901] - 2026-09-01

        - 无分组条目
        """;

    [Fact]
    public void Parse_按版本分节_忽略头部与分节线()
    {
        var items = ChangelogMarkdownParser.Parse(Sample);

        Assert.Equal(3, items.Count);
        Assert.Equal(new[] { "未发布", "1.2.20261109", "1.1.20260901" },
            items.Select(i => i.Version).ToArray());
    }

    [Fact]
    public void Parse_日期解析与缺失()
    {
        var items = ChangelogMarkdownParser.Parse(Sample);

        Assert.Equal(DateTime.MinValue, items[0].UpdatedAt);           // [未发布] 无日期
        Assert.Equal(new DateTime(2026, 11, 9), items[1].UpdatedAt);   // - 2026-11-09
        Assert.Equal(new DateTime(2026, 9, 1), items[2].UpdatedAt);
    }

    [Fact]
    public void Parse_保留分组标题与条目原文()
    {
        var items = ChangelogMarkdownParser.Parse(Sample);

        var notes = items[0].Notes;
        Assert.Contains("### 新增", notes);
        Assert.Contains("- 功能甲", notes);
        Assert.Contains("### 修复", notes);
        Assert.Contains("- 修复丙", notes);
        // 头部说明与分节线不得混入
        Assert.DoesNotContain("唯一素材来源", notes);
        Assert.DoesNotContain("---", notes);
    }

    [Fact]
    public void Parse_来源标记为随包()
    {
        var items = ChangelogMarkdownParser.Parse(Sample);
        Assert.All(items, i => Assert.Equal(UpdateHistoryItem.SourceBundled, i.Source));
        Assert.Equal("随包", items[0].SourceName);
    }

    [Fact]
    public void Parse_空入参_空表不抛()
    {
        Assert.Empty(ChangelogMarkdownParser.Parse(null));
        Assert.Empty(ChangelogMarkdownParser.Parse(""));
        Assert.Empty(ChangelogMarkdownParser.Parse("   \n  "));
    }

    // 已迁移：前 5 个纯解析用例。
    // 跳过（MIGRATION_NOTES.md §文件系统/宿主资源）：
    // · Parse_真实CHANGELOG结构_能解析出已发布版本 —— 需向上遍历磁盘查找仓库 CHANGELOG.md；
    // · 随包内置CHANGELOG资源_存在且可解析 —— 依赖 Desktop 宿主程序集 UpdateHistoryService 与嵌入资源。
}
