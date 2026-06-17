

# 2. アプリ側バリデーション仕様

JSON Schema で構造を検証した後、アプリ側で業務ルールを検証します。

---

## 2-1. 共通バリデーション

### 必須

* `id` は空文字不可
* `title` は空文字不可
* `timezone` はアプリがサポートする IANA Time Zone として解決可能であること
* `visibility` は `Public` または `Private`
* `version >= 1`

### 文字列正規化

* `title`, `location`, `eventType`, `description` は前後空白を trim して保存
* trim 後に空文字になった任意項目は `null` に統一してもよい

---

> 注意: 時刻モデルは `docs/time-model.md`（確定設計）が真実の源です。本章のうち
> 終日・終了時刻・日マタギに関する旧記述はそれに置き換えられています。予定は
> `DTSTART(ローカル壁時計) + TZID + DURATION(分)` で保持し、終了は `開始 + duration`
> で導出します。`allDay` はドメイン概念としては廃止され、`startTime = 00:00 かつ
> durationMinutes = 1440` の通常予定として表現されます。

## 2-2. 単発イベントのバリデーション

### 基本

* `kind == "Single"`
* `durationMinutes > 0`（終了は `開始 + duration` で導出。終了時刻は保持しない）

### 時刻

* `startTime` は `00:00:00`–`23:59:59`
* 日またぎを許可（`開始 + duration` が翌日以降に及んでよい）
* 同一日内でなくてもよい
* 終日相当は `startTime = 00:00:00 かつ durationMinutes = 1440`

---

## 2-3. 繰り返しイベントのバリデーション

### 基本

* `kind == "Recurring"`
* `startDate <= recurrence.endDate`
* `recurrence.interval >= 1`

### 時刻指定

* `startTime != null`（`00:00:00`–`23:59:59`）
* `durationMinutes > 0`
* 日またぎを許可（`endTime <= startTime` の旧禁止は撤廃。各オカレンスは登録 TZID の
  壁時計で `開始 + duration` を解決し、翌日に及んでよい）
* 終日相当の繰り返しは `startTime = 00:00:00 かつ durationMinutes = 1440`（当日24h）

### recurrence.ruleType ごとの検証

#### DAILY

* `weekly == null`
* `monthly == null`
* `yearly == null`

#### WEEKLY

* `weekly != null`
* `weekly.weekdays` は1件以上
* `monthly == null`
* `yearly == null`

#### MONTHLY

* `weekly == null`
* `monthly != null`
* `yearly == null`

#### YEARLY

* `weekly == null`
* `monthly == null`
* `yearly != null`

---

## 2-4. 月次・年次ルールの追加検証

### monthly.mode == DAY_OF_MONTH

* `day` が存在する
* `weekIndex`, `weekday` は存在しないか無視

### monthly.mode == NTH_WEEKDAY

* `weekIndex` が存在する
* `weekday` が存在する
* `day` は存在しないか無視

### yearly.mode == DAY_OF_MONTH

* `month` が 1..12
* `day` が 1..31

### yearly.mode == NTH_WEEKDAY

* `month` が 1..12
* `weekIndex` が `-1, 1, 2, 3, 4, 5`
* `weekday` が有効値

---

## 2-5. 祝日ずらし設定のバリデーション

### adjustment がある場合

* `calendarId` が実在するカレンダーファイルを参照していること
* カレンダーの `timezone` とイベントの `timezone` は原則一致を推奨

### shiftAmount

* 0 も仕様上は許可できるが、実用上は意味が薄い
  必要なら禁止してもよい

### BUSINESS_DAY

* `calendarId` 参照必須
* `workdaysOfWeek` が1件以上必要

---

## 2-6. exceptions のバリデーション

### 共通

* `occurrenceLocal` は、そのイベントの `timezone` 基準ローカル開始日時を表す
* `occurrenceLocal` は重複禁止

### SKIP

* 同じ `occurrenceLocal` に対する他の exception 不可
* 同じ `occurrenceLocal` に対する move 不可

### OVERRIDE

* `override` 必須
* `override` の全項目 null は禁止にしてよい
  つまり「何も変えない上書き」は不可推奨

### 時刻の override

* 時刻を変更するなら `startTime` と `durationMinutes` は両方指定必須（片方だけは不可）
* `durationMinutes > 0`
* 時刻を変えない（タイトル・場所・公開種別だけの）override は両方 null
* 日またぎ可（`開始 + duration` が翌日に及んでよい）

---

## 2-7. moves のバリデーション

### 共通

* `occurrenceLocal` は重複禁止
* 同じ `occurrenceLocal` を exceptions と共有してはいけない

### move の時刻

* `newStartTime` と `newDurationMinutes` は両方指定または両方 null
* `newDurationMinutes > 0`（指定時）
* 日またぎ可
* `newDate` のみ変更対象

### move の意味

* 元の `occurrenceLocal` は表示しない
* 移動先を表示する

---

## 2-8. カレンダーのバリデーション

### 基本

* `id` は一意
* `timezone` は有効な IANA Time Zone
* `workdaysOfWeek` は重複禁止
* `holidays[].date` は重複禁止

### 推奨

* `holidays` は日付昇順に保存
* `workdaysOfWeek` も `MO..SU` 順に正規化して保存

---

# 3. 保存時の正規化仕様

バリデーションとは別に、保存時に正規化を入れると扱いやすくなります。

---

## 3-1. イベント共通

* `updatedAt` は保存時に現在時刻へ更新
* 新規作成時のみ `createdAt` を設定
* 任意項目の空文字は `null`
* 配列は空なら `[]` を明示

---

## 3-2. recurrence

* 使わない枝は必ず `null`

  * 例: `ruleType = WEEKLY` なら `monthly = null`, `yearly = null`

---

## 3-3. exceptions / moves

* `occurrenceLocal` 昇順で保存
* UI から同一キーの追加が来た場合は上書きかエラーかを統一

  * おすすめはエラー

---

## 3-4. holidays

* `date` 昇順で保存

---

# 4. エラーコード案

実装しやすいように、エラーコードを切っておくとよいです。

---

## 4-1. 共通

* `E001`: id is required
* `E002`: title is required
* `E003`: invalid timezone
* `E004`: invalid visibility
* `E005`: invalid version

## 4-2. 単発

* `E101`: durationMinutes must be greater than zero

## 4-3. 繰り返し

* `E201`: startDate must be on or before recurrence.endDate
* `E202`: recurring event durationMinutes must be greater than zero
* `E206`: invalid recurrence branch for ruleType

## 4-4. exceptions

* `E301`: duplicate occurrenceLocal in exceptions
* `E302`: override payload is empty
* `E303`: override startTime and durationMinutes must be specified together
* `E304`: override durationMinutes must be greater than zero

## 4-5. moves

* `E401`: duplicate occurrenceLocal in moves
* `E402`: occurrenceLocal conflicts between exceptions and moves
* `E403`: move startTime and durationMinutes must be specified together
* `E404`: move durationMinutes must be greater than zero

## 4-6. calendars

* `E501`: duplicate holiday date
* `E502`: referenced calendarId not found

---

# 5. 実装順

1. JSON Schema 検証
2. アプリ側業務バリデーション
3. 保存時正規化
4. 月表示向け展開ロジック
5. 編集操作
   * この回だけ変更
   * この回だけ移動
   * この回以降変更
