-- ============================================================================
-- 司南 · P7391 问题闭环系统 —— SQLite DDL 唯一真源
--
-- 约定：
-- 1) 时间统一存 UTC ISO-8601（yyyy-MM-ddTHH:mm:ss.fffZ），UI 再转本地；
-- 2) 日期语义值存 yyyy-MM-dd，原文另存 *_raw，保证 Excel 往返无损；
-- 3) 可同步实体一律带 version（乐观锁），更新必须 WHERE id=? AND version=?；
-- 4) 问题软删除（deleted_at），不物理删除；
-- 5) 修改本文件必须同时升 schema_version（app_meta），Android Room 同步改。
-- ============================================================================

PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS app_meta (
    key         TEXT PRIMARY KEY,
    value       TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS users (
    id              TEXT PRIMARY KEY,
    employee_no     TEXT NOT NULL UNIQUE,
    name            TEXT NOT NULL,
    password_hash   TEXT NOT NULL,
    role            TEXT NOT NULL DEFAULT 'engineer',  -- admin / engineer / viewer
    dept            TEXT NOT NULL DEFAULT '',
    phone           TEXT NOT NULL DEFAULT '',
    active          INTEGER NOT NULL DEFAULT 1,
    created_at      TEXT NOT NULL,
    updated_at      TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS projects (
    id          TEXT PRIMARY KEY,
    code        TEXT NOT NULL UNIQUE,
    name        TEXT NOT NULL,
    customer    TEXT NOT NULL DEFAULT '',
    status      TEXT NOT NULL DEFAULT 'active',      -- active / paused / done
    created_at  TEXT NOT NULL,
    updated_at  TEXT NOT NULL,
    version     INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS subprojects (
    id              TEXT PRIMARY KEY,
    project_id      TEXT NOT NULL REFERENCES projects(id),
    code            TEXT NOT NULL,
    name            TEXT NOT NULL,
    machine_range   TEXT NOT NULL DEFAULT '',
    status          TEXT NOT NULL DEFAULT 'active',
    sort_no         INTEGER NOT NULL DEFAULT 0,
    created_at      TEXT NOT NULL,
    updated_at      TEXT NOT NULL,
    version         INTEGER NOT NULL DEFAULT 1,
    UNIQUE (project_id, code)
);

CREATE TABLE IF NOT EXISTS machines (
    id              TEXT PRIMARY KEY,
    project_id      TEXT NOT NULL REFERENCES projects(id),
    subproject_id   TEXT REFERENCES subprojects(id),   -- NULL = 待归组（如 P73913118）
    code            TEXT NOT NULL,
    name            TEXT NOT NULL DEFAULT '',
    seq_no          INTEGER,
    status          TEXT NOT NULL DEFAULT 'active',
    created_at      TEXT NOT NULL,
    updated_at      TEXT NOT NULL,
    version         INTEGER NOT NULL DEFAULT 1,
    UNIQUE (project_id, code)
);

CREATE TABLE IF NOT EXISTS issues (
    id              TEXT PRIMARY KEY,
    project_id      TEXT NOT NULL REFERENCES projects(id),
    subproject_id   TEXT REFERENCES subprojects(id),
    machine_id      TEXT NOT NULL REFERENCES machines(id),
    seq_no          TEXT NOT NULL DEFAULT '',          -- 原表序号，不重排
    title           TEXT NOT NULL,
    solution        TEXT NOT NULL DEFAULT '',
    status          TEXT NOT NULL DEFAULT '待分派',     -- 待分派/方案中/设计中/备料中/装配中/待调整线缆后连续运行观察/已关闭/已取消
    priority        TEXT NOT NULL DEFAULT '中',         -- 高/中/低（原表无，新增）
    category        TEXT NOT NULL DEFAULT '',           -- 问题族/共性标签
    completion_text TEXT NOT NULL DEFAULT '',           -- 完成情况原文
    closed_at       TEXT,
    source_batch_id TEXT,
    source_row      INTEGER,
    created_by      TEXT,
    updated_by      TEXT,
    created_at      TEXT NOT NULL,
    updated_at      TEXT NOT NULL,
    version         INTEGER NOT NULL DEFAULT 1,
    deleted_at      TEXT
);

CREATE INDEX IF NOT EXISTS ix_issues_project   ON issues(project_id);
CREATE INDEX IF NOT EXISTS ix_issues_sub       ON issues(subproject_id);
CREATE INDEX IF NOT EXISTS ix_issues_machine   ON issues(machine_id);
CREATE INDEX IF NOT EXISTS ix_issues_status    ON issues(status);
CREATE INDEX IF NOT EXISTS ix_issues_deleted   ON issues(deleted_at);

CREATE TABLE IF NOT EXISTS issue_stages (
    id          TEXT PRIMARY KEY,
    issue_id    TEXT NOT NULL REFERENCES issues(id),
    stage_code  TEXT NOT NULL,                         -- plan / design / material / assembly
    owner_text  TEXT NOT NULL DEFAULT '',              -- 原文，多人用 "/" 分隔
    owner_ids   TEXT NOT NULL DEFAULT '',              -- 关联 users.id，逗号分隔
    due_date    TEXT,                                  -- yyyy-MM-dd，可空
    due_raw     TEXT NOT NULL DEFAULT '',              -- 原表原文：11.5 / 已下图 / / / 空
    state       TEXT NOT NULL DEFAULT 'not_started',   -- na/not_started/in_progress/done
    actual_date TEXT,
    remark      TEXT NOT NULL DEFAULT '',
    updated_at  TEXT NOT NULL,
    version     INTEGER NOT NULL DEFAULT 1,
    UNIQUE (issue_id, stage_code)
);

CREATE INDEX IF NOT EXISTS ix_stages_issue ON issue_stages(issue_id);

CREATE TABLE IF NOT EXISTS attachments (
    id              TEXT PRIMARY KEY,
    issue_id        TEXT NOT NULL REFERENCES issues(id),
    file_name       TEXT NOT NULL,
    stored_name     TEXT NOT NULL,
    stored_path     TEXT NOT NULL,                     -- 相对附件根目录
    thumb_path      TEXT NOT NULL DEFAULT '',
    size            INTEGER NOT NULL DEFAULT 0,
    mime            TEXT NOT NULL DEFAULT 'image/jpeg',
    sha256          TEXT NOT NULL DEFAULT '',
    uploaded_by     TEXT,
    created_at      TEXT NOT NULL,
    is_demo         INTEGER NOT NULL DEFAULT 0,
    sync_state      TEXT NOT NULL DEFAULT 'synced'     -- local / pending / synced / failed
);

CREATE INDEX IF NOT EXISTS ix_attachments_issue ON attachments(issue_id);

CREATE TABLE IF NOT EXISTS comments (
    id          TEXT PRIMARY KEY,
    issue_id    TEXT NOT NULL REFERENCES issues(id),
    user_id     TEXT,
    content     TEXT NOT NULL,
    created_at  TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_comments_issue ON comments(issue_id);

CREATE TABLE IF NOT EXISTS activity_log (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    entity      TEXT NOT NULL,
    entity_id   TEXT NOT NULL,
    field       TEXT NOT NULL,
    old_value   TEXT,
    new_value   TEXT,
    user_id     TEXT,
    created_at  TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_activity_entity ON activity_log(entity, entity_id);

CREATE TABLE IF NOT EXISTS import_batches (
    id          TEXT PRIMARY KEY,
    project_id  TEXT NOT NULL REFERENCES projects(id),
    file_name   TEXT NOT NULL,
    file_hash   TEXT NOT NULL DEFAULT '',
    strategy    TEXT NOT NULL DEFAULT 'merge',
    total       INTEGER NOT NULL DEFAULT 0,
    inserted    INTEGER NOT NULL DEFAULT 0,
    updated     INTEGER NOT NULL DEFAULT 0,
    skipped     INTEGER NOT NULL DEFAULT 0,
    warnings    INTEGER NOT NULL DEFAULT 0,
    created_by  TEXT,
    created_at  TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS devices (
    id              TEXT PRIMARY KEY,
    name            TEXT NOT NULL,
    model           TEXT NOT NULL DEFAULT '',
    token_hash      TEXT NOT NULL,
    paired_at       TEXT NOT NULL,
    last_sync_at    TEXT,
    active          INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS pair_codes (
    code        TEXT PRIMARY KEY,
    expires_at  TEXT NOT NULL,
    created_at  TEXT NOT NULL,
    used        INTEGER NOT NULL DEFAULT 0
);

-- 同步增量日志：任何业务写操作都追加一行；客户端按 seq 拉增量。
CREATE TABLE IF NOT EXISTS change_log (
    seq         INTEGER PRIMARY KEY AUTOINCREMENT,
    entity      TEXT NOT NULL,
    entity_id   TEXT NOT NULL,
    op          TEXT NOT NULL,                          -- upsert / delete
    payload     TEXT NOT NULL,
    device_id   TEXT,
    created_at  TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_change_entity ON change_log(entity, entity_id);

CREATE TABLE IF NOT EXISTS sync_state (
    device_id   TEXT PRIMARY KEY,
    last_cursor INTEGER NOT NULL DEFAULT 0,
    last_sync_at TEXT
);
