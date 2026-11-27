import { readFileSync } from 'node:fs'
import { fileURLToPath, URL } from 'node:url'
import { defineConfig, loadEnv } from 'vite'
import type { ComponentResolver } from 'unplugin-vue-components'
import vue from '@vitejs/plugin-vue'
import AutoImport from 'unplugin-auto-import/vite'
import Components from 'unplugin-vue-components/vite'
import { ElementPlusResolver } from 'unplugin-vue-components/resolvers'

// 本地联调后端：NewCosmos.Server 默认监听 8787。
// /api 与 /api/auth 在开发模式下统一走 Vite 代理，避免浏览器跨域限制。
const devProxyTarget = process.env.NC_DEV_PROXY ?? 'http://127.0.0.1:8787'

// 版本单一来源：package.json；构建时注入 __APP_VERSION__，登录页/状态栏/心跳同源。
const packageJson = JSON.parse(
  readFileSync(new URL('./package.json', import.meta.url), 'utf-8'),
) as { version: string }

const EP_COMPONENTS = 'element-plus/es/components'

// 部分子组件没有独立入口，由父组件的 index 统一导出。
const EP_MODULE_ALIASES: Record<string, string> = Object.assign(Object.create(null), {
  'dropdown-menu': 'dropdown',
  'dropdown-item': 'dropdown',
  'table-column': 'table',
  'form-item': 'form',
  'menu-item': 'menu',
  'menu-item-group': 'menu',
  'sub-menu': 'menu',
  'checkbox-group': 'checkbox',
  'checkbox-button': 'checkbox',
  'radio-group': 'radio',
  'radio-button': 'radio',
  'button-group': 'button',
  'breadcrumb-item': 'breadcrumb',
  'tab-pane': 'tabs',
  'timeline-item': 'timeline',
  'descriptions-item': 'descriptions',
  'carousel-item': 'carousel',
  option: 'select',
  'option-group': 'select',
  'skeleton-item': 'skeleton',
  'avatar-group': 'avatar',
  'tour-step': 'tour',
  'anchor-link': 'anchor',
  'splitter-panel': 'splitter',
  step: 'steps',
  header: 'container',
  aside: 'container',
  main: 'container',
  footer: 'container',
})

function kebabCase(value: string): string {
  return value
    .replace(/([a-z0-9])([A-Z])/g, '$1-$2')
    .replace(/([A-Z])([A-Z][a-z])/g, '$1-$2')
    .toLowerCase()
}

function epStyles(name: string): string[] {
  return [`${EP_COMPONENTS}/base/style/css`, `${EP_COMPONENTS}/${name}/style/css`]
}

/**
 * Element Plus 的官方 resolver 从 `element-plus/es` 桶入口导入。
 * 配合 manualChunks 时，桶入口会把全部组件拉进依赖图，首屏 gzip 会超过 300KB 预算。
 * 这里改为直接指向具体组件目录，保持按需引入；官方 resolver 仍作为兜底。
 */
function ElementPlusDirectResolver(): ComponentResolver {
  return {
    type: 'component',
    resolve: (name: string) => {
      if (!/^El[A-Z]/.test(name)) return undefined

      // ElIconXxx 形式的图标组件直接走图标包
      if (/^ElIcon[A-Z]/.test(name)) {
        return { name: name.slice(2), from: '@element-plus/icons-vue' }
      }

      const partial = kebabCase(name.slice(2))
      const moduleName = EP_MODULE_ALIASES[partial] ?? partial
      return {
        name,
        from: `${EP_COMPONENTS}/${moduleName}/index`,
        sideEffects: epStyles(partial),
      }
    },
  }
}

function ElementPlusDirectiveResolver(): ComponentResolver {
  const directives: Record<string, string> = Object.assign(Object.create(null), {
    Loading: 'loading',
    Popover: 'popover',
    InfiniteScroll: 'infinite-scroll',
  })

  return {
    type: 'directive',
    resolve: (name: string) => {
      const partial = directives[name]
      if (!partial) return undefined
      return {
        name: name === 'Loading' ? 'ElLoadingDirective' : `El${name}Directive`,
        from: `${EP_COMPONENTS}/${partial}/index`,
        sideEffects: epStyles(partial),
      }
    },
  }
}

/** ElMessage / ElMessageBox 等命令式 API 同样改为具体路径导入。 */
function ElementPlusApiResolver() {
  const apis: Record<string, string> = Object.assign(Object.create(null), {
    ElMessage: 'message',
    ElMessageBox: 'message-box',
    ElNotification: 'notification',
    ElLoading: 'loading',
  })

  return {
    resolve: (name: string) => {
      const partial = apis[name]
      if (!partial) return undefined
      return {
        from: `${EP_COMPONENTS}/${partial}/index`,
        name,
        sideEffects: epStyles(partial),
      }
    },
  }
}

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  // root 固定为配置文件所在目录（真实路径）：C:\Projects 是指向 D:\Projects 的 junction，
  // 用 cwd 作 root 时 root=C:\... 而模块 id 被 realpath 成 D:\...，
  // path.relative 跨盘符返回绝对路径 → vite:build-html 发射 index.html 报错。
  const root = fileURLToPath(new URL('.', import.meta.url))
  return {
  root,
  // 生产页面在 .1，静态资源在 .2；必须用绝对 base，否则 .1 会 404 /assets。
  define: {
    __APP_VERSION__: JSON.stringify(packageJson.version),
  },
  base: (env.VITE_ASSET_BASE || '/').trim() || '/',
  plugins: [
    vue(),
    AutoImport({
      imports: ['vue', 'vue-router', 'pinia'],
      resolvers: [ElementPlusApiResolver(), ElementPlusResolver()],
      dts: 'auto-imports.d.ts',
    }),
    Components({
      resolvers: [ElementPlusDirectResolver(), ElementPlusDirectiveResolver(), ElementPlusResolver()],
      dts: 'components.d.ts',
    }),
  ],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    port: 5173,
    host: true,
    proxy: {
      '/api': {
        target: devProxyTarget,
        changeOrigin: true,
      },
    },
  },
  preview: {
    port: 4173,
  },
  build: {
    target: 'es2020',
    cssCodeSplit: true,
    sourcemap: false,
    // 首屏预算意识：路由级分包 + 依赖分包。
    chunkSizeWarningLimit: 700,
    rollupOptions: {
      output: {
        manualChunks(id) {
          const normalized = id.replace(/\\/g, '/')
          if (!normalized.includes('/node_modules/')) return undefined

          // 只归组实际进入依赖图的 Element Plus 组件，避免把图标/未用组件整体拉入。
          if (normalized.includes('/element-plus/')) {
            return 'element-plus'
          }
          if (
            normalized.includes('/vue/') ||
            normalized.includes('/@vue/') ||
            normalized.includes('/vue-router/') ||
            normalized.includes('/pinia/') ||
            normalized.includes('/vue-demi/')
          ) {
            return 'vue'
          }
          return undefined
        },
      },
    },
  },
  }
})
