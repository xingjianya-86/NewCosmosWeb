# 数据库维护

> 2026-11-27 起，**一键发布不再执行数据库 SQL**。
> 本目录只保留备份工具；Schema 结构在开发期随代码维护。

## 1. Schema 权威来源

| 内容 | 位置 |
|---|---|
| 表 / 列 / 索引定义 | `src/NewCosmos.Server/Resources/Schema/**/*.yaml` |
| 随服务端启动执行的增量迁移 | `src/NewCosmos.Server/Migrations/*.sql` |
| 业务口径与迁移说明 | `docs/MIGRATION_LOG.md`、`docs/SCHEMA_RECONCILIATION.md` |

开发期新增表/列时，优先改 Schema YAML；需要一次性数据修正时，在
`src/NewCosmos.Server/Migrations/` 下按序号新增 SQL，并确认
`NewCosmos.Server.csproj` 会把它复制到发布目录（当前是逐条显式列举，
新增 .sql 必须同步补一条 `None Update` 条目，否则不会进发布目录）。

`NC_AUTO_MIGRATE=true` 时服务端启动会按文件名顺序执行 `Migrations/*.sql`；
生产环境迁移完成后请改回 `false`。

当前迁移清单（按执行顺序，均幂等、可重复执行）：

| 序号 | 文件 | 内容 |
|---|---|---|
| 001 | `001_printer_binding.sql` | 打印机绑定（按 `user_id + device_id + printer_name` 绑定本机打印机） |
| 002 | `002_elderly_certification.sql` | 高龄认证（认证记录相关表/列） |
| 003 | `003_elderly_info_change.sql` | 高龄信息变更（变更记录相关表/列） |
| 004 | `004_device_capabilities.sql` | 设备能力列（`nc_sys_print_devices.capabilities_json`，Agent 心跳能力上报） |
| 005 | `005_ai_judgment_cache.sql` | AI 判定缓存表（重病判定结果缓存） |
| 006 | `006_remote_assistance.sql` | 异地协助轻流程分支（`application_type` 列） |
| 007 | `007_dict_value_cleanup.sql` | 字典字段值统一：历史中文脏值 → `nc_dict_items.item_key`（幂等 UPDATE） |
| 008 | `008_relationship_cleanup.sql` | 家庭关系列统一：历史中文关系值 → `FamilyRelationships.item_key`（幂等 UPDATE） |
| 009 | `009_other_difficulty_relationship_cleanup.sql` | 其他困难台账人员表关系列统一 → `FamilyRelationships.item_key`（幂等 UPDATE） |

首次部署沿用 `NC_AUTO_MIGRATE=true` 跑完上述迁移后改回 `false`
（见仓库根 `deploy/README.md` 第 5 节第 3 步）。

## 2. 手工执行 SQL

确实需要手工执行时：

```bash
# 1) 先备份
sudo /usr/local/sbin/newcosmos-pg-backup
# 或者：
pg_dump -h 10.110.0.2 -U newcosmos -d newcosmos -Fc \
  -f /srv/newcosmos/backups/postgres/manual_$(date +%Y%m%d_%H%M%S).dump

# 2) 执行
psql -h 10.110.0.2 -U newcosmos -d newcosmos \
  -v ON_ERROR_STOP=1 -f path/to/change.sql

# 3) 验证
psql -h 10.110.0.2 -U newcosmos -d newcosmos -c '\d+ 目标表'
```

约定：

- 使用 `CREATE ... IF NOT EXISTS` / `ADD COLUMN IF NOT EXISTS`，保证可重复执行；
- 含 `TRUNCATE` / `DELETE` / `DROP` 的一次性脚本不要放进自动迁移；
- 执行前必须备份，执行后抽查关键表与索引。

## 3. 备份

`backup.sh` 在 `.2` 宿主机运行，负责：

- `pg_dump -Fc` 到 `/srv/newcosmos/backups/postgres`；
- 先写 `.partial`，成功后原子改名；
- `pg_restore --list` 校验归档；
- 生成 `.sha256`；
- 按 `NC_BACKUP_RETENTION_DAYS` 清理旧备份；
- 拒绝写入 nginx 公开目录。

安装与 cron 配置见仓库根 `deploy/README.md` 第 7 节。

## 4. 一键发布

统一入口是 `C:\Projects\publish-all.ps1`（不在本仓库内）。
它只负责构建、上传、校验和可选的磁盘清理，**不会执行任何 SQL**。
