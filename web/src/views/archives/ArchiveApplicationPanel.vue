<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { Refresh, Search } from '@element-plus/icons-vue'
import { listApplications } from '@/api/applications'
import { getErrorMessage } from '@/api/http'
import NcStatusDot from '@/components/NcStatusDot.vue'
import { formatMoney } from '@/views/applications/format'
import { resolveApplicationStatus } from '@/views/applications/status'
import type { ApplicationListItem } from '@/types/application'

const props = withDefaults(
  defineProps<{
    selected: ApplicationListItem | null
    initialKeyword?: string
  }>(),
  {
    initialKeyword: '',
  },
)

const emit = defineEmits<{ select: [item: ApplicationListItem] }>()

const keyword = ref(props.initialKeyword)
const items = ref<ApplicationListItem[]>([])
const page = ref(1)
const pageSize = ref(10)
const totalCount = ref(0)
const totalPages = ref(1)
const loading = ref(false)
const error = ref('')

let loadTicket = 0

const selectedStatus = computed(() => {
  if (!props.selected) return null
  return resolveApplicationStatus(props.selected.status, props.selected.statusText)
})

const paginationLayout = 'prev, pager, next'

function statusOf(item: ApplicationListItem) {
  return resolveApplicationStatus(item.status, item.statusText)
}

function placeText(item: ApplicationListItem): string {
  return [item.town, item.village].filter(Boolean).join(' / ') || item.address || '—'
}

function idCardText(item: ApplicationListItem): string {
  return item.applicantIdCardMasked || '—'
}

/** 分类代码 → 中文（与 Core ClassificationConstants.CodeToDescription 同源）。 */
const CLASSIFICATION_NAMES: Record<string, string> = {
  RuralSubsistence: '最低生活保障对象',
  UrbanSubsistence: '最低生活保障对象',
  RuralLowIncome: '最低生活保障边缘家庭',
  UrbanLowIncome: '最低生活保障边缘家庭',
  RuralDestituteCentralized: '特困人员（集中供养）',
  UrbanDestituteCentralized: '特困人员（集中供养）',
  RuralDestituteScattered: '特困人员（分散供养）',
  UrbanDestituteScattered: '特困人员（分散供养）',
  RuralRigidExpenditure: '刚性支出困难家庭',
  UrbanRigidExpenditure: '刚性支出困难家庭',
  RuralIncomeExceeded: '收入超标',
  UrbanIncomeExceeded: '收入超标',
  IneligibleWithLabor: '不符合认定条件（有劳动力）',
  Ineligible: '不符合认定条件',
  IneligibleOther: '不符合认定条件（其他）',
  RuralLowIncomeSingle: '最低生活保障对象（单人）',
  UrbanLowIncomeSingle: '最低生活保障对象（单人）',
}

function classificationText(value: string | null | undefined): string {
  const code = value?.trim() ?? ''
  if (!code) return '—'
  const mapped = CLASSIFICATION_NAMES[code]
  if (mapped) return mapped
  return /[\u4e00-\u9fa5]/.test(code) ? code : '未识别分类'
}

async function load(): Promise<void> {
  const ticket = ++loadTicket
  loading.value = true
  error.value = ''

  try {
    const response = await listApplications({
      page: page.value,
      pageSize: pageSize.value,
      keyword: keyword.value.trim() || undefined,
    })
    if (ticket !== loadTicket) return

    items.value = response.items ?? []
    page.value = response.pageIndex || page.value
    pageSize.value = response.pageSize || pageSize.value
    totalCount.value = response.totalCount ?? 0
    totalPages.value = Math.max(response.totalPages ?? 1, 1)
  } catch (loadError) {
    if (ticket !== loadTicket) return
    items.value = []
    totalCount.value = 0
    totalPages.value = 1
    error.value = getErrorMessage(loadError, '申请列表加载失败')
  } finally {
    if (ticket === loadTicket) loading.value = false
  }
}

function handleSearch(): void {
  page.value = 1
  void load()
}

function handlePageChange(value: number): void {
  page.value = value
  void load()
}

onMounted(() => {
  void load()
})

defineExpose({ reload: load })
</script>

<template>
  <div class="application-panel">
    <form class="search-row" @submit.prevent="handleSearch">
      <el-input
        v-model="keyword"
        clearable
        :prefix-icon="Search"
        placeholder="申请编号 / 姓名 / 身份证"
        @clear="handleSearch"
      />
      <el-button type="primary" :icon="Search" native-type="submit" :loading="loading">
        查询
      </el-button>
    </form>

    <div v-if="selected" class="selected-block">
      <div class="selected-head">
        <strong>{{ selected.applicantName }}</strong>
        <NcStatusDot
          v-if="selectedStatus"
          :status="selectedStatus.dot"
          :label="selectedStatus.label"
        />
      </div>
      <dl class="selected-meta">
        <div>
          <dt>申请编号</dt>
          <dd>{{ selected.applicationNo || '—' }}</dd>
        </div>
        <div>
          <dt>身份证</dt>
          <dd>{{ idCardText(selected) }}</dd>
        </div>
        <div>
          <dt>乡镇 / 村</dt>
          <dd>{{ placeText(selected) }}</dd>
        </div>
        <div>
          <dt>家庭人口</dt>
          <dd>{{ selected.familySize ? `${selected.familySize} 人` : '—' }}</dd>
        </div>
        <div>
          <dt>分类</dt>
          <dd>{{ classificationText(selected.classificationResult) }}</dd>
        </div>
        <div>
          <dt>人均年收入</dt>
          <dd>{{ formatMoney(selected.perCapitaAnnualIncome) }} 元</dd>
        </div>
      </dl>
    </div>

    <div v-else class="selected-empty">
      <span>尚未选择申请</span>
      <small>从下方搜索结果中选择一条申请，继续选择文书。</small>
    </div>

    <el-alert
      v-if="error"
      class="panel-error"
      type="error"
      :title="error"
      :closable="false"
      show-icon
    >
      <el-button link type="primary" :icon="Refresh" @click="load">重新加载</el-button>
    </el-alert>

    <div class="list-wrap">
      <el-skeleton v-if="loading && items.length === 0" :rows="4" animated />

      <template v-else-if="items.length > 0">
        <button
          v-for="item in items"
          :key="item.id"
          type="button"
          class="application-item"
          :class="{ 'is-selected': selected?.id === item.id }"
          @click="emit('select', item)"
        >
          <span class="item-head">
            <strong>{{ item.applicantName }}</strong>
            <NcStatusDot
              :status="statusOf(item).dot"
              :label="statusOf(item).label"
            />
          </span>
          <span class="item-no">{{ item.applicationNo || '—' }}</span>
          <span class="item-meta">
            <span>{{ idCardText(item) }}</span>
            <span>{{ placeText(item) }}</span>
            <span v-if="item.classificationResult">{{ classificationText(item.classificationResult) }}</span>
          </span>
        </button>
      </template>

      <el-empty
        v-else-if="!loading && !error"
        :image-size="70"
        description="未找到匹配的申请"
      >
        <p class="empty-tip">试试输入完整的申请编号、姓名或身份证号。</p>
      </el-empty>
    </div>

    <footer v-if="totalCount > pageSize" class="panel-footer">
      <span class="panel-total">共 {{ totalCount }} 条 · 第 {{ page }}/{{ totalPages }} 页</span>
      <el-pagination
        :current-page="page"
        :page-size="pageSize"
        :total="totalCount"
        :layout="paginationLayout"
        :pager-count="5"
        small
        background
        @current-change="handlePageChange"
      />
    </footer>
  </div>
</template>

<style scoped>
.application-panel {
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-height: 0;
}

.search-row {
  display: flex;
  gap: 8px;
}

.search-row .el-input {
  flex: 1;
  min-width: 0;
}

.selected-block {
  padding: 12px 14px;
  border: 1px solid var(--nc-line);
  border-left: 3px solid var(--nc-seal);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-3);
}

.selected-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
}

.selected-head strong {
  overflow: hidden;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-lg);
  font-weight: 700;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.selected-meta {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 7px 14px;
  margin-top: 9px;
}

.selected-meta > div {
  display: flex;
  flex-direction: column;
  gap: 1px;
  min-width: 0;
}

.selected-meta dt {
  color: var(--nc-muted);
  font-size: 11px;
  line-height: 1.4;
}

.selected-meta dd {
  overflow: hidden;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  line-height: 1.45;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.selected-empty {
  display: flex;
  flex-direction: column;
  gap: 3px;
  padding: 12px 14px;
  border: 1px dashed var(--nc-line-strong);
  border-radius: var(--nc-radius-sm);
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.selected-empty small {
  color: var(--nc-muted);
  font-size: 11px;
  line-height: 1.6;
}

.panel-error {
  margin: 0;
}

.list-wrap {
  display: flex;
  flex-direction: column;
  gap: 6px;
  max-height: 318px;
  min-height: 150px;
  padding-right: 2px;
  overflow-y: auto;
}

.application-item {
  display: flex;
  flex-direction: column;
  gap: 4px;
  width: 100%;
  padding: 9px 11px;
  border: 1px solid var(--nc-line-faint);
  border-left: 2px solid transparent;
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-2);
  text-align: left;
  cursor: pointer;
  transition:
    border-color var(--nc-transition),
    background-color var(--nc-transition);
}

.application-item:hover {
  border-color: var(--nc-line);
  background-color: var(--nc-paper-3);
}

.application-item.is-selected {
  border-color: var(--nc-seal);
  border-left-color: var(--nc-seal);
  background-color: var(--nc-seal-soft);
}

.item-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  min-width: 0;
}

.item-head strong {
  overflow: hidden;
  color: var(--nc-ink);
  font-size: var(--nc-text-base);
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.item-no {
  color: var(--nc-ink-2);
  font-family: var(--nc-font-mono);
  font-size: 11px;
  letter-spacing: 0.02em;
}

.item-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 2px 12px;
  color: var(--nc-muted);
  font-size: 11px;
}

.empty-tip {
  margin-top: 4px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.panel-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  padding-top: 8px;
  border-top: 1px solid var(--nc-line-faint);
}

.panel-total {
  color: var(--nc-muted);
  font-size: 11px;
  white-space: nowrap;
}

.panel-footer :deep(.el-pagination) {
  padding: 0;
}

@media (max-width: 767px) {
  .list-wrap {
    max-height: none;
  }
}
</style>
