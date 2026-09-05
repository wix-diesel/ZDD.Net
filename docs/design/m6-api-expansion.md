# M6: API 拡充と相互運用（v0.6）設計書

- ドキュメント版数: v1 (2026-09-04)
- 対応するタスク表: [docs/ROADMAP.md](../ROADMAP.md) の M6 節
- 上位計画: [docs/PLAN.md](../PLAN.md)

> **追記 (2026-09-05、M6-16)**: M6-1〜M6-15 の実装完了後に本書と実装の食い違いを確認した。
> §5.2（`RegularGraphSpec` という型は作らず `DegreeConstraintSpec` の別名で済んだ）と
> §5.3（`BicliqueSpec` の状態は単純な 3 値ではなくパリティ付き union-find になった）の
> 2 箇所を実態に合わせて更新した。それ以外は設計どおりに実装されている。

## 0. なぜこのマイルストーンを v1.0 の前に挟むか

v0.5 までで Core・Frontier・Graphs の 3 レイヤと 22 個の組み込みスペックが揃った。
一方で、他ライブラリ（Graphillion / TdZdd / SAPPOROBDD / CUDD+EXTRA）と突き合わせると、
**「エンジンはあるのに入口が無い」**類の欠落が集中して残っている。

| 欠落の種類 | 具体例 | 深刻度 |
|---|---|---|
| 決めたのに実装しなかった API | `ComplementWithin` (B8) / `EnumerateInto` (B9) / `TryBuild` (B11) | 中。`docs/OPEN-QUESTIONS.md` に暫定案として明記済み |
| 実装済みスペックが高レベル API から触れない | `Specs/` の 22 個中、`GraphSet` から使えるのは 9 個だけ | **大**。M4 の成果物がユーザに届いていない |
| 族を別のユニバースへ移せない | `SetSet<T>` は `ReferenceEquals(Universe, ...)` で一致を要求、`ZddManager` は変数数固定 (B7) | **大**。部分問題を組み合わせる使い方が原理的に不可能 |
| Graphillion にあるスペックが無い | 頂点誘導部分グラフ・k 正則・biclique・頂点グループ連結・統合ビルダ `graphs()` | 中 |

v1.0 で公開 API を凍結する（M8-1）以上、**凍結後に足すと破壊的変更になるもの**を先に入れる。
特に「ユニバースをまたぐ移送」は、後から入れると `SetSet<T>` / `GraphSet` のコンストラクタと
不変条件に手を入れることになるため、凍結前に済ませる必要がある。

有向グラフ対応は独立した規模があるため M7 に分離した（[m7-directed-graphs.md](m7-directed-graphs.md)）。

---

## 1. Core: 決定済み API の穴埋め

### 1.1 `ComplementWithin` — 部分ユニバースでの補集合

`Complement()` はマネージャの全 `VariableCount` 変数に対する `2^N \ f` を返す（B8 の決定）。
実際に欲しいのは「注目している要素だけを動かした補集合」であることが多い。

```csharp
public readonly struct Zdd
{
    /// <summary>2^items \ f。items に無い要素は結果に一切現れない。</summary>
    public Zdd ComplementWithin(params ReadOnlySpan<int> items);
}

public sealed class ZddManager
{
    /// <summary>指定要素だけからなる冪集合 2^items。</summary>
    public Zdd PowerSetOf(params ReadOnlySpan<int> items);
}
```

**実装**: `PowerSetOf` は葉側（大きい item = 小さい level）から根側へ 1 パスで組む。
item を降順（level 昇順）に走査し、`n = 1 (Base)` から `n = GetNode(level, lo: n, hi: n)` を繰り返す。
`hi == n != 0` なのでゼロサプレス規則には触れず、k 要素なら k ノード・O(k) で作れる。
`ComplementWithin` はその結果との `Difference`。

**意味論の決定**:
- `items` に重複があってもよい（正規化して扱う）。空なら `2^∅ = { ∅ }`（`Base`）を返す。
- `f` が `items` の外側の要素を含む集合を持っていても例外にしない。
  `2^items \ f` の定義上、そういう集合は最初から `2^items` に無いので単に無視される。
  この挙動は XML doc に明記する（「support の検査はしない」）。
- 既存の `Complement()` は `ComplementWithin(全変数)` と一致する。これを回帰テストにする。

**なぜ `PowerSetOf` も公開するか**: `zdd.Subset(...)` や `Meet` の右辺として単体で有用で、
`ComplementWithin` の実装がそのまま使えるため。内部にしまう理由が無い。

### 1.2 `EnumerateInto` — アロケーションなしの列挙

現状の `Sets()` は `IEnumerable<int[]>` で、1 集合ごとに `new int[]` する（B9 の (a)）。
数百万集合を舐める用途では GC 圧が支配的になる。B9 の暫定案どおり (b) も提供する。

```csharp
public readonly struct Zdd
{
    /// <summary>集合をユーザ提供のバッファに書き込みながら列挙する。バッファは使い回される。</summary>
    public SetSpanEnumerator EnumerateInto(Span<int> buffer, ZddEnumerationOrder order = ZddEnumerationOrder.Default);

    /// <summary>この族に含まれる集合の最大要素数。EnumerateInto に必要なバッファ長。</summary>
    public int MaxSetSize { get; }
}

/// <summary>foreach でそのまま回せる ref struct 列挙子。</summary>
public ref struct SetSpanEnumerator
{
    public ReadOnlySpan<int> Current { get; }
    public bool MoveNext();
    public SetSpanEnumerator GetEnumerator();   // foreach パターンを満たす
}
```

**設計判断**:
- `IEnumerable<T>` にはしない。`ref struct` なので LINQ には乗らないが、それは意図した制約
  （バッファ使い回しを LINQ に渡すと壊れる、というのが B9 で (a) を既定にした理由そのもの）。
  `foreach` はパターンベースなので `GetEnumerator()` を生やせば動く。
- `Current` は `ReadOnlySpan<int>` で、`MoveNext()` のたびに内容が上書きされる。
  「保持したいならコピーせよ」を XML doc の先頭に書く。
- バッファ長が `MaxSetSize` 未満なら `ArgumentException`。切り詰めて黙って壊れるのが最悪。
- `MaxSetSize` は `IDdEval<int>` の新しい実装（`MaxSetSizeEval`）。
  終端 ⊤ で 0、ノードで `max(lo, hi + 1)`、⊥ は `int.MinValue` 相当の番兵。
  既存のメモ化評価基盤に乗るので実装は 30 行程度。空族は 0 を返す（バッファ 0 で正しく回る）。
- 実装本体は `Sets()` のイテレータ（`yield return`）を、明示スタックを持つ手書き状態機械に
  書き直したもの。`Sets()` 側は変更しない（後方互換）。共通部分は `SetEnumeration` に寄せる。

**受け入れ条件**: 変数 16 以下の全網羅で `Sets()` と要素・順序が完全一致すること。
`EnumerateInto` のループが 0 アロケーションであること（既存の `OperationCacheTests` と
同じ手法でアロケーションを実測する）。

### 1.3 `TryBuild` — 上限超過を例外にしない構築

現状は `BuildLimitExceededException` のみ。「上限を決めて、超えたら別の辺順序を試す」という
探索的な使い方では例外が制御フローになってしまう。B11 の暫定案どおり `Try` 版を足す。

```csharp
public static class FrontierBuilder
{
    public static bool TryBuild<TSpec, TState>(ZddManager manager, TSpec spec, BuildOptions options, out Zdd result)
        where TSpec : struct, IDdSpec<TState>;

    public static bool TryBuild<TSpec>(ZddManager manager, TSpec spec, BuildOptions options, out Zdd result)
        where TSpec : struct, IArrayDdSpec;
}
```

**意味論の決定**:
- `false` を返すのは **`BuildLimit` 超過のときだけ**。`MaxNodeCount` / `MaxFrontierSize` の両方。
- `CancellationToken` によるキャンセルは **例外のまま**（`OperationCanceledException`）。
  .NET の慣行では `Try` パターンはキャンセルを飲み込まない。
- スペック自身が投げた例外も飲み込まない。
- `false` のとき `result` は `default(Zdd)`、かつ **マネージャの状態は呼び出し前と変わらない**。
  トップダウン展開中は一時ノード表にしか書かないので、ボトムアップ削減に到達する前に
  中断すれば一意化表は無傷。この不変条件をテストで担保する（`NodeCount` が不変）。
- `options` は必須引数（`null` 不可）。上限を設定しない `TryBuild` は意味が無いため。

---

## 2. Core: 変数写像とマネージャ間転送

CUDD の `Cudd_bddPermute` / `Cudd_bddTransfer` に相当する機能が無く、以下ができない。

- 変数の張り替え（辺順序を変えた `Graph` で組み直した族を、元の順序で解釈する）
- 別の `ZddManager` へのコピー（現状はバイナリ形式で保存して読み直すしかない）
- 別々に作った 2 つの `SetSet<T>` の合成（ユニバース一致を要求するため不可能）

### 2.1 API

```csharp
public readonly struct Zdd
{
    /// <summary>同じマネージャ内で item を itemMap[item] に張り替える。</summary>
    public Zdd MapItems(ReadOnlySpan<int> itemMap);

    /// <summary>target マネージャ上に、item を itemMap[item] に張り替えて複製する。</summary>
    public Zdd MapItemsTo(ZddManager target, ReadOnlySpan<int> itemMap);

    /// <summary>target マネージャ上に、item をそのままに複製する。</summary>
    public Zdd TransferTo(ZddManager target);
}
```

`itemMap` は「旧 item → 新 item」の**全域かつ単射**な写像。長さは `Manager.VariableCount`。

### 2.2 意味論の決定（B17 として OPEN-QUESTIONS に追加）

- **単射のみ許可**。`itemMap` に重複があれば `ArgumentException`。
  非単射写像は「2 つの要素を同一視する」＝射影・存在量化であり、集合の族としての意味が
  変わる（`{a},{b}` が `{x},{x}` になり多重度が失われる）。必要なら別 API として後日足す。
- `itemMap[i]` が `target.VariableCount` の範囲外なら `ArgumentOutOfRangeException`。
- **support 外の要素の写像先は検査しない**。`f` に現れない item の行が何であっても結果は同じ。
  これにより「部分的にしか埋めない写像」を `-1` などの番兵で書かずに済む。
  ただし `-1` 自体は範囲外なので、support に現れた瞬間に例外になる（安全側）。
- `TransferTo(target)` は `MapItemsTo(target, 恒等写像)` と定義する。
  `target.VariableCount >= this.Manager.VariableCount` を要求。
  **これが B7（変数数は固定）の実質的な回避策になる**: 変数を増やしたければ、
  大きいマネージャを新しく作って `TransferTo` する。

### 2.3 実装: 2 つの経路

`level = VariableCount - item` なので、item の大小関係がそのまま level の順序を決める。

**(a) 順序保存の高速経路（M6-4）**

`itemMap` が support 上で狭義単調増加なら、親子の level 順序が保たれるので
**ボトムアップ 1 パスの再構築**でよい。ノード id をキーにメモ化し、
`target.Table.GetNode(newLevel, lo', hi')` を葉側から呼ぶだけ。O(ノード数)。

同一マネージャかつ恒等写像なら、そのまま自分を返す（コピーもしない）。

**(b) 一般置換の経路（M6-5）**

順序が保たれない場合、ノードをそのまま作り直すことはできない（子の level が親より大きくなる）。
ZDD の再帰的定義

```
f = f0 ∪ (f1 × {v})        v = ノードの item, f0 = lo 側, f1 = hi 側
map(f) = map(f0) ∪ Change(map(f1), σ(v))
```

を、ノード id をキーにメモ化しながら**後行順の明示スタック**で回す（§4.5 の再帰禁止方針に従う）。

*正当性*: `f1` の部分木に現れる item はすべて `v` より大きい。`σ` は単射なので
`σ(v)` は `map(f1)` の support に現れない。したがって `Change(map(f1), σ(v))` は
「全ての集合に `σ(v)` を足す」として正しく振る舞う（反転ではなく追加になる）。

*計算量*: ノード数 × (`Union` + `Change`) 回。線形ではないが指数ではない。
「順序を保つ写像なら O(ノード数)、そうでなければ再構築コストがかかる」ことを XML doc に明記する。

*マネージャ間*: `Union` / `Change` を `target` 上で呼べば (b) がそのまま転送になる。
(a) も `GetNode` を `target` 側に向けるだけ。したがって同一マネージャ版は
`MapItemsTo(this.Manager, map)` に委譲する。

**受け入れ条件**: 変数 12 以下の総当たりで、写像後の族が「各集合の要素を σ で写した族」と一致。
ランダム置換でのプロパティテスト。`MapItems(σ).MapItems(σ⁻¹) == f`（往復）。
順序保存経路と一般経路の結果が完全一致すること（同じ入力を両経路に通して比較する）。

### 2.4 高レベル API への波及（M6-6）

```csharp
public sealed class SetUniverse<T>
{
    /// <summary>要素を追加した新しいユニバースを返す（元は変更しない）。</summary>
    public SetUniverse<T> Extend(IEnumerable<T> additionalElements);
}

public sealed class SetSet<T>
{
    /// <summary>この族を target ユニバース上に移す。this のユニバースの要素が全て target に含まれること。</summary>
    public SetSet<T> ToUniverse(SetUniverse<T> target);
}

public sealed class GraphSet
{
    /// <summary>辺順序だけが違う同じグラフへ族を移す（Optimize 後の族を元の順序で解釈する等）。</summary>
    public GraphSet ToEdgeOrder(Graph target);
}
```

- `Extend` は**新しい `ZddManager` を作る**（B7 のとおり変数数は固定なので、既存を広げられない）。
  元のユニバースと族はそのまま生き続ける。
- `ToUniverse` は `Universe.Elements` の各要素を `target.IndexOf` で引いて `itemMap` を作り、
  `Zdd.MapItemsTo(target.Manager, itemMap)` を呼ぶだけ。要素が `target` に無ければ
  `ArgumentException`（どの要素が足りないかをメッセージに出す）。
- `Union` などの二項演算は**引き続きユニバース一致を要求する**（暗黙昇格はしない）。
  暗黙にマネージャを新規作成するとメモリ使用量が予測不能になるため。
  「違うユニバースの族を合成したい」利用者には、例外メッセージで `ToUniverse` を案内する。
- `GraphSet.ToEdgeOrder` は `Graph.SourceOrder`（`WithEdgeOrder` が残す辺の対応）を使って
  `itemMap` を作る。`Optimize()` した順序で構築し、結果を元の順序で扱う、という
  実運用で最も多いパターンを 1 行にする。

---

## 3. Core: Graphillion 由来の族操作

### 3.1 1 要素変種（M6-7）

Graphillion の `add_some_element` / `remove_some_element` / `remove_add_some_elements`。
局所探索や「1 手違いの解」を数える用途で使う。

```csharp
public readonly struct Zdd
{
    /// <summary>各集合に、含まれていない要素を 1 つ足した族。</summary>
    public Zdd AddSomeItem();
    public Zdd AddSomeItem(params ReadOnlySpan<int> items);

    /// <summary>各集合から、含まれている要素を 1 つ除いた族。</summary>
    public Zdd RemoveSomeItem();
    public Zdd RemoveSomeItem(params ReadOnlySpan<int> items);

    /// <summary>各集合から 1 要素を除き、別の 1 要素を足した族。</summary>
    public Zdd RemoveAddSomeItems();
    public Zdd RemoveAddSomeItems(params ReadOnlySpan<int> items);
}
```

**実装**: 新しい演算を足す必要は無い。既存の単項演算の合成で書ける。

```
RemoveSomeItem(f) = ⋃_{e ∈ items} OnSet(f, e)              // OnSet = Subset1 は e を取り除いて返す
AddSomeItem(f)    = ⋃_{e ∈ items} Change(OffSet(f, e), e)   // OffSet した族に e は現れないので Change は「追加」
```

**計算量の正直な記載**: 前 2 者は `|items|` 回の演算。`RemoveAddSomeItems` は
`⋃_{e≠e'} Change(OffSet(OnSet(f,e), e'), e')` で **O(|items|²) 回**の族演算になる
（Graphillion の実装も同じオーダー）。数千要素のユニバースでそのまま呼ぶと現実的でないため、

- 既定の引数なし版は `Manager.VariableCount` 全体を使う（小さいユニバース向け）
- `items` 版で対象を絞れる（局所探索では「動かしてよい要素」だけを渡すのが普通）
- XML doc とガイドに計算量を明記する

とする。単一パスの DP（メモ化キーに「除去済み/追加済み」の 2 ビットを持たせ、
レベルの飛びを「飛ばした要素のどれか 1 つを足す」チェーンとして展開する）に置き換えれば
線形に近づけられるが、レベル飛びの扱いが煩雑なので **v0.6 のスコープ外**とし、
設計書のこの段落を将来の最適化の出発点として残す。

**受け入れ条件**: 変数 12 以下の総当たり照合（素朴なビットマスク実装と一致）。

### 3.2 コストフィルタ（M6-8）

Graphillion の `cost_le`。既存の族に対して「重み合計が閾値以下の集合だけ」を残す。

```csharp
public readonly struct Zdd
{
    public Zdd CostAtMost(ReadOnlySpan<long> costs, long bound);
    public Zdd CostAtLeast(ReadOnlySpan<long> costs, long bound);
    public Zdd CostEquals(ReadOnlySpan<long> costs, long value);
}

public sealed class GraphSet
{
    public GraphSet CostAtMost(Func<Edge, long> cost, long bound);
    // CostAtLeast / CostEquals も同様
}

public sealed class SetSet<T>
{
    public SetSet<T> CostAtMost(IReadOnlyDictionary<T, long> costs, long bound);
    // 同上
}
```

**実装**: 新規のアルゴリズムは不要。`zdd.Subset(new LinearConstraintSpec(costs, op, bound))`
（M3-5 の ZddSubsetting）そのもの。**事後フィルタではなくフロンティア走査中に適用される**ので、
中間結果が膨らまない。

**設計判断**: 名前は Graphillion の `cost_le` ではなく .NET 命名規約の
`CostAtMost` / `CostAtLeast` / `CostEquals` にする（§8 の「Graphillion の語彙を
.NET 命名規約に直して踏襲」の方針どおり）。移行ガイド（M8-4）に対応表を載せる。
係数型は `long` に固定する（`LinearConstraintSpec` が `int[]` 係数・`long` 閾値のため。
`double` 係数は丸めの扱いが自明でないので入れない）。

---

## 4. Graphs: 高レベル API のカバー率

`Specs/` には 22 個のスペックがあるが、`GraphSet` の静的ファクトリから使えるのは 9 個。
**M4 で実装したスペックが高レベル API から一切触れない**のが v0.5 時点の最大の穴。

### 4.1 露出①: 辺の族（M6-9）

```csharp
public sealed class GraphSet
{
    public static GraphSet ConnectedSubgraphs(Graph graph, IEnumerable<int> terminals);
    public static GraphSet SteinerTrees(Graph graph, IEnumerable<int> terminals);
    public static GraphSet Cuts(Graph graph, int s, int t, bool minimalOnly = false);
    public static GraphSet DegreeConstrained(Graph graph, int[] lo, int[] hi);
    public static GraphSet DegreeConstrained(Graph graph, int lo, int hi);
    public static GraphSet EdgeCovers(Graph graph);
    public static GraphSet Knapsacks(Graph graph, int[] weights, long capacity);
}
```

いずれも `Generate<TSpec>(graph, spec)` に既存スペックを渡すだけ（1 メソッド 5 行前後）。
`EdgeCovers` は `DegreeConstraintSpec(graph, lo: 1, hi: graph.EdgeCount)` の別名。
**PLAN §7.2 の表に `EdgeCoverSpec` が載っているのに未実装だった**件は、
専用スペックを新設せずこの別名で解決する（次数制約の特殊形にすぎないため）。

### 4.2 露出②: 頂点の族と彩色（M6-10）

```csharp
public sealed class GraphSet
{
    public static SetSet<int> VertexCovers(Graph graph);
    public static SetSet<int> DominatingSets(Graph graph);
    public static GraphSet Partitions(Graph graph, int k, int minBlockSize, int maxBlockSize);
    public static GraphSet BalancedPartitions(Graph graph, int k, double tolerance = 0.0);
    public static SetSet<(int Vertex, int Color)> Colorings(Graph graph, int k, bool representativesOnly = false);
}
```

- `VertexCovers` / `DominatingSets` は既存の `GenerateVertexFamily` に流すだけ。
- `BalancedPartitions` は `Partitions` の糖衣。`tolerance` から
  `minBlockSize = floor(n/k · (1-tolerance))`、`maxBlockSize = ceil(n/k · (1+tolerance))` を計算する
  （Graphillion の `balanced_partitions` に対応）。境界計算は単体テストで固める。
- `Colorings` の戻り値型が特殊なのは `ColoringSpec` が **頂点 × 色を変数にする**ため
  （変数 index = `v * k + c`）。`SetSet<int>` で返すと利用者が自分で復号する羽目になるので、
  `SetUniverse<(int Vertex, int Color)>` を組んで返す。これで
  `foreach (var coloring in colorings) foreach (var (v, c) in coloring)` が自然に書ける。

### 4.3 統合ビルダ `Graphs()`（M6-14 / M6-15）

Graphillion の `graphs(degree_constraints=, num_edges=, num_comps=, no_loop=, graphset=, ...)`
に相当する「入口 1 つ」を用意する。現状も `spec.And(other)` で同じことは書けるが、
Graphillion から移ってくる利用者にとっては入口が分散しているのが学習コストになる。

```csharp
public sealed class GraphConstraints
{
    /// <summary>頂点ごとの次数の下限・上限。未指定の頂点は制約なし。</summary>
    public IReadOnlyDictionary<int, (int Lo, int Hi)>? DegreeConstraints { get; set; }

    /// <summary>辺数の範囲。</summary>
    public (int Min, int Max)? EdgeCount { get; set; }

    /// <summary>連結成分数（孤立頂点は数えない）。</summary>
    public int? ComponentCount { get; set; }

    /// <summary>閉路を含まないことを要求する。</summary>
    public bool NoLoop { get; set; }

    /// <summary>同じグループの頂点は互いに連結、違うグループの頂点は非連結であることを要求する。</summary>
    public IReadOnlyList<IReadOnlyList<int>>? VertexGroups { get; set; }

    /// <summary>線形制約（重み合計の範囲）。</summary>
    public IReadOnlyList<(int[] Coefficients, LinearConstraintOperator Op, long Bound)>? LinearConstraints { get; set; }
}

public sealed class GraphSet
{
    public static GraphSet Graphs(Graph graph, GraphConstraints constraints);

    /// <summary>既存の族を母集合として制約を課す（Graphillion の graphset= 引数に相当）。</summary>
    public GraphSet Where(GraphConstraints constraints);
}
```

**実装**: `IErasedGraphSpec` を返すファクトリを制約ごとに用意し、`AndErasedSpec` で畳み込む。
`GraphSet` の `Including` / `Excluding` / `Larger` / `Smaller` が既に使っている機構
（型消去したスペックの連鎖）にそのまま乗るので、新しい合成基盤は要らない。
母集合つきの `Where` は `this.Zdd.Subset(合成スペック)` に落とす。

**`VertexGroups` は独立した PR（M6-14）にする**。「同じグループの頂点は連結、違うグループの
頂点は非連結」は `GraphPartitionSpec` と `ConnectedSubgraphSpec` のどちらとも違う制約で、
mate/comp 配列に「その成分がどのグループに属するか（未定を含む）」を持たせた新規スペック
（`VertexGroupSpec`）が要る。単独で 300 行規模になるため統合ビルダとは分ける。

**`ComponentCount` の定義**: Graphillion の `num_comps` に合わせ、**孤立頂点は成分に数えない**
（辺を 1 本も持たない頂点は無視する）。`ForestSpec(components:)` が既に同じ定義を使っているので、
実装はそれに寄せる。この定義は XML doc に明記する（直感と食い違いうるため）。

---

## 5. Graphs: 新規スペック

### 5.1 `InducedSubgraphSpec`（M6-12）

Graphillion の `induced_graphs`。**頂点集合 S を選んだとき、S 内の全ての辺がちょうど選ばれている**
辺集合の族。「S 内に辺があるのに選ばない」を禁じる点が普通の部分グラフと違う。

**状態**: フロンティア頂点ごとに 3 値（`Unknown` / `In` / `Out`）。

- 辺 `(u,v)` を **選ぶ** → `u`, `v` をともに `In` に確定。既に `Out` なら ⊥。
- 辺 `(u,v)` を **選ばない** → 「`u` と `v` が両方 `In`」は禁止。すなわち
  少なくとも一方が `Out` に確定する必要がある。片方が既に `In` なら他方は `Out` に確定、
  両方 `Unknown` なら **両方の可能性を残せない**（状態が分岐してしまう）。
  → ここは「`u` が `Out`」「`u` が `In` かつ `v` が `Out`」の 2 状態に分けるのではなく、
  **頂点を忘れる時点で判定を遅らせる**方式を採る: `Unknown` のまま持ち越し、
  頂点がフロンティアから出る（`ForgottenVertices`）時に `Unknown` なら `Out` と確定する。
  「選ばない辺の両端がともに `In` になった」時点で ⊥ にすれば、遅延しても正しい。
- 頂点を忘れるとき、`In` の頂点は以降の辺に関与しないので単に破棄してよい。

孤立頂点（辺を 1 本も選ばれない頂点）は `Out` 扱いになるため、族は「辺集合」として一意。
Graphillion 同様、**連結性は要求しない**（連結な誘導部分グラフが欲しければ
`ConnectedSubgraphs` と `And` する）。

**受け入れ条件**: 頂点 8 以下の総当たり（全ての頂点部分集合 S について、S の誘導辺集合を
列挙した族と一致すること）。

### 5.2 次数系の拡充（M6-11）

```csharp
public readonly struct DegreeDistributionSpec : IArrayDdSpec  // 次数 d の頂点がちょうど n_d 個
```

- `k` 正則グラフには専用スペックを新設しない。`DegreeConstraintSpec(graph, k, k)` の別名で足りる
  ので、`GraphSet.RegularGraphs(graph, k)` としてのみ露出する（実装後に確定: 当初は
  `RegularGraphSpec` という新規型も検討したが、`DegreeConstraintSpec` を直接使うだけで済んだ）。
- `DegreeDistributionSpec(graph, int[] counts)` は本物の新規スペック。
  状態 = フロンティア頂点ごとの現在次数 ＋ **確定済み頂点の次数ヒストグラム**。
  ヒストグラムは状態サイズを押し上げる（最大次数 × 頂点数の組合せ）ため、
  `counts` の残数を減らしていく形で持つ（残数が負になったら ⊥）。これで状態は
  「フロンティア頂点の次数 + 残ヒストグラム」に収まる。
  Graphillion の `degree_distribution_graphs` に対応。

### 5.3 `BicliqueSpec`（M6-13）

Graphillion の `bicliques`。完全二部部分グラフ。「両側の全ての頂点対の間に辺があり、それが全て
選ばれている」ことを要求する。

**状態（実装時に確定）**: 単純な「頂点ごとの `SideA`/`SideB`/`Unused` の 3 値」という全体で
1 枚のラベルでは足りない——biclique の両側というラベルはグループ（連結成分）ごとにしか
決まらず、フロンティア上でまだ結合していない複数のグループが同時に育つ間、どちらの側を
「0」と呼ぶかはグループごとに独立な、辺の処理順に依存する任意の選択になる。そこで
`BicliqueVertexState` は頂点ごとに「所属グループ」と「そのグループ内での相対サイド」を持つ
パリティ付き union-find にした。2 つのグループが辺で結合するとき、それぞれの相対サイドの
対応関係（同じ側と見なすか逆側と見なすか）はその場で決まる。この点は `InducedSubgraphSpec`
（M6-12）の判定遅延の考え方は引き継ぐが、状態の構造そのものは 3 値では収まらない。

`(a, b)` サイズを固定する `BicliqueSpec(graph, a, b)` オーバーロードも用意する
（サイズ固定のほうが状態が小さく、実用上こちらがよく使われる）。

---

## 6. スコープ外（v0.6 では入れない）

明示的に落としたもの。理由も残す。

| 項目 | 理由 |
|---|---|
| 有向グラフ | 規模が独立しているため M7 に分離（[m7-directed-graphs.md](m7-directed-graphs.md)） |
| 木分解ベースの辺順序 | 性能項目。M4-8 で PLAN §10 の性能目標は全て達成済みで、緊急性が無い。v1.1 候補 |
| GraphML / JSON / CSV の入出力 | DIMACS・エッジリスト・Graphillion 互換で当面足りる。外部依存ゼロ方針とも相性が悪い（XML/JSON パーサを自前で書くことになる） |
| `zddIsop`（既約積和形）・prime cover | 論理合成向けで、D1（経路列挙・組合せ数え上げ）の用途外 |
| VSOP（値付き族） | `Probability` / `ExpectedValue` / `ItemFrequency` で主要用途は埋まっている |
| BDD 相互変換 | B2 で「ZDD 専用」と決定済み |
| 動的変数順序（sifting） | B15 で「実装しない」と決定済み |
| 非単射な項目写像（射影・存在量化） | 意味論の議論（多重度の扱い）が必要。単射版を出してから需要を見る |
| `RemoveAddSomeItems` の単一パス DP 化 | §3.1 のとおり O(\|U\|²) で出す。最適化は需要が出てから |
