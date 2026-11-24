import type { AssetCheckStatus } from '@/types/assetVerification'

export type AssetStatusTone = 'ink' | 'seal' | 'pine' | 'amber'
export type AssetStatusDot = 'online' | 'offline' | 'default' | 'idle'

export interface AssetStatusMeta {
  label: string
  tone: AssetStatusTone
  dot: AssetStatusDot
}

const STATUS_META: Record<string, AssetStatusMeta> = {
  '0': { label: '待上传', tone: 'amber', dot: 'idle' },
  '1': { label: '有报告未建档', tone: 'seal', dot: 'default' },
  '2': { label: '已建档', tone: 'pine', dot: 'online' },
  '3': { label: '不予认定', tone: 'ink', dot: 'offline' },
}

/** 列表筛选顺序与旧桌面端一致：全部由空值表示。 */
export const ASSET_STATUS_OPTIONS: Array<{ label: string; value: AssetCheckStatus }> = [
  { label: '待上传', value: '0' },
  { label: '有报告未建档', value: '1' },
  { label: '已建档', value: '2' },
  { label: '不予认定', value: '3' },
]

/** 以接口 statusText 为准；未知状态回退墨色标签。 */
export function resolveAssetStatus(status: string, statusText?: string): AssetStatusMeta {
  const meta = STATUS_META[status] ?? {
    label: status || '未知状态',
    tone: 'ink' as const,
    dot: 'default' as const,
  }

  return {
    ...meta,
    label: statusText?.trim() || meta.label,
  }
}
