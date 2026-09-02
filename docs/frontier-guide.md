# フロンティア法ガイド

フロンティア法フレームワーク（`ZDD.Net.Frontier` / `ZDD.Net.Graphs` / `ZDD.Net.Specs`）の使い方をまとめた資料。
「スペックを書けば ZDD が自動構築される」という本ライブラリの中核価値を、実際に手を動かして確かめられる
ところまでを狙っている。Core（`ZddManager`/`Zdd`）の使い方は [docs/api-guide.md](api-guide.md) を参照。

このガイドに載っている中心的なコード片（1 節・3〜7 節の実行例）は
[`samples/Zdd.FrontierGuide/Program.cs`](../samples/Zdd.FrontierGuide/Program.cs) にそのまま置いてあり、
CI が毎回ビルドして実行している（`.github/workflows/ci.yml` の「frontier-guide サンプルの実行」）。
2 節（`Graph` の作り方）の一部の断片は、示し方の一例として載せているだけで、サンプルには含まれない。
手元で確かめたいときは:

```sh
dotnet run --project samples/Zdd.FrontierGuide
```

- 対象バージョン: v0.2（M2 フロンティア法フレームワーク完成版）＋ M3-1（辺順序の最適化。次期リリース）

---

## 1. フロンティア法とは何か

ZDD を知らない読者向けに一言で言うと: フロンティア法は、「アイテムを 1 個ずつ『入れる／入れない』と
決めていき、**以降の判定に必要な情報だけ**を状態として持ち回る」という探索アルゴリズムである。
同じ状態に行き着いた枝は、そこから先の振る舞いが完全に同じなので 1 つにまとめられる。これを
アイテムの並び順に幅優先で行い、まとめた結果をそのまま DAG にすると ZDD ができる。

集合を 1 つも展開しないため、たとえば 5×5 格子の対角 s–t 単純パスが 8,512 通りあっても、パスを
1 本ずつ数え上げるのではなく、状態の種類の数だけの手間で ZDD を構築できる（下の例では、実際に
8,512 という厳密な個数を `Zdd.Count` で求めている。パスの経路そのものは 1 本も展開していない）。

```csharp
using System;
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;
using ZDD.Net.Specs;

Graph grid = Graph.Grid(5, 5);
using ZddManager manager = new ZddManager(grid.EdgeCount);

int s = 0;
int t = grid.VertexCount - 1;
Zdd paths = FrontierBuilder.Build<PathSpec>(manager, new PathSpec(grid, s, t));

// OEIS A007764（n×n 格子の対角単純パス数）: n=5 は 8512。
Console.WriteLine(paths.Count); // 8512
```

`FrontierBuilder.Build` が本ライブラリの中心的な入口である。利用者は「スペック」——
根の状態と、状態＋枝から次の状態を返す関数——を書くだけで、あとは `Build` が ZDD を構築する
（構築されたあとは `paths.Count` / `paths.Sample(random)` / `paths.MaxWeight(weights)` など、
Core の全演算がそのまま使える）。

## 2. `Graph` の作り方

グラフ問題のスペック（`PathSpec` など）は `ZDD.Net.Graphs.Graph` を受け取る。`Graph` は
「無向・単純（自己ループ・多重辺なし）グラフを辺リストとして持つ」だけの薄い型だが、
**辺の並び順がフロンティア法の変数順序そのもの**という点が重要（6 節）。

```csharp
Graph grid = Graph.Grid(3, 3);       // rows x cols 格子。既定で「行ごとに水平辺→次行への垂直辺」の順
Graph complete = Graph.Complete(5);  // 完全グラフ K5（辺は (u, v), u < v の辞書式順）
Graph cycle = Graph.Cycle(6);        // 閉路 0-1-2-...-(n-1)-0
Graph path = Graph.Path(6);          // 単純パス 0-1-2-...-(n-1)

// 自分でグラフを組み立てることもできる。
var custom = new Graph(vertexCount: 4, new[] { new Edge(0, 1), new Edge(1, 2), new Edge(2, 3), new Edge(3, 0) });

// 既存のグラフを、別の辺順序で作り直す（変数順序の最適化を自分で試したいとき）。
Graph reordered = grid.WithEdgeOrder(new[] { 2, 0, 1, /* ... */ });

// 辺順序を自動で最適化する（6.1 節）。元の grid は変更されない。
Graph optimized = grid.Optimize(EdgeOrderStrategy.Bfs);
```

`Graph.EdgeCount` が、そのグラフに対する ZDD の変数の個数（`ZddManager` に渡す `variableCount`）になる。

## 3. 組み込みスペックの一覧と使い方

`ZDD.Net.Specs` には、代表的な組み合わせ問題のスペックが最初から用意されている。どれも
`readonly struct` で、`FrontierBuilder.Build` にそのまま渡せる。

| スペック | 族 | 状態 | インタフェース |
|---|---|---|---|
| `PowerSetSpec` | `n` 要素の冪集合そのもの（`2^n` 個） | なし（1 種類） | `IDdSpec<byte>` |
| `CardinalitySpec` | 要素数が `[min, max]` に収まる部分集合 | これまでに選んだ個数 | `IDdSpec<int>` |
| `LinearConstraintSpec` | `Σ a[i] x[i] {<=, ==, >=} b` を満たす部分集合 | 重み付き和（`long`） | `IDdSpec<long>` |
| `KnapsackSpec` | `Σ weights[i] x[i] <= capacity`（`LinearConstraintSpec` の特化版） | 残り容量（`long`） | `IDdSpec<long>` |
| `PathSpec` | グラフの `s`–`t` 単純パス（`AllowAnyEndpoints` で任意の 2 頂点間） | フロンティア頂点ごとの mate | `IArrayDdSpec` |
| `SpanningTreeSpec` | グラフの全域木 | フロンティア頂点ごとの連結成分番号 | `IArrayDdSpec` |
| `ForestSpec` | 成分数を指定した森（`components: 1` は全域木と同じ族） | 同上 | `IArrayDdSpec` |
| `MatchingSpec` | グラフのマッチング（`perfect: true` で完全マッチングのみ） | フロンティア頂点ごとの被覆フラグ | `IArrayDdSpec` |
| `CycleSpec` | 単純サイクルの族（`single: true`（既定）は単一サイクルのみ、`false` は互いに素なサイクルの和） | フロンティア頂点ごとの mate | `IArrayDdSpec` |
| `HamiltonianPathSpec` | 全頂点を通る `s`–`t` 単純パス | 同上 | `IArrayDdSpec` |
| `HamiltonianCycleSpec` | 全頂点を通る単一の単純サイクル | 同上 | `IArrayDdSpec` |

```csharp
using ZddManager manager = new ZddManager(variableCount: 5);

Zdd powerSet = FrontierBuilder.Build<PowerSetSpec, byte>(manager, new PowerSetSpec(itemCount: 5));

Zdd sizeTwoOrThree = FrontierBuilder.Build<CardinalitySpec, int>(
    manager, new CardinalitySpec(itemCount: 5, min: 2, max: 3));

int[] coefficients = { 3, 1, 4, 1, 5 };
Zdd atMostSeven = FrontierBuilder.Build<LinearConstraintSpec, long>(
    manager, new LinearConstraintSpec(coefficients, LinearConstraintOperator.LessOrEqual, bound: 7));

int[] weights = { 2, 3, 4, 5, 9 };
Zdd fitsCapacity = FrontierBuilder.Build<KnapsackSpec, long>(manager, new KnapsackSpec(weights, capacity: 10));
```

グラフ問題のスペックは `IArrayDdSpec` なので、`Build` は型引数 1 つのオーバーロードで呼ぶ。
`ZddManager` の `variableCount` は `graph.EdgeCount` に合わせる（2 節）。

```csharp
Graph grid = Graph.Grid(3, 3);
using ZddManager manager = new ZddManager(grid.EdgeCount);

Zdd spanningTrees = FrontierBuilder.Build<SpanningTreeSpec>(manager, new SpanningTreeSpec(grid));
Zdd forest = FrontierBuilder.Build<ForestSpec>(manager, new ForestSpec(grid, components: 1));
// forest == spanningTrees（components: 1 は全域木と同じ族）

Zdd matchings = FrontierBuilder.Build<MatchingSpec>(manager, new MatchingSpec(grid));

Zdd cycles = FrontierBuilder.Build<CycleSpec>(manager, new CycleSpec(grid, single: true));
Zdd hamiltonianPaths = FrontierBuilder.Build<HamiltonianPathSpec>(
    manager, new HamiltonianPathSpec(grid, s: 0, t: grid.VertexCount - 1));
Zdd hamiltonianCycles = FrontierBuilder.Build<HamiltonianCycleSpec>(manager, new HamiltonianCycleSpec(grid));
```

いずれも正しさを検証済み: `PathSpec` は OEIS A007764、`SpanningTreeSpec`/`ForestSpec` は
Kirchhoff の行列木定理、`MatchingSpec` はパーマネント照合、`CycleSpec`/`HamiltonianPathSpec`/
`HamiltonianCycleSpec` は完全グラフの既知の式（ハミルトン閉路数 `(n-1)!/2` など）と Petersen
グラフ（ハミルトン閉路 0）との一致を CI のテストで確認している（`tests/ZDD.Net.Tests/Specs/`）。

## 4. 構築前の見積り（`EstimateMaxFrontierSize` / `FrontierManager`）

`ZDD.Net.Graphs.FrontierManager` は、スペックも ZDD の構築もまだ行わずに、**辺順序だけ**から
フロンティア幅（＝計算量とメモリの見積り）を求められる。「大きなグラフを渡す前に、この辺順序で
現実的な規模になりそうか確かめたい」というときに使う。

```csharp
Graph grid = Graph.Grid(3, 3);

// 手軽な方
Console.WriteLine(grid.EstimateMaxFrontierSize());              // この辺順序でのフロンティア幅の最大値
Console.WriteLine(grid.EstimateMaxFrontierSize(EdgeOrderStrategy.Bfs)); // 並べ替えたら幅がどうなるか

// 前計算した表ごと欲しい方（スペックを書くときはこちら）
FrontierManager frontierManager = new FrontierManager(grid);
Console.WriteLine(frontierManager.MaxFrontierSize);             // 上の 1 行目と同じ値
```

`Graph.EstimateMaxFrontierSize()` は `FrontierManager.MaxFrontierSize` と同じ値を、`FrontierManager`
の残りの前計算をせずに返す。どちらも `O(VertexCount + EdgeCount)` なので、数千辺のグラフでも
「構築を始める前に」呼べる。数の意味に注意: ここでの**フロンティア幅は頂点の個数**（スペックが
状態を持たなければならない頂点の数）で、`BuildOptions.MaxFrontierSize`（5 節）が数える
「1 水準の状態の種類数」とは別物。前者は後者の指数の肩に乗る量、という関係にある。

グラフ問題のスペックを自分で書くときも、`FrontierManager` はそのまま部品として使える:
`IntroducedVertices(edgeIndex)` / `ForgottenVertices(edgeIndex)` で各辺が持ち込む・手放す頂点を、
`MateIndex(edgeIndex, vertex)` でその頂点が状態配列のどのスロットに対応するかを教えてくれる。
組み込みの `PathSpec` / `SpanningTreeSpec` / `ForestSpec` / `MatchingSpec` は、いずれもこの上に
実装されている（`src/ZDD.Net/Specs/` を参照)。

## 5. `BuildOptions` による上限設定

`FrontierBuilder.Build` の第 3 引数 `BuildOptions` で、構築の上限とフックを指定できる。既定では
何も制限されない。

```csharp
var options = new BuildOptions
{
    MaxNodeCount = 10_000_000,     // 一時ノードの総数の上限
    MaxFrontierSize = 100_000,     // 1 水準の状態の種類数（＝フロンティア幅）の上限
    CancellationToken = token,     // 水準の切り替わりごとに観測される
};

// フロンティア幅の履歴を水準ごとに受け取る（bench/ZDD.Net.Benchmarks がピークフロンティア幅を
// 記録するのに使っているのと同じ仕組み）。
var progress = new Progress<BuildProgress>(p => Console.WriteLine($"level {p.Level}: width {p.FrontierSize}"));
options.Progress = progress;

Zdd result = FrontierBuilder.Build<PathSpec>(manager, spec, options);
```

上限を超えると、メモリを使い切って落ちる代わりに `BuildLimitExceededException` で止まる
（原因が「この上限を超えた」とはっきり分かる形で失敗する）。4 節の見積り（フロンティア幅＝頂点の
個数）が大きいグラフほど、ここで数える状態の種類数は指数的に増える——見積りが大きいときこそ
`MaxFrontierSize` / `MaxNodeCount` に許容できる上限を入れておく、という使い方になる。

## 6. 性能の勘所

### 6.1 辺順序でフロンティア幅が変わる

グラフ問題では、**辺の並び順がそのままフロンティア法の変数順序になる**（`Graph.Edges` の順序
＝スペックが辺を決めていく順序）。同じグラフでも辺順序が変わればフロンティア幅（＝計算量と
メモリ）は大きく変わりうる。`Graph.Grid` が「行ごとに水平辺→次行への垂直辺」という順序を既定に
しているのは、格子グラフでこの順序がフロンティアを狭く保つ経験則があるため。

一方、ファイルから読んだ辺リストのように**任意の順で並んだグラフ**は、そのままでは幅が桁違いに
大きいことがある。`Graph.Optimize` はそれを並べ替える:

```csharp
Graph optimized = graph.Optimize(EdgeOrderStrategy.Bfs);   // 既定は Bfs

Console.WriteLine(graph.EstimateMaxFrontierSize());        // 例: 1408（並べ替え前）
Console.WriteLine(optimized.EstimateMaxFrontierSize());    // 例: 42（並べ替え後）
```

| 戦略 | 内容 | 向いているグラフ |
|---|---|---|
| `AsGiven` | 何もしない（比較の基準） | 既に良い順序だと分かっているとき |
| `Bfs` | 幅優先で頂点を訪問し、両端が訪問済みになった時点で辺を出す（既定） | 大半のグラフ。Graphillion と同じ既定 |
| `Dfs` | 深さ優先版 | 中心から長い鎖が何本も伸びるグラフ（`Bfs` は全ての枝を同時に進めてしまう） |
| `Grid` | 格子専用の蛇行順序（短い辺に沿って折り返しながら長い辺方向へ進む） | 格子。格子でなければ `Bfs` にフォールバックする |
| `BeamSearchPathWidth` | パス幅の近似最小化 | **未実装（M3-3）**。呼ぶと `NotSupportedException` |

どの戦略が勝つかはグラフによる（[docs/benchmarks.md](benchmarks.md) の M3-1 節に実測値がある）。
`EstimateMaxFrontierSize(strategy)` は並べ替え後のグラフを作らずに幅だけを返すので、構築前に
戦略を比較できる:

```csharp
foreach (EdgeOrderStrategy strategy in new[] { EdgeOrderStrategy.Bfs, EdgeOrderStrategy.Dfs, EdgeOrderStrategy.Grid })
{
    Console.WriteLine($"{strategy}: {graph.EstimateMaxFrontierSize(strategy)}");
}
```

探索の開始頂点も選べる（既定は次数最小の頂点）。開始頂点だけで幅が何倍も変わることがある:

```csharp
graph.Optimize(EdgeOrderStrategy.Bfs, EdgeOrderOptions.FromVertex(0));        // 頂点を指定する
graph.Optimize(EdgeOrderStrategy.Bfs, EdgeOrderOptions.BestOfCandidates());   // 全頂点を試して最良を採る
graph.Optimize(EdgeOrderStrategy.Bfs, EdgeOrderOptions.BestOfCandidates(20)); // 次数の小さい 20 個だけ試す
```

#### 辺 index の対応表を必ず通すこと

**ここが辺順序最適化で最も事故りやすい点**。`Optimize` は新しい `Graph` を返し（元のグラフは
変更されない）、その中で**辺が振り直される**。つまり並べ替え後のグラフで構築した ZDD は
「並べ替え後の辺 index」で表されていて、そのまま元のグラフの辺として読むと**黙って間違った答え**に
なる。`Graph.SourceOrder`（`EdgeOrderMapping`）がその対応表:

```csharp
Graph optimized = graph.Optimize();
EdgeOrderMapping mapping = optimized.SourceOrder!;   // Optimize / WithEdgeOrder が返したグラフには必ず付く

using ZddManager manager = new ZddManager(optimized.EdgeCount);
Zdd paths = FrontierBuilder.Build<PathSpec>(manager, new PathSpec(optimized, 0, optimized.VertexCount - 1));

foreach (int[] edgeSet in paths.Sets())
{
    // 並べ替え後の辺 index → 元のグラフの辺 index（昇順に整列して返る）
    int[] original = mapping.ToSourceEdgeSet(edgeSet);

    foreach (int edgeIndex in original)
    {
        Edge edge = graph.GetEdge(edgeIndex);   // 元のグラフの辺として読める
    }
}
```

1 個だけ変換するなら `mapping.ToSourceEdgeIndex(i)`、逆向きは `mapping.FromSourceEdgeIndex(i)`。
`mapping.Source` は「並べ替えの直前のグラフ」なので、並べ替えを 2 回重ねたときは
`optimized.SourceOrder.Source.SourceOrder` と鎖をたどるか、毎回元のグラフから並べ替える。

### 6.2 `TSpec` を interface 型で受けないこと

`FrontierBuilder.Build` はスペックを**型引数 + `struct` 制約**で受ける
（`where TSpec : struct, IDdSpec<TState>` / `where TSpec : struct, IArrayDdSpec`）。`GetChild` は
「状態 1 個 × 枝 2 本ごと」に呼ばれる最も内側のループなので、`class` にしたり `IDdSpec<TState> spec`
のような interface 型の変数として持ち回ったりすると仮想呼び出しになり、実測で数倍遅くなる。
`struct` かつ型引数で受けていれば、JIT がスペックごとに特殊化し `GetChild` はインライン展開される。
同じ方針の背景は [docs/api-guide.md](api-guide.md) §5.1（`IDdEval`/`IWeightOps`）にも書いてある。

## 7. `IDdSpec<TState>` の書き方: 独自スペックを 1 つ書いてみる

スペックの契約（`GetRoot`/`GetChild` の戻り値の規約、状態の寿命、`StateEquals`/`StateHashCode` の
整合性など）の詳しい説明は [docs/frontier-spec-guide.md](frontier-spec-guide.md) にまとめてある。
ここでは、それを読んだうえで実際に 1 つ書いてみる——「連続する 3 要素を同時に選べない」という
簡単な制約。

考え方: 状態は「以降の遷移に影響する情報だけ」を持てばよい（frontier-spec-guide.md §4）。この
制約では、**直近に何個連続して選んだか**（0, 1, または 2）だけが以降の判定に効く。3 個並んだ
時点でアイテムの並び上どこであろうと不正になるので、それより前に何を選んだかは要らない。

```csharp
public readonly struct NoThreeConsecutiveSpec : IDdSpec<int>
{
    private readonly int _itemCount;

    public NoThreeConsecutiveSpec(int itemCount) => _itemCount = itemCount;

    public int GetRoot(ref int run)
    {
        run = 0;
        return _itemCount;
    }

    public int GetChild(ref int run, int level, int value)
    {
        if (value == 0)
        {
            run = 0;
        }
        else
        {
            run++;
            if (run >= 3)
            {
                return DdResult.False; // 枝刈り: 3 連続に達したら以降は全部不正
            }
        }

        int remaining = level - 1;
        return remaining == 0 ? DdResult.True : remaining;
    }

    public bool StateEquals(in int left, in int right) => left == right;
    public int StateHashCode(in int state) => state;
}
```

```csharp
using ZddManager manager = new ZddManager(variableCount: 8);
Zdd family = FrontierBuilder.Build<NoThreeConsecutiveSpec, int>(manager, new NoThreeConsecutiveSpec(8));
```

このスペックはブルートフォース（8 要素すべての `2^8` 通りを 1 つずつ数える）と一致することを
サンプル（[`samples/Zdd.FrontierGuide/Program.cs`](../samples/Zdd.FrontierGuide/Program.cs) の
`CustomSpecNoThreeConsecutive`）で確かめている——「チュートリアルどおりに書けば独自スペックが
作れる」ことの実例である。

## 8. さらに詳しく

- スペックの規約の詳しい説明（`IDdSpec`/`IArrayDdSpec`/`IHybridDdSpec` の契約、状態の寿命、
  `struct` を強く勧める理由）: [docs/frontier-spec-guide.md](frontier-spec-guide.md)
- Core（`ZddManager`/`Zdd`）の使い方: [docs/api-guide.md](api-guide.md)
- ベンチ基準値（代表ケースの実行時間・フロンティア幅・ノード数）: [docs/benchmarks.md](benchmarks.md)
- 仕様・アーキテクチャの全体像: [docs/PLAN.md](PLAN.md)
- マイルストーン別の実装計画: [docs/ROADMAP.md](ROADMAP.md)
- 実行できるサンプル: [`samples/Zdd.FrontierGuide`](../samples/Zdd.FrontierGuide)（このガイドの
  コード片）、[`samples/Zdd.Cli`](../samples/Zdd.Cli)（CLI）、
  [`samples/Zdd.ApiGuide`](../samples/Zdd.ApiGuide)（Core のコード片）
