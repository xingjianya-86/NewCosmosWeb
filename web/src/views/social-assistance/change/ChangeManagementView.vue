<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Refresh, Search } from '@element-plus/icons-vue'
import {
  getChangeManagementDetail,
  getChangeManagementFilters,
  getChangeManagementHistory,
  listChangeManagement,
  migrateImportedArchive,
  searchImportedArchives,
  submitVerifyStop,
} from '@/api/changeManagement'
import { getErrorMessage } from '@/api/http'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStat from '@/components/NcStat.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import type {
  ChangeHistoryItem,
  ChangeManagementDetail,
  ChangeManagementFilters,
  ChangeManagementListItem,
  ImportedArchiveItem,
} from '@/types/changeManagement'
import ChangeDetailPanel from './ChangeDetailPanel.vue'
import MemberChangeDialog from './MemberChangeDialog.vue'
import { formatMoney, graceDaysText } from './format'

const LIST_PATH = '/social-assistance/change-management'

const router = useRouter()
const isNarrow = useMediaQuery('(max-width: 1080px)')

const keyword = ref('')
const town = ref('')
const classification = ref('')
const page = ref(1)
const pageSize = ref(20)

const items = ref<ChangeManagementListItem[]>([])
const totalCount = ref(0)
const totalPages = ref(0)
const loading = ref(false)
const loadError = ref('')

const filters = ref<ChangeManagementFilters | null>(null)
const filtersError = ref('')

const importedItems = ref<ImportedArchiveItem[]>([])
const importedLoading = ref(false)
const importedError = ref('')
const importingKey = ref('')

const selectedId = ref<number | null>(null)
const detail = ref<ChangeManagementDetail | null>(null)
const detailLoading = ref(false)
const detailError = ref('')
const history = ref<ChangeHistoryItem[]>([])
const historyLoading = ref(false)
const detailDrawerVisible = ref(false)
const memberDialogVisible = ref(false)
const actionBusy = ref(false)

let requestTicket = 0
let detailTicket = 0

const hasFilters = computed(
  () => Boolean(keyword.value.trim() || town.value || classification.value),
)
const hasKeyword = computed(() => Boolean(keyword.value.trim()))
const importedSubtitle = computed(() => {
  if (importedError.value) return '历史导入库检索失败'
  if (importedLoading.value) return '正在检索历史导入库…'
  return `导入库命中 ${importedItems.value.length} 户（含成员命中）`
})
const displayTotalPages = computed(() => Math.max(totalPages.value, 1))
const paginationLayout = computed(() =>
  isNarrow.value ? 'prev, pager, next' : 'total, sizes, prev, pager, next, jumper',
)

const pageGraceCount = computed(
  () => items.value.filter((row) => row.gracePeriodEndDate).length,
)

const listSubtitle = computed(() => {
  if (loadError.value) return '数据加载失败'
  if (loading.value && items.value.length === 0) return '正在读取在享保障对象…'
  const suffix = hasFilters.value ? ' · 已应用筛选' : ''
  return `共 ${totalCount.value} 户在享对象${suffix}`
})

const emptyDescription = computed(() =>
  hasFilters.value ? '没有找到匹配的在享保障对象' : '暂无已审批/已完成的在享保障对象',
)

const canOpenMemberDialog = computed(
  () => Boolean(detail.value && filters.value && detail.value.canManage && detail.value.canChangeMember),
)

function rowClassName({ row }: { row: ChangeManagementListItem }): string {
  return row.applicationId === selectedId.value ? 'is-selected' : ''
}

async function loadFilters(): Promise<void> {
  filtersError.value = ''
  try {
    filters.value = await getChangeManagementFilters()
  } catch (error) {
    filters.value = null
    filtersError.value = getErrorMessage(error, '筛选选项加载失败')
  }
}

async function load(): Promise<void> {
  const ticket = ++requestTicket
  loading.value = true
  loadError.value = ''

  try {
    const response = await listChangeManagement({
      page: page.value,
      pageSize: pageSize.value,
      keyword: keyword.value.trim() || undefined,
      town: town.value || undefined,
      classification: classification.value || undefined,
    })
    if (ticket !== requestTicket) return

    items.value = response.items ?? []
    page.value = response.pageIndex || page.value
    pageSize.value = response.pageSize || pageSize.value
    totalCount.value = response.totalCount ?? 0
    totalPages.value = response.totalPages ?? 0

    if (selectedId.value && !items.value.some((row) => row.applicationId === selectedId.value)) {
      // 选中户已停保/移出在享范围：保留详情可见，但提示列表已不再包含该户。
      detailDrawerVisible.value = false
    }
  } catch (error) {
    if (ticket !== requestTicket) return
    items.value = []
    totalCount.value = 0
    totalPages.value = 0
    loadError.value = getErrorMessage(error, '在享保障对象列表加载失败')
  } finally {
    if (ticket === requestTicket) loading.value = false
  }
}

async function loadImported(): Promise<void> {
  if (!hasKeyword.value) {
    importedItems.value = []
    importedError.value = ''
    return
  }

  importedLoading.value = true
  importedError.value = ''
  try {
    const response = await searchImportedArchives(keyword.value.trim())
    importedItems.value = response.items ?? []
  } catch (error) {
    importedItems.value = []
    importedError.value = getErrorMessage(error, '历史导入库检索失败')
  } finally {
    importedLoading.value = false
  }
}

/** 导入库建档并进入变更流程：未建档 → 建档后去五步表单补全；已建档 → 直接打开详情。 */
async function handleMigrateImported(item: ImportedArchiveItem): Promise<void> {
  const key = `${item.sourceTable}#${item.sourceId}`
  importingKey.value = key
  try {
    const result = await migrateImportedArchive(
      item.sourceTable,
      item.sourceId,
      item.derivedClassificationCode || undefined,
    )
    importedItems.value = importedItems.value.map((row) =>
      row.sourceTable === item.sourceTable && row.sourceId === item.sourceId
        ? { ...row, existingApplicationId: result.applicationId }
        : row,
    )

    if (result.alreadyExisted) {
      ElMessage.success('该导入库档案此前已建档，已打开档案详情')
      await load()
      await loadDetail(result.applicationId)
      if (isNarrow.value) detailDrawerVisible.value = true
    } else {
      ElMessage.success('建档成功，请补全档案信息后办理变更')
      void router.push({ path: `/applications/${result.applicationId}/edit` })
    }
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '建档失败'))
  } finally {
    importingKey.value = ''
  }
}

/** 已建档的导入库来源行：直接加载当前库档案详情。 */
async function openImportedExisting(applicationId: number): Promise<void> {
  await loadDetail(applicationId)
  if (isNarrow.value) detailDrawerVisible.value = true
}

async function loadDetail(applicationId: number): Promise<void> {
  const ticket = ++detailTicket
  selectedId.value = applicationId
  detailLoading.value = true
  detailError.value = ''
  history.value = []
  try {
    const response = await getChangeManagementDetail(applicationId)
    if (ticket !== detailTicket) return
    detail.value = response
    void loadHistory(applicationId, ticket)
  } catch (error) {
    if (ticket !== detailTicket) return
    detail.value = null
    detailError.value = getErrorMessage(error, '选中户详情加载失败')
  } finally {
    if (ticket === detailTicket) detailLoading.value = false
  }
}

async function loadHistory(applicationId: number, ticket: number): Promise<void> {
  historyLoading.value = true
  try {
    const response = await getChangeManagementHistory(applicationId)
    if (ticket !== detailTicket) return
    history.value = response.items ?? []
  } catch (error) {
    if (ticket !== detailTicket) return
    history.value = []
    ElMessage.error(getErrorMessage(error, '变更历史加载失败'))
  } finally {
    if (ticket === detailTicket) historyLoading.value = false
  }
}

function selectRow(row: ChangeManagementListItem): void {
  if (isNarrow.value) detailDrawerVisible.value = true
  void loadDetail(row.applicationId)
}

function handleSearch(): void {
  page.value = 1
  void load()
  void loadImported()
}

function handleReset(): void {
  keyword.value = ''
  town.value = ''
  classification.value = ''
  page.value = 1
  importedItems.value = []
  importedError.value = ''
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

function closeDetail(): void {
  detailDrawerVisible.value = false
  selectedId.value = null
  detail.value = null
  history.value = []
  detailError.value = ''
}

function requireDetail(): ChangeManagementDetail | null {
  if (!detail.value) {
    ElMessage.warning('请先选择一户在享保障对象')
    return null
  }
  return detail.value
}

function goHeadChange(): void {
  const current = requireDetail()
  if (!current) return
  void router.push(`${LIST_PATH}/head/${current.applicationId}`)
}

function goHouseholdDeath(): void {
  const current = requireDetail()
  if (!current) return
  void router.push(`${LIST_PATH}/death/${current.applicationId}`)
}

function goGraceConfirm(): void {
  const current = requireDetail()
  if (!current) return
  void router.push(`${LIST_PATH}/grace/${current.applicationId}`)
}

function openMemberChange(): void {
  if (!canOpenMemberDialog.value) {
    ElMessage.warning('当前档案不满足成员变更条件（单人户或无办理权限）')
    return
  }
  memberDialogVisible.value = true
}

async function handleEconomicReview(): Promise<void> {
  const current = requireDetail()
  if (!current) return

  try {
    await ElMessageBox.confirm(
      '经济状况复核需在五步申请表单中完成。当前 Web 五步表单仅支持草稿编辑：已审批档案可进入查看复核信息，但保存会被服务端拒绝。是否继续前往？',
      '经济状况复核',
      {
        type: 'warning',
        confirmButtonText: '前往复核',
        cancelButtonText: '取消',
      },
    )
  } catch {
    return
  }

  void router.push({ path: `/applications/${current.applicationId}/edit`, query: { mode: 'review' } })
}

async function handleVerifyStop(): Promise<void> {
  const current = requireDetail()
  if (!current) return

  try {
    await ElMessageBox.confirm(
      `确定以「拒绝配合核验工作」为由停止 ${current.applicantName} 的保障？停止后档案转为已停保，分类与保障金不变，本次不会建立新档案。`,
      '核验停止',
      {
        type: 'warning',
        confirmButtonText: '停止保障',
        cancelButtonText: '取消',
      },
    )
  } catch {
    return
  }

  actionBusy.value = true
  try {
    const result = await submitVerifyStop(current.applicationId, { stopDate: null })
    ElMessage.success(
      result.triggeredStop
        ? '已停止保障（拒绝配合核验工作）。'
        : '核验停止已办理，请查看变更历史确认结果。',
    )
    closeDetail()
    await load()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '核验停止失败'))
  } finally {
    actionBusy.value = false
  }
}

async function handleMemberChangeSuccess(): Promise<void> {
  await load()
  const id = selectedId.value
  if (id) {
    await loadDetail(id)
  }
}

function handleMemberDialogVisibility(value: boolean): void {
  memberDialogVisible.value = value
  const id = selectedId.value
  if (!value && id) {
    void loadDetail(id)
  }
}

onMounted(() => {
  void loadFilters()
  void load()
})
</script>

<template>
  <div class="nc-page change-page">
    <NcPageHeader
      title="保障对象动态管理"
      description="在享保障对象检索、家庭成员增减、户主变更、户主死亡、渐退确认与核验停止。所有写操作均由服务端 Core 判定并落库。"
      kicker="低收入人口救助帮扶 · 动态管理"
    >
      <template #actions>
        <el-button :icon="Refresh" :loading="loading" @click="load()">刷新</el-button>
      </template>
    </NcPageHeader>

    <div class="summary">
      <NcStat label="在享保障对象" :value="totalCount" tone="ink" compact />
      <NcStat label="本页户数" :value="items.length" tone="pine" compact />
      <NcStat label="本页渐退期户" :value="pageGraceCount" tone="amber" compact />
      <NcStat label="当前页" :value="`${page} / ${displayTotalPages}`" tone="seal" compact />
    </div>

    <NcCard padding="none" class="filter-card">
      <form class="filter-bar" @submit.prevent="handleSearch">
        <el-input
          v-model="keyword"
          class="filter-search"
          clearable
          :prefix-icon="Search"
          placeholder="搜索户主姓名 / 身份证号"
          @clear="handleSearch"
        />
        <el-select v-model="town" class="filter-select" clearable placeholder="乡镇（街道）">
          <el-option
            v-for="option in filters?.towns ?? []"
            :key="option"
            :label="option"
            :value="option"
          />
        </el-select>
        <el-select
          v-model="classification"
          class="filter-select filter-select--wide"
          clearable
          placeholder="保障分类"
        >
          <el-option
            v-for="option in filters?.classifications ?? []"
            :key="option.code"
            :label="option.name"
            :value="option.code"
          />
        </el-select>
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
      v-if="filtersError"
      class="load-error"
      type="warning"
      :title="filtersError"
      :closable="false"
      show-icon
    >
      <el-button link type="primary" @click="loadFilters()">重新加载选项</el-button>
    </el-alert>

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

    <div class="workspace" :class="{ 'is-narrow': isNarrow }">
      <NcCard title="在享对象" :subtitle="listSubtitle" padding="none" class="list-card">
        <div v-loading="loading && items.length > 0" class="list-area">
          <el-skeleton v-if="loading && items.length === 0" :rows="6" animated class="list-skeleton" />

          <template v-else-if="items.length > 0">
            <el-table
              v-if="!isNarrow"
              :data="items"
              row-key="applicationId"
              class="change-table"
              :row-class-name="rowClassName"
              @row-click="selectRow"
            >
              <el-table-column label="户主" min-width="112">
                <template #default="{ row }">
                  <div class="person-cell">
                    <strong>{{ row.applicantName || '—' }}</strong>
                    <small>{{ row.applicationNo || '—' }}</small>
                  </div>
                </template>
              </el-table-column>

              <el-table-column label="身份证（脱敏）" min-width="138">
                <template #default="{ row }">
                  <span class="mono">{{ row.applicantIdCardMasked || '—' }}</span>
                </template>
              </el-table-column>

              <el-table-column label="保障分类" min-width="150">
                <template #default="{ row }">
                  <span class="classification">{{ row.classificationName || '—' }}</span>
                </template>
              </el-table-column>

              <el-table-column label="乡镇 / 村" min-width="132">
                <template #default="{ row }">
                  <span class="ellipsis" :title="[row.town, row.village].filter(Boolean).join(' / ')">
                    {{ [row.town, row.village].filter(Boolean).join(' / ') || '—' }}
                  </span>
                </template>
              </el-table-column>

              <el-table-column label="人数" width="66" align="right">
                <template #default="{ row }">{{ row.familySize }}</template>
              </el-table-column>

              <el-table-column label="保障金（元/月）" width="118" align="right">
                <template #default="{ row }">
                  <span class="mono">{{ formatMoney(row.totalGuaranteeAmount) }}</span>
                </template>
              </el-table-column>

              <el-table-column label="渐退期" width="112">
                <template #default="{ row }">
                  <span v-if="row.gracePeriodEndDate" class="grace" :class="{ 'is-expired': (row.graceDaysLeft ?? 0) < 0 }">
                    {{ graceDaysText(row.graceDaysLeft) }}
                  </span>
                  <span v-else class="muted">—</span>
                </template>
              </el-table-column>

              <el-table-column label="状态" width="82">
                <template #default="{ row }">
                  <span class="status">{{ row.statusText || row.status }}</span>
                </template>
              </el-table-column>

              <el-table-column label="操作" width="76" align="right">
                <template #default="{ row }">
                  <el-button
                    link
                    type="primary"
                    @click.stop="selectRow(row as ChangeManagementListItem)"
                  >
                    查看
                  </el-button>
                </template>
              </el-table-column>
            </el-table>

            <div v-else class="change-cards">
              <article
                v-for="row in items"
                :key="row.applicationId"
                class="change-card"
                role="button"
                tabindex="0"
                @click="selectRow(row)"
                @keydown.enter="selectRow(row)"
              >
                <header class="change-card__head">
                  <div>
                    <strong>{{ row.applicantName || '—' }}</strong>
                    <span>{{ row.applicationNo || '—' }}</span>
                  </div>
                  <span class="status">{{ row.statusText || row.status }}</span>
                </header>
                <p class="change-card__id">{{ row.applicantIdCardMasked || '—' }}</p>
                <p class="change-card__class">{{ row.classificationName || '—' }}</p>
                <div class="change-card__grid">
                  <div>
                    <span>家庭人数</span>
                    <strong>{{ row.familySize }} 人</strong>
                  </div>
                  <div>
                    <span>保障金（元/月）</span>
                    <strong class="mono">{{ formatMoney(row.totalGuaranteeAmount) }}</strong>
                  </div>
                  <div>
                    <span>渐退期</span>
                    <strong v-if="row.gracePeriodEndDate" class="grace">
                      {{ graceDaysText(row.graceDaysLeft) }}
                    </strong>
                    <strong v-else class="muted">—</strong>
                  </div>
                  <div>
                    <span>乡镇 / 村</span>
                    <strong class="ellipsis">{{ [row.town, row.village].filter(Boolean).join(' / ') || '—' }}</strong>
                  </div>
                </div>
              </article>
            </div>
          </template>

          <div v-else-if="!loading && !loadError" class="empty-wrap">
            <el-empty :description="emptyDescription">
              <el-button v-if="hasFilters" :icon="Refresh" @click="handleReset">清除筛选</el-button>
            </el-empty>
          </div>
        </div>

        <template #footer>
          <div v-if="items.length > 0" class="pagination">
            <span class="pagination-note">第 {{ page }} / {{ displayTotalPages }} 页 · 共 {{ totalCount }} 户</span>
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

      <div v-if="!isNarrow" class="detail-column">
        <NcCard title="选中户详情" padding="md" class="detail-card">
          <el-skeleton v-if="detailLoading && !detail" :rows="8" animated />
          <el-alert
            v-else-if="detailError"
            type="error"
            :title="detailError"
            :closable="false"
            show-icon
          >
            <el-button
              v-if="selectedId"
              link
              type="primary"
              @click="selectedId && loadDetail(selectedId)"
            >
              重新加载
            </el-button>
          </el-alert>
          <ChangeDetailPanel
            v-else-if="detail"
            :detail="detail"
            :history="history"
            :history-loading="historyLoading"
            :busy="actionBusy"
            @economic-review="handleEconomicReview"
            @member-change="openMemberChange"
            @head-change="goHeadChange"
            @household-death="goHouseholdDeath"
            @grace-confirm="goGraceConfirm"
            @verify-stop="handleVerifyStop"
            @close="closeDetail"
          />
          <el-empty v-else :image-size="60" description="选择左侧一户查看成员、分类、保障金与本月办理情况" />
        </NcCard>
      </div>
    </div>

    <NcCard
      v-if="hasKeyword"
      title="历史导入库（待建档）"
      :subtitle="importedSubtitle"
      padding="none"
      class="imported-card"
    >
      <div v-loading="importedLoading" class="imported-area">
        <el-alert
          v-if="importedError"
          type="warning"
          :title="importedError"
          :closable="false"
          show-icon
        >
          <el-button link type="primary" @click="loadImported()">重新检索</el-button>
        </el-alert>

        <el-table
          v-else-if="importedItems.length > 0"
          :data="importedItems"
          row-key="sourceId"
          class="imported-table"
        >
          <el-table-column label="姓名" min-width="110">
            <template #default="{ row }">
              <div class="person-cell">
                <strong>{{ row.applicantName || '—' }}</strong>
                <small v-if="row.matchedPersonInfo">{{ row.matchedPersonInfo }}</small>
              </div>
            </template>
          </el-table-column>

          <el-table-column label="身份证（脱敏）" min-width="138">
            <template #default="{ row }">
              <span class="mono">{{ row.applicantIdCardMasked || '—' }}</span>
            </template>
          </el-table-column>

          <el-table-column label="来源" min-width="132">
            <template #default="{ row }">
              <span>{{ row.sourceDisplay || '导入库' }}</span>
              <small v-if="row.derivedClassificationName" class="muted">
                {{ row.derivedClassificationName }}
              </small>
            </template>
          </el-table-column>

          <el-table-column label="地址" min-width="180">
            <template #default="{ row }">
              <span class="ellipsis" :title="row.address">{{ row.address || '—' }}</span>
            </template>
          </el-table-column>

          <el-table-column label="人数" width="66" align="right">
            <template #default="{ row }">{{ row.familySize || '—' }}</template>
          </el-table-column>

          <el-table-column label="保障金（元/月）" width="118" align="right">
            <template #default="{ row }">
              <span class="mono">{{ row.totalAmount ? formatMoney(row.totalAmount) : '—' }}</span>
            </template>
          </el-table-column>

          <el-table-column label="建档状态" width="94">
            <template #default="{ row }">
              <span class="status" :class="{ 'is-pending': !row.existingApplicationId }">
                {{ row.existingApplicationId ? '已建档' : '待建档' }}
              </span>
            </template>
          </el-table-column>

          <el-table-column label="操作" width="128" align="right">
            <template #default="{ row }">
              <el-button
                v-if="row.existingApplicationId"
                link
                type="primary"
                @click="openImportedExisting(row.existingApplicationId)"
              >
                查看档案
              </el-button>
              <el-button
                v-else
                link
                type="primary"
                :loading="importingKey === `${row.sourceTable}#${row.sourceId}`"
                @click="handleMigrateImported(row as ImportedArchiveItem)"
              >
                建档并办理
              </el-button>
            </template>
          </el-table-column>
        </el-table>

        <div v-else class="imported-empty">
          <p>历史导入库中未找到「{{ keyword.trim() }}」。</p>
          <p class="muted">
            导入库为农村低保 / 城市低保 / 低保边缘 / 特困供养 / 刚性支出 / 其他困难 6 类历史在保档案；
            如仍未找到，请到「数据中心 → 数据导入」核对该批数据是否已导入。
          </p>
        </div>
      </div>
    </NcCard>

    <el-drawer
      v-model="detailDrawerVisible"
      title="选中户详情"
      size="94%"
      append-to-body
      destroy-on-close
    >
      <el-skeleton v-if="detailLoading && !detail" :rows="9" animated />
      <el-alert
        v-else-if="detailError"
        type="error"
        :title="detailError"
        :closable="false"
        show-icon
      />
      <ChangeDetailPanel
        v-else-if="detail"
        :detail="detail"
        :history="history"
        :history-loading="historyLoading"
        :busy="actionBusy"
        show-close
        @economic-review="handleEconomicReview"
        @member-change="openMemberChange"
        @head-change="goHeadChange"
        @household-death="goHouseholdDeath"
        @grace-confirm="goGraceConfirm"
        @verify-stop="handleVerifyStop"
        @close="detailDrawerVisible = false"
      />
    </el-drawer>

    <MemberChangeDialog
      v-if="detail && filters"
      v-model="memberDialogVisible"
      :application-id="detail.applicationId"
      :detail="detail"
      :filters="filters"
      @success="handleMemberChangeSuccess"
      @update:model-value="handleMemberDialogVisibility"
    />
  </div>
</template>

<style scoped>
.change-page {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.summary {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 10px;
}

.filter-card {
  overflow: hidden;
}

.imported-card {
  margin-top: 2px;
}

.imported-area {
  padding: 14px 16px;
}

.imported-table {
  width: 100%;
}

.imported-empty {
  padding: 8px 2px;
  line-height: 1.7;
}

.imported-empty p {
  margin: 0;
}

.imported-empty .muted {
  margin-top: 4px;
  font-size: 12px;
}

.status.is-pending {
  color: var(--nc-amber, #9a6b00);
}

.filter-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
  padding: 14px 16px;
}

.filter-search {
  flex: 1 1 260px;
  max-width: 400px;
}

.filter-select {
  flex: 0 1 180px;
}

.filter-select--wide {
  flex-basis: 220px;
}

.filter-actions {
  display: flex;
  flex: none;
  gap: 8px;
  margin-left: auto;
}

.load-error {
  margin: 0;
}

.workspace {
  display: grid;
  grid-template-columns: minmax(0, 1.45fr) minmax(360px, 1fr);
  align-items: start;
  gap: 15px;
}

.workspace.is-narrow {
  grid-template-columns: minmax(0, 1fr);
}

.list-card {
  overflow: hidden;
}

.list-card :deep(.nc-card__foot) {
  margin-top: 0;
  padding-top: 0;
  border-top: none;
}

.list-area {
  min-height: 220px;
}

.list-skeleton {
  padding: 22px;
}

.change-table {
  width: 100%;
}

.change-table :deep(.el-table__row) {
  cursor: pointer;
}

.change-table :deep(.el-table__row.is-selected) {
  background-color: var(--nc-amber-soft);
}

.person-cell {
  display: flex;
  flex-direction: column;
  gap: 1px;
  min-width: 0;
}

.person-cell strong {
  color: var(--nc-ink);
  font-size: 13px;
  font-weight: 600;
}

.person-cell small {
  overflow: hidden;
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.mono {
  color: var(--nc-ink-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  white-space: nowrap;
}

.classification {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
}

.ellipsis {
  display: block;
  overflow: hidden;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.grace {
  color: var(--nc-amber);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  font-weight: 600;
  white-space: nowrap;
}

.grace.is-expired {
  color: var(--nc-seal);
}

.muted {
  color: var(--nc-muted);
}

.status {
  display: inline-flex;
  padding: 1px 7px;
  border: 1px solid rgba(46, 71, 56, 0.3);
  background-color: var(--nc-pine-soft);
  color: var(--nc-pine-dark);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  white-space: nowrap;
}

.change-cards {
  display: grid;
  gap: 10px;
  padding: 10px;
}

.change-card {
  padding: 13px 14px;
  border: 1px solid var(--nc-line);
  background-color: var(--nc-paper-soft);
  cursor: pointer;
  transition:
    border-color var(--nc-transition),
    box-shadow var(--nc-transition);
}

.change-card:hover {
  border-color: var(--nc-line-strong);
  box-shadow: var(--nc-shadow-sm);
}

.change-card:focus-visible {
  outline: 2px solid var(--nc-seal);
  outline-offset: 1px;
}

.change-card__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
}

.change-card__head > div {
  display: flex;
  align-items: baseline;
  gap: 8px;
  min-width: 0;
}

.change-card__head strong {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-lg);
  font-weight: 700;
}

.change-card__head span {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
}

.change-card__id,
.change-card__class {
  margin-top: 3px;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.change-card__class {
  color: rgb(33, 89, 138);
  font-weight: 600;
}

.change-card__grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px 12px;
  margin-top: 10px;
  padding-top: 9px;
  border-top: 1px solid var(--nc-line-faint);
}

.change-card__grid div {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.change-card__grid span {
  color: var(--nc-muted);
  font-size: 10px;
}

.change-card__grid strong {
  overflow: hidden;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.detail-column {
  min-width: 0;
}

.detail-card {
  position: sticky;
  top: 16px;
  max-height: calc(100vh - 120px);
  overflow-y: auto;
}

.empty-wrap {
  padding: 10px 8px;
}

.pagination {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 12px 16px;
  border-top: 1px solid var(--nc-line-faint);
}

.pagination-note {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
}

@media (max-width: 1240px) {
  .summary {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 767px) {
  .filter-bar {
    padding: 12px;
  }

  .filter-search,
  .filter-select,
  .filter-select--wide {
    flex: 1 1 100%;
    max-width: none;
  }

  .filter-actions {
    flex: 1 1 100%;
    margin-left: 0;
  }

  .filter-actions .el-button {
    flex: 1;
  }

  .pagination {
    justify-content: center;
    padding: 12px;
  }

  .pagination-note {
    width: 100%;
    text-align: center;
  }
}
</style>
