<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ArrowLeft, Download, Printer, Refresh, View } from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import {
  deleteMonthlyReport,
  exportMonthlyReport,
  generateMonthlyReport,
  getMonthlyMeetingAttendance,
  getMonthlyReportMeeting,
  getMonthlyReportOptions,
  getMonthlyReportRows,
  getMonthlyReportSummary,
  listMonthlyReportHistory,
  saveMonthlyReportMeeting,
} from '@/api/monthlyReport'
import type {
  MonthlyClassifiedAddRow,
  MonthlyMeeting,
  MonthlyReportExportFormat,
  MonthlyReportHistoryItem,
  MonthlyReportRows,
  MonthlyReportSummary,
} from '@/types/monthlyReport'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'

/* ───────────────────────── 分类与报表定义（与旧版表单清单一致） ───────────────────────── */

const CATEGORY = {
  subsistence: '最低生活保障',
  edge: '最低生活保障边缘家庭',
  destitute: '特困人员',
  rigid: '刚性支出困难家庭',
  other: '其他困难人员',
} as const

type DataSetKey =
  | 'added'
  | 'stopped'
  | 'increase'
  | 'decrease'
  | 'shiBaoReduction'
  | 'classifiedAdds'
  | 'death'
  | 'exitRectification'
  | 'tempRelief'

type FormKind = 'table' | 'classified' | 'meeting'

interface ColumnDef {
  prop: string
  label: string
  minWidth?: number
  /** 金额列：按两位小数展示。 */
  money?: boolean
  formatter?: (value: unknown, row: Record<string, unknown>) => string
}

interface FormDefinition {
  key: string
  label: string
  group: string
  kind: FormKind
  dataset?: DataSetKey
  category: string
  columns?: ColumnDef[]
  /** 新增/停保按农村、城市拆两页（旧版打印同口径）。 */
  splitByRural?: boolean
  showIndex?: boolean
  specialApproval?: boolean
}

const GROUP_ORDER = [
  '新增救助明细',
  '停保汇总表',
  '保障金增发/减发',
  '分类施保',
  '自然减员',
  '临时救助新增',
  '退出对象纠治',
  '会议记录',
] as const

const ADDED_COLUMNS: ColumnDef[] = [
  { prop: 'auditDate', label: '审批时间', minWidth: 110 },
  { prop: 'name', label: '户主姓名', minWidth: 90 },
  { prop: 'idCard', label: '身份证号', minWidth: 170 },
  { prop: 'age', label: '年龄', minWidth: 60 },
  { prop: 'gender', label: '性别', minWidth: 60 },
  { prop: 'address', label: '家庭住址', minWidth: 210 },
  { prop: 'familySize', label: '享受人口', minWidth: 80 },
  { prop: 'classification', label: '享受类别', minWidth: 120 },
]

const ADDED_EDGE_COLUMNS: ColumnDef[] = [
  ...ADDED_COLUMNS,
  { prop: 'maritalStatus', label: '婚姻状况', minWidth: 90 },
  { prop: 'annualIncome', label: '年总收入（元）', minWidth: 120, money: true },
  { prop: 'relation', label: '与户主关系', minWidth: 100 },
  { prop: 'healthStatus', label: '健康状况', minWidth: 90 },
  { prop: 'bankAccount', label: '银行账号', minWidth: 180 },
]

const STOPPED_BASE_COLUMNS: ColumnDef[] = [
  { prop: 'headName', label: '户主姓名', minWidth: 90 },
  { prop: 'headIdCard', label: '身份证号', minWidth: 170 },
  { prop: 'familySize', label: '享受人口', minWidth: 80 },
  { prop: 'address', label: '家庭住址', minWidth: 210 },
  { prop: 'category', label: '享受类别', minWidth: 120 },
]

const STOPPED_DEFAULT_COLUMNS: ColumnDef[] = [
  ...STOPPED_BASE_COLUMNS,
  { prop: 'classification', label: '户分类施保金额', minWidth: 120 },
  { prop: 'monthAmount', label: '保障金额/月', minWidth: 110 },
  { prop: 'stopReason', label: '停止原因', minWidth: 160 },
  { prop: 'stopDate', label: '停止日期', minWidth: 110 },
  { prop: 'enjoyDate', label: '享受日期', minWidth: 110 },
]

const STOPPED_EDGE_COLUMNS: ColumnDef[] = [
  ...STOPPED_BASE_COLUMNS,
  { prop: 'bizType', label: '业务类别', minWidth: 90 },
  { prop: 'stopReason', label: '停止/减员原因', minWidth: 180 },
  { prop: 'stopDate', label: '停止日期', minWidth: 110 },
  { prop: 'enjoyDate', label: '享受日期', minWidth: 110 },
]

const INCREASE_COLUMNS: ColumnDef[] = [
  { prop: 'headName', label: '户主姓名', minWidth: 90 },
  { prop: 'headBirth', label: '出生日期', minWidth: 110 },
  { prop: 'familySize', label: '家庭人口', minWidth: 80 },
  { prop: 'address', label: '家庭住址', minWidth: 210 },
  { prop: 'category', label: '享受类别', minWidth: 120 },
  { prop: 'enjoyDate', label: '享受时间', minWidth: 110 },
  { prop: 'oldAmount', label: '原保障金额', minWidth: 110 },
  { prop: 'newAmount', label: '现保障金额', minWidth: 110 },
  { prop: 'classification', label: '分类施保', minWidth: 110 },
  { prop: 'changeAmount', label: '增发金额', minWidth: 100 },
  { prop: 'reason', label: '增发理由', minWidth: 180 },
]

const DECREASE_COLUMNS: ColumnDef[] = [
  { prop: 'headName', label: '户主姓名', minWidth: 90 },
  { prop: 'headBirth', label: '出生日期', minWidth: 110 },
  { prop: 'oldFamilySize', label: '原家庭人口', minWidth: 100 },
  { prop: 'address', label: '家庭住址', minWidth: 210 },
  { prop: 'category', label: '享受类别', minWidth: 120 },
  { prop: 'enjoyDate', label: '享受时间', minWidth: 110 },
  { prop: 'oldAmount', label: '原保障金额', minWidth: 110 },
  { prop: 'newAmount', label: '现保障金额', minWidth: 110 },
  { prop: 'classifiedSubsidyAmount', label: '现分类施保', minWidth: 110, money: true },
  { prop: 'familySize', label: '现家庭人口', minWidth: 100 },
  { prop: 'decreasePerson', label: '减发人口', minWidth: 90 },
  { prop: 'reason', label: '减发原因', minWidth: 180 },
  { prop: 'changeAmount', label: '减发金额', minWidth: 100 },
]

const SHIBAO_COLUMNS: ColumnDef[] = [
  { prop: 'headName', label: '户主姓名', minWidth: 90 },
  { prop: 'address', label: '家庭住址', minWidth: 210 },
  { prop: 'category', label: '享受类别', minWidth: 120 },
  { prop: 'enjoyDate', label: '享受日期', minWidth: 110 },
  { prop: 'memberName', label: '减发成员', minWidth: 90 },
  { prop: 'decreaseAmount', label: '减发金额', minWidth: 100 },
  { prop: 'originalAmount', label: '原享受低保金', minWidth: 110 },
  { prop: 'currentAmount', label: '现享受低保金', minWidth: 110 },
  { prop: 'originalClassified', label: '原享受分类施保', minWidth: 130 },
  { prop: 'currentClassified', label: '现享受分类施保', minWidth: 130 },
  { prop: 'reason', label: '减发理由', minWidth: 180 },
]

const DEATH_COLUMNS: ColumnDef[] = [
  { prop: 'headName', label: '户主姓名', minWidth: 90 },
  { prop: 'headIdCard', label: '身份证号', minWidth: 170 },
  { prop: 'address', label: '家庭住址', minWidth: 210 },
  { prop: 'deceasedName', label: '死亡人员', minWidth: 90 },
  { prop: 'deceasedDate', label: '死亡日期', minWidth: 110 },
  { prop: 'enjoyDate', label: '享受时间', minWidth: 110 },
]

const EXIT_COLUMNS: ColumnDef[] = [
  { prop: 'name', label: '姓名', minWidth: 90 },
  { prop: 'idCard', label: '身份证号', minWidth: 170 },
  { prop: 'category', label: '类别', minWidth: 120 },
  { prop: 'familySize', label: '家庭人口', minWidth: 80 },
  { prop: 'stopMonth', label: '停止月份', minWidth: 100 },
  { prop: 'stopReason', label: '退保主要原因', minWidth: 170 },
  { prop: 'gracePeriod', label: '给予渐退期', minWidth: 90 },
  { prop: 'intoDibao', label: '纳入低保', minWidth: 84 },
  { prop: 'intoTeku', label: '纳入特困', minWidth: 84 },
  { prop: 'intoEdge', label: '纳入低保边缘家庭', minWidth: 130 },
  { prop: 'intoRigid', label: '纳入刚性支出困难家庭', minWidth: 150 },
  { prop: 'directExit', label: '直接退出', minWidth: 84 },
]

const TEMP_RELIEF_COLUMNS: ColumnDef[] = [
  {
    prop: 'reliefType',
    label: '救助类型',
    minWidth: 130,
    formatter: (value) =>
      value === 'Small' ? '小额快速救助（备用金发放）' : '大额救助（报县局审批）',
  },
  { prop: 'name', label: '姓名', minWidth: 90 },
  { prop: 'idCard', label: '身份证号', minWidth: 170 },
  { prop: 'age', label: '年龄', minWidth: 60 },
  { prop: 'gender', label: '性别', minWidth: 60 },
  { prop: 'familySize', label: '家庭人口', minWidth: 80 },
  { prop: 'address', label: '家庭住址', minWidth: 210 },
  { prop: 'category', label: '个人类别', minWidth: 110 },
  { prop: 'phone', label: '联系方式', minWidth: 120 },
  { prop: 'amount', label: '救助金额', minWidth: 100 },
  { prop: 'auditDate', label: '审批时间', minWidth: 110 },
  { prop: 'reason', label: '申请理由', minWidth: 200 },
  { prop: 'selfPay', label: '自付金额合计', minWidth: 110 },
  { prop: 'bankAccount', label: '一卡通账号', minWidth: 180 },
]

const CLASSIFIED_MEMBER_COLUMNS: ColumnDef[] = [
  { prop: 'name', label: '姓名', minWidth: 90 },
  { prop: 'gender', label: '性别', minWidth: 60 },
  { prop: 'birthDate', label: '出生日期', minWidth: 120 },
  { prop: 'relation', label: '与户主关系', minWidth: 100 },
  { prop: 'health', label: '健康状况', minWidth: 90 },
  { prop: 'annualIncome', label: '年收入（元）', minWidth: 110, money: true },
]

function addedForm(category: string): FormDefinition {
  return {
    key: `新增救助明细_${category}`,
    label: `新增救助明细（${category}）`,
    group: '新增救助明细',
    kind: 'table',
    dataset: 'added',
    category,
    splitByRural: true,
    columns: category === CATEGORY.edge ? ADDED_EDGE_COLUMNS : ADDED_COLUMNS,
  }
}

function stoppedForm(category: string): FormDefinition {
  const useEdge = category === CATEGORY.edge || category === CATEGORY.other
  return {
    key: `停保汇总表_${category}`,
    label: `停保汇总表（${category}）`,
    group: '停保汇总表',
    kind: 'table',
    dataset: 'stopped',
    category,
    splitByRural: true,
    columns: useEdge ? STOPPED_EDGE_COLUMNS : STOPPED_DEFAULT_COLUMNS,
  }
}

function deathForm(category: string | null): FormDefinition {
  return {
    key: category ? `自然减员表_${category}` : '自然减员表',
    label: `人员变动_自然减员月报表（${category ?? '全部'}）`,
    group: '自然减员',
    kind: 'table',
    dataset: 'death',
    category: category ?? '',
    columns: DEATH_COLUMNS,
  }
}

function exitForm(category: string | null): FormDefinition {
  return {
    key: category ? `档案_退出对象兜底情况纠治表_${category}` : '档案_退出对象兜底情况纠治表',
    label: `退出对象兜底情况纠治表（${category ?? '全部'}）`,
    group: '退出对象纠治',
    kind: 'table',
    dataset: 'exitRectification',
    category: category ?? '',
    showIndex: true,
    columns: EXIT_COLUMNS,
  }
}

const formDefinitions: FormDefinition[] = [
  addedForm(CATEGORY.subsistence),
  addedForm(CATEGORY.edge),
  addedForm(CATEGORY.destitute),
  addedForm(CATEGORY.rigid),

  stoppedForm(CATEGORY.subsistence),
  stoppedForm(CATEGORY.edge),
  stoppedForm(CATEGORY.destitute),
  stoppedForm(CATEGORY.rigid),
  stoppedForm(CATEGORY.other),

  {
    key: `保障金增发表_${CATEGORY.subsistence}`,
    label: `保障金增发表（${CATEGORY.subsistence}）`,
    group: '保障金增发/减发',
    kind: 'table',
    dataset: 'increase',
    category: CATEGORY.subsistence,
    columns: INCREASE_COLUMNS,
  },
  {
    key: `保障金减发表_${CATEGORY.subsistence}`,
    label: `保障金减发表（${CATEGORY.subsistence}）`,
    group: '保障金增发/减发',
    kind: 'table',
    dataset: 'decrease',
    category: CATEGORY.subsistence,
    columns: DECREASE_COLUMNS,
  },

  {
    key: '施保金减发',
    label: `分类施保金减发人员表（${CATEGORY.subsistence}）`,
    group: '分类施保',
    kind: 'table',
    dataset: 'shiBaoReduction',
    category: CATEGORY.subsistence,
    columns: SHIBAO_COLUMNS,
  },
  {
    key: '分类施保增加',
    label: `分类施保金增发人员表（${CATEGORY.subsistence}）`,
    group: '分类施保',
    kind: 'classified',
    dataset: 'classifiedAdds',
    category: CATEGORY.subsistence,
    columns: CLASSIFIED_MEMBER_COLUMNS,
  },

  deathForm(null),
  deathForm(CATEGORY.subsistence),
  deathForm(CATEGORY.edge),
  deathForm(CATEGORY.destitute),
  deathForm(CATEGORY.rigid),

  {
    key: '临时救助新增汇总表',
    label: '临时救助新增汇总表',
    group: '临时救助新增',
    kind: 'table',
    dataset: 'tempRelief',
    category: '',
    showIndex: true,
    columns: TEMP_RELIEF_COLUMNS,
  },

  exitForm(null),
  exitForm(CATEGORY.subsistence),
  exitForm(CATEGORY.edge),
  exitForm(CATEGORY.destitute),
  exitForm(CATEGORY.rigid),

  {
    key: '会议记录',
    label: '会议记录',
    group: '会议记录',
    kind: 'meeting',
    category: '',
  },
  {
    key: '会议记录_一事一议',
    label: '会议记录（一事一议）',
    group: '会议记录',
    kind: 'meeting',
    category: '',
    specialApproval: true,
  },
]

const groupedForms = computed(() =>
  GROUP_ORDER.map((group) => ({
    group,
    items: formDefinitions.filter((form) => form.group === group),
  })).filter((entry) => entry.items.length > 0),
)

/* ───────────────────────── 基础状态 ───────────────────────── */

const router = useRouter()
const isNarrow = useMediaQuery('(max-width: 960px)')

const today = new Date()
const selectedYear = ref(today.getFullYear())
const selectedMonth = ref(today.getMonth() + 1)
const selectedTown = ref('全部')

const years = ref<number[]>(Array.from({ length: 7 }, (_, index) => today.getFullYear() - 5 + index))
const months = Array.from({ length: 12 }, (_, index) => index + 1)
const towns = ref<string[]>([])

const optionsLoading = ref(false)
const optionsError = ref('')
const cycleText = ref('')

const summary = ref<MonthlyReportSummary | null>(null)
const summaryLoading = ref(false)
const summaryError = ref('')

const selectedKeys = ref<string[]>([])
const selectedForms = computed(() =>
  formDefinitions.filter((form) => selectedKeys.value.includes(form.key)),
)
const hasSelection = computed(() => selectedKeys.value.length > 0)

const townParam = computed(() =>
  selectedTown.value === '全部' ? undefined : selectedTown.value,
)

/* ───────────────────────── 预览数据 ───────────────────────── */

interface PreviewTable {
  title: string
  rows: Record<string, unknown>[]
}

interface PreviewSection {
  key: string
  title: string
  kind: FormKind
  columns: ColumnDef[]
  showIndex: boolean
  error: string
  empty: boolean
  emptyText: string
  tables: PreviewTable[]
  households: MonthlyClassifiedAddRow[]
  meeting: MonthlyMeeting | null
  special: boolean
}

const rowsCache = reactive<Record<string, MonthlyReportRows>>({})
const rowsErrors = reactive<Record<string, string>>({})
const meetingCache = reactive<Record<'normal' | 'special', MonthlyMeeting | null>>({
  normal: null,
  special: null,
})
const meetingErrors = reactive<Record<'normal' | 'special', string>>({
  normal: '',
  special: '',
})

const previewSections = ref<PreviewSection[]>([])
const previewLoading = ref(false)
const previewError = ref('')
const previewGeneratedAt = ref<Date | null>(null)

const previewTimeText = computed(() =>
  previewGeneratedAt.value
    ? `生成于 ${formatDateTime(previewGeneratedAt.value.toISOString())}`
    : '',
)

let summaryTicket = 0

function resetDataCaches(): void {
  for (const key of Object.keys(rowsCache)) delete rowsCache[key]
  for (const key of Object.keys(rowsErrors)) delete rowsErrors[key]
  meetingCache.normal = null
  meetingCache.special = null
  meetingErrors.normal = ''
  meetingErrors.special = ''
}

async function loadFormData(defs: FormDefinition[]): Promise<void> {
  resetDataCaches()

  const tasks: Promise<void>[] = []
  const datasetsByCategory = new Map<string, Set<string>>()
  for (const def of defs) {
    if (def.kind === 'meeting' || !def.dataset) continue
    const set = datasetsByCategory.get(def.category) ?? new Set<string>()
    set.add(def.dataset)
    datasetsByCategory.set(def.category, set)
  }

  for (const [category, datasetSet] of datasetsByCategory) {
    tasks.push(
      (async () => {
        try {
          rowsCache[category] = await getMonthlyReportRows({
            year: selectedYear.value,
            month: selectedMonth.value,
            town: townParam.value,
            category,
            datasets: Array.from(datasetSet),
          })
        } catch (error) {
          rowsErrors[category] = getErrorMessage(
            error,
            `「${category || '全部'}」报表数据加载失败`,
          )
        }
      })(),
    )
  }

  const flags = new Set<'normal' | 'special'>()
  for (const def of defs) {
    if (def.kind === 'meeting') flags.add(def.specialApproval ? 'special' : 'normal')
  }

  for (const flag of flags) {
    tasks.push(
      (async () => {
        try {
          meetingCache[flag] = await getMonthlyReportMeeting({
            year: selectedYear.value,
            month: selectedMonth.value,
            town: townParam.value,
            specialApproval: flag === 'special',
          })
        } catch (error) {
          meetingErrors[flag] = getErrorMessage(error, '会议记录加载失败')
        }
      })(),
    )
  }

  await Promise.all(tasks)
}

function buildSection(def: FormDefinition): PreviewSection {
  const section: PreviewSection = {
    key: def.key,
    title: def.label,
    kind: def.kind,
    columns: def.columns ?? [],
    showIndex: def.showIndex === true,
    error: '',
    empty: false,
    emptyText: '',
    tables: [],
    households: [],
    meeting: null,
    special: def.specialApproval === true,
  }

  if (def.kind === 'meeting') {
    const flag: 'normal' | 'special' = def.specialApproval ? 'special' : 'normal'
    if (meetingErrors[flag]) {
      section.error = meetingErrors[flag]
      return section
    }
    const meeting = meetingCache[flag]
    if (!meeting) {
      section.empty = true
      section.emptyText = '会议记录未加载'
      return section
    }
    section.meeting = meeting
    if (def.specialApproval && !(meeting.memberInfo ?? '').trim()) {
      section.empty = true
      section.emptyText = '本期无一事一议新增人员，不生成一事一议会议记录'
    }
    return section
  }

  if (rowsErrors[def.category]) {
    section.error = rowsErrors[def.category]
    return section
  }

  const data = rowsCache[def.category]
  if (!data) {
    section.empty = true
    section.emptyText = '数据未加载'
    return section
  }

  if (def.kind === 'classified') {
    section.households = data.classifiedAdds ?? []
    section.empty = section.households.length === 0
    if (section.empty) section.emptyText = `本期无${def.label}数据`
    return section
  }

  const dataset = def.dataset
  const rows = (dataset ? data[dataset] : []) as unknown as Record<string, unknown>[]

  if (dataset === 'tempRelief') {
    // 旧版输出顺序：报县局审批（大额）在前，备用金发放（小额）在后。
    rows.sort((left, right) => {
      const leftSmall = left.reliefType === 'Small' ? 1 : 0
      const rightSmall = right.reliefType === 'Small' ? 1 : 0
      return leftSmall - rightSmall
    })
  }

  if (def.splitByRural) {
    const rural = rows.filter((row) => row.isRural === true)
    const urban = rows.filter((row) => row.isRural !== true)
    if (rural.length > 0) section.tables.push({ title: '农村', rows: rural })
    if (urban.length > 0) section.tables.push({ title: '城市', rows: urban })
  } else if (rows.length > 0) {
    section.tables.push({ title: '', rows })
  }

  section.empty = section.tables.length === 0
  if (section.empty) {
    const period = cycleText.value ? `（${cycleText.value}）` : ''
    section.emptyText = `本期无${def.label}数据${period}`
  }
  return section
}

/* ───────────────────────── 加载：选项 / 汇总 / 会议 / 历史 ───────────────────────── */

async function loadOptions(): Promise<void> {
  optionsLoading.value = true
  optionsError.value = ''
  try {
    const options = await getMonthlyReportOptions()
    if (options.years?.length) years.value = options.years
    const optionTowns = (options.towns ?? []).filter((item) => item.trim().length > 0)
    towns.value = ['全部', ...optionTowns.filter((item) => item !== '全部')]
    if (!towns.value.includes(selectedTown.value)) selectedTown.value = '全部'
    cycleText.value = options.currentCycle?.display ?? ''
  } catch (error) {
    optionsError.value = getErrorMessage(error, '月报筛选项加载失败')
    towns.value = ['全部']
    selectedTown.value = '全部'
  } finally {
    optionsLoading.value = false
  }
}

async function loadSummary(): Promise<void> {
  const ticket = ++summaryTicket
  summaryLoading.value = true
  summaryError.value = ''
  try {
    const result = await getMonthlyReportSummary({
      year: selectedYear.value,
      month: selectedMonth.value,
      town: townParam.value,
    })
    if (ticket !== summaryTicket) return
    summary.value = result
    cycleText.value = result.cycle?.display ?? cycleText.value
  } catch (error) {
    if (ticket !== summaryTicket) return
    // 失败保留上次成功值并显式提示，避免被误读为「本期无人」。
    summaryError.value = getErrorMessage(error, '月报汇总加载失败')
  } finally {
    if (ticket === summaryTicket) summaryLoading.value = false
  }
}

const meetingForm = reactive({
  id: 0,
  meetingTime: '',
  host: '',
  recorder: '',
  attendees: '',
  absentees: '',
  applyCategory: '',
  memberInfo: '',
  memberNames: '',
  createdAt: '',
})

const meetingLoading = ref(false)
const meetingSaving = ref(false)
const meetingError = ref('')
let meetingTicket = 0

function applyMeeting(meeting: MonthlyMeeting | null): void {
  meetingForm.id = meeting?.id ?? 0
  meetingForm.meetingTime = meeting?.meetingTime ?? ''
  meetingForm.host = meeting?.host ?? ''
  meetingForm.recorder = meeting?.recorder ?? ''
  meetingForm.attendees = meeting?.attendees ?? ''
  meetingForm.absentees = meeting?.absentees ?? ''
  meetingForm.applyCategory = meeting?.applyCategory ?? ''
  meetingForm.memberInfo = meeting?.memberInfo ?? ''
  meetingForm.memberNames = meeting?.memberNames ?? ''
  meetingForm.createdAt = meeting?.createdAt ?? ''
}

async function loadMeeting(): Promise<void> {
  const ticket = ++meetingTicket
  meetingLoading.value = true
  meetingError.value = ''
  try {
    const meeting = await getMonthlyReportMeeting({
      year: selectedYear.value,
      month: selectedMonth.value,
      town: townParam.value,
    })
    if (ticket !== meetingTicket) return
    applyMeeting(meeting)
  } catch (error) {
    if (ticket !== meetingTicket) return
    meetingError.value = getErrorMessage(error, '会议记录加载失败')
  } finally {
    if (ticket === meetingTicket) meetingLoading.value = false
  }
}

const historyYear = ref(0)
const historyItems = ref<MonthlyReportHistoryItem[]>([])
const historyLoading = ref(false)
const historyError = ref('')
const historyPage = ref(1)
const historyPageSize = 5
let historyTicket = 0

const historyTotalPages = computed(() =>
  Math.max(1, Math.ceil(historyItems.value.length / historyPageSize)),
)
const historyPaged = computed(() => {
  const start = (historyPage.value - 1) * historyPageSize
  return historyItems.value.slice(start, start + historyPageSize)
})

async function loadHistory(): Promise<void> {
  const ticket = ++historyTicket
  historyLoading.value = true
  historyError.value = ''
  try {
    const items = await listMonthlyReportHistory(
      historyYear.value > 0 ? historyYear.value : null,
    )
    if (ticket !== historyTicket) return
    historyItems.value = items
    if (historyPage.value > historyTotalPages.value) historyPage.value = historyTotalPages.value
  } catch (error) {
    if (ticket !== historyTicket) return
    historyItems.value = []
    historyPage.value = 1
    historyError.value = getErrorMessage(error, '月报生成历史加载失败')
  } finally {
    if (ticket === historyTicket) historyLoading.value = false
  }
}

function handleHistorySearch(): void {
  historyPage.value = 1
  void loadHistory()
}

function handleHistoryPrev(): void {
  if (historyPage.value > 1) {
    historyPage.value -= 1
  }
}

function handleHistoryNext(): void {
  if (historyPage.value < historyTotalPages.value) {
    historyPage.value += 1
  }
}

function handleHistoryYearChange(): void {
  handleHistorySearch()
}

/* ───────────────────────── 筛选联动 ───────────────────────── */

async function handleFilterChange(): Promise<void> {
  previewSections.value = []
  previewError.value = ''
  previewGeneratedAt.value = null
  resetDataCaches()
  await Promise.all([loadSummary(), loadMeeting()])
}

watch([selectedYear, selectedMonth, selectedTown], () => {
  void handleFilterChange()
})

async function handleRefresh(): Promise<void> {
  await loadSummary()
  await loadMeeting()
  await loadHistory()
}

/* ───────────────────────── 表单勾选 ───────────────────────── */

function handleSelectAll(): void {
  selectedKeys.value = formDefinitions.map((form) => form.key)
}

function handleClearSelection(): void {
  selectedKeys.value = []
}

/* ───────────────────────── 预览 ───────────────────────── */

const skipAttendancePrompt = ref(false)
const pendingAfterAttendance = ref<(() => Promise<void>) | null>(null)

async function runPreview(): Promise<void> {
  const defs = [...selectedForms.value]
  if (defs.length === 0) return

  previewLoading.value = true
  previewError.value = ''
  try {
    await loadFormData(defs)
    previewSections.value = defs.map(buildSection)
    previewGeneratedAt.value = new Date()
  } catch (error) {
    previewSections.value = []
    previewError.value = getErrorMessage(error, '报表预览生成失败')
  } finally {
    previewLoading.value = false
  }
}

async function handlePreview(): Promise<void> {
  if (!hasSelection.value) {
    ElMessage.warning('请先勾选要预览的报表表单')
    return
  }

  const proceed = await ensureAttendanceChoice()
  if (!proceed) {
    if (attendanceVisible.value) pendingAfterAttendance.value = () => runPreview()
    return
  }
  await runPreview()
}

/* ───────────────────────── 会议出席/缺席弹窗 ───────────────────────── */

const attendanceVisible = ref(false)
const attendanceLoading = ref(false)
const attendanceSaving = ref(false)
const attendanceError = ref('')
const attendanceHistoryText = ref('')
const popupAttendees = ref('')
const popupAbsentees = ref('')

const attendanceActionLabel = computed(() =>
  pendingAfterAttendance.value ? '保存并继续' : '保存',
)

function normalizeLineBreaks(text: string | null | undefined): string {
  return (text ?? '')
    .replace(/\r\n/g, '\n')
    .replace(/\r/g, '\n')
    .trim()
}

async function openAttendanceDialog(): Promise<void> {
  attendanceVisible.value = true
  attendanceLoading.value = true
  attendanceError.value = ''
  attendanceHistoryText.value = ''
  popupAttendees.value = ''
  popupAbsentees.value = ''

  try {
    const latest = await getMonthlyMeetingAttendance({
      year: selectedYear.value,
      month: selectedMonth.value,
      town: townParam.value,
    })

    if (latest.id > 0 && latest.createdAt) {
      attendanceHistoryText.value = `最近保存：${formatDateTime(latest.createdAt)}（${latest.createdBy || '—'}）`
    }

    popupAttendees.value =
      normalizeLineBreaks(latest.attendees) || normalizeLineBreaks(meetingForm.attendees)
    popupAbsentees.value =
      normalizeLineBreaks(latest.absentees) || normalizeLineBreaks(meetingForm.absentees)
  } catch (error) {
    attendanceError.value = getErrorMessage(error, '出席/缺席历史加载失败')
    popupAttendees.value = normalizeLineBreaks(meetingForm.attendees)
    popupAbsentees.value = normalizeLineBreaks(meetingForm.absentees)
  } finally {
    attendanceLoading.value = false
  }
}

async function confirmAttendance(): Promise<void> {
  attendanceSaving.value = true
  attendanceError.value = ''
  try {
    await saveMonthlyReportMeeting({
      year: selectedYear.value,
      month: selectedMonth.value,
      town: townParam.value,
      meetingTime: meetingForm.meetingTime,
      host: meetingForm.host,
      recorder: meetingForm.recorder,
      attendees: normalizeLineBreaks(popupAttendees.value),
      absentees: normalizeLineBreaks(popupAbsentees.value),
      applyCategory: meetingForm.applyCategory,
    })
    attendanceVisible.value = false
    await loadMeeting()
    ElMessage.success('会议记录与出席/缺席人员已保存')

    const action = pendingAfterAttendance.value
    pendingAfterAttendance.value = null
    if (action) {
      skipAttendancePrompt.value = true
      try {
        await action()
      } finally {
        skipAttendancePrompt.value = false
      }
    }
  } catch (error) {
    attendanceError.value = getErrorMessage(error, '会议记录保存失败')
  } finally {
    attendanceSaving.value = false
  }
}

function cancelAttendance(): void {
  attendanceVisible.value = false
  pendingAfterAttendance.value = null
}

/** 用户用右上角关闭弹窗时视为中止原操作（与旧版“取消”一致）。 */
function handleAttendanceClosed(): void {
  if (!attendanceSaving.value) pendingAfterAttendance.value = null
}

/**
 * 旧版流程：勾选「会议记录」时先询问是否重新录入出席/缺席；
 * 返回 false 表示流程中止（弹窗已打开或用户关闭了询问框）。
 */
async function ensureAttendanceChoice(): Promise<boolean> {
  if (skipAttendancePrompt.value) return true
  if (!selectedForms.value.some((form) => form.kind === 'meeting')) return true

  try {
    await ElMessageBox.confirm(
      '本次操作包含「会议记录」，是否重新录入本期出席/缺席人员？\n（不录入将按已保存的内容生成）',
      '会议出席/缺席',
      {
        confirmButtonText: '重新录入',
        cancelButtonText: '直接生成',
        distinguishCancelAndClose: true,
        type: 'info',
      },
    )
  } catch (action) {
    return action === 'cancel'
  }

  await openAttendanceDialog()
  return false
}

/* ───────────────────────── 会议记录保存 ───────────────────────── */

async function handleSaveMeeting(): Promise<void> {
  meetingSaving.value = true
  meetingError.value = ''
  try {
    await saveMonthlyReportMeeting({
      year: selectedYear.value,
      month: selectedMonth.value,
      town: townParam.value,
      meetingTime: meetingForm.meetingTime,
      host: meetingForm.host,
      recorder: meetingForm.recorder,
      attendees: meetingForm.attendees,
      absentees: meetingForm.absentees,
      applyCategory: meetingForm.applyCategory,
    })
    await loadMeeting()
    ElMessage.success('会议记录已保存')
  } catch (error) {
    meetingError.value = getErrorMessage(error, '会议记录保存失败')
  } finally {
    meetingSaving.value = false
  }
}

/* ───────────────────────── 生成 / 删除 / 导出 ───────────────────────── */

const generating = ref(false)
const exporting = ref<'Excel' | 'Csv' | ''>('')
const exportingCsv = ref(false)
const copies = ref(1)
const pcDialogVisible = ref(false)

async function handleGenerate(overwrite: boolean): Promise<void> {
  if (overwrite) {
    try {
      await ElMessageBox.confirm(
        `覆盖生成会先删除 ${selectedYear.value}年${selectedMonth.value}月 的全部乡镇月报记录，再按当前数据重新统计。\n\n确定继续吗？`,
        '覆盖生成',
        { type: 'warning', confirmButtonText: '覆盖生成', cancelButtonText: '取消' },
      )
    } catch {
      return
    }
  }

  generating.value = true
  try {
    const result = await generateMonthlyReport({
      year: selectedYear.value,
      month: selectedMonth.value,
      town: townParam.value,
      overwrite,
    })
    ElMessage.success(
      `${result.year}年${result.month}月月报已生成（在保 ${result.totalArchives} 户，新增 ${result.newArchives}，停保 ${result.stoppedArchives}）`,
    )
    await Promise.all([loadSummary(), loadHistory()])
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '生成月报失败'))
  } finally {
    generating.value = false
  }
}

async function handleDeleteHistory(item: MonthlyReportHistoryItem): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `确定删除 ${item.year}年${item.month}月 的月报汇总记录吗？<br/><br/>` +
        `⚠ 将删除该年月【全部乡镇】的记录（含：全部 / ${escapeHtml(item.town)}）。<br/>` +
        '删除后可重新生成，不影响业务明细数据。',
      '删除报表记录',
      {
        type: 'warning',
        dangerouslyUseHTMLString: true,
        confirmButtonText: '删除',
        cancelButtonText: '取消',
      },
    )
  } catch {
    return
  }

  try {
    const result = await deleteMonthlyReport(item.year, item.month)
    ElMessage.success(result.deleted ? '已删除' : '该年月已无月报记录')
    await Promise.all([loadHistory(), loadSummary()])
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '删除月报记录失败'))
  }
}

async function handleExport(format: MonthlyReportExportFormat): Promise<void> {
  const current = summary.value
  if (
    !current ||
    current.year !== selectedYear.value ||
    current.month !== selectedMonth.value ||
    !current.generated
  ) {
    ElMessage.warning('该年月尚未生成月报，请先「生成月报」后再导出汇总')
    return
  }

  exporting.value = format
  try {
    const result = await exportMonthlyReport(selectedYear.value, selectedMonth.value, format)
    ElMessage.success(`已导出：${result.fileName}`)
  } catch (error) {
    ElMessage.error(getErrorMessage(error, `导出 ${format === 'Excel' ? 'Excel' : 'CSV'} 失败`))
  } finally {
    exporting.value = ''
  }
}

function openPcPrintDialog(): void {
  pcDialogVisible.value = true
}

/* ───────────────────────── 选中表单数据导出（CSV） ───────────────────────── */

async function handleExportCsv(all: boolean): Promise<void> {
  const defs = all ? [...formDefinitions] : [...selectedForms.value]
  if (defs.length === 0) {
    ElMessage.warning('请先勾选要导出的报表表单')
    return
  }

  exportingCsv.value = true
  try {
    await loadFormData(defs)

    const errors = [
      ...Object.entries(rowsErrors)
        .filter(([, message]) => Boolean(message))
        .map(([category, message]) => `${category || '全部'}：${message}`),
      ...(meetingErrors.normal ? [`会议记录：${meetingErrors.normal}`] : []),
      ...(meetingErrors.special ? [`会议记录（一事一议）：${meetingErrors.special}`] : []),
    ]
    if (errors.length > 0) {
      ElMessage.error(`部分表单数据加载失败，已取消导出：${errors.join('；')}`)
      return
    }

    const csv = buildCsv(defs)
    const suffix = all ? '全部表单' : '选中表单'
    downloadCsv(
      csv,
      `救助月报表_${selectedYear.value}-${pad(selectedMonth.value)}_${suffix}.csv`,
    )
    ElMessage.success(`已导出${suffix}数据（CSV）`)
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '导出表单数据失败'))
  } finally {
    exportingCsv.value = false
  }
}

function buildCsv(defs: FormDefinition[]): string {
  const blocks: string[] = []

  for (const def of defs) {
    blocks.push(csvCell(def.label))

    if (def.kind === 'meeting') {
      const meeting = meetingCache[def.specialApproval ? 'special' : 'normal']
      if (!meeting) {
        blocks.push(csvCell('（未加载）'))
        blocks.push('')
        continue
      }
      blocks.push(['项目', '内容'].map(csvCell).join(','))
      for (const item of meetingParagraphs(meeting)) {
        blocks.push([item.label, item.value].map(csvCell).join(','))
      }
      blocks.push('')
      continue
    }

    if (def.kind === 'classified') {
      const households = rowsCache[def.category]?.classifiedAdds ?? []
      blocks.push(
        [
          '户主姓名',
          '性别',
          '出生日期',
          '民族',
          '健康状况',
          '救助类别',
          '家庭人口',
          '联系电话',
          '家庭住址',
          '身份证号',
          '分类施保类型',
          '符合人数',
          '加发金额',
          '家庭成员',
        ]
          .map(csvCell)
          .join(','),
      )
      if (households.length === 0) {
        blocks.push(csvCell('本期无数据'))
      } else {
        for (const household of households) {
          const members = (household.members ?? [])
            .map((member) => `${member.name}（${member.relation || '—'}）`)
            .join('；')
          blocks.push(
            [
              household.applicantName,
              household.applicantGender,
              household.applicantBirthDate,
              household.nationality,
              household.healthStatus,
              household.classification,
              household.familySize,
              household.phone,
              household.address,
              household.applicantIdCard,
              household.classifiedType,
              household.count,
              household.amount,
              members,
            ]
              .map(csvCell)
              .join(','),
          )
        }
      }
      blocks.push('')
      continue
    }

    const dataset = def.dataset
    const rows = (dataset && rowsCache[def.category]
      ? rowsCache[def.category][dataset]
      : []) as unknown as Record<string, unknown>[]

    const header: string[] = []
    if (def.splitByRural) header.push('城乡')
    if (def.showIndex) header.push('序号')
    header.push(...(def.columns ?? []).map((column) => column.label))
    blocks.push(header.map(csvCell).join(','))

    if (rows.length === 0) {
      blocks.push(csvCell('本期无数据'))
    } else {
      rows.forEach((row, index) => {
        const cells: unknown[] = []
        if (def.splitByRural) cells.push(row.isRural === true ? '农村' : '城市')
        if (def.showIndex) cells.push(index + 1)
        for (const column of def.columns ?? []) {
          cells.push(formatCell(row, column))
        }
        blocks.push(cells.map(csvCell).join(','))
      })
    }
    blocks.push('')
  }

  return blocks.join('\r\n')
}

function csvCell(value: unknown): string {
  const text = value === null || value === undefined ? '' : String(value)
  return /[",\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text
}

function downloadCsv(content: string, fileName: string): void {
  const blob = new Blob(['\ufeff', content], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = fileName
  anchor.style.display = 'none'
  document.body.appendChild(anchor)
  anchor.click()
  anchor.remove()
  window.setTimeout(() => URL.revokeObjectURL(url), 1000)
}

/* ───────────────────────── 展示辅助 ───────────────────────── */

function pad(value: number): string {
  return String(value).padStart(2, '0')
}

function formatCell(row: Record<string, unknown>, column: ColumnDef): string {
  const value = row[column.prop]
  if (column.formatter) return column.formatter(value, row)
  if (value === null || value === undefined) return '—'
  if (typeof value === 'number') {
    return column.money
      ? value.toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
      : String(value)
  }
  if (typeof value === 'boolean') return value ? '是' : '否'
  const text = String(value).trim()
  return text.length > 0 ? text : '—'
}

function formatDateTime(value: string | null | undefined): string {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return '—'
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}`
}

function formatMoney(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) return '—'
  return value.toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

function escapeHtml(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
}

interface MeetingParagraph {
  label: string
  value: string
}

function meetingParagraphs(meeting: MonthlyMeeting): MeetingParagraph[] {
  return [
    { label: '会议时间', value: meeting.meetingTime || '—' },
    { label: '主持人', value: meeting.host || '—' },
    { label: '记录人', value: meeting.recorder || '—' },
    { label: '出席人员', value: meeting.attendees || '—' },
    { label: '缺席人员', value: meeting.absentees || '—' },
    { label: '申请类别', value: meeting.applyCategory || '—' },
    { label: '新增成员信息', value: meeting.memberInfo || '—' },
    { label: '新增成员名单', value: meeting.memberNames || '—' },
  ]
}

function sectionExtraText(section: PreviewSection): string {
  if (section.kind === 'meeting') {
    return section.special ? '一事一议' : '普通会议'
  }
  if (section.kind === 'classified') {
    return `${section.households.length} 户`
  }
  return `${section.tables.reduce((total, table) => total + table.rows.length, 0)} 条`
}

const isEmptySummary = computed(
  () =>
    summary.value !== null &&
    summary.value.addedCount === 0 &&
    summary.value.stoppedCount === 0 &&
    summary.value.increaseCount === 0 &&
    summary.value.decreaseCount === 0 &&
    summary.value.deathCount === 0,
)

/** 仅当汇总与当前筛选年月一致时视为“已生成”，避免切换筛选瞬间误判。 */
const currentGenerated = computed(() => {
  const current = summary.value
  if (!current || current.year !== selectedYear.value || current.month !== selectedMonth.value) {
    return null
  }
  return current.generated
})

/* ───────────────────────── 初始化 ───────────────────────── */

onMounted(async () => {
  await loadOptions()
  await Promise.all([loadSummary(), loadMeeting(), loadHistory()])
})
</script>

<template>
  <div class="nc-page monthly-report-page">
    <NcPageHeader
      title="救助月报表"
      description="按月汇总社会救助业务数据，生成救助月报与各类报表表单，导出汇总，并维护会议记录。"
      kicker="低收入人口救助帮扶 · 救助月报表"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="router.push('/social-assistance')">
          返回工作台
        </el-button>
        <el-button :icon="Refresh" :loading="summaryLoading || meetingLoading" @click="handleRefresh">
          刷新数据
        </el-button>
      </template>
    </NcPageHeader>

    <el-alert
      v-if="optionsError"
      class="page-alert"
      type="error"
      :title="`筛选项加载失败：${optionsError}`"
      :closable="false"
      show-icon
    >
      <el-button link type="primary" @click="loadOptions()">重新加载</el-button>
    </el-alert>

    <div class="report-layout">
      <!-- 左列：筛选 / 汇总 / 表单 / 会议 / 历史 -->
      <div class="report-left">
        <NcCard title="统计周期">
          <div class="cycle-fields">
            <div class="cycle-field">
              <span class="field-label">年份</span>
              <el-select
                v-model="selectedYear"
                class="cycle-select"
                aria-label="选择年份"
                :loading="optionsLoading"
              >
                <el-option
                  v-for="year in years"
                  :key="year"
                  :label="`${year} 年`"
                  :value="year"
                />
              </el-select>
            </div>
            <div class="cycle-field">
              <span class="field-label">月份</span>
              <el-select v-model="selectedMonth" class="cycle-select" aria-label="选择月份">
                <el-option
                  v-for="month in months"
                  :key="month"
                  :label="`${month} 月`"
                  :value="month"
                />
              </el-select>
            </div>
            <div class="cycle-field cycle-field--wide">
              <span class="field-label">乡镇</span>
              <el-select
                v-model="selectedTown"
                class="cycle-select"
                aria-label="选择乡镇"
                :loading="optionsLoading"
              >
                <el-option v-for="town in towns" :key="town" :label="town" :value="town" />
              </el-select>
            </div>
          </div>

          <p class="cycle-range">
            <span class="cycle-badge">统计周期</span>
            <span>{{ cycleText || '计算中…' }}</span>
          </p>
          <p class="field-hint">
            周期为 B 线口径（上月结算日次日至本月结算日，遇法定节假日提前）。
          </p>
        </NcCard>

        <NcCard title="本期汇总" subtitle="新增/停保等计数按乡镇筛选；在保与月保障金为全县汇总（与旧版一致）">
          <el-alert
            v-if="summaryError"
            class="card-alert"
            type="error"
            :title="summaryError"
            :closable="false"
            show-icon
          >
            <el-button link type="primary" @click="loadSummary()">重新加载</el-button>
          </el-alert>

          <div v-loading="summaryLoading" class="stat-grid">
            <div class="stat-tile tone-ink">
              <span>新增</span>
              <strong>{{ summary?.addedCount ?? '—' }}</strong>
            </div>
            <div class="stat-tile tone-amber">
              <span>停保</span>
              <strong>{{ summary?.stoppedCount ?? '—' }}</strong>
            </div>
            <div class="stat-tile tone-pine">
              <span>增发</span>
              <strong>{{ summary?.increaseCount ?? '—' }}</strong>
            </div>
            <div class="stat-tile tone-seal">
              <span>减发</span>
              <strong>{{ summary?.decreaseCount ?? '—' }}</strong>
            </div>
            <div class="stat-tile tone-gray">
              <span>自然减员</span>
              <strong>{{ summary?.deathCount ?? '—' }}</strong>
            </div>
          </div>

          <p v-if="summary" class="summary-line">
            在保 {{ summary.totalArchives }} 户，月保障金合计
            <strong>{{ formatMoney(summary.totalAmount) }}</strong> 元
          </p>
          <p v-else-if="!summaryLoading && !summaryError" class="summary-line muted">
            尚未读取本期汇总
          </p>
          <p v-if="isEmptySummary" class="summary-empty">
            所选周期暂无新增、停保、增减发与自然减员数据。
          </p>
        </NcCard>

        <NcCard title="月报生成" subtitle="写入月报汇总表（按年月，全县一条）">
          <p class="gen-status">
            <template v-if="currentGenerated">
              已生成：{{ formatDateTime(currentGenerated.generatedAt) }}
              <span v-if="currentGenerated.generatedBy">（{{ currentGenerated.generatedBy }}）</span>
            </template>
            <template v-else>尚未生成 {{ selectedYear }}年{{ selectedMonth }}月 月报</template>
          </p>
          <div class="gen-actions">
            <el-button
              type="primary"
              :loading="generating"
              :disabled="currentGenerated != null"
              @click="handleGenerate(false)"
            >
              生成月报
            </el-button>
            <el-button type="warning" plain :loading="generating" @click="handleGenerate(true)">
              覆盖生成
            </el-button>
          </div>
          <p class="field-hint">
            覆盖生成会先删除该年月全部乡镇记录再重算；不影响业务明细数据。
          </p>
        </NcCard>

        <NcCard title="报表表单" subtitle="勾选后预览；导出汇总可下载 Excel/CSV">
          <div class="form-toolbar">
            <el-button size="small" plain @click="handleSelectAll">全选</el-button>
            <el-button size="small" plain @click="handleClearSelection">清空</el-button>
            <span class="form-count">已选 {{ selectedKeys.length }} / {{ formDefinitions.length }}</span>
          </div>

          <div class="form-groups">
            <section v-for="entry in groupedForms" :key="entry.group" class="form-group">
              <h3 class="form-group__title">{{ entry.group }}</h3>
              <el-checkbox-group v-model="selectedKeys" class="form-checkboxes">
                <el-checkbox
                  v-for="form in entry.items"
                  :key="form.key"
                  :value="form.key"
                  class="form-checkbox"
                >
                  {{ form.label }}
                </el-checkbox>
              </el-checkbox-group>
            </section>
          </div>

          <p class="pc-note">
            近亲属备案（工作人员批量 / 汇总）依赖近亲属备案服务，Core 月报服务未提供数据接口，
            Web 端未接入，请在 PC 端旧版生成。
          </p>

          <div class="copy-row">
            <span class="field-label">打印份数（PC 端）</span>
            <el-input-number v-model="copies" :min="1" :max="99" size="small" controls-position="right" />
          </div>

          <div class="form-actions">
            <el-button
              type="primary"
              :icon="View"
              :loading="previewLoading"
              :disabled="!hasSelection"
              @click="handlePreview"
            >
              预览选中表单
            </el-button>
            <el-button :icon="Printer" @click="openPcPrintDialog">前往 PC 端打印</el-button>
          </div>

          <div class="form-actions form-actions--split">
            <el-button
              type="success"
              plain
              :loading="exportingCsv"
              :disabled="!hasSelection"
              @click="handleExportCsv(false)"
            >
              导出选中数据（CSV）
            </el-button>
            <el-button
              type="success"
              plain
              :loading="exportingCsv"
              @click="handleExportCsv(true)"
            >
              导出全部数据（CSV）
            </el-button>
          </div>

          <div class="form-actions form-actions--split">
            <el-button
              :icon="Download"
              :loading="exporting === 'Excel'"
              :disabled="exporting !== ''"
              @click="handleExport('Excel')"
            >
              导出月报汇总 Excel
            </el-button>
            <el-button
              :icon="Download"
              :loading="exporting === 'Csv'"
              :disabled="exporting !== ''"
              @click="handleExport('Csv')"
            >
              导出月报汇总 CSV
            </el-button>
          </div>

          <p class="field-hint">
            「导出月报汇总」调用 Core ExportAsync，仅含年份/月份/总档案/新增/停止/总金额；
            各表单明细请在左侧预览或导出 CSV。
          </p>
        </NcCard>

        <NcCard title="会议记录" subtitle="保存后供会议记录模板回填，并记入出席/缺席历史">
          <el-alert
            v-if="meetingError"
            class="card-alert"
            type="error"
            :title="meetingError"
            :closable="false"
            show-icon
          >
            <el-button link type="primary" @click="loadMeeting()">重新加载</el-button>
          </el-alert>

          <div v-loading="meetingLoading" class="meeting-form">
            <div class="meeting-field">
              <span class="field-label">会议时间</span>
              <el-input v-model="meetingForm.meetingTime" placeholder="如 2026年11月7日 星期六" />
            </div>
            <div class="meeting-field meeting-field--split">
              <div>
                <span class="field-label">主持人</span>
                <el-input v-model="meetingForm.host" placeholder="民政助理" />
              </div>
              <div>
                <span class="field-label">记录人</span>
                <el-input v-model="meetingForm.recorder" placeholder="当前用户" />
              </div>
            </div>
            <div class="meeting-field">
              <span class="field-label">申请类别</span>
              <el-input v-model="meetingForm.applyCategory" placeholder="如 最低生活保障" />
            </div>

            <div class="attendance-summary">
              <div>
                <span class="field-label">出席人员</span>
                <pre class="attendance-text">{{ meetingForm.attendees || '—' }}</pre>
              </div>
              <div>
                <span class="field-label">缺席人员</span>
                <pre class="attendance-text">{{ meetingForm.absentees || '—' }}</pre>
              </div>
            </div>

            <div class="meeting-actions">
              <el-button size="small" plain @click="openAttendanceDialog()">编辑出席/缺席</el-button>
              <el-button type="primary" size="small" :loading="meetingSaving" @click="handleSaveMeeting">
                保存会议记录
              </el-button>
            </div>

            <details v-if="meetingForm.memberInfo" class="member-info">
              <summary>本期新增成员信息（{{ meetingForm.memberNames || '—' }}）</summary>
              <pre>{{ meetingForm.memberInfo }}</pre>
            </details>
          </div>
        </NcCard>

        <NcCard title="生成历史">
          <div class="history-toolbar">
            <el-select
              v-model="historyYear"
              class="history-year"
              placeholder="全部年份"
              @change="handleHistoryYearChange"
            >
              <el-option label="全部年份" :value="0" />
              <el-option v-for="year in years" :key="year" :label="`${year} 年`" :value="year" />
            </el-select>
            <el-button size="small" plain :loading="historyLoading" @click="handleHistorySearch">
              查询
            </el-button>
          </div>

          <el-alert
            v-if="historyError"
            class="card-alert"
            type="error"
            :title="historyError"
            :closable="false"
            show-icon
          >
            <el-button link type="primary" @click="loadHistory()">重新加载</el-button>
          </el-alert>

          <div v-loading="historyLoading" class="history-list">
            <p v-if="historyItems.length === 0 && !historyLoading && !historyError" class="history-empty">
              暂无生成记录
            </p>

            <article v-for="item in historyPaged" :key="item.id" class="history-item">
              <div class="history-item__main">
                <strong>{{ item.year }}年{{ item.month }}月 · {{ item.town }}</strong>
                <span>
                  在保 {{ item.totalArchives }} · 新增 {{ item.newArchives }} · 停保
                  {{ item.stoppedArchives }} · {{ formatDateTime(item.generatedAt) }}
                </span>
              </div>
              <span class="history-item__amount">{{ formatMoney(item.totalAmount) }} 元</span>
              <el-button link type="danger" size="small" @click="handleDeleteHistory(item)">
                删除
              </el-button>
            </article>
          </div>

          <div class="history-pager">
            <el-button size="small" plain :disabled="historyPage <= 1" @click="handleHistoryPrev">
              上一页
            </el-button>
            <span class="history-page">{{ historyPage }} / {{ historyTotalPages }}</span>
            <el-button
              size="small"
              plain
              :disabled="historyPage >= historyTotalPages"
              @click="handleHistoryNext"
            >
              下一页
            </el-button>
          </div>
        </NcCard>
      </div>

      <!-- 右列：预览 -->
      <div class="report-right">
        <NcCard title="报表预览" class="preview-card">
          <template #actions>
            <span v-if="previewTimeText" class="preview-time">{{ previewTimeText }}</span>
          </template>

          <el-alert
            v-if="previewError"
            class="card-alert"
            type="error"
            :title="previewError"
            :closable="false"
            show-icon
          />

          <div v-loading="previewLoading" class="preview-body">
            <el-empty
              v-if="previewSections.length === 0 && !previewLoading"
              description="勾选左侧报表表单后点击「预览选中表单」"
            />

            <section
              v-for="section in previewSections"
              :key="section.key"
              class="preview-section"
            >
              <header class="preview-section__head">
                <h3>{{ section.title }}</h3>
                <span class="preview-section__meta">
                  <span v-if="!section.error">{{ sectionExtraText(section) }}</span>
                  <span v-else class="is-error">加载失败</span>
                </span>
              </header>

              <el-alert
                v-if="section.error"
                type="error"
                :title="section.error"
                :closable="false"
                show-icon
                class="section-error"
              >
                <el-button link type="primary" @click="runPreview()">重新加载全部预览</el-button>
              </el-alert>

              <template v-else-if="section.kind === 'meeting' && section.meeting">
                <div v-if="section.empty" class="section-empty">
                  {{ section.emptyText }}
                </div>
                <dl v-else class="meeting-preview">
                  <template v-for="item in meetingParagraphs(section.meeting!)" :key="item.label">
                    <dt>{{ item.label }}</dt>
                    <dd>{{ item.value }}</dd>
                  </template>
                </dl>
              </template>

              <template v-else-if="section.kind === 'classified'">
                <div v-if="section.empty" class="section-empty">{{ section.emptyText }}</div>
                <div v-else class="classified-list">
                  <article
                    v-for="(household, householdIndex) in section.households"
                    :key="`${section.key}-${householdIndex}`"
                    class="classified-household"
                  >
                    <header class="classified-head">
                      <strong>{{ household.applicantName }}</strong>
                      <span>{{ household.classification }} · {{ household.familySize }} 人</span>
                      <span class="classified-amount">
                        符合 {{ household.count }} 人 · 加发 {{ formatMoney(household.amount) }} 元
                      </span>
                    </header>
                    <dl class="classified-meta">
                      <div><dt>性别</dt><dd>{{ household.applicantGender || '—' }}</dd></div>
                      <div><dt>出生日期</dt><dd>{{ household.applicantBirthDate || '—' }}</dd></div>
                      <div><dt>民族</dt><dd>{{ household.nationality || '—' }}</dd></div>
                      <div><dt>健康状况</dt><dd>{{ household.healthStatus || '—' }}</dd></div>
                      <div><dt>联系电话</dt><dd>{{ household.phone || '—' }}</dd></div>
                      <div><dt>身份证号</dt><dd>{{ household.applicantIdCard || '—' }}</dd></div>
                      <div class="classified-meta__wide">
                        <dt>家庭住址</dt><dd>{{ household.address || '—' }}</dd>
                      </div>
                      <div class="classified-meta__wide">
                        <dt>分类施保类型</dt><dd>{{ household.classifiedType || '—' }}</dd>
                      </div>
                    </dl>

                    <div v-if="!isNarrow" class="table-scroll">
                      <el-table :data="household.members" border size="small" class="preview-table">
                        <el-table-column
                          v-for="column in section.columns"
                          :key="column.prop"
                          :label="column.label"
                          :min-width="column.minWidth ?? 100"
                        >
                          <template #default="{ row }">
                            {{ formatCell(row as Record<string, unknown>, column) }}
                          </template>
                        </el-table-column>
                      </el-table>
                    </div>
                    <div v-else class="preview-cards">
                      <article
                        v-for="(member, memberIndex) in household.members"
                        :key="memberIndex"
                        class="preview-card"
                      >
                        <div
                          v-for="column in section.columns"
                          :key="column.prop"
                          class="preview-card__row"
                        >
                          <span>{{ column.label }}</span>
                          <strong>{{ formatCell(member as unknown as Record<string, unknown>, column) }}</strong>
                        </div>
                      </article>
                    </div>
                  </article>
                </div>
              </template>

              <template v-else>
                <div v-if="section.empty" class="section-empty">{{ section.emptyText }}</div>
                <div v-else class="section-tables">
                  <div
                    v-for="(table, tableIndex) in section.tables"
                    :key="`${section.key}-${tableIndex}`"
                    class="section-table"
                  >
                    <h4 v-if="table.title" class="section-table__title">{{ table.title }}</h4>

                    <div v-if="!isNarrow" class="table-scroll">
                      <el-table :data="table.rows" border size="small" class="preview-table">
                        <el-table-column
                          v-if="section.showIndex"
                          type="index"
                          label="序号"
                          width="56"
                          align="center"
                        />
                        <el-table-column
                          v-for="column in section.columns"
                          :key="column.prop"
                          :label="column.label"
                          :min-width="column.minWidth ?? 100"
                        >
                          <template #default="{ row }">
                            {{ formatCell(row as Record<string, unknown>, column) }}
                          </template>
                        </el-table-column>
                      </el-table>
                    </div>

                    <div v-else class="preview-cards">
                      <article
                        v-for="(row, rowIndex) in table.rows"
                        :key="rowIndex"
                        class="preview-card"
                      >
                        <div class="preview-card__head">
                          <span>#{{ rowIndex + 1 }}</span>
                          <strong>{{ row.name ?? row.headName ?? row.applicantName ?? '—' }}</strong>
                        </div>
                        <div
                          v-for="column in section.columns"
                          :key="column.prop"
                          class="preview-card__row"
                        >
                          <span>{{ column.label }}</span>
                          <strong>{{ formatCell(row, column) }}</strong>
                        </div>
                      </article>
                    </div>
                  </div>
                </div>
              </template>
            </section>
          </div>
        </NcCard>
      </div>
    </div>

    <!-- 会议出席/缺席弹窗（旧版“重新录入”流程） -->
    <el-dialog
      v-model="attendanceVisible"
      title="会议出席/缺席登记"
      width="560px"
      :close-on-click-modal="false"
      append-to-body
      @closed="handleAttendanceClosed"
    >
      <div v-loading="attendanceLoading" class="attendance-dialog">
        <p v-if="attendanceHistoryText" class="dialog-hint">{{ attendanceHistoryText }}</p>
        <el-alert
          v-if="attendanceError"
          type="error"
          :title="attendanceError"
          :closable="false"
          show-icon
        />
        <label class="field-label">出席人员（每行一人）</label>
        <el-input
          v-model="popupAttendees"
          type="textarea"
          :rows="6"
          placeholder="请输入本期会议出席人员，每行一人..."
        />
        <label class="field-label">缺席人员（每行一人）</label>
        <el-input
          v-model="popupAbsentees"
          type="textarea"
          :rows="4"
          placeholder="请输入本期会议缺席人员，每行一人..."
        />
        <p class="dialog-note">保存后将作为本周期会议记录，并记入历史，供下次生成时回填。</p>
      </div>
      <template #footer>
        <el-button @click="cancelAttendance">取消</el-button>
        <el-button type="primary" :loading="attendanceSaving" @click="confirmAttendance">
          {{ attendanceActionLabel }}
        </el-button>
      </template>
    </el-dialog>

    <!-- PC 端打印说明（Web 无 Office/PC Agent 管线，不伪造打印） -->
    <el-dialog v-model="pcDialogVisible" title="前往 PC 端打印" width="620px" append-to-body>
      <div class="pc-dialog">
        <p>
          Web 端未接入 Office 模板渲染与 PC Agent 打印管线，无法像旧版桌面端那样直接生成 PDF
          并打印报表表单，因此这里不提供“打印”按钮的模拟结果。
        </p>
        <ul>
          <li>月报汇总可直接在本页导出 Excel / CSV（调用 Core ExportAsync）。</li>
          <li>
            需要《新增救助明细》《停保汇总表》《保障金增发/减发表》《分类施保》《自然减员》
            《退出对象纠治》《临时救助新增》《会议记录》等表单的 xlsx/docx 源文件或纸质打印件时，
            请在 PC 端旧版「救助月报表」按相同年月 / 乡镇生成并打印。
          </li>
          <li>打印份数：<strong>{{ copies }}</strong> 份（仅 PC 端生效）。</li>
        </ul>
        <div class="pc-actions">
          <el-button
            type="primary"
            :loading="exporting === 'Excel'"
            :disabled="exporting !== ''"
            @click="handleExport('Excel')"
          >
            导出汇总 Excel
          </el-button>
          <el-button
            :loading="exporting === 'Csv'"
            :disabled="exporting !== ''"
            @click="handleExport('Csv')"
          >
            导出汇总 CSV
          </el-button>
        </div>
      </div>
      <template #footer>
        <el-button @click="pcDialogVisible = false">关闭</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.monthly-report-page {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.page-alert {
  margin: 0;
}

.report-layout {
  display: grid;
  grid-template-columns: minmax(0, 430px) minmax(0, 1fr);
  align-items: start;
  gap: 15px;
}

.report-left {
  display: flex;
  flex-direction: column;
  gap: 15px;
  min-width: 0;
}

.report-right {
  min-width: 0;
}

.preview-card {
  display: flex;
  flex-direction: column;
}

.cycle-fields {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
}

.cycle-field {
  display: flex;
  flex-direction: column;
  gap: 5px;
  min-width: 0;
}

.cycle-field--wide {
  grid-column: 1 / -1;
}

.field-label {
  display: block;
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  font-weight: 600;
  letter-spacing: 0.08em;
}

.cycle-select {
  width: 100%;
}

.cycle-range {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  margin-top: 12px;
  padding: 8px 10px;
  border: 1px solid var(--nc-line-faint);
  border-left: 3px solid var(--nc-seal);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-seal-soft);
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  line-height: 1.6;
  overflow-wrap: anywhere;
}

.cycle-badge {
  flex: none;
  padding: 1px 6px;
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-seal);
  color: var(--nc-paper-soft);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  font-weight: 600;
  letter-spacing: 0.08em;
}

.field-hint {
  margin-top: 9px;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.65;
}

.card-alert {
  margin: 0 0 12px;
}

.stat-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px;
}

.stat-tile {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: 10px 12px 11px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-3);
}

.stat-tile:last-child {
  grid-column: 1 / -1;
}

.stat-tile span {
  font-family: var(--nc-font-mono);
  font-size: 10px;
  font-weight: 600;
  letter-spacing: 0.1em;
  color: var(--nc-muted);
}

.stat-tile strong {
  font-family: var(--nc-font-serif);
  font-size: 26px;
  font-weight: 700;
  line-height: 1.2;
  color: var(--nc-ink);
}

.stat-tile.tone-ink {
  background-color: var(--nc-paper-3);
}

.stat-tile.tone-amber {
  background-color: var(--nc-amber-soft);
  border-color: rgba(168, 118, 30, 0.22);
}

.stat-tile.tone-amber strong {
  color: var(--nc-amber);
}

.stat-tile.tone-pine {
  background-color: var(--nc-pine-soft);
  border-color: rgba(46, 71, 56, 0.2);
}

.stat-tile.tone-pine strong {
  color: var(--nc-pine-dark, var(--nc-pine));
}

.stat-tile.tone-seal {
  background-color: var(--nc-seal-soft);
  border-color: rgba(171, 50, 39, 0.22);
}

.stat-tile.tone-seal strong {
  color: var(--nc-seal);
}

.stat-tile.tone-gray {
  background-color: var(--nc-paper-3);
}

.stat-tile.tone-gray strong {
  color: var(--nc-ink-2);
}

.summary-line {
  margin-top: 11px;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  line-height: 1.6;
  text-align: right;
}

.summary-line strong {
  color: var(--nc-seal);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-lg);
}

.summary-line.muted {
  color: var(--nc-muted);
}

.summary-empty {
  margin-top: 6px;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.6;
  text-align: right;
}

.gen-status {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  line-height: 1.7;
}

.gen-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
  margin-top: 12px;
}

.gen-actions :deep(.el-button) {
  margin-left: 0;
}

.form-toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.form-toolbar :deep(.el-button) {
  margin-left: 0;
}

.form-count {
  margin-left: auto;
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
}

.form-groups {
  display: flex;
  flex-direction: column;
  gap: 12px;
  max-height: 340px;
  margin-top: 12px;
  padding: 10px 12px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-3);
  overflow-y: auto;
}

.form-group__title {
  margin-bottom: 4px;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-base);
  font-weight: 700;
  letter-spacing: 0.03em;
}

.form-checkboxes {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.form-checkbox {
  height: auto;
  min-height: 26px;
  margin-right: 0;
  white-space: normal;
}

.form-checkbox :deep(.el-checkbox__label) {
  font-size: var(--nc-text-sm);
  line-height: 1.5;
}

.pc-note {
  margin-top: 10px;
  padding: 7px 9px;
  border-left: 2px solid var(--nc-line-strong);
  background-color: var(--nc-paper-3);
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.65;
}

.copy-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  margin-top: 12px;
}

.form-actions {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
  margin-top: 12px;
}

.form-actions--split {
  grid-template-columns: repeat(2, minmax(0, 1fr));
  margin-top: 9px;
}

.form-actions :deep(.el-button) {
  width: 100%;
  margin-left: 0;
}

.meeting-form {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.meeting-field {
  display: flex;
  flex-direction: column;
  gap: 5px;
}

.meeting-field--split {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
}

.attendance-summary {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
}

.attendance-text {
  margin: 4px 0 0;
  padding: 8px 10px;
  max-height: 96px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-3);
  color: var(--nc-ink-2);
  font-family: inherit;
  font-size: var(--nc-text-sm);
  line-height: 1.6;
  white-space: pre-wrap;
  overflow: auto;
}

.meeting-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  justify-content: flex-end;
}

.meeting-actions :deep(.el-button) {
  margin-left: 0;
}

.member-info {
  border-top: 1px dashed var(--nc-line-faint);
  padding-top: 8px;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.member-info summary {
  cursor: pointer;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
}

.member-info pre {
  margin: 6px 0 0;
  white-space: pre-wrap;
  font-family: inherit;
  line-height: 1.65;
}

.history-toolbar {
  display: flex;
  align-items: center;
  gap: 8px;
}

.history-year {
  width: 140px;
}

.history-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-height: 60px;
  margin-top: 10px;
}

.history-empty {
  padding: 12px 0;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  text-align: center;
}

.history-item {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto auto;
  align-items: center;
  gap: 8px;
  padding: 8px 10px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-3);
}

.history-item__main {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.history-item__main strong {
  color: var(--nc-ink);
  font-size: var(--nc-text-sm);
}

.history-item__main span {
  overflow: hidden;
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.history-item__amount {
  color: var(--nc-seal);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  white-space: nowrap;
}

.history-pager {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 12px;
  margin-top: 10px;
}

.history-page {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
}

.preview-time {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  white-space: nowrap;
}

.preview-body {
  display: flex;
  flex-direction: column;
  gap: 16px;
  min-height: 220px;
}

.preview-section {
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-2);
  overflow: hidden;
}

.preview-section__head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 10px;
  padding: 9px 12px;
  border-bottom: 1px solid var(--nc-line-faint);
  background-color: var(--nc-paper-3);
}

.preview-section__head h3 {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-base);
  font-weight: 700;
  letter-spacing: 0.02em;
  line-height: 1.45;
}

.preview-section__meta {
  flex: none;
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
}

.preview-section__meta .is-error {
  color: var(--el-color-danger);
}

.section-error {
  margin: 10px;
}

.section-empty {
  padding: 22px 14px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  line-height: 1.65;
  text-align: center;
}

.section-tables {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 10px;
}

.section-table__title {
  margin: 0 0 6px;
  color: var(--nc-ink-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  font-weight: 700;
  letter-spacing: 0.08em;
}

.table-scroll {
  width: 100%;
  overflow-x: auto;
}

.preview-table {
  width: 100%;
}

.preview-table :deep(.el-table__cell) {
  font-size: var(--nc-text-xs);
}

.preview-table :deep(th.el-table__cell) {
  color: var(--nc-ink-2);
  background-color: var(--nc-paper-3);
}

.preview-cards {
  display: grid;
  gap: 8px;
}

.preview-card {
  padding: 9px 11px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
}

.preview-card__head {
  display: flex;
  align-items: baseline;
  gap: 8px;
  margin-bottom: 6px;
  padding-bottom: 5px;
  border-bottom: 1px dashed var(--nc-line-faint);
}

.preview-card__head span {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
}

.preview-card__head strong {
  color: var(--nc-ink);
  font-size: var(--nc-text-sm);
}

.preview-card__row {
  display: grid;
  grid-template-columns: 96px minmax(0, 1fr);
  gap: 8px;
  padding: 2px 0;
  font-size: var(--nc-text-xs);
  line-height: 1.55;
}

.preview-card__row span {
  color: var(--nc-muted);
  white-space: nowrap;
}

.preview-card__row strong {
  color: var(--nc-ink-2);
  font-weight: 500;
  overflow-wrap: anywhere;
}

.meeting-preview {
  display: grid;
  grid-template-columns: 104px minmax(0, 1fr);
  gap: 6px 12px;
  padding: 12px 14px;
  margin: 0;
}

.meeting-preview dt {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
}

.meeting-preview dd {
  margin: 0;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  line-height: 1.7;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}

.classified-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 10px;
}

.classified-household {
  padding: 10px 12px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
}

.classified-head {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 10px;
  padding-bottom: 7px;
  border-bottom: 1px dashed var(--nc-line-faint);
}

.classified-head strong {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-lg);
  font-weight: 700;
}

.classified-head span {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.classified-amount {
  margin-left: auto;
  color: var(--nc-seal) !important;
  font-family: var(--nc-font-mono);
}

.classified-meta {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 5px 12px;
  margin: 8px 0 10px;
}

.classified-meta div {
  display: flex;
  gap: 5px;
  min-width: 0;
  font-size: var(--nc-text-xs);
  line-height: 1.55;
}

.classified-meta dt {
  flex: none;
  color: var(--nc-muted);
}

.classified-meta dd {
  margin: 0;
  overflow: hidden;
  color: var(--nc-ink-2);
  text-overflow: ellipsis;
}

.classified-meta__wide {
  grid-column: span 3;
}

.attendance-dialog {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.dialog-hint {
  padding: 6px 9px;
  border-left: 2px solid var(--nc-seal);
  background-color: var(--nc-seal-soft);
  color: var(--nc-ink-2);
  font-size: var(--nc-text-xs);
  line-height: 1.6;
}

.dialog-note {
  margin-top: 2px;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.6;
  text-align: center;
}

.pc-dialog {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  line-height: 1.75;
}

.pc-dialog ul {
  margin: 8px 0;
  padding-left: 20px;
}

.pc-dialog li {
  margin-bottom: 4px;
}

.pc-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
  margin-top: 10px;
}

.pc-actions :deep(.el-button) {
  margin-left: 0;
}

@media (max-width: 1200px) {
  .report-layout {
    grid-template-columns: minmax(0, 390px) minmax(0, 1fr);
  }
}

@media (max-width: 960px) {
  .report-layout {
    grid-template-columns: minmax(0, 1fr);
  }

  .preview-body {
    max-height: none;
  }
}

@media (max-width: 767px) {
  .monthly-report-page {
    gap: 13px;
  }

  .cycle-fields {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .form-groups {
    max-height: 300px;
  }

  .form-actions,
  .form-actions--split {
    grid-template-columns: minmax(0, 1fr);
  }

  .meeting-field--split,
  .attendance-summary {
    grid-template-columns: minmax(0, 1fr);
  }

  .history-item {
    grid-template-columns: minmax(0, 1fr) auto;
  }

  .history-item__amount {
    grid-column: 1 / -1;
  }

  .classified-meta {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .classified-meta__wide {
    grid-column: span 2;
  }

  .meeting-preview {
    grid-template-columns: 88px minmax(0, 1fr);
    padding: 10px;
  }

  .preview-card__row {
    grid-template-columns: 88px minmax(0, 1fr);
  }
}

@media (max-width: 480px) {
  .stat-grid {
    grid-template-columns: minmax(0, 1fr);
  }

  .stat-tile:last-child {
    grid-column: auto;
  }

  .cycle-fields {
    grid-template-columns: minmax(0, 1fr);
  }

  .classified-meta {
    grid-template-columns: minmax(0, 1fr);
  }

  .classified-meta__wide {
    grid-column: auto;
  }
}
</style>
