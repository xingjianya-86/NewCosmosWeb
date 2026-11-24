"""Worker 配置——全部来自环境变量，不内置任何凭据或主机默认值。

对外契约（详见 deploy/lottery/README.md）：

    NC_DB_HOST / NC_DB_PORT / NC_DB_NAME / NC_DB_USER / NC_DB_PASSWORD
    NC_LOTTERY_MODEL_DIR        模型根目录（持久卷，默认 /models）
    NC_LOTTERY_JOBS             任务日志与运行目录（持久卷，默认 /jobs）
    NC_LOTTERY_MAX_MEMORY_MB    单个任务子进程 RSS 上限（0=不限制，默认 1536）
    NC_LOTTERY_DEFAULT_WINDOW   默认回看窗口（默认 73，与桌面端 LSTM_WINDOW 一致）
    NC_LOTTERY_MAX_PENDING      队列容量（默认 32）
    NC_LOTTERY_TRAIN_TIMEOUT_SEC / NC_LOTTERY_PREDICT_TIMEOUT_SEC
"""

from __future__ import annotations

import os
from dataclasses import dataclass
from pathlib import Path

SERVICE_NAME = "NewCosmos.LotteryWorker"
VERSION = "0.1.0"


def _env_str(name: str, default: str | None = None) -> str | None:
    raw = os.environ.get(name)
    if raw is None:
        return default
    value = raw.strip()
    return value or default


def _env_secret(name: str, default: str | None = None) -> str | None:
    """密码类变量不 strip：允许首尾空格，空串视为未设置。"""
    raw = os.environ.get(name)
    if raw is None or raw == "":
        return default
    return raw


def _env_int(name: str, default: int, minimum: int = 0) -> int:
    raw = os.environ.get(name)
    if raw is None or not raw.strip():
        return default
    try:
        value = int(raw)
    except ValueError as exc:
        raise RuntimeError(f"环境变量 {name} 必须是整数，当前值: {raw!r}") from exc
    if value < minimum:
        raise RuntimeError(f"环境变量 {name} 不能小于 {minimum}，当前值: {value}")
    return value


@dataclass(frozen=True)
class Settings:
    """进程级不可变配置（启动时读取一次）。"""

    db_host: str | None
    db_port: int
    db_name: str | None
    db_user: str | None
    db_password: str | None
    model_dir: Path
    jobs_dir: Path
    runtime_dir: Path
    max_memory_mb: int
    default_window: int
    max_pending_jobs: int
    train_timeout_sec: int
    predict_timeout_sec: int
    log_tail_lines: int = 200

    @property
    def worker_root(self) -> Path:
        """Worker 项目根目录（含 app/ 与 legacy/）。"""
        return Path(__file__).resolve().parents[1]

    @property
    def legacy_entry(self) -> Path:
        return self.worker_root / "legacy" / "lstm_entry.py"

    @property
    def legacy_project(self) -> Path:
        return self.worker_root / "legacy" / "predict_Lottery_ticket"

    @property
    def db_configured(self) -> bool:
        return bool(self.db_host and self.db_name and self.db_user and self.db_password)

    def model_window_dir(self, lottery_type: str, window: int) -> Path:
        return self.model_dir / lottery_type / f"window_{window}"


def load_settings() -> Settings:
    """读取环境变量并确保运行目录存在（目录不可写时启动直接失败）。"""
    jobs_dir = Path(_env_str("NC_LOTTERY_JOBS", "/jobs") or "/jobs").expanduser()
    model_dir = Path(_env_str("NC_LOTTERY_MODEL_DIR", "/models") or "/models").expanduser()

    # 运行数据（导出的 CSV、legacy 日志等）放在 jobs 卷下，避免与模型卷互相影响。
    runtime_dir = jobs_dir / "runtime"

    for path in (jobs_dir, model_dir, runtime_dir):
        path.mkdir(parents=True, exist_ok=True)

    return Settings(
        db_host=_env_str("NC_DB_HOST"),
        db_port=_env_int("NC_DB_PORT", 5432, minimum=1),
        db_name=_env_str("NC_DB_NAME"),
        db_user=_env_str("NC_DB_USER"),
        db_password=_env_secret("NC_DB_PASSWORD"),
        model_dir=model_dir.resolve(),
        jobs_dir=jobs_dir.resolve(),
        runtime_dir=runtime_dir.resolve(),
        max_memory_mb=_env_int("NC_LOTTERY_MAX_MEMORY_MB", 1536, minimum=0),
        default_window=_env_int("NC_LOTTERY_DEFAULT_WINDOW", 73, minimum=10),
        max_pending_jobs=_env_int("NC_LOTTERY_MAX_PENDING", 32, minimum=1),
        train_timeout_sec=_env_int("NC_LOTTERY_TRAIN_TIMEOUT_SEC", 6 * 60 * 60, minimum=60),
        predict_timeout_sec=_env_int("NC_LOTTERY_PREDICT_TIMEOUT_SEC", 30 * 60, minimum=30),
    )
