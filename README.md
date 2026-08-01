# WorkLog AI

WorkLog AI is a Windows-only .NET 8 WPF tray application for capturing short work
notes, collecting safe local work metadata, and exporting a manual weekly report.
This repository currently implements Phases 1 through 4, plus a 議事録モード
(meeting minutes mode) addition (see below).

The UI uses a refreshed, modern light theme (white cards, blue accents, rounded
corners) applied consistently across all windows.

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
- An opt-in weekday reminder covers the day in two half-day slots (settings default
  11:00 and 16:00, configurable as a comma-separated `HH:mm` list): each slot shows
  a tray balloon if nothing has been noted since the previous slot's time (the
  first slot covers since midnight) and that slot hasn't already fired today.
  Clicking the balloon opens quick capture. Optional **スマート通知** acceleration
  fires a hungry slot up to 60 minutes early when the user has just returned from
  a break — detected locally as a Windows session unlock or 10+ minutes of
  keyboard/mouse idle followed by renewed activity. (Git-commit activity was
  considered as a possible smart trigger and deliberately rejected as too noisy;
  there is no repository monitoring here.) The check runs from a 60-second tray
  timer against a pure `SlotReminderPlanner` decision; each slot's last-shown date
  is stored as its own plain settings state key.
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
- Each candidate card in the weekly review has a **削除** button; the removal is
  only persisted when **編集を保存** or **Excel出力** runs next, matching how other
  edits are saved. Rows added via **1行追加** are truly deleted (a plain Yes/No
  confirm), including their backing `review-manual:`-tagged source event, so they
  cannot resurface. Rows sourced from collected/AI data instead show
  `DeleteCardConfirmWindow`, a three-way choice: **行のみ削除** removes only this
  week's row (the next collection/generation run may surface it again, since the
  underlying record still exists) or **元データごと削除**, which permanently
  deletes the backing source event(s), adds their `source_ref`s to a suppression
  list so no future collection ever re-inserts or re-maps them, and soft-deletes
  any originating quick note so the history view matches.
- **Permanent deletion via a suppression list**: `suppressed_source_refs`
  (migration 005) records `source_ref`s that must never be (re-)collected.
  `LocalCollectionCoordinator` consults it at the start of every run, skipping
  suppressed refs both when inserting newly collected events and when mapping the
  week's stored events to candidates, so a suppressed origin stays gone even
  though the underlying git history or quick note text is still readable by the
  collector. Suppression is reversible: reopening a soft-deleted quick note in
  history un-suppresses its ref as well.
- The weekly history window gains a **削除済みを表示** checkbox (unchecked by
  default) so deleted notes stay hidden from the normal view; deleting a note now
  also suppresses its source ref and drops any already-stored event for it (same
  permanent-deletion mechanism as the review window), and reopening a note
  reverses the suppression. Double-clicking a non-deleted note opens
  `QuickNoteEditWindow` to edit its text in place; saving updates the note and
  deletes (without suppressing) its stale source event, so the next collection run
  stores the edited text instead of the old one.
- The weekly review's **Excel出力** checks coverage of selected rows only and, if
  any Monday–Friday day has zero selected candidates, includes a warning banner
  listing the blank weekdays in the export preview dialog described below.
  Weekends never trigger it.
- Exported Excel sheets use **BIZ UDPゴシック** as the base font; it ships with
  Windows 10 version 1809+ and Windows 11, so no separate install is needed on
  a supported machine. The app UI uses a separate font stack — **Noto Sans JP**,
  falling back to Yu Gothic UI/Segoe UI when it is not installed.
- The weekly review window shows a **今週のクイック入力** sidebar listing the review
  week's quick notes for reference, and the **記入状況** coverage bar's day chips now
  double as a filter — clicking a day (or 全て) narrows the cards and notes shown to
  that day, in addition to the existing low-confidence filter.
- The weekly review's card list is split into two sections: **出力される行 ({n}件)**
  — selected cards, sorted by date, exactly what **Excel出力** will produce — and a
  collapsed-by-default **除外中の行 ({m}件)** section with dimmed cards. Toggling a
  card's **採用** checkbox moves it between the two automatically. The **記入状況**
  coverage bar now counts selected candidates only, matching this section and the
  export preview's blank-weekday warning. Each card also shows a small colored
  origin badge (AI / 手動追加 / メモ / 議事録 / Git / Codex / ファイル / メール / 予定
  / ローカル) so it is obvious at a glance which rows are AI-generated versus raw
  local data, backed by a one-line guidance note above the status banner. Before
  **Excel出力** actually writes the file, an **ExportPreviewWindow** dialog shows a
  read-only, exactly-as-exported rendering of the selected rows (grouped by day,
  numbered like the real report) with the blank-weekday warning inline, replacing
  the previous bare confirmation prompt; **キャンセル** aborts, **出力** proceeds.
  This rework does not change the monthly **月次まとめを出力…** flow.
- Git-sourced candidates show the commit subject, body summary, and add/delete
  statistics in **活動内容** instead of the raw changed-file list, which stays
  visible only in the card's 根拠 line; the same file-list-free text is sent to the
  AI generation prompt. Candidates already stored (including merged rows with
  multiple file lists) before this stripping existed are cleaned up once at
  startup so they don't keep showing the raw file list.
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

## Obsidian連携 (vault sync)

- **自己成長する辞書**: an `entities` dictionary (customer/project/part/person/
  system, plus "other") accumulates canonical names and aliases with an occurrence
  count. It grows two ways — an optional AI extraction pass (see below) and simply
  being observed again — and is entirely local SQLite; nothing about it is synced
  anywhere except into the Markdown it renders as links.
- **2件以上の出現で自動リンク化**: `EntityLinkTargets.From` only offers an entity as
  a link target once its occurrence count reaches the configured
  **リンク化の最低出現回数** (default 2, 設定 → 議事録/Obsidian tab) — a name seen
  once is treated as noise, not yet worth linking. `EntityLinker` then rewrites
  matching text into Obsidian `[[Canonical]]` (or `[[Canonical|alias]]` when the
  matched spelling differs from the canonical one) wikilinks: longest-candidate-wins,
  case-insensitive, and text already inside an existing `[[...]]` link is left alone.
- **除外ファイル**: dropping an `entity-exclusions.md` file (one name per line,
  optionally wrapped as `[[Name]]`, `#`-comment and blank lines ignored) in the
  configured デイリーノート出力フォルダ lets you permanently exclude specific names
  from linking — every sync re-reads it and replaces the dictionary's excluded set
  to match exactly what the file currently lists.
- **同期メニュー**: the tray's **Obsidianへ同期…** action opens a small picker —
  今週 / 先週 / 全期間(バックフィル) (from the earliest quick note or meeting date in
  the database) — plus an "AIで固有名詞を抽出して辞書を更新する" checkbox (default
  on; disabled when no OpenAI API key is stored). With 送信前確認 enabled, extraction
  shows a mandatory count-based confirmation (「対象期間のメモ・議事録テキスト
  {n}件をOpenAIへ送信します」) before anything is sent; declining still lets the
  daily notes sync using the dictionary as it already stands. Extraction batches
  respect the client's per-request caps (≤100 texts / 64 KiB) and any per-batch
  failure is collected and reported, never aborting the rest of the sync.
- **一方向・上書き契約**: generated daily notes (`{folder}/yyyy-MM-dd.md` — メモ/
  会議/開発/週報 sections, each omitted when empty) are a fully regenerated,
  overwritten artifact on every sync, exactly like `MeetingMarkdownWriter`'s meeting
  files — never hand-merged. Do not hand-edit a daily note expecting it to survive
  the next sync; keep durable notes elsewhere in the vault and let WorkLog AI own
  only the generated files.
- Meeting Markdown exports (both the raw-log-only and AI整形 paths) also pass their
  body — never the YAML front matter — through the same entity-link transform at
  export time, so meeting notes and daily notes always link consistently against
  the current dictionary.

### Microsoft 365 setup

Microsoft Graph access requires an Azure AD app registration: a public client with
redirect URI `http://localhost` and delegated `Mail.Read` + `Calendars.Read`
permissions. Enter its client ID (and tenant ID, if not using the default `common`
multi-tenant endpoint) in **設定**, then use **Microsoftサインイン** to complete
sign-in in the system browser before enabling mail/calendar collection.

GitHub network APIs, installers, and automatic updates remain intentionally absent.
Auto-start, the local error log, and the weekly database backup are the Phase 5
operational-quality items already implemented (see Usability additions above).

## Reliability and generation-quality additions

- **Mandatory manual-memo coverage in AI generation**: `AiPromptBuilder`'s system
  instructions require that every 手動メモ (manual quick note) evidence item is
  reflected in at least one generated candidate — none may be silently dropped,
  though multiple memos for the same work may be consolidated into one candidate
  as long as every contributing memo stays cited in `sourceEventIds`. Terse memo
  wording must be rewritten into a complete company-facing report sentence rather
  than transcribed verbatim, and the model is instructed to infer `status` and a
  concrete result from the memo's own wording (e.g. 作成/完了/実施/対応済み →
  `completed` with a specific result; 継続/進行中/検討中 → `ongoing`; otherwise
  `pending`). When the model is not confident enough to state a result, it must
  still fill the result field with its best inference and set
  `needsConfirmation: true` with a `confirmationQuestion` rather than leaving the
  result blank. The JSON Schema and wire format are unchanged — this only
  strengthens the prompt's natural-language instructions.
- **AI candidates supersede covered local rows**: after a successful weekly
  generation, any still-selected, unedited local row (quick memo or meeting
  summary) whose entire evidence set is now fully covered by the generated AI
  candidates is automatically deselected via the pure `LocalCandidateSuppressor`,
  so the raw memo text does not also reach export alongside its AI-shaped
  replacement. A local row only partially covered stays selected as a safety net.
  The generation-summary banner reports how many rows were deselected this way
  (「元メモ由来の行 N件の採用を外しました」).
- **Latest-event-per-sourceRef reduction**: `SourceEventDeduplicator.LatestPerRef`
  collapses events that share a stable `sourceRef` (the prime case being a
  re-formatted meeting summary) down to the newest one — by `CollectedAt`, then
  `OccurredAt`, then id, deterministically — before both local mapping and the
  outbound AI prompt see the week's events, so a re-formatted meeting no longer
  doubles up into two candidates.
- **OpenAI transient retry**: both the weekly generation client and the meeting
  AI整形 client route their HTTP call through the shared `TransientRetryPolicy`,
  which retries a 429, any 5xx, or a transport-level failure up to twice with
  backoff, honoring a short `Retry-After` header when present. Non-retryable
  statuses (400/401/403/404) return immediately, unchanged from prior behavior.
- **APIキーをテスト**: the AI settings tab has a button that probes the configured
  key with a bare `GET /v1/models` call (`OpenAiKeyProbe`) and reports only
  ok/unauthorized/network-error — the key itself and any response body are never
  logged or displayed.
- **Single-instance guard**: `App.xaml.cs` acquires a named `Mutex`
  (`WorkLogAI.App.SingleInstance`, with a separate name under `--sample-data`)
  before any other startup work. A second launch shows a Japanese notice and
  exits instead of creating a duplicate tray icon and a `Ctrl+Alt+W`/`Ctrl+Alt+M`
  hotkey conflict; the mutex is released defensively on exit.
- **Immediate meeting-hotkey apply**: toggling 議事録ホットキー有効化 in settings now
  calls `App.ApplyMeetingHotKeySetting` on save, which registers or unregisters
  `Ctrl+Alt+M` on the spot — no restart required, unlike its original behavior.
- **Tabbed settings**: the settings window is organized into five tabs
  (基本/収集/AI/Microsoft 365/議事録/Obsidian) instead of one long scrolling column,
  at a smaller fixed 560×560 size.
- **Windows CI**: `.github/workflows/ci.yml` builds and tests the full solution —
  including the WPF `WorkLogAI.App` project, which cannot compile on this
  repository's Linux dev host — on `windows-latest` for every push and pull
  request to `main` (`dotnet restore` / `build -c Release` / `test -c Release`).
- **180-day source-event retention**: `DataRetentionService` runs once at startup
  (skipped under `--sample-data`) and deletes `source_events` older than 180 days,
  but only those not referenced by any stored report candidate's evidence list
  (any week, any origin/edited/selected state) — a source event still backing a
  kept candidate is never deleted regardless of age. Failures are caught and sent
  to the error log; retention never blocks startup.
- **Daily backup due-check**: the weekly SQLite backup previously only had a
  chance to run at startup, so a tray instance that stays alive for days without
  restarting could go unprotected. The existing 60-second reminder timer now also
  checks once per calendar day (via a stored `backup.last_checked_date`, the same
  pattern as the reminder's last-shown date) and re-invokes the same
  `DatabaseBackupService.RunIfNeeded` logic, which remains idempotent per week.
- **Monthly summary export**: tray menu action **月次まとめを出力…** opens a month
  picker (current month plus the previous 11, `YYYY年M月`, current first, built by
  the pure `MonthOptionBuilder`), then exports every selected candidate whose
  `work_date` falls in that calendar month — spanning any number of weeks — into
  one workbook via `IWeeklyReportExporter.ExportMonthAsync`, reusing the exact
  per-day grouped layout `ExportAsync` already produces. The filename is
  `{sanitized report title} 月次 {yyyyMM}.xlsx` and the title cell reads
  `{ReportTitle} {year}年{month}月 月次まとめ`. An empty month shows
  「対象月に採用済みの行がありません。」 instead of writing a file.

## Requirements and build

- Windows 10 or later
- .NET 8 SDK with Windows Desktop support, or Visual Studio 2022

```powershell
dotnet restore WorkLogAI.sln
dotnet build WorkLogAI.sln
dotnet test WorkLogAI.sln
dotnet run --project src/WorkLogAI.App
```

`.github/workflows/ci.yml` runs the same restore/build/test sequence on
`windows-latest` for every push and pull request to `main`, so the WPF `App`
project actually compiles in CI even though it cannot on this repository's
Linux dev host.

The `WorkLogAI.App` WPF project (`net8.0-windows`) only builds on Windows. On
non-Windows hosts, build and run `WorkLogAI.Tests` with
`-p:EnableWindowsTargeting=true`, e.g.
`dotnet test tests/WorkLogAI.Tests/WorkLogAI.Tests.csproj -p:EnableWindowsTargeting=true`.
The suite currently has 447 tests.

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
9. Open **今週の記録を見る** to browse notes or manually export the five-column
   Phase 1 report (also offers to open the file after export).
10. Select **月次まとめを出力…**, pick a month (current or one of the previous 11),
    to export every already-selected candidate in that month — across all of its
    weeks — into one workbook (also offers to open the file after export).

The generated file is named
`業務週報 YYYYMMDD-YYYYMMDD.xlsx` and matches the layout of the user's real
submitted weekly report: a title row (A1:C1) plus the company name (E1) and
employee name (E2) stacked in their own cells, a blue-filled white-bold header
row with the captions 日付/曜日/項目・案件・目標金額/活動内容/結果・決定事項・今後の課題,
and a **day block per calendar day** (only days with at least one selected
item) made up of **one sheet row per item**, so a day's 項目, 活動内容, and
結果・決定事項 for a given item always sit on the same row and stay aligned
even once text wraps. Each day's date and weekday live in their own separate
cells — 日付 holds the date (`yyyy/MM/dd`) alone and 曜日 holds the
single-character Japanese weekday in parentheses, e.g. `(月)` — each written
only on the block's first row; every other row in the block leaves both 日付
and 曜日 empty. Multiple items on the same day are numbered with circled
digits (①②③…, falling back to `(21)`, `(22)`, … past ①-⑳), with the same
number repeated on each item's 項目 and 活動内容 cells on its own row so the
two line up by number; that row's 結果・決定事項 cell holds the numbered
result text only when it is non-blank, and is otherwise left empty. Each
day's block is padded with blank rows so it always totals at least 4 sheet
rows and always keeps at least one trailing blank row, even on a day with 4
or more items, so every day reads as at least the same visual size and the
boundary before the next day is always visible. Borders are drawn
constructively, never as a blanket border cleared back off in places:
vertical lines run continuously down every column for every row of the table,
and a horizontal line appears only at the bottom of each day's block (the
boundary before the next day) — no lines appear between item rows or around
blank rows. The title (and the company/employee name shown alongside
it) are configurable in **設定**.

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

**APIキーをテスト** (`OpenAiKeyProbe`) is a fifth kind of outbound call, user-
initiated from settings: a bare `GET /v1/models` with the configured key as a
bearer token. It reports only ok/unauthorized/network-error and never logs or
displays the key or any response body.

The 180-day source-event retention (`DataRetentionService`) only ever deletes rows
from `source_events`; it never deletes a `report_candidates` row and never deletes
a source event still referenced by any stored candidate's evidence list, so a kept
weekly or monthly export result can never lose its backing evidence out from under
it.

See [architecture](docs/ARCHITECTURE.md) and the
[phase checklist](docs/PHASE_CHECKLIST.md).
