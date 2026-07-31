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

## Post-Phase 3 usability

- [x] Quick capture window sizes to content (`SizeToContent.Height`) instead of a
      fixed outer height, staying usable under title-bar chrome and DPI scaling
- [x] Save toast positions from the capture window's actual size, with a
      `SystemParameters.WorkArea` fallback
- [x] Weekly review **記入状況** coverage bar for all 7 days with per-day candidate
      counts, computed by the pure `WeekCoverageCalculator`
- [x] Zero-candidate days are highlighted (weekdays more strongly than weekends) and
      clickable to add a manual row pre-filled with that date
- [x] Weekday evening reminder: pure `ReminderPlanner` decision (enabled, weekday,
      configured time gate, zero-notes-today, once-per-day), driven by a 60-second
      tray timer
- [x] Reminder balloon click opens quick capture; settings expose
      `reminder.enabled` and `reminder.time` (default 17:00); last-shown date is
      stored as plain state before the balloon is shown
- [x] Opt-in Windows auto-start via `IStartupRegistrar`/`WindowsStartupRegistrar`,
      an HKCU Run key entry
- [x] Auto-start registration requires the published EXE — the dotnet host is
      rejected with a Japanese error — and is disabled in `--sample-data` mode
- [x] **候補を生成…** opens a week picker (今週 plus the previous 7 weeks) built
      from the pure `WeekOptionBuilder`; cancel aborts without running collection
- [x] Weekly review Excel export warns (Yes/No) when a selected weekday has zero
      selected rows, listing the blank days; weekends never warn
- [x] Both Excel export flows offer to open the generated file (Yes/No) after a
      successful export
- [x] Best-effort local error log (`ErrorLog`, Phase 5 operational-quality item)
      under `%LOCALAPPDATA%\WorkLog AI\Logs`, monthly files, 3-month retention,
      context/exception only — never note bodies, candidate text, or secrets
- [x] Weekly best-effort SQLite file backup (`DatabaseBackupService`, Phase 5
      operational-quality item) under `%LOCALAPPDATA%\WorkLog AI\Backups`, run at
      startup before any DB connection opens, skipped in `--sample-data`, newest
      4 backups retained
- [x] Weekly review card **削除** button removes a row (confirmed via Yes/No),
      persisted on next save/export; manually added (`1行追加`) rows also delete
      their backing `review-manual:` source event via
      `ISourceEventRepository.DeleteByIdsAsync` so they cannot resurface, while
      collected/AI-origin rows only leave this week's review and the confirmation
      dialog says they may reappear on the next collection/generation run
- [x] Weekly review **今週のクイック入力** sidebar lists the review week's
      non-deleted quick notes for reference
- [x] **記入状況** day chips (covered and empty) are clickable filters, with a
      prepended **全て** chip to clear the filter; the notes sidebar and card list
      both narrow to the selected day
- [x] Git candidates show the commit subject/summary/statistics in **活動内容**
      instead of the changed-file list (`GitEventText.StripFileList`), applied both
      to the local mapper and the outbound AI prompt; file paths remain visible only
      in the 根拠 line

## Phase 4 — Microsoft Graph mail and calendar

- [x] MSAL delegated public-client sign-in with system browser, `Mail.Read` and
      `Calendars.Read` scopes only
- [x] Interactive sign-in only from the settings window; collectors only acquire
      tokens silently and never prompt
- [x] Missing/expired silent token reported as a per-source collector error
      ("Microsoftサインインが必要です") instead of throwing or blocking the run
- [x] MSAL token cache persisted as a DPAPI-encrypted (`CurrentUser`) file, not
      Windows Credential Manager, with the deviation documented (README and
      architecture)
- [x] Tokens never enter SQLite, settings, or logs
- [x] Graph called via raw REST (no Graph SDK); bounded paged reads (`$top=50`,
      max 10 pages, 4 MB response cap per page)
- [x] Non-2xx Graph responses surface only a status code; response bodies are
      never echoed
- [x] Sent-mail collection: current week's `SentItems` filtered by `sentDateTime`;
      subject/sentDateTime/recipients kept
- [x] Mail body reduced to new content only (HTML stripped, quoted replies and
      dividers cut), auto-replies and empty `RE:` replies excluded
- [x] Mail body passes `SafeTextSanitizer` redaction with a 2000-character cap;
      fixed confidence 0.7 and work item label メール対応
- [x] Calendar collection: `calendarView` for the week; cancelled events skipped
- [x] Calendar evidence is the time range (or 終日 for all-day) plus location;
      sanitized body preview capped at 500 characters
- [x] Calendar candidates carry fixed confidence 0.5 and always stay `pending` — a
      calendar entry never implies a completed result; work item label 会議・予定
- [x] New `outlook_mail`/`calendar` source types flow through the existing
      dedup/mapping/coordinator pipeline unchanged
- [x] Settings gain `graph.client_id`, `graph.tenant_id`, `graph.mail_enabled`,
      `graph.calendar_enabled`, plus sign-in/sign-out controls with status display
- [x] Azure AD app registration setup documented (public client, redirect
      `http://localhost`, delegated `Mail.Read` + `Calendars.Read`)
- [x] Mocked Graph auth cache, collector, parser, and mail-body-extractor tests;
      no live Graph API test

Covers the Phase 4 spec items (Microsoft Graph sign-in, sent mail, calendar,
collection-scope settings); it does not add any other Microsoft 365 surface (no
Teams, OneDrive, or additional mailboxes).

## 議事録モード — meeting minutes mode

- [x] Migration `003_meeting_mode.sql` idempotently adds `meeting_sessions`,
      `meeting_lines`, and `meeting_summaries`
- [x] `Ctrl + Alt + M` hotkey via a now-parameterized `GlobalHotKey`, independently
      toggled from `Ctrl + Alt + W`, gated by `meeting.hotkey_enabled` (default on;
      `App.ApplyMeetingHotKeySetting` now applies a toggle immediately on settings
      save instead of requiring a restart — see Post-Phase 4 additions below)
- [x] `MeetingCaptureWindow`: title/participants/kind header, Enter-confirmed
      timestamped lines with no separate save step, inline double-click edit and
      `Delete` removal, remembered window placement
- [x] Pure `MeetingLineParser` recognizes leading `@`/`＠` (宿題) and `!`/`！`
      (決定) markers; anything else is an unmarked note
- [x] Draft persistence and resume via `MeetingSessionChooserWindow`; closing (✕)
      leaves a session as `draft`, **会議終了** closes it
- [x] Obsidian-ready Markdown export (`MeetingMarkdownBuilder` + `MeetingMarkdownWriter`):
      YAML front matter, 概要/決定事項/宿題/論点[+生ログ] sections, sanitized
      collision-suffixed (`_2`, `_3`, ...) filenames, offer to open the file
- [x] Meeting text (title/participants/line content) is excluded from `ErrorLog` —
      only context labels are logged on failure
- [x] `MeetingFormatClient` mirrors `OpenAiResponsesClient`: `store:false`, no
      tools, strict `text.format` JSON Schema (`additionalProperties:false`
      everywhere, every property required, nullable `owner`/`due` via `type`
      unions), all-item `output_text` aggregation, bounded response read, and safe
      refusal/incomplete/error/malformed handling that never echoes request or
      response bodies
- [x] Mandatory line-level send preview (`MeetingSendPreviewWindow`) — every line,
      checked by default, header shows model/line-count/approximate outbound KB
      recomputed on every checkbox toggle; no setting can bypass it; unchecked
      lines are excluded from the payload only, SQLite is never touched
- [x] Pure, independently testable `MeetingFormatPayloadBuilder`: sanitizes and
      redacts local paths from title/participants/line text, composes the shared
      `MeetingLineFormatter` marker labels, excludes unchecked lines, and computes
      the exact byte count both the preview and the client rely on
- [x] Independent `MeetingFormatValidator`: nonblank/`<=120`-char `summary_line`,
      strict `yyyy-MM-dd` `TryParseExact` for `due` (null allowed), nonblank
      decision/action-item/topic text — a schema-satisfying but otherwise invalid
      response still fails with a Japanese error and leaves the raw log untouched
- [x] Hard 256 KiB UTF-8 payload cap enforced before any network call, with a
      Japanese error and no silent truncation
- [x] Summary persistence: `SaveSummaryAsync` (new row + `status = formatted` in
      one transaction), `GetLatestSummaryAsync`, `ListFormattedInRangeAsync`;
      overwrite confirmation (**既存の整形結果を上書きしますか？**) before
      re-saving a session that already has a summary
- [x] `MeetingFormattedResultJson` is the single camelCase, case-insensitive
      round-trip contract for `meeting_summaries.formatted_json`
- [x] Weekly integration: `SourceTypes.Meeting`, `MeetingSummaryCollector` (always
      on, reads only local SQLite, confidence fixed at 0.8), `LocalSourceEventMapper`
      maps it to work item **会議・打合せ**, status `completed` (not `pending` —
      unlike every other local source), pre-selected like a manual note
- [x] Only a formatted session's `summary_line` (never raw `meeting_lines`) ever
      reaches the weekly AI generation prompt
- [x] Known, documented limitation: re-formatting after weekly collection can leave
      an older summary's mapped candidate present until deselected (content-hash
      dedup keeps both events), the same class of behavior as an amended Git commit
- [x] Automated tests: payload builder (exclusion, sanitization, marker labels,
      size calc), validator, mocked-handler client (request shape, refusal/
      incomplete/error/malformed handling, oversized-payload short-circuit,
      multi-item aggregation), repository summary methods, mapper, collector, and
      `MeetingFormattedResultJson` round-trip — no live API test

## Post-Phase 4 reliability, generation-quality, and CI additions

- [x] AI candidates supersede covered local rows: after a successful generation,
      `LocalCandidateSuppressor.SelectSupersededIds` deselects any still-selected,
      unedited local row (quick memo/meeting summary) whose entire evidence set is
      fully covered by the newly generated AI candidates; a partially-covered row
      stays selected. The generation-summary banner reports the deselected count
      (「元メモ由来の行 N件の採用を外しました」)
- [x] `SourceEventDeduplicator.LatestPerRef` collapses events sharing a stable
      `sourceRef` (e.g. a re-formatted meeting) down to the newest by
      `CollectedAt`/`OccurredAt`/id, applied before both local mapping and the
      outbound AI prompt build, so a re-formatted meeting no longer double-counts
- [x] Shared `TransientRetryPolicy` retries a 429/5xx/transport failure up to twice
      with backoff (honoring a bounded `Retry-After`) for both weekly generation's
      `OpenAiResponsesClient` and 議事録モード's `MeetingFormatClient`;
      non-retryable statuses (400/401/403/404) still return immediately
- [x] Settings AI tab gains **APIキーをテスト** (`OpenAiKeyProbe`): a bare
      `GET /v1/models` liveness check reporting only ok/unauthorized/network-error,
      never the key or a response body
- [x] Single-instance guard: a named `Mutex` (separate name under `--sample-data`)
      rejects a second launch with a Japanese notice instead of a duplicate tray
      icon and hotkey conflict; released defensively on exit
- [x] Meeting hotkey toggle applies immediately via
      `App.ApplyMeetingHotKeySetting` on settings save, replacing the previous
      restart-required behavior
- [x] Settings window reorganized into five tabs (基本/収集/AI/Microsoft 365/議事録)
      at a fixed 560×560 size; every control/handler/validation moved unchanged
- [x] `.github/workflows/ci.yml`: `windows-latest` build-test job (`restore` /
      `build -c Release` / `test -c Release`) on push and pull request to `main` —
      the only place `WorkLogAI.App` (WPF) actually compiles end to end, since this
      repository's own dev host cannot target `net8.0-windows` with `UseWPF`
- [x] `DataRetentionService`: deletes `source_events` older than 180 days that are
      not referenced by any stored report candidate's evidence list (any week,
      origin, edited, or selected state); runs once at startup (skipped under
      `--sample-data`), non-fatal on failure
- [x] Daily backup due-check: the existing 60-second reminder timer also checks
      once per calendar day (`backup.last_checked_date` state) and re-invokes
      `DatabaseBackupService.RunIfNeeded()`, so a tray instance that never restarts
      still gets a weekly backup opportunity
- [x] Monthly summary export: tray action **月次まとめを出力…** opens a month picker
      (current month plus the previous 11, built by the pure `MonthOptionBuilder`)
      and exports every selected candidate whose `work_date` falls in that
      calendar month — spanning any number of weeks — via
      `IReportCandidateRepository.ListSelectedByDateRangeAsync` and
      `IWeeklyReportExporter.ExportMonthAsync`, reusing the exact weekly
      per-day/社内-社外 grouped layout. Filename
      `{sanitized title} 月次 {yyyyMM}.xlsx`; an empty month shows
      「対象月に採用済みの行がありません。」 instead of writing a file
- [x] Automated tests: retry policy (retryable/non-retryable status, Retry-After
      honored, exhaustion, injectable delay), key probe (ok/unauthorized/network
      error via mocked handler), local-candidate-supersede selection rules,
      sourceRef-latest-wins deduplication (including reversed-input-order
      determinism), data retention (unreferenced-deleted/referenced-kept/
      recent-kept/idempotent), monthly repository range query, monthly export
      (filename, title cell, multi-week day-grouping, category split), and month
      option builder (count, ordering, year-boundary wrap) — no live API test

## Explicitly not implemented after Phase 4

- [ ] Phase 5: installer, crash recovery, and automatic updates

Auto-start, the local error log, and the weekly database backup — the Phase 5
operational-quality items that overlap with usability work — are already
implemented above, and the deletion and daily-check work above (180-day source-event
retention, daily backup due-check) further rounds out that operational-quality set.
`.github/workflows/ci.yml` adds Windows CI build/test coverage but is developer
tooling, not application runtime behavior — there are still no GitHub network calls
*from the running app*, no installer, and no auto-update code. The secret stores
remain Windows Credential Manager (OpenAI API key only, shared by weekly generation
and 議事録モード AI整形) and the DPAPI-encrypted MSAL token cache file (Microsoft
Graph tokens only). The app's external calls are the explicit user-approved OpenAI
Responses request for weekly generation, the same Responses endpoint for meeting
AI整形 (gated by its own mandatory per-line send preview), the settings-only
**APIキーをテスト** liveness probe, and the explicit, toggle-gated Microsoft Graph
REST reads — all now uniformly retried through `TransientRetryPolicy` where they
are simple HTTP sends (the two Responses API clients).
