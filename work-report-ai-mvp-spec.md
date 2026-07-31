# AI業務週報アシスタント MVP仕様

## 1. 目的

週報のために業務を再入力する作業をなくす。

- 現場・対人業務だけを1行メモで5〜10秒記録する
- PC上に残る業務履歴を週末に自動収集する
- AIが業務候補を作成する
- ユーザーは採用・除外・修正だけ行う
- 最終的に既存週報と同じ4列で出力する

最終出力列:

1. 日時
2. 業務項目
3. 活動内容
4. 結果・決定事項／今後の課題

## 2. 製品名と配置

仮称: `WorkLog AI`

推奨リポジトリ:

```text
C:\conda\worklog-ai
```

kintone用コードとは分離した独立リポジトリとする。

## 3. 採用技術

- Windowsデスクトップ: C# / .NET 8 / WPF
- 配布: self-contained single-file EXE
- ローカルDB: SQLite
- AI: OpenAI Responses API
- AI出力: JSON Schemaによる構造化出力
- Outlook・予定表: Microsoft Graph delegated permissions
- GitHub: GitHub REST APIまたはローカルgitコマンド
- Excel出力: ClosedXML
- 秘密情報: Windows Credential Manager

## 4. 基本UX

### 4.1 クイック入力

グローバルショートカット:

```text
Ctrl + Alt + W
```

表示内容は1行入力欄だけとする。

入力例:

```text
新人検査員 ハイトゲージ教育 継続
顧客A初品5点 検査 全数合格 出荷指示
マスク検査 姿勢改善 台車試した 不採用
```

動作:

- Enter: 保存して閉じる
- Esc: 保存せず閉じる
- Ctrl+Enter: 保存後も入力欄を残す
- 日時は自動付与
- 保存成功時は0.8秒だけ小さく「保存しました」と表示
- マウス操作は不要
- 必須項目は本文だけ

### 4.2 トレイメニュー

- クイック入力
- 今週の候補を生成
- 今週の記録を見る
- 設定
- 終了

### 4.3 週次レビュー

候補をカード形式で表示する。

各カード:

- 採用チェック
- 日付
- 業務項目
- 活動内容
- 結果・課題
- 根拠ソース
- AI確信度

操作:

- 全採用
- 低確信度だけ表示
- 重複候補を統合
- 1行追加
- Excel出力

レビュー画面では、AIが確定した文章を修正できる。

## 5. さかのぼり対象

### 5.1 手動クイックメモ

最優先の一次情報として扱う。

確信度: High

### 5.2 Outlook送信済みメール

取得対象:

- 当該週に送信したメール
- 件名
- 送信日時
- 宛先
- 本文の新規記述部分

除外:

- 定型返信
- 単なる日程調整
- 自動通知
- 署名
- 過去メールの引用

確信度: Medium〜High

### 5.3 Outlookカレンダー

取得対象:

- 当該週の予定
- 件名
- 開始・終了日時
- 場所
- 本文

予定だけでは完了と断定しない。

確信度: Medium

### 5.4 Gitリポジトリ

まずローカルgitを優先する。

取得対象:

- 当該週の自分のコミット
- コミットメッセージ
- 変更ファイル名
- 追加・削除行数
- 未コミット変更のファイル名

AIへ送る情報:

- コミットメッセージ
- 変更ファイル一覧
- 統計

原則としてソースコード本文やdiff全文は送らない。

確信度: High

### 5.5 Codexセッション

CodexのローカルセッションJSONLは巨大化する場合があるため、全文読み込み禁止。

許可する情報:

- セッション日時
- 作業ディレクトリ
- ユーザー指示
- 最終回答・完了報告
- 変更ファイル名
- 実行コマンド名の短い一覧

禁止:

- reasoningレコード
- compacted履歴
- function_call_output全文
- ソースコード本文
- 秘密情報を含む環境変数
- 1セッションあたり256KBを超える読み込み

確信度: Medium〜High

### 5.6 最近更新したファイル

設定された業務フォルダのみ対象とする。

取得対象:

- ファイル名
- パス
- 更新日時
- 種別

ファイル本文は初期設定では読まない。

確信度: Low〜Medium

### 5.7 ChatGPT履歴

ローカルアプリからChatGPT全履歴を公式に直接取得する前提にはしない。

代替:

- 重要業務はクイックメモへ1行保存
- Codex完了報告を収集
- 必要に応じて週次レビュー時にChatGPT側で追加候補を確認する

## 6. 候補生成ロジック

### 6.1 ソースイベント

全ソースを次の共通形式へ変換する。

```json
{
  "eventId": "uuid",
  "occurredAt": "2026-07-30T14:30:00-04:00",
  "sourceType": "manual|outlook_mail|calendar|git|codex|file",
  "title": "string",
  "body": "string",
  "evidence": "string",
  "sourceRef": "string",
  "confidence": 0.0
}
```

### 6.2 AI出力

```json
{
  "candidates": [
    {
      "date": "2026-07-30",
      "workItem": "PC故障対応",
      "activity": "ノートPCの充電不良について切り分け確認を実施。",
      "resultOrNext": "バッテリー取り外し後に起動を確認。バッテリー交換を検討する。",
      "status": "completed|ongoing|pending",
      "confidence": 0.91,
      "sourceEventIds": ["uuid"],
      "needsConfirmation": false,
      "confirmationQuestion": null
    }
  ]
}
```

### 6.3 AIルール

- 根拠がない成果を追加しない
- 予定表だけから完了扱いにしない
- 同じ業務の複数日作業は必要に応じて統合する
- 技術詳細を会社向けの業務成果へ変換する
- メール送信自体ではなく、メールが示す判断・対応・完了を抽出する
- 日常の定型処理は原則除外する
- 教育、改善、問題解決、検査、判断、システム開発、設備対応を優先する
- 不明点は最大3件まで確認候補にする
- 日付を推測で変更しない

## 7. 重複統合

次を使って重複を判定する。

- 日付の近さ
- 業務項目の意味類似度
- 共通する固有名詞
- 同じリポジトリ・同じ会議・同じメールスレッド
- 同じソースイベント

統合時には全根拠を保持する。

## 8. データベース

### quick_notes

- id
- created_at
- text
- deleted_at

### source_events

- id
- occurred_at
- source_type
- title
- body
- evidence
- source_ref
- confidence
- content_hash
- collected_at

### report_candidates

- id
- week_start
- work_date
- work_item
- activity
- result_or_next
- status
- confidence
- selected
- edited
- source_event_ids_json

### settings

- key
- value_encrypted

## 9. セキュリティ

- APIキー、Microsoftトークン、GitHubトークンはWindows Credential Managerへ保存
- SQLiteへ秘密情報を平文保存しない
- Outlookは読み取り権限のみ
- GitHubはContents readのみ
- AIへ送信する前に署名、引用、トークンらしき文字列を除去
- `.env`、秘密鍵、認証ファイルは収集対象外
- ユーザーが候補生成を押した時だけ外部送信する
- 送信前プレビューを設定で有効化できる
- ログにメール本文やAPIキーを残さない

## 10. Excel出力

ファイル名:

```text
業務週報 YYYYMMDD-YYYYMMDD.xlsx
```

タイトルは設定で変更可能。

レイアウト:

- 上部左: 週報タイトル 開始日〜終了日
- 上部右: 会社名 / 氏名
- 4列
- 横向き
- 1ページ幅に調整
- 行高自動
- セル内折り返し
- 日付順

PDFはExcelから印刷できることをMVP要件とする。

## 11. 設定画面

- 氏名
- 会社名
- ホットキー
- OpenAI APIキー
- 使用モデル
- Outlook連携
- GitHub連携
- 対象ローカルリポジトリ
- Codexセッションフォルダ
- 最近更新ファイルの対象フォルダ
- 週の開始曜日
- Excel保存先

## 12. 実装フェーズ

### Phase 1: 入力ツール

- WPFトレイアプリ
- グローバルホットキー
- 1行入力
- SQLite保存
- 履歴表示
- Excel手動出力

### Phase 2: ローカルさかのぼり

- ローカルgit収集
- Codex安全要約収集
- 最近更新ファイル収集
- 候補一覧作成

### Phase 3: AI候補生成

- Responses API
- JSON Schema出力
- 重複統合
- レビュー画面
- Excel出力

### Phase 4: Microsoft 365

- Microsoft Graphサインイン
- 送信済みメール
- カレンダー
- 収集範囲設定

### Phase 5: 運用品質

- インストーラー
- 自動起動
- クラッシュ復旧
- ログローテーション
- 自動更新は別途判断

## 13. MVP受入条件

1. `Ctrl + Alt + W`から5秒以内にメモを保存できる
2. 入力時にフォーム項目選択が不要
3. アプリ再起動後もメモが残る
4. 指定週のメモ、git、Codex候補を収集できる
5. Codex JSONLを全文読み込みしない
6. AIが4列形式の候補を構造化生成できる
7. 候補の採用・除外・編集ができる
8. 既存週報に近いExcelを出力できる
9. AI根拠ソースを候補ごとに確認できる
10. 根拠のない完了結果を生成しない

## 14. Codexへの実装指示

```text
Implement the WorkLog AI MVP described in this repository specification.

Execution order:

1. Inspect the repository and create a concise implementation plan.
2. Implement Phase 1 completely before starting Phase 2.
3. Keep the application Windows-only and use C# .NET 8 WPF.
4. Use SQLite for persistence.
5. Implement a tray application and global hotkey Ctrl+Alt+W.
6. The quick capture window must contain only a single-line input field.
7. Enter saves and closes, Esc closes without saving, Ctrl+Enter saves and stays open.
8. Add unit tests for storage, week-range calculation, source-event deduplication, and report mapping.
9. Do not implement Microsoft Graph until the local capture, git collection, Codex safe parser, AI candidate generation, review UI, and Excel export are working.
10. The Codex parser must use a strict allowlist and must never ingest reasoning, compacted history, or full function_call_output records.
11. Cap each Codex session read at 256 KB of selected content after parsing.
12. Never store API keys or tokens in SQLite or config files. Use Windows Credential Manager.
13. Use the OpenAI Responses API with strict JSON Schema output for report candidates.
14. Do not send source-code diffs or full email threads to the AI by default.
15. Preserve evidence links from every report candidate back to source events.
16. Generate an XLSX report matching the four-column Japanese weekly-report layout.
17. Run build and tests after each phase.
18. Do not commit or push unless explicitly instructed.

Deliverables:

- Buildable Visual Studio solution
- README with setup and usage
- Architecture notes
- Database migrations
- Automated tests
- Sample data mode
- Phase completion checklist

Before implementation, report any blocking ambiguity. Otherwise proceed without waiting for approval.
```

## 15. 初期判断

最初からOutlook連携まで作ると認証設定で止まりやすい。

最初の完成ラインは次とする。

```text
クイック入力
+ ローカルgit履歴
+ Codex完了報告の安全抽出
+ AI候補生成
+ レビュー
+ Excel出力
```

この構成だけでも、システム開発業務と手入力した現場業務を高い精度で週報化できる。
