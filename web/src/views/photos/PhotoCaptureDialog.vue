<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Camera, Delete, Refresh, Upload } from '@element-plus/icons-vue'
import { getErrorMessage } from '@/api/http'
import {
  deletePhoto,
  listPersonPhotos,
  listPhotoCategories,
  recognizePhotoIdCard,
  uploadPersonPhoto,
} from '@/api/photos'
import type {
  PhotoCaptureOutcome,
  PhotoCapturePerson,
  PhotoCaptureSummary,
  PhotoCategory,
  PhotoOcrResult,
  PhotoRecord,
} from '@/types/photos'
import { useMediaQuery } from '@/composables/useMediaQuery'
import { compressImageFile } from './imageCompress'
import PhotoPreviewDialog from './PhotoPreviewDialog.vue'
import PhotoThumb from './PhotoThumb.vue'
import {
  FALLBACK_PHOTO_CATEGORIES,
  MAX_PHOTO_BYTES,
  categoryLabelFrom,
  displayPhotoName,
  domainLabel,
  formatDateTimeText,
  formatFileSize,
  photoActionError,
} from './photoMeta'

interface PendingPhoto {
  key: string
  file: File
  url: string
  category: string
}

interface PhotoGroup {
  code: string
  label: string
  items: PhotoRecord[]
}

interface PersonStats {
  uploaded: number
  failed: number
}

const props = withDefaults(
  defineProps<{
    modelValue: boolean
    people: PhotoCapturePerson[]
    startIndex?: number
    presetCategory?: string
  }>(),
  {
    startIndex: 0,
    presetCategory: '',
  },
)

const emit = defineEmits<{
  (event: 'update:modelValue', value: boolean): void
  (event: 'changed'): void
  (event: 'finished', summary: PhotoCaptureSummary): void
}>()

const isNarrow = useMediaQuery('(max-width: 900px)')

const visible = computed({
  get: () => props.modelValue,
  set: (value: boolean) => emit('update:modelValue', value),
})

const categories = ref<PhotoCategory[]>([])
const selectedCategory = ref('ID_CARD')
const index = ref(0)
const photos = ref<PhotoRecord[]>([])
const photosLoading = ref(false)
const photosError = ref('')
const pending = ref<PendingPhoto[]>([])
const uploading = ref(false)
const compressing = ref(false)
const ocrBusy = ref(false)
const latestOcr = ref<PhotoOcrResult | null>(null)
const ocrMismatch = ref(false)
const ocrWarnings = ref<string[]>([])
const ocrError = ref('')

const outcomes = ref<Record<string, PhotoCaptureOutcome>>({})
const stats = ref<Record<string, PersonStats>>({})
const finished = ref(false)
const summary = ref<PhotoCaptureSummary | null>(null)

const previewPhoto = ref<PhotoRecord | null>(null)
const previewVisible = ref(false)

const cameraInput = ref<HTMLInputElement | null>(null)
const fileInput = ref<HTMLInputElement | null>(null)

let photoTicket = 0
let pendingSeq = 0
let advanceTimer: number | undefined

const current = computed<PhotoCapturePerson | null>(() => props.people[index.value] ?? null)
const isLast = computed(() => index.value >= props.people.length - 1)
const busy = computed(() => uploading.value || compressing.value || ocrBusy.value)
const pendingCount = computed(() => pending.value.length)
const selectedCategoryLabel = computed(() =>
  categoryLabelFrom(categories.value, selectedCategory.value),
)
const hasIdentityInOcr = computed(
  () => Boolean(latestOcr.value?.name || latestOcr.value?.idCard),
)

const title = computed(() => {
  if (finished.value) return '照片补全 · 汇总'
  if (!current.value) return '照片补全'
  return `照片补全 · 第 ${index.value + 1} / ${props.people.length} 人`
})

const primaryLabel = computed(() => {
  if (pendingCount.value > 0) return isLast.value ? '保存并完成' : '保存并下一位'
  return isLast.value ? '完成' : '下一位'
})

const groupedPhotos = computed<PhotoGroup[]>(() => {
  const map = new Map<string, PhotoRecord[]>()
  for (const photo of photos.value) {
    const code = photo.category || 'OTHER_CERT'
    const list = map.get(code)
    if (list) list.push(photo)
    else map.set(code, [photo])
  }

  const order = categories.value.map((item) => item.code)
  return [...map.entries()]
    .sort((left, right) => {
      const leftIndex = order.indexOf(left[0])
      const rightIndex = order.indexOf(right[0])
      const leftRank = leftIndex === -1 ? Number.MAX_SAFE_INTEGER : leftIndex
      const rightRank = rightIndex === -1 ? Number.MAX_SAFE_INTEGER : rightIndex
      return leftRank - rightRank || left[0].localeCompare(right[0])
    })
    .map(([code, items]) => ({
      code,
      label: categoryLabelFrom(categories.value, code, items[0]?.categoryLabel),
      items,
    }))
})

function clearAdvance(): void {
  if (advanceTimer !== undefined) {
    window.clearTimeout(advanceTimer)
    advanceTimer = undefined
  }
}

function scheduleAdvance(): void {
  clearAdvance()
  advanceTimer = window.setTimeout(() => {
    advanceTimer = undefined
    if (visible.value && !finished.value) advance(false)
  }, 1000)
}

function revokePending(items: PendingPhoto[]): void {
  for (const item of items) URL.revokeObjectURL(item.url)
}

function clearPending(): void {
  revokePending(pending.value)
  pending.value = []
}

function addFiles(files: File[]): void {
  if (files.length === 0 || busy.value) return
  void (async () => {
    compressing.value = true
    let added = 0
    let failed = 0
    let firstError = ''

    for (const file of files) {
      try {
        const compressed = await compressImageFile(file)
        if (compressed.size > MAX_PHOTO_BYTES) {
          throw new Error('照片压缩后仍超过 10MB')
        }
        pendingSeq += 1
        pending.value = [
          ...pending.value,
          {
            key: `pending-${pendingSeq}`,
            file: compressed,
            url: URL.createObjectURL(compressed),
            category: selectedCategory.value,
          },
        ]
        added += 1
      } catch (error) {
        failed += 1
        if (!firstError) firstError = getErrorMessage(error, '图片处理失败')
      }
    }

    compressing.value = false
    if (added > 0) ElMessage.success(`已加入 ${added} 张待上传照片`)
    if (failed > 0) {
      ElMessage.warning(`有 ${failed} 张未加入${firstError ? `：${firstError}` : ''}`)
    }
  })()
}

function onFilesPicked(event: Event): void {
  const input = event.target as HTMLInputElement
  const files = input.files ? Array.from(input.files) : []
  input.value = ''
  if (files.length > 0) addFiles(files)
}

function openCamera(): void {
  if (busy.value) return
  cameraInput.value?.click()
}

function openFiles(): void {
  if (busy.value) return
  fileInput.value?.click()
}

function removePending(item: PendingPhoto): void {
  URL.revokeObjectURL(item.url)
  pending.value = pending.value.filter((entry) => entry.key !== item.key)
}

function removePendingByKey(key: string): void {
  const item = pending.value.find((entry) => entry.key === key)
  if (item) URL.revokeObjectURL(item.url)
  pending.value = pending.value.filter((entry) => entry.key !== key)
}

async function loadCategories(): Promise<void> {
  if (categories.value.length === 0) {
    try {
      const list = await listPhotoCategories()
      categories.value = list.length > 0 ? list : FALLBACK_PHOTO_CATEGORIES
    } catch {
      categories.value = FALLBACK_PHOTO_CATEGORIES
    }
  }
  if (!categories.value.some((item) => item.code === selectedCategory.value)) {
    selectedCategory.value = categories.value[0]?.code ?? 'ID_CARD'
  }
}

async function loadPhotos(): Promise<void> {
  const person = current.value
  const ticket = ++photoTicket
  photosError.value = ''

  if (!person) {
    photos.value = []
    photosLoading.value = false
    return
  }

  photosLoading.value = true
  try {
    const list = await listPersonPhotos(person.idCard)
    if (ticket !== photoTicket) return
    photos.value = list
  } catch (error) {
    if (ticket !== photoTicket) return
    photos.value = []
    photosError.value = photoActionError(
      error,
      '该人员照片加载失败',
      '查看照片',
      'ARCHIVE_VIEW',
    )
  } finally {
    if (ticket === photoTicket) photosLoading.value = false
  }
}

function openPreview(photo: PhotoRecord): void {
  previewPhoto.value = photo
  previewVisible.value = true
}

async function removeExisting(photo: PhotoRecord): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `确定删除照片「${displayPhotoName(photo)}」吗？删除后不可恢复。`,
      '删除照片',
      { confirmButtonText: '删除', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  try {
    await deletePhoto(photo.id)
    photos.value = photos.value.filter((item) => item.id !== photo.id)
    ElMessage.success('照片已删除')
    emit('changed')
  } catch (error) {
    ElMessage.error(photoActionError(error, '删除照片失败', '删除照片'))
  }
}

async function recognize(file: File, person: PhotoCapturePerson): Promise<boolean> {
  ocrBusy.value = true
  ocrError.value = ''
  try {
    const result = await recognizePhotoIdCard(file)
    latestOcr.value = result

    const warnings: string[] = []
    const ocrName = result.name.trim()
    const ocrIdCard = result.idCard.trim().toUpperCase()
    const personName = person.name.trim()
    const personIdCard = person.idCard.trim().toUpperCase()

    if (ocrName && personName && ocrName !== personName) {
      warnings.push(`姓名不一致：识别为「${ocrName}」，当前人员「${personName}」`)
    }
    if (ocrIdCard && personIdCard && ocrIdCard !== personIdCard) {
      warnings.push(`身份证不一致：识别为「${ocrIdCard}」，当前人员「${personIdCard}」`)
    }

    ocrWarnings.value = warnings
    if (warnings.length > 0) {
      ocrMismatch.value = true
      ElMessage.warning('身份证识别信息与当前人员不一致，请核对')
    }
    return warnings.length > 0 || ocrMismatch.value
  } catch (error) {
    ocrError.value = getErrorMessage(error, '身份证识别失败（可手动核对后继续）')
    return ocrMismatch.value
  } finally {
    ocrBusy.value = false
  }
}

async function uploadPending(goNext: boolean): Promise<void> {
  const person = current.value
  if (!person || uploading.value || compressing.value) return

  if (pending.value.length === 0) {
    if (goNext) advance(true)
    return
  }

  const batch = [...pending.value]
  if (batch.some((item) => item.category === 'ID_CARD')) {
    latestOcr.value = null
    ocrMismatch.value = false
    ocrWarnings.value = []
    ocrError.value = ''
  }

  uploading.value = true
  let ok = 0
  let failed = 0
  let firstError = ''
  let mismatch = false

  for (const item of batch) {
    try {
      await uploadPersonPhoto(person.idCard, {
        file: item.file,
        category: item.category,
        applicationId: person.businessId,
        businessType: person.businessTypes[0],
      })
      ok += 1
      removePendingByKey(item.key)

      if (item.category === 'ID_CARD') {
        const result = await recognize(item.file, person)
        mismatch = mismatch || result
      }
    } catch (error) {
      failed += 1
      if (!firstError) firstError = getErrorMessage(error, '照片上传失败')
    }
  }

  const previous = stats.value[person.idCard] ?? { uploaded: 0, failed: 0 }
  stats.value = {
    ...stats.value,
    [person.idCard]: {
      uploaded: previous.uploaded + ok,
      failed: previous.failed + failed,
    },
  }

  if (ok > 0) {
    await loadPhotos()
    emit('changed')
  }

  uploading.value = false

  if (ok > 0 && failed === 0) {
    ElMessage.success(`已保存 ${ok} 张照片`)
  } else if (ok > 0) {
    ElMessage.warning(`已保存 ${ok} 张，${failed} 张失败${firstError ? `：${firstError}` : ''}`)
  } else {
    ElMessage.error(firstError || '照片上传失败')
  }

  if (!goNext || ok === 0) return

  if (failed > 0) {
    ElMessage.warning('仍有照片未上传成功，已停留在当前人员')
    return
  }

  if (mismatch) {
    ElMessage.warning('识别信息与当前人员不一致，请核对后点击「下一位」')
    return
  }

  scheduleAdvance()
}

function settleCurrent(markSkipped: boolean): void {
  const person = current.value
  if (!person) return

  const personStats = stats.value[person.idCard]
  if (personStats && personStats.uploaded > 0) {
    outcomes.value = { ...outcomes.value, [person.idCard]: 'success' }
  } else if (personStats && personStats.failed > 0) {
    outcomes.value = { ...outcomes.value, [person.idCard]: 'failed' }
  } else if (markSkipped) {
    outcomes.value = { ...outcomes.value, [person.idCard]: 'skipped' }
  }
}

function buildSummary(): PhotoCaptureSummary {
  let succeeded = 0
  let skipped = 0
  let failed = 0
  let uploadedPhotos = 0
  let failedPhotos = 0
  const skippedNames: string[] = []
  const failedNames: string[] = []

  for (const person of props.people) {
    const outcome = outcomes.value[person.idCard] ?? 'skipped'
    if (outcome === 'success') succeeded += 1
    else if (outcome === 'failed') {
      failed += 1
      failedNames.push(person.name || person.idCard)
    } else {
      skipped += 1
      skippedNames.push(person.name || person.idCard)
    }
  }

  for (const item of Object.values(stats.value)) {
    uploadedPhotos += item.uploaded
    failedPhotos += item.failed
  }

  return {
    totalPeople: props.people.length,
    succeeded,
    skipped,
    failed,
    uploadedPhotos,
    failedPhotos,
    skippedNames,
    failedNames,
  }
}

function finish(): void {
  clearAdvance()
  settleCurrent(false)
  summary.value = buildSummary()
  finished.value = true
  clearPending()
  latestOcr.value = null
  ocrWarnings.value = []
  ocrMismatch.value = false
  emit('finished', summary.value)
}

function advance(markSkipped: boolean): void {
  clearAdvance()
  settleCurrent(markSkipped)
  clearPending()
  previewVisible.value = false
  previewPhoto.value = null
  latestOcr.value = null
  ocrWarnings.value = []
  ocrMismatch.value = false
  ocrError.value = ''

  if (isLast.value) {
    finish()
    return
  }

  index.value += 1
  photos.value = []
  void loadPhotos()
}

function start(): void {
  clearAdvance()
  clearPending()
  photoTicket += 1
  finished.value = false
  summary.value = null
  outcomes.value = {}
  stats.value = {}
  photos.value = []
  photosError.value = ''
  latestOcr.value = null
  ocrMismatch.value = false
  ocrWarnings.value = []
  ocrError.value = ''
  previewVisible.value = false
  previewPhoto.value = null

  const max = Math.max(0, props.people.length - 1)
  index.value = Math.min(Math.max(0, props.startIndex), max)
  selectedCategory.value = props.presetCategory || 'ID_CARD'

  void loadCategories()
  void loadPhotos()
}

function handleClosed(): void {
  clearAdvance()
  clearPending()
  photoTicket += 1
  photos.value = []
  previewVisible.value = false
  previewPhoto.value = null
}

function handleBeforeClose(done: () => void): void {
  if (busy.value) {
    ElMessage.warning('照片正在处理中，请稍候再关闭')
    return
  }
  done()
}

watch(
  () => props.modelValue,
  (open) => {
    if (open) start()
    else {
      clearAdvance()
      clearPending()
    }
  },
  { immediate: true },
)

onBeforeUnmount(() => {
  clearAdvance()
  clearPending()
  photoTicket += 1
})
</script>

<template>
  <el-dialog
    v-model="visible"
    :title="title"
    :fullscreen="isNarrow"
    width="min(1120px, 96vw)"
    append-to-body
    :close-on-click-modal="false"
    :before-close="handleBeforeClose"
    class="photo-capture-dialog"
    @closed="handleClosed"
  >
    <template v-if="finished && summary">
      <div class="capture-done">
        <div class="capture-done__hero">
          <p class="capture-done__kicker">队列结束</p>
          <h3>本轮照片补全完成</h3>
          <p>共 {{ summary.totalPeople }} 人，上传 {{ summary.uploadedPhotos }} 张照片。</p>
        </div>

        <div class="capture-done__stats">
          <div class="done-stat done-stat--success">
            <span>成功</span>
            <strong>{{ summary.succeeded }}</strong>
            <small>人</small>
          </div>
          <div class="done-stat done-stat--skip">
            <span>跳过</span>
            <strong>{{ summary.skipped }}</strong>
            <small>人</small>
          </div>
          <div class="done-stat done-stat--fail">
            <span>失败</span>
            <strong>{{ summary.failed }}</strong>
            <small>人</small>
          </div>
        </div>

        <div v-if="summary.failedPhotos > 0" class="capture-done__note">
          有 {{ summary.failedPhotos }} 张照片上传失败，可重新进入对应人员补传。
        </div>

        <div v-if="summary.failedNames.length > 0" class="capture-done__list">
          <span class="capture-done__list-title">失败人员</span>
          <el-tag v-for="name in summary.failedNames" :key="name" type="danger" effect="plain">
            {{ name }}
          </el-tag>
        </div>

        <div v-if="summary.skippedNames.length > 0" class="capture-done__list">
          <span class="capture-done__list-title">跳过人员</span>
          <el-tag v-for="name in summary.skippedNames" :key="name" type="info" effect="plain">
            {{ name }}
          </el-tag>
        </div>
      </div>
    </template>

    <template v-else-if="current">
      <div class="person-bar">
        <div class="person-bar__main">
          <span class="person-bar__step">第 {{ index + 1 }} / {{ people.length }} 人</span>
          <strong class="person-bar__name">{{ current.name || '未提供姓名' }}</strong>
          <span class="person-bar__id mono">{{ current.idCard }}</span>
          <div class="person-bar__domains">
            <el-tag
              v-for="code in current.businessTypes"
              :key="code"
              size="small"
              effect="plain"
            >
              {{ domainLabel(code) }}
            </el-tag>
            <el-tag v-if="current.businessTypes.length === 0" size="small" type="info" effect="plain">
              未归类
            </el-tag>
          </div>
        </div>
        <div class="person-bar__progress">
          <span>{{ index + 1 }} / {{ people.length }}</span>
          <div class="person-bar__bar">
            <i :style="{ width: `${((index + 1) / Math.max(1, people.length)) * 100}%` }" />
          </div>
        </div>
      </div>

      <div class="capture-grid" :class="{ 'capture-grid--single': isNarrow }">
        <section class="capture-panel">
          <header class="panel-head">
            <h3>采集照片</h3>
            <span class="panel-head__hint">上传前自动压缩</span>
          </header>

          <div class="category-row">
            <span class="field-label">照片类别</span>
            <el-select v-model="selectedCategory" class="category-select" :disabled="busy">
              <el-option
                v-for="item in categories"
                :key="item.code"
                :label="item.label"
                :value="item.code"
              />
            </el-select>
          </div>

          <div class="capture-actions">
            <el-button type="primary" :icon="Camera" :disabled="busy" @click="openCamera">
              拍照
            </el-button>
            <el-button :icon="Upload" :disabled="busy" @click="openFiles">
              文件上传
            </el-button>
          </div>
          <p class="panel-hint">
            拍照优先调用后置摄像头；文件可多选。单张压缩到最大边 2000px、JPEG 质量 0.85，且不超过 10MB。
          </p>

          <div v-if="compressing" class="panel-processing">正在压缩图片…</div>

          <div class="pending-block">
            <header class="pending-head">
              <span>待上传 <strong>{{ pendingCount }}</strong> 张</span>
              <el-button v-if="pendingCount > 0" link :disabled="busy" @click="clearPending">
                清空
              </el-button>
            </header>

            <div v-if="pending.length > 0" class="pending-grid">
              <figure v-for="item in pending" :key="item.key" class="pending-card">
                <div class="pending-card__image">
                  <img :src="item.url" alt="待上传照片" />
                </div>
                <figcaption class="pending-card__body">
                  <span class="pending-card__name">
                    {{ categoryLabelFrom(categories, item.category) }}
                  </span>
                  <span class="pending-card__meta">{{ formatFileSize(item.file.size) }}</span>
                  <el-button
                    link
                    type="danger"
                    :icon="Delete"
                    :disabled="busy"
                    @click="removePending(item)"
                  >
                    移除
                  </el-button>
                </figcaption>
              </figure>
            </div>
            <el-empty v-else description="尚未选择照片" :image-size="48" />
          </div>

          <div v-if="ocrBusy" class="panel-processing">正在识别身份证…</div>
          <div v-if="ocrError" class="ocr-error">{{ ocrError }}</div>

          <div v-if="latestOcr" class="ocr-card">
            <header class="ocr-card__head">
              <strong>身份证识别结果</strong>
              <el-tag :type="ocrMismatch ? 'warning' : 'success'" size="small" effect="dark">
                {{ ocrMismatch ? '信息不一致' : '已识别' }}
              </el-tag>
            </header>

            <el-alert
              v-if="ocrMismatch"
              type="warning"
              title="识别信息与当前人员不一致"
              :closable="false"
              show-icon
            >
              <p v-for="(warning, warningIndex) in ocrWarnings" :key="warningIndex">
                {{ warning }}
              </p>
            </el-alert>

            <p v-if="!hasIdentityInOcr" class="ocr-hint">
              未识别出姓名或身份证号，请核对照片清晰度后手动确认。
            </p>

            <dl class="ocr-meta">
              <div>
                <dt>姓名</dt>
                <dd>{{ latestOcr.name || '—' }}</dd>
              </div>
              <div>
                <dt>身份证</dt>
                <dd class="mono">{{ latestOcr.idCard || '—' }}</dd>
              </div>
              <div>
                <dt>性别</dt>
                <dd>{{ latestOcr.gender || '—' }}</dd>
              </div>
              <div>
                <dt>出生日期</dt>
                <dd>{{ latestOcr.birthDate || '—' }}</dd>
              </div>
              <div class="ocr-meta__wide">
                <dt>住址</dt>
                <dd>{{ latestOcr.address || '—' }}</dd>
              </div>
            </dl>
            <p class="ocr-hint">识别结果仅用于核对，不阻断照片保存。</p>
          </div>
        </section>

        <section class="existing-panel">
          <header class="panel-head">
            <h3>已传照片</h3>
            <el-button link :icon="Refresh" :loading="photosLoading" @click="loadPhotos">
              刷新
            </el-button>
          </header>

          <el-alert
            v-if="photosError"
            type="error"
            :title="photosError"
            :closable="false"
            show-icon
          >
            <el-button link type="primary" @click="loadPhotos">重新加载</el-button>
          </el-alert>

          <el-skeleton v-if="photosLoading && photos.length === 0" :rows="5" animated />

          <template v-else-if="groupedPhotos.length > 0">
            <div v-for="group in groupedPhotos" :key="group.code" class="photo-group">
              <header class="photo-group__head">
                <span>{{ group.label }}</span>
                <em>{{ group.items.length }}</em>
              </header>
              <div class="existing-grid">
                <figure v-for="photo in group.items" :key="photo.id" class="existing-card">
                  <div class="existing-card__image">
                    <PhotoThumb
                      :photo-id="photo.id"
                      :alt="displayPhotoName(photo)"
                      @open="openPreview(photo)"
                    />
                  </div>
                  <figcaption class="existing-card__body">
                    <span class="existing-card__name" :title="displayPhotoName(photo)">
                      {{ displayPhotoName(photo) }}
                    </span>
                    <span class="existing-card__meta">
                      {{ formatFileSize(photo.fileSize) }} · {{ formatDateTimeText(photo.createdAt) }}
                    </span>
                    <el-button link type="danger" :icon="Delete" @click="removeExisting(photo)">
                      删除
                    </el-button>
                  </figcaption>
                </figure>
              </div>
            </div>
          </template>

          <el-empty
            v-else-if="!photosLoading && !photosError"
            description="该人员暂无照片"
            :image-size="64"
          >
            <p class="panel-hint">选择类别后拍照或上传文件，保存后会在此分组显示。</p>
          </el-empty>
        </section>
      </div>
    </template>

    <template #footer>
      <template v-if="finished">
        <el-button type="primary" @click="visible = false">完成</el-button>
      </template>
      <template v-else>
        <div class="footer-note">
          当前类别：{{ selectedCategoryLabel }} · 待上传 {{ pendingCount }} 张
        </div>
        <el-button :disabled="busy" @click="visible = false">退出</el-button>
        <el-button :disabled="busy" @click="advance(true)">跳过</el-button>
        <el-button
          v-if="pendingCount > 0"
          :loading="uploading"
          :disabled="compressing || ocrBusy"
          @click="uploadPending(false)"
        >
          仅保存
        </el-button>
        <el-button
          type="primary"
          :loading="uploading"
          :disabled="compressing || ocrBusy"
          @click="uploadPending(true)"
        >
          {{ primaryLabel }}
        </el-button>
      </template>
    </template>

    <input
      ref="cameraInput"
      class="capture-file"
      type="file"
      accept="image/*"
      capture="environment"
      @change="onFilesPicked"
    />
    <input
      ref="fileInput"
      class="capture-file"
      type="file"
      accept="image/*"
      multiple
      @change="onFilesPicked"
    />
  </el-dialog>

  <PhotoPreviewDialog v-model="previewVisible" :photo="previewPhoto" />
</template>

<style scoped>
.photo-capture-dialog :deep(.el-dialog__body) {
  max-height: 76vh;
  overflow-y: auto;
}

.capture-file {
  display: none;
}

.person-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 14px;
  padding: 12px 14px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-3);
}

.person-bar__main {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 8px 12px;
  min-width: 0;
}

.person-bar__step {
  padding: 2px 8px;
  background-color: var(--nc-seal);
  color: #fff;
  font-family: var(--nc-font-mono);
  font-size: 10px;
  letter-spacing: 0.08em;
}

.person-bar__name {
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-xl);
}

.person-bar__id {
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
}

.person-bar__domains {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}

.person-bar__progress {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 4px;
  min-width: 110px;
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
}

.person-bar__bar {
  width: 110px;
  height: 3px;
  overflow: hidden;
  background-color: var(--nc-paper-4);
}

.person-bar__bar i {
  display: block;
  height: 100%;
  background-color: var(--nc-seal);
  transition: width var(--nc-transition);
}

.capture-grid {
  display: grid;
  grid-template-columns: minmax(300px, 0.9fr) minmax(0, 1.1fr);
  gap: 14px;
  align-items: start;
}

.capture-grid--single {
  grid-template-columns: minmax(0, 1fr);
}

.capture-panel,
.existing-panel {
  min-width: 0;
  padding: 14px;
  border: 1px solid var(--nc-line);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-2);
}

.panel-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  margin-bottom: 12px;
  padding-bottom: 8px;
  border-bottom: 1px solid var(--nc-line-faint);
}

.panel-head h3 {
  margin: 0;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-lg);
}

.panel-head__hint {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.category-row {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 10px;
}

.field-label {
  flex: none;
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
  font-weight: 600;
}

.category-select {
  flex: 1;
  min-width: 0;
}

.capture-actions {
  display: flex;
  gap: 8px;
  margin-bottom: 8px;
}

.capture-actions :deep(.el-button) {
  flex: 1;
  margin-left: 0;
}

.panel-hint {
  margin: 0;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  line-height: 1.65;
}

.panel-processing {
  margin-top: 10px;
  padding: 8px 10px;
  border-left: 3px solid var(--nc-amber);
  background-color: var(--nc-amber-soft);
  color: var(--nc-amber-dark);
  font-size: var(--nc-text-xs);
}

.pending-block {
  margin-top: 12px;
}

.pending-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  margin-bottom: 8px;
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
}

.pending-head strong {
  color: var(--nc-seal-dark);
  font-family: var(--nc-font-mono);
}

.pending-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(120px, 1fr));
  gap: 8px;
}

.pending-card {
  margin: 0;
  overflow: hidden;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper);
}

.pending-card__image {
  height: 84px;
  overflow: hidden;
  background-color: var(--nc-paper-3);
}

.pending-card__image img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.pending-card__body {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: 6px 8px;
}

.pending-card__name {
  overflow: hidden;
  color: var(--nc-ink-2);
  font-size: 11px;
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.pending-card__meta {
  color: var(--nc-muted);
  font-size: 10px;
}

.ocr-card {
  margin-top: 12px;
  padding: 10px 12px;
  border: 1px solid rgba(46, 71, 56, 0.28);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-pine-soft);
}

.ocr-card__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  margin-bottom: 8px;
  color: var(--nc-ink);
  font-size: var(--nc-text-sm);
}

.ocr-card :deep(.el-alert) {
  margin-bottom: 8px;
}

.ocr-card :deep(.el-alert p) {
  margin: 2px 0;
  font-size: var(--nc-text-xs);
  line-height: 1.55;
}

.ocr-meta {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 6px 14px;
  margin: 0;
}

.ocr-meta div {
  min-width: 0;
}

.ocr-meta__wide {
  grid-column: 1 / -1;
}

.ocr-meta dt {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
}

.ocr-meta dd {
  margin: 1px 0 0;
  overflow: hidden;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.ocr-hint {
  margin: 6px 0 0;
  color: var(--nc-muted);
  font-size: 10px;
  line-height: 1.55;
}

.ocr-error {
  margin-top: 10px;
  padding: 8px 10px;
  border-left: 3px solid var(--nc-seal);
  background-color: var(--nc-seal-soft);
  color: var(--nc-seal-dark);
  font-size: var(--nc-text-xs);
  line-height: 1.6;
}

.photo-group + .photo-group {
  margin-top: 12px;
  padding-top: 12px;
  border-top: 1px dashed var(--nc-line);
}

.photo-group__head {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 8px;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  font-weight: 600;
}

.photo-group__head em {
  padding: 0 6px;
  background-color: var(--nc-paper-3);
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  font-style: normal;
}

.existing-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(140px, 1fr));
  gap: 8px;
}

.existing-card {
  margin: 0;
  overflow: hidden;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-xs);
  background-color: var(--nc-paper);
}

.existing-card__image {
  height: 108px;
  overflow: hidden;
  background-color: var(--nc-paper-3);
}

.existing-card__body {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: 6px 8px;
}

.existing-card__name {
  overflow: hidden;
  color: var(--nc-ink-2);
  font-size: 11px;
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.existing-card__meta {
  color: var(--nc-muted);
  font-size: 10px;
}

.capture-done {
  display: flex;
  flex-direction: column;
  gap: 16px;
  align-items: center;
  padding: 8px 4px 4px;
  text-align: center;
}

.capture-done__kicker {
  margin: 0 0 6px;
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  letter-spacing: 0.2em;
}

.capture-done__hero h3 {
  margin: 0;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-2xl);
}

.capture-done__hero p {
  margin: 8px 0 0;
  color: var(--nc-muted-2);
  font-size: var(--nc-text-sm);
}

.capture-done__stats {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 10px;
  width: min(520px, 100%);
}

.done-stat {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 2px;
  padding: 12px;
  border: 1px solid var(--nc-line);
  background-color: var(--nc-paper-2);
}

.done-stat span {
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
}

.done-stat strong {
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-3xl);
  line-height: 1.1;
}

.done-stat small {
  color: var(--nc-muted);
  font-size: 10px;
}

.done-stat--success strong {
  color: var(--nc-pine-dark);
}

.done-stat--skip strong {
  color: var(--nc-amber-dark);
}

.done-stat--fail strong {
  color: var(--nc-seal-dark);
}

.capture-done__note {
  padding: 8px 12px;
  border-left: 3px solid var(--nc-amber);
  background-color: var(--nc-amber-soft);
  color: var(--nc-amber-dark);
  font-size: var(--nc-text-xs);
}

.capture-done__list {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: center;
  gap: 6px;
  width: min(640px, 100%);
}

.capture-done__list-title {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  letter-spacing: 0.1em;
}

.footer-note {
  margin-right: auto;
  color: var(--nc-muted);
  font-size: var(--nc-text-xs);
  text-align: left;
}

.mono {
  font-family: var(--nc-font-mono);
}

@media (max-width: 900px) {
  .footer-note {
    flex: 1 1 100%;
    margin: 0 0 4px;
  }
}

@media (max-width: 767px) {
  .photo-capture-dialog :deep(.el-dialog__body) {
    max-height: none;
    overflow-y: visible;
  }

  .person-bar {
    align-items: flex-start;
  }

  .person-bar__progress {
    align-items: flex-start;
  }

  .person-bar__name {
    font-size: var(--nc-text-lg);
  }

  .capture-done__stats {
    grid-template-columns: repeat(3, minmax(0, 1fr));
  }

  .done-stat strong {
    font-size: var(--nc-text-2xl);
  }

  .capture-panel,
  .existing-panel {
    padding: 12px;
  }
}
</style>
