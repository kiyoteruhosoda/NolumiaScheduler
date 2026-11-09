# ADR-0009: 更新は RustEdit と同じく blobshare から、更新用の別のログインで取る

- 状態: 承認
- 日付: 2026-11-09
- 関連: task #937、ADR-0007（決定 3 をこれで置き換える）、blobshare ADR-0099（`/api/app-releases/{app}/{file}`）、RustEdit（RustLogView `crates/plugin-update`）

## 文脈

ADR-0007 で、更新は Scheduler のサーバーの中継（`/api/windows/releases/`）から、サーバーと同じログインで取る形にした。
組み立ては外の使い捨ての置き場（GitHub）で行い、Setup の zip を専用の入れ物 `nolumiascheduler/windows/` に置いていた。

持ち主（2026-11-09）「NolumiaScheduler の Windows アプリにも自動更新の仕組みを」。WinTrack と RustEdit の Velopack の
更新に揃え、配布は blobshare の artifacts、Setup と Portable は zip（exe は落とせない）。今の仕組みを見せたうえで
「RustEdit と同じ形に作り直す」を選んだ。

## 決定

1. **新しい版は blobshare の `https://share.nolumia.com/api/app-releases/nolumiascheduler/` から取る。**
   blobshare は Garage の `artifacts/nolumiascheduler/` を、`APP_RELEASE_CLIENTS` で結んだ client の利用者トークンにだけ返す。
2. **ログインは更新用の別のもの。** assay の public client `nolumiascheduler-update`（`588618dc…`、戻り先
   `http://127.0.0.1:48664/callback`）。blobshare-prod のアプリに結んであり、blobshare を使える人が入れる。
   更新トークンは `%LocalAppData%\NolumiaScheduler\update-auth.bin`（DPAPI）。サーバーの `auth.bin` とは別で、
   どちらかから出てももう一方は残る。設定の画面の「アプリの更新」でログイン・ログアウト・今すぐ確認ができる
3. **当て方は RustEdit の最新（Chrome 式）に揃える。**
   - 起動の 1 分後・6 時間ごと・ログインした直後・「今すぐ確認」で確かめる。自動の確認は黙ったまま、頼まれた確認は必ず結果を出す
   - 見つけたら黙って落とし、**アプリを終了したとき**（トレイの「終了」）に Velopack へ渡して入れ替える（再起動はしない）。
     終了せずに落ちた・PC が止まったときは、次の起動で Velopack が入れ替える（`VelopackApp` の既定）
   - すぐ替えたいときは設定の画面の「いま更新して再起動」
4. **配る物（組み立ては ADR-0007 のまま外の置き場）**: Velopack の一式（nupkg・`releases.win.json` ほか）を
   `artifacts/nolumiascheduler/` へ、持ち主が入れる `NolumiaScheduler-<版>-win-Setup.zip` と `-win-Portable.zip` を
   share.nolumia.com の `files/nolumiascheduler/` へ置く。生の exe は置かない
5. サーバーの中継（`/api/windows/releases/`）と入れ物 `nolumiascheduler` は使わなくなる（片付けはサーバー側と deploy-repo で）

## 影響

- 1.0.424 までの版（サーバーの中継を読む）は新しい版を見付けられない。一度だけ share.nolumia.com の zip から入れ直す
- Scheduler のサーバーにログインしていなくても、更新のログインだけで更新は届く
- 更新の口が blobshare になるので、blobshare を使える人だけが更新を受け取れる（Scheduler のサーバーを使える人と別に割り当てる）
