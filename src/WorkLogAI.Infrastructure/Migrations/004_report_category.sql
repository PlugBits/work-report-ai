ALTER TABLE report_candidates ADD COLUMN category TEXT NOT NULL DEFAULT 'internal';

PRAGMA user_version = 4;
