# ストレージ（永続化）ガイド

NolumiaScheduler はデータの保存先（リポジトリ実装）を **JSON ファイル** と **SQLite** から
選べます。既定は **JSON** です。切り替えはコード変更なしで、環境変数だけで行えます。

- 切り替え: `NOLUMIA_STORAGE`（`Json` / `Sqlite`、既定 `Json`）
- 既存データの移行: `NOLUMIA_MIGRATE`（`json-to-sqlite` / `sqlite-to-json`）

選択は合成ルート（`NolumiaScheduler.WinUI/App.xaml.cs`）で解決され、ドメインの
リポジトリインターフェース（`ICalendarEventRepository` ほか）に対して実装だけが
差し替わります。アプリのロジックは保存先を意識しません。

---

## 1. データの保存場所

ベースフォルダ: `%LOCALAPPDATA%\NolumiaScheduler\`
（通常 `C:\Users\<ユーザー>\AppData\Local\NolumiaScheduler\`）

| バックエンド | 場所 |
|---|---|
| JSON（既定） | `events\<id>.json`（1 予定 = 1 ファイル）、`business-calendars\<id>.json`、`settings.json` |
| SQLite | `nolumia.db`（単一ファイル。テーブル: `calendar_events` / `business_calendars` / `app_settings` / `schema_migrations`。`calendar_events` には期間検索用の `span_start_day` / `span_end_day` 列とインデックスあり） |

> 2 つは別々の場所に保存されます。バックエンドを切り替えても、移行（後述）を
> 行わない限り、もう一方のデータは見えません（消えるわけではありません）。

---

## 2. バックエンドの切り替え

### 一時的（その PowerShell セッションのみ）

```powershell
$env:NOLUMIA_STORAGE = "Sqlite"   # SQLite で起動
# 既定（JSON）に戻す:
Remove-Item Env:\NOLUMIA_STORAGE
```

### 恒久的（ユーザー環境変数として保存）

```powershell
setx NOLUMIA_STORAGE Sqlite
# 反映には新しいプロセス（再ログイン推奨）が必要
```

値が未設定・不正な場合は `Json` にフォールバックします。

---

## 3. JSON → SQLite へのデータ移行

`NOLUMIA_MIGRATE` を付けて **一度だけ** 起動すると、既存データがコピーされます。
移行は `StorageMigrator`（`NolumiaScheduler.Infrastructure`）が、ドメインの
リポジトリ経由で全件を読み出し、移行先へ `Save`（id でアップサート）します。

移行は **管理 CLI（`NolumiaScheduler.Cli`）** で行います。アプリの起動とは独立した
操作なので、環境変数を切り替える事故がありません。

```powershell
# 0. CLI をビルド（または publish した実行ファイルを使用）
dotnet build NolumiaScheduler.Cli/NolumiaScheduler.Cli.csproj

# 1. 念のためバックアップ
Copy-Item "$env:LOCALAPPDATA\NolumiaScheduler" "$env:LOCALAPPDATA\NolumiaScheduler.bak" -Recurse

# 2. 現状確認
dotnet run --project NolumiaScheduler.Cli -- info

# 3. JSON → SQLite へコピー（全予定・営業日カレンダー・設定）
dotnet run --project NolumiaScheduler.Cli -- migrate json-to-sqlite

# 4. アプリを SQLite で起動するよう設定（選択は引き続き環境変数）
setx NOLUMIA_STORAGE Sqlite
```

逆方向（SQLite → JSON）:

```powershell
dotnet run --project NolumiaScheduler.Cli -- migrate sqlite-to-json
```

### 移行の安全装置

- **移行先が空のときだけ実行**します（予定・営業日カレンダーが 0 件）。既にデータが
  ある場合はスキップし、終了コード 2 で通知します。意図的に上書きする場合のみ
  `--force` を付けます（id 単位のアップサート）。
- 元データ（移行元）は変更しません。コピーのみです。
- 不明な方向を指定するとエラー（`json-to-sqlite` / `sqlite-to-json` のみ有効）。

> バックエンドの**選択**（アプリ起動時にどちらを使うか）は引き続き
> `NOLUMIA_STORAGE`（[2 章](#2-バックエンドの切り替え)）です。**データの移行**だけが
> CLI に分離されています。

### CLI のその他のコマンド

```text
nolumia <command> [arguments] [--data-dir <dir>]

  info                            データ場所とバックエンド別の件数を表示
  migrate <direction> [--force]   バックエンド間でデータをコピー
  seed <backend>                  空のバックエンドにサンプル予定を投入
  list <backend>                  予定を活動日スパン付きで一覧
  db-migrate                      SQLite のスキーマ migration を適用
  help                            ヘルプ

backend: json | sqlite
--data-dir <dir>   データフォルダを上書き（既定: ユーザーのアプリデータ）
```

---

## 4. SQLite のスキーマ変更（バージョン付きマイグレーション）

スキーマは `schema_migrations` テーブルでバージョン管理され、起動時に未適用分だけが
昇順・トランザクション内で適用されます（冪等）。

新しいスキーマ変更を追加する手順:

1. `NolumiaScheduler.Infrastructure/Sqlite/Db/Migrations/` に
   `Mxxxx_説明.cs` を追加し、`ISqliteMigration` を実装（`Version` は連番）。
2. `SqliteMigrationRunner` の `Migrations` 配列に追加。
3. `M0001_InitialSchema` を参考に、`Up(connection, transaction)` 内で DDL を実行。

```csharp
internal sealed class M0002_AddSomething : ISqliteMigration
{
    public int Version => 2;
    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "ALTER TABLE calendar_events ADD COLUMN ...;";
        cmd.ExecuteNonQuery();
    }
}
```

---

## 5. 設計メモ（なぜこの形か）

- **DDD のレイヤー境界**: SQLite アクセスは Infrastructure 層に限定。Domain /
  Application は無変更。リポジトリは行モデル（`*Row`）とドメインを Mapper で変換し、
  SQL は DAO が担当します。
- **DRY**: 予定の複雑な再帰ルール等は、JSON 実装と同じ DTO をそのまま SQLite の
  `payload`（JSON テキスト列）に再利用。直列化ロジックの真実の源は 1 つです。

---

## 6. パフォーマンス：期間クエリ（実装済み）

カレンダー描画は、以前は予定を **全件読み出し** していました。現在は
`ICalendarEventRepository.FindByPeriod(from, to)` で **表示範囲に関係する予定だけ**
を取得します。

仕組み（再帰イベントがあるため 2 段階）:

1. **粗いフィルタ（リポジトリ）**: 各イベントの「活動日スパン」
   （単発: 予定日、繰り返し: 開始日〜繰り返し終了日。移動された回も含む）が
   表示窓と重なるものだけ返す。境界の業務日調整に備え `±31 日` の余裕を持たせる
   （`CalendarEvent.PeriodOverlapMarginDays`）。
2. **正確な展開（ドメイン）**: 絞り込んだ候補に対し `OccurrenceExpander` が実際の
   発生日を厳密に算出。粗いフィルタの誤ヒットはここで除去されるため、安全です。

バックエンド別の効き方:

- **SQLite**: `calendar_events.span_start_day` / `span_end_day`（インデックス付き）に
  対する `WHERE` で、DB 側が表示範囲分だけを返します。全件ロードが消え、ここで
  SQLite が明確に有利になります。スパン列は保存時に計算され、既存 DB は
  マイグレーション `M0002` が自動でバックフィルします。
- **JSON**: `FindByPeriod` の既定実装（全件読み出し＋メモリ内で重なり判定）です。
  ファイル I/O は減りませんが、その後の発生展開コストは削減されます（JSON は
  「シンプルな既定」という位置づけ）。

このほか:

- **アラーム走査**（`AlarmApplicationService`）も `today..tomorrow` の期間クエリで
  対象イベントだけを取得するよう変更済みです（展開は従来どおり厳密）。
- 週ビューの祝日判定をループ外で 1 回だけ集計するよう修正済みです（以前は日ごとに
  全営業日カレンダーを再走査していました）。

### まだ残っている改善余地（別タスク向け）

- 読み込み結果のキャッシュ／差分更新による `FindXxx` 呼び出し回数のさらなる削減。
- 起動の遅さには WinUI / Windows App SDK の初期化コストも含まれるため、体感が
  改善しない場合は起動時間の計測で切り分けるのが確実です。
