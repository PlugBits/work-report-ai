CREATE TABLE IF NOT EXISTS quick_notes (
    id TEXT NOT NULL PRIMARY KEY,
    created_at TEXT NOT NULL,
    text TEXT NOT NULL CHECK (length(trim(text)) > 0),
    deleted_at TEXT NULL
);

CREATE TABLE IF NOT EXISTS source_events (
    id TEXT NOT NULL PRIMARY KEY,
    occurred_at TEXT NOT NULL,
    source_type TEXT NOT NULL,
    title TEXT NOT NULL,
    body TEXT NOT NULL,
    evidence TEXT NOT NULL,
    source_ref TEXT NOT NULL,
    confidence REAL NOT NULL CHECK (confidence >= 0 AND confidence <= 1),
    content_hash TEXT NOT NULL,
    collected_at TEXT NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_source_events_content_hash
    ON source_events(content_hash);

CREATE TABLE IF NOT EXISTS report_candidates (
    id TEXT NOT NULL PRIMARY KEY,
    week_start TEXT NOT NULL,
    work_date TEXT NOT NULL,
    work_item TEXT NOT NULL,
    activity TEXT NOT NULL,
    result_or_next TEXT NOT NULL,
    status TEXT NOT NULL,
    confidence REAL NOT NULL CHECK (confidence >= 0 AND confidence <= 1),
    selected INTEGER NOT NULL CHECK (selected IN (0, 1)),
    edited INTEGER NOT NULL CHECK (edited IN (0, 1)),
    source_event_ids_json TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS settings (
    key TEXT NOT NULL PRIMARY KEY,
    value_encrypted TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_quick_notes_created_at ON quick_notes(created_at);
CREATE INDEX IF NOT EXISTS ix_report_candidates_week_start ON report_candidates(week_start);

PRAGMA user_version = 1;
