<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Camera, Delete, Refresh } from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import {
  deleteTempReliefPhoto,
  listTempReliefPhotos,
  recognizeTempReliefIdCard,
  uploadTempReliefPhoto,
} from '@/api/tempRelief'
import type { TempReliefOcrResponse, TempReliefPhoto } from '@/types/tempRelief'

type PhotoCategoryKey = 'ID_CARD' | 'BANK_CARD' | 'MEDICAL_RECORD'
type CategoryFilter = 'ALL' | PhotoCategoryKey

const CATEGORY_OPTIONS: Array<{ value: PhotoCategoryKey; label: string }> = [
  { value: 'ID_CARD', label: '身份证' },
  { value: 'BANK_CARD', label: '银行卡' },
  { value: 'MEDICAL_RECORD', label: '病历' },
]

const MAX_PHOTO_BYTES = 10 * 1024 * 1024

const props = withDefaults(
  defineProps<{
    modelValue: boolean
    applicationId: number | null
    applicantName?: string
    editable?: boolean
  }>(),
  {
    applicantName: '',
    editable: false,
  },
)

const emit = defineEmits<{
  (event: 'update:modelValue', value: boolean): void
  (event: 'recognized', result: TempReliefOcrResponse): void
  (event: 'changed'): void
}>()

const visible = computed({
  get: () => props.modelValue,
  set: (value: boolean) => emit('update:modelValue', value),
})

const photos = ref<TempReliefPhoto[]>([])
const loading = ref(false)
const loadError = ref('')
const uploading = ref(false)
const recognizingId = ref<number | null>(null)
const activeCategory = ref<CategoryFilter>('ALL')
const fileInput = ref<HTMLInputElement | null>(null)
const pendingCategory = ref<PhotoCategoryKey>('ID_CARD')

let requestTicket = 0

const categoryLabelMap = new Map(CATEGORY_OPTIONS.map((item) => [item.value, item.label]))

const filteredPhotos = computed(() => {
  if (activeCategory.value === 'ALL') return photos.value
  return photos.value.filter((photo) => photo.photoCategory === activeCategory.value)
})

const previewSources = computed(() => filteredPhotos.value.map((photo) => photo.dataUrl))

const uploadDisabled = computed(
  () => !props.editable || props.applicationId === null || uploading.value,
)

const uploadDisabledReason = computed(() => {
  if (props.applicationId === null) return '请先保存草稿，再上传照片'
  if (!props.editable) return '当前状态不可上传照片'
  return ''
})

function categoryText(photo: TempReliefPhoto): string {
  return photo.photoCategoryName || categoryLabelMap.get(photo.photoCategory as PhotoCategoryKey) || photo.photoCategory
}

function formatBytes(value: number): string {
  if (!Number.isFinite(value) || value <= 0) return '—'
  if (value < 1024) return `${value} B`
  if (value < 1024 * 1024) return `${(value / 1024).toFixed(1)} KB`
  return `${(value / 1024 / 1024).toFixed(2)} MB`
}

function formatDate(value: string): string {
  if (!value) return '—'
  const text = value.slice(0, 10)
  return /^\d{4}-\d{2}-\d{2}$/.test(text) ? text : value
}

async function load(): Promise<void> {
  const id = props.applicationId
  if (id === null) {
    photos.value = []
    loadError.value = ''
    return
  }

  const ticket = ++requestTicket
  loading.value = true
  loadError.value = ''
  try {
    const response = await listTempReliefPhotos(id)
    if (ticket !== requestTicket) return
    photos.value = response.items ?? []
  } catch (error) {
    if (ticket !== requestTicket) return
    photos.value = []
    loadError.value = getErrorMessage(error, '照片加载失败')
  } finally {
    if (ticket === requestTicket) loading.value = false
  }
}

function triggerUpload(category: PhotoCategoryKey): void {
  if (uploadDisabled.value) {
    if (uploadDisabledReason.value) ElMessage.warning(uploadDisabledReason.value)
    return
  }
  pendingCategory.value = category
  fileInput.value?.click()
}

async function handleFilePicked(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0] ?? null
  input.value = ''
  if (!file) return

  const id = props.applicationId
  if (id === null) return

  if (!file.type.startsWith('image/')) {
    ElMessage.warning('仅支持图片文件')
    return
  }
  if (file.size > MAX_PHOTO_BYTES) {
    ElMessage.warning('照片不能超过 10MB')
    return
  }

  uploading.value = true
  try {
    await uploadTempReliefPhoto(id, file, {
      category: pendingCategory.value,
      photoName: file.name,
    })
    ElMessage.success('照片已上传')
    await load()
    emit('changed')
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '照片上传失败'))
  } finally {
    uploading.value = false
  }
}

async function removePhoto(photo: TempReliefPhoto): Promise<void> {
  const id = props.applicationId
  if (id === null || !props.editable) return

  try {
    await ElMessageBox.confirm(
      `确定删除照片「${photo.photoName || `#${photo.id}`}」吗？此操作不可恢复。`,
      '删除照片',
      { confirmButtonText: '删除', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  try {
    await deleteTempReliefPhoto(id, photo.id)
    ElMessage.success('照片已删除')
    await load()
    emit('changed')
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '照片删除失败'))
  }
}

function dataUrlToFile(photo: TempReliefPhoto): Promise<File> {
  const name = photo.photoName || `temp-relief-photo-${photo.id}.jpg`
  if (!photo.dataUrl) return Promise.reject(new Error('照片数据为空，无法识别'))
  if (photo.dataUrl.startsWith('data:')) {
    const [meta, base64] = photo.dataUrl.split(',')
    const mime = /data:([^;]+)/.exec(meta)?.[1] ?? 'image/jpeg'
    const binary = atob(base64 ?? '')
    const bytes = new Uint8Array(binary.length)
    for (let index = 0; index < binary.length; index += 1) {
      bytes[index] = binary.charCodeAt(index)
    }
    return Promise.resolve(new File([bytes], name, { type: mime }))
  }

  return fetch(photo.dataUrl)
    .then((response) => {
      if (!response.ok) throw new Error(`照片下载失败（HTTP ${response.status}）`)
      return response.blob()
    })
    .then((blob) => new File([blob], name, { type: blob.type || 'image/jpeg' }))
}

async function recognizePhoto(photo: TempReliefPhoto): Promise<void> {
  const id = props.applicationId
  if (id === null) return

  recognizingId.value = photo.id
  try {
    const file = await dataUrlToFile(photo)
    const result = await recognizeTempReliefIdCard(file, { id, category: 'ID_CARD' })
    emit('recognized', result)
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '身份证识别失败'))
  } finally {
    recognizingId.value = null
  }
}

function handleClosed(): void {
  requestTicket += 1
  photos.value = []
  loadError.value = ''
  activeCategory.value = 'ALL'
  recognizingId.value = null
  uploading.value = false
}

watch(
  () => props.modelValue,
  (open) => {
    if (open) void load()
  },
)

watch(
  () => props.applicationId,
  () => {
    if (props.modelValue) void load()
  },
)
</script>

<template>
  <el-dialog
    v-model="visible"
    title="证件与材料照片"
    width="min(920px, 94vw)"
    append-to-body
    class="temp-relief-photos"
    @closed="handleClosed"
  >
    <div class="photos-head">
      <div class="photos-head__subject">
        <span class="photos-head__label">申请人</span>
        <strong>{{ applicantName || '—' }}</strong>
        <span v-if="uploadDisabledReason" class="photos-head__hint">{{ uploadDisabledReason }}</span>
      </div>
      <div class="photos-head__actions">
        <el-button
          v-for="category in CATEGORY_OPTIONS"
          :key="category.value"
          size="small"
          :icon="Camera"
          :disabled="uploadDisabled"
          :loading="uploading && pendingCategory === category.value"
          @click="triggerUpload(category.value)"
        >
          上传{{ category.label }}
        </el-button>
        <el-button size="small" :icon="Refresh" :loading="loading" @click="load">刷新</el-button>
      </div>
    </div>

    <input
      ref="fileInput"
      type="file"
      accept="image/*"
      class="photos-file"
      @change="handleFilePicked"
    />

    <el-alert
      v-if="loadError"
      class="photos-alert"
      type="error"
      :title="loadError"
      :closable="false"
      show-icon
    >
      <el-button link type="primary" @click="load">重新加载</el-button>
    </el-alert>

    <div class="photos-filter">
      <el-radio-group v-model="activeCategory" size="small">
        <el-radio-button value="ALL">全部（{{ photos.length }}）</el-radio-button>
        <el-radio-button
          v-for="category in CATEGORY_OPTIONS"
          :key="category.value"
          :value="category.value"
        >
          {{ category.label }}（{{ photos.filter((item) => item.photoCategory === category.value).length }}）
        </el-radio-button>
      </el-radio-group>
    </div>

    <div v-loading="loading" class="photos-grid">
      <figure v-for="(photo, index) in filteredPhotos" :key="photo.id" class="photo-card">
        <el-image
          class="photo-card__image"
          :src="photo.dataUrl"
          :alt="photo.photoName"
          :preview-src-list="previewSources"
          :initial-index="index"
          fit="cover"
          preview-teleported
        />
        <figcaption class="photo-card__body">
          <span class="photo-card__name" :title="photo.photoName">
            {{ photo.photoName || `照片 #${photo.id}` }}
          </span>
          <span class="photo-card__meta">
            {{ categoryText(photo) }} · {{ formatBytes(photo.fileSize) }} · {{ formatDate(photo.createdAt) }}
          </span>
          <span class="photo-card__meta">
            上传：{{ photo.createdByName || '—' }}
          </span>
          <div class="photo-card__actions">
            <el-button
              v-if="editable && photo.photoCategory === 'ID_CARD'"
              link
              type="primary"
              :loading="recognizingId === photo.id"
              @click="recognizePhoto(photo)"
            >
              识别
            </el-button>
            <el-button
              v-if="editable"
              link
              type="danger"
              :icon="Delete"
              @click="removePhoto(photo)"
            >
              删除
            </el-button>
          </div>
        </figcaption>
      </figure>

      <div v-if="!loading && filteredPhotos.length === 0 && !loadError" class="photos-empty">
        <el-empty :image-size="72" description="暂无照片" />
      </div>
    </div>

    <template #footer>
      <span class="photos-footnote">
        支持身份证、银行卡、病历三类，单张不超过 10MB；身份证照片可点「识别」回填申请人信息。
      </span>
      <el-button @click="visible = false">关闭</el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.photos-head {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 12px;
}

.photos-head__subject {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 8px;
  min-width: 0;
}

.photos-head__label {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  letter-spacing: 0.12em;
}

.photos-head__subject strong {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-lg);
}

.photos-head__hint {
  color: var(--nc-seal-dark);
  font-size: var(--nc-text-xs);
}

.photos-head__actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.photos-file {
  display: none;
}

.photos-alert {
  margin-bottom: 10px;
}

.photos-filter {
  margin-bottom: 12px;
  overflow-x: auto;
}

.photos-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
  gap: 12px;
  min-height: 140px;
}

.photo-card {
  display: flex;
  flex-direction: column;
  margin: 0;
  overflow: hidden;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper);
}

.photo-card__image {
  display: block;
  width: 100%;
  height: 150px;
  background-color: var(--nc-paper-3);
  cursor: zoom-in;
}

.photo-card__body {
  display: flex;
  flex-direction: column;
  gap: 3px;
  padding: 8px 10px;
}

.photo-card__name {
  overflow: hidden;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.photo-card__meta {
  color: var(--nc-muted);
  font-size: 11px;
  line-height: 1.5;
}

.photo-card__actions {
  display: flex;
  align-items: center;
  gap: 4px;
  margin-top: 3px;
}

.photos-empty {
  grid-column: 1 / -1;
  padding: 20px 0;
}

.photos-footnote {
  margin-right: 12px;
  color: var(--nc-muted);
  font-size: 12px;
}

@media (max-width: 640px) {
  .photos-head {
    flex-direction: column;
  }

  .photos-head__actions :deep(.el-button) {
    flex: 1;
  }

  .photos-grid {
    grid-template-columns: repeat(auto-fill, minmax(130px, 1fr));
  }

  .photo-card__image {
    height: 120px;
  }

  .photos-footnote {
    display: block;
    margin: 0 0 8px;
    text-align: left;
  }
}
</style>
