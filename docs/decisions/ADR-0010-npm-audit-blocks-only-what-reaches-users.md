# ADR-0010: npm の弱点の検査で止めるのは、利用者に届く部品だけにする

- 日付: 2026-11-17
- 状態: 承認（task #1097）

## 文脈

2026-11-17、picomatch（GHSA-fc5q-39xc-vjpf・high・直した版なし）がビルドと試験の道具の中だけで `npm audit --audit-level=high` を落とし、
ほかのリポジトリの main を赤くした。持ち主の判断で、全リポジトリの npm の検査を「届く部品は止める・道具は知らせるだけ」に揃える。

## 決定

deploy-repo の ADR-0003 に従う。`security-audit.yml` の npm を 2 段にする（`npm audit --audit-level=high --omit=dev` で止め、
道具を含む全部は警告を出すだけで緑のまま）。

このリポジトリの npm は画面の試験の道具（Playwright）だけで、利用者に届く部品は無い。止める段はいまは空だが、型として置く。

## 理由

deploy-repo の ADR-0003 を見る。

## 影響

- npm で利用者に届く部品を足すときは `dependencies` に書く（止める段の対象になる）
