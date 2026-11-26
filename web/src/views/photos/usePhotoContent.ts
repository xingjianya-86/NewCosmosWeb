import { onBeforeUnmount, ref, watch } from 'vue'
import { getPhotoContent } from '@/api/photos'
import { getErrorMessage } from '@/api/http'

/**
 * 通过 /api/photos/{id}/content 取原图并转成 object URL。
 * 列表只下载当前可见缩略图，不拉 Base64 大数组。
 */
export function usePhotoContent(photoId: () => number | null) {
  const src = ref('')
  const loading = ref(false)
  const error = ref('')

  let objectUrl = ''
  let ticket = 0
  let controller: AbortController | null = null

  function release(): void {
    controller?.abort()
    controller = null
    if (objectUrl) {
      URL.revokeObjectURL(objectUrl)
      objectUrl = ''
    }
    src.value = ''
  }

  async function load(): Promise<void> {
    const id = photoId()
    release()
    error.value = ''

    if (id === null || !Number.isFinite(id) || id <= 0) {
      loading.value = false
      return
    }

    const current = ++ticket
    const abort = new AbortController()
    controller = abort
    loading.value = true

    try {
      const blob = await getPhotoContent(id, abort.signal)
      if (current !== ticket) return
      if (!blob || blob.size === 0) throw new Error('照片内容为空')

      objectUrl = URL.createObjectURL(blob)
      src.value = objectUrl
    } catch (err) {
      if (current !== ticket) return
      error.value = getErrorMessage(err, '照片加载失败')
    } finally {
      if (current === ticket) {
        loading.value = false
        if (controller === abort) controller = null
      }
    }
  }

  watch(photoId, () => void load(), { immediate: true })

  onBeforeUnmount(() => {
    ticket += 1
    release()
  })

  return { src, loading, error, reload: load }
}
