# 打印机按账户绑定（设计）

> 目标：每个登录账号只打印自己的任务；同一台 Windows PC 多用户共用也不串打。

## 数据表

```sql
nc_sys_print_devices (
  id            bigserial primary key,
  device_key    uuid not null unique,
  machine_name  text not null,
  agent_version text,
  printers_json jsonb not null default '[]',
  last_seen_at  timestamptz,
  created_at    timestamptz not null default now(),
  updated_at    timestamptz not null default now()
);

nc_sys_printer_bindings (
  id          bigserial primary key,
  user_id     int not null references nc_sys_users(id),
  device_id   bigint not null references nc_sys_print_devices(id),
  printer_name text not null,
  is_default  boolean not null default false,
  created_at  timestamptz not null default now(),
  updated_at  timestamptz not null default now(),
  unique (user_id, device_id, printer_name)
);
```

- `printers_json` 保存 Agent 上报的 `[{name,isDefault}]`，绑定页从这里选，不手输。
- 同一账号默认打印机唯一（应用层保证：设默认时先清同账号其他默认）。
- 删除默认后，若还有绑定，自动把最早一条设为默认。

## 打印任务

```sql
nc_biz_print_jobs (
  ...,
  requester_id      int not null,
  target_user_id    int not null,
  target_device_id  bigint,
  target_printer    text,
  ...
)
```

- 入队时固化 `target_user_id/target_device_id/target_printer`。
- Agent 认领条件：`target_device_id = 本机 device_id`，且 `target_printer` 在本机上报列表。
- 管理员可改绑/代打，必须记审计。
- 设备离线：任务保持 `pending`，超过阈值 `blocked/expired` 并提示；不自动打给别人的打印机。

## UI

- 「我的打印机」：当前账号绑定列表、设备名、打印机名、默认、在线状态。
- 绑定：选择在线设备 → 选择该设备上报的打印机 → 是否默认。
- 打印对话框：只显示当前账号绑定；默认选中默认打印机；无绑定则引导去绑定。
- 手机端：同样只显示账号绑定；只入队，不本机打印。

## 数据清理

- 旧设备/绑定数据用 `deploy/database/20261124_clear_printer_registration.sql` 一次性清空。
- 清理后所有 Agent 需重新注册，用户需重新绑定；历史打印任务保留。

## 安全

- 绑定接口只允许操作当前 `user_id`；管理员例外走单独权限。
- Agent 注册使用一次性 `NC_AGENT_REGISTER_TOKEN`；心跳/认领使用 `device_key`。
- 绑定/解绑/改默认写审计日志。
