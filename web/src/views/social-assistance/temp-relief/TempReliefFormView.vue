<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ArrowLeft, Delete, Plus, Refresh, Search } from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import {
  checkTempReliefAnnual,
  confirmTempReliefApplication,
  createTempReliefApplication,
  deleteTempReliefApplication,
  getTempReliefApplication,
  getTempReliefCandidate,
  getTempReliefFamilyStatus,
  getTempReliefPolicySnapshot,
  getTempReliefReportUnitPhone,
  getTempReliefSmallAmountLevels,
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
  TempReliefMemberSnapshot,
  TempReliefSaveRequest,
  TempReliefSmallAmountLevel,
} from '@/types/tempRelief'

type FormMode = 'create' | 'edit' | 'view'
type ReliefType = 'Large' | 'Small'

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
  familySize: number | null
  familyCategory: string
  bankName: string
  bankAccount: string
}

interface FormState {
  id: number
  applicationNo: string
  status: string
  reliefType: ReliefType
  applyDate: string
  reportUnit: string
  reportTime: string | null
  difficultyType: string
  difficultyReason: string
  policyEnjoyed: string
  familyMemberStatus: string
  verifyResult: string
  acceptancePerson: string
  smallAmountLevel: string
  confirmAmount: number | undefined
  publicizeStartDate: string | null
  publicizeEndDate: string | null
  beneficiaryKey: string
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
const familyCategoryOptions = ref<string[]>([...FAMILY_CATEGORY_BASE])

const DIFFICULTY_TYPE_OPTIONS = ['疾病', '意外灾害', '教育支出', '其他困难']
const ACCIDENT_TYPE_OPTIONS = ['火灾', '交通事故', '溺水', '人身伤害', '其他意外']
const EDUCATION_STAGE_OPTIONS = ['学前教育', '小学', '初中', '高中', '中职', '高职', '大学']
const SCHOOL_DURATION_OPTIONS = [
  { label: '三年制', value: 3 },
  { label: '四年制', value: 4 },
  { label: '五年制', value: 5 },
]
const DEFAULT_VERIFY_TEXT = '已入户核实，申请人家庭实际情况与申报一致。'
const MAX_DETAIL_ROWS = 10
const HEAD_KEY = '__head__'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

let rowSeed = 0
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

function addDaysText(value: string, days: number): string | null {
  if (!value) return null
  const date = new Date(`${value}T00:00:00`)
  if (Number.isNaN(date.getTime())) return null
  date.setDate(date.getDate() + days)
  return toDateText(date)
}

function formatDate(value: string | null | undefined): string {
  if (!value) return '—'
  const text = value.slice(0, 10)
  return /^\d{4}-\d{2}-\d{2}$/.test(text) ? text.split('-').join('/') : '—'
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

function genderFromIdCard(idCard: string): string {
  if (idCard.length !== 18) return ''
  const digit = Number(idCard[16])
  return Number.isFinite(digit) ? (digit % 2 === 1 ? '男' : '女') : ''
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

function formatHukouType(value: string): string {
  const text = value.trim()
  if (!text) return '未识别，按两档手选'
  if (text.includes('农村') || text.toLowerCase() === 'rural') return '农村'
  if (text.includes('城镇') || text.includes('城市') || text.toLowerCase() === 'urban') return '城镇'
  return text
}

function extractDetailFromAddress(address: string, village: string): string {
  if (!address) return ''
  if (village) {
    const index = address.lastIndexOf(village)
    if (index >= 0) {
      return address.slice(index + village.length).trim()
    }
  }
  const match = address.match(/(?:村|社区)(.+)$/)
  return match?.[1]?.trim() ?? ''
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
    familySize: null,
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
    applyDate: todayText(),
    reportUnit: '',
    reportTime: null,
    difficultyType: '其他困难',
    difficultyReason: '',
    policyEnjoyed: '',
    familyMemberStatus: '',
    verifyResult: DEFAULT_VERIFY_TEXT,
    acceptancePerson: '',
    smallAmountLevel: '',
    confirmAmount: undefined,
    publicizeStartDate: null,
    publicizeEndDate: null,
    beneficiaryKey: HEAD_KEY,
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

const activeSection = ref(0)
const loading = ref(false)
const saving = ref(false)
const loadError = ref('')

const candidateKeyword = ref('')
const candidates = ref<TempReliefCandidate[]>([])
const candidateLoading = ref(false)
const candidateSearched = ref(false)
const candidateSelected = ref(false)
const candidateSummary = ref('')

const smallLevels = ref<TempReliefSmallAmountLevel[]>([])
const smallLevelsLoading = ref(false)
const reportUnitPhone = ref('')
const autoFilling = ref(false)

const annualBlocked = ref(false)
const annualMessage = ref('')
const reasonDirty = ref(false)

const sectionTabs = [
  { value: 0, label: '0 类型与申请人' },
  { value: 1, label: '1 申请信息' },
  { value: 2, label: '2 金额与公示核实' },
  { value: 3, label: '3 家庭成员' },
]

const isEditable = computed(
  () => mode.value !== 'view' && (isCreate.value || form.status === 'Draft'),
)
const canGoPrevious = computed(() => activeSection.value > 0)
const canGoNext = computed(() => activeSection.value < sectionTabs.length - 1)
const isSmall = computed(() => form.reliefType === 'Small')
const statusLabel = computed(() => {
  switch (form.status) {
    case 'Draft':
      return '草稿'
    case 'Confirmed':
      return '已确认'
    case 'Stopped':
      return '已终止'
    default:
      return form.status || '草稿'
  }
})

const familySizeDisplay = computed(() => {
  const count = members.value.filter(
    (row) => row.memberName.trim() && !isHeadMemberRow(row),
  ).length
  return 1 + count
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
      age: applicant.age ?? (applicant.idCard ? calculateAge(applicant.idCard) : null),
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

const selectedBeneficiary = computed<BeneficiaryOption | null>(() => {
  return (
    beneficiaryOptions.value.find((option) => option.key === form.beneficiaryKey)
    ?? beneficiaryOptions.value[0]
    ?? null
  )
})

const selectedLevel = computed<TempReliefSmallAmountLevel | null>(() => {
  return smallLevels.value.find((level) => level.standardName === form.smallAmountLevel) ?? null
})

const smallAmountHint = computed(() => {
  if (smallLevelsLoading.value) return '正在读取金额档位…'
  if (smallLevels.value.length === 0) return '未取到当前生效的低保月标准档位，无法确定小额救助金额。'
  if (!selectedLevel.value) return '请选择小额救助金额档位。'
  return `档位标准 ${formatMoney(selectedLevel.value.standardValue)}。确定金额可下调，须大于 0 且不超过档位标准；最终由服务端校验。`
})

const publicizeText = computed(() => {
  if (!form.publicizeStartDate || !form.publicizeEndDate) return '未设置'
  return `${formatDate(form.publicizeStartDate)} 至 ${formatDate(form.publicizeEndDate)}`
})

function resetAll(): void {
  Object.assign(form, createEmptyForm())
  Object.assign(applicant, createEmptyApplicant())
  members.value = [createMemberRow()]
  diseases.value = []
  accidents.value = []
  educations.value = []
  activeSection.value = 0
  loadError.value = ''
  candidateKeyword.value = ''
  candidates.value = []
  candidateSearched.value = false
  candidateSelected.value = false
  candidateSummary.value = ''
  smallLevels.value = []
  reportUnitPhone.value = ''
  annualBlocked.value = false
  annualMessage.value = ''
  reasonDirty.value = false
  autoFilling.value = false
}

function goPrevious(): void {
  if (canGoPrevious.value) activeSection.value -= 1
}

function goNext(): void {
  if (canGoNext.value) activeSection.value += 1
}

function backToList(): void {
  void router.push('/social-assistance/temp-relief')
}

function applyPublicizeDefaults(): void {
  if (form.reliefType === 'Small') {
    form.publicizeStartDate = null
    form.publicizeEndDate = null
    return
  }
  const base = form.applyDate || todayText()
  form.publicizeStartDate = addDaysText(base, 1)
  form.publicizeEndDate = addDaysText(base, 3)
}

function handleApplyDateChange(): void {
  if (form.reliefType === 'Large') {
    applyPublicizeDefaults()
  }
  void runAnnualCheck()
}

function handleReliefTypeChange(next: ReliefType): void {
  if (!isEditable.value) return
  form.reliefType = next
  form.smallAmountLevel = ''
  form.confirmAmount = undefined
  if (next === 'Small') {
    form.publicizeStartDate = null
    form.publicizeEndDate = null
    if (candidateSelected.value) void loadSmallLevels()
  } else {
    applyPublicizeDefaults()
  }
}

function regenerateReason(force = false): void {
  if (!force && reasonDirty.value) return
  const category = applicant.familyCategory || '困难群众'
  const head = applicant.name ? `申请人家庭成员${applicant.name}` : '申请人家庭成员'

  if (form.difficultyType === '疾病') {
    const items = diseases.value.filter(
      (row) => row.diseaseName.trim() || row.diseaseCode.trim(),
    )
    if (items.length > 0) {
      const names = items
        .map((row) => row.diseaseName.trim() || row.diseaseCode.trim())
        .join('、')
      form.difficultyReason = `${head}因患${names}，医疗费用支出较大，造成家庭基本生活暂时陷入困境，特申请临时救助。`
      return
    }
    form.difficultyReason = `${head}因患疾病，医疗费用支出较大，造成家庭基本生活暂时陷入困境，特申请临时救助。`
    return
  }

  if (form.difficultyType === '意外灾害') {
    const items = accidents.value.filter(
      (row) => row.accidentType.trim() || row.happenPlace.trim(),
    )
    if (items.length > 0) {
      const text = items
        .map((row) => {
          const parts = [row.accidentType.trim(), formatDate(row.happenDate), row.happenPlace.trim()]
            .filter((part) => part && part !== '—')
          return parts.join('，')
        })
        .join('；')
      form.difficultyReason = `${head}因${text}，家庭基本生活暂时陷入困境，特申请临时救助。`
      return
    }
    form.difficultyReason = `${head}因意外灾害，家庭基本生活暂时陷入困境，特申请临时救助。`
    return
  }

  if (form.difficultyType === '教育支出') {
    const items = educations.value.filter(
      (row) => row.studentName.trim() || row.educationStage.trim() || row.schoolName.trim(),
    )
    if (items.length > 0) {
      const text = items
        .map((row) => {
          const parts = [row.studentName.trim(), row.educationStage.trim(), row.schoolName.trim()]
            .filter(Boolean)
          return parts.join('，')
        })
        .join('；')
      form.difficultyReason = `${head}因家庭子女就学，${text}，学费等教育支出较大，家庭基本生活暂时陷入困境，特申请临时救助。`
      return
    }
    form.difficultyReason = `${head}因家庭子女就学，教育支出较大，造成家庭基本生活暂时陷入困境，特申请临时救助。`
    return
  }

  form.difficultyReason = `申请人家庭为${category}对象，因突发困难导致基本生活暂时陷入困境，特申请临时救助。`
}

function handleDifficultyTypeChange(): void {
  reasonDirty.value = false
  regenerateReason(true)
}

function handleReasonInput(): void {
  reasonDirty.value = true
}

function ensureCategoryOption(value: string): void {
  const category = normalizeFamilyCategory(value)
  if (category && !familyCategoryOptions.value.includes(category)) {
    familyCategoryOptions.value.push(category)
  }
  applicant.familyCategory = category
}

function resolveBeneficiaryKey(preferredIdCard?: string | null): void {
  const idCard = (preferredIdCard ?? '').trim()
  if (idCard && applicant.idCard && idCard === applicant.idCard) {
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

function handleMemberChanged(): void {
  if (!beneficiaryOptions.value.some((option) => option.key === form.beneficiaryKey)) {
    form.beneficiaryKey = HEAD_KEY
  }
}

function isApplicantItself(member: TempReliefMemberSnapshot): boolean {
  if (member.idCard.trim() && applicant.idCard.trim()) {
    return member.idCard.trim().toLowerCase() === applicant.idCard.trim().toLowerCase()
  }
  return member.relation === '户主' && member.memberName.trim() === applicant.name.trim()
}

function isHeadMemberRow(row: MemberRow): boolean {
  if (row.idCard.trim() && applicant.idCard.trim()) {
    return row.idCard.trim().toLowerCase() === applicant.idCard.trim().toLowerCase()
  }
  return row.relation.trim() === '户主' && row.memberName.trim() === applicant.name.trim()
}

interface MemberLike {
  id?: number
  memberName: string
  gender: string
  relation: string
  idCard: string
  workUnit: string
  annualIncome: number | null
  sortOrder?: number
}

function toMemberRow(member: MemberLike): MemberRow {
  return {
    key: nextRowKey(),
    id: member.id ?? 0,
    memberName: member.memberName ?? '',
    gender: member.gender ?? '',
    relation: member.relation ?? '',
    idCard: member.idCard ?? '',
    workUnit: member.workUnit ?? '',
    annualIncome: member.annualIncome ?? undefined,
    sortOrder: member.sortOrder ?? 0,
  }
}

function addMember(): void {
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
  regenerateReason()
}

function removeDisease(index: number): void {
  diseases.value.splice(index, 1)
  regenerateReason()
}

function addAccident(): void {
  if (accidents.value.length >= MAX_DETAIL_ROWS) {
    ElMessage.warning(`最多填写 ${MAX_DETAIL_ROWS} 条意外灾害明细`)
    return
  }
  accidents.value.push(createAccidentRow())
  regenerateReason()
}

function removeAccident(index: number): void {
  accidents.value.splice(index, 1)
  regenerateReason()
}

function addEducation(): void {
  if (educations.value.length >= MAX_DETAIL_ROWS) {
    ElMessage.warning(`最多填写 ${MAX_DETAIL_ROWS} 条教育支出明细`)
    return
  }
  educations.value.push(createEducationRow())
  regenerateReason()
}

function removeEducation(index: number): void {
  educations.value.splice(index, 1)
  regenerateReason()
}

async function loadSmallLevels(
  preferredName?: string,
  preferredAmount?: number | null,
): Promise<void> {
  smallLevelsLoading.value = true
  try {
    const response = await getTempReliefSmallAmountLevels(applicant.hukouType || undefined)
    smallLevels.value = response.levels ?? []

    const targetName = (preferredName ?? form.smallAmountLevel).trim()
    let level = smallLevels.value.find((item) => item.standardName === targetName) ?? null

    if (!level && targetName) {
      const legacy: TempReliefSmallAmountLevel = {
        standardType: '',
        standardName: targetName,
        standardValue: preferredAmount ?? 0,
        unit: '',
        hukouType: applicant.hukouType,
      }
      smallLevels.value.unshift(legacy)
      level = legacy
    }

    if (!level) {
      level = smallLevels.value[0] ?? null
    }

    if (level) {
      form.smallAmountLevel = level.standardName
      form.confirmAmount = preferredAmount ?? level.standardValue
    } else {
      form.smallAmountLevel = ''
      form.confirmAmount = undefined
    }
  } catch (error) {
    smallLevels.value = []
    form.smallAmountLevel = ''
    form.confirmAmount = undefined
    if (isEditable.value) {
      ElMessage.warning(getErrorMessage(error, '金额档位加载失败'))
    }
  } finally {
    smallLevelsLoading.value = false
  }
}

function handleLevelChange(value: string): void {
  const level = smallLevels.value.find((item) => item.standardName === value)
  form.smallAmountLevel = value
  form.confirmAmount = level ? level.standardValue : undefined
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

async function runAnnualCheck(): Promise<void> {
  const idCard = applicant.idCard.trim()
  if (!idCard || !form.applyDate) {
    annualBlocked.value = false
    annualMessage.value = ''
    return
  }

  const year = form.applyDate.slice(0, 4)
  try {
    const exists = await checkTempReliefAnnual(
      idCard,
      year,
      form.id > 0 ? form.id : undefined,
    )
    annualBlocked.value = exists
    annualMessage.value = exists
      ? `该申请人 ${year} 年已存在临时救助申请，同一身份证每年仅限一次。`
      : ''
  } catch (error) {
    annualBlocked.value = false
    annualMessage.value = ''
    ElMessage.warning(getErrorMessage(error, '年度唯一校验失败，请保存时留意服务端校验'))
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
      ElMessage.info('未检索到符合条件的申请人；申请人只能从导入台账或低保申请库选择。')
    }
  } catch (error) {
    candidates.value = []
    candidateSearched.value = true
    ElMessage.error(getErrorMessage(error, '申请人检索失败'))
  } finally {
    candidateLoading.value = false
  }
}

async function selectCandidate(candidate: TempReliefCandidate): Promise<void> {
  if (candidate.hasAppliedThisYear) {
    ElMessage.warning('该申请人本年度已申请过临时救助，每年仅限一次')
    return
  }

  candidateLoading.value = true
  autoFilling.value = true
  try {
    const detail = await getTempReliefCandidate(candidate.sourceTable, candidate.sourceFamilyId)
    if (detail.hasAppliedThisYear) {
      ElMessage.warning('该申请人本年度已申请过临时救助，每年仅限一次')
      return
    }

    applicant.sourceTable = detail.sourceTable
    applicant.sourceFamilyId = detail.sourceFamilyId
    applicant.name = detail.name
    applicant.idCard = detail.idCard
    applicant.gender = detail.gender || genderFromIdCard(detail.idCard)
    applicant.age = calculateAge(detail.idCard)
    applicant.phone = detail.phone
    applicant.familyProvince = detail.province || '黑龙江省'
    applicant.familyCity = detail.city
    applicant.familyDistrict = detail.district
    applicant.familyTown = detail.town
    applicant.town = detail.town
    applicant.village = detail.village || '村委会'
    applicant.detailAddress = detail.detailAddress
      || extractDetailFromAddress(detail.familyAddress, detail.village)
    applicant.familySize = detail.familySize
    applicant.bankName = detail.bankName
    applicant.bankAccount = detail.oneCardAccount || detail.bankAccount
    applicant.hukouType = detail.hukouType
    ensureCategoryOption(detail.familyCategory)

    if (!applicant.hukouType) {
      try {
        applicant.hukouType = await resolveTempReliefHukouType(detail.idCard)
      } catch {
        applicant.hukouType = ''
      }
    }

    members.value = detail.members
      .filter((member) => !isApplicantItself(member))
      .map((member) => toMemberRow(member))
    if (members.value.length === 0) {
      members.value = [createMemberRow()]
    }

    resolveBeneficiaryKey(detail.matchedMemberIdCard)

    candidateSelected.value = true
    candidateSummary.value = detail.matchedPersonInfo
      ? `已选择：${detail.name}（${detail.sourceName}）· ${detail.matchedPersonInfo}`
      : `已选择：${detail.name}（${detail.sourceName}）`
    candidates.value = []
    candidateKeyword.value = ''

    try {
      const policy = await getTempReliefPolicySnapshot(detail.idCard)
      form.policyEnjoyed = policy.length > 0 ? `${policy.join('；')}。` : ''
    } catch {
      // 政策快照失败不阻断选人；用户可手工补录。
    }

    try {
      form.familyMemberStatus = await getTempReliefFamilyStatus(detail.idCard)
    } catch {
      // 健康状况失败不阻断选人。
    }

    if (form.reliefType === 'Small') {
      form.smallAmountLevel = ''
      form.confirmAmount = undefined
      await loadSmallLevels()
    }

    applyPublicizeDefaults()
    reasonDirty.value = false
    regenerateReason(true)
    await runAnnualCheck()
    await loadReportUnitPhone()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '选择申请人失败'))
  } finally {
    candidateLoading.value = false
    autoFilling.value = false
  }
}

function hydrateApplicant(detail: TempReliefDetail): void {
  applicant.sourceTable = detail.sourceTable
  applicant.sourceFamilyId = detail.sourceFamilyId
  applicant.hukouType = detail.hukouType
  applicant.name = detail.applicantName
  applicant.idCard = detail.applicantIdCard
  applicant.gender = detail.gender || genderFromIdCard(detail.applicantIdCard)
  applicant.age = detail.age ?? calculateAge(detail.applicantIdCard)
  applicant.phone = detail.phone
  applicant.familyProvince = detail.familyProvince || '黑龙江省'
  applicant.familyCity = detail.familyCity
  applicant.familyDistrict = detail.familyDistrict
  applicant.familyTown = detail.familyTown || detail.town
  applicant.town = detail.town || detail.familyTown
  applicant.village = detail.village
  applicant.detailAddress = detail.familyDetail
  applicant.hukouProvince = detail.hukouProvince || '黑龙江省'
  applicant.hukouCity = detail.hukouCity
  applicant.hukouDistrict = detail.hukouDistrict
  applicant.hukouTown = detail.hukouTown
  applicant.familySize = detail.familySize
  ensureCategoryOption(detail.familyCategory)
  applicant.bankName = detail.bankName
  applicant.bankAccount = detail.bankAccount
}

function hydrateDetails(detail: TempReliefDetail): void {
  members.value = detail.members
    .filter((member) => !isApplicantItself(member))
    .map((member) => toMemberRow(member))
  if (members.value.length === 0) members.value = [createMemberRow()]

  diseases.value = detail.diseases.map((disease) => ({
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

  accidents.value = detail.accidents.map((accident) => ({
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

  educations.value = detail.educations.map((education) => ({
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

async function load(): Promise<void> {
  resetAll()

  if (isCreate.value) {
    form.applyDate = todayText()
    form.reportUnit = auth.user?.organizationName ?? ''
    form.reportTime = new Date().toISOString()
    applyPublicizeDefaults()
    await loadReportUnitPhone()
    return
  }

  if (!applicationId.value) {
    loadError.value = '缺少档案编号'
    return
  }

  loading.value = true
  loadError.value = ''
  try {
    const detail = await getTempReliefApplication(applicationId.value)

    form.id = detail.id
    form.applicationNo = detail.applicationNo
    form.status = detail.status
    form.reliefType = detail.reliefType === 'Small' ? 'Small' : 'Large'
    form.applyDate = detail.applyDate?.slice(0, 10) ?? todayText()
    form.reportUnit = detail.reportUnit
    form.reportTime = detail.reportTime
    form.difficultyType = detail.difficultyType || '其他困难'
    form.difficultyReason = detail.difficultyReason
    form.policyEnjoyed = detail.policyEnjoyed
    form.familyMemberStatus = detail.familyMemberStatus
    form.verifyResult = detail.verifyResult
    form.acceptancePerson = detail.acceptancePerson
    form.confirmAmount = detail.confirmAmount ?? undefined
    form.publicizeStartDate = detail.publicizeStartDate?.slice(0, 10) ?? null
    form.publicizeEndDate = detail.publicizeEndDate?.slice(0, 10) ?? null

    hydrateApplicant(detail)
    hydrateDetails(detail)
    resolveBeneficiaryKey(detail.beneficiaryIdCard)

    candidateSelected.value = true
    candidateSummary.value = `已选择：${detail.applicantName}（${detail.sourceType === 'Application' ? '最低生活保障申请库' : '导入台账'}）`
    reasonDirty.value = true

    if (form.reliefType === 'Small') {
      await loadSmallLevels(detail.smallAmountLevel, detail.confirmAmount)
    } else if (isEditable.value && (!form.publicizeStartDate || !form.publicizeEndDate)) {
      applyPublicizeDefaults()
    }

    await loadReportUnitPhone()
    await runAnnualCheck()
  } catch (error) {
    loadError.value = getErrorMessage(error, '临时救助档案加载失败')
  } finally {
    loading.value = false
  }
}

function buildFamilyAddress(): string {
  const village = applicant.village === '村委会' ? '' : applicant.village.trim()
  return [
    applicant.familyDistrict.trim(),
    applicant.familyTown.trim(),
    village,
    applicant.detailAddress.trim(),
  ]
    .filter(Boolean)
    .join('')
}

function buildHukouAddress(): string {
  return [
    applicant.hukouProvince.trim(),
    applicant.hukouCity.trim(),
    applicant.hukouDistrict.trim(),
    applicant.hukouTown.trim(),
  ]
    .filter(Boolean)
    .join('')
}

function validate(): string | null {
  if (annualBlocked.value) return annualMessage.value || '该申请人本年度已申请过临时救助'
  if (!candidateSelected.value || !applicant.sourceTable || applicant.sourceFamilyId <= 0) {
    return '请先检索并选择申请人（申请人只能从导入台账或低保申请库选择）'
  }
  if (!applicant.name.trim() || !applicant.idCard.trim()) return '申请人姓名或身份证号缺失'
  if (!form.applyDate) return '请选择申请日期'
  if (!form.difficultyReason.trim()) return '请填写申请救助原因/困难情况'

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

  if (!selectedBeneficiary.value) return '请选择救助对象'

  if (form.reliefType === 'Small') {
    if (!form.smallAmountLevel.trim()) return '请选择小额救助金额档位'
    if (!form.confirmAmount || form.confirmAmount <= 0) return '确定金额须为大于 0 的数字'
    if (selectedLevel.value && form.confirmAmount > selectedLevel.value.standardValue) {
      return `确定金额不能超过档位标准 ${formatMoney(selectedLevel.value.standardValue)}`
    }
  }

  if (form.publicizeStartDate && form.publicizeEndDate
    && form.publicizeEndDate < form.publicizeStartDate) {
    return '公示结束日期不能早于开始日期'
  }

  return null
}

function buildRequest(): TempReliefSaveRequest {
  const beneficiary = selectedBeneficiary.value
  return {
    reliefType: form.reliefType,
    sourceTable: applicant.sourceTable,
    sourceFamilyId: applicant.sourceFamilyId,
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
    town: applicant.town.trim(),
    village: applicant.village.trim(),
    familySize: familySizeDisplay.value,
    familyCategory: applicant.familyCategory,
    bankName: applicant.bankName.trim(),
    bankAccount: applicant.bankAccount.trim(),
    policyEnjoyed: form.policyEnjoyed,
    familyMemberStatus: form.familyMemberStatus,
    difficultyReason: form.difficultyReason.trim(),
    difficultyType: form.difficultyType,
    applyDate: form.applyDate || null,
    reportUnit: form.reportUnit.trim(),
    reportTime: form.reportTime,
    smallAmountLevel: form.reliefType === 'Small' ? form.smallAmountLevel : '',
    confirmAmount: form.reliefType === 'Small' ? form.confirmAmount ?? null : null,
    publicizeStartDate: form.publicizeStartDate,
    publicizeEndDate: form.publicizeEndDate,
    verifyResult: form.verifyResult,
    acceptancePerson: form.acceptancePerson.trim(),
    members: members.value
      .filter((row) => row.memberName.trim())
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
    diseases: diseases.value.map((row, index) => ({
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
    })),
    accidents: accidents.value.map((row, index) => ({
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
    })),
    educations: educations.value.map((row, index): TempReliefEducationInput => ({
      id: row.id,
      studentName: row.studentName.trim(),
      educationStage: row.educationStage,
      schoolName: row.schoolName.trim(),
      schoolDuration: row.schoolDuration ?? null,
      tuitionFee: row.tuitionFee ?? null,
      feeDate: row.feeDate,
      sortOrder: index,
    })),
  }
}

async function save(confirmAfterSave: boolean): Promise<void> {
  if (!isEditable.value) return
  const validation = validate()
  if (validation) {
    ElMessage.warning(validation)
    return
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

    if (confirmAfterSave) {
      await confirmTempReliefApplication(id)
      ElMessage.success('保存并确认成功')
    } else {
      ElMessage.success('保存成功')
    }

    await router.push('/social-assistance/temp-relief')
  } catch (error) {
    ElMessage.error(getErrorMessage(error, confirmAfterSave ? '保存并确认失败' : '保存失败'))
  } finally {
    saving.value = false
  }
}

async function removeApplication(): Promise<void> {
  if (!isEditable.value || form.id <= 0) return

  try {
    await ElMessageBox.confirm(
      `确定要删除申请 [${form.applicationNo}] ${applicant.name} 吗？\n此操作不可恢复。`,
      '确认删除',
      { confirmButtonText: '删除', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  try {
    await deleteTempReliefApplication(form.id)
    ElMessage.success('临时救助申请已删除')
    backToList()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '删除申请失败'))
  }
}

watch(
  () => route.fullPath,
  () => {
    if (!route.path.startsWith('/social-assistance/temp-relief')) return
    void load()
  },
)

onMounted(() => {
  void load()
})
</script>

<template>
  <div class="nc-page temp-relief-form-page">
    <NcPageHeader
      title="临时救助申请"
      description="申请人来源仅限导入台账库与最低生活保障申请库；金额由服务端按 Core 配置标准校验。"
      kicker="低收入人口救助帮扶 · 临时救助"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="backToList">返回列表</el-button>
        <el-button v-if="!isCreate" :icon="Refresh" :loading="loading" @click="load()">
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
      <el-button link type="primary" @click="load()">重新加载</el-button>
    </el-alert>

    <NcCard v-if="loading" padding="lg">
      <el-skeleton :rows="10" animated />
    </NcCard>

    <template v-else>
      <div v-if="!isCreate" class="mode-banner">
        <span>{{ isView ? '查看模式' : '编辑模式' }}</span>
        <strong>{{ form.applicationNo || '—' }}</strong>
        <span class="mode-status" :class="`is-${form.status.toLowerCase()}`">{{ statusLabel }}</span>
        <span v-if="!isEditable" class="mode-hint">当前状态只读；仅草稿可编辑。</span>
      </div>

      <div class="section-tabs" role="tablist" aria-label="临时救助申请分节">
        <button
          v-for="tab in sectionTabs"
          :key="tab.value"
          type="button"
          role="tab"
          class="section-tab"
          :class="{ 'is-active': activeSection === tab.value }"
          :aria-selected="activeSection === tab.value"
          @click="activeSection = tab.value"
        >
          {{ tab.label }}
        </button>
      </div>

      <!-- ── 0 类型与申请人 ────────────────────────────────────────── -->
      <div v-show="activeSection === 0" class="section-stack">
        <NcCard title="救助类型" subtitle="常规救助金额不填写；小额快救按户籍对应低保月标准档位确定。">
          <div class="choice-grid">
            <button
              type="button"
              class="choice-card"
              :class="{ 'is-selected': form.reliefType === 'Large' }"
              :disabled="!isEditable"
              @click="handleReliefTypeChange('Large')"
            >
              <strong>大额救助</strong>
              <span>金额不填写，按程序审批核定</span>
            </button>
            <button
              type="button"
              class="choice-card"
              :class="{ 'is-selected': form.reliefType === 'Small' }"
              :disabled="!isEditable"
              @click="handleReliefTypeChange('Small')"
            >
              <strong>小额快速救助</strong>
              <span>按户籍低保月标准定额，可下调</span>
            </button>
          </div>
        </NcCard>

        <NcCard title="选择申请人" subtitle="统一检索 5 个导入台账 + 最低生活保障申请库，不能自由录入。">
          <div v-if="isEditable && !candidateSelected" class="candidate-search">
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
            <article v-for="candidate in candidates" :key="`${candidate.sourceTable}-${candidate.sourceFamilyId}`" class="candidate-card">
              <div class="candidate-main">
                <div class="candidate-title">
                  <strong>{{ candidate.name }}</strong>
                  <span class="candidate-source">{{ candidate.sourceName }}</span>
                  <span v-if="candidate.hasAppliedThisYear" class="candidate-flag">本年已申请</span>
                </div>
                <p class="candidate-id mono">{{ candidate.idCard }}</p>
                <p v-if="candidate.matchedPersonInfo" class="candidate-match">
                  {{ candidate.matchedPersonInfo }}
                </p>
                <p class="candidate-meta">
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

          <p
            v-else-if="candidateSearched && isEditable && !candidateSelected"
            class="empty-note"
          >
            未检索到符合条件的申请人。请确认姓名或身份证号，或先完成台账 / 低保申请入库。
          </p>

          <div v-if="candidateSelected" class="candidate-summary">
            <span>{{ candidateSummary }}</span>
            <el-button v-if="isEditable" link type="primary" @click="candidateSelected = false">
              重新选择
            </el-button>
          </div>

          <el-alert
            v-if="annualBlocked"
            class="annual-alert"
            type="error"
            :title="annualMessage"
            :closable="false"
            show-icon
          />
        </NcCard>

        <NcCard v-if="candidateSelected" title="申请人信息" subtitle="身份字段由来源台账 / 低保申请库回填，不可自由录入。">
          <div class="form-grid">
            <label class="field">
              <span class="field-label">申请人姓名 *</span>
              <el-input :model-value="applicant.name" readonly />
            </label>
            <label class="field">
              <span class="field-label">身份证号 *</span>
              <el-input :model-value="applicant.idCard" readonly />
            </label>
            <label class="field">
              <span class="field-label">性别</span>
              <el-input :model-value="applicant.gender" readonly />
            </label>
            <label class="field">
              <span class="field-label">年龄</span>
              <el-input :model-value="applicant.age === null ? '' : `${applicant.age} 周岁`" readonly />
            </label>
            <label class="field">
              <span class="field-label">联系电话</span>
              <el-input :model-value="applicant.phone" readonly />
            </label>
            <label class="field">
              <span class="field-label">户籍分类</span>
              <el-input :model-value="formatHukouType(applicant.hukouType)" readonly />
            </label>
            <label class="field">
              <span class="field-label">家庭类别</span>
              <el-select v-model="applicant.familyCategory" :disabled="!isEditable" filterable>
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
            <label class="field">
              <span class="field-label">开户行</span>
              <el-input v-model="applicant.bankName" :disabled="!isEditable" maxlength="100" />
            </label>
            <label class="field field--wide">
              <span class="field-label">银行卡号 / 一卡通账号</span>
              <el-input v-model="applicant.bankAccount" :disabled="!isEditable" maxlength="50" />
            </label>
          </div>
        </NcCard>

        <NcCard
          v-if="candidateSelected"
          title="户籍与住址"
          subtitle="户籍地址默认黑龙江省；省 / 市 / 县区 / 镇社区可按实际修正。"
        >
          <p class="group-title">户籍所在地</p>
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

          <p class="group-title group-title--spaced">家庭住址</p>
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
            <label class="field">
              <span class="field-label">村 / 社区</span>
              <el-input v-model="applicant.village" :disabled="!isEditable" />
            </label>
            <label class="field field--wide">
              <span class="field-label">详细地址</span>
              <el-input v-model="applicant.detailAddress" :disabled="!isEditable" placeholder="如：柳树村99号" />
            </label>
          </div>
        </NcCard>
      </div>

      <!-- ── 1 申请信息 ────────────────────────────────────────────── -->
      <div v-show="activeSection === 1" class="section-stack">
        <NcCard title="申请内容">
          <div class="form-grid">
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
              <span class="field-label">申请日期 *</span>
              <el-date-picker
                v-model="form.applyDate"
                type="date"
                value-format="YYYY-MM-DD"
                :disabled="!isEditable"
                style="width: 100%"
                @change="handleApplyDateChange"
              />
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
              <el-input :model-value="form.reportTime ? formatDate(form.reportTime) : '—'" readonly />
            </label>
            <label class="field">
              <span class="field-label">困难类型</span>
              <el-select
                v-model="form.difficultyType"
                :disabled="!isEditable"
                @change="handleDifficultyTypeChange"
              >
                <el-option
                  v-for="option in DIFFICULTY_TYPE_OPTIONS"
                  :key="option"
                  :label="option"
                  :value="option"
                />
              </el-select>
            </label>
          </div>

          <!-- 疾病明细 -->
          <div v-if="form.difficultyType === '疾病'" class="detail-block">
            <div class="detail-head">
              <div>
                <p class="group-title">疾病明细</p>
                <p class="section-note">困难类型为疾病时填写，可多条，最多 {{ MAX_DETAIL_ROWS }} 条。</p>
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
                  <el-input v-model="row.diseaseName" :disabled="!isEditable" @input="regenerateReason()" />
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
                    :controls="false"
                    :disabled="!isEditable"
                    style="width: 100%"
                  />
                </label>
              </div>
            </article>

            <p v-if="diseases.length === 0" class="empty-note">尚未添加疾病明细。</p>
          </div>

          <!-- 意外灾害明细 -->
          <div v-if="form.difficultyType === '意外灾害'" class="detail-block">
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

          <!-- 教育支出明细 -->
          <div v-if="form.difficultyType === '教育支出'" class="detail-block">
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

          <div class="form-stack">
            <label class="field">
              <span class="field-label">申请救助原因 / 困难情况 *</span>
              <el-input
                v-model="form.difficultyReason"
                type="textarea"
                :rows="4"
                :disabled="!isEditable"
                placeholder="如：因病、因灾等突发困难，家庭基本生活暂时陷入困境..."
                @input="handleReasonInput"
              />
            </label>
            <label class="field">
              <span class="field-label">享受的政策救助情况</span>
              <el-input
                v-model="form.policyEnjoyed"
                type="textarea"
                :rows="3"
                :disabled="!isEditable"
                placeholder="如：最低生活保障 / 特困人员 / 最低生活保障边缘家庭等已享受政策..."
              />
            </label>
            <label class="field">
              <span class="field-label">家庭成员状态</span>
              <el-input
                v-model="form.familyMemberStatus"
                type="textarea"
                :rows="3"
                :disabled="!isEditable"
                placeholder="如：家庭成员健康 / 患病 / 劳动能力等情况..."
              />
            </label>
          </div>
        </NcCard>
      </div>

      <!-- ── 2 金额（小额）与公示核实 ──────────────────────────────── -->
      <div v-show="activeSection === 2" class="section-stack">
        <NcCard v-if="isSmall" title="救助金额（小额快速救助）" subtitle="金额上限来自服务端 Core 配置的低保月标准；前端不重算。">
          <div class="form-grid">
            <label class="field">
              <span class="field-label">金额档位 *</span>
              <el-select
                v-model="form.smallAmountLevel"
                :disabled="!isEditable"
                :loading="smallLevelsLoading"
                placeholder="请选择档位"
                @change="handleLevelChange"
              >
                <el-option
                  v-for="level in smallLevels"
                  :key="`${level.standardType}-${level.standardName}`"
                  :label="`${level.standardName}（${formatMoney(level.standardValue)}）`"
                  :value="level.standardName"
                />
              </el-select>
            </label>
            <label class="field">
              <span class="field-label">确定金额（可下调）*</span>
              <el-input-number
                v-model="form.confirmAmount"
                :min="0"
                :max="selectedLevel?.standardValue"
                :precision="2"
                :step="10"
                :disabled="!isEditable"
                style="width: 100%"
              />
            </label>
            <label class="field">
              <span class="field-label">档位标准</span>
              <el-input :model-value="formatMoney(selectedLevel?.standardValue)" readonly />
            </label>
          </div>
          <p class="section-note section-note--spaced">{{ smallAmountHint }}</p>
        </NcCard>

        <NcCard v-else title="救助金额（大额救助）">
          <p class="section-note">大额救助金额不填写，由上级按程序审批核定。公示与入户核实信息在下方填写。</p>
        </NcCard>

        <NcCard title="入户核实与公示">
          <div class="form-grid">
            <label class="field field--wide">
              <span class="field-label">入户核实情况</span>
              <el-input
                v-model="form.verifyResult"
                type="textarea"
                :rows="3"
                :disabled="!isEditable"
                placeholder="入户调查核实情况..."
              />
            </label>
            <label class="field">
              <span class="field-label">公示开始日期</span>
              <el-date-picker
                v-model="form.publicizeStartDate"
                type="date"
                value-format="YYYY-MM-DD"
                disabled
                style="width: 100%"
              />
            </label>
            <label class="field">
              <span class="field-label">公示结束日期</span>
              <el-date-picker
                v-model="form.publicizeEndDate"
                type="date"
                value-format="YYYY-MM-DD"
                disabled
                style="width: 100%"
              />
            </label>
            <label class="field">
              <span class="field-label">公示期间</span>
              <el-input :model-value="isSmall ? '小额快救免公示' : publicizeText" readonly />
            </label>
            <label class="field">
              <span class="field-label">公示异议反馈电话</span>
              <el-input :model-value="reportUnitPhone || '未取到填报单位电话'" readonly />
            </label>
            <label class="field">
              <span class="field-label">乡镇验收人</span>
              <el-input v-model="form.acceptancePerson" :disabled="!isEditable" maxlength="50" />
            </label>
          </div>
          <p v-if="!isSmall" class="section-note section-note--spaced">
            公示日期默认按申请日次日开始、第 3 天结束；保存后以档案值为准。
          </p>
          <p v-else class="section-note section-note--spaced">
            小额快救不生成公示起止日期；入户调查记录仍保留。
          </p>
        </NcCard>
      </div>

      <!-- ── 3 家庭成员 ────────────────────────────────────────────── -->
      <div v-show="activeSection === 3" class="section-stack">
        <NcCard
          title="家庭成员"
          subtitle="选中申请人时已自动带回，可增删调整。家庭人口 = 户主 1 人 + 有效家庭成员行数。"
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
                <span class="field-label">姓名 *</span>
                <el-input
                  v-model="row.memberName"
                  :disabled="!isEditable"
                  @input="handleMemberChanged"
                />
              </label>
              <label class="field">
                <span class="field-label">与户主关系</span>
                <el-input v-model="row.relation" :disabled="!isEditable" />
              </label>
              <label class="field">
                <span class="field-label">性别</span>
                <el-input v-model="row.gender" :disabled="!isEditable" />
              </label>
              <label class="field">
                <span class="field-label">身份证号</span>
                <el-input v-model="row.idCard" :disabled="!isEditable" maxlength="18" />
              </label>
              <label class="field">
                <span class="field-label">工作单位</span>
                <el-input v-model="row.workUnit" :disabled="!isEditable" />
              </label>
              <label class="field">
                <span class="field-label">年收入（元）</span>
                <el-input-number
                  v-model="row.annualIncome"
                  :min="0"
                  :controls="false"
                  :disabled="!isEditable"
                  style="width: 100%"
                />
              </label>
            </div>
          </article>

          <p class="section-note section-note--spaced">
            当前家庭人口：{{ familySizeDisplay }} 人（户主 1 人 + 有效成员 {{ familySizeDisplay - 1 }} 人）。
            审批表最多展示 5 行成员。
          </p>
        </NcCard>
      </div>

      <div class="bottom-bar">
        <div class="bottom-nav">
          <el-button :disabled="!canGoPrevious" @click="goPrevious">上一页</el-button>
          <el-button :disabled="!canGoNext" @click="goNext">下一页</el-button>
        </div>
        <div class="bottom-actions">
          <el-button v-if="isView" @click="backToList">返回列表</el-button>
          <el-button
            v-if="isEditable && form.id > 0"
            type="danger"
            plain
            :loading="saving"
            @click="removeApplication"
          >
            删除
          </el-button>
          <el-button v-if="isEditable" :loading="saving" @click="save(false)">保存草稿</el-button>
          <el-button
            v-if="isEditable"
            type="primary"
            :loading="saving"
            @click="save(true)"
          >
            保存并确认
          </el-button>
        </div>
      </div>

      <p v-if="autoFilling" class="auto-fill-note">正在按申请人来源回填家庭成员、政策快照与健康状况…</p>
    </template>
  </div>
</template>

<style scoped>
.temp-relief-form-page {
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

.mode-banner strong {
  color: var(--nc-ink);
  font-family: var(--nc-font-mono);
}

.mode-status {
  padding: 1px 7px;
  border: 1px solid transparent;
  font-size: var(--nc-text-xs);
  font-weight: 600;
}

.mode-status.is-draft {
  border-color: rgba(171, 50, 39, 0.3);
  background: var(--nc-paper);
  color: var(--nc-seal-dark);
}

.mode-status.is-confirmed {
  border-color: rgba(46, 71, 56, 0.35);
  background: var(--nc-pine-soft);
  color: var(--nc-pine-dark);
}

.mode-status.is-stopped {
  border-color: var(--nc-line-strong);
  background: var(--nc-paper-3);
  color: var(--nc-muted);
}

.mode-hint {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.section-tabs {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 10px;
}

.section-tab {
  padding: 11px 12px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background: var(--nc-paper);
  color: var(--nc-ink-2);
  font-family: inherit;
  font-size: var(--nc-text-base);
  cursor: pointer;
}

.section-tab.is-active {
  border-color: var(--nc-seal);
  background: var(--nc-seal-soft);
  color: var(--nc-seal);
  font-weight: 700;
}

.section-stack {
  display: flex;
  flex-direction: column;
  gap: 14px;
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

.field-label {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
}

.form-stack {
  display: flex;
  flex-direction: column;
  gap: 12px;
  margin-top: 16px;
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
  line-height: 1.6;
}

.section-note--spaced {
  margin-top: 10px;
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
  gap: 5px;
  padding: 16px 18px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background: var(--nc-paper-3);
  font-family: inherit;
  text-align: left;
  cursor: pointer;
}

.choice-card:disabled {
  cursor: not-allowed;
  opacity: 0.72;
}

.choice-card strong {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-lg);
  letter-spacing: 0.03em;
}

.choice-card span {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.choice-card.is-selected {
  border-color: var(--nc-seal);
  background: var(--nc-seal-soft);
}

.choice-card.is-selected strong {
  color: var(--nc-seal);
}

.candidate-search {
  display: flex;
  gap: 8px;
}

.candidate-search :deep(.el-input) {
  flex: 1;
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

.candidate-main {
  min-width: 0;
}

.candidate-title {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
}

.candidate-title strong {
  color: var(--nc-ink);
  font-size: var(--nc-text-base);
  font-weight: 700;
}

.candidate-source {
  color: var(--nc-pine-dark);
  font-size: var(--nc-text-xs);
  font-weight: 600;
}

.candidate-flag {
  padding: 1px 6px;
  border: 1px solid rgba(171, 50, 39, 0.35);
  background: var(--nc-seal-soft);
  color: var(--nc-seal-dark);
  font-size: 11px;
  font-weight: 600;
}

.candidate-id {
  margin: 3px 0 0;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
}

.mono {
  font-family: var(--nc-font-mono);
}

.candidate-match {
  margin: 4px 0 0;
  color: var(--nc-seal-dark);
  font-size: var(--nc-text-xs);
}

.candidate-meta {
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

.annual-alert {
  margin-top: 12px;
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

.detail-block {
  margin-top: 18px;
  padding-top: 16px;
  border-top: 1px dashed var(--nc-line-faint);
}

.detail-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
}

.detail-card,
.member-card {
  margin-top: 10px;
  padding: 12px 14px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background: var(--nc-paper-soft);
}

.detail-card__head,
.member-card__head {
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

.member-card {
  margin-top: 10px;
}

.bottom-bar {
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

.bottom-nav,
.bottom-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
}

.auto-fill-note {
  margin: 0;
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  text-align: right;
}

@media (max-width: 1100px) {
  .form-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .field--wide {
    grid-column: span 2;
  }
}

@media (max-width: 700px) {
  .section-tabs,
  .form-grid,
  .choice-grid {
    grid-template-columns: minmax(0, 1fr);
  }

  .field--wide {
    grid-column: auto;
  }

  .candidate-card,
  .candidate-summary,
  .detail-head {
    flex-direction: column;
    align-items: stretch;
  }

  .bottom-bar {
    flex-direction: column;
    align-items: stretch;
  }

  .bottom-nav,
  .bottom-actions {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}
</style>
