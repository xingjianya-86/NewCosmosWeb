<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  ArrowLeft,
  Check,
  Document,
  Download,
  EditPen,
  Printer,
  Refresh,
  Search,
  View,
} from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import {
  archiveDeathStatement,
  countRecentDeathStatements,
  createDeathStatementJob,
  declareDeathStatement,
  getDeathStatementContext,
  getDeathStatementScope,
  revokeDeathStatement,
  searchDeathStatements,
} from '@/api/deathStatement'
import { describePrintJobError, listPrintJobs } from '@/api/archivePrint'
import { listPrinters } from '@/api/printers'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStatusDot from '@/components/NcStatusDot.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import { useAuthStore } from '@/stores/auth'
import type { PrinterBinding } from '@/types/api'
import type {
  DeathStatementContext,
  DeathStatementDeclarePayload,
  DeathStatementJobMode,
  DeathStatementLinkage,
  DeathStatementScope,
  DeathStatementSearchItem,
} from '@/types/deathStatement'
import {
  archiveJobStatusLabel,
  archiveJobStatusTone,
  formatDateTimeText,
  isArchiveJobTerminal,
} from '@/views/archives/archiveDisplay'

/* ───────────────────────── 常量（与旧桌面端口径一致） ───────────────────────── */

const STEP_TITLES = ['申报信息', '核实与汇总', '文书输出'] as const

const DECLARANT_TYPES = ['家属', '村民委员会', '医疗机构', '公安机关', '其他'] as const
const RELATION_OPTIONS = [
  '配偶',
  '儿子',
  '女儿',
  '孙子女',
  '父母',
  '祖父母',
  '兄弟姐妹',
  '邻居',
  '朋友',
  '村干部',
  '其他',
] as const
const CERT_TYPE_OPTIONS = ['死亡医学证明', '非正常死亡证明'] as const
const DEATH_REASON_OPTIONS = ['疾病', '意外事故', '自然灾害', '其他'] as const
const DEATH_PLACE_OPTIONS = ['家中', '医疗机构', '养老机构', '其他'] as const
const SCENE_PRESET_OPTIONS = [
  '经入户核实，死者生前长期患病，于家中自然死亡，家属均在现场。',
  '经核实，死者于医疗机构经抢救无效死亡，死亡证明由医院出具。',
  '经现场核实，死者系意外死亡，公安机关已介入处理。',
] as const
/** 业务事项选项（旧版读字典 BusinessMatters；Web 端无字典接口，用固定集合兜底）。 */
const BUSINESS_MATTER_OPTIONS = [
  '申请居民死亡医学证明',
  '办理户籍注销',
  '停发高龄津贴',
  '终止社会救助待遇',
  '办理丧葬事宜',
] as const
const DISPOSAL_OPTIONS = ['火化', '安葬', '其他'] as const
const DISPUTE_OPTIONS = ['无', '有'] as const

const OTHER = '其他'
const DUPLICATE_WINDOW_DAYS = 3
const TEMPLATE_NAME = '证明_死亡情况说明'
const POLL_INTERVAL = 4_000
const JOB_PAGE_SIZE = 20

/* ───────────────────────── 基础状态 ───────────────────────── */

const router = useRouter()
const auth = useAuthStore()
const isNarrow = useMediaQuery('(max-width: 767px)')

function pad(value: number): string {
  return String(value).padStart(2, '0')
}

function todayText(): string {
  const date = new Date()
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

function monthsAgoText(months: number): string {
  const date = new Date()
  date.setMonth(date.getMonth() - months)
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

function formatLocalDateTime(date: Date): string {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`
}

/** 后端日期 / ISO 字符串 → yyyy-MM-dd HH:mm:ss（表单日期时间值）。 */
function toDateTimeText(value: string | null | undefined): string {
  const text = (value ?? '').trim()
  if (!text) return ''
  if (/^\d{4}-\d{2}-\d{2}$/.test(text)) return `${text} 00:00:00`
  if (text.includes('T') || text.endsWith('Z')) {
    const date = new Date(text)
    if (!Number.isNaN(date.getTime())) return formatLocalDateTime(date)
  }
  return text.slice(0, 19).replace('T', ' ')
}

function toDateText(value: string): string {
  return value.slice(0, 10)
}

function toTimeText(value: string): string {
  return value.length >= 16 ? value.slice(11, 16) : ''
}

function isFutureDateTime(value: string): boolean {
  const date = new Date(value.replace(' ', 'T'))
  return !Number.isNaN(date.getTime()) && date.getTime() > Date.now()
}

function isFutureDate(date: Date): boolean {
  return date.getTime() > Date.now()
}

function isBeyondOneYear(date: Date): boolean {
  return date.getTime() > Date.now() + 365 * 24 * 60 * 60 * 1000
}

const ID_WEIGHTS = [7, 9, 10, 5, 8, 4, 2, 1, 6, 3, 7, 9, 10, 5, 8, 4, 2] as const
const ID_CHECK_CODES = ['1', '0', 'X', '9', '8', '7', '6', '5', '4', '3', '2'] as const

/** 18 位身份证校验（与旧版 IdCardValidator 口径一致）。 */
function isValidIdCard(value: string): boolean {
  const text = value.trim().toUpperCase()
  if (!/^\d{17}[\dX]$/.test(text)) return false
  let sum = 0
  for (let index = 0; index < 17; index += 1) {
    sum += Number(text[index]) * ID_WEIGHTS[index]
  }
  return ID_CHECK_CODES[sum % 11] === text[17]
}

function genderFromIdCard(value: string): string {
  const text = value.trim()
  if (!/^\d{17}[\dXx]$/.test(text)) return ''
  return Number(text[16]) % 2 === 1 ? '男' : '女'
}

/** 18 位身份证 → 年龄（上下文无年龄字段时兜底）。 */
function ageFromIdCard(value: string): number | null {
  const text = value.trim()
  if (!/^\d{17}[\dXx]$/.test(text)) return null
  const year = Number(text.slice(6, 10))
  const month = Number(text.slice(10, 12))
  const day = Number(text.slice(12, 14))
  if (year <= 0 || month < 1 || month > 12 || day < 1 || day > 31) return null
  const today = new Date()
  let age = today.getFullYear() - year
  if (
    today.getMonth() + 1 < month ||
    (today.getMonth() + 1 === month && today.getDate() < day)
  ) {
    age -= 1
  }
  return age >= 0 && age < 150 ? age : null
}

/* ───────────────────────── 表单 ───────────────────────── */

interface DeathFormState {
  personName: string
  idCard: string
  deathDateTime: string
  deathReasonChoice: string
  deathReasonCustom: boolean
  deathReason: string
  deathPlaceChoice: string
  deathPlaceCustom: boolean
  deathPlace: string
  deathCertNo: string
  certType: string
  certIssueOrg: string
  declarantType: string
  declarantName: string
  declarantIdCard: string
  declarantPhone: string
  relation: string
  reporterSame: boolean
  reporterName: string
  reporterRelation: string
  reporterPhone: string
  village: string
  businessAgent: string
  businessMatters: string[]
  scenePreset: string
  sceneDescription: string
  disposalType: string
  cremationDate: string
  cremationPlace: string
  cremationCertNo: string
  burialDate: string
  burialPlace: string
  disposalNote: string
  familySituation: string
  disputeType: string
  disputeNote: string
}

type TextFormKey =
  | 'personName'
  | 'idCard'
  | 'deathReason'
  | 'deathPlace'
  | 'deathCertNo'
  | 'certType'
  | 'certIssueOrg'
  | 'declarantType'
  | 'declarantName'
  | 'declarantIdCard'
  | 'declarantPhone'
  | 'relation'
  | 'reporterName'
  | 'reporterRelation'
  | 'reporterPhone'
  | 'village'
  | 'businessAgent'
  | 'sceneDescription'
  | 'cremationDate'
  | 'cremationPlace'
  | 'cremationCertNo'
  | 'burialDate'
  | 'burialPlace'
  | 'disposalNote'
  | 'familySituation'
  | 'disputeType'
  | 'disputeNote'

function currentOperatorName(): string {
  return auth.user?.fullName || auth.user?.username || 'System'
}

function createEmptyForm(): DeathFormState {
  return {
    personName: '',
    idCard: '',
    deathDateTime: '',
    deathReasonChoice: '',
    deathReasonCustom: false,
    deathReason: '',
    deathPlaceChoice: '',
    deathPlaceCustom: false,
    deathPlace: '',
    deathCertNo: '',
    certType: '',
    certIssueOrg: '',
    declarantType: '村民委员会',
    declarantName: auth.user?.fullName || auth.user?.username || '',
    declarantIdCard: '',
    declarantPhone: '',
    relation: '',
    reporterSame: true,
    reporterName: '',
    reporterRelation: '',
    reporterPhone: '',
    village: '',
    businessAgent: auth.user?.fullName || auth.user?.username || '',
    businessMatters: [BUSINESS_MATTER_OPTIONS[0]],
    scenePreset: '',
    sceneDescription: '',
    disposalType: '',
    cremationDate: '',
    cremationPlace: '',
    cremationCertNo: '',
    burialDate: '',
    burialPlace: '',
    disposalNote: '',
    familySituation: '',
    disputeType: '',
    disputeNote: '',
  }
}

const step = ref(1)
const form = reactive<DeathFormState>(createEmptyForm())

/* ───────────────────────── 范围 ───────────────────────── */

const scope = ref<DeathStatementScope | null>(null)
const scopeLoading = ref(true)
const scopeError = ref('')
const pageError = ref('')

const degraded = computed(() => scope.value?.degraded ?? false)
const scopeLabel = computed(() => {
  const value = scope.value
  if (!value) return '范围解析中…'
  if (value.degraded) return `乡镇：${value.town || '—'}（全镇范围）`
  return `村/社区：${value.village || '—'}`
})

const villageOptions = computed(() => {
  const values = new Set<string>()
  const scopeVillage = scope.value?.village?.trim()
  if (scopeVillage) values.add(scopeVillage)
  const current = form.village.trim()
  if (current) values.add(current)
  return [...values]
})

/* ───────────────────────── 搜索 / 带出 ───────────────────────── */

const searchKeyword = ref('')
const searchRange = ref<string[]>([monthsAgoText(12), todayText()])
const searchShowAll = ref(false)
const searchLoading = ref(false)
const searchError = ref('')
const searchResults = ref<DeathStatementSearchItem[]>([])
const searchPerformed = ref(false)

const manualMode = ref(false)
const selectedItem = ref<DeathStatementSearchItem | null>(null)

const searchSummary = computed(() => {
  if (searchLoading.value) return '正在搜索…'
  if (!searchPerformed.value) return '按关键词或日期范围检索'
  return searchResults.value.length
    ? `共 ${searchResults.value.length} 条（本范围）`
    : '本范围无匹配记录'
})

function itemStatusTone(item: DeathStatementSearchItem): 'online' | 'offline' | 'default' | 'idle' {
  if (item.isRevoked) return 'offline'
  if (item.isArchived) return 'default'
  if (item.isDeclared) return 'online'
  return 'idle'
}

function itemStatusText(item: DeathStatementSearchItem): string {
  if (item.statusDisplay) return item.statusDisplay
  if (item.isRevoked) return '已作废'
  if (item.isArchived) return '已归档'
  return item.isDeclared ? '已申报' : '未申报'
}

async function runSearch(silent = false): Promise<void> {
  if (!scope.value) {
    if (!silent) ElMessage.warning(scopeError.value || '查询范围未就绪')
    return
  }

  searchLoading.value = true
  searchError.value = ''
  try {
    const items = await searchDeathStatements({
      keyword: searchKeyword.value,
      from: searchRange.value?.[0] || '',
      to: searchRange.value?.[1] || '',
      showAll: searchShowAll.value,
    })
    searchResults.value = items
    searchPerformed.value = true
    if (!silent) {
      if (items.length === 0) {
        ElMessage.info('未找到匹配记录，可点「手工录入」直接申报')
      } else {
        ElMessage.success(`共 ${items.length} 条记录`)
      }
    }
  } catch (error) {
    searchError.value = getErrorMessage(error, '搜索失败')
    if (!silent) ElMessage.error(searchError.value)
  } finally {
    searchLoading.value = false
  }
}

function refreshSearchSilently(): Promise<void> {
  return runSearch(true)
}

/* ───────────────────────── 申报状态 / 上下文 ───────────────────────── */

const declarationId = ref<number | null>(null)
const isDeclared = ref(false)
const isArchived = ref(false)
const isRevoked = ref(false)
const statusDisplay = ref('')
const declaredAtText = ref('')

const context = ref<DeathStatementContext | null>(null)
const contextLoading = ref(false)
const contextError = ref('')
const contextLoadingText = ref('')
const lastContextIdCard = ref('')

const submitting = ref(false)
const revoking = ref(false)
const archiving = ref(false)

const busy = computed(() => submitting.value || revoking.value || archiving.value)

const statusTone = computed<'online' | 'offline' | 'default' | 'idle'>(() => {
  if (isRevoked.value) return 'offline'
  if (isArchived.value) return 'default'
  if (isDeclared.value) return 'online'
  return 'idle'
})

const statusText = computed(() => {
  if (statusDisplay.value) return statusDisplay.value
  if (isRevoked.value) return '已作废'
  if (isArchived.value) return '已归档'
  return isDeclared.value ? '已申报' : '未申报'
})

const canRevoke = computed(
  () => isDeclared.value && !isArchived.value && !isRevoked.value && !busy.value,
)
const canArchive = computed(
  () => isDeclared.value && !isArchived.value && !isRevoked.value && !busy.value,
)

const personGender = computed(
  () => context.value?.person.gender || genderFromIdCard(form.idCard),
)
const personAge = computed(
  () => context.value?.person.age ?? ageFromIdCard(form.idCard),
)

const archiveInfo = computed(() => {
  const person = context.value?.person
  if (!person) return ''
  return [
    person.category,
    person.address,
    person.relation ? `与户主关系：${person.relation}` : '',
  ]
    .filter((item) => item.length > 0)
    .join(' · ')
})

const identities = computed(() => context.value?.identities ?? [])
const reminders = computed(() => context.value?.reminders ?? [])
const linkages = computed(() => context.value?.linkages ?? [])

const identitySummary = computed(() => {
  const value = context.value
  if (!value) return '尚未汇总'
  if (value.identities.length > 0) {
    return (
      `系统内命中 ${value.identities.length} 项身份` +
      (value.crossVillageCount > 0
        ? `（另有 ${value.crossVillageCount} 条跨村记录已剔除）`
        : '')
    )
  }
  if (value.crossVillageCount > 0) {
    return `系统内 ${value.crossVillageCount} 条身份记录均挂在其他村社区，已剔除出本说明`
  }
  return '系统内无该人身份记录'
})

const businessMatterOptions = computed(() => {
  const extras = form.businessMatters.filter(
    (matter) => !(BUSINESS_MATTER_OPTIONS as readonly string[]).includes(matter),
  )
  return [...BUSINESS_MATTER_OPTIONS, ...extras]
})

/** 选项集合 + 当前值（历史数据不在固定集合时仍能正常显示，旧版 EnsureOption 同口径）。 */
function withCurrent(base: readonly string[], ...values: string[]): string[] {
  const result = [...base]
  for (const value of values) {
    const text = value.trim()
    if (text && !result.includes(text)) result.push(text)
  }
  return result
}

const declarantTypeOptions = computed(() =>
  withCurrent(DECLARANT_TYPES, form.declarantType),
)
const relationOptions = computed(() =>
  withCurrent(RELATION_OPTIONS, form.relation, form.reporterRelation),
)
const certTypeOptions = computed(() => withCurrent(CERT_TYPE_OPTIONS, form.certType))

function fillText(key: TextFormKey, value: string, overwrite: boolean): void {
  if (!value) return
  if (overwrite || !form[key].trim()) form[key] = value
}

function syncDeathReasonChoice(value: string): void {
  const text = value.trim()
  if (!text) {
    form.deathReasonChoice = ''
    form.deathReasonCustom = false
    return
  }
  if ((DEATH_REASON_OPTIONS as readonly string[]).includes(text) && text !== OTHER) {
    form.deathReasonChoice = text
    form.deathReasonCustom = false
    return
  }
  form.deathReasonChoice = OTHER
  form.deathReasonCustom = true
}

function syncDeathPlaceChoice(value: string): void {
  const text = value.trim()
  if (!text) {
    form.deathPlaceChoice = ''
    form.deathPlaceCustom = false
    return
  }
  if ((DEATH_PLACE_OPTIONS as readonly string[]).includes(text) && text !== OTHER) {
    form.deathPlaceChoice = text
    form.deathPlaceCustom = false
    return
  }
  form.deathPlaceChoice = OTHER
  form.deathPlaceCustom = true
}

function applyContext(result: DeathStatementContext, overwrite: boolean): void {
  const person = result.person
  if (person.name) fillText('personName', person.name, overwrite)
  if (person.idCard) fillText('idCard', person.idCard, overwrite)

  const declaration = result.declaration
  if (declaration) {
    if (declaration.declarationId !== null && declarationId.value === null) {
      declarationId.value = declaration.declarationId
    }

    fillText('declarantName', declaration.declaredByName, overwrite)
    fillText('declarantIdCard', declaration.declaredByIdCard, overwrite)
    fillText('declarantPhone', declaration.declaredByPhone, overwrite)
    fillText('declarantType', declaration.declaredByType, overwrite)
    fillText('relation', declaration.declaredRelation, overwrite)

    if (declaration.deathDateText && (overwrite || !form.deathDateTime)) {
      form.deathDateTime = toDateTimeText(
        declaration.deathTimeText
          ? `${declaration.deathDateText} ${declaration.deathTimeText}`
          : declaration.deathDateText,
      )
    }
    if (declaration.deathReason) {
      fillText('deathReason', declaration.deathReason, overwrite)
      if (overwrite || !form.deathReasonChoice) syncDeathReasonChoice(declaration.deathReason)
    }
    fillText('deathPlace', declaration.deathPlace, overwrite)
    if (declaration.deathPlace) {
      if (overwrite || !form.deathPlaceChoice) syncDeathPlaceChoice(declaration.deathPlace)
    }
    fillText('deathCertNo', declaration.deathCertNo, overwrite)
    fillText('certType', declaration.certType, overwrite)
    fillText('certIssueOrg', declaration.certIssueOrg, overwrite)
    fillText('village', declaration.villageName, overwrite)
    fillText('sceneDescription', declaration.situationDesc, overwrite)
    fillText('businessAgent', declaration.businessAgent, overwrite)

    if (
      declaration.businessMatters.length > 0 &&
      (overwrite || form.businessMatters.length === 0)
    ) {
      form.businessMatters = [...declaration.businessMatters]
    }

    if (overwrite || !form.disposalType) {
      if (
        declaration.cremationDate ||
        declaration.cremationPlace ||
        declaration.cremationCertNo
      ) {
        form.disposalType = '火化'
      } else if (declaration.burialDate || declaration.burialPlace) {
        form.disposalType = '安葬'
      } else if (declaration.remainsArrangement) {
        form.disposalType = '其他'
      }
    }
    fillText('cremationDate', toDateText(declaration.cremationDate), overwrite)
    fillText('cremationPlace', declaration.cremationPlace, overwrite)
    fillText('cremationCertNo', declaration.cremationCertNo, overwrite)
    fillText('burialDate', toDateText(declaration.burialDate), overwrite)
    fillText('burialPlace', declaration.burialPlace, overwrite)
    fillText('disposalNote', declaration.remainsArrangement, overwrite)
    fillText('familySituation', declaration.familySituation, overwrite)
    fillText('disputeType', declaration.disputeStatus, overwrite)
    fillText('disputeNote', declaration.disputeDesc, overwrite)

    if (declaration.status || declaration.hasDeclaration) {
      isRevoked.value = declaration.status === 'Revoked'
      isArchived.value = declaration.status === 'Archived'
      isDeclared.value =
        !isRevoked.value && (declaration.hasDeclaration || declaration.status !== '')
      statusDisplay.value =
        declaration.statusDisplay ||
        (isRevoked.value ? '已作废' : isArchived.value ? '已归档' : '已申报')
    }
    if (declaration.declaredAtText && !declaredAtText.value) {
      declaredAtText.value = declaration.declaredAtText
    }
  }

  if (!form.village.trim()) {
    form.village = scope.value?.village || ''
  }
}

interface LoadContextOptions {
  force?: boolean
  overwrite?: boolean
  silent?: boolean
  declarationId?: number | null
}

async function loadContext(options: LoadContextOptions = {}): Promise<void> {
  const idCard = form.idCard.trim()
  if (!idCard) {
    if (!options.silent) ElMessage.warning('请先填写死者身份证号')
    return
  }
  if (!options.force && context.value && lastContextIdCard.value === idCard) return

  const explicitDeclarationId = options.declarationId
  const personChanged = Boolean(lastContextIdCard.value) && lastContextIdCard.value !== idCard
  if (personChanged && explicitDeclarationId === undefined) {
    declarationId.value = null
    isDeclared.value = false
    isArchived.value = false
    isRevoked.value = false
    statusDisplay.value = ''
    declaredAtText.value = ''
  }
  const contextDeclarationId =
    explicitDeclarationId !== undefined
      ? explicitDeclarationId
      : personChanged
        ? null
        : declarationId.value

  contextLoading.value = true
  contextError.value = ''
  contextLoadingText.value = '正在汇总身份与提醒…'
  try {
    const result = await getDeathStatementContext({
      declarationId: contextDeclarationId,
      idCard,
    })
    context.value = result
    lastContextIdCard.value = idCard
    applyContext(result, options.overwrite ?? false)
  } catch (error) {
    contextError.value = getErrorMessage(error, '汇总身份与提醒失败')
    if (!options.silent) ElMessage.error(contextError.value)
  } finally {
    contextLoading.value = false
    contextLoadingText.value = ''
  }
}

async function ensureContext(): Promise<void> {
  const idCard = form.idCard.trim()
  if (!idCard) return
  if (context.value && lastContextIdCard.value === idCard) return
  await loadContext({ silent: true })
}

function refreshContext(): void {
  void loadContext({ force: true })
}

function handleIdCardBlur(): void {
  const idCard = form.idCard.trim()
  if (idCard.length !== 18) return
  if (lastContextIdCard.value === idCard) return
  void loadContext({ silent: true })
}

watch(
  () => form.idCard,
  (value, previous) => {
    const idCard = value.trim()
    if (idCard.length !== 18 || idCard === (previous ?? '').trim()) return
    if (contextLoading.value || lastContextIdCard.value === idCard) return
    void loadContext({ silent: true })
  },
)

watch(
  () => form.reporterSame,
  (same) => {
    if (same) return
    if (!form.reporterName.trim()) form.reporterName = form.declarantName
    if (!form.reporterRelation.trim()) form.reporterRelation = form.relation
    if (!form.reporterPhone.trim()) form.reporterPhone = form.declarantPhone
  },
)

function onDeathReasonChange(value: string): void {
  if (!value) {
    form.deathReasonCustom = false
    form.deathReason = ''
    return
  }
  form.deathReasonCustom = value === OTHER
  if (value !== OTHER) form.deathReason = value
}

function onDeathPlaceChange(value: string): void {
  if (!value) {
    form.deathPlaceCustom = false
    form.deathPlace = ''
    return
  }
  form.deathPlaceCustom = value === OTHER
  if (value !== OTHER) form.deathPlace = value
}

function onScenePresetChange(value: string): void {
  if (value) form.sceneDescription = value
}

const reporterName = computed(() =>
  form.reporterSame ? form.declarantName : form.reporterName,
)
const reporterRelation = computed(() =>
  form.reporterSame ? form.relation : form.reporterRelation,
)
const reporterPhone = computed(() =>
  form.reporterSame ? form.declarantPhone : form.reporterPhone,
)

/* ───────────────────────── 选择 / 手工录入 ───────────────────────── */

async function selectItem(item: DeathStatementSearchItem): Promise<void> {
  selectedItem.value = item
  manualMode.value = false
  form.personName = item.personName
  form.idCard = item.idCard
  if (item.deathDate) form.deathDateTime = toDateTimeText(item.deathDate)

  declarationId.value = item.declarationId
  isDeclared.value = item.isDeclared && !item.isRevoked
  isArchived.value = item.isArchived
  isRevoked.value = item.isRevoked
  statusDisplay.value = item.statusDisplay
  declaredAtText.value = ''

  await loadContext({
    force: true,
    overwrite: true,
    declarationId: item.declarationId,
    silent: true,
  })
  if (!contextError.value) {
    ElMessage.success(`已带出 ${item.personName || '该人员'} 的信息`)
  }
}

function startManualEntry(): void {
  selectedItem.value = null
  manualMode.value = true
  declarationId.value = null
  isDeclared.value = false
  isArchived.value = false
  isRevoked.value = false
  statusDisplay.value = ''
  declaredAtText.value = ''
  context.value = null
  contextError.value = ''
  lastContextIdCard.value = ''
  Object.assign(form, createEmptyForm())
  form.village = scope.value?.village || ''
  step.value = 1
  ElMessage.info('已切换为手工录入，请填写死者信息')
}

/* ───────────────────────── 步骤 / 校验 ───────────────────────── */

function validateStep1(): boolean {
  if (!form.personName.trim()) {
    ElMessage.warning('请填写死者姓名')
    return false
  }
  if (!form.idCard.trim()) {
    ElMessage.warning('请填写死者身份证号')
    return false
  }
  return true
}

function validateDeclaration(): string | null {
  if (!form.personName.trim()) return '请填写死者姓名'
  if (!form.idCard.trim()) return '请填写死者身份证号'
  if (!isValidIdCard(form.idCard)) return '死者身份证号校验不通过，请核对后重新填写'
  if (!form.deathDateTime) return '请选择死亡日期与时间'
  if (isFutureDateTime(form.deathDateTime)) return '死亡时间不能晚于当前时间'
  if (!form.declarantName.trim()) return '请填写申报人姓名'
  if (!form.declarantIdCard.trim()) {
    return '请填写申报人身份证号码（文书「申报人承诺」段需要）'
  }
  if (!isValidIdCard(form.declarantIdCard)) {
    return '申报人身份证号校验不通过，请核对后重新填写'
  }
  if (!form.declarantType) return '请选择申报人类型'
  if (form.businessMatters.length === 0) return '请至少勾选一项相关业务事项'
  if (!reporterName.value.trim()) return '请填写发现（报告）人姓名'
  if (degraded.value && !form.village.trim()) {
    return '当前组织未绑定村社区，请填写「所在村」（文书落款与抬头需要）'
  }
  if (!form.disposalType) return '请选择遗体处置方式（火化 / 安葬 / 其他）'
  if (form.disposalType === '火化') {
    if (!form.cremationDate) return '请选择火化日期'
    if (!form.cremationPlace.trim()) return '请填写火化地点（殡仪馆）'
  } else if (form.disposalType === '安葬') {
    if (!form.burialDate) return '请选择安葬日期（已安葬或拟安葬）'
    if (!form.burialPlace.trim()) return '请填写安葬地点'
  } else if (form.disposalType === '其他') {
    if (!form.disposalNote.trim()) return '处置方式为「其他」时请填写遗体处置说明'
  }
  if (!form.disputeType) return '请选择矛盾纠纷及信访隐患情况（无 / 有）'
  if (form.disputeType === '有' && !form.disputeNote.trim()) {
    return '勾选「有」时请填写矛盾纠纷及信访隐患说明'
  }
  return null
}

function goStep(target: number): void {
  if (target === step.value) return
  if (target === 3 && !isDeclared.value) {
    ElMessage.warning('请先提交申报，申报成功后方可进入文书输出')
    return
  }
  if (step.value === 1 && target > 1 && !validateStep1()) return
  step.value = target
  if (target === 2) void ensureContext()
}

function nextStep(): void {
  if (step.value === 1) {
    if (!validateStep1()) return
    step.value = 2
    void ensureContext()
    return
  }
  if (step.value === 2) {
    if (!isDeclared.value) {
      ElMessage.info('请先提交申报')
      return
    }
    step.value = 3
  }
}

function previousStep(): void {
  if (step.value > 1) step.value -= 1
}

/* ───────────────────────── 提交申报 ───────────────────────── */

function buildDeclarePayload(): DeathStatementDeclarePayload {
  const dateText = toDateText(form.deathDateTime)
  const timeText = toTimeText(form.deathDateTime) || '00:00'
  const cremation = form.disposalType === '火化'
  const burial = form.disposalType === '安葬'
  const other = form.disposalType === '其他'
  const village = form.village.trim() || scope.value?.village || ''

  return {
    name: form.personName.trim(),
    personName: form.personName.trim(),
    idCard: form.idCard.trim(),
    deathDate: `${dateText} ${timeText}:00`,
    deathTime: timeText,
    deathReason: form.deathReason.trim(),
    deathCertNo: form.deathCertNo.trim(),
    deathCertificateNo: form.deathCertNo.trim(),
    deathPlace: form.deathPlace.trim(),
    relation: form.relation.trim(),
    declaredByType: form.declarantType,
    declaredByName: form.declarantName.trim(),
    declaredByIdCard: form.declarantIdCard.trim(),
    declaredByPhone: form.declarantPhone.trim(),
    reporterName: reporterName.value.trim(),
    reporterRelation: reporterRelation.value.trim(),
    reporterPhone: reporterPhone.value.trim(),
    discovererName: reporterName.value.trim(),
    discovererRelation: reporterRelation.value.trim(),
    discovererPhone: reporterPhone.value.trim(),
    situationDesc: form.sceneDescription.trim(),
    sceneDescription: form.sceneDescription.trim(),
    remainsArrangement: other ? form.disposalNote.trim() : '',
    disposalNote: other ? form.disposalNote.trim() : '',
    businessAgent: form.businessAgent.trim(),
    businessMatter: form.businessMatters.join('、'),
    businessMatters: [...form.businessMatters],
    certType: form.certType,
    certIssueOrg: form.certIssueOrg.trim(),
    dispositionBranch: form.disposalType,
    disposalType: form.disposalType,
    cremationDate: cremation ? form.cremationDate || null : null,
    cremationPlace: cremation ? form.cremationPlace.trim() : '',
    cremationCertNo: cremation ? form.cremationCertNo.trim() : '',
    burialDate: burial ? form.burialDate || null : null,
    burialPlace: burial ? form.burialPlace.trim() : '',
    familySituation: form.familySituation.trim(),
    disputeStatus: form.disputeType,
    disputeType: form.disputeType,
    disputeDesc: form.disputeType === '有' ? form.disputeNote.trim() : '',
    disputeNote: form.disputeType === '有' ? form.disputeNote.trim() : '',
    villageName: village,
    village,
    operatorName: currentOperatorName(),
    channel: 'PC',
  }
}

async function submitDeclaration(): Promise<void> {
  if (busy.value) return
  pageError.value = ''

  const invalid = validateDeclaration()
  if (invalid) {
    ElMessage.warning(invalid)
    return
  }

  submitting.value = true
  try {
    const deathDate = `${toDateText(form.deathDateTime)} ${toTimeText(form.deathDateTime) || '00:00'}:00`
    const count = await countRecentDeathStatements(form.idCard.trim(), deathDate)
    if (count > 0) {
      try {
        await ElMessageBox.confirm(
          `该死者在死亡日期前后 ${DUPLICATE_WINDOW_DAYS} 天内已有 ${count} 条有效申报。仍要提交本次申报吗？`,
          '重复申报确认',
          {
            confirmButtonText: '仍要提交',
            cancelButtonText: '取消',
            type: 'warning',
          },
        )
      } catch {
        return
      }
    }

    const result = await declareDeathStatement(buildDeclarePayload())
    declarationId.value = result.declarationId || result.id
    isDeclared.value = true
    isArchived.value = false
    isRevoked.value = false
    statusDisplay.value = '待核实'
    declaredAtText.value = formatLocalDateTime(new Date())

    ElMessage.success(
      `申报已提交（编号 ${declarationId.value}，受理时间 ${declaredAtText.value}）`,
    )
    step.value = 3
    await refreshSearchSilently()
    await loadContext({ force: true, silent: true })
  } catch (error) {
    pageError.value = getErrorMessage(error, '申报提交失败')
    ElMessage.error(pageError.value)
  } finally {
    submitting.value = false
  }
}

/* ───────────────────────── 作废 / 归档 ───────────────────────── */

async function revokeDeclaration(): Promise<void> {
  if (!canRevoke.value || declarationId.value === null) return

  let reason = ''
  try {
    const result = await ElMessageBox.prompt(
      '请填写作废原因（将记入台账与审计日志）',
      '作废申报',
      {
        confirmButtonText: '作废',
        cancelButtonText: '取消',
        inputPlaceholder: '作废原因',
        inputValidator: (value: string) => (value && value.trim() ? true : '作废原因不能为空'),
      },
    )
    reason = String(result.value ?? '').trim()
  } catch {
    return
  }

  revoking.value = true
  try {
    await revokeDeathStatement(declarationId.value, reason)
    isRevoked.value = true
    isDeclared.value = false
    statusDisplay.value = '已作废'
    stopPolling()
    ElMessage.success('该申报已作废')
    await refreshSearchSilently()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '作废失败'))
  } finally {
    revoking.value = false
  }
}

async function archiveDeclaration(): Promise<void> {
  if (!canArchive.value || declarationId.value === null) return

  try {
    await ElMessageBox.confirm(
      '归档表示该死亡申报已全部办结（文书已出、联动事项已处理完毕），归档后将不再出现在首页联动提醒中。确认归档？',
      '完结归档',
      {
        confirmButtonText: '确认归档',
        cancelButtonText: '取消',
        type: 'warning',
      },
    )
  } catch {
    return
  }

  archiving.value = true
  try {
    await archiveDeathStatement(declarationId.value)
    isArchived.value = true
    statusDisplay.value = '已归档'
    ElMessage.success('该申报已完结归档')
    await refreshSearchSilently()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '归档失败'))
  } finally {
    archiving.value = false
  }
}

/* ───────────────────────── 联动跳转 ───────────────────────── */

function linkageTarget(link: DeathStatementLinkage): string | null {
  switch (link.navKey) {
    case 'ElderlyStop':
      return link.elderlyApplicationId
        ? `/elderly/stops/${link.elderlyApplicationId}`
        : null
    case 'HouseholdDeath':
      return link.applicationId
        ? `/social-assistance/change-management/death/${link.applicationId}`
        : null
    case 'MemberChange':
      return '/social-assistance/change-management'
    case 'College':
      return '/social-assistance/college-students'
    case 'Recovery':
      return `/recovery?keyword=${encodeURIComponent(form.idCard.trim())}`
    case 'NearRelative':
      return '/social-assistance/near-relatives'
    default:
      return null
  }
}

function linkageActionText(link: DeathStatementLinkage): string {
  if (link.done) return '已完成'
  if (!link.canHandle) return '需镇级办理'
  return linkageTarget(link) ? '去办理' : '暂不支持'
}

function openLinkage(link: DeathStatementLinkage): void {
  if (link.navKey === 'Declare') {
    startManualEntry()
    return
  }
  if (link.done || !link.canHandle) return

  const target = linkageTarget(link)
  if (!target) {
    ElMessage.info('该关联事项暂不支持在 Web 端跳转，请到对应模块办理')
    return
  }
  void router.push(target)
}

/* ───────────────────────── 打印机 / 文书任务 ───────────────────────── */

const printers = ref<PrinterBinding[]>([])
const selectedPrinterId = ref<number | null>(null)
const printersLoading = ref(false)
const printersError = ref('')

const selectedPrinter = computed(
  () => printers.value.find((printer) => printer.id === selectedPrinterId.value) ?? null,
)

/** 文书输出门禁：已申报、未作废、有台账号，且已绑定打印机（服务端预览/打印/保存都要固化 Agent 目标）。 */
const canOutput = computed(
  () =>
    isDeclared.value &&
    !isRevoked.value &&
    declarationId.value !== null &&
    selectedPrinter.value !== null &&
    !busy.value,
)

const copies = ref<number | undefined>(1)
const isDuplex = ref(false)
const copiesCount = computed(() =>
  Math.min(99, Math.max(1, Math.round(copies.value || 1))),
)

interface OutputJobState {
  id: number
  jobNo: string
  status: string
  jobKind: string
  mode: DeathStatementJobMode
  printerName: string
  copies: number
  isDuplex: boolean
  errorMessage: string
  createdAt: string
}

const currentJob = ref<OutputJobState | null>(null)
const submittingMode = ref<DeathStatementJobMode | null>(null)
const jobError = ref('')
const jobPolling = ref(false)

let pollTimer: number | undefined
let jobTicket = 0

const JOB_MODE_LABELS: Record<DeathStatementJobMode, string> = {
  preview: '预览',
  print: '打印',
  save: '保存',
}

const JOB_SUCCESS_MESSAGES: Record<DeathStatementJobMode, string> = {
  preview: '已提交预览任务，请在本机 Agent 查看预览',
  print: '已提交打印任务，请在本机 Agent 查看打印机',
  save: '已提交保存任务，文件将由本机 Agent 保存到输出目录',
}

const JOB_ERROR_FALLBACKS: Record<DeathStatementJobMode, string> = {
  preview: '预览任务提交失败',
  print: '打印任务提交失败',
  save: '保存任务提交失败',
}

const jobStatusLabel = computed(() => {
  const job = currentJob.value
  if (!job) return ''
  if (job.status === 'Pending') {
    if (job.mode === 'preview') return '待预览'
    if (job.mode === 'save') return '待保存'
    return '待打印'
  }
  return archiveJobStatusLabel(job.status)
})

const jobStatusTone = computed(() =>
  currentJob.value ? archiveJobStatusTone(currentJob.value.status) : 'default',
)

/**
 * 任务消息展示口径：LocalOp 的完成消息是 `RESULT:` 结构化结果（不是错误），
 * 按完成信息渲染；其余（含失败摘要）仍按错误样式展示。见 archivePrint.describePrintJobError。
 */
const jobErrorDisplay = computed(() => {
  const job = currentJob.value
  return job ? describePrintJobError(job) : { text: '', isError: true }
})

async function loadPrinters(): Promise<void> {
  printersLoading.value = true
  printersError.value = ''
  try {
    const list = await listPrinters()
    printers.value = list

    const current = list.find((printer) => printer.id === selectedPrinterId.value)
    if (current) return

    const preferred =
      list.find((printer) => printer.isDefault && printer.online) ??
      list.find((printer) => printer.isDefault) ??
      list.find((printer) => printer.online) ??
      list[0]
    selectedPrinterId.value = preferred?.id ?? null
  } catch (error) {
    printers.value = []
    selectedPrinterId.value = null
    printersError.value = getErrorMessage(error, '打印机列表加载失败')
  } finally {
    printersLoading.value = false
  }
}

async function submitJob(mode: DeathStatementJobMode): Promise<void> {
  if (submittingMode.value) return
  if (!isDeclared.value || declarationId.value === null) {
    ElMessage.warning('请先提交申报，申报成功后方可输出文书')
    return
  }
  if (isRevoked.value) {
    ElMessage.warning('该申报已作废，不能输出文书')
    return
  }
  if (!selectedPrinter.value) {
    ElMessage.warning('请先绑定并选择打印机（预览 / 打印 / 保存均由本机 Agent 执行）')
    return
  }

  submittingMode.value = mode
  jobError.value = ''
  try {
    const printer = selectedPrinter.value
    const result = await createDeathStatementJob({
      declarationId: declarationId.value,
      mode,
      copies: copiesCount.value,
      isDuplex: isDuplex.value,
      printerBindingId: printer.id,
      printerName: printer.printerName,
    })

    currentJob.value = {
      id: result.id,
      jobNo: result.jobNo,
      status: result.status || 'Pending',
      jobKind: result.jobKind,
      mode,
      printerName: `${printer.deviceName} · ${printer.printerName}`,
      copies: copiesCount.value,
      isDuplex: isDuplex.value,
      errorMessage: '',
      createdAt: new Date().toISOString(),
    }

    ElMessage.success(JOB_SUCCESS_MESSAGES[mode])
    startPolling()
  } catch (error) {
    jobError.value = getErrorMessage(error, JOB_ERROR_FALLBACKS[mode])
    ElMessage.error(jobError.value)
  } finally {
    submittingMode.value = null
  }
}

async function pollJob(): Promise<void> {
  const job = currentJob.value
  if (!job || job.id <= 0) return
  if (isArchiveJobTerminal(job.status)) {
    stopPolling()
    return
  }

  const ticket = ++jobTicket
  jobPolling.value = true
  try {
    const page = await listPrintJobs({ pageIndex: 1, pageSize: JOB_PAGE_SIZE })
    if (ticket !== jobTicket) return

    const found = page.items.find(
      (item) => item.id === job.id || (job.jobNo && item.jobNo === job.jobNo),
    )
    if (!found) return

    currentJob.value = {
      ...job,
      status: found.status,
      printerName: found.printerName || job.printerName,
      errorMessage: found.errorMessage,
      createdAt: found.createdAt || job.createdAt,
    }
    if (isArchiveJobTerminal(found.status)) stopPolling()
  } catch {
    // 轮询失败不打断页面；下次定时器继续尝试。
  } finally {
    if (ticket === jobTicket) jobPolling.value = false
  }
}

function startPolling(): void {
  stopPolling()
  void pollJob()
  pollTimer = window.setInterval(() => {
    if (isArchiveJobTerminal(currentJob.value?.status ?? '')) {
      stopPolling()
      return
    }
    void pollJob()
  }, POLL_INTERVAL)
}

function stopPolling(): void {
  if (pollTimer !== undefined) {
    window.clearInterval(pollTimer)
    pollTimer = undefined
  }
}

/* ───────────────────────── 生命周期 / 导航 ───────────────────────── */

async function loadScope(): Promise<void> {
  scopeLoading.value = true
  scopeError.value = ''
  try {
    const result = await getDeathStatementScope()
    scope.value = result
    if (!result.degraded && !form.village.trim()) form.village = result.village
    await refreshSearchSilently()
  } catch (error) {
    scope.value = null
    scopeError.value = getErrorMessage(error, '查询范围解析失败')
  } finally {
    scopeLoading.value = false
  }
}

function goBack(): void {
  stopPolling()
  if (window.history.length > 1) {
    router.back()
    return
  }
  void router.push('/archives/query')
}

onMounted(() => {
  void loadScope()
  void loadPrinters()
})

onBeforeUnmount(() => {
  stopPolling()
})
</script>

<template>
  <div class="nc-page death-page">
    <NcPageHeader
      title="死亡情况说明"
      description="死亡申报 · 身份聚合 · 文书出具"
      kicker="档案管理 · 死亡申报"
    >
      <template #actions>
        <span class="scope-chip">{{ scopeLabel }}</span>
        <el-button :icon="ArrowLeft" @click="goBack">返回</el-button>
      </template>
    </NcPageHeader>

    <el-alert
      v-if="scopeLoading"
      class="page-alert"
      type="info"
      :closable="false"
      show-icon
      title="正在解析查询范围…"
    />
    <el-alert
      v-if="scope?.degraded"
      class="page-alert"
      type="warning"
      :closable="false"
      show-icon
      :title="
        scope.degradedReason ||
        `当前组织未绑定村/社区，已按全镇范围查询：${scope.town || '—'}`
      "
      description="如需按村口径办理，请到「经办机构管理」补齐区划；本页「所在村」需手工填写。"
    />
    <el-alert
      v-if="scope?.degradedReason && !scope?.degraded"
      class="page-alert"
      type="error"
      :closable="false"
      show-icon
      :title="scope.degradedReason"
    />
    <el-alert
      v-if="scopeError"
      class="page-alert"
      type="error"
      :closable="false"
      show-icon
      :title="scopeError"
    >
      <el-button link type="primary" @click="loadScope()">重新解析</el-button>
    </el-alert>
    <el-alert
      v-if="pageError"
      class="page-alert"
      type="error"
      :closable="false"
      show-icon
      :title="pageError"
    />

    <!-- ═══════ 步骤条 ═══════ -->
    <nav class="step-bar" aria-label="死亡情况说明办理步骤">
      <button
        v-for="(title, index) in STEP_TITLES"
        :key="title"
        type="button"
        class="step-item"
        :class="{ 'is-active': step === index + 1, 'is-done': step > index + 1 }"
        @click="goStep(index + 1)"
      >
        <span class="step-index">{{ step > index + 1 ? '✓' : index + 1 }}</span>
        <span class="step-title">{{ title }}</span>
      </button>
      <p v-if="isNarrow" class="step-mobile-text">
        第 {{ step }} 步 / 共 {{ STEP_TITLES.length }} 步 · {{ STEP_TITLES[step - 1] }}
      </p>
    </nav>

    <!-- ═══════ 第 1 步：申报信息 ═══════ -->
    <div v-show="step === 1" class="step-panel">
      <NcCard
        title="从库内带出"
        subtitle="按姓名 / 身份证检索本范围申报台账与档案死亡登记"
      >
        <template #actions>
          <el-button :icon="EditPen" @click="startManualEntry">手工录入</el-button>
        </template>

        <div class="search-row">
          <el-input
            v-model="searchKeyword"
            placeholder="姓名或身份证号（留空按日期范围）"
            clearable
            :prefix-icon="Search"
            @keyup.enter="runSearch()"
          />
          <el-button
            type="primary"
            :icon="Search"
            :loading="searchLoading"
            @click="runSearch()"
          >
            搜索
          </el-button>
        </div>

        <div class="search-filters">
          <el-date-picker
            v-model="searchRange"
            type="daterange"
            value-format="YYYY-MM-DD"
            range-separator="至"
            start-placeholder="起始日期"
            end-placeholder="截止日期"
            class="search-range"
          />
          <el-checkbox v-model="searchShowAll">含作废申报</el-checkbox>
        </div>

        <p class="meta-line">{{ searchSummary }}</p>

        <el-alert
          v-if="searchError"
          class="inline-alert"
          type="error"
          :closable="false"
          show-icon
          :title="searchError"
        />

        <div v-if="searchResults.length > 0" class="result-list">
          <div
            v-for="item in searchResults"
            :key="`${item.id}-${item.idCard}`"
            class="result-row"
            :class="{
              'is-selected':
                selectedItem?.id === item.id && selectedItem?.idCard === item.idCard,
            }"
            @click="selectItem(item)"
          >
            <div class="result-main">
              <div class="result-title">
                <NcStatusDot :status="itemStatusTone(item)" :label="itemStatusText(item)" />
                <strong>{{ item.personName || '未填写姓名' }}</strong>
              </div>
              <p class="result-id">{{ item.idCard || '—' }}</p>
              <p class="result-sub">{{ item.subtitle || item.source || '—' }}</p>
            </div>
            <el-button size="small" @click.stop="selectItem(item)">带出</el-button>
          </div>
        </div>
        <el-empty
          v-else-if="searchPerformed && !searchLoading"
          description="本范围暂无申报 / 死亡登记记录，可点「手工录入」直接申报"
          :image-size="60"
        />
      </NcCard>

      <NcCard title="死者信息">
        <template #actions>
          <NcStatusDot
            v-if="isDeclared || isArchived || isRevoked"
            :status="statusTone"
            :label="statusText"
          />
        </template>

        <div class="form-grid">
          <label class="field">
            <span class="field-label">死者姓名 *</span>
            <el-input v-model="form.personName" maxlength="50" placeholder="请填写死者姓名" />
          </label>
          <label class="field">
            <span class="field-label">死者身份证号 *</span>
            <el-input
              v-model="form.idCard"
              maxlength="18"
              placeholder="18 位，填满自动带出档案"
              @blur="handleIdCardBlur"
            />
          </label>
        </div>

        <p v-if="personGender || personAge !== null" class="meta-line">
          性别 / 年龄：{{ personGender || '—' }} ·
          {{ personAge === null ? '—' : `${personAge} 岁` }}
        </p>
        <p v-if="archiveInfo" class="meta-line">档案带出：{{ archiveInfo }}</p>
        <p v-if="declaredAtText" class="meta-line">申报受理时间：{{ declaredAtText }}</p>
        <p v-if="contextLoading" class="meta-line is-loading">
          {{ contextLoadingText || '正在汇总身份与提醒…' }}
        </p>
        <el-alert
          v-if="contextError"
          class="inline-alert"
          type="warning"
          :closable="false"
          show-icon
          :title="contextError"
        />
      </NcCard>

      <NcCard title="死亡信息">
        <div class="form-grid">
          <label class="field field--wide">
            <span class="field-label">死亡日期与时间 *</span>
            <el-date-picker
              v-model="form.deathDateTime"
              type="datetime"
              value-format="YYYY-MM-DD HH:mm:ss"
              placeholder="请选择死亡日期与时间（不得晚于当前）"
              :disabled-date="isFutureDate"
              class="full-control"
            />
          </label>

          <label class="field">
            <span class="field-label">死亡原因</span>
            <el-select
              v-model="form.deathReasonChoice"
              placeholder="请选择死亡原因"
              clearable
              class="full-control"
              @change="onDeathReasonChange"
            >
              <el-option
                v-for="option in DEATH_REASON_OPTIONS"
                :key="option"
                :label="option"
                :value="option"
              />
            </el-select>
            <el-input
              v-if="form.deathReasonCustom"
              v-model="form.deathReason"
              placeholder="请输入其他死亡原因"
            />
          </label>

          <label class="field">
            <span class="field-label">死亡地点</span>
            <el-select
              v-model="form.deathPlaceChoice"
              placeholder="请选择死亡地点"
              clearable
              class="full-control"
              @change="onDeathPlaceChange"
            >
              <el-option
                v-for="option in DEATH_PLACE_OPTIONS"
                :key="option"
                :label="option"
                :value="option"
              />
            </el-select>
            <el-input
              v-if="form.deathPlaceCustom"
              v-model="form.deathPlace"
              placeholder="选「其他」时手填"
            />
          </label>

          <label class="field">
            <span class="field-label">死亡证明编号</span>
            <el-input v-model="form.deathCertNo" placeholder="缺失会进数据提醒" />
          </label>

          <label class="field">
            <span class="field-label">死亡证明类型（条目一）</span>
            <el-select
              v-model="form.certType"
              placeholder="请选择证明类型"
              clearable
              class="full-control"
            >
              <el-option
                v-for="option in certTypeOptions"
                :key="option"
                :label="option"
                :value="option"
              />
            </el-select>
          </label>

          <label class="field field--wide">
            <span class="field-label">死亡证明出具机构（条目一）</span>
            <el-input v-model="form.certIssueOrg" placeholder="如：XX镇中心卫生院" />
          </label>
        </div>
      </NcCard>

      <NcCard title="申报人信息" subtitle="类型与姓名必填；身份证用于文书「申报人承诺」段">
        <div class="form-grid">
          <label class="field">
            <span class="field-label">申报人类型 *</span>
            <el-select v-model="form.declarantType" class="full-control">
              <el-option
                v-for="option in declarantTypeOptions"
                :key="option"
                :label="option"
                :value="option"
              />
            </el-select>
          </label>
          <label class="field">
            <span class="field-label">申报人姓名 *</span>
            <el-input v-model="form.declarantName" maxlength="50" placeholder="如：张三" />
          </label>
          <label class="field field--wide">
            <span class="field-label">申报人身份证号码 *</span>
            <el-input
              v-model="form.declarantIdCard"
              maxlength="18"
              placeholder="18 位身份证号"
            />
          </label>
          <label class="field">
            <span class="field-label">申报人电话</span>
            <el-input v-model="form.declarantPhone" maxlength="30" placeholder="选填" />
          </label>
          <label class="field">
            <span class="field-label">死者与申报人关系</span>
            <el-select
              v-model="form.relation"
              placeholder="请选择关系"
              clearable
              class="full-control"
            >
              <el-option
                v-for="option in relationOptions"
                :key="option"
                :label="option"
                :value="option"
              />
            </el-select>
          </label>
        </div>

        <el-divider class="card-divider" />

        <div class="reporter-head">
          <span class="group-title">发现（报告）人</span>
          <el-checkbox v-model="form.reporterSame">
            与申报人相同（姓名 / 关系 / 电话自动带入）
          </el-checkbox>
        </div>

        <div v-if="!form.reporterSame" class="form-grid">
          <label class="field">
            <span class="field-label">报告人姓名 *</span>
            <el-input v-model="form.reporterName" maxlength="50" placeholder="最先发现或报告死亡的人" />
          </label>
          <label class="field">
            <span class="field-label">报告人与死者关系</span>
            <el-select
              v-model="form.reporterRelation"
              placeholder="请选择关系"
              clearable
              class="full-control"
            >
              <el-option
                v-for="option in relationOptions"
                :key="option"
                :label="option"
                :value="option"
              />
            </el-select>
          </label>
          <label class="field">
            <span class="field-label">报告人联系电话</span>
            <el-input v-model="form.reporterPhone" maxlength="30" placeholder="选填" />
          </label>
        </div>
        <p v-else class="meta-line">
          报告人：{{ form.declarantName || '—' }}（与申报人相同）
        </p>
      </NcCard>
    </div>

    <!-- ═══════ 第 2 步：核实与汇总 ═══════ -->
    <div v-show="step === 2" class="step-panel">
      <el-alert
        v-if="contextError"
        class="page-alert"
        type="warning"
        :closable="false"
        show-icon
        :title="contextError"
      >
        <el-button link type="primary" @click="refreshContext">重新汇总</el-button>
      </el-alert>

      <NcCard title="核实说明要素" subtitle="《死亡情况核实说明》正文">
        <div class="form-grid">
          <label class="field">
            <span class="field-label">
              所在村{{ degraded ? '（镇级账号必填，落款用）' : '' }}
            </span>
            <el-select
              v-if="degraded"
              v-model="form.village"
              filterable
              allow-create
              default-first-option
              placeholder="请选择或填写所在村"
              class="full-control"
            >
              <el-option
                v-for="village in villageOptions"
                :key="village"
                :label="village"
                :value="village"
              />
            </el-select>
            <el-input v-else :model-value="form.village || scope?.village || ''" readonly />
          </label>
          <label class="field">
            <span class="field-label">业务办理人</span>
            <el-input v-model="form.businessAgent" maxlength="50" placeholder="默认经办人" />
          </label>
        </div>

        <div class="field-block">
          <span class="field-label">相关业务事项（可多选，至少 1 项）*</span>
          <el-checkbox-group v-model="form.businessMatters" class="matter-group">
            <el-checkbox
              v-for="matter in businessMatterOptions"
              :key="matter"
              :value="matter"
            >
              {{ matter }}
            </el-checkbox>
          </el-checkbox-group>
        </div>

        <label class="field">
          <span class="field-label">现场所见情况（模板 {死者情况描述}）</span>
          <el-select
            v-model="form.scenePreset"
            placeholder="选择预设后可在下方微调"
            clearable
            class="full-control"
            @change="onScenePresetChange"
          >
            <el-option
              v-for="option in SCENE_PRESET_OPTIONS"
              :key="option"
              :label="option"
              :value="option"
            />
          </el-select>
          <el-input
            v-model="form.sceneDescription"
            type="textarea"
            :rows="3"
            maxlength="500"
            show-word-limit
            placeholder="现场所见情况描述…"
          />
        </label>
      </NcCard>

      <NcCard title="情况说明" subtitle="文书条目二 / 四 / 五">
        <div class="field-block">
          <span class="field-label">遗体处置情况（必选）*</span>
          <el-radio-group v-model="form.disposalType">
            <el-radio-button
              v-for="option in DISPOSAL_OPTIONS"
              :key="option"
              :value="option"
            >
              {{ option }}
            </el-radio-button>
          </el-radio-group>
        </div>

        <div v-if="form.disposalType === '火化'" class="form-grid">
          <label class="field">
            <span class="field-label">火化日期 *</span>
            <el-date-picker
              v-model="form.cremationDate"
              type="date"
              value-format="YYYY-MM-DD"
              :disabled-date="isFutureDate"
              placeholder="请选择火化日期"
              class="full-control"
            />
          </label>
          <label class="field">
            <span class="field-label">火化地点（殡仪馆）*</span>
            <el-input v-model="form.cremationPlace" maxlength="100" />
          </label>
          <label class="field">
            <span class="field-label">火化证明编号</span>
            <el-input v-model="form.cremationCertNo" maxlength="50" placeholder="选填" />
          </label>
        </div>

        <div v-else-if="form.disposalType === '安葬'" class="form-grid">
          <label class="field">
            <span class="field-label">安葬日期（已安葬或拟安葬）*</span>
            <el-date-picker
              v-model="form.burialDate"
              type="date"
              value-format="YYYY-MM-DD"
              :disabled-date="isBeyondOneYear"
              placeholder="可选择未来 1 年内日期"
              class="full-control"
            />
          </label>
          <label class="field">
            <span class="field-label">安葬地点（公益性墓地）*</span>
            <el-input v-model="form.burialPlace" maxlength="100" />
          </label>
        </div>

        <label v-else-if="form.disposalType === '其他'" class="field">
          <span class="field-label">遗体处置说明 *</span>
          <el-input
            v-model="form.disposalNote"
            type="textarea"
            :rows="2"
            maxlength="300"
            show-word-limit
            placeholder="如：现停放于XX殡仪馆，拟于X月X日安葬于XX公益性墓地"
          />
        </label>

        <label class="field">
          <span class="field-label">家属情况及诉求（条目四）</span>
          <el-input
            v-model="form.familySituation"
            type="textarea"
            :rows="2"
            maxlength="500"
            show-word-limit
            placeholder="家属基本情况与相关诉求…"
          />
        </label>

        <div class="field-block">
          <span class="field-label">矛盾纠纷及信访隐患情况（必选）*</span>
          <el-radio-group v-model="form.disputeType">
            <el-radio-button
              v-for="option in DISPUTE_OPTIONS"
              :key="option"
              :value="option"
            >
              {{ option }}
            </el-radio-button>
          </el-radio-group>
          <el-input
            v-if="form.disputeType === '有'"
            v-model="form.disputeNote"
            type="textarea"
            :rows="2"
            maxlength="500"
            show-word-limit
            placeholder="请填写矛盾纠纷及信访隐患说明"
          />
        </div>
      </NcCard>

      <NcCard title="在库身份（本范围）">
        <template #actions>
          <span v-if="identities.length > 0" class="count-chip">{{ identities.length }}</span>
        </template>
        <p class="meta-line">{{ identitySummary }}</p>
        <div v-if="identities.length > 0" class="identity-list">
          <div
            v-for="(identity, index) in identities"
            :key="`${identity.label}-${index}`"
            class="identity-row"
          >
            <div class="identity-main">
              <strong>{{ identity.label }}</strong>
              <span class="identity-status">{{ identity.status || '—' }}</span>
            </div>
            <p class="identity-summary">{{ identity.summary || identity.source || '—' }}</p>
          </div>
        </div>
        <el-empty
          v-else
          description="系统内无该人身份记录（回到上一步填写身份证后会自动汇总）"
          :image-size="48"
        />
      </NcCard>

      <NcCard title="数据信息提醒">
        <template #actions>
          <span v-if="reminders.length > 0" class="count-chip count-chip--warning">
            {{ reminders.length }}
          </span>
        </template>
        <div v-if="reminders.length > 0" class="reminder-list">
          <div
            v-for="(reminder, index) in reminders"
            :key="index"
            class="reminder-item"
            :class="{ 'is-warning': reminder.level.includes('警告') }"
          >
            <span class="reminder-level">{{ reminder.level }}</span>
            <div class="reminder-body">
              <p>{{ reminder.text }}</p>
              <p v-if="reminder.suggestion" class="reminder-suggest">
                {{ reminder.suggestion }}
              </p>
            </div>
          </div>
        </div>
        <el-empty v-else description="无数据提醒" :image-size="48" />
      </NcCard>

      <NcCard
        v-if="linkages.length > 0"
        title="关联流程办理"
        subtitle="只读视图；点击跳转到对应模块办理，不在此处直接改写"
      >
        <div class="linkage-list">
          <div
            v-for="(link, index) in linkages"
            :key="`${link.domain}-${index}`"
            class="linkage-item"
          >
            <div class="linkage-main">
              <strong>{{ link.label }}</strong>
              <p>{{ link.statusDisplay || link.status || '—' }}</p>
            </div>
            <el-button
              size="small"
              :type="link.done ? 'success' : 'primary'"
              plain
              :disabled="link.done || !link.canHandle || !linkageTarget(link)"
              @click="openLinkage(link)"
            >
              {{ linkageActionText(link) }}
            </el-button>
          </div>
        </div>
      </NcCard>
    </div>

    <!-- ═══════ 第 3 步：文书输出 ═══════ -->
    <div v-show="step === 3" class="step-panel">
      <div class="output-layout">
        <div class="output-left">
          <NcCard title="文书模板" subtitle="模板由后端按文档类型自动定位">
            <div class="template-box">
              <el-icon class="template-icon"><Document /></el-icon>
              <div>
                <strong>{{ TEMPLATE_NAME }}</strong>
                <p>文档类型：死亡情况说明 · 证明文件</p>
              </div>
            </div>
            <p class="meta-line">
              Web 端不内嵌 PDF；预览、打印与保存均由本机 Agent 执行，请确保 Agent 在线并已绑定打印机。
            </p>
          </NcCard>

          <NcCard title="打印设置">
            <div class="print-grid">
              <label class="field">
                <span class="field-label">打印机</span>
                <el-select
                  v-model="selectedPrinterId"
                  placeholder="请选择打印机"
                  :loading="printersLoading"
                  class="full-control"
                >
                  <el-option
                    v-for="printer in printers"
                    :key="printer.id"
                    :label="`${printer.deviceName} · ${printer.printerName}`"
                    :value="printer.id"
                  >
                    <span>{{ printer.deviceName }} · {{ printer.printerName }}</span>
                    <span class="printer-state">
                      {{ printer.online ? '在线' : '离线' }}
                      {{ printer.isDefault ? ' · 默认' : '' }}
                    </span>
                  </el-option>
                </el-select>
              </label>

              <label class="field">
                <span class="field-label">份数</span>
                <el-input-number
                  v-model="copies"
                  :min="1"
                  :max="99"
                  step-strictly
                  controls-position="right"
                  class="full-control"
                />
              </label>

              <label class="field">
                <span class="field-label">双面打印</span>
                <el-switch v-model="isDuplex" />
              </label>
            </div>

            <el-alert
              v-if="printersError"
              class="inline-alert"
              type="warning"
              :closable="false"
              show-icon
              :title="printersError"
            />
            <p v-if="!printers.length && !printersLoading" class="meta-line">
              尚未绑定打印机，请先到「打印机绑定」完成绑定；预览 / 打印 / 保存均需本机 Agent 与已绑定设备。
            </p>

            <div class="output-actions">
              <el-button
                type="primary"
                :icon="View"
                :loading="submittingMode === 'preview'"
                :disabled="!canOutput"
                @click="submitJob('preview')"
              >
                预览
              </el-button>
              <el-button
                type="primary"
                :icon="Printer"
                :loading="submittingMode === 'print'"
                :disabled="!canOutput"
                @click="submitJob('print')"
              >
                打印
              </el-button>
              <el-button
                type="success"
                :icon="Download"
                :loading="submittingMode === 'save'"
                :disabled="!canOutput"
                @click="submitJob('save')"
              >
                保存
              </el-button>
            </div>
          </NcCard>
        </div>

        <div class="output-right">
          <NcCard
            title="预览 / 任务状态"
            subtitle="任务由本机 Agent 领取执行；Web 端不内嵌 PDF"
            padding="lg"
          >
            <div v-if="currentJob" class="job-panel">
              <div class="job-head">
                <span class="job-mode">{{ JOB_MODE_LABELS[currentJob.mode] }}</span>
                <NcStatusDot :status="jobStatusTone" :label="jobStatusLabel" />
              </div>

              <dl class="job-meta">
                <div class="job-meta-row">
                  <dt>任务号</dt>
                  <dd class="job-no">{{ currentJob.jobNo || '—' }}</dd>
                </div>
                <div class="job-meta-row">
                  <dt>方式</dt>
                  <dd>{{ JOB_MODE_LABELS[currentJob.mode] }}</dd>
                </div>
                <div class="job-meta-row">
                  <dt>打印机</dt>
                  <dd>{{ currentJob.printerName || '—' }}</dd>
                </div>
                <div class="job-meta-row">
                  <dt>份数</dt>
                  <dd>
                    {{ currentJob.copies }} 份 ·
                    {{ currentJob.isDuplex ? '双面' : '单面' }}
                  </dd>
                </div>
                <div class="job-meta-row">
                  <dt>状态</dt>
                  <dd>{{ jobStatusLabel }}（{{ currentJob.status }}）</dd>
                </div>
                <div class="job-meta-row">
                  <dt>提交时间</dt>
                  <dd>{{ formatDateTimeText(currentJob.createdAt) }}</dd>
                </div>
              </dl>

              <p v-if="jobErrorDisplay.text" :class="jobErrorDisplay.isError ? 'job-error' : 'job-desc'">
                {{ jobErrorDisplay.text }}
              </p>
              <p v-else class="job-desc">
                {{
                  currentJob.mode === 'preview'
                    ? '已推送到 PC 端预览'
                    : currentJob.mode === 'print'
                      ? '已推送到 PC 端打印'
                      : '已推送到 PC 端保存到输出目录'
                }}
              </p>

              <el-button
                class="poll-button"
                size="small"
                :icon="Refresh"
                :loading="jobPolling"
                @click="pollJob()"
              >
                刷新任务状态
              </el-button>
            </div>

            <el-empty
              v-else
              description="点「预览」生成死亡情况说明；打印 / 保存任务会显示在这里"
              :image-size="100"
            />

            <el-alert
              v-if="jobError"
              class="inline-alert"
              type="error"
              :closable="false"
              show-icon
              :title="jobError"
            />

            <div class="output-footer">
              <div class="output-status">
                <NcStatusDot :status="statusTone" :label="statusText" />
                <span v-if="declarationId !== null" class="declaration-no">
                  台账编号 {{ declarationId }}
                </span>
              </div>
              <div class="output-footer-actions">
                <el-button
                  v-if="canRevoke"
                  type="danger"
                  plain
                  size="small"
                  :loading="revoking"
                  @click="revokeDeclaration"
                >
                  作废
                </el-button>
                <el-button
                  v-if="canArchive"
                  type="primary"
                  plain
                  size="small"
                  :loading="archiving"
                  @click="archiveDeclaration"
                >
                  完结归档
                </el-button>
              </div>
            </div>

            <p class="meta-line output-hint">
              打开输出目录：保存完成后由本机 Agent 打开输出目录；如未自动打开，请到「打印机绑定」页确认 Agent 在线。
            </p>
          </NcCard>
        </div>
      </div>
    </div>

    <!-- ═══════ 底部操作条 ═══════ -->
    <div class="bottom-bar">
      <div class="bottom-hint">
        <span class="bottom-step">第 {{ step }} 步 · {{ STEP_TITLES[step - 1] }}</span>
        <span v-if="manualMode" class="bottom-mode">手工录入</span>
        <span v-else-if="selectedItem" class="bottom-mode">
          已带出：{{ selectedItem.personName || '—' }}
        </span>
      </div>

      <div class="bottom-actions">
        <el-button v-if="step > 1" :icon="ArrowLeft" @click="previousStep">上一步</el-button>

        <template v-if="step === 1">
          <el-button type="primary" @click="nextStep">下一步：核实与汇总</el-button>
        </template>

        <template v-else-if="step === 2">
          <el-button :icon="Refresh" :loading="contextLoading" @click="refreshContext">
            重新汇总
          </el-button>
          <el-button
            type="primary"
            :icon="Check"
            :loading="submitting"
            :disabled="busy"
            @click="submitDeclaration"
          >
            提交申报
          </el-button>
        </template>

        <template v-else>
          <el-button
            :icon="View"
            :loading="submittingMode === 'preview'"
            :disabled="!canOutput"
            @click="submitJob('preview')"
          >
            预览
          </el-button>
          <el-button
            :icon="Printer"
            :loading="submittingMode === 'print'"
            :disabled="!canOutput"
            @click="submitJob('print')"
          >
            打印
          </el-button>
          <el-button
            type="success"
            :icon="Download"
            :loading="submittingMode === 'save'"
            :disabled="!canOutput"
            @click="submitJob('save')"
          >
            保存
          </el-button>
        </template>
      </div>
    </div>
  </div>
</template>

<style scoped>
.death-page {
  display: flex;
  flex-direction: column;
  gap: 14px;
  padding-bottom: calc(84px + env(safe-area-inset-bottom));
}

.page-alert,
.inline-alert {
  margin: 0;
}

.scope-chip {
  display: inline-flex;
  align-items: center;
  max-width: 320px;
  padding: 3px 10px;
  overflow: hidden;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-3);
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
}

/* ── 步骤条 ───────────────────────────────────────────────────── */

.step-bar {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 8px;
  padding: 8px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-2);
  box-shadow: var(--nc-shadow-xs);
}

.step-item {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  padding: 9px 10px;
  border: 1px solid transparent;
  border-radius: var(--nc-radius-sm);
  background: transparent;
  color: var(--nc-muted-2);
  font-family: var(--nc-font-sans);
  font-size: var(--nc-text-base);
  cursor: pointer;
  transition: background-color var(--nc-transition), color var(--nc-transition);
}

.step-item:hover {
  background-color: var(--nc-paper-3);
}

.step-item.is-active {
  border-color: var(--nc-seal);
  background-color: var(--nc-seal-soft);
  color: var(--nc-seal-dark);
  font-weight: 700;
}

.step-item.is-done {
  color: var(--nc-pine);
}

.step-index {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 22px;
  height: 22px;
  flex: none;
  border: 1px solid currentColor;
  border-radius: 50%;
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  font-weight: 700;
}

.step-item.is-active .step-index {
  border-color: var(--nc-seal);
  background-color: var(--nc-seal);
  color: #fff;
}

.step-item.is-done .step-index {
  border-color: var(--nc-pine);
  background-color: var(--nc-pine);
  color: #fff;
}

.step-title {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.step-mobile-text {
  display: none;
}

.step-panel {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

/* ── 搜索 / 结果 ─────────────────────────────────────────────── */

.search-row {
  display: flex;
  gap: 10px;
}

.search-row :deep(.el-input) {
  flex: 1;
}

.search-filters {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 12px;
  margin-top: 10px;
}

.search-range {
  flex: 1;
  min-width: 260px;
}

.meta-line {
  margin-top: 10px;
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
  line-height: 1.65;
}

.meta-line.is-loading {
  color: var(--nc-amber-dark);
}

.result-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
  max-height: 320px;
  margin-top: 10px;
  padding: 6px;
  overflow-y: auto;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-3);
}

.result-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 9px 10px;
  border: 1px solid transparent;
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-2);
  cursor: pointer;
  transition: border-color var(--nc-transition), background-color var(--nc-transition);
}

.result-row:hover {
  border-color: var(--nc-line);
}

.result-row.is-selected {
  border-color: var(--nc-seal);
  background-color: var(--nc-seal-wash);
}

.result-main {
  min-width: 0;
}

.result-title {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.result-title strong {
  color: var(--nc-ink);
  font-size: var(--nc-text-md);
}

.result-id {
  margin-top: 2px;
  color: var(--nc-ink-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  letter-spacing: 0.02em;
}

.result-sub {
  margin-top: 2px;
  overflow: hidden;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  text-overflow: ellipsis;
  white-space: nowrap;
}

/* ── 表单 ────────────────────────────────────────────────────── */

.form-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 12px 14px;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
}

.field--wide {
  grid-column: span 2;
}

.field-label {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  line-height: 1.5;
}

.full-control {
  width: 100%;
}

.field-block {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin-top: 14px;
}

.matter-group {
  display: flex;
  flex-wrap: wrap;
  gap: 2px 14px;
  padding: 8px 10px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-3);
}

.card-divider {
  margin: 18px 0 14px;
}

.group-title {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  font-weight: 700;
}

.reporter-head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  margin-bottom: 12px;
}

/* ── 上下文卡片 ──────────────────────────────────────────────── */

.count-chip {
  display: inline-flex;
  align-items: center;
  min-width: 24px;
  padding: 0 7px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-3);
  color: var(--nc-seal-dark);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  font-weight: 700;
  line-height: 20px;
  justify-content: center;
}

.count-chip--warning {
  border-color: rgba(150, 104, 28, 0.28);
  background-color: var(--nc-amber-soft);
  color: var(--nc-amber-dark);
}

.identity-list,
.reminder-list,
.linkage-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin-top: 10px;
}

.identity-row,
.linkage-item {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  padding: 10px 12px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-3);
}

.identity-main {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.identity-main strong {
  color: var(--nc-ink);
  font-size: var(--nc-text-base);
}

.identity-status {
  padding: 0 6px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-xs);
  color: var(--nc-muted-2);
  font-size: var(--nc-text-xs);
  line-height: 18px;
}

.identity-summary {
  margin-top: 4px;
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
  line-height: 1.6;
}

.reminder-item {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  padding: 10px 12px;
  border-left: 3px solid var(--nc-amber);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-amber-soft);
}

.reminder-item.is-warning {
  border-left-color: var(--nc-seal);
  background-color: var(--nc-seal-soft);
}

.reminder-level {
  flex: none;
  color: var(--nc-amber-dark);
  font-size: var(--nc-text-xs);
  font-weight: 700;
  letter-spacing: 0.08em;
  line-height: 1.7;
}

.reminder-item.is-warning .reminder-level {
  color: var(--nc-seal-dark);
}

.reminder-body {
  min-width: 0;
}

.reminder-body p {
  color: var(--nc-ink);
  font-size: var(--nc-text-base);
  line-height: 1.6;
}

.reminder-suggest {
  margin-top: 3px;
  color: var(--nc-amber-dark);
  font-size: var(--nc-text-sm);
}

.reminder-item.is-warning .reminder-suggest {
  color: var(--nc-seal-dark);
}

.linkage-main {
  min-width: 0;
}

.linkage-main strong {
  color: var(--nc-ink);
  font-size: var(--nc-text-base);
}

.linkage-main p {
  margin-top: 3px;
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
  line-height: 1.6;
}

/* ── 文书输出 ────────────────────────────────────────────────── */

.output-layout {
  display: grid;
  grid-template-columns: 440px minmax(0, 1fr);
  gap: 16px;
  align-items: start;
}

.output-left,
.output-right {
  display: flex;
  flex-direction: column;
  gap: 14px;
  min-width: 0;
}

.template-box {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  padding: 12px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-3);
}

.template-icon {
  flex: none;
  margin-top: 2px;
  color: var(--nc-seal);
  font-size: 26px;
}

.template-box strong {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
}

.template-box p {
  margin-top: 3px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.print-grid {
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  gap: 12px;
}

.printer-state {
  float: right;
  margin-left: 16px;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.output-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 14px;
}

.output-actions :deep(.el-button) {
  flex: 1;
  margin-left: 0;
}

.job-panel {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.job-head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
}

.job-mode {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-xl);
  font-weight: 700;
  letter-spacing: 0.04em;
}

.job-meta {
  display: flex;
  flex-direction: column;
  margin: 0;
  border-top: 1px solid var(--nc-line-faint);
}

.job-meta-row {
  display: flex;
  align-items: baseline;
  gap: 16px;
  padding: 9px 0;
  border-bottom: 1px solid var(--nc-line-faint);
}

.job-meta-row dt {
  flex: none;
  width: 72px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.job-meta-row dd {
  min-width: 0;
  margin: 0;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-base);
  line-height: 1.6;
  word-break: break-all;
}

.job-meta-row dd.job-no {
  color: var(--nc-ink);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
}

.job-desc {
  padding: 10px 12px;
  border-left: 3px solid var(--nc-seal);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-3);
  color: var(--nc-seal-dark);
  font-size: var(--nc-text-base);
  font-weight: 600;
}

.job-error {
  padding: 10px 12px;
  border-left: 3px solid var(--nc-seal);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-seal-soft);
  color: var(--nc-seal-dark);
  font-size: var(--nc-text-sm);
  line-height: 1.6;
  word-break: break-word;
}

.poll-button {
  align-self: flex-start;
}

.output-footer {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  margin-top: 18px;
  padding-top: 14px;
  border-top: 1px solid var(--nc-line-faint);
}

.output-status {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
}

.declaration-no {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
}

.output-footer-actions {
  display: flex;
  gap: 8px;
}

.output-hint {
  margin-top: 12px;
  padding-top: 10px;
  border-top: 1px dashed var(--nc-line-faint);
}

/* ── 底部操作条 ──────────────────────────────────────────────── */

.bottom-bar {
  position: sticky;
  bottom: 0;
  z-index: 10;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  margin-top: 4px;
  padding: 10px 14px calc(10px + env(safe-area-inset-bottom));
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-2);
  box-shadow: 0 -2px 10px rgba(23, 19, 16, 0.07);
}

.bottom-hint {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
  min-width: 0;
}

.bottom-step {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  font-weight: 600;
}

.bottom-mode {
  overflow: hidden;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.bottom-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.bottom-actions :deep(.el-button) {
  margin-left: 0;
}

/* ── 响应式 ──────────────────────────────────────────────────── */

@media (max-width: 1100px) {
  .output-layout {
    grid-template-columns: minmax(0, 1fr);
  }

  .form-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 767px) {
  .step-bar {
    grid-template-columns: repeat(3, minmax(0, 1fr));
    gap: 4px;
    padding: 6px;
  }

  .step-item {
    flex-direction: column;
    gap: 4px;
    padding: 8px 4px;
    font-size: var(--nc-text-sm);
  }

  .step-title {
    max-width: 100%;
    font-size: var(--nc-text-sm);
  }

  .step-mobile-text {
    display: block;
    grid-column: 1 / -1;
    margin-top: 4px;
    color: var(--nc-muted);
    font-size: var(--nc-text-xs);
    text-align: center;
  }

  .form-grid {
    grid-template-columns: minmax(0, 1fr);
  }

  .field--wide {
    grid-column: span 1;
  }

  .search-row {
    flex-direction: column;
  }

  .search-filters {
    flex-direction: column;
    align-items: stretch;
  }

  .search-range {
    min-width: 0;
  }

  .bottom-bar {
    flex-direction: column;
    align-items: stretch;
  }

  .bottom-actions {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .bottom-actions :deep(.el-button) {
    width: 100%;
  }

  .bottom-hint {
    justify-content: space-between;
  }

  .output-footer {
    flex-direction: column;
    align-items: stretch;
  }

  .output-footer-actions :deep(.el-button) {
    flex: 1;
  }

  .output-actions {
    display: grid;
    grid-template-columns: repeat(3, minmax(0, 1fr));
  }

  .identity-row,
  .linkage-item {
    flex-direction: column;
    align-items: stretch;
  }
}
</style>
