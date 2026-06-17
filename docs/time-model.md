# 時刻モデル（確定設計）

予定の開始・終了・繰り返しを **どう保持し・どう解釈し・どう表示するか** の確定仕様です。
本書は予定の時刻表現に関して、`NolumiaSchedulerTest/Inputs/ValidationDifinication.md` の
旧記述（終日概念・繰り返しの日マタギ禁止・終了時刻保持）を **上書き** します。

> 位置づけ: ドメインの時刻モデルの単一の真実の源（source of truth）。実装・テスト・
> バリデーションはすべて本書に従います。

---

## 1. 結論（1枚サマリ）

- 単発・繰り返しを **一律で `DTSTART(ローカル壁時計) + TZID + DURATION` のデータモデル** で保持する
  （RFC 5545 の DTSTART+TZID の **考え方** を採用。シリアライズ形式そのものは独自 JSON）。
- **終了は保持しない**。常に `開始 + DURATION` で導出する。
- **`AllDay` というドメイン概念は廃止**。終日は編集画面の入力トグル（当日 24h 限定）に過ぎない。
- **`24:00` は表示専用**。値としては保持しない（`LocalTimeValue` は 0–23 のまま）。
- 旧データとの **互換は持たない**。変換は **一回限りの管理 CLI** で行う。

---

## 2. なぜこの形か（前提と制約）

### 2-1. 一律化（単発も繰り返しも同じ表現）

単発＝UTC instant、繰り返し＝壁時計、のように種別で表現を変えると分岐が増える。
両者を **`DTSTART + TZID + DURATION`** に揃えると、単発は「**RRULE 相当を持たない予定**」、
繰り返しは「**繰り返しルールを持つ予定**」というだけの違いになり、展開・表示・検証が一本化する。

### 2-2. なぜ完全な iCalendar 化はしないのか

本アプリには **ビジネスカレンダーによる営業日シフト**（`Adjustment` = Forward / Backward +
`CalendarId`：祝日に当たったら前／翌営業日へ寄せる）がある。これは RFC 5545 の `RRULE` では
表現できない（カスタム祝日カレンダー依存のシフトは仕様外で、非可逆）。
したがって **RRULE 文字列や `.ics` への完全準拠は採用しない**。採用するのは
DTSTART+TZID の **データモデル（ローカル壁時計 + 名前付きタイムゾーン）だけ** で、
繰り返しルールは既存の構造体（`WeeklyRule` / `MonthlyRule` / `YearlyRule` ＋ `Adjustment`）の
ままとする。

### 2-3. 表示は閲覧者ロケール

予定は「絶対時刻（instant）」として意味を持ち、表示時刻は **見る人のロケール（タイムゾーン）**
で変わる。「9:00 開始・1h は常に 10:00 終わり」ではない。instant は `DTSTART` を `TZID` で
解決して **毎回導出** する。

---

## 3. 保持モデル（単発・繰り返し共通）

| 役割 | 保持値 | 型 |
|------|--------|----|
| `DTSTART`（開始の壁時計） | `StartDate` + `StartTime` | `LocalDateValue` + `LocalTimeValue` |
| `TZID`（タイムゾーン） | `TimeZoneId` | IANA タイムゾーン名（イベントに既存） |
| `DURATION`（長さ） | `DurationMinutes` | `int`（分） |
| 繰り返しルール | `Rule`（+ `Adjustment`） | 既存構造体。**単発は持たない** |

- 既存の繰り返しは元々 `StartDate + StartTime + TimeZoneId` を持つため、実質 DTSTART+TZID。
  単発を `DateTimeOffset Start/End` から **同じ `StartDate + StartTime + DurationMinutes`** に
  寄せることで一律化する。
- `End` は持たない。必要時に `開始 + DurationMinutes` で導出する。

---

## 4. 時刻セマンティクス

### 4-1. instant の導出（単発・繰り返し共通）

```
localStart = StartDate + StartTime            // 壁時計（オフセットなし）
instant    = TZID で localStart を解決          // 絶対時刻（DST も名前付きゾーンで正しく解決）
end        = instant + DurationMinutes
表示        = instant を「閲覧者ロケール」へ変換
```

### 4-2. 繰り返しの曜日・日付・オカレンス時刻

- 繰り返しの「曜日 / 日付」とオカレンス時刻は **登録時 TZID の壁時計** で評価する。
  例: 「毎週月曜 6:00（登録 = JST）」は各月曜の 6:00 JST を組み立ててから instant 化する。
  UTC 基準で評価すると深夜・早朝のオカレンスが別日へずれるため、必ず TZID の壁時計で鎖める。
- DST のあるゾーンでも壁時計が一貫する（名前付きゾーンで各オカレンスを解決するため）。

### 4-3. `TimeZoneId` の用途

1. オカレンスの **壁時計 → instant 解決**（4-1 / 4-2）。
2. **ビジネスカレンダーの営業日シフト判定**（どのローカル日に当たるか）。

### 4-4. DURATION の加算規約

- 繰り返しは **壁時計加算**（ローカル日時に加算してから TZID 解決）を既定とする。
  主ロケール JST は DST がないため実害はない。
- `DurationMinutes > 0` 必須（0 長・負値は不正）。24h（終日相当）は `1440`。

---

## 5. 終日（All-day）の扱い

- **ドメインから `AllDay` フラグを廃止する。** 終日は「`StartTime = 00:00` かつ
  `DurationMinutes = 1440`」という通常の時間付き予定に過ぎない。
- カレンダー表示の **終日専用レーンは廃止**。終日相当も時間グリッド上のブロックとして描く。
- 編集画面に **終日トグル（ON/OFF）** を置く。これは入力糖衣であり、
  ON で時刻入力を隠して `StartTime = 00:00, DurationMinutes = 1440` を埋めるだけ。
  - **当日 24h 限定**。複数日終日は指定不可。
  - 再オープン時のトグル ON 判定は `StartTime == 00:00 && DurationMinutes == 1440` から派生する。

---

## 6. `24:00` と日マタギの描画

- **`24:00` は表示専用** の表記。値としては `00:00`（翌日）で扱い、`LocalTimeValue` は 0–23 のまま。
  ブロックの終端が **閲覧者ローカルの翌 0:00** で、その日のセル下端を閉じるときだけ `24:00` と表記する。
- **日マタギ・24h 超のブロックは閲覧者ローカルの 24:00 境界で分割し、続きを翌日側に描画**する。
  - 跨ぎ判定は **閲覧者ローカル基準**。同じ予定でも閲覧タイムゾーンにより跨ぐ／跨がないが変わり得る。
    分割は純粋にプレゼンテーション層の処理（ドメインは instant + duration のみを持つ）。
- 編集画面の終了ピッカーには末尾スロット `24:00` を用意し、選択時は内部的に
  「開始 + その日の終端までの分」を `DurationMinutes` として保存する。

---

## 7. JSON スキーマ（新）

> 旧フィールド `end` / `endTime` / `allDay` は廃止。`durationMinutes` を導入。
> 繰り返しルール（`rule` 等）の構造は従来どおり。

予定（共通フィールド・抜粋）:

```jsonc
{
  "id": "…",
  "title": "…",
  "timezone": "Asia/Tokyo",      // TZID
  "startDate": "2026-06-15",      // DTSTART の日付（ローカル）
  "startTime": "06:00:00",        // DTSTART の時刻（ローカル壁時計）。0:00–23:59
  "durationMinutes": 60,          // DURATION。> 0
  "visibility": "Public",
  "version": 1
  // 繰り返しのみ: "rule": { … }（既存構造） / "adjustment": { … }
  // 単発は rule を持たない
}
```

- `startTime` は常に `0:00:00`–`23:59:59`（`24:00` は保存しない）。
- 終日: `startTime = "00:00:00"`, `durationMinutes = 1440`。

---

## 8. 旧データの移行（互換なし・一回限りの CLI）

- **アプリ本体は新スキーマのみを読む**（旧フィールドの読み替えロジックは持たない）。
- 旧 → 新の変換は **管理 CLI（`NolumiaScheduler.Cli`）の専用サブコマンド** で一括実行する。
  （既存の `migrate`（バックエンド間コピー）とは別系統の、スキーマ変換コマンドとして追加する。）
- 変換規則:

  | 旧 | 新 |
  |----|----|
  | 単発 `start` / `end`（DateTimeOffset） | `startDate`/`startTime` = `start` を TZID ローカル化、`durationMinutes` = `end − start` |
  | 繰り返し `startTime` / `endTime` | `durationMinutes` = `endTime − startTime`（同日前提） |
  | `allDay: true` | `startTime = 00:00`, `durationMinutes = 1440`（複数日終日は best-effort で `N × 1440`） |

- 移行は破壊的変更を伴うため、実行前のバックアップを前提とする（`docs/storage.md` のバックアップ手順に準ずる）。

---

## 9. 影響を受ける主な実装箇所

| 箇所 | 変更内容 |
|------|---------|
| `SingleEventSchedule` | `Start`/`End`(DateTimeOffset) → `StartDate + StartTime + DurationMinutes` |
| `RecurringEventSchedule` | `EndTime`/`AllDay` 廃止、`DurationMinutes` 導入、検証は `Duration>0`・日マタギ許容 |
| `EventOccurrence` / `OccurrenceExpander` | 終端を Duration から導出、壁時計→instant 解決、TZID 一元化、`AllDay` 廃止 |
| `EventExpirationService.GetOccurrenceEnd` | `開始 + Duration` ベースへ（現状の同日固定を解消） |
| `CalendarViewModel`（週分割） / `CalendarEventItem` | 跨ぎ判定を閲覧者ローカル + 24:00 境界へ、`IsAllDay` 廃止 |
| 終日レーン一式（`*WeekAllDay*`） | 撤去 |
| `EventEditViewModel` / `EventEditPage` | 終日トグル = start/duration 糖衣、終了ピッカーに `24:00`、`hour < 24` パーサ拡張 |
| `JsonCalendarEventRepository`（および SQLite payload DTO） | 新スキーマ専用化（旧読み替えなし） |
| `NolumiaScheduler.Cli` | 旧 → 新スキーマ変換サブコマンドを追加 |
| `ValidationDifinication.md` ／各テスト | 本書に合わせて改訂（終日・日マタギ禁止・終了時刻の記述を更新） |

---

## 10. バリデーション規則（更新後）

- 共通: `durationMinutes > 0`。`startTime ∈ [00:00:00, 23:59:59]`。`timezone` は解決可能な IANA 名。
- 単発: 日マタギ可（`開始 + Duration` が翌日以降に及んでよい）。
- 繰り返し（時間付き）: 日マタギ可。旧「`endTime <= startTime` は不可」は撤廃。
- 終日: ドメインに概念なし。入力上は `startTime = 00:00 && durationMinutes = 1440` の通常予定として表現。
- アラーム: 従来どおり **開始時刻基準**（終了・Duration の変更の影響を受けない）。

---

## 11. 決定の経緯（要約）

1. 「1 日の終り 23:59／日マタギ制限」の違和感 → 終端を排他境界（`24:00` 相当）で考えたい。
2. 終端は **利用時間（Duration）** で保持し、`開始 + Duration` で導出（終了日時は保持しない）。
3. 「9:00 + 1h が常に 10:00」ではない＝表示は閲覧者ロケール依存。時刻は instant として扱う。
4. 単発・繰り返しを **DTSTART + TZID + DURATION で一律化**（一律のほうがシンプル）。
5. 繰り返しの曜日・時刻は **登録 TZID の壁時計** で鎖める（UTC 評価は日ずれ・DST ドリフトを招く）。
6. ビジネスカレンダーのシフトがあるため **完全 iCalendar 化は不可**。データモデルのみ採用。
7. 旧データ互換は不要。**一回限りの CLI** で変換する。
