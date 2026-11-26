<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  ArrowLeft,
  Delete,
  Download,
  MoreFilled,
  Plus,
  Refresh,
  Search,
  View,
} from '@element-plus/icons-vue'
import { ApiError, getErrorMessage } from '@/api/http'
import {
  deleteArchivePhoto,
  exportArchivePhotos,
  getArchivePhoto,
  getArchivePhotoCategories,
  listArchivePhotos,
  uploadArchivePhoto,
} from '@/api/archivePhoto'
import type {
  ArchivePhotoCategory,
  ArchivePhotoDetail,
  ArchivePhotoListItem,
} from '@/types/archivePhoto'
import NcCard from '@/components/NcCard.vue'
import NcPageHeader from '@/components/NcPageHeader.vue'
import NcStatusDot from '@/components/NcStatusDot.vue'
import { useMediaQuery } from '@/composables/useMediaQuery'
import { maskIdCard } from '@/views/applications/format'
import { formatDateTimeText } from './archiveDisplay'

const PAGE_SIZE = 20
const MAX_PHOTO_BYTES = 10 * 1024 * 1024

/** 后端类别接口不可用时的兜底字典（与旧版 PhotoCategoryConstants 同源）。 */
const FALLBACK_CATEGORIES: ArchivePhotoCategory[] = [
  { code: 'ID_CARD', label: '身份证' },
  { code: 'HOUSEHOLD_REGISTER', label: '户口本' },
  { code: 'PERSON_PHOTO', label: '本人照片/证件照' },
  { code: 'MEDICAL_RECORD', label: '病历/诊断证明' },
  { code: 'DISABILITY_CERT', label: '残疾证' },
  { code: 'BANK_CARD', label: '银行卡/一卡通' },
  { code: 'INCOME_PROOF', label: '收入/务工证明' },
  { code: 'PUBLICITY_CHECK', label: '公示/核查照片' },
  { code: 'HOUSEHOLD_INSPECTION', label: '入户排查' },
  { code: 'OTHER_CERT', label: '其他证件照' },
]

const router = useRouter()
const route = useRoute()
const isNarrow = useMediaQuery('(max-width: 1280px)')

const categories = ref<ArchivePhotoCategory[]>([])
const initialKeyword =
  typeof route.query.idCard === 'string'
    ? route.query.idCard
    : typeof route.query.keyword === 'string'
      ? route.query.keyword
      : ''
const keyword = ref(initialKeyword.trim())
const category = ref('')
const page = ref(1)
const total = ref(0)
const items = ref<ArchivePhotoListItem[]>([])
const loading = ref(false)
const loadError = ref('')

const selectedIds = ref<number[]>([])
const thumbnails = ref<Record<number, string>>({})
const thumbLoading = ref<Record<number, boolean>>({})
const details = ref<Record<number, ArchivePhotoDetail>>({})

const previewId = ref<number | null>(null)
const previewLoading = ref(false)
const previewError = ref('')
const mobilePreviewVisible = ref(false)

const insertVisible = ref(false)
const insertIdCard = ref('')
const insertCategory = ref('')
const insertFile = ref<File | null>(null)
const insertFileInput = ref<HTMLInputElement | null>(null)
const inserting = ref(false)

const exporting = ref<'all' | 'selected' | null>(null)
const deletingId = ref<number | null>(null)

let listTicket = 0
let thumbToken = 0
let thumbController: AbortController | null = null
let previewTicket = 0
const pendingDetails = new Map<number, Promise<ArchivePhotoDetail>>()

const totalPages = computed(() => Math.max(1, Math.ceil(total.value / PAGE_SIZE)))
const allSelected = computed(
  () =>
    items.value.length > 0 && items.value.every((row) => selectedIds.value.includes(row.id)),
)
const selectedCount = computed(() => selectedIds.value.length)
const someSelected = computed(() => selectedCount.value > 0 && !allSelected.value)
const previewItem = computed(
  () => items.value.find((row) => row.id === previewId.value) ?? null,
)
const previewDetail = computed(() =>
  previewId.value === null ? null : details.value[previewId.value] ?? null,
)
const previewSrc = computed(() =>
  previewDetail.value ? toDataUrl(previewDetail.value) : '',
)
const insertFileName = computed(() => insertFile.value?.name ?? '')

function categoryLabel(code: string | null | undefined, display?: string | null): string {
  if (display) return display
  if (!code) return '—'
  return categories.value.find((item) => item.code === code)?.label ?? code
}

function displayName(row: ArchivePhotoListItem): string {
  return row.photoName?.trim() || `照片 #${row.id}`
}

function formatFileSize(size: number | null | undefined): string {
  if (size === null || size === undefined || !Number.isFinite(size) || size < 0) return '—'
  if (size < 1024) return `${size} B`
  if (size < 1024 * 1024) return `${(size / 1024).toFixed(1)} KB`
  return `${(size / 1024 / 1024).toFixed(1)} MB`
}

function imageMime(format: string | null | undefined): string {
  const value = (format ?? '').toLowerCase()
  if (value.includes('png')) return 'image/png'
  if (value.includes('webp')) return 'image/webp'
  if (value.includes('gif')) return 'image/gif'
  if (value.includes('bmp')) return 'image/bmp'
  return 'image/jpeg'
}

function toDataUrl(detail: ArchivePhotoDetail): string {
  const raw = (detail.photoBase64 ?? '').trim()
  if (!raw) return ''
  if (raw.startsWith('data:')) return raw
  return `data:${imageMime(detail.imageFormat)};base64,${raw.replace(/\s+/g, '')}`
}

function actionError(
  error: unknown,
  fallback: string,
  action: string,
  permission = 'ARCHIVE_MANAGE',
): string {
  if (error instanceof ApiError && error.status === 403) {
    return `无权限${action}（需要${permission}权限）`
  }
  return getErrorMessage(error, fallback)
}

function saveBlob(blob: Blob, filename: string): void {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = filename
  document.body.appendChild(link)
  link.click()
  link.remove()
  window.setTimeout(() => URL.revokeObjectURL(url), 1000)
}

function timestampText(): string {
  const now = new Date()
  const pad = (part: number): string => String(part).padStart(2, '0')
  return `${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}_${pad(now.getHours())}${pad(now.getMinutes())}`
}

function cancelThumbnails(): void {
  thumbToken += 1
  thumbController?.abort()
  thumbController = null
  pendingDetails.clear()
}

function ensureDetail(id: number, signal?: AbortSignal): Promise<ArchivePhotoDetail> {
  const cached = details.value[id]
  if (cached) return Promise.resolve(cached)

  const pending = pendingDetails.get(id)
  if (pending) return pending

  const promise = getArchivePhoto(id, signal)
    .then((detail) => {
      details.value = { ...details.value, [id]: detail }
      return detail
    })
    .finally(() => {
      pendingDetails.delete(id)
    })
  pendingDetails.set(id, promise)
  return promise
}

async function loadThumbnails(rows: ArchivePhotoListItem[]): Promise<void> {
  cancelThumbnails()
  if (rows.length === 0) return

  const token = thumbToken
  const controller = new AbortController()
  thumbController = controller
  const queue = [...rows]

  const worker = async (): Promise<void> => {
    while (queue.length > 0) {
      if (controller.signal.aborted || token !== thumbToken) return
      const row = queue.shift()
      if (!row) return
      if (row.hasData === false) continue

      thumbLoading.value = { ...thumbLoading.value, [row.id]: true }
      try {
        const detail = await ensureDetail(row.id, controller.signal)
        if (controller.signal.aborted || token !== thumbToken) return
        const src = toDataUrl(detail)
        if (src) thumbnails.value = { ...thumbnails.value, [row.id]: src }
      } catch {
        // 单张读取失败保留占位，不阻塞其余缩略图。
      } finally {
        if (token === thumbToken) {
          const next = { ...thumbLoading.value }
          delete next[row.id]
          thumbLoading.value = next
        }
      }
    }
  }

  await Promise.all(Array.from({ length: 4 }, worker))
}

async function loadCategories(): Promise<void> {
  try {
    const rows = await getArchivePhotoCategories()
    categories.value = rows.length > 0 ? rows : FALLBACK_CATEGORIES
  } catch {
    categories.value = FALLBACK_CATEGORIES
  }
}

async function loadList(): Promise<void> {
  const ticket = ++listTicket
  cancelThumbnails()
  previewTicket += 1
  loading.value = true
  loadError.value = ''
  selectedIds.value = []
  previewId.value = null
  previewError.value = ''
  previewLoading.value = false
  details.value = {}
  thumbnails.value = {}
  thumbLoading.value = {}

  try {
    const response = await listArchivePhotos({
      keyword: keyword.value.trim() || undefined,
      category: category.value || undefined,
      page: page.value,
      pageSize: PAGE_SIZE,
    })
    if (ticket !== listTicket) return

    items.value = response.items ?? []
    total.value = response.total ?? 0
    if (response.page && response.page > 0) page.value = response.page
    void loadThumbnails(items.value)
  } catch (error) {
    if (ticket !== listTicket) return
    items.value = []
    total.value = 0
    loadError.value = actionError(error, '照片列表加载失败', '查询档案照片', 'ARCHIVE_VIEW')
  } finally {
    if (ticket === listTicket) loading.value = false
  }
}

function handleSearch(): void {
  page.value = 1
  void loadList()
}

function handleReset(): void {
  keyword.value = ''
  category.value = ''
  page.value = 1
  void loadList()
}

function handleCategoryChange(): void {
  page.value = 1
  void loadList()
}

function goPage(delta: number): void {
  const next = page.value + delta
  if (next < 1 || next > totalPages.value) return
  page.value = next
  void loadList()
}

function toggleSelect(id: number): void {
  selectedIds.value = selectedIds.value.includes(id)
    ? selectedIds.value.filter((value) => value !== id)
    : [...selectedIds.value, id]
}

function selectAll(): void {
  selectedIds.value = items.value.map((row) => row.id)
}

function clearSelection(): void {
  selectedIds.value = []
}

async function openPreview(row: ArchivePhotoListItem): Promise<void> {
  const ticket = ++previewTicket
  previewId.value = row.id
  previewError.value = ''
  if (isNarrow.value) mobilePreviewVisible.value = true

  if (row.hasData === false) {
    previewLoading.value = false
    previewError.value = '照片数据为空（可能未成功上传）'
    return
  }

  const cached = details.value[row.id]
  if (cached) {
    previewLoading.value = false
    if (!thumbnails.value[row.id]) {
      const src = toDataUrl(cached)
      if (src) thumbnails.value = { ...thumbnails.value, [row.id]: src }
    }
    return
  }

  previewLoading.value = true
  try {
    const detail = await ensureDetail(row.id)
    if (ticket !== previewTicket) return
    const src = toDataUrl(detail)
    if (src) thumbnails.value = { ...thumbnails.value, [row.id]: src }
  } catch (error) {
    if (ticket !== previewTicket) return
    previewError.value = actionError(error, '照片预览加载失败', '预览照片', 'ARCHIVE_VIEW')
  } finally {
    if (ticket === previewTicket) previewLoading.value = false
  }
}

function openInsert(): void {
  insertIdCard.value = /^\d{17}[\dXx]$/.test(keyword.value.trim())
    ? keyword.value.trim()
    : ''
  insertCategory.value =
    category.value || categories.value[0]?.code || FALLBACK_CATEGORIES[0].code
  insertFile.value = null
  insertVisible.value = true
}

function pickInsertFile(): void {
  insertFileInput.value?.click()
}

function onInsertFileChange(event: Event): void {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0] ?? null
  if (file && file.size > MAX_PHOTO_BYTES) {
    ElMessage.warning('照片不能超过 10MB')
    input.value = ''
    insertFile.value = null
    return
  }
  insertFile.value = file
}

async function submitInsert(): Promise<void> {
  const idCard = insertIdCard.value.trim()
  if (!/^\d{17}[\dXx]$/.test(idCard)) {
    ElMessage.warning('请输入 18 位身份证号')
    return
  }
  if (!insertCategory.value) {
    ElMessage.warning('请选择照片类别')
    return
  }
  if (!insertFile.value) {
    ElMessage.warning('请选择要插入的照片文件')
    return
  }

  inserting.value = true
  try {
    await uploadArchivePhoto({
      idCard,
      category: insertCategory.value,
      file: insertFile.value,
    })
    ElMessage.success('照片已插入')
    insertVisible.value = false
    insertFile.value = null
    page.value = 1
    await loadList()
  } catch (error) {
    ElMessage.error(actionError(error, '照片插入失败', '插入照片'))
  } finally {
    inserting.value = false
  }
}

async function removePhoto(row: ArchivePhotoListItem): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `确定删除照片「${displayName(row)}」吗？\n删除后不可恢复。`,
      '确认删除',
      { confirmButtonText: '删除', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  deletingId.value = row.id
  try {
    await deleteArchivePhoto(row.id)
    ElMessage.success('照片已删除')
    if (previewId.value === row.id) {
      previewId.value = null
      mobilePreviewVisible.value = false
    }
    await loadList()
  } catch (error) {
    ElMessage.error(actionError(error, '删除照片失败', '删除照片'))
  } finally {
    deletingId.value = null
  }
}

async function exportPhotos(scope: 'all' | 'selected'): Promise<void> {
  if (scope === 'selected' && selectedIds.value.length === 0) {
    ElMessage.warning('请先勾选要导出的照片')
    return
  }

  exporting.value = scope
  try {
    const blob = await exportArchivePhotos({
      keyword: keyword.value.trim() || undefined,
      category: category.value || undefined,
      ids: scope === 'selected' ? selectedIds.value.join(',') : undefined,
    })
    const filename =
      scope === 'selected'
        ? `档案照片_选中${selectedIds.value.length}张_${timestampText()}.zip`
        : `档案照片_${timestampText()}.zip`
    saveBlob(blob, filename)
    ElMessage.success('导出文件已开始下载')
  } catch (error) {
    ElMessage.error(actionError(error, '导出照片失败', '导出照片', 'ARCHIVE_VIEW'))
  } finally {
    exporting.value = null
  }
}

function handleMoreCommand(command: string): void {
  if (command === 'export-all') {
    void exportPhotos('all')
  } else if (command === 'export-selected') {
    void exportPhotos('selected')
  } else if (command === 'refresh') {
    void loadList()
  }
}

function goBack(): void {
  const returnTo = typeof route.query.returnTo === 'string' ? route.query.returnTo : ''
  if (returnTo.startsWith('/') && !returnTo.startsWith('//')) {
    void router.push(returnTo)
    return
  }
  void router.push('/archives/query')
}

onMounted(() => {
  void loadCategories()
  void loadList()
})

onBeforeUnmount(() => {
  cancelThumbnails()
})
</script>

<template>
  <div class="nc-page photo-page">
    <NcPageHeader
      title="档案照片查询"
      description="按身份证号或照片名检索档案照片，支持预览、插入与打包导出"
      kicker="救助档案 · 照片"
    >
      <template #actions>
        <el-button :icon="ArrowLeft" @click="goBack">返回档案查询</el-button>
        <el-button :icon="Refresh" :loading="loading" @click="loadList()">刷新</el-button>
      </template>
    </NcPageHeader>

    <NcCard padding="none" class="query-card">
      <form class="query-bar" @submit.prevent="handleSearch">
        <el-input
          v-model="keyword"
          class="query-keyword"
          clearable
          :prefix-icon="Search"
          placeholder="身份证号 / 照片名"
          @clear="handleSearch"
        />
        <el-select
          v-model="category"
          class="query-category"
          placeholder="全部类别"
          @change="handleCategoryChange"
        >
          <el-option label="全部类别" value="" />
          <el-option
            v-for="item in categories"
            :key="item.code"
            :label="item.label"
            :value="item.code"
          />
        </el-select>
        <el-button type="primary" :icon="Search" native-type="submit" :loading="loading">
          查询
        </el-button>
        <el-button v-if="keyword || category" text native-type="button" @click="handleReset">
          重置
        </el-button>

        <div class="query-actions">
          <el-button :icon="Plus" @click="openInsert">插入照片</el-button>
          <template v-if="!isNarrow">
            <el-button
              :icon="Download"
              :loading="exporting === 'all'"
              :disabled="exporting !== null"
              @click="exportPhotos('all')"
            >
              导出全部
            </el-button>
            <el-button
              :icon="Download"
              :loading="exporting === 'selected'"
              :disabled="exporting !== null || selectedCount === 0"
              @click="exportPhotos('selected')"
            >
              导出选中{{ selectedCount > 0 ? `（${selectedCount}）` : '' }}
            </el-button>
          </template>
          <el-dropdown v-else trigger="click" @command="handleMoreCommand">
            <el-button :icon="MoreFilled" :loading="exporting !== null">更多</el-button>
            <template #dropdown>
              <el-dropdown-menu>
                <el-dropdown-item command="export-all" :disabled="exporting !== null">
                  导出全部
                </el-dropdown-item>
                <el-dropdown-item
                  command="export-selected"
                  :disabled="exporting !== null || selectedCount === 0"
                >
                  导出选中{{ selectedCount > 0 ? `（${selectedCount}）` : '' }}
                </el-dropdown-item>
                <el-dropdown-item command="refresh">刷新列表</el-dropdown-item>
              </el-dropdown-menu>
            </template>
          </el-dropdown>
        </div>
      </form>

      <div v-if="items.length > 0" class="selection-bar">
        <span class="selection-count">
          已选 <strong>{{ selectedCount }}</strong> 张
        </span>
        <div class="selection-actions">
          <el-button link type="primary" @click="selectAll">全选本页</el-button>
          <el-button link :disabled="selectedCount === 0" @click="clearSelection">
            清空勾选
          </el-button>
        </div>
      </div>
    </NcCard>

    <el-alert
      v-if="loadError"
      class="load-error"
      type="error"
      :title="loadError"
      :closable="false"
      show-icon
    >
      <el-button link type="primary" @click="loadList()">重新加载</el-button>
    </el-alert>

    <div class="content-grid" :class="{ 'content-grid--single': isNarrow }">
      <NcCard padding="none" class="list-card" title="照片列表">
        <template #actions>
          <NcStatusDot status="default" :label="`共 ${total} 张`" />
        </template>

        <div v-loading="loading && items.length > 0" class="list-area">
          <el-skeleton v-if="loading && items.length === 0" :rows="8" animated class="list-skeleton" />

          <div v-else-if="items.length > 0" class="photo-table">
            <div class="photo-table__head">
              <div class="cell-check">
                <el-checkbox
                  :model-value="allSelected"
                  :indeterminate="someSelected"
                  @change="allSelected ? clearSelection() : selectAll()"
                />
              </div>
              <span>缩略图</span>
              <span>照片 / 类别</span>
              <span>身份证</span>
              <span>大小</span>
              <span>创建时间</span>
              <span class="cell-actions-head">操作</span>
            </div>

            <div
              v-for="row in items"
              :key="row.id"
              class="photo-row"
              :class="{ 'is-active': previewId === row.id }"
              @click="openPreview(row)"
            >
              <div class="cell-check" @click.stop>
                <el-checkbox
                  :model-value="selectedIds.includes(row.id)"
                  @change="toggleSelect(row.id)"
                />
              </div>

              <div class="cell-thumb">
                <div class="thumb-box">
                  <img
                    v-if="thumbnails[row.id]"
                    :src="thumbnails[row.id]"
                    :alt="displayName(row)"
                    loading="lazy"
                  />
                  <span v-else-if="thumbLoading[row.id]" class="thumb-hint">加载中</span>
                  <span v-else class="thumb-hint">无图</span>
                </div>
              </div>

              <div class="cell-name">
                <strong :title="displayName(row)">{{ displayName(row) }}</strong>
                <span class="chip">
                  {{ categoryLabel(row.photoCategory, row.photoCategoryDisplay) }}
                </span>
              </div>

              <div class="cell-id mono" :title="row.applicantIdCard">
                {{ row.applicantIdCard || '—' }}
              </div>

              <div class="cell-size">{{ formatFileSize(row.fileSize) }}</div>

              <div class="cell-time">{{ formatDateTimeText(row.createdAt) }}</div>

              <div class="cell-actions" @click.stop>
                <el-button link type="primary" :icon="View" @click="openPreview(row)">
                  预览
                </el-button>
                <el-button
                  link
                  type="danger"
                  :icon="Delete"
                  :loading="deletingId === row.id"
                  @click="removePhoto(row)"
                >
                  删除
                </el-button>
              </div>
            </div>
          </div>

          <div v-else-if="!loading && !loadError" class="empty-wrap">
            <el-empty description="暂无照片（先用身份证号查询）" :image-size="72">
              <p class="empty-hint">
                可在上方输入 18 位身份证号查询，或点击「插入照片」为档案人员补充照片。
              </p>
            </el-empty>
          </div>
        </div>

        <template #footer>
          <div v-if="total > 0" class="pager">
            <span class="pager-note">
              第 {{ page }} / {{ totalPages }} 页 · 每页 {{ PAGE_SIZE }} 张 · 共 {{ total }} 张
            </span>
            <div class="pager-buttons">
              <el-button :disabled="page <= 1 || loading" @click="goPage(-1)">上一页</el-button>
              <el-button :disabled="page >= totalPages || loading" @click="goPage(1)">
                下一页
              </el-button>
            </div>
          </div>
        </template>
      </NcCard>

      <aside v-if="!isNarrow" class="preview-column">
        <NcCard title="照片预览" class="preview-card">
          <template #actions>
            <NcStatusDot status="default" label="原图" />
          </template>

          <div v-if="!previewItem" class="preview-empty">
            <el-empty description="点选左侧照片查看预览" :image-size="64" />
          </div>

          <div v-else class="preview-body">
            <div class="preview-frame">
              <el-skeleton v-if="previewLoading && !previewSrc" :rows="4" animated />
              <img
                v-else-if="previewSrc"
                :src="previewSrc"
                :alt="displayName(previewItem)"
                class="preview-image"
              />
              <div v-else class="preview-fail">
                {{ previewError || '照片读取失败（可能已被删除）' }}
              </div>
            </div>

            <dl class="preview-meta">
              <div>
                <dt>照片名</dt>
                <dd>{{ displayName(previewItem) }}</dd>
              </div>
              <div>
                <dt>类别</dt>
                <dd>
                  {{ categoryLabel(previewItem.photoCategory, previewItem.photoCategoryDisplay) }}
                </dd>
              </div>
              <div>
                <dt>身份证</dt>
                <dd class="mono">{{ maskIdCard(previewItem.applicantIdCard) }}</dd>
              </div>
              <div>
                <dt>文件大小</dt>
                <dd>{{ formatFileSize(previewItem.fileSize) }}</dd>
              </div>
              <div>
                <dt>图片格式</dt>
                <dd>{{ previewItem.imageFormat || '—' }}</dd>
              </div>
              <div>
                <dt>创建时间</dt>
                <dd>{{ formatDateTimeText(previewItem.createdAt) }}</dd>
              </div>
            </dl>
          </div>
        </NcCard>
      </aside>
    </div>

    <el-dialog
      v-model="mobilePreviewVisible"
      fullscreen
      append-to-body
      class="mobile-preview-dialog"
      :title="previewItem ? displayName(previewItem) : '照片预览'"
    >
      <div v-if="previewItem" class="mobile-preview-body">
        <div class="mobile-preview-frame">
          <el-skeleton v-if="previewLoading && !previewSrc" :rows="5" animated />
          <img
            v-else-if="previewSrc"
            :src="previewSrc"
            :alt="displayName(previewItem)"
            class="mobile-preview-image"
          />
          <div v-else class="preview-fail">
            {{ previewError || '照片读取失败（可能已被删除）' }}
          </div>
        </div>

        <dl class="preview-meta">
          <div>
            <dt>类别</dt>
            <dd>
              {{ categoryLabel(previewItem.photoCategory, previewItem.photoCategoryDisplay) }}
            </dd>
          </div>
          <div>
            <dt>身份证</dt>
            <dd class="mono">{{ maskIdCard(previewItem.applicantIdCard) }}</dd>
          </div>
          <div>
            <dt>大小 / 格式</dt>
            <dd>{{ formatFileSize(previewItem.fileSize) }} · {{ previewItem.imageFormat || '—' }}</dd>
          </div>
          <div>
            <dt>创建时间</dt>
            <dd>{{ formatDateTimeText(previewItem.createdAt) }}</dd>
          </div>
        </dl>
      </div>
    </el-dialog>

    <el-dialog
      v-model="insertVisible"
      title="插入档案照片"
      :width="isNarrow ? '92%' : '520px'"
      append-to-body
      destroy-on-close
    >
      <div class="dialog-field">
        <label class="field-label" for="photo-id-card">
          身份证号 <em>*</em>
        </label>
        <el-input
          id="photo-id-card"
          v-model="insertIdCard"
          maxlength="18"
          clearable
          placeholder="请输入 18 位身份证号"
        />
        <p class="field-hint">
          照片将挂到该身份证对应的档案人员名下（低收入 / 高龄 / 临时救助）。
        </p>
      </div>

      <div class="dialog-field">
        <label class="field-label" for="photo-category">
          照片类别 <em>*</em>
        </label>
        <el-select
          id="photo-category"
          v-model="insertCategory"
          placeholder="请选择照片类别"
          class="full-width"
        >
          <el-option
            v-for="item in categories"
            :key="item.code"
            :label="item.label"
            :value="item.code"
          />
        </el-select>
      </div>

      <div class="dialog-field">
        <span class="field-label">照片文件 <em>*</em></span>
        <input
          ref="insertFileInput"
          class="file-input"
          type="file"
          accept=".png,.jpg,.jpeg,.webp,image/png,image/jpeg,image/webp"
          @change="onInsertFileChange"
        />
        <div class="file-row">
          <div class="file-box">
            <span v-if="insertFileName" class="file-name">{{ insertFileName }}</span>
            <span v-else class="file-placeholder">未选择照片文件</span>
          </div>
          <el-button :icon="Plus" @click="pickInsertFile">选择文件</el-button>
        </div>
        <p class="field-hint">支持 png / jpeg / webp，单张不超过 10MB。</p>
      </div>

      <template #footer>
        <el-button :disabled="inserting" @click="insertVisible = false">取消</el-button>
        <el-button type="primary" :loading="inserting" @click="submitInsert">
          插入照片
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.photo-page {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.query-card,
.list-card {
  overflow: hidden;
}

.query-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
  padding: 14px 16px;
}

.query-keyword {
  flex: 1 1 260px;
  max-width: 440px;
}

.query-category {
  flex: none;
  width: 190px;
}

.query-actions {
  display: flex;
  flex-wrap: wrap;
  flex: none;
  gap: 8px;
  margin-left: auto;
}

.query-actions :deep(.el-button + .el-button),
.query-actions :deep(.el-dropdown) {
  margin-left: 0;
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

.content-grid {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 350px;
  gap: 15px;
  align-items: start;
}

.content-grid--single {
  grid-template-columns: minmax(0, 1fr);
}

.list-card :deep(.nc-card__foot) {
  margin-top: 0;
  padding-top: 0;
  border-top: none;
}

.list-area {
  min-height: 240px;
  padding: 10px;
}

.list-skeleton {
  padding: 12px;
}

.photo-table {
  display: flex;
  flex-direction: column;
}

.photo-table__head,
.photo-row {
  display: grid;
  grid-template-columns: 40px 64px minmax(0, 1fr) 170px 90px 145px 128px;
  gap: 12px;
  align-items: center;
}

.photo-table__head {
  padding: 6px 10px 8px;
  border-bottom: 1px solid var(--nc-line);
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  letter-spacing: 0.08em;
}

.photo-row {
  padding: 10px;
  border-bottom: 1px solid var(--nc-line-faint);
  cursor: pointer;
  transition:
    background-color var(--nc-transition),
    box-shadow var(--nc-transition);
}

.photo-row:last-child {
  border-bottom: none;
}

.photo-row:hover {
  background-color: var(--nc-paper-3);
}

.photo-row.is-active {
  background-color: var(--nc-seal-wash);
  box-shadow: inset 3px 0 0 var(--nc-seal);
}

.cell-check {
  display: flex;
  align-items: center;
  justify-content: center;
}

.cell-thumb {
  min-width: 0;
}

.thumb-box {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 56px;
  height: 56px;
  overflow: hidden;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper-3);
}

.thumb-box img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.thumb-hint {
  color: var(--nc-muted);
  font-size: 10px;
  letter-spacing: 0.04em;
}

.cell-name {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
  min-width: 0;
}

.cell-name strong {
  overflow: hidden;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  font-weight: 700;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.chip {
  flex: none;
  padding: 1px 7px;
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-seal-soft);
  color: var(--nc-seal-dark);
  font-size: 10px;
  font-weight: 600;
  line-height: 1.7;
  white-space: nowrap;
}

.cell-id {
  overflow: hidden;
  color: var(--nc-muted-2);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.mono {
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-sm);
}

.cell-size,
.cell-time {
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
  white-space: nowrap;
}

.cell-actions {
  display: flex;
  justify-content: flex-end;
  gap: 2px;
}

.cell-actions :deep(.el-button + .el-button) {
  margin-left: 0;
}

.cell-actions-head {
  text-align: right;
}

.empty-wrap {
  padding: 14px 8px;
}

.empty-hint {
  margin-top: 6px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  text-align: center;
}

.pager {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 12px 16px;
  border-top: 1px solid var(--nc-line-faint);
}

.pager-note {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: var(--nc-text-xs);
}

.pager-buttons {
  display: flex;
  gap: 8px;
}

.pager-buttons :deep(.el-button + .el-button) {
  margin-left: 0;
}

.preview-column {
  position: sticky;
  top: 16px;
}

.preview-card {
  overflow: hidden;
}

.preview-empty {
  padding: 10px 0;
}

.preview-body {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.preview-frame {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 230px;
  max-height: 420px;
  overflow: hidden;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-3);
}

.preview-frame :deep(.el-skeleton) {
  width: 100%;
  padding: 16px;
}

.preview-image {
  display: block;
  max-width: 100%;
  max-height: 420px;
  object-fit: contain;
}

.preview-fail {
  padding: 24px 16px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  text-align: center;
}

.preview-meta {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px 14px;
  margin: 0;
}

.preview-meta div {
  min-width: 0;
}

.preview-meta dt {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  letter-spacing: 0.08em;
}

.preview-meta dd {
  margin: 2px 0 0;
  overflow: hidden;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.mobile-preview-body {
  display: flex;
  flex-direction: column;
  gap: 16px;
  min-height: 100%;
}

.mobile-preview-frame {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 240px;
  padding: 12px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-3);
}

.mobile-preview-image {
  display: block;
  max-width: 100%;
  max-height: 62vh;
  object-fit: contain;
}

.dialog-field {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin-bottom: 16px;
}

.field-label {
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
  font-weight: 600;
}

.field-label em {
  font-style: normal;
  color: var(--nc-seal);
}

.field-hint {
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
}

.full-width {
  width: 100%;
}

.file-input {
  display: none;
}

.file-row {
  display: flex;
  align-items: center;
  gap: 8px;
}

.file-row :deep(.el-button) {
  flex: none;
  margin-left: 0;
}

.file-box {
  flex: 1;
  min-width: 0;
  padding: 8px 12px;
  overflow: hidden;
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-3);
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.file-name {
  color: var(--nc-ink);
}

.file-placeholder {
  color: var(--nc-muted);
}

@media (max-width: 1440px) {
  .content-grid {
    grid-template-columns: minmax(0, 1fr) 300px;
  }

  .photo-table__head,
  .photo-row {
    grid-template-columns: 36px 56px minmax(0, 1fr) 140px 72px 120px 100px;
    gap: 8px;
  }
}

@media (max-width: 1280px) {
  .content-grid {
    grid-template-columns: minmax(0, 1fr);
  }
}

@media (max-width: 767px) {
  .query-bar {
    padding: 12px;
  }

  .query-keyword {
    flex-basis: 100%;
    max-width: none;
  }

  .query-category {
    flex: 1 1 140px;
    width: auto;
  }

  .query-actions {
    flex: 1 1 100%;
    margin-left: 0;
  }

  .query-actions :deep(.el-button),
  .query-actions :deep(.el-dropdown) {
    flex: 1;
  }

  .selection-bar {
    padding: 9px 12px;
  }

  .list-area {
    padding: 8px;
  }

  .photo-table__head {
    display: none;
  }

  .photo-row {
    grid-template-columns: 40px 64px minmax(0, 1fr);
    grid-template-areas:
      'check thumb name'
      'id id id'
      'size size time'
      'actions actions actions';
    gap: 8px 10px;
    padding: 12px 10px;
  }

  .photo-row .cell-check {
    grid-area: check;
  }

  .photo-row .cell-thumb {
    grid-area: thumb;
  }

  .photo-row .cell-name {
    grid-area: name;
  }

  .photo-row .cell-id {
    grid-area: id;
  }

  .photo-row .cell-size {
    grid-area: size;
  }

  .photo-row .cell-time {
    grid-area: time;
    text-align: right;
  }

  .photo-row .cell-actions {
    grid-area: actions;
    justify-content: flex-end;
    padding-top: 4px;
    border-top: 1px solid var(--nc-line-faint);
  }

  .thumb-box {
    width: 60px;
    height: 60px;
  }

  .pager {
    justify-content: center;
    padding: 12px;
  }

  .pager-note {
    width: 100%;
    text-align: center;
  }

  .pager-buttons {
    width: 100%;
  }

  .pager-buttons :deep(.el-button) {
    flex: 1;
  }

  .preview-meta {
    grid-template-columns: minmax(0, 1fr);
  }

  .file-row {
    flex-wrap: wrap;
  }

  .file-box {
    flex-basis: 100%;
  }
}
</style>
