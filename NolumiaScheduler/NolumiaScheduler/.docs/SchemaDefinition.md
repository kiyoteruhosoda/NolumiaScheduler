
# カレンダースケジューラ スキーマ定義書

## 1. 目的

本仕様は、ローカル環境で利用するカレンダースケジューラのデータ保存形式を定義する。
保存媒体は JSON ファイルとし、共有・同時編集は考慮しない。

本仕様では以下を扱う。

* 単発イベント
* 繰り返しイベント
* 繰り返しイベントの例外
* 繰り返しイベントの移動
* 祝日・営業日カレンダー

---

# 2. 基本方針

## 2.1 イベント種別

イベントは以下の 2 種類に分かれる。

* `Single` : 単発イベント
* `Recurring` : 繰り返しイベント

## 2.2 タイムゾーン

すべてのイベントは `timezone` を持つ。
特に繰り返しイベントは、**繰り返し判定・祝日判定・ずらし処理を timezone 基準で行う**。

## 2.3 終日予定

終日予定は許可する。

* 単発イベント: 複数日終日も許可
* 繰り返しイベント: **1 日終日のみ許可**

## 2.4 日またぎ

* 単発イベント: 許可
* 繰り返しイベント: 時間指定の日またぎは不可

## 2.5 公開種別

公開種別は固定列挙とし、以下のみを許可する。

* `Public`
* `Private`

## 2.6 繰り返し終了条件

繰り返し終了条件は `endDate` のみとする。
`count` は持たない。

## 2.7 移動と単発追加

* 「移動」は、元の繰り返し回を別日に振り替えたものとして扱う
* 「スキップ + 単発追加」は別概念として扱う

---

# 3. ファイル配置

```text
data/
  events/
    evt_0001.json
    evt_0002.json
  calendars/
    jp_default.json
```

* 1 イベント 1 ファイル
* 単発イベントと繰り返しイベントは同じ `events/` に格納する
* イベント種別は JSON 内の `kind` で識別する

---

# 4. 共通項目定義

## 4.1 Event 共通スキーマ

| 項目名           | 型             | 必須 | 説明                             |
| ------------- | ------------- | -: | ------------------------------ |
| `id`          | string        | 必須 | イベントID                         |
| `kind`        | string        | 必須 | `Single` / `Recurring`         |
| `title`       | string        | 必須 | イベント名                          |
| `location`    | string | null | 任意 | ロケーション                         |
| `visibility`  | string        | 必須 | `Public` / `Private`           |
| `eventType`   | string | null | 任意 | 業務上の種別                         |
| `description` | string | null | 任意 | 説明                             |
| `timezone`    | string        | 必須 | IANA Time Zone。例: `Asia/Tokyo` |
| `allDay`      | boolean       | 必須 | 終日予定フラグ                        |
| `createdAt`   | string        | 必須 | 作成日時 ISO 8601                  |
| `updatedAt`   | string        | 必須 | 更新日時 ISO 8601                  |
| `version`     | integer       | 必須 | スキーマ／レコード更新用の版数                |

---

# 5. 単発イベントスキーマ

## 5.1 概要

単発イベントは、1 回限りの予定を表す。
日またぎを許可する。

## 5.2 JSON 構造

```json
{
  "id": "evt_0001",
  "kind": "Single",
  "title": "深夜メンテナンス",
  "location": "本番環境",
  "visibility": "Private",
  "eventType": "maintenance",
  "description": "深夜作業",
  "timezone": "Asia/Tokyo",
  "allDay": false,
  "start": "2026-04-20T23:00:00+09:00",
  "end": "2026-04-21T01:00:00+09:00",
  "createdAt": "2026-04-20T10:00:00+09:00",
  "updatedAt": "2026-04-20T10:00:00+09:00",
  "version": 1
}
```

## 5.3 項目定義

| 項目名     | 型      | 必須 | 説明            |
| ------- | ------ | -: | ------------- |
| `start` | string | 必須 | 開始日時 ISO 8601 |
| `end`   | string | 必須 | 終了日時 ISO 8601 |

## 5.4 制約

### 通常予定

* `start < end`

### 終日予定

* `allDay = true`
* `start` は対象開始日の `00:00:00`
* `end` は終了日の翌日 `00:00:00`
* 複数日終日可

---

# 6. 繰り返しイベントスキーマ

## 6.1 概要

繰り返しイベントは、ローカル日付・ローカル時刻・繰り返しルールから生成されるイベント系列を表す。
繰り返し判定は `timezone` 基準で行う。

## 6.2 JSON 構造

```json
{
  "id": "evt_1001",
  "kind": "Recurring",
  "title": "定例会議",
  "location": "会議室A",
  "visibility": "Public",
  "eventType": "meeting",
  "description": null,
  "timezone": "Asia/Tokyo",
  "allDay": false,
  "startDate": "2026-04-20",
  "startTime": "10:00:00",
  "endTime": "11:00:00",
  "recurrence": {
    "ruleType": "WEEKLY",
    "interval": 1,
    "endDate": "2026-12-31",
    "weekly": {
      "weekdays": ["MO"]
    },
    "monthly": null,
    "yearly": null,
    "adjustment": null
  },
  "exceptions": [],
  "moves": [],
  "createdAt": "2026-04-20T09:00:00+09:00",
  "updatedAt": "2026-04-20T09:00:00+09:00",
  "version": 1
}
```

---

## 6.3 項目定義

| 項目名          | 型             |     必須 | 説明                 |
| ------------ | ------------- | -----: | ------------------ |
| `startDate`  | string        |     必須 | 初回基準日。`YYYY-MM-DD` |
| `startTime`  | string | null | 条件付き必須 | 開始時刻。`HH:mm:ss`    |
| `endTime`    | string | null | 条件付き必須 | 終了時刻。`HH:mm:ss`    |
| `recurrence` | object        |     必須 | 繰り返しルール            |
| `exceptions` | array         |     必須 | 例外一覧               |
| `moves`      | array         |     必須 | 移動一覧               |

### 条件付き必須

* `allDay = false` の場合
  `startTime`, `endTime` は必須
* `allDay = true` の場合
  `startTime`, `endTime` は `null` 可

---

## 6.4 制約

### 時間指定の繰り返し

* `allDay = false`
* `startTime < endTime`
* 日またぎ不可

### 終日の繰り返し

* `allDay = true`
* 1 日終日のみ許可
* 複数日終日の繰り返しは不可

---

# 7. 繰り返しルールスキーマ

## 7.1 Recurrence 共通構造

```json
{
  "ruleType": "DAILY",
  "interval": 1,
  "endDate": "2026-12-31",
  "weekly": null,
  "monthly": null,
  "yearly": null,
  "adjustment": null
}
```

## 7.2 項目定義

| 項目名          | 型             |   必須 | 説明                                        |
| ------------ | ------------- | ---: | ----------------------------------------- |
| `ruleType`   | string        |   必須 | `DAILY` / `WEEKLY` / `MONTHLY` / `YEARLY` |
| `interval`   | integer       |   必須 | 間隔。1 以上                                   |
| `endDate`    | string        |   必須 | 繰り返し終了日。`YYYY-MM-DD`                      |
| `weekly`     | object | null | 条件付き | 週単位設定                                     |
| `monthly`    | object | null | 条件付き | 月単位設定                                     |
| `yearly`     | object | null | 条件付き | 年単位設定                                     |
| `adjustment` | object | null |   任意 | 祝日ずらし設定                                   |

## 7.3 `ruleType` ごとの条件

| `ruleType` | 必須項目         |
| ---------- | ------------ |
| `DAILY`    | 追加項目なし       |
| `WEEKLY`   | `weekly` 必須  |
| `MONTHLY`  | `monthly` 必須 |
| `YEARLY`   | `yearly` 必須  |

---

# 8. 週単位設定

## 8.1 WeeklyRule

```json
{
  "weekdays": ["MO", "WE"]
}
```

## 8.2 項目定義

| 項目名        | 型        | 必須 | 説明   |
| ---------- | -------- | -: | ---- |
| `weekdays` | string[] | 必須 | 曜日一覧 |

## 8.3 曜日列挙

以下のみを許可する。

* `MO`
* `TU`
* `WE`
* `TH`
* `FR`
* `SA`
* `SU`

---

# 9. 月単位設定

## 9.1 日付固定型

```json
{
  "mode": "DAY_OF_MONTH",
  "day": 15
}
```

## 9.2 第n週曜日型

```json
{
  "mode": "NTH_WEEKDAY",
  "weekIndex": 2,
  "weekday": "MO"
}
```

## 9.3 最終週型

```json
{
  "mode": "NTH_WEEKDAY",
  "weekIndex": -1,
  "weekday": "FR"
}
```

## 9.4 項目定義

| 項目名         | 型              |   必須 | 説明                             |
| ----------- | -------------- | ---: | ------------------------------ |
| `mode`      | string         |   必須 | `DAY_OF_MONTH` / `NTH_WEEKDAY` |
| `day`       | integer | null | 条件付き | 日付固定型の日                        |
| `weekIndex` | integer | null | 条件付き | 第何週。`1` ～ `5`、最終週は `-1`        |
| `weekday`   | string | null  | 条件付き | 曜日                             |

---

# 10. 年単位設定

## 10.1 日付固定型

```json
{
  "mode": "DAY_OF_MONTH",
  "month": 4,
  "day": 20
}
```

## 10.2 第n週曜日型

```json
{
  "mode": "NTH_WEEKDAY",
  "month": 4,
  "weekIndex": 2,
  "weekday": "MO"
}
```

## 10.3 項目定義

| 項目名         | 型              |   必須 | 説明                             |
| ----------- | -------------- | ---: | ------------------------------ |
| `mode`      | string         |   必須 | `DAY_OF_MONTH` / `NTH_WEEKDAY` |
| `month`     | integer        |   必須 | 月。`1` ～ `12`                   |
| `day`       | integer | null | 条件付き | 日付固定型の日                        |
| `weekIndex` | integer | null | 条件付き | 第何週。`1` ～ `5`、最終週は `-1`        |
| `weekday`   | string | null  | 条件付き | 曜日                             |

---

# 11. 祝日ずらし設定

## 11.1 AdjustmentRule

```json
{
  "condition": "HOLIDAY",
  "shiftUnit": "BUSINESS_DAY",
  "shiftAmount": -1,
  "calendarId": "jp_default"
}
```

## 11.2 項目定義

| 項目名           | 型       | 必須 | 説明                     |
| ------------- | ------- | -: | ---------------------- |
| `condition`   | string  | 必須 | 現時点では `HOLIDAY` 固定     |
| `shiftUnit`   | string  | 必須 | `DAY` / `BUSINESS_DAY` |
| `shiftAmount` | integer | 必須 | ずらす量。負数可               |
| `calendarId`  | string  | 必須 | 参照する営業日カレンダーID         |

## 11.3 意味

* `DAY`: 単純な日数移動
* `BUSINESS_DAY`: 営業日単位移動

## 11.4 判定基準

祝日判定および移動処理は、**繰り返しイベントの timezone におけるローカル日付** を基準とする。

---

# 12. 例外スキーマ

## 12.1 概要

例外は、繰り返しイベントの特定の 1 回について、通常ルールから外れる内容を定義する。

* 1 回だけスキップ
* 1 回だけ内容変更

## 12.2 JSON 例

### スキップ

```json
{
  "occurrenceLocal": "2026-05-11T10:00:00",
  "action": "SKIP"
}
```

### 上書き

```json
{
  "occurrenceLocal": "2026-06-08T10:00:00",
  "action": "OVERRIDE",
  "override": {
    "title": "定例会議（短縮）",
    "location": "会議室B",
    "visibility": "Private",
    "startTime": "10:30:00",
    "endTime": "11:00:00"
  }
}
```

## 12.3 項目定義

| 項目名               | 型             |   必須 | 説明                                 |
| ----------------- | ------------- | ---: | ---------------------------------- |
| `occurrenceLocal` | string        |   必須 | 元の回のローカル開始日時。`YYYY-MM-DDTHH:mm:ss` |
| `action`          | string        |   必須 | `SKIP` / `OVERRIDE`                |
| `override`        | object | null | 条件付き | 上書き内容                              |

## 12.4 `override` 項目

| 項目名          | 型             | 必須 | 説明        |
| ------------ | ------------- | -: | --------- |
| `title`      | string | null | 任意 | 上書きタイトル   |
| `location`   | string | null | 任意 | 上書きロケーション |
| `visibility` | string | null | 任意 | 上書き公開種別   |
| `startTime`  | string | null | 任意 | 上書き開始時刻   |
| `endTime`    | string | null | 任意 | 上書き終了時刻   |

## 12.5 制約

* `action = SKIP` の場合、`override` は不要
* `action = OVERRIDE` の場合、`override` 必須

---

# 13. 移動スキーマ

## 13.1 概要

移動は、繰り返しイベントの特定の 1 回を別日に振り替えることを表す。
「スキップ + 単発追加」とは別概念である。

## 13.2 JSON 例

```json
{
  "occurrenceLocal": "2026-07-13T10:00:00",
  "newDate": "2026-07-14",
  "newStartTime": "14:00:00",
  "newEndTime": "15:00:00",
  "title": "定例会議（振替）",
  "location": "会議室C",
  "visibility": "Public"
}
```

## 13.3 項目定義

| 項目名               | 型             |   必須 | 説明           |
| ----------------- | ------------- | ---: | ------------ |
| `occurrenceLocal` | string        |   必須 | 元の回のローカル開始日時 |
| `newDate`         | string        |   必須 | 振替先日付        |
| `newStartTime`    | string | null | 条件付き | 振替先開始時刻      |
| `newEndTime`      | string | null | 条件付き | 振替先終了時刻      |
| `title`           | string | null |   任意 | 移動先タイトル      |
| `location`        | string | null |   任意 | 移動先ロケーション    |
| `visibility`      | string | null |   任意 | 移動先公開種別      |

## 13.4 制約

* 元の回は通常表示しない
* 移動先を表示する
* `allDay = false` の場合
  `newStartTime`, `newEndTime` は必須
* `allDay = true` の場合
  `newStartTime`, `newEndTime` は `null` 可

---

# 14. 営業日カレンダースキーマ

## 14.1 JSON 例

```json
{
  "id": "jp_default",
  "name": "Japan Default Business Calendar",
  "timezone": "Asia/Tokyo",
  "workdaysOfWeek": ["MO", "TU", "WE", "TH", "FR"],
  "holidays": [
    {
      "date": "2026-01-01",
      "name": "元日"
    },
    {
      "date": "2026-01-12",
      "name": "成人の日"
    }
  ]
}
```

## 14.2 項目定義

| 項目名              | 型        | 必須 | 説明            |
| ---------------- | -------- | -: | ------------- |
| `id`             | string   | 必須 | カレンダーID       |
| `name`           | string   | 必須 | カレンダー名        |
| `timezone`       | string   | 必須 | カレンダー基準タイムゾーン |
| `workdaysOfWeek` | string[] | 必須 | 営業曜日          |
| `holidays`       | array    | 必須 | 祝日一覧          |

## 14.3 Holiday 項目

| 項目名    | 型      | 必須 | 説明              |
| ------ | ------ | -: | --------------- |
| `date` | string | 必須 | 祝日。`YYYY-MM-DD` |
| `name` | string | 任意 | 祝日名             |

---

# 15. バリデーション仕様

## 15.1 共通

* `id` は一意
* `timezone` は有効な IANA Time Zone であること
* `visibility` は `Public` または `Private`
* `version >= 1`

## 15.2 単発イベント

* `start < end`
* `allDay = false` の場合、通常の日時範囲
* `allDay = true` の場合、開始・終了は日境界

## 15.3 繰り返しイベント

* `startDate <= recurrence.endDate`
* `interval >= 1`
* `allDay = false` の場合、`startTime < endTime`
* 時間指定繰り返しの日またぎは禁止
* `allDay = true` の場合、1 日終日のみ

## 15.4 例外

* 同一 `occurrenceLocal` に対して複数の `exceptions` を持たない
* `action = OVERRIDE` の場合 `override` 必須

## 15.5 移動

* 同一 `occurrenceLocal` に対して複数の `moves` を持たない
* 同一 `occurrenceLocal` が `exceptions` と `moves` の両方に存在しないこと

---

# 16. 処理順序仕様

繰り返しイベントの実体生成時は以下の順序で処理する。

1. `timezone` 基準で繰り返し候補日を生成
2. `adjustment` を適用して日付を補正
3. 開始時刻・終了時刻を組み立て
4. `exceptions` を適用
5. `moves` を適用
6. 表示対象期間と重なるものを抽出

---

# 17. この回だけ変更 / 以降変更

## 17.1 この回だけ変更

* `exceptions` に `OVERRIDE` を追加する

## 17.2 この回だけスキップ

* `exceptions` に `SKIP` を追加する

## 17.3 この回だけ移動

* `moves` に追加する

## 17.4 この回以降変更

* 既存イベントの `recurrence.endDate` を変更前最終回までに切る
* 新しい繰り返しイベントを新規作成する

---

# 18. JSON サンプル

## 18.1 単発イベント

```json
{
  "id": "evt_0001",
  "kind": "Single",
  "title": "深夜対応",
  "location": "DC",
  "visibility": "Private",
  "eventType": "maintenance",
  "description": null,
  "timezone": "Asia/Tokyo",
  "allDay": false,
  "start": "2026-04-20T23:00:00+09:00",
  "end": "2026-04-21T01:00:00+09:00",
  "createdAt": "2026-04-20T09:00:00+09:00",
  "updatedAt": "2026-04-20T09:00:00+09:00",
  "version": 1
}
```

## 18.2 繰り返しイベント

```json
{
  "id": "evt_1001",
  "kind": "Recurring",
  "title": "月例会議",
  "location": "会議室A",
  "visibility": "Public",
  "eventType": "meeting",
  "description": null,
  "timezone": "Asia/Tokyo",
  "allDay": false,
  "startDate": "2026-04-01",
  "startTime": "10:00:00",
  "endTime": "11:00:00",
  "recurrence": {
    "ruleType": "MONTHLY",
    "interval": 1,
    "endDate": "2026-12-31",
    "weekly": null,
    "monthly": {
      "mode": "NTH_WEEKDAY",
      "weekIndex": 2,
      "weekday": "MO"
    },
    "yearly": null,
    "adjustment": {
      "condition": "HOLIDAY",
      "shiftUnit": "BUSINESS_DAY",
      "shiftAmount": -1,
      "calendarId": "jp_default"
    }
  },
  "exceptions": [
    {
      "occurrenceLocal": "2026-06-08T10:00:00",
      "action": "SKIP"
    }
  ],
  "moves": [
    {
      "occurrenceLocal": "2026-07-13T10:00:00",
      "newDate": "2026-07-14",
      "newStartTime": "14:00:00",
      "newEndTime": "15:00:00",
      "title": "月例会議（振替）",
      "location": "会議室B",
      "visibility": "Public"
    }
  ],
  "createdAt": "2026-04-20T09:00:00+09:00",
  "updatedAt": "2026-04-20T09:00:00+09:00",
  "version": 1
}
```

