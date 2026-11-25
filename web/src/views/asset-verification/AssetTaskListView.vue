<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ArrowLeft, Printer, Refresh, Search } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { listAssetTasks } from '@/api/assetVerification'
import { useAssetPrintDialog } from '@/api/assetPrint'
import { getErrorMessage } from '@/api/http'
import type { AssetCheckStatus, AssetTaskListItem } from '@/types/assetVerification'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStat from '@/components/NcStat.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import AssetTaskDetailDrawer from './AssetTaskDetailDrawer.vue'
import AssetTaskTable from './AssetTaskTable.vue'
import { ASSET_STATUS_OPTIONS } from './status'

const router = useRouter()
const isNarrow = useMediaQuery('(max-width: 960px)')

const keyword = ref('')
const status = ref<AssetCheckStatus | ''>('')
const dateRange = ref<[string, string] | null>(null)
const page = ref(1)
const pageSize = ref(20)

const items = ref<AssetTaskListItem[]>([])
const totalCount = ref(0)
const totalPages = ref(0)
const loading = ref(false)
const loadError = ref('')

const drawerVisible = ref(false)
const activeTaskId = ref<number | null>(null)

/* ── 打印授权书 / 核查报告 ─────────────────────────────────────── */
const printVisible = ref(false)
const printTaskId = ref<number | null>(null)
const {
  templates: printTemplates,
  templatesLoading: printTemplatesLoading,
  templatesError: printTemplatesError,
  printers: printPrinters,
  printersLoading: printPrintersLoading,
  printersError: printPrintersError,
  submitting: printSubmitting,
  templateId: printTemplateId,
  printerId: printPrinterId,
  mode: printMode,
  copies: printCopies,
  isDuplex: printIsDuplex,
  canSubmit: printCanSubmit,
  loadTemplates: loadPrintTemplates,
  loadPrinters: loadPrintPrinters,
  open: openPrintDialog,
  submit: submitPrintJob,
} = useAssetPrintDialog()

let requestTicket = 0

const hasFilters = computed(
  () => keyword.value.trim().length > 0 || status.value !== '' || dateRange.value !== null,
)
const displayTotalPages = computed(() => Math.max(totalPages.value, 1))
const paginationLayout = computed(() =>
  isNarrow.value ? 'prev, pager, next' : 'total, sizes, prev, pager, next, jumper',
)

const listSubtitle = computed(() => {
  if (loadError.value) return '数据加载失败'
  if (loading.value && items.value.length === 0) return '正在读取核对任务…'
  const suffix = hasFilters.value ? ' · 已应用筛选' : ''
  return `共 ${totalCount.value} 条记录${suffix}`
})

const emptyDescription = computed(() =>
  hasFilters.value ? '没有找到匹配的核对任务' : '暂无家庭经济状况核对任务',
)

async function load(): Promise<void> {
  const ticket = ++requestTicket
  loading.value = true
  loadError.value = ''

  try {
    const response = await listAssetTasks({
      page: page.value,
      pageSize: pageSize.value,
      keyword: keyword.value.trim() || undefined,
      status: status.value || undefined,
      dateFrom: dateRange.value?.[0] || undefined,
      dateTo: dateRange.value?.[1] || undefined,
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
    loadError.value = getErrorMessage(error, '核对任务加载失败')
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
  dateRange.value = null
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

function openDetail(item: AssetTaskListItem): void {
  activeTaskId.value = item.id
  drawerVisible.value = true
}

async function openPrint(item?: AssetTaskListItem): Promise<void> {
  if (items.value.length === 0) {
    ElMessage.warning('暂无可打印的核对任务')
    return
  }

  printTaskId.value = item?.id ?? items.value[0]?.id ?? null
  printVisible.value = true
  await openPrintDialog()
}

async function handlePrintSubmit(): Promise<void> {
  if (printTaskId.value === null) {
    ElMessage.warning('请选择要打印的核对任务')
    return
  }

  try {
    await submitPrintJob(printTaskId.value)
    ElMessage.success('已推送到 PC 端')
    printVisible.value = false
    await load()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '推送打印失败'))
  }
}

onMounted(() => {
  void load()
})
</script>

<template>
  <div class="nc-page asset-task-page">
    <NcPageHeader
      title="快速经济核对"
      description="查看快速经济核对任务、报告状态与同批次家庭成员。"
      kicker="家庭经济状况核对 · 快速核对任务"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="router.push('/asset-verification/new')">
          返回快速经济核对
        </el-button>
        <el-button :icon="Printer" @click="openPrint()">打印授权书 / 核查报告</el-button>
        <el-button :icon="Refresh" :loading="loading" @click="load()">刷新</el-button>
      </template>
    </NcPageHeader>

    <div class="summary">
      <NcStat label="核对任务总数" :value="totalCount" tone="ink" compact />
      <NcStat label="本页记录" :value="items.length" tone="pine" compact />
      <NcStat label="当前页" :value="`${page} / ${displayTotalPages}`" tone="seal" compact />
    </div>

    <NcCard padding="none" class="filter-card">
      <form class="filter-bar" @submit.prevent="handleSearch">
        <el-input
          v-model="keyword"
          class="filter-search"
          clearable
          :prefix-icon="Search"
          placeholder="搜索姓名 / 身份证 / 户主身份证"
          @clear="handleSearch"
        />

        <el-select
          v-model="status"
          class="filter-status"
          clearable
          placeholder="全部状态"
          @change="handleStatusChange"
        >
          <el-option
            v-for="option in ASSET_STATUS_OPTIONS"
            :key="option.value"
            :label="option.label"
            :value="option.value"
          />
        </el-select>

        <el-date-picker
          v-model="dateRange"
          class="filter-date"
          type="daterange"
          value-format="YYYY-MM-DD"
          range-separator="至"
          start-placeholder="申请开始日期"
          end-placeholder="申请结束日期"
          unlink-panels
          @change="handleSearch"
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

    <NcCard title="核对任务" :subtitle="listSubtitle" padding="none" class="list-card">
      <div
        v-loading="loading && items.length > 0"
        class="list-area"
        :class="{ 'is-cards': isNarrow }"
      >
        <el-skeleton v-if="loading && items.length === 0" :rows="5" animated class="list-skeleton" />

        <AssetTaskTable
          v-else-if="items.length > 0"
          :items="items"
          :narrow="isNarrow"
          @select="openDetail"
        />

        <div v-else-if="!loadError" class="empty-wrap">
          <el-empty :description="emptyDescription">
            <el-button v-if="hasFilters" :icon="Refresh" @click="handleReset">清除筛选</el-button>
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

    <el-dialog
      v-model="printVisible"
      :width="isNarrow ? '94%' : '520px'"
      title="打印授权书 / 核查报告"
      append-to-body
      class="asset-print-dialog"
    >
      <el-form label-position="top" class="print-form">
        <el-form-item label="核对任务">
          <el-select v-model="printTaskId" filterable placeholder="选择要打印的核对任务">
            <el-option
              v-for="item in items"
              :key="item.id"
              :label="`${item.archiveName || '未命名'}（${item.batchId || '无批次号'}）`"
              :value="item.id"
            />
          </el-select>
        </el-form-item>

        <el-form-item label="打印机">
          <el-select
            v-model="printPrinterId"
            :loading="printPrintersLoading"
            :disabled="printPrintersLoading"
            filterable
            clearable
            placeholder="选择已绑定的打印机"
          >
            <el-option
              v-for="printer in printPrinters"
              :key="printer.id"
              :label="`${printer.deviceName} · ${printer.printerName} · ${printer.online ? '在线' : '离线'}`"
              :value="printer.id"
            />
          </el-select>
          <p v-if="printPrintersError" class="form-hint is-error">
            {{ printPrintersError }}
            <el-button link type="primary" @click="loadPrintPrinters(true)">重试</el-button>
          </p>
          <p v-else-if="printPrintersLoading" class="form-hint">正在读取已绑定打印机…</p>
          <p v-else-if="printPrinters.length === 0" class="form-hint is-error">
            当前账号未绑定打印机，请先在本机 Agent 绑定；预览模式可继续使用。
          </p>
          <p v-else-if="printMode === 'print' && !printPrinterId" class="form-hint is-error">
            打印模式需要选择打印机
          </p>
          <p v-else-if="printMode === 'preview' && !printPrinterId" class="form-hint">
            预览模式可不选打印机
          </p>
        </el-form-item>

        <el-form-item label="打印模板">
          <el-select
            v-model="printTemplateId"
            :loading="printTemplatesLoading"
            :disabled="printTemplatesLoading"
            placeholder="选择模板"
          >
            <el-option
              v-for="template in printTemplates"
              :key="template.id"
              :label="template.name"
              :value="template.id"
            />
          </el-select>
          <p v-if="printTemplatesError" class="form-hint is-error">
            {{ printTemplatesError }}
            <el-button link type="primary" @click="loadPrintTemplates(true)">重试</el-button>
          </p>
          <p v-else-if="!printTemplatesLoading && printTemplates.length === 0" class="form-hint">
            当前业务域暂无可用的打印模板
          </p>
        </el-form-item>

        <el-form-item label="输出方式">
          <el-radio-group v-model="printMode">
            <el-radio-button value="preview">预览</el-radio-button>
            <el-radio-button value="print">打印</el-radio-button>
          </el-radio-group>
        </el-form-item>

        <div class="print-options">
          <el-form-item label="份数">
            <el-input-number v-model="printCopies" :min="1" :max="99" />
          </el-form-item>
          <el-form-item label="单双面">
            <el-radio-group v-model="printIsDuplex">
              <el-radio-button :value="false">单面</el-radio-button>
              <el-radio-button :value="true">双面</el-radio-button>
            </el-radio-group>
          </el-form-item>
        </div>
      </el-form>

      <template #footer>
        <el-button @click="printVisible = false">取消</el-button>
        <el-button
          type="primary"
          :loading="printSubmitting"
          :disabled="!printCanSubmit"
          @click="handlePrintSubmit"
        >
          确定推送
        </el-button>
      </template>
    </el-dialog>

    <AssetTaskDetailDrawer v-model="drawerVisible" :task-id="activeTaskId" />
  </div>
</template>

<style scoped>
.asset-task-page {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.summary {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 10px;
}

.filter-card {
  overflow: hidden;
}

.filter-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
  padding: 14px 16px;
}

.filter-search {
  flex: 1 1 250px;
  max-width: 400px;
}

.filter-status {
  width: 168px;
}

.filter-date {
  flex: 1 1 300px;
  max-width: 360px;
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

.list-area.is-cards {
  padding: 10px;
}

.list-skeleton {
  padding: 22px;
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

.print-form :deep(.el-select) {
  width: 100%;
}

.print-options {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.form-hint {
  margin: 6px 0 0;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.5;
}

.form-hint.is-error {
  color: var(--el-color-danger);
}

@media (max-width: 767px) {
  .print-options {
    grid-template-columns: minmax(0, 1fr);
  }
}

@media (max-width: 767px) {
  .filter-bar {
    padding: 12px;
  }

  .filter-search {
    flex: 1 1 100%;
    max-width: none;
  }

  .filter-status {
    flex: 1 1 calc(50% - 5px);
    width: auto;
  }

  .filter-date {
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
