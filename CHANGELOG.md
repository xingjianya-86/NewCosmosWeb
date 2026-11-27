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

### 变更

- 统一发布入口改为 `C:\Projects\publish-all.ps1`；发布工具不再执行数据库 SQL。
- 发布工具支持 `.1` / `.2` 独立 SSH 密钥，并自动解析 `C:\Projects` Junction 的真实路径。

### 修复

- 修复从 `C:\Projects` Junction 构建 Vue 时 Vite 报 `index.html` 绝对路径的问题。

---

## [2.0.20261126] - 2026-11-26

### 新增

- 纳入统一发布入口 `C:\Projects\publish-all.ps1`（构建、上传、校验、可选清理）。

### 变更

- 数据库迁移 SQL 不再随发布工具执行；Schema 改由开发期 `src/NewCosmos.Server/Migrations` 与 `Resources/Schema` 维护。

### 修复

- 版本一致性检查补充 Inno Setup 与 package-lock 根版本校验。
