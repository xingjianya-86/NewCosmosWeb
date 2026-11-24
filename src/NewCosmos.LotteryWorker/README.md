# NewCosmos.LotteryWorker

彩票 LSTM 训练/预测 Worker（Python 3.11 + FastAPI + TensorFlow CPU）。
部署与接口契约见 [`deploy/lottery/README.md`](../../deploy/lottery/README.md)。

## 目录

```text
app/
  main.py       FastAPI 路由：/health、/jobs/train、/jobs/predict、/jobs/{id}、/jobs/{id}/cancel
  config.py     环境变量配置（NC_DB_*、NC_LOTTERY_*）
  jobs.py       内存队列 + 单 worker 线程（并发 1）+ 状态机 + 落盘
  runner.py     legacy 适配层：子进程调用 lstm_entry.py、进度/日志、取消、超时、RSS 上限
  schemas.py    请求模型
  errors.py     统一错误码
legacy/         从桌面端整理的必要脚本（含 GPL-3.0 原件，见 legacy/README.md）
requirements.txt
.dockerignore
```

## 设计要点

- **并发 1**：API 进程内只有一个执行线程；`uvicorn --workers 1`，不要再加副本。
- **进程隔离**：训练/预测经 `legacy/lstm_entry.py` 子进程执行，TensorFlow 内存随进程释放；
  API 进程不 import TensorFlow。
- **取消**：终止子进程组（POSIX `killpg`）；运行中的任务最终状态为 `cancelled`。
- **内存**：每 0.5s 读取 `/proc/<pid>/status` 的 VmRSS，超过 `NC_LOTTERY_MAX_MEMORY_MB` 即终止；
  容器 cgroup 另有 2g 硬上限（见 compose）。
- **数据源**：只读 PostgreSQL `nc_lottery_draws`（由 legacy 导出为 CSV），不联网抓取。
- **不伪造结果**：`job.result` 是 legacy 子进程 stdout 的原始 JSON；依赖/配置/模型缺失时
  入队即返回明确错误（503/409）。

## 本地运行（不安装 TensorFlow 时只能验证语法，不能真正训练）

```powershell
python -m py_compile app\*.py legacy\lstm_entry.py
$env:NC_DB_HOST="127.0.0.1"
$env:NC_DB_NAME="new_cosmos"
$env:NC_DB_USER="new_cosmos"
$env:NC_DB_PASSWORD="***"
$env:NC_LOTTERY_MODEL_DIR="$PWD\data\models"
$env:NC_LOTTERY_JOBS="$PWD\data\jobs"
uvicorn app.main:app --host 127.0.0.1 --port 8790 --workers 1
```

缺少 `tensorflow` / `psycopg2` 时 `/health` 返回 `status=degraded`，任务入队返回
`DEPENDENCY_MISSING`，不会产生假结果。

## 状态机

`pending → running → succeeded | failed | cancelled`

- 取消排队中的任务：立即 `cancelled`；
- 取消运行中的任务：先进入「正在取消」，子进程退出后 `cancelled`；
- 已结束任务再次取消：`409 JOB_NOT_CANCELLABLE`；
- 进程重启后内存队列丢失，任务查询返回 `404`，磁盘日志保留在 `NC_LOTTERY_JOBS/<jobId>/`。

## 许可

`legacy/predict_Lottery_ticket/` 为 GPL-3.0（KittenCN/predict_Lottery_ticket），许可与来源见
`legacy/README.md` 与 `legacy/predict_Lottery_ticket/LICENSE`。分发镜像时须保留许可并提供源码。
