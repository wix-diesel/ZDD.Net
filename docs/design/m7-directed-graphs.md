# M7: 有向グラフ対応（v0.7）設計書

- ドキュメント版数: v1 (2026-09-04)
- 対応するタスク表: [docs/ROADMAP.md](../ROADMAP.md) の M7 節
- 前提: [m6-api-expansion.md](m6-api-expansion.md)（v0.6）が完了していること

## 0. なぜ必要か

`Graphs/Edge.cs` の `Edge` は `GetHashCode` を `HashCode.Combine(Math.Min(U,V), Math.Max(U,V))` で
組んでおり、`Graph` のコンストラクタは自己ループと多重辺を明示的に拒否している。
つまり現状の ZDD.Net は**無向単純グラフ専用**で、

- 一方通行のある道路網の経路列挙
- 依存関係グラフ・ワークフローの経路列挙
- 有向全域木（arborescence）・有向閉路

が原理的に書けない。D1（主用途 = 経路列挙・数え上げ）を踏まえると、これは
「他ライブラリにもある機能」ではなく「主用途の半分が欠けている」に近い。

Graphillion も無向専用なので**互換性のために我慢する理由は無い**。
TdZdd は `DdSpec` が汎用なので有向グラフ用スペックを自分で書けるが、
ライブラリとしては提供していない。つまりここは ZDD.Net が先に出られる部分でもある。

## 1. スコープ

| 入れる | 入れない |
|---|---|
| `DirectedGraph`（逆平行辺 `u→v` と `v→u` の共存を許す） | 自己ループ（`u→u`）。無向側と揃えて拒否する |
| 有向 s–t 単純パス・有向単純閉路 | 多重辺（同じ `u→v` が 2 本） |
| 有向ハミルトンパス・閉路 | 混合グラフ（有向辺と無向辺の混在） |
| 有向次数制約（入次数・出次数を別々に） | フロー・最小費用流（ZDD の守備範囲外） |
| arborescence（根つき有向全域木） | DAG 部分グラフの列挙（トポロジカル順の状態が大きく、別途検討） |
| `DirectedGraphSet` 高レベル API | 有向グラフの木分解ベース辺順序（v1.1 候補） |
| 有向グラフ I/O（エッジリスト・DIMACS 拡張） | |

---

## 2. データ構造

### 2.1 `DirectedEdge`

```csharp
public readonly struct DirectedEdge : IEquatable<DirectedEdge>
{
    public int From { get; }
    public int To { get; }

    public DirectedEdge(int from, int to);
    public DirectedEdge Reversed();
    public Edge AsUndirected();     // フロンティア計算用に端点対だけを取り出す
}
```

`Edge` と違い `GetHashCode` は **向きを区別する**（`HashCode.Combine(From, To)`）。
`DirectedEdge(0,1) != DirectedEdge(1,0)`。これが `Edge` と分ける唯一かつ十分な理由で、
`Edge` に `IsDirected` フラグを足す案は採らない（等価性の意味がフラグで変わる型は事故のもと）。

### 2.2 `DirectedGraph`

```csharp
public sealed class DirectedGraph
{
    public DirectedGraph(int vertexCount, IEnumerable<DirectedEdge> edges);

    public int VertexCount { get; }
    public int EdgeCount { get; }
    public IReadOnlyList<DirectedEdge> Edges { get; }

    public IReadOnlyList<int> OutgoingEdges(int vertex);
    public IReadOnlyList<int> IncomingEdges(int vertex);
    public IReadOnlyList<int> IncidentEdges(int vertex);   // 向き無視
    public int OutDegree(int vertex);
    public int InDegree(int vertex);

    // Graph と同じ辺順序 API
    public int EdgeIndexToVariableIndex(int edgeIndex);
    public int EdgeIndexToLevel(int edgeIndex);
    public DirectedGraph WithEdgeOrder(IReadOnlyList<int> edgeOrder);
    public DirectedGraph Optimize(EdgeOrderStrategy strategy = EdgeOrderStrategy.Bfs, EdgeOrderOptions options = default);
    public int EstimateMaxFrontierSize();
    public EdgeOrderMapping? SourceOrder { get; }

    // 相互変換
    public Graph ToUndirected();                  // 逆平行辺は 1 本に潰れる（辺数が減りうる）
    public static DirectedGraph Bidirected(Graph graph);   // 各無向辺を両向きの 2 本に開く

    // 生成ショートカット（Graph と対応させる）
    public static DirectedGraph Grid(int rows, int cols);   // = Bidirected(Graph.Grid(...))
    public static DirectedGraph Complete(int n);            // 全順序対
    public static DirectedGraph Cycle(int n);               // 一方向の閉路
    public static DirectedGraph Path(int n);
}
```

**`ToUndirected()` の意味論**: 逆平行辺 `u→v` と `v→u` は無向辺 1 本に潰れるので
**辺数が変わりうる**。辺 index の対応が壊れるため、戻り値の `SourceOrder` は `null` にする
（`GraphSet.ToEdgeOrder`（M6-6）を誤って通せないようにする）。用途は「ざっくり構造を見る」
デバッグと辺順序計算の下請けに限る、と XML doc に書く。

**検証**: `Bidirected(g).ToUndirected()` が `g` と（辺順序を除いて）一致すること。

### 2.3 フロンティア基盤の共有（M7-2）

`FrontierManager` は `graph.VertexCount` / `graph.EdgeCount` / `graph.GetEdge(i).U`, `.V` しか
見ていない。`EdgeOrdering` も `IncidentEdges` / `Degree` / `TryGetGridShape` までで、
**どちらも辺の向きを必要としない**。

そこで内部型 `EdgeTopology`（頂点数・端点対の配列・頂点ごとの接続辺リスト）を切り出し、
`Graph` と `DirectedGraph` の双方がこれを公開（internal）する。
`FrontierManager` と `EdgeOrdering` は `EdgeTopology` を受け取るように付け替える。

```csharp
internal sealed class EdgeTopology
{
    public int VertexCount { get; }
    public int EdgeCount { get; }
    public (int U, int V) Endpoints(int edgeIndex);
    public IReadOnlyList<int> IncidentEdges(int vertex);
}

public sealed class FrontierManager
{
    public FrontierManager(Graph graph);
    public FrontierManager(DirectedGraph graph);      // 追加
}
```

**「無向の影グラフを作って使い回す」案は採れない**。逆平行辺があると影グラフに多重辺が生じ、
`Graph` のコンストラクタが拒否するため。端点対の配列を直接渡す形にする必要がある。

この PR は**振る舞いを一切変えない純粋なリファクタ**であり、既存テストが全て通ることが
受け入れ条件。行数は多いが差分は機械的になる。

---

## 3. 有向スペック

### 3.1 状態表現の指針

有向の制約は「無向としての形（連結性・閉路の有無）」＋「各頂点の入出次数」に分解できる。
前者は既存の mate 配列（`MateChainState`）／comp 配列（`SpanningComponentState`）が
そのまま使える。**新しく要るのは向きの情報だけ**。

有向 s–t 単純パスの場合、フロンティア頂点ごとに必要な追加情報は **1 ビット**で足りる。

| 無向次数（mate が持っている） | 必要な向き情報 |
|---|---|
| 0 | 不要 |
| 1 | その 1 本が「入る」か「出る」か → **1 ビット** |
| 2 | 単純パスなので必ず「1 本入って 1 本出る」→ 一意に決まる。不要 |

したがって状態は `mate 配列 + MaxFrontierSize ビット`。M3-2 のパック済み状態バッファに
1 バイト／頂点の向き配列を追加する形で実装し、ビットへの詰め直しは
「状態サイズがボトルネックになったら」の最適化として後回しにする（設計書に理由を残す）。

### 3.2 `DirectedPathSpec`（M7-3）

```csharp
public readonly struct DirectedPathSpec : IArrayDdSpec
{
    public DirectedPathSpec(DirectedGraph graph, int from, int to, bool allowAnyEndpoints = false);
}
```

弧 `u→v` を**採用**するときの遷移:

1. `u` が既に出次数 1 → ⊥。`v` が既に入次数 1 → ⊥。
2. `u == to` なら ⊥（`to` から出る弧は無い）。`v == from` なら ⊥。
3. `MateChainState.Splice(u, v)` を呼ぶ。閉路になるなら ⊥（`PathSpec` と同じ）。
4. 向きビットを更新する。

弧を**不採用**のときは何もしない（既存の `PathSpec` と同じ）。

頂点 `w` が**フロンティアから出る**とき:

| `w` | 要求する (入次数, 出次数) |
|---|---|
| `from` | (0, 1) |
| `to` | (1, 0) |
| その他 | (0, 0) または (1, 1) |

`allowAnyEndpoints = true` のときは「入次数 0・出次数 1 の頂点がちょうど 1 つ、
入次数 1・出次数 0 の頂点がちょうど 1 つ、残りは (0,0) か (1,1)」となり、
端点をまだ見ていない個数をカウンタで持つ（`PathSpec` の同オプションと同じ作り）。

**受け入れ条件（強い検証が取れる）**:

`DirectedGraph.Bidirected(g)` 上の有向 s→t 単純パスの個数は、
`g` 上の無向 s–t 単純パスの個数と**厳密に一致する**（無向パスは s→t 向きに一意に定まるため）。
したがって **OEIS A007764（格子の自己回避パス数）をそのまま流用できる**。
7×7 までを CI、8×8 以上は手動。加えて頂点 8 以下のランダム有向グラフで総当たり照合。

### 3.3 `DirectedCycleSpec` / 有向ハミルトン（M7-4）

```csharp
public readonly struct DirectedCycleSpec : IArrayDdSpec
{
    public DirectedCycleSpec(DirectedGraph graph, bool single = true);
}
public readonly struct DirectedHamiltonianPathSpec : IArrayDdSpec { ... }
public readonly struct DirectedHamiltonianCycleSpec : IArrayDdSpec { ... }
```

有向単純閉路は「全頂点で入次数 = 出次数 ∈ {0,1}」＋ mate 配列の閉路検出。
`CycleSpec` の受理条件をそのまま使い、向きの整合だけ追加する。

**受け入れ条件**: `Bidirected(g)` 上の有向単純閉路の個数は `g` 上の無向単純閉路の個数の
**ちょうど 2 倍**（各閉路に 2 通りの向きがある）。完全グラフ・Petersen グラフの既知値と照合。
有向ハミルトン閉路は完全有向グラフ `K_n` で `(n-1)!` と一致すること。

### 3.4 有向次数制約と arborescence（M7-5）

```csharp
public readonly struct DirectedDegreeConstraintSpec : IArrayDdSpec
{
    public DirectedDegreeConstraintSpec(DirectedGraph graph, int[] inLo, int[] inHi, int[] outLo, int[] outHi);
}

/// <summary>根つき有向全域木（root から全頂点へ到達可能な out-arborescence）。</summary>
public readonly struct ArborescenceSpec : IArrayDdSpec
{
    public ArborescenceSpec(DirectedGraph graph, int root, bool spanning = true);
}
```

arborescence は「無向として全域木」＋「`root` 以外の入次数がちょうど 1、`root` の入次数 0」。
前半は `SpanningTreeSpec` の comp 配列がそのまま使え、後半は入次数カウンタだけで済む。

**受け入れ条件**: **有向版行列木定理（Tutte の BEST 定理の基礎、有向ラプラシアンの
余因子）**で独立に計算した値と一致すること。無向の `SpanningTreeSpec` が
Kirchhoff の行列木定理で検証されているのと同じ手法の有向版。

### 3.5 `DirectedGraphSet`（M7-6）

```csharp
public sealed class DirectedGraphSet : IEnumerable<IReadOnlySet<DirectedEdge>>
{
    public static DirectedGraphSet Paths(DirectedGraph graph, int from, int to, bool allowAnyEndpoints = false);
    public static DirectedGraphSet Cycles(DirectedGraph graph, bool single = true);
    public static DirectedGraphSet HamiltonianPaths(DirectedGraph graph, int s, int t);
    public static DirectedGraphSet HamiltonianCycles(DirectedGraph graph);
    public static DirectedGraphSet Arborescences(DirectedGraph graph, int root);
    public static DirectedGraphSet DegreeConstrained(DirectedGraph graph, int[] inLo, int[] inHi, int[] outLo, int[] outHi);

    // GraphSet と同じフィルタ・列挙・重み API
    public DirectedGraphSet Including(DirectedEdge edge);
    public DirectedGraphSet Excluding(DirectedEdge edge);
    public DirectedGraphSet Including(int vertex);
    public DirectedGraphSet Excluding(int vertex);
    public DirectedGraphSet Larger(int n);
    public DirectedGraphSet Smaller(int n);
    public DirectedGraphSet LenEquals(int n);
    public DirectedGraphSet CostAtMost(Func<DirectedEdge, long> cost, long bound);
    // Count / MinIter / MaxIter / RandIter / MaxWeight / TopK / Sample / Probability ...
}
```

`GraphSet` とほぼ同型なので、**共通部分は `SetSet<T>` に寄せて薄いラッパにする**。
`GraphSet` が `SetSet<Edge>` の上に載っているのと同じ構造で、
`DirectedGraphSet` は `SetSet<DirectedEdge>` の上に載せる。
`IErasedGraphSpec`（型消去したスペックの連鎖）もそのまま再利用する。

**`GraphSet` と共通の基底クラスは作らない**。C# のジェネリクスでは
「`Including` が自分自身の型を返す」ための自己参照型引数（CRTP）が必要になり、
公開 API の可読性が大きく落ちるため。重複するのは各 30 行程度のラッパで、
本体のロジックは `SetSet<T>` 側に 1 つしか無い。

### 3.6 有向グラフ I/O（M7-7）

| 形式 | 有向での扱い |
|---|---|
| エッジリスト | `u v` を `u→v` として読む。`DirectedEdgeListGraph` を新設 |
| 簡易テキスト | ヘッダに `directed` を書けるよう拡張。既存ファイルは無向として読める（後方互換） |
| DIMACS | `p edge` に対して `p arc` を受け付ける（`.gr` 系の慣行に合わせる） |
| Graphillion 互換 | **対応しない**。Graphillion 側に有向の概念が無いため |
| DOT | `digraph` として出力する（`->`） |

---

## 4. 性能とベンチ（M7-8）

有向は無向に比べ、同じ頂点数でも**変数（弧）の数がおよそ 2 倍**になるためフロンティア幅が広がる。
基準値を取っておく。

| ケース | 目的 |
|---|---|
| `Bidirected(Grid(n,n))` の s–t 有向パス（n = 5..8） | 無向の同ケースとの倍率を測る。正当性検証も兼ねる（A007764 と一致） |
| 一方通行を混ぜた格子（各辺を確率 p で単方向化） | 実際の道路網に近い形。p を振って幅の変化を見る |
| `K_n` の有向ハミルトン閉路（n = 6..9） | `(n-1)!` との照合と、密グラフでの限界確認 |
| arborescence（格子・ランダム） | 有向行列木定理との照合 |

結果は `docs/benchmarks.md` の M7 節に記録する。
**性能目標は置かない**。v0.7 の目的は機能であり、有向で無向と同等の速度が出る保証は無い
（変数が倍なので当然遅くなる）。「どれくらい遅くなるか」を測って記録することが目的。

---

## 5. 破壊的変更の有無

無い。`Graph` / `Edge` / `GraphSet` / 既存スペックのシグネチャは一切変えない。
M7-2 のリファクタは `FrontierManager` の**コンストラクタ追加**のみで、既存のものは残す。
`EdgeOrdering` は internal なので自由に変えられる。

v1.0（M8）の API 凍結の前にこれを済ませることで、有向対応を後から入れたときに
起きるはずだった `FrontierManager` の破壊的変更を回避する。
