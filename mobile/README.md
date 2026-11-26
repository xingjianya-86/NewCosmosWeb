# NewCosmos Android 壳（Capacitor）

WebView 壳加载线上 Vue 业务页面（`https://cosmos.sua17.cn`），不复制、不重写业务 UI。原生能力与构建说明见 [`../deploy/android/README.md`](../deploy/android/README.md)。

## 本地命令

```powershell
npm install
npx cap sync android
npx cap open android

# 或直接：
..\deploy\android\build-apk.ps1 -Configuration Debug
```

> `android/` 目录已包含壳层原生定制（`MainActivity.java` 等）。**不要**在已有工程上再次执行
> `npx cap add android`，该命令会覆盖原生工程；日常只执行 `npx cap sync android`。


## 测试服务器

```powershell
$env:NC_MOBILE_URL = "http://192.168.1.10:5173"
npx cap sync android
# 之后必须用 Debug 构建（Release 禁止明文）
```

`NC_MOBILE_URL` 在 `cap sync` 时写入 `android/app/src/main/assets/capacitor.config.json`；`NC_MOBILE_UPDATE_URL` 可在构建时覆盖更新清单地址。

## 版本

`package.json` 的 `version` 是唯一来源，末段数字同时作为 Android `versionCode`：

- `2.0.20261126` → versionCode `20261126`
- 每次发布必须递增末段，否则壳内更新不会提示。

## 原生能力一览

| 能力 | 实现 |
| --- | --- |
| 文件选择 / 拍照 | Capacitor `BridgeWebChromeClient.onShowFileChooser` + 相机权限 + `<queries>` |
| 下载 | `NcDownloader`：http(s) → DownloadManager（通知栏）；blob → MediaStore + 通知 |
| 返回键 | `MainActivity.onBackPressed`：优先 `webView.goBack()`，首页退出 |
| 外链 | `NcWebViewClient.shouldOverrideUrlLoading` + `NcWebChromeClient.onCreateWindow` → 系统浏览器 |
| 网络错误页 | `NcErrorView` 原生覆盖层，重试/浏览器打开 |
| 状态栏 / 安全区 | Capacitor SystemBars `insetsHandling: native` |
| 明文控制 | Release 禁止；Debug 资源覆盖允许 |
| 客户端版本头 | UA + 同源 JS 注入（`X-NC-Client` / `X-NC-App-Version`） |
| 应用内更新 | `NcUpdateManager`：latest.json → 缓存 APK → SHA256/包校验 → FileProvider 安装 |
