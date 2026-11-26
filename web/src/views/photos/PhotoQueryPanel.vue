<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Delete, Download, Printer, Refresh, Search, View } from '@element-plus/icons-vue'
import { deletePhoto, exportPhotoZip, listPhotoCategories, listPhotos } from '@/api/photos'
import type { PhotoCategory, PhotoRecord } from '@/types/photos'
import NcCard from '@/components/NcCard.vue'
import NcStatusDot from '@/components/NcStatusDot.vue'
import PhotoPreviewDialog from './PhotoPreviewDialog.vue'
import PhotoPrintDialog from './PhotoPrintDialog.vue'
import PhotoThumb from './PhotoThumb.vue'
import {
  FALLBACK_PHOTO_CATEGORIES,
  PHOTO_PAGE_SIZE,
  categoryLabelFrom,
  displayPhotoName,
  formatDateTimeText,
  formatFileSize,
  photoActionError,
  saveBlob,
  timestampText,
} from './photoMeta'

const props = withDefaults(
  defineProps<{
    initialKeyword?: string
    initialCategory?: string
  }>(),
  {
    initialKeyword: '',
    initialCategory: '',
  },
)

const categories = ref<PhotoCategory[]>([])
const keyword = ref(props.initialKeyword.trim())
const category = ref(props.initialCategory)
const page = ref(1)
const total = ref(0)
const items = ref<PhotoRecord[]>([])
const loading = ref(false)
const loadError = ref('')
const selectedIds = ref<number[]>([])
const exporting = ref(false)
const deletingId = ref<number | null>(null)

const previewVisible = ref(false)
const previewPhoto = ref<PhotoRecord | null>(null)
const printVisible = ref(false)

let listTicket = 0

const totalPages = computed(() => Math.max(1, Math.ceil(total.value / PHOTO_PAGE_SIZE)))
const allPageSelected = computed(
  () =>
    items.value.length > 0 && items.value.every((item) => selectedIds.value.includes(item.id)),
)
const selectedCount = computed(() => selectedIds.value.length)
const hasFilter = computed(() => Boolean(keyword.value.trim() || category.value))

function categoryText(photo: PhotoRecord): string {
  return categoryLabelFrom(categories.value, photo.category, photo.categoryLabel)
}

async function loadCategories(): Promise<void> {
  try {
    const list = await listPhotoCategories()
    categories.value = list.length > 0 ? list : FALLBACK_PHOTO_CATEGORIES
  } catch {
    categories.value = FALLBACK_PHOTO_CATEGORIES
  }
}

async function loadList(): Promise<void> {
  const ticket = ++listTicket
  loading.value = true
  loadError.value = ''
  selectedIds.value = []
  previewVisible.value = false
  previewPhoto.value = null

  try {
    const response = await listPhotos({
      keyword: keyword.value,
      category: category.value,
      page: page.value,
      pageSize: PHOTO_PAGE_SIZE,
    })
    if (ticket !== listTicket) return
    items.value = response.items
    total.value = response.total
    if (response.page > 0) page.value = response.page
  } catch (error) {
    if (ticket !== listTicket) return
    items.value = []
    total.value = 0
    loadError.value = photoActionError(error, '照片列表加载失败', '查询照片', 'ARCHIVE_VIEW')
  } finally {
    if (ticket === listTicket) loading.value = false
  }
}

function search(): void {
  page.value = 1
  void loadList()
}

function reset(): void {
  keyword.value = ''
  category.value = ''
  page.value = 1
  void loadList()
}

function handleCategoryChange(): void {
  page.value = 1
  void loadList()
}

function goPage(next: number): void {
  if (next < 1 || next > totalPages.value || next === page.value) return
  page.value = next
  void loadList()
}

function toggleSelect(id: number): void {
  selectedIds.value = selectedIds.value.includes(id)
    ? selectedIds.value.filter((value) => value !== id)
    : [...selectedIds.value, id]
}

function selectAllPage(): void {
  selectedIds.value = items.value.map((item) => item.id)
}

function clearSelection(): void {
  selectedIds.value = []
}

function openPreview(photo: PhotoRecord): void {
  previewPhoto.value = photo
  previewVisible.value = true
}

async function removePhoto(photo: PhotoRecord): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `确定删除照片「${displayPhotoName(photo)}」吗？\n删除后不可恢复。`,
      '确认删除',
      { confirmButtonText: '删除', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  deletingId.value = photo.id
  try {
    await deletePhoto(photo.id)
    ElMessage.success('照片已删除')
    selectedIds.value = selectedIds.value.filter((id) => id !== photo.id)
    if (items.value.length <= 1 && page.value > 1) page.value -= 1
    await loadList()
  } catch (error) {
    ElMessage.error(photoActionError(error, '删除照片失败', '删除照片'))
  } finally {
    deletingId.value = null
  }
}

async function exportSelected(): Promise<void> {
  if (selectedIds.value.length === 0) {
    ElMessage.warning('请先勾选要导出的照片')
    return
  }

  const ids = [...selectedIds.value]
  exporting.value = true
  try {
    const blob = await exportPhotoZip(ids)
    saveBlob(blob, `照片_选中${ids.length}张_${timestampText()}.zip`)
    ElMessage.success('导出文件已开始下载')
  } catch (error) {
    ElMessage.error(photoActionError(error, '导出照片失败', '导出照片', 'ARCHIVE_VIEW'))
  } finally {
    exporting.value = false
  }
}

function openPrint(): void {
  if (selectedIds.value.length === 0) {
    ElMessage.warning('请先勾选要打印的照片')
    return
  }
  printVisible.value = true
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
  () => props.initialCategory,
  (value) => {
    if (value === category.value) return
    category.value = value ?? ''
    page.value = 1
    void loadList()
  },
)

onMounted(() => {
  void loadCategories()
  void loadList()
})

defineExpose({ reload: loadList })
</script>

<template>
  <div class="query-panel">
    <NcCard padding="none" class="filter-card">
      <form class="query-bar" @submit.prevent="search">
        <el-input
          v-model="keyword"
          class="query-keyword"
          clearable
          :prefix-icon="Search"
          placeholder="身份证号 / 照片名"
          @clear="search"
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
        <el-button v-if="hasFilter" text native-type="button" @click="reset">重置</el-button>
        <el-button class="refresh-button" :icon="Refresh" :loading="loading" @click="loadList">
          刷新
        </el-button>
      </form>

      <div class="selection-bar">
        <span class="selection-count">
          已选 <strong>{{ selectedCount }}</strong> 张
          <span class="selection-total">· 当前结果 {{ total }} 张</span>
        </span>
        <div class="selection-actions">
          <el-button link type="primary" @click="allPageSelected ? clearSelection() : selectAllPage()">
            {{ allPageSelected ? '取消全选' : '全选本页' }}
          </el-button>
          <el-button link :disabled="selectedCount === 0" @click="clearSelection">
            清空勾选
          </el-button>
          <el-button
            :icon="Download"
            :loading="exporting"
            :disabled="exporting || selectedCount === 0"
            @click="exportSelected"
          >
            导出选中{{ selectedCount > 0 ? `（${selectedCount}）` : '' }}
          </el-button>
          <el-button
            :icon="Printer"
            :disabled="selectedCount === 0"
            @click="openPrint"
          >
            打印选中{{ selectedCount > 0 ? `（${selectedCount}）` : '' }}
          </el-button>
        </div>
      </div>
    </NcCard>

    <NcCard padding="none" class="list-card" title="照片列表">
      <template #actions>
        <NcStatusDot status="default" :label="`共 ${total} 张`" />
      </template>

      <el-alert
        v-if="loadError"
        class="load-error"
        type="error"
        :title="loadError"
        :closable="false"
        show-icon
      >
        <el-button link type="primary" @click="loadList">重新加载</el-button>
      </el-alert>

      <div v-loading="loading && items.length > 0" class="grid-area">
        <el-skeleton v-if="loading && items.length === 0" :rows="7" animated class="grid-skeleton" />

        <div v-else-if="items.length > 0" class="photo-grid">
          <article
            v-for="photo in items"
            :key="photo.id"
            class="photo-card"
            :class="{ 'is-selected': selectedIds.includes(photo.id) }"
          >
            <div class="photo-card__check" @click.stop>
              <el-checkbox
                :model-value="selectedIds.includes(photo.id)"
                @change="toggleSelect(photo.id)"
              />
            </div>

            <div class="photo-card__thumb">
              <PhotoThumb
                :photo-id="photo.id"
                :alt="displayPhotoName(photo)"
                @open="openPreview(photo)"
              />
            </div>

            <div class="photo-card__body" @click="openPreview(photo)">
              <div class="photo-card__title">
                <strong :title="displayPhotoName(photo)">{{ displayPhotoName(photo) }}</strong>
                <span class="chip">{{ categoryText(photo) }}</span>
              </div>
              <span class="mono photo-card__id">{{ photo.applicantIdCard || '—' }}</span>
              <span class="photo-card__meta">
                {{ formatFileSize(photo.fileSize) }} · {{ formatDateTimeText(photo.createdAt) }}
              </span>
            </div>

            <div class="photo-card__actions" @click.stop>
              <el-button link type="primary" :icon="View" @click="openPreview(photo)">
                查看
              </el-button>
              <el-button
                link
                type="danger"
                :icon="Delete"
                :loading="deletingId === photo.id"
                @click="removePhoto(photo)"
              >
                删除
              </el-button>
            </div>
          </article>
        </div>

        <div v-else-if="!loading && !loadError" class="empty-wrap">
          <el-empty description="暂无照片" :image-size="72">
            <p class="empty-hint">
              可按身份证号、姓名或照片名查询；在「人员补全」中采集的照片也会出现在这里。
            </p>
          </el-empty>
        </div>
      </div>

      <template #footer>
        <div class="footer-bar">
          <span class="pager-note">
            第 {{ page }} / {{ totalPages }} 页 · 每页 {{ PHOTO_PAGE_SIZE }} 张 · 共 {{ total }} 张
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
        </div>
      </template>
    </NcCard>

    <PhotoPreviewDialog v-model="previewVisible" :photo="previewPhoto" />
    <PhotoPrintDialog v-model="printVisible" :photo-ids="selectedIds" />
  </div>
</template>

<style scoped>
.query-panel {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.filter-card,
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
  width: 180px;
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

.selection-total {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.selection-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.selection-actions :deep(.el-button + .el-button) {
  margin-left: 0;
}

.load-error {
  margin: 0;
}

.grid-area {
  min-height: 300px;
  padding: 12px;
}

.grid-skeleton {
  padding: 12px;
}

.photo-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(210px, 1fr));
  gap: 12px;
}

.photo-card {
  position: relative;
  display: flex;
  flex-direction: column;
  min-width: 0;
  overflow: hidden;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-2);
  transition:
    border-color var(--nc-transition),
    box-shadow var(--nc-transition);
}

.photo-card:hover {
  box-shadow: var(--nc-shadow-sm);
}

.photo-card.is-selected {
  border-color: var(--nc-seal);
  box-shadow: inset 0 0 0 1px var(--nc-seal);
}

.photo-card__check {
  position: absolute;
  top: 6px;
  left: 6px;
  z-index: 2;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-xs);
  background-color: rgba(248, 243, 233, 0.9);
}

.photo-card__thumb {
  height: 158px;
  overflow: hidden;
  background-color: var(--nc-paper-3);
}

.photo-card__body {
  display: flex;
  flex-direction: column;
  gap: 3px;
  padding: 9px 10px;
  cursor: zoom-in;
}

.photo-card__title {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
  min-width: 0;
}

.photo-card__title strong {
  overflow: hidden;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.chip {
  flex: none;
  padding: 1px 6px;
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-seal-soft);
  color: var(--nc-seal-dark);
  font-size: 10px;
  font-weight: 600;
  white-space: nowrap;
}

.photo-card__id {
  overflow: hidden;
  color: var(--nc-muted-2);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.photo-card__meta {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.photo-card__actions {
  display: flex;
  justify-content: flex-end;
  gap: 4px;
  padding: 0 10px 8px;
}

.photo-card__actions :deep(.el-button + .el-button) {
  margin-left: 0;
}

.empty-wrap {
  padding: 18px 8px;
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

.footer-bar :deep(.el-pagination) {
  margin-left: auto;
}

@media (max-width: 1200px) {
  .refresh-button {
    margin-left: 0;
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

  .selection-bar {
    padding: 9px 12px;
  }

  .selection-actions {
    width: 100%;
  }

  .selection-actions :deep(.el-button) {
    flex: 1 1 auto;
  }

  .grid-area {
    padding: 9px;
  }

  .photo-grid {
    grid-template-columns: repeat(auto-fill, minmax(150px, 1fr));
    gap: 9px;
  }

  .photo-card__thumb {
    height: 118px;
  }

  .photo-card__title strong {
    font-size: var(--nc-text-sm);
  }

  .footer-bar {
    padding: 10px 12px;
  }

  .footer-bar :deep(.el-pagination) {
    margin-left: 0;
  }
}
</style>
