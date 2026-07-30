# Architecture

## Project boundaries

- `WorkLogAI.Core` (`net8.0`) contains domain records, storage/export abstractions,
  week calculation, non-secret settings policy, collection coordination,
  deterministic mapping, the weekly coverage calculator, and the reminder/auto-start
  decision contracts (`ReminderPlanner`, `IStartupRegistrar`). It has no UI or
  database dependency.
- `WorkLogAI.Infrastructure` (`net8.0-windows`) provides SQLite persistence,
  embedded SQL migration, default/injectable database paths, isolated sample-data
  seeding, ClosedXML export, local Git/Codex/file collectors, Credential Manager
  interop, bounded prompt construction, the Responses API client, MSAL-based
  Microsoft Graph sign-in and token caching, and the Outlook mail/calendar REST
  collectors.
- `WorkLogAI.App` (`net8.0-windows`) is the WPF tray host. It owns the Win32 hotkey,
  quick capture, weekly history, the weekday reminder timer, settings, and UI
  composition.
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

The review window's **記入状況** coverage bar is computed by the pure
`WeekCoverageCalculator`, which folds the loaded candidates' work dates against the
requested `WeekRange` into one `DayCoverage` per day (date, day-of-week,
candidate count, weekday flag). `CandidateWindow` renders all 7 days every time
cards are rendered; a day with zero candidates is an interactive button
(highlighted more strongly for weekdays than weekends) that calls the same manual
row path as the **1行追加** action, pre-filled with that day's date.

## Phase 4 Microsoft Graph flow

Two independent paths touch Microsoft Graph:

```text
settings window (user-initiated)
        |
        v
GraphAuthService.SignInAsync -- MSAL AcquireTokenInteractive (system browser)
        |
        v
DPAPI-encrypted MSAL token cache file (%LOCALAPPDATA%\WorkLog AI\Auth\msal.cache)

tray collection action
        |
        v
OutlookSentMailCollector / OutlookCalendarCollector
        |
        v
GraphAuthService.GetAccessTokenAsync -- MSAL AcquireTokenSilent (cache only)
        |                                    |
        | token                              | MsalUiRequiredException / no account
        v                                    v
Graph REST GET (bounded pages)      per-source "Microsoftサインインが必要です" error
        |
        v
GraphMailParser / GraphCalendarParser -- SourceEvent
        |
        v
existing LocalCollectionCoordinator dedup/mapping pipeline
```

Collectors never call `AcquireTokenInteractive`; only the settings window does, so a
background collection run can never pop a sign-in browser window. When silent
acquisition fails, the collector reports one Japanese error string as its per-source
result instead of throwing, matching the coordinator's failure-isolation contract
used since Phase 2.

`GraphAuthService`'s MSAL token cache is registered with `SetBeforeAccess`/
`SetAfterAccess` callbacks that round-trip through `TokenCacheSerializer`, a pure
file/DPAPI helper kept encryption-injectable for testing. The cache file lives at
`GraphAuthService.DefaultCacheFilePath` under `%LOCALAPPDATA%\WorkLog AI\Auth`,
named `msal.cache` in production and `msal.sample.cache` under `--sample-data`.

`OutlookSentMailCollector` and `OutlookCalendarCollector` call the Graph REST API
directly (no Graph SDK dependency) through an injected `HttpClient`. Both bound
paging with `GraphCollectorSupport.MaximumPages` (10) and read each page through
`ReadBoundedAsync`, which throws past a 4 MB cumulative response size. Non-2xx
responses record only the numeric status code as the error text; response bodies
are never surfaced.

`GraphMailParser` filters `SentItems` by `sentDateTime` (server-side `$filter`),
drops auto-reply/empty-body `RE:` messages via a subject-prefix allowlist, and hands
the raw body to `GraphMailBodyExtractor`, which strips HTML tags/entities and
truncates at the first quoted-reply marker (`-----Original Message-----`, a
`From:`/`差出人:` line, `> ` quoting, or an "On ... wrote:" line) before
`SafeTextSanitizer` redaction and a 2000-character cap. Each mail candidate is
created with confidence 0.7.

`GraphCalendarParser` reads `calendarView`, skips `isCancelled` events, and builds
evidence from the UTC time range (requests set `Prefer: outlook.timezone="UTC"`) or
`終日` for all-day events plus the location display name. The body preview goes
through the same extractor with a 500-character cap. Each calendar candidate is
created with confidence 0.5; because a calendar entry only proves a meeting was
scheduled and not that work was completed, calendar-derived candidates stay
`pending` and are never mapped to a completed/ongoing status.

Both collectors emit `SourceEvent`s through the same `SourceEventFactory.Create`
path as every other collector, so the new `outlook_mail`/`calendar` source types get
identical SHA-256 dedup, deterministic candidate mapping, and coordinator failure
isolation as Phase 2 sources — no separate code path exists downstream of parsing.
`AppServices.CollectLocalSourcesAsync` adds the two Graph collectors to the same
collector list only when `graph.mail_enabled`/`graph.calendar_enabled` is set.

Settings gain four new non-secret keys: `graph.client_id`, `graph.tenant_id`
(defaults to `common`), `graph.mail_enabled`, and `graph.calendar_enabled`. They
require no schema change — the existing `settings` key/value table already stores
arbitrary non-secret preferences.

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

The capture window has one single-line `TextBox` and uses `SizeToContent.Height`
instead of a fixed outer height, so the client area stays usable regardless of
title-bar chrome or DPI scaling. Successful saves show a separate, non-interactive
toast for approximately 800 ms; the toast positions itself from the capture window's
`ActualWidth`/`ActualHeight`, falling back to `SystemParameters.WorkArea` when those
are not yet available (e.g. before the window has laid out).

A `DispatcherTimer` ticks every 60 seconds and calls the pure `ReminderPlanner`
(enabled flag, weekday check, configured time gate, zero-notes-today check,
once-per-day via a stored last-shown date) to decide whether to show a tray balloon
prompting the user to record a note; clicking the balloon opens quick capture. The
last-shown date is persisted before the balloon is raised, as plain
`reminder.last_shown_date` settings state, so a restart mid-day cannot re-trigger
the same day's reminder.

`WindowsStartupRegistrar` (`WorkLogAI.Infrastructure`, behind the `IStartupRegistrar`
Core contract) implements opt-in auto-start by writing the current process path to
the `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` key. It rejects the dotnet
host (`dotnet`/`dotnet.exe`) with a Japanese error, since only a published,
self-contained EXE has a stable path to register; the settings window also disables
the checkbox entirely under `--sample-data`.

## Excel contract

ClosedXML writes one worksheet named `業務週報`, a Japanese title and identity row,
and exactly four report columns. Rows are chronological, wrapped, bordered, and
configured for landscape printing at one page wide.

## Security and deferred integrations

No secret is stored in SQLite, configuration, or logs. The settings store rejects
secret-like keys at its public boundary. The schema column remains named
`value_encrypted` to match the supplied specification, but it contains only
non-secret preferences. The OpenAI API key remains the only secret stored in
Windows Credential Manager, at the fixed `WorkLog AI/OpenAI API Key` target.

Microsoft Graph tokens are a deliberate, documented deviation from the MVP
specification. The specification calls for Windows Credential Manager, but a
generic credential blob caps at 2560 bytes, smaller than an MSAL token cache can
require. `GraphAuthService` instead persists the MSAL cache as a
DPAPI-encrypted (`DataProtectionScope.CurrentUser`) file at
`%LOCALAPPDATA%\WorkLog AI\Auth\msal.cache` (`msal.sample.cache` under
`--sample-data`), via `TokenCacheSerializer`. Tokens never enter SQLite, settings,
or logs; `graph.client_id`, `graph.tenant_id`, and the mail/calendar enable flags
stored in settings are non-secret configuration, not credentials.

Phase 4 adds exactly two new external request types, both delegated, read-only, and
gated by explicit user enablement: Graph REST `GET` calls for sent mail and
calendar, made only when the corresponding settings toggle is on and only during an
explicit collection run (or an explicit settings sign-in). Interactive sign-in is
never triggered by a background timer or collector. Combined with the explicit
user-approved Responses API call, WorkLog AI now performs three kinds of outbound
network request, all user-initiated and all excluding source code, diffs, full
message/event bodies beyond their sanitized/capped reduction, and credentials.

GitHub network APIs, installers, crash recovery, log rotation, and automatic update
implementation remain absent. Local collectors (Git, Codex, recent-file) still
perform no network operation of their own.
