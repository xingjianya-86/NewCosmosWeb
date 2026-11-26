# NewCosmos 彩票 Worker（CPU-only）
# 构建上下文 = src/NewCosmos.LotteryWorker（见 deploy/lottery/docker-compose.yml）
# 生产服务器无法直连 Docker Hub：基础镜像走 DaoCloud 镜像，pip 走阿里云镜像。
FROM docker.1ms.run/library/python:3.11-slim

ENV PYTHONUNBUFFERED=1 \
    PYTHONIOENCODING=utf-8 \
    PYTHONDONTWRITEBYTECODE=1 \
    LANG=C.UTF-8 \
    LC_ALL=C.UTF-8 \
    PIP_NO_CACHE_DIR=1 \
    PIP_DISABLE_PIP_VERSION_CHECK=1 \
    PIP_INDEX_URL=https://pypi.tuna.tsinghua.edu.cn/simple/ \
    PIP_TRUSTED_HOST=pypi.tuna.tsinghua.edu.cn \
    TF_CPP_MIN_LOG_LEVEL=2 \
    MPLBACKEND=Agg \
    OMP_NUM_THREADS=2 \
    TF_NUM_INTRAOP_THREADS=2 \
    TF_NUM_INTEROP_THREADS=1

# libgomp1：TensorFlow CPU 运行时依赖；其余为基础镜像自带
# 生产服务器无法直连 deb.debian.org，apt 源切阿里云镜像。
RUN sed -i 's|http://deb.debian.org/debian|https://mirrors.aliyun.com/debian|g; s|http://security.debian.org/debian-security|https://mirrors.aliyun.com/debian-security|g' \
        /etc/apt/sources.list /etc/apt/sources.list.d/*.sources 2>/dev/null || true \
    && apt-get update \
    && apt-get install -y --no-install-recommends libgomp1 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# 非 root 运行（uid/gid 10001）；模型/任务卷属主需一致或在宿主机 chown
RUN groupadd --gid 10001 lottery \
    && useradd --uid 10001 --gid lottery \
       --create-home --shell /usr/sbin/nologin lottery \
    && mkdir -p /models /jobs \
    && chown -R lottery:lottery /models /jobs

COPY requirements.txt ./requirements.txt
RUN pip install --no-cache-dir -r requirements.txt

COPY --chown=lottery:lottery app ./app
COPY --chown=lottery:lottery legacy ./legacy

USER lottery

EXPOSE 8790

# 单进程单执行器：--workers 必须为 1，否则并发/内存预算失效
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8790", "--workers", "1", "--log-level", "info"]

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD python -c "import urllib.request; urllib.request.urlopen('http://127.0.0.1:8790/health', timeout=4)"
