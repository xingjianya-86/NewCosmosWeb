"""内存任务队列与单并发执行器。

* 队列：``queue.Queue`` + 唯一 worker 线程，并发恒为 1。
* 状态：pending → running → succeeded / failed / cancelled。
* 持久化：任务日志与最终结果写入 ``<NC_LOTTERY_JOBS>/<jobId>/``；
  队列本身只在内存中，进程重启后未完成任务状态丢失（磁盘日志保留）。
"""

from __future__ import annotations

import json
import logging
import queue
import threading
import uuid
from dataclasses import dataclass, field
from datetime import datetime, timezone
from enum import Enum
from pathlib import Path
from typing import Any

from .config import Settings
from .errors import WorkerError
from .runner import LegacyRunner

logger = logging.getLogger("uvicorn.error")


class JobStatus(str, Enum):
    PENDING = "pending"
    RUNNING = "running"
    SUCCEEDED = "succeeded"
    FAILED = "failed"
    CANCELLED = "cancelled"


TERMINAL_STATUSES = {
    JobStatus.SUCCEEDED,
    JobStatus.FAILED,
    JobStatus.CANCELLED,
}


def utcnow_iso() -> str:
    return (
        datetime.now(timezone.utc)
        .isoformat(timespec="milliseconds")
        .replace("+00:00", "Z")
    )


@dataclass
class Job:
    job_id: str
    kind: str
    params: dict[str, Any]
    window: int
    directory: Path
    status: JobStatus = JobStatus.PENDING
    progress: int = 0
    message: str = "排队中"
    created_at: str = field(default_factory=utcnow_iso)
    started_at: str | None = None
    finished_at: str | None = None
    result: Any = None
    error: dict[str, str] | None = None
    logs: list[str] = field(default_factory=list)
    cancel_requested: bool = False

    def to_dict(self, log_limit: int = 100) -> dict[str, Any]:
        return {
            "jobId": self.job_id,
            "type": self.kind,
            "status": self.status.value,
            "progress": self.progress,
            "message": self.message,
            "params": self.params,
            "createdAt": self.created_at,
            "startedAt": self.started_at,
            "finishedAt": self.finished_at,
            "result": self.result,
            "error": self.error,
            "logTail": self.logs[-log_limit:] if log_limit >= 0 else [],
        }


class JobManager:
    def __init__(self, settings: Settings, runner: LegacyRunner) -> None:
        self._settings = settings
        self._runner = runner
        self._lock = threading.RLock()
        self._jobs: dict[str, Job] = {}
        self._queue: queue.Queue[str] = queue.Queue(maxsize=settings.max_pending_jobs)
        self._stop = threading.Event()
        self._cancel_events: dict[str, threading.Event] = {}
        self._thread = threading.Thread(
            target=self._loop, name="lottery-job-worker", daemon=True
        )
        self._thread.start()
        logger.info(
            "任务队列已启动：并发=1，容量=%s，模型目录=%s，任务目录=%s",
            settings.max_pending_jobs,
            settings.model_dir,
            settings.jobs_dir,
        )

    # ------------------------------------------------------------------ 提交

    def submit(self, kind: str, params: dict[str, Any], window: int) -> Job:
        job_id = uuid.uuid4().hex
        directory = self._settings.jobs_dir / job_id
        try:
            directory.mkdir(parents=True, exist_ok=True)
        except OSError as exc:
            raise WorkerError(
                "JOBS_DIR_NOT_WRITABLE",
                f"任务目录不可写: {directory}（检查 NC_LOTTERY_JOBS 挂载权限）: {exc}",
                500,
            ) from exc

        job = Job(
            job_id=job_id,
            kind=kind,
            params=params,
            window=window,
            directory=directory,
        )
        with self._lock:
            self._jobs[job_id] = job

        try:
            self._queue.put_nowait(job_id)
        except queue.Full as exc:
            with self._lock:
                self._jobs.pop(job_id, None)
            raise WorkerError(
                "QUEUE_FULL",
                f"任务队列已满（容量 {self._settings.max_pending_jobs}），请稍后重试",
                429,
            ) from exc

        self._persist(job)
        return job

    # ------------------------------------------------------------------ 查询

    def get(self, job_id: str) -> Job:
        with self._lock:
            job = self._jobs.get(job_id)
        if job is None:
            raise WorkerError("JOB_NOT_FOUND", f"任务不存在: {job_id}", 404)
        return job

    def list(self, status: str | None = None, limit: int = 50) -> list[Job]:
        with self._lock:
            jobs = list(reversed(self._jobs.values()))
        if status is not None:
            try:
                wanted = JobStatus(status)
            except ValueError as exc:
                raise WorkerError(
                    "VALIDATION_ERROR",
                    f"未知状态: {status}；可选 {', '.join(s.value for s in JobStatus)}",
                    400,
                ) from exc
            jobs = [job for job in jobs if job.status is wanted]
        return jobs[: max(1, limit)]

    def serialize(self, job: Job, log_limit: int = 100) -> dict[str, Any]:
        """在锁内生成快照，避免与 worker 线程追加日志/更新状态竞争。"""
        with self._lock:
            return job.to_dict(log_limit=log_limit)

    def stats(self) -> dict[str, Any]:
        with self._lock:
            pending = sum(
                1 for job in self._jobs.values() if job.status is JobStatus.PENDING
            )
            running = sum(
                1 for job in self._jobs.values() if job.status is JobStatus.RUNNING
            )
            total = len(self._jobs)
        return {
            "pending": pending,
            "running": running,
            "total": total,
            "capacity": self._settings.max_pending_jobs,
            "concurrency": 1,
        }

    # ------------------------------------------------------------------ 取消

    def cancel(self, job_id: str) -> Job:
        with self._lock:
            job = self._jobs.get(job_id)
            if job is None:
                raise WorkerError("JOB_NOT_FOUND", f"任务不存在: {job_id}", 404)
            if job.status in TERMINAL_STATUSES:
                raise WorkerError(
                    "JOB_NOT_CANCELLABLE",
                    f"任务已结束（{job.status.value}），无法取消",
                    409,
                )

            job.cancel_requested = True
            if job.status is JobStatus.PENDING:
                job.status = JobStatus.CANCELLED
                job.finished_at = utcnow_iso()
                job.message = "已取消（尚未开始）"
                event = None
            else:
                job.message = "正在取消…"
                event = self._cancel_events.get(job_id)

        if event is not None:
            event.set()
            return job

        self._persist(job)
        return job

    # ------------------------------------------------------------------ worker

    def _loop(self) -> None:
        while not self._stop.is_set():
            try:
                job_id = self._queue.get(timeout=0.5)
            except queue.Empty:
                continue
            try:
                job: Job | None = None
                with self._lock:
                    job = self._jobs.get(job_id)
                if job is None or job.status is not JobStatus.PENDING:
                    continue
                self._execute(job)
            except Exception:  # noqa: BLE001 - worker 线程不能死
                logger.exception("任务执行器未捕获异常（jobId=%s）", job_id)
                with self._lock:
                    job = self._jobs.get(job_id)
                    if job is not None and job.status not in TERMINAL_STATUSES:
                        job.status = JobStatus.FAILED
                        job.finished_at = utcnow_iso()
                        job.message = "内部错误"
                        job.error = {
                            "code": "INTERNAL_ERROR",
                            "message": "任务执行器内部错误，详见 worker 日志",
                        }
                if job is not None:
                    self._persist(job)
            finally:
                self._queue.task_done()

    def _execute(self, job: Job) -> None:
        cancel_event = threading.Event()
        with self._lock:
            job.status = JobStatus.RUNNING
            job.started_at = utcnow_iso()
            job.progress = 5
            job.message = "启动 legacy 子进程…"
            self._cancel_events[job.job_id] = cancel_event
        self._persist(job)

        def on_progress(percent: int, message: str) -> None:
            with self._lock:
                if job.status is not JobStatus.RUNNING:
                    return
                job.progress = max(job.progress, min(99, max(0, percent)))
                if message:
                    job.message = message

        def on_log(line: str) -> None:
            with self._lock:
                job.logs.append(line)
                overflow = len(job.logs) - self._settings.log_tail_lines * 2
                if overflow > 0:
                    del job.logs[:overflow]

        try:
            result = self._runner.run(
                job_id=job.job_id,
                kind=job.kind,
                params=job.params,
                window=job.window,
                job_dir=job.directory,
                on_progress=on_progress,
                on_log=on_log,
                cancel_event=cancel_event,
            )
            with self._lock:
                job.result = result
                job.status = JobStatus.SUCCEEDED
                job.progress = 100
                job.message = "完成"
                job.finished_at = utcnow_iso()
        except WorkerError as exc:
            with self._lock:
                job.error = {"code": exc.code, "message": exc.message}
                job.status = (
                    JobStatus.CANCELLED
                    if exc.code == "JOB_CANCELLED"
                    else JobStatus.FAILED
                )
                job.message = "已取消" if job.status is JobStatus.CANCELLED else "失败"
                job.finished_at = utcnow_iso()
        except Exception as exc:  # noqa: BLE001 - 兜底，任务不能卡在 running
            logger.exception("任务 %s 执行异常", job.job_id)
            with self._lock:
                job.status = JobStatus.FAILED
                job.message = "失败"
                job.error = {
                    "code": "INTERNAL_ERROR",
                    "message": f"未预期异常: {exc}",
                }
                job.finished_at = utcnow_iso()
        finally:
            with self._lock:
                self._cancel_events.pop(job.job_id, None)
            self._persist(job)

    # ------------------------------------------------------------------ 持久化

    def _persist(self, job: Job) -> None:
        """best-effort 落盘：job.json + result.json（不阻塞状态机）。"""
        try:
            with self._lock:
                payload = job.to_dict(log_limit=-1)
                result = job.result
            job.directory.mkdir(parents=True, exist_ok=True)
            (job.directory / "job.json").write_text(
                json.dumps(payload, ensure_ascii=False, indent=2),
                encoding="utf-8",
            )
            if result is not None:
                (job.directory / "result.json").write_text(
                    json.dumps(result, ensure_ascii=False, indent=2),
                    encoding="utf-8",
                )
        except OSError as exc:
            logger.warning("任务元数据写入失败（jobId=%s）: %s", job.job_id, exc)

    # ------------------------------------------------------------------ 关停

    def shutdown(self, timeout: float = 20.0) -> None:
        self._stop.set()
        with self._lock:
            running = list(self._cancel_events.values())
        for event in running:
            event.set()
        self._thread.join(timeout=timeout)
        if self._thread.is_alive():
            logger.warning("任务线程未在 %.0fs 内退出，进程退出时将强制中断", timeout)
