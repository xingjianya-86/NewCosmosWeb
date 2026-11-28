<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  ArrowLeft,
  Camera,
  Delete,
  Document,
  Money,
  Plus,
  Refresh,
  Search,
  Tickets,
  User,
  UserFilled,
} from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import { getIcd10 } from '@/api/icd10'
import {
  changeTempReliefApplyDate,
  confirmTempReliefApplication,
  createTempReliefApplication,
  getTempReliefApplication,
  getTempReliefCandidate,
  getTempReliefFamilyStatus,
  getTempReliefFormDefaults,
  getTempReliefInsuredIdentity,
  getTempReliefPolicySnapshot,
  getTempReliefPublicizePreview,
  getTempReliefSmallTiers,
  resolveTempReliefHukouType,
  searchTempReliefCandidates,
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

type FormMode = 'create' | 'edit' | 'view'
type ReliefType = 'Large' | 'Small'
type PaymentChannel = 'County' | 'Reserve'
type DifficultyType = '疾病' | '意外灾害' | '教育支出' | '其他困难'

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
  batchHintText: string
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

interface DiseaseSharedState {
  hospital: string
  treatStartDate: string | null
  treatEndDate: string | null
  medicalTotal: number | undefined
  insurancePaid: number | undefined
  selfPaid: number | undefined
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
const MAX_MEMBER_ROWS = 5
const HEAD_KEY = '__head__'

/** PC 的视觉分区顺序：类型与申请人(0) → 家庭成员(3) → 申请信息(1) → 金额与公示(2)。 */
const SECTION_ORDER: readonly number[] = [0, 3, 1, 2]
const SECTION_TABS = [
  { key: 0, label: '类型与申请人', icon: User },
  { key: 3, label: '家庭成员', icon: UserFilled },
  { key: 1, label: '申请信息', icon: Tickets },
  { key: 2, label: '金额与公示', icon: Money },
] as const

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
let tierTicket = 0

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
  if (!value) return '保存后生成'
  const text = value.slice(0, 10)
  return /^\d{4}-\d{2}-\d{2}$/.test(text) ? text.split('-').join('/') : '保存后生成'
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
    batchHintText: '',
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
    treatStartDate: todayText(),
    treatEndDate: todayText(),
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
    happenDate: todayText(),
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

const diseaseShared = reactive<DiseaseSharedState>({
  hospital: '',
  treatStartDate: todayText(),
  treatEndDate: todayText(),
  medicalTotal: undefined,
  insurancePaid: undefined,
  selfPaid: undefined,
})
const multiHospitalMode = ref(false)

const detail = ref<TempReliefDetail | null>(null)
const storedFamilySize = ref<number | null>(null)
const familyCategoryOptions = ref<string[]>([...FAMILY_CATEGORY_BASE])
const reasonDirty = ref(false)
let suppressReasonWrite = false

const loading = ref(false)
const saving = ref(false)
const confirming = ref(false)
const loadError = ref('')
const saveError = ref('')
const autoFilling = ref(false)
const activeSection = ref(0)

const candidateKeyword = ref('')
const candidates = ref<TempReliefCandidate[]>([])
const candidateLoading = ref(false)
const candidateSearched = ref(false)
const candidateSelected = ref(false)
const candidateSummary = ref('')
const selectedCandidateKey = ref('')
const manualEntry = ref(false)

/** 保障人群卡片仅作判定展示；业务确认暂时隐藏，判定逻辑仍保留。 */
const showPopulationCard = false

const identity = ref<TempReliefInsuredIdentityResponse | null>(null)
const identityLoading = ref(false)
const identityError = ref('')

const smallTiers = ref<TempReliefSmallTier[]>([])
const smallTiersBase = ref<number | null>(null)
const smallTiersLoading = ref(false)
const smallTiersError = ref('')

const photoVisible = ref(false)
const ocrVisible = ref(false)

const changeDateVisible = ref(false)
const changeDateValue = ref('')
const changeDateSaving = ref(false)

const lastLargeChannel = ref<PaymentChannel>('County')
const savedPublicizeDates = ref(false)
let publicizeTimer: number | null = null
let publicizePreviewTicket = 0

const isEditable = computed(() => {
  if (isView.value) return false
  if (isCreate.value) return true
  if (!detail.value) return false
  return form.status === 'Draft' || form.status === 'Returned'
})
const isSmall = computed(() => form.reliefType === 'Small')

/** 历史小额档案未记录档位：不自动猜档，提示重新选择；金额按原值展示。 */
const legacyTierMissing = computed(
  () => !isCreate.value && isSmall.value && !form.standardTier && Boolean(detail.value?.id),
)
const effectiveChannel = computed<PaymentChannel>(
  () => (isSmall.value ? 'Reserve' : form.paymentChannel),
)
const statusText = computed(() => detail.value?.statusText || STATUS_TEXT[form.status] || form.status || '草稿')
const printableId = computed<number | null>(() => (form.id > 0 ? form.id : null))
const canChangeDate = computed(() => !isCreate.value && isEditable.value && form.id > 0)

const sectionPosition = computed(() => SECTION_ORDER.indexOf(activeSection.value))
const canGoPrevious = computed(() => sectionPosition.value > 0)
const canGoNext = computed(() => sectionPosition.value >= 0 && sectionPosition.value < SECTION_ORDER.length - 1)
const isLastSection = computed(() => activeSection.value === 2)
const canSaveAndConfirm = computed(() => isEditable.value && isLastSection.value)
const canEnterManualEntry = computed(() => isEditable.value && !manualEntry.value && !candidateSelected.value)

const populationResolved = computed(
  () => Boolean(identity.value) || manualEntry.value || candidateSelected.value || Boolean(detail.value),
)
const populationStatusText = computed(() => {
  if (identity.value) return identity.value.populationStatusText
  if (manualEntry.value) return '不在保障人群内'
  if (detail.value) return detail.value.populationStatusText
  return '待判定'
})
const isInsured = computed(
  () => populationResolved.value && populationStatusText.value === '在保障人群内',
)
const isUninsured = computed(
  () => populationResolved.value && populationStatusText.value === '不在保障人群内',
)
const populationBasis = computed(() => {
  if (identity.value) {
    if (manualEntry.value) {
      return identity.value.isInsured
        ? `${identity.value.basisText}，请改用检索选择该申请人`
        : `${identity.value.basisText}（非在册登记）`
    }
    return identity.value.basisText
  }
  if (manualEntry.value) return '非在册登记：填写身份证后自动核查四类在册台账'
  if (detail.value) return `按保存时判定结果（身份证反查四类在册台账）：${detail.value.populationStatusText}`
  return '检索并选择申请人后，系统按身份证自动判定（人工不可改）'
})
const manualHitsLedger = computed(
  () => manualEntry.value && identity.value?.isInsured === true,
)

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
      relation: '本人/户主',
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

const reliefPersonCountHint = computed(() => (
  familySizeDisplay.value > 0
    ? `家庭人口 ${familySizeDisplay.value} 人；遇困人数含户主，须 >0 且 ≤ ${familySizeDisplay.value}`
    : '遇困人数含户主，须大于 0'
))

const amountHint = computed(() => {
  if (isSmall.value) {
    if (smallTiersLoading.value) return '正在读取服务端档位上限…'
    if (smallTiersError.value) return smallTiersError.value
    if (smallTiers.value.length === 0) return '未取到小额档位配置，无法填写金额。'
    if (!selectedTier.value) return '请选择 50% / 75% / 100% 档位。'
    return `档位上限 ${formatMoney(selectedTier.value.ceilingAmount)}（${selectedTier.value.personCount} 人 × 城市低保标准 ${formatMoney(selectedTier.value.standardValue)} × ${selectedTier.value.multiplier}）；确定金额可下调，不得超过该上限。`
  }
  if (effectiveChannel.value === 'Reserve') {
    return '备用金发放需手工填写金额；上限由服务端按遇困人数与城市低保标准校验，超出将拒绝保存。'
  }
  return '常规救助·上级部门发放：金额由上级按程序核定，系统不填写。'
})

const publicizeStartText = computed(() => {
  if (isSmall.value) return '小额快救免公示'
  return form.publicizeStartDate ? formatDate(form.publicizeStartDate) : '保存后生成'
})
const publicizeEndText = computed(() => {
  if (isSmall.value) return '小额快救免公示'
  return form.publicizeEndDate ? formatDate(form.publicizeEndDate) : '保存后生成'
})

const modeHint = computed(() => {
  if (isView.value) return '查看模式：仅可查看，保存与编辑动作已隐藏。'
  if (!isEditable.value) return `当前状态「${statusText.value}」不可编辑；状态动作请在列表页办理。`
  return ''
})

/**
 * 户主关系的两种历史显示值：旧词表「户主」/ 统一词表「本人/户主」（DictValueMapper）。
 * 判断户主一律走这里，拿单一文本比较会在换词表时静默失效。
 */
const HEAD_RELATION_LABELS: ReadonlySet<string> = new Set(['户主', '本人/户主'])

function isHeadMemberRow(row: MemberRow): boolean {
  if (row.idCard.trim() && applicant.idCard.trim()) {
    return row.idCard.trim().toLowerCase() === applicant.idCard.trim().toLowerCase()
  }
  return HEAD_RELATION_LABELS.has(row.relation.trim()) && row.memberName.trim() === applicant.name.trim()
}

function isApplicantSnapshot(member: TempReliefMemberSnapshot): boolean {
  if (member.idCard.trim() && applicant.idCard.trim()) {
    return member.idCard.trim().toLowerCase() === applicant.idCard.trim().toLowerCase()
  }
  return HEAD_RELATION_LABELS.has(member.relation.trim()) && member.memberName.trim() === applicant.name.trim()
}

function ensureCategoryOption(value: string): void {
  const category = normalizeFamilyCategory(value)
  if (category && !familyCategoryOptions.value.includes(category)) {
    familyCategoryOptions.value.push(category)
  }
  applicant.familyCategory = category
}

function goToSection(key: number): void {
  activeSection.value = key
}

function goPrevious(): void {
  if (!canGoPrevious.value) return
  activeSection.value = SECTION_ORDER[sectionPosition.value - 1]
}

function goNext(): void {
  if (!canGoNext.value) return
  activeSection.value = SECTION_ORDER[sectionPosition.value + 1]
}

function backToList(): void {
  void router.push('/social-assistance/temp-relief')
}

function candidateKey(candidate: TempReliefCandidate): string {
  return `${candidate.sourceTable}:${candidate.sourceFamilyId}`
}

const selectedCandidate = computed(
  () => candidates.value.find((candidate) => candidateKey(candidate) === selectedCandidateKey.value) ?? null,
)

function resetAll(): void {
  Object.assign(form, createEmptyForm())
  Object.assign(applicant, createEmptyApplicant())
  members.value = [createMemberRow()]
  diseases.value = []
  accidents.value = []
  educations.value = []
  Object.assign(diseaseShared, {
    hospital: '',
    treatStartDate: todayText(),
    treatEndDate: todayText(),
    medicalTotal: undefined,
    insurancePaid: undefined,
    selfPaid: undefined,
  })
  multiHospitalMode.value = false
  detail.value = null
  storedFamilySize.value = null
  familyCategoryOptions.value = [...FAMILY_CATEGORY_BASE]
  loading.value = false
  saving.value = false
  confirming.value = false
  loadError.value = ''
  saveError.value = ''
  autoFilling.value = false
  activeSection.value = 0
  candidateKeyword.value = ''
  candidates.value = []
  candidateLoading.value = false
  candidateSearched.value = false
  candidateSelected.value = false
  candidateSummary.value = ''
  selectedCandidateKey.value = ''
  manualEntry.value = false
  identity.value = null
  identityLoading.value = false
  identityError.value = ''
  smallTiers.value = []
  smallTiersBase.value = null
  smallTiersLoading.value = false
  smallTiersError.value = ''
  photoVisible.value = false
  ocrVisible.value = false
  changeDateVisible.value = false
  changeDateValue.value = ''
  changeDateSaving.value = false
  lastLargeChannel.value = 'County'
  reasonDirty.value = false
  suppressReasonWrite = false
  savedPublicizeDates.value = false
  publicizePreviewTicket += 1
  if (publicizeTimer !== null) {
    window.clearTimeout(publicizeTimer)
    publicizeTimer = null
  }
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
  applicant.village = current.village || '村委会'
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

function hasRowDivergence(rows: DiseaseRow[]): boolean {
  if (rows.length < 2) return false
  const distinct = <T>(values: T[]): boolean => new Set(values).size > 1

  const hospitals = rows
    .map((row) => row.hospital.trim())
    .filter(Boolean)
  if (distinct(hospitals)) return true

  const starts = rows
    .map((row) => row.treatStartDate?.slice(0, 10) ?? '')
    .filter(Boolean)
  if (distinct(starts)) return true

  const ends = rows
    .map((row) => row.treatEndDate?.slice(0, 10) ?? '')
    .filter(Boolean)
  if (distinct(ends)) return true

  const amounts = (selector: (row: DiseaseRow) => number | undefined): boolean => {
    const values = rows
      .map((row) => selector(row))
      .filter((value): value is number => value !== undefined)
    return distinct(values)
  }
  if (amounts((row) => row.medicalTotal)) return true
  if (amounts((row) => row.insurancePaid)) return true
  if (amounts((row) => row.selfPaid)) return true
  return false
}

function setSharedFromRow(row: DiseaseRow): void {
  diseaseShared.hospital = row.hospital
  diseaseShared.treatStartDate = row.treatStartDate ?? todayText()
  diseaseShared.treatEndDate = row.treatEndDate ?? todayText()
  diseaseShared.medicalTotal = row.medicalTotal
  diseaseShared.insurancePaid = row.insurancePaid
  diseaseShared.selfPaid = row.selfPaid
}

function applySharedToRows(): void {
  diseases.value.forEach((row) => {
    row.hospital = diseaseShared.hospital
    row.treatStartDate = diseaseShared.treatStartDate
    row.treatEndDate = diseaseShared.treatEndDate
    row.medicalTotal = diseaseShared.medicalTotal
    row.insurancePaid = diseaseShared.insurancePaid
    row.selfPaid = diseaseShared.selfPaid
  })
}

function handleSharedDiseaseChange(): void {
  if (multiHospitalMode.value) return
  applySharedToRows()
  regenerateDifficultyReason()
}

function handleMultiHospitalChange(value: string | number | boolean): void {
  if (!isEditable.value) return
  const enabled = Boolean(value)
  if (enabled) {
    applySharedToRows()
    regenerateDifficultyReason()
    return
  }
  const first = diseases.value[0]
  if (first) setSharedFromRow(first)
  applySharedToRows()
  regenerateDifficultyReason()
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

  multiHospitalMode.value = hasRowDivergence(diseases.value)
  if (!multiHospitalMode.value) {
    const first = diseases.value[0]
    if (first) setSharedFromRow(first)
  }

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
  if (form.reliefType === 'Large') lastLargeChannel.value = form.paymentChannel
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
  form.batchHintText = ''
  savedPublicizeDates.value = Boolean(form.publicizeStartDate || form.publicizeEndDate)
  storedFamilySize.value = current.familySize

  hydrateApplicant(current)
  hydrateMembers(current)
  hydrateDetails(current)
  resolveBeneficiaryKey(current.beneficiaryIdCard)

  manualEntry.value = current.isManualEntry
  candidateSelected.value = !current.isManualEntry && Boolean(current.sourceTable)
  candidateSummary.value = current.isManualEntry
    ? ''
    : `已选择：${current.applicantName}${current.sourceName ? `（${current.sourceName}）` : ''}`

  if (form.reliefType === 'Small') void loadSmallTiers()

  // 读入已有申请：救助原因取保存值，视为“已有内容”，类型/明细变化前不自动覆盖
  reasonDirty.value = true
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
    await refreshIdentity(true)
    if (!current.policyEnjoyed || !current.familyMemberStatus) {
      void loadPolicyAndHealth(current.applicantIdCard, false)
    }
    if (!savedPublicizeDates.value) schedulePublicizePreview()
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
      ElMessage.warning(`${result.basisText}，请改用检索选择该申请人`)
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

async function loadPolicyAndHealth(idCard: string, overwrite: boolean): Promise<void> {
  const id = idCard.trim()
  if (!id) return

  try {
    const policy = await getTempReliefPolicySnapshot(id)
    if ((overwrite || !form.policyEnjoyed.trim()) && policy.length > 0) {
      form.policyEnjoyed = `${policy.join('；')}。`
    }
  } catch {
    // 政策快照失败不阻断录入，用户可手工补录。
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

/** 新建表单默认值：户籍地址与乡镇验收人；仅填空，已有值不覆盖。 */
async function loadFormDefaults(): Promise<void> {
  try {
    const defaults = await getTempReliefFormDefaults()
    const province = defaults.hukouProvince?.trim() || '黑龙江省'
    if (!applicant.hukouProvince.trim() || applicant.hukouProvince.trim() === '黑龙江省') {
      applicant.hukouProvince = province
    }
    if (!applicant.hukouCity.trim() && defaults.hukouCity?.trim()) {
      applicant.hukouCity = defaults.hukouCity.trim()
    }
    if (!applicant.hukouDistrict.trim() && defaults.hukouCounty?.trim()) {
      applicant.hukouDistrict = defaults.hukouCounty.trim()
    }
    if (!applicant.hukouTown.trim() && defaults.hukouTown?.trim()) {
      applicant.hukouTown = defaults.hukouTown.trim()
    }
    if (!form.acceptancePerson.trim() && defaults.acceptancePerson?.trim()) {
      form.acceptancePerson = defaults.acceptancePerson.trim()
    }
  } catch {
    // 默认值加载失败不阻断录入；用户可手工填写。
  }
}

/** 清空公示预览与批次提示（小额快救 / 切换类型时）。 */
function clearPublicizePreview(): void {
  publicizePreviewTicket += 1
  if (publicizeTimer !== null) {
    window.clearTimeout(publicizeTimer)
    publicizeTimer = null
  }
  form.publicizeStartDate = null
  form.publicizeEndDate = null
  form.batchHintText = ''
}

/**
 * 申请日期/救助类型变化后的公示预览（防抖 + 请求代际）。
 * 仅新建、草稿且从未保存过公示日期的记录应用预览，避免覆盖库里已有值。
 */
function schedulePublicizePreview(): void {
  if (!isCreate.value && (!isEditable.value || form.status !== 'Draft')) return
  if (!isCreate.value && savedPublicizeDates.value) return
  if (form.reliefType === 'Small') {
    clearPublicizePreview()
    return
  }

  publicizePreviewTicket += 1
  const ticket = publicizePreviewTicket
  if (publicizeTimer !== null) window.clearTimeout(publicizeTimer)
  publicizeTimer = window.setTimeout(() => {
    publicizeTimer = null
    void applyPublicizePreview(ticket)
  }, 300)
}

async function applyPublicizePreview(ticket: number): Promise<void> {
  if (!isCreate.value && (!isEditable.value || form.status !== 'Draft' || savedPublicizeDates.value)) {
    return
  }
  if (form.reliefType === 'Small') {
    clearPublicizePreview()
    return
  }

  const applyDate = form.applyDate
  const reliefType = form.reliefType
  if (!applyDate) return

  try {
    const preview = await getTempReliefPublicizePreview(applyDate, reliefType)
    if (ticket !== publicizePreviewTicket) return
    if (form.applyDate !== applyDate || form.reliefType !== reliefType) return
    if (!isCreate.value
      && (!isEditable.value || form.status !== 'Draft' || savedPublicizeDates.value)) {
      return
    }

    form.publicizeStartDate = preview.publicizeStartDate?.slice(0, 10) ?? null
    form.publicizeEndDate = preview.publicizeEndDate?.slice(0, 10) ?? null
    form.batchHintText = preview.batchHintText ?? ''
  } catch {
    // 预览失败不阻断保存；保留现有值，服务端保存时会重新推导。
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
    if (!form.standardTier && smallTiers.value.length > 0 && isCreate.value) {
      form.standardTier = smallTiers.value[0].code
      form.confirmAmount = smallTiers.value[0].ceilingAmount
    } else if (!form.standardTier) {
      // 历史档位缺失时不猜档，保留已存金额供核对；用户重新选档后再按档位校准。
      form.confirmAmount = detail.value?.confirmAmount ?? undefined
    } else {
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
  selectedCandidateKey.value = ''
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
  form.difficultyType = '其他困难'
  form.difficultyReason = ''
  diseases.value = []
  accidents.value = []
  educations.value = []
  multiHospitalMode.value = false
  reasonDirty.value = false
  regenerateDifficultyReason()
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
    identityError.value = ''
    applyCandidateDetail(current)
    await refreshIdentity(true)
    candidateSelected.value = true
    candidateSummary.value = current.matchedPersonInfo
      ? `已选择：${current.name}（${current.sourceName}）· ${current.matchedPersonInfo}`
      : `已选择：${current.name}（${current.sourceName}）`
    candidates.value = []
    candidateKeyword.value = ''
    selectedCandidateKey.value = ''

    if (!applicant.hukouType) {
      try {
        applicant.hukouType = await resolveTempReliefHukouType(current.idCard)
      } catch {
        applicant.hukouType = ''
      }
    }

    if (form.reliefType === 'Small') await loadSmallTiers()
    await loadPolicyAndHealth(current.idCard, false)
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '选择申请人失败'))
  } finally {
    candidateLoading.value = false
    autoFilling.value = false
  }
}

function useSelectedCandidate(): void {
  if (!selectedCandidate.value) return
  void selectCandidate(selectedCandidate.value)
}

function enterManualEntry(): void {
  if (!canEnterManualEntry.value) return
  manualEntry.value = true
  applicant.sourceTable = ''
  applicant.sourceFamilyId = 0
  candidateSelected.value = false
  candidateSummary.value = ''
  candidates.value = []
  candidateSearched.value = false
  selectedCandidateKey.value = ''
  identity.value = null
  identityError.value = ''
}

function handleManualIdInput(): void {
  if (!manualEntry.value) return
  identity.value = null
  identityError.value = ''
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

  await refreshIdentity(false)
}

function formatCnDate(value: string | null): string {
  if (!value) return ''
  const [year, month, day] = value.slice(0, 10).split('-')
  if (!year || !month || !day) return ''
  return `${Number(year)}年${Number(month)}月${Number(day)}日`
}

function formatTreatRangeCn(start: string | null, end: string | null): string {
  if (!start && !end) return ''
  if (!start) return formatCnDate(end)
  if (!end) return formatCnDate(start)
  const startText = start.slice(0, 10)
  const endText = end.slice(0, 10)
  return startText === endText
    ? formatCnDate(start)
    : `${formatCnDate(start)}至${formatCnDate(end)}`
}

function reasonHead(names: string[]): string {
  const valid = Array.from(new Set(
    names.map((name) => name.trim()).filter((name) => Boolean(name) && name !== '家庭成员'),
  ))
  if (valid.length === 1) return `申请人家庭成员${valid[0]}`
  if (valid.length > 1) return `申请人家庭成员${valid.join('、')}`
  return '申请人家庭成员'
}

function fallbackReasonHead(): string {
  return applicant.name.trim()
    ? `申请人家庭成员${applicant.name.trim()}`
    : '申请人家庭成员'
}

function diseaseCostText(row: DiseaseRow): string {
  const parts: string[] = []
  if (row.medicalTotal !== undefined) parts.push(`费用总额${row.medicalTotal.toFixed(2)}元`)
  if (row.insurancePaid !== undefined) parts.push(`报销${row.insurancePaid.toFixed(2)}元`)
  if (row.selfPaid !== undefined) parts.push(`自付${row.selfPaid.toFixed(2)}元`)
  return parts.join('，')
}

function sharedDiseaseCostText(): string {
  const parts: string[] = []
  if (diseaseShared.medicalTotal !== undefined) parts.push(`本次医疗费用总额${diseaseShared.medicalTotal.toFixed(2)}元`)
  if (diseaseShared.insurancePaid !== undefined) parts.push(`经基本医疗保险及大病保险等报销${diseaseShared.insurancePaid.toFixed(2)}元`)
  if (diseaseShared.selfPaid !== undefined) parts.push(`个人自付${diseaseShared.selfPaid.toFixed(2)}元`)
  return parts.join('，')
}

function buildDiseaseReasonText(): string {
  const items = diseases.value.filter(
    (row) => row.diseaseName.trim() || row.diseaseCode.trim(),
  )
  if (items.length === 0) return ''

  const ownerOf = (row: DiseaseRow): string => (
    row.memberName.trim() || applicant.name.trim() || '家庭成员'
  )
  const owners = Array.from(new Set(items.map(ownerOf)))
  const head = reasonHead(owners.length === 1 ? owners : [])
  const itemOwner = (row: DiseaseRow): string => (
    owners.length === 1 ? '' : `${ownerOf(row)} `
  )
  const diseaseLabel = (row: DiseaseRow): string => (
    `${row.diseaseName.trim()}${row.diseaseCode.trim() ? `（编码${row.diseaseCode.trim()}）` : ''}`
  )

  if (multiHospitalMode.value) {
    const segments = items.map((row) => {
      const hospital = row.hospital.trim() || '医院'
      const date = formatTreatRangeCn(row.treatStartDate, row.treatEndDate)
      const cost = diseaseCostText(row)
      let segment = `${itemOwner(row)}${diseaseLabel(row)}，${date ? `于${date}在${hospital}住院治疗` : `于${hospital}住院治疗`}`
      if (cost) segment += `，${cost}`
      return segment
    })
    const body = segments.length === 1
      ? `因患${segments[0]}`
      : `因患以下疾病：${segments.map((segment, index) => `${index + 1}、${segment}`).join('；')}`
    return `${head}${body}，医疗费用支出较大，造成家庭基本生活暂时陷入困境，特申请临时救助。`
  }

  const diseaseText = items.length === 1
    ? diseaseLabel(items[0])
    : `以下疾病：${items.map((row, index) => `${index + 1}、${itemOwner(row)}${diseaseLabel(row)}`).join('；')}`
  const dateText = formatTreatRangeCn(diseaseShared.treatStartDate, diseaseShared.treatEndDate)
  const hospitalText = diseaseShared.hospital.trim() || '医院'
  const body = dateText
    ? `于${dateText}在${hospitalText}住院治疗`
    : `于${hospitalText}住院治疗`
  const costText = sharedDiseaseCostText()
  const tail = costText
    ? `，${costText}，个人自付费用较高，造成家庭基本生活暂时陷入困境，特申请临时救助。`
    : '，医疗费用支出较大，造成家庭基本生活暂时陷入困境，特申请临时救助。'
  return `${head}因患${diseaseText}，${body}${tail}`
}

function buildAccidentReasonText(): string {
  const items = accidents.value.filter(
    (row) => row.accidentType.trim()
      || row.happenDate
      || row.happenPlace.trim()
      || row.injurySituation.trim(),
  )
  if (items.length === 0) return ''

  const ownerOf = (row: AccidentRow): string => (
    row.memberName.trim() || applicant.name.trim() || '家庭成员'
  )
  const owners = Array.from(new Set(items.map(ownerOf)))
  const head = reasonHead(owners.length === 1 ? owners : [])
  const itemOwner = (row: AccidentRow): string => (
    owners.length === 1 ? '' : `${ownerOf(row)} `
  )

  const segments = items.map((row) => {
    const parts: string[] = []
    if (row.accidentType.trim()) parts.push(`${itemOwner(row)}${row.accidentType.trim()}`)
    else if (row.memberName.trim()) parts.push(row.memberName.trim())
    if (row.happenDate) parts.push(formatCnDate(row.happenDate))
    if (row.happenPlace.trim()) parts.push(row.happenPlace.trim())
    let segment = parts.join('，')
    if (row.injurySituation.trim()) segment += `（${row.injurySituation.trim()}）`
    const lossParts: string[] = []
    if (row.propertyLoss !== undefined) lossParts.push(`财产损失${row.propertyLoss.toFixed(2)}元`)
    if (row.compensationPaid !== undefined) lossParts.push(`已获保险及赔偿${row.compensationPaid.toFixed(2)}元`)
    if (lossParts.length > 0) segment += `，${lossParts.join('，')}`
    return segment
  })

  const body = segments.length === 1
    ? (segments[0].startsWith('因') ? segments[0] : `因${segments[0]}`)
    : `因发生以下意外灾害：${segments.map((segment, index) => `${index + 1}、${segment}`).join('；')}`
  return `${head}${body}，家庭基本生活暂时陷入困境，特申请临时救助。`
}

function buildEducationReasonText(): string {
  const items = educations.value.filter(
    (row) => row.studentName.trim()
      || row.educationStage.trim()
      || row.schoolName.trim()
      || row.tuitionFee !== undefined,
  )
  if (items.length === 0) return ''

  const studentOf = (row: EducationRow): string => (
    row.studentName.trim() || applicant.name.trim() || '家庭成员'
  )
  const students = Array.from(new Set(items.map(studentOf)))
  const head = reasonHead(students.length === 1 ? students : [])

  const segments = items.map((row) => {
    const parts: string[] = []
    if (row.studentName.trim()) parts.push(row.studentName.trim())
    if (row.educationStage.trim()) parts.push(`就读${row.educationStage.trim()}`)
    if (row.schoolName.trim()) parts.push(row.schoolName.trim())
    const duration = SCHOOL_DURATION_OPTIONS.find((option) => option.value === row.schoolDuration)
    if (duration) parts.push(duration.label)
    let text = parts.join('，')
    if (row.tuitionFee !== undefined) text += `（学费${row.tuitionFee.toFixed(2)}元）`
    return text
  })

  const body = segments.length === 1
    ? `因家庭子女就学，${segments[0]}`
    : `因家庭子女就学，教育支出明细如下：${segments.map((segment, index) => `${index + 1}、${segment}`).join('；')}`
  return `${head}${body}，学费等教育支出较大，家庭基本生活暂时陷入困境，特申请临时救助。`
}

function buildDifficultyReasonText(): string {
  if (form.difficultyType === '疾病') {
    const text = buildDiseaseReasonText()
    if (text) return text
    return `${fallbackReasonHead()}因患疾病，医疗费用支出较大，造成家庭基本生活暂时陷入困境，特申请临时救助。`
  }
  if (form.difficultyType === '意外灾害') {
    const text = buildAccidentReasonText()
    if (text) return text
    return `${fallbackReasonHead()}因意外灾害，家庭基本生活暂时陷入困境，特申请临时救助。`
  }
  if (form.difficultyType === '教育支出') {
    const text = buildEducationReasonText()
    if (text) return text
    return `${fallbackReasonHead()}因家庭子女就学，教育支出较大，造成家庭基本生活暂时陷入困境，特申请临时救助。`
  }
  const category = applicant.familyCategory.trim() || '困难群众'
  return `申请人家庭为${category}对象，因突发困难导致基本生活暂时陷入困境，特申请临时救助。`
}

function regenerateDifficultyReason(): void {
  if (reasonDirty.value) return
  const text = buildDifficultyReasonText()
  suppressReasonWrite = true
  form.difficultyReason = text
  suppressReasonWrite = false
}

function handleDifficultyTypeChange(): void {
  // 与 PC 一致：切换困难类型视为重新起草，清掉手动编辑标记后按新类型生成
  reasonDirty.value = false
  regenerateDifficultyReason()
}

function handleDetailChanged(): void {
  regenerateDifficultyReason()
}

/**
 * ICD-10 编码输入完成（blur/change）后查询常见病名：仅回填空病名，失败静默降级。
 * 请求返回时再次核对行仍在、编码未变、病名仍为空，避免竞态覆盖。
 */
async function handleDiseaseCodeChange(row: DiseaseRow): Promise<void> {
  handleDetailChanged()
  const code = row.diseaseCode.trim()
  if (!code || row.diseaseName.trim()) return

  try {
    const result = await getIcd10(code)
    const current = diseases.value.find((item) => item.key === row.key)
    if (!current) return
    if (current.diseaseCode.trim().toLowerCase() !== code.toLowerCase()) return
    if (current.diseaseName.trim()) return
    const name = result.name?.trim()
    if (!name) return
    current.diseaseName = name
    regenerateDifficultyReason()
  } catch {
    // ICD 字典查询失败不阻断录入，用户可手动填写病名。
  }
}

function selectReliefType(next: ReliefType): void {
  if (!isEditable.value || form.reliefType === next) return
  if (next === 'Small') {
    lastLargeChannel.value = form.paymentChannel
    form.reliefType = 'Small'
    form.paymentChannel = 'Reserve'
    form.standardTier = ''
    form.confirmAmount = undefined
    clearPublicizePreview()
    void loadSmallTiers()
    return
  }

  form.reliefType = 'Large'
  form.paymentChannel = lastLargeChannel.value
  form.standardTier = ''
  form.confirmAmount = undefined
  void loadSmallTiers()
  schedulePublicizePreview()
}

function handleApplyDateChange(): void {
  schedulePublicizePreview()
}

function selectChannel(next: PaymentChannel): void {
  if (!isEditable.value) return
  form.paymentChannel = next
  if (next === 'County') {
    form.standardTier = ''
    form.confirmAmount = undefined
  }
}

function handlePersonCountChange(): void {
  const value = Math.max(Math.min(form.reliefPersonCount || 1, familySizeDisplay.value), 1)
  form.reliefPersonCount = value
  if (isSmall.value) void loadSmallTiers()
}

function handleTierSelect(next: string | number | boolean | undefined): void {
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
    ElMessage.warning(`家庭成员最多填写 ${MAX_MEMBER_ROWS} 行（审批表展示上限）`)
    return
  }
  members.value.push(createMemberRow())
}

function removeMember(index: number): void {
  if (members.value.length <= 1) {
    ElMessage.warning('至少保留一名家庭成员')
    return
  }
  members.value.splice(index, 1)
  handleMemberChanged()
}

function addDisease(): void {
  if (diseases.value.length >= MAX_DETAIL_ROWS) {
    ElMessage.warning(`最多添加 ${MAX_DETAIL_ROWS} 条疾病明细`)
    return
  }
  const row = createDiseaseRow()
  if (!multiHospitalMode.value) {
    row.hospital = diseaseShared.hospital
    row.treatStartDate = diseaseShared.treatStartDate
    row.treatEndDate = diseaseShared.treatEndDate
    row.medicalTotal = diseaseShared.medicalTotal
    row.insurancePaid = diseaseShared.insurancePaid
    row.selfPaid = diseaseShared.selfPaid
  }
  diseases.value.push(row)
  regenerateDifficultyReason()
}

function removeDisease(index: number): void {
  diseases.value.splice(index, 1)
  regenerateDifficultyReason()
}

function addAccident(): void {
  if (accidents.value.length >= MAX_DETAIL_ROWS) {
    ElMessage.warning(`最多添加 ${MAX_DETAIL_ROWS} 条意外灾害明细`)
    return
  }
  accidents.value.push(createAccidentRow())
  regenerateDifficultyReason()
}

function removeAccident(index: number): void {
  accidents.value.splice(index, 1)
  regenerateDifficultyReason()
}

function addEducation(): void {
  if (educations.value.length >= MAX_DETAIL_ROWS) {
    ElMessage.warning(`最多添加 ${MAX_DETAIL_ROWS} 条教育支出明细`)
    return
  }
  educations.value.push(createEducationRow())
  regenerateDifficultyReason()
}

function removeEducation(index: number): void {
  educations.value.splice(index, 1)
  regenerateDifficultyReason()
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
  if (!populationResolved.value) {
    return '请先检索并选择申请人（或转非在册登记），系统将自动判定保障人群'
  }
  if (manualHitsLedger.value) {
    return identity.value?.basisText
      ? `${identity.value.basisText}，请改用检索选择该申请人`
      : '该身份证命中四类在册台账，请改用检索选择该申请人'
  }
  if (manualEntry.value) {
    if (!applicant.name.trim()) return '请填写申请人姓名'
    if (!applicant.idCard.trim()) return '请填写申请人身份证号'
    if (!isValidIdCard(applicant.idCard)) return '申请人身份证号格式不正确'
  } else if (!candidateSelected.value && !applicant.name.trim()) {
    return '请先检索并选择申请人；检索不到可点「转非在册登记」'
  }
  if (!applicant.idCard.trim()) return '申请人身份证号缺失'
  if (!form.applyDate) return '请选择申请日期'
  if (form.applyDate > maxApplyDateText()) {
    return `申请日期最晚为 ${maxApplyDateText()}`
  }
  if (form.reliefPersonCount <= 0) return '家庭遇困人数（含户主）须大于 0'
  if (form.reliefPersonCount > familySizeDisplay.value) {
    return `家庭遇困人数（${form.reliefPersonCount}）不能大于家庭人口（${familySizeDisplay.value}）`
  }
  if (!selectedBeneficiary.value || !selectedBeneficiary.value.idCard) {
    return '请选择申请成员作为救助对象'
  }
  if (!form.difficultyReason.trim()) return '请填写申请救助原因/困难情况'

  if (form.difficultyType === '疾病') {
    if (diseases.value.length === 0) return '困难类型为疾病，请至少添加一条疾病明细'
    if (diseases.value.some((row) => !row.diseaseName.trim() && !row.diseaseCode.trim())) {
      return '请填写每条疾病明细的诊断结果或疾病编码'
    }
    if (multiHospitalMode.value) {
      if (diseases.value.some((row) => !row.hospital.trim())) {
        return '多医院模式下，请为每条疾病明细填写所在医院'
      }
      if (diseases.value.some((row) => (row.selfPaid ?? 0) < 0)) {
        return '自费费用不能为负数'
      }
    } else if ((diseaseShared.selfPaid ?? 0) < 0) {
      return '自费费用不能为负数'
    }
  }

  if (form.difficultyType === '意外灾害' && accidents.value.length === 0) {
    return '困难类型为意外灾害，请至少添加一条意外灾害明细'
  }
  if (form.difficultyType === '教育支出' && educations.value.length === 0) {
    return '困难类型为教育支出，请至少添加一条教育支出明细'
  }

  if (isSmall.value) {
    if (!form.standardTier) return '请选择小额快救政策档位（50%/75%/100%）'
    if (!form.confirmAmount || form.confirmAmount <= 0) return '确定金额须为大于 0 的数字'
    if (selectedTier.value && form.confirmAmount > selectedTier.value.ceilingAmount) {
      return `确定金额不能超过档位上限 ${formatMoney(selectedTier.value.ceilingAmount)}`
    }
  } else if (effectiveChannel.value === 'Reserve') {
    if (!form.confirmAmount || form.confirmAmount <= 0) return '备用金发放请填写救助金额'
  }

  if (form.publicizeStartDate && form.publicizeEndDate
    && form.publicizeEndDate < form.publicizeStartDate) {
    return '公示结束日期不能早于开始日期'
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
      ?? (manualEntry.value ? 'Uninsured' : null),
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
        hospital: (multiHospitalMode.value ? row.hospital : diseaseShared.hospital).trim(),
        treatStartDate: multiHospitalMode.value ? row.treatStartDate : diseaseShared.treatStartDate,
        treatEndDate: multiHospitalMode.value ? row.treatEndDate : diseaseShared.treatEndDate,
        medicalTotal: multiHospitalMode.value ? (row.medicalTotal ?? null) : (diseaseShared.medicalTotal ?? null),
        insurancePaid: multiHospitalMode.value ? (row.insurancePaid ?? null) : (diseaseShared.insurancePaid ?? null),
        selfPaid: multiHospitalMode.value ? (row.selfPaid ?? null) : (diseaseShared.selfPaid ?? null),
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
  saveError.value = ''
  if (manualEntry.value && isValidIdCard(applicant.idCard)) {
    await refreshIdentity(true)
    if (manualHitsLedger.value) {
      const message = identity.value?.basisText
        ? `${identity.value.basisText}，请改用检索选择该申请人`
        : '该身份证命中四类在册台账，请改用检索选择该申请人'
      ElMessage.warning(message)
      return null
    }
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
    }

    return id
  } catch (error) {
    const message = getErrorMessage(error, '保存临时救助申请失败')
    saveError.value = message
    ElMessage.error(message)
    return null
  } finally {
    saving.value = false
  }
}

async function saveDraft(): Promise<void> {
  const id = await persist()
  if (id === null) return
  ElMessage.success(form.status === 'Returned' ? '已保存，申请仍为退回补正状态' : '草稿已保存')
  await router.push('/social-assistance/temp-relief')
}

async function saveAndConfirm(): Promise<void> {
  const id = await persist()
  if (id === null) return

  confirming.value = true
  saveError.value = ''
  try {
    await confirmTempReliefApplication(id)
    ElMessage.success('保存并确认成功')
    await router.push(`/social-assistance/temp-relief/${id}/output`)
  } catch (error) {
    const message = getErrorMessage(error, '确认失败，已保存当前修改')
    saveError.value = message
    ElMessage.error(message)
  } finally {
    confirming.value = false
  }
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

  assign('姓名', applicant.name, result.name.trim(), () => { applicant.name = result.name.trim() })
  assign('身份证号', applicant.idCard, result.idCard.trim().toUpperCase(), () => {
    applicant.idCard = result.idCard.trim().toUpperCase()
  })
  assign('住址（详细地址）', applicant.detailAddress, stripKnownAddressPrefix(result.address), () => {
    applicant.detailAddress = stripKnownAddressPrefix(result.address)
  })
  assign('性别', applicant.gender, result.gender.trim(), () => { applicant.gender = result.gender.trim() })

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
    } catch {
      // 取消/关闭均视为「仅填空」：不覆盖已修改内容。
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
    ElMessage.info('未回填任何字段，现有内容已保留')
    return
  }

  ElMessage.success(`已回填：${Array.from(new Set(emptyFields)).join('、')}`)
  await refreshIdentity(true)
  await loadPolicyAndHealth(applicant.idCard, false)

  if (!manualEntry.value && originalIdCard && applicant.idCard.trim() !== originalIdCard) {
    ElMessage.warning('身份证号与检索来源不一致，服务端将拒绝保存；请重新检索选择申请人。')
  }
}

function handlePhotoChanged(): void {
  // 照片增删后无需改表单数据；保留通知入口便于后续扩展。
}

function refreshPage(): void {
  void loadDetail()
}

function hydrateForCreate(): void {
  form.reportUnit = auth.user?.organizationName ?? ''
  form.applyDate = todayText()
  form.reportTime = new Date().toISOString()
  form.reliefPersonCount = 1
}

async function load(): Promise<void> {
  resetAll()

  if (isCreate.value) {
    hydrateForCreate()
    schedulePublicizePreview()
    await loadFormDefaults()
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
    // 仅表单路由（/new、/:id/edit）触发重载；离开到列表/输出页时不重载。
    if (!route.path.endsWith('/new') && !route.path.endsWith('/edit')) return
    const raw = route.params.id
    const target = Array.isArray(raw) ? raw[0] : raw
    if (target && form.id > 0 && Number(target) === form.id) return
    void load()
  },
)

watch(familySizeDisplay, (next) => {
  if (form.reliefPersonCount > next) form.reliefPersonCount = Math.max(next, 1)
})

watch(
  () => form.difficultyReason,
  () => {
    if (suppressReasonWrite) return
    reasonDirty.value = true
  },
  { flush: 'sync' },
)

watch(beneficiaryOptions, (options) => {
  if (!options.some((option) => option.key === form.beneficiaryKey)) {
    form.beneficiaryKey = HEAD_KEY
  }
})

onMounted(() => {
  void load()
})

onBeforeUnmount(() => {
  if (publicizeTimer !== null) {
    window.clearTimeout(publicizeTimer)
    publicizeTimer = null
  }
  publicizePreviewTicket += 1
})
</script>

<template>
  <div class="nc-page temp-relief-form">
    <NcPageHeader
      :title="isCreate ? '新建临时救助申请' : isView ? '查看临时救助申请' : '临时救助申请'"
      description="申请人来源仅限导入台账库与最低生活保障申请库"
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
        <span v-if="detail?.categoryDisplayName" class="mode-banner__category">{{ detail.categoryDisplayName }}</span>
        <span v-if="modeHint" class="mode-banner__hint">{{ modeHint }}</span>
      </div>

      <!-- 分区 Tab（视觉顺序与 PC 一致） -->
      <div class="form-tabs" role="tablist" aria-label="表单分区">
        <button
          v-for="tab in SECTION_TABS"
          :key="tab.key"
          type="button"
          role="tab"
          class="form-tabs__item"
          :class="{ 'is-active': activeSection === tab.key }"
          :aria-selected="activeSection === tab.key"
          @click="goToSection(tab.key)"
        >
          <el-icon :size="16"><component :is="tab.icon" /></el-icon>
          <span>{{ tab.label }}</span>
        </button>
      </div>

      <!-- ════════════ 第 1 页：类型与申请人 ════════════ -->
      <div v-show="activeSection === 0" class="form-section">
        <NcCard v-if="showPopulationCard" title="保障人群 *">
          <template #actions>
            <span class="card-hint">系统自动判定</span>
          </template>
          <div class="choice-grid">
            <div class="choice-card" :class="{ 'is-selected': isInsured }">
              <strong>在保障人群内</strong>
              <span>四类在册：低保/特困/边缘/刚性支出；5 日办结</span>
            </div>
            <div class="choice-card" :class="{ 'is-selected': isUninsured }">
              <strong>不在保障人群内</strong>
              <span>手工登记；需核对/公示，15 日办结</span>
            </div>
          </div>
          <p class="section-note section-note--spaced">
            {{ populationBasis }}
          </p>
          <p v-if="identityLoading" class="section-note">正在判定…</p>
          <p v-if="identityError" class="error-note">{{ identityError }}</p>
        </NcCard>

        <NcCard title="救助类型 *">
          <div class="choice-grid">
            <button
              type="button"
              class="choice-card"
              :class="{ 'is-selected': form.reliefType === 'Large', 'is-disabled': !isEditable }"
              @click="selectReliefType('Large')"
            >
              <strong>常规救助</strong>
              <span>金额不填写，按程序审批核定</span>
            </button>
            <button
              type="button"
              class="choice-card"
              :class="{ 'is-selected': form.reliefType === 'Small', 'is-disabled': !isEditable }"
              @click="selectReliefType('Small')"
            >
              <strong>小额快救</strong>
              <span>免公示、免核对，≤2 日；按城市低保标准 × 人数 × 档位</span>
            </button>
          </div>
          <p class="section-note section-note--spaced">
            请根据实际救助情形选择类型，审批表将随之区分（常规审批表 / 小额快救审批表）。
          </p>
        </NcCard>

        <NcCard title="选择申请人" subtitle="统一检索台账与申请库，系统自动判定大类">
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
          </div>

          <div v-if="candidates.length > 0" class="candidate-list">
            <button
              v-for="candidate in candidates"
              :key="candidateKey(candidate)"
              type="button"
              class="candidate-card"
              :class="{ 'is-selected': selectedCandidateKey === candidateKey(candidate) }"
              @click="selectedCandidateKey = candidateKey(candidate)"
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
            </button>
          </div>

          <p v-else-if="candidateSearched && isEditable && !candidateSelected && !manualEntry" class="empty-note">
            未检索到符合条件的申请人。请核对姓名或身份证号，或转非在册手工登记。
          </p>

          <div v-if="isEditable && !candidateSelected && !manualEntry" class="candidate-actions">
            <el-button
              type="primary"
              :disabled="!selectedCandidate"
              :loading="candidateLoading"
              @click="useSelectedCandidate"
            >
              使用该申请人
            </el-button>
            <el-button v-if="canEnterManualEntry" @click="enterManualEntry">
              未检索到？转非在册登记
            </el-button>
          </div>

          <el-alert
            v-if="manualEntry"
            class="manual-alert"
            type="warning"
            :closable="false"
            show-icon
            title="非在册登记"
          >
            非在册登记：请直接填写申请人信息；需入户/经济核对与 3 日公示，15 日办结。填写身份证后系统自动核查四类在册台账。
          </el-alert>

          <el-alert
            v-if="manualHitsLedger"
            class="manual-alert"
            type="error"
            :closable="false"
            show-icon
            title="该身份证命中四类在册台账，请改用上方检索选择该申请人（不可手工登记）。"
          />

          <div v-if="candidateSelected || manualEntry" class="candidate-summary">
            <span>{{ candidateSummary || '非在册手工登记' }}</span>
          </div>

          <div class="form-grid form-grid--spaced">
            <label class="field">
              <span class="field-label">申请人姓名 *</span>
              <el-input
                v-model="applicant.name"
                :disabled="!isEditable || !manualEntry"
                maxlength="50"
              />
            </label>
            <div class="field">
              <span class="field-label">身份证号 *</span>
              <div class="inline-actions">
                <el-input
                  v-model="applicant.idCard"
                  :disabled="!isEditable || !manualEntry"
                  maxlength="18"
                  class="mono"
                  @input="handleManualIdInput"
                  @blur="handleManualIdBlur"
                />
                <el-button size="small" :icon="Document" :disabled="!isEditable" @click="ocrVisible = true">
                  扫描
                </el-button>
                <el-button size="small" :icon="Camera" @click="photoVisible = true">
                  照片
                </el-button>
              </div>
            </div>
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

            <label class="field field--span-2">
              <span class="field-label">联系电话</span>
              <el-input
                v-model="applicant.phone"
                :disabled="!isEditable || !manualEntry"
                maxlength="30"
              />
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

            <label class="field field--span-2">
              <span class="field-label">开户行</span>
              <el-input
                v-model="applicant.bankName"
                :disabled="!isEditable"
                maxlength="100"
                placeholder="银行卡开户行（来源台账自动带出，可修改）"
              />
            </label>
            <label class="field field--span-2">
              <span class="field-label">银行卡号</span>
              <el-input
                v-model="applicant.bankAccount"
                :disabled="!isEditable"
                maxlength="50"
                class="mono"
                placeholder="银行卡号/一卡通账号（来源台账自动带出，可修改）"
              />
            </label>
          </div>

          <div class="sub-card">
            <div class="sub-card__head">
              <span class="sub-card__title">户籍所在地</span>
              <span class="sub-card__hint">（默认取当前登录用户组织机构，可修改）</span>
            </div>
            <div class="form-grid">
              <label class="field">
                <span class="field-label">省</span>
                <el-input v-model="applicant.hukouProvince" :disabled="!isEditable" />
              </label>
              <label class="field">
                <span class="field-label">市</span>
                <el-input v-model="applicant.hukouCity" :disabled="!isEditable" />
              </label>
              <label class="field">
                <span class="field-label">县区</span>
                <el-input v-model="applicant.hukouDistrict" :disabled="!isEditable" />
              </label>
              <label class="field">
                <span class="field-label">镇社区</span>
                <el-input v-model="applicant.hukouTown" :disabled="!isEditable" />
              </label>
            </div>
          </div>

          <div class="sub-card">
            <div class="sub-card__head">
              <span class="sub-card__title">家庭住址</span>
              <span class="sub-card__hint">（默认取申请人来源数据，省市区县镇社区/村与详细地址均可修改）</span>
            </div>
            <div class="form-grid">
              <label class="field">
                <span class="field-label">省</span>
                <el-input v-model="applicant.familyProvince" :disabled="!isEditable" />
              </label>
              <label class="field">
                <span class="field-label">市</span>
                <el-input v-model="applicant.familyCity" :disabled="!isEditable" />
              </label>
              <label class="field">
                <span class="field-label">县区</span>
                <el-input v-model="applicant.familyDistrict" :disabled="!isEditable" />
              </label>
              <label class="field">
                <span class="field-label">镇社区</span>
                <el-input v-model="applicant.familyTown" :disabled="!isEditable" />
              </label>
            </div>
            <div class="address-extra">
              <label class="field">
                <span class="field-label">村</span>
                <el-input v-model="applicant.village" :disabled="!isEditable" />
              </label>
              <label class="field">
                <span class="field-label">详细地址</span>
                <el-input
                  v-model="applicant.detailAddress"
                  :disabled="!isEditable"
                  placeholder="如：柳树村99号"
                />
              </label>
            </div>
          </div>

          <p v-if="autoFilling" class="auto-fill-note">
            正在回填申请人来源、政策快照与家庭健康摘要…
          </p>
        </NcCard>
      </div>

      <!-- ════════════ 第 2 页：家庭成员 ════════════ -->
      <div v-show="activeSection === 3" class="form-section">
        <NcCard
          title="家庭成员（审批表逐行展示）"
          subtitle="家庭成员将展示在大额/小额审批表中（最多 5 行）。选中申请人时已自动带回，可增删调整。"
        >
          <template #actions>
            <el-button v-if="isEditable" size="small" :icon="Plus" @click="addMember">
              添加
            </el-button>
          </template>

          <article v-for="(row, index) in members" :key="row.key" class="row-card">
            <div class="row-card__head">
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
            <div class="form-grid form-grid--3">
              <label class="field">
                <span class="field-label">姓名 *</span>
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
            </div>
            <div class="form-grid form-grid--3 form-grid--spaced">
              <label class="field field--span-3">
                <span class="field-label">身份证号</span>
                <el-input v-model="row.idCard" :disabled="!isEditable" maxlength="18" class="mono" />
              </label>
            </div>
            <div class="form-grid form-grid--3 form-grid--spaced">
              <label class="field field--span-2">
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
        </NcCard>
      </div>

      <!-- ════════════ 第 3 页：申请信息 ════════════ -->
      <div v-show="activeSection === 1" class="form-section">
        <NcCard title="选择申请成员（救助对象）">
          <div class="form-grid form-grid--2">
            <label class="field">
              <span class="field-label">救助对象 *</span>
              <el-select
                v-model="form.beneficiaryKey"
                :disabled="!isEditable || beneficiaryOptions.length === 0"
              >
                <el-option
                  v-for="option in beneficiaryOptions"
                  :key="option.key"
                  :label="option.isHead ? `${option.name}（户主）` : `${option.name}（${option.relation}）`"
                  :value="option.key"
                />
              </el-select>
            </label>
            <label class="field">
              <span class="field-label">与户主关系</span>
              <el-input :model-value="selectedBeneficiary?.relation ?? ''" readonly />
            </label>
          </div>
          <div v-if="selectedBeneficiary?.idCard" class="beneficiary-summary">
            <strong>救助对象：</strong>
            <span>{{ selectedBeneficiary.name }}</span>
            <span class="mono">{{ selectedBeneficiary.idCard }}</span>
          </div>
        </NcCard>

        <NcCard title="申请内容">
          <div class="form-grid form-grid--2">
            <div class="field">
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
                <el-input :model-value="form.applyDate ? formatDate(form.applyDate) : '—'" readonly />
                <el-button
                  v-if="canChangeDate"
                  size="small"
                  @click="openChangeDate"
                >
                  修改
                </el-button>
              </div>
            </div>
            <label class="field">
              <span class="field-label">填报单位</span>
              <el-input v-model="form.reportUnit" :disabled="!isEditable" maxlength="100" />
            </label>
          </div>

          <label class="field field--spaced">
            <span class="field-label">困难类型</span>
            <el-select
              v-model="form.difficultyType"
              :disabled="!isEditable"
              @change="handleDifficultyTypeChange"
            >
              <el-option
                v-for="option in DIFFICULTY_TYPES"
                :key="option"
                :label="option"
                :value="option"
              />
            </el-select>
          </label>

          <!-- 疾病明细 -->
          <div v-if="form.difficultyType === '疾病'" class="detail-block">
            <div class="detail-head">
              <div class="detail-head__title">
                <p class="group-title">疾病明细</p>
                <span class="section-note">（困难类型为疾病时，可多条，最多 10 条）</span>
              </div>
              <div class="detail-head__actions">
                <span class="switch-label">逐条填写医院/日期/费用（多次住院）</span>
                <el-switch
                  v-model="multiHospitalMode"
                  :disabled="!isEditable"
                  @change="handleMultiHospitalChange"
                />
                <el-button size="small" :icon="Plus" :disabled="!isEditable" @click="addDisease">
                  添加疾病
                </el-button>
              </div>
            </div>

            <div v-if="!multiHospitalMode" class="shared-block">
              <label class="field">
                <span class="field-label">所在医院</span>
                <el-input
                  v-model="diseaseShared.hospital"
                  :disabled="!isEditable"
                  placeholder="如：县人民医院"
                  @change="handleSharedDiseaseChange"
                />
              </label>
              <div class="form-grid form-grid--3">
                <label class="field">
                  <span class="field-label">治疗开始日期</span>
                  <el-date-picker
                    v-model="diseaseShared.treatStartDate"
                    type="date"
                    value-format="YYYY-MM-DD"
                    :disabled="!isEditable"
                    style="width: 100%"
                    @change="handleSharedDiseaseChange"
                  />
                </label>
                <label class="field">
                  <span class="field-label">治疗结束日期</span>
                  <el-date-picker
                    v-model="diseaseShared.treatEndDate"
                    type="date"
                    value-format="YYYY-MM-DD"
                    :disabled="!isEditable"
                    style="width: 100%"
                    @change="handleSharedDiseaseChange"
                  />
                </label>
                <label class="field">
                  <span class="field-label">医疗费用总额（元）</span>
                  <el-input-number
                    v-model="diseaseShared.medicalTotal"
                    :min="0"
                    :precision="2"
                    :controls="false"
                    :disabled="!isEditable"
                    style="width: 100%"
                    @change="handleSharedDiseaseChange"
                  />
                </label>
                <label class="field">
                  <span class="field-label">医保/其他报销（元）</span>
                  <el-input-number
                    v-model="diseaseShared.insurancePaid"
                    :min="0"
                    :precision="2"
                    :controls="false"
                    :disabled="!isEditable"
                    style="width: 100%"
                    @change="handleSharedDiseaseChange"
                  />
                </label>
                <label class="field">
                  <span class="field-label">个人自付费用（元）*</span>
                  <el-input-number
                    v-model="diseaseShared.selfPaid"
                    :min="0"
                    :precision="2"
                    :controls="false"
                    :disabled="!isEditable"
                    style="width: 100%"
                    @change="handleSharedDiseaseChange"
                  />
                </label>
              </div>
            </div>

            <article v-for="(row, index) in diseases" :key="row.key" class="row-card">
              <div class="row-card__head">
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
              <div class="form-grid form-grid--3">
                <label class="field">
                  <span class="field-label">疾病诊断结果（填编码可自动带出）</span>
                  <el-input v-model="row.diseaseName" :disabled="!isEditable" @change="handleDetailChanged" />
                </label>
                <label class="field">
                  <span class="field-label">患病成员</span>
                  <el-select
                    v-model="row.memberName"
                    :disabled="!isEditable"
                    filterable
                    clearable
                    @change="handleDetailChanged"
                  >
                    <el-option v-for="name in memberNameOptions" :key="name" :label="name" :value="name" />
                  </el-select>
                </label>
                <label class="field">
                  <span class="field-label">疾病编码（ICD-10）</span>
                  <el-input
                    v-model="row.diseaseCode"
                    :disabled="!isEditable"
                    placeholder="输入编码自动带出名称"
                    @change="handleDiseaseCodeChange(row)"
                  />
                </label>
              </div>
              <div v-if="multiHospitalMode" class="form-grid form-grid--3 form-grid--spaced">
                <label class="field">
                  <span class="field-label">所在医院 *</span>
                  <el-input
                    v-model="row.hospital"
                    :disabled="!isEditable"
                    placeholder="县人民医院"
                    @change="handleDetailChanged"
                  />
                </label>
                <label class="field">
                  <span class="field-label">开始</span>
                  <el-date-picker
                    v-model="row.treatStartDate"
                    type="date"
                    value-format="YYYY-MM-DD"
                    :disabled="!isEditable"
                    style="width: 100%"
                    @change="handleDetailChanged"
                  />
                </label>
                <label class="field">
                  <span class="field-label">结束</span>
                  <el-date-picker
                    v-model="row.treatEndDate"
                    type="date"
                    value-format="YYYY-MM-DD"
                    :disabled="!isEditable"
                    style="width: 100%"
                    @change="handleDetailChanged"
                  />
                </label>
                <label class="field">
                  <span class="field-label">费用总额</span>
                  <el-input-number
                    v-model="row.medicalTotal"
                    :min="0"
                    :precision="2"
                    :controls="false"
                    :disabled="!isEditable"
                    style="width: 100%"
                    @change="handleDetailChanged"
                  />
                </label>
                <label class="field">
                  <span class="field-label">报销</span>
                  <el-input-number
                    v-model="row.insurancePaid"
                    :min="0"
                    :precision="2"
                    :controls="false"
                    :disabled="!isEditable"
                    style="width: 100%"
                    @change="handleDetailChanged"
                  />
                </label>
                <label class="field">
                  <span class="field-label">自付 *</span>
                  <el-input-number
                    v-model="row.selfPaid"
                    :min="0"
                    :precision="2"
                    :controls="false"
                    :disabled="!isEditable"
                    style="width: 100%"
                    @change="handleDetailChanged"
                  />
                </label>
              </div>
            </article>

            <p v-if="diseases.length === 0" class="empty-note">尚未添加疾病明细。</p>
          </div>

          <!-- 意外灾害明细 -->
          <div v-if="form.difficultyType === '意外灾害'" class="detail-block">
            <div class="detail-head">
              <div class="detail-head__title">
                <p class="group-title">意外灾害明细</p>
                <span class="section-note">（困难类型为意外灾害时，可多条，最多 10 条）</span>
              </div>
              <el-button size="small" :icon="Plus" :disabled="!isEditable" @click="addAccident">
                添加意外灾害
              </el-button>
            </div>

            <article v-for="(row, index) in accidents" :key="row.key" class="row-card">
              <div class="row-card__head">
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
              <div class="form-grid form-grid--2">
                <label class="field">
                  <span class="field-label">意外/灾害类型 *</span>
                  <el-select v-model="row.accidentType" :disabled="!isEditable" @change="handleDetailChanged">
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
                  <el-select
                    v-model="row.memberName"
                    :disabled="!isEditable"
                    filterable
                    clearable
                    @change="handleDetailChanged"
                  >
                    <el-option v-for="name in memberNameOptions" :key="name" :label="name" :value="name" />
                  </el-select>
                </label>
              </div>
              <div class="form-grid form-grid--2 form-grid--spaced">
                <label class="field">
                  <span class="field-label">发生时间</span>
                  <el-date-picker
                    v-model="row.happenDate"
                    type="date"
                    value-format="YYYY-MM-DD"
                    :disabled="!isEditable"
                    style="width: 100%"
                    @change="handleDetailChanged"
                  />
                </label>
                <label class="field">
                  <span class="field-label">发生地点</span>
                  <el-input
                    v-model="row.happenPlace"
                    :disabled="!isEditable"
                    @change="handleDetailChanged"
                  />
                </label>
              </div>
              <label class="field field--spaced">
                <span class="field-label">人身伤害/伤残情况</span>
                <el-input
                  v-model="row.injurySituation"
                  type="textarea"
                  :rows="2"
                  :disabled="!isEditable"
                  @change="handleDetailChanged"
                />
              </label>
              <div class="form-grid form-grid--2 form-grid--spaced">
                <label class="field">
                  <span class="field-label">财产损失（元）</span>
                  <el-input-number
                    v-model="row.propertyLoss"
                    :min="0"
                    :precision="2"
                    :controls="false"
                    :disabled="!isEditable"
                    style="width: 100%"
                    @change="handleDetailChanged"
                  />
                </label>
                <label class="field">
                  <span class="field-label">已获保险/赔偿（元）</span>
                  <el-input-number
                    v-model="row.compensationPaid"
                    :min="0"
                    :precision="2"
                    :controls="false"
                    :disabled="!isEditable"
                    style="width: 100%"
                    @change="handleDetailChanged"
                  />
                </label>
              </div>
              <label class="field field--spaced">
                <span class="field-label">责任认定情况</span>
                <el-input v-model="row.responsibilityDesc" :disabled="!isEditable" @change="handleDetailChanged" />
              </label>
              <label class="field field--spaced">
                <span class="field-label">佐证材料说明</span>
                <el-input v-model="row.materialDesc" :disabled="!isEditable" @change="handleDetailChanged" />
              </label>
            </article>

            <p v-if="accidents.length === 0" class="empty-note">尚未添加意外灾害明细。</p>
          </div>

          <!-- 教育支出明细 -->
          <div v-if="form.difficultyType === '教育支出'" class="detail-block">
            <div class="detail-head">
              <div class="detail-head__title">
                <p class="group-title">教育支出明细</p>
                <span class="section-note">（困难类型为教育支出时，可多条，最多 10 条）</span>
              </div>
              <el-button size="small" :icon="Plus" :disabled="!isEditable" @click="addEducation">
                添加教育支出
              </el-button>
            </div>

            <article v-for="(row, index) in educations" :key="row.key" class="row-card">
              <div class="row-card__head">
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
              <div class="form-grid form-grid--2">
                <label class="field">
                  <span class="field-label">学生姓名 *</span>
                  <el-input v-model="row.studentName" :disabled="!isEditable" @change="handleDetailChanged" />
                </label>
                <label class="field">
                  <span class="field-label">就学阶段 *</span>
                  <el-select v-model="row.educationStage" :disabled="!isEditable" @change="handleDetailChanged">
                    <el-option
                      v-for="option in EDUCATION_STAGE_OPTIONS"
                      :key="option"
                      :label="option"
                      :value="option"
                    />
                  </el-select>
                </label>
              </div>
              <div class="form-grid form-grid--2 form-grid--spaced">
                <label class="field">
                  <span class="field-label">就读学校</span>
                  <el-input v-model="row.schoolName" :disabled="!isEditable" @change="handleDetailChanged" />
                </label>
                <label class="field">
                  <span class="field-label">学年制</span>
                  <el-select
                    v-model="row.schoolDuration"
                    :disabled="!isEditable"
                    clearable
                    @change="handleDetailChanged"
                  >
                    <el-option
                      v-for="option in SCHOOL_DURATION_OPTIONS"
                      :key="option.value"
                      :label="option.label"
                      :value="option.value"
                    />
                  </el-select>
                </label>
              </div>
              <div class="form-grid form-grid--2 form-grid--spaced">
                <label class="field">
                  <span class="field-label">学费/住宿费（元）</span>
                  <el-input-number
                    v-model="row.tuitionFee"
                    :min="0"
                    :precision="2"
                    :controls="false"
                    :disabled="!isEditable"
                    style="width: 100%"
                    @change="handleDetailChanged"
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
                    @change="handleDetailChanged"
                  />
                </label>
              </div>
            </article>

            <p v-if="educations.length === 0" class="empty-note">尚未添加教育支出明细。</p>
          </div>

          <label class="field field--spaced">
            <span class="field-label">申请救助原因 / 困难情况 *</span>
            <el-input
              v-model="form.difficultyReason"
              type="textarea"
              :rows="4"
              :disabled="!isEditable"
              placeholder="如：因病、因灾等突发困难，家庭基本生活暂时陷入困境..."
            />
          </label>
          <label class="field field--spaced">
            <span class="field-label">享受的政策救助情况</span>
            <el-input
              v-model="form.policyEnjoyed"
              type="textarea"
              :rows="3"
              :disabled="!isEditable"
              placeholder="如：最低生活保障/特困人员/最低生活保障边缘家庭等已享受政策..."
            />
          </label>
          <label class="field field--spaced">
            <span class="field-label">家庭成员状态</span>
            <el-input
              v-model="form.familyMemberStatus"
              type="textarea"
              :rows="3"
              :disabled="!isEditable"
              placeholder="如：家庭成员健康/患病/劳动能力等情况..."
            />
          </label>
        </NcCard>
      </div>

      <!-- ════════════ 第 4 页：金额与公示 ════════════ -->
      <div v-show="activeSection === 2" class="form-section">
        <el-alert
          v-if="form.batchHintText"
          class="batch-hint"
          type="info"
          :closable="false"
          show-icon
          :title="form.batchHintText"
        />

        <NcCard v-if="isSmall" title="救助金额（小额快救）">
          <el-alert
            v-if="legacyTierMissing"
            class="amount-alert"
            type="warning"
            :closable="false"
            show-icon
            title="该历史档案未记录政策档位"
            description="请重新选择 50%/75%/100% 档位后再保存；确定金额已按原值显示。"
          />
          <div class="form-grid form-grid--3">
            <label class="field">
              <span class="field-label">遇困人数（含户主） *</span>
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
              <span class="field-label">政策档位 *</span>
              <el-select
                v-model="form.standardTier"
                :disabled="!isEditable || smallTiersLoading || smallTiers.length === 0"
                @change="handleTierSelect"
              >
                <el-option
                  v-for="tier in smallTiers"
                  :key="tier.code"
                  :label="`${tier.label}（上限 ${formatMoney(tier.ceilingAmount)}）`"
                  :value="tier.code"
                />
              </el-select>
            </label>
            <label class="field">
              <span class="field-label">确定金额（可下调） *</span>
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
          </div>
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
          <p class="section-note section-note--spaced">{{ reliefPersonCountHint }}</p>
          <p class="section-note">{{ amountHint }}</p>
        </NcCard>

        <NcCard v-else title="常规救助金额">
          <span class="field-label">发放渠道 *</span>
          <div class="choice-grid">
            <button
              type="button"
              class="choice-card"
              :class="{ 'is-selected': effectiveChannel === 'County', 'is-disabled': !isEditable }"
              @click="selectChannel('County')"
            >
              <strong>上级部门发放</strong>
              <span>金额由上级核定，系统不填</span>
            </button>
            <button
              type="button"
              class="choice-card"
              :class="{ 'is-selected': effectiveChannel === 'Reserve', 'is-disabled': !isEditable }"
              @click="selectChannel('Reserve')"
            >
              <strong>备用金发放</strong>
              <span>乡镇备用金，手工填金额</span>
            </button>
          </div>

          <label class="field field--spaced">
            <span class="field-label">家庭遇困人数（含户主） *</span>
            <el-input-number
              v-model="form.reliefPersonCount"
              :min="1"
              :max="familySizeDisplay"
              :disabled="!isEditable"
              controls-position="right"
              style="width: 100%"
              @change="handlePersonCountChange"
            />
            <span class="field-hint">{{ reliefPersonCountHint }}</span>
          </label>

          <label v-if="effectiveChannel === 'Reserve'" class="field field--spaced">
            <span class="field-label">备用金救助金额（元） *</span>
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
          <p class="section-note section-note--spaced">{{ amountHint }}</p>
        </NcCard>

        <NcCard title="入户核实">
          <label class="field">
            <span class="field-label">入户核实情况</span>
            <el-input
              v-model="form.verifyResult"
              type="textarea"
              :rows="3"
              :disabled="!isEditable"
              placeholder="入户调查核实情况..."
            />
          </label>
          <p v-if="isSmall" class="section-note section-note--spaced">
            小额快救免公示、免经济状况核对（保留入户调查记录）。
          </p>
          <div class="form-grid form-grid--3 form-grid--spaced">
            <label v-if="!isSmall" class="field">
              <span class="field-label">公示开始日期（申请日次日）</span>
              <el-input :model-value="publicizeStartText" readonly />
            </label>
            <label v-if="!isSmall" class="field">
              <span class="field-label">公示结束日期（公示第3天）</span>
              <el-input :model-value="publicizeEndText" readonly />
            </label>
            <label class="field" :class="{ 'field--start': isSmall }">
              <span class="field-label">乡镇验收人</span>
              <el-input v-model="form.acceptancePerson" :disabled="!isEditable" maxlength="50" />
            </label>
          </div>
        </NcCard>
      </div>

      <el-alert
        v-if="saveError"
        class="save-error"
        type="error"
        :title="saveError"
        :closable="false"
        show-icon
      />

      <!-- 底部固定按钮栏：仅 PC 的 4 个动作 -->
      <div class="action-bar">
        <div class="action-bar__info">
          <el-tag size="small" effect="plain">{{ statusText }}</el-tag>
          <span>第 {{ sectionPosition + 1 }} / {{ SECTION_ORDER.length }} 页</span>
          <span v-if="modeHint">{{ modeHint }}</span>
        </div>

        <div class="action-bar__buttons">
          <el-button :disabled="!canGoPrevious" @click="goPrevious">上一页</el-button>
          <el-button :disabled="!canGoNext" @click="goNext">下一页</el-button>
          <el-button
            v-if="isEditable"
            :loading="saving"
            :disabled="confirming"
            @click="saveDraft"
          >
            保存草稿
          </el-button>
          <el-button
            v-if="canSaveAndConfirm"
            type="primary"
            :loading="saving || confirming"
            @click="saveAndConfirm"
          >
            保存并确认
          </el-button>
        </div>
      </div>
    </template>

    <el-dialog
      v-model="changeDateVisible"
      title="修改申请日期"
      width="min(460px, 92vw)"
      append-to-body
    >
      <p class="change-date-note">
        修改后将重新生成申请编号（LZ…），并重算公示、验收与办理时限；已打印的文书需重新出文书才会更新。
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
          确认修改
        </el-button>
      </template>
    </el-dialog>

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

.load-error,
.save-error {
  margin: 0;
}

.batch-hint {
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

.form-tabs {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 8px;
}

.form-tabs__item {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 7px;
  padding: 11px 12px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background: var(--nc-paper);
  color: var(--nc-ink-2);
  font-family: inherit;
  font-size: var(--nc-text-base);
  cursor: pointer;
}

.form-tabs__item.is-active {
  border-color: var(--nc-seal);
  background: var(--nc-seal-soft);
  color: var(--nc-seal);
  font-weight: 700;
}

.form-section {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.card-hint {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.choice-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.choice-card {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 4px;
  padding: 14px 16px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background: var(--nc-paper-3);
  font-family: inherit;
  text-align: left;
  cursor: pointer;
}

.choice-card strong {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-lg);
}

.choice-card span {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  line-height: 1.55;
}

.choice-card.is-selected {
  border-color: var(--nc-seal);
  background: var(--nc-seal-soft);
}

.choice-card.is-selected strong {
  color: var(--nc-seal);
}

.choice-card.is-disabled {
  cursor: not-allowed;
  opacity: 0.72;
}

.form-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 12px 14px;
}

.form-grid--3 {
  grid-template-columns: repeat(3, minmax(0, 1fr));
}

.form-grid--2 {
  grid-template-columns: repeat(2, minmax(0, 1fr));
}

.form-grid--spaced {
  margin-top: 14px;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
}

.field--spaced {
  margin-top: 14px;
}

.field--span-2 {
  grid-column: span 2;
}

.field--span-3 {
  grid-column: span 3;
}

.field--start {
  grid-column-start: 3;
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

.inline-actions {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}

.inline-actions :deep(.el-input) {
  flex: 1;
  min-width: 0;
}

.readonly-with-action {
  display: flex;
  align-items: center;
  gap: 8px;
}

.readonly-with-action :deep(.el-input) {
  flex: 1;
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

.error-note {
  margin: 6px 0 0;
  color: var(--nc-seal);
  font-size: var(--nc-text-xs);
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
  max-height: 360px;
  overflow-y: auto;
}

.candidate-card {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  width: 100%;
  padding: 12px 14px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background: var(--nc-paper-soft);
  font-family: inherit;
  text-align: left;
  cursor: pointer;
}

.candidate-card.is-selected {
  border-color: var(--nc-seal);
  background: var(--nc-seal-soft);
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

.candidate-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 12px;
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

.manual-alert {
  margin-top: 12px;
}

.sub-card {
  margin-top: 16px;
  padding: 14px 16px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background: var(--nc-paper-3);
}

.sub-card__head {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 8px;
  margin-bottom: 12px;
}

.sub-card__title {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  font-weight: 700;
}

.sub-card__hint {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.address-extra {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 2fr);
  gap: 12px 14px;
  margin-top: 12px;
}

.beneficiary-summary {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 8px;
  margin-top: 12px;
  padding: 10px 12px;
  border-radius: var(--nc-radius-sm);
  background: var(--nc-seal-soft);
  color: var(--nc-seal-dark);
  font-size: var(--nc-text-sm);
}

.group-title {
  margin: 0;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  font-weight: 700;
}

.detail-block {
  margin-top: 18px;
  padding: 12px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background: var(--nc-paper-3);
}

.detail-head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  margin-bottom: 10px;
}

.detail-head__title {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 8px;
}

.detail-head__actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.switch-label {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
}

.shared-block {
  display: flex;
  flex-direction: column;
  gap: 12px;
  margin-bottom: 12px;
  padding: 12px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background: var(--nc-paper);
}

.row-card {
  margin-top: 10px;
  padding: 12px 14px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background: var(--nc-paper);
}

.row-card__head {
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

.empty-note {
  margin: 10px 0 0;
  padding: 12px;
  border: 1px dashed var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  text-align: center;
}

.amount-alert {
  margin: 12px 0 0;
}

.auto-fill-note {
  margin: 12px 0 0;
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  text-align: right;
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
  .form-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .form-grid--3 {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .field--span-3 {
    grid-column: span 2;
  }

  .field--start {
    grid-column-start: auto;
  }
}

@media (max-width: 700px) {
  .form-tabs {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .choice-grid,
  .form-grid,
  .form-grid--2,
  .form-grid--3,
  .address-extra {
    grid-template-columns: minmax(0, 1fr);
  }

  .field--span-2,
  .field--span-3 {
    grid-column: auto;
  }

  .candidate-summary,
  .detail-head,
  .sub-card__head {
    flex-direction: column;
    align-items: stretch;
  }

  .inline-actions {
    flex-wrap: wrap;
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
}
</style>
