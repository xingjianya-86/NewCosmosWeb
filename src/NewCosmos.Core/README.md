# NewCosmos.Core

`net10.0` 领域核心，供 Web 服务端、Windows Agent、后续 MAUI 桌面共用。

## 已迁入

- `Constants/`：错误码、字段键、业务常量、权限码
- `Models/`：实体、请求、结果、选项、Schema
- `Helpers/`：金额/身份证/地址/照片/模板字段等纯逻辑
- `Services/Database/`：`IDatabaseService` + Npgsql 实现（AsyncLocal 事务）
- `Services/StateMachine/`：申请状态机
- `Services/Domain/`：社会救助、资产核查、高龄、临时救助、追缴、变更、近亲属等
- `Services/UserManagement/`：权限、角色、数据范围
- `Services/System/`：字典、地区、标准配置、节假日
- `Services/Import/`：Excel 台账导入
- `Services/Core/`：Result/BaseService/Logger/DataMasker/CurrentUserContext/SessionGuard
- `Services/Platform/`：OCR/AI HTTP 客户端接口与实现

## 暂未迁入（后续批次）

- `Services/Templates/`：Word/Excel/COM → `NewCosmos.Office`
- `Services/Domain/Printing/`：打印队列/执行 → `NewCosmos.Office`
- `Services/Domain/ArchiveManagement/`：档案制作/文书 → 依赖 Office，后续拆分
- `Services/Domain/Reporting/PrintService`：打印 → Office
- `Services/Lottery/`：Python 运行时 → `NewCosmos.LotteryWorker`
- `ConfigService`/`LoggerService` 的桌面实现：服务端用 `NewCosmos.Server` 的配置/日志适配
- `ViewModels/`、`Pages/`、`Controls/`、`Converters/`：UI 层不迁入 Core

## 约定

- 不引用 MAUI / WinUI / WebView2 / System.Management / Office COM。
- 不直接读 `App.Current*`；用 `CurrentUserContext`（请求级 AsyncLocal）。
- 金额/分类/状态机单点实现，禁止在 Web/Agent 重抄。
- 新增业务代码先判断是否平台无关；平台相关放 Server/Office/Agent/Desktop。
