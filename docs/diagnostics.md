# 診断・イベントログガイド

「気づいたら落ちている」「休止から復帰すると落ちていることが多い気がする」——
その原因を後から特定できるようにするための仕組みをまとめます。

---

## 1. なぜ今まで何も残っていなかったのか

これは記録漏れではなく、記録される条件を満たしていなかったためです。原因は 4 つあります。

| # | 原因 | 結果 |
|---|---|---|
| 1 | 例外ハンドラが XAML の `Application.UnhandledException` だけだった | UI スレッド以外（タイマー、スレッドプール、WinRT コールバック）で発生した例外はハンドラを通らず、**何も書かずにプロセスが終了**する |
| 2 | `TaskScheduler.UnobservedTaskException` が未購読 | `_ = CheckAlarmsAsync()` のような fire-and-forget が失敗しても .NET は既定で無視するため、**アラームだけ死んでアプリは動き続ける** |
| 3 | ハンドラが `e.Handled = true` としてから `Exit()` していた | プロセスは「正常終了」として終わるため、Windows のイベントログに `Application Error` / `.NET Runtime` が**記録されない**（＝「エラーなら残るはず」が成立しない） |
| 4 | ネイティブ側のクラッシュ（XAML/コンポジション層、GDI/USER ハンドル枯渇、OS による kill） | マネージド例外が一切発生しないので、**どんな例外ハンドラを足しても捕捉できない** |

4 番目に対しては例外ハンドラでは原理的に足りません。そのため「生存記録」を残し、
次回起動時に**前回が正常終了だったかどうかを判定する**方式を採っています（→ 3 節）。

---

## 2. 出力先

ベースフォルダ: `%LOCALAPPDATA%\NolumiaScheduler\`

| 出力先 | 内容 |
|---|---|
| `logs\nolumia-YYYYMMDD.log` | 日付ごとのローリングログ。既定 14 日で自動削除 |
| `logs\session.txt` | 実行中セッションの生存記録（3 節） |
| `crash.log` | 致命的例外の詳細（内部例外の HRESULT 連鎖つき）。**追記**されます |
| Windows イベントログ `Application` | `Warning` 以上のみ。ソース名 `Nolumia Scheduler` |

ログフォルダは **設定画面の「診断」→「ログフォルダを開く」** からも開けます。

ログは 1 行 1 レコードで、書き込みのたびにファイルを閉じます（バッファに溜めません）。
プロセスが突然死しても、**直前の行まで必ずディスクに残る**ようにするためです。

```
2026-07-30 21:03:11.482 +09:00 INFO    pid=12345 tid=1 [Power] suspend: System is suspending (sleep/hibernate).
2026-07-30 23:41:02.117 +09:00 INFO    pid=12345 tid=1 [Power] resume: System resumed from suspend by user action. Suspended for 2:37:51 (since 2026-07-30 21:03:11).
2026-07-30 23:41:02.140 +09:00 INFO    pid=12345 tid=7 [Health] (resume) workingSet=214MB gcHeap=38MB handles=612 gdi=284 user=173 threads=31 uiLag=0.0s
```

### Windows イベントログのソース登録（任意）

未登録でもイベントは `Application` ログに記録されますが、メッセージリソース DLL が無いため
Event Viewer が「説明が見つかりません」という前置きを付けます（本文自体は表示されます）。
これを消すには、**管理者権限の PowerShell** で 1 度だけ次を実行します。

```powershell
New-EventLog -LogName Application -Source "Nolumia Scheduler"
```

---

## 3. 突然死の検出（session.txt）

例外を出さずに死ぬケースを捕まえるための仕組みです。考え方は「**明示的に正常終了と書かない限り、
異常終了とみなす**」です。

1. 起動時に `logs\session.txt` を作成し、pid・バージョン・開始時刻を書き込む
2. 1 分ごとに `lastHeartbeat` を更新する（生存記録）
3. サスペンド／復帰／画面オフなどの節目で `lastEvent` を更新する
4. 正常終了時に `cleanExit=true` を書く。
   Windows のシャットダウン／ログオフ（`WM_ENDSESSION`）も正常終了として扱います。
   OS 都合の終了まで異常終了として数えると、本当の突然死が埋もれるためです

次回起動時、`cleanExit=false` のまま残っていれば前回は突然死しています。その事実を
`Fatal` としてログと Windows イベントログに記録します。

```
2026-07-31 08:12:03.554 +09:00 FATAL   pid=15012 tid=1 [Crash] Previous session ended without a clean
    shutdown — it was killed, or it crashed without raising a managed exception.
    pid=12345 version=v1.0.0-3-g1a2b3c4 startedAt=2026-07-30 09:14:22 +09:00
    lastHeartbeat=2026-07-30 23:42:02 +09:00 uptime=14:27:40 lastEvent=resume cleanExit=False
    exitReason=(none)
```

ここが本題への答えになります。`lastEvent` が `resume` で、`lastHeartbeat` が復帰直後で
止まっていれば、**「復帰の直後に落ちている」ことが推測ではなく記録として確定**します。
逆に `lastEvent=display-off` なら疑うべき層はまったく別です。

同じ内容は**設定画面の「診断」**にも「前回は正常に終了していません（最終動作: …、直前のイベント: …）」
として表示されます。

---

## 4. 記録している内容

| カテゴリ | イベント ID | 内容 |
|---|---|---|
| `Lifecycle` | 1010 | プロセス開始／起動完了／終了、二重起動のリダイレクト |
| `Crash` | 1000 | 未処理例外、未観測タスク例外、前回セッションの異常終了 |
| `Power` | 1030 | サスペンド、復帰（休止していた時間つき）、画面オン／オフ／減光、ノート PC の蓋の開閉 |
| `Session` | 1020 | ロック／ロック解除、コンソール接続／切断、リモート接続／切断、ログオフ・シャットダウン |
| `Health` | 1040 | 10 分ごとのリソース標本、および異常時の警告 |
| `Alarm` | 1050 | アラーム監視の開始／停止、通知失敗、期限切れ予定の削除 |
| `Tray` | 1060 | タスクトレイアイコンの再作成・登録失敗 |

イベント ID は Event Viewer のフィルターや監視ルールから参照される前提の契約です。
**既存の値の意味を変えず、追加のみ**にしてください。

### Health 標本が見ているもの

長期常駐アプリで例外を残さず死ぬ典型パターンを狙って計測しています。

- `gdi` / `user` — GDI・USER ハンドル数。Windows のプロセス上限は 10,000 で、
  超えるとマネージド例外なしにプロセスが落ちます。ウィンドウ・アイコン・メニューを
  繰り返し作るアプリのリークはここに出ます。8,000 で警告します。
- `workingSet` / `gcHeap` — メモリリーク傾向。10 分ごとの推移で読みます。
- `uiLag` — UI スレッドの無応答時間。バックグラウンドスレッドから ping して測るため、
  **UI が固まっているだけの状態とプロセスが死んだ状態を区別**できます。
  ユーザーからはどちらも「落ちた」に見えます。

---

## 5. 「落ちた」ときの調べ方

1. 設定画面の「診断」→「ログフォルダを開く」
2. `nolumia-<落ちた日>.log` を開き、末尾から `[Crash]` を探す
3. 見つからない場合は**次回起動分のログの先頭**を見る。突然死は死んだ側ではなく、
   次に起動した側が記録します（3 節）
4. `lastEvent` と `lastHeartbeat` で、どの状態のときに止まったかを確定させる
5. `[Health]` の直近数件で `gdi` / `user` / `workingSet` が伸び続けていないか確認する
6. Windows イベントログの `Application` を同じ時刻帯で確認する。
   `Nolumia Scheduler`（本アプリ）に加えて、OS 側の `Application Error` /
   `.NET Runtime` / `Kernel-Power` が同時刻にあればネイティブ層のクラッシュや
   電源イベントとの関連が裏付けられます

---

## 6. 実装の場所

| 役割 | 場所 |
|---|---|
| ログ抽象・ファイル出力・保持期間 | `NolumiaScheduler.Infrastructure/Diagnostics/` |
| 生存記録（突然死の検出） | `Infrastructure/Diagnostics/AppSessionMarker.cs` |
| グローバル例外ハンドラ | `WinUI/Diagnostics/CrashReporter.cs` |
| 電源・セッション監視 | `WinUI/Diagnostics/SystemStateWatcher.cs` |
| リソース・UI 応答監視 | `WinUI/Diagnostics/AppHealthMonitor.cs` |
| Windows イベントログ出力 | `WinUI/Diagnostics/WindowsEventLogAppLog.cs` |
| 起動時の組み立て | `WinUI/Diagnostics/AppDiagnostics.cs`、`WinUI/Program.cs` |

ログ出力は**絶対に例外を投げません**。クラッシュハンドラから呼ばれるため、
診断側の失敗で本来の原因が隠れることのほうが有害だからです。
