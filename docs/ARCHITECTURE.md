# Architecture

## Project boundaries

- `WorkLogAI.Core` (`net8.0`) contains domain records, storage/export abstractions,
  week calculation, non-secret settings policy, collection coordination, and
  deterministic mapping. It has no UI or database dependency.
- `WorkLogAI.Infrastructure` (`net8.0-windows`) provides SQLite persistence,
  embedded SQL migration, default/injectable database paths, isolated sample-data
  seeding, ClosedXML export, local Git/Codex/file collectors, Credential Manager
  interop, bounded prompt construction, and the Responses API client.
- `WorkLogAI.App` (`net8.0-windows`) is the WPF tray host. It owns the Win32 hotkey,
  quick capture, weekly history, settings, and UI composition.
- `WorkLogAI.Tests` verifies boundaries and file output without using the production
  data directory.

## Phase 1 flow

```text
Ctrl+Alt+W / tray
        |
        v
QuickCaptureWindow -- IQuickNoteRepository --> SQLite quick_notes
        |
        v
HistoryWindow -- deterministic mapping --> ClosedXML weekly XLSX
```

Manual note mapping intentionally copies the note into the activity column, uses
`手動メモ` as the work item, preserves the captured local date, and leaves the
result/next-action column blank. It does not infer or invent a result.

## Phase 2 collection flow

```text
user tray action
      |
      v
Manual + Git + Codex JSONL + recent-file collectors
      |
      v
content-hash INSERT OR IGNORE --> source_events
      |
      v
deterministic pending mapping --> replace report_candidates for week
      |
      v
read-only CandidateWindow
```

The coordinator isolates collector failures and persists every successful source.
It then rebuilds the requested week's candidate set from the complete deduplicated
weekly event set. Candidate IDs are deterministic and every candidate retains its
source event ID list in JSON.

Git uses an injectable process runner and `ProcessStartInfo.ArgumentList`. It applies
the repository's configured `user.email`, or `user.name` fallback, as the author
filter. Only commit timestamps, safe subject/body summary, filenames, aggregate
added/deleted line counts, and uncommitted filenames are retained. No diff or file
content command is issued.

The Codex parser reads JSONL through a bounded byte-level line reader. Lines over
64 KiB and malformed JSON are skipped independently. A strict record allowlist keeps
only timestamp/cwd, user text, assistant final/completion text, changed filenames,
and allowlisted function names. Function arguments and outputs are never selected.
Reasoning, compacted/summary history, environment records, source code, and secret-like
text are ignored or redacted. Selection stops at 256 KiB of UTF-8 per session.

Recent-file collection traverses configured business folders without following
reparse points and opens no file bodies. It excludes `.env`, credentials, keys,
certificates, authentication files, VCS directories, and common generated folders.

## Phase 3 generation and review

```text
explicit tray action
  -> Phase 2 local collection
  -> credential check + optional metadata preview
  -> bounded/redacted weekly event package
  -> POST https://api.openai.com/v1/responses
       store:false, no tools, text.format strict JSON Schema
  -> aggregate output_text across all output items
  -> independent validation against sent IDs/week/status rules
  -> conservative deterministic merge
  -> preserve edited/manual/local candidates while replacing unedited AI rows
  -> card review -> selected-only XLSX
```

The JSON Schema sets `additionalProperties:false` on the root and candidate objects,
requires every candidate property, uses the exact status enum, bounded confidence,
nonempty evidence IDs, and a nullable confirmation question. API response status,
refusal, incomplete output, errors, empty output, malformed JSON, and invalid
evidence are converted to safe messages without echoing request or response bodies.

The outbound builder re-runs redaction and excludes `sourceRef`, full local paths,
source code, diffs, function output, full threads, and credentials. Selection is
deterministic and bounded by 200 events and 256 KiB by default. Truncation is exposed
to preview and completion UI.

The API key crosses only the `ICredentialStore` boundary and is stored at
`WorkLog AI/OpenAI API Key` in Windows Credential Manager. SQLite stores only the
model name and preview preference. The client adds the secret only to the
Authorization header; it never enters request JSON, settings, SQLite, or errors.

Migration `002_phase3_review.sql` adds confirmation and candidate-origin fields.
Regeneration deletes only unedited `origin='ai'` rows, retaining user edits, manual
rows, and local candidates. Review saves replace the week atomically.

## Persistence

Embedded SQL migrations run in version order inside a transaction according to
`PRAGMA user_version`. `001_initial.sql` creates the four specification tables and
`002_phase3_review.sql` upgrades existing Phase 1–2 databases without rewriting the
initial migration:

- `quick_notes`
- `source_events`
- `report_candidates`
- `settings`

Connections are short-lived and created from an injected path provider. Soft delete
sets `quick_notes.deleted_at`; reopen sets it back to `NULL`.

Production and sample modes use separate subdirectories under Local Application
Data. Tests inject temporary database paths.

## Windows integration

The WPF process stays alive without a main window and exposes a Windows notification
area icon. A message-only `HwndSource` registers `Ctrl+Alt+W` using
`RegisterHotKey`; disposal always calls `UnregisterHotKey`.

The capture window has one single-line `TextBox`. Successful saves show a separate,
non-interactive toast for approximately 800 ms, so no additional form field is
introduced.

## Excel contract

ClosedXML writes one worksheet named `業務週報`, a Japanese title and identity row,
and exactly four report columns. Rows are chronological, wrapped, bordered, and
configured for landscape printing at one page wide.

## Security and deferred integrations

No secret is stored in SQLite, configuration, or logs. The settings store rejects
secret-like keys at its public boundary. The schema column remains named
`value_encrypted` to match the supplied specification, but it contains only
non-secret preferences.

Phases 1–3 contain no Microsoft Graph, Outlook, calendar, GitHub network client,
email collector, installer, or auto-update implementation. The only external request
is the explicit user-approved Responses API call. Local collectors perform no network
operation.
