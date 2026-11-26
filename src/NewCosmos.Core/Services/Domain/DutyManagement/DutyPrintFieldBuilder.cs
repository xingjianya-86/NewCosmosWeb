using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;

namespace NewCosmos.Services.Domain.DutyManagement;

/// <summary>
/// 政务值班表模板字段装配：31 天 × 10 字段（日期/星期与农历/领导姓名/领导电话/中层姓名/
/// 中层电话/女生姓名/女生电话/男生姓名/男生电话）。模板渲染由 Windows Agent 完成。
/// </summary>
public interface IDutyPrintFieldBuilder
{
    /// <summary>
    /// 构建模板占位符字典：{日期N}、{星期与农历N}、{领导姓名N}、{领导电话N}、
    /// {中层姓名N}、{中层电话N}、{女生姓名N}、{女生电话N}、{男生姓名N}、{男生电话N}（N=1..31）。
    /// </summary>
    Dictionary<string, string> Build(DutyMonthSchedule monthSchedule);
}

/// <summary>政务值班表模板字段装配实现（口径：legacy DutyService.Schedule.cs BuildGovTemplateFields）。</summary>
public sealed class DutyPrintFieldBuilder : IDutyPrintFieldBuilder
{
    public Dictionary<string, string> Build(DutyMonthSchedule monthSchedule)
    {
        ArgumentNullException.ThrowIfNull(monthSchedule);

        var fields = new Dictionary<string, string>();
        var daysInMonth = DateTime.DaysInMonth(monthSchedule.Year, monthSchedule.Month);
        var empty = DutyConstants.OutputRules.EmptyCellPlaceholder;

        for (var day = 1; day <= 31; day++)
        {
            if (day > daysInMonth)
            {
                fields[$"{{日期{day}}}"] = DutyConstants.OutputRules.OverflowCellPlaceholder;
                fields[$"{{星期与农历{day}}}"] = DutyConstants.OutputRules.OverflowCellPlaceholder;
                fields[$"{{领导姓名{day}}}"] = DutyConstants.OutputRules.OverflowCellPlaceholder;
                fields[$"{{领导电话{day}}}"] = DutyConstants.OutputRules.OverflowCellPlaceholder;
                fields[$"{{中层姓名{day}}}"] = DutyConstants.OutputRules.OverflowCellPlaceholder;
                fields[$"{{中层电话{day}}}"] = DutyConstants.OutputRules.OverflowCellPlaceholder;
                fields[$"{{女生姓名{day}}}"] = DutyConstants.OutputRules.OverflowCellPlaceholder;
                fields[$"{{女生电话{day}}}"] = DutyConstants.OutputRules.OverflowCellPlaceholder;
                fields[$"{{男生姓名{day}}}"] = DutyConstants.OutputRules.OverflowCellPlaceholder;
                fields[$"{{男生电话{day}}}"] = DutyConstants.OutputRules.OverflowCellPlaceholder;
                continue;
            }

            var row = monthSchedule.Days[day - 1];
            var middleName = row.IsHoliday
                ? (string.IsNullOrEmpty(row.MiddleCell.MemberName) ? empty : row.MiddleCell.MemberName)
                : empty; // 中层仅法定节假日出场（确认口径：非节假日填"—"）

            // 电话跟随姓名：无值班人时电话同样填"—"；有值班人但未填手机号时留空
            fields[$"{{日期{day}}}"] = row.DutyDate.ToString(DutyConstants.OutputRules.DateCellFormat);
            fields[$"{{星期与农历{day}}}"] = LunarCalendarHelper.GetWeekAndLunarString(row.DutyDate, row.HolidayName);
            fields[$"{{领导姓名{day}}}"] = string.IsNullOrEmpty(row.LeaderCell.MemberName) ? empty : row.LeaderCell.MemberName;
            fields[$"{{领导电话{day}}}"] = string.IsNullOrEmpty(row.LeaderCell.MemberName) ? empty : row.LeaderCell.Phone;
            fields[$"{{中层姓名{day}}}"] = middleName;
            fields[$"{{中层电话{day}}}"] = middleName == empty ? empty : row.MiddleCell.Phone;
            fields[$"{{女生姓名{day}}}"] = string.IsNullOrEmpty(row.FemaleCell.MemberName) ? empty : row.FemaleCell.MemberName;
            fields[$"{{女生电话{day}}}"] = string.IsNullOrEmpty(row.FemaleCell.MemberName) ? empty : row.FemaleCell.Phone;
            fields[$"{{男生姓名{day}}}"] = string.IsNullOrEmpty(row.MaleCell.MemberName) ? empty : row.MaleCell.MemberName;
            fields[$"{{男生电话{day}}}"] = string.IsNullOrEmpty(row.MaleCell.MemberName) ? empty : row.MaleCell.Phone;
        }

        return fields;
    }
}
