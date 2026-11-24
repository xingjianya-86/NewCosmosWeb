import { onBeforeUnmount, onMounted, ref } from 'vue'

/** 响应式媒体查询；用于在窄屏渲染卡片、宽屏渲染表格，避免同时输出两套 DOM。 */
export function useMediaQuery(query: string) {
  const supported = typeof window !== 'undefined' && typeof window.matchMedia === 'function'
  const media = supported ? window.matchMedia(query) : null
  const matches = ref(media?.matches ?? false)

  function handleChange(event: MediaQueryListEvent): void {
    matches.value = event.matches
  }

  onMounted(() => {
    if (!media) return
    matches.value = media.matches
    media.addEventListener('change', handleChange)
  })

  onBeforeUnmount(() => {
    media?.removeEventListener('change', handleChange)
  })

  return matches
}
