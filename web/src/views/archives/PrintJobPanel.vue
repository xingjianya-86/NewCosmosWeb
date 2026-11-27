<script setup lang="ts">
import { computed } from 'vue'
import { Delete, RefreshRight, Refresh, WarningFilled } from '@element-plus/icons-vue'
import NcStatusDot from '@/components/NcStatusDot.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import { formatDateTime } from '@/views/applications/format'
import { describePrintJobError } from '@/api/archivePrint'
import type { PrintJobListItem } from '@/api/archivePrint'

const props = defineProps<{
  jobs: PrintJobListItem[]
  loading: boolean
  error: string
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
  busyId: number | null
}>()

const emit = defineEmits<{
  refresh: []
  retry: [job: PrintJobListItem]
  remove: [job: PrintJobListItem]
  pageChange: [page: number]
}>()

const isNarrow = useMediaQuery('(max-width: 1023px)')
const showFooter = computed(() => props.totalCount > props.pageSize)

function statusText(job: PrintJobListItem): string {
  if (job.status === 'Pending') return job.mode === 'preview' ? '待预览' : '待打印'
  const labels: Record<string, string> = {
    Processing: '处理中',
    Completed: '已完成',
    Failed: '失败',
    Cancelled: '已取消',
  }
  return labels[job.status] ?? job.status
}

function statusDot(job: PrintJobListItem): 'online' | 'offline' | 'default' | 'idle' {
  if (job.status === 'Completed') return 'online'
  if (job.status === 'Processing') return 'default'
  if (job.status === 'Failed') return 'offline'
  return 'idle'
}

function modeText(job: PrintJobListItem): string {
  return job.mode === 'preview' ? '预览' : '打印'
}

function duplexText(job: PrintJobListItem): string {
  return job.isDuplex ? '双面' : '单面'
}

function timeText(job: PrintJobListItem): string {
  return formatDateTime(job.completedAt || job.createdAt)
}

function applicantText(job: PrintJobListItem): string {
  if (job.applicantName) return job.applicantName
  return job.businessId ? `申请 #${job.businessId}` : '—'
}

function printerText(job: PrintJobListItem): string {
  if (job.printerName) return job.printerName
  return job.mode === 'preview' ? 'PC 端预览' : '默认打印机'
}

/**
 * 「错误信息」列展示口径：LocalOp 的完成消息是 `RESULT:` 结构化结果（不是错误），
 * 按完成消息渲染；其余（含 LocalOp 失败摘要）仍按错误渲染。见 archivePrint.describePrintJobError。
 */
function jobError(job: PrintJobListItem): { text: string; isError: boolean } {
  return describePrintJobError(job)
}
</script>

<template>
  <div class="job-panel">
    <el-alert
      v-if="error"
      class="panel-error"
      type="error"
      :title="error"
      :closable="false"
      show-icon
    >
      <el-button link type="primary" :icon="Refresh" @click="emit('refresh')">重新加载</el-button>
    </el-alert>

    <el-skeleton v-if="loading && jobs.length === 0" :rows="4" animated />

    <template v-else-if="jobs.length > 0">
      <!-- 桌面端表格 -->
      <el-table v-if="!isNarrow" :data="jobs" row-key="id" class="job-table">
        <el-table-column label="任务 / 文书" min-width="190">
          <template #default="{ row }">
            <div class="job-title">
              <strong>{{ row.templateName }}</strong>
              <span class="job-no">{{ row.jobNo }}</span>
            </div>
          </template>
        </el-table-column>

        <el-table-column label="申请" min-width="100">
          <template #default="{ row }">
            <span class="job-applicant">{{ applicantText(row as PrintJobListItem) }}</span>
          </template>
        </el-table-column>

        <el-table-column label="打印机" min-width="150">
          <template #default="{ row }">
            <span class="job-printer">{{ printerText(row as PrintJobListItem) }}</span>
          </template>
        </el-table-column>

        <el-table-column label="方式" width="76" align="center">
          <template #default="{ row }">
            <span class="mode-chip" :class="`mode-chip--${row.mode}`">{{ modeText(row as PrintJobListItem) }}</span>
          </template>
        </el-table-column>

        <el-table-column label="份数" width="92" align="center">
          <template #default="{ row }">
            <span class="job-copies">{{ row.copies }} 份 · {{ duplexText(row as PrintJobListItem) }}</span>
          </template>
        </el-table-column>

        <el-table-column label="状态" width="104">
          <template #default="{ row }">
            <span class="job-status" :class="{ 'is-failed': row.status === 'Failed' }">
              <NcStatusDot :status="statusDot(row as PrintJobListItem)" :label="statusText(row as PrintJobListItem)" />
            </span>
          </template>
        </el-table-column>

        <el-table-column label="时间" width="150">
          <template #default="{ row }">{{ timeText(row as PrintJobListItem) }}</template>
        </el-table-column>

        <el-table-column label="错误信息" min-width="150">
          <template #default="{ row }">
            <el-tooltip
              v-if="jobError(row as PrintJobListItem).isError"
              :content="jobError(row as PrintJobListItem).text"
              placement="top"
              effect="dark"
            >
              <span class="job-error">
                <el-icon><WarningFilled /></el-icon>
                {{ jobError(row as PrintJobListItem).text }}
              </span>
            </el-tooltip>
            <span v-else-if="jobError(row as PrintJobListItem).text" class="job-done">{{
              jobError(row as PrintJobListItem).text
            }}</span>
            <span v-else class="nc-muted">—</span>
          </template>
        </el-table-column>

        <el-table-column label="操作" width="132" align="right">
          <template #default="{ row }">
            <el-button
              v-if="row.status === 'Failed'"
              link
              type="primary"
              :icon="RefreshRight"
              :loading="busyId === row.id"
              @click="emit('retry', row as PrintJobListItem)"
            >
              重试
            </el-button>
            <el-button
              link
              type="danger"
              :icon="Delete"
              :disabled="row.status === 'Processing'"
              :loading="busyId === row.id"
              @click="emit('remove', row as PrintJobListItem)"
            >
              删除
            </el-button>
          </template>
        </el-table-column>
      </el-table>

      <!-- 窄屏卡片 -->
      <div v-else class="job-cards">
        <article v-for="job in jobs" :key="job.id" class="job-card">
          <header class="job-card__head">
            <div class="job-title">
              <strong>{{ job.templateName }}</strong>
              <span class="job-no">{{ job.jobNo }}</span>
            </div>
            <span class="job-status" :class="{ 'is-failed': job.status === 'Failed' }">
              <NcStatusDot :status="statusDot(job)" :label="statusText(job)" />
            </span>
          </header>

          <div class="job-card__chips">
            <span class="mode-chip" :class="`mode-chip--${job.mode}`">{{ modeText(job) }}</span>
            <span class="job-chip">{{ job.copies }} 份 · {{ duplexText(job) }}</span>
            <span class="job-chip">{{ applicantText(job) }}</span>
          </div>

          <dl class="job-card__meta">
            <div>
              <dt>打印机</dt>
              <dd>{{ printerText(job) }}</dd>
            </div>
            <div>
              <dt>时间</dt>
              <dd>{{ timeText(job) }}</dd>
            </div>
          </dl>

          <p
            v-if="jobError(job).text"
            :class="jobError(job).isError ? 'job-card__error' : 'job-card__done'"
          >
            <el-icon v-if="jobError(job).isError"><WarningFilled /></el-icon>
            <span>{{ jobError(job).text }}</span>
          </p>

          <footer class="job-card__actions">
            <el-button
              v-if="job.status === 'Failed'"
              size="small"
              :icon="RefreshRight"
              :loading="busyId === job.id"
              @click="emit('retry', job)"
            >
              重试
            </el-button>
            <el-button
              size="small"
              type="danger"
              plain
              :icon="Delete"
              :disabled="job.status === 'Processing'"
              :loading="busyId === job.id"
              @click="emit('remove', job)"
            >
              删除
            </el-button>
          </footer>
        </article>
      </div>
    </template>

    <el-empty
      v-else-if="!loading && !error"
      :image-size="80"
      description="暂无打印任务"
    >
      <p class="empty-tip">选择申请与文书后，点击「预览」或「打印」即可推送到 PC 端。</p>
    </el-empty>

    <footer v-if="showFooter" class="job-footer">
      <span class="job-total">共 {{ totalCount }} 条 · 第 {{ page }}/{{ totalPages }} 页</span>
      <el-pagination
        :current-page="page"
        :page-size="pageSize"
        :total="totalCount"
        layout="prev, pager, next"
        :pager-count="5"
        small
        background
        @current-change="emit('pageChange', $event)"
      />
    </footer>
  </div>
</template>

<style scoped>
.job-panel {
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-height: 120px;
}

.panel-error {
  margin: 0;
}

.job-table {
  width: 100%;
}

.job-title {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.job-title strong {
  overflow: hidden;
  color: var(--nc-ink);
  font-size: var(--nc-text-sm);
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.job-no {
  overflow: hidden;
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 11px;
  letter-spacing: 0.02em;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.job-applicant,
.job-printer,
.job-copies {
  display: block;
  overflow: hidden;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.mode-chip {
  display: inline-flex;
  align-items: center;
  padding: 1px 7px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-3);
  color: var(--nc-ink-2);
  font-size: 11px;
  line-height: 1.6;
  white-space: nowrap;
}

.mode-chip--preview {
  border-color: rgba(49, 87, 110, 0.24);
  background-color: #e8f0f7;
  color: #31576e;
}

.mode-chip--print {
  border-color: rgba(171, 50, 39, 0.22);
  background-color: var(--nc-seal-soft);
  color: var(--nc-seal-dark);
}

.job-status {
  display: inline-flex;
}

.job-status.is-failed :deep(.nc-status) {
  --status-tone: var(--nc-seal);
}

.job-status.is-failed :deep(.nc-status__dot) {
  background-color: var(--nc-seal);
  box-shadow: 0 0 0 3px rgba(171, 50, 39, 0.14);
}

.job-error {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  max-width: 100%;
  overflow: hidden;
  color: var(--nc-seal);
  font-size: 11px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.job-error .el-icon {
  flex: none;
}

/* LocalOp 完成消息（RESULT: 结构化结果）：按普通完成信息渲染，不用错误色。 */
.job-done {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  max-width: 100%;
  overflow: hidden;
  color: var(--nc-muted);
  font-size: 11px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.job-cards {
  display: grid;
  gap: 9px;
}

.job-card {
  padding: 12px 13px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-2);
}

.job-card__head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 10px;
}

.job-card__chips {
  display: flex;
  flex-wrap: wrap;
  gap: 5px;
  margin-top: 8px;
}

.job-chip {
  display: inline-flex;
  align-items: center;
  padding: 1px 7px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-xs);
  color: var(--nc-muted);
  font-size: 11px;
  line-height: 1.6;
  white-space: nowrap;
}

.job-card__meta {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 7px 12px;
  margin-top: 9px;
}

.job-card__meta > div {
  display: flex;
  flex-direction: column;
  gap: 1px;
  min-width: 0;
}

.job-card__meta dt {
  color: var(--nc-muted);
  font-size: 11px;
}

.job-card__meta dd {
  overflow: hidden;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.job-card__error {
  display: flex;
  align-items: flex-start;
  gap: 5px;
  margin-top: 8px;
  padding: 6px 8px;
  border-left: 2px solid var(--nc-seal);
  background-color: var(--nc-seal-wash);
  color: var(--nc-seal-dark);
  font-size: 11px;
  line-height: 1.6;
  word-break: break-word;
}

.job-card__error .el-icon {
  flex: none;
  margin-top: 2px;
}

/* LocalOp 完成消息（卡片态）：普通完成信息，不用错误色。 */
.job-card__done {
  margin-top: 8px;
  padding: 6px 8px;
  border-left: 2px solid var(--nc-line);
  background-color: var(--nc-paper-3);
  color: var(--nc-muted);
  font-size: 11px;
  line-height: 1.6;
  word-break: break-word;
}

.job-card__actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  margin-top: 10px;
  padding-top: 9px;
  border-top: 1px solid var(--nc-line-faint);
}

.job-card__actions :deep(.el-button + .el-button) {
  margin-left: 0;
}

.empty-tip {
  margin-top: 4px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.job-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  margin-top: 12px;
  padding-top: 10px;
  border-top: 1px solid var(--nc-line-faint);
}

.job-total {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  white-space: nowrap;
}

@media (max-width: 767px) {
  .job-card__meta {
    grid-template-columns: minmax(0, 1fr);
  }

  .job-footer {
    flex-direction: column;
    align-items: stretch;
  }
}
</style>
