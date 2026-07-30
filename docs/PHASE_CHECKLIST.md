# Phase completion checklist

## Phase 1 — input tool

- [x] Buildable .NET 8 solution with Core, Infrastructure, WPF App, and Tests
- [x] Windows tray application with the required five menu entries
- [x] Win32 global `Ctrl + Alt + W` registration and unregister-on-exit
- [x] One-line, keyboard-only capture with Enter/Esc/Ctrl+Enter behavior
- [x] Automatic timestamp, initial focus, and approximately 0.8 second save feedback
- [x] SQLite persistence under Local Application Data through an injectable provider
- [x] All four specification tables and idempotent migration
- [x] Note creation, weekly list, soft deletion, and reopen
- [x] Configurable week-start calculation and weekly history navigation
- [x] Deterministic manual mapping with no invented result
- [x] Manual ClosedXML export with exact Japanese filename and four-column layout
- [x] Landscape, one-page-wide, wrapped, chronological Excel output
- [x] Isolated `--sample-data` database and idempotent sample seed
- [x] Non-secret settings boundary rejects secret-like keys
- [x] Automated migration, persistence/reopen, date-range, mapping, settings, and
      Excel layout tests
- [x] README and architecture notes

## Phase 2 — local lookback

- [x] Shared collector contract, per-source result, and failure-isolating coordinator
- [x] Stable SHA-256 source-event hashes and SQLite insert-if-new deduplication
- [x] Weekly source-event list and transactional candidate replacement
- [x] Deterministic pending-only local mapping with blank results and evidence IDs
- [x] Manual notes represented as authoritative, selected local events
- [x] Configured local Git repositories only
- [x] Shell-free bounded Git runner with configured author filtering
- [x] Git commit metadata, safe summary, filenames, line statistics, and working tree
- [x] No Git diff or source content ingestion
- [x] Streaming Codex JSONL parser with 64 KiB line bound
- [x] Strict Codex allowlist and explicit forbidden-record rejection
- [x] Function name only; no arguments or function output
- [x] Secret redaction, source-code filtering, and 256 KiB selected-content cap
- [x] Configured recent-file folders with metadata-only collection
- [x] Sensitive file and generated/VCS directory exclusions
- [x] Per-source warning summary without losing successful collections
- [x] User-triggered, non-concurrent tray collection with responsive async UI
- [x] Read-only Phase 2 candidate list
- [x] Non-secret newline-delimited path settings
- [x] Idempotent isolated sample source event and candidate
- [x] Deduplication, mapping/evidence, persistence, Git safety, Codex safety/cap,
      recent-file exclusion, coordinator-repeat, and Phase 1 regression tests

## Phase 3 — AI generation and review

- [x] Ordered idempotent migration 001 → 002 with upgrade coverage
- [x] Candidate confirmation/origin persistence and atomic review saves
- [x] Regeneration preserves local, manual, and edited candidates
- [x] Windows Credential Manager abstraction and fixed generic target
- [x] API key never stored or revealed through SQLite/settings/request body
- [x] Configurable model (`gpt-5.6-sol` default) and send-preview toggle
- [x] Bounded deterministic outbound event selection and final redaction
- [x] No sourceRef/full paths/source code/diffs/function output/logs/credentials
- [x] Exact `/v1/responses`, `store:false`, no tools, strict `text.format` schema
- [x] Robust all-item `output_text` aggregation
- [x] Safe refusal/incomplete/error/empty/malformed response handling
- [x] Independent date/status/confidence/text/evidence/confirmation validation
- [x] Conservative deterministic merge with complete evidence union
- [x] Card review with adoption, low-confidence filter, merge, manual row, and edits
- [x] Human-readable evidence inspection without credential display
- [x] Selected-only chronological Excel export
- [x] Mocked request/response, credentials, migration, regeneration, merge, and export
      tests; no live API test

## Explicitly not implemented after Phase 3

- [ ] Phase 4: Microsoft Graph, Outlook mail, and calendar
- [ ] Phase 5: installer, auto-start, recovery, log rotation, and updates

There are no Graph permissions/packages, Outlook/calendar/email collectors, GitHub
network calls, token fields, installer, or auto-update code. The only secret store is
Windows Credential Manager, and the only external call is an explicit user-approved
OpenAI Responses request.
