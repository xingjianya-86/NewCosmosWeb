# 数据库维护脚本

## 002_schema_reconciliation.sql

P0/P1 schema 对账增量：用户/照料人 `position`、申请/家庭成员年值与医保列、照片临时池、打印任务目标列、打印状态归一。全部 `IF NOT EXISTS`/可重复执行；执行前先备份。

```bash
psql -h 10.110.0.2 -U newcosmos -d new_cosmos \
  -f deploy/database/002_schema_reconciliation.sql
```

## 20261124_clear_printer_registration.sql

清空旧打印注册数据：

- `nc_sys_printer_bindings`
- `nc_sys_print_devices`

保留历史打印任务 `nc_biz_print_jobs` 与打印留痕。执行前先备份；执行后所有 Agent 需要重新注册，用户需要重新绑定打印机。

```bash
psql -h 10.110.0.2 -U newcosmos -d new_cosmos \
  -f deploy/database/20261124_clear_printer_registration.sql
```

如果确认旧打印任务也不需要，再单独执行脚本末尾注释的 `TRUNCATE nc_biz_print_jobs`。

## 20261126_archive_photo_pending.sql

补齐「待归属照片池」表：`nc_biz_archive_photo_pending`（Web 照片中心「待归属」页读写；PC 端已建同表）。全部 `IF NOT EXISTS`，可重复执行；执行前先备份。

```bash
psql -h 10.110.0.2 -U newcosmos -d new_cosmos \
  -f deploy/database/20261126_archive_photo_pending.sql
```

