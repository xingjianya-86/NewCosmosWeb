<script setup lang="ts">
import { computed } from 'vue'
import type { PhotoRecord } from '@/types/photos'
import { useMediaQuery } from '@/composables/useMediaQuery'
import { type PhotoContentSource, usePhotoContent } from './usePhotoContent'
import { FALLBACK_PHOTO_CATEGORIES, categoryLabelFrom, displayPhotoName, formatDateTimeText, formatFileSize } from './photoMeta'

const props = withDefaults(
  defineProps<{
    modelValue: boolean
    photo: PhotoRecord | null
    /** 取图来源：正式照片 / 待归属照片池。 */
    source?: PhotoContentSource
  }>(),
  {
    source: 'photo',
  },
)

const emit = defineEmits<{
  (event: 'update:modelValue', value: boolean): void
}>()

const isNarrow = useMediaQuery('(max-width: 767px)')

const visible = computed({
  get: () => props.modelValue,
  set: (value: boolean) => emit('update:modelValue', value),
})

const { src, loading, error, reload } = usePhotoContent(
  () => (props.modelValue && props.photo ? props.photo.id : null),
  props.source,
)

const title = computed(() => (props.photo ? displayPhotoName(props.photo) : '照片预览'))
const categoryText = computed(() =>
  props.photo
    ? categoryLabelFrom(FALLBACK_PHOTO_CATEGORIES, props.photo.category, props.photo.categoryLabel)
    : '—',
)
</script>

<template>
  <el-dialog
    v-model="visible"
    :title="title"
    :fullscreen="isNarrow"
    width="min(960px, 92vw)"
    append-to-body
    destroy-on-close
    class="photo-preview-dialog"
  >
    <div v-if="photo" class="preview-body">
      <div class="preview-frame">
        <el-skeleton v-if="loading && !src" :rows="6" animated class="preview-skeleton" />
        <img v-else-if="src" :src="src" :alt="title" class="preview-image" />
        <div v-else class="preview-fail">
          <p>{{ error || '照片不可用（可能已被删除）' }}</p>
          <el-button size="small" @click="reload">重新加载</el-button>
        </div>
      </div>

      <dl class="preview-meta">
        <div>
          <dt>类别</dt>
          <dd>{{ categoryText }}</dd>
        </div>
        <div>
          <dt>身份证</dt>
          <dd class="mono">{{ photo.applicantIdCard || '—' }}</dd>
        </div>
        <div>
          <dt>文件大小</dt>
          <dd>{{ formatFileSize(photo.fileSize) }}</dd>
        </div>
        <div>
          <dt>图片格式</dt>
          <dd>{{ photo.imageFormat || '—' }}</dd>
        </div>
        <div>
          <dt>采集时间</dt>
          <dd>{{ formatDateTimeText(photo.createdAt) }}</dd>
        </div>
        <div>
          <dt>照片编号</dt>
          <dd class="mono">#{{ photo.id }}</dd>
        </div>
      </dl>
    </div>

    <template #footer>
      <el-button @click="visible = false">关闭</el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.photo-preview-dialog :deep(.el-dialog__body) {
  max-height: 74vh;
  overflow-y: auto;
}

.preview-body {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.preview-frame {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 240px;
  max-height: 58vh;
  overflow: hidden;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-3);
}

.preview-skeleton {
  width: 100%;
  padding: 18px;
}

.preview-image {
  display: block;
  max-width: 100%;
  max-height: 58vh;
  object-fit: contain;
}

.preview-fail {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 10px;
  padding: 24px 16px;
  color: var(--nc-muted);
  font-size: var(--nc-text-sm);
  text-align: center;
}

.preview-fail p {
  margin: 0;
}

.preview-meta {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 10px 18px;
  margin: 0;
  padding: 12px 14px;
  border: 1px solid var(--nc-line-faint);
  border-radius: var(--nc-radius-sm);
  background-color: var(--nc-paper-3);
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

.mono {
  font-family: var(--nc-font-mono);
}

@media (max-width: 767px) {
  .preview-meta {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .preview-frame {
    max-height: 56vh;
  }
}
</style>
