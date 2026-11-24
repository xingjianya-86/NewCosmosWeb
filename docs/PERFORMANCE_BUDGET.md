# 性能预算（PERFORMANCE_BUDGET）

> 基于真实服务器与现有部署配置：
> - `.1` 10.110.0.1：2C / 1.6G / **3Mbps**，只发 `index.html` + 反代 `/api/auth/*`。
> - `.2` 10.110.0.2：4C / 7.7G / **100Mbps**，nginx 静态 + ASP.NET Core API + PostgreSQL + OCR + Lottery Worker。
> - Windows 固定 PC：`NewCosmos.Agent`，Office COM/打印，出站连 `.2`。
>
> 配置事实来源：`deploy/edge-1/nginx-cosmos.sua17.cn.conf`、`deploy/app-2/nginx-cosmos-static.sua17.cn.conf`、`docs/ARCHITECTURE.md`。
> 目标：在现有硬件上保证“登录 2 秒级可用、列表 1 秒内出数、打印任务可预期”，不靠加带宽掩盖问题。

## 0. 硬约束与换算

| 项 | 数值 | 含义 |
|---|---:|---|
| `.1` 带宽 | 3Mbps ≈ 375KB/s | 任何 300KB 资源放 `.1`，单用户就占满 ~0.8s；10 人并发直接排队 |
| `.2` 带宽 | 100Mbps ≈ 12.5MB/s 理论，按 8MB/s 规划 | 照片/Excel/PDF 都在这里，峰值不超过 60–70Mbps |
| `.2` CPU | 4 核 | API + PG + OCR + Worker 共享；重任务必须错峰 |
| `.2` 内存 | 7.7G | PG 1G shared_buffers + API 堆 + OCR 模型 + Python/TF 必须分账 |
| `.1` 请求体上限 | 2MB | 登录/心跳足够；业务上传不许走 `.1` |
| `.2` 请求体上限 | 128MB | Excel/照片上传上限；超过走分片/后台任务 |
| `.1` 代理读超时 | 30s | `/api/auth/*` 必须 30s 内返回 |
| `.2` 代理读超时 | 300s | 仅用于少数长任务；正常 API 应远低于此 |
| OCR 单张耗时 | 约 30s，大图可达 2 分钟 | 客户端超时 360s；服务端 `OLLAMA_NUM_PARALLEL=1` 单槽串行 |
| 照片压缩 | 长边 2000/1200，目标 350KB，硬上限 500KB | 新采集 WebP；旧数据 JPEG/PNG |

### 0.1 `.1` 只允许两类流量

| 路径 | 预算 | 说明 |
|---|---|---|
| `GET /index.html` | gzip ≤ 10KB | `no-store`（配置已如此），每次导航重新取，必须小 |
| `/api/auth/login`、`heartbeat`、`logout`、`me` | 请求 ≤ 2KB，响应 ≤ 2KB | 只做认证与会话，不查业务表 |
| 其它 | 404 | `/assets`、照片、导入、导出、备份一律不得经过 `.1` |

> 红线：`.1` 出现 `/assets` 或 `/downloads` 即视为事故。3Mbps 下 300KB 首屏资源 = 0.8s 纯传输，10 人并发 = 8s，体验不可接受。

## 1. 首屏预算

### 1.1 分项（gzip 后）

| 资源 | 预算 | 上限 | 说明 |
|---|---:|---:|---|
| `index.html` | 6KB | 10KB | 内联少量关键 CSS，不引外部字体 |
| 入口 JS（app + router + pinia + axios） | 60KB | 80KB | 不含业务页面 |
| 入口 CSS | 25KB | 40KB | Element Plus 变量 + 布局 |
| Vue 运行时 vendor | 45KB | 55KB | 已由 `manualChunks` 单独分包 |
| Element Plus 实际组件 chunk | 90KB | 130KB | 按需引入；登录只用 Form/Input/Button/Checkbox |
| 登录路由 chunk | 20KB | 30KB | 登录页 + 品牌区 |
| 图标/Logo | 10KB | 30KB | 单色 SVG/PNG，不用整套图标字体 |
| **登录首屏合计** | **≤ 250KB** | **≤ 300KB** | 与 `ARCHITECTURE.md` 的 300KB 上限一致 |

### 1.2 登录后页面

| 场景 | 预算 | 说明 |
|---|---:|---|
| 主框架 + 首页 | 增量 ≤ 180KB | 布局、菜单、首页卡片；不含 Element Plus 共享 chunk |
| 单个业务路由 chunk | ≤ 150KB | 表单页可到 200KB，但必须独立 chunk |
| 单次会话累计（登录 → 首页 → 1 个业务页） | ≤ 550KB | 之后命中 immutable 缓存 |
| 任意 async chunk 警告线 | 300KB | 超过必须在 CI 报警 |
| 任意 async chunk 硬上限 | 400KB | 超过禁止合并 |

### 1.3 时间预算

| 网络 | 有效带宽 | 登录首屏 250KB 传输 | 目标 |
|---|---:|---:|---|
| 办公宽带 20Mbps+ | ~2.5MB/s | 0.1s | ≤1.5s 可交互 |
| 4G 良好 | ~1.5Mbps | 1.4s | ≤2.5s 可交互 |
| 4G 弱/乡镇 | ~0.8Mbps | 2.6s | ≤4s 可交互（骨架屏必须立即出现） |
| `.1` 3Mbps 仅 index | 375KB/s | 10KB = 0.03s | 不受带宽限制 |

> 首屏时间以 `index.html` TTFB + 入口 chunk 下载 + 路由 chunk 下载 + 首次接口为准；照片、字典、非首屏路由不计入。

## 2. API 响应预算

### 2.1 时延（服务端 p95，不含网络）

| 接口类型 | 预算 p95 | p99 | 超时/降级 |
|---|---:|---:|---|
| `POST /api/auth/login` | 500ms | 800ms | BCrypt 是主要成本；>1s 告警 |
| `POST /api/auth/heartbeat` | 30ms | 60ms | 只查会话索引 |
| `GET /api/auth/me` | 50ms | 100ms | 可缓存 30s |
| 列表（50 行） | 120ms | 250ms | 必须分页 + 覆盖索引 |
| 详情（单实体 + 关联） | 200ms | 400ms | 禁止 N+1 |
| 保存/提交（单事务） | 300ms | 600ms | 事务内不做文件/Office |
| 收入核算/分类判定 | 100ms | 200ms | 纯内存计算 |
| 字典/区划全量 | 80ms | 150ms | ETag + 客户端缓存 |
| 月报/统计聚合 | 2s | 4s | 超过 2s 改异步任务 + 进度 |
| OCR 识别（单张） | 30s | 130s | 单槽串行；建议异步任务 + 轮询，不长期占用 HTTP |
| 照片缩略图 | 80ms | 150ms | 命中缓存 <20ms |
| 照片原图 | 200ms | 400ms | 单张 ≤500KB |
| 打印入队 | 80ms | 150ms | 只写队列表 |
| 导入任务受理 | 150ms | 300ms | 立即返回 jobId，后台解析 |

### 2.2 响应体（未压缩 JSON）

| 接口 | 预算 | 说明 |
|---|---:|---|
| 登录/心跳/me | ≤ 2KB | 只返回必要字段 |
| 列表 50 行 | ≤ 80KB | 单行 ≤1.6KB；宽表必须裁剪 DTO |
| 列表 100 行 | ≤ 150KB | 上限；手机默认 10–20 行 |
| 详情 | ≤ 200KB | 不含照片；照片只给 URL |
| 字典单类型 | ≤ 100KB | 全量字典拆类型、分版本 |
| 月报 JSON | ≤ 500KB | 大表走文件下载 |
| 错误体 | ≤ 1KB | `code/message/traceId` |
| 照片 | 缩略图 ≤30KB / 预览 ≤150KB / 原图 ≤500KB | 禁止 base64 塞 JSON（体积 +33%） |

### 2.3 `.2` 带宽账

| 场景 | 并发 | 单次体积 | 合计 | 8MB/s 下耗时 |
|---|---:|---:|---:|---:|
| 列表浏览 | 30 | 80KB | 2.4MB | 0.3s |
| 照片网格（20 张缩略图） | 10 人 | 20×25KB=500KB | 5MB | 0.6s |
| 照片预览（5 张） | 10 人 | 5×120KB=600KB | 6MB | 0.75s |
| 批量导出下载 | 2 | 30MB | 60MB | 7.5s（需限速/排队） |
| Excel 上传 | 2 | 20MB | 40MB | 5s |

> 规划峰值 ≤60Mbps（7.5MB/s）。`/downloads` 已配 `limit_rate 8m`、每 IP 4 连接、10r/s，保持现状。

## 3. 照片与缩略图

### 3.1 三档规格

| 档位 | 长边 | 格式/质量 | 目标大小 | 用途 |
|---|---:|---|---:|---|
| 缩略图 | 320px | WebP q70 | 20–30KB | 列表/网格/OCR 结果卡片 |
| 预览图 | 1280px | WebP q75 | 100–150KB | 详情预览、手机查看 |
| 原图 | 2000px | WebP q80→50 | 350KB（硬上限 500KB） | 打印、归档 |

- 采集端保持现有压缩口径（`PhotoCompressionConstants`），服务端补生成缩略图/预览图，原图不动。
- 列表接口只返回 `id/category/photoName/fileSize/createdAt/thumbUrl`，**绝不返回 `photo_data`**（原实现已如此，Web 必须保持）。
- 取图接口：`GET /api/photos/{id}/thumb|preview|original`，鉴权 + 数据范围校验；`ETag` 用内容哈希，`Cache-Control: private, max-age=86400, immutable`。
- 前端：`loading="lazy"`、`decoding="async"`、`srcset` 选档；照片网格超过 100 张用虚拟滚动。
- 打印链路：Agent 取原图或由服务端把照片放入打印任务（`nc_biz_print_jobs.photo_data`），手机不传原图两次。

### 3.2 容量估算

| 人均照片 | 人均体积 | 5 万人 | 10 万人 | 20 万人 |
|---:|---:|---:|---:|---:|
| 3 张 | 1.05MB | 52GB | 105GB | 210GB |
| 5 张 | 1.75MB | 87GB | 175GB | 350GB |
| 8 张 | 2.8MB | 140GB | 280GB | 560GB |

结论：
- 数据盘至少 500GB；超过 15 万人建议 1TB 并单独挂载 PG 数据目录。
- WebP 已压缩，PG TOAST 不会再显著压缩；`pg_dump` 体积≈库体积，备份盘按 **数据盘 ×2** 预留。
- 缩略图可重建，备份可不含缩略图表；原图必须进备份。

## 4. 导出、导入、备份

| 任务 | 同步上限 | 异步策略 | 预算 |
|---|---:|---|---|
| Excel 导出 | 2 万行 / 10MB | 超过转后台任务，完成后给下载链接 | 2 万行 ≤5s；10 万行 ≤30s |
| PDF 单份 | — | Agent/Server 渲染 | 单份 ≤3s；≤5MB |
| PDF 批量 | 20 份 | 超过分批 + 进度 | 50 份 ≤2 分钟 |
| Excel 导入 | 1 万行 | 超过后台任务；并发 2 | 1 万行 ≤15s；10 万行 ≤3 分钟 |
| 数据库备份 | — | 每日低峰 `pg_dump -Fc` | 100GB 库 ≤40 分钟（并行 4） |
| 数据库恢复 | — | 维护模式 + 二次确认 | 100GB ≤60 分钟；RTO 2 小时 |

规则：
- 导出/备份**不得经过 `.1`**；下载只在 `.2` `/downloads` 或鉴权 API，带限速。
- 导入上传 ≤128MB（nginx 上限），超过拆文件；一次只接受 2 个导入任务。
- 备份保留：日备 7 份、周备 4 份、月备 3 份；每月做一次恢复演练。
- Linux Server 使用系统 `pg_dump/psql`（版本必须与 PG 主版本一致）；不要沿用桌面随包的 `Resources/PostgreSQL/*.exe`。
- RPO：默认 24 小时；若磁盘允许，开启 WAL 归档可做到 ≤15 分钟。
- 备份文件命名带时间戳 + SHA256；恢复前先自动备份当前库。

## 5. PostgreSQL / 内存 / 并发

### 5.1 内存分账（总量 7.7G）

| 组件 | 常态预算 | 训练/高峰预算 | 说明 |
|---|---:|---:|---|
| OS + nginx + 监控 | 0.5G | 0.5G | 含页缓存，不挤占 |
| PostgreSQL | 2.6G | 2.4G | `shared_buffers=1G`，其余连接/排序/维护 |
| ASP.NET Core API | 1.8G | 1.5G | 工作站 GC；堆硬限 1.5G（`DOTNET_GCHeapHardLimit=0x60000000`） |
| OCR 服务（Ollama） | 1.2G | 0.8G | 单槽；模型常驻 |
| Lottery Worker | 0.6G | 2.0G | 仅训练窗口升到 2G |
| 余量 | 1.0G | 0.5G | 防 OOM/突发 |
| **合计** | **7.7G** | **7.7G** | 训练时必须降 API/OCR，不能同时满配 |

> `ARCHITECTURE.md` 的“堆上限 3GB”是上限而非常态分配；4C/7.7G 与 PG/OCR/Worker 同机时，API 常态 1.5–1.8G 更安全。

### 5.2 PostgreSQL 建议参数

| 参数 | 建议值 | 理由 |
|---|---|---|
| `shared_buffers` | 1GB | 与现有架构文档一致 |
| `effective_cache_size` | 2.5GB | 让规划器倾向索引 |
| `work_mem` | 8MB | 全局；报表会话 `SET LOCAL work_mem='32MB'` |
| `maintenance_work_mem` | 256MB | vacuum/索引重建 |
| `max_connections` | 100 | 应用池 20–30，留运维/备份 |
| `random_page_cost` | 1.1 | SSD |
| `effective_io_concurrency` | 200 | SSD |
| `wal_compression` | on | 降低 WAL 体积 |
| `max_wal_size` / `min_wal_size` | 4GB / 1GB | 避免频繁 checkpoint |
| `checkpoint_timeout` | 15min | 平滑写盘 |
| `autovacuum_max_workers` | 3 | 照片/队列表更新频繁 |
| 大表 autovacuum scale factor | 0.05 | `nc_biz_applications`、照片、打印队列表 |
| `default_statistics_target` | 100 | 宽表关联 |
| `log_min_duration_statement` | 500ms | 慢查询基线 |
| `pg_stat_statements` | 开启 | 监控 Top SQL |

连接池（Npgsql）：`MaxPoolSize=30`、`MinPoolSize=5`、`Timeout=15s`、`CommandTimeout=30s`；导入/备份单独连接串（`CommandTimeout=300s`），避免占满业务池。

### 5.3 并发配额

| 资源 | 并发上限 | 说明 |
|---|---:|---|
| API 同时在途请求 | 80–100 | 全异步 I/O；超过排队而非加线程 |
| 报表聚合 | 2 | 超过排队/转异步 |
| Excel 导入 | 2 | 每任务单线程流式 |
| OCR 推理 | 1（Ollama 单槽） | 客户端批量并发必须 =1，否则排队撞 300s |
| 打印任务（Agent） | 每 Agent 1 | Office COM STA 串行 |
| Lottery 训练 | 1 | 错峰，训练时暂停批量导出 |
| 备份 | 1 | 低峰执行 |

### 5.4 关键查询预算与索引

| 查询 | 目标 | 依赖索引 |
|---|---:|---|
| 会话校验（sessionId） | <5ms | `nc_sys_user_sessions` 主键/唯一 |
| 申请列表（组织+状态+时间） | <80ms | `(organization_id, status, created_at)` |
| 按身份证聚合档案 | <50ms | `applicant_id_card` |
| 照片列表（人+类别+时间） | <50ms | 已有 `idx_archive_photos_person` |
| 打印队列认领 | <20ms | 已有 `(status, created_at)`；用 `FOR UPDATE SKIP LOCKED` |
| 月报聚合 | <1.5s | 时间范围 + 分类；禁止 `EXTRACT()` 包列 |
| 字典读取 | <10ms | 版本号缓存，进程内 DictCache |

## 6. 监控项与告警阈值

| 层 | 指标 | 告警阈值 | 处置 |
|---|---|---|---|
| `.1` | 带宽 | 持续 >2.4Mbps（80%） | 查是否误发 assets/大响应 |
| `.1` | 内存 | >85% | 查 nginx 缓冲/连接 |
| `.1` | 5xx / 499 | >1% / >5% | 查 `.2` 上游与超时 |
| `.2` | CPU | >75% 持续 5 分钟 | 查报表/导入/OCR/训练 |
| `.2` | 负载 | >8（4C） | 限流重任务 |
| `.2` | 内存 | >90% | 查 API 堆、PG、OCR、TF |
| `.2` | 磁盘 | >80% | 清临时/旧备份/日志 |
| `.2` | 网络出 | 持续 >70Mbps | 限下载、缩略图、批量导出 |
| API | p95 时延 | >500ms（列表）/ >2s（报表） | 查慢 SQL、GC、线程池 |
| API | 5xx | >0.5% | 立即查日志/traceId |
| PG | 连接数 | >80 | 查泄漏/池配置 |
| PG | 慢查询 | >500ms 次数突增 | `pg_stat_statements` Top SQL |
| PG | 锁等待/死锁 | >30s / 任意 | 查长事务 |
| PG | 表膨胀 | >30% | 手动 vacuum/调整 autovacuum |
| PG | 库大小 | 日增 >2% | 查照片/日志/导入残留 |
| 照片 | 缩略图命中率 | <90% | 预热/回源过多 |
| 打印 | Pending 最老任务 | >5 分钟 | Agent 离线/打印机故障 |
| Agent | 心跳 | >2 分钟未上报 | 通知对应 PC |
| OCR | 单张耗时 | >60s 比例 >20% | 降图片尺寸/查模型 |
| OCR | 失败率 | >5% | 查 Ollama/队列 |
| Lottery | Worker 内存/CPU | >2G / >1.5 核 | 停止训练 |
| 备份 | 最近成功时间 | >26 小时 | 立即补备 |
| 证书 | 到期 | <14 天 | 续签 |

## 7. Vue 侧策略

### 7.1 按需引入（现状 + 要求）

现状：`web/vite.config.ts` 已使用 `unplugin-auto-import` + `unplugin-vue-components` 的 `ElementPlusResolver`，并已按 `element-plus`/`vue` 做 `manualChunks`。

要求：
- 禁止 `app.use(ElementPlus)` 全量注册；禁止全量 `import 'element-plus/dist/index.css'`（用按需样式或 CSS 变量）。
- 图标按名引入：`import { Search } from '@element-plus/icons-vue'`，不引整套。
- 中文 locale 只引一次；日期库不额外引入（用 `dayjs` 的现有依赖或原生）。
- 业务组件（签名、照片网格、PDF 预览）独立 chunk，不进首屏。
- 构建产物纳入预算检查：解析 `dist/assets/*.js` gzip 后大小，超 300KB 报警、超 400KB 失败。

### 7.2 路由分包

```ts
// 路由级懒加载，按域分 chunk
{ path: '/assist', component: () => import('@/views/assist/Home.vue') }
{ path: '/assist/applications/new', component: () => import('@/views/assist/ApplicationForm.vue') }
{ path: '/archives/output/:type/:id', component: () => import('@/views/archives/ArchiveOutput.vue') }
```

- 域级前缀共享一个 layout chunk；同域页面可合并，但单 chunk ≤150KB。
- 登录后 `requestIdleCallback` 预取高频路由：`/home`、`/assist`、`/archives/query`。
- 不在首屏预取 PDF/Office/报表/彩票模块。
- `keep-alive` 只用于列表页（最多 6 个），表单页禁用，避免脏草稿；离开表单用 `onBeforeRouteLeave` 提示。

### 7.3 数据与缓存

| 数据 | 策略 | TTL |
|---|---|---|
| 字典/区划 | Pinia + localStorage，带 `version`/`ETag` | 24h 或版本变更 |
| 当前用户 | Pinia 内存 | 会话期 |
| 列表 | 不缓存，路由回退可保留 1 页 | — |
| 详情 | 不缓存（PII） | — |
| 照片缩略图 | 浏览器 HTTP 缓存（immutable） | 1 天 |
| 照片原图 | 仅打印/预览按需 | 1 天 |
| 报表/导出文件 | 服务端生成，带一次性/短期链接 | 24h |

- 禁止把 token、身份证、照片写 localStorage/IndexedDB。
- 搜索 300ms 防抖；路由切换 `AbortController` 取消在途请求。
- 分页：桌面 20/50，手机 10/20；服务端分页，禁止前端全量拉取。
- 表格 >200 行用虚拟滚动（`el-table-v2` 或自研）；照片网格 >100 张虚拟化。
- PDF：动态 `import('pdfjs-dist')`，只渲染当前页/缩略图；大文件分片 range 请求。
- 骨架屏：列表/详情/首页首屏必须有，避免白屏。

### 7.4 静态缓存

| 资源 | 头 | 说明 |
|---|---|---|
| `index.html` | `no-store`（`.1` 已配） | 发布即生效 |
| `/assets/*-hash.js/css` | `public, max-age=31536000, immutable`（`.2` 已配） | 文件名 hash |
| 字体/图片（hash） | 同上 | 不放 `.1` |
| `/api/auth/*` | `no-store`（`.1` 已配） | 不缓存 |
| 业务 GET（字典） | `private, max-age=300` + `ETag` | 允许 304 |
| 业务 GET（列表/详情） | `no-store` | PII |
| 照片 | `private, max-age=86400, immutable` + `ETag` | 按内容哈希 |
| 下载 | `private, max-age=3600` + 限速 | 不放公开目录 |

> 不引入 Service Worker：内网发布频繁、会话敏感，静态资源已 immutable，收益低、失效风险高。

## 8. 打印与 Agent 预算

| 项 | 预算 | 说明 |
|---|---:|---|
| Agent 心跳 | 30s（契约） | 断线 2 分钟告警 |
| 打印队列轮询 | 5s 或长轮询 30s | 用 `FOR UPDATE SKIP LOCKED` 认领，避免抢单 |
| 单任务超时 | 5 分钟（可配） | 超时回写 Failed，允许重试 |
| 单份 Word→PDF | ≤5s | 模板简单；复杂表格 ≤15s |
| 批量打印 | 每批 ≤50 份 | 逐份进度，失败明细可重试 |
| 照片打印 | 单张 ≤10s | 转换 + 排版 + 打印 |
| 任务保留 | 30 天 | 软删除，定期清理 |

移动端 `/m/print-jobs` 只在页面可见时 3–5s 轮询，隐藏即停；不要用 WebSocket 常连（3Mbps/移动网络成本高）。

## 9. OCR 预算（重点）

事实：服务端 `OLLAMA_NUM_PARALLEL=1`，单槽串行；单张约 30s，大图可达 128s；客户端超时 360s；批量并发必须为 1。

| 项 | 预算/策略 |
|---|---|
| 单张 API 响应 | 不建议同步等待 30–130s；改为 `POST /ocr/jobs` + 轮询/SSE |
| 队列 | 单槽，最多排队 20 张；超过提示“前方 N 张” |
| 图片预处理 | 客户端长边压到 1600–2000px，再上传；大图先缩 |
| 超时 | 服务端 httpx 300s，客户端 360s；超时明确 502/OCR_TIMEOUT |
| 并发 | 严格 1；多用户共享队列，按提交时间 FIFO |
| 结果缓存 | 身份证号 + 图片哈希去重，命中直接返回 |
| 资源 | OCR 常驻 1.2G；训练/大批量导入时暂停 |

## 10. 不达标时的处置顺序

1. **缩略图/分页**：照片列表先上缩略图，列表强制分页。
2. **异步化**：报表 >2s、导入 >15s、OCR、批量打印全部转任务 + 进度。
3. **砍包**：路由分包、按需引入、去掉未用图标/图表库；单 chunk 超 300KB 必须拆。
4. **缓存**：字典 ETag/localStorage；静态 immutable；API 304。
5. **索引/慢 SQL**：`pg_stat_statements` 找 Top 5，补复合索引，禁止 `SELECT *`。
6. **限流**：导入/导出/OCR 并发配额，下载限速。
7. **加带宽**：最后手段；先把 `.1` 的误流量清干净。

## 11. 验收基线（上线前必须实测）

| 场景 | 通过标准 |
|---|---|
| 冷启动登录（清缓存） | 4G 模拟 ≤2.5s 可交互；首屏 ≤300KB gzip |
| 登录 API | p95 ≤500ms，错误码正确 |
| 申请列表 50 行 | p95 ≤120ms，响应 ≤80KB |
| 申请详情 | p95 ≤200ms，响应 ≤200KB |
| 保存申请 | p95 ≤300ms，事务无泄漏 |
| 照片网格 20 张缩略图 | 首屏 ≤1s，单张 ≤30KB，滚动不卡 |
| 照片预览 | 单张 ≤150KB，≤400ms |
| 月报生成 | 2s 内返回或转异步；批量 50 份 ≤2 分钟 |
| Excel 导入 1 万行 | ≤15s，失败明细可下载 |
| 打印闭环 | 入队 ≤80ms；Agent 5s 内认领；结果回写 |
| 备份 | 100GB ≤40 分钟；恢复演练通过 |
| 并发 | 100 个列表请求无 5xx；2 个导入不拖垮 API |
| 内存 | 连续压测 2 小时无 OOM，API 堆 <1.5G |
| PG | 连接 <80，无 >5s 查询 |

## 12. 预算变更规则

- 任何页面/接口超出预算，先在 PR 说明“为什么超、如何降”，不允许默默放宽。
- 首屏 300KB、单 chunk 400KB、API p95 500ms 为硬线；调整需更新本文件并说明依据。
- 新增重任务（报表/导入/OCR/训练）必须声明并发配额与内存预算。
- 每月复核一次 `.1` 带宽、`.2` 磁盘增速、PG 库增速、备份成功率。
