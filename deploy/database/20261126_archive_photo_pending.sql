-- 2026-11-26 待归属照片池（无身份证先拍照/选图，拿到身份证后再归属到档案照片）
-- 目标表：nc_biz_archive_photo_pending
-- 说明：PC 端（NewCosmosCode docs/migrations/20261125_archive_photo_pending.sql）已建同表；
--       Web 服务端照片中心「待归属」页直接读写该表，这里补齐缺失环境。
-- 全部使用 IF NOT EXISTS，可重复执行；执行前先备份。
--
-- 用法：
--   psql -h 10.110.0.2 -U newcosmos -d new_cosmos \
--     -f deploy/database/20261126_archive_photo_pending.sql

BEGIN;

CREATE TABLE IF NOT EXISTS nc_biz_archive_photo_pending (
    id                   bigserial PRIMARY KEY,
    photo_data           bytea NOT NULL,
    image_format         varchar(16) NOT NULL DEFAULT 'webp',
    photo_category       varchar(30) NOT NULL DEFAULT 'OTHER_CERT',
    photo_name           varchar(200),
    file_size            bigint NOT NULL DEFAULT 0,
    recognized_name      varchar(100),
    recognized_id_card   varchar(30),
    source               varchar(30),
    status               varchar(20) NOT NULL DEFAULT 'Pending',
    assigned_photo_id    bigint,
    assigned_at          timestamp,
    created_by           integer,
    created_by_name      varchar(100),
    created_at           timestamp NOT NULL DEFAULT NOW()
);

COMMENT ON TABLE nc_biz_archive_photo_pending IS '待归属照片池：身份证未定时先存，归属后写入 nc_biz_archive_photos';
COMMENT ON COLUMN nc_biz_archive_photo_pending.recognized_name IS 'OCR 识别姓名（可空）';
COMMENT ON COLUMN nc_biz_archive_photo_pending.recognized_id_card IS 'OCR 识别身份证号（可空）';
COMMENT ON COLUMN nc_biz_archive_photo_pending.status IS 'Pending待归属 / Assigned已归属';

CREATE INDEX IF NOT EXISTS idx_archive_photo_pending_status
    ON nc_biz_archive_photo_pending (status, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_archive_photo_pending_id_card
    ON nc_biz_archive_photo_pending (recognized_id_card);

COMMIT;
