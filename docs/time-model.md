# 時刻モデル（確定設計）

予定の開始・終了・繰り返しを **どう保持し・どう解釈し・どう表示するか** の確定仕様です。
本書は予定の時刻表現に関して、`NolumiaSchedulerTest/Inputs/ValidationDifinication.md` の
旧記述（終日概念・繰り返しの日マタギ禁止・終了時刻保持）を **上書き** します。

> 位置づけ: ドメインの時刻モデルの単一の真実の源（source of truth）。実装・テスト・
> バリデーションはすべて本書に従います。

---

## 1. 結論（1枚サマリ）

- 予定は **UTC の絶対時刻（instant）で保持**し、`TimeZoneId` は表示・繰り返し評価・業務日シフト用の
  **メタデータ**として持つ。
  - 単発: `StartUtc`（UTC instant）＋ `DurationMinutes`。
  - 繰り返し: `AnchorUtc`（先頭オカレンスの UTC instant）＋ ルール ＋ `DurationMinutes`。
- **終了は保持しない**。常に `開始instant + DURATION` で導出する。
- **`AllDay` というドメイン概念は廃止**。終日は編集画面の入力トグル（当日 24h 限定）に過ぎない。
- **`24:00` は表示専用**。値としては保持しない。
- **instant 優先**：制度（DST／政治的ルール）が変わって**ローカル時刻がずれるのは当たり前**。
  ずらしたくなければ**再保存**する（＝制度変更の負担はその制度内の人が負い、外の人の表示は動かない）。
- 旧データとの **互換は持たない**。変換は **一回限りの管理 CLI** で行う。

---

## 2. なぜこの形か（前提と制約）

### 2-1. なぜ UTC 保持か（壁時計保持からの転換）

予定の「絶対時刻」を真値とし、UTC で固定する。狙いは **制度変更の影響を、その制度の中の人だけに
留め、外の人を巻き込まない**こと。

- UTC 固定なら：絶対時刻が不変 → 外（例：日本）の人の表示は動かない。制度が変わった側（例：NY）の
  ローカル表示だけが動く（＝当たり前）。ずらしたくなければ再保存。
- 壁時計＋TZID で都度解決すると：tz データベース更新（制度変更）で**未来のオカレンスの instant が
  再計算され**、外の人の表示まで動く。これを避ける。
- トレードオフ：DST のあるゾーンでは、繰り返しの**ローカル時刻が季節で動く**（例「毎朝9:00」が
  夏冬で 8:00/10:00）。これは instant 固定の当然の帰結として受け入れる。
  **主ロケール JST は DST がないため、この差は生じない**（UTC 固定＝壁時計が完全一致）。

### 2-2. なぜ完全な iCalendar 化はしないのか

本アプリには **ビジネスカレンダーによる営業日シフト**（`Adjustment`：祝日に当たったら前／翌営業日へ
寄せる）がある。これは RFC 5545 の `RRULE` では表現できない（カスタム祝日カレンダー依存・非可逆）。
よって `.ics`／RRULE 文字列への完全準拠は採用せず、繰り返しルールは既存の構造体
（`WeeklyRule` / `MonthlyRule` / `YearlyRule` ＋ `Adjustment`）のまま持つ。

### 2-3. 表示は閲覧者ロケール

予定は instant が真値で、表示時刻は **見る人のロケール（タイムゾーン）** で変わる。表示は
保持 UTC を閲覧者ロケールへ変換して導出する。

---

## 3. 保持モデル

| 種別 | 保持値 | 型 |
|------|--------|----|
| 単発 | `StartUtc` ＋ `DurationMinutes` | `DateTimeOffset`(UTC) ＋ `int`(分) |
| 繰り返し | `AnchorUtc` ＋ `Rule`(+`Adjustment`) ＋ `DurationMinutes` | `DateTimeOffset`(UTC) ＋ 既存構造体 ＋ `int` |
| 共通 | `TimeZoneId` | IANA タイムゾーン名（メタデータ） |

- `End` は持たない。必要時に `開始instant + DurationMinutes` で導出する。
- `AnchorUtc` は先頭（基準）オカレンスの絶対時刻。各オカレンスは §4 の式で導出する。

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

- tz データベースに依存しない整数日加算なので、**制度変更があっても各オカレンスの instant は不変**。
- ローカル時刻は季節で動きうる（§2-1 のトレードオフ）。JST は DST なしのため動かない。
- 日付パターン（曜日・日）は TZID 上で評価するので意図どおり（1h 程度のズレで曜日は跨がない）。

### 4-3. `TimeZoneId` の用途

1. 繰り返しルールと**営業日シフト**の「どのローカル日か」の評価。
2. アンカー／開始の**ローカル日付**の算出（§4-2）。
3. **表示**：保持 UTC → 閲覧者ロケール変換。

### 4-4. DURATION

- `end = 開始instant + DurationMinutes`（UTC 上の加算。DST 内包の曖昧さは発生しない）。
- `DurationMinutes > 0` 必須。24h（終日相当）は `1440`。

---

## 5. タイムゾーンの所属と編集スコープ（TZ ownership）

> 本節はデータ仕様。クロスTZ編集の往復（編集フォームを編集者TZで表示し、保存時に
> 系列TZへ刻む UI 配線）は実装上の残課題で、ここでは挙動の規定のみ。

### 5-1. 所属タイムゾーン

- イベントの `TimeZoneId` は **最終更新者のタイムゾーン**。作成時、および「すべて」スコープの
  編集時に編集者の TZ へ刻む。
- 保持は UTC（§3）なので、`TimeZoneId` の差し替えは **純粋なメタデータ再ラベル**であり、
  `StartUtc` / `AnchorUtc` は触らない → **絶対時刻（瞬間）は本質的に不変**（＝「データが UTC だから
  変わらない」が文字どおり成立）。
- 編集フォームは保持 UTC を編集者 TZ に変換して表示し、保存時に `TimeZoneId = 編集者TZ`。**時刻を
  変更しなければ `StartUtc` はそのまま**＝瞬間不変。時刻を変えたぶんだけ UTC が動く。
- 注（繰り返し）：TZID を変えると、繰り返しの**ローカル日付評価の基準ゾーン**（§4-2）も変わるため、
  「すべて」編集で系列が編集者 TZ の暦に乗り換わる（アンカー instant は保たれる）。

### 5-2. 編集スコープ × TZ

| スコープ | 対象 | TZ の扱い |
|---|---|---|
| すべて | 系列全体を再定義 | 系列 `TimeZoneId` = 編集者 TZ（(A) 変換） |
| 以降 | 当該回以降を新系列へ分割 | **新系列**の `TimeZoneId` = 編集者 TZ。**元（以前）系列は時刻も TZ も不変** |
| この回だけ | 当該回をスキップし、新しい単体予定を作成 | 新単体予定の `TimeZoneId` = 系列 TZ（系列は不変） |

### 5-3. 「この回だけ」（SplitThisOccurrence）の動作

- **操作の流れ**：当該回を系列からスキップ（`SkipOccurrence`）し、同じ内容で新しい単体予定（`SingleEventSchedule`）を作成する。
- 単体予定は系列と独立しており、以後は通常の単体予定として扱われる。
- 後方互換：既存データに `ExceptionOverride` / `EventMove` が含まれる場合、`OccurrenceExpander` は引き続きこれらを正しく読み込む。新規の override/move は作成できない。

### 5-4. 業務日シフト

- 系列の `Adjustment`（祝日→前 / 翌営業日）は **系列 TZ のローカル日**で判定する。系列 TZ が
  変われば判定基準日も変わる（＝「TZ に準ずる」）。
- 「この回だけ」例外は展開後の単発カスタムであり、シフト判定には関与しない。

### 5-5. 後方互換

- 例外 / 移動の `TimeZoneId` は null 許容＝既存データは系列 TZ 解釈のまま。スキーマ破壊なし。
- イベント本体の時刻モデル移行は §9（旧→新）の CLI で対応済み。

---

## 6. 終日（All-day）の扱い

- **ドメインから `AllDay` フラグを廃止する。** 終日は「`StartTime = 00:00` かつ
  `DurationMinutes = 1440`」という通常の時間付き予定に過ぎない。
- カレンダー表示の **終日専用レーンは廃止**。終日相当も時間グリッド上のブロックとして描く。
- 編集画面に **終日トグル（ON/OFF）** を置く。これは入力糖衣であり、
  ON で時刻入力を隠して `StartTime = 00:00, DurationMinutes = 1440` を埋めるだけ。
  - **当日 24h 限定**。複数日終日は指定不可。
  - 再オープン時のトグル ON 判定は `StartTime == 00:00 && DurationMinutes == 1440` から派生する。

---

## 7. `24:00` と日マタギの描画

- **`24:00` は表示専用** の表記。値としては `00:00`（翌日）で扱い、`LocalTimeValue` は 0–23 のまま。
  ブロックの終端が **閲覧者ローカルの翌 0:00** で、その日のセル下端を閉じるときだけ `24:00` と表記する。
- **日マタギ・24h 超のブロックは閲覧者ローカルの 24:00 境界で分割し、続きを翌日側に描画**する。
  - 跨ぎ判定は **閲覧者ローカル基準**。同じ予定でも閲覧タイムゾーンにより跨ぐ／跨がないが変わり得る。
    分割は純粋にプレゼンテーション層の処理（ドメインは instant + duration のみを持つ）。
- 編集画面の終了ピッカーには末尾スロット `24:00` を用意し、選択時は内部的に
  「開始 + その日の終端までの分」を `DurationMinutes` として保存する。

---

## 8. JSON スキーマ（新）

> 旧フィールド `end` / `endTime` / `allDay` は廃止。開始は **UTC instant**、長さは `durationMinutes`。
> 繰り返しルール（`rule` 等）の構造は従来どおり。

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
    "rule": { /* 既存構造 */ }            // adjustment も従来どおり
  },
  "version": 1
}
```

- 終日相当: `durationMinutes = 1440` かつアンカーが TZID ローカルの 00:00 に当たる UTC instant。

> 実装状況: 検証可能なコア（Domain/Application/Infrastructure/Cli ＋ CoreTests）は**この UTC 形で
> 実装済み**。`migrate-schema` も UTC 形を出力する（旧 start/end・終日形と移行段階の
> DTSTART ローカル形の両方を変換）。Presentation / WinUI / Windows E2E は同 API へ機械的に
> 追従済みだが Windows ビルドでの検証が必要。

---

## 9. 旧データの移行（互換なし・一回限りの CLI）

- **アプリ本体は新スキーマのみを読む**（旧フィールドの読み替えロジックは持たない）。
- 旧 → 新の変換は **管理 CLI のサブコマンド `migrate-schema`** で一括実行する（実装済み。
  既存の `migrate`〔バックエンド間コピー〕とは別系統。`NolumiaScheduler.Infrastructure`
  の `LegacySchemaMigrator` が旧レコードをドメインに復元し、現行リポジトリ経由で再保存
  するため、新JSON形と SQLite の span 列が同じコードで生成される）。

  ```bash
  # 変換対象を確認（書き込みなし）
  dotnet run --project NolumiaScheduler.Cli -- migrate-schema all --dry-run
  # JSON / SQLite 両方をその場で変換（既定は all）
  dotnet run --project NolumiaScheduler.Cli -- migrate-schema
  ```

  - 冪等：新形式（`durationMinutes` を持つ）は「already current」としてスキップ。
  - 営業日カレンダー・設定は時刻モデル非依存のため対象外。
- 変換規則:

  | 旧 | 新（最終形 = UTC） |
  |----|----|
  | 単発 `start` / `end`（DateTimeOffset） | `startUtc` = `start` を UTC 化、`durationMinutes` = `end − start` |
  | 繰り返し `startDate` / `startTime` / `endTime` | `anchorUtc` = `startDate+startTime` を TZID で UTC 化、`durationMinutes` = `endTime − startTime` |
  | `allDay: true` | `durationMinutes = 1440`、アンカーは当日 00:00(ローカル) の UTC |

  > 現行 CLI（`migrate-schema`）は移行段階の DTSTART(ローカル) 形へ変換する。UTC 形への
  > 再実装時に、この表の最終形へ更新する（同 CLI を拡張）。

- 移行は破壊的変更を伴うため、実行前のバックアップを前提とする（`docs/storage.md` のバックアップ手順に準ずる）。

---

## 10. 影響を受ける主な実装箇所

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

### 10-1. UTC 保持への再実装（コア実装済み）

検証可能なコアは UTC 保持形（§3）で実装済み。要素ごとの対応：

| 箇所 | 変更内容 |
|------|---------|
| `SingleEventSchedule` | `StartDate+StartTime` → `StartUtc`(UTC instant) ＋ `DurationMinutes` |
| `RecurringEventSchedule` | `StartDate+StartTime` → `AnchorUtc`(UTC instant)。ルールは据え置き |
| `OccurrenceExpander` | §4-2 の `AnchorUtc + (occLocalDate − anchorLocalDate)×24h` で instant 生成 |
| `EventExpirationService` | 開始 instant を保持 UTC から直接利用 |
| 例外 / 移動 | その回の開始を **UTC instant ＋ 任意表示 TZID** へ（§5-3） |
| JSON / SQLite DTO | `startUtc` / `anchorUtc` フィールドへ |
| `migrate-schema` | §9 の UTC 形へ変換するよう拡張 |
| 表示（Presentation） | 保持 UTC → 閲覧者ロケール変換（§2-3）。DST 帯の繰り返しはローカル時刻が季節で動く |

---

## 11. バリデーション規則（更新後）

- 共通: `durationMinutes > 0`。`timezone` は解決可能な IANA 名。開始は UTC instant。
- 単発・繰り返しとも日マタギ可（`開始instant + Duration` が翌日以降に及んでよい）。
- 終日: ドメインに概念なし。入力上は当日 00:00(ローカル)起点 ＋ `durationMinutes = 1440` の通常予定。
- アラーム: 従来どおり **開始時刻基準**（終了・Duration の変更の影響を受けない）。

---

## 12. 決定の経緯（要約）

1. 「1 日の終り 23:59／日マタギ制限」の違和感 → 終端を排他境界（`24:00` 相当）で考えたい。
2. 終端は **利用時間（Duration）** で保持し、`開始 + Duration` で導出（終了日時は保持しない）。
3. 「9:00 + 1h が常に 10:00」ではない＝表示は閲覧者ロケール依存。時刻は instant として扱う。
4. 単発・繰り返しを **DTSTART + TZID + DURATION で一律化**（一律のほうがシンプル）。
5. 繰り返しの曜日・時刻は **登録 TZID の壁時計** で鎖める（UTC 評価は日ずれ・DST ドリフトを招く）。
6. ビジネスカレンダーのシフトがあるため **完全 iCalendar 化は不可**。データモデルのみ採用。
7. 旧データ互換は不要。**一回限りの CLI** で変換する。
8. イベントの所属 TZ は **最終更新者の TZ**。刻みは **(A) 絶対時刻を保存する変換**（時刻を変えなければ瞬間不変）。
9. 編集スコープに応じて TZ が及ぶ範囲が決まる：すべて＝系列全体、以降＝新系列のみ（元系列不変）、この回だけ＝その例外のみ。
10. 「この回だけ」は **例外（override/move）が自身の TZ を持つ**（null は系列 TZ）。一度刻んだら **ピン留め**（後の全体編集でも不変）。
11. **【方針転換】保持を壁時計から UTC へ**：制度変更でローカル時刻がずれるのは当たり前で、ずらしたくなければ再保存する前提。UTC 固定なら**制度変更の負担をその制度内に留め、外の人の表示を動かさない**。→ 4・5 を上書き（保持は UTC＋TZID メタ、繰り返しは §4-2 の整数日加算で instant をピン留め）。8 の「変換で瞬間保存」は、UTC 保持では **メタ TZID 再ラベルで本質的に不変** に簡素化。
12. その代償として **DST 帯の繰り返しはローカル時刻が季節で動く**（JST は DST なしのため無影響）＝受容。
