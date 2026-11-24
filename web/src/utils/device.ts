const DEVICE_ID_KEY = 'nc.web.deviceId'

// localStorage 不可用（隐私模式/内嵌浏览器）时，保证同一次会话内 deviceId 稳定。
let memoryDeviceId = ''

function randomToken(): string {
  try {
    if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
      return crypto.randomUUID().replace(/-/g, '')
    }
  } catch {
    // 忽略：部分内嵌浏览器禁用 crypto
  }
  return `${Date.now().toString(36)}${Math.random().toString(36).slice(2, 12)}`
}

/**
 * 稳定的设备标识：写入 localStorage，登录、心跳、业务请求共用。
 * 形如 web-4f2c...，与 API 契约中的 deviceId 保持一致。
 */
export function getDeviceId(): string {
  if (memoryDeviceId) return memoryDeviceId
  try {
    const existing = localStorage.getItem(DEVICE_ID_KEY)
    if (existing) {
      memoryDeviceId = existing
      return existing
    }
    const created = `web-${randomToken()}`
    localStorage.setItem(DEVICE_ID_KEY, created)
    memoryDeviceId = created
    return created
  } catch {
    memoryDeviceId = `web-${randomToken()}`
    return memoryDeviceId
  }
}

export function isMobileUserAgent(): boolean {
  if (typeof navigator === 'undefined') return false
  return /Android|iPhone|iPad|iPod|HarmonyOS|Mobile/i.test(navigator.userAgent)
}

/** 契约允许的 deviceKind：web / mobile-web / desktop / android。 */
export function currentDeviceKind(): 'web' | 'mobile-web' {
  return isMobileUserAgent() ? 'mobile-web' : 'web'
}

/** 登录时上报的可读设备名，例如 "Chrome / Windows"。 */
export function describeDevice(): string {
  if (typeof navigator === 'undefined') return 'Browser / Unknown'
  const ua = navigator.userAgent
  const browser = /Edg\//.test(ua)
    ? 'Edge'
    : /OPR\//.test(ua)
      ? 'Opera'
      : /Firefox\//.test(ua)
        ? 'Firefox'
        : /Chrome\//.test(ua) || /CriOS\//.test(ua)
          ? 'Chrome'
          : /Safari\//.test(ua)
            ? 'Safari'
            : 'Browser'

  const os = /Windows/.test(ua)
    ? 'Windows'
    : /Android/.test(ua)
      ? 'Android'
      : /iPhone|iPad|iPod/.test(ua)
        ? 'iOS'
        : /Mac OS X/.test(ua)
          ? 'macOS'
          : /Linux/.test(ua)
            ? 'Linux'
            : 'Unknown'

  return `${browser} / ${os}`
}
