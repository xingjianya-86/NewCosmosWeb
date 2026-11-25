<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  ArrowLeft,
  Delete,
  Document,
  Plus,
  Refresh,
  Search,
} from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import {
  changeTempReliefApplyDate,
  checkTempReliefAnnual,
  confirmTempReliefApplication,
  createTempReliefApplication,
  deleteTempReliefApplication,
  fileTempReliefApplication,
  getTempReliefApplication,
  getTempReliefCandidate,
  getTempReliefFamilyStatus,
  getTempReliefInsuredIdentity,
  getTempReliefPolicySnapshot,
  getTempReliefReportUnitPhone,
  getTempReliefSmallTiers,
  rejectTempReliefApplication,
  resolveTempReliefHukouType,
  returnTempReliefApplication,
  searchTempReliefCandidates,
  stopTempReliefApplication,
  submitTempReliefApplication,
  updateTempReliefApplication,
} from '@/api/tempRelief'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import { useAuthStore } from '@/stores/auth'
import type {
  TempReliefCandidate,
  TempReliefDetail,
  TempReliefEducationInput,
  TempReliefInsuredIdentityResponse,
  TempReliefMemberSnapshot,
  TempReliefOcrResponse,
  TempReliefSaveRequest,
  TempReliefSmallTier,
} from '@/types/tempRelief'
import TempReliefOcrDialog from './TempReliefOcrDialog.vue'
import TempReliefPhotoPanel from './TempReliefPhotoPanel.vue'
import TempReliefPrintDialog from './TempReliefPrintDialog.vue'

type FormMode = 'create' | 'edit' | 'view'
type ReliefType = 'Large' | 'Small'
type PaymentChannel = 'County' | 'Reserve'
type DifficultyType = '疾病' | '意外灾害' | '教育支出' | '其他困难'
type ActionKey = 'submit' | 'return' | 'reject' | 'confirm' | 'file' | 'stop' | 'delete'

interface ApplicantState {
  sourceTable: string
  sourceFamilyId: number
  hukouType: string
  name: string
  idCard: string
  gender: string
  age: number | null
  phone: string
  familyProvince: string
  familyCity: string
  familyDistrict: string
  familyTown: string
  town: string
  village: string
  detailAddress: string
  hukouProvince: string
  hukouCity: string
  hukouDistrict: string
  hukouTown: string
  familyCategory: string
  bankName: string
  bankAccount: string
}

interface FormState {
  id: number
  applicationNo: string
  status: string
  reliefType: ReliefType
  paymentChannel: PaymentChannel
  applyDate: string
  reportUnit: string
  reportTime: string | null
  difficultyType: DifficultyType
  difficultyReason: string
  policyEnjoyed: string
  familyMemberStatus: string
  verifyResult: string
  acceptancePerson: string
  standardTier: string
  confirmAmount: number | undefined
  beneficiaryKey: string
  reliefPersonCount: number
  publicizeStartDate: string | null
  publicizeEndDate: string | null
}

interface MemberRow {
  key: number
  id: number
  memberName: string
  gender: string
  relation: string
  idCard: string
  workUnit: string
  annualIncome: number | undefined
  sortOrder: number
}

interface DiseaseRow {
  key: number
  id: number
  diseaseName: string
  diseaseCode: string
  memberName: string
  hospital: string
  treatStartDate: string | null
  treatEndDate: string | null
  medicalTotal: number | undefined
  insurancePaid: number | undefined
  selfPaid: number | undefined
  sortOrder: number
}

interface AccidentRow {
  key: number
  id: number
  accidentType: string
  memberName: string
  happenDate: string | null
  happenPlace: string
  injurySituation: string
  propertyLoss: number | undefined
  compensationPaid: number | undefined
  responsibilityDesc: string
  materialDesc: string
  sortOrder: number
}

interface EducationRow {
  key: number
  id: number
  studentName: string
  educationStage: string
  schoolName: string
  schoolDuration: number | undefined
  tuitionFee: number | undefined
  feeDate: string | null
  sortOrder: number
}

interface BeneficiaryOption {
  key: string
  name: string
  idCard: string
  gender: string
  age: number | null
  relation: string
  isHead: boolean
}

const FAMILY_CATEGORY_BASE = [
  '最低生活保障家庭',
  '最低生活保障边缘家庭',
  '特困人员',
  '重点优抚对象',
  '遭受突发事件家庭',
  '建档立卡贫困户',
  '其他困难类型家庭',
]
const DIFFICULTY_TYPES: readonly DifficultyType[] = ['疾病', '意外灾害', '教育支出', '其他困难']
const ACCIDENT_TYPE_OPTIONS = ['火灾', '交通事故', '溺水', '人身伤害', '其他意外']
const EDUCATION_STAGE_OPTIONS = ['学前教育', '小学', '初中', '高中', '中职', '高职', '大学']
const SCHOOL_DURATION_OPTIONS = [
  { label: '三年制', value: 3 },
  { label: '四年制', value: 4 },
  { label: '五年制', value: 5 },
]
const DEFAULT_VERIFY_TEXT = '已入户核实，申请人家庭实际情况与申报一致。'
const MAX_DETAIL_ROWS = 10
const MAX_MEMBER_ROWS = 10
const HEAD_KEY = '__head__'

const STATUS_TEXT: Record<string, string> = {
  Draft: '草稿',
  Submitted: '已提交',
  Confirmed: '已确认',
  Returned: '退回补正',
  Rejected: '不予救助',
  Stopped: '已终止',
}

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

let rowSeed = 0
let identityTicket = 0
let annualTicket = 0
let tierTicket = 0
let routeReloadSuppressed = false

function nextRowKey(): number {
  rowSeed += 1
  return rowSeed
}

function pad(value: number): string {
  return String(value).padStart(2, '0')
}

function toDateText(date: Date): string {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

function todayText(): string {
  return toDateText(new Date())
}

function formatDate(value: string | null | undefined): string {
  if (!value) return '—'
  const text = value.slice(0, 10)
  return /^\d{4}-\d{2}-\d{2}$/.test(text) ? text.split('-').join('/') : '—'
}

function formatDateTime(value: string | null | undefined): string {
  if (!value) return '—'
  return value.length >= 16 ? value.slice(0, 16).replace('T', ' ') : formatDate(value)
}

function formatMoney(value: number | null | undefined): string {
  if (value === null || value === undefined || !Number.isFinite(value)) return '—'
  return `${value.toFixed(2)} 元`
}

function calculateAge(idCard: string): number | null {
  if (idCard.length !== 18) return null
  const year = Number(idCard.slice(6, 10))
  const month = Number(idCard.slice(10, 12))
  const day = Number(idCard.slice(12, 14))
  if (!Number.isFinite(year) || !Number.isFinite(month) || !Number.isFinite(day)) return null
  const now = new Date()
  let age = now.getFullYear() - year
  if (now.getMonth() + 1 < month || (now.getMonth() + 1 === month && now.getDate() < day)) {
    age -= 1
  }
  return age >= 0 ? age : null
}

function ageFromBirthDate(value: string | null): number | null {
  if (!value) return null
  const date = new Date(`${value.slice(0, 10)}T00:00:00`)
  if (Number.isNaN(date.getTime())) return null
  const now = new Date()
  let age = now.getFullYear() - date.getFullYear()
  if (now.getMonth() < date.getMonth()
    || (now.getMonth() === date.getMonth() && now.getDate() < date.getDate())) {
    age -= 1
  }
  return age >= 0 ? age : null
}

function genderFromIdCard(idCard: string): string {
  if (idCard.length !== 18) return ''
  const digit = Number(idCard[16])
  return Number.isFinite(digit) ? (digit % 2 === 1 ? '男' : '女') : ''
}

function isValidIdCard(value: string): boolean {
  return /^\d{17}[\dXx]$/.test(value.trim())
}

function normalizeFamilyCategory(value: string): string {
  const text = value.trim()
  if (!text) return ''
  if (familyCategoryOptions.value.includes(text)) return text
  if (text.includes('特困')) return '特困人员'
  if (text.includes('边缘') || text.includes('刚性支出')) return '最低生活保障边缘家庭'
  if (text.includes('低保') || text.includes('最低生活保障')) return '最低生活保障家庭'
  if (text.includes('优抚')) return '重点优抚对象'
  if (text.includes('突发')) return '遭受突发事件家庭'
  if (text.includes('建档立卡')) return '建档立卡贫困户'
  if (text.includes('其他困难')) return '其他困难类型家庭'
  return text
}

function normalizeDifficulty(value: string | null | undefined): DifficultyType {
  const text = (value ?? '').trim()
  return (DIFFICULTY_TYPES as readonly string[]).includes(text)
    ? (text as DifficultyType)
    : '其他困难'
}

function formatHukouType(value: string): string {
  const text = value.trim()
  if (!text) return '未识别'
  if (text.includes('农村') || text.toLowerCase() === 'rural') return '农村'
  if (text.includes('城镇') || text.includes('城市') || text.toLowerCase() === 'urban') return '城镇'
  return text
}

function extractDetailFromAddress(address: string, village: string): string {
  if (!address) return ''
  if (village) {
    const index = address.lastIndexOf(village)
    if (index >= 0) return address.slice(index + village.length).trim()
  }
  const match = address.match(/(?:村|社区)(.+)$/)
  return match?.[1]?.trim() ?? ''
}

/** OCR 住址是「省+市+县+镇+村+门牌」全量形态，剥掉已分列的前缀，只保留详细地址。 */
function stripKnownAddressPrefix(address: string): string {
  let text = address.trim()
  const parts = [
    applicant.familyProvince,
    applicant.familyCity,
    applicant.familyDistrict,
    applicant.familyTown,
    applicant.village,
  ]
    .map((part) => part.trim())
    .filter(Boolean)
    .sort((a, b) => b.length - a.length)
  for (const part of parts) {
    if (text.startsWith(part)) text = text.slice(part.length).trim()
  }
  return text || address.trim()
}

function maxApplyDateText(): string {
  const today = new Date()
  const target = new Date(today.getFullYear(), today.getMonth() + 1, 1)
  const lastDay = new Date(target.getFullYear(), target.getMonth() + 1, 0).getDate()
  target.setDate(Math.min(today.getDate(), lastDay))
  return toDateText(target)
}

function isDateBeyondMax(date: Date): boolean {
  return toDateText(date) > maxApplyDateText()
}

function createEmptyApplicant(): ApplicantState {
  return {
    sourceTable: '',
    sourceFamilyId: 0,
    hukouType: '',
    name: '',
    idCard: '',
    gender: '',
    age: null,
    phone: '',
    familyProvince: '黑龙江省',
    familyCity: '',
    familyDistrict: '',
    familyTown: '',
    town: '',
    village: '',
    detailAddress: '',
    hukouProvince: '黑龙江省',
    hukouCity: '',
    hukouDistrict: '',
    hukouTown: '',
    familyCategory: '',
    bankName: '',
    bankAccount: '',
  }
}

function createEmptyForm(): FormState {
  return {
    id: 0,
    applicationNo: '',
    status: 'Draft',
    reliefType: 'Large',
    paymentChannel: 'County',
    applyDate: todayText(),
    reportUnit: '',
    reportTime: null,
    difficultyType: '其他困难',
    difficultyReason: '',
    policyEnjoyed: '',
    familyMemberStatus: '',
    verifyResult: DEFAULT_VERIFY_TEXT,
    acceptancePerson: '',
    standardTier: '',
    confirmAmount: undefined,
    beneficiaryKey: HEAD_KEY,
    reliefPersonCount: 1,
    publicizeStartDate: null,
    publicizeEndDate: null,
  }
}

function createMemberRow(): MemberRow {
  return {
    key: nextRowKey(),
    id: 0,
    memberName: '',
    gender: '',
    relation: '',
    idCard: '',
    workUnit: '',
    annualIncome: undefined,
    sortOrder: 0,
  }
}

function createDiseaseRow(): DiseaseRow {
  return {
    key: nextRowKey(),
    id: 0,
    diseaseName: '',
    diseaseCode: '',
    memberName: '',
    hospital: '',
    treatStartDate: null,
    treatEndDate: null,
    medicalTotal: undefined,
    insurancePaid: undefined,
    selfPaid: undefined,
    sortOrder: 0,
  }
}

function createAccidentRow(): AccidentRow {
  return {
    key: nextRowKey(),
    id: 0,
    accidentType: '',
    memberName: '',
    happenDate: null,
    happenPlace: '',
    injurySituation: '',
    propertyLoss: undefined,
    compensationPaid: undefined,
    responsibilityDesc: '',
    materialDesc: '',
    sortOrder: 0,
  }
}

function createEducationRow(): EducationRow {
  return {
    key: nextRowKey(),
    id: 0,
    studentName: '',
    educationStage: '',
    schoolName: '',
    schoolDuration: undefined,
    tuitionFee: undefined,
    feeDate: todayText(),
    sortOrder: 0,
  }
}

const mode = computed<FormMode>(() => {
  if (route.path.endsWith('/new')) return 'create'
  const raw = route.query.mode
  const value = Array.isArray(raw) ? raw[0] : raw
  return value === 'view' ? 'view' : 'edit'
})
const isCreate = computed(() => mode.value === 'create')
const isView = computed(() => mode.value === 'view')
const applicationId = computed(() => {
  const raw = route.params.id
  const value = Array.isArray(raw) ? raw[0] : raw
  return value ? String(value) : ''
})

const form = reactive<FormState>(createEmptyForm())
const applicant = reactive<ApplicantState>(createEmptyApplicant())
const members = ref<MemberRow[]>([createMemberRow()])
const diseases = ref<DiseaseRow[]>([])
const accidents = ref<AccidentRow[]>([])
const educations = ref<EducationRow[]>([])

const detail = ref<TempReliefDetail | null>(null)
const storedFamilySize = ref<number | null>(null)
const familyCategoryOptions = ref<string[]>([...FAMILY_CATEGORY_BASE])

const loading = ref(false)
const saving = ref(false)
const actionLoading = ref<ActionKey | null>(null)
const loadError = ref('')
const autoFilling = ref(false)
const activeSection = ref(0)

const section1 = ref<HTMLElement | null>(null)
const section2 = ref<HTMLElement | null>(null)
const section3 = ref<HTMLElement | null>(null)

const candidateKeyword = ref('')
const candidates = ref<TempReliefCandidate[]>([])
const candidateLoading = ref(false)
const candidateSearched = ref(false)
const candidateSelected = ref(false)
const candidateSummary = ref('')
const manualEntry = ref(false)

const identity = ref<TempReliefInsuredIdentityResponse | null>(null)
const identityLoading = ref(false)
const identityError = ref('')

const annualBlocked = ref(false)
const annualMessage = ref('')
const annualChecking = ref(false)

const smallTiers = ref<TempReliefSmallTier[]>([])
const smallTiersBase = ref<number | null>(null)
const smallTiersLoading = ref(false)
const smallTiersError = ref('')

const reportUnitPhone = ref('')
const policyItems = ref<string[]>([])

const printVisible = ref(false)
const photoVisible = ref(false)
const ocrVisible = ref(false)

const changeDateVisible = ref(false)
const changeDateValue = ref('')
const changeDateSaving = ref(false)

const isEditable = computed(
  () => !isView.value && (isCreate.value || form.status === 'Draft' || form.status === 'Returned'),
)
const isReadonly = computed(() => !isEditable.value)
const isSmall = computed(() => form.reliefType === 'Small')
const effectiveChannel = computed<PaymentChannel>(
  () => (isSmall.value ? 'Reserve' : form.paymentChannel),
)
const statusText = computed(() => detail.value?.statusText || STATUS_TEXT[form.status] || form.status || '草稿')
const printableId = computed<number | null>(() => (form.id > 0 ? form.id : null))
const canChangeDate = computed(() => !isCreate.value && isEditable.value && form.id > 0)

const populationStatusText = computed(() => {
  if (manualEntry.value && !identity.value) return '待判定'
  return identity.value?.populationStatusText
    ?? detail.value?.populationStatusText
    ?? '待判定'
})

const populationBasis = computed(() => {
  if (identity.value) {
    const source = !manualEntry.value && candidateSummary.value ? `（来源：${candidateSummary.value}）` : ''
    return `${identity.value.basisText}${source}`
  }
  if (detail.value) return `已入库判定：${detail.value.populationStatusText}`
  return '选择申请人或填写身份证号后，系统按身份证反查四类在册台账自动判定。'
})

const manualHitsLedger = computed(
  () => manualEntry.value && identity.value?.isInsured === true,
)

const scenarioFresh = computed(() => {
  const current = detail.value
  if (!current) return false
  const currentPopulation = identity.value?.populationStatus ?? current.populationStatus
  return (
    current.reliefType === form.reliefType
    && current.paymentChannel === effectiveChannel.value
    && current.populationStatus === currentPopulation
  )
})

const channelText = computed(() =>
  effectiveChannel.value === 'County' ? '上级部门发放' : '备用金发放',
)
const categoryDisplayName = computed(() => {
  if (scenarioFresh.value && detail.value?.categoryDisplayName) return detail.value.categoryDisplayName
  return `${populationStatusText.value} · ${isSmall.value ? '小额快救' : `常规救助（${channelText.value}）`}`
})

const scenarioItems = computed(() => {
  const fresh = scenarioFresh.value
  const current = detail.value
  return [
    { label: '大类', value: populationStatusText.value },
    { label: '小类', value: isSmall.value ? '小额快救' : `常规救助·${channelText.value}` },
    { label: '渠道', value: channelText.value },
    {
      label: '时限',
      value: fresh && current
        ? (current.deadlineText || (current.deadlineDate ? `截止 ${formatDate(current.deadlineDate)}` : '未起算'))
        : '保存后按政策生成',
    },
    {
      label: '公示',
      value: isSmall.value
        ? '小额快救免公示'
        : fresh && current
          ? (current.publicizeRequired
            ? `需公示${current.publicizeDays ? ` ${current.publicizeDays} 天` : ''}`
            : '免公示')
          : '保存后生成起止日期',
    },
    {
      label: '经济核对',
      value: fresh && current
        ? (current.economicCheckRequired ? '需经济核对' : '不需经济核对')
        : '保存后由服务端判定',
    },
  ]
})

const familySizeDisplay = computed(() => {
  const count = members.value.filter(
    (row) => row.memberName.trim() && !isHeadMemberRow(row),
  ).length
  return count > 0 ? count + 1 : Math.max(storedFamilySize.value ?? 1, 1)
})

const memberNameOptions = computed(() => {
  const names: string[] = []
  const include = (value: string | null | undefined) => {
    const name = (value ?? '').trim()
    if (name && !names.includes(name)) names.push(name)
  }
  include(applicant.name)
  members.value.forEach((row) => include(row.memberName))
  diseases.value.forEach((row) => include(row.memberName))
  accidents.value.forEach((row) => include(row.memberName))
  return names
})

const beneficiaryOptions = computed<BeneficiaryOption[]>(() => {
  const options: BeneficiaryOption[] = []
  if (applicant.name.trim()) {
    options.push({
      key: HEAD_KEY,
      name: applicant.name.trim(),
      idCard: applicant.idCard.trim(),
      gender: applicant.gender,
      age: applicant.age ?? calculateAge(applicant.idCard),
      relation: '户主',
      isHead: true,
    })
  }

  members.value.forEach((row) => {
    const name = row.memberName.trim()
    if (!name || isHeadMemberRow(row)) return
    const key = row.idCard.trim() ? `id:${row.idCard.trim()}` : `m:${row.key}`
    if (options.some((option) => option.key === key)) return
    options.push({
      key,
      name,
      idCard: row.idCard.trim(),
      gender: row.gender,
      age: row.idCard ? calculateAge(row.idCard) : null,
      relation: row.relation || '家庭成员',
      isHead: false,
    })
  })

  return options
})

const selectedBeneficiary = computed<BeneficiaryOption | null>(
  () => beneficiaryOptions.value.find((option) => option.key === form.beneficiaryKey)
    ?? beneficiaryOptions.value[0]
    ?? null,
)

const selectedTier = computed<TempReliefSmallTier | null>(
  () => smallTiers.value.find((tier) => tier.code === form.standardTier) ?? null,
)

const amountHint = computed(() => {
  if (isSmall.value) {
    if (smallTiersLoading.value) return '正在读取服务端档位上限…'
    if (smallTiersError.value) return smallTiersError.value
    if (smallTiers.value.length === 0) return '未取到小额档位配置，无法填写金额。'
    if (!selectedTier.value) return '请选择 50% / 75% / 100% 档位。'
    return `档位上限 ${formatMoney(selectedTier.value.ceilingAmount)}（${selectedTier.value.personCount} 人 × 城市低保标准 ${formatMoney(selectedTier.value.standardValue)} × ${selectedTier.value.multiplier}）。确定金额可下调，不得超过上限。`
  }
  if (effectiveChannel.value === 'Reserve') {
    return '备用金发放需手工填写金额；上限由服务端按遇困人数与低保月标准校验，超出将拒绝保存。'
  }
  return '常规救助·上级部门发放：金额由上级按程序核定，系统不填写。'
})

const selfPaidTotal = computed(() =>
  diseases.value.reduce((sum, row) => sum + (row.selfPaid ?? 0), 0),
)

const publicizeText = computed(() => {
  if (isSmall.value) return '小额快救免公示'
  if (scenarioFresh.value && detail.value?.publicizeRangeText) return detail.value.publicizeRangeText
  return '保存后由服务端按政策生成'
})

const deadlineText = computed(() => {
  if (!scenarioFresh.value || !detail.value) return '保存后按政策生成'
  return detail.value.deadlineText || (detail.value.deadlineDate ? `截止 ${formatDate(detail.value.deadlineDate)}` : '未起算')
})

const countyConfirmText = computed(() => {
  if (!detail.value) return '保存后生成'
  if (!detail.value.countyConfirmRequired) return '不适用（备用金 / 小额）'
  if (!detail.value.countyConfirmedAt) return '待县确认'
  return `已确认 ${formatDateTime(detail.value.countyConfirmedAt)}${detail.value.countyConfirmedBy ? ` · ${detail.value.countyConfirmedBy}` : ''}`
})

const filingText = computed(() => {
  if (!detail.value) return '保存后生成'
  if (!detail.value.filingRequired) return '不适用（备用金 / 小额）'
  return detail.value.filingStatusText || (detail.value.filingStatus === 'Filed' ? '已备案' : '待备案')
})

const economicCheckText = computed(() => {
  if (!scenarioFresh.value || !detail.value) return '保存后由服务端判定'
  return detail.value.economicCheckRequired ? '需经济核对' : '不需经济核对'
})

const confirmLabel = computed(() => {
  const channel = detail.value?.paymentChannel ?? effectiveChannel.value
  return channel === 'County' ? '县确认' : '乡镇确认'
})

const canReturnAction = computed(() => detail.value?.canReturn === true)
const canRejectAction = computed(() => detail.value?.canReject === true)
const canConfirmAction = computed(() => detail.value?.canConfirm === true)
const canFileAction = computed(() => detail.value?.canMarkFiled === true || detail.value?.canFile === true)
const canStopAction = computed(() => detail.value?.status === 'Confirmed')
const canDeleteAction = computed(() => detail.value?.status === 'Draft')

const actionHint = computed(() => {
  const notes: string[] = []
  if (annualBlocked.value) notes.push('该申请人本年度已存在临时救助申请')
  if (manualHitsLedger.value) notes.push('身份证命中四类在册台账，禁止手工登记，请改用检索选择')
  if (isCreate.value) {
    notes.push('新建申请保存后可执行提交、确认、打印等动作')
  } else if (isReadonly.value) {
    notes.push(`当前状态「${statusText.value}」为只读，仅可查看与打印`)
  } else {
    notes.push('当前可编辑；保存后按状态显示可执行动作')
  }
  return notes.join('；')
})

const returnDisabledReason = computed(() =>
  canReturnAction.value ? '' : '仅已提交状态可退回补正',
)
const rejectDisabledReason = computed(() =>
  canRejectAction.value ? '' : '仅草稿或已提交状态可登记不予救助',
)
const confirmDisabledReason = computed(() =>
  canConfirmAction.value ? '' : '仅草稿或已提交状态可确认',
)
const fileDisabledReason = computed(() =>
  canFileAction.value ? '' : '仅上级部门发放且已确认、待备案时可登记备案',
)
const stopDisabledReason = computed(() =>
  canStopAction.value ? '' : '仅已确认状态可终止',
)
const deleteDisabledReason = computed(() =>
  canDeleteAction.value ? '' : '仅草稿状态可删除',
)

function isHeadMemberRow(row: MemberRow): boolean {
  if (row.idCard.trim() && applicant.idCard.trim()) {
    return row.idCard.trim().toLowerCase() === applicant.idCard.trim().toLowerCase()
  }
  return row.relation.trim() === '户主' && row.memberName.trim() === applicant.name.trim()
}

function isApplicantSnapshot(member: TempReliefMemberSnapshot): boolean {
  if (member.idCard.trim() && applicant.idCard.trim()) {
    return member.idCard.trim().toLowerCase() === applicant.idCard.trim().toLowerCase()
  }
  return member.relation === '户主' && member.memberName.trim() === applicant.name.trim()
}

function ensureCategoryOption(value: string): void {
  const category = normalizeFamilyCategory(value)
  if (category && !familyCategoryOptions.value.includes(category)) {
    familyCategoryOptions.value.push(category)
  }
  applicant.familyCategory = category
}

function scrollToSection(index: number): void {
  activeSection.value = index
  const target = [section1.value, section2.value, section3.value][index]
  target?.scrollIntoView({ behavior: 'smooth', block: 'start' })
}

function backToList(): void {
  void router.push('/social-assistance/temp-relief')
}

function resetAll(): void {
  Object.assign(form, createEmptyForm())
  Object.assign(applicant, createEmptyApplicant())
  members.value = [createMemberRow()]
  diseases.value = []
  accidents.value = []
  educations.value = []
  detail.value = null
  storedFamilySize.value = null
  familyCategoryOptions.value = [...FAMILY_CATEGORY_BASE]
  loading.value = false
  saving.value = false
  actionLoading.value = null
  loadError.value = ''
  autoFilling.value = false
  activeSection.value = 0
  candidateKeyword.value = ''
  candidates.value = []
  candidateSearched.value = false
  candidateSelected.value = false
  candidateSummary.value = ''
  manualEntry.value = false
  identity.value = null
  identityLoading.value = false
  identityError.value = ''
  annualBlocked.value = false
  annualMessage.value = ''
  annualChecking.value = false
  smallTiers.value = []
  smallTiersBase.value = null
  smallTiersLoading.value = false
  smallTiersError.value = ''
  reportUnitPhone.value = ''
  policyItems.value = []
  printVisible.value = false
  photoVisible.value = false
  ocrVisible.value = false
  changeDateVisible.value = false
  changeDateValue.value = ''
  changeDateSaving.value = false
}

function hydrateApplicant(current: TempReliefDetail): void {
  applicant.sourceTable = current.sourceTable
  applicant.sourceFamilyId = current.sourceFamilyId
  applicant.hukouType = current.hukouType
  applicant.name = current.applicantName
  applicant.idCard = current.applicantIdCard
  applicant.gender = current.gender || genderFromIdCard(current.applicantIdCard)
  applicant.age = current.age ?? calculateAge(current.applicantIdCard)
  applicant.phone = current.phone
  applicant.familyProvince = current.familyProvince || '黑龙江省'
  applicant.familyCity = current.familyCity
  applicant.familyDistrict = current.familyDistrict
  applicant.familyTown = current.familyTown || current.town
  applicant.town = current.town || current.familyTown
  applicant.village = current.village
  applicant.detailAddress = current.familyDetail
    || extractDetailFromAddress(current.familyAddress, current.village)
  applicant.hukouProvince = current.hukouProvince || '黑龙江省'
  applicant.hukouCity = current.hukouCity
  applicant.hukouDistrict = current.hukouDistrict
  applicant.hukouTown = current.hukouTown
  applicant.bankName = current.bankName
  applicant.bankAccount = current.bankAccount
  ensureCategoryOption(current.familyCategory)
}

function hydrateMembers(current: TempReliefDetail): void {
  members.value = current.members
    .filter((member) => !isApplicantSnapshot(member))
    .map((member) => ({
      key: nextRowKey(),
      id: member.id,
      memberName: member.memberName,
      gender: member.gender,
      relation: member.relation,
      idCard: member.idCard,
      workUnit: member.workUnit,
      annualIncome: member.annualIncome ?? undefined,
      sortOrder: member.sortOrder,
    }))
  if (members.value.length === 0) members.value = [createMemberRow()]
}

function hydrateDetails(current: TempReliefDetail): void {
  diseases.value = current.diseases.map((disease) => ({
    key: nextRowKey(),
    id: disease.id,
    diseaseName: disease.diseaseName,
    diseaseCode: disease.diseaseCode,
    memberName: disease.memberName,
    hospital: disease.hospital,
    treatStartDate: disease.treatStartDate?.slice(0, 10) ?? null,
    treatEndDate: disease.treatEndDate?.slice(0, 10) ?? null,
    medicalTotal: disease.medicalTotal ?? undefined,
    insurancePaid: disease.insurancePaid ?? undefined,
    selfPaid: disease.selfPaid ?? undefined,
    sortOrder: disease.sortOrder,
  }))

  accidents.value = current.accidents.map((accident) => ({
    key: nextRowKey(),
    id: accident.id,
    accidentType: accident.accidentType,
    memberName: accident.memberName,
    happenDate: accident.happenDate?.slice(0, 10) ?? null,
    happenPlace: accident.happenPlace,
    injurySituation: accident.injurySituation,
    propertyLoss: accident.propertyLoss ?? undefined,
    compensationPaid: accident.compensationPaid ?? undefined,
    responsibilityDesc: accident.responsibilityDesc,
    materialDesc: accident.materialDesc,
    sortOrder: accident.sortOrder,
  }))

  educations.value = current.educations.map((education) => ({
    key: nextRowKey(),
    id: education.id,
    studentName: education.studentName,
    educationStage: education.educationStage,
    schoolName: education.schoolName,
    schoolDuration: education.schoolDuration ?? undefined,
    tuitionFee: education.tuitionFee ?? undefined,
    feeDate: education.feeDate?.slice(0, 10) ?? todayText(),
    sortOrder: education.sortOrder,
  }))
}

function resolveBeneficiaryKey(preferredIdCard?: string | null): void {
  const idCard = (preferredIdCard ?? '').trim()
  if (idCard && applicant.idCard.trim() && idCard === applicant.idCard.trim()) {
    form.beneficiaryKey = HEAD_KEY
    return
  }
  if (idCard) {
    const match = members.value.find((row) => row.idCard.trim() === idCard)
    if (match) {
      form.beneficiaryKey = `id:${match.idCard.trim()}`
      return
    }
  }
  form.beneficiaryKey = HEAD_KEY
}

function applyDetail(current: TempReliefDetail): void {
  detail.value = current
  form.id = current.id
  form.applicationNo = current.applicationNo
  form.status = current.status
  form.reliefType = current.reliefType === 'Small' ? 'Small' : 'Large'
  form.paymentChannel = current.paymentChannel === 'Reserve' ? 'Reserve' : 'County'
  form.applyDate = current.applyDate?.slice(0, 10) ?? todayText()
  form.reportUnit = current.reportUnit
  form.reportTime = current.reportTime
  form.difficultyType = normalizeDifficulty(current.difficultyType)
  form.difficultyReason = current.difficultyReason
  form.policyEnjoyed = current.policyEnjoyed
  form.familyMemberStatus = current.familyMemberStatus
  form.verifyResult = current.verifyResult
  form.acceptancePerson = current.acceptancePerson
  form.standardTier = current.standardTier ?? ''
  form.confirmAmount = current.confirmAmount ?? undefined
  form.reliefPersonCount = Math.max(
    current.reliefPersonCount || current.familySize || 1,
    1,
  )
  form.publicizeStartDate = current.publicizeStartDate?.slice(0, 10) ?? null
  form.publicizeEndDate = current.publicizeEndDate?.slice(0, 10) ?? null
  storedFamilySize.value = current.familySize

  hydrateApplicant(current)
  hydrateMembers(current)
  hydrateDetails(current)
  resolveBeneficiaryKey(current.beneficiaryIdCard)

  manualEntry.value = current.isManualEntry
  candidateSelected.value = !current.isManualEntry && Boolean(current.sourceTable)
  candidateSummary.value = current.isManualEntry
    ? '非在册手工登记'
    : `已选择：${current.applicantName}（${current.sourceName}）`
  policyItems.value = []

  if (form.reliefType === 'Small') void loadSmallTiers()
}

async function loadDetail(options: { silent?: boolean } = {}): Promise<void> {
  const id = form.id > 0 ? form.id : Number(applicationId.value)
  if (!id) return

  if (!options.silent) {
    loading.value = true
    loadError.value = ''
  }

  try {
    const current = await getTempReliefApplication(id)
    applyDetail(current)
    await Promise.allSettled([
      refreshIdentity(true),
      loadReportUnitPhone(),
      runAnnualCheck(true),
    ])
    if (!current.policyEnjoyed || !current.familyMemberStatus) {
      void loadPolicyAndHealth(current.applicantIdCard, false)
    }
  } catch (error) {
    const message = getErrorMessage(error, '临时救助档案加载失败')
    if (options.silent) ElMessage.warning(message)
    else loadError.value = message
  } finally {
    if (!options.silent) loading.value = false
  }
}

async function refreshIdentity(silent = false): Promise<void> {
  const idCard = applicant.idCard.trim()
  const ticket = ++identityTicket
  identityLoading.value = true
  identityError.value = ''
  if (!idCard) {
    identity.value = null
    identityLoading.value = false
    return
  }

  try {
    const result = await getTempReliefInsuredIdentity(idCard)
    if (ticket !== identityTicket) return
    identity.value = result
    if (manualEntry.value && result.isInsured && !silent) {
      ElMessage.warning(result.basisText + '，请改用检索选择该申请人')
    }
  } catch (error) {
    if (ticket !== identityTicket) return
    identity.value = null
    identityError.value = getErrorMessage(error, '保障人群判定失败，保存时服务端将重新判定')
    if (!silent) ElMessage.error(identityError.value)
  } finally {
    if (ticket === identityTicket) identityLoading.value = false
  }
}

async function runAnnualCheck(silent = false): Promise<void> {
  const idCard = applicant.idCard.trim()
  const year = form.applyDate.slice(0, 4)
  if (!idCard || !year) {
    annualBlocked.value = false
    annualMessage.value = ''
    return
  }

  const ticket = ++annualTicket
  annualChecking.value = true
  try {
    const exists = await checkTempReliefAnnual(idCard, year, form.id > 0 ? form.id : undefined)
    if (ticket !== annualTicket) return
    annualBlocked.value = exists
    annualMessage.value = exists
      ? `该申请人 ${year} 年已存在临时救助申请，同一身份证每年仅限一次。`
      : ''
  } catch (error) {
    if (ticket !== annualTicket) return
    annualBlocked.value = false
    annualMessage.value = ''
    if (!silent) {
      ElMessage.warning(getErrorMessage(error, '年度唯一校验失败，保存时以服务端校验为准'))
    }
  } finally {
    if (ticket === annualTicket) annualChecking.value = false
  }
}

async function loadPolicyAndHealth(idCard: string, overwrite: boolean): Promise<void> {
  const id = idCard.trim()
  if (!id) return

  try {
    const policy = await getTempReliefPolicySnapshot(id)
    policyItems.value = policy
    if ((overwrite || !form.policyEnjoyed.trim()) && policy.length > 0) {
      form.policyEnjoyed = `${policy.join('；')}。`
    }
  } catch {
    if (policyItems.value.length === 0) policyItems.value = []
  }

  try {
    const status = await getTempReliefFamilyStatus(id)
    if ((overwrite || !form.familyMemberStatus.trim()) && status) {
      form.familyMemberStatus = status
    }
  } catch {
    // 健康摘要失败不阻断录入，用户可手工补录。
  }
}

async function loadReportUnitPhone(): Promise<void> {
  const unit = form.reportUnit.trim()
  if (!unit) {
    reportUnitPhone.value = ''
    return
  }
  try {
    reportUnitPhone.value = await getTempReliefReportUnitPhone(unit)
  } catch {
    reportUnitPhone.value = ''
  }
}

async function loadSmallTiers(): Promise<void> {
  const ticket = ++tierTicket
  if (form.reliefType !== 'Small') {
    smallTiers.value = []
    smallTiersBase.value = null
    smallTiersError.value = ''
    smallTiersLoading.value = false
    return
  }

  const previousCeiling = selectedTier.value?.ceilingAmount
  smallTiersLoading.value = true
  smallTiersError.value = ''
  try {
    const response = await getTempReliefSmallTiers(Math.max(form.reliefPersonCount, 1))
    if (ticket !== tierTicket) return
    smallTiers.value = response.items ?? []
    smallTiersBase.value = response.baseStandard
    if (form.standardTier && !smallTiers.value.some((tier) => tier.code === form.standardTier)) {
      form.standardTier = ''
    }
    if (!form.standardTier) form.confirmAmount = undefined
    else {
      const tier = smallTiers.value.find((item) => item.code === form.standardTier)
      if (tier && (
        form.confirmAmount === undefined
        || form.confirmAmount === 0
        || form.confirmAmount === previousCeiling
      )) {
        form.confirmAmount = tier.ceilingAmount
      }
    }
  } catch (error) {
    if (ticket !== tierTicket) return
    smallTiers.value = []
    smallTiersBase.value = null
    smallTiersError.value = getErrorMessage(error, '小额档位加载失败')
  } finally {
    if (ticket === tierTicket) smallTiersLoading.value = false
  }
}

async function searchCandidates(): Promise<void> {
  const keyword = candidateKeyword.value.trim()
  if (!keyword) {
    ElMessage.warning('请输入姓名或身份证号进行检索')
    return
  }

  candidateLoading.value = true
  try {
    candidates.value = await searchTempReliefCandidates(keyword)
    candidateSearched.value = true
    if (candidates.value.length === 0) {
      ElMessage.info('未检索到符合条件的申请人；可转非在册手工登记。')
    }
  } catch (error) {
    candidates.value = []
    candidateSearched.value = true
    ElMessage.error(getErrorMessage(error, '申请人检索失败'))
  } finally {
    candidateLoading.value = false
  }
}

function applyCandidateDetail(current: TempReliefCandidate): void {
  applicant.sourceTable = current.sourceTable
  applicant.sourceFamilyId = current.sourceFamilyId
  applicant.hukouType = current.hukouType
  applicant.name = current.name
  applicant.idCard = current.idCard
  applicant.gender = current.gender || genderFromIdCard(current.idCard)
  applicant.age = calculateAge(current.idCard)
  applicant.phone = current.phone
  applicant.familyProvince = current.province || '黑龙江省'
  applicant.familyCity = current.city
  applicant.familyDistrict = current.district
  applicant.familyTown = current.town
  applicant.town = current.town
  applicant.village = current.village || '村委会'
  applicant.detailAddress = current.detailAddress
    || extractDetailFromAddress(current.familyAddress, current.village)
  applicant.hukouProvince = '黑龙江省'
  applicant.bankName = current.bankName
  applicant.bankAccount = current.oneCardAccount || current.bankAccount
  ensureCategoryOption(current.familyCategory)
  storedFamilySize.value = current.familySize
  form.reliefPersonCount = 1

  members.value = current.members
    .filter((member) => !isApplicantSnapshot(member))
    .map((member) => ({
      key: nextRowKey(),
      id: 0,
      memberName: member.memberName,
      gender: member.gender,
      relation: member.relation,
      idCard: member.idCard,
      workUnit: member.workUnit,
      annualIncome: member.annualIncome ?? undefined,
      sortOrder: 0,
    }))
  if (members.value.length === 0) members.value = [createMemberRow()]

  resolveBeneficiaryKey(current.matchedMemberIdCard)
}

async function selectCandidate(candidate: TempReliefCandidate): Promise<void> {
  if (candidate.hasAppliedThisYear) {
    ElMessage.warning('该申请人本年度已申请过临时救助，每年仅限一次')
    return
  }

  candidateLoading.value = true
  autoFilling.value = true
  try {
    const current = await getTempReliefCandidate(candidate.sourceTable, candidate.sourceFamilyId)
    if (current.hasAppliedThisYear) {
      ElMessage.warning('该申请人本年度已申请过临时救助，每年仅限一次')
      return
    }

    manualEntry.value = false
    identity.value = null
    annualBlocked.value = false
    annualMessage.value = ''
    applyCandidateDetail(current)
    candidateSelected.value = true
    candidateSummary.value = current.matchedPersonInfo
      ? `已选择：${current.name}（${current.sourceName}）· ${current.matchedPersonInfo}`
      : `已选择：${current.name}（${current.sourceName}）`
    candidates.value = []
    candidateKeyword.value = ''

    if (!applicant.hukouType) {
      try {
        applicant.hukouType = await resolveTempReliefHukouType(current.idCard)
      } catch {
        applicant.hukouType = ''
      }
    }

    if (form.reliefType === 'Small') await loadSmallTiers()
    await Promise.allSettled([
      refreshIdentity(true),
      runAnnualCheck(true),
      loadPolicyAndHealth(current.idCard, false),
      loadReportUnitPhone(),
    ])
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '选择申请人失败'))
  } finally {
    candidateLoading.value = false
    autoFilling.value = false
  }
}

function enterManualEntry(): void {
  if (!isEditable.value) return
  manualEntry.value = true
  candidateSelected.value = false
  candidateSummary.value = '非在册手工登记'
  Object.assign(applicant, createEmptyApplicant())
  storedFamilySize.value = null
  form.reliefPersonCount = 1
  members.value = [createMemberRow()]
  identity.value = null
  identityError.value = ''
  annualBlocked.value = false
  annualMessage.value = ''
  candidates.value = []
  candidateSearched.value = false
}

function backToSearch(): void {
  if (!isEditable.value) return
  manualEntry.value = false
  candidateSelected.value = false
  candidateSummary.value = ''
  Object.assign(applicant, createEmptyApplicant())
  storedFamilySize.value = null
  form.reliefPersonCount = 1
  members.value = [createMemberRow()]
  identity.value = null
  identityError.value = ''
  annualBlocked.value = false
  annualMessage.value = ''
}

function handleManualIdInput(): void {
  if (!manualEntry.value) return
  identity.value = null
  identityError.value = ''
  annualBlocked.value = false
  annualMessage.value = ''
}

async function handleManualIdBlur(): Promise<void> {
  if (!manualEntry.value) return
  const id = applicant.idCard.trim()
  if (!isValidIdCard(id)) return

  if (!applicant.gender) applicant.gender = genderFromIdCard(id)
  if (applicant.age === null) applicant.age = calculateAge(id)

  if (!applicant.hukouType) {
    try {
      applicant.hukouType = await resolveTempReliefHukouType(id)
    } catch {
      applicant.hukouType = ''
    }
  }

  await Promise.allSettled([
    refreshIdentity(false),
    runAnnualCheck(false),
    loadPolicyAndHealth(id, false),
  ])
}

function handleApplyDateChange(): void {
  void runAnnualCheck(false)
}

function handleReliefTypeChange(next: ReliefType): void {
  if (!isEditable.value || form.reliefType === next) return
  form.reliefType = next
  form.standardTier = ''
  form.confirmAmount = undefined
  form.publicizeStartDate = null
  form.publicizeEndDate = null
  if (next === 'Small') {
    form.paymentChannel = 'Reserve'
    void loadSmallTiers()
  } else {
    void loadSmallTiers()
  }
}

function handlePaymentChannelChange(next: PaymentChannel): void {
  form.paymentChannel = next
  if (next === 'County') {
    form.confirmAmount = undefined
    form.standardTier = ''
  }
}

function handlePersonCountChange(next: number | undefined): void {
  const value = Math.max(Math.min(next ?? 1, familySizeDisplay.value), 1)
  form.reliefPersonCount = value
  if (isSmall.value) void loadSmallTiers()
}

function handleTierChange(next: string | number | boolean | undefined): void {
  const code = String(next ?? '')
  form.standardTier = code
  const tier = smallTiers.value.find((item) => item.code === code)
  form.confirmAmount = tier ? tier.ceilingAmount : undefined
}

function handleMemberChanged(): void {
  if (form.reliefPersonCount > familySizeDisplay.value) {
    form.reliefPersonCount = familySizeDisplay.value
  }
  if (!beneficiaryOptions.value.some((option) => option.key === form.beneficiaryKey)) {
    form.beneficiaryKey = HEAD_KEY
  }
}

function addMember(): void {
  if (members.value.length >= MAX_MEMBER_ROWS) {
    ElMessage.warning(`最多填写 ${MAX_MEMBER_ROWS} 名家庭成员`)
    return
  }
  members.value.push(createMemberRow())
}

function removeMember(index: number): void {
  if (members.value.length <= 1) {
    ElMessage.warning('至少保留一名家庭成员行')
    return
  }
  members.value.splice(index, 1)
  handleMemberChanged()
}

function addDisease(): void {
  if (diseases.value.length >= MAX_DETAIL_ROWS) {
    ElMessage.warning(`最多填写 ${MAX_DETAIL_ROWS} 条疾病明细`)
    return
  }
  diseases.value.push(createDiseaseRow())
}

function removeDisease(index: number): void {
  diseases.value.splice(index, 1)
}

function addAccident(): void {
  if (accidents.value.length >= MAX_DETAIL_ROWS) {
    ElMessage.warning(`最多填写 ${MAX_DETAIL_ROWS} 条意外灾害明细`)
    return
  }
  accidents.value.push(createAccidentRow())
}

function removeAccident(index: number): void {
  accidents.value.splice(index, 1)
}

function addEducation(): void {
  if (educations.value.length >= MAX_DETAIL_ROWS) {
    ElMessage.warning(`最多填写 ${MAX_DETAIL_ROWS} 条教育支出明细`)
    return
  }
  educations.value.push(createEducationRow())
}

function removeEducation(index: number): void {
  educations.value.splice(index, 1)
}

function generateReason(): void {
  const head = applicant.name.trim() ? `申请人家庭成员${applicant.name.trim()}` : '申请人家庭成员'
  if (form.difficultyType === '疾病') {
    const items = diseases.value
      .filter((row) => row.diseaseName.trim() || row.diseaseCode.trim())
      .map((row) => row.diseaseName.trim() || row.diseaseCode.trim())
    form.difficultyReason = items.length > 0
      ? `${head}因患${items.join('、')}，医疗费用支出较大，造成家庭基本生活暂时陷入困境，特申请临时救助。`
      : `${head}因患疾病，医疗费用支出较大，造成家庭基本生活暂时陷入困境，特申请临时救助。`
    return
  }
  if (form.difficultyType === '意外灾害') {
    const items = accidents.value
      .filter((row) => row.accidentType.trim() || row.happenPlace.trim())
      .map((row) => [row.accidentType.trim(), formatDate(row.happenDate), row.happenPlace.trim()]
        .filter((part) => part && part !== '—')
        .join('，'))
      .filter(Boolean)
    form.difficultyReason = items.length > 0
      ? `${head}因${items.join('；')}，家庭基本生活暂时陷入困境，特申请临时救助。`
      : `${head}因意外灾害，家庭基本生活暂时陷入困境，特申请临时救助。`
    return
  }
  if (form.difficultyType === '教育支出') {
    const items = educations.value
      .filter((row) => row.studentName.trim() || row.educationStage.trim() || row.schoolName.trim())
      .map((row) => [row.studentName.trim(), row.educationStage.trim(), row.schoolName.trim()]
        .filter(Boolean)
        .join('，'))
      .filter(Boolean)
    form.difficultyReason = items.length > 0
      ? `${head}因家庭子女就学，${items.join('；')}，学费等教育支出较大，家庭基本生活暂时陷入困境，特申请临时救助。`
      : `${head}因家庭子女就学，教育支出较大，造成家庭基本生活暂时陷入困境，特申请临时救助。`
    return
  }
  form.difficultyReason = `申请人家庭为${applicant.familyCategory || '困难群众'}对象，因突发困难导致基本生活暂时陷入困境，特申请临时救助。`
}

function buildFamilyAddress(): string {
  const village = applicant.village === '村委会' ? '' : applicant.village.trim()
  return [
    applicant.familyDistrict.trim(),
    applicant.familyTown.trim(),
    village,
    applicant.detailAddress.trim(),
  ].filter(Boolean).join('')
}

function buildHukouAddress(): string {
  return [
    applicant.hukouProvince.trim(),
    applicant.hukouCity.trim(),
    applicant.hukouDistrict.trim(),
    applicant.hukouTown.trim(),
  ].filter(Boolean).join('')
}

function validateForm(): string | null {
  if (!manualEntry.value && !candidateSelected.value && !applicant.name.trim()) {
    return '请先检索并选择申请人；检索不到可转非在册手工登记'
  }
  if (manualHitsLedger.value) {
    return identity.value?.basisText
      ? `${identity.value.basisText}，请改用检索选择该申请人`
      : '该身份证命中四类在册台账，请改用检索选择该申请人'
  }
  if (!applicant.name.trim()) return '请填写申请人姓名'
  if (!applicant.idCard.trim()) return '请填写申请人身份证号'
  if (!isValidIdCard(applicant.idCard)) return '申请人身份证号格式不正确'
  if (annualBlocked.value) return annualMessage.value || '该申请人本年度已存在临时救助申请'
  if (!form.applyDate) return '请选择申请日期'
  if (form.applyDate > maxApplyDateText()) {
    return `申请日期最晚为 ${maxApplyDateText()}`
  }
  if (form.reliefPersonCount <= 0) return '家庭遇困人数（含户主）须大于 0'
  if (form.reliefPersonCount > familySizeDisplay.value) {
    return `家庭遇困人数不能大于家庭人口（${familySizeDisplay.value} 人）`
  }
  if (!selectedBeneficiary.value) return '请选择救助对象'
  if (!form.difficultyReason.trim()) return '请填写申请救助原因 / 困难情况'

  if (form.difficultyType === '疾病') {
    if (diseases.value.length === 0) return '困难类型为疾病，请至少添加一条疾病明细'
    if (diseases.value.some((row) => !row.diseaseName.trim() && !row.diseaseCode.trim())) {
      return '请填写每条疾病明细的诊断结果或疾病编码'
    }
    if (diseases.value.some(
      (row) => (row.medicalTotal ?? 0) < 0 || (row.insurancePaid ?? 0) < 0 || (row.selfPaid ?? 0) < 0,
    )) {
      return '疾病明细金额不能为负数'
    }
  }

  if (form.difficultyType === '意外灾害') {
    if (accidents.value.length === 0) return '困难类型为意外灾害，请至少添加一条意外灾害明细'
    if (accidents.value.some((row) => !row.accidentType.trim())) {
      return '请填写每条意外灾害的类型'
    }
  }

  if (form.difficultyType === '教育支出') {
    if (educations.value.length === 0) return '困难类型为教育支出，请至少添加一条教育支出明细'
    if (educations.value.some((row) => !row.studentName.trim() && !row.educationStage.trim())) {
      return '请填写每条教育支出明细的学生姓名或就学阶段'
    }
  }

  if (isSmall.value) {
    if (!form.standardTier) return '请选择小额快救档位（50% / 75% / 100%）'
    if (!form.confirmAmount || form.confirmAmount <= 0) return '确定金额须为大于 0 的数字'
    if (selectedTier.value && form.confirmAmount > selectedTier.value.ceilingAmount) {
      return `确定金额不能超过${selectedTier.value.label}档上限 ${formatMoney(selectedTier.value.ceilingAmount)}`
    }
  } else if (effectiveChannel.value === 'Reserve') {
    if (!form.confirmAmount || form.confirmAmount <= 0) return '备用金发放请填写救助金额'
  }

  return null
}

function buildRequest(): TempReliefSaveRequest {
  const beneficiary = selectedBeneficiary.value
  const currentType = form.difficultyType
  return {
    reliefType: form.reliefType,
    populationStatus: identity.value?.populationStatus
      ?? detail.value?.populationStatus
      ?? null,
    paymentChannel: effectiveChannel.value,
    sourceTable: manualEntry.value ? '' : applicant.sourceTable,
    sourceFamilyId: manualEntry.value ? 0 : applicant.sourceFamilyId,
    hukouType: applicant.hukouType,
    applicantName: applicant.name.trim(),
    applicantIdCard: applicant.idCard.trim(),
    gender: applicant.gender,
    age: applicant.age,
    phone: applicant.phone.trim(),
    beneficiaryName: beneficiary?.name ?? null,
    beneficiaryIdCard: beneficiary?.idCard ?? null,
    beneficiaryGender: beneficiary?.gender ?? null,
    beneficiaryAge: beneficiary?.age ?? null,
    beneficiaryRelation: beneficiary?.relation ?? null,
    familyProvince: applicant.familyProvince.trim(),
    familyCity: applicant.familyCity.trim(),
    familyDistrict: applicant.familyDistrict.trim(),
    familyTown: applicant.familyTown.trim(),
    familyDetail: applicant.detailAddress.trim(),
    familyAddress: buildFamilyAddress(),
    hukouProvince: applicant.hukouProvince.trim(),
    hukouCity: applicant.hukouCity.trim(),
    hukouDistrict: applicant.hukouDistrict.trim(),
    hukouTown: applicant.hukouTown.trim(),
    hukouAddress: buildHukouAddress(),
    town: (applicant.town || applicant.familyTown).trim(),
    village: applicant.village.trim(),
    familySize: familySizeDisplay.value,
    reliefPersonCount: form.reliefPersonCount,
    familyCategory: applicant.familyCategory,
    bankName: applicant.bankName.trim(),
    bankAccount: applicant.bankAccount.trim(),
    policyEnjoyed: form.policyEnjoyed,
    familyMemberStatus: form.familyMemberStatus,
    difficultyReason: form.difficultyReason.trim(),
    difficultyType: currentType,
    applyDate: form.applyDate || null,
    reportUnit: form.reportUnit.trim(),
    reportTime: form.reportTime,
    smallAmountLevel: isSmall.value && selectedTier.value ? `${selectedTier.value.label}档` : '',
    standardTier: isSmall.value ? form.standardTier || null : null,
    confirmAmount: isSmall.value || effectiveChannel.value === 'Reserve'
      ? form.confirmAmount ?? null
      : null,
    publicizeStartDate: isSmall.value ? null : form.publicizeStartDate,
    publicizeEndDate: isSmall.value ? null : form.publicizeEndDate,
    verifyResult: form.verifyResult,
    acceptancePerson: form.acceptancePerson.trim(),
    members: members.value
      .filter((row) => row.memberName.trim())
      .filter((row) => !isHeadMemberRow(row))
      .map((row, index) => ({
        id: row.id,
        memberName: row.memberName.trim(),
        gender: row.gender,
        relation: row.relation,
        idCard: row.idCard.trim(),
        workUnit: row.workUnit.trim(),
        annualIncome: row.annualIncome ?? null,
        sortOrder: index,
      })),
    diseases: currentType === '疾病'
      ? diseases.value.map((row, index) => ({
        id: row.id,
        diseaseName: row.diseaseName.trim(),
        diseaseCode: row.diseaseCode.trim(),
        memberName: row.memberName,
        hospital: row.hospital.trim(),
        treatStartDate: row.treatStartDate,
        treatEndDate: row.treatEndDate,
        medicalTotal: row.medicalTotal ?? null,
        insurancePaid: row.insurancePaid ?? null,
        selfPaid: row.selfPaid ?? null,
        sortOrder: index,
      }))
      : [],
    accidents: currentType === '意外灾害'
      ? accidents.value.map((row, index) => ({
        id: row.id,
        accidentType: row.accidentType,
        memberName: row.memberName,
        happenDate: row.happenDate,
        happenPlace: row.happenPlace.trim(),
        injurySituation: row.injurySituation.trim(),
        propertyLoss: row.propertyLoss ?? null,
        compensationPaid: row.compensationPaid ?? null,
        responsibilityDesc: row.responsibilityDesc.trim(),
        materialDesc: row.materialDesc.trim(),
        sortOrder: index,
      }))
      : [],
    educations: currentType === '教育支出'
      ? educations.value.map((row, index): TempReliefEducationInput => ({
        id: row.id,
        studentName: row.studentName.trim(),
        educationStage: row.educationStage,
        schoolName: row.schoolName.trim(),
        schoolDuration: row.schoolDuration ?? null,
        tuitionFee: row.tuitionFee ?? null,
        feeDate: row.feeDate,
        sortOrder: index,
      }))
      : [],
  }
}

async function persist(): Promise<number | null> {
  if (!isEditable.value) {
    ElMessage.warning('当前状态不可编辑')
    return null
  }
  const validation = validateForm()
  if (validation) {
    ElMessage.warning(validation)
    return null
  }

  saving.value = true
  try {
    const request = buildRequest()
    let id = form.id
    if (id > 0) {
      await updateTempReliefApplication(id, request)
    } else {
      const created = await createTempReliefApplication(request)
      id = created.id
      form.id = created.id
      if (isCreate.value) {
        routeReloadSuppressed = true
        try {
          await router.replace(`/social-assistance/temp-relief/${created.id}/edit`)
        } finally {
          routeReloadSuppressed = false
        }
      }
    }

    await loadDetail({ silent: true })
    return id
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '保存临时救助申请失败'))
    return null
  } finally {
    saving.value = false
  }
}

async function saveDraft(): Promise<void> {
  const id = await persist()
  if (id === null) return
  ElMessage.success(form.status === 'Returned' ? '已保存，申请仍为退回补正状态' : '草稿已保存')
}

async function saveAndSubmit(): Promise<void> {
  const id = await persist()
  if (id === null) return

  actionLoading.value = 'submit'
  try {
    await submitTempReliefApplication(id)
    ElMessage.success('保存并提交成功，已进入办理时限')
    await loadDetail({ silent: true })
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '提交失败，已保存当前修改'))
    await loadDetail({ silent: true })
  } finally {
    actionLoading.value = null
  }
}

async function saveAndPrint(): Promise<void> {
  const id = await persist()
  if (id === null) return
  printVisible.value = true
}

function openPrint(): void {
  if (form.id <= 0) {
    ElMessage.warning('请先保存申请后再打印')
    return
  }
  printVisible.value = true
}

function openChangeDate(): void {
  if (!canChangeDate.value) return
  changeDateValue.value = form.applyDate
  changeDateVisible.value = true
}

async function submitChangeDate(): Promise<void> {
  const value = changeDateValue.value
  if (!value) {
    ElMessage.warning('请选择新的申请日期')
    return
  }
  if (value > maxApplyDateText()) {
    ElMessage.warning(`申请日期最晚为 ${maxApplyDateText()}`)
    return
  }
  if (value === form.applyDate) {
    changeDateVisible.value = false
    return
  }

  changeDateSaving.value = true
  try {
    const result = await changeTempReliefApplyDate(form.id, value)
    form.applyDate = value
    if (result.applicationNo) form.applicationNo = result.applicationNo
    changeDateVisible.value = false
    ElMessage.success(
      result.applicationNo
        ? `已改期，新申请编号 ${result.applicationNo}`
        : '申请日期已修改',
    )
    await loadDetail({ silent: true })
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '改期失败'))
  } finally {
    changeDateSaving.value = false
  }
}

async function runStatusAction(key: ActionKey): Promise<void> {
  if (form.id <= 0) return

  if (key === 'delete') {
    try {
      await ElMessageBox.confirm(
        `确定要删除申请 [${form.applicationNo}] ${applicant.name} 吗？\n此操作不可恢复。`,
        '确认删除',
        { confirmButtonText: '删除', cancelButtonText: '取消', type: 'warning' },
      )
    } catch {
      return
    }

    actionLoading.value = 'delete'
    try {
      await deleteTempReliefApplication(form.id)
      ElMessage.success('临时救助申请已删除')
      backToList()
    } catch (error) {
      ElMessage.error(getErrorMessage(error, '删除申请失败'))
    } finally {
      actionLoading.value = null
    }
    return
  }

  let reason = ''
  if (key === 'return' || key === 'reject' || key === 'stop') {
    const title = key === 'return' ? '退回补正' : key === 'reject' ? '不予救助' : '终止申请'
    const confirmText = key === 'return' ? '退回' : key === 'reject' ? '确定' : '终止'
    const placeholder = key === 'return'
      ? '如：材料不全 / 信息需核实'
      : key === 'reject'
        ? '如：不符合救助条件'
        : '请填写终止原因'
    try {
      const result = await ElMessageBox.prompt(
        key === 'return'
          ? `请填写 [${form.applicationNo}] ${applicant.name} 的退回补正原因：`
          : key === 'reject'
            ? `请填写 [${form.applicationNo}] ${applicant.name} 的不予救助理由：`
            : `确定要终止 [${form.applicationNo}] ${applicant.name} 的临时救助申请吗？\n终止后本年度仍占用唯一申请名额。`,
        title,
        {
          confirmButtonText: confirmText,
          cancelButtonText: '取消',
          inputPlaceholder: placeholder,
          inputValidator: (value: string) => Boolean(value && value.trim()) || `请填写${
            key === 'return' ? '退回补正原因' : key === 'reject' ? '不予救助理由' : '终止原因'
          }`,
          type: 'warning',
        },
      )
      reason = result.value.trim()
    } catch {
      return
    }
  } else {
    const label = key === 'confirm' ? confirmLabel.value : key === 'file' ? '登记备案' : '提交申请'
    try {
      await ElMessageBox.confirm(
        key === 'confirm'
          ? `确定要${label} [${form.applicationNo}] ${applicant.name} 的临时救助申请吗？\n${
            label === '县确认' ? '县确认后进入待备案。' : '乡镇确认后即生效。'
          }`
          : key === 'file'
            ? `确定 [${form.applicationNo}] ${applicant.name} 已完成备案登记吗？`
            : `确定提交 [${form.applicationNo}] ${applicant.name} 的临时救助申请吗？`,
        label,
        {
          confirmButtonText: label,
          cancelButtonText: '取消',
          type: key === 'file' ? 'info' : 'warning',
        },
      )
    } catch {
      return
    }
  }

  actionLoading.value = key
  try {
    if (key === 'return') {
      await returnTempReliefApplication(form.id, reason)
      ElMessage.success('已退回补正')
    } else if (key === 'reject') {
      await rejectTempReliefApplication(form.id, {
        reason,
        notifiedAt: new Date().toISOString(),
        noticeMethod: '书面',
      })
      ElMessage.success('已登记不予救助')
    } else if (key === 'stop') {
      await stopTempReliefApplication(form.id, reason)
      ElMessage.success('临时救助申请已终止')
    } else if (key === 'confirm') {
      await confirmTempReliefApplication(form.id)
      ElMessage.success(`已${confirmLabel.value}`)
    } else if (key === 'file') {
      await fileTempReliefApplication(form.id)
      ElMessage.success('已登记备案')
    } else if (key === 'submit') {
      await submitTempReliefApplication(form.id)
      ElMessage.success('临时救助申请已提交')
    }
    await loadDetail({ silent: true })
  } catch (error) {
    const fallback: Record<ActionKey, string> = {
      submit: '提交申请失败',
      return: '退回补正失败',
      reject: '登记不予救助失败',
      confirm: `${confirmLabel.value}失败`,
      file: '登记备案失败',
      stop: '终止申请失败',
      delete: '删除申请失败',
    }
    ElMessage.error(getErrorMessage(error, fallback[key]))
  } finally {
    actionLoading.value = null
  }
}

async function handleOcrResult(result: TempReliefOcrResponse): Promise<void> {
  const originalIdCard = applicant.idCard.trim()
  const emptyFields: string[] = []
  const conflicts: Array<{ label: string; current: string; next: string }> = []

  const pending: Array<() => void> = []
  const assign = (label: string, current: string, next: string, apply: () => void) => {
    if (!next || next === current) return
    if (!current.trim()) {
      apply()
      emptyFields.push(label)
      return
    }
    conflicts.push({ label, current, next })
    pending.push(apply)
  }

  assign('姓名', applicant.name, result.name, () => { applicant.name = result.name })
  assign('身份证号', applicant.idCard, result.idCard.toUpperCase(), () => {
    applicant.idCard = result.idCard.toUpperCase()
  })
  assign('住址（详细地址）', applicant.detailAddress, stripKnownAddressPrefix(result.address), () => {
    applicant.detailAddress = stripKnownAddressPrefix(result.address)
  })
  assign('性别', applicant.gender, result.gender, () => { applicant.gender = result.gender })

  if (conflicts.length > 0) {
    const message = conflicts
      .map((item) => `${item.label}：\n  当前「${item.current || '空'}」\n  识别「${item.next}」`)
      .join('\n\n')
    let overwrite = false
    try {
      await ElMessageBox.confirm(
        `识别结果与现有内容不同：\n\n${message}\n\n是否覆盖这些字段？选择「仅填空」将保留现有内容。`,
        '确认回填识别结果',
        {
          confirmButtonText: '覆盖',
          cancelButtonText: '仅填空',
          distinguishCancelAndClose: true,
          type: 'warning',
        },
      )
      overwrite = true
    } catch (error) {
      if (error === 'close') return
    }
    if (overwrite) {
      pending.forEach((apply) => apply())
      emptyFields.push(...conflicts.map((item) => item.label))
    }
  }

  if (applicant.age === null && result.birthDate) {
    applicant.age = ageFromBirthDate(result.birthDate)
  }
  if (!applicant.gender) applicant.gender = result.gender || genderFromIdCard(applicant.idCard)

  if (emptyFields.length === 0) {
    ElMessage.info('识别结果与现有内容一致，无需回填')
    return
  }

  ElMessage.success(`已回填：${Array.from(new Set(emptyFields)).join('、')}`)
  await Promise.allSettled([
    refreshIdentity(true),
    runAnnualCheck(true),
    loadPolicyAndHealth(applicant.idCard, false),
  ])

  if (!manualEntry.value && originalIdCard && applicant.idCard.trim() !== originalIdCard) {
    ElMessage.warning('身份证号与检索来源不一致，服务端将拒绝保存；请重新检索选择申请人。')
  }
}

function handlePhotoChanged(): void {
  // 照片增删后无需改表单数据；保留通知入口便于后续扩展。
}

function refreshPage(): void {
  if (isCreate.value) {
    void loadDetail()
    return
  }
  void loadDetail()
}

function hydrateForCreate(): void {
  form.reportUnit = auth.user?.organizationName ?? ''
  form.applyDate = todayText()
  form.reportTime = null
  form.reliefPersonCount = 1
  void loadReportUnitPhone()
}

async function load(): Promise<void> {
  resetAll()

  if (isCreate.value) {
    hydrateForCreate()
    return
  }

  if (!applicationId.value) {
    loadError.value = '缺少档案编号'
    return
  }

  await loadDetail()
}

watch(
  () => route.fullPath,
  () => {
    if (routeReloadSuppressed) return
    if (!route.path.startsWith('/social-assistance/temp-relief')) return
    const raw = route.params.id
    const target = Array.isArray(raw) ? raw[0] : raw
    if (target && form.id > 0 && Number(target) === form.id) return
    void load()
  },
)

watch(familySizeDisplay, (next) => {
  if (form.reliefPersonCount > next) form.reliefPersonCount = Math.max(next, 1)
})

onMounted(() => {
  void load()
})
</script>

<template>
  <div class="nc-page temp-relief-form">
    <NcPageHeader
      :title="isCreate ? '新建临时救助申请' : isView ? '查看临时救助申请' : '临时救助申请'"
      description="两大类 × 三小类、六状态；金额、时限、公示、备案与可执行动作均以服务端返回为准。"
      kicker="低收入人口救助帮扶 · 临时救助"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="backToList">返回列表</el-button>
        <el-button v-if="!isCreate" :icon="Refresh" :loading="loading" @click="refreshPage">
          刷新
        </el-button>
      </template>
    </NcPageHeader>

    <el-alert
      v-if="loadError"
      class="load-error"
      type="error"
      :title="loadError"
      :closable="false"
      show-icon
    >
      <el-button link type="primary" @click="refreshPage">重新加载</el-button>
    </el-alert>

    <NcCard v-if="loading" padding="lg">
      <el-skeleton :rows="12" animated />
    </NcCard>

    <template v-else>
      <div v-if="!isCreate" class="mode-banner">
        <span class="mode-banner__label">{{ isView ? '查看模式' : isEditable ? '编辑模式' : '只读模式' }}</span>
        <strong class="mono">{{ form.applicationNo || '—' }}</strong>
        <el-tag size="small" effect="plain">{{ statusText }}</el-tag>
        <span class="mode-banner__category">{{ categoryDisplayName }}</span>
        <span v-if="!isEditable" class="mode-banner__hint">
          当前状态不可编辑，仅可查看与打印。
        </span>
      </div>

      <div class="section-nav" role="navigation" aria-label="表单分区">
        <button
          v-for="(item, index) in ['① 分类与申请人', '② 申请信息与金额', '③ 家庭成员与明细']"
          :key="item"
          type="button"
          class="section-nav__item"
          :class="{ 'is-active': activeSection === index }"
          @click="scrollToSection(index)"
        >
          {{ item }}
        </button>
      </div>

      <!-- ① 分类与申请人 -->
      <section ref="section1" class="form-section">
        <NcCard title="业务场景" subtitle="大类由身份证反查四类在册台账自动判定；小类与发放渠道按实际情形选择。">
          <div class="scenario-grid">
            <div v-for="item in scenarioItems" :key="item.label" class="scenario-card">
              <span class="scenario-card__label">{{ item.label }}</span>
              <strong class="scenario-card__value">{{ item.value }}</strong>
            </div>
          </div>
          <div class="scenario-foot">
            <span>当前场景：{{ categoryDisplayName }}</span>
            <span v-if="detail?.applyYear">申请年度：{{ detail.applyYear }}</span>
            <span v-if="detail?.deadlineDate">办理截止：{{ formatDate(detail.deadlineDate) }}</span>
          </div>

          <div class="type-choice" v-if="isEditable">
            <button
              type="button"
              class="type-card"
              :class="{ 'is-selected': form.reliefType === 'Large' }"
              @click="handleReliefTypeChange('Large')"
            >
              <strong>常规救助</strong>
              <span>上级部门发放金额由上级核定；备用金渠道手工填写金额。</span>
            </button>
            <button
              type="button"
              class="type-card"
              :class="{ 'is-selected': form.reliefType === 'Small' }"
              @click="handleReliefTypeChange('Small')"
            >
              <strong>小额快救</strong>
              <span>按城市低保月标准 × 遇困人数 × 档位系数自动计算，可下调。</span>
            </button>
          </div>
        </NcCard>

        <NcCard title="选择申请人" subtitle="统一检索四类在册台账、其他困难台账与最低生活保障申请库；非在册可手工登记。">
          <div v-if="isEditable && !candidateSelected && !manualEntry" class="candidate-search">
            <el-input
              v-model="candidateKeyword"
              clearable
              :prefix-icon="Search"
              placeholder="输入姓名或身份证号检索..."
              @keyup.enter="searchCandidates"
            />
            <el-button type="primary" :loading="candidateLoading" @click="searchCandidates">
              检索
            </el-button>
            <el-button :disabled="candidateLoading" @click="enterManualEntry">转非在册登记</el-button>
          </div>

          <div v-if="candidates.length > 0" class="candidate-list">
            <article
              v-for="candidate in candidates"
              :key="`${candidate.sourceTable}-${candidate.sourceFamilyId}`"
              class="candidate-card"
            >
              <div class="candidate-card__main">
                <div class="candidate-card__title">
                  <strong>{{ candidate.name }}</strong>
                  <span class="candidate-card__source">{{ candidate.sourceName }}</span>
                  <el-tag v-if="candidate.hasAppliedThisYear" size="small" type="danger" effect="plain">
                    本年已申请
                  </el-tag>
                </div>
                <p class="candidate-card__id mono">{{ candidate.idCard }}</p>
                <p v-if="candidate.matchedPersonInfo" class="candidate-card__match">
                  {{ candidate.matchedPersonInfo }}
                </p>
                <p class="candidate-card__meta">
                  {{ candidate.familyCategory || '—' }} · {{ candidate.familyAddress || '—' }}
                </p>
              </div>
              <el-button
                type="primary"
                size="small"
                :disabled="candidate.hasAppliedThisYear"
                :loading="candidateLoading"
                @click="selectCandidate(candidate)"
              >
                使用该申请人
              </el-button>
            </article>
          </div>

          <p v-else-if="candidateSearched && isEditable && !candidateSelected && !manualEntry" class="empty-note">
            未检索到符合条件的申请人。请核对姓名或身份证号，或转非在册手工登记。
          </p>

          <div v-if="candidateSelected || manualEntry" class="candidate-summary">
            <span>{{ candidateSummary || '非在册手工登记' }}</span>
            <el-button v-if="isEditable" link type="primary" @click="backToSearch">
              重新选择
            </el-button>
          </div>

          <el-alert
            v-if="manualEntry"
            class="manual-alert"
            type="warning"
            :closable="false"
            show-icon
            title="非在册手工登记"
          >
            请填写真实姓名与身份证号。系统按身份证反查四类在册台账，命中将阻断保存并要求改用检索选择。
          </el-alert>

          <el-alert
            v-if="annualBlocked"
            class="annual-alert"
            type="error"
            :title="annualMessage"
            :closable="false"
            show-icon
          />
        </NcCard>

        <NcCard
          v-if="candidateSelected || manualEntry"
          title="申请人信息"
          :subtitle="manualEntry ? '手工登记可填写身份与住址；保存时服务端仍会复核在册身份。' : '姓名、身份证号与地址分列来自来源记录；联系电话与银行账户可修正。'"
        >
          <div class="form-grid">
            <label class="field">
              <span class="field-label">申请人姓名 *</span>
              <el-input
                v-model="applicant.name"
                :disabled="!isEditable || !manualEntry"
                maxlength="50"
              />
            </label>
            <label class="field">
              <span class="field-label">身份证号 *</span>
              <el-input
                v-model="applicant.idCard"
                :disabled="!isEditable || !manualEntry"
                maxlength="18"
                class="mono"
                @input="handleManualIdInput"
                @blur="handleManualIdBlur"
              />
            </label>
            <label class="field">
              <span class="field-label">性别</span>
              <el-select
                v-model="applicant.gender"
                :disabled="!isEditable || !manualEntry"
                clearable
              >
                <el-option label="男" value="男" />
                <el-option label="女" value="女" />
              </el-select>
            </label>
            <label class="field">
              <span class="field-label">年龄</span>
              <el-input :model-value="applicant.age === null ? '' : `${applicant.age} 周岁`" readonly />
            </label>
            <label class="field">
              <span class="field-label">户籍分类</span>
              <el-input :model-value="formatHukouType(applicant.hukouType)" readonly />
            </label>
            <label class="field">
              <span class="field-label">家庭类别</span>
              <el-select v-model="applicant.familyCategory" :disabled="!isEditable" filterable clearable>
                <el-option
                  v-for="option in familyCategoryOptions"
                  :key="option"
                  :label="option"
                  :value="option"
                />
              </el-select>
            </label>
            <label class="field">
              <span class="field-label">家庭人口</span>
              <el-input :model-value="`${familySizeDisplay} 人`" readonly />
            </label>
          </div>

          <div class="identity-actions">
            <el-button
              v-if="isEditable"
              size="small"
              :icon="Search"
              @click="ocrVisible = true"
            >
              识别身份证
            </el-button>
            <el-button
              size="small"
              :icon="Document"
              :disabled="form.id <= 0"
              @click="photoVisible = true"
            >
              证件 / 材料照片
            </el-button>
            <span v-if="form.id <= 0" class="identity-actions__hint">保存草稿后可上传照片</span>
          </div>

          <div class="population-panel">
            <div class="population-panel__head">
              <el-tag
                :type="populationStatusText === '在保障人群内' ? 'success' : populationStatusText === '不在保障人群内' ? 'warning' : 'info'"
                effect="dark"
              >
                {{ populationStatusText }}
              </el-tag>
              <el-tag size="small" effect="plain" :type="manualEntry ? 'warning' : 'info'">
                {{ manualEntry ? '非在册手工登记' : '检索选择' }}
              </el-tag>
              <span v-if="identityLoading" class="population-panel__loading">正在判定…</span>
            </div>
            <p class="population-panel__basis">{{ populationBasis }}</p>
            <div v-if="identity?.ledgerNames.length" class="population-panel__ledgers">
              <el-tag
                v-for="name in identity.ledgerNames"
                :key="name"
                size="small"
                type="success"
                effect="plain"
              >
                {{ name }}
              </el-tag>
            </div>
            <el-alert
              v-if="manualHitsLedger"
              class="population-panel__alert"
              type="error"
              :closable="false"
              show-icon
              title="该身份证命中四类在册台账，不能手工登记"
            >
              请点击「重新选择」从检索结果选择该申请人，以便带出成员与银行快照。
            </el-alert>
            <p v-if="identityError" class="population-panel__error">{{ identityError }}</p>
          </div>

          <p class="group-title group-title--spaced">户籍所在地</p>
          <div class="form-grid">
            <label class="field">
              <span class="field-label">省</span>
              <el-input v-model="applicant.hukouProvince" :disabled="!isEditable || !manualEntry" />
            </label>
            <label class="field">
              <span class="field-label">市</span>
              <el-input v-model="applicant.hukouCity" :disabled="!isEditable || !manualEntry" />
            </label>
            <label class="field">
              <span class="field-label">县区</span>
              <el-input v-model="applicant.hukouDistrict" :disabled="!isEditable || !manualEntry" />
            </label>
            <label class="field">
              <span class="field-label">镇社区</span>
              <el-input v-model="applicant.hukouTown" :disabled="!isEditable || !manualEntry" />
            </label>
          </div>

          <p class="group-title group-title--spaced">家庭住址</p>
          <div class="form-grid">
            <label class="field">
              <span class="field-label">省</span>
              <el-input v-model="applicant.familyProvince" :disabled="!isEditable || !manualEntry" />
            </label>
            <label class="field">
              <span class="field-label">市</span>
              <el-input v-model="applicant.familyCity" :disabled="!isEditable || !manualEntry" />
            </label>
            <label class="field">
              <span class="field-label">县区</span>
              <el-input v-model="applicant.familyDistrict" :disabled="!isEditable || !manualEntry" />
            </label>
            <label class="field">
              <span class="field-label">镇社区</span>
              <el-input v-model="applicant.familyTown" :disabled="!isEditable || !manualEntry" />
            </label>
            <label class="field">
              <span class="field-label">村 / 社区</span>
              <el-input v-model="applicant.village" :disabled="!isEditable || !manualEntry" />
            </label>
            <label class="field field--wide">
              <span class="field-label">详细地址</span>
              <el-input
                v-model="applicant.detailAddress"
                :disabled="!isEditable"
                placeholder="如：柳树村99号"
              />
            </label>
          </div>
        </NcCard>
      </section>

      <!-- ② 申请信息与金额 -->
      <section ref="section2" class="form-section">
        <NcCard title="申请信息" subtitle="申请日期、遇困人数与救助对象共同决定金额档位与时限。">
          <div class="form-grid">
            <label class="field">
              <span class="field-label">申请日期 *</span>
              <el-date-picker
                v-if="isCreate"
                v-model="form.applyDate"
                type="date"
                value-format="YYYY-MM-DD"
                :disabled="!isEditable"
                :disabled-date="isDateBeyondMax"
                style="width: 100%"
                @change="handleApplyDateChange"
              />
              <div v-else class="readonly-with-action">
                <el-input :model-value="formatDate(form.applyDate)" readonly />
                <el-button
                  v-if="canChangeDate"
                  size="small"
                  @click="openChangeDate"
                >
                  改期
                </el-button>
              </div>
              <span class="field-hint">最晚 {{ maxApplyDateText() }}；已入库仅草稿 / 退回补正可改期。</span>
            </label>
            <label class="field">
              <span class="field-label">遇困人数（含户主）*</span>
              <el-input-number
                v-model="form.reliefPersonCount"
                :min="1"
                :max="familySizeDisplay"
                :disabled="!isEditable"
                controls-position="right"
                style="width: 100%"
                @change="handlePersonCountChange"
              />
            </label>
            <label class="field">
              <span class="field-label">家庭人口</span>
              <el-input :model-value="`${familySizeDisplay} 人`" readonly />
            </label>
            <label class="field">
              <span class="field-label">联系电话</span>
              <el-input v-model="applicant.phone" :disabled="!isEditable" maxlength="30" />
            </label>
            <label class="field">
              <span class="field-label">救助对象 *</span>
              <el-select
                v-model="form.beneficiaryKey"
                :disabled="!isEditable || beneficiaryOptions.length === 0"
              >
                <el-option
                  v-for="option in beneficiaryOptions"
                  :key="option.key"
                  :label="`${option.name}（${option.relation}）`"
                  :value="option.key"
                />
              </el-select>
            </label>
            <label class="field">
              <span class="field-label">与户主关系</span>
              <el-input :model-value="selectedBeneficiary?.relation ?? ''" readonly />
            </label>
            <label class="field">
              <span class="field-label">开户行</span>
              <el-input v-model="applicant.bankName" :disabled="!isEditable" maxlength="100" />
            </label>
            <label class="field field--wide">
              <span class="field-label">银行卡号 / 一卡通账号</span>
              <el-input v-model="applicant.bankAccount" :disabled="!isEditable" maxlength="50" class="mono" />
            </label>
            <label class="field">
              <span class="field-label">填报单位</span>
              <el-input
                v-model="form.reportUnit"
                :disabled="!isEditable"
                maxlength="100"
                @blur="loadReportUnitPhone"
              />
            </label>
            <label class="field">
              <span class="field-label">填报时间</span>
              <el-input :model-value="form.reportTime ? formatDateTime(form.reportTime) : '保存时生成'" readonly />
            </label>
          </div>

          <div class="difficulty-head">
            <p class="group-title">困难情况</p>
            <el-button v-if="isEditable" size="small" @click="generateReason">
              按明细生成申请理由
            </el-button>
          </div>

          <div class="form-grid">
            <label class="field">
              <span class="field-label">遇困类型 *</span>
              <el-select
                v-model="form.difficultyType"
                :disabled="!isEditable"
              >
                <el-option
                  v-for="option in DIFFICULTY_TYPES"
                  :key="option"
                  :label="option"
                  :value="option"
                />
              </el-select>
            </label>
            <label class="field field--wide">
              <span class="field-label">申请理由 / 困难情况 *</span>
              <el-input
                v-model="form.difficultyReason"
                type="textarea"
                :rows="3"
                :disabled="!isEditable"
                placeholder="如：因病、因灾等突发困难，家庭基本生活暂时陷入困境..."
              />
            </label>
          </div>

          <p class="group-title group-title--spaced">政策快照与家庭健康</p>
          <div v-if="policyItems.length > 0" class="policy-chips">
            <el-tag
              v-for="item in policyItems"
              :key="item"
              size="small"
              effect="plain"
              type="info"
            >
              {{ item }}
            </el-tag>
          </div>
          <div class="form-grid">
            <label class="field field--wide">
              <span class="field-label">享受的政策救助情况</span>
              <el-input
                v-model="form.policyEnjoyed"
                type="textarea"
                :rows="2"
                :disabled="!isEditable"
                placeholder="如：最低生活保障 / 特困人员 / 最低生活保障边缘家庭等..."
              />
            </label>
            <label class="field field--wide">
              <span class="field-label">家庭健康摘要</span>
              <el-input
                v-model="form.familyMemberStatus"
                type="textarea"
                :rows="2"
                :disabled="!isEditable"
                placeholder="如：家庭成员健康 / 患病 / 劳动能力等情况..."
              />
            </label>
          </div>
        </NcCard>

        <NcCard title="救助金额" subtitle="金额、档位与上限均以服务端配置与 Core 公式为准，前端不重算。">
          <div v-if="isSmall" class="small-amount">
            <div class="small-amount__head">
              <span class="group-title">小额快救档位</span>
              <span v-if="smallTiersBase !== null" class="field-hint">
                城市低保月标准 {{ formatMoney(smallTiersBase) }}
              </span>
            </div>
            <el-radio-group
              v-if="smallTiers.length > 0"
              v-model="form.standardTier"
              :disabled="!isEditable || smallTiersLoading"
              @change="handleTierChange"
            >
              <el-radio-button
                v-for="tier in smallTiers"
                :key="tier.code"
                :value="tier.code"
              >
                {{ tier.label }} · 上限 {{ formatMoney(tier.ceilingAmount) }}
              </el-radio-button>
            </el-radio-group>
            <el-alert
              v-if="smallTiersError"
              class="amount-alert"
              type="error"
              :title="smallTiersError"
              :closable="false"
              show-icon
            >
              <el-button link type="primary" @click="loadSmallTiers">重试</el-button>
            </el-alert>

            <div class="form-grid">
              <label class="field">
                <span class="field-label">确定金额（可下调）*</span>
                <el-input-number
                  v-model="form.confirmAmount"
                  :min="0"
                  :max="selectedTier?.ceilingAmount"
                  :precision="2"
                  :step="10"
                  :disabled="!isEditable || !selectedTier"
                  controls-position="right"
                  style="width: 100%"
                />
              </label>
              <label class="field">
                <span class="field-label">档位上限（服务端）</span>
                <el-input :model-value="formatMoney(selectedTier?.ceilingAmount)" readonly />
              </label>
            </div>
            <p class="section-note">{{ amountHint }}</p>
          </div>

          <div v-else class="large-amount">
            <label class="field">
              <span class="field-label">发放渠道</span>
              <el-select
                v-model="form.paymentChannel"
                :disabled="!isEditable"
                @change="handlePaymentChannelChange"
              >
                <el-option label="上级部门发放（金额不填写）" value="County" />
                <el-option label="备用金发放（手工填写金额）" value="Reserve" />
              </el-select>
            </label>

            <div v-if="effectiveChannel === 'County'" class="amount-placeholder">
              <span>常规救助 · 上级部门发放</span>
              <strong>金额由上级按程序核定，系统不填写、不落库。</strong>
            </div>
            <label v-else class="field">
              <span class="field-label">救助金额（元）*</span>
              <el-input-number
                v-model="form.confirmAmount"
                :min="0"
                :precision="2"
                :step="100"
                :disabled="!isEditable"
                controls-position="right"
                style="width: 100%"
              />
            </label>
            <p class="section-note">{{ amountHint }}</p>
          </div>
        </NcCard>

        <NcCard title="公示、受理与备案" subtitle="公示起止、办理时限、县确认与备案状态由服务端按政策推导。">
          <div class="form-grid">
            <label class="field">
              <span class="field-label">公示期间</span>
              <el-input :model-value="publicizeText" readonly />
            </label>
            <label class="field">
              <span class="field-label">公示起止</span>
              <el-input
                :model-value="scenarioFresh && detail?.publicizeStartDate
                  ? `${formatDate(detail.publicizeStartDate)} 至 ${formatDate(detail.publicizeEndDate)}`
                  : (isSmall ? '小额快救免公示' : '保存后生成')"
                readonly
              />
            </label>
            <label class="field">
              <span class="field-label">公示异议反馈电话</span>
              <el-input :model-value="reportUnitPhone || '未取到填报单位电话'" readonly />
            </label>
            <label class="field">
              <span class="field-label">办理时限</span>
              <el-input :model-value="deadlineText" readonly />
            </label>
            <label class="field">
              <span class="field-label">乡镇受理人</span>
              <el-input v-model="form.acceptancePerson" :disabled="!isEditable" maxlength="50" />
            </label>
            <label class="field">
              <span class="field-label">受理日期</span>
              <el-input
                :model-value="detail?.acceptedAt ? formatDateTime(detail.acceptedAt) : '提交 / 确认时生成'"
                readonly
              />
            </label>
            <label class="field">
              <span class="field-label">经济核对</span>
              <el-input :model-value="economicCheckText" readonly />
            </label>
            <label class="field">
              <span class="field-label">县确认</span>
              <el-input :model-value="countyConfirmText" readonly />
            </label>
            <label class="field">
              <span class="field-label">备案状态</span>
              <el-input :model-value="filingText" readonly />
            </label>
          </div>

          <label class="field field--wide field--spaced">
            <span class="field-label">入户核实情况</span>
            <el-input
              v-model="form.verifyResult"
              type="textarea"
              :rows="2"
              :disabled="!isEditable"
              placeholder="入户调查核实情况..."
            />
          </label>

          <div v-if="detail?.isOverdue" class="overdue-note">
            已逾期 {{ detail.remainingDays === null ? '' : Math.abs(detail.remainingDays) }} 天，请尽快办理。
          </div>
        </NcCard>
      </section>

      <!-- ③ 家庭成员与明细 -->
      <section ref="section3" class="form-section">
        <NcCard
          title="家庭成员"
          subtitle="家庭人口 = 户主 1 人 + 有效家庭成员行数；审批表最多展示 5 行。"
        >
          <template #actions>
            <el-button v-if="isEditable" size="small" :icon="Plus" @click="addMember">
              添加成员
            </el-button>
          </template>

          <article v-for="(row, index) in members" :key="row.key" class="member-card">
            <div class="member-card__head">
              <span>家庭成员 {{ index + 1 }}</span>
              <el-button
                v-if="isEditable"
                size="small"
                type="danger"
                plain
                :icon="Delete"
                @click="removeMember(index)"
              >
                删除
              </el-button>
            </div>
            <div class="form-grid">
              <label class="field">
                <span class="field-label">姓名</span>
                <el-input
                  v-model="row.memberName"
                  :disabled="!isEditable"
                  maxlength="50"
                  @input="handleMemberChanged"
                />
              </label>
              <label class="field">
                <span class="field-label">与户主关系</span>
                <el-input v-model="row.relation" :disabled="!isEditable" maxlength="30" />
              </label>
              <label class="field">
                <span class="field-label">性别</span>
                <el-select v-model="row.gender" :disabled="!isEditable" clearable>
                  <el-option label="男" value="男" />
                  <el-option label="女" value="女" />
                </el-select>
              </label>
              <label class="field">
                <span class="field-label">身份证号</span>
                <el-input v-model="row.idCard" :disabled="!isEditable" maxlength="18" class="mono" />
              </label>
              <label class="field">
                <span class="field-label">工作单位</span>
                <el-input v-model="row.workUnit" :disabled="!isEditable" maxlength="100" />
              </label>
              <label class="field">
                <span class="field-label">年收入（元）</span>
                <el-input-number
                  v-model="row.annualIncome"
                  :min="0"
                  :precision="2"
                  :disabled="!isEditable"
                  :controls="false"
                  style="width: 100%"
                />
              </label>
            </div>
          </article>

          <p class="section-note section-note--spaced">
            当前家庭人口：{{ familySizeDisplay }} 人（户主 1 人 + 有效成员 {{ Math.max(familySizeDisplay - 1, 0) }} 人）。
            添加或删除成员后，遇困人数不得超过家庭人口。
          </p>
        </NcCard>

        <NcCard title="困难明细" subtitle="按遇困类型填写；金额明细用于核对自付合计，不参与前端金额计算。">
          <div v-if="form.difficultyType === '疾病'" class="detail-block">
            <div class="detail-head">
              <div>
                <p class="group-title">疾病明细</p>
                <p class="section-note">可多条，最多 {{ MAX_DETAIL_ROWS }} 条。</p>
              </div>
              <el-button v-if="isEditable" size="small" :icon="Plus" @click="addDisease">
                添加疾病
              </el-button>
            </div>

            <article v-for="(row, index) in diseases" :key="row.key" class="detail-card">
              <div class="detail-card__head">
                <span>疾病 {{ index + 1 }}</span>
                <el-button
                  v-if="isEditable"
                  size="small"
                  type="danger"
                  plain
                  :icon="Delete"
                  @click="removeDisease(index)"
                >
                  删除
                </el-button>
              </div>
              <div class="form-grid">
                <label class="field">
                  <span class="field-label">疾病诊断结果</span>
                  <el-input v-model="row.diseaseName" :disabled="!isEditable" />
                </label>
                <label class="field">
                  <span class="field-label">疾病编码（ICD-10）</span>
                  <el-input v-model="row.diseaseCode" :disabled="!isEditable" placeholder="可手工填写" />
                </label>
                <label class="field">
                  <span class="field-label">患病成员</span>
                  <el-select v-model="row.memberName" :disabled="!isEditable" filterable clearable>
                    <el-option v-for="name in memberNameOptions" :key="name" :label="name" :value="name" />
                  </el-select>
                </label>
                <label class="field">
                  <span class="field-label">所在医院</span>
                  <el-input v-model="row.hospital" :disabled="!isEditable" placeholder="如：县人民医院" />
                </label>
                <label class="field">
                  <span class="field-label">治疗开始日期</span>
                  <el-date-picker
                    v-model="row.treatStartDate"
                    type="date"
                    value-format="YYYY-MM-DD"
                    :disabled="!isEditable"
                    style="width: 100%"
                  />
                </label>
                <label class="field">
                  <span class="field-label">治疗结束日期</span>
                  <el-date-picker
                    v-model="row.treatEndDate"
                    type="date"
                    value-format="YYYY-MM-DD"
                    :disabled="!isEditable"
                    style="width: 100%"
                  />
                </label>
                <label class="field">
                  <span class="field-label">医疗费用总额（元）</span>
                  <el-input-number
                    v-model="row.medicalTotal"
                    :min="0"
                    :precision="2"
                    :controls="false"
                    :disabled="!isEditable"
                    style="width: 100%"
                  />
                </label>
                <label class="field">
                  <span class="field-label">医保 / 其他报销（元）</span>
                  <el-input-number
                    v-model="row.insurancePaid"
                    :min="0"
                    :precision="2"
                    :controls="false"
                    :disabled="!isEditable"
                    style="width: 100%"
                  />
                </label>
                <label class="field">
                  <span class="field-label">个人自付费用（元）</span>
                  <el-input-number
                    v-model="row.selfPaid"
                    :min="0"
                    :precision="2"
                    :controls="false"
                    :disabled="!isEditable"
                    style="width: 100%"
                  />
                </label>
              </div>
            </article>

            <p v-if="diseases.length === 0" class="empty-note">尚未添加疾病明细。</p>
            <div class="detail-total">
              <span>自付合计</span>
              <strong>{{ formatMoney(selfPaidTotal) }}</strong>
            </div>
          </div>

          <div v-else-if="form.difficultyType === '意外灾害'" class="detail-block">
            <div class="detail-head">
              <div>
                <p class="group-title">意外灾害明细</p>
                <p class="section-note">可多条，最多 {{ MAX_DETAIL_ROWS }} 条。</p>
              </div>
              <el-button v-if="isEditable" size="small" :icon="Plus" @click="addAccident">
                添加意外灾害
              </el-button>
            </div>

            <article v-for="(row, index) in accidents" :key="row.key" class="detail-card">
              <div class="detail-card__head">
                <span>意外灾害 {{ index + 1 }}</span>
                <el-button
                  v-if="isEditable"
                  size="small"
                  type="danger"
                  plain
                  :icon="Delete"
                  @click="removeAccident(index)"
                >
                  删除
                </el-button>
              </div>
              <div class="form-grid">
                <label class="field">
                  <span class="field-label">意外 / 灾害类型 *</span>
                  <el-select v-model="row.accidentType" :disabled="!isEditable">
                    <el-option
                      v-for="option in ACCIDENT_TYPE_OPTIONS"
                      :key="option"
                      :label="option"
                      :value="option"
                    />
                  </el-select>
                </label>
                <label class="field">
                  <span class="field-label">受灾成员</span>
                  <el-select v-model="row.memberName" :disabled="!isEditable" filterable clearable>
                    <el-option v-for="name in memberNameOptions" :key="name" :label="name" :value="name" />
                  </el-select>
                </label>
                <label class="field">
                  <span class="field-label">发生时间</span>
                  <el-date-picker
                    v-model="row.happenDate"
                    type="date"
                    value-format="YYYY-MM-DD"
                    :disabled="!isEditable"
                    style="width: 100%"
                  />
                </label>
                <label class="field">
                  <span class="field-label">发生地点</span>
                  <el-input v-model="row.happenPlace" :disabled="!isEditable" />
                </label>
                <label class="field field--wide">
                  <span class="field-label">人身伤害 / 伤残情况</span>
                  <el-input
                    v-model="row.injurySituation"
                    type="textarea"
                    :rows="2"
                    :disabled="!isEditable"
                  />
                </label>
                <label class="field">
                  <span class="field-label">财产损失（元）</span>
                  <el-input-number
                    v-model="row.propertyLoss"
                    :min="0"
                    :precision="2"
                    :controls="false"
                    :disabled="!isEditable"
                    style="width: 100%"
                  />
                </label>
                <label class="field">
                  <span class="field-label">已获保险 / 赔偿（元）</span>
                  <el-input-number
                    v-model="row.compensationPaid"
                    :min="0"
                    :precision="2"
                    :controls="false"
                    :disabled="!isEditable"
                    style="width: 100%"
                  />
                </label>
                <label class="field">
                  <span class="field-label">责任认定情况</span>
                  <el-input v-model="row.responsibilityDesc" :disabled="!isEditable" />
                </label>
                <label class="field">
                  <span class="field-label">佐证材料说明</span>
                  <el-input v-model="row.materialDesc" :disabled="!isEditable" />
                </label>
              </div>
            </article>

            <p v-if="accidents.length === 0" class="empty-note">尚未添加意外灾害明细。</p>
          </div>

          <div v-else-if="form.difficultyType === '教育支出'" class="detail-block">
            <div class="detail-head">
              <div>
                <p class="group-title">教育支出明细</p>
                <p class="section-note">可多条，最多 {{ MAX_DETAIL_ROWS }} 条。</p>
              </div>
              <el-button v-if="isEditable" size="small" :icon="Plus" @click="addEducation">
                添加教育支出
              </el-button>
            </div>

            <article v-for="(row, index) in educations" :key="row.key" class="detail-card">
              <div class="detail-card__head">
                <span>教育支出 {{ index + 1 }}</span>
                <el-button
                  v-if="isEditable"
                  size="small"
                  type="danger"
                  plain
                  :icon="Delete"
                  @click="removeEducation(index)"
                >
                  删除
                </el-button>
              </div>
              <div class="form-grid">
                <label class="field">
                  <span class="field-label">学生姓名 *</span>
                  <el-input v-model="row.studentName" :disabled="!isEditable" />
                </label>
                <label class="field">
                  <span class="field-label">就学阶段 *</span>
                  <el-select v-model="row.educationStage" :disabled="!isEditable">
                    <el-option
                      v-for="option in EDUCATION_STAGE_OPTIONS"
                      :key="option"
                      :label="option"
                      :value="option"
                    />
                  </el-select>
                </label>
                <label class="field">
                  <span class="field-label">就读学校</span>
                  <el-input v-model="row.schoolName" :disabled="!isEditable" />
                </label>
                <label class="field">
                  <span class="field-label">学年制</span>
                  <el-select v-model="row.schoolDuration" :disabled="!isEditable" clearable>
                    <el-option
                      v-for="option in SCHOOL_DURATION_OPTIONS"
                      :key="option.value"
                      :label="option.label"
                      :value="option.value"
                    />
                  </el-select>
                </label>
                <label class="field">
                  <span class="field-label">学费 / 住宿费（元）</span>
                  <el-input-number
                    v-model="row.tuitionFee"
                    :min="0"
                    :precision="2"
                    :controls="false"
                    :disabled="!isEditable"
                    style="width: 100%"
                  />
                </label>
                <label class="field">
                  <span class="field-label">缴费时间</span>
                  <el-date-picker
                    v-model="row.feeDate"
                    type="date"
                    value-format="YYYY-MM-DD"
                    :disabled="!isEditable"
                    style="width: 100%"
                  />
                </label>
              </div>
            </article>

            <p v-if="educations.length === 0" class="empty-note">尚未添加教育支出明细。</p>
          </div>

          <p v-else class="empty-note">遇困类型为「其他困难」，无需填写专项明细。</p>
        </NcCard>
      </section>

      <div class="action-bar">
        <div class="action-bar__info">
          <el-tag size="small" effect="plain">{{ statusText }}</el-tag>
          <span>{{ actionHint }}</span>
        </div>

        <div class="action-bar__buttons">
          <el-button @click="backToList">取消</el-button>

          <template v-if="form.id > 0 && !isView">
            <el-button
              v-if="isEditable"
              type="danger"
              plain
              :title="deleteDisabledReason"
              :disabled="!canDeleteAction || Boolean(actionLoading) || saving"
              :loading="actionLoading === 'delete'"
              @click="runStatusAction('delete')"
            >
              删除
            </el-button>
            <el-button
              :title="returnDisabledReason"
              :disabled="!canReturnAction || Boolean(actionLoading) || saving"
              :loading="actionLoading === 'return'"
              @click="runStatusAction('return')"
            >
              退回补正
            </el-button>
            <el-button
              type="danger"
              plain
              :title="rejectDisabledReason"
              :disabled="!canRejectAction || Boolean(actionLoading) || saving"
              :loading="actionLoading === 'reject'"
              @click="runStatusAction('reject')"
            >
              不予救助
            </el-button>
            <el-button
              type="warning"
              plain
              :title="stopDisabledReason"
              :disabled="!canStopAction || Boolean(actionLoading) || saving"
              :loading="actionLoading === 'stop'"
              @click="runStatusAction('stop')"
            >
              终止
            </el-button>
            <el-button
              type="success"
              plain
              :title="fileDisabledReason"
              :disabled="!canFileAction || Boolean(actionLoading) || saving"
              :loading="actionLoading === 'file'"
              @click="runStatusAction('file')"
            >
              登记备案
            </el-button>
            <el-button
              type="success"
              :title="confirmDisabledReason"
              :disabled="!canConfirmAction || Boolean(actionLoading) || saving"
              :loading="actionLoading === 'confirm'"
              @click="runStatusAction('confirm')"
            >
              {{ confirmLabel }}
            </el-button>
          </template>

          <el-button
            v-if="form.id > 0"
            :icon="Document"
            :disabled="Boolean(actionLoading) || saving"
            @click="openPrint"
          >
            打印
          </el-button>

          <template v-if="isEditable">
            <el-button :loading="saving" :disabled="Boolean(actionLoading)" @click="saveDraft">
              保存草稿
            </el-button>
            <el-button
              type="primary"
              plain
              :loading="saving || actionLoading === 'submit'"
              :disabled="Boolean(actionLoading)"
              @click="saveAndSubmit"
            >
              保存并提交
            </el-button>
            <el-button
              type="primary"
              :icon="Document"
              :loading="saving"
              :disabled="Boolean(actionLoading)"
              @click="saveAndPrint"
            >
              保存并打印
            </el-button>
          </template>
          <el-button v-else-if="isView && form.id > 0" @click="backToList">返回列表</el-button>
        </div>
      </div>

      <p v-if="autoFilling" class="auto-fill-note">
        正在回填申请人来源、政策快照与家庭健康摘要…
      </p>
    </template>

    <el-dialog
      v-model="changeDateVisible"
      title="修改申请日期"
      width="min(460px, 92vw)"
      append-to-body
    >
      <p class="change-date-note">
        改期后将重新生成申请编号，并按新日期重算公示、验收与办理时限；已打印文书需重新打印。
      </p>
      <el-date-picker
        v-model="changeDateValue"
        type="date"
        value-format="YYYY-MM-DD"
        :disabled-date="isDateBeyondMax"
        style="width: 100%"
      />
      <p class="field-hint">最晚 {{ maxApplyDateText() }}。</p>
      <template #footer>
        <el-button :disabled="changeDateSaving" @click="changeDateVisible = false">取消</el-button>
        <el-button type="primary" :loading="changeDateSaving" @click="submitChangeDate">
          确认改期
        </el-button>
      </template>
    </el-dialog>

    <TempReliefPrintDialog
      v-model="printVisible"
      :application-id="printableId"
      :application-no="form.applicationNo"
      :applicant-name="applicant.name"
      :relief-type="form.reliefType"
      mode="print"
    />

    <TempReliefPhotoPanel
      v-model="photoVisible"
      :application-id="printableId"
      :applicant-name="applicant.name"
      :editable="isEditable"
      @recognized="handleOcrResult"
      @changed="handlePhotoChanged"
    />

    <TempReliefOcrDialog
      v-model="ocrVisible"
      :application-id="printableId"
      :applicant-name="applicant.name"
      @recognized="handleOcrResult"
    />
  </div>
</template>

<style scoped>
.temp-relief-form {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.load-error {
  margin: 0;
}

.mode-banner {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
  padding: 10px 14px;
  border-left: 3px solid var(--nc-seal);
  border-radius: var(--nc-radius-sm);
  background: var(--nc-seal-soft);
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
}

.mode-banner__label {
  color: var(--nc-seal-dark);
  font-weight: 600;
}

.mode-banner strong {
  color: var(--nc-ink);
  font-family: var(--nc-font-mono);
}

.mode-banner__category {
  color: var(--nc-ink-2);
}

.mode-banner__hint {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.section-nav {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 10px;
}

.section-nav__item {
  padding: 11px 12px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background: var(--nc-paper);
  color: var(--nc-ink-2);
  font-family: inherit;
  font-size: var(--nc-text-base);
  cursor: pointer;
}

.section-nav__item.is-active {
  border-color: var(--nc-seal);
  background: var(--nc-seal-soft);
  color: var(--nc-seal);
  font-weight: 700;
}

.form-section {
  display: flex;
  flex-direction: column;
  gap: 14px;
  scroll-margin-top: 76px;
}

.form-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
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

.field--spaced {
  margin-top: 14px;
}

.field-hint {
  color: var(--nc-muted);
  font-size: 11px;
  line-height: 1.5;
}

.field-label {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
}

.readonly-with-action {
  display: flex;
  align-items: center;
  gap: 8px;
}

.readonly-with-action :deep(.el-input) {
  flex: 1;
}

.group-title {
  margin: 0;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  font-weight: 700;
}

.group-title--spaced {
  margin-top: 18px;
}

.section-note {
  margin: 0;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  line-height: 1.65;
}

.section-note--spaced {
  margin-top: 10px;
}

.scenario-grid {
  display: grid;
  grid-template-columns: repeat(6, minmax(0, 1fr));
  gap: 10px;
}

.scenario-card {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 10px 12px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background: var(--nc-paper-3);
}

.scenario-card__label {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  letter-spacing: 0.12em;
}

.scenario-card__value {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  line-height: 1.5;
}

.scenario-foot {
  display: flex;
  flex-wrap: wrap;
  gap: 8px 18px;
  margin-top: 10px;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.type-choice {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
  margin-top: 14px;
}

.type-card {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 5px;
  padding: 14px 16px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background: var(--nc-paper-3);
  font-family: inherit;
  text-align: left;
  cursor: pointer;
}

.type-card strong {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-lg);
}

.type-card span {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  line-height: 1.55;
}

.type-card.is-selected {
  border-color: var(--nc-seal);
  background: var(--nc-seal-soft);
}

.type-card.is-selected strong {
  color: var(--nc-seal);
}

.candidate-search {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.candidate-search :deep(.el-input) {
  flex: 1;
  min-width: 220px;
}

.candidate-list {
  display: grid;
  gap: 8px;
  margin-top: 12px;
}

.candidate-card {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  padding: 12px 14px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background: var(--nc-paper-soft);
}

.candidate-card__main {
  min-width: 0;
}

.candidate-card__title {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
}

.candidate-card__title strong {
  color: var(--nc-ink);
  font-size: var(--nc-text-base);
}

.candidate-card__source {
  color: var(--nc-pine-dark);
  font-size: var(--nc-text-xs);
  font-weight: 600;
}

.candidate-card__id {
  margin: 3px 0 0;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
}

.candidate-card__match {
  margin: 4px 0 0;
  color: var(--nc-seal-dark);
  font-size: var(--nc-text-xs);
}

.candidate-card__meta {
  margin: 4px 0 0;
  overflow: hidden;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.candidate-summary {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin-top: 12px;
  padding: 10px 12px;
  border: 1px solid rgba(46, 71, 56, 0.25);
  border-radius: var(--nc-radius-sm);
  background: var(--nc-pine-soft);
  color: var(--nc-pine-dark);
  font-size: var(--nc-text-sm);
}

.manual-alert,
.annual-alert {
  margin-top: 12px;
}

.identity-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  margin-top: 12px;
}

.identity-actions__hint {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.population-panel {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin-top: 14px;
  padding: 12px 14px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background: var(--nc-paper-3);
}

.population-panel__head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.population-panel__loading {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.population-panel__basis {
  margin: 0;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  line-height: 1.6;
}

.population-panel__ledgers {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.population-panel__alert {
  margin-top: 2px;
}

.population-panel__error {
  margin: 0;
  color: var(--nc-seal);
  font-size: var(--nc-text-xs);
}

.difficulty-head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  margin: 18px 0 10px;
}

.policy-chips {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin-bottom: 10px;
}

.small-amount {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.small-amount__head {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  justify-content: space-between;
  gap: 8px;
}

.amount-alert {
  margin: 0;
}

.amount-placeholder {
  display: flex;
  flex-direction: column;
  gap: 3px;
  padding: 12px 14px;
  border: 1px dashed var(--nc-line-strong);
  border-radius: var(--nc-radius-sm);
  background: var(--nc-paper-3);
}

.amount-placeholder span {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.amount-placeholder strong {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
}

.overdue-note {
  margin-top: 12px;
  padding: 8px 10px;
  border-left: 3px solid var(--nc-seal);
  background: var(--nc-seal-soft);
  color: var(--nc-seal-dark);
  font-size: var(--nc-text-sm);
}

.member-card,
.detail-card {
  margin-top: 10px;
  padding: 12px 14px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background: var(--nc-paper-soft);
}

.member-card__head,
.detail-card__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  margin-bottom: 10px;
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  letter-spacing: 0.06em;
}

.detail-block {
  margin-top: 4px;
}

.detail-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
}

.detail-total {
  display: flex;
  justify-content: flex-end;
  align-items: baseline;
  gap: 10px;
  margin-top: 10px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.detail-total strong {
  color: var(--nc-ink);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-md);
}

.empty-note {
  margin: 10px 0 0;
  padding: 12px;
  border: 1px dashed var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  text-align: center;
}

.action-bar {
  position: sticky;
  bottom: 0;
  z-index: 10;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 12px 16px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background: var(--nc-paper);
  box-shadow: 0 -2px 8px rgba(0, 0, 0, 0.06);
}

.action-bar__info {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.action-bar__buttons {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.auto-fill-note {
  margin: 0;
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  text-align: right;
}

.change-date-note {
  margin: 0 0 10px;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  line-height: 1.65;
}

.mono {
  font-family: var(--nc-font-mono);
}

@media (max-width: 1200px) {
  .scenario-grid {
    grid-template-columns: repeat(3, minmax(0, 1fr));
  }

  .form-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .field--wide {
    grid-column: span 2;
  }
}

@media (max-width: 700px) {
  .section-nav,
  .scenario-grid,
  .form-grid,
  .type-choice {
    grid-template-columns: minmax(0, 1fr);
  }

  .field--wide {
    grid-column: auto;
  }

  .candidate-card,
  .candidate-summary,
  .detail-head,
  .difficulty-head {
    flex-direction: column;
    align-items: stretch;
  }

  .action-bar {
    flex-direction: column;
    align-items: stretch;
  }

  .action-bar__buttons {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .action-bar__buttons :deep(.el-button) {
    width: 100%;
    margin-left: 0;
  }

  .readonly-with-action {
    flex-wrap: wrap;
  }

  .section-nav__item {
    text-align: left;
  }
}
</style>
