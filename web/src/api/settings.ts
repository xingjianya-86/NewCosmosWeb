import { authHttp, apiHttp } from './http'

export interface SessionPolicy {
  mode: number
  maxDesktop: number
  maxMobile: number
  heartbeatDesktopSeconds: number
  heartbeatMobileSeconds: number
  onlineWindowSeconds: number
  absoluteDays: number
  graceSeconds: number
}

export function getSessionPolicy(): Promise<SessionPolicy> {
  return authHttp.get<SessionPolicy>('/policy').then((response) => response.data)
}

export function changePassword(
  oldPassword: string,
  newPassword: string,
): Promise<{ message: string }> {
  return authHttp
    .post<{ message: string }>('/change-password', { oldPassword, newPassword })
    .then((response) => response.data)
}

export interface ClientRelease {
  version?: string
  versionCode?: number
  versionName?: string
  url: string
  sha256?: string
  notes?: string
  publishedAt?: string
}

const DOWNLOAD_BASE = 'https://cosmos-static.sua17.cn:20000/downloads'

export async function getClientReleases(): Promise<{
  windows: ClientRelease | null
  android: ClientRelease | null
}> {
  const load = async (url: string): Promise<ClientRelease | null> => {
    try {
      const response = await fetch(`${url}?t=${Date.now()}`, { cache: 'no-store' })
      if (!response.ok) return null
      return (await response.json()) as ClientRelease
    } catch {
      return null
    }
  }

  const [windows, android] = await Promise.all([
    load(`${DOWNLOAD_BASE}/windows/latest.json`),
    load(`${DOWNLOAD_BASE}/android/latest.json`),
  ])
  return { windows, android }
}

export const CLIENT_DOWNLOADS = {
  windows: `${DOWNLOAD_BASE}/windows/NewCosmosShellSetup_2.0.20261124.exe`,
  android: `${DOWNLOAD_BASE}/android/newcosmos-2.0.20261126.apk`,
  codeSignCert: `${DOWNLOAD_BASE}/windows/NewCosmosCodeSign.cer`,
}

export { apiHttp }
