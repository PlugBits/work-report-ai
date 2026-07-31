# Architecture

## Project boundaries

- `WorkLogAI.Core` (`net8.0`) contains domain records, storage/export abstractions,
  week calculation, non-secret settings policy, collection coordination,
  deterministic mapping, the weekly coverage calculator, and the reminder/auto-start
  decision contracts (`ReminderPlanner`, `IStartupRegistrar`). It has no UI or
  database dependency.
- `WorkLogAI.Infrastructure` (`net8.0-windows`) provides SQLite persistence,
  embedded SQL migration, default/injectable database paths, isolated sample-data
  seeding, ClosedXML export (weekly and monthly), local Git/Codex/file collectors,
  Credential Manager interop, bounded prompt construction, the Responses API
  client, the shared `TransientRetryPolicy`, `OpenAiKeyProbe`, MSAL-based
  Microsoft Graph sign-in and token caching, the Outlook mail/calendar REST
  collectors, meeting SQLite persistence and Markdown writer, the meeting AI
  formatting client/payload builder, the meeting-to-weekly-source collector, and
  `DataRetentionService`.
- `WorkLogAI.App` (`net8.0-windows`) is the WPF tray host. It owns the single-
  instance mutex, the Win32 hotkeys (quick capture and 議事録モード), quick
  capture, weekly history (including the **削除済みを表示** toggle and
  `QuickNoteEditWindow` note editing), the weekday reminder/daily-backup-check
  timer, the tabbed settings window, the month picker and monthly summary export
  action, meeting capture/session-chooser/send-preview windows,
  `DeleteCardConfirmWindow`'s row-only-vs-row-and-source review deletion choice,
  and UI composition.
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

`LocalCollectionCoordinator.RunAsync` loads `ISourceEventRepository.ListSuppressedSourceRefsAsync`
once per run into an in-memory set before touching any collector, then consults it
twice: newly collected events whose `SourceRef` is suppressed are skipped before
`InsertIfNewAsync` (so a permanently deleted origin — an amended git commit, a
still-readable quick note — is never re-inserted), and the weekly event list is
filtered against the same set before `SourceEventDeduplicator.LatestPerRef` and
mapping run, so a suppressed ref already sitting in `source_events` (e.g. from
before it was suppressed) is never mapped into a candidate either. Suppression is
a standing exclusion list, independent of and orthogonal to the content-hash
dedup — it survives across runs until explicitly reversed via
`UnsuppressSourceRefsAsync`.

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

`AiPromptBuilder.Instructions` additionally requires full coverage of manual quick
notes: every `sourceType: manual` evidence item must be reflected in at least one
candidate (consolidation across multiple same-work memos is allowed as long as
every contributing memo stays cited), memo wording must be rewritten into a
complete business report sentence rather than transcribed, `status`/result should
be inferred from completion-like wording in the memo itself (作成/完了/実施/
対応済み → `completed` with a stated result; 継続/進行中/検討中 → `ongoing`;
otherwise `pending`), and a low-confidence result must still be filled in with
`needsConfirmation: true` and a `confirmationQuestion` rather than left blank.
This changes only the natural-language instructions sent to the model — the JSON
Schema and wire format are unchanged.

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
row path as the **1行追加** action, pre-filled with that day's date. The bar folds
only *selected* candidates' work dates (`CandidateWindow.SelectedWorkDates()`), so
it stays consistent with the 出力される行 section and the export preview's
blank-weekday warning below — all three answer "what would an export right now
contain?" the same way.

## Weekly-review two-section card list, badges, and export preview

`CandidateWindow.RenderCards` splits the card list (after the existing day/low-
confidence filters) into two sections instead of one flat list:

```text
出力される行 ({n}件)      -- Selected == true, OrderBy(parsed WorkDateText),
                              unparseable dates sort last, stable within a date.
                              This section IS what MapSelected/the exporter will
                              produce.
除外中の行 ({m}件)        -- an Expander, collapsed by default, Selected == false,
   (Expander)                cards dimmed (#F3F4F6 background, 0.75 opacity)
```

Each `CandidateEditor` subscribes exactly once to its own `PropertyChanged`, at
creation time (`RegisterSelectionListener`, called from `LoadAsync`,
`MergeCandidates`, `AddManualRowAsync`, and `PersistAsync`'s post-save rebuild —
never re-subscribed on render). A `Selected` change queues a debounced
`Dispatcher.BeginInvoke(RenderCards)` rather than rendering inline, so the render
never re-enters while the checkbox click (or a `foreach` toggle like **全採用**)
that raised the change is still being processed; a `_renderQueued` flag collapses
a burst of changes into one re-render. The 除外中の行 `Expander`'s open/closed
state is tracked in a separate `_excludedExpanded` field and reapplied on every
render, since the `Expander` control itself is recreated each time.

Each card also gets a small colored origin badge, top-left of the header row
before the 採用 checkbox, resolved by the pure `CandidateBadge.Resolve(origin,
firstSourceType)` (Core) once per `CandidateEditor` at creation
(`CandidateWindow.ResolveBadge`, using the `_sourceEventsById` dictionary
populated at load time and kept current on manual-row add) and stored on the
editor rather than recomputed per render: `ai` → **AI** (`#2563EB`), `manual` →
**手動追加** (`#0D9488`), `local` → by its first backing source event's
`SourceTypes` (メモ/議事録/Git/Codex/ファイル/メール/予定, falling back to
ローカル). The App layer's only job is mapping `Badge.HexColor` to a
`SolidColorBrush` (`CandidateWindow.BrushFromHex`). A muted guidance line under
the action bar tells the user AI rows are normally what to keep, and that a
「メモ」 badge left in 出力される行 means the AI did not pick that memo up.

`LocalSourceEventMapper` (git/codex/file/mail/calendar → `Selected: false`;
manual/meeting → `Selected: true`) and `LocalCandidateSuppressor` (deselects
local rows fully superseded by AI evidence) are unchanged — under the two-section
layout their already-existing `Selected: false` output is what lands raw local
rows in 除外中の行 automatically, with no new data-layer behavior needed.

`Excel出力` no longer shows a bare blank-weekday confirmation `MessageBox`.
Instead, after `PersistAsync`/`TryBuildCandidates`/`MapSelected` and the
zero-rows check, `CandidateWindow.ExportAsync` computes the same blank-weekday
list (via `WeekCoverageCalculator.Calculate(_range, SelectedWorkDates())`) and
opens `ExportPreviewWindow` (modeled on `WeekPickerWindow`'s dialog style, but
700×600 and resizable) with the mapped `ReportRow`s, the `WeekRange`, and the
blank-weekday labels. The dialog renders a read-only `DailyReportGrouper.Group`
view — per day a bold `{yyyy/MM/dd} ({曜})` line, then per item
`{circled number} {WorkItem} — {Activity}` plus an indented `→ {ResultOrNext}`
line when non-blank — with an amber warning banner above it when any selected
weekday is blank. Cancel aborts the export; **出力** proceeds to the unchanged
`IWeeklyReportExporter.ExportAsync` call and `ExportResultPrompt`. The monthly
export flow (`App.xaml.cs`'s **月次まとめを出力…**) is untouched by this rework.

## Post-Phase 3 generation-quality and reliability flow

```text
AppServices.GenerateAiCandidatesAsync
      |
      v
SourceEventDeduplicator.LatestPerRef(weekly events) -- collapses same-sourceRef
      |                                                 reruns (e.g. a re-formatted
      |                                                 meeting) to the newest one
      v
AiPromptBuilder / LocalSourceEventMapper only ever see the reduced set
      |
      v
OpenAiResponsesClient.GenerateAsync -- HttpClient send wrapped in
      |                                 TransientRetryPolicy.ExecuteAsync
      v
CandidateMergeService.Merge -> Candidates.SaveGeneratedAsync
      |
      v
LocalCandidateSuppressor.SelectSupersededIds(merged AI candidates, week candidates)
      |
      v
Candidates.SetSelectedAsync(supersededIds, false) -- deselects fully-covered,
                                                       unedited local rows
      |
      v
CandidateWindow banner reports DeselectedLocalCount
```

`SourceEventDeduplicator.LatestPerRef` groups events by `SourceRef` (events with a
blank ref pass through untouched) and keeps only the newest per group, using
`CollectedAt` then `OccurredAt` then ordinal `Id` as a fully deterministic
tiebreak so two calls over the same input always agree. It never mutates stored
rows — both `LocalCollectionCoordinator.RunAsync` (before local mapping) and
`AiPromptBuilder.Build` (before prompt construction) apply it independently to
whatever they just read.

`TransientRetryPolicy.ExecuteAsync` wraps one HTTP attempt callback (the caller
must build a fresh `HttpRequestMessage` per attempt — a message cannot be sent
twice) and retries a 429, any 5xx, or a transport-level `HttpRequestException`/
per-attempt `TaskCanceledException` up to `DefaultDelays.Count` (2) more times,
honoring a bounded `Retry-After` header (delta or date form, capped at 30s) when
the server sends one and otherwise falling back to the fixed 2s/5s delays. The
caller's own cancellation is always rethrown rather than retried. Both
`OpenAiResponsesClient` (weekly generation) and `MeetingFormatClient` (議事録
AI整形) route their single HTTP send through this shared policy; non-retryable
statuses (400/401/403/404) return on the first attempt exactly as before the
policy existed.

`LocalCandidateSuppressor.SelectSupersededIds` unions the evidence (source event
ids) of every newly generated AI candidate, then scans the week's full candidate
list for rows that are local-origin, unedited, currently selected, and whose
entire evidence set is a subset of that union — those ids are returned for
deselection. A local row only partially covered by AI evidence is left alone, so
the AI can never silently drop a memo it did not fully account for; manual-origin
and already-edited rows are never touched regardless of coverage.
`AppServices.GenerateAiCandidatesAsync` calls it once per successful generation,
right after `SaveGeneratedAsync`, and folds the deselected count into
`AiGenerationResult.DeselectedLocalCount`, which `App.xaml.cs` appends to the
generation-summary banner text.

`OpenAiKeyProbe.ProbeAsync` is a small, independent liveness check: a bare
`GET /v1/models` with the candidate key as a bearer token and a 10s timeout,
collapsing every outcome to `Ok`/`Unauthorized`/`NetworkError` — the key and any
response body never leave the method. The settings AI tab's **APIキーをテスト**
button calls it against whatever is currently in the key field (or the stored
credential if the field is blank) and shows only the three-way Japanese result.

## Permanent deletion and history editing

```text
CandidateWindow 削除                      HistoryWindow 削除 / edit
      |                                          |
      v                                          v
DeleteCardConfirmWindow                   soft-delete note (deleted_at)
 行のみ削除 | 元データごと削除 | キャンセル          |
      |                                          v
      | (元データごと削除)                 SuppressSourceRefsAsync([quick-note:{id}])
      v                                    DeleteBySourceRefsAsync([quick-note:{id}])
DeleteByIdsAsync(source events)                  |
SuppressSourceRefsAsync(their refs)              v
SoftDeleteAsync(originating quick notes)   再開 --> UnsuppressSourceRefsAsync
      |
      v
suppressed_source_refs (never re-collected/re-mapped)
```

Review-window deletion of a collected/AI-origin card opens `DeleteCardConfirmWindow`,
a three-way modal (**行のみ削除** / **元データごと削除** / キャンセル) mirroring
`MeetingLineEditWindow`'s minimal dialog style. **行のみ削除** only removes the
in-memory row, same as before this feature — the underlying source event(s) and
any originating quick note are untouched, so a future collection/generation run
may surface the same row again. **元データごと削除** additionally queues the
row's backing source event ids, their non-blank `source_ref`s, and (for any
`quick-note:`-prefixed ref) the originating quick note id; `PersistAsync` flushes
these together after `SaveReviewAsync` succeeds — the backing events are deleted
via `DeleteByIdsAsync`, their refs are added to `suppressed_source_refs` via
`SuppressSourceRefsAsync`, and each originating note is soft-deleted via
`SoftDeleteAsync`, in that order, all wrapped in one try/catch logged to
`ErrorLog` under `CandidateWindow.SuppressSources`. Manual rows (`origin ==
manual`) keep the older, simpler flow — a plain Yes/No confirm that always fully
deletes the backing `review-manual:` event, since a manual row's only source is
that one event.

`HistoryWindow` gains a **削除済みを表示** checkbox (default unchecked, driving
`ListAsync(..., includeDeleted:)`) so deleted notes stay hidden from the normal
view instead of always being included. Deleting a note now also calls
`SuppressSourceRefsAsync`/`DeleteBySourceRefsAsync` for its deterministic
`quick-note:{id:D}` ref, matching the review window's permanent-deletion
mechanism so a deleted note cannot resurface via the next collection even though
its row is only soft-deleted (`deleted_at` set, not a hard SQL delete); **再開**
(reopen) calls `UnsuppressSourceRefsAsync` for the same ref, reversing the
suppression alongside clearing `deleted_at`. Double-clicking a non-deleted grid
row (`_grid.MouseDoubleClick`) opens `QuickNoteEditWindow`; on save,
`IQuickNoteRepository.UpdateTextAsync` replaces the note's text in place (id,
`created_at`, `deleted_at` untouched; rejects blank text without writing) and the
note's stale source event is deleted (without suppressing — the ref must remain
collectable) via `DeleteBySourceRefsAsync`, so the next collection run stores a
fresh event carrying the edited text instead of the old one.

All suppression-related repository calls in both windows are wrapped in their own
try/catch logged to `ErrorLog` (`HistoryWindow.SuppressDeletedNote`,
`HistoryWindow.UnsuppressReopenedNote`, `HistoryWindow.DeleteStaleNoteEvent`,
`CandidateWindow.SuppressSources`) so a persistence failure in the suppression
side-effect never blocks the primary soft-delete/reopen/edit operation from
completing and refreshing the view.

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

## 議事録モード (meeting minutes) flow

```text
Ctrl+Alt+M / tray 議事録を開始
      |
      v
MeetingSessionChooserWindow (only if drafts exist) -- resume or new
      |
      v
MeetingCaptureWindow -- IMeetingRepository --> SQLite meeting_sessions / meeting_lines
      |
      | (会議終了, or the AI整形 button at any time while capturing)
      v
  no API key / user declines ------------------------------------------------+
      |                                                                      |
  API key present, user opts in                                             |
      v                                                                      |
MeetingSendPreviewWindow (mandatory; every line, checked by default;         |
recomputes model/line-count/approx-KB from MeetingFormatPayloadBuilder       |
on every checkbox toggle; no setting bypasses it)                           |
      |                                                                      |
      v                                                                      |
MeetingFormatPayloadBuilder (SafeTextSanitizer + local-path redaction        |
+ 256 KiB UTF-8 hard cap, no truncation)                                     |
      |                                                                      |
      v                                                                      |
MeetingFormatClient -- POST /v1/responses (store:false, no tools,            |
strict text.format JSON Schema) --> MeetingFormatEnvelope                    |
      |                                                                      |
      v                                                                      |
MeetingFormatValidator (independent: summary length, strict due-date         |
format, nonblank text) --> MeetingFormattedResult                            |
      |                                                                      |
      v                                                                      |
IMeetingRepository.SaveSummaryAsync (new meeting_summaries row,              |
session.status = formatted; confirms overwrite if a summary exists)          |
      |                                                                      v
      v                                                          MeetingMarkdownBuilder(formatted: null)
MeetingMarkdownBuilder(formatted) --------------------------------------------+
      |
      v
Obsidian .md (front matter + 概要/決定事項/宿題/論点[+生ログ]) -- source of truth

separately, on demand:
MeetingSummaryCollector -- IMeetingRepository.ListFormattedInRangeAsync -->
  SourceEvent(sourceType="meeting", body=summary_line only, confidence 0.8)
      |
      v
existing LocalCollectionCoordinator dedup/mapping pipeline --> completed,
pre-selected 会議・打合せ candidate
```

Capture is a thin, immediate-persist loop: `MeetingLineParser` (pure) strips a
leading `@`/`＠` (宿題) or `!`/`！` (決定) marker and `MeetingCaptureWindow` appends
the parsed line to SQLite on `Enter` — there is no separate "save" step, and closing
the window (✕) simply leaves the session `draft` for `MeetingSessionChooserWindow`
to resume later. `MeetingMarkdownBuilder` (pure, `WorkLogAI.Core`) renders only the
raw-log section while `formatted` is null, and all sections (概要/決定事項/宿題/論点
plus an optional 生ログ) once a `MeetingFormattedResult` exists; `MeetingMarkdownWriter`
(`WorkLogAI.Infrastructure`) writes it with the same sanitized, collision-suffixed
(`_2`, `_3`, ...) filename scheme as the weekly Excel export (`MeetingFileNameBuilder`).

AI formatting mirrors the Phase 3 Responses client end-to-end: `MeetingFormatClient`
posts `store:false`, no `tools`, and a strict `text.format` JSON Schema
(`additionalProperties:false` everywhere, every property required, nullability via
`type` unions for `owner`/`due`), aggregates `output_text` across all output items,
and converts refusal/incomplete/error-status/empty-output/malformed-JSON into a
generic Japanese message that never echoes the request or response body — the same
contract `OpenAiResponsesClientTests` already established for the weekly generator.
Unlike weekly generation, the entire request payload is built by one pure,
independently testable class, `MeetingFormatPayloadBuilder`: it sanitizes title,
participants, and every line's text through `SafeTextSanitizer`, further redacts
local Windows/Unix paths, composes each line as `MeetingLineFormatter`'s shared
"HH:mm [宿題|決定] text" rendering, and never includes the session's GUID. The same
builder computes the exact payload `MeetingSendPreviewWindow` shows the user and the
byte count `MeetingFormatClient` checks against the 256 KiB cap, so the preview is
never lying about what would be sent. Even though the model's structured output is
already constrained by the strict JSON Schema, `MeetingFormatValidator` re-validates
it independently and purely (schemas cannot enforce "summary_line is <=120 chars and
actually non-blank" or "due, if present, parses as a strict `yyyy-MM-dd` date") —
any violation returns a Japanese error and leaves the raw log completely untouched.

`IMeetingRepository.SaveSummaryAsync` inserts a new `meeting_summaries` row (prior
rows for a re-formatted session are kept, not overwritten — `GetLatestSummaryAsync`
returns the newest by `created_at`) and sets `meeting_sessions.status = 'formatted'`
in the same transaction. `MeetingSummaryCollector` is the only path from a formatted
meeting into the weekly pipeline: it calls `ListFormattedInRangeAsync` and maps each
`(session, summary)` pair to one `SourceEvent` carrying **only** the summary line
plus the session's title and start time — raw `meeting_lines` are never read by this
collector, which is what guarantees they can never reach `AiPromptBuilder` (it only
ever serializes `SourceEvent.Title`/`Body`/`Evidence`). `LocalSourceEventMapper` maps
`SourceTypes.Meeting` to work item **会議・打合せ**, status `completed` (a formatted
meeting already happened and was explicitly reviewed by the user — unlike every
other local source, which stays `pending`), and pre-selects it like a manual note.
`AppServices.CollectLocalSourcesAsync` always includes `MeetingSummaryCollector`
since it only reads local SQLite and needs no external configuration.

Known limitation: re-formatting a session whose week has already run weekly
collection can leave the earlier summary's mapped candidate present until the user
deselects it, since content-hash deduplication keeps both source events distinct —
the same class of behavior an amended Git commit already produces for
`LocalGitCollector`.

## Persistence

Embedded SQL migrations run in version order inside a transaction according to
`PRAGMA user_version`. `001_initial.sql` creates the four specification tables,
`002_phase3_review.sql` upgrades existing Phase 1–2 databases without rewriting the
initial migration, `003_meeting_mode.sql` adds the three 議事録モード tables,
`004_report_category.sql` (schema v4) adds `report_candidates.category` (社内/社外,
default `internal`) — the column, model field, and repository mapping remain in
place, but the product surface no longer exposes or reads it: there is no
review-time category selector, and the exporter no longer groups or labels by
it, so the stored value is dormant data carried through unchanged — and
`005_suppressed_source_refs.sql` (schema v5) adds `suppressed_source_refs`:

- `quick_notes`
- `source_events`
- `report_candidates`
- `settings`
- `meeting_sessions` (header: title, participants, kind, started_at, ended_at,
  status, created_at)
- `meeting_lines` (session_id, line_no, marker, text, logged_at; indexed on
  `(session_id, line_no)`)
- `meeting_summaries` (session_id, formatted_json, summary_line, created_at;
  indexed on `session_id` — one row per AI-formatting run, oldest rows kept)
- `suppressed_source_refs` (`source_ref` text primary key, `suppressed_at`) — the
  permanent-deletion exclusion list consulted by `LocalCollectionCoordinator`
  (see Permanent deletion and history editing above); `SuppressSourceRefsAsync`
  inserts via `INSERT OR IGNORE` (re-suppressing an already-suppressed ref is a
  no-op) and `UnsuppressSourceRefsAsync` deletes by ref

Connections are short-lived and created from an injected path provider. Soft delete
sets `quick_notes.deleted_at`; reopen sets it back to `NULL`. `UpdateTextAsync`
replaces `quick_notes.text` in place without touching `id`/`created_at`/
`deleted_at`, and rejects blank text by returning `false` without writing.

Production and sample modes use separate subdirectories under Local Application
Data. Tests inject temporary database paths.

`DataRetentionService` (`WorkLogAI.Infrastructure`) is the one path that deletes
`source_events` rows outright, rather than just candidates or notes. It never
touches `report_candidates`: `AppServices.InitializeAsync` calls
`RunAsync(DateTimeOffset.Now)` once at startup (skipped under `--sample-data`,
wrapped in its own try/catch logging to `ErrorLog` under
`AppServices.DataRetention`), which computes `cutoff = now - 180 days`, asks
`ISourceEventRepository.ListIdsOlderThanAsync(cutoff)` for candidate ids, asks
`IReportCandidateRepository.ListAllSourceEventIdJsonAsync()` for every stored
candidate's raw `source_event_ids_json` (any week, any origin/edited/selected
state), parses and unions those into a referenced-id set in C#, subtracts it from
the old-id list, and deletes only what remains via the existing
`ISourceEventRepository.DeleteByIdsAsync`. This reuse — rather than a single SQL
`NOT IN` over a JSON array column — is deliberate: SQLite has no first-class way to
explode `source_event_ids_json` server-side, so the set difference happens in C#
against two already-existing repository primitives instead of adding a new one.

## Windows integration

The WPF process stays alive without a main window and exposes a Windows notification
area icon. `App.OnStartup` first acquires a named `Mutex`
(`WorkLogAI.App.SingleInstance`, or `WorkLogAI.App.SingleInstance.Sample` under
`--sample-data`) with `initiallyOwned: true`; if it did not create the mutex, a
second instance is already running, so it shows a Japanese notice and calls
`Shutdown(0)` before any tray icon, hotkey, or `AppServices` construction happens.
`OnExit` releases and disposes the mutex defensively (wrapped try/catch/finally —
an abandoned-mutex or already-released edge case must never crash exit).

A message-only `HwndSource` registers `Ctrl+Alt+W` using
`RegisterHotKey`; disposal always calls `UnregisterHotKey`. `Ctrl+Alt+M` for
議事録モード registers the same way, through the same parameterized
`GlobalHotKey`, and is additionally toggled live: `App.ApplyMeetingHotKeySetting`
(called from `SettingsWindow` right after a successful save) compares the
requested enabled state against whether `_meetingHotKey` is currently non-null and
registers/unregisters on the spot — no restart is required, unlike the original
startup-only registration this replaced.

The capture window has one single-line `TextBox` and uses `SizeToContent.Height`
instead of a fixed outer height, so the client area stays usable regardless of
title-bar chrome or DPI scaling. Successful saves show a separate, non-interactive
toast for approximately 800 ms; the toast positions itself from the capture window's
`ActualWidth`/`ActualHeight`, falling back to `SystemParameters.WorkArea` when those
are not yet available (e.g. before the window has laid out).

A `DispatcherTimer` ticks every 60 seconds. Each tick first calls
`CheckDailyBackupAsync`, which compares today's date against a stored
`backup.last_checked_date` setting (same pattern as the reminder's last-shown
date); on the first tick of a new calendar day it stores today's date and calls
`AppServices.RunDatabaseBackupIfDue()` (a no-op under `--sample-data`), which
re-invokes the same `DatabaseBackupService.RunIfNeeded()` the startup path already
uses — that method stays idempotent per week, so this only widens *how often* it
gets a chance to run, letting a tray instance that never restarts still pick up a
weekly backup. The tick then calls the pure `ReminderPlanner` (enabled flag,
weekday check, configured time gate, zero-notes-today check, once-per-day via a
stored last-shown date) to decide whether to show a tray balloon prompting the
user to record a note; clicking the balloon opens quick capture. The last-shown
date is persisted before the balloon is raised, as plain
`reminder.last_shown_date` settings state, so a restart mid-day cannot re-trigger
the same day's reminder. Both checks share the tick's one try/catch, logged to
`ErrorLog` under `App.ReminderTick` on failure.

`WindowsStartupRegistrar` (`WorkLogAI.Infrastructure`, behind the `IStartupRegistrar`
Core contract) implements opt-in auto-start by writing the current process path to
the `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` key. It rejects the dotnet
host (`dotnet`/`dotnet.exe`) with a Japanese error, since only a published,
self-contained EXE has a stable path to register; the settings window also disables
the checkbox entirely under `--sample-data`.

**週報候補を生成…** opens `WeekPickerWindow` before running collection instead of
always targeting the week containing today. The window itself stays dumb: it asks
the pure Core `WeekOptionBuilder.Build(today, weekStartsOn, 8)` for an ordered list
of `WeekRange`/`WeekOptionKind` (this-week, last-week, older), attaches only the
Japanese labels (`今週`/`先週`/no prefix) in the App layer, and defaults the
selection to 今週. Cancel (Esc or キャンセル) aborts before any collection or
generation call runs; OK (Enter or OK) flows the chosen `WeekRange` through the
same `GenerateCandidatesAsync` path used previously for "this week only".

**月次まとめを出力…** follows the identical dumb-window/pure-builder split:
`MonthPickerWindow` asks the pure Core `MonthOptionBuilder.Build(today, 12)` for
the current month plus the previous 11 (`MonthOption(Year, Month)`, newest first,
correctly wrapping the year boundary), attaches only the `{year}年{month}月` label
in the App layer, and defaults the selection to the current month. On OK,
`App.ExportMonthlySummaryAsync` computes the calendar month's first/last day,
calls `IReportCandidateRepository.ListSelectedByDateRangeAsync` (which spans every
`week_start` that overlaps the month, not just one), maps the result through the
existing `CandidateReportMapper.MapSelected`, and calls
`IWeeklyReportExporter.ExportMonthAsync`. An empty result shows
「対象月に採用済みの行がありません。」 instead of writing a file; a non-empty result
offers to open the file afterward via `ExportResultPrompt`'s owner-less overload
(the tray menu itself has no parent `Window` to pass as owner, unlike every other
call site of `ExportResultPrompt`, which are all `Window` subclasses using `this`).

The settings window is organized into a `TabControl` with five tabs
(基本/収集/AI/Microsoft 365/議事録) at a fixed 560×560 size, replacing the earlier
single scrolling column; every control, event handler, and validation rule moved
into its tab unchanged.

## Excel contract

ClosedXML writes one worksheet named `業務週報`, matching the layout of the
user's real submitted weekly report. Row 1 holds the merged title (A1:C1); E1
and E2 hold the company name and employee name as two stacked cells in the
report's last column (replacing the earlier single "company / employee" title
cell). Row 3 is a completely blank spacer row — no content, borders, or fill —
between the title area and the header. Row 4 is the header row (日付/曜日/項目・
案件・目標金額/活動内容/結果・決定事項・今後の課題) with a blue fill and white
bold text (replacing the earlier light-gray header). `WorkLogAI.Core`'s pure
`DailyReportGrouper` groups
the already-filtered, chronologically sorted `ReportRow`s into one
`DailyReportRow` per calendar day — `ReportRow.Category` plays no part in
grouping — and numbers each day's items in arrival order (circled digits ①–⑳
via `DailyReportGrouper.CircledNumber`, falling back to `(21)`, `(22)`, …
beyond that); `DailyReportGrouper` itself is unaware of the exporter's column
layout. The exporter renders **one sheet row per item**, not one row per day:
each `DailyReportItem` in a `DailyReportRow` gets its own row, with the same
circled number written into the 項目 (C) and 活動内容 (D) cells so the two
line up horizontally on that row even after text wraps, and the 結果・決定事項
(E) cell on that row holding the numbered result text only when it is
non-blank (an empty string otherwise — never a stray number with no text).
The date and weekday live in their own separate cells — 日付 (A) holds
`yyyy/MM/dd` alone, and 曜日 (B) holds the single-character Japanese weekday
in parentheses, e.g. `(月)` — each written only on the block's first item row;
every other row in the block — further item rows and all blank rows — leaves
both columns A and B empty, so a day never repeats its date or weekday. Rows
are wrapped, bordered, and configured for landscape printing at one page
wide on A4 paper by default; the data area has no merged cells. The worksheet's default font is set
to BIZ UDPゴシック right after worksheet creation and again explicitly on the
title, header, and data ranges (a single range covering rows 1 through the
last written row, so it also covers every new item row without a separate
per-row font call); it ships with Windows 10 1809+ and Windows 11, so no
separate install is needed. Every day's block — its item rows plus enough
blank rows to pad it to a uniform minimum of 4 sheet rows, with at least one
trailing blank row always present even when the item count alone already
reaches or exceeds 4 — occupies at least the same visual size regardless of
item count; every date gets this treatment, including the last one. Blank
rows get no explicit height, so they stay ordinary rows that
`AdjustToContents` and any later manual auto-fit re-run in Excel size like any
other empty row. Print area and freeze-row math account for the item and
blank rows automatically since they are folded into the same running row
counter.

Column widths are fixed in Excel units, converted from the user's real
report's pixel measurements: A (日付) 20.14 (≈146px), B (曜日) 7.0 (≈54px, the
narrow new weekday-only column), C (項目) 39.71 (≈283px), D (活動内容) 69.29
(≈490px), E (結果・決定事項) 60.14 (≈426px).

Borders in the data area (rows 5 and down) are drawn **constructively**: the
exporter never applies a blanket `Border.InsideBorder`/`OutsideBorder` over
the data area and then clears specific edges back off — that draw-then-clear
pattern rendered inconsistently across viewers. Instead only the intended
edges are set, once, from nothing. Vertical lines are continuous down the
whole table regardless of block or row type: `LeftBorder = Thin` on every
cell in columns A–E for every row from 5 to the last row (giving each column
its left divider, including the table's outer left edge on column A) plus
`RightBorder = Thin` on column E alone for that same row range (the table's
outer right edge). Horizontal lines appear only at day boundaries: each day
block's *last* row (its last blank row, or its last item row on the rare
day whose item count already meets the minimum) gets `BottomBorder = Thin`
across A–E — the line between that day and the next, and, on the final
block, the bottom of the table — and no other row in the data area gets any
`TopBorder` or `BottomBorder` at all, so there are no lines between item
rows, between or around blank rows, or under row 5 (the boundary with the
header is provided by the header row's own full thin border, set separately
via `OutsideBorder`/`InsideBorder` on the header row (row 4) only).

`ClosedXmlWeeklyReportExporter.ExportAsync` and the newer `ExportMonthAsync` share
one private `RenderAsync(titleText, fileName, rows, outputDirectory, identity, ct)`
core — every cell/style/grouping decision above is written exactly once.
`ExportAsync` supplies the weekly title/filename (`{ReportTitle}
{start}〜{end}`, `{title} {yyyyMMdd}-{yyyyMMdd}.xlsx`); `ExportMonthAsync` supplies
the monthly ones (`{ReportTitle} {year}年{month}月 月次まとめ`, `{title} 月次
{yyyyMM}.xlsx`, sanitized the same way via `ReportFileNameSanitizer`). Because
`DailyReportGrouper.Group` only ever groups by `(ReportRow.Date, Category)` and is
never told which week a row came from, feeding it a whole month's `ReportRow`s —
which can span several `week_start` values — produces exactly the same one-row-
per-day output the weekly export already produces, with no month-specific
grouping logic required.

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

GitHub network APIs, installers, and automatic update implementation remain absent.
Local collectors (Git, Codex, recent-file) still perform no network operation of
their own.

`ErrorLog` (`WorkLogAI.Infrastructure`, dependency-free, first Phase 5
operational-quality item) is a best-effort local diagnostic log, not an audit or
content log: it writes only a caller-supplied context label plus, for exceptions,
the exception type/message/stack trace, to monthly files under
`%LOCALAPPDATA%\WorkLog AI\Logs\worklog-YYYYMM.log`. It never receives note bodies,
candidate text, mail content, or credentials — call sites pass a short label
(`"CandidateWindow.Export"`) and either an `Exception` or an already-sanitized
summary string (e.g. collector error text, which is sanitized before it ever
reaches a `CollectorRunSummary`). Its own I/O is wrapped so logging can never
throw or surface a new failure, and it deletes its own files past 3 months on
every write via the pure `FormatLine`/`SelectExpiredLogFiles` helpers.

`DatabaseBackupService` (`WorkLogAI.Infrastructure`, second Phase 5
operational-quality item) makes a weekly, best-effort file-level copy of the
production SQLite database to `%LOCALAPPDATA%\WorkLog AI\Backups\worklog-YYYYMMDD.db`,
keeping the newest 4 backups. `AppServices.InitializeAsync` runs it first, skipped
entirely under `--sample-data`, and always before `IDatabaseInitializer.InitializeAsync`
opens the first connection — a plain `File.Copy` is only safe to reason about while
no connection (and therefore no WAL/journal file) exists yet. All failures are
caught and sent to `ErrorLog`; a backup problem never blocks startup.

議事録モード's outbound AI request is a fourth, separately governed kind of network
call, still explicit and user-approved at two points: the OpenAI API key check
(same `WorkLog AI/OpenAI API Key` Credential Manager target Phase 3 uses — no new
secret store) and, mandatorily, the per-line `MeetingSendPreviewWindow`, which no
setting can skip. `MeetingFormatPayloadBuilder` sanitizes every text field through
`SafeTextSanitizer`, further redacts local filesystem paths, and never includes the
session GUID; `MeetingFormatClient` enforces a hard 256 KiB UTF-8 cap on that exact
payload before sending, failing with a Japanese error rather than truncating
silently — meeting logs are expected to never legitimately reach that size. Meeting
capture, edit, and delete failures write only a context label to `ErrorLog`, never
line text, title, or participants, matching the discipline already established for
notes, candidates, and mail. Weekly generation still only ever sees a formatted
meeting's `summary_line` (via `SourceEvent.Body`) — raw `meeting_lines` rows are
read solely by `MeetingCaptureWindow`/`MeetingMarkdownBuilder`/`MeetingFormatClient`
and never by anything in the weekly collection or generation path.

`OpenAiKeyProbe` (settings' **APIキーをテスト**) is a fifth, separately governed
network call type: a single `GET https://api.openai.com/v1/models` with the
candidate key as a bearer token and a 10s timeout. It never sends request JSON
(there is none), never logs the key, and never surfaces a response body —
`ProbeAsync` collapses every outcome to one of three enum values. Both weekly
generation's `OpenAiResponsesClient` and 議事録モード's `MeetingFormatClient` now
send their single HTTP attempt through the shared, injectable-delay
`TransientRetryPolicy`, which can turn one logical request into up to three actual
attempts (2s/5s backoff, or a bounded server `Retry-After`) on a 429/5xx/transport
failure; this changes retry *count*, not payload shape or destination, so none of
the redaction/exclusion guarantees above are affected.

`DataRetentionService`'s 180-day source-event deletion (see Persistence) is a
storage-lifecycle change, not a new network or secret boundary: it deletes rows
from `source_events` only, via the existing `ISourceEventRepository`, and it
verifies a row is unreferenced by any stored `report_candidates` row (any
week/origin/edited/selected state) before deleting it, so a kept weekly or monthly
export result can never have its evidence disappear out from under it. Failures
are caught and sent to `ErrorLog` under `AppServices.DataRetention`; a retention
problem never blocks startup, mirroring `DatabaseBackupService`'s own
never-blocks-startup discipline.

`.github/workflows/ci.yml` builds and tests `WorkLogAI.sln` (`restore` /
`build -c Release` / `test -c Release`) on `windows-latest` for every push and
pull request to `main`. It is the only place the `WorkLogAI.App` WPF project
(`net8.0-windows`, `UseWPF`/`UseWindowsForms`) actually compiles end to end —
this repository's own Linux dev host can only build/test `WorkLogAI.Core` and
`WorkLogAI.Infrastructure` plus run `WorkLogAI.Tests` with
`-p:EnableWindowsTargeting=true`, per the existing non-Windows workflow described
in the README.
