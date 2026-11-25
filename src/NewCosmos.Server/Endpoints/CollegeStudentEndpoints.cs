using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 低收入人口救助帮扶 → 大学生管理接口。
/// 只读：档案列表（已关联/已退出分栏由前端按实体计算列判断）、可关联成员搜索、今年毕业提醒；
/// 写入：关联新增、编辑、软删除。全部端点统一要求 COLLEGE_MANAGE 权限并校验登录会话。
/// 业务 SQL / 分栏口径全部在 Core <see cref="ICollegeStudentService"/>，本层只做 DTO 投影与入参校验。
/// </summary>
public static class CollegeStudentEndpoints
{
    /// <summary>可写入的状态键（与 CollegeStudentConstants 保持一致，不接受任意字符串）。</summary>
    private static readonly string[] AllowedStatuses =
    {
        CollegeStudentConstants.StatusStudying,
        CollegeStudentConstants.StatusGraduated,
        CollegeStudentConstants.StatusNotEligible,
        CollegeStudentConstants.StatusNoHigherEducation
    };

    public static IEndpointRouteBuilder MapCollegeStudentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/social-assistance/college-students", ListAsync);
        app.MapGet("/api/social-assistance/college-students/eligible-members", ListEligibleMembersAsync);
        app.MapGet("/api/social-assistance/college-students/graduating", ListGraduatingAsync);
        app.MapPost("/api/social-assistance/college-students", CreateAsync);
        app.MapPut("/api/social-assistance/college-students/{id:long}", UpdateAsync);
        app.MapDelete("/api/social-assistance/college-students/{id:long}", DeleteAsync);
        return app;
    }

    // ───────────────────────────── 只读 ─────────────────────────────

    /// <summary>全部大学生档案（姓名/身份证关键字可选）；前端按 IsHouseholdStopped/Status 分「已关联 / 已退出」。</summary>
    private static async Task<IResult> ListAsync(
        HttpContext context,
        INewPermissionService permissions,
        ICollegeStudentService collegeStudents,
        CancellationToken ct,
        string? keyword)
    {
        var denied = await RequireManageAsync(context, permissions, ct);
        if (denied is not null)
        {
            return denied;
        }

        var result = await collegeStudents.GetAllAsync(keyword, ct);
        if (result.IsFailure)
        {
            return ApiErrors.FromResult(context, result);
        }

        var items = (result.Value ?? new List<CollegeStudent>())
            .Select(MapStudent)
            .ToList();

        return Results.Json(items);
    }

    /// <summary>可关联成员：18-23 周岁、未建立大学生档案、且不属于其他困难人员的家庭成员。</summary>
    private static async Task<IResult> ListEligibleMembersAsync(
        HttpContext context,
        INewPermissionService permissions,
        ICollegeStudentService collegeStudents,
        CancellationToken ct,
        string? keyword)
    {
        var denied = await RequireManageAsync(context, permissions, ct);
        if (denied is not null)
        {
            return denied;
        }

        var result = await collegeStudents.SearchEligibleMembersAsync(keyword, ct);
        if (result.IsFailure)
        {
            return ApiErrors.FromResult(context, result);
        }

        var items = (result.Value ?? new List<FamilyMember>())
            .Select(MapMember)
            .ToList();

        return Results.Json(items);
    }

    /// <summary>今年（或指定年份）毕业的在读/已毕业大学生，用于年度毕业提醒与「今年毕业」分栏。</summary>
    private static async Task<IResult> ListGraduatingAsync(
        HttpContext context,
        INewPermissionService permissions,
        ICollegeStudentService collegeStudents,
        CancellationToken ct,
        int? year)
    {
        var denied = await RequireManageAsync(context, permissions, ct);
        if (denied is not null)
        {
            return denied;
        }

        var targetYear = year ?? DateTime.Today.Year;
        if (targetYear is < 1900 or > 2200)
        {
            return InvalidRequest(context, "毕业年份无效");
        }

        var result = await collegeStudents.GetGraduatingStudentsAsync(targetYear, ct);
        if (result.IsFailure)
        {
            return ApiErrors.FromResult(context, result);
        }

        var items = (result.Value ?? new List<CollegeStudent>())
            .Select(MapStudent)
            .ToList();

        return Results.Json(items);
    }

    // ───────────────────────────── 写入 ─────────────────────────────

    /// <summary>关联新增大学生档案（FamilyMemberId 来自可关联成员列表）。</summary>
    private static async Task<IResult> CreateAsync(
        CollegeStudentFormRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        ICollegeStudentService collegeStudents,
        CancellationToken ct)
    {
        var denied = await RequireManageAsync(context, permissions, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (request is null)
        {
            return InvalidRequest(context, "请求体不能为空");
        }

        if (request.FamilyMemberId <= 0)
        {
            return InvalidRequest(context, "请选择要关联的家庭成员");
        }

        var validation = Validate(request, out var status);
        if (validation is not null)
        {
            return InvalidRequest(context, validation);
        }

        var student = ToEntity(request, status);
        var result = await collegeStudents.SaveAsync(student, ct);

        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(new CollegeStudentCreatedResponse(result.Value));
    }

    /// <summary>编辑大学生档案（学校/学历/学制/入学年份/状态/备注）。</summary>
    private static async Task<IResult> UpdateAsync(
        long id,
        CollegeStudentFormRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        ICollegeStudentService collegeStudents,
        CancellationToken ct)
    {
        var denied = await RequireManageAsync(context, permissions, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (request is null)
        {
            return InvalidRequest(context, "请求体不能为空");
        }

        var validation = Validate(request, out var status);
        if (validation is not null)
        {
            return InvalidRequest(context, validation);
        }

        var student = ToEntity(request, status);
        student.Id = id;

        var result = await collegeStudents.UpdateAsync(student, ct);
        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.NoContent();
    }

    /// <summary>删除大学生档案（Core 执行软删除 deleted_at）。</summary>
    private static async Task<IResult> DeleteAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        ICollegeStudentService collegeStudents,
        CancellationToken ct)
    {
        var denied = await RequireManageAsync(context, permissions, ct);
        if (denied is not null)
        {
            return denied;
        }

        var result = await collegeStudents.DeleteAsync(id, ct);
        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.NoContent();
    }

    // ───────────────────────────── 校验 / 投影 ─────────────────────────────

    /// <summary>
    /// 与旧版 CollegeStudentManagementViewModel.SaveStudentAsync 同序校验：
    /// 姓名必填；非「未收到高等教育」时学校名称/学历/学制/入学年份必填。
    /// </summary>
    private static string? Validate(CollegeStudentFormRequest request, out string status)
    {
        status = string.IsNullOrWhiteSpace(request.Status)
            ? CollegeStudentConstants.StatusStudying
            : request.Status.Trim();

        if (Array.IndexOf(AllowedStatuses, status) < 0)
        {
            return "状态无效";
        }

        if (string.IsNullOrWhiteSpace(request.StudentName))
        {
            return "请输入大学生姓名";
        }

        var noHigherEducation = status == CollegeStudentConstants.StatusNoHigherEducation;
        if (!noHigherEducation)
        {
            if (string.IsNullOrWhiteSpace(request.SchoolName))
            {
                return "请输入学校名称";
            }

            if (string.IsNullOrWhiteSpace(request.EducationLevel))
            {
                return "请选择学历层次";
            }

            if (request.SchoolDuration is null)
            {
                return "请选择学年制";
            }

            if (request.EnrollmentYear is null)
            {
                return "请选择入学年份";
            }
        }

        if (!string.IsNullOrWhiteSpace(request.EducationLevel)
            && Array.IndexOf(CollegeStudentConstants.EducationLevelOptions, request.EducationLevel.Trim()) < 0)
        {
            return "学历层次无效";
        }

        if (request.SchoolDuration is { } duration
            && string.IsNullOrEmpty(CollegeStudentConstants.GetSchoolDurationDisplay(duration)))
        {
            return "学年制无效";
        }

        return null;
    }

    private static CollegeStudent ToEntity(CollegeStudentFormRequest request, string status)
    {
        return new CollegeStudent
        {
            ApplicationId = request.ApplicationId,
            FamilyMemberId = request.FamilyMemberId,
            StudentName = request.StudentName?.Trim() ?? string.Empty,
            IdCard = TrimOrNull(request.IdCard),
            EducationLevel = TrimOrNull(request.EducationLevel),
            SchoolName = TrimOrNull(request.SchoolName),
            SchoolDuration = request.SchoolDuration,
            EnrollmentYear = request.EnrollmentYear,
            Status = status,
            Remark = TrimOrNull(request.Remark)
        };
    }

    private static CollegeStudentListItemDto MapStudent(CollegeStudent student)
    {
        var status = student.Status ?? string.Empty;
        var statusDisplay = student.StatusDisplay;
        if (string.IsNullOrWhiteSpace(statusDisplay))
        {
            statusDisplay = status;
        }

        return new CollegeStudentListItemDto(
            student.Id,
            student.ApplicationId,
            student.FamilyMemberId,
            student.StudentName ?? string.Empty,
            student.IdCard ?? string.Empty,
            student.EducationLevel ?? string.Empty,
            student.SchoolName ?? string.Empty,
            student.SchoolDuration,
            student.SchoolDurationDisplay,
            student.EnrollmentYear,
            student.GraduationYear,
            status,
            statusDisplay,
            student.Remark ?? string.Empty,
            student.IsHouseholdStopped,
            student.IsBeyondJobSeekingPeriod,
            student.IsGraduatingThisYear);
    }

    private static EligibleCollegeMemberDto MapMember(FamilyMember member)
    {
        var gender = string.IsNullOrWhiteSpace(member.Gender)
            ? IdCardValidator.ExtractGender(member.IdCard) ?? string.Empty
            : member.Gender;
        var age = member.Age ?? IdCardValidator.ExtractAgeBasic(member.IdCard);
        var genderAgeDisplay = string.IsNullOrWhiteSpace(member.GenderAgeDisplay)
            ? (age.HasValue ? $"{gender} {age.Value}岁".Trim() : gender)
            : member.GenderAgeDisplay;

        return new EligibleCollegeMemberDto(
            member.Id,
            member.ApplicationId,
            member.Name ?? string.Empty,
            member.IdCard ?? string.Empty,
            gender,
            age,
            member.MemberCategory ?? string.Empty,
            member.HouseholdHeadName ?? string.Empty,
            member.Phone ?? string.Empty,
            member.HomeLocationDisplay ?? string.Empty,
            genderAgeDisplay);
    }

    private static string? TrimOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // ───────────────────────────── 通用 ─────────────────────────────

    private static Task<IResult?> RequireManageAsync(
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
        => EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.COLLEGE_MANAGE, ct);

    private static IResult InvalidRequest(HttpContext context, string message)
        => ApiErrors.Write(
            context,
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            message);
}
