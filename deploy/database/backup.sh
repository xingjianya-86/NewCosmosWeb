#!/usr/bin/env bash
# =============================================================================
# NewCosmos PostgreSQL 备份（在 .2 宿主机运行，不在容器/公开目录里）
# -----------------------------------------------------------------------------
# 功能：
#   - pg_dump -Fc（custom 格式，可 pg_restore 选择性恢复）
#   - 先写 .partial，success 后原子改名，失败不留半截文件
#   - pg_restore --list 校验归档可读
#   - SHA256 校验文件
#   - 按天清理旧备份
#   - 拒绝把备份写进 nginx 公开根目录
#
# 用法：
#   sudo install -m 0755 deploy/database/backup.sh /usr/local/sbin/newcosmos-pg-backup
#   sudo install -m 0600 deploy/database/backup.env.example /etc/newcosmos/backup.env
#   sudo vi /etc/newcosmos/backup.env        # 填真实密码
#   sudo /usr/local/sbin/newcosmos-pg-backup
#
# cron（/etc/cron.d/newcosmos-pg-backup，注意文件末尾要留空行）：
#   15 2 * * * root /usr/local/sbin/newcosmos-pg-backup >> /var/log/newcosmos-pg-backup.log 2>&1
#
# 恢复（先恢复到临时库验证，不要直接覆盖生产库）：
#   createdb -h 127.0.0.1 -U postgres newcosmos_restore
#   pg_restore -h 127.0.0.1 -U postgres -d newcosmos_restore \
#       --clean --if-exists --no-owner /srv/newcosmos/backups/postgres/newcosmos_YYYYMMDD_HHMMSS.dump
#
# 密码只从环境文件读取，绝不写进本脚本或 cron 行。
# =============================================================================
set -Eeuo pipefail
IFS=$'\n\t'

# cron 的 PATH 很短；显式补上常见目录。
export PATH="/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:${PATH:-}"

ENV_FILE="${NC_BACKUP_ENV_FILE:-/etc/newcosmos/backup.env}"
if [[ -r "$ENV_FILE" ]]; then
    set -a
    # shellcheck disable=SC1090
    . "$ENV_FILE"
    set +a
fi

log()  { printf '[%s] %s\n' "$(date '+%F %T')" "$*" >&2; }
die()  { log "ERROR: $*"; exit 1; }

: "${NC_DB_PASSWORD:?NC_DB_PASSWORD 未设置（放到 /etc/newcosmos/backup.env，chmod 600）}"

BACKUP_DIR="${NC_BACKUP_DIR:-/srv/newcosmos/backups/postgres}"
RETENTION_DAYS="${NC_BACKUP_RETENTION_DAYS:-14}"

PGHOST="${NC_DB_HOST:-127.0.0.1}"
PGPORT="${NC_DB_PORT:-5432}"
PGDATABASE="${NC_DB_NAME:-newcosmos}"
PGUSER="${NC_DB_USER:-newcosmos}"

export PGPASSWORD="$NC_DB_PASSWORD"
export PGCONNECT_TIMEOUT="${PGCONNECT_TIMEOUT:-10}"
export PGSSLMODE="${NC_DB_SSLMODE:-prefer}"
export PGTZ="${PGTZ:-UTC}"

umask 077

[[ "$RETENTION_DAYS" =~ ^[0-9]+$ ]] || die "NC_BACKUP_RETENTION_DAYS 必须是整数：$RETENTION_DAYS"
(( RETENTION_DAYS >= 1 )) || die "NC_BACKUP_RETENTION_DAYS 必须 >= 1"

for cmd in pg_dump pg_restore sha256sum find; do
    command -v "$cmd" >/dev/null 2>&1 || die "缺少命令：$cmd"
done

# 备份绝不能落到 nginx 公开目录。
RESOLVED_DIR="$(readlink -m "$BACKUP_DIR")"
case "$RESOLVED_DIR" in
    /srv/newcosmos/web|/srv/newcosmos/web/*|/var/www|/var/www/*|/usr/share/nginx/html|/usr/share/nginx/html/*)
        die "备份目录不能位于公开 Web 根目录：$RESOLVED_DIR"
        ;;
esac

install -d -m 0700 "$BACKUP_DIR"
chmod 700 "$BACKUP_DIR"

AVAIL_KB="$(df -Pk "$BACKUP_DIR" | awk 'NR==2 {print $4}')"
if [[ -n "$AVAIL_KB" ]] && (( AVAIL_KB < 5 * 1024 * 1024 )); then
    log "WARN: 剩余空间不足 5GB（${AVAIL_KB}KB），请清理旧备份或扩容"
fi

TS="$(date '+%Y%m%d_%H%M%S')"
OUT="$BACKUP_DIR/${PGDATABASE}_${TS}.dump"
TMP="${OUT}.partial"

cleanup() {
    [[ -f "$TMP" ]] && rm -f -- "$TMP"
}
trap cleanup EXIT

log "开始备份 $PGDATABASE -> $OUT"

DUMP_CMD=(
    pg_dump
    --format=custom
    --compress=6
    --no-password
    --lock-wait-timeout=30s
    --host="$PGHOST"
    --port="$PGPORT"
    --username="$PGUSER"
    --dbname="$PGDATABASE"
    --file="$TMP"
)

# 降低备份对业务查询的 CPU/IO 影响（ionice 不存在时仅 nice）。
if command -v ionice >/dev/null 2>&1; then
    nice -n 10 ionice -c 2 -n 7 "${DUMP_CMD[@]}"
else
    nice -n 10 "${DUMP_CMD[@]}"
fi

[[ -s "$TMP" ]] || die "pg_dump 产物为空"

# 校验归档目录可读；失败则删除产物，不留下“看起来成功”的坏备份。
if ! pg_restore --list "$TMP" >/dev/null 2>&1; then
    die "备份校验失败（pg_restore --list 无法读取）"
fi

TABLE_COUNT="$(pg_restore --list "$TMP" | grep -c 'TABLE DATA' || true)"
if (( TABLE_COUNT == 0 )); then
    log "WARN: 归档中没有任何 TABLE DATA，检查是否连错库"
fi

mv -- "$TMP" "$OUT"
trap - EXIT

chmod 600 "$OUT"
sha256sum -- "$OUT" > "${OUT}.sha256"
chmod 600 "${OUT}.sha256"

SIZE_MB="$(du -m "$OUT" | awk '{print $1}')"
log "备份完成：$OUT（${SIZE_MB}MB，${TABLE_COUNT} 个表数据段）"

# 保留 N 天：按文件修改时间清理 .dump 及其 .sha256。
mapfile -d '' OLD_FILES < <(
    find "$BACKUP_DIR" -maxdepth 1 -type f \
        \( -name '*.dump' -o -name '*.dump.sha256' \) \
        -mtime +"$RETENTION_DAYS" -print0
)

if (( ${#OLD_FILES[@]} > 0 )); then
    for old in "${OLD_FILES[@]}"; do
        [[ -n "$old" ]] || continue
        rm -f -- "$old"
    done
fi

log "清理旧备份：删除 ${#OLD_FILES[@]} 个超过 ${RETENTION_DAYS} 天的文件"
log "当前备份目录：$(find "$BACKUP_DIR" -maxdepth 1 -type f -name '*.dump' | wc -l) 个 dump"

if command -v logger >/dev/null 2>&1; then
    logger -t newcosmos-pg-backup "ok ${OUT} ${SIZE_MB}MB"
fi
