# Graphillion 対応表（暫定版）

Python [Graphillion](https://github.com/graphillion/graphillion) から ZDD.Net に移ってくる利用者向けの、
API 対応表の暫定版（v0.6、issue #151）。**本番の移行ガイドは M8-4**（v1.0 の API 凍結後）で書く——
それまでは命名・シグネチャが変わり得るので、ここは「だいたいこう書き換えられる」という早見表にとどめる。

- 対象バージョン: v0.6
- Graphillion 側の関数名・引数名は、実際に `pip install graphillion`（`graphillion==2.1`、Python 3.11）
  して確認したもの（[docs/graphillion-io.md](graphillion-io.md) §1 と同じ手順）
- 「族」は Graphillion の `GraphSet` / `setset`、ZDD.Net の `GraphSet` / `SetSet<T>` のどちらも指す

---

## 1. 生成関数（`GraphSet` のコンストラクタ相当）

| Graphillion | ZDD.Net | 備考 |
|---|---|---|
| `GraphSet.graphs(...)` | `GraphSet.Graphs(graph, constraints)` | 単一入口。下の §2 を参照（M6-15） |
| `GraphSet.paths(s, t)` | `GraphSet.Paths(graph, from: s, to: t)` | |
| `GraphSet.cycles()` | `GraphSet.Cycles(graph, single: true)` | `single: false` で互いに素なサイクルの和 |
| `GraphSet.trees(is_spanning=True)` | `GraphSet.Trees(graph)` | 全域木 |
| `GraphSet.forests(roots)` | `GraphSet.Forests(graph, components:)` | 成分数を指定する形（`roots` の集合そのものではない） |
| `GraphSet.matchings()` | `GraphSet.Matchings(graph)` | `perfect: true` で完全マッチングのみ |
| `GraphSet.cliques(k)` | `GraphSet.Cliques(graph)` | サイズ固定は事後 `LenEquals(k)` |
| `GraphSet.independent_sets()` | `GraphSet.IndependentSets(graph)` | |
| `GraphSet.vertex_covers()` | `GraphSet.VertexCovers(graph)` | `SetSet<int>` で返る（M6-10） |
| `GraphSet.dominating_sets()` | `GraphSet.DominatingSets(graph)` | `SetSet<int>` で返る（M6-10） |
| `GraphSet.graph_partitions(num_comp_lb, num_comp_ub, ...)` | `GraphSet.Partitions(graph, k, minBlockSize, maxBlockSize)` | |
| `GraphSet.balanced_partitions(...)` | `GraphSet.BalancedPartitions(graph, k, tolerance:)` | 境界計算は `Partitions` の糖衣（M6-10） |
| `GraphSet.colorings(k)` | `GraphSet.Colorings(graph, k)` | `SetSet<(int Vertex, int Color)>` で返り、`(頂点, 色)` を直接読める（M6-10） |
| `GraphSet.regular_graphs(k)` | `GraphSet.RegularGraphs(graph, k)` | `DegreeConstrained(graph, lo: k, hi: k)` の別名（M6-11） |
| `GraphSet.degree_distribution_graphs(deg_dist)` | `GraphSet.DegreeDistributions(graph, counts)` | `counts[d]` = 次数 `d` の頂点数（M6-11） |
| `GraphSet.induced_graphs()` | `GraphSet.InducedSubgraphs(graph)` | 連結性は要求しない（M6-12） |
| `GraphSet.bicliques(a, b)` | `GraphSet.Bicliques(graph, a, b)` | サイズ無指定版 `GraphSet.Bicliques(graph)` もある（M6-13） |
| `GraphSet.connected_components(terminals)` | `GraphSet.ConnectedSubgraphs(graph, terminals)` | terminals が全て同じ連結成分（M6-9） |
| `GraphSet.steiner_subgraphs(terminals)` | `GraphSet.SteinerTrees(graph, terminals)` | terminal 以外に葉を作らない木（M6-9） |
| `GraphSet.graphs(vertex_groups=[[...], [...]])` | `GraphSet.VertexGroups(graph, groups)` | 単独メソッドとしても露出（M6-14） |
| `GraphSet.min_cuts(s, t)` / カット全般 | `GraphSet.Cuts(graph, s, t, minimalOnly:)` | `minimalOnly: true` で極小カットに絞れる（M6-9） |
| — | `GraphSet.DegreeConstrained(graph, lo, hi)` | 汎用の次数制約（辺被覆・k 正則の元になっている一般形、M6-9） |
| — | `GraphSet.EdgeCovers(graph)` | `DegreeConstrained(graph, lo: 1, hi: graph.EdgeCount)` の別名（M6-9） |
| `GraphSet.knapsacks(weights, capacity)`（あるいは相当のコード） | `GraphSet.Knapsacks(graph, weights, capacity)` | M6-9 |

## 2. 統合ビルダ `graphs()` ↔ `GraphSet.Graphs` / `Where`

```python
# Graphillion
gs = GraphSet.graphs(
    degree_constraints={0: range(0, 3), 3: range(1, 2)},
    num_edges=range(4, 9),
    num_comps=1,
    no_loop=True,
    vertex_groups=[[0, 4], [2, 6]],
)
```

```csharp
// ZDD.Net（M6-15）
var constraints = new GraphConstraints
{
    DegreeConstraints = new Dictionary<int, (int Lo, int Hi)> { [0] = (0, 2), [3] = (1, 1) },
    EdgeCount = (Min: 4, Max: 8),
    ComponentCount = 1,
    NoLoop = true,
    VertexGroups = new[] { new[] { 0, 4 }, new[] { 2, 6 } },
};

GraphSet gs = GraphSet.Graphs(grid, constraints);
```

**range の端点に注意**: Python の `range(a, b)` は `b` を含まない半開区間だが、ZDD.Net の
`(Min, Max)` / `(Lo, Hi)` はどちらも**閉区間**（両端を含む）。上の例で `range(0, 3)` →
`(0, 2)`、`range(4, 9)` → `(4, 8)` と、上限を 1 減らして移し替えている。

既存の族を絞り込みたいとき（Graphillion の `GraphSet.graphs(graphset=gs, ...)` に相当）は
`gs.Where(constraints)` を使う——事後 `Intersect` と結果は同じだが、母集合を全部作ってから絞る
より中間 ZDD が小さい。

`ComponentCount`（Graphillion の `num_comps`）は**孤立頂点を数えない**——`ForestSpec` の
「森の木の本数」（孤立頂点も 1 本の木として数える）とは定義が違うので注意（詳細は
[docs/design/m6-api-expansion.md](design/m6-api-expansion.md) §4.3）。

## 3. 族の変換・移送

| Graphillion | ZDD.Net | 備考 |
|---|---|---|
| （暗黙のユニバース拡張。制限なし） | `SetUniverse<T>.Extend(additionalElements)` | ZDD.Net は要素追加ごとに新しいユニバース/マネージャを作る（M6-6） |
| — | `SetSet<T>.ToUniverse(target)` | 別々に作った 2 つの族を同じユニバースに揃えて合成できるようにする（M6-6） |
| `GraphSet.converters`（辺順序の付け替え相当の処理） | `GraphSet.ToEdgeOrder(target)` | `Optimize()` した辺順序で構築し、元の辺順序に戻して読む（M6-6） |
| （`setset` は 1 つの universe に固定） | `Zdd.MapItems` / `MapItemsTo` / `TransferTo` | item の張り替え・別マネージャへの複製（M6-4、M6-5） |

## 4. 族操作・フィルタ

| Graphillion | ZDD.Net | 備考 |
|---|---|---|
| `gs.union(other)` / `gs | other` | `F.Union(G)` / `F \| G` | |
| `gs.intersection(other)` / `gs & other` | `F.Intersect(G)` / `F & G` | |
| `gs.difference(other)` / `gs - other` | `F.Difference(G)` / `F - G` | |
| `gs.symmetric_difference(other)` | `F.SymmetricDifference(G)` / `F ^ G` | |
| `gs.complement()` | `F.Complement()` | ZDD.Net には部分ユニバース版 `F.ComplementWithin(items)` もある（M6-1） |
| `gs.cost_le(costs, cost)` | `gs.CostAtMost(costs, bound)` | `cost_ge`/`cost_eq` は `CostAtLeast`/`CostEquals`（M6-8） |
| `gs.larger_than(size)` / `smaller_than(size)` | `gs.Larger(size)` / `gs.Smaller(size)` | |
| `gs.len(size)` | `gs.LenEquals(size)` | |
| `gs.including(edge_or_vertex)` / `gs.excluding(...)` | `gs.Including(edge)` / `gs.Excluding(edge)` | |
| `gs.add_some_element()` | `F.AddSomeItem()` / `gs.AddSomeItem()` | 対象を絞る `items` 版もある（M6-7） |
| `gs.remove_some_element()` | `F.RemoveSomeItem()` / `gs.RemoveSomeItem()` | 同上 |
| `gs.remove_add_some_elements()` | `F.RemoveAddSomeItems()` / `gs.RemoveAddSomeItems()` | `O(|items|²)` になる点は Graphillion と同じ |
| `gs.len()` / `gs.__len__()` | `F.Count` | `BigInteger`。近似が欲しいなら `F.CountApprox` |
| `gs.rand_iter()` | `gs.RandIter(random)` | 遅延列挙。1 つだけ欲しいなら `F.Sample(random)` |
| `gs.choice()` | `F.Sample(random)` | |
| `gs.min_iter(weights)` / `max_iter(weights)` | `gs.MinIter(weights)` / `gs.MaxIter(weights)` | 遅延列挙 |
| `gs.probability(probabilities)` | `F.Probability(probabilities)` | |
| `gs.dump(fp)` / `dumps()` / `load(fp)` / `loads(s)` | `ZDD.Net.Io.GraphillionTextFormat` | 相互運用済み。詳細は [docs/graphillion-io.md](graphillion-io.md)（M5-2） |

## 5. 未対応・対応が薄い項目

- **有向グラフ**（`networkx.DiGraph` を渡した場合の Graphillion の挙動）は ZDD.Net にまだ無い。
  M7「有向グラフ対応 (v0.7)」で追加予定（[docs/design/m7-directed-graphs.md](design/m7-directed-graphs.md)）
- Graphillion の一部の便利関数（`GraphSet.omit`、`show_messages` などデバッグ・可視化寄りのもの）は
  対応する ZDD.Net API を意図的に用意していない。ZDD.Net 側は `ToDot()`/`DotOptions` で代替する
- 本表は「大まかにどう書き換えるか」の早見表であり、**引数の細かい意味の違いまでは保証しない**——
  実際の移行時は必ず該当 API の XML doc（`docs/api-guide.md` / `docs/frontier-guide.md`）を確認すること

## 6. さらに詳しく

- [docs/design/m6-api-expansion.md](design/m6-api-expansion.md) — 本表のもとになった v0.6 設計書
- [docs/frontier-guide.md](frontier-guide.md) §9 — `GraphSet` / `SetSet<T>` の使い方
- [docs/api-guide.md](api-guide.md) — `Zdd` / `ZddManager` の使い方
- [docs/graphillion-io.md](graphillion-io.md) — Graphillion 互換のシリアライズ形式
- [CHANGELOG.md](https://github.com/wix-diesel/ZDD.Net/blob/main/CHANGELOG.md#060---2026-09-05) — v0.6 で追加した API の一覧
