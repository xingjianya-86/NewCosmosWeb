<script setup lang="ts">
import { usePhotoContent } from './usePhotoContent'

const props = withDefaults(
  defineProps<{
    photoId: number
    alt?: string
    fit?: 'cover' | 'contain'
  }>(),
  {
    alt: '照片',
    fit: 'cover',
  },
)

const emit = defineEmits<{
  (event: 'open'): void
}>()

const { src, loading, error, reload } = usePhotoContent(() => props.photoId)

function open(): void {
  emit('open')
}
</script>

<template>
  <div
    class="photo-thumb"
    role="button"
    tabindex="0"
    :title="alt"
    @click="open"
    @keydown.enter.prevent="open"
  >
    <img
      v-if="src"
      :src="src"
      :alt="alt"
      class="photo-thumb__image"
      :style="{ objectFit: fit }"
      loading="lazy"
    />
    <span v-else-if="loading" class="photo-thumb__hint">加载中…</span>
    <button
      v-else
      type="button"
      class="photo-thumb__retry"
      :title="error || '照片不可用'"
      @click.stop="reload"
    >
      {{ error ? '加载失败 · 重试' : '无图' }}
    </button>
  </div>
</template>

<style scoped>
.photo-thumb {
  position: relative;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 100%;
  height: 100%;
  min-height: 64px;
  overflow: hidden;
  background-color: var(--nc-paper-3);
  cursor: zoom-in;
}

.photo-thumb:focus-visible {
  outline: 2px solid var(--nc-seal);
  outline-offset: -2px;
}

.photo-thumb__image {
  display: block;
  width: 100%;
  height: 100%;
}

.photo-thumb__hint {
  color: var(--nc-muted);
  font-family: var(--nc-font-mono);
  font-size: 10px;
  letter-spacing: 0.06em;
}

.photo-thumb__retry {
  padding: 4px 8px;
  border: none;
  background: transparent;
  color: var(--nc-muted);
  font-size: 10px;
  cursor: pointer;
}

.photo-thumb__retry:hover {
  color: var(--nc-seal);
}
</style>
