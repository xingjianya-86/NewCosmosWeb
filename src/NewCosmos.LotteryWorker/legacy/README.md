# legacy 目录说明（GPL-3.0 与来源）

本目录是从桌面端 `legacy/NewCosmos.Desktop/Scripts/Lottery/` 整理出的 Worker 运行时副本。
**只读参考原件**，桌面端仍在维护自己的副本；本目录随 Worker 容器一起发布。

## 来源

| 本目录路径 | 上游来源 | 许可 |
|---|---|---|
| `lstm_entry.py` | `legacy/NewCosmos.Desktop/Scripts/Lottery/lstm_entry.py`（NewCosmos 适配层） | 项目代码；依赖 GPL-3.0 组件 |
| `fetch_history.py` | 同上（开奖数据抓取/落库工具，Worker **不调用**） | 项目代码 |
| `create_tables.sql` | 同上（彩票表结构参考） | 项目代码 |
| `requirements-desktop.txt` | 同上（桌面端依赖清单，仅参考） | — |
| `predict_Lottery_ticket/` | [KittenCN/predict_Lottery_ticket](https://github.com/KittenCN/predict_Lottery_ticket) 「基于 tensorflow lstm 模型的彩票预测」commit `6cf60bb730e78e5e77ab0fdcd151fced9f19385a` | **GPL-3.0**，全文见 `predict_Lottery_ticket/LICENSE` |

`predict_Lottery_ticket/` 保留上游 `README.md`、`config/config.yaml`、`src/*.py` 与许可；
`requirements-upstream.txt` 为上游依赖清单，仅供参考（Worker 使用根目录 `requirements.txt`）。

## 相对桌面端的改动

1. `lstm_entry.py`（唯一改动）：在 PATHS 初始化后增加

   ```python
   _MODEL_DIR_OVERRIDE = os.environ.get("NC_LOTTERY_MODEL_DIR")
   if _MODEL_DIR_OVERRIDE:
       _cfg.PATHS["model"] = Path(_MODEL_DIR_OVERRIDE)
   ```

   使容器内模型目录与 `NC_LOTTERY_JOBS`（日志/导出数据）分离，便于单独挂载持久卷。
   未设置该变量时行为与桌面端一致。
2. `predict_Lottery_ticket/src/bootstrap.py` 沿用桌面端已合入的 Keras 3 兼容 shim
   （`tf.transpose` 对 KerasTensor 的适配），本次未再改动。
3. 其余文件原样复制。

## 明确排除

- `*.keras` 模型文件、`data/`、`predict/`、`logs/` 训练数据与产物；
- 上游 `tests/`、`docs/`、`examples/`、`scripts/`、`.github/`（Worker 不运行上游脚本）；
- 任何凭据、数据库连接串。

模型由训练任务写入 `NC_LOTTERY_MODEL_DIR`，或由运维从桌面端复制（见
`deploy/lottery/README.md`）。

## 许可义务

`predict_Lottery_ticket/` 为 GPL-3.0 程序。分发包含该目录的镜像/产物时，必须：

- 保留 `LICENSE` 与版权声明、来源说明（本文件）；
- 提供对应源代码（本仓库即源码；镜像内 `/app/legacy` 亦包含源码）；
- 不以额外限制约束接收方依 GPL 享有的权利；
- 知悉本软件不含任何担保。详见 `predict_Lottery_ticket/LICENSE`。

## 重新生成副本

在仓库根目录执行（PowerShell）：

```powershell
$src = 'legacy\NewCosmos.Desktop\Scripts\Lottery'
$dst = 'src\NewCosmos.LotteryWorker\legacy'
Copy-Item "$src\lstm_entry.py" "$dst\lstm_entry.py"
Copy-Item "$src\fetch_history.py" "$dst\fetch_history.py"
Copy-Item "$src\create_tables.sql" "$dst\create_tables.sql"
Copy-Item "$src\requirements.txt" "$dst\requirements-desktop.txt"
Copy-Item "$src\predict_Lottery_ticket\src\*.py" "$dst\predict_Lottery_ticket\src\"
Copy-Item "$src\predict_Lottery_ticket\config\config.yaml" "$dst\predict_Lottery_ticket\config\config.yaml"
Copy-Item "$src\predict_Lottery_ticket\LICENSE" "$dst\predict_Lottery_ticket\LICENSE"
Copy-Item "$src\predict_Lottery_ticket\README.md" "$dst\predict_Lottery_ticket\README.md"
Copy-Item "$src\predict_Lottery_ticket\requirements.txt" "$dst\predict_Lottery_ticket\requirements-upstream.txt"
```

复制后必须重新应用上面的 `NC_LOTTERY_MODEL_DIR` 改动。
