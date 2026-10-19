# ADR-0001: CI は forge の Linux の台で Core を組み、CoreTests を流す

- 状態: 承認
- 日付: 2026-10-19
- 関連: task #537、サーバーの ADR-0054 決定 7（forge `kyon/nolumiascheduler-server` の
  `docs/decisions/ADR-0054-nolumiascheduler-server-is-split-out-of-wbs.md`）

このリポジトリの設計判断はここ（`docs/decisions/`）に置く。番号はこのリポジトリの中で 0001 から振る
（サーバーの ADR とは別の連番。サーバーのものを指すときは「サーバーの ADR-0054」と書く）。

## 背景

2026-10-19 に正本を GitHub から forge `kyon/NolumiaScheduler` へ移した（GitHub は forge の写し）。
それまで CI は無かった。forge の台は Linux だけで、Windows の台は無い。

## 決定

- `.forgejo/workflows/ci.yml` で、net10.0 の 5 つ（Domain・Application・Infrastructure・Cli・CoreTests）を
  名指しで組み、`NolumiaScheduler.CoreTests` を流す。PR と main への push で走る。
- 画面の殻（`NolumiaScheduler.Presentation`・`NolumiaScheduler.WinUI`・`NolumiaSchedulerTest`）は
  Windows 向け（`net10.0-windows…`・WinUI）なので CI に入れない。`NolumiaScheduler.slnx` には殻も入っているので、
  slnx ではなく csproj を名指しする。
- **判断は殻に書かず、Core（Domain・Application・Infrastructure）に置いて CoreTests で守る**。殻は配線と表示だけにする。
- 2026-10-19 の時点で CoreTests（228 件）は Linux でそのまま全部通った。Windows でしか通らない試験は無かったので、
  外す印（`[TestCategory]` など）は付けていない。今後足す試験が Windows に頼る（DPAPI・レジストリ・Windows のタイムゾーン名など）
  なら、その部分は殻の側に置くか、Core ではインターフェースの向こうに置いて偽物で試験する。
- 手本は WinTrack（forge `kyon/wintrack` の CI）。SDK は `dotnet-install.sh` で `global.json` の版を入れ、`~/.dotnet` を覚えておく。

## 理由

- WinUI の殻は CI でビルドも試験もされない。殻に判断を書くと、壊れても持ち主の PC で使うまで分からない（WinTrack の教訓）。
- `dotnet format` の検査は入れていない。今のコードが通るか確かめておらず、CI を入れる PR で整形の差分を混ぜたくないため。要るなら別の PR で入れる。
