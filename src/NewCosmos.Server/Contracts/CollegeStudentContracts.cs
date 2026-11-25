namespace NewCosmos.Server.Contracts;

#region 低收入人口救助帮扶 · 大学生管理

/// <summary>
/// 大学生档案列表项。字段与旧版 CollegeStudent 实体一致，并附带只读展示/分栏标志：
/// IsHouseholdStopped（保障已终止）、IsBeyondJobSeekingPeriod（已超择业期）用于「已关联 / 已退出」分栏，
/// IsGraduatingThisYear 用于「今年毕业」提示。
/// </summary>
public sealed record CollegeStudentListItemDto(
    long Id,
    long ApplicationId,
    long FamilyMemberId,
    string StudentName,
    string IdCard,
    string EducationLevel,
    string SchoolName,
    int? SchoolDuration,
    string SchoolDurationDisplay,
    int? EnrollmentYear,
    int? GraduationYear,
    string Status,
    string StatusDisplay,
    string Remark,
    bool IsHouseholdStopped,
    bool IsBeyondJobSeekingPeriod,
    bool IsGraduatingThisYear);

/// <summary>
/// 可关联的 18-23 周岁家庭成员（未建立大学生档案）。
/// 户主、所在、联系方式由 Core 的 SearchEligibleMembersAsync 统一装配。
/// </summary>
public sealed record EligibleCollegeMemberDto(
    long Id,
    long ApplicationId,
    string Name,
    string IdCard,
    string Gender,
    int? Age,
    string MemberCategory,
    string HouseholdHeadName,
    string Phone,
    string HomeLocationDisplay,
    string GenderAgeDisplay);

/// <summary>
/// 新增/编辑大学生档案请求。
/// Status 传常量键（Studying/Graduated/NotEligible/NoHigherEducation）；
/// 状态为 NoHigherEducation 时学校信息可留空（与旧版表单一致）。
/// </summary>
public sealed record CollegeStudentFormRequest(
    long ApplicationId,
    long FamilyMemberId,
    string? StudentName,
    string? IdCard,
    string? EducationLevel,
    string? SchoolName,
    int? SchoolDuration,
    int? EnrollmentYear,
    string? Status,
    string? Remark);

/// <summary>新增成功返回的档案 ID。</summary>
public sealed record CollegeStudentCreatedResponse(long Id);

#endregion
