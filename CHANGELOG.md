# Changelog

このプロジェクトの変更点は本ファイルに記載する。フォーマットは
[Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) に、バージョニングは
[Semantic Versioning](https://semver.org/lang/ja/) に準拠する。

v1.0 までは API 未確定のプレリリース版として公開する（[docs/PLAN.md](docs/PLAN.md) §13）。

## [Unreleased]

## [0.2.0] - 2026-09-01

M2「フロンティア法フレームワーク」マイルストーン（[docs/PLAN.md](docs/PLAN.md) §12）の内容。
「スペックを書けば ZDD が自動構築される」という中核価値が、s–t パス・全域木・マッチング・
基数制約という 4 つの実問題で検証済みの状態になった。

### Added

- `ZDD.Net.Frontier`: フロンティア法のスペックのインタフェース
  `IDdSpec<TState>` / `IArrayDdSpec` / `IHybridDdSpec<TScalar>` と、終端の定数 `DdResult`
  （戻り値の規約は TdZdd 互換: `0` = ⊥、`-1` = ⊤、正数 = 次の水準）
- レベル単位の状態表（internal）: 固定長 struct 状態用
  `StructLevelStateTable<TSpec, TState>`（スペックの `StateEquals` / `StateHashCode` で照合）と、
  可変長配列状態用 `ArrayLevelStateTable`（1 本の `int[]` に詰めて要素ごとに照合）。
  どちらもオープンアドレス法で、状態の重複除去（同じ状態になった枝を 1 本にまとめる）を担う。
  レベルの切り替えは `LevelStateTablePair<TTable>` が表 2 枚を回して行い、
  バッファは `ArrayPool` から借りるので、ピークメモリは深さによらず 2 レベル分に収まる
- トップダウン幅優先展開（internal）: `TopDownExpander<TSpec, TState>` / `ArrayTopDownExpander<TSpec>`
  が根の水準から 1 まで水準を 1 つずつ降りながら `GetChild` をたどり、**一時ノード表**
  `TemporaryNodeTable`（レベルごとの `(lo, hi)` 配列。ZDD の削減規則はまだ適用していない）を作る。
  同じ状態に至った枝は 1 つの一時ノードに合流する。展開は反復で、変数 10 万でも
  スタックオーバーフローしない
- `BuildOptions`: 構築の上限とフック。`MaxNodeCount`（一時ノードの総数）/
  `MaxFrontierSize`（1 水準の状態の種類数）を超えると `BuildLimitExceededException` で止まる
  （メモリを使い切って落ちる代わりに、原因の分かる例外で止める。docs/PLAN.md §13）。
  `CancellationToken` で中断でき、`IProgress<BuildProgress>` に水準ごとの進捗が届く
- `FrontierBuilder.Build`: トップダウン展開とボトムアップ削減（`BottomUpReducer`）をつなぎ、
  `ZddManager` の正準なノード表に取り込む公開の構築器。`IDdSpec<TState>` と `IArrayDdSpec` の
  両方に対応するオーバーロードがあり、スペックを書けばそのまま `Zdd` が手に入る
- `ZDD.Net.Specs`: 組み込みスペック `PowerSetSpec`（冪集合） / `CardinalitySpec`（要素数の範囲制約） /
  `LinearConstraintSpec`（線形不等式・等式制約） / `KnapsackSpec`（容量制約、`LinearConstraintSpec`
  の特化版）
- `ZDD.Net.Graphs`: グラフデータ構造 `Graph`（辺リスト。辺順序が変数順序そのもの）と
  `Edge`、組み込みショートカット `Graph.Grid` / `Complete` / `Cycle` / `Path`、辺順序を差し替える
  `Graph.WithEdgeOrder`
- `FrontierManager`: グラフの辺順序だけから、スペックも ZDD の構築も行わずにフロンティア幅
  （`MaxFrontierSize`）を事前見積りできる。`IntroducedVertices` / `ForgottenVertices` /
  `MateIndex` はグラフ問題のスペックを自分で書くときの部品にもなる
- グラフ問題の組み込みスペック（すべて `ZDD.Net.Graphs.FrontierManager` の上に実装）:
  `PathSpec`（`s`–`t` 単純パス、`AllowAnyEndpoints` で任意の 2 頂点間。Knuth の `SIMPATH`、
  OEIS A007764 と照合済み） / `SpanningTreeSpec` と `ForestSpec`（成分数指定の森。Kirchhoff の
  行列木定理と照合済み） / `MatchingSpec`（マッチング、`perfect: true` で完全マッチング。
  パーマネント照合済み）
- `bench/ZDD.Net.Benchmarks`: BenchmarkDotNet によるベンチ基準値（代表 10 ケース）と、
  `docs/benchmarks.md` への記録。以降の性能改善 PR（辺順序最適化・bit-packing 等）は、
  ここに記録された数値との相対比較で受け入れを判定する（issue #31）
- `docs/frontier-guide.md`: フロンティア法ガイド（フロンティア法とは何か、組み込みスペック一覧、
  `Graph`/`FrontierManager`/`BuildOptions` の使い方、性能の勘所、独自スペックを 1 つ書く実例）。
  コード片は `samples/Zdd.FrontierGuide` として実際に動き、CI が毎回実行する
- `docs/frontier-spec-guide.md`: スペックの書き方（`IDdSpec`/`IArrayDdSpec`/`IHybridDdSpec` の契約、
  状態の寿命、実装例）
- `docs/benchmarks.md`: ベンチ基準値と測定環境・再現方法
- プレリリース版タグ `v0.2.0-preview.1`

### Notes

- `IHybridDdSpec<TScalar>`（スカラ + `int` 配列の複合状態）は契約のみで、
  `FrontierBuilder.Build` のオーバーロードは未対応（v0.3 以降）
- 変数順序（辺順序）の自動最適化は未実装。今のところ利用者が `Graph.WithEdgeOrder` で選ぶ
  （docs/frontier-guide.md §6.1、M3 以降の課題）

## [0.1.0] - 2026-08-31

M1「Core エンジン」マイルストーン（[docs/PLAN.md](docs/PLAN.md) §12）の内容。ZDD Core エンジンが
単体で完結し、外部から「Core だけでも使える .NET 製 ZDD ライブラリ」として触れる状態になった。

### Added

- `ZddManager` / `Zdd`: ノード表・一意化表・演算キャッシュを持つ ZDD エンジン本体。
  変数（item）の個数は生成時に固定、正準形（同じ族 ⇔ 同じノード ID）を保証する。
- 家族代数の全演算:
  - 集合演算: `Union` (`|`) / `Intersect` (`&`) / `Difference` (`-`) /
    `SymmetricDifference` (`^`) / `Complement` (`~`) / `IsSubsetOf` / `Overlaps`
  - ZDD 固有演算（Minato の基本演算）: `Product` (`*`) / `Quotient` (`/`) /
    `Remainder` (`%`) / `Meet` / `SupersetsOf`(`Restrict`) / `SubsetsOf`(`Permit`) /
    `NonSubsetsOf` / `NonSupersetsOf` / `Change` / `OnSet`(`Subset1`) /
    `OffSet`(`Subset0`) / `Flip` / `Maximal` / `Minimal` / `HittingSets`(`Blocking`)
- 問い合わせ・列挙: `Contains` / `Count`（`BigInteger`、厳密） / `CountApprox`（`double`、近似） /
  `CountBySize` / `Support` / `Sets`（遅延列挙、`ZddEnumerationOrder` で順序選択）
- unranking / ranking / サンプリング: `ElementAt` / `IndexOf` / `Sample`
  （族を展開せずノード数に比例する手間で、一様ランダムな抽出を行う）
- 重み最適化: `MaxWeight` / `MinWeight` / `TopK`（`IWeightOps<TWeight>` による
  利用者定義の重み型に対応。組み込みは `Int32WeightOps` / `Int64WeightOps` /
  `DoubleWeightOps` / `BigIntegerWeightOps`）
- 確率・期待値: `Probability` / `ExpectedValue` / `ItemFrequency`
- カスタム評価器の枠組み: `IDdEval<TValue>` とボトムアップ評価
  （`Zdd.Evaluate<TEval, TValue>`）。`Count` などの組み込み評価はすべてこの上に実装されている
- Graphviz DOT 出力: `Zdd.ToDot()` / `Zdd.WriteDot(TextWriter)`
- `docs/api-guide.md`: API ガイド（使い方・演算一覧・性能上の注意）
- サンプル: `samples/Zdd.Cli`（族を組み立てて統計・DOT を出す CLI）、
  `samples/Zdd.ApiGuide`（`docs/api-guide.md` のコード片を実行して検証するサンプル）
- CI（GitHub Actions）: ビルド・単体テスト・プロパティテスト（CsCheck）・
  サンプルの実行と DOT 構文検証・カバレッジ計測
- `THIRD-PARTY-NOTICES.md`: 参考にしたアルゴリズムの出典

### Notes

- **すべての演算が反復実装**で、再帰しない（ZDD の深さは変数の個数そのものであり、
  再帰では大規模な族で `StackOverflowException` になり得るため）
- `src/ZDD.Net` は**外部 NuGet 依存ゼロ**（テスト・サンプルのみ依存を持つ）
- **フロンティア法フレームワーク（Frontier）とグラフ問題 API（Graphs）は未実装**（v0.2 以降）
- API はプレリリース扱いで、v1.0 まで破壊的変更があり得る
