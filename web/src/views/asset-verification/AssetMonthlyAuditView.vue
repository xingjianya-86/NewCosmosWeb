<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  Document,
  FolderOpened,
  Picture,
  Printer,
  Refresh,
  Search,
  Upload,
} from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import {
  ASSET_PHOTO_CATEGORY_OPTIONS,
  fetchCheckReportContent,
  getMonthlyAuditOverview,
  listCheckPhotos,
  listCheckReports,
  listMonthlyAuditTasks,
  photoCategoryText,
  photoDataUrl,
  rejectAssetCheck,
  uploadCheckPhoto,
  uploadCheckReport,
  type AssetCheckPhoto,
  type AssetReportVersion,
  type MonthlyAuditOverview,
} from '@/api/assetMonthlyAudit'
import type { AssetCheckStatus, AssetTaskListItem } from '@/types/assetVerification'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStat from '@/components/NcStat.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import AssetStatusTag from './AssetStatusTag.vue'
import AssetTaskDetailDrawer from './AssetTaskDetailDrawer.vue'
import { formatDate, relationshipText, textOrDash } from './format'
import { ASSET_STATUS_OPTIONS } from './status'

const isNarrow = useMediaQuery('(max-width: 960px)')

const now = new Date()
const year = ref(now.getFullYear())
const month = ref(now.getMonth() + 1)

const yearOptions = computed(() => {
  const base = new Date().getFullYear()
  return Array.from({ length: 11 }, (_, index) => base - 5 + index)
})
const monthOptions = Array.from({ length: 12 }, (_, index) => index + 1)

const overview = ref<MonthlyAuditOverview | null>(null)
const overviewLoading = ref(false)
const overviewNote = ref('')

const keyword = ref('')
const status = ref<AssetCheckStatus | ''>('')
const page = ref(1)
const pageSize = ref(20)

const items = ref<AssetTaskListItem[]>([])
const totalCount = ref(0)
const totalPages = ref(0)
const listLoading = ref(false)
const listError = ref('')

const selectedTask = ref<AssetTaskListItem | null>(null)
const drawerVisible = ref(false)
const activeTaskId = ref<number | null>(null)

let overviewTicket = 0
let listTicket = 0

/* ── 核查照片 ─────────────────────────────────────────────────── */
const photoDialogVisible = ref(false)
const photoDialogTask = ref<AssetTaskListItem | null>(null)
const photos = ref<AssetCheckPhoto[]>([])
const photosLoading = ref(false)
const photosError = ref('')
const photoUploading = ref(false)
const photoCategory = ref('PERSON_PHOTO')
const photoInputRef = ref<HTMLInputElement | null>(null)
let photoTicket = 0

/* ── 报告上传 / 版本 / 预览 ───────────────────────────────────── */
const reportInputRef = ref<HTMLInputElement | null>(null)
const reportUploading = ref(false)
const reports = ref<AssetReportVersion[]>([])
const reportsLoading = ref(false)
const reportsError = ref('')
const activeReportId = ref<number | null>(null)
const previewLoading = ref(false)
const previewError = ref('')
const previewKind = ref<'none' | 'pdf' | 'image'>('none')
const previewUrl = ref('')
let reportsTicket = 0
let previewTicket = 0

/* ── 批量上传 ─────────────────────────────────────────────────── */
const batchInputRef = ref<HTMLInputElement | null>(null)
const batchRunning = ref(false)
const batchTotal = ref(0)
const batchDone = ref(0)
const batchFailed = ref(0)
const batchMessage = ref('')
let batchAbort: AbortController | null = null
let batchStopRequested = false

const rejectingId = ref<number | null>(null)

const pageLoading = computed(() => listLoading.value || overviewLoading.value)
const hasFilters = computed(() => keyword.value.trim().length > 0 || status.value !== '')
const displayTotalPages = computed(() => Math.max(totalPages.value, 1))
const paginationLayout = computed(() =>
  isNarrow.value ? 'prev, pager, next' : 'total, sizes, prev, pager, next, jumper',
)

const monthLabel = computed(() => `${year.value} 年 ${month.value} 月`)
const periodText = computed(() => {
  const lastDay = new Date(year.value, month.value, 0).getDate()
  const prefix = `${year.value}-${pad(month.value)}`
  return `统计期间：${prefix}-01 ~ ${prefix}-${pad(lastDay)}`
})

const overviewCards = computed<
  Array<{ label: string; value: string; tone: 'ink' | 'seal' | 'pine' | 'amber' }>
>(() => {
  const value = overview.value
  return [
    { label: '已提交', value: value ? String(value.submitted) : '—', tone: 'ink' },
    { label: '有报告未建档', value: value ? String(value.hasReport) : '—', tone: 'amber' },
    { label: '已建档', value: value ? String(value.archived) : '—', tone: 'pine' },
    { label: '不予认定', value: value ? String(value.rejected) : '—', tone: 'seal' },
  ]
})

const overviewTotal = computed(() =>
  overview.value ? `总计 ${overview.value.total} 户` : '总计 — 户',
)

const listSubtitle = computed(() => {
  if (listError.value) return '数据加载失败'
  if (listLoading.value && items.value.length === 0) return '正在读取月度待核对人员…'
  const suffix = hasFilters.value ? ' · 已应用筛选' : ''
  return `${monthLabel.value} · 共 ${totalCount.value} 条记录${suffix}`
})

const emptyDescription = computed(() =>
  hasFilters.value ? '没有找到匹配的待核对人员' : `${monthLabel.value}暂无待核对人员`,
)

const activeReport = computed(
  () => reports.value.find((report) => report.id === activeReportId.value) ?? null,
)

const previewSubtitle = computed(() => {
  if (!selectedTask.value) return '选中待核对人员后查看本户核查报告'
  if (reportsLoading.value) return '正在读取报告版本…'
  if (!activeReport.value) return '本户暂无报告文件'
  const current = activeReport.value.isCurrent ? ' · 当前版本' : ''
  return `v${activeReport.value.version} · ${textOrDash(activeReport.value.fileName)}${current}`
})

const photoSubject = computed(() => {
  const task = photoDialogTask.value
  if (!task) return ''
  return `${textOrDash(task.archiveName)} · ${textOrDash(task.idCardMasked)}`
})

const batchProgress = computed(() => {
  const finished = batchDone.value + batchFailed.value
  if (batchTotal.value <= 0) return 0
  return Math.min(100, Math.round((finished / batchTotal.value) * 100))
})

const batchSummary = computed(() => {
  if (batchTotal.value === 0) return '尚未选择文件夹'
  return `已上传 ${batchDone.value} / ${batchTotal.value} · 失败 ${batchFailed.value}`
})

function pad(value: number): string {
  return String(value).padStart(2, '0')
}

async function loadOverview(): Promise<void> {
  const ticket = ++overviewTicket
  overviewLoading.value = true
  overviewNote.value = ''

  try {
    const result = await getMonthlyAuditOverview(year.value, month.value)
    if (ticket !== overviewTicket) return

    if (!result.complete) {
      overview.value = null
      overviewNote.value = `当月共 ${result.totalCount} 条任务，超过 ${result.scannedCount} 条的概览聚合上限，统计暂以“—”显示。`
      return
    }

    overview.value = result.overview
  } catch (error) {
    if (ticket !== overviewTicket) return
    overview.value = null
    overviewNote.value = getErrorMessage(error, '核对概览加载失败')
  } finally {
    if (ticket === overviewTicket) overviewLoading.value = false
  }
}

async function loadList(): Promise<void> {
  const ticket = ++listTicket
  listLoading.value = true
  listError.value = ''

  try {
    const response = await listMonthlyAuditTasks({
      year: year.value,
      month: month.value,
      page: page.value,
      pageSize: pageSize.value,
      keyword: keyword.value.trim() || undefined,
      status: status.value || undefined,
    })
    if (ticket !== listTicket) return

    items.value = response.items ?? []
    page.value = response.pageIndex || page.value
    pageSize.value = response.pageSize || pageSize.value
    totalCount.value = response.totalCount ?? 0
    totalPages.value = response.totalPages ?? 0

    if (selectedTask.value && !items.value.some((item) => item.id === selectedTask.value?.id)) {
      selectedTask.value = null
    }
  } catch (error) {
    if (ticket !== listTicket) return
    items.value = []
    totalCount.value = 0
    totalPages.value = 0
    selectedTask.value = null
    listError.value = getErrorMessage(error, '月度待核对人员加载失败')
  } finally {
    if (ticket === listTicket) listLoading.value = false
  }
}

function loadAll(): void {
  void loadList()
  void loadOverview()
}

function handlePeriodChange(): void {
  page.value = 1
  selectedTask.value = null
  loadAll()
}

function handleSearch(): void {
  page.value = 1
  void loadList()
}

function handleStatusChange(): void {
  page.value = 1
  selectedTask.value = null
  void loadList()
}

function handleReset(): void {
  keyword.value = ''
  status.value = ''
  page.value = 1
  selectedTask.value = null
  void loadList()
}

function handlePageChange(value: number): void {
  page.value = value
  void loadList()
}

function handleSizeChange(value: number): void {
  pageSize.value = value
  page.value = 1
  void loadList()
}

function selectTask(item: AssetTaskListItem): void {
  selectedTask.value = item
}

function openDetail(item: AssetTaskListItem): void {
  activeTaskId.value = item.id
  drawerVisible.value = true
}

function formatBytes(size: number): string {
  if (!Number.isFinite(size) || size <= 0) return '—'
  if (size < 1024) return `${Math.round(size)} B`
  if (size < 1024 * 1024) return `${(size / 1024).toFixed(1)} KB`
  return `${(size / 1024 / 1024).toFixed(1)} MB`
}

function isReportFile(file: File): boolean {
  if (file.type === 'application/pdf') return true
  if (file.type === 'image/jpeg' || file.type === 'image/png' || file.type === 'image/webp') {
    return true
  }
  return /\.(pdf|jpe?g|jpe|png|webp)$/i.test(file.name)
}

/* ── 照片对话框 ───────────────────────────────────────────────── */

async function loadPhotos(): Promise<void> {
  const task = photoDialogTask.value
  if (!task) return

  const ticket = ++photoTicket
  photosLoading.value = true
  photosError.value = ''

  try {
    const list = await listCheckPhotos(task.id)
    if (ticket !== photoTicket) return
    photos.value = list
  } catch (error) {
    if (ticket !== photoTicket) return
    photos.value = []
    photosError.value = getErrorMessage(error, '照片列表加载失败')
  } finally {
    if (ticket === photoTicket) photosLoading.value = false
  }
}

function openPhotoDialog(): void {
  const task = selectedTask.value
  if (!task) {
    ElMessage.warning('请先在人员列表选中一人')
    return
  }
  photoDialogTask.value = task
  photoCategory.value = 'PERSON_PHOTO'
  photoDialogVisible.value = true
  void loadPhotos()
}

function handlePhotoDialogClosed(): void {
  photoTicket += 1
  photos.value = []
  photosError.value = ''
  photosLoading.value = false
  photoUploading.value = false
  photoDialogTask.value = null
}

function triggerPhotoPick(): void {
  if (photoUploading.value || !photoDialogTask.value) return
  photoInputRef.value?.click()
}

async function handlePhotoFileChange(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file) return

  const task = photoDialogTask.value
  if (!task) return
  if (!file.type.startsWith('image/')) {
    ElMessage.error('请选择图片文件')
    return
  }

  photoUploading.value = true
  try {
    await uploadCheckPhoto(task.id, file, photoCategory.value)
    ElMessage.success('照片上传成功')
    await loadPhotos()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '照片上传失败'))
  } finally {
    photoUploading.value = false
  }
}

/* ── 报告版本与预览 ───────────────────────────────────────────── */

function releasePreviewUrl(): void {
  if (previewUrl.value) {
    URL.revokeObjectURL(previewUrl.value)
    previewUrl.value = ''
  }
}

function resolvePreviewKind(contentType: string, ...names: string[]): 'pdf' | 'image' | 'none' {
  const type = (contentType ?? '').toLowerCase()
  if (type.includes('pdf')) return 'pdf'
  if (type.startsWith('image/')) return 'image'

  const name = names.find((value) => (value ?? '').trim().length > 0)?.toLowerCase() ?? ''
  if (name.endsWith('.pdf')) return 'pdf'
  if (/\.(png|jpe?g|gif|bmp|webp)$/.test(name)) return 'image'
  return 'none'
}

async function loadPreview(reportId: number | null): Promise<void> {
  const ticket = ++previewTicket
  releasePreviewUrl()
  previewKind.value = 'none'
  previewError.value = ''
  previewLoading.value = false

  if (reportId === null) return

  const report = reports.value.find((item) => item.id === reportId) ?? null
  previewLoading.value = true

  try {
    const content = await fetchCheckReportContent(reportId)
    if (ticket !== previewTicket) return

    const kind = resolvePreviewKind(content.contentType, content.fileName, report?.fileName ?? '')
    if (kind === 'none') {
      previewError.value = '该报告文件格式暂不支持在线预览，请下载后查看'
      return
    }

    previewUrl.value = URL.createObjectURL(content.blob)
    previewKind.value = kind
  } catch (error) {
    if (ticket !== previewTicket) return
    if ((error as Error | null)?.name === 'AbortError') return
    previewError.value = getErrorMessage(error, '报告预览加载失败')
  } finally {
    if (ticket === previewTicket) previewLoading.value = false
  }
}

async function loadReports(checkId: number): Promise<void> {
  const ticket = ++reportsTicket
  reportsLoading.value = true
  reportsError.value = ''

  try {
    const list = await listCheckReports(checkId)
    if (ticket !== reportsTicket) return

    reports.value = list
    const current = list.find((report) => report.isCurrent) ?? list[0] ?? null
    activeReportId.value = current?.id ?? null
    void loadPreview(activeReportId.value)
  } catch (error) {
    if (ticket !== reportsTicket) return
    reports.value = []
    activeReportId.value = null
    previewTicket += 1
    releasePreviewUrl()
    previewKind.value = 'none'
    previewError.value = ''
    previewLoading.value = false
    reportsError.value = getErrorMessage(error, '报告版本加载失败')
  } finally {
    if (ticket === reportsTicket) reportsLoading.value = false
  }
}

function selectReportVersion(report: AssetReportVersion): void {
  if (report.id === activeReportId.value) return
  activeReportId.value = report.id
  void loadPreview(report.id)
}

function triggerReportPick(): void {
  if (!selectedTask.value) {
    ElMessage.warning('请先在人员列表选中一人')
    return
  }
  if (reportUploading.value) return
  reportInputRef.value?.click()
}

async function handleReportFileChange(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file) return

  const task = selectedTask.value
  if (!task) return
  if (!isReportFile(file)) {
    ElMessage.error('请选择 PDF 或图片（jpg/png/webp）格式的报告文件')
    return
  }

  reportUploading.value = true
  try {
    await uploadCheckReport(task.id, file)
    ElMessage.success('报告已保存，正在刷新版本')
    if (selectedTask.value?.id === task.id) {
      await loadReports(task.id)
    }
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '报告保存失败'))
  } finally {
    reportUploading.value = false
  }
}

/* ── 驳回（不予认定） ─────────────────────────────────────────── */

async function handleReject(item: AssetTaskListItem): Promise<void> {
  if (item.status !== '1' || rejectingId.value !== null) return

  try {
    await ElMessageBox.confirm(
      `确认将“${textOrDash(item.archiveName)}”标记为不予认定？此操作不可撤销。`,
      '驳回确认',
      {
        confirmButtonText: '确认驳回',
        cancelButtonText: '取消',
        type: 'warning',
      },
    )
  } catch {
    return
  }

  rejectingId.value = item.id
  try {
    await rejectAssetCheck(item.id)
    ElMessage.success('已标记为不予认定')
    if (selectedTask.value?.id === item.id) {
      selectedTask.value = null
    }
    loadAll()
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '驳回失败'))
  } finally {
    rejectingId.value = null
  }
}

/* ── 批量上传报告 ─────────────────────────────────────────────── */

function triggerBatchPick(): void {
  if (!selectedTask.value) {
    ElMessage.warning('请先选中人员，再选择该人员的报告文件夹')
    return
  }
  if (batchRunning.value) return
  batchInputRef.value?.click()
}

function cancelBatchUpload(): void {
  if (!batchRunning.value) return
  batchStopRequested = true
  batchMessage.value = '正在取消上传…'
  batchAbort?.abort()
}

async function handleBatchFileChange(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement
  const files = Array.from(input.files ?? [])
  input.value = ''
  if (files.length === 0) return

  const task = selectedTask.value
  if (!task) return

  const validFiles = files.filter(isReportFile)
  if (validFiles.length === 0) {
    ElMessage.warning('所选文件夹中没有 PDF 或图片（jpg/png/webp）文件')
    return
  }

  batchRunning.value = true
  batchStopRequested = false
  batchTotal.value = validFiles.length
  batchDone.value = 0
  batchFailed.value = 0
  batchMessage.value =
    files.length > validFiles.length
      ? `已忽略 ${files.length - validFiles.length} 个非 PDF/图片文件`
      : ''

  const controller = new AbortController()
  batchAbort = controller

  try {
    for (const file of validFiles) {
      if (batchStopRequested) break

      try {
        await uploadCheckReport(task.id, file, { signal: controller.signal })
        batchDone.value += 1
      } catch (error) {
        if (batchStopRequested || controller.signal.aborted) break
        batchFailed.value += 1
        if (!batchMessage.value) {
          batchMessage.value = `“${file.name}”上传失败：${getErrorMessage(error, '未知错误')}`
        }
      }
    }
  } finally {
    batchRunning.value = false
    batchAbort = null

    if (batchStopRequested) {
      batchMessage.value = `已取消：成功 ${batchDone.value} 个，失败 ${batchFailed.value} 个`
      ElMessage.info(batchMessage.value)
    } else if (batchFailed.value > 0) {
      batchMessage.value = `批量上传完成：成功 ${batchDone.value} 个，失败 ${batchFailed.value} 个`
      ElMessage.warning(batchMessage.value)
    } else {
      batchMessage.value = `批量上传完成：共 ${batchDone.value} 个`
      ElMessage.success(batchMessage.value)
    }

    if (batchDone.value > 0 && selectedTask.value?.id === task.id) {
      await loadReports(task.id)
    }
  }
}

watch(selectedTask, (task) => {
  reportsTicket += 1
  previewTicket += 1
  reports.value = []
  reportsError.value = ''
  reportsLoading.value = false
  activeReportId.value = null
  previewError.value = ''
  previewKind.value = 'none'
  previewLoading.value = false
  releasePreviewUrl()
  batchMessage.value = ''

  if (task) {
    void loadReports(task.id)
  }
})

onBeforeUnmount(() => {
  releasePreviewUrl()
  batchAbort?.abort()
})

onMounted(() => {
  loadAll()
})
</script>

<template>
  <div class="nc-page asset-monthly-page">
    <NcPageHeader
      title="月度审核"
      description="按月核查资产状况与核对报告：任务名单、照片归档、报告保存与版本预览、批量上传与不予认定均已接入；报告打印待开放。"
      kicker="家庭经济状况核对 · 月度审核"
    >
      <template #actions>
        <el-button :icon="Refresh" :loading="pageLoading" @click="loadAll">刷新</el-button>
      </template>
    </NcPageHeader>

    <el-alert
      class="readonly-note"
      type="info"
      :closable="false"
      show-icon
      title="照片归档、报告保存/版本预览、批量上传与驳回已接入；PDF 报告打印暂未开放（按钮禁用）。"
    />

    <div class="audit-layout">
      <div class="audit-main">
        <NcCard title="报表周期" subtitle="按申请日期所在年月筛选月度任务">
          <div class="period-row">
            <el-select v-model="year" class="period-select" @change="handlePeriodChange">
              <el-option
                v-for="option in yearOptions"
                :key="option"
                :label="`${option} 年`"
                :value="option"
              />
            </el-select>
            <el-select v-model="month" class="period-select" @change="handlePeriodChange">
              <el-option
                v-for="option in monthOptions"
                :key="option"
                :label="`${option} 月`"
                :value="option"
              />
            </el-select>
          </div>
          <p class="period-text">{{ periodText }}</p>
          <p class="card-note">
            Web 月度接口按申请日期所在自然月统计；旧桌面端的 A 线周期（上月 11 日 ~ 本月 10
            日）不在此页生效。
          </p>
        </NcCard>

        <NcCard title="核对概览" subtitle="按户主身份证号去重聚合，统计按户">
          <div v-loading="overviewLoading" class="overview-body">
            <div class="overview-grid">
              <NcStat
                v-for="card in overviewCards"
                :key="card.label"
                :label="card.label"
                :value="card.value"
                :tone="card.tone"
              />
            </div>

            <div class="overview-foot">
              <span class="overview-total">{{ overviewTotal }}</span>
              <span class="card-note">统计自月度任务列表聚合，与名单“按人”计数口径不同。</span>
            </div>

            <el-alert
              v-if="overviewNote"
              class="overview-note"
              type="warning"
              :title="overviewNote"
              :closable="false"
              show-icon
            >
              <el-button link type="primary" @click="loadOverview">重新统计</el-button>
            </el-alert>
          </div>
        </NcCard>

        <NcCard title="待核对人员" :subtitle="listSubtitle" padding="none" class="people-card">
          <template #actions>
            <span class="count-chip">{{ totalCount }}</span>
            <span :title="selectedTask ? '查看并上传该人员照片' : '请先选中人员'">
              <el-button
                size="small"
                :icon="Picture"
                :disabled="!selectedTask || photoUploading"
                @click="openPhotoDialog"
              >
                照片
              </el-button>
            </span>
            <span :title="selectedTask ? '上传新的报告版本' : '请先选中人员'">
              <el-button
                size="small"
                :loading="reportUploading"
                :disabled="!selectedTask || reportUploading"
                @click="triggerReportPick"
              >
                保存报告
              </el-button>
            </span>
            <span class="pending-wrap" title="PDF 报告打印待接入">
              <el-button size="small" :icon="Printer" title="PDF 报告打印待接入" disabled>
                打印报告
              </el-button>
            </span>
          </template>

          <el-alert
            v-if="listError"
            class="people-error"
            type="error"
            :title="listError"
            :closable="false"
            show-icon
          >
            <el-button link type="primary" @click="loadList">重新加载</el-button>
          </el-alert>

          <form class="people-toolbar" @submit.prevent="handleSearch">
            <el-input
              v-model="keyword"
              class="people-search"
              clearable
              :prefix-icon="Search"
              placeholder="搜索姓名/身份证号"
              @clear="handleSearch"
            />
            <el-select
              v-model="status"
              class="people-status"
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
            <el-button type="primary" :icon="Search" native-type="submit" :loading="listLoading">
              搜索
            </el-button>
            <span class="pending-wrap" title="待接入">
              <el-button :icon="FolderOpened" disabled>打开输出目录</el-button>
            </span>
            <el-button v-if="hasFilters" text native-type="button" @click="handleReset">
              重置
            </el-button>
          </form>

          <div v-loading="listLoading && items.length > 0" class="people-list">
            <el-skeleton
              v-if="listLoading && items.length === 0"
              :rows="4"
              animated
              class="list-skeleton"
            />

            <template v-else-if="items.length > 0">
              <article
                v-for="item in items"
                :key="item.id"
                class="person-row"
                :class="{ 'is-active': selectedTask?.id === item.id }"
                role="button"
                tabindex="0"
                @click="selectTask(item)"
                @keydown.enter="selectTask(item)"
              >
                <div class="person-ident">
                  <strong class="person-name">{{ textOrDash(item.archiveName) }}</strong>
                  <span class="person-id mono">{{ textOrDash(item.idCardMasked) }}</span>
                </div>

                <div class="person-meta">
                  <span>
                    {{ relationshipText(item.relationship) }}
                    <em v-if="item.isHead" class="head-badge">户主</em>
                  </span>
                  <span class="batch">{{ textOrDash(item.batchId) }}</span>
                  <span>申请 {{ formatDate(item.applicationDate) }}</span>
                </div>

                <div class="person-status">
                  <AssetStatusTag :status="item.status" :status-text="item.statusText" />
                </div>

                <div class="person-actions">
                  <el-button
                    v-if="item.status === '1'"
                    size="small"
                    :loading="rejectingId === item.id"
                    :disabled="rejectingId !== null"
                    @click.stop="handleReject(item)"
                  >
                    驳回
                  </el-button>
                  <el-button link type="primary" @click.stop="openDetail(item)">详情</el-button>
                </div>
              </article>
            </template>

            <div v-else-if="!listError" class="empty-wrap">
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
                {{ monthLabel }} · 第 {{ page }} / {{ displayTotalPages }} 页 · 共
                {{ totalCount }} 条
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

        <NcCard
          title="报告上传"
          :subtitle="
            selectedTask
              ? `批量上传到：${textOrDash(selectedTask.archiveName)}（${textOrDash(selectedTask.idCardMasked)}）`
              : '请先在人员列表选中一人，再选择其报告文件夹'
          "
        >
          <div class="upload-stats">
            <div class="upload-stat">
              <span class="upload-label">已上传</span>
              <strong class="upload-value">{{ batchDone }}</strong>
              <span class="card-note">本次成功数量</span>
            </div>
            <div class="upload-stat upload-stat--total">
              <span class="upload-label">总数</span>
              <strong class="upload-value">{{ batchTotal }}</strong>
              <span class="card-note">本次待上传文件</span>
            </div>
            <div class="upload-stat upload-stat--danger">
              <span class="upload-label">失败</span>
              <strong class="upload-value">{{ batchFailed }}</strong>
              <span class="card-note">本次失败数量</span>
            </div>
          </div>

          <span
            class="pending-block"
            :title="selectedTask ? '选择包含报告文件的文件夹' : '请先选中人员'"
          >
            <el-button
              type="primary"
              class="upload-button"
              :icon="Upload"
              :loading="batchRunning"
              :disabled="!selectedTask || batchRunning"
              @click="triggerBatchPick"
            >
              选择文件夹并批量上传
            </el-button>
          </span>

          <div class="upload-options">
            <span class="card-note">
              支持 PDF / JPG / PNG / WEBP，子文件夹会一并纳入；逐个上传，不并行。
            </span>
            <el-button size="small" :disabled="!batchRunning" @click="cancelBatchUpload">
              取消上传
            </el-button>
          </div>

          <div class="upload-progress">
            <div
              class="upload-progress-track"
              role="progressbar"
              aria-label="上传进度"
              aria-valuemin="0"
              aria-valuemax="100"
              :aria-valuenow="batchProgress"
            >
              <span class="upload-progress-fill" :style="{ width: `${batchProgress}%` }" />
            </div>
            <span class="card-note upload-progress-text">
              {{ batchSummary }} · {{ batchProgress }}%
            </span>
          </div>

          <p v-if="batchMessage" class="card-note upload-footnote">{{ batchMessage }}</p>
          <p v-else class="card-note upload-footnote">
            上传前请确认已选中人员；报告按户归档，会归属到本户核对记录。
          </p>
        </NcCard>

        <NcCard v-if="selectedTask" title="比对信息">
          <p class="match-line">
            申请人：<strong>{{ textOrDash(selectedTask.archiveName) }}</strong>
            <span class="match-divider">|</span>
            身份证：<span class="mono">{{ textOrDash(selectedTask.idCardMasked) }}</span>
          </p>
          <p class="card-note">
            批次 {{ textOrDash(selectedTask.batchId) }} · 申请日期
            {{ formatDate(selectedTask.applicationDate) }} · 状态
            {{ textOrDash(selectedTask.statusText) }}
          </p>
        </NcCard>
      </div>

      <aside class="audit-preview">
        <NcCard title="报告预览" :subtitle="previewSubtitle" class="preview-card">
          <div v-if="!selectedTask" class="preview-empty">
            <el-icon :size="34"><Document /></el-icon>
            <p>选择左侧人员后显示报告预览</p>
          </div>

          <template v-else>
            <div class="preview-bar">
              <span class="preview-file">
                {{ textOrDash(activeReport?.fileName || selectedTask.archiveName) }}
              </span>
              <span class="preview-actions">
                <span v-if="activeReport?.isCurrent" class="preview-current">当前版本</span>
                <span class="pending-wrap" title="PDF 报告打印待接入">
                  <el-button size="small" :icon="Printer" title="PDF 报告打印待接入" disabled>
                    打印报告
                  </el-button>
                </span>
              </span>
            </div>

            <el-alert
              v-if="reportsError"
              class="preview-alert"
              type="error"
              :title="reportsError"
              :closable="false"
              show-icon
            >
              <el-button link type="primary" @click="loadReports(selectedTask.id)">
                重新加载
              </el-button>
            </el-alert>

            <div v-else-if="reportsLoading" class="preview-loading">
              <el-skeleton :rows="5" animated />
            </div>

            <div v-else-if="reports.length === 0" class="preview-placeholder">
              <span class="preview-badge" aria-hidden="true">PDF</span>
              <strong class="preview-title">本户暂无报告</strong>
              <p class="preview-note">
                可通过“保存报告”上传单个文件，或在下方“报告上传”中批量上传文件夹。
              </p>
              <el-button size="small" @click="triggerReportPick">上传报告</el-button>
            </div>

            <div v-else v-loading="previewLoading" class="preview-stage">
              <el-alert
                v-if="previewError"
                type="error"
                :title="previewError"
                :closable="false"
                show-icon
              />
              <iframe
                v-else-if="previewKind === 'pdf' && previewUrl"
                :src="previewUrl"
                class="preview-frame"
                title="报告 PDF 预览"
              />
              <img
                v-else-if="previewKind === 'image' && previewUrl"
                :src="previewUrl"
                class="preview-image"
                alt="报告图片预览"
              />
              <div v-else class="preview-empty preview-empty--inline">
                <p>选择下方报告版本以预览</p>
              </div>
            </div>
          </template>
        </NcCard>

        <NcCard
          title="报告版本"
          :subtitle="
            selectedTask ? `共 ${reports.length} 个版本，点击切换预览` : '选中人员后显示报告版本'
          "
        >
          <div v-if="!selectedTask" class="versions-empty">选中左侧人员后显示报告版本</div>
          <div v-else-if="reportsLoading" class="versions-empty">正在读取报告版本…</div>
          <div v-else-if="reportsError" class="versions-empty">{{ reportsError }}</div>
          <div v-else-if="reports.length === 0" class="versions-empty">本户暂无报告版本</div>

          <ul v-else class="version-list">
            <li v-for="report in reports" :key="report.id">
              <button
                type="button"
                class="version-item"
                :class="{ 'is-active': report.id === activeReportId }"
                @click="selectReportVersion(report)"
              >
                <span class="version-no">v{{ report.version }}</span>
                <span class="version-main">
                  <strong class="version-file" :title="report.fileName">
                    {{ textOrDash(report.fileName) }}
                  </strong>
                  <span class="version-meta">
                    {{ formatDate(report.reportDate) }} · {{ formatBytes(report.size) }}
                  </span>
                </span>
                <span v-if="report.isCurrent" class="version-current">当前</span>
                <span v-else class="version-history">历史</span>
              </button>
            </li>
          </ul>

          <div class="versions-foot">
            <span class="card-note">预览默认显示当前版本；切换版本会重新读取文件。</span>
            <span class="pending-wrap" title="PDF 报告打印待接入">
              <el-button size="small" :icon="Printer" title="PDF 报告打印待接入" disabled>
                打印此版本
              </el-button>
            </span>
          </div>
        </NcCard>
      </aside>
    </div>

    <el-dialog
      v-model="photoDialogVisible"
      :width="isNarrow ? '94%' : '720px'"
      title="核查照片"
      append-to-body
      class="asset-photo-dialog"
      @closed="handlePhotoDialogClosed"
    >
      <div class="photo-toolbar">
        <p class="photo-subject">{{ photoSubject || '未选中人员' }}</p>
        <div class="photo-upload">
          <el-select
            v-model="photoCategory"
            size="small"
            class="photo-category"
            :disabled="photoUploading"
          >
            <el-option
              v-for="option in ASSET_PHOTO_CATEGORY_OPTIONS"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
          <el-button
            type="primary"
            size="small"
            :icon="Upload"
            :loading="photoUploading"
            :disabled="!photoDialogTask"
            @click="triggerPhotoPick"
          >
            上传照片
          </el-button>
        </div>
      </div>

      <el-alert
        v-if="photosError"
        class="photo-alert"
        type="error"
        :title="photosError"
        :closable="false"
        show-icon
      >
        <el-button link type="primary" @click="loadPhotos">重新加载</el-button>
      </el-alert>

      <div v-loading="photosLoading" class="photo-grid">
        <figure v-for="photo in photos" :key="photo.id" class="photo-card">
          <img
            :src="photoDataUrl(photo)"
            :alt="photo.name || '核查照片'"
            class="photo-image"
            loading="lazy"
          />
          <figcaption class="photo-caption">
            <span class="photo-name" :title="photo.name">{{ textOrDash(photo.name) }}</span>
            <span class="photo-meta">
              {{ photoCategoryText(photo.category) }} · {{ formatBytes(photo.size) }}
            </span>
          </figcaption>
        </figure>

        <div v-if="photos.length === 0 && !photosError" class="photo-empty">
          <el-empty description="该人员暂无照片" :image-size="72" />
        </div>
      </div>

      <template #footer>
        <span class="photo-footnote">照片上传后立即归档，默认分类为“本人照片/证件照”。</span>
        <el-button @click="photoDialogVisible = false">关闭</el-button>
      </template>
    </el-dialog>

    <input
      ref="photoInputRef"
      class="hidden-file-input"
      type="file"
      accept="image/*"
      @change="handlePhotoFileChange"
    />
    <input
      ref="reportInputRef"
      class="hidden-file-input"
      type="file"
      accept="application/pdf,image/jpeg,image/png,image/webp"
      @change="handleReportFileChange"
    />
    <input
      ref="batchInputRef"
      class="hidden-file-input"
      type="file"
      accept="application/pdf,image/jpeg,image/png,image/webp"
      webkitdirectory
      multiple
      @change="handleBatchFileChange"
    />

    <AssetTaskDetailDrawer v-model="drawerVisible" :task-id="activeTaskId" />
  </div>
</template>

<style scoped>
.asset-monthly-page {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.readonly-note {
  margin: 0;
}

.audit-layout {
  display: grid;
  grid-template-columns: minmax(0, 430px) minmax(0, 1fr);
  align-items: start;
  gap: 16px;
}

.audit-main,
.audit-preview {
  display: flex;
  flex-direction: column;
  min-width: 0;
  gap: 15px;
}

.audit-preview {
  position: sticky;
  top: 12px;
}

.period-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
}

.period-select {
  width: 132px;
}

.period-text {
  margin-top: 10px;
  color: var(--nc-seal-dark);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  letter-spacing: 0.02em;
}

.overview-body {
  min-height: 120px;
}

.overview-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
}

.overview-foot {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 3px;
  margin-top: 12px;
  text-align: right;
}

.overview-total {
  color: var(--nc-ink-2);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  font-weight: 700;
}

.overview-note {
  margin-top: 10px;
}

.card-note {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.65;
}

.people-card {
  overflow: hidden;
}

.people-card :deep(.nc-card__head) {
  flex-wrap: wrap;
  gap: 10px;
}

.people-card :deep(.nc-card__foot) {
  margin-top: 0;
  padding-top: 0;
  border-top: none;
}

.count-chip {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 28px;
  height: 22px;
  padding: 0 8px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-3);
  color: var(--nc-ink-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  font-weight: 700;
}

.pending-wrap {
  display: inline-flex;
}

.people-toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  padding: 14px 16px;
  border-bottom: 1px solid var(--nc-line-faint);
}

.people-error {
  margin: 12px 16px 0;
}

.people-search {
  flex: 1 1 220px;
  max-width: 340px;
}

.people-status {
  width: 160px;
}

.people-list {
  display: flex;
  flex-direction: column;
  gap: 7px;
  min-height: 150px;
  padding: 12px 16px 14px;
}

.list-skeleton {
  padding: 8px 2px;
}

.person-row {
  display: grid;
  grid-template-columns: minmax(0, 1.25fr) minmax(0, 1.1fr) auto auto;
  grid-template-areas: 'ident meta status actions';
  align-items: center;
  gap: 10px;
  min-width: 0;
  padding: 10px 12px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
  cursor: pointer;
  transition:
    border-color var(--nc-transition),
    background-color var(--nc-transition),
    box-shadow var(--nc-transition);
}

.person-row:hover {
  border-color: var(--nc-line-strong);
  box-shadow: var(--nc-shadow-xs);
}

.person-row:focus-visible {
  outline: 2px solid var(--nc-seal);
  outline-offset: 1px;
}

.person-row.is-active {
  border-color: var(--nc-seal);
  background-color: var(--nc-paper-2);
  box-shadow: inset 3px 0 0 var(--nc-seal);
}

.person-ident {
  display: flex;
  grid-area: ident;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.person-name {
  overflow: hidden;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  font-weight: 700;
  letter-spacing: 0.02em;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.person-id {
  overflow: hidden;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.person-meta {
  display: flex;
  grid-area: meta;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
  color: var(--nc-muted-2);
  font-size: var(--nc-text-xs);
  line-height: 1.5;
}

.person-meta .batch {
  overflow: hidden;
  font-family: var(--nc-font-mono);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.head-badge {
  margin-left: 4px;
  padding: 0 4px;
  border: 1px solid rgba(171, 50, 39, 0.42);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-seal-wash);
  color: var(--nc-seal-dark);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  font-style: normal;
  line-height: 1.5;
}

.person-status {
  grid-area: status;
}

.person-actions {
  display: flex;
  grid-area: actions;
  align-items: center;
  gap: 4px;
}

.mono {
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
  white-space: nowrap;
}

.empty-wrap {
  padding: 8px 2px;
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

.upload-stats {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 10px;
}

.upload-stat {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
  padding: 11px 14px;
  border: 1px solid var(--nc-line-faint);
  border-left: 3px solid var(--nc-amber);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-amber-soft);
}

.upload-stat--total {
  border-left-color: var(--nc-pine);
  background-color: var(--nc-pine-soft);
}

.upload-stat--danger {
  border-left-color: var(--nc-seal);
  background-color: var(--nc-seal-soft);
}

.upload-label {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  font-weight: 600;
}

.upload-stat--danger .upload-label {
  color: var(--nc-seal-dark);
}

.upload-value {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: 26px;
  line-height: 1.2;
}

.pending-block {
  display: flex;
  margin-top: 12px;
}

.upload-button {
  width: 100%;
}

.upload-options {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  margin-top: 8px;
}

.upload-progress {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-top: 10px;
  opacity: 0.65;
}

.upload-progress-track {
  flex: 1 1 auto;
  height: 10px;
  overflow: hidden;
  border-radius: 999px;
  background-color: var(--nc-paper-3);
}

.upload-progress-fill {
  display: block;
  width: 0;
  height: 100%;
  background-color: var(--nc-amber);
}

.upload-footnote {
  margin-top: 10px;
}

.match-line {
  color: var(--nc-ink-2);
  font-size: var(--nc-text-md);
  line-height: 1.8;
}

.match-line strong {
  color: var(--nc-ink);
}

.match-divider {
  margin: 0 8px;
  color: var(--nc-line-strong);
}

.preview-card :deep(.nc-card__body) {
  min-height: 280px;
}

.preview-empty,
.preview-placeholder {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 10px;
  min-height: 280px;
  padding: 24px 18px;
  text-align: center;
}

.preview-empty {
  color: var(--nc-muted);
}

.preview-empty .el-icon {
  color: var(--nc-muted-2);
}

.preview-empty p {
  font-size: var(--nc-text-sm);
}

.preview-placeholder {
  border: 1px dashed var(--nc-line-strong);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
}

.preview-badge {
  display: grid;
  place-items: center;
  width: 54px;
  height: 64px;
  border: 1px solid var(--nc-line-strong);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-2);
  color: var(--nc-seal);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  font-weight: 700;
  letter-spacing: 0.12em;
}

.preview-title {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  font-weight: 700;
  letter-spacing: 0.02em;
}

.preview-note {
  max-width: 340px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  line-height: 1.7;
}

.preview-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  margin-bottom: 10px;
}

.preview-file {
  overflow: hidden;
  max-width: 100%;
  color: var(--nc-ink-2);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.preview-actions {
  display: inline-flex;
  align-items: center;
  gap: 8px;
}

.preview-current {
  padding: 1px 6px;
  border: 1px solid rgba(58, 104, 74, 0.4);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-pine-soft);
  color: var(--nc-ink-2);
  font-size: 10px;
  font-weight: 700;
  white-space: nowrap;
}

.preview-alert {
  margin-bottom: 10px;
}

.preview-loading {
  padding: 12px 4px;
}

.preview-stage {
  position: relative;
  min-height: 320px;
  overflow: hidden;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-3);
}

.preview-frame {
  display: block;
  width: 100%;
  height: min(66vh, 640px);
  min-height: 320px;
  border: 0;
  background-color: var(--nc-paper-2);
}

.preview-image {
  display: block;
  width: 100%;
  max-height: min(66vh, 640px);
  object-fit: contain;
  background-color: var(--nc-paper-3);
}

.preview-empty--inline {
  min-height: 320px;
}

.versions-empty {
  padding: 18px 14px;
  border: 1px dashed var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  text-align: center;
}

.versions-foot {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  margin-top: 10px;
}

.version-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin: 0;
  padding: 0;
  list-style: none;
}

.version-item {
  display: flex;
  align-items: center;
  gap: 10px;
  width: 100%;
  min-width: 0;
  padding: 9px 11px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
  color: inherit;
  font: inherit;
  text-align: left;
  cursor: pointer;
  transition:
    border-color var(--nc-transition),
    background-color var(--nc-transition),
    box-shadow var(--nc-transition);
}

.version-item:hover {
  border-color: var(--nc-line-strong);
  box-shadow: var(--nc-shadow-xs);
}

.version-item:focus-visible {
  outline: 2px solid var(--nc-seal);
  outline-offset: 1px;
}

.version-item.is-active {
  border-color: var(--nc-seal);
  background-color: var(--nc-paper-2);
  box-shadow: inset 3px 0 0 var(--nc-seal);
}

.version-no {
  flex: none;
  min-width: 34px;
  color: var(--nc-seal-dark);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
  font-weight: 700;
}

.version-main {
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.version-file {
  overflow: hidden;
  color: var(--nc-ink);
  font-size: var(--nc-text-sm);
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.version-meta {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
}

.version-current {
  flex: none;
  padding: 1px 6px;
  border: 1px solid rgba(58, 104, 74, 0.4);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-pine-soft);
  color: var(--nc-ink-2);
  font-size: 10px;
  font-weight: 700;
}

.version-history {
  flex: none;
  padding: 1px 6px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-3);
  color: var(--nc-muted);
  font-size: 10px;
}

.upload-progress-text {
  flex: none;
  white-space: nowrap;
}

.hidden-file-input {
  display: none;
}

/* ── 照片对话框 ──────────────────────────────────────────────── */

.photo-toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  margin-bottom: 12px;
}

.photo-subject {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  font-weight: 700;
}

.photo-upload {
  display: inline-flex;
  align-items: center;
  gap: 8px;
}

.photo-category {
  width: 168px;
}

.photo-alert {
  margin-bottom: 10px;
}

.photo-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(150px, 1fr));
  gap: 10px;
  min-height: 160px;
  max-height: min(56vh, 520px);
  overflow-y: auto;
  padding: 2px;
}

.photo-card {
  display: flex;
  flex-direction: column;
  min-width: 0;
  margin: 0;
  overflow: hidden;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-soft);
}

.photo-image {
  display: block;
  width: 100%;
  height: 150px;
  object-fit: cover;
  background-color: var(--nc-paper-3);
}

.photo-caption {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
  padding: 7px 9px;
}

.photo-name {
  overflow: hidden;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-xs);
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.photo-meta {
  color: var(--nc-muted);
  font-size: 11px;
  line-height: 1.5;
}

.photo-empty {
  grid-column: 1 / -1;
  display: flex;
  align-items: center;
  justify-content: center;
}

.photo-footnote {
  margin-right: auto;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.asset-photo-dialog :deep(.el-dialog__footer) {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
}

@media (max-width: 1080px) {
  .audit-layout {
    grid-template-columns: minmax(0, 1fr);
  }

  .audit-preview {
    position: static;
  }
}

@media (max-width: 767px) {
  .period-select {
    flex: 1 1 calc(50% - 5px);
    width: auto;
  }

  .overview-grid,
  .upload-stats {
    gap: 8px;
  }

  .people-toolbar {
    padding: 12px;
  }

  .people-search {
    flex: 1 1 100%;
    max-width: none;
  }

  .people-status {
    flex: 1 1 calc(50% - 4px);
    width: auto;
  }

  .people-toolbar .el-button {
    flex: 0 0 auto;
  }

  .people-list {
    padding: 10px 12px 12px;
  }

  .person-row {
    grid-template-columns: minmax(0, 1fr) auto;
    grid-template-areas:
      'ident status'
      'meta meta'
      'actions actions';
    gap: 6px 10px;
  }

  .person-actions {
    justify-content: flex-end;
    padding-top: 4px;
    border-top: 1px solid var(--nc-line-faint);
  }

  .pagination {
    justify-content: center;
    padding: 12px;
  }

  .pagination-note {
    width: 100%;
    text-align: center;
  }

  .preview-card :deep(.nc-card__body),
  .preview-empty,
  .preview-placeholder {
    min-height: 200px;
  }

  .preview-stage,
  .preview-empty--inline {
    min-height: 240px;
  }

  .preview-frame {
    height: 56vh;
    min-height: 240px;
  }

  .photo-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .photo-image {
    height: 118px;
  }

  .photo-upload {
    width: 100%;
  }

  .photo-category {
    flex: 1 1 auto;
    width: auto;
  }
}
</style>
