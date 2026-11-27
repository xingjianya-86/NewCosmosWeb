import type { ApplicationStatus } from '@/types/application'

export type ApplicationStatusTone = 'ink' | 'seal' | 'pine' | 'amber' | 'violet'
export type ApplicationStatusDot = 'online' | 'offline' | 'default' | 'idle'

export interface ApplicationStatusMeta {
  label: string
  tone: ApplicationStatusTone
  dot: ApplicationStatusDot
}

const STATUS_META: Record<string, ApplicationStatusMeta> = {
  Draft: { label: '草稿', tone: 'ink', dot: 'offline' },
  Submitted: { label: '已提交', tone: 'amber', dot: 'idle' },
  Approved: { label: '已通过', tone: 'pine', dot: 'online' },
  Completed: { label: '已完成', tone: 'pine', dot: 'online' },
  Stopped: { label: '已终止', tone: 'seal', dot: 'default' },
  RemoteAssistanceDraft: { label: '异地协助办理中', tone: 'ink', dot: 'idle' },
  RemoteAssistanceCompleted: { label: '异地协助已办结', tone: 'pine', dot: 'online' },
}

export const APPLICATION_STATUS_OPTIONS: Array<{ label: string; value: ApplicationStatus }> = [
  { label: '草稿', value: 'Draft' },
  { label: '已提交', value: 'Submitted' },
  { label: '已通过', value: 'Approved' },
  { label: '已完成', value: 'Completed' },
  { label: '已终止', value: 'Stopped' },
]

/** 以接口返回的 statusText 为准，未知状态回退为墨色标签。 */
export function resolveApplicationStatus(
  status: string,
  statusText?: string,
): ApplicationStatusMeta {
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
