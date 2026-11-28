# Changelog

本文件是 NewCosmos Web 发布说明的单一来源。
`C:\Projects\publish-all.ps1` 发版时会按版本号抽取对应段落写入 Windows / Android 更新清单；
找不到对应版本段时回退到 `## [未发布]`。

发版前把待发内容写在 `## [未发布]` 下；发版时把标题改成实际版本号（如 `## [2.0.20261127] - 2026-11-27`）。

## [未发布]

### 新增

- 高龄老人年度认证：认证活动、认证记录、停止预览、报表导出。
- 高龄老人信息变更：变更申请、审核流转、列表查询。
- 认证报表 PDF 支持内置中文字体（Noto Sans），不依赖服务器系统字体包。
- 顶栏右上角新增「北斗终端」入口：点击用系统浏览器打开随包发布的终端序列页（`web/public/terminal/index.html`），覆盖浏览器桌面/移动、Windows 壳、Android 壳。
- 新增**客户端下载页** `https://cosmos.sua17.cn/download.html`：网页端 / Windows 桌面壳 / 打印代理 / Android 移动壳 / 独立应用端 Windows 与 Android，**每个软件一个下载按钮**；版本与地址由发布脚本生成同源 `download-data.json`（`.2` 的 `/downloads/` 无 CORS，页面跨域读不到清单），并内置兜底数据。

### 变更

- 统一发布入口改为 `C:\Projects\publish-all.ps1`；发布工具不再执行数据库 SQL。
- 发布工具支持 `.1` / `.2` 独立 SSH 密钥，并自动解析 `C:\Projects` Junction 的真实路径。
- 依赖升级：EPPlus 8.7.3、NPOI 2.8.1、Npgsql 10.0.4、SkiaSharp 3.119.4、PDFsharp 6.5.0；PDFsharp 6.5.0 起改为 AGPL/商业双许可，已按 AGPL 显式加载（`PdfSharpLicenseBootstrap`，本项目 GPL-3.0 与 AGPLv3 兼容）。
- 发布新增**回环下载校验**：Verify 步骤把刚上传的 Windows 安装包 / Android APK 从服务器拉回，与清单、本地文件三方比对 SHA256（`-SkipDownloadVerify` 可跳过）。
- Agent / Shell 改为**单文件压缩发布**（`PublishSingleFile` + `EnableCompressionInSingleFile` + `IncludeNativeLibrariesForSelfExtract`，并去掉 XML 文档），安装包体积显著下降。
- 发布菜单与计划新增「本次涉及的服务器路径」摘要，确认前即可看到会覆盖哪些主机路径。
- Vite 构建显式使用 esbuild 压缩，并移除第三方 license 注释。

### 修复

- 修复从 `C:\Projects` Junction 构建 Vue 时 Vite 报 `index.html` 绝对路径的问题。
- 消除 NuGet 漏洞审计警告 NU1902/NU1903：显式 pin `System.Security.Cryptography.Pkcs` / `System.Security.Cryptography.Xml` 10.0.15、`Microsoft.Build.Tasks.Git` 10.0.404；移除 Agent 多余 PackageReference 消除 NU1510。
- **修复发布脚本漏传静态文件**：原逻辑只把 `index.html` 发到 `.1`、只把 `assets/` 发到 `.2`，导致 `download.html`、`terminal/`、`images/`、`splash/` 线上 404 或被 `.1` 的 SPA 兜底吞成 `index.html`；现改为 `.1` 收根目录所有文件、`.2` 收所有静态资源目录，并逐文件 SHA256 校验。
- **回退 Agent/Shell 单文件压缩**：`PublishSingleFile + EnableCompressionInSingleFile` 反而使安装包变大（Shell 98.6→127.1MB、Agent 49.3→61.8MB，.NET 单文件自带压缩后 lzma2 已压不动），恢复散文件夹 + Inno `lzma2/max`，仅保留 `PublishDocumentationFiles=false`。
- **消除编译警告**：删除 Office 侧重复的 `IArchivePhotoService`（CS0436 ×3，且同名类型分叉会让 Agent 未来触发 CS0433 硬错误）、`HukouType.Normalize` 参数改可空（CS8604）、`RemoteAssistanceRules.Resolve` 调用点补空值（CS8620）；Web 构建现为 **0 警告 0 错误**。

---

## [2.0.20261126] - 2026-11-26

### 新增

- 纳入统一发布入口 `C:\Projects\publish-all.ps1`（构建、上传、校验、可选清理）。

### 变更

- 数据库迁移 SQL 不再随发布工具执行；Schema 改由开发期 `src/NewCosmos.Server/Migrations` 与 `Resources/Schema` 维护。

### 修复

- 版本一致性检查补充 Inno Setup 与 package-lock 根版本校验。
