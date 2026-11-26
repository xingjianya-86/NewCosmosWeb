<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ArrowLeft, Clock, CopyDocument, Download } from '@element-plus/icons-vue'
import {
  describeLotteryError,
  enqueuePredict,
  getJob,
  getLatestDraw,
  getModelStatus,
} from '@/api/lottery'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import type {
  LotteryJob,
  LotteryModelStatus,
  LotteryPrediction,
  LotteryType,
} from '@/types/lottery'
import {
  LOTTERY_JOB_STATUS_LABELS,
  LOTTERY_PREDICTION_COUNTS,
  LOTTERY_TYPE_OPTIONS,
  lotteryTypeLabel,
} from '@/types/lottery'
import {
  copyText,
  downloadFileName,
  downloadTextFile,
  formatDateOnly,
  formatDateTime,
  formatNumberList,
  formatPercent,
  jobLogText,
  jobStatusClass,
  predictionText,
} from './display'

const router = useRouter()

const lotteryType = ref<LotteryType>('SSQ')
const predictionCount = ref(5)
const latestDrawDate = ref('—')
const latestLoading = ref(false)
const latestError = ref('')

const modelStatus = ref<LotteryModelStatus | null>(null)
const modelChecking = ref(false)

const predictions = ref<LotteryPrediction[]>([])
const generatedAt = ref<Date | null>(null)
const generating = ref(false)
const job = ref<LotteryJob | null>(null)
const jobType = ref<LotteryType>('SSQ')
const jobError = ref('')
const saving = ref(false)

let latestTicket = 0
let jobTimer: number | undefined
let jobToken = 0
let jobPollFailures = 0

const algorithmName = computed(
  () => predictions.value[0]?.algorithmName || predictions.value[0]?.algorithm || '机器学习(LSTM)',
)
const jobStatusText = computed(() => {
  if (!job.value) return generating.value ? '正在提交预测任务' : '未开始'
  return LOTTERY_JOB_STATUS_LABELS[job.value.status]
})
const jobProgress = computed(() => job.value?.progress ?? 0)
const jobProgressStatus = computed<'success' | 'exception' | undefined>(() => {
  if (job.value?.status === 'Succeeded') return 'success'
  if (job.value?.status === 'Failed') return 'exception'
  return undefined
})
const jobLog = computed(() => {
  const text = jobLogText(job.value)
  if (text) return text
  if (jobError.value) return jobError.value
  if (generating.value) return '正在等待后端生成预测号码…'
  return '等待任务启动…'
})
const canSave = computed(() => predictions.value.length > 0)
const isPermissionDenied = computed(
  () => latestError.value.includes('LOTTERY_ACCESS') || jobError.value.includes('LOTTERY_ACCESS'),
)
const modelHint = computed(() => {
  if (modelChecking.value) return '正在检查模型状态…'
  if (!modelStatus.value) return '生成预测前会先检查模型状态'
  if (modelStatus.value.ready) {
    return modelStatus.value.trainedAt
      ? `模型已就绪 · 训练于 ${formatDateTime(modelStatus.value.trainedAt)}`
      : '模型已就绪'
  }
  return '模型尚未训练，请先到彩票助手训练模型'
})

function pad(value: number): string {
  return String(value).padStart(2, '0')
}

function stopJobTimer(): void {
  if (jobTimer !== undefined) {
    window.clearTimeout(jobTimer)
    jobTimer = undefined
  }
}

async function loadLatest(): Promise<void> {
  const ticket = ++latestTicket
  latestLoading.value = true
  latestError.value = ''
  try {
    const draw = await getLatestDraw(lotteryType.value)
    if (ticket !== latestTicket) return
    latestDrawDate.value = draw ? formatDateOnly(draw.drawDate) : '—'
  } catch (error) {
    if (ticket !== latestTicket) return
    latestDrawDate.value = '—'
    latestError.value = describeLotteryError(error, '最新开奖日期加载失败')
  } finally {
    if (ticket === latestTicket) latestLoading.value = false
  }
}

async function checkModelReady(): Promise<boolean> {
  modelChecking.value = true
  try {
    const status = await getModelStatus(lotteryType.value)
    modelStatus.value = status
    if (status.ready) return true

    try {
      await ElMessageBox.confirm(
        `尚未检测到${lotteryTypeLabel(lotteryType.value)}的可用于预测的 LSTM 模型。\n请先在彩票助手「训练预测模型」中完成训练，再回来生成预测。`,
        '模型未就绪',
        {
          confirmButtonText: '去训练',
          cancelButtonText: '暂不',
          type: 'warning',
        },
      )
      void router.push('/lottery')
    } catch {
      // 用户选择暂不。
    }
    return false
  } catch (error) {
    modelStatus.value = null
    jobError.value = describeLotteryError(error, '模型状态查询失败')
    ElMessage.error(jobError.value)
    return false
  } finally {
    modelChecking.value = false
  }
}

function applyFinishedJob(value: LotteryJob): void {
  job.value = value
  generating.value = false

  if (value.status === 'Succeeded') {
    if (value.predictions.length === 0) {
      jobError.value = '任务已完成，但未返回预测号码，请稍后重新生成'
      ElMessage.warning(jobError.value)
      return
    }
    predictions.value = value.predictions
    generatedAt.value = new Date()
    jobError.value = ''
    const savedText = value.savedCount > 0 ? `，后端已自动保存 ${value.savedCount} 注` : '，后端已自动入库'
    ElMessage.success(`已生成 ${value.predictions.length} 注预测号码${savedText}`)
    return
  }

  if (value.status === 'Failed') {
    jobError.value = value.error || value.message || '预测任务失败，请稍后重试'
    ElMessage.error(jobError.value)
    return
  }

  if (value.status === 'Cancelled') {
    jobError.value = '预测任务已取消'
    ElMessage.info(jobError.value)
  }
}

function startJobPolling(jobId: string, type: LotteryType): void {
  stopJobTimer()
  const token = ++jobToken
  jobPollFailures = 0

  const tick = async (): Promise<void> => {
    if (token !== jobToken) return
    try {
      const value = await getJob(jobId, type)
      if (token !== jobToken) return
      job.value = value
      jobError.value = ''
      jobPollFailures = 0

      if (
        value.status === 'Succeeded' ||
        value.status === 'Failed' ||
        value.status === 'Cancelled'
      ) {
        applyFinishedJob(value)
        return
      }

      jobTimer = window.setTimeout(() => void tick(), 2500)
    } catch (error) {
      if (token !== jobToken) return
      jobPollFailures += 1
      jobError.value = describeLotteryError(error, '预测任务状态查询失败')
      if (jobPollFailures >= 3) {
        generating.value = false
        ElMessage.error(jobError.value)
        return
      }
      jobTimer = window.setTimeout(() => void tick(), 5000)
    }
  }

  void tick()
}

async function handleGenerate(): Promise<void> {
  if (generating.value) return

  predictions.value = []
  generatedAt.value = null
  job.value = null
  jobError.value = ''
  generating.value = true
  jobType.value = lotteryType.value

  try {
    const ready = await checkModelReady()
    if (!ready) {
      generating.value = false
      return
    }

    const created = await enqueuePredict(lotteryType.value, predictionCount.value)
    if (!created.jobId) {
      throw new Error('服务端未返回任务编号')
    }
    job.value = created

    if (
      created.status === 'Succeeded' ||
      created.status === 'Failed' ||
      created.status === 'Cancelled'
    ) {
      applyFinishedJob(created)
      return
    }

    startJobPolling(created.jobId, lotteryType.value)
  } catch (error) {
    generating.value = false
    jobError.value = describeLotteryError(error, '预测任务提交失败')
    ElMessage.error(jobError.value)
  }
}

async function handleSave(): Promise<void> {
  if (saving.value) return
  if (predictions.value.length === 0) {
    ElMessage.warning('没有可保存的预测结果，请先生成预测')
    return
  }

  saving.value = true
  try {
    const at = generatedAt.value ?? new Date()
    const content = predictionText(
      lotteryTypeLabel(jobType.value),
      at,
      algorithmName.value,
      predictions.value,
    )
    const filename = downloadFileName(jobType.value, at)
    // 先触发下载，避免部分浏览器在异步剪贴板授权后丢失用户激活态。
    downloadTextFile(filename, content)
    const copied = await copyText(content)

    if (copied) {
      ElMessage.success(`已复制到剪贴板，并下载 ${filename}`)
    } else {
      ElMessage.warning(`已下载 ${filename}；浏览器未允许写入剪贴板，请手动复制文件内容`)
    }
  } catch (error) {
    ElMessage.error(describeLotteryError(error, '保存预测结果失败'))
  } finally {
    saving.value = false
  }
}

function resetResults(): void {
  stopJobTimer()
  jobToken += 1
  job.value = null
  jobError.value = ''
  jobPollFailures = 0
  generating.value = false
  predictions.value = []
  generatedAt.value = null
}

watch(lotteryType, () => {
  resetResults()
  modelStatus.value = null
  void loadLatest()
})

onMounted(() => {
  void loadLatest()
})

onBeforeUnmount(() => {
  jobToken += 1
  stopJobTimer()
})
</script>

<template>
  <div class="nc-page prediction-page">
    <NcPageHeader
      title="智能预测"
      description="机器学习（LSTM） · 基于历史开奖序列的双色球 / 大乐透号码预测"
      kicker="彩票助手 · 智能预测"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="router.push('/lottery')">返回彩票助手</el-button>
        <el-button :icon="Clock" :loading="latestLoading" @click="loadLatest()">
          刷新开奖日期
        </el-button>
      </template>
    </NcPageHeader>

    <el-alert
      v-if="latestError"
      class="load-error"
      type="error"
      :title="latestError"
      :closable="false"
      show-icon
    >
      <template v-if="isPermissionDenied">
        <span>请联系管理员分配 LOTTERY_ACCESS 权限后重新进入。</span>
      </template>
      <template v-else>
        <el-button link type="primary" @click="loadLatest()">重新加载</el-button>
      </template>
    </el-alert>

    <NcCard title="预测设置">
      <template #actions>
        <span
          class="model-state"
          :class="{
            'is-ready': modelStatus?.ready,
            'is-pending': !modelStatus,
          }"
        >
          {{ modelStatus?.ready ? '模型已就绪' : modelStatus ? '模型未训练' : '模型状态未知' }}
        </span>
      </template>

      <div class="settings-grid">
        <div class="setting-field">
          <span class="setting-label">彩种</span>
          <el-radio-group v-model="lotteryType" :disabled="generating">
            <el-radio-button
              v-for="option in LOTTERY_TYPE_OPTIONS"
              :key="option.value"
              :value="option.value"
            >
              {{ option.label }}
            </el-radio-button>
          </el-radio-group>
        </div>

        <div class="setting-field">
          <span class="setting-label">注数</span>
          <el-radio-group v-model="predictionCount" :disabled="generating">
            <el-radio-button
              v-for="count in LOTTERY_PREDICTION_COUNTS"
              :key="count"
              :value="count"
            >
              {{ count }}
            </el-radio-button>
          </el-radio-group>
        </div>

        <div class="setting-field setting-field--date">
          <span class="setting-label">最新开奖日期</span>
          <strong class="setting-value">
            {{ latestLoading ? '加载中…' : latestDrawDate }}
          </strong>
        </div>
      </div>

      <p class="model-hint">{{ modelHint }}</p>
    </NcCard>

    <NcCard v-if="generating || job || jobError" title="预测任务" class="job-card">
      <div class="job-status">
        <span class="status-badge" :class="job ? jobStatusClass(job) : 'is-queued'">
          {{ jobStatusText }}
        </span>
        <span class="job-progress-text">
          {{ jobProgress > 0 ? `进度 ${formatPercent(jobProgress)}` : '等待进度上报' }}
        </span>
        <span v-if="job?.jobId" class="job-id">任务 {{ job.jobId }}</span>
      </div>

      <el-progress
        :percentage="jobProgress"
        :status="jobProgressStatus"
        :stroke-width="8"
        class="job-progress"
      />

      <pre class="job-log">{{ jobLog }}</pre>
    </NcCard>

    <el-alert
      v-if="jobError && !job"
      class="job-error"
      type="error"
      :title="jobError"
      :closable="false"
      show-icon
    />

    <section class="result-section" aria-label="预测结果">
      <div class="result-heading">
        <h2 class="section-title"><span class="seal-dash" aria-hidden="true" />预测结果</h2>
        <p class="result-note">策略契合度不代表中奖概率；彩票中奖为随机事件，请理性购彩。</p>
      </div>

      <div v-if="predictions.length > 0" class="prediction-list">
        <article
          v-for="(item, index) in predictions"
          :key="`${item.algorithmName}-${index}`"
          class="prediction-card"
        >
          <header class="prediction-head">
            <div class="prediction-algorithm">
              <strong>{{ item.algorithmName || item.algorithm || '机器学习(LSTM)' }}</strong>
              <small>第 {{ index + 1 }} 注</small>
            </div>
            <span class="confidence">策略契合度 {{ formatPercent(item.confidence) }}</span>
          </header>

          <div class="number-row">
            <span class="number-label">红球</span>
            <div class="ball-row">
              <span
                v-for="number in item.redNumbers"
                :key="`red-${index}-${number}`"
                class="ball ball--red"
              >
                {{ pad(number) }}
              </span>
            </div>
          </div>

          <div class="number-row">
            <span class="number-label">蓝球</span>
            <div class="ball-row">
              <span
                v-for="number in item.blueNumbers"
                :key="`blue-${index}-${number}`"
                class="ball ball--blue"
              >
                {{ pad(number) }}
              </span>
            </div>
          </div>

          <p class="prediction-line">
            {{ formatNumberList(item.redNumbers) }} + {{ formatNumberList(item.blueNumbers) }}
          </p>
        </article>
      </div>

      <el-empty
        v-else
        description="尚未生成预测号码；确认模型就绪后点击「生成预测」"
        :image-size="96"
      />
    </section>

    <div class="action-bar">
      <el-button
        type="primary"
        :loading="generating"
        :disabled="modelChecking"
        @click="handleGenerate"
      >
        {{ generating ? '正在生成预测…' : '生成预测' }}
      </el-button>
      <el-button
        :icon="CopyDocument"
        :loading="saving"
        :disabled="!canSave"
        @click="handleSave"
      >
        保存结果
      </el-button>
      <span class="action-hint">
        <el-icon :size="14"><Download /></el-icon>
        保存会复制到剪贴板并下载 txt 文件
      </span>
    </div>
  </div>
</template>

<style scoped>
.prediction-page {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.load-error {
  margin: 0;
}

.model-state {
  display: inline-flex;
  align-items: center;
  padding: 2px 10px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-pill);
  background-color: var(--nc-paper-3);
  color: var(--nc-muted-2);
  font-size: var(--nc-text-xs);
  font-weight: 700;
  line-height: 1.7;
  white-space: nowrap;
}

.model-state.is-ready {
  border-color: rgba(46, 71, 56, 0.35);
  background-color: var(--nc-pine-soft);
  color: var(--nc-pine);
}

.model-state.is-pending {
  border-color: rgba(150, 104, 28, 0.35);
  background-color: var(--nc-amber-soft);
  color: var(--nc-amber-dark);
}

.settings-grid {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr) minmax(180px, 0.8fr);
  gap: 18px;
}

.setting-field {
  display: flex;
  flex-direction: column;
  gap: 7px;
  min-width: 0;
}

.setting-label {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  font-weight: 600;
}

.setting-value {
  color: var(--nc-ink);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-md);
  line-height: 1.9;
}

.model-hint {
  margin-top: 14px;
  padding-top: 12px;
  border-top: 1px solid var(--nc-line-faint);
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  line-height: 1.7;
}

.job-card {
  border-color: rgba(171, 50, 39, 0.34);
}

.job-status {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
  margin-bottom: 10px;
}

.status-badge {
  display: inline-flex;
  align-items: center;
  padding: 2px 10px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-pill);
  background-color: var(--nc-paper-3);
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
  font-weight: 700;
  line-height: 1.6;
}

.status-badge.is-running {
  border-color: rgba(171, 50, 39, 0.4);
  background-color: var(--nc-seal-soft);
  color: var(--nc-seal-dark);
}

.status-badge.is-queued {
  border-color: rgba(150, 104, 28, 0.35);
  background-color: var(--nc-amber-soft);
  color: var(--nc-amber-dark);
}

.status-badge.is-succeeded {
  border-color: rgba(46, 71, 56, 0.35);
  background-color: var(--nc-pine-soft);
  color: var(--nc-pine);
}

.status-badge.is-failed {
  border-color: rgba(171, 50, 39, 0.4);
  background-color: #fbe9e7;
  color: var(--nc-seal-dark);
}

.job-progress-text,
.job-id {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.job-id {
  font-family: var(--nc-font-mono);
  overflow-wrap: anywhere;
}

.job-progress {
  margin-bottom: 12px;
}

.job-log {
  max-height: 200px;
  margin: 0;
  padding: 12px 14px;
  overflow: auto;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background-color: #211c17;
  color: #e9e1d1;
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  line-height: 1.7;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}

.job-error {
  margin: 0;
}

.result-heading {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  justify-content: space-between;
  gap: 8px 18px;
  margin-bottom: 12px;
}

.section-title {
  display: flex;
  align-items: center;
  gap: 10px;
  margin: 4px 0 0;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: 19px;
  font-weight: 700;
  line-height: 1.4;
  letter-spacing: 0.02em;
}

.seal-dash {
  flex: none;
  width: 14px;
  height: 2px;
  background-color: var(--nc-seal);
}

.result-note {
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
  line-height: 1.7;
}

.prediction-list {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.prediction-card {
  min-width: 0;
  padding: 16px 18px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-2);
  box-shadow: var(--nc-shadow-xs);
}

.prediction-head {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-start;
  justify-content: space-between;
  gap: 8px;
  margin-bottom: 12px;
  padding-bottom: 10px;
  border-bottom: 1px solid var(--nc-line-faint);
}

.prediction-algorithm {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.prediction-algorithm strong {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
}

.prediction-algorithm small {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.confidence {
  color: var(--nc-amber-dark);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  font-weight: 600;
  white-space: nowrap;
}

.number-row {
  display: flex;
  align-items: center;
  gap: 12px;
  min-width: 0;
}

.number-row + .number-row {
  margin-top: 9px;
}

.number-label {
  flex: none;
  width: 34px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  font-weight: 600;
}

.ball-row {
  display: flex;
  flex-wrap: wrap;
  gap: 7px;
}

.ball {
  display: inline-grid;
  place-items: center;
  width: 34px;
  height: 34px;
  border: 1px solid transparent;
  border-radius: 50%;
  color: #fff;
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-base);
  font-weight: 700;
  line-height: 1;
}

.ball--red {
  border-color: #b71c1c;
  background-color: #e53935;
}

.ball--blue {
  border-color: #1565c0;
  background-color: #1e88e5;
}

.prediction-line {
  margin-top: 12px;
  color: var(--nc-muted-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  overflow-wrap: anywhere;
}

.action-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
  padding: 14px 16px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-2);
  box-shadow: var(--nc-shadow-xs);
}

.action-hint {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

@media (max-width: 1023px) {
  .settings-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .setting-field--date {
    grid-column: span 2;
  }

  .prediction-list {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 767px) {
  .prediction-page {
    gap: 14px;
  }

  .settings-grid {
    grid-template-columns: 1fr;
    gap: 14px;
  }

  .setting-field--date {
    grid-column: auto;
  }

  .ball {
    width: 32px;
    height: 32px;
    font-size: var(--nc-text-sm);
  }

  .action-bar {
    position: sticky;
    bottom: 0;
    z-index: 5;
  }

  .action-bar :deep(.el-button) {
    flex: 1 1 auto;
  }

  .action-hint {
    flex-basis: 100%;
  }

  .job-log {
    max-height: 260px;
  }
}
</style>
