# WorkLog AI

WorkLog AI is a Windows-only .NET 8 WPF tray application for capturing short work
notes, collecting safe local work metadata, and exporting a manual weekly report.
This repository currently implements Phases 1 through 4, plus a 議事録モード
(meeting minutes mode) addition (see below).

## Phase 1 features

- `Ctrl + Alt + W` opens a keyboard-only, single-line quick capture window. The
  window sizes itself to its content (`SizeToContent`) rather than a fixed outer
  height, so the input box stays usable under title-bar chrome and DPI scaling.
- `Enter` saves and closes, `Esc` discards and closes, and `Ctrl + Enter` saves,
  clears, and stays open.
- Notes receive a local timestamp automatically and persist in SQLite.
- The tray menu opens quick capture, weekly history, settings, and the collection
  action extended by Phase 2.
- Weekly history supports previous/next week, soft deletion, reopening, and manual
  XLSX export.
- Settings include company, employee, report title, week start, and Excel output
  directory.
- A `--sample-data` mode uses a separate database and seeds three example notes.

## Phase 2 features

- A user-triggered **週報候補を生成…** action (originally 今週の候補を生成; now
  preceded by a week picker, see Usability additions) collects only configured
  local repositories and folders.
- Local Git collection uses the installed `git` executable without a shell. It reads
  commit metadata, filenames, line statistics, and uncommitted filenames, never
  source contents or diffs.
- Codex JSONL collection streams bounded lines through a strict allowlist. It keeps
  session time/cwd, user instructions, final completion text, changed filenames,
  and short allowlisted tool names only.
- Codex reasoning, compacted history, summaries, function outputs, command arguments,
  environment records, and source code are ignored. Selected content stops at
  256 KiB per session and secret-like text is redacted.
- Recent-file collection reads metadata only from configured business folders.
  Sensitive files and generated/VCS directories are excluded.
- Source events are SHA-256 deduplicated and deterministically mapped to read-only
  pending candidates with every evidence event ID retained.
- Collector failures are reported per source without discarding successful sources.
- Sample mode also seeds one isolated sample source event and candidate.

## Phase 3 features

- OpenAI Responses API candidate generation uses `POST /v1/responses`, `store:false`,
  no tools, and strict Structured Outputs under `text.format`.
- The default model is `gpt-5.6-sol`; another compatible model can be entered in
  settings.
- The API key is stored only as the fixed generic credential
  `WorkLog AI/OpenAI API Key` in Windows Credential Manager. Existing keys are never
  displayed, and no `.env` key loading exists.
- The outbound payload is rebuilt from the requested week's deduplicated events,
  redacted again, stripped of source references and full local paths, and bounded
  deterministically by event count and UTF-8 size.
- Optional send preview shows only model, week, source counts, event counts, and
  truncation status before transmission.
- Responses are independently validated for requested-week dates, exact statuses,
  confidence bounds, nonblank report text, confirmation rules, and evidence IDs that
  were actually sent.
- Conservative duplicate merging retains all source evidence and never upgrades a
  result or completion status without evidence.
- The weekly card review supports adoption, low-confidence filtering, merging,
  manual evidence-backed rows, edits, save, and selected-only Excel export.
- Regeneration replaces only unedited AI candidates. Local candidates, manual rows,
  and all user-edited candidates are preserved.

## Usability additions

- The weekly review window shows a **記入状況** coverage bar with all 7 days of the
  target week and each day's candidate count. Days with zero candidates are
  highlighted (weekdays more strongly than weekends) and clickable to add a manual
  row pre-filled with that date.
- An opt-in weekday evening reminder (settings default 17:00) shows a tray balloon
  when today has zero quick notes and no reminder has been shown yet that day.
  Clicking the balloon opens quick capture. The check runs from a 60-second tray
  timer against a pure `ReminderPlanner` decision; the last-shown date is stored as
  plain settings state.
- An opt-in **Windowsログイン時に自動起動する** checkbox in settings registers the
  app under the current user's `HKCU\...\Run` key. Registration requires the
  published, self-contained EXE — running through `dotnet run`/the dotnet host is
  rejected with a Japanese error — and the option is disabled entirely in
  `--sample-data` mode.
- **週報候補を生成…** opens a week picker (今週 plus the previous 7 weeks, newest
  first) before running collection/generation, instead of always targeting the
  week containing today. The picker's option list is built by the pure
  `WeekOptionBuilder`; only its Japanese labels live in the App layer. A small
  topmost `ProgressStatusWindow` tracks progress through local collection, send
  preparation, and AI generation, and the weekly review window that opens
  afterward shows an in-window generation-summary banner instead of a separate
  completion dialog.
- Each candidate card in the weekly review has a **削除** button (confirmed via a
  Yes/No dialog) that removes the row from the in-memory list; the removal is only
  persisted when **編集を保存** or **Excel出力** runs next, matching how other edits
  are saved. Rows added via **1行追加** are truly deleted, including their backing
  `review-manual:`-tagged source event, so they cannot resurface. Rows sourced from
  collected/AI data are only removed from this week's review — the confirmation
  dialog notes that the next collection/generation run may surface them again since
  the underlying record still exists.
- The weekly review's **Excel出力** checks coverage of selected rows only and, if
  any Monday–Friday day has zero selected candidates, shows a Yes/No confirmation
  listing the blank weekdays before exporting. Weekends never trigger it.
- Both **Excel出力** actions (weekly review and history) ask **ファイルを開きますか？**
  after a successful export and open the file with the OS default handler on Yes.
- A best-effort local error log (`ErrorLog`) appends `context`/exception
  type+message+stack lines to a monthly file under
  `%LOCALAPPDATA%\WorkLog AI\Logs\worklog-YYYYMM.log`, created on demand and
  pruned past 3 months. It never logs note bodies, candidate text, mail content,
  or secrets, and logging itself can never throw.
- App-wide handlers for dispatcher, AppDomain, and unobserved task exceptions log
  unexpected UI errors to `ErrorLog` and keep the tray app running instead of the
  process dying silently and leaving a ghost tray icon.
- A weekly, best-effort SQLite file backup (`DatabaseBackupService`) runs at
  startup (skipped in `--sample-data` mode) before any database connection opens.
  If the production DB exists and no backup is newer than 7 days, it copies the
  file to `%LOCALAPPDATA%\WorkLog AI\Backups\worklog-YYYYMMDD.db` (same-day
  overwrite) and keeps only the newest 4 backups. All failures are swallowed and
  written to the error log; backups never block startup.

## Phase 4 features

- Delegated, read-only Microsoft Graph sign-in via MSAL (`Mail.Read` and
  `Calendars.Read` scopes) using the system browser. Interactive sign-in only
  happens from a user-initiated **設定** action; collectors only ever acquire
  tokens silently and report **"Microsoftサインインが必要です"** as a per-source
  collector error instead of prompting.
- Graph is called via raw REST (no Graph SDK) with bounded paged reads: `$top=50`,
  a maximum of 10 pages, and a 4 MB response cap per page. Non-2xx responses surface
  only an HTTP status code; response bodies are never echoed.
- **Outlook sent mail** collects the current week's `SentItems`, filtered server-side
  by `sentDateTime`. Subject, send time, and recipients are kept; the body is reduced
  to its new content only (HTML stripped, quoted replies and reply dividers cut),
  auto-replies and empty `RE:` replies are excluded, and the result passes through
  `SafeTextSanitizer` with a 2000-character cap. Confidence is fixed at 0.7 and the
  candidate work item is labeled **メール対応**.
- **Outlook calendar** collects the week's `calendarView`. Cancelled events are
  skipped; evidence is the event's time range (or **終日** for all-day events) plus
  location, and the sanitized body preview is capped at 500 characters. Confidence
  is fixed at 0.5 and candidates always stay `pending` — a calendar entry never
  implies a completed result. The candidate work item is labeled **会議・予定**.
- The new `outlook_mail` and `calendar` source types flow through the same
  content-hash dedup, deterministic mapping, and failure-isolating coordinator as
  the Phase 2 local collectors, unchanged.
- Settings gain a client ID, an optional tenant ID (default `common`), independent
  enable checkboxes for mail and calendar collection, and sign-in/sign-out buttons
  with a signed-in-user status line.

## 議事録モード (meeting minutes mode)

- `Ctrl + Alt + M` (independently toggled from `Ctrl + Alt + W`, and registered only
  once at startup — changing the **議事録ホットキー** checkbox in settings takes
  effect after a restart) or the tray's **議事録を開始 (Ctrl+Alt+M)** opens meeting
  capture. If
  draft sessions already exist, `MeetingSessionChooserWindow` offers to resume one
  or start fresh first.
- `MeetingCaptureWindow` is a small always-on-top window: 件名/相手先・参加者/種別
  (会議・来客・電話) header, a single-line input that appends a timestamped line to
  SQLite on `Enter` (no separate save step), inline double-click edit and `Delete`
  removal, and a remembered window position/size across sessions.
- A leading `@`/`＠` marks a line as 宿題 (todo/action item); a leading `!`/`！`
  marks it as 決定 (decision); anything else is an unmarked note. Closing the window
  (✕) leaves the session as a draft for later resume; **会議終了** closes it.
- **AI整形**: with an OpenAI API key configured, either the **AI整形** button or the
  **会議終了** follow-up prompt runs the same flow — a **mandatory** line-level send
  preview (`MeetingSendPreviewWindow`) lists every captured line with a checkbox
  (all checked by default), the target model name, the line count, and the
  approximate outbound UTF-8 size, recomputed on every toggle. No setting bypasses
  this preview. Unchecked lines are excluded from the request payload only — SQLite
  is never modified by the preview. On confirmed send, `MeetingFormatClient` mirrors
  the Phase 3 Responses client (`store:false`, no tools, strict `text.format` JSON
  Schema, bounded response read, safe refusal/incomplete/error/malformed handling)
  and the model's structured output is independently re-validated (summary length,
  strict `yyyy-MM-dd` due dates, nonblank text) before it is trusted. The built
  payload is hard-capped at 256 KiB UTF-8 with a clear Japanese error and no silent
  truncation — meeting logs are expected to never be that large. Re-formatting a
  session that already has a summary asks **既存の整形結果を上書きしますか？** before
  overwriting.
- **Markdown export**: an Obsidian-ready `.md` file (YAML front matter with
  date/type/participants/tags, `## 概要`/`## 決定事項`/`## 宿題`/`## 論点` sections
  once a summary exists, and an optional `## 生ログ` raw-log section per settings)
  is written to the configured 議事録出力フォルダ, with the same sanitized,
  collision-suffixed (`_2`, `_3`, ...) filename scheme as the weekly export, and an
  offer to open the file afterward. Markdown is the durable record for Obsidian; if
  no output folder is configured, the app says so but still keeps the summary in
  SQLite.
- **Weekly report integration**: every AI-formatted session becomes one local source
  event (source type `meeting`, confidence fixed at 0.8) via `MeetingSummaryCollector`,
  mapped to work item **会議・打合せ**, status `completed` (a formatted meeting
  already happened and was explicitly reviewed — unlike every other local source, it
  is not `pending`), and pre-selected like a manual note. **Only the summary_line
  plus the session title/time ever leave `IMeetingRepository` this way — raw meeting
  lines are never read by the collector and can therefore never reach the weekly AI
  generation prompt.** This collector is always on; it reads only local SQLite.
- Settings add 議事録出力フォルダ (blank disables export), 生ログをMDに同梱
  (default on), and 議事録ホットキー有効化 (default on, restart required to take
  effect).
- Known limitation: re-formatting a session after its week has already run weekly
  collection can leave the earlier summary's mapped candidate present until the
  user deselects it — content-hash deduplication keeps both source events, the same
  class of behavior as an amended Git commit already produces.

### Microsoft 365 setup

Microsoft Graph access requires an Azure AD app registration: a public client with
redirect URI `http://localhost` and delegated `Mail.Read` + `Calendars.Read`
permissions. Enter its client ID (and tenant ID, if not using the default `common`
multi-tenant endpoint) in **設定**, then use **Microsoftサインイン** to complete
sign-in in the system browser before enabling mail/calendar collection.

GitHub network APIs, installers, and automatic updates remain intentionally absent.
Auto-start, the local error log, and the weekly database backup are the Phase 5
operational-quality items already implemented (see Usability additions above).

## Requirements and build

- Windows 10 or later
- .NET 8 SDK with Windows Desktop support, or Visual Studio 2022

```powershell
dotnet restore WorkLogAI.sln
dotnet build WorkLogAI.sln
dotnet test WorkLogAI.sln
dotnet run --project src/WorkLogAI.App
```

The `WorkLogAI.App` WPF project (`net8.0-windows`) only builds on Windows. On
non-Windows hosts, build and run `WorkLogAI.Tests` with
`-p:EnableWindowsTargeting=true`, e.g.
`dotnet test tests/WorkLogAI.Tests/WorkLogAI.Tests.csproj -p:EnableWindowsTargeting=true`.
The suite currently has 239 tests.

Create the specified self-contained, single-file Windows build with:

```powershell
dotnet publish src/WorkLogAI.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Start the isolated demonstration database with:

```powershell
dotnet run --project src/WorkLogAI.App -- --sample-data
```

The production database defaults to:

```text
%LOCALAPPDATA%\WorkLog AI\Data\worklog.db
```

Sample mode defaults to:

```text
%LOCALAPPDATA%\WorkLog AI\SampleData\worklog-sample.db
```

The paths are injectable for tests and future hosting.

## Usage

1. Start the app; it remains in the notification area.
2. Press `Ctrl + Alt + W`, type one line, then press `Enter`.
3. Open **設定** and enter local Git repositories and recent-file folders as one
   absolute path per line, plus an optional Codex session folder.
4. Enter an OpenAI API key in **設定**. It is written directly to Windows Credential
   Manager, not SQLite. Configure the model and send-preview toggle.
5. Optionally enter a Microsoft Graph client ID (and tenant ID) in **設定**, sign in
   with **Microsoftサインイン**, and enable Outlook mail and/or calendar collection.
6. Select **週報候補を生成…**, pick the target week (今週 or one of the previous
   7 weeks), to run local (and, if enabled, Graph) collection and, after preview
   approval, generate candidates.
7. Review cards, edit fields, inspect evidence, merge duplicates, check the
   **記入状況** coverage bar for empty days, and select rows.
8. Use **Excel出力** in review to persist changes and export selected rows only —
   a blank-weekday warning appears first if any selected weekday has no rows, and
   a prompt to open the file appears after a successful export.
9. Open **今週の記録を見る** to browse notes or manually export the four-column
   Phase 1 report (also offers to open the file after export).

The generated file is named
`業務週報 YYYYMMDD-YYYYMMDD.xlsx`. The title (and the company/employee
name shown alongside it) are configurable in **設定**.

## Security

The SQLite `settings` table is used only for non-secret preferences. Its historical
column name is `value_encrypted`, but Phase 1 does not claim encryption and rejects
setting keys that could contain passwords, tokens, API keys, client secrets, or
credentials. The application does not log note bodies or credentials.

The OpenAI API key still lives only in Windows Credential Manager
(`WorkLog AI/OpenAI API Key`). Microsoft Graph tokens are a deliberate deviation
from the MVP specification: the spec calls for Credential Manager, but a generic
credential blob caps at 2560 bytes, which is smaller than an MSAL token cache. The
MSAL cache is instead stored as a DPAPI-encrypted (`CurrentUser` scope) file at
`%LOCALAPPDATA%\WorkLog AI\Auth\msal.cache` (`msal.sample.cache` in
`--sample-data` mode). Tokens never enter SQLite, settings, or logs, and the Graph
`client_id`/`tenant_id`/enable flags stored in settings are non-secret by design.

Collection paths are stored newline-delimited as non-secret preferences. A collection
run occurs only after the user selects the tray action. Git is invoked through
`ProcessStartInfo.ArgumentList`; no configured path is interpolated into a shell.

The outbound AI request contains sanitized event IDs, dates, source types, titles,
summaries, and reduced evidence only. It excludes `sourceRef`, full local paths,
source code, diffs, function output, full threads, logs, credentials, and API keys.
No transmission happens during startup, background operation, or settings save.

Meeting text (session title/participants/line content) is sanitized through
`SafeTextSanitizer` plus local-path redaction before ever leaving the process for
AI formatting, is never included in the session id, is capped at 256 KiB UTF-8 with
no silent truncation, and is only sent at all after the user explicitly approves it
line-by-line in the mandatory `MeetingSendPreviewWindow`. Meeting capture, edit, and
delete operations log only a context label to `ErrorLog` on failure — never the
line text, title, or participants — matching the existing note/candidate/mail
logging discipline. Only a formatted session's `summary_line` (never raw lines)
reaches the weekly AI generation prompt, via the same `SourceEvent`
title/body/evidence fields every other local source already uses.

See [architecture](docs/ARCHITECTURE.md) and the
[phase checklist](docs/PHASE_CHECKLIST.md).
