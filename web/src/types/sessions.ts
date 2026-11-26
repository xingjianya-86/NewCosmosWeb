export interface OnlineSession {
  sessionId: string
  userId: number
  username: string
  fullName: string
  organizationName: string
  roleName: string
  deviceKind: string
  deviceName: string
  appVersion: string
  loginAt: string
  lastSeenAt: string
  isOnline: boolean
  status: string
}

export interface OnlineSessionList {
  items: OnlineSession[]
  total: number
}
