# ZeroTier 与公文字体 Web 迁移（2026-11-27）

> 口径权威：桌面 `legacy/NewCosmos.Desktop`（`NetworkAccessPage.xaml` + `NetworkAccessViewModel` +
> `Services/System/NetworkAccessService.cs`、`Services/Utilities/FontInstallChecker.cs`，均只读参考）。
> 本文只写 **Web 侧架构、op 契约与差异**，不复述桌面细则。
> 权威代码：Agent `src/NewCosmos.Agent/Services/LocalOpExecutor.cs` / `LocalOpFontOps.cs` /
> `LocalOpZeroTierOps.cs`；服务端 `src/NewCosmos.Server/Endpoints/LocalOpEndpoints.cs`；
> 前端 `web/src/api/localOps.ts`、`web/src/views/system/NetworkAccessView.vue`、
> `web/src/views/system/FontInstallCard.vue`。

## 一、架构：复用打印任务队列的本机操作通道

```text
Web 页面（网络接入 / 设置-公文字体）
  └─ POST /api/devices/{deviceId}/local-ops   { op, args? }     权限 SYSTEM_BACKUP
       └─ 入队 nc_biz_print_jobs（JobKind=LocalOp，target_device_id 固化，无模板）
            └─ Agent JobWorker claim → GET payload（fields = {op, args, requestedByUserId}）
                 └─ JobTaskExecutor → LocalOpExecutor 按 op 分发执行（本机逻辑，仅 Windows Agent）
                      ├─ 成功 → POST /complete { message: "RESULT:" + JSON }
                      └─ 失败 → POST /fail { errorMessage }        ← BusinessException
  └─ 轮询 GET /api/print/jobs（1.5s / 上限 30s）→ 按 id 命中任务
       ├─ Completed → 解析 errorMessage 的 RESULT: 前缀 → 刷新状态/缺失清单
       └─ Failed    → 直接展示 errorMessage（含 CANCELLED、逐条安装原因）
```

- **铁律 5**：Linux 服务端不执行任何本机逻辑——服务端只负责入队/下发/回写，所有文件、注册表、
  进程、UAC 动作都在 Agent（`net10.0-windows`）上。
- **没有新增服务端端点**：结果回传复用既有 complete `message` → `error_message` 字段。
- 通道语义沿用打印队列：10 分钟超时回收、5 次最大重试、软删除、失败重试全部不变。

## 二、op 契约

`op` ≤100 字符、`args` 序列化后 ≤4000 字符（服务端校验）；未知 op → `LOCAL_OP_UNKNOWN` 失败回传。

| op | 作用 | args | 成功回传 `RESULT:` JSON |
|---|---|---|---|
| `font.check` | 扫随包 `公文字体\` 递归 ttf/ttc → `FontFamilyReader` 解析族名 → 与系统已装族名比对 | — | `{op, dirExists, total, missing:[文件名…], omitted?}` |
| `font.install` | 复制到 `%LOCALAPPDATA%\Microsoft\Windows\Fonts`（已存在=已就位只补注册表）→ 写 HKCU Fonts → 广播 `WM_FONTCHANGE`，**免管理员** | — | `{op, installed, total}` |
| `zerotier.status` | `zerotier-cli info` + `listnetworks` | `cliPath?` `tokenPath?` | `{op, installed, nodeId, version, online, tokenAccessDenied, networks:[{nwid,name,status,assignedAddresses}]}` |
| `zerotier.install` | 随包 `Resources\ZeroTier\*.msi` → `msiexec /i … /qn /norestart`（`Verb=runas`）→ 装完复检 | `cliPath?` | `{op, installed:true}` |
| `zerotier.join` | `join <nwid>`；令牌错误先提权 `icacls` 授权再重试；有 `moonId` 补 `orbit`（失败仅告警） | `networkId`(必填) `moonId?` `cliPath?` `tokenPath?` | `{op, nwid, joined:true}` |
| `zerotier.leave` | `leave <nwid>`（二次确认在前端） | `networkId`(必填) `cliPath?` `tokenPath?` | `{op, nwid, left:true}`

约束与安全：

- 所有 `zerotier-cli` 调用统一 `cmd.exe /c` 无窗口 + 重定向 + **20 秒超时**（超时 kill 整棵进程树并回明确错误）；
  提权执行（msiexec / icacls）单独 180 秒——静默安装本身可达数十秒，不能套 CLI 的 20 秒上限。
- `networkId` / `moonId` 强制 **16 位十六进制**，`cliPath` / `tokenPath` 拒绝引号与换行：
  这是把用户输入拼进 `cmd.exe` 命令行时的注入防护。
- 字体目录不存在 → `font.check` 返回 `dirExists:false` 且缺失清单为空（视为无缺失，**不报错**）。
- 字体安装**部分成功**按失败回传：`[FILE_WRITE_ERROR] 已安装 x/n 个公文字体，m 个失败：文件名: 原因；…`。
- 能力上报（心跳 `capabilities`）改为真实探测 + **5 分钟缓存**：`font` = 目录存在且可写，
  `zerotier` = CLI 存在；探测异常不上报该能力。

## 三、结果回传的取舍

| 方案 | 改动 | 结论 |
|---|---|---|
| **完成消息 `RESULT:` + JSON（采用）** | 0 个服务端改动；前端按前缀解析 | 够用：字体缺失清单、ZeroTier 状态四行都是小结构 |
| 新增 `POST /api/agent/jobs/{id}/result` | 服务端端点 + `SessionAuthenticationMiddleware.AnonymousPaths` + Agent 客户端 | 改动面大，暂不需要 |

代价（已知）：完成消息落 `error_message`，服务端截 2000、Agent `CompleteAsync` 截 1000，
故执行器把结果 JSON **主动控制在 900 字符内**（长清单逐条裁剪并记 `omitted`，保证可解析）；
同时 `RESULT:` 原文会出现在通用任务列表（档案输出/补打等面板）的"错误信息"列——后续可按
`jobKind=LocalOp` 过滤或隐藏该前缀消息。

失败一律走既有 fail 通道（`JobWorker.SummarizeError` 截 500），前端直接展示，
包括 UAC 取消回传的 `[CANCELLED] 已取消管理员授权，操作未完成`。

## 四、与桌面版的差异

| 维度 | 桌面（MAUI） | Web（本仓库） |
|---|---|---|
| 执行进程 | 页面 ViewModel 进程内直接调 `INetworkAccessService` / `FontInstallChecker` | 前端下发任务 → **另一台/本台电脑的 Agent** 执行 → 轮询取结果（多一跳队列，延迟约 1.5–3 秒） |
| 配置存放 | `config\network.ini`（`IConfigService` 读写），保存后可"同步数据库地址 + 重启应用" | **浏览器 `localStorage`**（`nc.network-access.config`）：模式/网络ID/Moon/授权地址。服务端 network.ini 属**部署侧**配置，本页不读写 |
| 数据库地址同步 / 配置向导 | `NetworkAccessPage` 内"同步为上方地址"+ 重启；`ConfigWizardPage` 首启向导 | **不迁**：部署侧由脚本/运维处理（见 `PAGE_MATRIX` 配置向导仍不迁） |
| 打开授权页 | `Launcher.OpenAsync(url)`（系统浏览器） | `window.open(url, '_blank', 'noopener')`（浏览器能力，无需 Agent） |
| 权限 | 桌面登录即可操作 | 下发需 `SYSTEM_BACKUP`；轮询任务列表需既有打印类权限 |
| 能力判定 | 本机永远可用 | 设备必须**在线且心跳上报对应能力码**（老 Agent 未上报 → 按钮禁用并提示"无在线 Agent 设备"） |
| UAC 提权 | 页面上直接弹 UAC，取消后本地弹窗 | UAC 弹在 **Agent 所在电脑**上；网页侧只能等任务 fail 后展示 `CANCELLED` 文案（页面提示"取消会以 CANCELLED 回传"） |
| 安装包缺失 | 弹"未找到随包 ZeroTier 安装包" | 任务 fail 回传 `缺少离线安装包，请手动安装`（`Resources\ZeroTier\*.msi` 属未随仓库分发资产） |
| 字体检查时机 | 应用启动时非阻塞查一次（进程内缓存） | 按需点「检测」（每次重新扫描，装完可立刻复检） |
| 字体安装结果 | 弹窗"已安装 n 个…请重启应用与 Word" | 同文案 `ElMessage`；失败展示逐条原因 + 手动安装兜底文案 |

## 五、涉及文件

**Agent（新增 3 个文件 + 2 处既有分支替换）**

- `src/NewCosmos.Agent/Services/LocalOpExecutor.cs`（分发、args 解析、`RESULT:` 组装、能力探测缓存）
- `src/NewCosmos.Agent/Services/LocalOpFontOps.cs`（移植 `FontInstallChecker`）
- `src/NewCosmos.Agent/Services/LocalOpZeroTierOps.cs`（移植 `NetworkAccessService`）
- `JobTaskExecutor.cs` 只把 `LOCAL_OP_EXECUTOR_NOT_IMPLEMENTED` 占位分支换成调用执行器；
  `HeartbeatRunner.cs` 只把静态能力清单换成 `LocalOpCapabilityProbe.Resolve(logger)`。

**前端**

- `web/src/api/localOps.ts`（下发 + 轮询 + `RESULT:` 解析，新文件）
- `web/src/views/system/NetworkAccessView.vue`（新页面 `/system/network-access`）
- `web/src/views/system/FontInstallCard.vue`（新组件，挂在 `/settings`）
- `web/src/router/index.ts`（新路由）、`web/src/layouts/AppLayout.vue`（侧栏「网络接入」入口）、
  `web/src/views/SettingsView.vue`（本机维护入口卡 + 字体卡片）、
  `web/src/views/PrintersView.vue`（设备下拉显示能力标签）

**文档**：`src/NewCosmos.Agent/README.md` §10/§11/§12、`docs/PAGE_MATRIX.md`、本文。

## 六、冒烟路径

1. `dotnet build NewCosmosWeb.slnx` / `dotnet test tests\NewCosmos.Core.Tests\…` / `cd web; npm run build` 三项全绿。
2. 启动 Agent（心跳）→ `GET /api/printers/devices` 的 `capabilities` 出现 `font`/`zerotier`。
3. 系统设置 → 网络接入 → 检测状态：取回四行状态；未装 ZeroTier 时状态行给出安装提示。
4. 设置 → 公文字体 → 检测：列出缺失文件名 → 一键安装 → 成功提示；重启应用后复检无缺失。
5. 接入网络时在 Agent 机器上取消 UAC → 网页弹出 `[CANCELLED] 已取消管理员授权，操作未完成`。
