CREATE TABLE IF NOT EXISTS meeting_sessions (
    id TEXT NOT NULL PRIMARY KEY,
    title TEXT NOT NULL,
    participants TEXT NOT NULL DEFAULT '',
    kind TEXT NOT NULL,
    started_at TEXT NOT NULL,
    ended_at TEXT NULL,
    status TEXT NOT NULL,
    created_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS meeting_lines (
    id TEXT NOT NULL PRIMARY KEY,
    session_id TEXT NOT NULL REFERENCES meeting_sessions(id),
    line_no INTEGER NOT NULL,
    marker TEXT NOT NULL,
    text TEXT NOT NULL,
    logged_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_meeting_lines_session_line
    ON meeting_lines(session_id, line_no);

CREATE TABLE IF NOT EXISTS meeting_summaries (
    id TEXT NOT NULL PRIMARY KEY,
    session_id TEXT NOT NULL REFERENCES meeting_sessions(id),
    formatted_json TEXT NOT NULL,
    summary_line TEXT NOT NULL,
    created_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_meeting_summaries_session
    ON meeting_summaries(session_id);

PRAGMA user_version = 3;
