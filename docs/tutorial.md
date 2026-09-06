# チュートリアル

ZDD.Net を初めて触る読者が、「格子グラフの s–t パスを数える」という最小の例から出発して、
フィルタ・サンプリング・辺順序の最適化を経て、**実データ（DIMACS 形式のファイル）を読み込んで
解くところまで**を一直線に辿れるようにした資料。Core（`ZddManager`/`Zdd`）そのものの使い方は
[docs/api-guide.md](api-guide.md)、フロンティア法フレームワークの詳しい説明は
[docs/frontier-guide.md](frontier-guide.md) を参照。このチュートリアルはその上に立つ、
「まず動かして感触を掴む」ための一本道である。

このガイドに載っているコード片は [`samples/Zdd.Tutorial/Program.cs`](https://github.com/wix-diesel/ZDD.Net/blob/main/samples/Zdd.Tutorial/Program.cs)
にそのまま置いてあり、CI が毎回ビルドして実行している（`.github/workflows/ci.yml` の
「tutorial サンプルの実行」）。手元で確かめたいときは:

```sh
dotnet run --project samples/Zdd.Tutorial
```

- 対象バージョン: v0.3（M3「数千辺への対応と高レベル API」完成版。`GraphSet` / `SetSet<T>` /
  `Graph.Optimize` / グラフ入出力を含む）

---

## 1. 格子グラフの s–t パスを数える

ZDD.Net の入口は `ZDD.Net.Graphs.GraphSet`——Graphillion の語彙を .NET 命名規約に直した
高レベル API である。`GraphSet.Paths(graph, from, to)` だけで、s–t 単純パスの族が手に入る。

```csharp
using ZDD.Net.Graphs;

Graph grid = Graph.Grid(5, 5);
GraphSet paths = GraphSet.Paths(grid, from: 0, to: grid.VertexCount - 1);

Console.WriteLine(paths.Count); // 8512（OEIS A007764: 5×5 格子の対角単純パス数）
```

`paths.Count` は `BigInteger` で、**パスを 1 本も展開せず**にノード数に比例する手間で求まる
（内部で何が起きているかは [docs/frontier-guide.md](frontier-guide.md) §1 を参照。
「フロンティア法とは何か」の説明そのものがこの例）。`GraphSet` は
`ZDD.Net.Frontier.FrontierBuilder.Build<PathSpec>` を呼ぶ薄いラッパーで、低レベル API を
直接使っても同じ結果になる:

```csharp
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Specs;

using ZddManager manager = new ZddManager(grid.EdgeCount);
Zdd lowLevel = FrontierBuilder.Build<PathSpec>(manager, new PathSpec(grid, s: 0, t: grid.VertexCount - 1));
// lowLevel.Count == paths.Count
```

`GraphSet` は `Paths` 以外にも `Cycles` / `Trees` / `Forests` / `Matchings` /
`HamiltonianPaths` / `HamiltonianCycles` / `Cliques` / `IndependentSets` を用意している
（一覧は [docs/frontier-guide.md](frontier-guide.md) §3 の組み込みスペック表と対応する）。

## 2. フィルタとサンプリング

`GraphSet` にはフィルタ（`Including` / `Excluding` / `Larger` / `Smaller` / `LenEquals`）と、
遅延列挙・サンプリングのメソッドが揃っている。

```csharp
Edge firstStep = grid.GetEdge(0);

GraphSet through = paths.Including(firstStep);   // firstStep を通るパスだけ
GraphSet avoiding = paths.Excluding(firstStep);   // firstStep を通らないパスだけ
// through.Count + avoiding.Count == paths.Count

GraphSet shortPaths = paths.Smaller(10); // 辺数が 10 未満のパスだけ
GraphSet longPaths = paths.Larger(9);    // 辺数が 9 を超えるパスだけ
// shortPaths.Count + longPaths.Count == paths.Count
```

**フィルタは構築時に適用される**——`paths` を作ってから `Intersect` で絞るのではなく、
フィルタ込みの新しいスペックで `FrontierBuilder` を最初から回し直す。捨てる枝を探索の途中で
刈っていくので、絞り込む前の族より中間結果が大きくなることはない
（考え方は [docs/frontier-guide.md](frontier-guide.md) §8「スペックの合成」と同じ）。

```csharp
// MinIter / MaxIter は遅延列挙: 先頭 k 件だけを見るなら、手間も k に比例する
// （族全体を展開してから並べ替えるのではない）。
int shortestLength = paths.MinIter(edge => 1).First().Count; // 8（最短の対角パスの辺数）

// Sample は族に属するどの集合も等しい確率で選ぶ一様ランダム抽出。
var random = new Random(Seed: 42);
IReadOnlySet<Edge> sample = paths.Sample(random);
// paths.Contains(sample) は常に true
```

## 3. 実グラフを読み込んで解く

ここまでは `Graph.Grid` で作った格子だったが、実際にはファイルから読んだグラフを扱うことが多い。
`ZDD.Net.Io.DimacsGraph` は DIMACS 形式（グラフベンチマークの事実上の標準形式）の読み書きを提供する。

以下は 3×3 格子と同じグラフを表す DIMACS テキストだが、辺は行ごとの綺麗な順序ではなく
**「ファイルに書かれていた順」を模した任意の順**に並んでいる——実データはフロンティア法にとって
都合の良い順序になっているとは限らない、という現実的な状況を再現している
（DIMACS は頂点が 1 始まりであることに注意。`DimacsGraph` がこの変換を吸収するので、
`Graph` 側は常に 0 始まりのまま扱える）:

```csharp
using ZDD.Net.Io;

const string dimacsText = """
    c 3x3 格子と同じグラフ。辺は「ファイルに書かれていた順」を模して並んでいる
    p edge 9 12
    e 5 6
    e 1 2
    e 4 7
    e 2 3
    e 5 8
    e 1 4
    e 6 9
    e 2 5
    e 7 8
    e 3 6
    e 8 9
    e 4 5
    """;

// 実ファイルを読むなら File.OpenText(path) を DimacsGraph.Read(TextReader) に渡せばよい。
// 文字列から直接読みたいだけなら、この string オーバーロードで十分。
Graph graph = DimacsGraph.Read(dimacsText);
```

## 4. 辺順序の最適化と見積り

読み込んだグラフをそのまま使うと、フロンティア幅（＝計算量とメモリ）が無駄に広いことがある。
`Graph.EstimateMaxFrontierSize()` は ZDD を 1 つも構築せずに、**辺順序だけ**から幅を見積れる
（`O(VertexCount + EdgeCount)` なので、数千辺のグラフでも構築を始める前に呼べる）。

```csharp
int asGivenWidth = graph.EstimateMaxFrontierSize();       // ファイルの順のままの幅
Graph optimized = graph.Optimize(EdgeOrderStrategy.Bfs);  // 辺を並べ替えた新しいグラフ（既定は Bfs）
int optimizedWidth = optimized.EstimateMaxFrontierSize(); // 並べ替え後の幅
// optimizedWidth < asGivenWidth（この例では 8 → 4）
```

`Optimize` は**新しい `Graph` を返し、元のグラフは変更しない**。頂点番号は変わらないので、
`GraphSet.Paths(optimized, from: 0, to: ...)` のように `s`/`t` はそのまま渡せる
（変わるのは**辺**の番号だけ——`Zdd.Sets()` などで辺集合を直接取り出して元のグラフの辺として
読みたい場合は `Graph.SourceOrder` を通す必要がある。詳しくは
[docs/frontier-guide.md](frontier-guide.md) §6.1 の「辺 index の対応表を必ず通すこと」を参照）。

```csharp
GraphSet paths = GraphSet.Paths(optimized, from: 0, to: optimized.VertexCount - 1);

// 並べ替え前のグラフで直接数えても同じ族になる——変わるのは構築の手間だけ。
GraphSet pathsAsGiven = GraphSet.Paths(graph, from: 0, to: graph.VertexCount - 1);
// pathsAsGiven.Count == paths.Count
```

### 幅が大きすぎるときにどうするか

見積りが大きく、構築が現実的な時間・メモリで終わりそうにないときの実践的な選択肢:

1. **辺順序を変える**: `Graph.Optimize(strategy)` の戦略を変えてみる
   （`Bfs` / `Dfs` / `Grid` / `BeamSearchPathWidth`。使い分けは
   [docs/frontier-guide.md](frontier-guide.md) §6.1 の表を参照）。どれが勝つかはグラフの形による
   ので、`EstimateMaxFrontierSize(strategy)` で構築前に比較するのがよい。
2. **対象を先に絞る**: `GraphSet` の `Including` / `Excluding` / `Smaller` で条件を絞ってから数える
   （§2）。絞り込みは構築時に効くので、絞り込む前の族を経由しない。
3. **問いを軽くする**: 「族に属する集合の総数」ではなく、「最短のもの」（`MinWeight`）や
   「上位 k 件」（`TopK`）など、数え上げより軽い問いに切り替えられないか考える。

それでも構築が終わらない・終わるべきでない規模なら、いきなりメモリを使い切って落ちるのではなく
`BuildOptions.MaxNodeCount` / `MaxFrontierSize` で上限を切っておく。超えると
`BuildLimitExceededException` で、原因の分かる形で止まる（`GraphSet` はこのオプションを
直接は取らないので、この用途では 1 節の低レベル API に戻って `FrontierBuilder.Build` に
`BuildOptions` を渡す):

```csharp
using ZDD.Net.Frontier;

var tooTight = new BuildOptions { MaxFrontierSize = grid.EstimateMaxFrontierSize() - 1 };
using ZddManager manager = new ZddManager(grid.EdgeCount);

try
{
    FrontierBuilder.Build<PathSpec>(manager, new PathSpec(grid, s: 0, t: grid.VertexCount - 1), tooTight);
}
catch (BuildLimitExceededException)
{
    // 見積りより厳しい上限を指定すると、メモリを使い切る前にここに来る。
}
```

**「見積り（幅）が狭ければ必ず完走する」わけではない**ことにも注意がいる。
`EstimateMaxFrontierSize` が測るのはフロンティアの**頂点**の個数で、これは状態数が指数的に
増えうる「肩」の広さを示すだけであり、その肩の上で実際に何種類の状態が生まれるかはグラフの
迂回路の多さに強く依存する。数千辺の実グラフで実際に何が起き、どこまでなら完走するかの
生の実測（幅が狭くても完走しないケースを含む）は
[docs/benchmarks.md](benchmarks.md) の M3-11 節に記録してある。

## 5. 有向グラフ: 一方通行のある道路網

ここまでは無向グラフ（`Graph`）だけを扱ってきたが、実際の道路網には一方通行の道がある。
ZDD.Net はこれを `ZDD.Net.Graphs.DirectedEdge` / `DirectedGraph` で表す。`Edge` と違い
`DirectedEdge(u, v)` は向きを区別する（`DirectedEdge(0, 1) != DirectedEdge(1, 0)`）ため、
逆平行の 2 本 `u→v` / `v→u` を両方持たせれば「両方向通行」の道も表せる——一方通行の道は
片方の弧だけを、両方向の道は両方の弧を用意すればよい。

次の 5 つの交差点（頂点 0〜4）からなる道路網を考える: `0→1`・`1→2`・`2→4`・`0→3` は
一方通行、**交差点 2 と 3 の間だけは両方向通行**（`2→3` と `3→2` の両方の弧を持つ）。

```csharp
using ZDD.Net.Graphs;

DirectedEdge[] roads =
{
    new DirectedEdge(0, 1),
    new DirectedEdge(1, 2),
    new DirectedEdge(2, 4),
    new DirectedEdge(0, 3),
    new DirectedEdge(2, 3), // 交差点 2-3 間の両方向通行のうち 2->3 側
    new DirectedEdge(3, 2), // 同じく 3->2 側
};
DirectedGraph roadNetwork = new DirectedGraph(vertexCount: 5, roads);

DirectedGraphSet paths = DirectedGraphSet.Paths(roadNetwork, from: 0, to: 4);
Console.WriteLine(paths.Count); // 2
```

`DirectedGraphSet` は `GraphSet` と同じ使い心地の高レベル API で、`Including`/`Excluding`/
`Count`/`Sample` などが揃っている（低レベル API は `FrontierBuilder.Build<DirectedPathSpec>`）。
2 本のパスのうち 1 本（`0→3→2→4`）は、両方向通行の**逆向き**の弧 `3→2` を通らないと
存在しない——この弧が無ければ交差点 3 は行き止まりになる:

```csharp
DirectedGraph oneWayOnly = new DirectedGraph(vertexCount: 5, new[]
{
    new DirectedEdge(0, 1),
    new DirectedEdge(1, 2),
    new DirectedEdge(2, 4),
    new DirectedEdge(0, 3),
    new DirectedEdge(2, 3), // 3->2 が無いので、交差点 3 は行き止まり
});
Console.WriteLine(DirectedGraphSet.Paths(oneWayOnly, from: 0, to: 4).Count); // 1
```

`DirectedGraph.ToUndirected()` は弧の向きを無視して無向グラフに変換する（逆平行の 2 本は
無向辺 1 本に潰れるため辺数が減りうる——`roadNetwork` は弧 6 本だが `ToUndirected()` の結果は
辺 5 本になる）。デバッグや辺順序計算の下請け以外の用途は想定していない。

有向グラフでも `PathSpec` 以外の組み込みスペックが一通り揃っている
（`DirectedCycleSpec` / `DirectedHamiltonianPathSpec` / `DirectedHamiltonianCycleSpec` /
`DirectedDegreeConstraintSpec` / `ArborescenceSpec`（根つき有向全域木））。一覧と使い方は
[docs/frontier-guide.md](frontier-guide.md) §3 の組み込みスペック表、設計の背景は
[docs/design/m7-directed-graphs.md](design/m7-directed-graphs.md) を参照。

## 6. さらに詳しく

- 型・メンバ単位の詳しいリファレンス（全 public API の XML doc から自動生成）:
  [wix-diesel.github.io/ZDD.Net](https://wix-diesel.github.io/ZDD.Net/)
- フロンティア法フレームワークの詳しい説明（フロンティア法とは何か、組み込みスペック一覧、
  `BuildOptions`、独自スペックの書き方、スペックの合成）: [docs/frontier-guide.md](frontier-guide.md)
- Core（`ZddManager`/`Zdd`）の使い方: [docs/api-guide.md](api-guide.md)
- 数千辺の実グラフでの実測（完走するか・何を要するか、幅が狭くても完走しない実例を含む）:
  [docs/benchmarks.md](benchmarks.md) の M3-11 節
- グラフ入出力（DIMACS 以外にエッジリスト形式・本ライブラリ独自の簡易テキスト形式もある）:
  `ZDD.Net.Io`（`DimacsGraph` / `EdgeListGraph` / `SimpleTextGraph`）の XML ドキュメント
- 任意の要素型の族を扱いたいとき（グラフの辺集合以外）: `ZDD.Net.Sets.SetSet<T>`
- 実行できるサンプル: [`samples/Zdd.Tutorial`](https://github.com/wix-diesel/ZDD.Net/tree/main/samples/Zdd.Tutorial)（このチュートリアルの
  コード片）、[`samples/Zdd.FrontierGuide`](https://github.com/wix-diesel/ZDD.Net/tree/main/samples/Zdd.FrontierGuide)、
  [`samples/Zdd.ApiGuide`](https://github.com/wix-diesel/ZDD.Net/tree/main/samples/Zdd.ApiGuide)、[`samples/Zdd.Cli`](https://github.com/wix-diesel/ZDD.Net/tree/main/samples/Zdd.Cli)
