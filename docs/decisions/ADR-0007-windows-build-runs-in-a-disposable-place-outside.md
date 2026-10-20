# ADR-0007: Windows のビルドは外の使い捨ての置き場で行い、更新はサーバーの中継から取る

- 状態: 承認
- 日付: 2026-10-20
- 関連: task #551、ADR-0001（forge の CI は Core だけ）、WinTrack の配布（WinTrack ADR・Tracker ADR-0044）

## 文脈

画面（WinUI）は forge の CI で組めない（XAML の組み立てが Windows でしか動かない。ADR-0001）。そのため配布が無く、
第 2 段の変更は画面を一度も組んでいなかった。公開の写し `kiyoteruhosoda/NolumiaScheduler` があり、GitHub の Windows 環境は
公開の置き場なら無料で使える。

持ち主（2026-10-20）「アクションを git 管理しないでいいよ。なくなっていい前提」。

## 決定

1. **組む手順はこのリポジトリに置かない。** GitHub の別の公開の置き場 `kiyoteruhosoda/nolumiascheduler-build` が
   15 分ごとに写しの main の先頭を見て、新しければ Windows で画面ごと組み、Velopack で Setup.exe と更新の一式を作って
   その置き場の Release に置く。失敗は ntfy に知らせる。
   - forge の main → 写し（push mirror）→ 次の定期の確認、で組まれる。forge 側には何も足さない
   - 家の置き場（Garage）は外から書けないので、GitHub は書く鍵を持たない。家の側（forge `kyon/nolumiascheduler-build`）が
     Release を取りに来て `nolumiascheduler` の入れ物へ上げ、share.nolumia.com から入れられるようにする
   - どちらも使い捨て。消えても配布が止まるだけで、このリポジトリと動いているアプリには響かない（作り直せばよい）
2. **入れるのと更新は Velopack。** `Program.Main` の最初で `VelopackApp.Build().Run()` を呼ぶ。
   - 版は `1.0.<commit の数>`（WinTrack と同じ）。設定の画面の「バージョン」に出る
   - ⚠ **packId は `NolumiaSchedulerWin`**。Velopack は `%LocalAppData%\<packId>` に入れ、アンインストールで丸ごと消す。
     データは `%LocalAppData%\NolumiaScheduler`（docs/storage.md）なので、同じ名前にすると予定と設定が消える
3. **更新はサーバーの中継から取る（WinTrack と同じ形）。** Velopack は `https://<サーバー>/api/windows/releases/` を
   assay のトークン付きで読む（`SchedulerApiClient.ReleasesUri`）。サーバーは配布の入れ物を読む鍵だけを持って中継する。
   - 6 時間ごとと起動時に確かめ、新しい版は裏で落として**アプリを終えたときに入れ替える**（常駐して何日も動くため）。
     設定の画面に「いま更新して再起動」も出す
   - ログインしていない・サーバーにつながらない・Velopack で入れていない（開発中）ときは黙って確かめない

## 影響

- このリポジトリの CI（forge）は今まで通り Core だけ。画面が組めるかは、マージの後に外の置き場で分かる（PR に合否は返らない）
- 外の置き場が消えたら、同じ手順で作り直す（手順はその置き場の `build.yml` と課題 #551 のコメント）
