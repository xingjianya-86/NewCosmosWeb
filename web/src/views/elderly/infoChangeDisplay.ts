import type { ChipTone } from './display'
import type {
  InfoChangeDetail,
  InfoChangeFieldKey,
  InfoChangeRecord,
  InfoChangeRisk,
  InfoChangeStatus,
} from '@/types/elderlyInfoChange'
import { INFO_CHANGE_FIELD_MAP } from '@/types/elderlyInfoChange'

/** 合同 3：状态字典；提交/筛选只使用 key。 */
export const INFO_CHANGE_STATUS_OPTIONS = [
  { value: 'Pending', label: '待审批' },
  { value: 'Approved', label: '已通过' },
  { value: 'Rejected', label: '已驳回' },
  { value: 'Cancelled', label: '已作废' },
] as const

const STATUS_TEXT: Record<InfoChangeStatus, string> = {
  Pending: '待审批',
  Approved: '已通过',
  Rejected: '已驳回',
  Cancelled: '已作废',
}

export function infoChangeStatusTone(status: string): ChipTone {
  switch (status) {
    case 'Approved':
      return 'pine'
    case 'Pending':
      return 'amber'
    case 'Rejected':
      return 'seal'
    default:
      return 'ink'
  }
}

export function infoChangeStatusText(status: string, statusText?: string): string {
  const serverText = (statusText ?? '').trim()
  if (serverText) return serverText
  return STATUS_TEXT[status as InfoChangeStatus] ?? status ?? '—'
}

export function infoChangeRiskTone(risk: string): ChipTone {
  if (risk === 'HIGH') return 'seal'
  if (risk === 'LOW') return 'amber'
  return 'ink'
}

export function infoChangeSourceText(
  sourceType?: string,
  sourceTypeText?: string,
): string {
  const text = (sourceTypeText ?? '').trim()
  if (text) return text
  switch (sourceType) {
    case 'Manual':
      return '手工登记'
    case 'CertificationImport':
      return '认证导入'
    case 'Certification':
      return '认证过程'
    default:
      return sourceType || '—'
  }
}

export function infoChangeRiskText(risk: string): string {
  if (risk === 'HIGH') return '高风险'
  if (risk === 'LOW') return '低风险'
  return risk || '—'
}

/** 明细缺少 fieldLabel 时按合同白名单补中文名。 */
export function infoChangeFieldLabel(fieldKey: string, fieldLabel?: string): string {
  const text = (fieldLabel ?? '').trim()
  if (text) return text
  return INFO_CHANGE_FIELD_MAP[fieldKey as InfoChangeFieldKey]?.fieldLabel ?? fieldKey ?? '—'
}

/** 明细风险：服务端缺省时按字段白名单回填。 */
export function detailRisk(detail: InfoChangeDetail): string {
  if (detail.risk === 'HIGH' || detail.risk === 'LOW') return detail.risk
  return INFO_CHANGE_FIELD_MAP[detail.fieldKey as InfoChangeFieldKey]?.risk ?? 'LOW'
}

export function detailRiskText(detail: InfoChangeDetail): string {
  const text = (detail.riskText ?? '').trim()
  if (text) return text
  return infoChangeRiskText(detailRisk(detail))
}

/** 银行卡/账户类字段：审批时可填生效月。 */
export function isBankRelatedField(fieldKey: string): boolean {
  return fieldKey.includes('bank')
}

export function recordInvolvesBank(record: InfoChangeRecord | null | undefined): boolean {
  if (!record) return false
  return record.details.some((detail) => isBankRelatedField(detail.fieldKey))
}

/** 整单风险：服务端 changeType 优先，否则按明细取最高。 */
export function recordRisk(record: InfoChangeRecord | null | undefined): InfoChangeRisk | string {
  if (!record) return '—'
  if (record.changeType === 'HIGH' || record.changeType === 'LOW') return record.changeType
  return record.details.some(
    (detail) =>
      detail.risk === 'HIGH' ||
      INFO_CHANGE_FIELD_MAP[detail.fieldKey as InfoChangeFieldKey]?.risk === 'HIGH',
  )
    ? 'HIGH'
    : 'LOW'
}

export function recordFieldLabels(record: InfoChangeRecord | null | undefined): string[] {
  if (!record) return []
  return record.details.map((detail) => infoChangeFieldLabel(detail.fieldKey, detail.fieldLabel))
}

/** 下个月，YYYY-MM；作为银行卡变更的默认生效月。 */
export function nextMonthValue(): string {
  const now = new Date()
  const date = new Date(now.getFullYear(), now.getMonth() + 1, 1)
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`
}
