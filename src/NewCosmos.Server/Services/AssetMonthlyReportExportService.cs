using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Results;
using NewCosmos.Services.Database;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace NewCosmos.Server.Services;

/// <summary>
/// 核对月报表 / 周报导出：按周期（或全部月份）筛选 <c>nc_biz_asset_checks</c>，
/// 默认展开同一 <c>batch_id</c> 的整户成员，勾选“仅导出当前状态人员”时只保留状态命中行。
/// <para>
/// 列口径沿用旧版核对月报表名册：序号 / 姓名 / 身份证号 / 证件类型 / 与户主关系 / 是否户主 /
/// 家庭住址 / 村社区 / 申请原因 / 申请日期 / 当前状态 / 批次号，另附
/// <c>nc_biz_asset_check_operators</c> 的经办人与经办单位。
/// </para>
/// <para>
/// 关系 / 证件类型 / 申请原因与村社区名称均通过字典、区划表转中文；字典缺失时回退原始值，不臆造。
/// 无数据也返回仅含表头的 xlsx。
/// </para>
/// </summary>
public sealed class AssetMonthlyReportExportService(
    IDatabaseService database,
    DataScopeFilterBuilder dataScope)
{
    public const string ContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public const string ZipContentType = "application/zip";

    private const int MaxExportRows = 100_000;
    private const string WorksheetName = "核对名册";

    /// <summary>旧版《通用_经济核查汇总表》模板分类（nc_biz_templates.categories）。</summary>
    private const string MonthlyReportTemplateCategory = "AssetVerificationMonthlyReport";

    /// <summary>模板每页最多 28 人；超过该页数时回退名册，避免一次生成上千个文件。</summary>
    private const int MaxTemplatePages = 200;

    private const string SelectColumns =
        @"v.id, v.batch_id, v.applicant_name, v.applicant_id_card, v.applicant_id_type,
          COALESCE((SELECT i.item_value FROM nc_dict_items i
                    WHERE i.category = 'FamilyRelationships'
                      AND i.item_key = v.relationship AND i.is_active LIMIT 1),
                   NULLIF(v.relationship, '')) AS relationship_display,
          COALESCE((SELECT i.item_value FROM nc_dict_items i
                    WHERE i.category = 'IdTypes'
                      AND i.item_key = v.applicant_id_type AND i.is_active LIMIT 1),
                   NULLIF(v.applicant_id_type, '')) AS id_type_display,
          v.relationship, v.head_id_card, v.is_head, v.family_address,
          COALESCE((SELECT rv.village_name FROM nc_regions_villages rv
                    WHERE rv.village_code = v.community LIMIT 1),
                   NULLIF(v.community, '')) AS community_display,
          COALESCE((SELECT i.item_value FROM nc_dict_items i
                    WHERE i.category = 'ApplicationReasons'
                      AND i.item_key = v.application_reason AND i.is_active LIMIT 1),
                   NULLIF(v.application_reason, '')) AS application_reason_display,
          v.application_date, v.status,
          COALESCE(op.operator_name, '') AS operator_name,
          COALESCE(op.operator_unit_name, '') AS operator_unit_name";

    private const string FromClause =
        @"nc_biz_asset_checks v
          LEFT JOIN LATERAL (
              SELECT o.operator_name, o.operator_unit_name
              FROM nc_biz_asset_check_operators o
              WHERE o.batch_id = v.batch_id
              LIMIT 1
          ) op ON TRUE";

    private const string OrderByClause =
        "v.application_date, v.batch_id, v.is_head DESC, v.id";

    /// <summary>
    /// 生成导出文件。<paramref name="userId"/> 用于叠加村（community）数据范围，
    /// 防止仅有报表权限的账号越权导出其他村名册。
    /// </summary>
    public async Task<Result<AssetMonthlyReportExportFile>> ExportAsync(
        int userId,
        AssetMonthlyReportExportRequest request,
        CancellationToken ct = default)
    {
        var period = ResolvePeriod(request);
        var (where, parameters) = await BuildWhereAsync(userId, request, period, ct);

        var targetSql =
            $"SELECT {SelectColumns} FROM {FromClause}{where} " +
            $"ORDER BY {OrderByClause} LIMIT {MaxExportRows}";

        var targetResult = await database.QueryAsync<AssetMonthlyReportRow>(
            targetSql, ct, parameters.ToArray());
        if (targetResult.IsFailure)
        {
            return Result.Failure<AssetMonthlyReportExportFile>(
                targetResult.ErrorCode!, targetResult.Message!);
        }

        var targetRows = targetResult.Value ?? [];
        IReadOnlyList<AssetMonthlyReportRow> exportRows = targetRows;

        // 默认展开整户：目标行只用于确定 batch，成员查询不再筛状态与日期。
        if (!request.OnlyCurrent && targetRows.Count > 0)
        {
            var batchIds = targetRows
                .Select(row => row.BatchId)
                .Where(batchId => !string.IsNullOrWhiteSpace(batchId))
                .Select(batchId => batchId!)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (batchIds.Length > 0)
            {
                var memberSql =
                    $"SELECT {SelectColumns} FROM {FromClause} " +
                    "WHERE v.batch_id = ANY($1) AND v.deleted_at IS NULL " +
                    $"ORDER BY {OrderByClause} LIMIT {MaxExportRows}";

                var memberResult = await database.QueryAsync<AssetMonthlyReportRow>(
                    memberSql, ct, (object)batchIds);
                if (memberResult.IsFailure)
                {
                    return Result.Failure<AssetMonthlyReportExportFile>(
                        memberResult.ErrorCode!, memberResult.Message!);
                }

                exportRows = targetRows
                    .Concat(memberResult.Value ?? [])
                    .GroupBy(row => row.Id)
                    .Select(group => group.First())
                    .ToList();
            }
        }

        var orderedRows = exportRows
            .OrderBy(row => row.ApplicationDate)
            .ThenBy(row => row.BatchId, StringComparer.Ordinal)
            .ThenByDescending(row => row.IsHead)
            .ThenBy(row => row.Id)
            .ToList();

        // 旧版核对月报表走 ArchiveOutputPage 的模板输出：
        // nc_biz_templates 中 categories 含 AssetVerificationMonthlyReport 的 xlsx 模板，
        // 按 FAMILY_MEMBER_NAME_1..28 索引槽每页 28 人；超过一页时生成多个独立 xlsx。
        try
        {
            var template = await LoadTemplateAsync(ct);
            if (template is not null)
            {
                var files = BuildTemplateFiles(template, exportRows, request, period);
                if (files.Count > 0)
                {
                    return Result.Success(PackTemplateFiles(files, period.FileName));
                }
            }
        }
        catch (Exception)
        {
            // 模板损坏/格式不符时回退到名册导出，避免整次导出失败。
        }

        byte[] content;
        try
        {
            content = BuildWorkbook(orderedRows);
        }
        catch (Exception ex)
        {
            return Result.Failure<AssetMonthlyReportExportFile>(
                ErrorCodes.UNKNOWN_ERROR, $"生成核对月报表失败: {ex.Message}");
        }

        return Result.Success(
            new AssetMonthlyReportExportFile(content, period.FileName, ContentType));
    }

    /// <summary>读取旧版《通用_经济核查汇总表》模板（分类 AssetVerificationMonthlyReport）。</summary>
    private async Task<MonthlyReportTemplate?> LoadTemplateAsync(CancellationToken ct)
    {
        const string sql = @"
            SELECT file_data, config_json::text AS config_json
            FROM nc_biz_templates
            WHERE file_type = 'xlsx'
              AND $1 = ANY(categories)
            ORDER BY sort_order, id
            LIMIT 1";

        var result = await database.QuerySingleAsync<MonthlyReportTemplateRow>(
            sql, ct, MonthlyReportTemplateCategory);
        if (result.IsFailure)
        {
            return null;
        }

        var row = result.Value;
        if (row is null || row.FileData is not { Length: > 0 })
        {
            return null;
        }

        return new MonthlyReportTemplate(row.FileData, row.ConfigJson);
    }

    /// <summary>
    /// 按模板生成一页一个 xlsx：每页最多 28 人，超出时拆成多个独立文件。
    /// 页数过多（&gt; <see cref="MaxTemplatePages"/>）时返回空列表，由调用方回退名册导出。
    /// </summary>
    private static List<TemplateExportFile> BuildTemplateFiles(
        MonthlyReportTemplate template,
        IReadOnlyList<AssetMonthlyReportRow> rows,
        AssetMonthlyReportExportRequest request,
        ExportPeriod period)
    {
        var fields = ParseTemplateFields(template.ConfigJson);
        var capacity = GetIndexSlotCapacity(fields);
        if (capacity <= 0)
        {
            return [];
        }

        // 与旧版 MonthlyReportViewModel 一致：按户主身份证聚在一起，户主/配偶优先。
        var orderedRows = rows
            .OrderBy(row => row.HeadIdCard ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(RelationPriority)
            .ThenBy(row => row.Id)
            .ToList();

        var pageCount = Math.Max(1, (orderedRows.Count + capacity - 1) / capacity);
        if (pageCount > MaxTemplatePages)
        {
            return [];
        }

        var baseName = Path.GetFileNameWithoutExtension(period.FileName);
        var contextValues = BuildReportContextValues(request, period);
        var files = new List<TemplateExportFile>(pageCount);

        for (var page = 0; page < pageCount; page++)
        {
            var pageRows = orderedRows
                .Skip(page * capacity)
                .Take(capacity)
                .ToList();

            var content = BuildTemplatePage(template.Data, fields, capacity, pageRows, contextValues);
            var fileName = pageCount == 1
                ? $"{baseName}.xlsx"
                : $"{baseName}_{page + 1}.xlsx";

            files.Add(new TemplateExportFile(fileName, content));
        }

        return files;
    }

    private static byte[] BuildTemplatePage(
        byte[] templateData,
        IReadOnlyList<TemplateField> fields,
        int capacity,
        IReadOnlyList<AssetMonthlyReportRow> rows,
        IReadOnlyDictionary<string, string> contextValues)
    {
        ExcelPackage.License.SetNonCommercialOrganization("民政社会救助管理系统");

        using var package = new ExcelPackage(new MemoryStream(templateData, writable: false));
        var sheet = package.Workbook.Worksheets[0]
            ?? throw new InvalidOperationException("模板缺少工作表");

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in contextValues)
        {
            values[key] = value;
        }

        for (var index = 0; index < capacity; index++)
        {
            var row = index < rows.Count ? rows[index] : null;
            var suffix = index + 1;

            values[$"FAMILY_MEMBER_NAME_{suffix}"] = row?.ApplicantName ?? string.Empty;
            values[$"FAMILY_MEMBER_ID_CARD_{suffix}"] = row?.ApplicantIdCard ?? string.Empty;
            values[$"FAMILY_MEMBER_RELATION_{suffix}"] = row?.RelationshipDisplay ?? string.Empty;
            values[$"HEAD_ID_CARD_{suffix}"] = row?.HeadIdCard ?? string.Empty;
            values[$"FAMILY_MEMBER_ADDRESS_{suffix}"] = row is null
                ? string.Empty
                : BuildTemplateAddress(row);
        }

        var placeholders = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            if (IsImageFieldKey(field.FieldKey))
            {
                continue;
            }

            if (values.TryGetValue(field.FieldKey, out var value))
            {
                placeholders[field.Placeholder] = value ?? string.Empty;
            }
            else if (!string.IsNullOrEmpty(field.DefaultValue))
            {
                placeholders[field.Placeholder] = field.DefaultValue!;
            }
        }

        ReplacePlaceholders(sheet, placeholders);
        return package.GetAsByteArray();
    }

    private static void ReplacePlaceholders(
        ExcelWorksheet sheet,
        IReadOnlyDictionary<string, string> placeholders)
    {
        if (placeholders.Count == 0 || sheet.Dimension is not { } dimension)
        {
            return;
        }

        for (var row = dimension.Start.Row; row <= dimension.End.Row; row++)
        {
            for (var column = dimension.Start.Column; column <= dimension.End.Column; column++)
            {
                var cell = sheet.Cells[row, column];
                var text = cell.Text;
                if (string.IsNullOrEmpty(text) || !text.Contains('{'))
                {
                    continue;
                }

                var replaced = text;
                foreach (var (placeholder, value) in placeholders)
                {
                    if (replaced.Contains(placeholder, StringComparison.Ordinal))
                    {
                        replaced = replaced.Replace(placeholder, value, StringComparison.Ordinal);
                    }
                }

                if (!string.Equals(replaced, text, StringComparison.Ordinal))
                {
                    cell.Value = replaced;
                    if (replaced.Contains('\n'))
                    {
                        cell.Style.WrapText = true;
                    }
                }
            }
        }
    }

    private static List<TemplateField> ParseTemplateFields(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(configJson);
            if (!document.RootElement.TryGetProperty("fields", out var fields)
                || fields.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var result = new List<TemplateField>();
            foreach (var item in fields.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var fieldKey = ReadString(item, "fieldKey");
                var placeholder = ReadString(item, "placeholder");
                if (string.IsNullOrWhiteSpace(fieldKey) || string.IsNullOrWhiteSpace(placeholder))
                {
                    continue;
                }

                result.Add(new TemplateField(
                    fieldKey!,
                    placeholder!,
                    ReadString(item, "defaultValue")));
            }

            return result;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private static int GetIndexSlotCapacity(IReadOnlyList<TemplateField> fields)
    {
        const string prefix = "FAMILY_MEMBER_NAME_";
        var capacity = 0;

        foreach (var field in fields)
        {
            if (!field.FieldKey.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (int.TryParse(field.FieldKey[prefix.Length..], out var index) && index > capacity)
            {
                capacity = index;
            }
        }

        return capacity;
    }

    private static int RelationPriority(AssetMonthlyReportRow row)
    {
        var relationship = (row.Relationship ?? string.Empty).Trim();
        if (row.IsHead || string.Equals(relationship, "Head", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (string.Equals(relationship, "Spouse", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 2;
    }

    /// <summary>
    /// 与旧版 AddressResolver.BuildAddress 同口径：
    /// 详细地址为空时用村社区名；详细地址已含镇/村/街道时直接使用；否则村社区名 + 门牌。
    /// </summary>
    private static string BuildTemplateAddress(AssetMonthlyReportRow row)
    {
        var detail = (row.FamilyAddress ?? string.Empty).Trim();
        var community = (row.CommunityDisplay ?? string.Empty).Trim();

        if (detail.Length == 0)
        {
            return community;
        }

        if (detail.Contains('镇') || detail.Contains('村') || detail.Contains("街道", StringComparison.Ordinal))
        {
            return detail;
        }

        if (community.Length == 0
            || detail.StartsWith(community, StringComparison.Ordinal))
        {
            return detail;
        }

        return community + detail;
    }

    private static Dictionary<string, string> BuildReportContextValues(
        AssetMonthlyReportExportRequest request,
        ExportPeriod period)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["REPORT_PERIOD"] = DescribePeriod(request, period),
            ["REPORT_ROSTER_SCOPE"] = request.OnlyCurrent ? "当前状态人员" : "整户名册",
            ["REPORT_LABEL"] = DescribeReportLabel(request.Status),
        };

        if (period.Start is { } start)
        {
            values["REPORT_START_DATE"] = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (period.EndExclusive is { } end)
        {
            values["REPORT_END_DATE"] = end
                .AddDays(-1)
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return values;
    }

    private static string DescribePeriod(
        AssetMonthlyReportExportRequest request,
        ExportPeriod period)
    {
        if (request.AllMonths)
        {
            return "全部月份";
        }

        if (request.WeekStart is { } weekStart)
        {
            return $"{ISOWeek.GetYear(weekStart)}年第{ISOWeek.GetWeekOfYear(weekStart)}周";
        }

        var year = request.Year ?? DateTime.Now.Year;
        return request.Month is { } month ? $"{year}年{month}月" : $"{year}年";
    }

    private static string DescribeReportLabel(string? status)
        => status?.Trim() switch
        {
            AssetCheckStatusConstants.SUBMITTED => "已提交申请",
            AssetCheckStatusConstants.VERIFIED => "有报告未建档",
            AssetCheckStatusConstants.INCLUDED => "已建档",
            AssetCheckStatusConstants.REFUSED => "不予认定",
            _ => "全部导出",
        };

    private static bool IsImageFieldKey(string fieldKey)
        => fieldKey.StartsWith("SIGNATURE_", StringComparison.OrdinalIgnoreCase)
           || fieldKey.StartsWith("PHOTO_", StringComparison.OrdinalIgnoreCase);

    private static AssetMonthlyReportExportFile PackTemplateFiles(
        IReadOnlyList<TemplateExportFile> files,
        string baseFileName)
    {
        if (files.Count == 1)
        {
            return new AssetMonthlyReportExportFile(
                files[0].Content, files[0].FileName, ContentType);
        }

        var zipName = Path.GetFileNameWithoutExtension(baseFileName) + ".zip";
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(
                    file.FileName, System.IO.Compression.CompressionLevel.Fastest);
                using var stream = entry.Open();
                stream.Write(file.Content, 0, file.Content.Length);
            }
        }

        return new AssetMonthlyReportExportFile(buffer.ToArray(), zipName, ZipContentType);
    }

    private async Task<(string Where, List<object?> Parameters)> BuildWhereAsync(
        int userId,
        AssetMonthlyReportExportRequest request,
        ExportPeriod period,
        CancellationToken ct)
    {
        var conditions = new List<string> { "v.deleted_at IS NULL" };
        var parameters = new List<object?>();

        if (period.Start is { } start)
        {
            parameters.Add(start);
            conditions.Add($"v.application_date >= ${parameters.Count}");
        }

        if (period.EndExclusive is { } endExclusive)
        {
            parameters.Add(endExclusive);
            conditions.Add($"v.application_date < ${parameters.Count}");
        }

        var status = Normalize(request.Status);
        if (status is not null)
        {
            parameters.Add(status);
            conditions.Add($"v.status = ${parameters.Count}");
        }

        var keyword = Normalize(request.Keyword);
        if (keyword is not null)
        {
            parameters.Add(ToLikePattern(keyword));
            var index = parameters.Count;
            conditions.Add(
                $"(v.applicant_name ILIKE ${index} OR v.applicant_id_card ILIKE ${index} " +
                $"OR v.head_id_card ILIKE ${index})");
        }

        var scope = await dataScope.BuildAsync(
            userId, DataScopeTarget.AssetChecks, "v", parameters.Count + 1, ct);
        if (!scope.IsEmpty)
        {
            conditions.Add($"({scope.Sql})");
            parameters.AddRange(scope.Parameters);
        }

        return (" WHERE " + string.Join(" AND ", conditions), parameters);
    }

    private static ExportPeriod ResolvePeriod(AssetMonthlyReportExportRequest request)
    {
        if (request.AllMonths)
        {
            return new ExportPeriod(null, null, "核对月报表_全部月份.xlsx");
        }

        if (request.WeekStart is { } weekStart && request.WeekEnd is { } weekEnd)
        {
            var start = weekStart.Date;
            var end = weekEnd.Date;
            var weekYear = ISOWeek.GetYear(start);
            var weekNumber = ISOWeek.GetWeekOfYear(start);
            return new ExportPeriod(
                start,
                end.AddDays(1),
                $"核对周报表_{weekYear}年第{weekNumber}周.xlsx");
        }

        var year = request.Year ?? DateTime.Now.Year;
        if (request.Month is { } month)
        {
            var (start, endExclusive) = ALinePeriodHelper.GetPeriod(year, month);
            return new ExportPeriod(
                start, endExclusive, $"核对月报表_{year}-{month:D2}.xlsx");
        }

        var (yearStart, yearEndExclusive) = ALinePeriodHelper.GetYearPeriod(year);
        return new ExportPeriod(
            yearStart, yearEndExclusive, $"核对月报表_{year}年.xlsx");
    }

    private static byte[] BuildWorkbook(IReadOnlyList<AssetMonthlyReportRow> rows)
    {
        ExcelPackage.License.SetNonCommercialOrganization("民政社会救助管理系统");

        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add(WorksheetName);

        string[] headers =
        [
            "序号", "姓名", "身份证号", "证件类型", "与户主关系", "是否户主",
            "家庭住址", "村社区", "申请原因", "申请日期", "当前状态", "批次号",
            "经办人", "经办单位",
        ];

        for (var column = 1; column <= headers.Length; column++)
        {
            var cell = sheet.Cells[1, column];
            cell.Value = headers[column - 1];
            cell.Style.Font.Bold = true;
            cell.Style.Font.Size = 12;
            cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(
                global::System.Drawing.Color.FromArgb(0xEE, 0xF1, 0xF5));
            cell.Style.Border.Bottom.Style = ExcelBorderStyle.Medium;
        }

        var rowIndex = 2;
        foreach (var row in rows)
        {
            sheet.Cells[rowIndex, 1].Value = rowIndex - 1;
            sheet.Cells[rowIndex, 2].Value = row.ApplicantName ?? string.Empty;
            sheet.Cells[rowIndex, 3].Value = row.ApplicantIdCard ?? string.Empty;
            sheet.Cells[rowIndex, 4].Value = row.IdTypeDisplay ?? string.Empty;
            sheet.Cells[rowIndex, 5].Value = row.RelationshipDisplay ?? string.Empty;
            sheet.Cells[rowIndex, 6].Value = row.IsHead ? "是" : "否";
            sheet.Cells[rowIndex, 7].Value = row.FamilyAddress ?? string.Empty;
            sheet.Cells[rowIndex, 8].Value = row.CommunityDisplay ?? string.Empty;
            sheet.Cells[rowIndex, 9].Value = row.ApplicationReasonDisplay ?? string.Empty;

            if (row.ApplicationDate != default)
            {
                var dateCell = sheet.Cells[rowIndex, 10];
                dateCell.Value = row.ApplicationDate;
                dateCell.Style.Numberformat.Format = "yyyy-mm-dd";
            }

            sheet.Cells[rowIndex, 11].Value = ToStatusText(row.Status);
            sheet.Cells[rowIndex, 12].Value = row.BatchId ?? string.Empty;
            sheet.Cells[rowIndex, 13].Value = row.OperatorName ?? string.Empty;
            sheet.Cells[rowIndex, 14].Value = row.OperatorUnitName ?? string.Empty;

            rowIndex++;
        }

        // 身份证号整列按文本显示，避免 Excel 把 18 位号码转成科学计数法。
        sheet.Column(3).Style.Numberformat.Format = "@";
        sheet.Column(1).Width = 7;
        sheet.Column(2).Width = 12;
        sheet.Column(3).Width = 22;
        sheet.Column(4).Width = 12;
        sheet.Column(5).Width = 12;
        sheet.Column(6).Width = 9;
        sheet.Column(7).Width = 36;
        sheet.Column(8).Width = 16;
        sheet.Column(9).Width = 16;
        sheet.Column(10).Width = 13;
        sheet.Column(11).Width = 15;
        sheet.Column(12).Width = 26;
        sheet.Column(13).Width = 12;
        sheet.Column(14).Width = 22;

        sheet.View.FreezePanes(2, 1);
        if (rowIndex > 2)
        {
            sheet.Cells[1, 1, rowIndex - 1, headers.Length].AutoFilter = true;
        }

        return package.GetAsByteArray();
    }

    /// <summary>导出状态文案与页面统计口径一致（2 固定显示“已建档”）。</summary>
    private static string ToStatusText(string? status) => status?.Trim() switch
    {
        AssetCheckStatusConstants.SUBMITTED => "已提交",
        AssetCheckStatusConstants.VERIFIED => "有报告未建档",
        AssetCheckStatusConstants.INCLUDED => "已建档",
        AssetCheckStatusConstants.REFUSED => "不予认定",
        null or "" => string.Empty,
        var other => other,
    };

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>转义 LIKE 通配符，关键字按字面量匹配（PostgreSQL 默认转义字符为反斜杠）。</summary>
    private static string ToLikePattern(string keyword)
    {
        var escaped = keyword
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        return $"%{escaped}%";
    }

    private sealed record ExportPeriod(
        DateTime? Start,
        DateTime? EndExclusive,
        string FileName);
}

/// <summary>月报 / 周报导出请求；周参数优先于年月，<see cref="AllMonths"/> 优先于两者。</summary>
public sealed class AssetMonthlyReportExportRequest
{
    public int? Year { get; init; }

    /// <summary>为空表示整年（A 线年周期）；<see cref="AllMonths"/> 为 true 时忽略。</summary>
    public int? Month { get; init; }

    /// <summary>周报起始日（含）；与 <see cref="WeekEnd"/> 必须同时提供。</summary>
    public DateTime? WeekStart { get; init; }

    /// <summary>周报结束日（含）。</summary>
    public DateTime? WeekEnd { get; init; }

    public string? Status { get; init; }

    /// <summary>true = 仅导出状态匹配行；false = 展开同批次整户成员。</summary>
    public bool OnlyCurrent { get; init; }

    public bool AllMonths { get; init; }

    public string? Keyword { get; init; }
}

/// <summary>
/// 导出文件快照：内容 + 下载文件名 + Content-Type。
/// 单页模板/名册回退为 xlsx；多页模板打包为 zip，内含多个独立 xlsx。
/// </summary>
public sealed record AssetMonthlyReportExportFile(
    byte[] Content,
    string FileName,
    string ContentType);

/// <summary>名册行：字典 / 区划已在上游 SQL 转成可展示文案。</summary>
public sealed class AssetMonthlyReportRow
{
    public long Id { get; set; }
    public string? BatchId { get; set; }
    public string? ApplicantName { get; set; }
    public string? ApplicantIdCard { get; set; }
    public string? ApplicantIdType { get; set; }
    public string? Relationship { get; set; }
    public string? RelationshipDisplay { get; set; }
    public string? IdTypeDisplay { get; set; }
    public bool IsHead { get; set; }
    public string? HeadIdCard { get; set; }
    public string? FamilyAddress { get; set; }
    public string? CommunityDisplay { get; set; }
    public string? ApplicationReasonDisplay { get; set; }
    public DateTime ApplicationDate { get; set; }
    public string? Status { get; set; }
    public string? OperatorName { get; set; }
    public string? OperatorUnitName { get; set; }
}

internal sealed class MonthlyReportTemplateRow
{
    public byte[]? FileData { get; set; }
    public string? ConfigJson { get; set; }
}

internal sealed record MonthlyReportTemplate(byte[] Data, string? ConfigJson);

internal sealed record TemplateField(string FieldKey, string Placeholder, string? DefaultValue);

internal sealed record TemplateExportFile(string FileName, byte[] Content);
