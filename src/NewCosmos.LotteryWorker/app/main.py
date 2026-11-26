"""FastAPI 入口。

本地启动（开发）：

    set NC_DB_HOST=... & set NC_LOTTERY_MODEL_DIR=... & set NC_LOTTERY_JOBS=...
    uvicorn app.main:app --host 127.0.0.1 --port 8790 --workers 1

生产使用 deploy/lottery/docker-compose.yml；必须保持 ``--workers 1``，
否则每个进程都会各自启一个执行线程，突破「并发 1」与内存预算。
"""

from __future__ import annotations

import json
import logging
from contextlib import asynccontextmanager
from pathlib import Path

from fastapi import FastAPI, Query, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse

from .config import SERVICE_NAME, VERSION, load_settings
from .errors import WorkerError
from .jobs import JobManager, utcnow_iso
from .runner import LegacyRunner, dependency_status
from .schemas import PredictRequest, TrainRequest

logger = logging.getLogger("uvicorn.error")


@asynccontextmanager
async def lifespan(app: FastAPI):
    settings = load_settings()
    runner = LegacyRunner(settings)
    manager = JobManager(settings, runner)
    app.state.settings = settings
    app.state.runner = runner
    app.state.manager = manager
    logger.info(
        "%s %s 就绪：modelDir=%s jobsDir=%s maxMemoryMb=%s defaultWindow=%s",
        SERVICE_NAME,
        VERSION,
        settings.model_dir,
        settings.jobs_dir,
        settings.max_memory_mb,
        settings.default_window,
    )
    try:
        yield
    finally:
        manager.shutdown()


app = FastAPI(
    title=SERVICE_NAME,
    version=VERSION,
    description="NewCosmos 彩票 LSTM Worker（内存队列 + 单并发）",
    lifespan=lifespan,
)


# ---------------------------------------------------------------- 错误格式


@app.exception_handler(WorkerError)
async def _worker_error_handler(_request: Request, exc: WorkerError) -> JSONResponse:
    return JSONResponse(
        status_code=exc.status_code,
        content={"code": exc.code, "message": exc.message},
    )


@app.exception_handler(RequestValidationError)
async def _validation_error_handler(
    _request: Request, exc: RequestValidationError
) -> JSONResponse:
    parts: list[str] = []
    for error in exc.errors():
        loc = ".".join(str(item) for item in error.get("loc", ()) if item != "body")
        parts.append(f"{loc}: {error.get('msg', '参数不合法')}" if loc else str(error.get("msg")))
    return JSONResponse(
        status_code=422,
        content={
            "code": "VALIDATION_ERROR",
            "message": "; ".join(parts) or "参数不合法",
        },
    )


# ---------------------------------------------------------------- 健康检查


@app.get("/health")
def health(request: Request) -> dict:
    settings = request.app.state.settings
    manager: JobManager = request.app.state.manager
    deps = dependency_status()
    missing = [name for name, ok in deps.items() if not ok]
    return {
        "status": "ok" if not missing else "degraded",
        "service": SERVICE_NAME,
        "version": VERSION,
        "time": utcnow_iso(),
        "queue": manager.stats(),
        "dependencies": deps,
        "missingDependencies": missing,
        "paths": {
            "modelDir": str(settings.model_dir),
            "jobsDir": str(settings.jobs_dir),
        },
        "limits": {
            "concurrency": 1,
            "maxMemoryMb": settings.max_memory_mb,
            "defaultWindow": settings.default_window,
            "maxPendingJobs": settings.max_pending_jobs,
        },
    }


# ---------------------------------------------------------------- 模型


def _read_metadata(path: Path) -> dict:
    try:
        if path.is_file():
            data = json.loads(path.read_text(encoding="utf-8"))
            if isinstance(data, dict):
                return data
    except (OSError, ValueError):
        logger.warning("模型元数据读取失败: %s", path)
    return {}


@app.get("/models")
def list_models(request: Request) -> dict:
    """列出已训练模型（供 Server 判定「模型就绪」，不触发任何训练）。"""
    settings = request.app.state.settings
    models: list[dict] = []
    root = settings.model_dir
    if root.is_dir():
        for type_dir in sorted(root.iterdir()):
            if not type_dir.is_dir():
                continue
            lottery_type = type_dir.name.lower()
            if lottery_type not in ("ssq", "dlt"):
                continue
            for window_dir in sorted(type_dir.glob("window_*")):
                if not window_dir.is_dir():
                    continue
                if not (window_dir / "red.keras").is_file() or not (window_dir / "blue.keras").is_file():
                    continue
                try:
                    window = int(window_dir.name.split("_", 1)[1])
                except (IndexError, ValueError):
                    continue
                metadata = _read_metadata(window_dir / "metadata.json")
                models.append(
                    {
                        "lotteryType": lottery_type,
                        "window": window,
                        "trainedAt": metadata.get("trained_at") or metadata.get("timestamp"),
                        "metadata": metadata,
                    }
                )
    return {"models": models, "modelDir": str(root)}


# ---------------------------------------------------------------- 任务


@app.post("/jobs/train", status_code=202)
def enqueue_train(
    request: Request, body: TrainRequest | None = None
) -> JSONResponse:
    settings = request.app.state.settings
    runner: LegacyRunner = request.app.state.runner
    manager: JobManager = request.app.state.manager

    payload = body or TrainRequest()
    lottery_type = payload.lottery_type
    window = payload.window if payload.window is not None else settings.default_window

    runner.preflight("train", lottery_type, window)
    job = manager.submit(
        "train",
        {"lotteryType": lottery_type, "window": window},
        window,
    )
    return JSONResponse(status_code=202, content=manager.serialize(job))


@app.post("/jobs/predict", status_code=202)
def enqueue_predict(
    request: Request, body: PredictRequest | None = None
) -> JSONResponse:
    settings = request.app.state.settings
    runner: LegacyRunner = request.app.state.runner
    manager: JobManager = request.app.state.manager

    payload = body or PredictRequest()
    lottery_type = payload.lottery_type
    window = payload.window if payload.window is not None else settings.default_window

    runner.preflight("predict", lottery_type, window)
    job = manager.submit(
        "predict",
        {
            "lotteryType": lottery_type,
            "window": window,
            "count": payload.count,
        },
        window,
    )
    return JSONResponse(status_code=202, content=manager.serialize(job))


@app.get("/jobs")
def list_jobs(
    request: Request,
    status: str | None = Query(default=None),
    limit: int = Query(default=50, ge=1, le=200),
) -> dict:
    manager: JobManager = request.app.state.manager
    jobs = manager.list(status=status, limit=limit)
    return {"items": [manager.serialize(job, log_limit=20) for job in jobs]}


@app.get("/jobs/{job_id}")
def get_job(request: Request, job_id: str) -> dict:
    manager: JobManager = request.app.state.manager
    return manager.serialize(manager.get(job_id), log_limit=100)


@app.post("/jobs/{job_id}/cancel")
def cancel_job(request: Request, job_id: str) -> dict:
    manager: JobManager = request.app.state.manager
    return manager.serialize(manager.cancel(job_id), log_limit=100)
