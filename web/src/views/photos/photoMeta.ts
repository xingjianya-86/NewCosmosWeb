import { ApiError, getErrorMessage } from '@/api/http'
import type {
  PhotoBackfillItem,
  PhotoBackfillStatus,
  PhotoCapturePerson,
  PhotoCategory,
  PhotoDomain,
  PhotoPrintLayout,
  PhotoRecord,
} from '@/types/photos'

export const PHOTO_PAGE_SIZE = 20
export const MAX_PHOTO_BYTES = 10 * 1024 * 1024

/** 后端类别接口不可用时的兜底字典（与 PC PhotoCategoryConstants 同源）。 */
export const FALLBACK_PHOTO_CATEGORIES: PhotoCategory[] = [
  { code: 'ID_CARD', label: '身份证' },
  { code: 'HOUSEHOLD_REGISTER', label: '户口簿' },
  { code: 'PERSON_PHOTO', label: '本人照片' },
  { code: 'MEDICAL_RECORD', label: '病历' },
  { code: 'DISABILITY_CERT', label: '残疾证' },
  { code: 'BANK_CARD', label: '银行卡' },
  { code: 'INCOME_PROOF', label: '收入证明' },
  { code: 'PUBLICITY_CHECK', label: '公示核查' },
  { code: 'HOUSEHOLD_INSPECTION', label: '入户排查' },
  { code: 'OTHER_CERT', label: '其他' },
]

export const PHOTO_DOMAIN_OPTIONS: Array<{ value: PhotoDomain; label: string }> = [
  { value: '', label: '全部' },
  { value: 'ElderlyBenefits', label: '高龄津贴' },
  { value: 'SocialAssistance', label: '低收入' },
  { value: 'TempRelief', label: '临时救助' },
  { value: 'AssetVerification', label: '资产核对' },
]

export const PHOTO_STATUS_OPTIONS: Array<{ value: PhotoBackfillStatus; label: string }> = [
  { value: '', label: '全部状态' },
  { value: 'unuploaded', label: '未传' },
  { value: 'partial', label: '部分' },
  { value: 'uploaded', label: '已传' },
]

export const PHOTO_LAYOUT_OPTIONS: Array<{ value: PhotoPrintLayout; label: string }> = [
  { value: '', label: '按默认（证件原寸/整页）' },
  { value: 'FULL_PAGE', label: '整页' },
  { value: 'HALF_PAGE', label: '半页' },
  { value: 'ID_CARD', label: '证件原寸' },
]

/**
 * 各业务域的常用证件类别（与服务端 PhotoEndpoints.RequiredCategoriesByDomain 同口径）。
 * 用于在接口只返回已传张数时推算“未传”，避免列表只显示半截信息。
 */
const REQUIRED_CATEGORIES_BY_DOMAIN: Record<string, string[]> = {
  ElderlyBenefits: ['ID_CARD', 'HOUSEHOLD_REGISTER', 'PERSON_PHOTO', 'BANK_CARD'],
  SocialAssistance: [
    'ID_CARD',
    'HOUSEHOLD_REGISTER',
    'INCOME_PROOF',
    'MEDICAL_RECORD',
    'BANK_CARD',
  ],
  TempRelief: ['ID_CARD', 'HOUSEHOLD_REGISTER', 'MEDICAL_RECORD', 'BANK_CARD'],
  AssetVerification: ['ID_CARD', 'HOUSEHOLD_REGISTER', 'INCOME_PROOF', 'BANK_CARD'],
}

const DOMAIN_LABELS: Record<string, string> = {
  ElderlyBenefits: '高龄津贴',
  SocialAssistance: '低收入',
  TempRelief: '临时救助',
  AssetVerification: '资产核对',
}

const DOMAIN_ALIASES: Record<string, PhotoDomain> = {
  elderly: 'ElderlyBenefits',
  elderlybenefits: 'ElderlyBenefits',
  高龄: 'ElderlyBenefits',
  高龄津贴: 'ElderlyBenefits',
  socialassistance: 'SocialAssistance',
  低收入: 'SocialAssistance',
  低收入人口: 'SocialAssistance',
  temprelief: 'TempRelief',
  临时救助: 'TempRelief',
  assetverification: 'AssetVerification',
  资产核查: 'AssetVerification',
  资产核对: 'AssetVerification',
}

export function normalizeDomain(value: string | null | undefined): PhotoDomain {
  const text = (value ?? '').trim()
  if (!text) return ''
  const direct = PHOTO_DOMAIN_OPTIONS.find((option) => option.value === text)
  if (direct) return direct.value
  return DOMAIN_ALIASES[text.toLowerCase()] ?? DOMAIN_ALIASES[text] ?? ''
}

export function domainLabel(code: string | null | undefined): string {
  const text = (code ?? '').trim()
  if (!text) return '—'
  return DOMAIN_LABELS[text] ?? text
}

export function categoryLabelFrom(
  categories: PhotoCategory[],
  code: string | null | undefined,
  display?: string | null,
): string {
  if (display && display.trim()) return display.trim()
  const text = (code ?? '').trim()
  if (!text) return '未分类'
  return (
    categories.find((item) => item.code === text)?.label ??
    FALLBACK_PHOTO_CATEGORIES.find((item) => item.code === text)?.label ??
    text
  )
}

export function formatFileSize(size: number | null | undefined): string {
  if (size === null || size === undefined || !Number.isFinite(size) || size < 0) return '—'
  if (size < 1024) return `${size} B`
  if (size < 1024 * 1024) return `${(size / 1024).toFixed(1)} KB`
  return `${(size / 1024 / 1024).toFixed(2)} MB`
}

const dateTimeFormatter = new Intl.DateTimeFormat('zh-CN', {
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  hour12: false,
})

export function formatDateTimeText(value: string | null | undefined): string {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return dateTimeFormatter.format(date)
}

export function displayPhotoName(photo: PhotoRecord): string {
  return photo.photoName.trim() || `照片 #${photo.id}`
}

export function isIdCard(value: string | null | undefined): boolean {
  return /^\d{17}[\dXx]$/.test((value ?? '').trim())
}

export function saveBlob(blob: Blob, filename: string): void {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = filename
  document.body.appendChild(link)
  link.click()
  link.remove()
  window.setTimeout(() => URL.revokeObjectURL(url), 1000)
}

export function timestampText(): string {
  const now = new Date()
  const pad = (part: number): string => String(part).padStart(2, '0')
  return `${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}_${pad(
    now.getHours(),
  )}${pad(now.getMinutes())}`
}

export function photoActionError(
  error: unknown,
  fallback: string,
  action: string,
  permission = 'ARCHIVE_MANAGE',
): string {
  if (error instanceof ApiError && error.status === 403) {
    return `无权限${action}（需要${permission}权限）`
  }
  return getErrorMessage(error, fallback)
}

export function requiredCountFor(item: PhotoBackfillItem): number | null {
  if (item.requiredCount !== null) return item.requiredCount
  const required = new Set<string>()
  for (const code of item.businessTypes) {
    const categories = REQUIRED_CATEGORIES_BY_DOMAIN[code]
    if (categories) for (const category of categories) required.add(category)
  }
  return required.size > 0 ? required.size : null
}

export function missingCountFor(item: PhotoBackfillItem): number | null {
  if (item.missingCount !== null) return item.missingCount
  const required = requiredCountFor(item)
  return required === null ? null : Math.max(0, required - item.uploadedCount)
}

export function backfillStatusMeta(item: PhotoBackfillItem): {
  label: string
  type: 'success' | 'warning' | 'info'
} {
  const status = item.status.toLowerCase()
  if (['uploaded', 'complete', 'completed', 'done', 'full'].includes(status)) {
    return { label: '已传', type: 'success' }
  }
  if (['partial', 'part'].includes(status)) {
    return { label: '部分', type: 'warning' }
  }
  if (['unuploaded', 'missing', 'none', 'empty', 'pending'].includes(status)) {
    return { label: '未传', type: 'info' }
  }

  const required = requiredCountFor(item)
  if (required !== null) {
    if (item.uploadedCount >= required) return { label: '已传', type: 'success' }
    if (item.uploadedCount > 0) return { label: '部分', type: 'warning' }
    return { label: '未传', type: 'info' }
  }

  if (item.uploadedCount > 0) return { label: '已传', type: 'success' }
  return { label: '未传', type: 'info' }
}

export function uploadPercent(item: PhotoBackfillItem): number {
  const required = requiredCountFor(item)
  if (required === null || required <= 0) {
    return item.uploadedCount > 0 ? 100 : 0
  }
  return Math.min(100, Math.round((item.uploadedCount / required) * 100))
}

export function toCapturePerson(item: PhotoBackfillItem): PhotoCapturePerson {
  return {
    idCard: item.idCard,
    name: item.personName,
    businessTypes: [...item.businessTypes],
    businessId: item.businessId,
  }
}
