namespace NewCosmos.Models.Results;

/// <summary>月报预览数据：一张表一页，供 HTML/前端预览使用。</summary>
public sealed class MonthlyFormPreviewData
{
    public string Title { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public List<MonthlyFormPreviewColumn> Columns { get; set; } = new();
    public List<Dictionary<string, string>> Rows { get; set; } = new();
    public List<KeyValuePair<string, string>> Paragraphs { get; set; } = new();
}

/// <summary>月报预览列定义。</summary>
public sealed class MonthlyFormPreviewColumn
{
    public string FieldKey { get; set; } = string.Empty;
    public string Header { get; set; } = string.Empty;
}
