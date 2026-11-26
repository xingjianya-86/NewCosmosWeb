<script setup lang="ts">
import { computed, h, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  ArrowLeft,
  ArrowRight,
  Calendar,
  DataAnalysis,
  MagicStick,
  Refresh,
  Timer,
  Trophy,
} from '@element-plus/icons-vue'
import type { Component } from 'vue'
import {
  cancelWorkerJob,
  describeLotteryError,
  enqueueTrain,
  getDrawCount,
  getJob,
  getLatestDraw,
  quickPick,
  syncHistory,
} from '@/api/lottery'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStat from '@/components/NcStat.vue'
import type { LotteryDraw, LotteryJob, LotteryPrediction, LotteryType } from '@/types/lottery'
import {
  LOTTERY_JOB_STATUS_LABELS,
  LOTTERY_TYPE_OPTIONS,
  lotteryTypeLabel,
} from '@/types/lottery'
import {
  formatCount,
  formatDateOnly,
  formatNumberList,
  formatPercent,
  jobLogText,
  jobStatusClass,
  verificationLines,
} from './display'

interface FeatureCard {
  key: string
  title: string
  description: string
  icon: Component
  action: () => void
  busy?: boolean
}

const router = useRouter()

const lotteryType = ref<LotteryType>('SSQ')
const latestDraw = ref<LotteryDraw | null>(null)
const drawCount = ref(0)
const loading = ref(false)
const loadError = ref('')
let loadTicket = 0

const syncing = ref(false)
const picking = ref(false)
const quickPickVisible = ref(false)
const quickPickItem = ref<LotteryPrediction | null>(null)
const quickPickType = ref<LotteryType>('SSQ')

const trainJob = ref<LotteryJob | null>(null)
const trainType = ref<LotteryType>('SSQ')
const trainStarting = ref(false)
const trainTracking = ref(false)
const trainCancelled = ref(false)
const trainCancelling = ref(false)
const trainPollError = ref('')
let trainTimer: number | undefined
let trainToken = 0
let trainPollFailures = 0

const currentLabel = computed(() => lotteryTypeLabel(lotteryType.value))
const latestDrawNumber = computed(() => latestDraw.value?.drawNumber || '—')
const trainTypeLabel = computed(() => lotteryTypeLabel(trainType.value))
const trainActive = computed(() => {
  if (!trainTracking.value) return false
  const status = trainJob.value?.status
  return status !== 'Succeeded' && status !== 'Failed' && status !== 'Cancelled'
})
const trainStatusText = computed(() => {
  if (trainCancelled.value) return '已停止跟踪'
  if (trainStarting.value) return '正在提交任务'
  if (!trainJob.value) return '未开始'
  return LOTTERY_JOB_STATUS_LABELS[trainJob.value.status]
})
const trainProgress = computed(() => trainJob.value?.progress ?? 0)
const trainProgressStatus = computed<'success' | 'exception' | undefined>(() => {
  if (trainJob.value?.status === 'Succeeded') return 'success'
  if (trainJob.value?.status === 'Failed') return 'exception'
  return undefined
})
const trainLog = computed(() => {
  if (trainPollError.value) return trainPollError.value
  const text = jobLogText(trainJob.value)
  if (text) return text
  return trainStarting.value ? '正在提交训练任务…' : '等待后端返回训练日志…'
})
const isPermissionDenied = computed(() => loadError.value.includes('LOTTERY_ACCESS'))

const featureCards = computed<FeatureCard[]>(() => [
  {
    key: 'sync',
    title: '同步历史数据',
    description: '从官方接口增量下载双色球 / 大乐透开奖数据',
    icon: Refresh,
    action: handleSync,
    busy: syncing.value,
  },
  {
    key: 'quick',
    title: '快速机选',
    description: '随机生成一组推荐号码',
    icon: MagicStick,
    action: handleQuickPick,
    busy: picking.value,
  },
  {
    key: 'predict',
    title: '智能预测',
    description: '机器学习（LSTM）预测双色球 / 大乐透',
    icon: DataAnalysis,
    action: () => router.push('/lottery/prediction'),
  },
  {
    key: 'history',
    title: '历史记录',
    description: '查看往期开奖结果与统计数据',
    icon: Calendar,
    action: () => router.push('/lottery/history'),
  },
  {
    key: 'train',
    title: '训练预测模型',
    description: '训练 LSTM 模型（全量历史，约 10-40 分钟）',
    icon: Timer,
    action: handleTrain,
    busy: trainStarting.value || trainActive.value,
  },
  {
    key: 'result',
    title: '中奖结果',
    description: '查看购彩命中、奖级与奖金明细',
    icon: Trophy,
    action: () => router.push('/lottery/result'),
  },
])

function pad(value: number): string {
  return String(value).padStart(2, '0')
}

async function load(): Promise<void> {
  const ticket = ++loadTicket
  loading.value = true
  loadError.value = ''

  try {
    const [draw, count] = await Promise.all([
      getLatestDraw(lotteryType.value),
      getDrawCount(lotteryType.value),
    ])
    if (ticket !== loadTicket) return
    latestDraw.value = draw
    drawCount.value = count
  } catch (error) {
    if (ticket !== loadTicket) return
    latestDraw.value = null
    drawCount.value = 0
    loadError.value = describeLotteryError(error, '彩票数据加载失败')
  } finally {
    if (ticket === loadTicket) loading.value = false
  }
}

function stopTrainTimer(): void {
  if (trainTimer !== undefined) {
    window.clearTimeout(trainTimer)
    trainTimer = undefined
  }
}

function finishTraining(job: LotteryJob): void {
  trainTracking.value = false
  trainStarting.value = false
  trainPollError.value = ''
  if (job.status === 'Succeeded') {
    ElMessage.success(`${trainTypeLabel.value} LSTM 模型训练完成`)
  } else if (job.status === 'Failed') {
    ElMessage.error(job.error || job.message || '模型训练失败')
  } else if (job.status === 'Cancelled') {
    ElMessage.info('训练任务已取消')
  }
}

function startTrainPolling(jobId: string, type: LotteryType): void {
  stopTrainTimer()
  const token = ++trainToken
  trainPollFailures = 0
  trainTracking.value = true
  trainStarting.value = false

  const tick = async (): Promise<void> => {
    if (token !== trainToken) return
    try {
      const job = await getJob(jobId, type)
      if (token !== trainToken) return
      trainJob.value = job
      trainPollError.value = ''
      trainPollFailures = 0

      if (job.status === 'Succeeded' || job.status === 'Failed' || job.status === 'Cancelled') {
        finishTraining(job)
        return
      }

      // 训练可能耗时数十分钟，3 秒轮询不会给服务端造成明显压力。
      trainTimer = window.setTimeout(() => void tick(), 3000)
    } catch (error) {
      if (token !== trainToken) return
      trainPollFailures += 1
      trainPollError.value = describeLotteryError(error, '训练状态查询失败')
      if (trainPollFailures >= 3) {
        trainTracking.value = false
        trainStarting.value = false
        ElMessage.error(trainPollError.value)
        return
      }
      trainTimer = window.setTimeout(() => void tick(), 5000)
    }
  }

  void tick()
}

async function handleTrain(): Promise<void> {
  if (trainStarting.value || trainActive.value) {
    ElMessage.info('训练任务正在进行中，请等待任务完成')
    return
  }

  try {
    await ElMessageBox.confirm(
      `将训练${currentLabel.value}的 LSTM 模型，使用全量历史数据，预计 10-40 分钟。\n\n训练可长时间运行，期间可以离开本页，但请保持浏览器打开。是否开始？`,
      '训练预测模型',
      {
        confirmButtonText: '开始训练',
        cancelButtonText: '取消',
        type: 'warning',
      },
    )
  } catch {
    return
  }

  trainStarting.value = true
  trainCancelled.value = false
  trainPollError.value = ''
  trainJob.value = null
  trainType.value = lotteryType.value

  try {
    const job = await enqueueTrain(lotteryType.value)
    if (!job.jobId) {
      throw new Error('服务端未返回任务编号')
    }
    trainJob.value = job
    if (job.status === 'Succeeded' || job.status === 'Failed' || job.status === 'Cancelled') {
      finishTraining(job)
      return
    }
    startTrainPolling(job.jobId, lotteryType.value)
  } catch (error) {
    trainStarting.value = false
    trainTracking.value = false
    trainPollError.value = describeLotteryError(error, '训练任务提交失败')
    await ElMessageBox.alert(trainPollError.value, '训练启动失败', {
      confirmButtonText: '知道了',
    })
  }
}

function cancelTraining(): void {
  if (!trainTracking.value) return
  stopTrainTimer()
  trainToken += 1
  trainTracking.value = false
  trainStarting.value = false
  trainCancelled.value = true
  ElMessage.info('已停止跟踪训练任务；后台任务可能仍在继续运行')
}

async function cancelTrainingTask(): Promise<void> {
  const job = trainJob.value
  if (!job?.jobId || trainCancelling.value) return

  try {
    await ElMessageBox.confirm(
      '将请求 Worker 取消当前训练任务。已完成的训练进度可能丢失，是否继续？',
      '取消训练任务',
      { confirmButtonText: '取消任务', cancelButtonText: '继续训练', type: 'warning' },
    )
  } catch {
    return
  }

  trainCancelling.value = true
  try {
    await cancelWorkerJob(job.jobId)
    ElMessage.success('已提交取消请求，Worker 会在安全点停止任务')
    const value = await getJob(job.jobId, trainType.value)
    trainJob.value = value
    if (value.status === 'Cancelled' || value.status === 'Failed' || value.status === 'Succeeded') {
      finishTraining(value)
    }
  } catch (error) {
    ElMessage.error(describeLotteryError(error, '取消训练任务失败'))
  } finally {
    trainCancelling.value = false
  }
}

async function handleSync(): Promise<void> {
  if (syncing.value) return
  syncing.value = true
  try {
    const result = await syncHistory(lotteryType.value)
    await load()

    const lines: string[] = []
    lines.push(
      result.newCount > 0
        ? `新增 ${result.newCount} 期开奖数据`
        : '已是最新，无新增开奖数据',
    )
    if (result.verification) {
      lines.push('', ...verificationLines(result.verification))
    }

    await ElMessageBox.alert(
      h('div', { style: 'white-space: pre-line; line-height: 1.8' }, lines.join('\n')),
      '同步完成',
      { confirmButtonText: '知道了' },
    )
  } catch (error) {
    await ElMessageBox.alert(describeLotteryError(error, '同步数据失败'), '同步失败', {
      confirmButtonText: '知道了',
    })
  } finally {
    syncing.value = false
  }
}

async function handleQuickPick(): Promise<void> {
  if (picking.value) return
  picking.value = true
  try {
    const items = await quickPick(lotteryType.value, 1)
    const item = items[0]
    if (!item) {
      ElMessage.warning('本次未返回机选号码，请稍后重试')
      return
    }
    quickPickItem.value = item
    quickPickType.value = lotteryType.value
    quickPickVisible.value = true
  } catch (error) {
    ElMessage.error(describeLotteryError(error, '机选失败'))
  } finally {
    picking.value = false
  }
}

watch(lotteryType, () => {
  latestDraw.value = null
  drawCount.value = 0
  void load()
})

onMounted(() => {
  void load()
})

onBeforeUnmount(() => {
  trainToken += 1
  stopTrainTimer()
})
</script>

<template>
  <div class="nc-page lottery-home">
    <NcPageHeader
      title="彩票助手"
      description="双色球 · 大乐透 · 历史数据 · 智能预测"
      kicker="彩票工具"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="router.push('/')">返回工作台</el-button>
        <el-button
          type="primary"
          :icon="Refresh"
          :loading="syncing"
          :disabled="loading"
          @click="handleSync"
        >
          同步历史
        </el-button>
        <el-button :icon="Refresh" :loading="loading" @click="load()">刷新</el-button>
      </template>
    </NcPageHeader>

    <div class="stats-grid">
      <NcStat
        label="最新开奖期号"
        :value="latestDrawNumber"
        tone="seal"
        compact
        :class="{ 'is-loading': loading }"
      />
      <NcStat
        label="历史数据（期）"
        :value="formatCount(drawCount)"
        tone="ink"
        compact
        :class="{ 'is-loading': loading }"
      />
    </div>

    <el-alert
      v-if="loadError"
      class="load-error"
      type="error"
      :title="loadError"
      :closable="false"
      show-icon
    >
      <template v-if="isPermissionDenied">
        <span>请联系管理员分配 LOTTERY_ACCESS 权限后重新进入。</span>
      </template>
      <template v-else>
        <el-button link type="primary" @click="load()">重新加载</el-button>
      </template>
    </el-alert>

    <NcCard class="latest-card" :title="`最新开奖 · ${currentLabel}`">
      <template #actions>
        <el-radio-group v-model="lotteryType" size="small" :disabled="syncing">
          <el-radio-button
            v-for="option in LOTTERY_TYPE_OPTIONS"
            :key="option.value"
            :value="option.value"
          >
            {{ option.label }}
          </el-radio-button>
        </el-radio-group>
      </template>

      <div v-loading="loading" class="latest-body">
        <template v-if="latestDraw">
          <header class="latest-head">
            <div>
              <p class="latest-eyebrow">开奖号码</p>
              <strong class="latest-period">{{ latestDraw.drawNumber }}</strong>
            </div>
            <span class="latest-date">{{ formatDateOnly(latestDraw.drawDate) }}</span>
          </header>

          <div class="number-row">
            <span class="number-label">红球</span>
            <div class="ball-row">
              <span
                v-for="number in latestDraw.redNumbers"
                :key="`red-${number}`"
                class="ball ball--red"
              >
                {{ pad(number) }}
              </span>
              <span v-if="latestDraw.redNumbers.length === 0" class="number-empty">—</span>
            </div>
          </div>

          <div class="number-row">
            <span class="number-label">蓝球</span>
            <div class="ball-row">
              <span
                v-for="number in latestDraw.blueNumbers"
                :key="`blue-${number}`"
                class="ball ball--blue"
              >
                {{ pad(number) }}
              </span>
              <span v-if="latestDraw.blueNumbers.length === 0" class="number-empty">—</span>
            </div>
          </div>

          <p v-if="latestDraw.prizeDescription" class="latest-prize">
            {{ latestDraw.prizeDescription }}
          </p>
        </template>

        <el-empty
          v-else-if="!loading && !loadError"
          description="暂无开奖数据，请先同步历史数据"
          :image-size="86"
        />
      </div>
    </NcCard>

    <section class="feature-section" aria-label="彩票功能入口">
      <h2 class="section-title"><span class="seal-dash" aria-hidden="true" />功能入口</h2>

      <div class="feature-grid">
        <article
          v-for="card in featureCards"
          :key="card.key"
          class="feature-card"
          :class="{ 'is-busy': card.busy }"
          role="button"
          tabindex="0"
          @click="card.action()"
          @keydown.enter="card.action()"
        >
          <header class="feature-head">
            <span class="feature-icon">
              <el-icon :size="22"><component :is="card.icon" /></el-icon>
            </span>
            <span class="feature-heading">
              <strong class="feature-title">{{ card.title }}</strong>
              <small class="feature-desc">
                {{ card.busy ? '处理中，请稍候…' : card.description }}
              </small>
            </span>
          </header>
          <span class="feature-foot">
            {{ card.busy ? '处理中' : '进入' }}
            <el-icon :size="14"><ArrowRight /></el-icon>
          </span>
        </article>
      </div>
    </section>

    <NcCard
      v-if="trainStarting || trainJob || trainCancelled"
      title="训练预测模型"
      :subtitle="`${trainTypeLabel} · 全量历史训练，任务可能持续数十分钟`"
      class="training-card"
    >
      <template #actions>
        <el-button
          v-if="trainActive && trainJob?.jobId"
          size="small"
          type="danger"
          plain
          :loading="trainCancelling"
          @click="cancelTrainingTask"
        >
          取消任务
        </el-button>
        <el-button v-if="trainActive" size="small" @click="cancelTraining">
          取消跟踪
        </el-button>
      </template>

      <div class="training-status">
        <span
          class="status-badge"
          :class="trainJob ? jobStatusClass(trainJob) : 'is-queued'"
        >
          {{ trainStatusText }}
        </span>
        <span class="training-progress-text">
          {{ trainProgress > 0 ? `进度 ${formatPercent(trainProgress)}` : '等待进度上报' }}
        </span>
        <span v-if="trainJob?.jobId" class="training-job">任务 {{ trainJob.jobId }}</span>
      </div>

      <el-progress
        :percentage="trainProgress"
        :status="trainProgressStatus"
        :stroke-width="8"
        class="training-progress"
      />

      <pre class="training-log">{{ trainLog }}</pre>

      <p class="training-hint">
        训练使用全量历史数据，约 10-40 分钟；关闭页面不会中断服务端任务，但会停止状态跟踪。
      </p>
    </NcCard>

    <el-dialog v-model="quickPickVisible" title="机选结果" width="min(92vw, 420px)">
      <div v-if="quickPickItem" class="quick-pick">
        <p class="quick-pick-type">{{ lotteryTypeLabel(quickPickType) }} · 随机一注</p>

        <div class="number-row">
          <span class="number-label">红球</span>
          <div class="ball-row">
            <span
              v-for="number in quickPickItem.redNumbers"
              :key="`pick-red-${number}`"
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
              v-for="number in quickPickItem.blueNumbers"
              :key="`pick-blue-${number}`"
              class="ball ball--blue"
            >
              {{ pad(number) }}
            </span>
          </div>
        </div>

        <dl class="quick-pick-meta">
          <div>
            <dt>算法</dt>
            <dd>{{ quickPickItem.algorithmName || quickPickItem.algorithm || '随机机选' }}</dd>
          </div>
          <div>
            <dt>策略契合度</dt>
            <dd>{{ formatPercent(quickPickItem.confidence) }}</dd>
          </div>
        </dl>

        <p class="quick-pick-numbers">
          {{ formatNumberList(quickPickItem.redNumbers) }} +
          {{ formatNumberList(quickPickItem.blueNumbers) }}
        </p>
      </div>

      <template #footer>
        <el-button :icon="MagicStick" :loading="picking" @click="handleQuickPick">
          再选一组
        </el-button>
        <el-button type="primary" @click="quickPickVisible = false">知道了</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.lottery-home {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.stats-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.stats-grid :deep(.nc-stat.is-loading .nc-stat__value) {
  opacity: 0.55;
}

.load-error {
  margin: 0;
}

.latest-card {
  overflow: hidden;
}

.latest-body {
  min-height: 128px;
}

.latest-head {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-start;
  justify-content: space-between;
  gap: 10px;
  margin-bottom: 18px;
}

.latest-eyebrow {
  margin-bottom: 4px;
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  letter-spacing: 0.12em;
}

.latest-period {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: 26px;
  font-weight: 700;
  line-height: 1.2;
  letter-spacing: 0.04em;
}

.latest-date {
  padding-top: 4px;
  color: var(--nc-muted-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
}

.number-row {
  display: flex;
  align-items: center;
  gap: 12px;
  min-width: 0;
}

.number-row + .number-row {
  margin-top: 10px;
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

.number-empty {
  color: var(--nc-muted);
}

.latest-prize {
  margin-top: 16px;
  padding-top: 12px;
  border-top: 1px solid var(--nc-line-faint);
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
  line-height: 1.75;
  white-space: pre-line;
  overflow-wrap: anywhere;
}

.section-title {
  display: flex;
  align-items: center;
  gap: 10px;
  margin: 4px 0 12px;
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

.feature-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 14px;
}

.feature-card {
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-width: 0;
  padding: 18px 20px;
  border: 1px solid var(--nc-line);
  border-radius: 2px;
  background-color: #f8f3e9;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.06);
  cursor: pointer;
  transition:
    border-color var(--nc-transition),
    box-shadow var(--nc-transition),
    transform var(--nc-transition);
}

.feature-card:hover {
  border-color: var(--nc-line-strong);
  box-shadow: 0 6px 16px rgba(23, 19, 16, 0.12);
  transform: translateY(-1px);
}

.feature-card:focus-visible {
  outline: 2px solid var(--nc-seal);
  outline-offset: 1px;
}

.feature-card.is-busy {
  cursor: progress;
  opacity: 0.72;
}

.feature-head {
  display: flex;
  align-items: center;
  gap: 12px;
  min-width: 0;
}

.feature-icon {
  display: grid;
  flex: none;
  place-items: center;
  width: 42px;
  height: 42px;
  border-radius: 12px;
  background-color: #ebf5ff;
  color: var(--nc-seal);
}

.feature-heading {
  display: flex;
  flex-direction: column;
  gap: 3px;
  min-width: 0;
}

.feature-title {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: 16px;
  font-weight: 700;
  line-height: 1.4;
  letter-spacing: 0.02em;
}

.feature-desc {
  color: var(--nc-muted-2);
  font-size: 13px;
  line-height: 1.55;
}

.feature-foot {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 5px;
  color: var(--nc-seal);
  font-size: 13px;
  font-weight: 600;
}

.training-card {
  border-color: rgba(171, 50, 39, 0.34);
}

.training-status {
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

.training-progress-text,
.training-job {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.training-job {
  font-family: var(--nc-font-mono);
  overflow-wrap: anywhere;
}

.training-progress {
  margin-bottom: 12px;
}

.training-log {
  max-height: 220px;
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

.training-hint {
  margin-top: 10px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  line-height: 1.7;
}

.quick-pick-type {
  margin-bottom: 14px;
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
}

.quick-pick-meta {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
  margin: 16px 0 0;
  padding-top: 12px;
  border-top: 1px solid var(--nc-line-faint);
}

.quick-pick-meta dt {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.quick-pick-meta dd {
  margin: 2px 0 0;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
}

.quick-pick-numbers {
  margin-top: 14px;
  color: var(--nc-muted-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  overflow-wrap: anywhere;
}

@media (max-width: 1023px) {
  .feature-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 767px) {
  .lottery-home {
    gap: 14px;
  }

  .stats-grid {
    gap: 8px;
  }

  .feature-grid {
    grid-template-columns: 1fr;
  }

  .feature-card {
    padding: 15px 16px;
  }

  .ball {
    width: 32px;
    height: 32px;
    font-size: var(--nc-text-sm);
  }

  .latest-period {
    font-size: 23px;
  }

  .training-log {
    max-height: 260px;
  }
}

@media (max-width: 420px) {
  .latest-head {
    flex-direction: column;
  }
}
</style>
