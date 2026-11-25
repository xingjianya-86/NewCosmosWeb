<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { ArrowLeft, CopyDocument, Printer, Refresh, Search } from '@element-plus/icons-vue'
import {
  buildQuickVerifyReport,
  getQuickVerify,
  getQuickVerifyStandards,
  simulateQuickVerify,
} from '@/api/quickVerify'
import { getErrorMessage } from '@/api/http'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import type {
  QuickVerifyGroup,
  QuickVerifyReportRequest,
  QuickVerifyResponse,
  QuickVerifySimulationInput,
  QuickVerifySimulationResult,
  QuickVerifyStandards,
} from '@/types/quickVerify'

/**
 * 低收入人口救助帮扶 · 快速核验 · 模拟测算。
 * 1:1 复刻旧版 Pages/SocialAssistance/QuickVerifyPage.xaml：
 *   输入卡 → 收入核算（置顶结果）→ 台账核验 10 组，一个「开始核算」按钮。
 * 所有金额/区间/标准/报告文本均由服务端 Core 计算，本页只提交输入与渲染结果。
 */

interface FormState {
  name: string
  idCard: string
  hukou: 'Rural' | 'Urban'
  familySizeText: string
  childrenText: string
  perChildFeeText: string
  landSelfText: string
  landSubleaseText: string
  landContractText: string
  subsidyText: string
}

interface ReportMeta {
  length: number
  verificationIncluded: boolean
  verificationError: string
}

const HUKOU_OPTIONS: { value: FormState['hukou']; label: string }[] = [
  { value: 'Rural', label: '农村户口' },
  { value: 'Urban', label: '城市户口' },
]

const router = useRouter()
const isNarrow = useMediaQuery('(max-width: 960px)')

function createForm(): FormState {
  return {
    name: '',
    idCard: '',
    hukou: 'Rural',
    familySizeText: '',
    childrenText: '',
    perChildFeeText: '',
    landSelfText: '',
    landSubleaseText: '',
    landContractText: '',
    subsidyText: '',
  }
}

const form = reactive<FormState>(createForm())

const standards = ref<QuickVerifyStandards | null>(null)
const standardsLoading = ref(false)
const standardsError = ref('')

const verifying = ref(false)
const simulating = ref(false)
const reporting = ref(false)
const busy = computed(() => verifying.value || simulating.value)

const verifyResult = ref<QuickVerifyResponse | null>(null)
const simulation = ref<QuickVerifySimulationResult | null>(null)
const verifyError = ref('')
const simulationError = ref('')

const landHint = ref('')
const subsidyHint = ref('')
const lastImportedLandTotal = ref(0)
const lastVerifiedIdCard = ref('')

const showAllGroups = ref(false)
const reportVisible = ref(false)
const reportText = ref('')
const reportMeta = ref<ReportMeta | null>(null)
const copied = ref(false)

let simulateTicket = 0

const canImport = computed(() => isIdCardLike(form.idCard))
const perChildFeePlaceholder = computed(() => {
  const current = standards.value
  if (!current || !current.standardAvailable || !current.perChildFeeText) return '0'
  return current.perChildFeeText
})

const displayedGroups = computed<QuickVerifyGroup[]>(() => {
  const groups = verifyResult.value?.groups ?? []
  return showAllGroups.value ? groups : groups.filter((group) => group.hasItems || group.isFailure)
})

const hasVerifyEmpty = computed(() => {
  const groups = verifyResult.value?.groups ?? []
  return groups.length > 0 && groups.every((group) => !group.hasItems && !group.isFailure)
})

/** 土地承包台账无身份证列，按姓名匹配的同名风险提示。 */
const sameNameRisk = computed(() => {
  const group = verifyResult.value?.groups.find((item) => item.key === 'contractLand')
  return group?.hasItems
    ? '土地承包台账无身份证列、按姓名匹配，存在同名风险；请结合历史确权、补贴等按身份证匹配的组综合判断。'
    : ''
})

const standardWarning = computed(() => {
  if (standardsError.value) return `标准读取失败：${standardsError.value}`
  if (standards.value && !standards.value.standardAvailable) return standards.value.standardError
  return ''
})

const standardSourceText = computed(() =>
  standards.value?.standardSource
    ? `${standards.value.standardSource}（${standards.value.standardType}）`
    : '数据中心-标准配置管理',
)

function isIdCardLike(value: string): boolean {
  return /^\d{17}[\dXx]$/.test(value.trim())
}

function parseCount(text: string): number | null {
  const value = text.trim()
  if (!value) return 0
  if (!/^\d+$/.test(value)) return null
  const parsed = Number(value)
  return Number.isSafeInteger(parsed) ? parsed : null
}

function parseDecimal(text: string): number | null {
  const value = text.trim()
  if (!value) return 0
  if (!/^\d+(\.\d+)?$/.test(value)) return null
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : null
}

function shortNumber(value: number): string {
  const fixed = value.toFixed(2)
  return fixed.includes('.') ? fixed.replace(/0+$/, '').replace(/\.$/, '') : fixed
}

function moneyText(value: number): string {
  return value.toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

/** 与旧版 ValidateIncomeInputs 文案保持一致。 */
function validateForm(): string | null {
  if (!form.name.trim()) return '请输入姓名'
  if (!isIdCardLike(form.idCard)) return '请输入正确的 18 位身份证号'
  if (parseCount(form.familySizeText) === null) return '家庭人口（不含户主）请输入 0 或正整数'
  if (parseCount(form.childrenText) === null) return '子女数量请输入 0 或正整数'

  const areas: Array<[string, string]> = [
    ['自种面积', form.landSelfText],
    ['转包面积', form.landSubleaseText],
    ['承包面积', form.landContractText],
    ['补贴年值', form.subsidyText],
  ]
  for (const [label, text] of areas) {
    if (parseDecimal(text) === null) return `${label}格式不正确`
  }

  if (form.perChildFeeText.trim() && parseDecimal(form.perChildFeeText) === null) {
    return '每子女月赡养费格式不正确'
  }

  return null
}

function buildInput(): QuickVerifySimulationInput {
  const feeText = form.perChildFeeText.trim()
  return {
    name: form.name.trim(),
    idCard: form.idCard.trim().toUpperCase(),
    hukou: form.hukou,
    familySizeExcludingHead: parseCount(form.familySizeText) ?? 0,
    children: parseCount(form.childrenText) ?? 0,
    perChildMonthlyFee: feeText ? parseDecimal(feeText) : null,
    landSelfArea: parseDecimal(form.landSelfText) ?? 0,
    landSubleaseArea: parseDecimal(form.landSubleaseText) ?? 0,
    landContractArea: parseDecimal(form.landContractText) ?? 0,
    subsidyAnnual: parseDecimal(form.subsidyText) ?? 0,
  }
}

function normalizeIdCard(): void {
  form.idCard = form.idCard.trim().toUpperCase()
}

async function loadStandards(): Promise<void> {
  standardsLoading.value = true
  standardsError.value = ''
  try {
    standards.value = await getQuickVerifyStandards(form.hukou)
  } catch (error) {
    standards.value = null
    standardsError.value = getErrorMessage(error, '标准读取失败')
  } finally {
    standardsLoading.value = false
  }
}

watch(
  () => form.hukou,
  () => {
    void loadStandards()
  },
)

/** 保存测算结果：与旧版一致，把实际采用的每子女月赡养费回显到输入框。 */
function applySimulation(result: QuickVerifySimulationResult): void {
  simulation.value = result
  form.perChildFeeText = result.perChildFeeText
}

/** 「开始核算」：并行执行收入测算 + 台账核验（旧版 RunAll）。 */
async function runAll(): Promise<void> {
  const validationError = validateForm()
  if (validationError) {
    ElMessage.warning(validationError)
    return
  }
  if (busy.value) return

  verifyError.value = ''
  simulationError.value = ''
  const input = buildInput()

  verifying.value = true
  simulating.value = true
  const [verifyOutcome, simulateOutcome] = await Promise.allSettled([
    getQuickVerify(input.idCard, input.name),
    simulateQuickVerify(input),
  ] as const)
  verifying.value = false
  simulating.value = false

  if (verifyOutcome.status === 'fulfilled') {
    verifyResult.value = verifyOutcome.value
    lastVerifiedIdCard.value = input.idCard
    landHint.value = ''
    subsidyHint.value = ''
  } else {
    verifyResult.value = null
    verifyError.value = getErrorMessage(verifyOutcome.reason, '台账核验失败')
  }

  if (simulateOutcome.status === 'fulfilled') {
    applySimulation(simulateOutcome.value)
  } else {
    simulation.value = null
    simulationError.value = getErrorMessage(simulateOutcome.reason, '收入测算失败')
  }

  if (verifyError.value && simulationError.value) {
    ElMessage.error('核算与核验均失败，请查看下方原因')
  } else if (simulateOutcome.status === 'rejected' && verifyOutcome.status === 'fulfilled') {
    ElMessage.warning('台账核验已完成，但收入测算失败')
  } else if (verifyOutcome.status === 'rejected' && simulateOutcome.status === 'fulfilled') {
    ElMessage.warning('收入测算已完成，但台账核验失败')
  }
}

/** 「从核验带入」前置：未核验/证号或姓名变化时自动先核验（旧版 EnsureVerifiedForImport）。 */
async function ensureVerified(): Promise<QuickVerifyResponse | null> {
  const idCard = form.idCard.trim().toUpperCase()
  if (!isIdCardLike(idCard)) {
    ElMessage.warning('请先输入正确的 18 位身份证号')
    return null
  }

  const name = form.name.trim()
  const current = verifyResult.value
  const nameChanged = current !== null && name.length > 0 && current.queryName !== name
  if (current && lastVerifiedIdCard.value === idCard && !nameChanged) {
    return current
  }

  verifying.value = true
  try {
    const result = await getQuickVerify(idCard, name || undefined)
    verifyResult.value = result
    verifyError.value = ''
    lastVerifiedIdCard.value = idCard
    return result
  } catch (error) {
    verifyError.value = getErrorMessage(error, '台账核验失败')
    ElMessage.error(verifyError.value)
    return null
  } finally {
    verifying.value = false
  }
}

/** 输入变化后若已有测算结果，按当前输入立即回算（旧版 RecomputeIncomeIfResultExists）。 */
async function recomputeIfResultExists(): Promise<void> {
  if (!simulation.value) return
  if (validateForm()) return

  const ticket = ++simulateTicket
  simulating.value = true
  try {
    const result = await simulateQuickVerify(buildInput())
    if (ticket !== simulateTicket) return
    applySimulation(result)
    simulationError.value = ''
  } catch (error) {
    if (ticket !== simulateTicket) return
    simulationError.value = getErrorMessage(error, '收入测算失败')
  } finally {
    if (ticket === simulateTicket) simulating.value = false
  }
}

async function importLand(): Promise<void> {
  const result = await ensureVerified()
  if (!result) return

  const total = result.importSuggestion.landTotal
  if (total <= 0) {
    landHint.value = '核验未查到可带入的土地数据'
    return
  }

  form.landSelfText = shortNumber(total)
  form.landSubleaseText = '0'
  form.landContractText = '0'
  lastImportedLandTotal.value = total
  landHint.value = `已带入 ${total.toFixed(2)} 亩（来自${result.importSuggestion.landSource}，默认计入自种，可用下方按钮重新分配）`
  await recomputeIfResultExists()
}

async function importSubsidy(): Promise<void> {
  const result = await ensureVerified()
  if (!result) return

  const suggestion = result.importSuggestion
  if (suggestion.subsidyCount <= 0 && suggestion.subsidyTotal <= 0) {
    subsidyHint.value = '核验未查到可带入的补贴数据'
    return
  }

  form.subsidyText = shortNumber(suggestion.subsidyTotal)
  subsidyHint.value = `已带入 ${moneyText(suggestion.subsidyTotal)} 元（${suggestion.subsidyCount} 条，金额型+面积型折算）`
  await recomputeIfResultExists()
}

/** 全自种/全转包/全承包：优先按最近带入的台账总面积，未带入时取三格当前合计。 */
async function batchSetLand(usage: 'self' | 'sublease' | 'contract'): Promise<void> {
  const currentTotal =
    (parseDecimal(form.landSelfText) ?? 0) +
    (parseDecimal(form.landSubleaseText) ?? 0) +
    (parseDecimal(form.landContractText) ?? 0)
  const total = lastImportedLandTotal.value > 0 ? lastImportedLandTotal.value : currentTotal
  if (total <= 0) {
    landHint.value = '请先带入或填写土地面积'
    return
  }

  const text = shortNumber(total)
  form.landSelfText = usage === 'self' ? text : '0'
  form.landSubleaseText = usage === 'sublease' ? text : '0'
  form.landContractText = usage === 'contract' ? text : '0'
  const label = usage === 'self' ? '自种' : usage === 'sublease' ? '转包' : '承包'
  landHint.value = `已将 ${total.toFixed(2)} 亩全部计入${label}`
  await recomputeIfResultExists()
}

function clearAll(): void {
  Object.assign(form, createForm())
  verifyResult.value = null
  simulation.value = null
  verifyError.value = ''
  simulationError.value = ''
  landHint.value = ''
  subsidyHint.value = ''
  lastImportedLandTotal.value = 0
  lastVerifiedIdCard.value = ''
  showAllGroups.value = false
  reportVisible.value = false
  reportText.value = ''
  reportMeta.value = null
}

async function exportReport(): Promise<void> {
  if (!simulation.value && !verifyResult.value) {
    ElMessage.warning('请先「开始核算」或触发核验后再导出')
    return
  }
  if (!isIdCardLike(form.idCard)) {
    ElMessage.warning('请输入正确的 18 位身份证号')
    return
  }
  if (simulation.value) {
    const validationError = validateForm()
    if (validationError) {
      ElMessage.warning(validationError)
      return
    }
  }
  if (reporting.value) return

  reporting.value = true
  try {
    const payload: QuickVerifyReportRequest = {
      ...buildInput(),
      includeIncome: simulation.value !== null,
      includeVerification: verifyResult.value !== null,
    }
    const report = await buildQuickVerifyReport(payload)
    reportText.value = report.text
    reportMeta.value = {
      length: report.length,
      verificationIncluded: report.verificationIncluded,
      verificationError: report.verificationError,
    }
    reportVisible.value = true
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '导出报告失败'))
  } finally {
    reporting.value = false
  }
}

function fallbackCopy(text: string): void {
  const textarea = document.createElement('textarea')
  textarea.value = text
  textarea.setAttribute('readonly', 'readonly')
  textarea.style.position = 'fixed'
  textarea.style.top = '-1000px'
  document.body.appendChild(textarea)
  textarea.select()
  const ok = document.execCommand('copy')
  document.body.removeChild(textarea)
  if (!ok) throw new Error('复制失败')
}

async function copyReport(): Promise<void> {
  const text = reportText.value
  if (!text) return
  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(text)
    } else {
      fallbackCopy(text)
    }
    copied.value = true
    ElMessage.success(`报告文本已复制到剪贴板（${text.length} 字）`)
    window.setTimeout(() => {
      copied.value = false
    }, 2000)
  } catch {
    try {
      fallbackCopy(text)
      ElMessage.success(`报告文本已复制到剪贴板（${text.length} 字）`)
    } catch {
      ElMessage.error('复制失败，请手动选择文本复制')
    }
  }
}

function escapeHtml(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
}

/**
 * 浏览器打印：新窗口写入纯文本报告后调用 window.print()。
 * 不依赖 Office/WPS COM，也不伪造公文格式 Word/PDF。
 */
function printReport(): void {
  if (!reportText.value) return
  const printWindow = window.open('', '_blank', 'width=820,height=900')
  if (!printWindow) {
    ElMessage.warning('浏览器阻止了打印窗口，请允许弹窗后重试')
    return
  }

  printWindow.document.write(
    '<!DOCTYPE html><html lang="zh-CN"><head><meta charset="utf-8" />' +
      '<title>快速核验报告</title>' +
      '<style>@page{margin:18mm}body{margin:0;color:#1a1a1a;font-family:"Microsoft YaHei","PingFang SC",sans-serif;font-size:12px;line-height:1.7}' +
      'pre{margin:0;white-space:pre-wrap;word-break:break-word;font-family:"Sarasa Mono SC","Consolas",monospace}</style>' +
      '</head><body><pre>' +
      escapeHtml(reportText.value) +
      '</pre></body></html>',
  )
  printWindow.document.close()
  printWindow.setTimeout(() => {
    printWindow.focus()
    printWindow.print()
  }, 150)
}

onMounted(() => {
  void loadStandards()
})
</script>

<template>
  <div
    v-loading="busy"
    element-loading-text="正在核算与核验..."
    class="nc-page quick-verify-page"
  >
    <NcPageHeader
      title="快速核验 · 模拟测算"
      description="核算家庭收入并判断标准 · 按身份证核验系统内台账（全程只读，不写入任何数据）"
      kicker="低收入人口救助帮扶 · 快速核验"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="router.push('/social-assistance')">返回工作台</el-button>
        <el-button :icon="Refresh" :loading="standardsLoading" @click="loadStandards">刷新标准</el-button>
      </template>
    </NcPageHeader>

    <el-alert
      v-if="standardWarning"
      class="block-alert"
      type="warning"
      :title="standardWarning"
      :closable="false"
      show-icon
    >
      <span class="small">低保标准缺失时「开始核算」会显式中止，不回退过期数值。</span>
    </el-alert>

    <!-- ═══════════ 输入卡 ═══════════ -->
    <NcCard padding="md" class="input-card">
      <h2 class="card-heading">👤 被核算人</h2>

      <div class="field-row field-row--name">
        <div class="field">
          <span class="field__label">姓名</span>
          <el-input v-model="form.name" placeholder="申请人姓名" clearable maxlength="40" />
        </div>
        <div class="field">
          <span class="field__label">身份证号</span>
          <el-input
            v-model="form.idCard"
            placeholder="18 位身份证号"
            maxlength="18"
            clearable
            @blur="normalizeIdCard"
          />
        </div>
      </div>

      <div class="field-row field-row--three">
        <div class="field">
          <span class="field__label">户口性质</span>
          <el-select v-model="form.hukou" class="full-width">
            <el-option
              v-for="option in HUKOU_OPTIONS"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
        </div>
        <div class="field">
          <span class="field__label">家庭人口（不含户主）</span>
          <el-input
            v-model="form.familySizeText"
            inputmode="numeric"
            placeholder="如 2"
          />
        </div>
        <div class="field">
          <span class="field__label">子女数量</span>
          <el-input v-model="form.childrenText" inputmode="numeric" placeholder="如 3" />
        </div>
      </div>

      <div class="divider" />
      <h2 class="card-heading">🧮 收入（三个有数据来源的项）</h2>

      <div class="field">
        <span class="field__label">
          每子女月赡养费（留空＝低保标准×20%，核算后自动回显）
        </span>
        <el-input
          v-model="form.perChildFeeText"
          inputmode="decimal"
          :placeholder="perChildFeePlaceholder"
        />
      </div>

      <div class="field-row field-row--three">
        <div class="field">
          <span class="field__label">自种面积（亩）</span>
          <el-input v-model="form.landSelfText" inputmode="decimal" placeholder="0" />
        </div>
        <div class="field">
          <span class="field__label">转包面积（亩）</span>
          <el-input v-model="form.landSubleaseText" inputmode="decimal" placeholder="0" />
        </div>
        <div class="field">
          <span class="field__label">承包面积（亩）</span>
          <el-input v-model="form.landContractText" inputmode="decimal" placeholder="0" />
        </div>
      </div>

      <div class="inline-row">
        <span class="unit-price">
          {{ standards?.unitPriceText || '单价：—' }}
        </span>
        <el-button
          size="small"
          :icon="Refresh"
          :disabled="!canImport"
          :loading="verifying"
          @click="importLand"
        >
          从核验带入
        </el-button>
      </div>

      <div class="inline-row batch-row">
        <el-button size="small" @click="batchSetLand('self')">全自种</el-button>
        <el-button size="small" @click="batchSetLand('sublease')">全转包</el-button>
        <el-button size="small" @click="batchSetLand('contract')">全承包</el-button>
      </div>
      <p v-if="landHint" class="hint hint--warn">{{ landHint }}</p>

      <div class="field-row field-row--subsidy">
        <div class="field">
          <span class="field__label">种植补贴年值（元）</span>
          <el-input v-model="form.subsidyText" inputmode="decimal" placeholder="0" />
        </div>
        <el-button
          class="subsidy-import"
          size="small"
          :icon="Refresh"
          :disabled="!canImport"
          :loading="verifying"
          @click="importSubsidy"
        >
          从核验带入
        </el-button>
      </div>
      <p v-if="subsidyHint" class="hint hint--warn">{{ subsidyHint }}</p>

      <p v-if="!simulation" class="hint">
        填好后点「开始核算」：一次算出年家庭总收入与收入区间，并同步核验系统内台账（全程只读、不写入任何数据）。
      </p>

      <div class="actions-row">
        <el-button class="action-clear" @click="clearAll">清空</el-button>
        <el-button :loading="reporting" @click="exportReport">📋 导出报告</el-button>
        <el-button type="primary" :icon="Search" :loading="busy" @click="runAll">
          开始核算
        </el-button>
      </div>
    </NcCard>

    <el-alert
      v-if="verifyError"
      class="block-alert"
      type="error"
      :title="`台账核验失败：${verifyError}`"
      :closable="false"
      show-icon
    />
    <el-alert
      v-if="simulationError"
      class="block-alert"
      type="error"
      :title="`收入测算失败：${simulationError}`"
      :closable="false"
      show-icon
    />

    <!-- ═══════════ 收入核算结果（置顶结果） ═══════════ -->
    <NcCard v-if="simulation" title="💰 收入核算（年值权威口径）" padding="md">
      <div class="income-lines">
        <div v-for="line in simulation.lines" :key="line.label" class="income-line">
          <span class="income-line__label">{{ line.label }}</span>
          <strong class="income-line__value">{{ line.value }}</strong>
        </div>
      </div>

      <div class="divider" />

      <div class="metric-grid metric-grid--three">
        <div class="metric">
          <span class="metric__label">年家庭总收入</span>
          <strong class="metric__value">{{ simulation.annualIncomeText }}</strong>
        </div>
        <div class="metric">
          <span class="metric__label">年人均</span>
          <strong class="metric__value">{{ simulation.perCapitaAnnualText }}</strong>
        </div>
        <div class="metric">
          <span class="metric__label">月人均</span>
          <strong class="metric__value">{{ simulation.perCapitaMonthlyText }}</strong>
        </div>
      </div>

      <div class="divider" />

      <div class="metric-grid metric-grid--three">
        <div class="metric">
          <span class="metric__label">户口性质</span>
          <strong class="metric__value metric__value--plain">{{ simulation.hukouText }}</strong>
        </div>
        <div class="metric">
          <span class="metric__label">低保标准（月）</span>
          <strong class="metric__value metric__value--plain">{{ simulation.standardText }}</strong>
        </div>
        <div class="metric">
          <span class="metric__label">边缘标准上限（×1.5）</span>
          <strong class="metric__value metric__value--plain">{{ simulation.edgeText }}</strong>
        </div>
      </div>

      <div class="zone-box" :class="`zone-box--${simulation.zoneKind.toLowerCase()}`">
        <strong>{{ simulation.zoneText }}</strong>
      </div>

      <p class="hint small">
        以上数值与区间由服务端 Core 统一计算（年值权威口径、一次舍入到分），页面不参与任何公式计算。
        标准来源：{{ standardSourceText }}。
      </p>
    </NcCard>

    <!-- ═══════════ 台账核验（10 组只读） ═══════════ -->
    <section class="verify-section" aria-label="台账核验">
      <h2 class="section-title">
        📋 台账核验（只读）
        <span v-if="verifyResult" class="section-count">
          命中 {{ verifyResult.hitGroupCount }} 组
        </span>
      </h2>

      <p v-if="!verifyResult" class="hint">
        尚未核验：点上方「开始核算」，或直接点「从核验带入」（会自动先核验再带入，输入身份证即可点）。
      </p>

      <template v-else>
        <div class="verify-summary">
          <p>{{ verifyResult.summary }}</p>
          <div class="summary-tags">
            <el-tag size="small" effect="plain">{{ verifyResult.idCardMasked }}</el-tag>
            <el-tag size="small" effect="plain" type="info">
              {{ verifyResult.queryName || '未匹配到姓名' }}
            </el-tag>
            <el-tag
              v-if="verifyResult.nameSource"
              size="small"
              effect="plain"
              type="warning"
            >
              姓名来源：{{ verifyResult.nameSource }}
            </el-tag>
          </div>
        </div>

        <el-alert
          v-if="sameNameRisk"
          class="block-alert"
          type="warning"
          :title="sameNameRisk"
          :closable="false"
          show-icon
        />

        <p v-if="hasVerifyEmpty" class="hint">
          系统内未查到该身份证的任何台账记录。
        </p>

        <div class="verify-toolbar">
          <el-switch
            v-model="showAllGroups"
            active-text="显示全部 10 组"
            inactive-text="仅显示有数据/失败组"
          />
          <span class="hint small">
            默认隐藏零命中分组（与旧版一致）；打开开关可逐组查看空态。
          </span>
        </div>

        <div v-if="displayedGroups.length > 0" class="group-grid">
          <NcCard
            v-for="group in displayedGroups"
            :key="group.key"
            padding="none"
            class="group-card"
          >
            <div class="group-pad">
              <header class="group-head">
                <h3 class="group-title">{{ group.title }}</h3>
                <span v-if="group.countText" class="group-count">{{ group.countText }}</span>
              </header>

              <p v-if="group.matchNote" class="match-note">
                <template v-if="group.matchNote.includes('同名风险')">⚠ </template>{{ group.matchNote }}
              </p>

              <el-alert
                v-if="group.isFailure"
                class="group-alert"
                type="error"
                :title="`本组查询失败：${group.failureReason}`"
                :closable="false"
                show-icon
              />

              <template v-else-if="group.hasItems">
                <el-table
                  v-if="!isNarrow"
                  :data="group.items"
                  size="small"
                  class="group-table"
                >
                  <el-table-column label="名称 / 项目" min-width="180">
                    <template #default="{ row }">
                      <div class="item-title">
                        <span>{{ row.title }}</span>
                        <el-tag v-if="row.badge" size="small" type="warning" effect="dark">
                          {{ row.badge }}
                        </el-tag>
                      </div>
                    </template>
                  </el-table-column>
                  <el-table-column label="明细" min-width="220">
                    <template #default="{ row }">
                      <span class="item-detail">{{ row.detail || '—' }}</span>
                    </template>
                  </el-table-column>
                  <el-table-column label="数值" width="180" align="right">
                    <template #default="{ row }">
                      <span class="item-value">{{ row.value || '—' }}</span>
                    </template>
                  </el-table-column>
                </el-table>

                <div v-else class="item-cards">
                  <article v-for="(item, index) in group.items" :key="`${group.key}-${index}`" class="item-card">
                    <header class="item-card__head">
                      <strong>{{ item.title }}</strong>
                      <el-tag v-if="item.badge" size="small" type="warning" effect="dark">
                        {{ item.badge }}
                      </el-tag>
                    </header>
                    <p v-if="item.detail" class="item-card__detail">{{ item.detail }}</p>
                    <p v-if="item.value" class="item-card__value">{{ item.value }}</p>
                  </article>
                </div>
              </template>

              <div v-else class="group-empty">
                <el-empty description="本组无记录" :image-size="46" />
              </div>
            </div>
          </NcCard>
        </div>
      </template>
    </section>

    <!-- ═══════════ 报告预览 ═══════════ -->
    <el-dialog
      v-model="reportVisible"
      title="快速核验报告"
      :width="isNarrow ? '94%' : '760px'"
      destroy-on-close
    >
      <div class="report-meta">
        <span class="hint small">共 {{ reportMeta?.length ?? 0 }} 字</span>
        <el-tag
          v-if="reportMeta && !reportMeta.verificationIncluded"
          size="small"
          type="warning"
          effect="plain"
        >
          未包含台账核验
        </el-tag>
      </div>

      <el-alert
        v-if="reportMeta?.verificationError"
        class="block-alert"
        type="warning"
        :title="`台账核验失败：${reportMeta.verificationError}`"
        :closable="false"
        show-icon
      />

      <pre class="report-text">{{ reportText }}</pre>

      <p class="hint small">
        报告含完整身份证号，仅供内部核对；网页端「打印」使用浏览器打印，不生成公文格式
        Word/PDF（公文输出请走桌面端 Agent）。
      </p>

      <template #footer>
        <el-button @click="reportVisible = false">关闭</el-button>
        <el-button :icon="CopyDocument" @click="copyReport">
          {{ copied ? '已复制' : '复制文本' }}
        </el-button>
        <el-button type="primary" :icon="Printer" @click="printReport">打印</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.quick-verify-page {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.block-alert {
  margin: 0;
}

.small {
  font-size: var(--nc-text-xs);
}

.input-card {
  overflow: hidden;
}

.card-heading {
  margin: 0 0 10px;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: 16px;
  font-weight: 700;
  letter-spacing: 0.02em;
}

.field-row {
  display: grid;
  gap: 12px;
  margin-bottom: 12px;
}

.field-row--name {
  grid-template-columns: minmax(0, 1fr) minmax(0, 2fr);
}

.field-row--three {
  grid-template-columns: repeat(3, minmax(0, 1fr));
}

.field-row--subsidy {
  grid-template-columns: minmax(0, 1fr) auto;
  align-items: end;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.field__label {
  color: var(--nc-ink-2);
  font-size: 13px;
  line-height: 1.5;
}

.full-width {
  width: 100%;
}

.divider {
  height: 1px;
  margin: 14px 0;
  background-color: var(--nc-line-faint);
}

.inline-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  margin-top: -2px;
  margin-bottom: 10px;
}

.unit-price {
  color: var(--nc-muted);
  font-size: 12px;
  line-height: 1.6;
}

.batch-row {
  justify-content: flex-start;
  flex-wrap: wrap;
}

.subsidy-import {
  margin-bottom: 1px;
}

.hint {
  margin: 0 0 10px;
  color: var(--nc-muted);
  font-size: 12px;
  line-height: 1.7;
}

.hint--warn {
  color: var(--nc-seal-dark);
}

.actions-row {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 10px;
  margin-top: 6px;
}

.action-clear {
  margin-right: auto;
}

.income-lines {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.income-line {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 12px;
}

.income-line__label {
  color: var(--nc-ink-2);
  font-size: 13px;
  line-height: 1.6;
}

.income-line__value {
  flex: none;
  color: var(--nc-ink);
  font-family: var(--nc-font-mono);
  font-size: 13px;
  font-weight: 600;
}

.metric-grid {
  display: grid;
  gap: 10px;
}

.metric-grid--three {
  grid-template-columns: repeat(3, minmax(0, 1fr));
}

.metric {
  display: flex;
  flex-direction: column;
  gap: 3px;
  min-width: 0;
}

.metric__label {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  letter-spacing: 0.08em;
}

.metric__value {
  overflow: hidden;
  color: var(--nc-seal);
  font-family: var(--nc-font-serif);
  font-size: 16px;
  font-weight: 700;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.metric__value--plain {
  color: var(--nc-ink);
  font-family: var(--nc-font-sans);
  font-size: 15px;
}

.zone-box {
  margin-top: 14px;
  padding: 12px 14px;
  border-left: 4px solid var(--nc-pine);
  background-color: var(--nc-pine-soft);
  color: var(--nc-pine-dark);
  font-size: 14px;
  line-height: 1.6;
}

.zone-box--edge {
  border-left-color: var(--nc-amber);
  background-color: var(--nc-amber-soft);
  color: var(--nc-seal-dark);
}

.zone-box--over {
  border-left-color: var(--nc-seal);
  background-color: var(--nc-seal-soft);
  color: var(--nc-seal-dark);
}

.verify-section {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.section-title {
  display: flex;
  align-items: center;
  gap: 10px;
  margin: 6px 0 2px;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: 17px;
  font-weight: 700;
  letter-spacing: 0.02em;
}

.section-count {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 11px;
  font-weight: 500;
  letter-spacing: 0.06em;
}

.verify-summary {
  padding: 12px 14px;
  border: 1px solid var(--nc-line-faint);
  background-color: var(--nc-paper-2);
}

.verify-summary p {
  margin: 0;
  color: var(--nc-ink-2);
  font-size: 13px;
  line-height: 1.7;
}

.summary-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin-top: 8px;
}

.verify-toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
}

.group-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.group-card {
  overflow: hidden;
}

.group-pad {
  padding: 14px 16px 10px;
}

.group-head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 10px;
}

.group-title {
  margin: 0;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: 15px;
  font-weight: 700;
  letter-spacing: 0.02em;
}

.group-count {
  flex: none;
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 11px;
}

.match-note {
  margin: 7px 0 0;
  color: var(--nc-amber);
  font-size: 11px;
  line-height: 1.6;
}

.group-alert {
  margin-top: 10px;
}

.group-table {
  width: 100%;
  margin-top: 8px;
}

.item-title {
  display: flex;
  align-items: center;
  gap: 6px;
  min-width: 0;
}

.item-title span {
  overflow: hidden;
  color: var(--nc-ink);
  font-size: 13px;
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.item-detail {
  display: block;
  color: var(--nc-ink-2);
  font-size: 12px;
  line-height: 1.6;
  word-break: break-word;
}

.item-value {
  color: var(--nc-seal-dark);
  font-family: var(--nc-font-mono);
  font-size: 12px;
  white-space: nowrap;
}

.item-cards {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin-top: 10px;
}

.item-card {
  padding: 9px 10px;
  border: 1px solid var(--nc-line-faint);
  background-color: var(--nc-paper-3);
}

.item-card__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.item-card__head strong {
  color: var(--nc-ink);
  font-size: 13px;
  font-weight: 600;
  line-height: 1.5;
}

.item-card__detail {
  margin: 4px 0 0;
  color: var(--nc-ink-2);
  font-size: 11px;
  line-height: 1.6;
}

.item-card__value {
  margin: 4px 0 0;
  color: var(--nc-seal-dark);
  font-family: var(--nc-font-mono);
  font-size: 12px;
}

.group-empty {
  margin-top: 4px;
}

.group-empty :deep(.el-empty) {
  padding: 6px 0;
}

.report-meta {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  margin-bottom: 8px;
}

.report-text {
  max-height: min(58vh, 560px);
  margin: 0;
  padding: 14px 16px;
  overflow: auto;
  border: 1px solid var(--nc-line-faint);
  background-color: var(--nc-paper-3);
  color: var(--nc-ink);
  font-family: var(--nc-font-mono);
  font-size: 12px;
  line-height: 1.75;
  white-space: pre-wrap;
  word-break: break-word;
}

@media (max-width: 1100px) {
  .group-grid {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 767px) {
  .field-row--name,
  .field-row--three,
  .field-row--subsidy {
    grid-template-columns: 1fr;
  }

  .subsidy-import {
    width: 100%;
  }

  .actions-row {
    flex-direction: column;
  }

  .action-clear {
    margin-right: 0;
  }

  .actions-row .el-button {
    width: 100%;
    margin-left: 0;
  }

  .metric-grid--three {
    grid-template-columns: 1fr;
  }

  .income-line {
    flex-direction: column;
    gap: 2px;
  }

  .income-line__value {
    align-self: flex-end;
  }

  .group-pad {
    padding: 12px 12px 8px;
  }

  .group-head {
    flex-wrap: wrap;
  }
}

@media (max-width: 420px) {
  .metric-grid--three {
    grid-template-columns: 1fr;
  }
}
</style>
