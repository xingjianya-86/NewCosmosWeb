export interface User {
  id: number
  username: string
  fullName?: string | null
  phone?: string | null
  organizationId?: number | null
  organizationName?: string | null
}

export interface LoginRequest {
  username: string
  password: string
  deviceId: string
  deviceKind: 'web' | 'mobile-web' | 'desktop' | 'android'
  deviceName: string
  appVersion: string
}

export interface LoginResponse {
  sessionId: string
  expiresAt: string
  user: User
}

export interface HeartbeatResponse {
  active: boolean
  expiresAt: string
}

export interface PrinterBinding {
  id: number
  deviceId: number
  deviceName: string
  printerName: string
  isDefault: boolean
  online: boolean
  lastSeenAt?: string | null
}

export interface PrinterDevice {
  id: number
  machineName: string
  online: boolean
  lastSeenAt?: string | null
  printers: string[]
  /** 本机操作能力码（font / zerotier …）；老 Agent 未上报时缺省。 */
  capabilities?: string[]
}

export interface BindPrinterRequest {
  deviceId: number
  printerName: string
  isDefault?: boolean
}

export interface ApiErrorBody {
  code?: string
  message?: string
  traceId?: string
}
