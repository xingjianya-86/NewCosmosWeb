<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { Refresh } from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import { getAssetTask } from '@/api/assetVerification'
import type { AssetTaskDetail } from '@/types/assetVerification'
import { useMediaQuery } from '@/composables/useMediaQuery'
import AssetStatusTag from './AssetStatusTag.vue'
import { formatDate, formatDateTime, textOrDash } from './format'

const props = withDefaults(
  defineProps<{
    modelValue: boolean
    taskId: number | null
  }>(),
  {
    taskId: null,
  },
)

const emit = defineEmits<{ 'update:modelValue': [value: boolean] }>()

const narrow = useMediaQuery('(max-width: 720px)')
const detail = ref<AssetTaskDetail | null>(null)
const loading = ref(false)
const loadError = ref('')

let requestTicket = 0

const basicFields = computed<Array<{ label: string; value: string }>>(() => {
  const task = detail.value
  if (!task) return []

  return [
    { label: '批次号', value: textOrDash(task.batchId) },
    { label: '申请人', value: textOrDash(task.applicantName) },
    { label: '身份证号', value: textOrDash(task.applicantIdCard) },
    { label: '证件类型', value: textOrDash(task.applicantIdType) },
    { label: '与户主关系', value: textOrDash(task.relationship) },
    { label: '是否户主', value: task.isHead ? '是' : '否' },
    { label: '户主身份证号', value: textOrDash(task.headIdCard) },
    { label: '联系电话', value: textOrDash(task.contactPhone) },
    { label: '所属社区', value: textOrDash(task.community) },
    { label: '家庭住址', value: textOrDash(task.familyAddress) },
    { label: '申请日期', value: formatDate(task.applicationDate) },
    { label: '当前状态', value: textOrDash(task.statusText) },
    { label: '创建时间', value: formatDateTime(task.createdAt) },
    { label: '更新时间', value: formatDateTime(task.updatedAt) },
  ]
})

async function load(taskId: number): Promise<void> {
  const ticket = ++requestTicket
  loading.value = true
  loadError.value = ''

  try {
    const response = await getAssetTask(taskId)
    if (ticket !== requestTicket) return
    detail.value = response
  } catch (error) {
    if (ticket !== requestTicket) return
    detail.value = null
    loadError.value = getErrorMessage(error, '核对详情加载失败')
  } finally {
    if (ticket === requestTicket) loading.value = false
  }
}

function handleVisibleChange(value: boolean): void {
  emit('update:modelValue', value)
}

watch(
  () => [props.modelValue, props.taskId] as const,
  ([visible, taskId]) => {
    if (!visible || taskId === null) return
    detail.value = null
    void load(taskId)
  },
)
</script>

<template>
  <el-drawer
    :model-value="modelValue"
    :size="narrow ? '94%' : '580px'"
    append-to-body
    @update:model-value="handleVisibleChange"
  >
    <template #header>
      <div class="drawer-head">
        <div>
          <p class="drawer-kicker">ASSET VERIFICATION / 核对详情</p>
          <h2 class="drawer-title">{{ detail?.applicantName || '核对详情' }}</h2>
        </div>
        <AssetStatusTag
          v-if="detail"
          :status="detail.status"
          :status-text="detail.statusText"
        />
      </div>
    </template>

    <div v-if="loading" class="drawer-loading">
      <el-skeleton :rows="8" animated />
    </div>

    <el-alert
      v-else-if="loadError"
      type="error"
      :title="loadError"
      :closable="false"
      show-icon
    >
      <el-button
        v-if="taskId !== null"
        link
        type="primary"
        :icon="Refresh"
        @click="load(taskId)"
      >
        重新加载
      </el-button>
    </el-alert>

    <div v-else-if="detail" class="detail-body">
      <section class="detail-section">
        <h3 class="section-title">申请信息</h3>
        <dl class="detail-grid">
          <div v-for="field in basicFields" :key="field.label" class="detail-item">
            <dt>{{ field.label }}</dt>
            <dd>{{ field.value }}</dd>
          </div>
          <div class="detail-item detail-item--wide">
            <dt>申请事由</dt>
            <dd class="reason">{{ textOrDash(detail.applicationReason) }}</dd>
          </div>
        </dl>
      </section>

      <section class="detail-section">
        <h3 class="section-title">同批次家庭成员</h3>

        <el-table
          v-if="detail.members.length > 0"
          :data="detail.members"
          size="small"
          class="member-table"
        >
          <el-table-column prop="applicantName" label="姓名" min-width="90">
            <template #default="{ row }">{{ textOrDash(row.applicantName) }}</template>
          </el-table-column>
          <el-table-column prop="idCard" label="身份证号" min-width="164">
            <template #default="{ row }">
              <span class="mono">{{ textOrDash(row.idCard) }}</span>
            </template>
          </el-table-column>
          <el-table-column label="与户主关系" min-width="96">
            <template #default="{ row }">{{ textOrDash(row.relationship) }}</template>
          </el-table-column>
          <el-table-column label="户主" width="64" align="center">
            <template #default="{ row }">{{ row.isHead ? '是' : '—' }}</template>
          </el-table-column>
        </el-table>

        <p v-else class="member-empty">暂无同批次家庭成员。</p>
      </section>
    </div>

    <div v-else class="drawer-empty">
      <el-empty description="未找到核对详情" />
    </div>
  </el-drawer>
</template>

<style scoped>
.drawer-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  min-width: 0;
}

.drawer-kicker {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  font-weight: 600;
  letter-spacing: 0.16em;
}

.drawer-title {
  margin-top: 4px;
  overflow: hidden;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-xl);
  font-weight: 700;
  letter-spacing: 0.03em;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.drawer-loading {
  padding: 8px 2px;
}

.detail-body {
  display: flex;
  flex-direction: column;
  gap: 22px;
  padding-bottom: 12px;
}

.detail-section {
  min-width: 0;
}

.section-title {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 12px;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  font-weight: 700;
  letter-spacing: 0.03em;
}

.section-title::before {
  content: '';
  flex: none;
  width: 3px;
  height: 13px;
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-seal);
}

.detail-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 1px;
  overflow: hidden;
  margin: 0;
  border: 1px solid var(--nc-line-faint);
  background-color: var(--nc-line-faint);
}

.detail-item {
  min-width: 0;
  padding: 10px 12px;
  background-color: var(--nc-paper-2);
}

.detail-item--wide {
  grid-column: 1 / -1;
}

.detail-item dt {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  letter-spacing: 0.08em;
}

.detail-item dd {
  margin: 3px 0 0;
  overflow-wrap: anywhere;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  line-height: 1.55;
}

.detail-item dd.reason {
  white-space: pre-wrap;
}

.member-table {
  width: 100%;
}

.member-empty {
  padding: 18px 4px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  text-align: center;
}

.drawer-empty {
  padding: 24px 0;
}

.mono {
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  white-space: nowrap;
}

@media (max-width: 720px) {
  .detail-grid {
    grid-template-columns: minmax(0, 1fr);
  }

  .drawer-title {
    font-size: var(--nc-text-lg);
  }
}
</style>
