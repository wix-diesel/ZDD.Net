# Changelog

このプロジェクトの変更点は本ファイルに記載する。フォーマットは
[Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) に、バージョニングは
[Semantic Versioning](https://semver.org/lang/ja/) に準拠する。

v1.0 までは API 未確定のプレリリース版として公開する（[docs/PLAN.md](docs/PLAN.md) §13）。

## [Unreleased]

### Added

- `ZDD.Net.Specs`: サイクル・ハミルトン系のグラフスペック（M3-4、issue #36）
  - `CycleSpec(graph, single:)`: 単純サイクルの族。`single: true`（既定）は単一の単純サイクル、
    `single: false` は互いに素な単純サイクルの和（空集合は含まない）。`single` のほうが常に
    `!single` の部分集合になる
  - `HamiltonianPathSpec(graph, s, t)`: 全頂点を通る `s`–`t` 単純パス
  - `HamiltonianCycleSpec(graph)`: 全頂点を通る単一の単純サイクル
  - 状態は `PathSpec`（M2-8）と同じ mate 配列で、共通部分は `MateChainState`（internal）に
    抽出した。4 つのスペックはそこから生えるチェーンの接合ロジックを共有し、終端条件
    （forgotten になる頂点にどの次数を許すか、チェーンが閉じたときに受理とするか拒否とするか）
    だけがそれぞれ異なる
  - 完全グラフの既知値と一致: ハミルトン閉路数 `(n-1)!/2`、ハミルトンパス数（全始点対の総和）
    `n!/2`、単純サイクル総数（`Σ C(n,k)(k-1)!/2`）。Petersen グラフはハミルトン閉路 0
    （かつハミルトンパスは存在する）、`Cycle(n)` は（両モードとも）サイクル数 1 と一致
- `ZDD.Net.Graphs`: 辺順序（＝フロンティア法の変数順序）の最適化（M3-1、issue #33）
  - `Graph.Optimize(EdgeOrderStrategy, EdgeOrderOptions)`: 辺を並べ替えた**新しい `Graph`** を返す
    （元のグラフは変更しない）。戦略は `AsGiven` / `Bfs`（既定。Graphillion と同じ）/ `Dfs` /
    `Grid`（格子専用の蛇行順序。格子でなければ `Bfs` にフォールバック）/ `BeamSearchPathWidth`
    （M3-3、下記）
  - `EdgeOrderOptions`: 探索の開始頂点の選び方（次数最小＝既定 / `FromVertex` で指定 /
    `BestOfCandidates` で複数試して最良）
  - `Graph.SourceOrder`（`EdgeOrderMapping`）: 並べ替え後の辺 index ↔ 元の辺 index の対応表。
    並べ替え後のグラフで構築した ZDD は並べ替え後の辺 index で表されるため、元のグラフの辺として
    読むには `ToSourceEdgeIndex` / `ToSourceEdgeSet` を通す必要がある（最も事故りやすい点）
  - `Graph.EstimateMaxFrontierSize()` / `EstimateMaxFrontierSize(EdgeOrderStrategy, EdgeOrderOptions)`:
    構築を始める前の見積り API（`O(VertexCount + EdgeCount)`）。後者は並べ替え後のグラフを作らずに
    戦略ごとの幅を比較できる
  - 効果: 辺が任意の順に並んだ 40×40 格子（3,120 辺）でフロンティア幅 1,408 → 42、
    3×9 格子の s–t パス構築が 2,065 ms → 0.3 ms（[docs/benchmarks.md](docs/benchmarks.md) の M3-1 節）。
    `dotnet run -c Release --project bench/ZDD.Net.Benchmarks -- edge-order` で再現できる
- `ZDD.Net.Graphs`: `EdgeOrderStrategy.BeamSearchPathWidth` ——頂点順序のビームサーチによる
  パス幅近似最小化（厳密最小化は NP 困難）（M3-3、issue #35）
  - 頂点を 1 つずつ追加する探索で、候補はそのつど「これまでの最大フロンティア幅」を主、
    同点なら「BFS が次に訪れる頂点への距離」を副、「幅の総和」をさらに副にして選ぶ。
    幅だけの貪欲は局所構造を持つグラフで安く見える袋小路に迷い込みやすく、実測で `Bfs` の
    2〜3 倍まで悪化することがあった——BFS の訪問順を同点時の指針にすることでこれを避けている
    （選定理由は `BeamSearchPathWidth.cs` のコメント、効果は docs/benchmarks.md の M3-3 節）
  - `EdgeOrderOptions.BeamWidth` / `CancellationToken` を追加。既定のビーム幅は 8、
    既定では次数最小の頂点から 3 通りを試す（`Bfs` / `Dfs` は既定 1 通り——複数の開始頂点を
    試すこと自体がこの戦略の一部）。キャンセルされた探索は例外を投げず、その時点までの
    最良の順序をそのまま返す
  - 効果（[docs/benchmarks.md](docs/benchmarks.md) の M3-3 節）: 格子ではない不規則なグラフ
    （道路網・電力網を模した最近傍グラフ、数百〜数千辺）で `Bfs` 比 17〜28% 改善。
    前処理は数千辺で数秒以内。格子では `Bfs`/`Grid` が既に近い最適なので伸びしろは薄い（2%）

### Changed

- `ZDD.Net.Frontier`: フロンティア状態の **bit-packing**（M3-2、issue #34）。
  `IArrayDdSpec` の状態を `int[]`（1 スロット 4 バイト）ではなく、
  **1 本の `byte[]` へ固定ストライドで詰めて保持する**ようになった（internal な変更で、
  スペックの API は `Span<int>` のまま。利用者側の変更は不要）
  - スロット幅は値域に応じて 1 / 2 / 4 バイトを自動で選ぶ（`PackedStateLayout`）。
    初期の窓は `-8..247`——mate / comp のスロットは「フロンティア内のスロット番号か
    小さな番兵（`-1` / `-2`）」しか取らないので、実際のグラフではこれで足りる。
    窓から外れる値が来たら窓を広げて既存の状態を詰め直すが、広げた窓は必ず前の窓を含むため、
    詰め直しは 1 回の構築で高々 2 回（1 → 2 → 4 バイト）で打ち止めになる
  - 比較とハッシュは、要素ごとではなく**詰めたバイト列に対してワード単位（`ulong`）で**行う
  - 構築される ZDD は**ノード ID まで含めて変更前と完全に一致する**
    （`StateBitPackingTests` が DOT 出力のダイジェストで固定している）
  - 効果（[docs/benchmarks.md](docs/benchmarks.md) の M3-2 節。
    `-- memory` / `-- time` で再現できる）: 状態がメモリを支配するケースで
    **ピークメモリ 64〜65% 削減・実行時間 28〜36% 短縮**。
    一方、フロンティアが常に小さいケース（`PerfectMatching_Grid6x6` で 0.44 ms → 0.57 ms）は
    詰める手間だけが残るため最大 1.4 倍遅くなる
- `bench/ZDD.Net.Benchmarks`: `-- memory`（ピークメモリ）と `-- time`（構築時間の最小値・中央値）
  の 2 モードを追加

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
