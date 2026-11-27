import type { ChipTone } from './display'

/** 合同 1. 状态字典的前端展示选项；提交值时只使用 key。 */
export const CERT_STATUS_OPTIONS = [
  { value: 'Pending', label: '待核查' },
  { value: 'Verified', label: '已通过' },
  { value: 'Refused', label: '拒绝核查' },
  { value: 'Failed', label: '核查不通过' },
  { value: 'Deceased', label: '已去世' },
  { value: 'MovedOut', label: '已迁出' },
  { value: 'NotLocated', label: '无法联系' },
  { value: 'Exempt', label: '免核查' },
  { value: 'Stopped', label: '其他原因已停发' },
  { value: 'Invalid', label: '数据无效' },
] as const

export const VERIFY_METHOD_OPTIONS = [
  { value: 'Face', label: '人脸识别' },
  { value: 'Onsite', label: '上门' },
  { value: 'Video', label: '视频' },
  { value: 'Photo', label: '照片' },
  { value: 'Other', label: '其他' },
] as const

export const EVIDENCE_STATUS_OPTIONS = [
  { value: 'None', label: '无凭证' },
  { value: 'Complete', label: '齐全' },
  { value: 'Partial', label: '部分缺失' },
  { value: 'Missing', label: '全部缺失' },
  { value: 'Ambiguous', label: '文件名歧义' },
] as const

export function certStatusTone(status: string): ChipTone {
  switch (status) {
    case 'Verified':
    case 'Exempt':
      return 'pine'
    case 'Pending':
      return 'amber'
    case 'Refused':
    case 'Failed':
    case 'Deceased':
    case 'MovedOut':
    case 'NotLocated':
    case 'Stopped':
    case 'Invalid':
      return 'seal'
    default:
      return 'ink'
  }
}

export function evidenceStatusTone(status: string): ChipTone {
  switch (status) {
    case 'Complete':
      return 'pine'
    case 'Partial':
    case 'Ambiguous':
      return 'amber'
    case 'Missing':
      return 'seal'
    default:
      return 'ink'
  }
}

export function stopDecisionTone(decision: string): ChipTone {
  switch (decision) {
    case 'Approved':
      return 'pine'
    case 'Pending':
    case 'Deferred':
      return 'amber'
    case 'Excluded':
      return 'seal'
    default:
      return 'ink'
  }
}

export function campaignStatusTone(status: string): ChipTone {
  switch (status) {
    case 'Collecting':
      return 'pine'
    case 'Reviewing':
      return 'amber'
    case 'Closed':
      return 'seal'
    default:
      return 'ink'
  }
}

export function formatFileSize(size: number | null | undefined): string {
  if (size === null || size === undefined || !Number.isFinite(size)) return '—'
  if (size < 1024) return `${size} B`
  if (size < 1024 * 1024) return `${(size / 1024).toFixed(1)} KB`
  return `${(size / 1024 / 1024).toFixed(1)} MB`
}

export function campaignOptionLabel(displayName: string, year: number, round: number): string {
  const name = displayName.trim() || '未命名批次'
  const suffix = `${year} 年 · 第 ${round} 轮`
  return name.includes(suffix) ? name : `${name}（${suffix}）`
}
