# NewCosmos.Agent

Windows 固定 PC 上的文档/打印代理（P0 控制台版）。职责：

- 向服务端注册设备并保存 `deviceKey`
- 每 30 秒上报心跳、机器名、Agent 版本、本机打印机列表
- 探测 Microsoft Office / 本机打印机
- 提供一条真实 Office COM 的模板渲染命令（模板 → PDF）
- 轮询服务端打印/预览任务，在本地渲染 PDF、打开预览或打印，并回写状态

> 后续可在此项目上加托盘/Windows 服务宿主；当前是 `OutputType=Exe` 的控制台程序。
> Linux 服务端不运行本项目，也不运行 Office COM。

- 目标框架：`net10.0-windows`
- 项目引用：`..\NewCosmos.Office`、`..\NewCosmos.Core`
- 接口契约：`docs/API_CONTRACT.md` §3（Agent 注册/心跳）

## 1. 构建与运行

```powershell
dotnet build src\NewCosmos.Agent\NewCosmos.Agent.csproj
dotnet run --project src\NewCosmos.Agent\NewCosmos.Agent.csproj -- --help
```

发布单文件/自包含示例：

```powershell
dotnet publish src\NewCosmos.Agent\NewCosmos.Agent.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish\agent
```

> 单文件发布仍需要目标机安装 Microsoft Office 桌面版；Office COM 无法随包分发。

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
# 持续心跳（默认 30 秒）+ 任务轮询，Ctrl+C 退出
NewCosmos.Agent.exe

# 只发心跳，便于排障/部署校验
NewCosmos.Agent.exe --heartbeat-once
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

# 缺省命令 / --heartbeat：心跳 + 任务轮询并行
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
   其他任务 → 先渲染源文档与 PDF，再用 Office COM 打印（支持 `printerName` / `copies` / `isDuplex`）；
6. 成功 `POST /complete`；失败 `POST /fail` 并带截断后的错误摘要。

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

## 7. 账户绑定说明（部署必读）

- 打印任务按 `user_id + device_id + printer_name` 绑定，任务固化目标，只有对应 Agent 能认领。
- `deviceKey` 用 **DPAPI CurrentUser** 加密，绑定当前 Windows 登录账户：
  - 换账户运行 → 必须重新 `--register`；
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

## 9. 退出码

| 退出码 | 含义 |
|---|---|
| `0` | 成功 / Ctrl+C 正常退出 |
| `1` | 未处理异常（看日志） |
| `2` | 参数/配置错误（缺 ServerBaseUrl、缺注册令牌、未注册等） |
| `3` | 服务端 API 失败 / 设备未授权 / 数据文件错误 |
| `4` | 未检测到可用的 Microsoft Office |
| `5` | 渲染失败（COM/模板/PDF 导出） |
| `130` | 用户取消（Ctrl+C） |

## 10. 已实现 / 未实现

已实现：

- 配置读取（文件 + 环境变量 + 命令行覆盖）
- 设备注册 + DPAPI 密钥保存 + ACL 降级
- 心跳循环 / 单次心跳，Ctrl+C 退出
- 打印机枚举、Office 探测
- 单次模板渲染（Word/Excel → PDF，真实 Office COM）
- 任务队列 claim/payload/complete/fail + 预览/打印执行 + 临时文件清理

未实现（预留接口 + TODO）：

- 托盘/开机自启/自动更新：后续在 Agent 宿主层补齐。
- 照片任务（JobKind=Photo）：`/api/agent/jobs/payload` 当前只下发模板，Agent 会以明确错误回写失败。
- 索引槽分页：当 `tableRows` 数量超过模板 `FAMILY_MEMBER_NAME_1..N` 槽位容量时，
  Agent 只填充可用槽位（legacy `PrintExecuteService` 会分页并合并 PDF）；当前服务端仅支持
  FamilyApplication，家庭人数通常低于模板槽位。表格类模板（`tables[]`）不受此限制。
- 批量打印、补打、打印记录回传：依赖服务端任务 API 后续扩展。

## 11. 手工验证清单

- [ ] `--version`、`--help` 正常。
- [ ] `--printers` 列出真实打印机并正确标记默认打印机。
- [ ] `--probe-office` 显示 Word/Excel 可用。
- [ ] `--register` 成功写入 `%APPDATA%\NewCosmos\agent.json`，且文件中没有明文 `deviceKey`（DPAPI 正常时）。
- [ ] `--heartbeat-once` 返回成功；服务端设备列表显示 lastSeen 更新。
- [ ] 停用设备后 `--heartbeat-once` 返回 401 并给出明确提示。
- [ ] `--render` 生成 PDF，中文/表格/分页与 Word 手工打印一致。
- [ ] 换 Windows 账户运行心跳失败并提示重新注册（DPAPI 绑定验证）。
