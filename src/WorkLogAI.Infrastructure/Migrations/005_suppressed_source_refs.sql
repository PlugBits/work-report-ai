CREATE TABLE suppressed_source_refs (
    source_ref TEXT NOT NULL PRIMARY KEY,
    suppressed_at TEXT NOT NULL
);

PRAGMA user_version = 5;
