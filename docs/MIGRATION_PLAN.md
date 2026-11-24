# NewCosmos Web 迁移计划

> 目标：把 NewCosmos（.NET MAUI，桌面 + Android）迁移为 Web 全功能系统，保留 Windows 端 Office/打印代理。
> 拓扑：`.1` 边缘（index + auth 反代），`.2` 应用/数据（API + PostgreSQL + 静态 + OCR + Lottery）。

## 1. 原则

1. **业务单点**：金额、分类、状态机、校验只在 `NewCosmos.Core` 实现；Web/桌面/Agent 不重抄。
2. **数据库契约不变**：`Resources/Schema` YAML 为权威；新增表/列走迁移脚本，不破坏旧桌面端。
3. **Office 只在 Windows**：服务端不跑 COM；所有文书/打印走 `NewCosmos.Agent`。
4. **打印按账户绑定**：`user_id + device_id + printer_name`；任务固化目标，只有对应 Agent 能认领。
5. **性能优先**：`.1` 不承载业务大流量；`.2` 静态资源 100Mbps；API 分页/压缩；照片缩略图。
6. **安全**：业务 API 用 Bearer 会话；`.2` CORS 只允许 `.1`；服务端强制权限/数据范围。

## 2. 项目结构

```text
src/NewCosmos.Core/          领域/模型/常量/Helpers/DB/业务服务
src/NewCosmos.Server/        ASP.NET Core API
src/NewCosmos.Office/        Word/Excel/PDF 引擎（net10.0-windows）
src/NewCosmos.Agent/         Windows 托盘代理（Office/打印）
src/NewCosmos.Shell/         Windows WebView2 壳（同一套 Vue 页面，独立窗口/托盘）
src/NewCosmos.LotteryWorker/ Python/TensorFlow Worker
src/NewCosmos.Desktop/       后续 MAUI（迁移期过渡，不再新增业务 UI）
web/                         Vue 3 + Element Plus
deploy/edge-1/               .1 nginx
deploy/app-2/                .2 nginx/API
deploy/database/             PG 配置/迁移/备份
deploy/agent/                Windows 安装
deploy/lottery/              Worker 容器
```

## 3. 批次

### P0 基础（当前）

- Core 抽取并可编译（已完成）。
- Server：认证/会话/心跳/登出、当前用户上下文、打印机绑定、Agent 注册/心跳、健康检查。
- Web：登录、主框架、首页、打印机绑定页、桌面/手机响应式。
- 部署：`.1`/`.2` nginx、CORS、静态缓存、systemd/Docker。
- Agent/Office：骨架 + 一条 Word 模板渲染/打印。
- Lottery：Python Worker 骨架 + 一次推理冒烟。

### P1 社会救助核心

- 申请五步表单、收入核算、分类判定、审批/工作流、档案查询/详情。
- 权限/数据范围、字典/地区、文件/照片。

### P2 档案/文书/打印

- 档案制作、模板、照片/签名、Office Agent 全量、PDF 预览、批量打印、补打。

### P3 其他业务域

- 高龄、临时救助、追缴、资产核查、银行流水、变更/死亡/近亲属/大学生/能力评估/特殊审批、值班。

### P4 报表/导入

- 月报、公示、分类施保、各类台账导入、银行流水导入、组织导入。

### P5 管理与系统

- 用户/角色/组织、配置/标准/字典/地区/节假日、在线会话、日志、客户端版本、打印设备、数据库管理、备份恢复、彩票、手机端优化。

### P6 Windows WebView2 壳（后置）

- `NewCosmos.Shell`：WPF + WebView2，加载同一套 Vue 页面；
- 独立窗口/桌面图标/托盘/单实例/自动更新；
- Agent 状态与本机下载目录入口；
- 不复制业务 UI、不直连数据库、不跑 Office COM。
- 详见 `docs/WEBVIEW2_SHELL.md`。

## 4. 验收

- `dotnet build NewCosmosWeb.sln` 零错误。
- `cd web && npm run build` 通过，首屏 gzip ≤ 300KB。
- 登录 → 心跳 → 登出；被顶号/过期返回正确错误码。
- 账户绑定打印机 → Agent 心跳 → 绑定列表只显示自己的打印机。
- 一条 Word 模板 → PDF → 打印/回传闭环。
- `.1` 只发 index/auth；`.2` assets/API/照片/备份不经过 `.1`。
