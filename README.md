# WorkLog AI

WorkLog AI is a Windows-only .NET 8 WPF tray application for capturing short work
notes, collecting safe local work metadata, and exporting a manual weekly report.
This repository currently implements Phases 1 through 3 only.

## Phase 1 features

- `Ctrl + Alt + W` opens a keyboard-only, single-line quick capture window.
- `Enter` saves and closes, `Esc` discards and closes, and `Ctrl + Enter` saves,
  clears, and stays open.
- Notes receive a local timestamp automatically and persist in SQLite.
- The tray menu opens quick capture, weekly history, settings, and the collection
  action extended by Phase 2.
- Weekly history supports previous/next week, soft deletion, reopening, and manual
  XLSX export.
- Settings include company, employee, week start, and Excel output directory.
- A `--sample-data` mode uses a separate database and seeds three example notes.

## Phase 2 features

- A user-triggered **今週の候補を生成** action collects only configured local
  repositories and folders.
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

Microsoft Graph, Outlook/calendar, GitHub network APIs, email collection, installers,
and automatic updates remain intentionally absent.

## Requirements and build

- Windows 10 or later
- .NET 8 SDK with Windows Desktop support, or Visual Studio 2022

```powershell
dotnet restore WorkLogAI.sln
dotnet build WorkLogAI.sln
dotnet test WorkLogAI.sln
dotnet run --project src/WorkLogAI.App
```

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
5. Select **今週の候補を生成** to run local collection and, after preview approval,
   generate candidates.
6. Review cards, edit fields, inspect evidence, merge duplicates, and select rows.
7. Use **Excel出力** in review to persist changes and export selected rows only.
8. Open **今週の記録を見る** to browse notes or manually export the four-column
   Phase 1 report.

The generated file is named
`業務週報(USA太田) YYYYMMDD-YYYYMMDD.xlsx`.

## Security

The SQLite `settings` table is used only for non-secret preferences. Its historical
column name is `value_encrypted`, but Phase 1 does not claim encryption and rejects
setting keys that could contain passwords, tokens, API keys, client secrets, or
credentials. Future credential-bearing phases must use Windows Credential Manager.
The application does not log note bodies or credentials.

Collection paths are stored newline-delimited as non-secret preferences. A collection
run occurs only after the user selects the tray action. Git is invoked through
`ProcessStartInfo.ArgumentList`; no configured path is interpolated into a shell.

The outbound AI request contains sanitized event IDs, dates, source types, titles,
summaries, and reduced evidence only. It excludes `sourceRef`, full local paths,
source code, diffs, function output, full threads, logs, credentials, and API keys.
No transmission happens during startup, background operation, or settings save.

See [architecture](docs/ARCHITECTURE.md) and the
[phase checklist](docs/PHASE_CHECKLIST.md).
