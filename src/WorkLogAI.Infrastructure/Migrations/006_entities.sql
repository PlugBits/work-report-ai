CREATE TABLE entities (
    id TEXT NOT NULL PRIMARY KEY,
    canonical_name TEXT NOT NULL UNIQUE COLLATE NOCASE,
    kind TEXT NOT NULL DEFAULT 'other',
    occurrence_count INTEGER NOT NULL DEFAULT 0,
    first_seen_at TEXT NOT NULL,
    last_seen_at TEXT NOT NULL,
    excluded INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL
);

CREATE TABLE entity_aliases (
    id TEXT NOT NULL PRIMARY KEY,
    entity_id TEXT NOT NULL REFERENCES entities(id),
    alias TEXT NOT NULL UNIQUE COLLATE NOCASE
);

CREATE INDEX ix_entity_aliases_entity_id
    ON entity_aliases(entity_id);

PRAGMA user_version = 6;
