"""HTTP 契约：任务入队 / 查询 / 取消 + 健康检查。

接口只监听本机回环或容器内部网络（部署见 deploy/lottery），不做用户认证；
认证/权限/数据范围全部由 NewCosmos.Server 负责。
"""

from __future__ import annotations

from typing import Literal

from pydantic import BaseModel, ConfigDict, Field


class TrainRequest(BaseModel):
    model_config = ConfigDict(extra="forbid", populate_by_name=True)

    lottery_type: Literal["ssq", "dlt"] = Field(default="ssq", alias="lotteryType")
    window: int | None = Field(default=None, ge=10, le=500)


class PredictRequest(BaseModel):
    model_config = ConfigDict(extra="forbid", populate_by_name=True)

    lottery_type: Literal["ssq", "dlt"] = Field(default="ssq", alias="lotteryType")
    window: int | None = Field(default=None, ge=10, le=500)
    count: int = Field(default=5, ge=1, le=20)
