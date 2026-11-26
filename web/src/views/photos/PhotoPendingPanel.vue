<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Check, Delete, Refresh, View } from '@element-plus/icons-vue'
import {
  assignPendingPhoto,
  deletePendingPhoto,
  listPhotoBackfill,
  listPendingPhotos,
} from '@/api/photos'
import type { PhotoPendingItem, PhotoRecord } from '@/types/photos'
import NcCard from '@/components/NcCard.vue'
import NcStatusDot from '@/components/NcStatusDot.vue'
import PhotoPreviewDialog from './PhotoPreviewDialog.vue'
import PhotoThumb from './PhotoThumb.vue'
import {
  FALLBACK_PHOTO_CATEGORIES,
  PHOTO_PAGE_SIZE,
  categoryLabelFrom,
  domainLabel,
  formatDateTimeText,
  formatFileSize,
  isIdCard,
  photoActionError,
} from './photoMeta'

const items = ref<PhotoPendingItem[]>([])
const page = ref(1)
const total = ref(0)
const loading = ref(false)
const loadError = ref('')
const scopeNote = ref('')
const deletingId = ref<number | null>(null)

const previewVisible = ref(false)
const previewPhoto = ref<PhotoRecord | null>(null)

const assignVisible = ref(false)
const assignTarget = ref<PhotoPendingItem | null>(null)
const assignIdCard = ref('')
const assignName = ref('')
const assignHint = ref('')
const assignHintType = ref<'success' | 'warning' | 'info'>('info')
const matching = ref(false)
const assigning = ref(false)

let listTicket = 0
let matchTicket = 0

const totalPages = computed(() => Math.max(1, Math.ceil(total.value / PHOTO_PAGE_SIZE)))

function categoryText(item: PhotoPendingItem): string {
  return categoryLabelFrom(FALLBACK_PHOTO_CATEGORIES, item.category, item.categoryLabel)
}

function displayName(item: PhotoPendingItem): string {
  return item.recognizedName || item.photoName || `待归属照片 #${item.id}`
}

function toPreviewRecord(item: PhotoPendingItem): PhotoRecord {
  return {
    id: item.id,
    applicantIdCard: item.recognizedIdCard || '—',
    applicationId: null,
    category: item.category,
    categoryLabel: categoryText(item),
    photoName: displayName(item),
    fileSize: item.fileSize,
    imageFormat: item.imageFormat,
    createdAt: item.createdAt,
    hasData: true,
    personName: item.recognizedName,
  }
}

async function loadList(): Promise<void> {
  const ticket = ++listTicket
  loading.value = true
  loadError.value = ''
  previewVisible.value = false
  previewPhoto.value = null

  try {
    const response = await listPendingPhotos({ page: page.value, pageSize: PHOTO_PAGE_SIZE })
    if (ticket !== listTicket) return
    items.value = response.items
    total.value = response.total
    scopeNote.value = response.message ?? ''
    if (response.page > 0) page.value = response.page
    if (!response.available) {
      loadError.value = '当前服务端尚未启用待归属照片池，请联系管理员升级服务'
      items.value = []
      total.value = 0
    }
  } catch (error) {
    if (ticket !== listTicket) return
    items.value = []
    total.value = 0
    scopeNote.value = ''
    loadError.value = photoActionError(error, '待归属照片加载失败', '查询待归属照片', 'ARCHIVE_VIEW')
  } finally {
    if (ticket === listTicket) loading.value = false
  }
}

function goPage(next: number): void {
  if (next < 1 || next > totalPages.value || next === page.value) return
  page.value = next
  void loadList()
}

function openPreview(item: PhotoPendingItem): void {
  previewPhoto.value = toPreviewRecord(item)
  previewVisible.value = true
}

async function removeItem(item: PhotoPendingItem): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `确定删除待归属照片「${displayName(item)}」吗？\n删除后不可恢复。`,
      '删除待归属照片',
      { confirmButtonText: '删除', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  deletingId.value = item.id
  try {
    await deletePendingPhoto(item.id)
    ElMessage.success('待归属照片已删除')
    if (items.value.length <= 1 && page.value > 1) page.value -= 1
    await loadList()
  } catch (error) {
    ElMessage.error(photoActionError(error, '删除待归属照片失败', '删除照片'))
  } finally {
    deletingId.value = null
  }
}

/** 用补传候选查询（四业务域档案人员）校验身份证并带出姓名。 */
async function lookupPerson(): Promise<void> {
  const idCard = assignIdCard.value.trim()
  assignHint.value = ''

  if (!idCard) {
    assignHintType.value = 'info'
    assignName.value = ''
    return
  }

  if (!isIdCard(idCard)) {
    assignHintType.value = 'warning'
    assignHint.value = '身份证号格式不正确（需 18 位）'
    return
  }

  const ticket = ++matchTicket
  matching.value = true
  try {
    const result = await listPhotoBackfill({ keyword: idCard, page: 1, pageSize: 10 })
    if (ticket !== matchTicket) return

    const hit = result.items.find((person) => person.idCard === idCard)
    if (hit) {
      if (hit.personName && hit.personName !== '—') assignName.value = hit.personName
      const domains = hit.businessTypes.map(domainLabel).filter(Boolean).join(' · ')
      assignHintType.value = 'success'
      assignHint.value = `已匹配档案人员：${assignName.value || '（姓名为空）'}${
        domains ? `（${domains}）` : ''
      }`
    } else {
      assignName.value = ''
      assignHintType.value = 'warning'
      assignHint.value = '未在低收入/高龄/临时救助/资产核查档案中匹配到该身份证，需先建立申请后再归属'
    }
  } catch (error) {
    if (ticket !== matchTicket) return
    assignHintType.value = 'warning'
    assignHint.value = photoActionError(error, '人员匹配失败', '查询人员', 'ARCHIVE_VIEW')
  } finally {
    if (ticket === matchTicket) matching.value = false
  }
}

function openAssign(item: PhotoPendingItem): void {
  assignTarget.value = item
  assignIdCard.value = item.recognizedIdCard || ''
  assignName.value = item.recognizedName || ''
  assignHint.value = ''
  assignHintType.value = 'info'
  assignVisible.value = true

  if (isIdCard(assignIdCard.value)) void lookupPerson()
}

async function confirmAssign(): Promise<void> {
  const item = assignTarget.value
  if (!item) return

  const idCard = assignIdCard.value.trim()
  if (!isIdCard(idCard)) {
    ElMessage.warning('请输入正确的 18 位身份证号')
    return
  }

  assigning.value = true
  try {
    await assignPendingPhoto(item.id, {
      idCard,
      name: assignName.value.trim() || undefined,
    })
    ElMessage.success(`已归属「${displayName(item)}」到档案照片`)
    assignVisible.value = false
    if (items.value.length <= 1 && page.value > 1) page.value -= 1
    await loadList()
  } catch (error) {
    ElMessage.error(photoActionError(error, '照片归属失败', '归属照片'))
  } finally {
    assigning.value = false
  }
}

onMounted(() => {
  void loadList()
})

defineExpose({ reload: loadList })
</script>

<template>
  <div class="pending-panel">
    <NcCard padding="none" class="list-card" title="待归属照片">
      <template #actions>
        <div class="head-actions">
          <NcStatusDot status="idle" :label="`待归属 ${total} 张`" />
          <el-button :icon="Refresh" :loading="loading" text @click="loadList">刷新</el-button>
        </div>
      </template>

      <el-alert
        v-if="scopeNote && !loadError"
        class="scope-note"
        type="info"
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
        <el-button link type="primary" @click="loadList">重新加载</el-button>
      </el-alert>

      <div v-loading="loading && items.length > 0" class="grid-area">
        <el-skeleton v-if="loading && items.length === 0" :rows="7" animated class="grid-skeleton" />

        <div v-else-if="items.length > 0" class="pending-grid">
          <article v-for="item in items" :key="item.id" class="pending-card">
            <div class="pending-card__thumb">
              <PhotoThumb
                :photo-id="item.id"
                source="pending"
                :alt="displayName(item)"
                @open="openPreview(item)"
              />
              <span class="pending-card__badge">{{ categoryText(item) }}</span>
            </div>

            <div class="pending-card__body" @click="openPreview(item)">
              <div class="pending-card__title">
                <strong :title="displayName(item)">{{ displayName(item) }}</strong>
              </div>
              <span class="mono pending-card__id">
                {{ item.recognizedIdCard || '未识别身份证' }}
              </span>
              <span class="pending-card__meta">
                {{ formatFileSize(item.fileSize) }} · {{ formatDateTimeText(item.createdAt) }}
              </span>
              <span class="pending-card__meta">
                上传人：{{ item.createdByName || '—' }}
                <template v-if="item.source"> · {{ item.source }}</template>
              </span>
            </div>

            <div class="pending-card__actions" @click.stop>
              <el-button link type="primary" :icon="View" @click="openPreview(item)">预览</el-button>
              <el-button link type="primary" :icon="Check" @click="openAssign(item)">归属</el-button>
              <el-button
                link
                type="danger"
                :icon="Delete"
                :loading="deletingId === item.id"
                @click="removeItem(item)"
              >
                删除
              </el-button>
            </div>
          </article>
        </div>

        <div v-else-if="!loading && !loadError" class="empty-wrap">
          <el-empty description="暂无待归属照片" :image-size="72">
            <p class="empty-hint">
              未带身份证采集的照片会先进入待归属池；拿到身份证并确认人员已建档后，在此归属到档案照片。
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

    <PhotoPreviewDialog v-model="previewVisible" :photo="previewPhoto" source="pending" />

    <el-dialog
      v-model="assignVisible"
      title="归属到人员"
      width="min(520px, 92vw)"
      append-to-body
      destroy-on-close
      class="assign-dialog"
    >
      <div v-if="assignTarget" class="assign-body">
        <div class="assign-photo">
          <div class="assign-photo__thumb">
            <PhotoThumb
              :photo-id="assignTarget.id"
              source="pending"
              :alt="displayName(assignTarget)"
            />
          </div>
          <div class="assign-photo__meta">
            <strong>{{ displayName(assignTarget) }}</strong>
            <span>{{ categoryText(assignTarget) }} · {{ formatFileSize(assignTarget.fileSize) }}</span>
            <span>{{ formatDateTimeText(assignTarget.createdAt) }}</span>
          </div>
        </div>

        <el-form label-position="top" @submit.prevent="confirmAssign">
          <el-form-item label="身份证号（须已在业务档案中）" required>
            <el-input
              v-model="assignIdCard"
              maxlength="18"
              clearable
              placeholder="请输入 18 位身份证号"
              @blur="lookupPerson"
              @keyup.enter="lookupPerson"
            >
              <template #append>
                <el-button :loading="matching" @click="lookupPerson">匹配</el-button>
              </template>
            </el-input>
          </el-form-item>

          <el-form-item label="姓名">
            <el-input
              v-model="assignName"
              maxlength="30"
              placeholder="匹配到档案人员后自动带出，可修改"
            />
          </el-form-item>

          <el-alert
            v-if="assignHint"
            :type="assignHintType"
            :title="assignHint"
            :closable="false"
            show-icon
            class="assign-hint"
          />

          <p class="assign-note">
            归属后照片写入正式档案照片库（保持原类别 {{ categoryText(assignTarget) }}），并从待归属池移除。
          </p>
        </el-form>
      </div>

      <template #footer>
        <el-button @click="assignVisible = false">取消</el-button>
        <el-button
          type="primary"
          :loading="assigning"
          :disabled="!isIdCard(assignIdCard.trim())"
          @click="confirmAssign"
        >
          确认归属
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.pending-panel {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.list-card {
  overflow: hidden;
}

.head-actions {
  display: flex;
  align-items: center;
  gap: 10px;
}

.scope-note {
  margin: 0 12px 10px;
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

.pending-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(240px, 1fr));
  gap: 12px;
}

.pending-card {
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

.pending-card:hover {
  box-shadow: var(--nc-shadow-sm);
}

.pending-card__thumb {
  position: relative;
  height: 158px;
  overflow: hidden;
  background-color: var(--nc-paper-3);
}

.pending-card__badge {
  position: absolute;
  top: 6px;
  right: 6px;
  z-index: 2;
  padding: 1px 6px;
  border-radius: var(--nc-radius-xs);
  background-color: rgba(166, 0, 0, 0.85);
  color: #fff;
  font-size: 10px;
  font-weight: 600;
  white-space: nowrap;
}

.pending-card__body {
  display: flex;
  flex-direction: column;
  gap: 3px;
  padding: 9px 10px;
  cursor: zoom-in;
}

.pending-card__title {
  display: flex;
  align-items: center;
  min-width: 0;
}

.pending-card__title strong {
  overflow: hidden;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.pending-card__id {
  overflow: hidden;
  color: var(--nc-muted-2);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.pending-card__meta {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.pending-card__actions {
  display: flex;
  justify-content: flex-end;
  gap: 4px;
  padding: 0 10px 8px;
}

.pending-card__actions :deep(.el-button + .el-button) {
  margin-left: 0;
}

.empty-wrap {
  padding: 18px 8px;
}

.empty-hint {
  max-width: 520px;
  margin: 6px auto 0;
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

.assign-body {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.assign-photo {
  display: flex;
  align-items: center;
  gap: 12px;
}

.assign-photo__thumb {
  flex: none;
  width: 84px;
  height: 84px;
  overflow: hidden;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-3);
}

.assign-photo__meta {
  display: flex;
  flex-direction: column;
  gap: 3px;
  min-width: 0;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.assign-photo__meta strong {
  overflow: hidden;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.assign-hint {
  margin-bottom: 4px;
}

.assign-note {
  margin: 0;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.6;
}

@media (max-width: 767px) {
  .pending-grid {
    grid-template-columns: 1fr;
  }

  .grid-area {
    padding: 9px;
  }

  .pending-card__thumb {
    height: 180px;
  }

  .footer-bar {
    padding: 10px 12px;
  }

  .footer-bar :deep(.el-pagination) {
    margin-left: 0;
  }

  .assign-photo__thumb {
    width: 68px;
    height: 68px;
  }
}
</style>
