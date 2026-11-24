# Windows WebView2 壳（NewCosmos.Shell）

> 定位：给 Windows 用户一个“像原来桌面程序”的入口，但不复制业务 UI。
> 页面仍然是同一套 Vue Web，壳只负责窗口、托盘、单实例、更新和本机能力桥接。

## 1. 为什么做

- 用户习惯桌面图标、独立窗口、任务栏、托盘，不像“打开浏览器”；
- 不维护第二套 MAUI 页面，业务 UI 只有 Vue 一份；
- 与 Rust 的 Tauri 双端思路一致，只是壳换成 C# + WebView2；
- 现有 `NewCosmos.Agent` 继续负责 Office/打印，壳不碰 COM。

## 2. 形态

```text
NewCosmos.Shell.exe（WPF/WinForms + WebView2）
  ├─ 加载 https://cosmos.sua17.cn（可配置 test/prod）
  ├─ 托盘：显示窗口 / 检查 Agent / 打开下载目录 / 退出
  ├─ 单实例：第二次启动唤醒已有窗口
  ├─ 自动更新：复用现有 UpdateService/Inno
  ├─ 本机桥：Agent 状态、打开文件、打印 PDF、设备注册
  └─ 不直连 PostgreSQL，不跑业务 SQL

NewCosmos.Agent.exe（托盘/后台）
  └─ Office COM / Word/Excel/PDF / 打印机 / 任务认领
```

## 3. 技术选型

- .NET 10 WPF（或 WinForms，优先 WPF，窗口/托盘成熟）；
- `Microsoft.Web.WebView2`（Edge Chromium）；
- 自包含发布，不要求装 .NET；WebView2 Runtime 由安装器检测/引导；
- 配置：`%APPDATA%\NewCosmos\shell.json`（服务器地址、环境、启动行为、代理设置）；
- 单实例：Mutex + 唤醒；
- 更新：复用现有 Inno/在线更新清单，壳和 Agent 分开升级。

## 4. WebView2 设置

- 生产关闭 DevTools、状态栏、默认上下文菜单中的“查看源”；
- 限制导航：只允许 `https://cosmos.sua17.cn` 和登录/下载所需域；外部链接用系统浏览器打开；
- 禁止任意新窗口；下载走 WebView2 下载事件，保存到用户下载目录；
- 证书错误不忽略；内网 CA 需提前安装；
- 用户数据目录固定在 `%LOCALAPPDATA%\NewCosmos\WebView2`，退出不清缓存；
- 登录 token 由 Web 页面自己管理（sessionStorage/HttpOnly refresh cookie），壳不读取密码；
- 退出登录时清理 WebView2 会话缓存（可选）。

## 5. 本机桥

壳与 Agent 的边界：

- 壳只展示 Agent 状态（在线/离线/Office 版本/打印机）；
- 文书/打印任务由 Web 页面调 API 入队，Agent 认领执行；
- 壳可提供“打开本机下载目录”“重试启动 Agent”“查看 Agent 日志”；
- 不在壳里实现 Office COM，避免和 Agent 重复。

可选桥接（后续）：

- `window.chrome.webview.postMessage` 让页面查询 Agent 状态；
- 自定义协议 `newcosmos://print/...` 仅做本机提示，不绕过服务端权限。

## 6. 与 MAUI 桌面的关系

- 现有 MAUI 厚客户端（直连 PG）只做迁移期过渡；
- 迁移完成后不再维护完整 MAUI 业务 UI；
- 如果未来需要桌面入口，用 `NewCosmos.Shell`（WebView2），不再做第二套 MAUI 页面；
- 需要离线业务时才考虑本地库，这与瘦客户端目标冲突，另行评估。

## 7. 交付阶段

| 阶段 | 内容 |
|---|---|
| S0 | WPF + WebView2 骨架，加载 prod/test URL，窗口/托盘/单实例/配置 |
| S1 | 登录态、下载、外部链接、错误页、Agent 状态 |
| S2 | Inno 安装包、WebView2 Runtime 检测、自动更新、签名 |
| S3 | 可选本机桥（Agent 状态/日志/下载目录），不做 Office COM |

## 8. 验收

- 双击桌面图标打开独立窗口，页面与浏览器一致；
- 第二个实例唤醒已有窗口；
- 登录/刷新/登出正常，退出后无明文密码；
- Office/打印仍由 Agent 完成，壳不直连数据库；
- 服务器不可达时显示明确错误页，可切换到 test 环境；
- 安装包在无 .NET 的 Win10/11 上可运行（WebView2 Runtime 已装或引导安装）。
