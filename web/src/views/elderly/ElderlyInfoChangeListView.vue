<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { ArrowLeft, Check, Close, Refresh, Search, View } from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import {
  approveInfoChange,
  listInfoChanges,
  listTaskInfoChanges,
  rejectInfoChange,
} from '@/api/elderlyInfoChange'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStat from '@/components/NcStat.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import type {
  InfoChangeApproveRequest,
  InfoChangeRecord,
} from '@/types/elderlyInfoChange'
import ElderlyChip from './ElderlyChip.vue'
import { formatDateTime, formatMonth, formatShortDateTime } from './display'
import {
  detailRisk,
  detailRiskText,
  infoChangeFieldLabel,
  infoChangeRiskText,
  infoChangeRiskTone,
  infoChangeSourceText,
  infoChangeStatusText,
  infoChangeStatusTone,
  nextMonthValue,
  recordFieldLabels,
  recordInvolvesBank,
  recordRisk,
} from './infoChangeDisplay'

const router = useRouter()
const isNarrow = useMediaQuery('(max-width: 960px)')

const STATUS_TABS = [
  { label: '待审批', value: 'Pending' },
  { label: '全部', value: '' },
]

/* ── 列表 ───────────────────────────────────────────────────────── */

const keyword = ref('')
const status = ref('Pending')
const page = ref(1)
const pageSize = ref(20)
const items = ref<InfoChangeRecord[]>([])
const totalCount = ref(0)
const totalPages = ref(0)
const loading = ref(false)
const loadError = ref('')

let requestTicket = 0

const hasFilters = computed(() => keyword.value.trim().length > 0 || status.value !== '')
const displayTotalPages = computed(() => Math.max(totalPages.value, 1))
const pendingCount = computed<number | null>(() =>
  status.value === 'Pending' ? totalCount.value : null,
)
const paginationLayout = computed(() =>
  isNarrow.value ? 'prev, pager, next' : 'total, sizes, prev, pager, next, jumper',
)

const listSubtitle = computed(() => {
  if (loadError.value) return '数据加载失败'
  if (loading.value && items.value.length === 0) return '正在读取信息变更单…'
  const suffix = hasFilters.value ? ' · 已应用筛选' : ''
  return `共 ${totalCount.value} 条变更单${suffix}`
})

const emptyDescription = computed(() =>
  status.value === 'Pending'
    ? '暂无待审批的信息变更单'
    : hasFilters.value
      ? '没有找到匹配的信息变更单'
      : '暂无信息变更记录',
)

async function load(): Promise<void> {
  const ticket = ++requestTicket
  loading.value = true
  loadError.value = ''

  try {
    const response = await listInfoChanges({
      page: page.value,
      pageSize: pageSize.value,
      keyword: keyword.value.trim() || undefined,
      status: status.value || undefined,
    })
    if (ticket !== requestTicket) return

    items.value = response.items ?? []
    page.value = response.pageIndex || page.value
    pageSize.value = response.pageSize || pageSize.value
    totalCount.value = response.totalCount ?? 0
    totalPages.value = response.totalPages ?? 0
  } catch (error) {
    if (ticket !== requestTicket) return
    items.value = []
    totalCount.value = 0
    totalPages.value = 0
    loadError.value = getErrorMessage(error, '信息变更列表加载失败')
  } finally {
    if (ticket === requestTicket) loading.value = false
  }
}

function handleSearch(): void {
  page.value = 1
  void load()
}

function handleStatusChange(): void {
  page.value = 1
  void load()
}

function handleReset(): void {
  keyword.value = ''
  status.value = ''
  page.value = 1
  void load()
}

function handlePageChange(value: number): void {
  page.value = value
  void load()
}

function handleSizeChange(value: number): void {
  pageSize.value = value
  page.value = 1
  void load()
}

function goBack(): void {
  void router.push('/elderly/certifications')
}

/* ── 详情 ───────────────────────────────────────────────────────── */

const detailVisible = ref(false)
const detailLoading = ref(false)
const activeRecord = ref<InfoChangeRecord | null>(null)

function riskOf(record: InfoChangeRecord | null): string {
  const text = (record?.changeTypeText ?? '').trim()
  if (text) return text
  return infoChangeRiskText(recordRisk(record))
}

/** 列表若未带明细，按 taskId 拉一次任务变更单补齐。 */
async function ensureDetails(record: InfoChangeRecord): Promise<InfoChangeRecord> {
  if (record.details.length > 0 || record.taskId === null) return record
  try {
    const list = await listTaskInfoChanges(record.taskId)
    return list.find((item) => item.id === record.id) ?? record
  } catch {
    return record
  }
}

async function openDetail(record: InfoChangeRecord): Promise<void> {
  activeRecord.value = record
  detailVisible.value = true
  if (record.details.length === 0 && record.taskId !== null) {
    detailLoading.value = true
    try {
      activeRecord.value = await ensureDetails(record)
    } finally {
      detailLoading.value = false
    }
  }
}

/* ── 通过 ───────────────────────────────────────────────────────── */

const approveVisible = ref(false)
const approveTarget = ref<InfoChangeRecord | null>(null)
const approving = ref(false)
const approveForm = reactive({
  reviewOpinion: '',
  effectiveMonth: '',
})

async function startApprove(record: InfoChangeRecord): Promise<void> {
  const full = await ensureDetails(record)
  detailVisible.value = false
  approveTarget.value = full
  approveForm.reviewOpinion = ''
  approveForm.effectiveMonth = recordInvolvesBank(full) ? nextMonthValue() : ''
  approveVisible.value = true
}

async function submitApprove(): Promise<void> {
  const record = approveTarget.value
  if (!record) return

  const involvesBank = recordInvolvesBank(record)
  if (involvesBank && !approveForm.effectiveMonth) {
    ElMessage.warning('银行卡 / 账户变更请选择生效月份')
    return
  }

  approving.value = true
  try {
    const request: InfoChangeApproveRequest = {
      reviewOpinion: approveForm.reviewOpinion.trim(),
    }
    if (involvesBank) request.effectiveMonth = approveForm.effectiveMonth

    await approveInfoChange(record.id, request)
    approveVisible.value = false
    ElMessage.success('信息变更已通过并写入申请档案')
    await load()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '审批通过失败'))
  } finally {
    approving.value = false
  }
}

/* ── 驳回 ───────────────────────────────────────────────────────── */

const rejectVisible = ref(false)
const rejectTarget = ref<InfoChangeRecord | null>(null)
const rejecting = ref(false)
const rejectForm = reactive({ reviewOpinion: '' })

async function startReject(record: InfoChangeRecord): Promise<void> {
  const full = await ensureDetails(record)
  detailVisible.value = false
  rejectTarget.value = full
  rejectForm.reviewOpinion = ''
  rejectVisible.value = true
}

async function submitReject(): Promise<void> {
  const record = rejectTarget.value
  if (!record) return

  const opinion = rejectForm.reviewOpinion.trim()
  if (!opinion) {
    ElMessage.warning('请填写驳回原因')
    return
  }

  rejecting.value = true
  try {
    await rejectInfoChange(record.id, { reviewOpinion: opinion })
    rejectVisible.value = false
    ElMessage.success('信息变更已驳回')
    await load()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '驳回失败'))
  } finally {
    rejecting.value = false
  }
}

onMounted(() => {
  void load()
})
</script>

<template>
  <div class="nc-page info-change-list-page">
    <NcPageHeader
      title="信息变更审批"
      description="认证过程中发现电话、地址、社保卡、代办人变化时先落变更单；审批通过后才写入高龄申请档案并保留旧值。"
      kicker="高龄津贴 · 年度认证"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="goBack">返回认证</el-button>
        <el-button :icon="Refresh" :loading="loading" @click="load()">刷新</el-button>
      </template>
    </NcPageHeader>

    <div class="summary">
      <NcStat
        label="待审批"
        :value="pendingCount === null ? '—' : pendingCount"
        tone="amber"
        compact
      />
      <NcStat label="本页记录" :value="items.length" tone="pine" compact />
      <NcStat label="当前页" :value="`${page} / ${displayTotalPages}`" tone="seal" compact />
    </div>

    <NcCard padding="none" class="filter-card">
      <div class="status-tabs" role="tablist" aria-label="按审批状态筛选">
        <el-radio-group v-model="status" @change="handleStatusChange">
          <el-radio-button
            v-for="tab in STATUS_TABS"
            :key="tab.value || 'all'"
            :value="tab.value"
          >
            {{ tab.label }}
          </el-radio-button>
        </el-radio-group>
      </div>

      <form class="filter-bar" @submit.prevent="handleSearch">
        <el-input
          v-model="keyword"
          class="filter-search"
          clearable
          :prefix-icon="Search"
          placeholder="搜索姓名 / 身份证号 / 变更单号"
          @clear="handleSearch"
        />
        <div class="filter-actions">
          <el-button type="primary" :icon="Search" native-type="submit" :loading="loading">
            查询
          </el-button>
          <el-button v-if="hasFilters" text native-type="button" @click="handleReset">
            重置
          </el-button>
        </div>
      </form>
    </NcCard>

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

    <NcCard title="变更单" :subtitle="listSubtitle" padding="none" class="list-card">
      <div
        v-loading="loading && items.length > 0"
        class="list-area"
        :class="{ 'is-cards': isNarrow }"
      >
        <el-skeleton
          v-if="loading && items.length === 0"
          :rows="5"
          animated
          class="list-skeleton"
        />

        <template v-else-if="items.length > 0">
          <el-table v-if="!isNarrow" :data="items" row-key="id" class="change-table">
            <el-table-column label="变更单号" min-width="150">
              <template #default="{ row }">
                <span class="mono">{{ row.changeNo || '—' }}</span>
              </template>
            </el-table-column>

            <el-table-column label="姓名 / 身份证" min-width="170">
              <template #default="{ row }">
                <div class="primary-cell">
                  <strong>{{ row.name }}</strong>
                  <small class="mono">{{ row.idCard || '—' }}</small>
                </div>
              </template>
            </el-table-column>

            <el-table-column label="变更字段" min-width="210">
              <template #default="{ row }">
                <span class="field-summary" :title="recordFieldLabels(row as InfoChangeRecord).join('、')">
                  {{ recordFieldLabels(row as InfoChangeRecord).join('、') || '—' }}
                </span>
              </template>
            </el-table-column>

            <el-table-column label="风险" width="90">
              <template #default="{ row }">
                <ElderlyChip
                  :text="riskOf(row as InfoChangeRecord)"
                  :tone="infoChangeRiskTone(String(recordRisk(row as InfoChangeRecord)))"
                />
              </template>
            </el-table-column>

            <el-table-column label="状态" width="92">
              <template #default="{ row }">
                <ElderlyChip
                  :text="infoChangeStatusText(row.status, row.statusText)"
                  :tone="infoChangeStatusTone(row.status)"
                />
              </template>
            </el-table-column>

            <el-table-column label="申请时间" width="104">
              <template #default="{ row }">
                {{ formatShortDateTime(row.createdAt || row.appliedAt) }}
              </template>
            </el-table-column>

            <el-table-column label="操作" width="226" align="right">
              <template #default="{ row }">
                <div class="row-actions">
                  <el-button
                    link
                    type="primary"
                    size="small"
                    :icon="View"
                    @click="openDetail(row as InfoChangeRecord)"
                  >
                    查看
                  </el-button>
                  <template v-if="row.status === 'Pending'">
                    <el-button
                      link
                      type="success"
                      size="small"
                      :icon="Check"
                      @click="startApprove(row as InfoChangeRecord)"
                    >
                      通过
                    </el-button>
                    <el-button
                      link
                      type="danger"
                      size="small"
                      :icon="Close"
                      @click="startReject(row as InfoChangeRecord)"
                    >
                      驳回
                    </el-button>
                  </template>
                </div>
              </template>
            </el-table-column>
          </el-table>

          <div v-else class="change-cards">
            <article v-for="row in items" :key="row.id" class="change-card">
              <header class="card-head">
                <div>
                  <span class="mono change-no">{{ row.changeNo || '—' }}</span>
                  <strong>{{ row.name }}</strong>
                  <small class="mono">{{ row.idCard || '—' }}</small>
                </div>
                <ElderlyChip
                  :text="infoChangeStatusText(row.status, row.statusText)"
                  :tone="infoChangeStatusTone(row.status)"
                />
              </header>

              <div class="card-chips">
                <ElderlyChip
                  :text="riskOf(row)"
                  :tone="infoChangeRiskTone(String(recordRisk(row)))"
                />
                <span class="card-fields">
                  {{ recordFieldLabels(row).join('、') || '—' }}
                </span>
              </div>

              <div class="card-meta-grid">
                <div class="span-2">
                  <span>变更原因</span>
                  <strong>{{ row.reason || '—' }}</strong>
                </div>
                <div>
                  <span>申请时间</span>
                  <strong>{{ formatDateTime(row.createdAt || row.appliedAt) }}</strong>
                </div>
                <div>
                  <span>生效月</span>
                  <strong>{{ formatMonth(row.effectiveMonth) }}</strong>
                </div>
              </div>

              <div class="card-actions">
                <el-button size="small" :icon="View" @click="openDetail(row)">查看</el-button>
                <template v-if="row.status === 'Pending'">
                  <el-button
                    size="small"
                    type="primary"
                    :icon="Check"
                    @click="startApprove(row)"
                  >
                    通过
                  </el-button>
                  <el-button
                    size="small"
                    type="danger"
                    :icon="Close"
                    @click="startReject(row)"
                  >
                    驳回
                  </el-button>
                </template>
              </div>
            </article>
          </div>
        </template>

        <div v-else-if="!loading && !loadError" class="empty-wrap">
          <el-empty :description="emptyDescription">
            <el-button v-if="hasFilters" :icon="Refresh" @click="handleReset">
              清除筛选
            </el-button>
          </el-empty>
        </div>
      </div>

      <template #footer>
        <div v-if="items.length > 0" class="pagination">
          <span class="pagination-note">
            第 {{ page }} / {{ displayTotalPages }} 页 · 共 {{ totalCount }} 条
          </span>
          <el-pagination
            :current-page="page"
            :page-size="pageSize"
            :page-sizes="[10, 20, 50]"
            :total="totalCount"
            :layout="paginationLayout"
            :pager-count="isNarrow ? 5 : 7"
            :small="isNarrow"
            background
            @current-change="handlePageChange"
            @size-change="handleSizeChange"
          />
        </div>
      </template>
    </NcCard>

    <!-- 详情 -->
    <el-drawer
      v-model="detailVisible"
      title="变更单详情"
      :size="isNarrow ? '100%' : '640px'"
    >
      <div v-loading="detailLoading" class="detail-body">
        <template v-if="activeRecord">
          <section class="detail-section">
            <div class="detail-person">
              <div>
                <h3>{{ activeRecord.name }}</h3>
                <span class="mono">{{ activeRecord.idCard || '—' }}</span>
              </div>
              <div class="detail-chips">
                <ElderlyChip
                  :text="infoChangeStatusText(activeRecord.status, activeRecord.statusText)"
                  :tone="infoChangeStatusTone(activeRecord.status)"
                />
                <ElderlyChip
                  :text="riskOf(activeRecord)"
                  :tone="infoChangeRiskTone(String(recordRisk(activeRecord)))"
                />
              </div>
            </div>

            <dl class="detail-grid">
              <div>
                <dt>变更单号</dt>
                <dd class="mono">{{ activeRecord.changeNo || '—' }}</dd>
              </div>
              <div>
                <dt>来源</dt>
                <dd>
                  {{ infoChangeSourceText(activeRecord.sourceType, activeRecord.sourceTypeText) }}
                </dd>
              </div>
              <div class="detail-wide">
                <dt>变更原因</dt>
                <dd>{{ activeRecord.reason || '—' }}</dd>
              </div>
              <div>
                <dt>生效月</dt>
                <dd>{{ formatMonth(activeRecord.effectiveMonth) }}</dd>
              </div>
              <div>
                <dt>申请</dt>
                <dd>
                  {{ activeRecord.createdBy || activeRecord.appliedBy || '—' }} ·
                  {{ formatDateTime(activeRecord.createdAt || activeRecord.appliedAt) }}
                </dd>
              </div>
              <div>
                <dt>审批</dt>
                <dd>
                  {{ activeRecord.reviewedBy || '—' }} ·
                  {{ formatDateTime(activeRecord.reviewedAt) }}
                </dd>
              </div>
              <div>
                <dt>审批意见</dt>
                <dd>{{ activeRecord.reviewOpinion || '—' }}</dd>
              </div>
            </dl>
          </section>

          <section class="detail-section">
            <h3>字段明细（{{ activeRecord.details.length }}）</h3>

            <div v-if="activeRecord.details.length > 0" class="change-list">
              <div
                v-for="detail in activeRecord.details"
                :key="detail.id"
                class="change-row"
              >
                <div class="change-field">
                  <strong>{{ infoChangeFieldLabel(detail.fieldKey, detail.fieldLabel) }}</strong>
                  <ElderlyChip
                    :text="detailRiskText(detail)"
                    :tone="infoChangeRiskTone(detailRisk(detail))"
                  />
                </div>
                <div class="change-values">
                  <div class="change-value">
                    <span>原值</span>
                    <p>{{ detail.oldValue || '—' }}</p>
                  </div>
                  <span class="change-arrow" aria-hidden="true">→</span>
                  <div class="change-value is-new">
                    <span>新值</span>
                    <p>{{ detail.newValue || '—' }}</p>
                  </div>
                </div>
              </div>
            </div>
            <el-empty v-else :image-size="64" description="没有字段明细" />
          </section>
        </template>
      </div>

      <template #footer>
        <div v-if="activeRecord && activeRecord.status === 'Pending'" class="detail-actions">
          <el-button type="danger" plain :icon="Close" @click="startReject(activeRecord)">
            驳回
          </el-button>
          <el-button type="primary" :icon="Check" @click="startApprove(activeRecord)">
            通过
          </el-button>
        </div>
      </template>
    </el-drawer>

    <!-- 通过 -->
    <el-dialog v-model="approveVisible" title="通过信息变更" width="min(500px, 92vw)">
      <div v-if="approveTarget" class="review-dialog">
        <div class="review-target">
          <strong>{{ approveTarget.name }}</strong>
          <span class="mono">{{ approveTarget.changeNo || '—' }}</span>
        </div>

        <div class="review-fields">
          <ElderlyChip
            v-for="label in recordFieldLabels(approveTarget)"
            :key="label"
            :text="label"
          />
        </div>

        <el-alert
          v-if="recordInvolvesBank(approveTarget)"
          class="review-alert"
          type="warning"
          :closable="false"
          show-icon
          title="社保卡 / 账户变更默认下月生效，可按实际调整生效月份。"
        />
        <el-alert
          v-else
          class="review-alert"
          type="info"
          :closable="false"
          show-icon
          title="电话、地址等低风险变更审批通过后立即生效。"
        />

        <el-form label-position="top">
          <el-form-item label="审批意见">
            <el-input
              v-model="approveForm.reviewOpinion"
              type="textarea"
              :rows="2"
              maxlength="500"
              show-word-limit
              placeholder="如：已核对新卡照片"
            />
          </el-form-item>
          <el-form-item v-if="recordInvolvesBank(approveTarget)" label="银行卡生效月">
            <el-date-picker
              v-model="approveForm.effectiveMonth"
              type="month"
              value-format="YYYY-MM"
              placeholder="选择生效月份"
              style="width: 100%"
            />
          </el-form-item>
        </el-form>
      </div>
      <template #footer>
        <el-button @click="approveVisible = false">取消</el-button>
        <el-button type="primary" :loading="approving" @click="submitApprove">
          确认通过
        </el-button>
      </template>
    </el-dialog>

    <!-- 驳回 -->
    <el-dialog v-model="rejectVisible" title="驳回信息变更" width="min(480px, 92vw)">
      <div v-if="rejectTarget" class="review-dialog">
        <div class="review-target">
          <strong>{{ rejectTarget.name }}</strong>
          <span class="mono">{{ rejectTarget.changeNo || '—' }}</span>
        </div>

        <div class="review-fields">
          <ElderlyChip
            v-for="label in recordFieldLabels(rejectTarget)"
            :key="label"
            :text="label"
          />
        </div>

        <el-form label-position="top">
          <el-form-item label="驳回原因（必填）">
            <el-input
              v-model="rejectForm.reviewOpinion"
              type="textarea"
              :rows="3"
              maxlength="500"
              show-word-limit
              placeholder="如：新卡号与照片不一致"
            />
          </el-form-item>
        </el-form>
      </div>
      <template #footer>
        <el-button @click="rejectVisible = false">取消</el-button>
        <el-button type="danger" :loading="rejecting" @click="submitReject">
          确认驳回
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.info-change-list-page {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.summary {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 10px;
}

.filter-card,
.list-card {
  overflow: hidden;
}

.status-tabs {
  padding: 12px 16px 0;
  border-bottom: 1px solid var(--nc-line-faint);
}

.filter-bar {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
  padding: 14px 16px;
}

.filter-search {
  flex: 1 1 280px;
}

.filter-actions {
  display: flex;
  gap: 8px;
}

.list-area {
  padding: 0 16px 16px;
}

.list-skeleton {
  padding-top: 14px;
}

.primary-cell {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.primary-cell small {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.field-summary {
  display: block;
  overflow: hidden;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.row-actions {
  display: flex;
  justify-content: flex-end;
  gap: 2px;
}

.row-actions :deep(.el-button + .el-button) {
  margin-left: 0;
}

.change-cards {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding-top: 12px;
}

.change-card {
  display: flex;
  flex-direction: column;
  gap: 9px;
  padding: 12px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background: var(--nc-paper);
}

.card-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 10px;
}

.card-head > div {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.card-head strong {
  font-size: var(--nc-text-md);
}

.card-head small {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.change-no {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.card-chips {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
}

.card-fields {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
}

.card-meta-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px;
}

.card-meta-grid .span-2 {
  grid-column: 1 / -1;
}

.card-meta-grid span {
  display: block;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.card-meta-grid strong {
  display: block;
  margin-top: 2px;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  font-weight: 500;
  word-break: break-all;
}

.card-actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 8px;
}

.empty-wrap {
  padding: 30px 0;
}

.pagination {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding: 12px 16px;
  border-top: 1px solid var(--nc-line-faint);
}

.pagination-note {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.mono {
  font-family: var(--nc-font-mono);
}

.muted {
  color: var(--nc-muted);
}

.load-error {
  margin: 0;
}

/* 详情 */
.detail-body {
  display: flex;
  flex-direction: column;
  gap: 18px;
  padding-bottom: 20px;
}

.detail-section {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.detail-section h3 {
  margin: 0;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-lg);
}

.detail-person {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
}

.detail-person > div:first-child {
  display: flex;
  flex-direction: column;
  gap: 3px;
}

.detail-person h3 {
  font-size: var(--nc-text-xl);
}

.detail-person .mono {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.detail-chips {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.detail-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px 14px;
  margin: 0;
  padding: 12px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background: var(--nc-paper-2);
}

.detail-wide {
  grid-column: 1 / -1;
}

.detail-grid dt {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.detail-grid dd {
  margin: 2px 0 0;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  word-break: break-all;
}

.change-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.change-row {
  display: flex;
  flex-direction: column;
  gap: 7px;
  padding: 10px 12px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background: var(--nc-paper);
}

.change-field {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.change-field strong {
  color: var(--nc-ink);
  font-size: var(--nc-text-sm);
}

.change-values {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto minmax(0, 1fr);
  align-items: stretch;
  gap: 8px;
}

.change-value {
  min-width: 0;
  padding: 7px 9px;
  border-radius: var(--nc-radius-xs);
  background: var(--nc-paper-3);
}

.change-value.is-new {
  background: var(--nc-pine-soft);
}

.change-value span {
  display: block;
  margin-bottom: 2px;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.change-value p {
  margin: 0;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  word-break: break-all;
}

.change-arrow {
  align-self: center;
  color: var(--nc-muted);
  font-size: var(--nc-text-md);
}

.detail-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}

/* 审批弹窗 */
.review-dialog {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.review-target {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 8px;
}

.review-target strong {
  font-size: var(--nc-text-md);
}

.review-target .mono {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.review-fields {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.review-alert {
  margin: 0;
}

@media (max-width: 767px) {
  .summary {
    grid-template-columns: repeat(3, minmax(0, 1fr));
    gap: 8px;
  }

  .filter-bar {
    flex-direction: column;
  }

  .filter-actions {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .detail-grid {
    grid-template-columns: minmax(0, 1fr);
  }

  .detail-wide {
    grid-column: auto;
  }

  .change-values {
    grid-template-columns: minmax(0, 1fr);
  }

  .change-arrow {
    justify-self: center;
    transform: rotate(90deg);
  }

  .card-actions {
    display: grid;
    grid-template-columns: repeat(3, minmax(0, 1fr));
  }

  .card-actions :deep(.el-button) {
    width: 100%;
    margin-left: 0;
  }

  .pagination {
    flex-direction: column;
    align-items: stretch;
  }
}

@media (max-width: 480px) {
  .summary {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
