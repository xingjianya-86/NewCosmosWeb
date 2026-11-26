<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { CircleClose, Refresh } from '@element-plus/icons-vue'
import { cancelDataCenterJob, getDataCenterJob } from '@/api/dataCenter'
import { getErrorMessage } from '@/api/http'
import type { DataCenterJob } from '@/types/dataCenter'

const props = withDefaults(
  defineProps<{
    modelValue: boolean
    jobId: string | null
    title?: string
  }>(),
  { title: '任务进度' },
)

const emit = defineEmits<{
  (event: 'update:modelValue', value: boolean): void
  (event: 'finished', job: DataCenterJob): void
}>()

const visible = computed({
  get: () => props.modelValue,
  set: (value: boolean) => emit('update:modelValue', value),
})

const job = ref<DataCenterJob | null>(null)
const loading = ref(false)
const error = ref('')
let timer: number | undefined
let stopped = false

const running = computed(
  () => job.value?.state === 'pending' || job.value?.state === 'running',
)
const stateText = computed(() => {
  switch (job.value?.state) {
    case 'pending':
      return '等待执行'
    case 'running':
      return '执行中'
    case 'succeeded':
      return '已完成'
    case 'failed':
      return '失败'
    case 'cancelled':
      return '已取消'
    default:
      return ''
  }
})
const stateTone = computed(() => {
  switch (job.value?.state) {
    case 'succeeded':
      return 'success'
    case 'failed':
      return 'danger'
    case 'cancelled':
      return 'warning'
    default:
      return 'primary'
  }
})

function clearTimer(): void {
  if (timer !== undefined) {
    window.clearInterval(timer)
    timer = undefined
  }
}

async function poll(): Promise<void> {
  if (!props.jobId || stopped) return
  try {
    const data = await getDataCenterJob(props.jobId)
    job.value = data
    error.value = ''
    if (data.state === 'succeeded' || data.state === 'failed' || data.state === 'cancelled') {
      stopped = true
      clearTimer()
      emit('finished', data)
    }
  } catch (err) {
    error.value = getErrorMessage(err, '读取任务进度失败')
  }
}

function start(): void {
  clearTimer()
  stopped = false
  job.value = null
  error.value = ''
  if (!props.jobId) return
  loading.value = true
  void poll().finally(() => {
    loading.value = false
  })
  timer = window.setInterval(() => void poll(), 1000)
}

watch(
  () => [props.modelValue, props.jobId] as const,
  ([open]) => {
    if (open && props.jobId) start()
    else clearTimer()
  },
  { immediate: true },
)

onBeforeUnmount(clearTimer)

async function handleCancel(): Promise<void> {
  if (!props.jobId) return
  try {
    await ElMessageBox.confirm('确定取消当前任务吗？已写入的数据可能保留。', '取消任务', {
      confirmButtonText: '取消任务',
      cancelButtonText: '继续等待',
      type: 'warning',
    })
  } catch {
    return
  }

  try {
    await cancelDataCenterJob(props.jobId)
    ElMessage.success('已发送取消请求')
  } catch (err) {
    ElMessage.error(getErrorMessage(err, '取消失败'))
  }
}

function handleBeforeClose(done: () => void): void {
  if (running.value) {
    ElMessage.warning('任务仍在执行，请等待完成或先取消任务')
    return
  }
  done()
}
</script>

<template>
  <el-dialog
    v-model="visible"
    :title="props.title || job?.title || '任务进度'"
    width="min(760px, 94vw)"
    :close-on-click-modal="false"
    :before-close="handleBeforeClose"
    append-to-body
  >
    <div class="job-dialog">
      <div v-if="job" class="job-head">
        <el-tag :type="stateTone" effect="plain">{{ stateText }}</el-tag>
        <span class="job-title">{{ job.title }}</span>
        <span class="job-percent">{{ job.percent }}%</span>
      </div>

      <el-progress
        v-if="job"
        :percentage="job.percent"
        :status="job.state === 'failed' ? 'exception' : job.state === 'succeeded' ? 'success' : undefined"
        :stroke-width="10"
      />

      <p v-if="job" class="job-message">{{ job.message }}</p>
      <p v-if="job?.error" class="job-error">{{ job.error }}</p>
      <p v-if="error" class="job-error">{{ error }}</p>

      <div v-if="job?.logs?.length" class="job-logs">
        <pre>{{ job.logs.join('\n') }}</pre>
      </div>

      <p v-if="running" class="job-hint">任务在服务端执行，请保持页面打开；刷新后可用任务号重新查询。</p>
    </div>

    <template #footer>
      <div class="job-footer">
        <el-button v-if="running" :icon="CircleClose" @click="handleCancel">取消任务</el-button>
        <el-button :icon="Refresh" :loading="loading" @click="poll">刷新</el-button>
        <el-button :disabled="running" type="primary" @click="visible = false">关闭</el-button>
      </div>
    </template>
  </el-dialog>
</template>

<style scoped>
.job-dialog {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.job-head {
  display: flex;
  align-items: center;
  gap: 10px;
}

.job-title {
  font-weight: 600;
  color: var(--nc-ink);
}

.job-percent {
  margin-left: auto;
  font-family: var(--nc-font-mono);
  color: var(--nc-muted);
}

.job-message {
  margin: 0;
  color: var(--nc-ink);
  line-height: 1.7;
}

.job-error {
  margin: 0;
  color: var(--nc-danger, #b42318);
  line-height: 1.7;
  white-space: pre-wrap;
}

.job-logs {
  max-height: 240px;
  overflow: auto;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-xs);
  background: var(--nc-paper-2);
  padding: 10px 12px;
}

.job-logs pre {
  margin: 0;
  font-family: var(--nc-font-mono);
  font-size: 12px;
  line-height: 1.7;
  color: var(--nc-muted-2);
  white-space: pre-wrap;
  word-break: break-all;
}

.job-hint {
  margin: 0;
  font-size: var(--nc-text-xs);
  color: var(--nc-muted);
}

.job-footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}
</style>
