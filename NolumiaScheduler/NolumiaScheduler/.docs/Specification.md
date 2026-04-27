# カレンダースケジューラ設計

---

# 1. 概要

本システムは、ローカル環境で動作するカレンダースケジューラであり、
JSONファイルベースでイベントを管理する。

主な特徴:

* 単発イベントと繰り返しイベントをサポート
* 繰り返しルール（週・月・年）対応
* 例外（スキップ・上書き）対応
* 移動（振替）対応
* 祝日・営業日カレンダーによる日付補正
* タイムゾーン考慮

---

# 2. 設計方針

## 2.1 基本方針

* データは JSON ファイルとして永続化
* DDD（ドメイン駆動設計）ベースで設計
* ドメインモデルと永続化構造（JSON）は分離
* 繰り返しは「ルール + 例外」で表現（展開保存しない）

---

## 2.2 イベント分類

| 種別        | 説明       |
| --------- | -------- |
| Single    | 単発イベント   |
| Recurring | 繰り返しイベント |

---

## 2.3 タイムゾーン方針

* 全イベントは `timezone` を持つ
* 繰り返しは **ローカル時間基準（wall clock）**
* 祝日判定・営業日判定も timezone 基準

---

## 2.4 日またぎルール

| 種別         | 日またぎ     |
| ---------- | -------- |
| 単発         | 許可       |
| 繰り返し（時間指定） | 禁止       |
| 繰り返し（終日）   | 1日終日のみ許可 |

---

# 3. データモデル概要

## 3.1 Aggregate

### CalendarEvent（Aggregate Root）

* イベント本体
* 例外・移動を内包

### BusinessCalendar（Aggregate Root）

* 祝日・営業日定義

---

## 3.2 Entity

* EventException
* EventMove

---

## 3.3 Value Object

* EventId
* EventTitle
* Location
* Visibility
* TimeZoneId
* LocalDateValue
* LocalTimeValue
* OccurrenceLocalKey
* SingleEventSchedule
* RecurringEventSchedule
* RecurrenceRule
* WeeklyRule / MonthlyRule / YearlyRule
* AdjustmentRule
* Holiday

---

## 3.4 Domain Service

* OccurrenceExpander（繰り返し展開）
* BusinessDayShiftService（営業日補正）

---

## 3.5 Repository

* ICalendarEventRepository
* IBusinessCalendarRepository

---

# 4. JSON構造概要

## 4.1 単発イベント

```json
{
  "kind": "Single",
  "start": "ISO8601",
  "end": "ISO8601"
}
```

---

## 4.2 繰り返しイベント

```json
{
  "kind": "Recurring",
  "startDate": "YYYY-MM-DD",
  "startTime": "HH:mm:ss",
  "endTime": "HH:mm:ss",
  "recurrence": { ... },
  "exceptions": [],
  "moves": []
}
```

---

## 4.3 例外

* SKIP: その回を削除
* OVERRIDE: 内容変更

---

## 4.4 移動

* 元の回を削除し別日に再配置

---

# 5. ドメインロジックの中核

## 5.1 繰り返しの考え方

繰り返しは以下で表現する:

```
発生日 = ルールで生成
→ 祝日補正
→ 例外適用
→ 移動適用
```

---

## 5.2 処理順序

1. 繰り返し候補日生成
2. 祝日・営業日補正
3. 時刻付与
4. exceptions 適用
5. moves 適用
6. 表示対象抽出

---

# 6. CalendarEvent の責務

## 6.1 責務

* 自身の整合性を保証
* 例外・移動を管理
* スケジュール変更を管理

---

## 6.2 非責務

* 繰り返し展開
* ファイル保存
* 複数イベント操作（シリーズ分割）

---

# 7. CalendarEvent メソッド一覧

## 7.1 基本変更

* ChangeDetails()
* RescheduleSingle()
* ChangeRecurringSchedule()
* ChangeRecurrenceEndDate()

---

## 7.2 Occurrence 操作

* SkipOccurrence()
* OverrideOccurrence()
* MoveOccurrence()

---

## 7.3 削除

* RemoveOccurrenceException()
* RemoveOccurrenceMove()

---

## 7.4 問い合わせ

* IsSingle()
* IsRecurring()
* HasExceptionFor()
* HasMoveFor()

---

# 8. Application Service の責務

Application Service は以下を担当する:

* イベント作成
* イベント更新
* この回以降変更（シリーズ分割）
* Repository操作
* トランザクション管理

---

## 8.1 主なユースケース

* CreateSingleEvent
* CreateRecurringEvent
* SkipOccurrence
* OverrideOccurrence
* MoveOccurrence
* ChangeFollowingOccurrences（重要）

---

# 9. シリーズ分割（重要）

「この回以降変更」は以下で実現:

1. 既存イベントの endDate を変更
2. 新しいイベントを作成
3. parentSeriesId を設定

---

# 10. バリデーション方針

## 10.1 JSON Schema

* 構造チェック

## 10.2 アプリ側

* 業務ルールチェック

例:

* start < end
* startTime < endTime
* occurrence 重複禁止
* exceptions / moves の競合禁止

---

# 11. ファイル構成

```text
data/
  events/
  calendars/
```

---

# 12. 実装優先順位

1. JSON Schema 検証
2. Domain モデル実装
3. Repository（JSON）
4. Application Service
5. OccurrenceExpander
6. UI連携

---

# 13. 設計上の重要判断

## 13.1 繰り返しは展開保存しない

→ ルールベース

## 13.2 日またぎ制限

→ 繰り返し簡素化のため

## 13.3 timezone 必須

→ 祝日・曜日・DST対応

## 13.4 例外と移動を分離

→ 意味の違いを保持

---

# 14. 今後の拡張余地

* 複数日終日の繰り返し
* カレンダー共有
* 招待・参加者
* 通知機能
* 検索最適化（インデックス）

---

# 15. 注意事項

* JSON と Domain を混同しないこと
* ValueObject を必ず通すこと
* 例外処理を甘くしないこと（特に occurrence）

---

# 16. 最重要ポイント

このシステムの核心は以下です:

> 「繰り返しはデータではなくルールで持つ」

そして、

> 「例外と移動で差分を表現する」

この2点を崩すと設計が破綻します。

