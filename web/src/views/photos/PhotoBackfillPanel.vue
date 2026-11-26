<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import { Camera, Refresh, Search, View } from '@element-plus/icons-vue'
import { listPhotoBackfill } from '@/api/photos'
import type {
  PhotoBackfillItem,
  PhotoBackfillStatus,
  PhotoCapturePerson,
  PhotoDomain,
} from '@/types/photos'
import NcCard from '@/components/NcCard.vue'
import NcStatusDot from '@/components/NcStatusDot.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import {
  PHOTO_DOMAIN_OPTIONS,
  PHOTO_PAGE_SIZE,
  PHOTO_STATUS_OPTIONS,
  backfillStatusMeta,
  domainLabel,
  missingCountFor,
  normalizeDomain,
  photoActionError,
  toCapturePerson,
  uploadPercent,
} from './photoMeta'

const props = withDefaults(
  defineProps<{
    initialKeyword?: string
    initialDomain?: string
    initialStatus?: PhotoBackfillStatus
  }>(),
  {
    initialKeyword: '',
    initialDomain: '',
    initialStatus: '',
  },
)

const emit = defineEmits<{
  (event: 'open-capture', people: PhotoCapturePerson[], startIndex: number): void
}>()

const isNarrow = useMediaQuery('(max-width: 900px)')

const keyword = ref(props.initialKeyword.trim())
const domain = ref<PhotoDomain>(normalizeDomain(props.initialDomain))
const status = ref<PhotoBackfillStatus>(props.initialStatus)
const page = ref(1)
const total = ref(0)
const items = ref<PhotoBackfillItem[]>([])
const loading = ref(false)
const loadError = ref('')
const scopeNote = ref('')
const selectedMap = ref<Record<string, PhotoBackfillItem>>({})

let listTicket = 0

const totalPages = computed(() => Math.max(1, Math.ceil(total.value / PHOTO_PAGE_SIZE)))
const selectedCount = computed(() => Object.keys(selectedMap.value).length)
const allSelected = computed(
  () =>
    items.value.length > 0 &&
    items.value.every((item) => Boolean(selectedMap.value[item.idCard])),
)
const someSelected = computed(() => selectedCount.value > 0 && !allSelected.value)
const selectedPeople = computed<PhotoCapturePerson[]>(() =>
  Object.values(selectedMap.value).map(toCapturePerson),
)
const hasFilter = computed(() => Boolean(keyword.value.trim() || domain.value || status.value))

function missingText(item: PhotoBackfillItem): string {
  const missing = missingCountFor(item)
  return missing === null ? '—' : String(missing)
}

function toggleRow(item: PhotoBackfillItem): void {
  const next = { ...selectedMap.value }
  if (next[item.idCard]) delete next[item.idCard]
  else next[item.idCard] = item
  selectedMap.value = next
}

function selectAll(): void {
  const next = { ...selectedMap.value }
  for (const item of items.value) next[item.idCard] = item
  selectedMap.value = next
}

function clearSelection(): void {
  selectedMap.value = {}
}

function openSelected(): void {
  if (selectedPeople.value.length === 0) {
    ElMessage.warning('请先勾选需要补传照片的人员')
    return
  }
  emit('open-capture', selectedPeople.value, 0)
  clearSelection()
}

function openSingle(item: PhotoBackfillItem): void {
  emit('open-capture', [toCapturePerson(item)], 0)
}

async function loadList(options: { clearSelection?: boolean } = { clearSelection: true }): Promise<void> {
  const ticket = ++listTicket
  loading.value = true
  loadError.value = ''
  if (options.clearSelection !== false) selectedMap.value = {}

  try {
    const response = await listPhotoBackfill({
      domain: domain.value,
      keyword: keyword.value,
      status: status.value,
      page: page.value,
      pageSize: PHOTO_PAGE_SIZE,
    })
    if (ticket !== listTicket) return
    items.value = response.items
    total.value = response.total
    scopeNote.value = response.scopeNote ?? ''
    if (response.page > 0) page.value = response.page
  } catch (error) {
    if (ticket !== listTicket) return
    items.value = []
    total.value = 0
    scopeNote.value = ''
    loadError.value = photoActionError(
      error,
      '人员清单加载失败',
      '查询照片补全人员',
      'ARCHIVE_VIEW',
    )
  } finally {
    if (ticket === listTicket) loading.value = false
  }
}

function reload(): void {
  void loadList({ clearSelection: true })
}

function search(): void {
  page.value = 1
  void loadList()
}

function reset(): void {
  keyword.value = ''
  domain.value = ''
  status.value = ''
  page.value = 1
  void loadList()
}

function handleDomainChange(): void {
  page.value = 1
  void loadList()
}

function handleStatusChange(): void {
  page.value = 1
  void loadList()
}

function goPage(next: number): void {
  if (next < 1 || next > totalPages.value || next === page.value) return
  page.value = next
  void loadList()
}

watch(
  () => props.initialKeyword,
  (value) => {
    const text = (value ?? '').trim()
    if (text === keyword.value) return
    keyword.value = text
    page.value = 1
    void loadList()
  },
)

watch(
  () => props.initialDomain,
  (value) => {
    const next = normalizeDomain(value)
    if (next === domain.value) return
    domain.value = next
    page.value = 1
    void loadList()
  },
)

onMounted(() => {
  void loadList()
})

defineExpose({ reload })
</script>

<template>
  <div class="backfill-panel">
    <NcCard padding="none" class="filter-card">
      <form class="filter-bar" @submit.prevent="search">
        <el-input
          v-model="keyword"
          class="filter-keyword"
          clearable
          :prefix-icon="Search"
          placeholder="姓名 / 身份证号"
          @clear="search"
        />

        <el-radio-group
          v-model="domain"
          class="domain-group"
          size="small"
          @change="handleDomainChange"
        >
          <el-radio-button
            v-for="option in PHOTO_DOMAIN_OPTIONS"
            :key="option.value || 'all'"
            :value="option.value"
          >
            {{ option.label }}
          </el-radio-button>
        </el-radio-group>

        <el-select
          v-model="status"
          class="filter-status"
          placeholder="全部状态"
          @change="handleStatusChange"
        >
          <el-option
            v-for="option in PHOTO_STATUS_OPTIONS"
            :key="option.value || 'all'"
            :label="option.label"
            :value="option.value"
          />
        </el-select>

        <el-button type="primary" :icon="Search" native-type="submit" :loading="loading">
          查询
        </el-button>
        <el-button v-if="hasFilter" text native-type="button" @click="reset">重置</el-button>
        <el-button class="refresh-button" :icon="Refresh" :loading="loading" @click="reload">
          刷新
        </el-button>
      </form>
    </NcCard>

    <NcCard padding="none" class="list-card" title="人员清单">
      <template #actions>
        <NcStatusDot status="default" :label="`共 ${total} 人`" />
      </template>

      <div class="selection-bar">
        <span class="selection-count">
          已选 <strong>{{ selectedCount }}</strong> 人
        </span>
        <div class="selection-actions">
          <el-button link type="primary" @click="allSelected ? clearSelection() : selectAll()">
            {{ allSelected ? '取消全选' : '全选本页' }}
          </el-button>
          <el-button link :disabled="selectedCount === 0" @click="clearSelection">
            清空勾选
          </el-button>
        </div>
      </div>

      <el-alert
        v-if="scopeNote"
        class="scope-note"
        type="warning"
        :title="scopeNote"
        :closable="false"
        show-icon
      />

      <el-alert
        v-if="loadError"
        class="load-error"
        type="error"
        :title="loadError"
        :closable="false"
        show-icon
      >
        <el-button link type="primary" @click="reload">重新加载</el-button>
      </el-alert>

      <div v-loading="loading && items.length > 0" class="list-area">
        <el-skeleton v-if="loading && items.length === 0" :rows="8" animated class="list-skeleton" />

        <el-table
          v-else-if="!isNarrow && items.length > 0"
          :data="items"
          row-key="idCard"
          size="small"
          class="backfill-table"
        >
          <el-table-column width="46" align="center">
            <template #default="{ row }">
              <el-checkbox
                :model-value="Boolean(selectedMap[row.idCard])"
                @change="toggleRow(row as PhotoBackfillItem)"
              />
            </template>
          </el-table-column>

          <el-table-column label="姓名" min-width="100">
            <template #default="{ row }">
              <strong class="person-name">{{ row.personName }}</strong>
            </template>
          </el-table-column>

          <el-table-column label="身份证" min-width="190">
            <template #default="{ row }">
              <span class="mono">{{ row.idCard || '—' }}</span>
            </template>
          </el-table-column>

          <el-table-column label="业务域" min-width="170">
            <template #default="{ row }">
              <div class="domain-tags">
                <el-tag
                  v-for="code in row.businessTypes"
                  :key="code"
                  size="small"
                  effect="plain"
                >
                  {{ domainLabel(code) }}
                </el-tag>
                <span v-if="row.businessTypes.length === 0" class="muted">—</span>
              </div>
            </template>
          </el-table-column>

          <el-table-column label="照片" min-width="160">
            <template #default="{ row }">
              <div class="photo-progress">
                <div class="photo-count">
                  <span>已传 <b>{{ row.uploadedCount }}</b></span>
                  <span class="muted">/ 未传 <b>{{ missingText(row as PhotoBackfillItem) }}</b></span>
                </div>
                <div class="photo-bar">
                  <i :style="{ width: `${uploadPercent(row as PhotoBackfillItem)}%` }" />
                </div>
              </div>
            </template>
          </el-table-column>

          <el-table-column label="状态" width="86" align="center">
            <template #default="{ row }">
              <el-tag :type="backfillStatusMeta(row as PhotoBackfillItem).type" size="small" effect="plain">
                {{ backfillStatusMeta(row as PhotoBackfillItem).label }}
              </el-tag>
            </template>
          </el-table-column>

          <el-table-column label="操作" width="92" align="right" fixed="right">
            <template #default="{ row }">
              <el-button link type="primary" :icon="View" @click="openSingle(row as PhotoBackfillItem)">
                查看
              </el-button>
            </template>
          </el-table-column>
        </el-table>

        <div v-else-if="isNarrow && items.length > 0" class="mobile-list">
          <article v-for="row in items" :key="row.idCard" class="mobile-row">
            <el-checkbox
              :model-value="Boolean(selectedMap[row.idCard])"
              @change="toggleRow(row)"
            />
            <div class="mobile-main">
              <div class="mobile-head">
                <strong class="person-name">{{ row.personName }}</strong>
                <el-tag :type="backfillStatusMeta(row).type" size="small" effect="plain">
                  {{ backfillStatusMeta(row).label }}
                </el-tag>
              </div>
              <span class="mono mobile-id">{{ row.idCard || '—' }}</span>
              <div class="domain-tags">
                <el-tag
                  v-for="code in row.businessTypes"
                  :key="code"
                  size="small"
                  effect="plain"
                >
                  {{ domainLabel(code) }}
                </el-tag>
                <span v-if="row.businessTypes.length === 0" class="muted">未归类</span>
              </div>
              <div class="photo-progress">
                <div class="photo-count">
                  <span>已传 <b>{{ row.uploadedCount }}</b></span>
                  <span class="muted">/ 未传 <b>{{ missingText(row) }}</b></span>
                </div>
                <div class="photo-bar">
                  <i :style="{ width: `${uploadPercent(row)}%` }" />
                </div>
              </div>
            </div>
            <el-button link type="primary" :icon="View" @click="openSingle(row)">查看</el-button>
          </article>
        </div>

        <div v-else-if="!loading && !loadError" class="empty-wrap">
          <el-empty description="暂无匹配人员" :image-size="72">
            <p class="empty-hint">
              可输入姓名或身份证号检索；切换业务域可查看不同来源的人员。
            </p>
          </el-empty>
        </div>
      </div>

      <template #footer>
        <div class="footer-bar">
          <span class="pager-note">
            第 {{ page }} / {{ totalPages }} 页 · 每页 {{ PHOTO_PAGE_SIZE }} 人 · 共 {{ total }} 人
          </span>
          <el-pagination
            v-if="total > 0"
            small
            background
            layout="prev, pager, next"
            :current-page="page"
            :page-size="PHOTO_PAGE_SIZE"
            :total="total"
            @current-change="goPage"
          />
          <div class="footer-actions">
            <span class="selection-hint">
              已选 {{ selectedCount }} 人
              <template v-if="someSelected">（{{ allSelected ? '本页已全选' : '部分选中' }}）</template>
            </span>
            <el-button
              type="primary"
              :icon="Camera"
              :disabled="selectedCount === 0"
              @click="openSelected"
            >
              补传照片{{ selectedCount > 0 ? `（${selectedCount}）` : '' }}
            </el-button>
          </div>
        </div>
      </template>
    </NcCard>
  </div>
</template>

<style scoped>
.backfill-panel {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.filter-card,
.list-card {
  overflow: hidden;
}

.filter-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
  padding: 14px 16px;
}

.filter-keyword {
  flex: 1 1 240px;
  max-width: 360px;
}

.domain-group {
  display: flex;
  flex-wrap: wrap;
  flex: none;
}

.domain-group :deep(.el-radio-button__inner) {
  border-left: 1px solid var(--el-border-color);
}

.filter-status {
  flex: none;
  width: 130px;
}

.refresh-button {
  margin-left: auto;
}

.selection-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding: 9px 16px;
  border-top: 1px solid var(--nc-line-faint);
  background-color: var(--nc-paper-3);
}

.selection-count {
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
}

.selection-count strong {
  color: var(--nc-seal-dark);
  font-family: var(--nc-font-mono);
}

.selection-actions {
  display: flex;
  gap: 4px;
}

.selection-actions :deep(.el-button + .el-button) {
  margin-left: 0;
}

.load-error {
  margin: 0;
}

.scope-note {
  margin: 0;
  border-radius: 0;
}

.list-area {
  min-height: 260px;
}

.list-skeleton {
  padding: 14px;
}

.backfill-table :deep(.el-table__cell) {
  vertical-align: middle;
}

.person-name {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
}

.mono {
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
}

.domain-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}

.muted {
  color: var(--nc-muted);
}

.photo-progress {
  display: flex;
  flex-direction: column;
  gap: 5px;
}

.photo-count {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 4px;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
}

.photo-count b {
  color: var(--nc-seal-dark);
  font-family: var(--nc-font-mono);
}

.photo-bar {
  width: 100%;
  max-width: 130px;
  height: 3px;
  overflow: hidden;
  background-color: var(--nc-paper-4);
}

.photo-bar i {
  display: block;
  height: 100%;
  background-color: var(--nc-pine-light);
}

.mobile-list {
  display: flex;
  flex-direction: column;
}

.mobile-row {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  padding: 12px 14px;
  border-bottom: 1px solid var(--nc-line-faint);
}

.mobile-row:last-child {
  border-bottom: none;
}

.mobile-main {
  display: flex;
  flex-direction: column;
  gap: 4px;
  flex: 1;
  min-width: 0;
}

.mobile-head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.mobile-id {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.empty-wrap {
  padding: 16px 8px;
}

.empty-hint {
  margin-top: 6px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  text-align: center;
}

.footer-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px 16px;
  padding: 4px 16px 2px;
}

.pager-note {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
}

.footer-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
  margin-left: auto;
}

.selection-hint {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

@media (max-width: 1100px) {
  .refresh-button {
    margin-left: 0;
  }
}

@media (max-width: 767px) {
  .filter-bar {
    padding: 12px;
  }

  .filter-keyword {
    flex-basis: 100%;
    max-width: none;
  }

  .domain-group {
    flex: 1 1 100%;
    overflow-x: auto;
    flex-wrap: nowrap;
    padding-bottom: 2px;
  }

  .filter-status {
    flex: 1 1 120px;
    width: auto;
  }

  .refresh-button {
    margin-left: 0;
  }

  .selection-bar {
    padding: 9px 12px;
  }

  .footer-bar {
    padding: 10px 12px;
  }

  .footer-actions {
    flex: 1 1 100%;
    margin-left: 0;
    justify-content: space-between;
  }

  .footer-actions :deep(.el-button) {
    flex: 1;
  }
}
</style>
