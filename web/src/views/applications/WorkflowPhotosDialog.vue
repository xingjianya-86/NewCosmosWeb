<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { photoCategoryText, listCheckPhotos, photoDataUrl } from '@/api/assetMonthlyAudit'
import { getErrorMessage } from '@/api/http'
import { listWorkflowPhotos } from '@/api/applicationWorkflow'
import type { WorkflowPhoto } from '@/types/applicationWorkflow'

/**
 * 工作流「照片」对话框：
 * - 申请卡片走 /social-assistance/applications/{id}/photos（服务端按 applicant_id_card 聚合）；
 * - 资产核查卡片复用已有 /asset-verification/checks/{id}/photos。
 * 两张来源的展示模型统一为 WorkflowPhoto（dataUrl 可直接用于 img src）。
 */
const props = defineProps<{
  modelValue: boolean
  subject: string
  applicationId: number | null
  assetCheckId: number | null
}>()

const emit = defineEmits<{ 'update:modelValue': [value: boolean] }>()

const visible = computed({
  get: () => props.modelValue,
  set: (value: boolean) => emit('update:modelValue', value),
})

const photos = ref<WorkflowPhoto[]>([])
const loading = ref(false)
const loadError = ref('')
const previewSources = computed(() => photos.value.map((photo) => photo.dataUrl))

let requestTicket = 0

async function load(): Promise<void> {
  const applicationId = props.applicationId
  const assetCheckId = props.assetCheckId
  if (applicationId === null && assetCheckId === null) return

  const ticket = ++requestTicket
  loading.value = true
  loadError.value = ''
  photos.value = []

  try {
    let list: WorkflowPhoto[]
    if (applicationId !== null) {
      list = await listWorkflowPhotos(applicationId)
    } else if (assetCheckId !== null) {
      const assetPhotos = await listCheckPhotos(assetCheckId)
      list = assetPhotos.map((photo) => ({
        id: photo.id,
        category: photo.category,
        photoName: photo.name || `照片 #${photo.id}`,
        format: photo.format,
        dataUrl: photoDataUrl(photo),
      }))
    } else {
      list = []
    }

    if (ticket !== requestTicket) return
    photos.value = list
  } catch (error) {
    if (ticket !== requestTicket) return
    photos.value = []
    loadError.value = getErrorMessage(error, '照片加载失败')
  } finally {
    if (ticket === requestTicket) loading.value = false
  }
}

watch(
  () => props.modelValue,
  (open) => {
    if (open) void load()
  },
)

/** 关闭后释放 Base64 内存并让进行中的响应失效。 */
function handleClosed(): void {
  requestTicket += 1
  photos.value = []
  loadError.value = ''
  loading.value = false
}

function formatLabel(format: string): string {
  const value = (format ?? '').trim()
  return value ? value.toUpperCase() : 'JPEG'
}
</script>

<template>
  <el-dialog
    v-model="visible"
    title="档案照片"
    width="min(760px, 94vw)"
    append-to-body
    class="workflow-photos-dialog"
    @closed="handleClosed"
  >
    <p class="workflow-photos__subject">{{ subject || '未指定申请人' }}</p>

    <el-alert
      v-if="loadError"
      class="workflow-photos__alert"
      type="error"
      :title="loadError"
      :closable="false"
      show-icon
    >
      <el-button link type="primary" @click="load">重新加载</el-button>
    </el-alert>

    <div v-loading="loading" class="workflow-photos__grid">
      <figure v-for="(photo, index) in photos" :key="photo.id" class="workflow-photos__card">
        <el-image
          class="workflow-photos__image"
          :src="photo.dataUrl"
          :alt="photo.photoName"
          :preview-src-list="previewSources"
          :initial-index="index"
          fit="cover"
          preview-teleported
        />
        <figcaption class="workflow-photos__caption">
          <span class="workflow-photos__name" :title="photo.photoName">
            {{ photo.photoName }}
          </span>
          <span class="workflow-photos__meta">
            {{ photoCategoryText(photo.category) }} · {{ formatLabel(photo.format) }}
          </span>
        </figcaption>
      </figure>

      <div
        v-if="!loading && photos.length === 0 && !loadError"
        class="workflow-photos__empty"
      >
        <el-empty description="该申请人暂无档案照片" :image-size="72" />
      </div>
    </div>

    <template #footer>
      <span class="workflow-photos__footnote">
        照片按申请人身份证跨业务聚合，最多显示最近 50 张；点击缩略图可查看大图。
      </span>
      <el-button @click="visible = false">关闭</el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.workflow-photos__subject {
  margin: 0 0 10px;
  color: var(--nc-ink);
  font-family: var(--nc-font-serif);
  font-size: 16px;
  font-weight: 700;
}

.workflow-photos__alert {
  margin-bottom: 10px;
}

.workflow-photos__grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(150px, 1fr));
  gap: 12px;
  min-height: 120px;
}

.workflow-photos__card {
  display: flex;
  flex-direction: column;
  margin: 0;
  border: 1px solid var(--nc-line);
  background-color: #fff;
}

.workflow-photos__image {
  display: block;
  width: 100%;
  height: 130px;
  background-color: var(--nc-paper-3);
  cursor: zoom-in;
}

.workflow-photos__caption {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: 7px 8px;
  border-top: 1px solid var(--nc-line);
}

.workflow-photos__name {
  overflow: hidden;
  color: var(--nc-ink-2);
  font-size: var(--nc-text-sm);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.workflow-photos__meta {
  color: var(--nc-muted);
  font-size: 12px;
}

.workflow-photos__empty {
  grid-column: 1 / -1;
  padding: 18px 0;
}

.workflow-photos__footnote {
  margin-right: 12px;
  color: var(--nc-muted);
  font-size: 12px;
}

@media (max-width: 767px) {
  .workflow-photos__grid {
    grid-template-columns: repeat(auto-fill, minmax(120px, 1fr));
  }

  .workflow-photos__image {
    height: 110px;
  }

  .workflow-photos__footnote {
    display: block;
    margin: 0 0 8px;
    text-align: left;
  }
}
</style>
