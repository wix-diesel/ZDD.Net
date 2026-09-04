# ZDD.Net

C# ネイティブ実装の ZDD（Zero-suppressed Decision Diagram）／フロンティア法ライブラリ。

- 100% managed C#（P/Invoke なし・NativeAOT 対応・外部 NuGet 依存ゼロ）
- ターゲット: `net10.0`
- 構成: Core（ZDD エンジン）／ Frontier（フロンティア法フレームワーク）／ Graphs（グラフ問題 API）

集合の族（family of sets）を 1 つの DAG に圧縮して表す ZDD を使うと、「10^24 個の解を数える」
「一様ランダムに 1 つ選ぶ」「重みが最大の集合を求める」といった操作を、族を展開せずノード数に
比例する手間で行える。.NET にはこれのネイティブ実装が事実上存在しない（CUDD の P/Invoke ラッパ
しか選択肢がない）ことが、このライブラリの動機になっている。

## 到達点（v0.5 = v0.4 + I/O・メモリ管理）

- **Core レイヤ（ZDD エンジン）**: `ZddManager` / `Zdd` によるノード表・一意化表・演算キャッシュと、
  家族代数の全演算（和・積・差・対称差・積(`*`)・商・剰余・Meet・`SupersetsOf`/`SubsetsOf` などの
  ふるい・`Change`/`OnSet`/`OffSet`・`Maximal`/`Minimal`/`HittingSets`/`Complement`）、濃度
  （`Count` / `CountApprox` / `CountBySize`）・列挙（`Sets`）・unranking/ranking
  （`ElementAt` / `IndexOf`）・一様ランダムサンプリング（`Sample`）、重み最適化
  （`MaxWeight` / `MinWeight` / `TopK`）、確率・期待値（`Probability` / `ExpectedValue` /
  `ItemFrequency`）、Graphviz DOT 出力（`ToDot` / `WriteDot`）
- **フロンティア法フレームワーク（Frontier）**: `FrontierBuilder.Build` に「スペック」
  （`IDdSpec<TState>` / `IArrayDdSpec`）を渡すだけで ZDD が自動構築される。集合を 1 つも展開しない
  ので、解の個数が `10^24` を超える族でも状態の種類の数だけの手間で構築できる
  （[docs/frontier-guide.md](docs/frontier-guide.md)）。2 つのスペックを中間結果を経由せず直接
  合成する `And`/`Or`/`Subset` にも対応
- **グラフ問題 API（Graphs / Specs）**: `Graph`（格子・完全グラフ・閉路・パスの組み込みショートカット
  つき）と、その上に実装された組み込みスペック（`PathSpec`（s–t 単純パス）/ `SpanningTreeSpec` /
  `ForestSpec` / `MatchingSpec` / `CycleSpec` / `HamiltonianPathSpec` / `HamiltonianCycleSpec` /
  `IndependentSetSpec` / `CliqueSpec` / `VertexCoverSpec` / `DominatingSetSpec` /
  `DegreeConstraintSpec` / `ConnectedSubgraphSpec` / `SteinerTreeSpec` / `GraphPartitionSpec` /
  `CutSpec` / `ColoringSpec` / `DfaSpec` / `PowerSetSpec` / `CardinalitySpec` /
  `LinearConstraintSpec` / `KnapsackSpec`）。いずれも正しさを検証済み（OEIS A007764・Kirchhoff の
  行列木定理・パーマネント照合・彩色多項式・最大流最小カット定理・完全グラフとPetersenグラフの
  既知値など。[docs/benchmarks.md](docs/benchmarks.md) に実行時間・フロンティア幅の基準値）
- **数千辺への対応**: 辺順序の自動最適化（`Graph.Optimize`。BFS/DFS/格子専用/ビームサーチの
  4 戦略）と構築前の見積り（`Graph.EstimateMaxFrontierSize`）、フロンティア状態の bit-packing
  （メモリ 64〜65% 削減）。数千〜数万辺の実データ規模で s–t パス数え上げが完走することを
  [docs/benchmarks.md](docs/benchmarks.md) の M3-11 節で実測
- **高レベル API**: Graphillion 相当の `GraphSet`（`Paths`/`Cycles`/`Trees`/`Forests`/`Matchings`
  などの生成、`Including`/`Excluding`/`Larger`/`Smaller` フィルタ、`MinIter`/`MaxIter`/`RandIter`
  遅延列挙）と、任意の要素型を扱える `SetSet<T>`
- **グラフ入出力（`ZDD.Net.Io`）**: DIMACS / エッジリスト / 本ライブラリ独自の簡易テキスト形式の
  読み書き。実データファイルをそのまま `Graph` として読み込める
- **性能改善（v0.4）**: 演算キャッシュのサイズ自動調整（拡大時にエントリを移行、代表ケースで
  15〜20% 改善）、状態ハッシュの SIMD 化（`Vector256`/`Vector128`、フロンティアが広いケースで
  7〜17% 改善）、フロンティア構築の並列化（`BuildOptions.MaxDegreeOfParallelism`。並列度によらず
  ノード ID は完全一致）。Graphillion（Python + C++ コア）・TdZdd（生 C++）との比較で
  [docs/PLAN.md](docs/PLAN.md) §10 の性能目標（9×9 格子 1 秒以内・11×11 格子 60 秒以内/8 GB 以内・
  Graphillion 比 3 倍以内）を全て達成——測定した全ケースで Graphillion を上回った
  （[docs/benchmarks.md](docs/benchmarks.md) の M4-1〜M4-3・M4-8 節）
- **シリアライズ（v0.5）**: `ZDD.Net.Io.ZddBinaryFormat`（独自バイナリ形式、構築より 1〜2 桁速い
  保存・復元）と `ZDD.Net.Io.GraphillionTextFormat`（Python Graphillion の
  `setset.dump`/`dumps`/`load`/`loads` 互換のテキスト形式、実際に Graphillion 2.1 の出力で
  相互運用を確認済み）。どちらもフレッシュなマネージャへ読み込めばノード ID まで含めて元の族と
  一致し、壊れた入力は `ZddFormatException` になる
- **ノード GC（v0.5）**: `ZddManager.Collect()` / `RootSet` による mark & sweep + コンパクション
  + ID リマップ。参照カウントではなく明示 GC 方式で、GC を生き延びさせたい族だけ `RootSet` に
  登録する。登録していない古いハンドルを GC 後に使うと `ZddCollectedException` になり、黙って
  壊れた結果を返さない
- **DOT 出力の拡張（v0.5）**: `DotOptions` で状態ラベル・レベルラベル・部分表示（`MaxLevels` /
  `MaxNodes` / `FocusNodeId`）・スタイルを指定できる。既定は今までの `ToDot()` と完全に同じ出力

`GraphSet` を使った 5 行サンプル（5×5 格子の対角 s–t 単純パスを 1 本も展開せずに数える）:

```csharp
using System;
using ZDD.Net.Graphs;

Graph grid = Graph.Grid(5, 5);
GraphSet paths = GraphSet.Paths(grid, from: 0, to: grid.VertexCount - 1);
Console.WriteLine(paths.Count); // 8512（OEIS A007764）
```

「格子グラフの s–t パスを数える」から「実グラフ（DIMACS 形式）を読み込んで解く」までを一直線に
辿れるチュートリアルは [docs/tutorial.md](docs/tutorial.md) を参照。

**API はまだ確定していない**（プレリリース版）。v1.0 まではブレーキングチェンジがあり得る。

## インストール

NuGet パッケージは `v0.5.0-preview.1` のようなプレリリースタグから生成される
（v1.0 に達するまではプレリリース版として `--prerelease` が要る）。

```sh
dotnet add package ZDD.Net --prerelease
```

## 最小サンプル

```csharp
using ZDD.Net.Core;

using ZddManager manager = new ZddManager(variableCount: 3);

// 2^{0,1,2} = {∅, {0}, {1}, {2}, {0,1}, {0,2}, {1,2}, {0,1,2}}
Zdd powerSet = manager.Empty.Complement();
Console.WriteLine(powerSet.Count); // 8

// item 0 を含む集合だけを残す。
Zdd containingItem0 = powerSet.OnSet(0);
Console.WriteLine(containingItem0.Count); // 4

foreach (int[] set in containingItem0.Sets())
{
    Console.WriteLine(string.Join(",", set));
}
```

もう少し長い例（家族代数演算・列挙・unranking・一様サンプリング・重み最適化・カスタム評価器）は
[docs/api-guide.md](docs/api-guide.md) と、実際に動く [`samples/Zdd.ApiGuide`](samples/Zdd.ApiGuide) を参照。
フロンティア法・グラフ問題 API の例は [docs/frontier-guide.md](docs/frontier-guide.md) と
[`samples/Zdd.FrontierGuide`](samples/Zdd.FrontierGuide)。
`GraphSet` によるフィルタ・サンプリングから実グラフの読み込みまでの一本道は
[docs/tutorial.md](docs/tutorial.md) と [`samples/Zdd.Tutorial`](samples/Zdd.Tutorial)。
CLI から触ってみたい場合は [`samples/Zdd.Cli`](samples/Zdd.Cli)（`dotnet run --project samples/Zdd.Cli -- --help`）。
`grid-path` / `spanning-tree` / `partition` / `matching` の各サブコマンドで組み込みスペックをそのまま
叩ける（例: `dotnet run --project samples/Zdd.Cli -- grid-path 7 7` は OEIS A007764 の `575780564` を出す）。

## ドキュメント

**[wix-diesel.github.io/ZDD.Net](https://wix-diesel.github.io/ZDD.Net/)** — 手書きガイドと全 public
API の XML doc から生成した、検索可能な公開サイト（DocFX、`.github/workflows/docs.yml` が main への
push ごとに再公開する。M5-6、issue #58）。以下は同じ内容をリポジトリ内で読む場合のリンク:

- **[docs/tutorial.md](docs/tutorial.md)** — チュートリアル（格子グラフの s–t パスを数える →
  フィルタ・サンプリング → 実グラフ（DIMACS）を読み込んで解くまでの一本道）
- **[docs/api-guide.md](docs/api-guide.md)** — API ガイド（`ZddManager`/`Zdd` の使い方、演算一覧、性能上の注意）
- **[docs/frontier-guide.md](docs/frontier-guide.md)** — フロンティア法ガイド（フロンティア法とは何か、
  組み込みスペック一覧、`Graph`/`FrontierManager`/`BuildOptions`、性能の勘所と性能チューニングの指針、
  独自スペックの実例）
- **[docs/frontier-spec-guide.md](docs/frontier-spec-guide.md)** — スペックの書き方（`IDdSpec` 等の契約）
- **[docs/benchmarks.md](docs/benchmarks.md)** — ベンチ基準値（代表ケースの実行時間・フロンティア幅・ノード数）
- **[docs/PLAN.md](docs/PLAN.md)** — 機能・仕様・アーキテクチャ
- **[docs/ROADMAP.md](docs/ROADMAP.md)** — マイルストーン別の PR 単位タスク分割
- **[docs/OPEN-QUESTIONS.md](docs/OPEN-QUESTIONS.md)** — 未確定事項
- **[docs/api-review-notes.md](docs/api-review-notes.md)** — v1.0 に向けた public API レビューメモ
  （M5-7 が棚卸しし、M6-1 で凍結・命名変更する）
- **[CHANGELOG.md](CHANGELOG.md)** — 変更履歴

## ライセンス

Apache-2.0

参考にしたアルゴリズムの出典は [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) を参照。
