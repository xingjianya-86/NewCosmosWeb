"""Worker 统一错误类型。

对外 HTTP 错误统一为 ``{"code": "...", "message": "..."}``，与 NewCosmos.Server
的 ``{code, message, traceId}`` 约定保持同形（traceId 由 Server 层补）。
任务执行期的失败则写入 job.error，不使用异常做业务流控制。
"""

from __future__ import annotations


class WorkerError(Exception):
    """带错误码与建议 HTTP 状态码的可预期错误。"""

    def __init__(self, code: str, message: str, status_code: int = 400) -> None:
        super().__init__(message)
        self.code = code
        self.message = message
        self.status_code = status_code
