ALTER TABLE report_candidates ADD COLUMN needs_confirmation INTEGER NOT NULL DEFAULT 0;
ALTER TABLE report_candidates ADD COLUMN confirmation_question TEXT NULL;
ALTER TABLE report_candidates ADD COLUMN origin TEXT NOT NULL DEFAULT 'local';

CREATE INDEX IF NOT EXISTS ix_report_candidates_origin
    ON report_candidates(week_start, origin, edited);

PRAGMA user_version = 2;
