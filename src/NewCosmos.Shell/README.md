# NewCosmos.Shell（Windows WebView2 壳）

给 Windows 用户一个“像桌面程序”的入口，页面仍然是同一套 Vue Web。

```text
NewCosmos.Shell.exe
  └─ WPF 窗口 + WebView2 + WinForms 托盘
       加载 https://cosmos.sua17.cn（可配置）
       独立窗口 / 托盘 / 单实例唤醒 / 下载 / 外部链接拦截
       Agent 状态与启停 / 更新 / 不直连 PostgreSQL，不跑 Office COM
```

## 运行

```powershell
dotnet run --project src\NewCosmos.Shell\NewCosmos.Shell.csproj
```

- 默认 prod：`https://cosmos.sua17.cn/`
- 临时切换：`$env:NC_SHELL_URL='http://127.0.0.1:5173/'` 后运行
- 配置文件：`%APPDATA%\NewCosmos\shell.json`（不存在时使用默认值）

```json
{
  "startUrl": "https://cosmos.sua17.cn/",
  "title": "帝皇权杖δ-me13 社会救助管理系统",
  "enableDevTools": false,
  "allowedHosts": ["cosmos.sua17.cn", "cosmos-static.sua17.cn"],
  "minimizeToTray": true,
  "updateManifestUrl": "https://cosmos-static.sua17.cn:20000/downloads/shell-update.json",
  "agentPath": ""
}
```

| 字段 | 说明 |
|---|---|
| `startUrl` | 启动加载的页面；`NC_SHELL_URL` 环境变量优先 |
| `title` | 窗口标题 |
| `enableDevTools` | 生产应为 `false`（同时关闭默认右键菜单） |
| `allowedHosts` | 允许壳内导航的域（含子域），其他链接交给系统浏览器 |
| `minimizeToTray` | `true`（默认）关闭窗口最小化到托盘；`false` 关闭即退出 |
| `updateManifestUrl` | 更新清单地址；空 = 托盘“检查更新”提示未配置 |
| `agentPath` | 可选，显式指定 `NewCosmos.Agent.exe`；空 = 自动查找 |

## 托盘

- 双击托盘图标显示主窗口；关闭窗口默认隐藏到托盘（可配置）。
- 菜单：显示主窗口、Agent 状态、启动/重启 Agent、打开 Agent 日志、打开 Agent 配置、
  打开输出目录、打开下载目录、检查更新、退出。
- 只有托盘“退出”才真正结束进程（`minimizeToTray=false` 时关闭窗口即退出）。

## 单实例

- `Mutex`（`NewCosmos_Shell_SingleInstance`）判定首个实例；
- 第二个实例通过命名 `EventWaitHandle`（`NewCosmos_Shell_Activate`）唤醒已有窗口，
  还原最小化/隐藏状态后静默退出，不弹 MessageBox。

## Agent 联动

Agent 可执行文件查找顺序：

1. `shell.json` 的 `agentPath`
2. `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\NewCosmos.Agent` 命令行中的 exe
3. `%LOCALAPPDATA%\Programs\NewCosmos Agent\NewCosmos.Agent.exe`
4. 壳同目录 `NewCosmos.Agent.exe`
5. 壳目录 `Agent\NewCosmos.Agent.exe`

- 状态：按进程名 `NewCosmos.Agent` 判断运行中/未运行；未运行时“启动 Agent”无参数启动（托盘模式）。
- 日志：`%APPDATA%\NewCosmos\Logs`
- 配置：`%APPDATA%\NewCosmos\agent.json`（支持 `NC_AGENT_CONFIG` 覆盖）
- 输出目录：读取 `agent.json` 的 `outputRoot`；缺省 `%USERPROFILE%\Documents\NewCosmos\输出`
- 下载目录：当前用户 Shell Folders 的 Downloads 已知文件夹

## 更新

清单契约：

```json
{
  "version": "2.0.20261126",
  "url": "https://example.com/downloads/NewCosmos.Shell-2.0.20261126.exe",
  "sha256": "0123456789ABCDEF...",
  "notes": "修复内容",
  "publishedAt": "2026-11-26T10:00:00+08:00"
}
```

- 当前版本取程序集 `AssemblyInformationalVersion`（去掉 `+commit`）；
- 下载到 `%TEMP%\NewCosmos\ShellUpdate`，SHA256 校验通过后启动安装包
  （`/SILENT /NORESTART`），随后可立即退出壳完成安装；
- 清单地址、下载、校验或启动失败均以对话框提示；无新版本也会提示当前版本。

## 已实现

- WebView2 初始化、加载中/错误页、重试、打开浏览器
- 未安装 WebView2 Runtime 时单独提示，并提供
  `https://go.microsoft.com/fwlink/p/?LinkId=2124703` 下载入口
- 托盘、最小化到托盘、托盘退出、双击还原
- Mutex + 命名事件单实例唤醒
- Agent 状态/启动/重启/日志/配置/输出/下载目录
- 在线更新清单检查、SHA256 校验、静默启动安装包
- 外部链接拦截并用系统浏览器打开
- 下载完成后在资源管理器定位文件
- 生产关闭 DevTools/默认右键菜单
- WebView2 用户数据目录：`%LOCALAPPDATA%\NewCosmos\WebView2`

## 后续

- Inno 安装包签名、WebView2 Runtime 由安装器预检
- 可选 `window.chrome.webview.postMessage` 本机桥（不绕过服务端权限）
