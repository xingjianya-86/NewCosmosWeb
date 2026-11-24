"""legacy LSTM 适配层。

设计要点：

* **不在 API 进程内 import TensorFlow**——训练/预测都以 ``legacy/lstm_entry.py``
  子进程执行。API 常驻内存只保留 FastAPI/队列，TF 内存随任务进程释放。
* 子进程 stdout/stderr 合并读取，final JSON（legacy 打印到 stdout）从日志流中识别；
  完整日志落 ``<NC_LOTTERY_JOBS>/<jobId>/job.log``。
* 取消 = 终止进程组；内存上限 = 轮询 ``/proc/<pid>/status`` 的 VmRSS（容器已另有 cgroup 上限）。
* 进度是启发式（Keras ``Epoch x/y``），只用于展示，不参与业务判断。
"""

from __future__ import annotations

import importlib.util
import json
import logging
import os
import re
import signal
import subprocess
import sys
import threading
import time
from collections import deque
from pathlib import Path
from typing import Any, Callable

from .config import Settings
from .errors import WorkerError

logger = logging.getLogger("uvicorn.error")

ProgressCallback = Callable[[int, str], None]
LogCallback = Callable[[str], None]

SUPPORTED_LOTTERIES = ("ssq", "dlt")
REQUIRED_MODULES = ("numpy", "pandas", "psycopg2", "tensorflow")
MODEL_FILES = ("red.keras", "blue.keras")
EPOCH_RE = re.compile(r"Epoch\s+(\d+)\s*/\s*(\d+)", re.IGNORECASE)

# 失败日志 → 可操作提示（只做提示，不改变原始退出码语义）
_FAILURE_HINTS: tuple[tuple[str, str], ...] = (
    ("No module named 'tensorflow'", "缺少 TensorFlow：请重新构建镜像（pip install -r requirements.txt）"),
    ("No module named 'psycopg2'", "缺少 psycopg2：请重新构建镜像（pip install -r requirements.txt）"),
    ("No module named 'pandas'", "缺少 pandas：请重新构建镜像（pip install -r requirements.txt）"),
    ("缺少数据库连接参数", "数据库未配置：检查 NC_DB_HOST/NC_DB_NAME/NC_DB_USER"),
    ("未设置数据库密码", "缺少数据库密码：检查 NC_DB_PASSWORD"),
    ("password authentication failed", "数据库认证失败：检查 NC_DB_USER/NC_DB_PASSWORD"),
    ("could not connect", "数据库不可达：检查 NC_DB_HOST/NC_DB_PORT 与容器网络"),
    ("Connection refused", "数据库不可达：检查 NC_DB_HOST/NC_DB_PORT 与容器网络"),
    ("could not translate host name", "数据库主机名无法解析：检查 NC_DB_HOST"),
    ("libgomp.so.1", "缺少系统库 libgomp1：请重建镜像（Dockerfile 已安装）"),
    ("GLIBCXX_", "系统 libstdc++ 版本过旧：请使用 python:3.11-slim 基础镜像"),
    ("开奖数据不足", "开奖数据不足：请先在彩票首页同步开奖数据（nc_lottery_draws）"),
    ("模型未训练或窗口不匹配", "模型不存在或窗口不匹配：请先 POST /jobs/train 训练该窗口"),
    ("MemoryError", "子进程内存不足：降低 epochs/窗口或提高 NC_LOTTERY_MAX_MEMORY_MB"),
)


def _module_available(name: str) -> bool:
    try:
        return importlib.util.find_spec(name) is not None
    except (ImportError, ValueError):
        return False


def dependency_status() -> dict[str, bool]:
    """只做 find_spec，不 import TF，避免健康检查触发大内存。"""
    return {name: _module_available(name) for name in REQUIRED_MODULES}


def _read_rss_mb(pid: int) -> float | None:
    """读取 Linux 进程 RSS（MB）；非 Linux / 进程已退出返回 None。"""
    try:
        with open(f"/proc/{pid}/status", encoding="utf-8") as fp:
            for line in fp:
                if line.startswith("VmRSS:"):
                    parts = line.split()
                    if len(parts) >= 2:
                        return int(parts[1]) / 1024.0
    except (OSError, ValueError):
        return None
    return None


def _failure_message(log_tail: list[str], exit_code: int) -> str:
    if exit_code in (137, -9):
        return (
            f"legacy 子进程被系统终止（退出码 {exit_code}，常见于内存超限/OOM）；"
            "请错峰、减小窗口或检查 NC_LOTTERY_MAX_MEMORY_MB 与容器 2g 上限"
        )
    joined = "\n".join(log_tail)
    for needle, hint in _FAILURE_HINTS:
        if needle.lower() in joined.lower():
            return f"{hint}（legacy 退出码 {exit_code}）"
    last = log_tail[-1].strip() if log_tail else "(无输出)"
    return f"legacy 子进程失败，退出码 {exit_code}；最后输出：{last}"


class LegacyRunner:
    """把任务参数翻译为 lstm_entry.py 调用，并管理子进程生命周期。"""

    def __init__(self, settings: Settings) -> None:
        self._settings = settings
        if settings.max_memory_mb > 0 and os.name != "posix":
            logger.warning("NC_LOTTERY_MAX_MEMORY_MB 仅在 Linux 生效（当前平台 %s）", os.name)

    @property
    def settings(self) -> Settings:
        return self._settings

    def preflight(self, kind: str, lottery_type: str, window: int) -> None:
        """入队前检查依赖/配置/模型，缺失时返回明确 HTTP 错误，不伪造任务。"""
        if kind not in ("train", "predict"):
            raise WorkerError("VALIDATION_ERROR", f"未知任务类型: {kind}", 400)
        if lottery_type not in SUPPORTED_LOTTERIES:
            raise WorkerError(
                "VALIDATION_ERROR",
                f"只支持 {', '.join(SUPPORTED_LOTTERIES)}，收到: {lottery_type}",
                400,
            )

        entry = self._settings.legacy_entry
        if not entry.is_file():
            raise WorkerError("LEGACY_NOT_FOUND", f"legacy 入口缺失: {entry}", 500)
        if not (self._settings.legacy_project / "src").is_dir():
            raise WorkerError(
                "LEGACY_NOT_FOUND",
                f"legacy 原版工程缺失: {self._settings.legacy_project}",
                500,
            )

        deps = dependency_status()
        missing = [name for name, ok in deps.items() if not ok]
        if missing:
            raise WorkerError(
                "DEPENDENCY_MISSING",
                f"运行时缺少依赖: {', '.join(missing)}；请重新构建镜像或安装 requirements.txt",
                503,
            )

        if not self._settings.db_configured:
            raise WorkerError(
                "DB_NOT_CONFIGURED",
                "数据库未配置：需要 NC_DB_HOST、NC_DB_NAME、NC_DB_USER、NC_DB_PASSWORD",
                503,
            )

        if kind == "predict":
            missing_models = self.missing_model_files(lottery_type, window)
            if missing_models:
                raise WorkerError(
                    "MODEL_NOT_FOUND",
                    "模型不存在或窗口不匹配："
                    f"{self._settings.model_window_dir(lottery_type, window)} 缺少 "
                    f"{', '.join(missing_models)}；请先 POST /jobs/train",
                    409,
                )

    def missing_model_files(self, lottery_type: str, window: int) -> list[str]:
        directory = self._settings.model_window_dir(lottery_type, window)
        return [name for name in MODEL_FILES if not (directory / name).is_file()]

    def run(
        self,
        job_id: str,
        kind: str,
        params: dict[str, Any],
        window: int,
        job_dir: Path,
        on_progress: ProgressCallback,
        on_log: LogCallback,
        cancel_event: threading.Event,
    ) -> dict[str, Any]:
        """执行一次 legacy 调用，成功返回 legacy 的原始 JSON 结果。"""
        lottery_type = str(params["lotteryType"])
        count = int(params.get("count") or 5)
        job_dir.mkdir(parents=True, exist_ok=True)
        log_path = job_dir / "job.log"

        cmd = self._build_command(kind, lottery_type, window, count)
        env = self._build_env()

        if cancel_event.is_set():
            raise WorkerError("JOB_CANCELLED", "任务已取消", 409)

        logger.info("job %s 启动: %s", job_id, " ".join(cmd))
        on_progress(5, "启动 legacy 子进程…")

        with log_path.open("a", encoding="utf-8") as log_fp:
            log_fp.write(f"\n===== job {job_id} {kind} {lottery_type} window={window} "
                         f"at {time.strftime('%Y-%m-%dT%H:%M:%S%z')} =====\n")
            log_fp.flush()
            try:
                proc = subprocess.Popen(
                    cmd,
                    cwd=str(self._settings.worker_root),
                    env=env,
                    stdout=subprocess.PIPE,
                    stderr=subprocess.STDOUT,
                    text=True,
                    encoding="utf-8",
                    errors="replace",
                    bufsize=1,
                    **_platform_popen_kwargs(),
                )
            except OSError as exc:
                raise WorkerError(
                    "LEGACY_SPAWN_FAILED",
                    f"无法启动 legacy 子进程: {exc}",
                    500,
                ) from exc

            result_candidates: list[dict[str, Any]] = []
            log_tail: deque[str] = deque(maxlen=40)
            output_lock = threading.Lock()
            progress_value = 5

            def _reader() -> None:
                nonlocal progress_value
                assert proc.stdout is not None
                for raw_line in proc.stdout:
                    line = raw_line.rstrip("\r\n")
                    if not line:
                        continue
                    log_fp.write(line + "\n")
                    log_fp.flush()

                    with output_lock:
                        log_tail.append(line)

                    on_log(line)

                    new_progress, message = _derive_progress(
                        kind, line, progress_value
                    )
                    if new_progress > progress_value or message:
                        progress_value = max(progress_value, new_progress)
                        on_progress(progress_value, message)

                    candidate = _parse_result_line(line)
                    if candidate is not None:
                        with output_lock:
                            result_candidates.append(candidate)

            reader = threading.Thread(
                target=_reader, name=f"job-{job_id}-reader", daemon=True
            )
            reader.start()

            timeout = (
                self._settings.train_timeout_sec
                if kind == "train"
                else self._settings.predict_timeout_sec
            )
            memory_limit = self._settings.max_memory_mb
            started = time.monotonic()
            cancelled = False
            timed_out = False
            memory_exceeded = False

            while proc.poll() is None:
                if cancel_event.is_set():
                    cancelled = True
                    logger.info("job %s 收到取消请求，终止子进程", job_id)
                    _stop_process(proc)
                    break

                if time.monotonic() - started > timeout:
                    timed_out = True
                    logger.warning("job %s 超时（%ss），终止子进程", job_id, timeout)
                    _stop_process(proc)
                    break

                if memory_limit > 0:
                    rss = _read_rss_mb(proc.pid)
                    if rss is not None and rss > memory_limit:
                        memory_exceeded = True
                        logger.warning(
                            "job %s RSS %.0fMB 超过上限 %sMB，终止子进程",
                            job_id,
                            rss,
                            memory_limit,
                        )
                        _stop_process(proc)
                        break

                time.sleep(0.5)

            exit_code = proc.wait()
            reader.join(timeout=10)
            with output_lock:
                tail_snapshot = list(log_tail)

        if cancelled:
            raise WorkerError("JOB_CANCELLED", "任务已取消", 409)
        if timed_out:
            raise WorkerError(
                "JOB_TIMEOUT",
                f"任务超过 {timeout} 秒未完成，已终止；可调大 NC_LOTTERY_*_TIMEOUT_SEC",
                504,
            )
        if memory_exceeded:
            raise WorkerError(
                "JOB_OUT_OF_MEMORY",
                f"任务 RSS 超过 NC_LOTTERY_MAX_MEMORY_MB={memory_limit}，已终止",
                500,
            )
        if exit_code != 0:
            raise WorkerError(
                f"{kind.upper()}_FAILED",
                _failure_message(tail_snapshot, exit_code),
                500,
            )

        with output_lock:
            candidates = list(result_candidates)
        if not candidates:
            raise WorkerError(
                "LEGACY_BAD_OUTPUT",
                "legacy 未输出可解析的 JSON 结果；"
                f"最后输出：{tail_snapshot[-1] if tail_snapshot else '(无)'}",
                500,
            )
        return candidates[-1]

    def _build_command(
        self, kind: str, lottery_type: str, window: int, count: int
    ) -> list[str]:
        settings = self._settings
        cmd = [
            sys.executable,
            str(settings.legacy_entry),
            kind,
            "--name",
            lottery_type,
            "--window",
            str(window),
        ]
        if kind == "predict":
            cmd += ["--count", str(count)]
        cmd += [
            "--db-host",
            str(settings.db_host),
            "--db-port",
            str(settings.db_port),
            "--db-name",
            str(settings.db_name),
            "--db-user",
            str(settings.db_user),
        ]
        return cmd

    def _build_env(self) -> dict[str, str]:
        settings = self._settings
        env = os.environ.copy()
        env.update(
            {
                "NEWCOSMOS_LOTTERY_HOME": str(settings.runtime_dir),
                "NC_LOTTERY_MODEL_DIR": str(settings.model_dir),
                "PGPASSWORD": settings.db_password or "",
                "NEWCOSMOS_DB_PASSWORD": settings.db_password or "",
                "PYTHONUNBUFFERED": "1",
                "PYTHONIOENCODING": "utf-8",
                "PYTHONDONTWRITEBYTECODE": "1",
                "TF_CPP_MIN_LOG_LEVEL": "2",
                "MPLBACKEND": "Agg",
            }
        )
        # 容器 cpus=1.5：限制 TF/OpenMP 线程，避免超卖导致整体变慢。
        env.setdefault("OMP_NUM_THREADS", "2")
        env.setdefault("TF_NUM_INTRAOP_THREADS", "2")
        env.setdefault("TF_NUM_INTEROP_THREADS", "1")
        return env


def _platform_popen_kwargs() -> dict[str, Any]:
    if os.name == "posix":
        return {"start_new_session": True}
    return {}


def _stop_process(proc: subprocess.Popen[str], grace_seconds: float = 10.0) -> None:
    """先 SIGTERM（整进程组），宽限后 SIGKILL。"""
    if proc.poll() is not None:
        return
    try:
        if os.name == "posix":
            os.killpg(os.getpgid(proc.pid), signal.SIGTERM)
        else:
            proc.terminate()
        proc.wait(timeout=grace_seconds)
    except (ProcessLookupError, subprocess.TimeoutExpired):
        try:
            if os.name == "posix":
                os.killpg(os.getpgid(proc.pid), signal.SIGKILL)
            else:
                proc.kill()
        except ProcessLookupError:
            pass
    except OSError:
        try:
            proc.kill()
        except OSError:
            pass


def _derive_progress(
    kind: str, line: str, current: int
) -> tuple[int, str]:
    """从日志行推导进度与阶段文案（仅展示用途）。"""
    match = EPOCH_RE.search(line)
    if match and kind == "train":
        epoch = int(match.group(1))
        total = max(1, int(match.group(2)))
        pct = min(95, 5 + int(90 * min(1.0, epoch / total)))
        return max(current, pct), f"训练中 Epoch {epoch}/{total}"

    if kind == "predict":
        if "已从 nc_lottery_draws 导出" in line:
            return max(current, 35), "已导出开奖数据"
        if "载入模型" in line:
            return max(current, 65), "已加载模型，推理中…"

    return current, ""


def _parse_result_line(line: str) -> dict[str, Any] | None:
    stripped = line.strip()
    if not (stripped.startswith("{") and stripped.endswith("}")):
        return None
    try:
        value = json.loads(stripped)
    except json.JSONDecodeError:
        return None
    if not isinstance(value, dict):
        return None
    if "predictions" in value or value.get("action") == "train":
        return value
    return None
