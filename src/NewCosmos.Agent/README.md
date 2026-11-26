# NewCosmos.Agent

Windows 固定 PC 上的托盘/命令行文档与照片打印代理。职责：

- 向服务端注册设备并保存 `deviceKey`
- 每 30 秒上报心跳、机器名、Agent 版本、本机打印机列表
- 探测 Microsoft Office / 本机打印机
- 提供一条真实 Office COM 的模板渲染命令（模板 → PDF）
- 轮询服务端打印/预览/照片任务，在本地渲染 PDF、打开预览或打印，并回写状态
- 托盘宿主：右键绑定打印机、立即心跳、打开日志/输出目录、退出
- 开机自启（HKCU `Run`，可选托盘/纯 worker 模式）

> 项目输出类型为 `OutputType=WinExe`（GUI 子系统）：双击运行不出现控制台窗口。
> 托盘模式不附着控制台；CLI 命令由 `ConsoleHost` 检测并附着父控制台，命令行仍能看到输出。
> Linux 服务端不运行本项目，也不运行 Office COM。

- 目标框架：`net10.0-windows`
- 项目引用：`..\NewCosmos.Office`、`..\NewCosmos.Core`
- 接口契约：`docs/API_CONTRACT.md` §3（Agent 注册/心跳）

## 1. 构建与运行

```powershell
dotnet build src\NewCosmos.Agent\NewCosmos.Agent.csproj
dotnet run --project src\NewCosmos.Agent\NewCosmos.Agent.csproj -- --help
```

发布单文件/自包含示例（WinExe，双击不弹控制台）：

```powershell
dotnet publish src\NewCosmos.Agent\NewCosmos.Agent.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish\agent
```

> 单文件发布仍需要目标机安装 Microsoft Office 桌面版；Office COM 无法随包分发。

### 1.1 无控制台与 CLI 行为

| 启动方式 | 行为 |
|---|---|
| 双击 `NewCosmos.Agent.exe`（无参数） | 进入托盘模式；不创建/不附着控制台，日志写文件，重要状态用托盘气泡提示 |
| 双击后带错误参数 | 用 `MessageBox` 显示错误与用法，不闪黑窗 |
| 从控制台运行 CLI（`--help` / `--version` / `--printers` / `--probe-office` / `--bind-code` / `--register` / `--heartbeat-once` / `--heartbeat` / `--worker` / `--render` / `install-autostart` 等） | `ConsoleHost` 调用 `AttachConsole(父进程)` 并重绑 stdout/stderr，命令行照常显示输出 |
| 控制台已重定向（`> out.txt` / 管道） | 沿用已有 stdout/stderr 句柄，输出写入重定向目标 |
| CLI 无控制台（如计划任务） | 输出暂存，结束/出错时用 `MessageBox` 展示（≤12000 字符） |

- `Console.CancelKeyPress` 只在真正附着控制台时注册；托盘模式没有 Ctrl+C。
- 异常兜底：写文件日志 + 托盘气泡/MessageBox，不依赖 `Console` 是否存在。
- 因为入口是 GUI 子系统，`cmd` 交互式执行 CLI 时可能立即返回提示符；需要等待退出码时用
  `Start-Process ... -Wait` 或 `cmd /c start /wait`。

## 2. 配置

默认配置文件：`%APPDATA%\NewCosmos\agent.json`

```json
{
  "serverBaseUrl": "https://cosmos-static.sua17.cn:20000",
  "registerToken": "",
  "deviceKeyFile": "",
  "heartbeatSeconds": 30,
  "jobPollSeconds": 3,
  "printJobsDirectory": "%TEMP%\\NewCosmos\\PrintJobs",
  "previewKeepCount": 10,
  "machineName": "MZ-01"
}
```

| 配置项 | 说明 | 默认 |
|---|---|---|
| `serverBaseUrl` | 业务 API 根地址（`.2`） | 空，必须配置 |
| `registerToken` | 一次性注册令牌；注册成功后会从配置中清除 | 空 |
| `deviceKeyFile` | 设备密钥文件；空 = 与 `agent.json` 同路径 | `%APPDATA%\NewCosmos\agent.json` |
| `heartbeatSeconds` | 心跳间隔（注册响应可覆盖），5–3600 | `30` |
| `jobPollSeconds` | 任务轮询间隔，2–300 | `3` |
| `printJobsDirectory` | 打印任务临时目录（模板/渲染源/预览 PDF），支持 `%TEMP%` | `%TEMP%\NewCosmos\PrintJobs` |
| `previewKeepCount` | 预览 PDF 保留最近 N 个 | `10` |
| `machineName` | 上报机器名 | `Environment.MachineName` |

优先级（高 → 低）：

1. 命令行 `--server` / `--register-token` / `--machine-name` / `--heartbeat-seconds`
2. 环境变量 `NC_AGENT_SERVER_URL` / `NC_AGENT_REGISTER_TOKEN`
3. `agent.json`
4. 内置默认值

其他环境变量：

- `NC_AGENT_CONFIG`：等同于 `--config <path>`。

**禁止**在源码、脚本、仓库中硬编码密码或注册令牌。推荐部署时由运维注入
`NC_AGENT_REGISTER_TOKEN`，注册完成后清除。

`agent.sample.json` 是脱敏样例。

## 3. 设备注册

### 3.1 首次使用：网页绑定码自动注册（推荐）

在网页「打印机」页生成本机绑定码，然后：

```powershell
NewCosmos.Agent.exe --server https://cosmos-static.sua17.cn:20000 --bind-code 482913
# 托盘模式：右键「绑定本机打印机…」输入 6 位码
```

`--bind-code` 发现本地没有 `deviceKey` 时，会自动调用匿名接口
`POST /api/agent/register-by-code`，一次完成：

1. 注册本机：请求体 `code`、`machineName`、`agentVersion`、`printers[{name,isDefault}]`；
2. 服务端返回 `{ deviceId, deviceKey, heartbeatSeconds, bindings[] }`；
3. 本地保存 `deviceId` / `machineName` / `heartbeatSeconds` / `deviceKey`；
4. 直接使用返回的 `bindings[]`，不再二次调用 `bind-by-code`。

成功后提示「已自动注册并绑定 N 台打印机」。**不再需要** `--register` 或服务器注册令牌。

### 3.2 可选：一次性注册令牌（兼容旧部署）

```powershell
$env:NC_AGENT_SERVER_URL = "https://cosmos-static.sua17.cn:20000"
$env:NC_AGENT_REGISTER_TOKEN = "<向管理员索取的一次性令牌>"

NewCosmos.Agent.exe --register
```

流程：

1. `POST /api/agent/register`，请求头 `X-NC-Agent-Register: <registerToken>`
2. 请求体：`machineName`、`agentVersion`、`printers[{name,isDefault}]`
3. 响应：`{ deviceId, deviceKey, heartbeatSeconds }`
4. 保存 `deviceId` / `heartbeatSeconds` / `deviceKey`

`deviceKey` 的保存方式：

- **首选**：Windows DPAPI（`DataProtectionScope.CurrentUser`），只有当前 Windows 账户能解密；
- **降级**：DPAPI 不可用时写当前用户 ACL 文件（`icacls` 收紧），日志记录警告；
- 密钥永远不会打印到控制台/日志。

> 不落盘 `registerToken`：注册成功后 Agent 主动清空配置中的令牌，避免一次性凭据长期留存。

## 4. 心跳

```powershell
# 持续心跳（默认 30 秒）+ 任务轮询，Ctrl+C 退出（控制台模式）
NewCosmos.Agent.exe --heartbeat

# 只发心跳，便于排障/部署校验
NewCosmos.Agent.exe --heartbeat-once

# 托盘模式（无参数）：托盘图标 + 心跳 + 任务轮询，无控制台
NewCosmos.Agent.exe
NewCosmos.Agent.exe --tray
```

- `POST /api/agent/heartbeat`
- 请求头：`X-NC-Device-Key: <deviceKey>`
- 请求体：`machineName`、`agentVersion`、`printers[{name,isDefault}]`
- 成功：HTTP 204
- 设备不存在/被禁用：HTTP 401 → Agent 打印错误并停止循环，提示重新注册
- 网络/服务端错误：记录日志，按间隔重试

每轮心跳都会重新枚举打印机，现场插拔打印机无需重启 Agent。

## 5. 打印机与 Office 探测

```powershell
NewCosmos.Agent.exe --printers
NewCosmos.Agent.exe --probe-office
```

- 打印机：`System.Drawing.Printing.PrinterSettings`，列出名称并标记 `[默认]`；
  枚举失败只告警并返回空列表，不崩溃。
- Office：`OfficeProviderDetector` 探测 `Word.Application` / `Excel.Application` 与 WPS；
  输出 Provider、ProgID、版本。

### 5.1 绑定本机打印机

托盘右键菜单选择「绑定本机打印机…」，输入服务端生成的 6 位绑定码；
无托盘环境（例如远程排障）也可用命令行：

```powershell
NewCosmos.Agent.exe --bind-code 482913
# 等价写法
NewCosmos.Agent.exe bind-code 482913
```

流程：

1. 校验绑定码必须是 6 位数字，并检查 ServerBaseUrl、本机打印机列表；
2. 读取 `deviceKey`：
   - **有**：调用 `POST /api/printers/bind-by-code`（请求头 `X-NC-Device-Key`，
     请求体 `{ "code": "482913", "printers": [...] }`）；
   - **无**：调用匿名 `POST /api/agent/register-by-code` 自动注册并绑定，
     保存 `deviceId` / `machineName` / `heartbeatSeconds` / `deviceKey`（DPAPI），
     直接采用响应里的 `bindings[]`；
3. 成功返回 `bindings[]`，命令行打印「已自动注册并绑定 N 台打印机」或「已绑定 N 台打印机」；
4. 托盘模式随后补发一次心跳刷新在线状态；首次自动注册后还会启动后台心跳/任务轮询。

常见失败：

- 绑定码格式错误 / 不存在或已被使用（HTTP 404）/ 已过期（HTTP 400）；
- 请求过于频繁（HTTP 429）→ 稍后重试；
- 设备已失效（HTTP 401/403）→ 确认服务端设备状态；必要时清理本地设备密钥后重新绑定；
- 未发现本机打印机 → 检查打印驱动与 Windows 打印服务。

## 6. 模板渲染（真实 Office COM）

```powershell
NewCosmos.Agent.exe --render `
  --template C:\templates\低保审核审批表.docx `
  --data C:\templates\data.json `
  --out C:\out\低保审核审批表.pdf
```

`--data` 可以是 JSON 文件路径，也可以直接传 JSON 文本。

### 6.1 扁平字段

```json
{
  "{户主姓名}": "张三",
  "{家庭住址}": "林口县柳树镇XX村",
  "{家庭人数}": "3"
}
```

### 6.2 带表格行

```json
{
  "fields": {
    "{户主姓名}": "张三",
    "{家庭人数}": "3"
  },
  "tableRows": [
    { "{成员姓名}": "张三", "{与户主关系}": "本人" },
    { "{成员姓名}": "李四", "{与户主关系}": "配偶" }
  ],
  "tableStartMarker": "{成员姓名}",
  "tableEndMarker": "{成员姓名}"
}
```

说明：

- `.docx` 走 `WordEngine`，`.xlsx` 走 `ExcelEngine`；
- `tableRows` 必须同时提供 `tableStartMarker` / `tableEndMarker`（模板表格定位标记）；
- 渲染前先探测 Microsoft Office。**未安装 MS Office 时直接给出明确错误并退出**，
  不会用 WPS 兜底（`WordEngine`/`ExcelEngine` 的 COM 路径只接受 MS Office ProgID）；
- 输出一定是 PDF（`--out` 无 `.pdf` 后缀会自动补上）。

## 6.3 任务队列（worker）

```powershell
# 只做任务轮询（不关心跳）
NewCosmos.Agent.exe --worker

# --heartbeat：心跳 + 任务轮询并行（控制台）
NewCosmos.Agent.exe --heartbeat

# 无参数 / --tray：托盘 + 心跳 + 任务轮询（无控制台）
NewCosmos.Agent.exe
```

轮询流程：

1. `POST /api/agent/jobs/claim`（header `X-NC-Device-Key`，body `{ "max": 1 }`）；
2. `GET /api/agent/jobs/{id}/payload` 取模板 Base64 与字段/表格数据；
3. 模板写入 `%TEMP%\NewCosmos\PrintJobs\{jobNo}.{fileType}`；
4. 用 `WordEngine` / `ExcelEngine` 从文件渲染 PDF（**不访问数据库**）：
   `fields`/`tableRows` 的键是 `FieldKeys`，Agent 按 payload 里的 `templateConfigJson`
   （`fields[].fieldKey/placeholder`、`tables[].columns/startMarker/endMarker`、`indexShifts`）
   映射到模板占位符，与 `PrintExecuteService` 的口径一致；
5. `jobKind == "Preview"` → 写 PDF 并用系统默认阅读器打开，视为完成；
6. `jobKind == "Photo"` → 走照片打印分支（见 6.4），不读模板、不调用 Office；
7. 其他模板任务 → 先渲染源文档与 PDF，再用 Office COM 打印（支持 `printerName` / `copies` / `isDuplex`）；
8. 成功 `POST /complete`；失败 `POST /fail` 并带截断后的错误摘要（≤500 字符）。

失败处理：

- 单个任务失败只回写 `fail`，轮询继续；
- 认领失败（网络/服务端错误）记录日志后按间隔重试；
- 设备被禁用（401/403）时停止任务轮询并退出。

打印降级：Office COM 打印失败时，尝试 ShellExecute `printto`（指定打印机）/`print`
（默认打印机）。降级路径依赖系统已注册的 PDF 阅读器，**无法保证份数与双面**
（由阅读器/打印机默认值决定）。

临时文件：

- 打印成功：模板、渲染源、PDF 全部删除；
- 预览成功：PDF 不立即删除（用户可能还开着），保留最近 `previewKeepCount` 个；
- 任务失败：本地模板/渲染源/PDF 一并清理，避免敏感文书滞留在 `%TEMP%`。
- 日志只记录任务号、模板名、字节数、份数、错误摘要，不写字段值、姓名或身份证。

## 6.4 照片打印（JobKind=Photo）

服务端 payload 字段：

```json
{
  "jobKind": "Photo",
  "photoDataBase64": "...",
  "photoLayout": "FULL_PAGE|HALF_PAGE|ID_CARD",
  "photoCategory": "ID_CARD|...",
  "printerName": "...",
  "copies": 1,
  "applicantName": "...",
  "applicantIdCard": "..."
}
```

执行路径（`JobTaskExecutor` 最前面判断任务类型，**不会**进入模板/Office 路径）：

1. `photoDataBase64` 解码；SkiaSharp 解码 WebP/JPEG/PNG，读取 `SKCodec.EncodedOrigin` 处理 EXIF 方向；
2. 统一转 Bgra8888 位图，按 `PhotoLayoutConstants` 映射版式；
3. `System.Drawing.Printing.PrintDocument` 打印：尊重 `printerName` / `copies`，照片任务固定 `IsDuplex=false`；
4. 成功由 JobWorker `POST /complete`；失败 `POST /fail`（错误摘要 ≤500 字符）；
5. 照片只在内存处理，不写临时文件，因此没有残留需要清理；`applicantName` / `applicantIdCard` 不参与日志。

版式映射（`PhotoLayoutMapping`，唯一来源是 Core `PhotoLayoutConstants`）：

| 版式 | 目标区域 | 缩放 |
|---|---|---|
| `ID_CARD` | 证件原寸包围盒（`GetBox`：242×153 点 ≈ 85.4×54mm，标准身份证 85.6×54mm 的排版取整），页面居中 | 等比装入、不裁剪、不拉伸 |
| `HALF_PAGE` | A4 可打印区上半部分 | 等比装入上半区并居中 |
| `FULL_PAGE`（及未知/空值） | A4 可打印区整块 | 等比缩放铺满可打印区、不裁剪 |

- 版式为空的按类别默认：`ID_CARD` 类别 → 证件原寸，其余 → 整页（`PhotoLayoutConstants.DefaultForCategory`）。
- 照片日志只写任务号、类别、字节数；不写姓名、身份证号。
- 依赖 Windows 打印机队列；未安装指定打印机时任务直接 `fail`，不会退回默认打印机误打。

## 7. 账户绑定说明（部署必读）

- 打印任务按 `user_id + device_id + printer_name` 绑定，任务固化目标，只有对应 Agent 能认领。
- `deviceKey` 用 **DPAPI CurrentUser** 加密，绑定当前 Windows 登录账户：
  - 换账户运行 → 需在网页端重新生成绑定码，再运行 `--bind-code` 自动注册；
  - 把 `%APPDATA%\NewCosmos\agent.json` 拷到另一台机器/另一账户 → 解密失败，属预期。
- Office COM、打印机队列、默认打印机、字体都属于当前 Windows 账户，**不要**用 Windows 服务（Session 0）运行。
- 建议固定 PC 使用专用账户自动登录，只安装业务需要的打印机，并把模板/输出目录授权给该账户。

## 8. 日志与临时文件

| 内容 | 路径 |
|---|---|
| 配置文件 | `%APPDATA%\NewCosmos\agent.json`（可用 `--config` 覆盖） |
| 日志 | `%APPDATA%\NewCosmos\Logs\app_.log`（Serilog 按日滚动） |
| 渲染临时文件 | `%TEMP%\NewCosmos\PrintTemp\` |
| 打印任务临时文件 | `%TEMP%\NewCosmos\PrintJobs\`（可用 `printJobsDirectory` 覆盖） |
| 渲染输出 | `--out` 指定路径 |

日志不会写入 `deviceKey`、`registerToken` 或公文个人敏感字段（打印/渲染日志只记录路径、字节数、错误）。
托盘模式没有控制台输出，日志只写文件；重要状态通过托盘气泡提示。照片任务只在内存中解码/打印，不产生临时图片文件。

## 9. 退出码

| 退出码 | 含义 |
|---|---|
| `0` | 成功 / Ctrl+C 正常退出 |
| `1` | 未处理异常（看日志） |
| `2` | 参数/配置错误（缺 ServerBaseUrl、绑定码格式错误、未发现打印机等） |
| `3` | 服务端 API 失败 / 设备未授权 / 数据文件错误 |
| `4` | 未检测到可用的 Microsoft Office |
| `5` | 渲染失败（COM/模板/PDF 导出） |
| `130` | 用户取消（Ctrl+C） |

## 10. 已实现 / 未实现

已实现：

- 配置读取（文件 + 环境变量 + 命令行覆盖）
- WinExe 无控制台启动；CLI 附着父控制台 / 无控制台 MessageBox 兜底
- 设备注册 + DPAPI 密钥保存 + ACL 降级
- 用网页绑定码自动注册并绑定打印机（无需 `--register` / 注册令牌）
- 心跳循环 / 单次心跳，CLI 模式 Ctrl+C 退出
- 打印机枚举、Office 探测
- 托盘宿主（绑定/心跳/打开日志/输出目录/退出）与开机自启
- 按绑定码绑定本机打印机（托盘菜单 + 命令行）
- 单次模板渲染（Word/Excel → PDF，真实 Office COM）
- 任务队列 claim/payload/complete/fail + 预览/打印执行 + 临时文件清理
- 照片任务（JobKind=Photo）：WebP/JPEG/PNG 解码、EXIF 方向、整页/半页/证件原寸直打
- 批量模板任务（TemplateBatch：预览合并/打印/保存/打开目录）、索引槽分页、打印记录回传

未实现 / 后续：

- 自动更新、安装包签名与升级回滚。
- Windows 服务（Session 0）宿主：Office COM 与打印机队列依赖登录用户会话，不建议也不计划支持。
- 照片云端二次处理（如水印）由服务端/采集端负责，Agent 只按下发字节打印。

## 11. 手工验证清单

- [ ] 双击 `NewCosmos.Agent.exe` 只出现托盘图标，不出现控制台/黑窗。
- [ ] 从控制台运行 `--version`、`--help`、`--printers` 能看到输出；`--help` 重定向到文件也正常。
- [ ] 从资源管理器双击并传错误参数时，弹 `MessageBox` 提示且无控制台窗口。
- [ ] `--printers` 列出真实打印机并正确标记默认打印机。
- [ ] 照片任务三种版式（FULL_PAGE / HALF_PAGE / ID_CARD）各打印一张：方向正确、不裁剪、证件原寸居中。
- [ ] 托盘「绑定本机打印机…」输入 6 位码后可绑定，并补发心跳。
- [ ] 全新机器（无 agent.json / 无 deviceKey）直接输入网页绑定码：自动注册、保存密钥并绑定，无需 `--register`。
- [ ] `--bind-code 482913` 成功打印绑定的打印机名称；过期/已用/频率限制/网络错误有明确提示。
- [ ] `--probe-office` 显示 Word/Excel 可用。
- [ ] `--register` 成功写入 `%APPDATA%\NewCosmos\agent.json`，且文件中没有明文 `deviceKey`（DPAPI 正常时）。
- [ ] `--heartbeat-once` 返回成功；服务端设备列表显示 lastSeen 更新。
- [ ] 停用设备后 `--heartbeat-once` 返回 401 并给出明确提示。
- [ ] `--render` 生成 PDF，中文/表格/分页与 Word 手工打印一致。
- [ ] 换 Windows 账户运行心跳失败并提示重新注册（DPAPI 绑定验证）。
