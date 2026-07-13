# 時刻モデルとデータ構造（確定仕様）

予定の開始・終了・繰り返しを **どう保持し・どう解釈し・どう表示するか** の正式仕様です。
ドメインの時刻モデルおよびデータ構造の単一の真実の源（source of truth）であり、実装・
テスト・バリデーションはすべて本書に従います。

本書は予定の時刻表現に関して、`NolumiaSchedulerTest/Inputs/ValidationDifinication.md` の
旧記述（終日概念・繰り返しの日マタギ禁止・終了時刻保持）を **上書き** します。

---

## 1. 結論（1枚サマリ）

- 予定は **UTC の絶対時刻（instant）で保持**し、`TimeZoneId` は表示・繰り返し評価・業務日シフト用の
  **メタデータ**として持つ。
  - 単発: `StartUtc`（UTC instant）＋ `DurationMinutes`。
  - 繰り返し: `AnchorUtc`（先頭オカレンスの UTC instant）＋ ルール ＋ `DurationMinutes`。
- **終了は保持しない**。常に `開始instant + DurationMinutes` で導出する。
- **`AllDay` というドメイン概念は持たない**。終日は編集画面の入力トグル（当日 24h 限定）に過ぎない。
- **`24:00` は表示専用**。値としては保持しない。
- **instant 優先**：制度（DST／政治的ルール）が変わってローカル時刻がずれても、保持した instant は
  不変。ずらしたくなければ再保存する（＝制度変更の負担はその制度内の人が負い、外の人の表示は動かない）。
- 旧データとの **互換は持たない**。変換は一回限りの管理 CLI（`migrate-schema`）で行う（§9）。

---

## 2. 設計原則（前提と制約）

### 2-1. UTC 保持

予定の「絶対時刻」を真値とし、UTC で固定する。狙いは **制度変更の影響を、その制度の中の人だけに
留め、外の人を巻き込まない**こと。

- UTC 固定なら絶対時刻が不変 → 外（例：日本）の人の表示は動かない。制度が変わった側（例：NY）の
  ローカル表示だけが動く。ずらしたくなければ再保存する。
- 壁時計＋TZID で都度解決する方式では、tz データベース更新（制度変更）で未来のオカレンスの instant が
  再計算され、外の人の表示まで動いてしまう。これを避ける。
- トレードオフ：DST のあるゾーンでは、繰り返しの **ローカル時刻が季節で動く**（例「毎朝9:00」が
  夏冬で 8:00/10:00 になる）。これは instant 固定の当然の帰結として受け入れる。
  主ロケール JST は DST がないため、この差は生じない（UTC 固定＝壁時計が完全一致）。

### 2-2. 完全な iCalendar 化はしない

本アプリには **ビジネスカレンダーによる営業日シフト**（`AdjustmentRule`：祝日に当たったら前／翌
営業日へ寄せる）がある。これは RFC 5545 の `RRULE` では表現できない（カスタム祝日カレンダー依存・
非可逆）。よって `.ics`／RRULE 文字列への完全準拠は採用せず、繰り返しルールは独自の構造体
（`WeeklyRule` / `MonthlyRule` / `YearlyRule` ＋ `AdjustmentRule`）で保持する。

### 2-3. 表示は閲覧者ロケール

予定は instant が真値で、表示時刻は **見る人のロケール（タイムゾーン）** で変わる。表示は
保持 UTC を閲覧者ロケールへ変換して導出する。

---

## 3. 保持モデル

| 種別 | 保持値 | 型 |
|------|--------|----|
| 単発 | `StartUtc` ＋ `DurationMinutes` | `DateTimeOffset`(UTC) ＋ `int`(分) |
| 繰り返し | `AnchorUtc` ＋ `RecurrenceRule` ＋ `DurationMinutes` | `DateTimeOffset`(UTC) ＋ 構造体 ＋ `int`(分) |
| 共通 | `TimeZoneId` | IANA タイムゾーン名（メタデータ） |

- `End` は持たない。必要時に `開始instant + DurationMinutes` で導出する。
- `AnchorUtc` は先頭（基準）オカレンスの絶対時刻。各オカレンスは §4 の式で導出する。
- `DurationMinutes > 0` 必須。24h（終日相当）は `1440`。

ドメインオブジェクトとしての完全な構造は §10 を参照。

---

## 4. 時刻セマンティクス

### 4-1. 単発

```
instant = StartUtc                       // 保持値そのもの（再解決しない）
end     = StartUtc + DurationMinutes
表示     = instant を閲覧者ロケールへ変換
```

### 4-2. 繰り返し（instant 固定の展開）

ルールは **ローカル日付パターン**（毎週月曜・毎月15日・毎年4/20・第n曜日…）を TZID 上で評価し、
各オカレンスの instant を **アンカーからの整数日加算**でピン留めする：

```
anchorLocalDate = AnchorUtc を TZID でローカル化した日付
occLocalDate    = ルール（＋営業日シフト）を TZID で評価して得た候補日
occurrenceUtc   = AnchorUtc + (occLocalDate - anchorLocalDate).Days * 24h
end             = occurrenceUtc + DurationMinutes
```

- tz データベースに依存しない整数日加算なので、制度変更があっても各オカレンスの instant は不変。
- ローカル時刻は季節で動きうる（§2-1 のトレードオフ）。JST は DST なしのため動かない。
- 日付パターン（曜日・日）は TZID 上で評価するので意図どおり（1h 程度のズレで曜日は跨がない）。

### 4-3. `TimeZoneId` の用途

1. 繰り返しルールと **営業日シフト** の「どのローカル日か」の評価。
2. アンカー／開始の **ローカル日付** の算出（§4-2）。
3. **表示**：保持 UTC → 閲覧者ロケール変換。

### 4-4. DurationMinutes

- `end = 開始instant + DurationMinutes`（UTC 上の加算。DST 内包の曖昧さは発生しない）。
- `DurationMinutes > 0` 必須。24h（終日相当）は `1440`。

---

## 5. タイムゾーンの所属と編集スコープ（TZ ownership）

### 5-1. 所属タイムゾーン

- イベントの `TimeZoneId` は **最終更新者のタイムゾーン**。作成時、および「すべて」スコープの
  編集時に編集者の TZ へ刻む。
- 保持は UTC（§3）なので、`TimeZoneId` の差し替えは **純粋なメタデータ再ラベル**であり、
  `StartUtc` / `AnchorUtc` は触らない → 絶対時刻（瞬間）は本質的に不変。
- 編集フォームは保持 UTC を編集者 TZ に変換して表示し、保存時に `TimeZoneId = 編集者TZ`。時刻を
  変更しなければ `StartUtc` はそのまま＝瞬間不変。時刻を変えたぶんだけ UTC が動く。
- 繰り返しでは TZID を変えると、繰り返しの **ローカル日付評価の基準ゾーン**（§4-2）も変わるため、
  「すべて」編集で系列が編集者 TZ の暦に乗り換わる（アンカー instant は保たれる）。

### 5-2. 編集スコープ × TZ

| スコープ | 対象 | TZ の扱い |
|---|---|---|
| すべて | 系列全体を再定義 | 系列 `TimeZoneId` = 編集者 TZ |
| 以降 | 当該回以降を新系列へ分割 | 新系列の `TimeZoneId` = 編集者 TZ。元（以前）系列は時刻も TZ も不変 |
| この回だけ | 当該回をスキップし、新しい単体予定を作成 | 新単体予定の `TimeZoneId` = 系列 TZ（系列は不変） |

### 5-3. 「この回だけ」（SplitThisOccurrence）の動作

- 当該回を系列からスキップ（`SkipOccurrence` → `EventException.Skip`）し、同じ内容で新しい単体予定
  （`SingleEventSchedule`）を作成する。
- 単体予定は系列と独立しており、以後は通常の単体予定として扱われる。
- 後方互換：既存データに `Override` 例外 / `EventMove` が含まれる場合、`OccurrenceExpander` は引き続き
  これらを正しく読み込む（§10-3）。

### 5-4. 業務日シフト

- 系列の `AdjustmentRule`（祝日→前 / 翌営業日）は **系列 TZ のローカル日**で判定する。系列 TZ が
  変われば判定基準日も変わる。
- 「この回だけ」例外は展開後の単発カスタムであり、シフト判定には関与しない。

### 5-5. 後方互換

- 例外 / 移動の `TimeZoneId` は null 許容＝既存データは系列 TZ 解釈のまま。スキーマ破壊なし。
- イベント本体の時刻モデル移行は §9 の CLI で対応する。

---

## 6. 終日（All-day）の扱い

- **ドメインに `AllDay` フラグは持たない。** 終日は「`StartTime = 00:00` かつ
  `DurationMinutes = 1440`」という通常の時間付き予定に過ぎない。
- カレンダー表示に終日専用レーンは設けない。終日相当も時間グリッド上のブロックとして描く。
- 編集画面の **終日トグル（ON/OFF）** は入力糖衣であり、ON で時刻入力を隠して
  `StartTime = 00:00, DurationMinutes = 1440` を埋めるだけ。
  - **当日 24h 限定**。複数日終日は指定不可。
  - 再オープン時のトグル ON 判定は `StartTime == 00:00 && DurationMinutes == 1440` から派生する。

---

## 7. `24:00` と日マタギの描画

- **`24:00` は表示専用** の表記。値としては `00:00`（翌日）で扱い、`LocalTimeValue` は 0–23 のまま。
  ブロックの終端が閲覧者ローカルの翌 0:00 で、その日のセル下端を閉じるときだけ `24:00` と表記する。
- **日マタギ・24h 超のブロックは閲覧者ローカルの 24:00 境界で分割し、続きを翌日側に描画** する。
  - 跨ぎ判定は閲覧者ローカル基準。同じ予定でも閲覧タイムゾーンにより跨ぐ／跨がないが変わり得る。
    分割は純粋にプレゼンテーション層の処理（ドメインは instant + duration のみを持つ）。
- 編集画面の終了ピッカーには末尾スロット `24:00` を用意し、選択時は内部的に
  「開始 + その日の終端までの分」を `DurationMinutes` として保存する。

---

## 8. JSON スキーマ

> 開始は **UTC instant**、長さは `durationMinutes`。終了 (`end` / `endTime`) と `allDay` は保持しない。

単発:

```jsonc
{
  "id": "…",
  "title": "…",
  "timezone": "Asia/Tokyo",        // TZID（表示・業務日シフト用メタ）
  "singleSchedule": {
    "startUtc": "2026-06-15T01:00:00Z",  // 絶対時刻（UTC）
    "durationMinutes": 60                 // > 0
  },
  "visibility": "Public",
  "version": 1
}
```

繰り返し:

```jsonc
{
  "id": "…", "title": "…", "timezone": "Asia/Tokyo",
  "recurringSchedule": {
    "anchorUtc": "2026-01-05T01:00:00Z",  // 先頭オカレンスの絶対時刻（UTC）
    "durationMinutes": 30,
    "rule": { /* §10-2 の RecurrenceRule 構造 */ }
  },
  "version": 1
}
```

- 終日相当: `durationMinutes = 1440` かつアンカーが TZID ローカルの 00:00 に当たる UTC instant。

---

## 9. 旧データの移行（互換なし・一回限りの CLI）

- **アプリ本体は新スキーマのみを読む**（旧フィールドの読み替えロジックは持たない）。
- 旧 → 新の変換は管理 CLI のサブコマンド `migrate-schema` で一括実行する（バックエンド間コピーの
  `migrate` とは別系統）。`NolumiaScheduler.Infrastructure` の `LegacySchemaMigrator` が旧レコードを
  ドメインに復元し、現行リポジトリ経由で再保存するため、新 JSON 形と SQLite の列が同じコードで
  生成される。

  ```bash
  # 変換対象を確認（書き込みなし）
  dotnet run --project NolumiaScheduler.Cli -- migrate-schema all --dry-run
  # JSON / SQLite 両方をその場で変換（既定は all）
  dotnet run --project NolumiaScheduler.Cli -- migrate-schema
  ```

  - 冪等：新形式（`durationMinutes` を持つ）は「already current」としてスキップ。
  - 営業日カレンダー・設定は時刻モデル非依存のため対象外。
- 変換規則:

  | 旧 | 新（UTC 形） |
  |----|----|
  | 単発 `start` / `end`（DateTimeOffset） | `startUtc` = `start` を UTC 化、`durationMinutes` = `end − start` |
  | 繰り返し `startDate` / `startTime` / `endTime` | `anchorUtc` = `startDate+startTime` を TZID で UTC 化、`durationMinutes` = `endTime − startTime` |
  | `allDay: true` | `durationMinutes = 1440`、アンカーは当日 00:00(ローカル) の UTC |

- 移行は破壊的変更を伴うため、実行前のバックアップを前提とする（`docs/storage.md` のバックアップ手順に準ずる）。

---

## 10. ドメインデータモデル

繰り返し・例外・移動を含む、予定の完全な保持構造。値オブジェクトは不変（immutable）で、
イベント本体（集約ルート）だけが状態遷移を持つ。

### 10-1. 集約ルート `CalendarEvent`

| フィールド | 型 | 説明 |
|---|---|---|
| `Id` | `EventId` | 同一性 |
| `Kind` | `EventKind` = `Single` \| `Recurring` | どちらのスケジュールを持つか |
| `Title` | `EventTitle` | 空不可 |
| `Location` | `Location?` | 任意 |
| `Visibility` | `Visibility` = `Public` \| `Private` | |
| `EventType` | `EventType?` | 任意分類 |
| `Description` | `Description?` | 任意メモ |
| `TimeZoneId` | `TimeZoneId` | IANA 名。表示・評価用メタ（§4-3） |
| `SingleSchedule` | `SingleEventSchedule?` | `Kind == Single` のとき非 null |
| `RecurringSchedule` | `RecurringEventSchedule?` | `Kind == Recurring` のとき非 null |
| `Exceptions` | `IReadOnlyList<EventException>` | スキップ／上書き（§10-3） |
| `Moves` | `IReadOnlyList<EventMove>` | ドラッグ移動された回（§10-3） |
| `Alarm` | `EventAlarm?` | 通知設定。基準は **開始時刻**（Duration 変更の影響を受けない） |
| `ColorKey` | `EventColorKey` | 表示色。`Default` は色指定なし |
| `Version` | `VersionNo` | 楽観ロック用。更新ごとに `Next()` |
| `CreatedAt` / `UpdatedAt` | `DateTimeOffset` | 監査用。更新ごとに `Touch()` |

- 不変条件：アンカーのローカル日（TZID で解決）は `RecurrenceRule.EndDate` 以前でなければならない。
- スケジュール（`SingleEventSchedule` / `RecurringEventSchedule`）の定義は §3 を参照。

### 10-2. `RecurrenceRule` と繰り返しルール

`RecurrenceRule` は繰り返しの本体で、種別に応じて対応する下位ルールを 1 つだけ保持する。

| フィールド | 型 | 説明 |
|---|---|---|
| `RuleType` | `RecurrenceType` = `Weekly` \| `Monthly` \| `Yearly` | |
| `Interval` | `int` (≥ 1) | 何期間ごと。既定 1 |
| `EndDate` | `LocalDateValue` | 終了日。`9999-12-31` は無期限 |
| `Weekly` / `Monthly` / `Yearly` | 各下位ルール？ | `RuleType` に対応するものが非 null |
| `Adjustment` | `AdjustmentRule?` | 営業日シフト（§10-4） |

**Weekly** — `WeeklyRule`

| フィールド | 型 | 説明 |
|---|---|---|
| `Weekdays` | `IReadOnlyList<Weekday>` (1 個以上) | 対象曜日 |

**Monthly** — `MonthlyRule`（抽象。以下のいずれか）

| 具象型 | フィールド | 説明 |
|---|---|---|
| `DayOfMonthMonthlyRule` | `Day` (1–31) | 毎月○日 |
| `NthWeekdayMonthlyRule` | `WeekIndex` (1–5, または `-1`=最終), `Weekday` | **第 n 曜日**（例：第3水曜日 = `WeekIndex 3, Wednesday`） |
| `LastDayOfMonthMonthlyRule` | （なし） | 毎月末日 |

**Yearly** — `YearlyRule`（抽象。以下のいずれか）

| 具象型 | フィールド | 説明 |
|---|---|---|
| `DayOfMonthYearlyRule` | `Month` (1–12), `Day` (1–31) | 毎年○月○日 |
| `NthWeekdayYearlyRule` | `Month` (1–12), `WeekIndex` (1–5, または `-1`=最終), `Weekday` | 毎年○月の第 n 曜日 |

`Weekday` は `Sunday = 0 … Saturday = 6`。`WeekIndex` の `-1` はその月の最終週を表す。

> 第 n 曜日ルールは編集画面の「繰り返し → 毎月/毎年 → 第○曜日」で入力できる。週インデックス
> （第1〜第5・最終）と曜日を選ぶと、`NthWeekdayMonthlyRule` / `NthWeekdayYearlyRule` が組み立てられる。

### 10-3. 例外と移動

系列の個々の回に対する差分。オカレンスは `OccurrenceLocalKey`（`Date` ＋ 系列開始 time-of-day
`Time`）で識別する。開始 time-of-day を変える編集では、既存の例外／移動キーを付け替える
（re-key）ことで参照を保つ。

| 型 | 内容 |
|---|---|
| `EventException`（`Type = Skip`） | その回を展開から除外する |
| `EventException`（`Type = Override`） | その回の内容を上書き（後方互換のみ。新規作成はしない） |
| `EventMove` | ドラッグで別日時に移動した回。移動先の日付・開始・長さ・任意の上書きを持つ |

### 10-4. `AdjustmentRule`（営業日シフト）

| フィールド | 型 | 説明 |
|---|---|---|
| `Condition` | `AdjustmentCondition` = `Holiday` \| `Always` | `Holiday`=候補日が休日のときだけ寄せる／`Always`=常に寄せる |
| `ShiftUnit` | `AdjustmentShiftUnit` = `BusinessDay` \| `CalendarDay` | シフトの単位 |
| `ShiftAmount` | `int`（符号付き） | 負=前倒し（Backward）／正=後ろ倒し（Forward） |
| `CalendarId` | `BusinessCalendarId?` | 参照する営業日カレンダー |

- 判定基準日は系列 TZ のローカル日（§5-4）。
- `Condition = Always` は「15日の3営業日前」のように、休日か否かに関わらず不変に寄せる用途。

---

## 11. バリデーション規則

- 共通: `durationMinutes > 0`。`timezone` は解決可能な IANA 名。開始は UTC instant。
- 単発・繰り返しとも日マタギ可（`開始instant + Duration` が翌日以降に及んでよい）。
- 終日: ドメインに概念なし。入力上は当日 00:00(ローカル)起点 ＋ `durationMinutes = 1440` の通常予定。
- 繰り返し: `RuleType` に対応する下位ルールが必須。`Interval ≥ 1`。アンカーのローカル日は
  `EndDate` 以前。
- 第 n 曜日: `WeekIndex` は `1–5` または `-1`（最終）。`0` は不可。
- アラーム: 従来どおり **開始時刻基準**（終了・Duration の変更の影響を受けない）。
