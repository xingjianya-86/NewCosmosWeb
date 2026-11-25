<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { ArrowLeft, Printer, Refresh } from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import {
  calculateRecovery,
  createRecoveryRecord,
  getRecoveryRecord,
  getRecoverySegments,
  updateRecoveryRecord,
} from '@/api/recovery'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import type {
  RecoveryAmountSource,
  RecoveryCalculateResult,
  RecoveryRecord,
  RecoveryRecordDto,
  RecoverySegment,
  StoppedPersonDto,
} from '@/types/recovery'
import {
  recoveryAmountSourceLabel,
  recoverySegmentSourceLabel,
  recoverySourceTypeLabel,
  recoveryStatusLabel,
} from '@/types/recovery'
import {
  buildMonth,
  clearStoppedPerson,
  formatMoney,
  monthDiff,
  parseMonth,
  readStoppedPerson,
  roundFen,
  sourceLabel,
} from './display'
import RecoveryPrintDialog from './RecoveryPrintDialog.vue'

const route = useRoute()
const router = useRouter()

const now = new Date()
const yearOptions = Array.from({ length: now.getFullYear() + 2 - 2020 }, (_, index) => 2020 + index)
const monthOptions = Array.from({ length: 12 }, (_, index) => index + 1)

const recordId = ref<number | null>(parseRecordId())
const recordStatus = ref('Draft')
const loading = ref(false)
const loadError = ref('')
const personLoaded = ref(false)

const sourceType = ref('')
const sourceDisplay = ref('')
const sourceId = ref(0)
const personName = ref('')
const idCard = ref('')
const monthlyAmount = ref(0)
const recoveryReason = ref('')

const startYear = ref(now.getFullYear())
const startMonthValue = ref(now.getMonth() + 1)
const endYear = ref(now.getFullYear())
const endMonthValue = ref(now.getMonth() + 1)

const recoveryAmount = ref(0)
const autoAmount = ref(0)
const autoSource = ref<RecoveryAmountSource>('Flat')
const amountSource = ref<RecoveryAmountSource>('Flat')
const amountLocked = ref(false)
const amountNote = ref('')
const cannotAutoReason = ref('')
const calcError = ref('')
const calcLoading = ref(false)
const recoveredAmount = ref(0)

const segments = ref<RecoverySegment[]>([])
const storedSegments = ref<RecoverySegment[]>([])
const segmentsExpanded = ref(false)

const saving = ref(false)
const saveMode = ref<'draft' | 'print' | ''>('')

const printVisible = ref(false)
const printAutoSubmit = ref(false)
const printRecordId = ref<number | null>(null)

let calcTicket = 0

const isEdit = computed(() => recordId.value !== null)
const readonly = computed(() => isEdit.value && recordStatus.value !== 'Draft')
const pageTitle = '填写追缴信息'
const statusText = computed(() => recoveryStatusLabel(recordStatus.value) || recordStatus.value)

const startMonth = computed(() => buildMonth(startYear.value, startMonthValue.value))
const endMonth = computed(() => buildMonth(endYear.value, endMonthValue.value))
const recoveryMonths = computed(() => monthDiff(startMonth.value, endMonth.value))

const isManualAmount = computed(() => amountSource.value === 'Manual')
const amountSourceText = computed(
  () => recoveryAmountSourceLabel(amountSource.value) || amountSource.value,
)
const hasAutoDifference = computed(
  () =>
    isManualAmount.value
    && autoAmount.value > 0
    && Math.abs(recoveryAmount.value - autoAmount.value) >= 0.005,
)
const autoReferenceText = computed(() => `自动计算参考值：${formatMoney(autoAmount.value)}`)
const hasSegments = computed(() => segments.value.length > 0)
const segmentsTotalText = computed(() => {
  if (segments.value.length === 0) return ''
  const months = segments.value.reduce((sum, segment) => sum + (Number(segment.months) || 0), 0)
  const amount = segments.value.reduce(
    (sum, segment) => sum + (Number(segment.segmentAmount) || 0),
    0,
  )
  return `合计 ${months} 个月 / ${amount.toFixed(2)} 元`
})

const printPersonName = computed(() => personName.value)
const printSourceDisplay = computed(
  () => sourceDisplay.value || recoverySourceTypeLabel(sourceType.value) || sourceType.value,
)

function parseRecordId(): number | null {
  const raw = Array.isArray(route.query.recordId) ? route.query.recordId[0] : route.query.recordId
  const parsed = Number(raw)
  return Number.isFinite(parsed) && parsed > 0 ? parsed : null
}

function normalizeAmountSource(value: string | null | undefined): RecoveryAmountSource {
  if (value === 'Segment' || value === 'Manual' || value === 'Flat') return value
  return 'Flat'
}

function applyPerson(person: StoppedPersonDto): void {
  sourceType.value = person.sourceType ?? ''
  sourceDisplay.value = person.sourceDisplay || recoverySourceTypeLabel(person.sourceType)
  sourceId.value = Number(person.sourceId) || 0
  personName.value = person.personName ?? ''
  idCard.value = person.idCard ?? ''
  monthlyAmount.value = roundFen(Number(person.monthlyAmount) || 0)
  amountLocked.value = false
  personLoaded.value = true
}

function applyRecord(record: RecoveryRecord): void {
  recordStatus.value = record.status || 'Draft'
  sourceType.value = record.sourceType ?? ''
  sourceDisplay.value = record.sourceTypeDisplay || recoverySourceTypeLabel(record.sourceType)
  sourceId.value = Number(record.sourceId) || 0
  personName.value = record.personName ?? ''
  idCard.value = record.idCard ?? ''
  monthlyAmount.value = roundFen(Number(record.monthlyAmount) || 0)
  recoveryReason.value = record.recoveryReason ?? ''
  recoveredAmount.value = roundFen(Number(record.recoveredAmount) || 0)
  recoveryAmount.value = roundFen(Number(record.recoveryAmount) || 0)
  amountSource.value = normalizeAmountSource(record.amountSource)
  amountLocked.value = amountSource.value === 'Manual'
  amountNote.value = record.amountNote ?? ''
  autoAmount.value = roundFen(Number(record.autoAmount) || 0)

  const start = parseMonth(record.startMonth)
  if (start) {
    startYear.value = start.year
    startMonthValue.value = start.month
  }
  const end = parseMonth(record.endMonth)
  if (end) {
    endYear.value = end.year
    endMonthValue.value = end.month
  }

  personLoaded.value = true
}

async function runCalculate(options: {
  apply: boolean
  sourceType?: string
  sourceId?: number
} = { apply: true }): Promise<void> {
  const ticket = ++calcTicket
  calcLoading.value = true
  calcError.value = ''
  try {
    const result = await calculateRecovery({
      sourceType: options.sourceType ?? sourceType.value,
      sourceId: options.sourceId ?? sourceId.value,
      startMonth: startMonth.value,
      endMonth: endMonth.value,
      monthlyAmount: monthlyAmount.value,
    })
    if (ticket !== calcTicket) return

    applyAutoReference(result)
    if (options.apply && !amountLocked.value) {
      applyAuto(result)
    }
  } catch (error) {
    if (ticket !== calcTicket) return
    calcError.value = getErrorMessage(error, '自动计算失败，请手工填写应追缴总额')
  } finally {
    if (ticket === calcTicket) calcLoading.value = false
  }
}

function applyAutoReference(result: RecoveryCalculateResult): void {
  autoAmount.value = roundFen(Number(result.amount) || 0)
  autoSource.value = normalizeAmountSource(result.amountSource)
  cannotAutoReason.value = result.canCalculate ? '' : String(result.reason ?? '')
  segments.value = result.segments?.length ? result.segments : storedSegments.value
}

function applyAuto(result: RecoveryCalculateResult): void {
  if (!result.canCalculate) {
    amountSource.value = 'Manual'
    return
  }
  amountSource.value = autoSource.value
  recoveryAmount.value = roundFen(Number(result.amount) || 0)
}

function handleMonthlyChange(value: number | undefined): void {
  monthlyAmount.value = roundFen(Number(value) || 0)
  void runCalculate({ apply: true })
}

function handlePeriodChange(): void {
  void runCalculate({ apply: true })
}

function handleRecoveryAmountChange(value: number | undefined): void {
  const next = roundFen(Number(value) || 0)
  recoveryAmount.value = next

  if (autoAmount.value > 0 && Math.abs(next - autoAmount.value) < 0.005) {
    amountLocked.value = false
    amountSource.value = autoSource.value
    return
  }

  amountLocked.value = true
  amountSource.value = 'Manual'
}

function toggleSegments(): void {
  segmentsExpanded.value = !segmentsExpanded.value
}

function segmentSourceText(segment: RecoverySegment): string {
  return segment.sourceDisplay || recoverySegmentSourceLabel(segment.source) || segment.source || '—'
}

function buildDto(): RecoveryRecordDto {
  return {
    sourceType: sourceType.value,
    sourceId: sourceId.value,
    personName: personName.value.trim(),
    idCard: idCard.value.trim(),
    recoveryReason: recoveryReason.value.trim(),
    monthlyAmount: roundFen(monthlyAmount.value),
    startMonth: startMonth.value,
    endMonth: endMonth.value,
    recoveredAmount: roundFen(recoveredAmount.value),
    recoveryAmount: roundFen(recoveryAmount.value),
    amountSource: amountSource.value,
    amountNote: amountNote.value.trim(),
    autoAmount: roundFen(autoAmount.value),
    segments: segments.value.map((segment) => ({
      segmentNo: segment.segmentNo,
      monthlyAmount: roundFen(segment.monthlyAmount),
      segmentStartMonth: segment.segmentStartMonth,
      segmentEndMonth: segment.segmentEndMonth,
      months: segment.months,
      segmentAmount: roundFen(segment.segmentAmount),
      source: segment.source,
      sourceDisplay: segment.sourceDisplay,
    })),
  }
}

async function persistRecord(): Promise<number | null> {
  const dto = buildDto()
  if (recordId.value !== null) {
    await updateRecoveryRecord(recordId.value, dto)
    return recordId.value
  }

  const saved = await createRecoveryRecord(dto)
  if (saved.id > 0) {
    recordId.value = saved.id
    recordStatus.value = saved.record?.status || 'Draft'
    return saved.id
  }
  return null
}

function validateForPrint(): string | null {
  if (!personName.value.trim()) return '请输入追缴人员姓名'
  if (recoveryMonths.value <= 0) return '追缴月数必须大于0'
  if (recoveryAmount.value <= 0) return '应追缴金额必须大于0'
  if (isManualAmount.value && !amountNote.value.trim()) {
    return '手工填写的应追缴总额必须说明原因'
  }
  return null
}

async function handleSaveDraft(): Promise<void> {
  if (readonly.value || saving.value) return
  saving.value = true
  saveMode.value = 'draft'
  try {
    await persistRecord()
    ElMessage.success('草稿已保存')
    clearStoppedPerson()
    await router.push({ path: '/recovery', query: { tab: 'records' } })
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '保存草稿失败'))
  } finally {
    saving.value = false
    saveMode.value = ''
  }
}

async function handleSaveAndPrint(): Promise<void> {
  if (readonly.value || saving.value) return

  const invalid = validateForPrint()
  if (invalid) {
    ElMessage.warning(invalid)
    return
  }

  saving.value = true
  saveMode.value = 'print'
  try {
    const id = await persistRecord()
    clearStoppedPerson()
    if (id === null) {
      ElMessage.warning('已保存，但服务端未返回记录编号，无法自动打印；请到列表补打')
      return
    }
    printRecordId.value = id
    printAutoSubmit.value = true
    printVisible.value = true
    ElMessage.success('已保存，正在推送打印任务…')
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '保存并打印失败'))
  } finally {
    saving.value = false
    saveMode.value = ''
  }
}

function goBack(): void {
  void router.push('/recovery')
}

function reload(): void {
  if (recordId.value !== null) {
    void loadRecord(recordId.value)
    return
  }
  void runCalculate({ apply: true })
}

function retryCalculate(): void {
  void runCalculate({ apply: true })
}

async function loadRecord(id: number): Promise<void> {
  loading.value = true
  loadError.value = ''
  try {
    const record = await getRecoveryRecord(id)
    applyRecord(record)

    try {
      storedSegments.value = await getRecoverySegments(id)
    } catch {
      storedSegments.value = []
    }
    segments.value = storedSegments.value

    await runCalculate({ apply: false })
  } catch (error) {
    loadError.value = getErrorMessage(error, '追缴记录加载失败')
  } finally {
    loading.value = false
  }
}

onMounted(() => {
  if (recordId.value !== null) {
    void loadRecord(recordId.value)
    return
  }

  const person = readStoppedPerson()
  if (!person) return
  applyPerson(person)
  void runCalculate({ apply: true })
})
</script>

<template>
  <div class="nc-page recovery-form-page">
    <NcPageHeader
      :title="pageTitle"
      description="为停止人员补录追缴周期明细"
      kicker="后补追缴 · 搜索录入"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="goBack">返回后补追缴</el-button>
      </template>
    </NcPageHeader>

    <el-alert
      v-if="loadError"
      class="page-alert"
      type="error"
      :title="loadError"
      :closable="false"
      show-icon
    >
      <el-button link type="primary" :icon="Refresh" @click="reload">重新加载</el-button>
    </el-alert>

    <el-alert
      v-if="readonly"
      class="page-alert"
      type="warning"
      :title="`当前记录状态为「${statusText}」，仅草稿可编辑，本页为只读查看。`"
      :closable="false"
      show-icon
    />

    <NcCard v-if="loading" padding="lg">
      <el-skeleton :rows="8" animated />
    </NcCard>

    <NcCard v-else-if="loadError" padding="lg">
      <el-empty description="追缴记录加载失败，无法编辑">
        <el-button @click="goBack">返回后补追缴</el-button>
        <el-button type="primary" :icon="Refresh" @click="reload">重新加载</el-button>
      </el-empty>
    </NcCard>

    <NcCard v-else-if="!personLoaded" padding="lg">
      <el-empty description="未选择追缴人员，请返回列表选择停止人员，或使用手工录入">
        <el-button @click="goBack">返回后补追缴</el-button>
        <el-button type="primary" @click="router.push('/recovery/manual')">手工录入</el-button>
      </el-empty>
    </NcCard>

    <template v-else>
      <!-- 卡片 1：追缴人员 -->
      <NcCard title="追缴人员" subtitle="人员与来源档案由列表选择带入，姓名与身份证只读。">
        <div class="person-head">
          <strong class="person-name">{{ personName || '—' }}</strong>
          <el-tag type="warning" effect="dark">
            {{ sourceDisplay || sourceLabel(sourceType) || '—' }}
          </el-tag>
        </div>
        <p class="person-id mono">{{ idCard || '—' }}</p>

        <div class="field-grid">
          <label class="field">
            <span class="field-label">每月保障标准</span>
            <el-input-number
              v-model="monthlyAmount"
              class="number-input"
              :min="0"
              :precision="2"
              :step="10"
              :controls="false"
              :disabled="readonly"
              placeholder="来源档案带出，可修改"
              @change="handleMonthlyChange"
            />
            <span class="field-hint">
              月保障额自动带出来源档案；历史数据不全时可直接填写应追缴总额并说明原因。
            </span>
          </label>
        </div>
      </NcCard>

      <!-- 卡片 2：追缴明细 -->
      <NcCard title="追缴明细">
        <div class="detail-stack">
          <label class="field">
            <span class="field-label">追缴原因</span>
            <el-input
              v-model="recoveryReason"
              placeholder="请输入追缴原因"
              clearable
              :disabled="readonly"
            />
          </label>

          <div class="period-block">
            <span class="field-label">追缴周期</span>
            <div class="period-row">
              <span class="period-label">起始</span>
              <el-select
                v-model="startYear"
                class="period-select"
                :disabled="readonly"
                aria-label="追缴起始年"
                @change="handlePeriodChange"
              >
                <el-option v-for="year in yearOptions" :key="year" :label="String(year)" :value="year" />
              </el-select>
              <span class="period-unit">年</span>
              <el-select
                v-model="startMonthValue"
                class="period-select"
                :disabled="readonly"
                aria-label="追缴起始月"
                @change="handlePeriodChange"
              >
                <el-option v-for="month in monthOptions" :key="month" :label="String(month)" :value="month" />
              </el-select>
              <span class="period-unit">月</span>
            </div>

            <div class="period-row">
              <span class="period-label">终止</span>
              <el-select
                v-model="endYear"
                class="period-select"
                :disabled="readonly"
                aria-label="追缴终止年"
                @change="handlePeriodChange"
              >
                <el-option v-for="year in yearOptions" :key="year" :label="String(year)" :value="year" />
              </el-select>
              <span class="period-unit">年</span>
              <el-select
                v-model="endMonthValue"
                class="period-select"
                :disabled="readonly"
                aria-label="追缴终止月"
                @change="handlePeriodChange"
              >
                <el-option v-for="month in monthOptions" :key="month" :label="String(month)" :value="month" />
              </el-select>
              <span class="period-unit">月</span>
            </div>
          </div>

          <div class="field">
            <span class="field-label">应追缴总额</span>
            <div class="amount-row">
              <el-input-number
                v-model="recoveryAmount"
                class="number-input amount-input"
                :min="0"
                :precision="2"
                :step="100"
                :controls="false"
                :disabled="readonly"
                placeholder="自动计算或手工填写"
                @change="handleRecoveryAmountChange"
              />
              <span class="amount-badge" :class="`is-${amountSource.toLowerCase()}`">
                {{ amountSourceText }}
              </span>
            </div>
            <p class="field-hint">
              累计 {{ recoveryMonths }} 个月
              <span v-if="calcLoading" class="calc-busy">· 自动计算中…</span>
            </p>
            <p v-if="hasAutoDifference" class="diff-hint">{{ autoReferenceText }}</p>
          </div>

          <el-alert
            v-if="cannotAutoReason"
            type="warning"
            :title="cannotAutoReason"
            :closable="false"
            show-icon
          />

          <el-alert
            v-if="calcError"
            type="error"
            :title="calcError"
            :closable="false"
            show-icon
          >
            <el-button link type="primary" @click="retryCalculate">重算</el-button>
          </el-alert>

          <label v-if="isManualAmount" class="field">
            <span class="field-label">手工填写原因（必填）</span>
            <el-input
              v-model="amountNote"
              placeholder="请说明手工填写原因（如：历史数据不全、按业务核实金额等）"
              clearable
              :disabled="readonly"
            />
          </label>
        </div>

        <div v-if="hasSegments" class="segments">
          <button type="button" class="segments-toggle" @click="toggleSegments">
            <span>自动计算依据（分段明细）</span>
            <span class="segments-action">{{ segmentsExpanded ? '收起' : '展开' }}</span>
          </button>

          <div v-if="segmentsExpanded" class="segments-body">
            <el-table :data="segments" size="small" class="segments-table">
              <el-table-column label="起始月" min-width="100">
                <template #default="{ row }">{{ row.segmentStartMonth || '—' }}</template>
              </el-table-column>
              <el-table-column label="终止月" min-width="100">
                <template #default="{ row }">{{ row.segmentEndMonth || '—' }}</template>
              </el-table-column>
              <el-table-column label="月数" width="70" align="right">
                <template #default="{ row }">{{ row.months }}</template>
              </el-table-column>
              <el-table-column label="月标准" min-width="100" align="right">
                <template #default="{ row }">{{ Number(row.monthlyAmount).toFixed(2) }}</template>
              </el-table-column>
              <el-table-column label="段金额" min-width="110" align="right">
                <template #default="{ row }">{{ Number(row.segmentAmount).toFixed(2) }}</template>
              </el-table-column>
              <el-table-column label="依据" min-width="110">
                <template #default="{ row }">{{ segmentSourceText(row as RecoverySegment) }}</template>
              </el-table-column>
            </el-table>
            <p class="segments-total">{{ segmentsTotalText }}</p>
          </div>
        </div>
      </NcCard>

      <!-- 卡片 3：到账状态 -->
      <NcCard title="到账状态">
        <label class="field field--narrow">
          <span class="field-label">已到账金额</span>
          <el-input-number
            v-model="recoveredAmount"
            class="number-input"
            :min="0"
            :precision="2"
            :step="100"
            :controls="false"
            :disabled="readonly"
            placeholder="请输入已到账金额"
          />
        </label>
      </NcCard>

      <div class="form-footer">
        <el-button :disabled="saving" @click="goBack">取消</el-button>
        <el-button
          :loading="saving && saveMode === 'draft'"
          :disabled="readonly || saving"
          @click="handleSaveDraft"
        >
          保存草稿
        </el-button>
        <el-button
          type="primary"
          :icon="Printer"
          :loading="saving && saveMode === 'print'"
          :disabled="readonly || saving"
          @click="handleSaveAndPrint"
        >
          保存并打印
        </el-button>
      </div>
    </template>

    <RecoveryPrintDialog
      v-model="printVisible"
      :record-id="printRecordId"
      :person-name="printPersonName"
      :source-display="printSourceDisplay"
      :auto-submit="printAutoSubmit"
      action="print"
    />
  </div>
</template>

<style scoped>
.recovery-form-page {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.page-alert {
  margin: 0;
}

.person-head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
}

.person-name {
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-2xl);
  font-weight: 700;
  letter-spacing: 0.04em;
  color: var(--nc-ink);
}

.person-id {
  margin-top: 6px;
  color: var(--nc-muted-2);
  font-size: var(--nc-text-base);
  overflow-wrap: anywhere;
}

.field-grid {
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  gap: 12px 18px;
  margin-top: 16px;
  padding-top: 14px;
  border-top: 1px solid var(--nc-line-faint);
}

.field {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
}

.field--narrow {
  max-width: 360px;
}

.field-label {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-base);
  font-weight: 600;
}

.field-hint {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  line-height: 1.65;
}

.number-input {
  width: 100%;
}

.detail-stack {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.period-block {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.period-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.period-label {
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
}

.period-select {
  width: 110px;
}

.period-unit {
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
}

.amount-row {
  display: flex;
  align-items: center;
  gap: 12px;
}

.amount-input {
  flex: 1 1 auto;
  min-width: 0;
}

.amount-badge {
  flex: none;
  padding: 4px 10px;
  border-radius: var(--nc-radius-pill);
  background-color: var(--nc-paper-3);
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
  font-weight: 600;
}

.amount-badge.is-segment {
  background-color: var(--nc-seal-soft);
  color: var(--nc-seal-dark);
}

.amount-badge.is-flat {
  background-color: var(--nc-amber-soft);
  color: var(--nc-amber-dark);
}

.amount-badge.is-manual {
  background-color: var(--nc-pine-soft);
  color: var(--nc-pine);
}

.calc-busy {
  color: var(--nc-amber-dark);
}

.diff-hint {
  color: var(--nc-amber-dark);
  font-size: var(--nc-text-sm);
}

.segments {
  margin-top: 18px;
  padding-top: 14px;
  border-top: 1px solid var(--nc-line-faint);
}

.segments-toggle {
  display: flex;
  align-items: center;
  justify-content: space-between;
  width: 100%;
  padding: 0;
  border: 0;
  background: transparent;
  color: var(--nc-muted-2);
  font-size: var(--nc-text-base);
  cursor: pointer;
}

.segments-toggle:hover .segments-action {
  color: var(--nc-seal);
}

.segments-action {
  color: var(--nc-seal);
  font-weight: 600;
}

.segments-body {
  margin-top: 10px;
}

.segments-table :deep(.el-table__header th) {
  background-color: var(--nc-paper-3);
  color: var(--nc-muted-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
}

.segments-total {
  margin-top: 10px;
  color: var(--nc-seal-dark);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-base);
  font-weight: 700;
}

.form-footer {
  position: sticky;
  bottom: 0;
  z-index: 5;
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 10px;
  padding: 12px 0;
  border-top: 1px solid var(--nc-line-faint);
  background-color: var(--nc-paper);
}

.mono {
  font-family: var(--nc-font-mono);
}

@media (max-width: 767px) {
  .field-grid {
    grid-template-columns: minmax(0, 1fr);
  }

  .period-select {
    flex: 1 1 90px;
    width: auto;
  }

  .amount-row {
    align-items: stretch;
    flex-direction: column;
  }

  .amount-badge {
    align-self: flex-start;
  }

  .form-footer :deep(.el-button) {
    flex: 1 1 auto;
  }

  .form-footer :deep(.el-button + .el-button) {
    margin-left: 0;
  }
}
</style>
