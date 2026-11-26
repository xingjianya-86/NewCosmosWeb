# Android 壳构建与发布

`mobile/` 是 Capacitor 工程（WebView 加载线上 Vue 页面，不打包业务 UI），`deploy/android/` 负责构建、签名与更新清单。

## 目录

```text
mobile/
  capacitor.config.ts        壳配置（appId / appName / server.url / 混合内容）
  package.json               版本号单一来源（version = 2.0.YYYYMMDD）
  www/index.html             仅占位：server.url 生效时不会显示
  android/
    app/src/main/java/cn/sua17/newcosmos/
      MainActivity.java      生命周期、返回键、权限、安全区
      NcWebViewClient.java   外链、错误页、SSL、注入时机
      NcWebChromeClient.java window.open / target=_blank
      NcDownloader.java      DownloadManager + blob 下载 + 通知
      NcUpdateManager.java   latest.json 检查 / SHA256 校验 / 安装
      NcJsBridge.java        window.NcShellNative
    app/src/main/assets/nc-shell/header-inject.js
    app/src/debug/res/xml/network_security_config.xml   仅 Debug 允许明文
deploy/android/
  build-apk.ps1              一键构建
  latest.json.example        更新清单样例
deploy/keys/                 签名（git 忽略，首次构建自动生成）
publish/android/             产物（git 忽略）
```

## 构建

```powershell
# 正式包（默认）：生成/复用 deploy/keys/newcosmos-android.keystore
.\deploy\android\build-apk.ps1

# 调试包（允许 http 联调地址）
.\deploy\android\build-apk.ps1 -Configuration Debug

# 指定测试服务器（仅 Debug 可用 http）
$env:NC_MOBILE_URL = "http://192.168.1.10:5173"
.\deploy\android\build-apk.ps1 -Configuration Debug

# 只构建不复制
.\deploy\android\build-apk.ps1 -NoPublish
```

脚本流程：`npm install` → `npx cap sync android` → `gradlew assemble<Configuration>` → 复制到 `publish/android/` → 生成 `latest.json`（含 SHA256）。

环境要求：Node 22+、JDK 21、Android SDK（platform 36 / build-tools 36）。设置 `ANDROID_HOME`，脚本也会自动探测 `%LOCALAPPDATA%\Android\Sdk`。

产物：

- Release：`publish/android/newcosmos-<versionName>.apk` 与 `latest.json`
- Debug：`publish/android/newcosmos-<versionName>-debug.apk` 与 `latest.debug.json`

## 签名

优先级：脚本参数 > 环境变量 > `deploy/keys/newcosmos-android.keystore`。

```powershell
$env:NC_ANDROID_KEYSTORE      = "D:\keys\newcosmos-android.keystore"
$env:NC_ANDROID_KEYSTORE_PASS = "********"
$env:NC_ANDROID_KEY_ALIAS     = "newcosmos"
$env:NC_ANDROID_KEY_PASS      = "********"   # 默认与 store 密码相同
.\deploy\android\build-apk.ps1 -Configuration Release
```

首次运行且默认 keystore 不存在时，脚本用 `keytool` 生成自签名 RSA-4096（有效期 30 年），随机密码写入 `deploy/keys/newcosmos-android.keystore.pass`。**该文件与 keystore 都必须备份且不得提交**；丢失后无法覆盖安装升级，只能卸载重装。未配置 keystore 时 Release 会回退到 debug 签名（可安装但不适合分发）。

## 客户端标识

- User-Agent 追加 `NewCosmosAndroid/<versionName>`：覆盖文档、API、下载等所有请求，无 CORS 影响。
- 同源请求（`/api/auth` 等）由注入脚本追加 `X-NC-Client: android`、`X-NC-App-Version: <versionName>`。
- 跨域业务 API（`cosmos-static.sua17.cn:20000`）**未**注入这两个头：服务端 CORS 目前只允许 `Authorization / Content-Type / X-NC-Device / X-NC-Device-Key`，新增头会触发预检失败。若服务端把这两个头加入 `WithHeaders(...)` 白名单，可在 `header-inject.js` 中放开跨域主机。

注入不修改 `web/` 源码；页面可通过 `window.NcShell`（`client` / `version` / `checkUpdate()` / `openExternal(url)`）按需调用。

## 应用内更新

1. 启动 4 秒后（以及 `NcShell.checkUpdate()`）请求：
   `https://cosmos-static.sua17.cn:20000/downloads/android/latest.json`
2. `versionCode` 大于当前包时弹窗提示；`notes` 原样展示。
3. 下载 APK 到应用缓存 `updates/`，校验 SHA256、包名与 versionCode。
4. 通过 FileProvider 交给系统安装器；Android 8+ 未授权“安装未知应用”时会引导到设置页，返回后自动继续。

发布新版本：

```powershell
# 1) 修改 mobile/package.json 的 version（末段同时作为 versionCode）
# 2) 构建
.\deploy\android\build-apk.ps1 -Notes "本次更新内容"
# 3) 上传 publish/android/newcosmos-<version>.apk 与 latest.json 到
#    https://cosmos-static.sua17.cn:20000/downloads/android/
```

`latest.json` 字段：

```json
{ "versionCode": 20261126, "versionName": "2.0.20261126",
  "url": "https://cosmos-static.sua17.cn:20000/downloads/android/newcosmos-2.0.20261126.apk",
  "sha256": "<64 位小写 hex>", "notes": "更新说明" }
```

注意：服务端必须允许 APK 的 `Content-Type: application/vnd.android.package-archive`（或通用二进制），并保证 `latest.json` 不被缓存（壳侧每次启动请求，HttpURLConnection 默认可能命中缓存；必要时服务端设置 `Cache-Control: no-store`）。

## 安全区与明文 HTTP

- Capacitor `SystemBars` 使用 `insetsHandling: native`：WebView 整体避开状态栏/导航栏/输入法。业务页 `viewport-fit=cover` 的 `env(safe-area-inset-*)` 不会与原生 padding 叠加。
- Release 使用 `res/xml/network_security_config.xml` 禁止明文；`src/debug` 覆盖为允许明文并信任用户 CA，仅用于联调。`build-apk.ps1` 拒绝 Release + `http://` 服务器地址。
