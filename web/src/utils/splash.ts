export const SPLASH_SEEN_KEY = 'nc.splash.seen'

/** URL ?nosplash=1 时跳过启动动画。 */
export function isSplashSuppressed(): boolean {
  if (typeof window === 'undefined') return true
  return new URLSearchParams(window.location.search).get('nosplash') === '1'
}

/** URL ?splash=1 时强制重播（即使已登录）。 */
export function isSplashForced(): boolean {
  if (typeof window === 'undefined') return false
  return new URLSearchParams(window.location.search).get('splash') === '1'
}

/** 保留给后续“每会话只播一次”策略；当前未登录每次启动都播。 */
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
