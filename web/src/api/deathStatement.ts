import type {
  DeathStatementContext,
  DeathStatementDeclarePayload,
  DeathStatementDeclareResult,
  DeathStatementDeclarationSnapshot,
  DeathStatementIdentity,
  DeathStatementJobInput,
  DeathStatementJobResult,
  DeathStatementLinkage,
  DeathStatementLinkageTask,
  DeathStatementPerson,
  DeathStatementReminder,
  DeathStatementScope,
  DeathStatementSearchItem,
} from '@/types/deathStatement'
import { apiHttp } from './http'

/* ───────────────────────── 归一化小工具 ───────────────────────── */

function asRecord(value: unknown): Record<string, unknown> | null {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null
}

function toText(value: unknown): string {
  if (value === null || value === undefined) return ''
  return typeof value === 'string' ? value : String(value)
}

function toNumber(value: unknown, fallback = 0): number {
  if (value === null || value === undefined || value === '') return fallback
  const parsed = typeof value === 'number' ? value : Number(value)
  return Number.isFinite(parsed) ? parsed : fallback
}

function toNullableNumber(value: unknown): number | null {
  if (value === null || value === undefined || value === '') return null
  const parsed = typeof value === 'number' ? value : Number(value)
  return Number.isFinite(parsed) ? parsed : null
}

function toBool(value: unknown, fallback = false): boolean {
  if (typeof value === 'boolean') return value
  if (typeof value === 'number') return value !== 0
  if (typeof value === 'string') {
    const text = value.trim().toLowerCase()
    if (['true', '1', 'yes', 'y'].includes(text)) return true
    if (['false', '0', 'no', 'n'].includes(text)) return false
  }
  return fallback
}

/** 取第一个非空值（兼容后端字段改名 / 旧桌面端字段名）。 */
function pick(...values: unknown[]): unknown {
  for (const value of values) {
    if (value === null || value === undefined) continue
    if (typeof value === 'string' && !value.trim()) continue
    return value
  }
  return undefined
}

function unwrapArray(value: unknown): unknown[] {
  if (Array.isArray(value)) return value
  const raw = asRecord(value)
  if (!raw) return []
  if (Array.isArray(raw.items)) return raw.items
  if (Array.isArray(raw.Items)) return raw.Items
  const nested = asRecord(raw.data)
  if (nested) {
    if (Array.isArray(nested.items)) return nested.items
    if (Array.isArray(nested.Items)) return nested.Items
  }
  return []
}

function unwrapItems(data: unknown): unknown[] {
  return unwrapArray(data)
}

/** 业务事项 / 多选串 → 字符串数组（兼容「、」「，」「,」分隔）。 */
function toStringArray(value: unknown): string[] {
  if (Array.isArray(value)) {
    return value.map((item) => toText(item).trim()).filter((item) => item.length > 0)
  }
  const text = toText(value).trim()
  if (!text) return []
  return text
    .split(/[、，,]/)
    .map((item) => item.trim())
    .filter((item) => item.length > 0)
}

function genderFromIdCard(idCard: string): string {
  if (!/^\d{17}[\dXx]$/.test(idCard)) return ''
  return Number(idCard[16]) % 2 === 1 ? '男' : '女'
}

/* ───────────────────────── 查询范围 ───────────────────────── */

/** 当前账号的死亡申报查询范围（村 / 镇级降级）。 */
export async function getDeathStatementScope(): Promise<DeathStatementScope> {
  const data = await apiHttp
    .get<unknown>('/death-statements/scope')
    .then((response) => response.data)
  const raw = asRecord(data) ?? {}

  return {
    village: toText(pick(raw.village, raw.villageName, raw.villageLabel)),
    town: toText(pick(raw.town, raw.townName)),
    degraded: toBool(pick(raw.degraded, raw.isTownScope, raw.isDegraded), false),
    degradedReason: toText(
      pick(raw.degradedReason, raw.scopeNotice, raw.scopeError, raw.reason),
    ),
  }
}

/* ───────────────────────── 搜索 ───────────────────────── */

export interface SearchDeathStatementsParams {
  keyword?: string
  /** 起始日期（yyyy-MM-dd）。 */
  from?: string
  /** 截止日期（yyyy-MM-dd）。 */
  to?: string
  /** 含作废申报。 */
  showAll?: boolean
}

function normalizeSearchItem(value: unknown): DeathStatementSearchItem | null {
  const raw = asRecord(value)
  if (!raw) return null

  const declarationIdRaw = toNullableNumber(pick(raw.declarationId, raw.declarationID))
  const declarationId = declarationIdRaw !== null && declarationIdRaw > 0 ? declarationIdRaw : null
  const id = toNullableNumber(pick(raw.id, raw.deathRecordId, declarationId))
  if (id === null) return null

  const status = toText(pick(raw.status, raw.statusCode))
  const statusDisplay = toText(
    pick(raw.statusDisplay, raw.statusName, raw.badge, status),
  )
  const isRevoked = toBool(pick(raw.isRevoked, raw.revoked), status === 'Revoked')
  const isArchived = toBool(pick(raw.isArchived, raw.archived), status === 'Archived')
  const isDeclared = toBool(
    pick(raw.isDeclared, raw.hasDeclaration, raw.declared),
    declarationId !== null && !isRevoked,
  )
  const source = toText(pick(raw.source, raw.sourceLabel))
  const subtitle =
    toText(pick(raw.subtitle, raw.description)) ||
    [source, statusDisplay].filter((item) => item.length > 0).join(' · ')

  return {
    id,
    declarationId,
    idCard: toText(pick(raw.idCard, raw.deceasedIdCard)),
    personName: toText(pick(raw.personName, raw.name, raw.deceasedName)),
    deathDate: toText(pick(raw.deathDate, raw.deathDateText)),
    status,
    statusDisplay: statusDisplay || (isDeclared ? '已申报' : '未申报'),
    source,
    subtitle,
    isDeclared,
    isArchived,
    isRevoked,
  }
}

/** 本范围搜索申报台账 / 档案死亡登记 / 未申报候选。 */
export async function searchDeathStatements(
  params: SearchDeathStatementsParams = {},
): Promise<DeathStatementSearchItem[]> {
  const data = await apiHttp
    .get<unknown>('/death-statements/search', {
      params: {
        keyword: params.keyword?.trim() || undefined,
        from: params.from || undefined,
        to: params.to || undefined,
        showAll: params.showAll ?? false,
      },
    })
    .then((response) => response.data)

  return unwrapItems(data)
    .map(normalizeSearchItem)
    .filter((item): item is DeathStatementSearchItem => item !== null)
}

/* ───────────────────────── 上下文聚合 ───────────────────────── */

function normalizePerson(value: unknown, root: Record<string, unknown>): DeathStatementPerson {
  const raw = asRecord(value) ?? {}
  const idCard = toText(pick(raw.idCard, raw.idCardNo, root.idCard))
  const gender =
    toText(pick(raw.gender, raw.sex, root.gender)) || genderFromIdCard(idCard)

  return {
    name: toText(pick(raw.name, raw.personName, root.personName, root.name)),
    idCard,
    gender,
    birthDate: toText(pick(raw.birthDate, root.birthDate)),
    age: toNullableNumber(pick(raw.age, root.age)),
    category: toText(pick(raw.category, raw.classification, root.category, root.classification)),
    address: toText(pick(raw.address, raw.homeAddress, root.address)),
    relation: toText(pick(raw.relation, raw.relationshipToHead, root.relation)),
  }
}

function normalizeIdentity(value: unknown): DeathStatementIdentity | null {
  const raw = asRecord(value)
  if (!raw) return null

  const label = toText(pick(raw.label, raw.typeLabel, raw.type, raw.sourceLabel))
  const status = toText(pick(raw.status, raw.statusLabel, raw.state))
  const summary =
    toText(pick(raw.summary, raw.description)) ||
    [
      toText(pick(raw.period, raw.periodText)),
      toText(pick(raw.note, raw.remark, raw.memo)),
      toText(pick(raw.sourceLabel, raw.source, raw.sourceKey)),
    ]
      .filter((item) => item.length > 0)
      .join(' · ')

  if (!label && !status && !summary) return null
  return {
    source: toText(pick(raw.source, raw.sourceKey, raw.sourceLabel)),
    label: label || '在库身份',
    status,
    summary,
  }
}

function normalizeReminder(value: unknown): DeathStatementReminder | null {
  const raw = asRecord(value)
  if (!raw) return null

  const text = toText(pick(raw.text, raw.message, raw.content, raw.title))
  if (!text) return null

  return {
    level: toText(pick(raw.level, raw.levelName, raw.severity)) || '提示',
    text,
    suggestion: toText(pick(raw.suggestion, raw.suggestAction, raw.action, raw.tip)),
  }
}

function normalizeLinkage(value: unknown): DeathStatementLinkage | null {
  const raw = asRecord(value)
  if (!raw) return null

  const label = toText(pick(raw.label, raw.title, raw.domainName, raw.name))
  if (!label) return null

  const status = toText(pick(raw.status, raw.state))
  return {
    domain: toText(pick(raw.domain, raw.taskDomain)),
    label,
    status,
    statusDisplay: toText(
      pick(raw.statusDisplay, raw.stateText, raw.statusText, raw.actionText, status),
    ),
    navKey: toText(pick(raw.navKey, raw.navigationKey)) || 'None',
    applicationId: toNullableNumber(pick(raw.applicationId, raw.refApplicationId)),
    elderlyApplicationId: toNullableNumber(
      pick(raw.elderlyApplicationId, raw.refElderlyId),
    ),
    canHandle: toBool(pick(raw.canHandle, raw.enabled), true),
    done: toBool(pick(raw.done, raw.isDone, raw.completed), false),
  }
}

function normalizeDeclaration(
  root: Record<string, unknown>,
): DeathStatementDeclarationSnapshot | null {
  const nested = asRecord(root.declaration) ?? {}
  const hasAny =
    Object.keys(nested).length > 0 ||
    [
      'declarationId',
      'declaredByName',
      'declaredByIdCard',
      'declaredByType',
      'declaredAt',
      'declaredAtText',
      'hasDeclaration',
    ].some((key) => key in root)
  if (!hasAny) return null

  const value = (key: string): unknown => pick(nested[key], root[key])

  const declarationId = toNullableNumber(
    pick(nested.declarationId, root.declarationId),
  )

  return {
    declarationId: declarationId !== null && declarationId > 0 ? declarationId : null,
    declaredByName: toText(value('declaredByName')),
    declaredByIdCard: toText(value('declaredByIdCard')),
    declaredByPhone: toText(value('declaredByPhone')),
    declaredByType: toText(value('declaredByType')),
    declaredRelation: toText(value('declaredRelation')),
    declaredAtText: toText(pick(value('declaredAtText'), value('declaredAt'))),
    status: toText(value('status')),
    statusDisplay: toText(pick(value('statusDisplay'), value('statusName'))),
    hasDeclaration: toBool(pick(value('hasDeclaration'), nested.hasDeclaration), false),
    deathDateText: toText(pick(value('deathDateText'), value('deathDate'))),
    deathTimeText: toText(pick(value('deathTimeText'), value('deathTime'))),
    deathReason: toText(value('deathReason')),
    deathPlace: toText(value('deathPlace')),
    deathCertNo: toText(
      pick(value('deathCertNo'), value('deathCertificateNo')),
    ),
    certType: toText(value('certType')),
    certIssueOrg: toText(value('certIssueOrg')),
    villageName: toText(value('villageName')),
    situationDesc: toText(pick(value('situationDesc'), value('sceneDescription'))),
    remainsArrangement: toText(
      pick(value('remainsArrangement'), value('disposalNote')),
    ),
    businessAgent: toText(value('businessAgent')),
    businessMatters: toStringArray(
      pick(value('businessMatters'), value('businessMatter')),
    ),
    cremationDate: toText(pick(value('cremationDateText'), value('cremationDate'))),
    cremationPlace: toText(value('cremationPlace')),
    cremationCertNo: toText(value('cremationCertNo')),
    burialDate: toText(pick(value('burialDateText'), value('burialDate'))),
    burialPlace: toText(value('burialPlace')),
    familySituation: toText(value('familySituation')),
    disputeStatus: toText(pick(value('disputeStatus'), value('disputeType'))),
    disputeDesc: toText(pick(value('disputeDesc'), value('disputeNote'))),
  }
}

export interface DeathStatementContextParams {
  declarationId?: number | string | null
  idCard?: string | null
}

/** 身份 / 提醒 / 联动聚合（带出、手工录入汇总、进入第 2 步共用）。 */
export async function getDeathStatementContext(
  params: DeathStatementContextParams = {},
): Promise<DeathStatementContext> {
  const data = await apiHttp
    .get<unknown>('/death-statements/context', {
      params: {
        declarationId:
          params.declarationId === null || params.declarationId === undefined
            ? undefined
            : params.declarationId,
        idCard: params.idCard?.trim() || undefined,
      },
    })
    .then((response) => response.data)
  const raw = asRecord(data) ?? {}

  return {
    person: normalizePerson(raw.person, raw),
    identities: unwrapArray(raw.identities)
      .map(normalizeIdentity)
      .filter((item): item is DeathStatementIdentity => item !== null),
    reminders: unwrapArray(raw.reminders)
      .map(normalizeReminder)
      .filter((item): item is DeathStatementReminder => item !== null),
    linkages: unwrapArray(pick(raw.linkages, raw.links))
      .map(normalizeLinkage)
      .filter((item): item is DeathStatementLinkage => item !== null),
    declaration: normalizeDeclaration(raw),
    crossVillageCount: toNumber(
      pick(raw.crossVillageCount, raw.excludedCrossVillageCount),
    ),
  }
}

/* ───────────────────────── 申报 ───────────────────────── */

/** 窗口期重复申报检查（死亡日期前后 ±3 天）。 */
export async function countRecentDeathStatements(
  idCard: string,
  deathDate: string,
): Promise<number> {
  const data = await apiHttp
    .post<unknown>('/death-statements/count-recent', { idCard, deathDate })
    .then((response) => response.data)

  if (typeof data === 'number') return data
  const raw = asRecord(data) ?? {}
  return toNumber(pick(raw.count, raw.recentCount, raw.total))
}

/** 提交死亡申报。 */
export async function declareDeathStatement(
  payload: DeathStatementDeclarePayload,
): Promise<DeathStatementDeclareResult> {
  const data = await apiHttp
    .post<unknown>('/death-statements/declare', payload)
    .then((response) => response.data)
  const raw = asRecord(data) ?? {}
  const id = toNumber(pick(raw.id, raw.declarationId))
  const declarationId = toNullableNumber(raw.declarationId) ?? id

  return {
    id,
    declarationId,
    status: toText(pick(raw.status, raw.statusCode)) || 'Pending',
  }
}

/** 作废申报（已申报未归档）。 */
export async function revokeDeathStatement(
  id: number | string,
  reason: string,
): Promise<void> {
  await apiHttp
    .post(`/death-statements/${encodeURIComponent(String(id))}/revoke`, { reason })
    .then(() => undefined)
}

/** 完结归档（已申报未归档）。 */
export async function archiveDeathStatement(id: number | string): Promise<void> {
  await apiHttp
    .post(`/death-statements/${encodeURIComponent(String(id))}/archive`)
    .then(() => undefined)
}

/* ───────────────────────── 联动待办 ───────────────────────── */

function normalizeLinkageTask(value: unknown): DeathStatementLinkageTask | null {
  const raw = asRecord(value)
  if (!raw) return null

  const id = toNullableNumber(raw.id)
  if (id === null) return null

  const domain = toText(pick(raw.domain, raw.taskDomain))
  return {
    id,
    declarationId: toNumber(raw.declarationId),
    personName: toText(pick(raw.personName, raw.deceasedName)),
    idCard: toText(pick(raw.idCard, raw.deceasedIdCard)),
    domain,
    domainLabel: toText(pick(raw.domainLabel, raw.taskDomainName, domain)),
    status: toText(pick(raw.status, raw.state)),
    statusDisplay: toText(pick(raw.statusDisplay, raw.stateText, raw.statusText)),
    navKey: toText(pick(raw.navKey, raw.navigationKey)) || 'None',
    createdAt: toText(pick(raw.createdAt, raw.createdAtDisplay)),
  }
}

/** 当前账号可承接的死亡联动待办（只读列表）。 */
export async function listDeathLinkageTasks(): Promise<DeathStatementLinkageTask[]> {
  const data = await apiHttp
    .get<unknown>('/death-statements/linkage-tasks')
    .then((response) => response.data)

  return unwrapItems(data)
    .map(normalizeLinkageTask)
    .filter((item): item is DeathStatementLinkageTask => item !== null)
}

/* ───────────────────────── 文书任务 ───────────────────────── */

/** 创建文书输出任务（预览 / 打印 / 保存）；实际执行由本机 Agent 领取。 */
export async function createDeathStatementJob(
  input: DeathStatementJobInput,
): Promise<DeathStatementJobResult> {
  const data = await apiHttp
    .post<unknown>('/death-statements/jobs', input)
    .then((response) => response.data)
  const raw = asRecord(data) ?? {}

  return {
    id: toNumber(raw.id),
    jobNo: toText(raw.jobNo),
    status: toText(raw.status) || 'Pending',
    jobKind: toText(raw.jobKind) || 'Template',
  }
}
