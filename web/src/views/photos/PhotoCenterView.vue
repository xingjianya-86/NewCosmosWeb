<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { ElMessage } from 'element-plus'
import type {
  PhotoBackfillStatus,
  PhotoCapturePerson,
  PhotoCaptureSummary,
} from '@/types/photos'
import NcPageHeader from '@/components/NcPageHeader.vue'
import PhotoBackfillPanel from './PhotoBackfillPanel.vue'
import PhotoCaptureDialog from './PhotoCaptureDialog.vue'
import PhotoPendingPanel from './PhotoPendingPanel.vue'
import PhotoQueryPanel from './PhotoQueryPanel.vue'
import { isIdCard, normalizeDomain } from './photoMeta'

interface BackfillPanelExpose {
  reload: () => void
}

interface QueryPanelExpose {
  reload: () => void
}

interface PendingPanelExpose {
  reload: () => void
}

const route = useRoute()

function queryText(value: unknown): string {
  if (typeof value === 'string') return value.trim()
  if (Array.isArray(value) && typeof value[0] === 'string') return value[0].trim()
  return ''
}

function queryStatus(value: unknown): PhotoBackfillStatus {
  const text = queryText(value)
  return text === 'unuploaded' || text === 'partial' || text === 'uploaded' ? text : ''
}

const initialIdCard = queryText(route.query.idCard)
const initialName = queryText(route.query.name)
const initialDomain = normalizeDomain(queryText(route.query.domain))
const initialBusinessId = Number(queryText(route.query.businessId))
const initialKeyword = initialIdCard || queryText(route.query.keyword) || initialName
const initialStatus = queryStatus(route.query.status)
const initialQueryKeyword = queryText(route.query.keyword) || initialIdCard
const initialCategory = queryText(route.query.category)
const presetCategory = queryText(route.query.photoCategory) || initialCategory

const activeTab = ref<'backfill' | 'query' | 'pending'>(
  queryText(route.query.tab) === 'query'
    ? 'query'
    : queryText(route.query.tab) === 'pending'
      ? 'pending'
      : 'backfill',
)

const backfillRef = ref<BackfillPanelExpose | null>(null)
const queryRef = ref<QueryPanelExpose | null>(null)
const pendingRef = ref<PendingPanelExpose | null>(null)
const captureVisible = ref(false)
const capturePeople = ref<PhotoCapturePerson[]>([])
const captureStart = ref(0)

function openCapture(people: PhotoCapturePerson[], startIndex: number): void {
  if (people.length === 0) {
    ElMessage.warning('请先选择需要补传照片的人员')
    return
  }
  capturePeople.value = people
  captureStart.value = Math.max(0, startIndex)
  captureVisible.value = true
}

function handleCaptureChanged(): void {
  backfillRef.value?.reload()
}

function handleCaptureFinished(_summary: PhotoCaptureSummary): void {
  backfillRef.value?.reload()
}

function handleTabChange(name: string | number): void {
  if (name === 'query') queryRef.value?.reload()
  else if (name === 'pending') pendingRef.value?.reload()
  else if (name === 'backfill') backfillRef.value?.reload()
}

watch(captureVisible, (open) => {
  if (!open) backfillRef.value?.reload()
})

onMounted(() => {
  if (!initialIdCard) return
  if (!isIdCard(initialIdCard)) {
    ElMessage.warning('入口身份证号格式不正确，已按关键字查询')
    return
  }

  capturePeople.value = [
    {
      idCard: initialIdCard,
      name: initialName,
      businessTypes: initialDomain ? [initialDomain] : [],
      businessId: Number.isFinite(initialBusinessId) && initialBusinessId > 0 ? initialBusinessId : null,
    },
  ]
  captureStart.value = 0
  captureVisible.value = true
})
</script>

<template>
  <div class="nc-page photo-center">
    <NcPageHeader
      title="照片中心"
      description="四个业务域共用同一套照片库：按人员补全证件与核查照片，按条件查询、导出与推送打印。照片仅存内网，不上传外网。"
      kicker="全局 · 照片"
    />

    <el-tabs v-model="activeTab" class="photo-tabs" @tab-change="handleTabChange">
      <el-tab-pane label="人员补全" name="backfill">
        <PhotoBackfillPanel
          ref="backfillRef"
          :initial-keyword="initialKeyword"
          :initial-domain="initialDomain"
          :initial-status="initialStatus"
          @open-capture="openCapture"
        />
      </el-tab-pane>

      <el-tab-pane label="照片查询" name="query" lazy>
        <PhotoQueryPanel
          ref="queryRef"
          :initial-keyword="initialQueryKeyword"
          :initial-category="initialCategory"
        />
      </el-tab-pane>

      <el-tab-pane label="待归属" name="pending" lazy>
        <PhotoPendingPanel ref="pendingRef" />
      </el-tab-pane>
    </el-tabs>

    <PhotoCaptureDialog
      v-model="captureVisible"
      :people="capturePeople"
      :start-index="captureStart"
      :preset-category="presetCategory"
      @changed="handleCaptureChanged"
      @finished="handleCaptureFinished"
    />
  </div>
</template>

<style scoped>
.photo-center {
  display: flex;
  flex-direction: column;
  gap: 15px;
}

.photo-tabs :deep(.el-tabs__header) {
  margin-bottom: 14px;
}

.photo-tabs :deep(.el-tabs__nav-wrap::after) {
  height: 1px;
  background-color: var(--nc-line);
}

.photo-tabs :deep(.el-tabs__item) {
  height: 38px;
  color: var(--nc-muted-2);
  font-family: var(--nc-font-serif);
  font-size: var(--nc-text-md);
  letter-spacing: 0.04em;
}

.photo-tabs :deep(.el-tabs__item.is-active) {
  color: var(--nc-seal-dark);
  font-weight: 700;
}

.photo-tabs :deep(.el-tabs__active-bar) {
  height: 2px;
  background-color: var(--nc-seal);
}

.photo-tabs :deep(.el-tabs__content) {
  overflow: visible;
}

@media (max-width: 767px) {
  .photo-tabs :deep(.el-tabs__item) {
    height: 36px;
    padding: 0 12px;
    font-size: var(--nc-text-sm);
  }
}
</style>
