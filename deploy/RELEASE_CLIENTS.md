# NewCosmos Windows / Android 客户端发布与更新

适用范围：`NewCosmos.Agent`、`NewCosmos.Shell` 的 Windows 安装包，以及 Android APK 的发布清单。

本目录只放**发布基础设施**，不改应用代码：

```text
deploy/
├─ scripts/
│  ├─ new-codesign-cert.ps1      # 生成内部自签名代码签名证书
│  ├─ sign-windows.ps1           # signtool 签名 exe/msi
│  ├─ build-installers.ps1       # dotnet publish + Inno Setup
│  ├─ write-update-manifest.ps1  # 计算 SHA256，生成 latest.json（可选 CMS .sig）
│  └─ check_encoding.ps1         # 检查脚本 UTF-8 BOM
├─ installer/
│  ├─ NewCosmosAgent.iss         # Agent 按用户安装包
│  ├─ NewCosmosShell.iss         # Shell 按用户安装包（默认捆绑 Agent）
│  └─ cache/                     # 放 MicrosoftEdgeWebview2Setup.exe（可选）
├─ keys/                         # 本机 PFX/CER/Android keystore，整个目录不入库
├─ update/
│  └─ latest.json.example        # Windows 更新清单模板
└─ android/
   ├─ build-apk.ps1              # Capacitor/Gradle 构建、签名、生成 Android 清单
   ├─ latest.json.example        # Android 更新清单模板
   └─ README.md                  # Android 客户端构建说明
```

产物：

```text
publish/agent/                    # dotnet publish 输出
publish/shell/
publish/installers/
├─ NewCosmosAgentSetup_<版本>.exe
├─ NewCosmosShellSetup_<版本>.exe
└─ ...（签名后同名覆盖）
```

---

## 1. 前置工具

| 工具 | 用途 | 检查 |
| --- | --- | --- |
| .NET 10 SDK | `dotnet publish` | `dotnet --version` |
| Inno Setup 7 或 6.3+ | 编译 `.iss` | `ISCC.exe` 可被脚本探测到 |
| Windows SDK signtool | Authenticode 签名 | `signtool.exe` 可被脚本探测到 |
| WebView2 Evergreen Bootstrapper | 离线安装 Shell 依赖 | 可选，放 `deploy/installer/cache/` |

本机已安装的 Inno Setup 7 默认路径：

```text
%LOCALAPPDATA%\Programs\Inno Setup 7\ISCC.exe
```

`build-installers.ps1` 的探测顺序：

1. `-IsccPath` 参数
2. `%LOCALAPPDATA%\Programs\Inno Setup 7\ISCC.exe`
3. `%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe`
4. `C:\Program Files\Inno Setup 7\ISCC.exe`
5. `C:\Program Files (x86)\Inno Setup 7\ISCC.exe`
6. `C:\Program Files\Inno Setup 6\ISCC.exe`
7. `C:\Program Files (x86)\Inno Setup 6\ISCC.exe`
8. `PATH` 中的 `ISCC.exe`

> Inno Setup 7.1.1 编译时输出 `Non-commercial use only`。如果 NewCosmos 属于商业发布，
> 请先确认 Inno Setup 的授权，或改用允许商用的 Inno Setup 6 / 其他安装器。

Windows SDK 的 signtool 通常位于：

```text
C:\Program Files (x86)\Windows Kits\10\bin\<SDK 版本>\x64\signtool.exe
```

`sign-windows.ps1` 会自动选择最新 SDK 版本，无需手工配置 PATH。

---

## 2. 自签名代码签名证书

用户目前没有商业代码签名证书，因此使用内部自签名证书。它能让 Windows 识别“发布者”，
但**不会自动获得 SmartScreen 信誉**。

### 2.1 生成

```powershell
$env:NC_CODESIGN_PFX_PASSWORD = '<强密码>'
powershell -File deploy\scripts\new-codesign-cert.ps1
```

输出：

```text
deploy/keys/NewCosmosCodeSign.pfx   # 含私钥，只留在发布机
deploy/keys/NewCosmosCodeSign.cer   # 公钥，分发到客户端
```

要点：

- PFX 密码只从环境变量 `NC_CODESIGN_PFX_PASSWORD` 读取，脚本不落盘、不打印。
- 默认主题 `CN=NewCosmos Internal Code Signing`，有效期 5 年，可用 `-ValidYears` 修改。
- 默认不覆盖已有 PFX/CER；重新生成必须加 `-Force`。
- 整个 `deploy/keys/` 已在 `.gitignore` 中忽略（含 Windows PFX/CER、Android keystore）。
- 私钥丢失后无法再给旧版本补签名；请备份 PFX 到受控的密码库。

### 2.2 客户端信任

每台运行 Agent/Shell 的机器都要导入 `.cer`。**只发 `.cer`，绝不发 `.pfx`。**

当前用户（无需管理员）：

```powershell
Import-Certificate -FilePath .\NewCosmosCodeSign.cer -CertStoreLocation Cert:\CurrentUser\Root
Import-Certificate -FilePath .\NewCosmosCodeSign.cer -CertStoreLocation Cert:\CurrentUser\TrustedPublisher
```

本机所有用户（管理员）：

```powershell
Import-Certificate -FilePath .\NewCosmosCodeSign.cer -CertStoreLocation Cert:\LocalMachine\Root
Import-Certificate -FilePath .\NewCosmosCodeSign.cer -CertStoreLocation Cert:\LocalMachine\TrustedPublisher
```

图形界面：双击 `.cer` -> 安装证书 -> 选择“受信任的根证书颁发机构”；
再导入一次到“受信任的发布者”。

静默部署（运维脚本）：

```powershell
certutil -user -addstore -f Root .\NewCosmosCodeSign.cer
certutil -user -addstore -f TrustedPublisher .\NewCosmosCodeSign.cer
```

验证：

```powershell
Get-AuthenticodeSignature .\NewCosmosAgentSetup_*.exe |
    Format-List Status, StatusMessage, SignerCertificate
```

### 2.3 SmartScreen 说明

- 导入信任后，UAC 的“未知发布者”会消失。
- SmartScreen 仍可能提示“Windows 已保护你的电脑”，因为它还看下载量、文件信誉和签名证书声誉。
- 内部发布可让用户点“更多信息 -> 仍要运行”；对外分发建议购买受信任 CA 的代码签名证书。
- 时间戳很重要：签名时默认使用 `http://timestamp.digicert.com`，证书过期后旧安装包仍可验证。

---

## 3. 构建安装包

### 3.1 一键构建

```powershell
powershell -File deploy\scripts\build-installers.ps1
```

脚本会：

1. `dotnet publish src\NewCosmos.Agent -c Release -r win-x64 --self-contained true -o publish\agent`
2. `dotnet publish src\NewCosmos.Shell -c Release -r win-x64 --self-contained true -o publish\shell`
3. 用探测到的 `ISCC.exe` 编译两个 `.iss`
4. 输出到 `publish\installers\`

### 3.2 构建并签名

```powershell
$env:NC_CODESIGN_PFX_PASSWORD = '<强密码>'
powershell -File deploy\scripts\build-installers.ps1 -Sign
```

`-Sign` 会：

1. 先签 `publish\agent\NewCosmos.Agent.exe`、`publish\shell\NewCosmos.Shell.exe`
   （这样安装包内落地的主程序本身也是已签名文件）
2. 编译安装包
3. 再签 `NewCosmosAgentSetup_*.exe`、`NewCosmosShellSetup_*.exe`

默认 PFX：`deploy\keys\NewCosmosCodeSign.pfx`，可用 `-PfxPath` 覆盖。

### 3.3 常用参数

```powershell
# 指定 ISCC
powershell -File deploy\scripts\build-installers.ps1 `
  -IsccPath 'C:\Users\me\AppData\Local\Programs\Inno Setup 7\ISCC.exe'

# 复用已有 publish 产物（例如只改安装脚本）
powershell -File deploy\scripts\build-installers.ps1 -SkipPublish

# 覆盖显示版本（不改 Directory.Build.props）
powershell -File deploy\scripts\build-installers.ps1 -Version 2.0.20261126
```

如果本机没有 Inno Setup，脚本会打印已探测的路径、`-IsccPath` 示例和下载地址，
并以退出码 `2` 结束，不会只抛一个难以理解的异常。

### 3.4 WebView2 离线包

把 Evergreen Bootstrapper 放到：

```text
deploy/installer/cache/MicrosoftEdgeWebview2Setup.exe
```

下载：<https://go.microsoft.com/fwlink/p/?LinkId=2124703>

- 文件存在：Shell 安装包内置它，检测到目标机缺少 WebView2 时静默执行 `/silent /install`。
- 文件不存在：安装包仍可编译；安装时弹窗提示下载地址，并询问是否继续。

---

## 4. 安装包布局与行为

### 4.1 Agent

```text
%LOCALAPPDATA%\Programs\NewCosmos Agent\
├─ NewCosmos.Agent.exe
└─ ...（self-contained 运行时与依赖）

%APPDATA%\NewCosmos\agent.json          # 首次注册后生成，卸载不删除
%APPDATA%\NewCosmos\*.key / DPAPI 数据  # 设备密钥，卸载不删除
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
└─ NewCosmos.Agent = "<安装目录>\NewCosmos.Agent.exe"
```

- 默认按用户安装，`PrivilegesRequired=lowest`，不需要管理员。
- 可选任务“开机自动启动”：写入 HKCU Run；卸载时由 `uninsdeletevalue` 删除。
- 安装完成后可勾选“启动 NewCosmos Agent 托盘”。
- 卸载不删除 `%APPDATA%\NewCosmos` 下的配置和设备身份；重装后仍可沿用。

### 4.2 Shell

```text
%LOCALAPPDATA%\Programs\NewCosmos Shell\
├─ NewCosmos.Shell.exe
├─ ...（WPF / WebView2 依赖）
└─ Agent\
   ├─ NewCosmos.Agent.exe
   └─ ...（默认捆绑）

%LOCALAPPDATA%\NewCosmos\WebView2\   # Shell 的 WebView2 用户数据
```

- 默认按用户安装，`PrivilegesRequired=lowest`。
- 任务“安装打印代理（NewCosmos Agent）”默认勾选，把 Agent 装到 `{app}\Agent`。
- 如果机器上已有独立安装的 Agent 自启项，Shell **不会覆盖**它（`AgentAutostartNeedsWrite` 检查），
  也不会自动启动捆绑 Agent，避免两个 Agent 同时处理打印任务。
- 没有独立 Agent 自启项时，安装 Shell 会写入：
  `HKCU\...\Run\NewCosmos.Agent = "<Shell 目录>\Agent\NewCosmos.Agent.exe"`。
- 快捷方式：开始菜单 `NewCosmos\NewCosmos Shell`、可选桌面快捷方式；卸载入口在开始菜单。
- 卸载会删除 `{app}\Agent`，并删除本次安装写入的自启项；WebView2/用户配置不在卸载范围。

### 4.3 WebView2 检测

Shell 安装时检查以下注册表位置的 `pv` 值：

```text
HKLM\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}
HKLM\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}
HKCU\Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}
```

缺失时优先静默安装本地缓存的 Bootstrapper；仍失败则提示下载地址并让用户选择是否继续。

---

## 5. 单独签名

```powershell
$env:NC_CODESIGN_PFX_PASSWORD = '<强密码>'

powershell -File deploy\scripts\sign-windows.ps1 `
  -Path .\publish\installers
```

参数：

| 参数 | 默认 | 说明 |
| --- | --- | --- |
| `-Path` | 必填 | 文件或目录；目录递归收集 `*.exe` / `*.msi` |
| `-PfxPath` | `deploy\keys\NewCosmosCodeSign.pfx` | 代码签名 PFX |
| `-PfxPasswordEnv` | `NC_CODESIGN_PFX_PASSWORD` | 密码环境变量名 |
| `-TimestampUrl` | `http://timestamp.digicert.com` | 时间戳服务；空字符串表示不时间戳 |

退出码：

```text
0  成功
2  参数/文件错误
3  找不到 signtool.exe
4  PFX 或密码不可用
5  signtool 签名失败
6  签名后校验失败
```

签名后脚本会用 `Get-AuthenticodeSignature` 检查签名者证书。自签名证书在未导入信任的
发布机上通常显示 `NotTrusted` 或 `UnknownError`，这是正常的；只要不是 `NotSigned` /
`HashMismatch` 即可。导入 CER 到 Root + TrustedPublisher 后会变为 `Valid`。

---

## 6. Windows 更新清单

### 6.1 生成 latest.json

```powershell
powershell -File deploy\scripts\write-update-manifest.ps1 `
  -InstallerPath .\publish\installers\NewCosmosShellSetup_2.0.20261124.exe `
  -Version 2.0.20261124 `
  -Notes '修复打印任务重试；更新 Shell 启动页。'
```

默认输出 `deploy\update\latest.json`，UTF-8 无 BOM：

```json
{
  "version": "2.0.20261124",
  "url": "https://cosmos-static.sua17.cn:20000/downloads/windows/NewCosmosShellSetup_2.0.20261124.exe",
  "sha256": "...",
  "notes": "修复打印任务重试；更新 Shell 启动页。",
  "publishedAt": "2026-11-26T12:00:00+08:00"
}
```

- `url` 默认由 `-BaseUrl` + 文件名生成，也可用 `-Url` 覆盖。
- `publishedAt` 默认当前时间，带本地时区偏移。
- `notes` 可含中文和换行。

### 6.2 可选：给清单做分离签名

```powershell
$env:NC_CODESIGN_PFX_PASSWORD = '<强密码>'
powershell -File deploy\scripts\write-update-manifest.ps1 `
  -InstallerPath .\publish\installers\NewCosmosShellSetup_2.0.20261124.exe `
  -Version 2.0.20261124 -Sign
```

生成 `latest.json.sig`（CMS/PKCS#7 分离签名，DER 编码）。分发时两个文件必须成对上传。

客户端/运维验签（只需 .cer）：

```powershell
Add-Type -AssemblyName System.Security
$content = [IO.File]::ReadAllBytes('.\latest.json')
$signature = [IO.File]::ReadAllBytes('.\latest.json.sig')
$contentInfo = New-Object System.Security.Cryptography.Pkcs.ContentInfo -ArgumentList (,$content)
$cms = New-Object System.Security.Cryptography.Pkcs.SignedCms -ArgumentList $contentInfo, $true
$cms.Decode($signature)
$cms.CheckSignature($true)
```

`CheckSignature($true)` 只验证签名本身；证书链是否受信任由客户端按自己的策略另查。

> Shell 托盘菜单已内置“检查更新”（`src/NewCosmos.Shell/Services/UpdateService.cs`）：
> - 读取 `%APPDATA%\NewCosmos\shell.json` 的 `updateManifestUrl`；未配置时只提示，不会自动升级。
> - 示例：`{ "updateManifestUrl": "https://cosmos-static.sua17.cn:20000/downloads/windows/latest.json" }`
> - 比较清单 `version` 与当前 `InformationalVersion`；下载后校验 SHA256，再以
>   `/SILENT /NORESTART` 启动 Inno 安装包。
> - Agent 没有独立更新检查；Windows 端通常由 Shell 托盘更新（安装包已捆绑 Agent）
>   或运维直接覆盖安装。

---

## 7. Android 更新清单

Android 构建/签名/清单由 `deploy/android/build-apk.ps1` 负责，模板见
`deploy/android/latest.json.example`：

```json
{
  "versionCode": 20261126,
  "versionName": "2.0.20261126",
  "url": "https://cosmos-static.sua17.cn:20000/downloads/android/newcosmos-2.0.20261126.apk",
  "sha256": "...",
  "notes": "修复已知问题并优化移动端体验。"
}
```

构建：

```powershell
# Release（默认读取/生成 deploy\keys\newcosmos-android.keystore）
.\deploy\android\build-apk.ps1 -Notes "本次更新说明"

# 本地调试（允许 http，Debug 签名）
.\deploy\android\build-apk.ps1 -Configuration Debug
```

脚本会 `npm install`、`npx cap sync android`、`gradlew assembleRelease`，
输出 `publish/android/newcosmos-<versionName>.apk` 和 `latest.json`（Debug 为
`latest.debug.json`），并自动计算 SHA256。

约定：

- `versionCode` 取 `mobile/package.json` 版本号最后一段数字（例如 `2.0.20261126` -> `20261126`），
  必须单调递增。
- `versionName` 给用户看，建议与 Windows 版本号对齐。
- APK 必须用 Android keystore 签名（不要用 Windows 的 PFX）；升级包必须与已安装 APK 同签名。
- 默认 keystore 在 `deploy\keys\newcosmos-android.keystore`，首次构建自动生成，
  密码写入同目录 `.pass`。这两个文件都已被 `deploy/.gitignore` 忽略，**必须单独备份**；
  丢失后无法覆盖升级已安装的 APK。
- 生产环境可用 `NC_ANDROID_KEYSTORE` / `NC_ANDROID_KEYSTORE_PASS` /
  `NC_ANDROID_KEY_ALIAS` / `NC_ANDROID_KEY_PASS` 指定受控密钥。
- 发布前可校验：

```bash
apksigner verify --print-certs publish/android/newcosmos-2.0.20261126.apk
sha256sum publish/android/newcosmos-2.0.20261126.apk
```

- Android 无法像 Windows 桌面那样静默覆盖安装。客户端 `NcUpdateManager`
  下载后校验 SHA256，再通过 `FileProvider` / `ACTION_VIEW` 拉起系统安装器；
  用户需允许“安装未知应用”。MDM/Device Owner 场景才可静默安装。
- 服务器只提供 APK 和清单，不参与签名校验；SHA256 校验在客户端做。

---

## 8. 发布到 `.2`

目标目录（与现有 nginx `location ^~ /downloads/` 对应）：

```text
/www/wwwroot/cosmos-static.sua17.cn/downloads/windows/
/www/wwwroot/cosmos-static.sua17.cn/downloads/android/
```

从发布机上传：

```powershell
scp .\publish\installers\NewCosmosShellSetup_2.0.20261124.exe `
    root@10.110.0.2:/www/wwwroot/cosmos-static.sua17.cn/downloads/windows/

scp .\publish\installers\NewCosmosAgentSetup_2.0.20261124.exe `
    root@10.110.0.2:/www/wwwroot/cosmos-static.sua17.cn/downloads/windows/

scp .\deploy\update\latest.json `
    root@10.110.0.2:/www/wwwroot/cosmos-static.sua17.cn/downloads/windows/latest.json
```

如果启用了清单签名，再上传 `latest.json.sig`。

在 `.2` 上修正权限并校验：

```bash
cd /www/wwwroot/cosmos-static.sua17.cn/downloads/windows
chown www-data:www-data *
chmod 0644 *
sha256sum NewCosmosShellSetup_2.0.20261124.exe
ls -lh
```

公网验证：

```bash
curl -sSI https://cosmos-static.sua17.cn:20000/downloads/windows/latest.json
curl -fsS  https://cosmos-static.sua17.cn:20000/downloads/windows/latest.json
curl -sSI https://cosmos-static.sua17.cn:20000/downloads/windows/NewCosmosShellSetup_2.0.20261124.exe
```

注意：

- 现有 nginx 对 `/downloads/` 设置 `Cache-Control: public, max-age=3600`。
  客户端检查 `latest.json` 时建议加时间戳查询参数，例如
  `latest.json?t=20261126120000`，避免最多 1 小时的缓存延迟。
- `.2` 下载限速 8MiB/s/IP、每 IP 4 并发，大安装包上传/下载属正常。
- 不要把 PFX、`server.env`、数据库备份放进 `/downloads/`。

---

## 9. 升级流程

### 9.1 Agent（Windows）

1. 修改版本：`Directory.Build.props` 的 `Version` / `InformationalVersion`，并同步
   `src/NewCosmos.Agent/Services/AgentRuntime.cs` 的版本兜底（`deploy/check_versions.ps1` 会检查）。
2. 构建并签名：

   ```powershell
   $env:NC_CODESIGN_PFX_PASSWORD = '<强密码>'
   powershell -File deploy\scripts\build-installers.ps1 -Sign
   ```

3. 生成 Windows 清单。**建议指向 Shell 安装包**（内含 Agent），这样 Shell 托盘的
   “检查更新”能同时更新 Shell 和 Agent：

   ```powershell
   powershell -File deploy\scripts\write-update-manifest.ps1 `
     -InstallerPath .\publish\installers\NewCosmosShellSetup_<版本>.exe `
     -Version <版本> -Notes '...' -Sign
   ```

   Agent-only 机器可另生成指向 `NewCosmosAgentSetup_<版本>.exe` 的清单，或直接覆盖安装。

4. 上传安装包 + `latest.json` + `latest.json.sig`。
5. 终端升级：运行新安装包，安装到同一 `%LOCALAPPDATA%` 目录，覆盖 exe/依赖；
   `%APPDATA%\NewCosmos\agent.json`、设备密钥和 HKCU 自启项保留。
   Agent 没有独立更新检查；同机 Shell 配置了 `updateManifestUrl` 时，可用托盘
   “检查更新”走 Shell+Agent 一起升级。
6. 静默升级（运维脚本）：

   ```powershell
   .\NewCosmosAgentSetup_<版本>.exe /SILENT /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS
   ```

   升级后可用 `NewCosmos.Agent.exe --version` 和
   `Get-AuthenticodeSignature` 校验。

### 9.2 Shell（Windows）

1. 构建 Shell 安装包（默认把 Agent 一起打进去）。
2. 安装/升级到同一 `%LOCALAPPDATA%\Programs\NewCosmos Shell`。
3. 如果该机器已有独立 Agent：
   - 交互安装时取消勾选“安装打印代理”，避免重复安装；
   - 静默升级时传 `/TASKS=""`（或只保留需要的任务），例如：

     ```powershell
     .\NewCosmosShellSetup_<版本>.exe /SILENT /TASKS="" /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS
     ```

4. 在 `%APPDATA%\NewCosmos\shell.json` 配置更新清单（可选）：

   ```json
   {
     "updateManifestUrl": "https://cosmos-static.sua17.cn:20000/downloads/windows/latest.json"
   }
   ```

   托盘右键 -> “检查更新”会下载 `latest.json` 指向的安装包，校验 SHA256，
   再以 `/SILENT /NORESTART` 启动安装器。未配置时只提示，不会自动升级。
5. Shell 的 WebView2 用户数据在 `%LOCALAPPDATA%\NewCosmos\WebView2`，升级不清理。
6. 若目标机没有 WebView2，先安装 `MicrosoftEdgeWebview2Setup.exe`，或使用内置缓存的安装包。

### 9.3 Android

1. 修改 `mobile/package.json` 的 `version`（`2.0.YYYYMMDD`）；最后一段就是 `versionCode`。
2. 构建并签名（保证与已安装 APK 同 keystore）：

   ```powershell
   .\deploy\android\build-apk.ps1 -Notes "本次更新说明"
   ```

3. 校验产物：

   ```bash
   apksigner verify --print-certs publish/android/newcosmos-<version>.apk
   sha256sum publish/android/newcosmos-<version>.apk
   ```

4. 上传 APK 和 `publish/android/latest.json` 到 `/downloads/android/`。
5. 客户端 `NcUpdateManager` 对比 `versionCode`，下载后校验 SHA256，再通过
   `FileProvider` 拉起系统安装器。
6. `deploy/keys/newcosmos-android.keystore` 与 `.pass` 必须单独备份；
   keystore 丢失后无法再发布可覆盖升级的 APK。

---

## 10. 回滚

- **Windows 安装包**：保留上一版 exe 和对应 `latest.json`。回滚时把旧 exe 与旧清单重新上传，
  终端运行旧安装包覆盖安装即可（AppId 相同，配置/设备身份保留）。
- **清单**：`latest.json` 写回上一版版本号和 URL；如果客户端有本地缓存，带新的 `?t=` 查询参数。
- **Android**：Android 不允许降级安装（`versionCode` 更低会被系统拒绝）。紧急回滚需要发布一个
  `versionCode` 更高的修复包，或让用户卸载后重装旧 APK（会丢本地数据）。
- **签名证书**：不要删除旧 PFX；旧安装包验签和回滚都可能需要它。

---

## 11. 发布前检查清单

```text
[ ] deploy\scripts\check_encoding.ps1 通过
[ ] build-installers.ps1 退出码 0，两个 Setup exe 均生成
[ ] -Sign 时 Get-AuthenticodeSignature 有 SignerCertificate
[ ] 安装包在干净 Windows 10/11 用户上可安装、可启动、可卸载
[ ] Agent 自启项写入/删除正确（HKCU Run）
[ ] Shell 在无 WebView2 的机器上有明确引导
[ ] latest.json 的 version/url/sha256 与实际文件一致
[ ] 上传后 curl 能下载，sha256sum 与清单一致
[ ] 保留上一版安装包和清单，便于回滚
```

---

## 12. 已知限制

- Shell 只有托盘菜单“检查更新”，不是后台自动轮询；Agent 没有独立更新检查。
  `latest.json` 同时可供 Web 页面提示或其他客户端复用。
- 自签名证书需要逐台导入信任，且不能消除 SmartScreen 的信誉提示。
- Inno Setup 7.1.1 编译输出提示 `Non-commercial use only`，商用前需确认授权。
- Shell 安装包默认捆绑 Agent；已单独部署 Agent 的机器应在安装时取消该任务，避免重复。
- 安装包未签名卸载器（Inno 内嵌 `unins000.exe`）；如需连卸载器一起签名，可在 Inno 中配置
  `SignTool` / `SignedUninstaller`，并复用 `sign-windows.ps1` 的证书。
