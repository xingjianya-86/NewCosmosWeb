import type { CapacitorConfig } from '@capacitor/cli'

/**
 * NewCosmos Android 壳配置。
 *
 * 生产：直接加载 https://cosmos.sua17.cn（Vue 业务页面仍在服务器上迭代，壳不打包业务 UI）。
 * 测试：设置环境变量 NC_MOBILE_URL 后执行 npx cap sync android，例如
 *   $env:NC_MOBILE_URL = "http://192.168.1.10:5173"; npx cap sync android
 * 仅当使用 http:// 测试地址时才开启 cleartext / mixed content，生产永远走 https。
 */

const PRODUCTION_URL = 'https://cosmos.sua17.cn'

function resolveServerUrl(): string {
  const override = (process.env.NC_MOBILE_URL ?? '').trim()
  return (override || PRODUCTION_URL).replace(/\/+$/, '')
}

const serverUrl = resolveServerUrl()
const isCleartext = serverUrl.startsWith('http://')
// 任何 NC_MOBILE_URL 覆盖都视为联调构建：打开 WebView 调试与混合内容。
const isDevBuild = isCleartext || Boolean((process.env.NC_MOBILE_URL ?? '').trim())

const config: CapacitorConfig = {
  appId: 'cn.sua17.newcosmos',
  appName: '帝皇权杖δ-me13',
  webDir: 'www',
  server: {
    url: serverUrl,
    androidScheme: 'https',
    cleartext: isCleartext,
    // 业务域与静态资源域留在壳内；其余链接交给系统浏览器（原生 NcWebViewClient 最终裁决）。
    allowNavigation: ['cosmos.sua17.cn', 'cosmos-static.sua17.cn'],
  },
  android: {
    // 仅联调构建允许 https 页面加载 http 子资源；生产为 false。
    allowMixedContent: isDevBuild,
    captureInput: true,
    webContentsDebuggingEnabled: isDevBuild,
    backgroundColor: '#f4f6fa',
  },
  plugins: {
    // 使用原生 insets 让 WebView 整体避开状态栏/导航栏/输入法，页面 env(safe-area-inset-*)
    // 由壳统一处理，避免业务页面顶部被状态栏遮挡。
    SystemBars: {
      insetsHandling: 'native',
      style: 'LIGHT',
    },
  },
}

export default config
