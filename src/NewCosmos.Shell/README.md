# NewCosmos.Shell（Windows WebView2 壳）

给 Windows 用户一个“像桌面程序”的入口，页面仍然是同一套 Vue Web。

```text
NewCosmos.Shell.exe
  └─ WPF 窗口 + WebView2
       加载 https://cosmos.sua17.cn（可配置）
       独立窗口 / 单实例 / 下载 / 外部链接拦截
       不直连 PostgreSQL，不跑 Office COM
```

## 运行

```powershell
dotnet run --project src\NewCosmos.Shell\NewCosmos.Shell.csproj
```

- 默认 prod：`https://cosmos.sua17.cn/`
- 临时切换：`$env:NC_SHELL_URL='http://127.0.0.1:5173/'` 后运行
- 配置文件：`%APPDATA%\NewCosmos\shell.json`

```json
{
  "startUrl": "https://cosmos.sua17.cn/",
  "title": "帝皇权杖δ-me13 社会救助管理系统",
  "enableDevTools": false,
  "allowedHosts": ["cosmos.sua17.cn", "cosmos-static.sua17.cn"]
}
```

## 已实现

- WebView2 初始化、加载中/错误页、重试、打开浏览器
- 单实例 Mutex
- 外部链接拦截并用系统浏览器打开
- 下载完成后在资源管理器定位文件
- 生产关闭 DevTools/默认右键菜单
- WebView2 用户数据目录：`%LOCALAPPDATA%\NewCosmos\WebView2`

## 后续

- 托盘图标/最小化到托盘
- Agent 状态/日志/下载目录入口
- Inno 安装包 + WebView2 Runtime 检测 + 自动更新 + 签名
- 可选 `window.chrome.webview.postMessage` 本机桥（不绕过服务端权限）
