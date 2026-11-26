using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.DeathStatement;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;

namespace NewCosmos.Services.Domain.ArchiveManagement;

/// <summary>
/// 申报台账读写（提交/作废/归档）+ 死亡联动待办。
/// 移植自 legacy/NewCosmos.Desktop/Services/Domain/ArchiveManagement/DeathStatementService.Declare.cs。
/// Web 端没有 ViewModel 层，因此新增带范围的 DeclareAsync 重载：
/// 服务端强制以当前组织范围覆盖村/镇归属，并执行原 VM 的 DeathDeclareValidator 前置校验。
/// </summary>
public partial class DeathStatementService
{
    #region 申报台账读写

    /// <inheritdoc />
    public async Task<Result<int>> CountRecentValidAsync(string idCard, DateTime deathDate, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idCard))
            return Result.Success(0);

        try
        {
            var countSql = @"SELECT COUNT(*) FROM nc_biz_death_declarations
                             WHERE deceased_id_card = $1 AND deleted_at IS NULL AND status <> 'Revoked'
                               AND death_date >= $2 AND death_date < $3";
            var countResult = await _db.ExecuteScalarAsync<long>(countSql, ct,
                idCard.Trim(), deathDate.Date.AddDays(-DeathDeclarationConstants.DuplicateWindowDays),
                deathDate.Date.AddDays(DeathDeclarationConstants.DuplicateWindowDays + 1));
            if (countResult.IsFailure)
                return Result.Failure<int>(countResult.ErrorCode!, countResult.Message!);
            return Result.Success((int)countResult.Value);
        }
        catch (Exception ex)
        {
            LogException(ex, "CountRecentValidAsync");
            return Result.FromException<int>(ex);
        }
    }

    /// <inheritdoc />
    public async Task<Result<DeathDeclareResult>> DeclareAsync(DeathDeclareRequest request, CancellationToken ct = default)
    {
        if (request == null)
            return Result.Failure<DeathDeclareResult>(ErrorCodes.VALIDATION_FAILED, "申报内容不能为空");
        if (string.IsNullOrWhiteSpace(request.Name))
            return Result.Failure<DeathDeclareResult>(ErrorCodes.VALIDATION_FAILED, "请填写死者姓名");
        if (string.IsNullOrWhiteSpace(request.IdCard))
            return Result.Failure<DeathDeclareResult>(ErrorCodes.VALIDATION_FAILED, "请填写死者身份证号");
        if (!IdCardValidator.IsValid(request.IdCard.Trim()))
            return Result.Failure<DeathDeclareResult>(ErrorCodes.VALIDATION_FAILED, "死者身份证号校验不通过，请核对后重新填写");
        if (request.DeathDate == default)
            return Result.Failure<DeathDeclareResult>(ErrorCodes.VALIDATION_FAILED, "请选择死亡日期");
        if (request.DeathDate.Date > DateTime.Today)
            return Result.Failure<DeathDeclareResult>(ErrorCodes.VALIDATION_FAILED, "死亡日期不能晚于今天");
        if (string.IsNullOrWhiteSpace(request.DeclaredByName))
            return Result.Failure<DeathDeclareResult>(ErrorCodes.VALIDATION_FAILED, "请填写申报人姓名");
        if (string.IsNullOrWhiteSpace(request.DeclaredByIdCard))
            return Result.Failure<DeathDeclareResult>(ErrorCodes.VALIDATION_FAILED,
                "请填写申报人身份证号码（文书「申报人承诺」段需要）");
        if (!IdCardValidator.IsValid(request.DeclaredByIdCard.Trim()))
            return Result.Failure<DeathDeclareResult>(ErrorCodes.VALIDATION_FAILED, "申报人身份证号校验不通过，请核对后重新填写");
        if (string.IsNullOrWhiteSpace(request.DeclaredByType))
            return Result.Failure<DeathDeclareResult>(ErrorCodes.VALIDATION_FAILED, "请选择申报人类型");
        // 条目五（矛盾纠纷）服务端兜底：VM 校验之外的调用方（含历史路径）也必须给勾选态
        if (string.IsNullOrWhiteSpace(request.DisputeStatus))
            return Result.Failure<DeathDeclareResult>(ErrorCodes.VALIDATION_FAILED, "请选择矛盾纠纷及信访隐患情况（无 / 有）");

        try
        {
            var idCard = request.IdCard.Trim();

            // 同身份证窗口期内的有效申报计数（提交前已由调用方弹确认，此处回传供提示）
            var recentResult = await CountRecentValidAsync(idCard, request.DeathDate, ct);
            if (recentResult.IsFailure)
                return Result.Failure<DeathDeclareResult>(recentResult.ErrorCode!, recentResult.Message!);
            var recentValid = recentResult.Value;

            var insertSql = @"INSERT INTO nc_biz_death_declarations
                    (deceased_name, deceased_id_card, deceased_relation,
                     death_date, death_time, death_reason, death_certificate_no, death_place,
                     declared_by_name, declared_by_id_card, declared_by_phone, declared_by_type,
                     declared_at, declare_channel, status,
                     application_id, village_id, town_id, village_name,
                     operator_name, remark, created_by, created_at,
                     reporter_name, reporter_relation, reporter_phone,
                     situation_desc, remains_arrangement, business_agent, business_matter,
                     cert_type, cert_issue_org, cremation_date, cremation_place, cremation_cert_no,
                     burial_date, burial_place, family_situation, dispute_status, dispute_desc)
                  VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12, NOW(), $13, $14, $15, $16, $17, $18, $19, $20, $19, NOW(),
                          $21,$22,$23,$24,$25,$26,$27,
                          $28,$29,$30,$31,$32,$33,$34,$35,$36,$37)
                  RETURNING id, declared_at";

            var row = await _db.QuerySingleAsync<DeclareInsertRow>(insertSql, ct,
                request.Name.Trim(),
                idCard,
                NullIfEmpty(request.Relation),
                request.DeathDate.Date,
                TimeSpan.FromHours(request.DeathTime.Hours).Add(TimeSpan.FromMinutes(request.DeathTime.Minutes)),
                NullIfEmpty(request.DeathReason),
                NullIfEmpty(request.DeathCertNo),
                NullIfEmpty(request.DeathPlace),
                request.DeclaredByName.Trim(),
                NullIfEmpty(request.DeclaredByIdCard),
                NullIfEmpty(request.DeclaredByPhone),
                request.DeclaredByType,
                string.IsNullOrWhiteSpace(request.Channel) ? DeathDeclarationConstants.ChannelPc : request.Channel,
                DeathDeclarationConstants.StatusPending,
                request.ApplicationId,
                request.VillageId,
                request.TownId,
                NullIfEmpty(request.VillageName),
                request.OperatorName,
                NullIfEmpty(request.Remark),
                NullIfEmpty(request.ReporterName),
                NullIfEmpty(request.ReporterRelation),
                NullIfEmpty(request.ReporterPhone),
                NullIfEmpty(request.SituationDesc),
                NullIfEmpty(request.RemainsArrangement),
                NullIfEmpty(request.BusinessAgent),
                NullIfEmpty(request.BusinessMatter),
                NullIfEmpty(request.CertType),
                NullIfEmpty(request.CertIssueOrg),
                request.CremationDate?.Date,
                NullIfEmpty(request.CremationPlace),
                NullIfEmpty(request.CremationCertNo),
                request.BurialDate?.Date,
                NullIfEmpty(request.BurialPlace),
                NullIfEmpty(request.FamilySituation),
                NullIfEmpty(request.DisputeStatus),
                NullIfEmpty(request.DisputeDesc));

            if (row.IsFailure || row.Value == null)
                return Result.Failure<DeathDeclareResult>(row.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    row.Message ?? "申报写入失败");

            // 死亡联动待办（方案2）：申报即派活。失败不阻断申报——任何列表/计数读取路径
            // 会先按范围幂等补齐（EnsureLinkageTasksForScopeAsync 兜底），失败仅告警留痕。
            var linkageResult = await EnsureLinkageTasksForDeclarationAsync(row.Value.Id, ct);
            if (linkageResult.IsFailure)
                LogWarn($"死亡联动待办生成失败（由列表读取兜底补齐）: {linkageResult.Message}");

            Logger.LogBusiness("死亡申报提交",
                ("Deceased", DataMasker.MaskName(request.Name)),
                ("IdCard", DataMasker.MaskIdCard(idCard)),
                ("DeathDate", request.DeathDate.ToString("yyyy-MM-dd")),
                ("DeclaredBy", DataMasker.MaskName(request.DeclaredByName)),
                ("Type", request.DeclaredByType),
                ("Channel", request.Channel ?? "PC"));

            return Result.Success(new DeathDeclareResult
            {
                Id = row.Value.Id,
                DeclaredAt = row.Value.DeclaredAt,
                RecentValidCount = recentValid
            });
        }
        catch (Exception ex)
        {
            LogException(ex, "DeclareAsync");
            return Result.FromException<DeathDeclareResult>(ex);
        }
    }

    /// <inheritdoc />
    public async Task<Result<DeathDeclareResult>> DeclareAsync(
        VillageScope scope, DeathDeclareRequest request, CancellationToken ct = default)
    {
        if (scope == null)
            return Result.Failure<DeathDeclareResult>(ErrorCodes.VALIDATION_FAILED, "无法确定查询范围");
        if (request == null)
            return Result.Failure<DeathDeclareResult>(ErrorCodes.VALIDATION_FAILED, "申报内容不能为空");

        // 原 VM 的 9 段顺序校验 + 规范化条目二/五硬门（Web 无 VM，必须在服务端执行）。
        var deathDateTime = request.DeathDate == default
            ? (DateTime?)null
            : request.DeathDate.Date + request.DeathTime;
        var invalid = DeathDeclareValidator.Validate(new DeathDeclareValidator.Input
        {
            Name = request.Name,
            IdCard = request.IdCard,
            DeathDateTime = deathDateTime,
            DeclaredByName = request.DeclaredByName,
            DeclaredByIdCard = request.DeclaredByIdCard,
            DeclaredByType = request.DeclaredByType,
            ReporterName = request.ReporterName,
            IsTownScope = scope.IsTownScope,
            DeclaredVillage = request.VillageName,
            // 字典选项数未知：不在此拦截业务事项（与旧版"字典未加载不拦"一致）。
            BusinessMatterOptionCount = 0,
            BusinessMatterCheckedCount = string.IsNullOrWhiteSpace(request.BusinessMatter) ? 0 : 1,
            DispositionBranch = ResolveDispositionBranch(request),
            CremationDate = request.CremationDate,
            CremationPlace = request.CremationPlace,
            BurialDate = request.BurialDate,
            BurialPlace = request.BurialPlace,
            RemainsArrangement = request.RemainsArrangement,
            DisputeStatus = request.DisputeStatus,
            DisputeDesc = request.DisputeDesc,
            DisputeNone = DeathDeclarationConstants.DisputeNone,
            DisputeHas = DeathDeclarationConstants.DisputeHas
        });
        if (invalid != null)
            return Result.Failure<DeathDeclareResult>(ErrorCodes.VALIDATION_FAILED, invalid);

        // 服务端强制范围归属（不信任前端传来的 villageId/townId/villageName）：
        // 村级 = 组织村；镇级降级 = 申报表单手填的「所在村」。
        request.VillageId = scope.IsTownScope ? null : scope.VillageId;
        request.TownId = scope.TownId;
        request.VillageName = scope.IsTownScope
            ? (request.VillageName?.Trim() ?? string.Empty)
            : scope.VillageName;
        request.OperatorName = string.IsNullOrWhiteSpace(CurrentUserContext.UserName)
            ? "System"
            : CurrentUserContext.UserName;
        if (string.IsNullOrWhiteSpace(request.Channel))
            request.Channel = DeathDeclarationConstants.ChannelPc;

        return await DeclareAsync(request, ct);
    }

    /// <summary>
    /// 遗体处置分支：优先使用请求显式分支；未传时按结构化字段推断
    ///（火化/安葬/其他），与旧版表单 SyncDispositionSelection 同口径。
    /// </summary>
    private static string ResolveDispositionBranch(DeathDeclareRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.DispositionBranch))
            return request.DispositionBranch.Trim();

        if (request.CremationDate.HasValue
            || !string.IsNullOrWhiteSpace(request.CremationPlace)
            || !string.IsNullOrWhiteSpace(request.CremationCertNo))
            return DeathDeclarationConstants.DispositionCremation;

        if (request.BurialDate.HasValue || !string.IsNullOrWhiteSpace(request.BurialPlace))
            return DeathDeclarationConstants.DispositionBurial;

        if (!string.IsNullOrWhiteSpace(request.RemainsArrangement))
            return DeathDeclarationConstants.DispositionOther;

        return string.Empty;
    }

    private static object? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // VerifyAsync（Pending → Verified）已删（2026-11-08 P1-5）：状态机收敛 Pending → Archived/Revoked。

    /// <inheritdoc />
    public async Task<Result> RevokeAsync(long declarationId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "请填写作废原因");

        try
        {
            var sql = @"UPDATE nc_biz_death_declarations
                        SET status = $1,
                            remark = COALESCE(remark, '') || CASE WHEN COALESCE(remark,'') = '' THEN '' ELSE E'\n' END
                                     || '[作废] ' || $3,
                            updated_at = NOW()
                        WHERE id = $2 AND deleted_at IS NULL";
            var result = await _db.ExecuteNonQueryAsync(sql, ct,
                DeathDeclarationConstants.StatusRevoked, declarationId, reason.Trim());
            if (result.IsFailure)
                return Result.Failure(result.ErrorCode!, result.Message!);
            if (result.Value <= 0)
                return Result.Failure(ErrorCodes.RECORD_NOT_FOUND, "申报记录不存在");

            Logger.LogBusiness("死亡申报作废",
                ("DeclarationId", declarationId.ToString()), ("Reason", reason.Trim()));
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, "RevokeAsync");
            return Result.FromException(ex);
        }
    }

    /// <inheritdoc />
    public async Task<Result> ArchiveAsync(long declarationId, CancellationToken ct = default)
    {
        try
        {
            // Pending → Archived；已作废（Revoked）不可归档（0 行命中），已归档幂等成功（status <> Archived 挡住）
            var sql = @"UPDATE nc_biz_death_declarations
                        SET status = $1, updated_at = NOW()
                        WHERE id = $2 AND deleted_at IS NULL
                          AND status <> $1 AND status <> $3";
            var result = await _db.ExecuteNonQueryAsync(sql, ct,
                DeathDeclarationConstants.StatusArchived, declarationId,
                DeathDeclarationConstants.StatusRevoked);
            if (result.IsFailure)
                return Result.Failure(result.ErrorCode!, result.Message!);
            if (result.Value <= 0)
                return Result.Failure(ErrorCodes.RECORD_NOT_FOUND,
                    "申报记录不存在、已作废或已归档（作废的申报不能归档，请重新申报）");

            Logger.LogBusiness("死亡申报完结归档", ("DeclarationId", declarationId.ToString()));
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, "ArchiveAsync");
            return Result.FromException(ex);
        }
    }

    #endregion

    #region 死亡联动待办（nc_biz_death_linkage_tasks，2026-10-31 方案2）

    /// <summary>
    /// live 已办判定（**唯一 SQL 单点**）：按任务域计算目标域实时状态——
    /// ElderlyStop=无在享/草稿高龄、ArchiveHead=有户主死亡登记、ArchiveMember=有死亡登记、
    /// College=无在读、Recovery=无在办 Draft；ELSE FALSE。
    /// 使用方：<see cref="GetLinkageTasksAsync"/>（AS is_done）与 <see cref="GetLinkageTaskCountAsync"/>（NOT (…) 反选）。
    /// ⚠️ 镜像：联动卡 <c>DeathStatementService.Linkage.BuildLinksAsync</c> 按内存身份走**同语义**判定，
    /// 改动判定条件必须两处同步。
    /// </summary>
    private const string LinkageLiveDoneCaseSql = @"CASE t.task_domain
                          WHEN 'ElderlyStop' THEN NOT EXISTS (
                               SELECT 1 FROM nc_biz_elderly_applications e
                               WHERE e.id_card = t.deceased_id_card AND e.deleted_at IS NULL
                                 AND e.status IN ('Confirmed','Draft'))
                          WHEN 'ArchiveHead' THEN EXISTS (
                               SELECT 1 FROM nc_biz_death_records r
                               WHERE r.member_id_card = t.deceased_id_card AND r.is_household_head)
                          WHEN 'ArchiveMember' THEN EXISTS (
                               SELECT 1 FROM nc_biz_death_records r
                               WHERE r.member_id_card = t.deceased_id_card)
                          WHEN 'College' THEN NOT EXISTS (
                               SELECT 1 FROM nc_biz_college_students c
                               WHERE c.id_card = t.deceased_id_card AND c.deleted_at IS NULL
                                 AND c.status = 'Studying')
                          WHEN 'Recovery' THEN NOT EXISTS (
                               SELECT 1 FROM nc_biz_recovery_records rc
                               WHERE rc.id_card = t.deceased_id_card AND rc.status = 'Draft')
                          ELSE FALSE
                       END";

    /// <summary>
    /// 五域幂等补齐核心：对 WHERE 命中的有效申报，按各域身份命中条件 INSERT 缺失任务行。
    /// 去重 = 身份证 + 域且存在**有效申报**下的任务行（作废/归档申报的旧行不阻断重新申报后的新任务）。
    /// 两把钥匙共用：scopeCond 恒为单参数 $1（范围版 = town_id/village_id；单申报版 = id）。
    /// </summary>
    private async Task<Result<int>> EnsureLinkageTasksCoreAsync(string scopeCond, object scopeParam, CancellationToken ct)
    {
        const string dedup = @" AND NOT EXISTS (
                SELECT 1 FROM nc_biz_death_linkage_tasks t
                JOIN nc_biz_death_declarations td ON td.id = t.declaration_id
                WHERE t.deceased_id_card = d.deceased_id_card AND t.task_domain = {0}
                  AND td.deleted_at IS NULL AND td.status <> 'Revoked' AND td.status <> 'Archived')";

        static string BuildSql(string scopeCond, string domain, string permission, string? refExpr, string hitCond) => @"
            INSERT INTO nc_biz_death_linkage_tasks
                (declaration_id, deceased_id_card, deceased_name, task_domain,
                 ref_application_id, ref_elderly_id, permission_code, status, village_id, town_id, created_at)
            SELECT d.id, d.deceased_id_card, d.deceased_name, '" + domain + @"',
                   " + (refExpr ?? "NULL") + @", " + (domain == DeathLinkageConstants.DomainElderlyStop
                         ? @"(SELECT e2.id FROM nc_biz_elderly_applications e2
                             WHERE e2.id_card = d.deceased_id_card AND e2.deleted_at IS NULL
                               AND e2.status IN ('Confirmed','Draft')
                             ORDER BY CASE e2.status WHEN 'Confirmed' THEN 0 ELSE 1 END, e2.id DESC LIMIT 1)"
                         : "NULL") + @", '" + permission + @"', 'Pending', d.village_id, d.town_id, NOW()
            FROM nc_biz_death_declarations d
            WHERE d.deleted_at IS NULL AND d.status <> 'Revoked' AND d.status <> 'Archived'
              AND " + scopeCond + @"
              AND " + hitCond + string.Format(dedup, "'" + domain + "'");

        var headHit = @"EXISTS (SELECT 1 FROM nc_biz_applications a
                                WHERE a.deleted_at IS NULL AND a.applicant_id_card = d.deceased_id_card)";

        var statements = new[]
        {
            BuildSql(scopeCond, DeathLinkageConstants.DomainElderlyStop,
                PermissionCodes.ELDERLY_EDIT, null,
                @"EXISTS (SELECT 1 FROM nc_biz_elderly_applications e
                          WHERE e.id_card = d.deceased_id_card AND e.deleted_at IS NULL
                            AND e.status IN ('Confirmed','Draft'))"),

            BuildSql(scopeCond, DeathLinkageConstants.DomainArchiveHead,
                PermissionCodes.CHANGE_MANAGE,
                @"(SELECT a2.id FROM nc_biz_applications a2
                   WHERE a2.deleted_at IS NULL AND a2.applicant_id_card = d.deceased_id_card
                   ORDER BY a2.id DESC LIMIT 1)",
                headHit),

            BuildSql(scopeCond, DeathLinkageConstants.DomainArchiveMember,
                PermissionCodes.CHANGE_MANAGE,
                @"(SELECT m2.application_id FROM nc_biz_family_members m2
                   WHERE m2.deleted_at IS NULL AND m2.id_card = d.deceased_id_card
                   ORDER BY m2.id DESC LIMIT 1)",
                @"EXISTS (SELECT 1 FROM nc_biz_family_members m
                          WHERE m.deleted_at IS NULL AND m.id_card = d.deceased_id_card)
                  AND NOT " + headHit),

            BuildSql(scopeCond, DeathLinkageConstants.DomainCollege,
                PermissionCodes.COLLEGE_MANAGE, null,
                @"EXISTS (SELECT 1 FROM nc_biz_college_students c
                          WHERE c.id_card = d.deceased_id_card AND c.deleted_at IS NULL
                            AND c.status = 'Studying')"),

            BuildSql(scopeCond, DeathLinkageConstants.DomainRecovery,
                PermissionCodes.RECOVERY_MANAGE, null,
                @"EXISTS (SELECT 1 FROM nc_biz_recovery_records rc
                          WHERE rc.id_card = d.deceased_id_card AND rc.status = 'Draft')"),
        };

        var total = 0;
        foreach (var sql in statements)
        {
            var result = await _db.ExecuteNonQueryAsync(sql, ct, scopeParam);
            if (result.IsFailure)
                return Result.Failure<int>(result.ErrorCode!, $"死亡联动待办生成失败：{result.Message}");
            total += result.Value;
        }

        if (total > 0)
            Logger.LogBusiness("死亡联动待办生成", ("Scope", scopeParam.ToString() ?? string.Empty), ("Created", total));
        return Result.Success(total);
    }

    /// <inheritdoc />
    public async Task<Result<int>> EnsureLinkageTasksForDeclarationAsync(long declarationId, CancellationToken ct = default)
    {
        try
        {
            return await EnsureLinkageTasksCoreAsync("d.id = $1", declarationId, ct);
        }
        catch (Exception ex)
        {
            LogException(ex, "EnsureLinkageTasksForDeclarationAsync");
            return Result.FromException<int>(ex);
        }
    }

    /// <summary>范围版补齐（读取路径兜底：覆盖申报后身份才变化的漏任务 + 存量申报）。</summary>
    private async Task<Result<int>> EnsureLinkageTasksForScopeAsync(VillageScope scope, CancellationToken ct)
        => await EnsureLinkageTasksCoreAsync(ScopeDeclarationSql(scope), ScopeId(scope), ct);

    /// <inheritdoc />
    public async Task<Result<List<DeathLinkageTaskItem>>> GetLinkageTasksAsync(
        VillageScope scope, string[]? onlyDomains = null, CancellationToken ct = default)
    {
        try
        {
            var ensure = await EnsureLinkageTasksForScopeAsync(scope, ct);
            if (ensure.IsFailure)
                return Result.Failure<List<DeathLinkageTaskItem>>(ensure.ErrorCode!, ensure.Message!);

            // live 判定 = 目标域实时状态（事实源）；t.status=Completed 仅是自愈标记。
            // 范围条件必须带 d. 前缀：t 表也有 village_id/town_id 列，裸写会歧义。
            var sql = @"
                SELECT t.id, t.declaration_id, t.deceased_id_card, d.deceased_name, t.task_domain,
                       t.permission_code, t.ref_application_id, t.ref_elderly_id, t.created_at,
                       COALESCE(TO_CHAR(d.death_date,'YYYY-MM-DD'),'') AS death_date_text,
                       COALESCE(TO_CHAR(d.death_time,'HH24:MI'),'') AS death_time_text,
                       COALESCE(d.death_reason,'') AS death_reason,
                       COALESCE(d.death_certificate_no,'') AS death_cert_no,
                       " + LinkageLiveDoneCaseSql + @" AS is_done
                FROM nc_biz_death_linkage_tasks t
                JOIN nc_biz_death_declarations d ON d.id = t.declaration_id
                WHERE d.deleted_at IS NULL AND d.status <> 'Revoked' AND d.status <> 'Archived'
                  AND t.status = 'Pending'
                  AND " + (scope.IsTownScope ? "d.town_id = $1" : "d.village_id = $1");

            object[] args;
            if (onlyDomains is { Length: > 0 })
            {
                sql += @" AND t.task_domain = ANY($2)";
                args = new object[] { ScopeId(scope), onlyDomains };
            }
            else
            {
                args = new object[] { ScopeId(scope) };
            }
            sql += @" ORDER BY t.created_at DESC LIMIT 200";

            var result = await _db.QueryAsync<LinkageTaskRow>(sql, ct, args);
            if (result.IsFailure)
                return Result.Failure<List<DeathLinkageTaskItem>>(result.ErrorCode!, result.Message!);

            var items = (result.Value ?? []).Select(r => new DeathLinkageTaskItem
            {
                Id = r.Id,
                DeclarationId = r.DeclarationId,
                DeceasedIdCard = r.DeceasedIdCard ?? string.Empty,
                DeceasedName = r.DeceasedName ?? string.Empty,
                TaskDomain = r.TaskDomain ?? string.Empty,
                PermissionCode = r.PermissionCode ?? string.Empty,
                RefApplicationId = r.RefApplicationId,
                RefElderlyId = r.RefElderlyId,
                CreatedAt = r.CreatedAt,
                DeathDateText = r.DeathDateText ?? string.Empty,
                DeathTimeText = r.DeathTimeText ?? string.Empty,
                DeathReason = r.DeathReason ?? string.Empty,
                DeathCertNo = r.DeathCertNo ?? string.Empty,
                IsDone = r.IsDone
            }).ToList();

            // 自愈：实时已办的行置 Completed（标记非事实源），并从本次返回退出
            var doneIds = items.Where(i => i.IsDone).Select(i => i.Id).ToList();
            if (doneIds.Count > 0)
            {
                var heal = await _db.ExecuteNonQueryAsync(
                    @"UPDATE nc_biz_death_linkage_tasks SET status = 'Completed', completed_at = NOW()
                      WHERE id = ANY($1) AND status = 'Pending'", ct, doneIds);
                if (heal.IsFailure)
                    LogWarn($"死亡联动待办自愈标记失败: {heal.Message}");
                items = items.Where(i => !i.IsDone).ToList();
            }

            // 同人同域去重（重复申报窗口期内两条有效申报可能各生成一行；created_at DESC 取最新）
            items = items
                .GroupBy(i => (i.DeceasedIdCard, i.TaskDomain))
                .Select(g => g.First())
                .ToList();

            return Result.Success(items);
        }
        catch (Exception ex)
        {
            LogException(ex, "GetLinkageTasksAsync");
            return Result.FromException<List<DeathLinkageTaskItem>>(ex);
        }
    }

    /// <inheritdoc />
    public async Task<Result<int>> GetLinkageTaskCountAsync(VillageScope scope, CancellationToken ct = default)
    {
        try
        {
            var ensure = await EnsureLinkageTasksForScopeAsync(scope, ct);
            if (ensure.IsFailure)
            {
                LogWarn($"死亡联动待办计数跳过补齐: {ensure.Message}");
                return Result.Success(0);
            }

            var sql = @"
                SELECT COUNT(DISTINCT (t.deceased_id_card, t.task_domain))
                FROM nc_biz_death_linkage_tasks t
                JOIN nc_biz_death_declarations d ON d.id = t.declaration_id
                WHERE d.deleted_at IS NULL AND d.status <> 'Revoked' AND d.status <> 'Archived'
                  AND t.status = 'Pending'
                  AND " + (scope.IsTownScope ? "d.town_id = $1" : "d.village_id = $1") + @"
                  AND NOT (" + LinkageLiveDoneCaseSql + @")";

            var result = await _db.ExecuteScalarAsync<long>(sql, ct, ScopeId(scope));
            if (result.IsFailure)
            {
                LogWarn($"死亡联动待办计数失败: {result.Message}");
                return Result.Success(0);
            }
            return Result.Success((int)result.Value);
        }
        catch (Exception ex)
        {
            LogException(ex, "GetLinkageTaskCountAsync");
            return Result.FromException<int>(ex);
        }
    }

    #endregion
}
