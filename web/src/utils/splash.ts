export const SPLASH_SEEN_KEY = 'nc.splash.seen'

/** URL ?nosplash=1 或本次会话已播过时，不再启动动画。 */
export function isSplashSuppressed(): boolean {
  if (typeof window === 'undefined') return true

  const params = new URLSearchParams(window.location.search)
  if (params.get('nosplash') === '1') return true

  try {
    return window.sessionStorage.getItem(SPLASH_SEEN_KEY) === '1'
  } catch {
    return false
  }
}

export function markSplashSeen(): void {
  try {
    window.sessionStorage.setItem(SPLASH_SEEN_KEY, '1')
  } catch {
    // 隐私模式 / 存储不可用：不阻断启动
  }
}

/** 动画静态资源在 .2；页面在 .1，origin 参数用于 bridge.postMessage 的 targetOrigin。 */
export function buildSplashUrl(): string {
  const base = String(import.meta.env.VITE_ASSET_BASE || '/').trim() || '/'
  const dir = base.endsWith('/') ? base : `${base}/`
  return `${dir}splash/splash.html?origin=${encodeURIComponent(window.location.origin)}`
}
