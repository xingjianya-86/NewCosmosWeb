import { MAX_PHOTO_BYTES } from './photoMeta'

const MAX_EDGE = 2000
const INITIAL_QUALITY = 0.85
const MIN_QUALITY = 0.55

interface DecodedImage {
  source: CanvasImageSource
  width: number
  height: number
  dispose: () => void
}

function decodeImage(file: File): Promise<DecodedImage> {
  if (typeof createImageBitmap === 'function') {
    return createImageBitmap(file, { imageOrientation: 'from-image' })
      .then((bitmap) => ({
        source: bitmap,
        width: bitmap.width,
        height: bitmap.height,
        dispose: () => bitmap.close(),
      }))
      .catch(() => decodeWithImageElement(file))
  }
  return decodeWithImageElement(file)
}

function decodeWithImageElement(file: File): Promise<DecodedImage> {
  const url = URL.createObjectURL(file)
  return new Promise<DecodedImage>((resolve, reject) => {
    const image = new Image()
    image.onload = () => {
      resolve({
        source: image,
        width: image.naturalWidth || image.width,
        height: image.naturalHeight || image.height,
        dispose: () => URL.revokeObjectURL(url),
      })
    }
    image.onerror = () => {
      URL.revokeObjectURL(url)
      reject(new Error('图片无法解码，请换一张照片'))
    }
    image.src = url
  })
}

function drawToJpeg(
  image: DecodedImage,
  maxEdge: number,
  quality: number,
): Promise<Blob> {
  const longest = Math.max(image.width, image.height)
  if (longest <= 0) return Promise.reject(new Error('图片尺寸无效'))

  const scale = Math.min(1, maxEdge / longest)
  const width = Math.max(1, Math.round(image.width * scale))
  const height = Math.max(1, Math.round(image.height * scale))

  const canvas = document.createElement('canvas')
  canvas.width = width
  canvas.height = height
  const context = canvas.getContext('2d')
  if (!context) return Promise.reject(new Error('当前浏览器不支持图片压缩'))

  // PNG 透明底转 JPEG 会变黑，统一铺白底。
  context.fillStyle = '#ffffff'
  context.fillRect(0, 0, width, height)
  context.imageSmoothingEnabled = true
  context.imageSmoothingQuality = 'high'
  context.drawImage(image.source, 0, 0, width, height)

  return new Promise<Blob>((resolve, reject) => {
    canvas.toBlob(
      (blob) => {
        if (blob) resolve(blob)
        else reject(new Error('图片压缩失败'))
      },
      'image/jpeg',
      quality,
    )
  })
}

function jpegName(originalName: string): string {
  const base = originalName.replace(/\.[^.\\/]+$/, '').trim() || 'photo'
  return `${base}.jpg`
}

/**
 * 上传前压缩：最大边 2000px、JPEG 质量 0.85；超出 10MB 时逐级降质/缩边。
 * 返回的 File 已可直接放入 multipart。
 */
export async function compressImageFile(file: File): Promise<File> {
  if (!file.size) throw new Error('文件为空')
  if (file.type && !file.type.startsWith('image/')) {
    throw new Error('仅支持图片文件')
  }

  const decoded = await decodeImage(file)
  try {
    let maxEdge = MAX_EDGE
    let quality = INITIAL_QUALITY

    for (let attempt = 0; attempt < 5; attempt += 1) {
      const blob = await drawToJpeg(decoded, maxEdge, quality)
      if (blob.size <= MAX_PHOTO_BYTES) {
        return new File([blob], jpegName(file.name), {
          type: 'image/jpeg',
          lastModified: Date.now(),
        })
      }
      if (quality > MIN_QUALITY) {
        quality = Math.max(MIN_QUALITY, Number((quality - 0.1).toFixed(2)))
      } else {
        maxEdge = Math.max(1200, Math.round(maxEdge * 0.8))
      }
    }

    throw new Error('照片压缩后仍超过 10MB，请降低分辨率后重试')
  } finally {
    decoded.dispose()
  }
}
