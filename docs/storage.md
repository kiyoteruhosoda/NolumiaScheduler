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
| SQLite | `nolumia.db`（単一ファイル。テーブル: `calendar_events` / `business_calendars` / `app_settings` / `schema_migrations`） |

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

手順（PowerShell）:

```powershell
# 1. 念のためバックアップ（フォルダごとコピー）
Copy-Item "$env:LOCALAPPDATA\NolumiaScheduler" "$env:LOCALAPPDATA\NolumiaScheduler.bak" -Recurse

# 2. 移行先を SQLite に設定し、移行方向を指定
$env:NOLUMIA_STORAGE = "Sqlite"
$env:NOLUMIA_MIGRATE = "json-to-sqlite"

# 3. アプリを一度起動 → JSON の全予定・営業日カレンダー・設定が nolumia.db にコピーされる
#    （移行先に既にデータがある場合は安全のためスキップします）

# 4. 移行フラグを外す。以後は SQLite で通常起動
Remove-Item Env:\NOLUMIA_MIGRATE
```

逆方向（SQLite → JSON）も同様です:

```powershell
$env:NOLUMIA_STORAGE = "Json"
$env:NOLUMIA_MIGRATE = "sqlite-to-json"
# 起動 → フラグを外す
Remove-Item Env:\NOLUMIA_MIGRATE
```

### 移行の安全装置

- **移行先が空のときだけ実行**します（予定・営業日カレンダーが 0 件）。
  既にデータがあるとスキップするので、`NOLUMIA_MIGRATE` を外し忘れても
  新しい編集を古いデータで上書きしません。
- 元データ（移行元）は変更しません。コピーのみです。
- 不明な値を指定すると起動時に例外になります（`json-to-sqlite` /
  `sqlite-to-json` のみ有効）。

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

## 6. パフォーマンスに関する注意（重要）

現状、アプリは予定を **常に全件読み出し** ます（`ICalendarEventRepository` には
`FindAll` / `FindById` しかなく、月・週単位の範囲クエリがありません）。さらに
`CalendarViewModel` などは描画のたびに `FindAll` を複数回呼びます。

このため:

- **SQLite に替えるだけでは大幅には速くなりません**。全件ロード＋（同じ）JSON
  逆シリアライズという処理量は JSON 実装と大差なく、SQLite の真価（インデックスを
  使った範囲クエリ）を活かせていないためです。
- 起動の遅さには、WinUI / Windows App SDK の初期化コストも含まれます。まずは
  どこで時間がかかっているか計測することを推奨します。

将来の高速化方針（別タスク向け）:

1. リポジトリに日付範囲クエリを追加（例: `FindByPeriod(from, to)`）。SQLite なら
   `start`/`end` 列＋インデックスで表示範囲のみ取得でき、ここで初めて SQLite が
   明確に有利になります。
2. `FindAll` の重複呼び出しを削減（読み込み結果のキャッシュ／差分更新）。
3. SQLite の `payload` 内 JSON ではなく、検索に使う列を正規化する。
