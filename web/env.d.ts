/// <reference types="vite/client" />

/** 构建期注入的产品版本号（来源 web/package.json）。 */
declare const __APP_VERSION__: string

interface ImportMetaEnv {
  /** 业务 API 根地址，例如 https://cosmos-static.sua17.cn:20000；开发模式留空以走 Vite 代理。 */
  readonly VITE_API_BASE?: string
  /** 认证 API 根地址；留空表示同源（/api/auth）。 */
  readonly VITE_AUTH_BASE?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
