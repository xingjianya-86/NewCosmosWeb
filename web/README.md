# NewCosmos Web

Vue 3 + TypeScript + Element Plus 前端。页面部署在 `.1`，业务 API 直连 `.2`，认证走 `.1` 同源。

## 本地开发

```powershell
cd web
npm install
npm run dev
```

- 页面：http://localhost:5173
- `/api`（含 `/api/auth`）由 Vite 代理到 `http://127.0.0.1:8787`
- 修改后端地址：`$env:NC_DEV_PROXY = 'http://127.0.0.1:9000'; npm run dev`
- 如需开发时直连远端业务 API，新建 `.env.development.local` 覆盖 `VITE_API_BASE`（远端 CORS 仅允许生产域名，通常不建议）。

## 构建与检查

```powershell
npm run type-check   # vue-tsc
npm run build        # 类型检查 + 生产构建，输出 dist/
npm run preview      # 本地预览 dist/
```

## 环境变量

| 变量 | 说明 |
|---|---|
| `VITE_API_BASE` | 业务 API 根地址，如 `https://cosmos-static.sua17.cn:20000`。开发模式留空走代理。 |
| `VITE_AUTH_BASE` | 认证 API 根地址。留空表示同源 `/api/auth`。 |

参考 `.env.example`、`.env.development`、`.env.production`。`.env.*.local` 不提交。

## 目录

```text
src/
  api/           axios 客户端、认证/打印机接口、会话存储
  composables/   通用组合式函数（媒体查询）
  config/        应用名称/版本
  layouts/       主框架（桌面侧栏 + 手机底部导航）
  router/        路由与登录守卫
  stores/        Pinia（认证会话）
  styles/        全局样式与 Element Plus 主题变量
  types/         API/路由/axios 类型
  utils/         设备标识与 UA 描述
  views/         登录、首页、打印机绑定、404
```

## 约定

- 认证请求：`VITE_AUTH_BASE + /api/auth/*`；业务请求：`VITE_API_BASE + /api/*`。
- 请求头：`Authorization: Bearer <sessionId>`、`X-NC-Device: <deviceId>`。
- 会话存 `sessionStorage`，设备标识存 `localStorage`；业务接口 401 清会话并回登录页。
- Element Plus 按需引入（`unplugin-auto-import` + `unplugin-vue-components`），不引入全量包。
- 路由级懒加载；`vue` 与 `element-plus` 单独分包。

### Element Plus 分包说明

官方 `ElementPlusResolver` 从 `element-plus/es` 桶入口导入。与 `manualChunks` 组合时，
桶入口会把全部组件纳入依赖图，`element-plus` 分包会膨胀到约 300KB gzip。
`vite.config.ts` 中的 `ElementPlusDirectResolver` 改为按具体组件路径导入（官方 resolver 兜底），
在保持按需引入的同时让分包只包含实际使用的组件，首屏 JS gzip 约 190KB。

