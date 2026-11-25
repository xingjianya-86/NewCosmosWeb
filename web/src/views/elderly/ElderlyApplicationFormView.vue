<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { ArrowLeft, Camera, Refresh } from '@element-plus/icons-vue'
import AddressCascader from '@/components/AddressCascader.vue'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import {
  confirmElderlyApplication,
  createElderlyApplication,
  evaluateElderly,
  evaluateElderlySpecial,
  getElderlyApplicationForm,
  getElderlyImportedByIdCard,
  getElderlyOperatorAddress,
  ocrElderlyIdCard,
  updateElderlyApplication,
} from '@/api/elderly'
import { getErrorMessage } from '@/api/http'
import type {
  ElderlyApplicationFormRequest,
  ElderlyEvaluateResult,
  ElderlyOperatorAddress,
  ElderlyPaybackSegment,
} from '@/types/elderly'

type FormMode = 'create' | 'edit' | 'view'

interface FormState {
  name: string
  idCard: string
  gender: string
  birthDate: string | null
  phone: string
  hukouProvince: string
  hukouCity: string
  hukouCounty: string
  hukouTown: string
  hukouVillage: string
  hukouDetailAddress: string
  familyProvince: string
  familyCity: string
  familyCounty: string
  familyTown: string
  familyVillage: string
  detailAddress: string
  bankName: string
  bankAccount: string
  agentName: string
  agentRelation: string
  agentReceiveName: string
  agentReceiveRelation: string
  agentReceiveBankName: string
  agentReceiveBankAccount: string
  agentReceiveReason: string
  category: string
  isCategoryManual: boolean
  identityFlag: string
  identitySource: string
  issueStartMonth: string
  issueAmount: number
  paybackStartMonth: string
  paybackEndMonth: string
  paybackMonths: number
  paybackAmount: number
  paybackReason: string
  isSpecialCase: boolean
  specialReason: string
  applyDate: string | null
  status: string
}

const route = useRoute()
const router = useRouter()

const categoryOptions = [
  { value: 'CAT1', label: '80-89周岁最低生活保障/边缘/特困人员' },
  { value: 'CAT2', label: '80-89周岁其他老年人' },
  { value: 'CAT3', label: '90-99周岁老年人' },
  { value: 'CAT4', label: '100周岁以上老年人' },
]

const paybackReasonOptions = [
  { value: 'MISSED_REGISTRATION', label: '未及时登记' },
  { value: 'NOT_LOCATED', label: '查找不到本人' },
  { value: 'LOST_CONTACT', label: '短暂失联' },
  { value: 'OTHER', label: '其他' },
]

const sectionTabs = [
  { label: '基本信息', value: 0 },
  { label: '代办代领', value: 1 },
  { label: '判定认证', value: 2 },
]

const today = formatDate(new Date())
const mode = computed<FormMode>(() => {
  if (route.name === 'elderly-application-new') return 'create'
  if (route.name === 'elderly-application-edit') return 'edit'
  return 'view'
})
const isView = computed(() => mode.value === 'view')
const isCreate = computed(() => mode.value === 'create')
const applicationId = computed(() => {
  const raw = route.params.id
  const value = Array.isArray(raw) ? raw[0] : raw
  return value ? String(value) : ''
})

const form = reactive<FormState>(createEmptyForm())
const applicationNo = ref('')
const activeSection = ref(0)
const loading = ref(false)
const saving = ref(false)
const evaluating = ref(false)
const loadError = ref('')
const evaluation = ref<ElderlyEvaluateResult | null>(null)
const segments = ref<ElderlyPaybackSegment[]>([])
const evaluated = ref(false)
const ocrInputRef = ref<HTMLInputElement | null>(null)
const ocrRunning = ref(false)
const addressScope = ref<ElderlyOperatorAddress | null>(null)
const addressScopeLoading = ref(false)
const addressScopeError = ref('')

const addressUnavailable = computed(
  () => isCreate.value && addressScope.value !== null && addressScope.value.lockLevel === 0,
)

const age = computed(() => calculateAge(form.birthDate))
const categoryText = computed(
  () =>
    evaluation.value?.categoryName
    || categoryOptions.find((option) => option.value === form.category)?.label
    || '',
)
const monthlyAmount = computed(() => evaluation.value?.monthlyAmount ?? 0)
const isSavable = computed(() => !isView.value && form.status !== 'Stopped')
const showSaveAndConfirm = computed(() => isSavable.value && activeSection.value === 2)

function createEmptyForm(): FormState {
  return {
    name: '',
    idCard: '',
    gender: '',
    birthDate: null,
    phone: '',
    hukouProvince: '黑龙江省',
    hukouCity: '',
    hukouCounty: '',
    hukouTown: '',
    hukouVillage: '',
    hukouDetailAddress: '',
    familyProvince: '黑龙江省',
    familyCity: '',
    familyCounty: '',
    familyTown: '',
    familyVillage: '',
    detailAddress: '',
    bankName: '',
    bankAccount: '',
    agentName: '',
    agentRelation: '',
    agentReceiveName: '',
    agentReceiveRelation: '',
    agentReceiveBankName: '',
    agentReceiveBankAccount: '',
    agentReceiveReason: '',
    category: '',
    isCategoryManual: false,
    identityFlag: '',
    identitySource: '',
    issueStartMonth: '',
    issueAmount: 0,
    paybackStartMonth: '',
    paybackEndMonth: '',
    paybackMonths: 0,
    paybackAmount: 0,
    paybackReason: '',
    isSpecialCase: false,
    specialReason: '',
    applyDate: today,
    status: 'Draft',
  }
}

function formatDate(date: Date): string {
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

function calculateAge(birthDate: string | null): number | null {
  if (!birthDate) return null
  const birth = new Date(`${birthDate}T00:00:00`)
  if (Number.isNaN(birth.getTime())) return null
  const now = new Date()
  let value = now.getFullYear() - birth.getFullYear()
  const beforeBirthday =
    now.getMonth() < birth.getMonth()
    || (now.getMonth() === birth.getMonth() && now.getDate() < birth.getDate())
  if (beforeBirthday) value -= 1
  return value >= 0 ? value : null
}

function formatMoney(value: number): string {
  return Number.isFinite(value) ? value.toFixed(2) : '0.00'
}

function genderFromIdCard(idCard: string): string {
  if (idCard.length !== 18) return ''
  const digit = Number(idCard[16])
  return Number.isFinite(digit) ? (digit % 2 === 1 ? '男' : '女') : ''
}

function birthDateFromIdCard(idCard: string): string | null {
  if (idCard.length !== 18) return null
  const year = idCard.slice(6, 10)
  const month = idCard.slice(10, 12)
  const day = idCard.slice(12, 14)
  if (!/^\d{4}$/.test(year) || !/^\d{2}$/.test(month) || !/^\d{2}$/.test(day)) return null
  return `${year}-${month}-${day}`
}

function validateIdCard(value: string): boolean {
  const id = value.trim()
  if (!/^\d{17}[\dXx]$/.test(id)) return false
  const weights = [7, 9, 10, 5, 8, 4, 2, 1, 6, 3, 7, 9, 10, 5, 8, 4, 2]
  const checks = ['1', '0', 'X', '9', '8', '7', '6', '5', '4', '3', '2']
  let sum = 0
  for (let index = 0; index < 17; index += 1) sum += Number(id[index]) * weights[index]
  return checks[sum % 11] === id[17].toUpperCase()
}

function normalizeMonth(value: string): string {
  return (value ?? '').trim()
}

async function runEvaluation(showMessage = true): Promise<void> {
  const idCard = form.idCard.trim()
  if (!validateIdCard(idCard)) {
    if (showMessage) ElMessage.warning('身份证号码无效')
    return
  }

  evaluating.value = true
  try {
    const result = await evaluateElderly(idCard, form.applyDate || today)
    evaluation.value = result
    evaluated.value = true
    form.gender = result.gender || form.gender
    form.birthDate = result.birthDate?.slice(0, 10) ?? form.birthDate
    form.category = result.category
    form.identityFlag = result.identityFlag
    form.identitySource = result.identitySource
    form.issueStartMonth = result.issueStartMonth
    form.issueAmount = result.issueAmount
    form.paybackStartMonth = result.payback.startMonth
    form.paybackEndMonth = result.payback.endMonth
    form.paybackMonths = result.payback.totalMonths
    form.paybackAmount = result.payback.totalAmount
    segments.value = result.payback.segments ?? []
    form.isCategoryManual = false
  } catch (error) {
    evaluated.value = false
    if (showMessage) ElMessage.error(getErrorMessage(error, '判定失败'))
  } finally {
    evaluating.value = false
  }
}

async function loadOperatorAddress(): Promise<void> {
  addressScopeLoading.value = true
  addressScopeError.value = ''
  try {
    const scope = await getElderlyOperatorAddress()
    addressScope.value = scope
    if (isCreate.value) {
      applyOperatorScope(scope)
    }
  } catch (error) {
    addressScope.value = null
    addressScopeError.value = getErrorMessage(error, '读取经办机构区划失败')
  } finally {
    addressScopeLoading.value = false
  }
}

function applyOperatorScope(scope: ElderlyOperatorAddress): void {
  const province = scope.provinceName || '黑龙江省'
  const village = scope.villageName || scope.villages[0]?.name || ''

  form.hukouProvince = province
  form.hukouCity = scope.cityName
  form.hukouCounty = scope.countyName
  form.hukouTown = scope.townName
  form.hukouVillage = village
  form.hukouDetailAddress = ''

  form.familyProvince = province
  form.familyCity = scope.cityName
  form.familyCounty = scope.countyName
  form.familyTown = scope.townName
  form.familyVillage = village
}

async function handleIdCardBlur(): Promise<void> {
  if (isView.value) return
  const idCard = form.idCard.trim()
  if (!validateIdCard(idCard)) return

  form.gender = genderFromIdCard(idCard) || form.gender
  form.birthDate = birthDateFromIdCard(idCard) ?? form.birthDate

  if (!form.name.trim()) {
    try {
      const imported = await getElderlyImportedByIdCard(idCard)
      if (imported) {
        form.name = imported.name || form.name
        form.phone = form.phone || imported.phone || ''
        form.gender = form.gender || imported.gender || ''
        form.birthDate = form.birthDate || imported.birthDate?.slice(0, 10) || null
      }
    } catch {
      // 导入名册查询失败不阻断手工录入。
    }
  }

  await runEvaluation(false)
}

async function recalcSpecialPayback(): Promise<void> {
  if (!form.isSpecialCase) return
  const start = normalizeMonth(form.paybackStartMonth)
  const end = normalizeMonth(form.paybackEndMonth)
  if (!start && !end) {
    segments.value = []
    form.paybackMonths = 0
    form.paybackAmount = 0
    return
  }
  if (!start || !end) {
    ElMessage.warning('特殊情况：补发起算月与止算月需同时填写，或同时留空表示无补发')
    return
  }
  if (!validateIdCard(form.idCard.trim())) {
    ElMessage.warning('身份证号码无效')
    return
  }
  try {
    const result = await evaluateElderlySpecial(form.idCard.trim(), start, end)
    segments.value = result.segments ?? []
    form.paybackMonths = result.totalMonths
    form.paybackAmount = result.totalAmount
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '特殊补发计算失败'))
  }
}

function handleCategoryChange(): void {
  form.isCategoryManual = true
}

function triggerOcr(): void {
  if (isView.value) return
  ocrInputRef.value?.click()
}

async function handleOcrFile(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file) return
  ocrRunning.value = true
  try {
    const result = await ocrElderlyIdCard(file)
    if (result.name) form.name = result.name
    if (result.idCard) form.idCard = result.idCard
    if (result.gender) form.gender = result.gender
    if (result.birthDate) form.birthDate = result.birthDate.slice(0, 10)
    ElMessage.success('身份证识别完成')
    await handleIdCardBlur()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '身份证识别失败'))
  } finally {
    ocrRunning.value = false
  }
}

function validateForm(): string | null {
  if (!form.name.trim()) return '请填写申请人姓名'
  if (!form.idCard.trim()) return '请填写身份证号码'
  if (!validateIdCard(form.idCard)) return '身份证号码无效'
  if (!form.phone.trim()) return '请填写联系电话'
  if (!form.hukouCity) return '请选择户籍城市'
  if (!form.hukouCounty) return '请选择户籍区县'
  if (!form.hukouTown) return '请选择户籍乡镇'
  if (!form.hukouVillage) return '请选择户籍村/社区'
  if (!form.bankName.trim()) return '请填写社保卡开户行'
  if (!form.bankAccount.trim()) return '请填写社保卡账号'
  if (!evaluated.value) return '请先通过身份证评估享受类别与补发金额'
  if (form.isSpecialCase && !form.specialReason.trim()) return '请填写特殊情况原因说明'
  if (form.isSpecialCase) {
    const start = normalizeMonth(form.paybackStartMonth)
    const end = normalizeMonth(form.paybackEndMonth)
    if (Boolean(start) !== Boolean(end)) {
      return '特殊情况：补发起算月与止算月需同时填写，或同时留空表示无补发'
    }
    if (start && end) {
      if (!/^\d{4}-\d{2}$/.test(start) || !/^\d{4}-\d{2}$/.test(end)) {
        return '补发起止月格式应为 yyyy-MM（如 2026-03）'
      }
      if (start > end) return '补发起算月不能晚于止算月'
    }
  }
  return null
}

function buildRequest(): ElderlyApplicationFormRequest {
  return {
    name: form.name.trim(),
    idCard: form.idCard.trim(),
    gender: form.gender,
    birthDate: form.birthDate,
    phone: form.phone.trim(),
    hukouProvince: form.hukouProvince,
    hukouCity: form.hukouCity,
    hukouCounty: form.hukouCounty,
    hukouTown: form.hukouTown,
    hukouVillage: form.hukouVillage,
    hukouDetailAddress: '',
    familyProvince: form.familyProvince,
    familyCity: form.familyCity,
    familyCounty: form.familyCounty,
    familyTown: form.familyTown,
    familyVillage: form.familyVillage,
    detailAddress: form.detailAddress.trim(),
    bankName: form.bankName.trim(),
    bankAccount: form.bankAccount.trim(),
    agentName: form.agentName.trim(),
    agentRelation: form.agentRelation,
    agentReceiveName: form.agentReceiveName.trim(),
    agentReceiveRelation: form.agentReceiveRelation,
    agentReceiveBankName: form.agentReceiveBankName.trim(),
    agentReceiveBankAccount: form.agentReceiveBankAccount.trim(),
    agentReceiveReason: form.agentReceiveReason.trim(),
    category: form.category,
    isCategoryManual: form.isCategoryManual,
    paybackStartMonth: form.paybackStartMonth,
    paybackEndMonth: form.paybackEndMonth,
    paybackReason: form.paybackReason,
    isSpecialCase: form.isSpecialCase,
    specialReason: form.specialReason.trim(),
    applyDate: form.applyDate,
  }
}

async function save(confirm: boolean): Promise<void> {
  if (!isSavable.value) return
  const validation = validateForm()
  if (validation) {
    ElMessage.warning(validation)
    return
  }

  saving.value = true
  try {
    let id = applicationId.value
    if (isCreate.value) {
      const created = await createElderlyApplication(buildRequest())
      id = String(created.id)
      applicationNo.value = created.applicationNo
    } else {
      await updateElderlyApplication(id, buildRequest())
    }

    if (confirm) {
      await confirmElderlyApplication(id)
      ElMessage.success('保存并确认成功')
    } else {
      ElMessage.success('保存成功')
    }

    await router.push({ name: 'elderly-applications' })
  } catch (error) {
    ElMessage.error(getErrorMessage(error, confirm ? '保存并确认失败' : '保存失败'))
  } finally {
    saving.value = false
  }
}

function goPrevious(): void {
  if (activeSection.value > 0) activeSection.value -= 1
}

function goNext(): void {
  if (activeSection.value < 2) activeSection.value += 1
}

function backToList(): void {
  void router.push({ name: 'elderly-applications' })
}

async function load(): Promise<void> {
  if (isCreate.value) {
    form.applyDate = today
    await loadOperatorAddress()
    return
  }
  if (!applicationId.value) {
    loadError.value = '缺少档案编号'
    return
  }

  await loadOperatorAddress()
  loading.value = true
  loadError.value = ''
  try {
    const response = await getElderlyApplicationForm(applicationId.value)
    const app = response.application
    applicationNo.value = app.applicationNo
    Object.assign(form, {
      name: app.name,
      idCard: app.idCard,
      gender: app.gender,
      birthDate: app.birthDate?.slice(0, 10) ?? null,
      phone: app.phone,
      hukouProvince: response.hukouProvince || '黑龙江省',
      hukouCity: response.hukouCity,
      hukouCounty: response.hukouCounty,
      hukouTown: response.hukouTown,
      hukouVillage: response.hukouVillage,
      hukouDetailAddress: app.hukouDetailAddress,
      familyProvince: response.familyProvince || '黑龙江省',
      familyCity: response.familyCity,
      familyCounty: response.familyCounty,
      familyTown: response.familyTown,
      familyVillage: response.familyVillage,
      detailAddress: app.detailAddress,
      bankName: app.bankName,
      bankAccount: app.bankAccount,
      agentName: app.agentName,
      agentRelation: app.agentRelation,
      agentReceiveName: app.agentReceiveName,
      agentReceiveRelation: app.agentReceiveRelation,
      agentReceiveBankName: app.agentReceiveBankName,
      agentReceiveBankAccount: app.agentReceiveBankAccount,
      agentReceiveReason: app.agentReceiveReason,
      category: app.category,
      isCategoryManual: app.isCategoryManual,
      identityFlag: app.identityFlag,
      identitySource: app.identitySource,
      issueStartMonth: app.issueStartMonth,
      issueAmount: app.issueAmount,
      paybackStartMonth: app.paybackStartMonth,
      paybackEndMonth: app.paybackEndMonth,
      paybackMonths: app.paybackMonths,
      paybackAmount: app.paybackAmount,
      paybackReason: app.paybackReason,
      isSpecialCase: app.isSpecialCase,
      specialReason: app.specialReason,
      applyDate: app.applyDate?.slice(0, 10) ?? today,
      status: app.status,
    })
    segments.value = response.segments ?? []
    evaluated.value = Boolean(app.category)
  } catch (error) {
    loadError.value = getErrorMessage(error, '高龄津贴档案加载失败')
  } finally {
    loading.value = false
  }
}

watch(
  () => route.name,
  () => {
    if (route.name === 'elderly-application-new') {
      Object.assign(form, createEmptyForm())
      applicationNo.value = ''
      evaluation.value = null
      segments.value = []
      evaluated.value = false
      activeSection.value = 0
      void load()
    } else {
      void load()
    }
  },
)

onMounted(() => {
  void load()
})
</script>

<template>
  <div class="nc-page elderly-form-page">
    <NcPageHeader
      title="高龄津贴申请登记"
      description="输入身份证自动评估享受类别与补发金额"
      kicker="普惠高龄 · 申请登记"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="backToList">返回列表</el-button>
        <el-button
          v-if="!isCreate"
          :icon="Refresh"
          :loading="loading"
          @click="load()"
        >
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
      <div v-if="!isCreate && applicationNo" class="mode-banner">
        <span>{{ isView ? '查看模式' : '编辑模式' }}</span>
        <strong>{{ applicationNo }}</strong>
        <span v-if="form.status === 'Confirmed'">已确认</span>
        <span v-else-if="form.status === 'Stopped'">已停发</span>
        <span v-else>草稿</span>
      </div>

      <div class="section-tabs" role="tablist" aria-label="申请登记分节">
        <button
          v-for="tab in sectionTabs"
          :key="tab.value"
          type="button"
          class="section-tab"
          :class="{ 'is-active': activeSection === tab.value }"
          @click="activeSection = tab.value"
        >
          {{ tab.label }}
        </button>
      </div>

      <!-- ── 基本信息（申请人 + 户籍住址 + 社保卡） ───────────────── -->
      <div v-show="activeSection === 0" class="section-stack">
        <NcCard title="申请人信息">
          <div class="form-grid">
            <label class="field">
              <span class="field-label">申请人姓名 *</span>
              <el-input v-model="form.name" :readonly="isView" maxlength="50" />
            </label>
            <label class="field">
              <span class="field-label">身份证号码 *</span>
              <div class="inline-field">
                <el-input
                  v-model="form.idCard"
                  :readonly="isView"
                  maxlength="18"
                  @blur="handleIdCardBlur"
                />
                <el-button
                  v-if="!isView"
                  :icon="Camera"
                  :loading="ocrRunning"
                  @click="triggerOcr"
                >
                  照片
                </el-button>
              </div>
              <input
                ref="ocrInputRef"
                type="file"
                accept="image/*"
                class="hidden-file"
                @change="handleOcrFile"
              />
            </label>
            <label class="field">
              <span class="field-label">联系电话 *</span>
              <el-input v-model="form.phone" :readonly="isView" maxlength="30" />
            </label>
            <label class="field">
              <span class="field-label">性别（自动）</span>
              <el-input :model-value="form.gender" readonly />
            </label>
            <label class="field">
              <span class="field-label">出生日期（自动）</span>
              <el-input :model-value="form.birthDate || ''" readonly />
            </label>
            <label class="field">
              <span class="field-label">当前年龄（自动）</span>
              <el-input :model-value="age === null ? '' : `${age} 周岁`" readonly />
            </label>
          </div>
        </NcCard>

        <NcCard title="户籍与住址">
          <p v-if="addressScopeLoading" class="section-note">正在读取经办机构区划…</p>
          <p v-else-if="addressScopeError" class="section-note scope-warning">
            {{ addressScopeError }}
          </p>
          <p v-else-if="addressUnavailable" class="section-note scope-warning">
            当前账号未绑定任何区划，地址不可选择。请先到「用户与权限管理 → 经办机构管理」为本单位补齐省/市/县/镇/村后再录入。
          </p>
          <p v-else-if="addressScope" class="section-note">
            {{ addressScope.scopeDescription }}
          </p>

          <p class="section-note">户籍地址（限本单位管辖范围）</p>
          <div class="address-block">
            <AddressCascader
              v-model:province="form.hukouProvince"
              v-model:city="form.hukouCity"
              v-model:district="form.hukouCounty"
              v-model:town="form.hukouTown"
              v-model:village="form.hukouVillage"
              :disabled="isView || addressUnavailable"
              :locked-level="addressScope?.lockLevel ?? 0"
            />
          </div>

          <p class="section-note section-note--spaced">家庭住址（独立区划 · 全国可选）</p>
          <div class="address-block">
            <AddressCascader
              v-model:province="form.familyProvince"
              v-model:city="form.familyCity"
              v-model:district="form.familyCounty"
              v-model:town="form.familyTown"
              v-model:village="form.familyVillage"
              :disabled="isView || addressUnavailable"
              :locked-level="0"
            />
            <label class="field">
              <span class="field-label">家庭详细地址（门牌号）</span>
              <el-input
                v-model="form.detailAddress"
                :readonly="isView"
                placeholder="如：柳树村99号"
              />
            </label>
          </div>
        </NcCard>

        <NcCard title="社保卡信息">
          <div class="form-grid">
            <label class="field">
              <span class="field-label">社保卡开户行 *</span>
              <el-input v-model="form.bankName" :readonly="isView" maxlength="100" />
            </label>
            <label class="field">
              <span class="field-label">社保卡账号 *</span>
              <el-input v-model="form.bankAccount" :readonly="isView" maxlength="50" />
            </label>
          </div>
        </NcCard>
      </div>

      <!-- ── 代办代领 ─────────────────────────────────────────────── -->
      <div v-show="activeSection === 1" class="section-stack">
        <NcCard title="代办人 / 代领人">
          <p class="group-title">代办人（代为申请办理）</p>
          <div class="form-grid">
            <label class="field">
              <span class="field-label">代办人姓名</span>
              <el-input v-model="form.agentName" :readonly="isView" maxlength="50" />
            </label>
            <label class="field">
              <span class="field-label">与申请人关系</span>
              <el-input v-model="form.agentRelation" :readonly="isView" maxlength="50" />
            </label>
          </div>

          <p class="group-title group-title--spaced">代领人（代领补贴款项）</p>
          <div class="form-grid">
            <label class="field">
              <span class="field-label">代领人姓名</span>
              <el-input v-model="form.agentReceiveName" :readonly="isView" maxlength="50" />
            </label>
            <label class="field">
              <span class="field-label">与申请人关系</span>
              <el-input v-model="form.agentReceiveRelation" :readonly="isView" maxlength="50" />
            </label>
            <label class="field field--wide">
              <span class="field-label">代领原因</span>
              <el-input v-model="form.agentReceiveReason" :readonly="isView" maxlength="200" />
            </label>
            <label class="field">
              <span class="field-label">代领人社保卡开户行</span>
              <el-input v-model="form.agentReceiveBankName" :readonly="isView" maxlength="100" />
            </label>
            <label class="field">
              <span class="field-label">代领人社保卡账号</span>
              <el-input v-model="form.agentReceiveBankAccount" :readonly="isView" maxlength="50" />
            </label>
          </div>
        </NcCard>
      </div>

      <!-- ── 判定认证 ─────────────────────────────────────────────── -->
      <div v-show="activeSection === 2" class="section-stack">
        <NcCard title="享受类别与补发计算">
          <div class="evaluate-bar">
            <el-button
              type="primary"
              :loading="evaluating"
              :disabled="isView"
              @click="runEvaluation(true)"
            >
              判定
            </el-button>
            <span v-if="evaluated" class="evaluate-message">
              判定完成：{{ categoryText }}，月标准 {{ formatMoney(monthlyAmount) }} 元/月
            </span>
          </div>

          <div class="form-grid">
            <label class="field">
              <span class="field-label">享受类别判定</span>
              <el-select
                v-model="form.category"
                :disabled="isView"
                @change="handleCategoryChange"
              >
                <el-option
                  v-for="option in categoryOptions"
                  :key="option.value"
                  :label="option.label"
                  :value="option.value"
                />
              </el-select>
            </label>
            <label class="field">
              <span class="field-label">月补贴标准</span>
              <el-input :model-value="`${formatMoney(monthlyAmount)} 元/月`" readonly />
            </label>
            <label class="field">
              <span class="field-label">身份比对结果</span>
              <el-input :model-value="form.identityFlag" readonly />
            </label>
            <label class="field">
              <span class="field-label">受理日期</span>
              <el-date-picker
                v-model="form.applyDate"
                type="date"
                value-format="YYYY-MM-DD"
                :disabled="isView"
                style="width: 100%"
                @change="runEvaluation(false)"
              />
            </label>
            <label class="field">
              <span class="field-label">补发起算月</span>
              <el-date-picker
                v-model="form.paybackStartMonth"
                type="month"
                value-format="YYYY-MM"
                :disabled="isView || !form.isSpecialCase"
                style="width: 100%"
                @change="recalcSpecialPayback"
              />
            </label>
            <label class="field">
              <span class="field-label">补发止算月</span>
              <el-date-picker
                v-model="form.paybackEndMonth"
                type="month"
                value-format="YYYY-MM"
                :disabled="isView || !form.isSpecialCase"
                style="width: 100%"
                @change="recalcSpecialPayback"
              />
            </label>
            <label class="field">
              <span class="field-label">补发月数</span>
              <el-input :model-value="`${form.paybackMonths || 0} 个月`" readonly />
            </label>
            <label class="field">
              <span class="field-label">补发金额</span>
              <el-input :model-value="`${formatMoney(form.paybackAmount)} 元`" readonly />
            </label>
            <label class="field">
              <span class="field-label">计发年月（受理次月）</span>
              <el-input :model-value="form.issueStartMonth" readonly />
            </label>
            <label class="field">
              <span class="field-label">计发金额</span>
              <el-input :model-value="`${formatMoney(form.issueAmount)} 元/月`" readonly />
            </label>
          </div>

          <p class="group-title group-title--spaced">补发分段明细</p>
          <el-table v-if="segments.length > 0" :data="segments" size="small" border>
            <el-table-column label="类别" prop="categoryName" min-width="180" />
            <el-table-column label="起算月" prop="segmentStartMonth" width="110" />
            <el-table-column label="止算月" prop="segmentEndMonth" width="110" />
            <el-table-column label="月数" width="80">
              <template #default="{ row }">{{ row.months }} 个月</template>
            </el-table-column>
            <el-table-column label="金额" width="110" align="right">
              <template #default="{ row }">{{ formatMoney(row.segmentAmount) }} 元</template>
            </el-table-column>
          </el-table>
          <p v-else class="empty-segments">暂无补发分段</p>

          <div class="form-grid form-grid--single">
            <label class="field">
              <span class="field-label">补发原因（可选）</span>
              <el-select v-model="form.paybackReason" :disabled="isView" clearable>
                <el-option
                  v-for="option in paybackReasonOptions"
                  :key="option.value"
                  :label="option.label"
                  :value="option.value"
                />
              </el-select>
            </label>
          </div>

          <div class="special-case">
            <el-checkbox v-model="form.isSpecialCase" :disabled="isView">
              特殊情况处理（手动调整补发起止月份）
            </el-checkbox>
            <template v-if="form.isSpecialCase">
              <label class="field">
                <span class="field-label">特殊情况原因说明 *</span>
                <el-input
                  v-model="form.specialReason"
                  type="textarea"
                  :rows="3"
                  :readonly="isView"
                  placeholder="如：因身份登记信息纠错、材料延迟申报等，需手动调整补发起止月份"
                />
              </label>
              <p class="section-note">
                提示：补发起算月与止算月都留空 = 无补发；填写后补发月数/金额/分段按该区间立即重算，须与原因说明一致
              </p>
            </template>
          </div>
        </NcCard>
      </div>

      <div class="bottom-bar">
        <div class="bottom-nav">
          <el-button :disabled="activeSection === 0" @click="goPrevious">上一页</el-button>
          <el-button :disabled="activeSection === 2" @click="goNext">下一页</el-button>
        </div>
        <div class="bottom-actions">
          <el-button v-if="isView" @click="backToList">返回列表</el-button>
          <el-button
            v-if="isSavable"
            :loading="saving"
            @click="save(false)"
          >
            保存草稿
          </el-button>
          <el-button
            v-if="showSaveAndConfirm"
            type="primary"
            :loading="saving"
            @click="save(true)"
          >
            保存并确认
          </el-button>
        </div>
      </div>
    </template>
  </div>
</template>

<style scoped>
.elderly-form-page {
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
  font-family: var(--nc-font-mono);
  color: var(--nc-ink);
}

.section-tabs {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 10px;
}

.section-tab {
  padding: 11px 12px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background: var(--nc-paper);
  color: var(--nc-ink-2);
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

.form-grid--single {
  grid-template-columns: minmax(0, 1fr);
  margin-top: 14px;
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

.inline-field {
  display: flex;
  gap: 8px;
}

.inline-field :deep(.el-input) {
  flex: 1;
}

.hidden-file {
  display: none;
}

.section-note {
  margin: 0 0 10px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  line-height: 1.6;
}

.section-note--spaced {
  margin-top: 18px;
}

.scope-warning {
  color: var(--el-color-danger);
}

.address-block {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.group-title {
  margin: 0 0 10px;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  font-weight: 700;
}

.group-title--spaced {
  margin-top: 18px;
}

.evaluate-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 12px;
  margin-bottom: 14px;
}

.evaluate-message {
  color: var(--nc-seal);
  font-size: var(--nc-text-sm);
}

.empty-segments {
  margin: 0;
  padding: 14px;
  border: 1px dashed var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  text-align: center;
}

.special-case {
  display: flex;
  flex-direction: column;
  gap: 10px;
  margin-top: 18px;
  padding-top: 14px;
  border-top: 1px dashed var(--nc-line-faint);
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
  gap: 10px;
}

@media (max-width: 1100px) {
  .form-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 700px) {
  .section-tabs,
  .form-grid {
    grid-template-columns: minmax(0, 1fr);
  }

  .field--wide {
    grid-column: auto;
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
