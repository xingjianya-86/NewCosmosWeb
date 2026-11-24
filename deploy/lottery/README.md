# 彩票 Worker 部署（10.110.0.2）

NewCosmos 彩票训练/推理的独立容器：Python 3.11 + FastAPI + TensorFlow CPU。
容器只监听 `127.0.0.1:8790`（或内部网络），**不映射公网**；NewCosmos.Server
通过 `http://127.0.0.1:8790` 入队/轮询，认证与权限仍在 Server 侧。

## 1. 组成

```text
deploy/lottery/
├─ Dockerfile            python:3.11-slim、非 root(uid 10001)、HEALTHCHECK
├─ docker-compose.yml    memory 2g / cpus 1.5、回环端口、模型/任务卷
├─ .env.example          复制为 .env 后填写（.env 不入库）
└─ data/                 运行时创建（不入库）
   ├─ models/<彩种>/window_<N>/{red.keras,blue.keras,metadata.json}
   └─ jobs/<jobId>/{job.log,job.json,result.json}
```

构建上下文是 `src/NewCosmos.LotteryWorker`（compose 已配置），镜像内包含
`app/` 与 `legacy/`。

## 2. 前置条件

- Docker Engine 20.10+，Docker Compose v2；
- PostgreSQL 已建 `nc_lottery_draws`（及负样本表 `nc_lottery_user_purchases`）；
- 可用磁盘：镜像约 1.5–2 GB（TensorFlow CPU），模型/任务卷按实际增长；
- 内存：容器上限 2g；**不要同时跑训练与其它大内存任务**。

## 3. 部署

```bash
cd /path/to/NewCosmosWeb/deploy/lottery

mkdir -p data/models data/jobs
sudo chown -R 10001:10001 data        # 容器以 uid/gid 10001 运行
cp .env.example .env
vi .env                               # 填 NC_DB_*，密码只放 .env

docker compose up -d --build
docker compose ps
curl -s http://127.0.0.1:8790/health | python3 -m json.tool
```

`docker compose` 会自动读取同目录 `.env` 做变量替换。更新代码后：

```bash
docker compose build --no-cache lottery
docker compose up -d lottery
```

### 数据库连通性

容器访问宿主机 PostgreSQL 有两条路：

1. **默认（推荐）**：`NC_DB_HOST=host.docker.internal`（compose 已加
   `host-gateway`），并让 PG 接受来自 Docker 网桥的连接：

   ```conf
   # postgresql.conf
   listen_addresses = 'localhost,172.17.0.1'
   # pg_hba.conf
   host  new_cosmos  new_cosmos  172.17.0.0/16  scram-sha-256
   ```

   仅监听 `127.0.0.1` 的 PG 容器内无法访问。
2. PostgreSQL 也在 Docker 网络中：把 `NC_DB_HOST` 改成服务名，并把 Worker 加入
   同一网络；此时可删掉 `extra_hosts`。

连不上时 `/jobs/*` 入队会返回 `DB_NOT_CONFIGURED`（未配置）或任务失败并提示
`数据库不可达 / 认证失败`，不会返回假预测。

## 4. 配置项

| 环境变量 | 默认 | 说明 |
|---|---|---|
| `NC_DB_HOST` | — | PostgreSQL 主机，**必填** |
| `NC_DB_PORT` | `5432` | 端口 |
| `NC_DB_NAME` | — | 数据库名，**必填** |
| `NC_DB_USER` | — | 用户，**必填** |
| `NC_DB_PASSWORD` | — | 密码，**必填**；只经环境变量传给 legacy（`PGPASSWORD`） |
| `NC_LOTTERY_MODEL_DIR` | `/models` | 模型根目录（持久卷） |
| `NC_LOTTERY_JOBS` | `/jobs` | 任务日志/导出数据根目录（持久卷） |
| `NC_LOTTERY_MAX_MEMORY_MB` | `1536` | 单任务子进程 RSS 上限；`0`=不监控（仍受 cgroup 2g 限制） |
| `NC_LOTTERY_DEFAULT_WINDOW` | `73` | 默认回看窗口，与桌面端 `LSTM_WINDOW=73` 对齐 |
| `NC_LOTTERY_MAX_PENDING` | `32` | 队列容量，满时入队返回 429 |
| `NC_LOTTERY_TRAIN_TIMEOUT_SEC` | `21600` | 训练超时（6h） |
| `NC_LOTTERY_PREDICT_TIMEOUT_SEC` | `1800` | 预测超时（30min） |

窗口可选范围 `10..500`；预测注数 `1..20`；彩种仅 `ssq`（双色球）/ `dlt`（大乐透）。

## 5. 模型放置

训练任务会把模型写到：

```text
data/models/<ssq|dlt>/window_<N>/
├─ red.keras
├─ blue.keras
└─ metadata.json
```

从桌面端迁移已有模型（桌面端路径 `%APPDATA%\NewCosmos\LotteryLSTM\model\...`）：

```bash
# 在部署机执行；源文件从 Windows 拷贝到临时目录后
mkdir -p data/models/ssq/window_73
cp /tmp/lottery-model/ssq/window_73/{red.keras,blue.keras,metadata.json} \
   data/models/ssq/window_73/
sudo chown -R 10001:10001 data/models
```

预测前会校验 `red.keras` 与 `blue.keras` 同时存在；缺失时入队直接返回
`409 MODEL_NOT_FOUND`（提示先训练），不会生成随机结果。

## 6. 训练 / 推理

所有任务异步执行，**并发恒为 1**；返回 `jobId` 后用 `GET /jobs/{jobId}` 轮询。
训练可能数十分钟到数小时（legacy 默认 60/30 epochs），建议错峰。

### 6.1 训练

```bash
curl -s -X POST http://127.0.0.1:8790/jobs/train \
  -H 'Content-Type: application/json' \
  -d '{"lotteryType":"ssq","window":73}'
```

无 body 时等价于 `{"lotteryType":"ssq","window":73}`。

### 6.2 预测

```bash
curl -s -X POST http://127.0.0.1:8790/jobs/predict \
  -H 'Content-Type: application/json' \
  -d '{"lotteryType":"ssq","window":73,"count":5}'
```

### 6.3 查询 / 取消

```bash
JOB_ID=xxxxxxxx
curl -s "http://127.0.0.1:8790/jobs/$JOB_ID"
curl -s -X POST "http://127.0.0.1:8790/jobs/$JOB_ID/cancel"
curl -s 'http://127.0.0.1:8790/jobs?status=running&limit=20'   # 附加：任务列表
```

## 7. 接口契约（NewCosmos.Server 调用）

- Base URL：`http://127.0.0.1:8790`（不得经 `.1` 或公网 nginx 暴露）
- 编码：`application/json; charset=utf-8`
- 时间：ISO 8601 UTC（`2026-11-24T10:00:00.000Z`）
- 无认证头；调用方必须是同机 Server 进程（或同一内部网络）

### `GET /health`

```json
{
  "status": "ok",
  "service": "NewCosmos.LotteryWorker",
  "version": "0.1.0",
  "time": "2026-11-24T10:00:00.000Z",
  "queue": { "pending": 0, "running": 0, "total": 0, "capacity": 32, "concurrency": 1 },
  "dependencies": { "numpy": true, "pandas": true, "psycopg2": true, "tensorflow": true },
  "missingDependencies": [],
  "paths": { "modelDir": "/models", "jobsDir": "/jobs" },
  "limits": { "concurrency": 1, "maxMemoryMb": 1536, "defaultWindow": 73, "maxPendingJobs": 32 }
}
```

依赖缺失时 `status=degraded`（HTTP 仍为 200，仅表示 API 存活；任务入队会 503）。

### `POST /jobs/train` → `202`

请求：

```json
{ "lotteryType": "ssq", "window": 73 }
```

响应（`Job`）：

```json
{
  "jobId": "b3c1...",
  "type": "train",
  "status": "pending",
  "progress": 0,
  "message": "排队中",
  "params": { "lotteryType": "ssq", "window": 73 },
  "createdAt": "2026-11-24T10:00:00.000Z",
  "startedAt": null,
  "finishedAt": null,
  "result": null,
  "error": null,
  "logTail": []
}
```

### `POST /jobs/predict` → `202`

```json
{ "lotteryType": "ssq", "window": 73, "count": 5 }
```

`params` 增加 `count`。成功后 `result` 为 legacy 原始 JSON（键名与桌面端
`LotteryPredictService.ParsePredictions` 兼容）：

```json
{
  "lottery_type": "ssq",
  "algorithm": "lstm",
  "window": 73,
  "count": 5,
  "predictions": [
    { "red_numbers": [3, 8, 12, 19, 25, 31], "blue_numbers": [9], "confidence": 15.7 }
  ]
}
```

训练成功 `result`：

```json
{
  "action": "train",
  "lottery_type": "ssq",
  "window": 73,
  "code": "ssq",
  "trained_on_issues": ["2024001", "2026123"],
  "timestamp": "2026-11-24T10:20:00"
}
```

### `GET /jobs/{jobId}` → `200 | 404`

同一 `Job` 结构；`progress` 为启发式估计（Keras `Epoch x/y`），`logTail` 保留最后
100 行。任务成功后 `status=succeeded`、`progress=100`；失败时 `error` 为
`{"code":"TRAIN_FAILED","message":"..."}`。

### `POST /jobs/{jobId}/cancel` → `200 | 404 | 409`

- `pending`：立即 `cancelled`；
- `running`：`message=正在取消…`，子进程被终止后 `cancelled`；
- 已结束：`409 JOB_NOT_CANCELLABLE`。

### `GET /jobs?status=&limit=`（附加）

`{"items":[...]}`，按创建时间倒序，`limit` 1–200。

### 状态

| status | 含义 |
|---|---|
| `pending` | 已入队，未开始 |
| `running` | 子进程执行中（含「正在取消」阶段） |
| `succeeded` | 完成，`result` 可用 |
| `failed` | 失败，`error` 可读 |
| `cancelled` | 已取消 |

### 错误格式与错误码

HTTP 错误统一为：

```json
{ "code": "MODEL_NOT_FOUND", "message": "模型不存在或窗口不匹配：…；请先 POST /jobs/train" }
```

| code | HTTP | 场景 |
|---|---|---|
| `VALIDATION_ERROR` | 422 | 参数/彩种/窗口/注数不合法 |
| `JOB_NOT_FOUND` | 404 | jobId 不存在（或 Worker 重启后内存队列丢失） |
| `MODEL_NOT_FOUND` | 409 | 预测所需 `.keras` 缺失 |
| `JOB_NOT_CANCELLABLE` | 409 | 任务已结束 |
| `QUEUE_FULL` | 429 | 队列满 |
| `DEPENDENCY_MISSING` | 503 | 镜像缺 tensorflow/psycopg2 等 |
| `DB_NOT_CONFIGURED` | 503 | `NC_DB_*` 不完整 |
| `TRAIN_FAILED` / `PREDICT_FAILED` | 任务内 | legacy 非零退出，`error.message` 带提示 |
| `JOB_TIMEOUT` / `JOB_OUT_OF_MEMORY` | 任务内 | 超时 / RSS 超限 |
| `LEGACY_BAD_OUTPUT` | 任务内 | legacy 未输出可解析 JSON |

### Server 集成建议

1. `POST` 入队超时 5s；**不要**在 HTTP 请求内等待训练完成。
2. 轮询间隔：训练 5–15s，预测 1–2s；`status` 终态即停止。
3. Worker 并发 1，Server 可展示「排队中/第 N 位」，避免用户重复提交。
4. Worker 重启后旧 jobId 返回 404：视为「任务丢失」，重新入队即可（磁盘日志供排查）。
5. 预测结果落库由 Server 决定（历史表 `nc_lottery_predictions` 已无代码读写，勿直接写）。
6. 模型训练成功后 Server 可清缓存/刷新「模型就绪」标记。

## 8. 资源与并发

| 限制 | 值 | 实现 |
|---|---|---|
| 内存 | 2g | compose `deploy.resources.limits.memory`（cgroup） |
| CPU | 1.5 | compose `deploy.resources.limits.cpus` |
| 并发 | 1 | 单 worker 线程 + 单 uvicorn worker + 子进程 |
| 任务 RSS | 1536 MB | `NC_LOTTERY_MAX_MEMORY_MB`，超限终止任务 |
| 线程 | TF intra=2 / inter=1 / OMP=2 | Dockerfile ENV，避免 1.5 CPU 下超卖 |

训练内存峰值通常高于预测。若任务因 `JOB_OUT_OF_MEMORY` 失败：优先减小窗口/错峰，
或调整 legacy 训练轮数（需改代码重新构建）；**不要**把容器内存上限抬过宿主机余量。

## 9. 运维

```bash
JOB_ID=xxxxxxxx                                 # 从入队/列表响应取 jobId
docker compose logs -f --tail=200 lottery       # 容器日志
docker stats newcosmos-lottery                  # 实时内存/CPU
ls -lt data/jobs | head                         # 最近任务
tail -f "data/jobs/$JOB_ID/job.log"             # 单任务完整日志
docker compose restart lottery                  # 重启（会取消运行中任务）
```

- 容器 `restart: unless-stopped`；健康检查每 30s 请求 `/health`。
- 日志轮转：json-file 10MB × 3；任务日志不轮转，需按磁盘情况清理旧 `data/jobs/*`。
- 备份：定期备份 `data/models`（训练产物）；`data/jobs` 仅为审计，可按需保留。
- 升级：`docker compose build --no-cache && docker compose up -d`；模型卷不受影响。

## 10. 故障排查

| 现象 | 排查 |
|---|---|
| `/health` 503/连不上 | `docker compose ps`、`logs`；uvicorn 是否启动；端口是否被占用 |
| `status=degraded` | 镜像依赖缺失，`docker compose build --no-cache` |
| `DEPENDENCY_MISSING` | 同上；不要在宿主机裸跑缺 TF 的环境 |
| `DB_NOT_CONFIGURED` | `.env` 缺 `NC_DB_*`；改后 `docker compose up -d` 重建容器 |
| 数据库不可达 | PG `listen_addresses/pg_hba`；`NC_DB_HOST=host.docker.internal` |
| `MODEL_NOT_FOUND` | 放模型或先训练；窗口必须与目录 `window_<N>` 一致 |
| `TRAIN_FAILED` + 开奖数据不足 | 先在彩票首页同步 `nc_lottery_draws`，期数需 > window |
| `JOB_OUT_OF_MEMORY` | 见 §8；检查是否并发提交了多个任务 |
| 任务一直 pending | 前一任务仍在跑（并发 1）；查 `/jobs?status=running` |
| 重启后 jobId 404 | 内存队列不持久化，属预期；重新入队 |

## 11. 安全

- 端口只绑 `127.0.0.1:8790`；**禁止**在 nginx/防火墙把 8790 暴露到公网。
- 无内置认证：任何能访问该端口的人都能提交训练。若 Server 也是容器，建议：
  1. 让 Server 与 Worker 加入同一 user-defined 内部网络，compose 中删掉 `ports`
     并加 `expose: ["8790"]`，Server 用 `http://lottery:8790`；或
  2. 仅在宿主机绑定 `172.17.0.1:8790:8790`，并限制防火墙来源。
- 数据库密码只存在 `.env` 与容器环境；legacy 通过 `PGPASSWORD` 读取，不进命令行。
- 镜像非 root（uid 10001）、`no-new-privileges`、`pids_limit=256`。
- 输入只允许 `ssq/dlt` 与受限窗口/注数，避免任意命令/路径注入。

## 12. 数据与许可

- 开奖数据唯一来源：PostgreSQL `nc_lottery_draws`；Worker 不联网抓取。
  `legacy/fetch_history.py` 仅作参考（桌面端/历史运维使用）。
- `src/NewCosmos.LotteryWorker/legacy/predict_Lottery_ticket/` 来自
  [KittenCN/predict_Lottery_ticket](https://github.com/KittenCN/predict_Lottery_ticket)
  commit `6cf60bb730e78e5e77ab0fdcd151fced9f19385a`，**GPL-3.0**。
- 许可全文：`src/NewCosmos.LotteryWorker/legacy/predict_Lottery_ticket/LICENSE`；
  来源、改动与再生成步骤：`src/NewCosmos.LotteryWorker/legacy/README.md`。
- 分发镜像/二进制时须保留 GPL 许可与版权声明、提供对应源码（本仓库及镜像内
  `/app/legacy` 已包含），且不得附加额外限制。软件不含任何担保。

## 13. 已知限制 / 后续

- 队列仅在内存：Worker 重启会丢失 pending/running 状态（磁盘保留日志与结果）。
- 无任务历史查询 API（`GET /jobs` 只覆盖本次进程）；长期审计建议 Server 落库。
- 无 epochs/学习率等训练参数透传（沿用 legacy 默认），如需调参改
  `legacy/predict_Lottery_ticket/src/config.py` 并重建镜像。
- 单机单实例；扩副本会破坏「并发 1」与内存预算，需先改造为集中队列。
