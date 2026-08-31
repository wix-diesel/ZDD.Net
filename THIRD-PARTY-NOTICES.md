# Third-Party Notices

ZDD.Net (Apache-2.0) は、コードを直接移植せず、以下の文献・OSS に記載されたアルゴリズムを
再実装している（方針の詳細は [docs/PLAN.md](docs/PLAN.md) §1「ライセンス方針」を参照）。
`src/ZDD.Net` は外部 NuGet パッケージへの依存を持たない。

このファイルは「アルゴリズムの出典」を記載するものであり、以下のリポジトリ・論文の著作権者と
ZDD.Net の間にコードの派生関係があることを示すものではない。

## アルゴリズムの出典

### TdZdd

- リポジトリ: https://github.com/kunisura/TdZdd
- 作者: ERATO MINATO Discrete Structure Manipulation System Project
- ライセンス: MIT
- 参考にした点: `DdSpec` によるフロンティア法の抽象化、レベル単位の幅優先構築と削減、
  `DdEval` によるボトムアップ評価、`zddSubset` によるスペック合成という設計の主軸
  （ZDD.Net では `IDdEval<TValue>` / `ZddEvaluation.Evaluate` として再実装）

### Graphillion

- リポジトリ: https://github.com/graphillion/graphillion
- ライセンス: MIT
- 参考にした点: `GraphSet` / `SetSet` の集合ライクな高レベル API の語彙
  （`paths()` / `trees()` / `matchings()` などの命名、`rand_iter` / `max_iter` /
  `probability` に相当する操作の設計）。実装は独自

### SAPPOROBDD

- 作者: Shin-ichi Minato
- ライセンス: MIT
- 参考にした点: ZDD の家族代数演算の定義そのもの（`Change` / `OnSet` / `OffSet` /
  `Product` / `Quotient` / `Remainder` / `Meet` / `Permit` / `Restrict` 等。
  ZDD.Net では対応する演算名の別名として `Subset1` / `Subset0` / `Permit` / `Restrict` を提供）、
  一意化表と演算キャッシュの古典的な構成

### CUDD および EXTRA

- リポジトリ: https://github.com/ivmai/cudd（および Minato による EXTRA 拡張）
- ライセンス: BSD 系
- 参考にした点: 演算キャッシュ（lossy direct-mapped cache）の設計、動的リサイズ、
  mark & sweep によるノード GC の実装知見（ノード GC は v0.1 時点では未実装、
  [docs/PLAN.md](docs/PLAN.md) §12 の M5 以降で対応予定）

### Knuth, *The Art of Computer Programming*, Volume 4A, §7.1.4

- 作者: Donald E. Knuth
- 出典: 教育目的で公開されている BDD14 プログラム群を含む
- 参考にした点: SIMPATH（単純パス列挙）アルゴリズム、mate 配列の定義、
  ZDD 上のカウント・ランダム抽出・最適化アルゴリズムの記述
  （`Zdd.Sample` / `Zdd.MaxWeight` / `Zdd.MinWeight` / `Zdd.TopK` として再実装）

### フロンティア法に関する論文群

- Kawahara, Saitoh, Yoshinaka ほかによるフロンティア法の一般化・グラフ分割・
  連結成分制約・次数制約の状態設計に関する論文
- 参考にした点: v0.2 以降の Frontier レイヤの設計（[docs/PLAN.md](docs/PLAN.md) §6〜§7）

### JDD / Sylvan / OxiDD

- 参考にした点: 並列 DD 構築、ノード表のロックフリー化の設計（[docs/PLAN.md](docs/PLAN.md) §10 の
  並列化方針。v0.4 以降のスコープ）

## OEIS

- **A007764**（n×n 頂点格子の対角自己回避パス数）: テストにおける既知値との照合に使用。
  https://oeis.org/A007764
