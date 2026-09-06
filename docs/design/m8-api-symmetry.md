# M8: API の対称化と凍結準備（v0.8）設計書

- ドキュメント版数: v1 (2026-09-06)
- 対応するタスク表: [docs/ROADMAP.md](../ROADMAP.md) の M8 節
- 上位計画: [docs/PLAN.md](../PLAN.md)
- 前提: [docs/api-review-notes.md](../api-review-notes.md)（v0.5 時点の public API 棚卸し）

> **改番の注記 (2026-09-06)**: 本マイルストーンを差し込むにあたり、従来の
> **M8「安定化と公開 (v1.0)」を M9 に繰り下げた**（issue #60〜64 のタイトル・ラベルも
> `[M9-x]` / `M9` に付け替えた）。M6・M7 を差し込んだときと同じ手順。
> 繰り下げた理由は §0 のとおり。

## 0. なぜこのマイルストーンを凍結の前に挟むか

v0.7 到達時点で、ビルドは警告 0、テストは 2,202 + 109 件が全てグリーン。機能面では
Core・Frontier・Graphs の 3 レイヤと 30 個超のスペック、無向・有向の両対応が揃っている。

一方で **4 つの層（`Zdd` / `SetSet<T>` / `GraphSet` / `DirectedGraphSet`）の API 面が揃っていない**。
これは体裁の問題ではなく、次の 2 つの理由で v1.0 の凍結前に片付けるべき性質のものである。

1. **機能的な穴が含まれている**。`GraphSet` / `DirectedGraphSet` はコンストラクタが
   `private` のみで、族代数演算も `Zdd` から戻す手段も、明示的な辺集合リストから作る手段も無い。
   Graphillion の `GraphSet([[(1,2),(2,3)], ...])` や `gs1 | gs2` は最頻出の操作なので、
   移行してきた利用者が最初に詰まる（[docs/graphillion-mapping.md](../graphillion-mapping.md) §4 の
   `F.Union(G)` は `Zdd` の話であって `GraphSet` では書けない）
2. **凍結後は直せない項目がある**。配列パラメータの `ReadOnlySpan<T>` 化（§5）と
   別名 4 組の整理（§7）は完全に破壊的変更で、`IHybridDdSpec<TScalar>` の削除（§6）も同様。
   API 承認テスト（M9-1）が入った後だと、これらは「意図せぬ差分」ではなく
   「意図した破壊的変更」として major バージョンを上げる話になる

M6・M7 を差し込んだときと同じ判断基準（**凍結後に足すと破壊的変更になるものだけを前倒しする**）を
適用した。性能改善・新形式の追加など「後からでも足せるもの」は §9 の v1.1 バックログに落とした。

---

## 1. 現状の棚卸し

`src/ZDD.Net` の 4 層の public メソッドを機械的に突き合わせた結果（v0.7 時点）。

| API | `Zdd` | `SetSet<T>` | `GraphSet` | `DirectedGraphSet` |
|---|:-:|:-:|:-:|:-:|
| `Union` / `Intersect` / `Difference` / `SymmetricDifference` | ○ | ○ | **×** | **×** |
| `Product` / `Quotient` / `Meet` / `SupersetsOf` / `SubsetsOf` | ○ | ○ | **×** | **×** |
| `Maximal` / `Minimal` | ○ | ○ | **×** | **×** |
| `Complement` / `ComplementWithin` | ○ | **×** | × | × |
| 明示的な族の生成（`FromSets` / `Empty` / `PowerSet`） | ○ | ○ | **×** | **×** |
| `Larger` / `Smaller` / `LenEquals` | — | **×** | ○ | ○ |
| `MinIter` / `MaxIter` / `RandIter` | — | **×** | ○ | ○ |
| `AddSomeItem` / `RemoveSomeItem` / `RemoveAddSomeItems` | ○ | ○ | ○ | **×** |
| `LongCount` | **×** | ○ | ○ | ○ |
| `Where(GraphConstraints)` | — | — | ○ | **×** |

（`—` は「その層には概念が無い／不要」、`×` は「あるべきだが無い」）

原因は歴史的なもので、`GraphSet`（M3-9）と `SetSet<T>`（M3-8）が別々の PR で作られ、
`DirectedGraphSet`（M7-6）が `GraphSet` の一部だけを写したことによる。B22 の決定
（共通基底クラスは作らない）は維持したまま、**同じ語彙を各層に手で揃える**のが本マイルストーンの仕事。

---

## 2. `GraphSet` / `DirectedGraphSet` の生成 API（M8-1）

### 2.1 API

```csharp
public sealed class GraphSet
{
    /// <summary>明示的な辺集合のリストから族を作る（Graphillion の GraphSet([...]) 相当）。</summary>
    public static GraphSet FromSets(Graph graph, IEnumerable<IEnumerable<Edge>> edgeSets);

    /// <summary>空の族（メンバーが 1 つも無い）。</summary>
    public static GraphSet Empty(Graph graph);

    /// <summary>graph の全辺部分集合 2^E。</summary>
    public static GraphSet PowerSet(Graph graph);

    /// <summary>低レベル API で組んだ ZDD を GraphSet として読む。</summary>
    public static GraphSet FromZdd(Graph graph, Zdd zdd);
}
```

`DirectedGraphSet` にも `DirectedEdge` 版の同じ 4 つを置く。

### 2.2 実装

`GraphSet` は `(Graph, SetUniverse<Edge>, Zdd, IErasedGraphSpec)` を持ち、`_spec` は
**フィルタを事後 `Intersect` ではなく再構築で適用する**ために保持されている
（`GraphSet.cs` の remarks 節）。生成 API はスペックを持たないが、**この経路は既に存在する**：
`AddSomeItem` などが使う `WrapPrecomputed` が、既存 ZDD をなぞる
`PrecomputedZddSpec`（`GraphSetSpec.cs`）を組み立てている。

したがって新しい生成 API は全て

```csharp
var universe = new SetUniverse<Edge>(graph.Edges);
Zdd zdd = /* 各生成方法 */;
return new GraphSet(graph, universe, zdd, new PrecomputedZddSpec(zdd));
```

に帰着する。**新しい内部機構は要らない**。`FromSets` の ZDD 組み立ては
`SetSet<Edge>.FromSets(universe, sets)` にそのまま委譲でき、`PowerSet` は
`ZddManager.PowerSetOf`（M6-1）を使う。

### 2.3 意味論の決定

- `FromSets` は**重複する辺集合を畳む**（`SetSet<T>.FromSets` と同じ）。`graph` に属さない辺が
  現れたら `ArgumentException`（メッセージにその辺を出す）
- `FromZdd` は `zdd` の `Manager.VariableCount` が `graph.EdgeCount` 以上であることだけを検査する。
  それ以上の検証（本当にその辺順序で作られたか）は原理的にできないので、
  **「利用者が保証する低レベルの入口」**として XML doc に明記する
- `Empty(graph)` と `PowerSet(graph)` は毎回新しいユニバースを作る（§3.2 のとおり、これは
  既存のジェネレータと同じ挙動）

---

## 3. `GraphSet` / `DirectedGraphSet` の族代数演算（M8-2）

### 3.1 API

```csharp
public sealed class GraphSet
{
    public GraphSet Union(GraphSet other);
    public GraphSet Intersect(GraphSet other);
    public GraphSet Difference(GraphSet other);
    public GraphSet SymmetricDifference(GraphSet other);

    public static GraphSet operator |(GraphSet left, GraphSet right);
    public static GraphSet operator &(GraphSet left, GraphSet right);
    public static GraphSet operator -(GraphSet left, GraphSet right);
    public static GraphSet operator ^(GraphSet left, GraphSet right);

    public GraphSet Maximal();
    public GraphSet Minimal();

    /// <summary>この族を other と同じユニバースの上に載せ替える。</summary>
    public GraphSet ToUniverseOf(GraphSet other);
}
```

結果は全て `WrapPrecomputed`（§2.2）で包む。`Maximal` / `Minimal` は
`Zdd.Maximal()` / `Zdd.Minimal()` への委譲。

### 3.2 ここが本タスクの本題: ユニバースが共有されていない

`GraphSet.Generate` は呼ばれるたびに `new SetUniverse<Edge>(graph.Edges)` を作る。つまり

```csharp
var g = Graph.Grid(4, 4);
GraphSet paths  = GraphSet.Paths(g, 0, 15);
GraphSet cycles = GraphSet.Cycles(g);
// paths と cycles は同じ Graph から作ったのに、別々の SetUniverse<Edge> と別々の ZddManager を持つ
```

`SetSet<T>.Combine` は `ReferenceEquals(Universe, other.Universe)` を要求する（B18: 暗黙昇格をしない）
ため、この 2 つは**そのままでは結合できない**。

**採る案（推奨）**: B18 を維持し、`GraphSet` にも `SetSet<T>.ToUniverse` と同じ
「明示的に載せ替える」入口を置く。ただし `GraphSet` は自分の `Graph` を知っているので、
引数はユニバースではなく相手の `GraphSet` を取る形（`ToUniverseOf`）にする。

- 二項演算は `ReferenceEquals` 不一致で `ArgumentException`。メッセージは
  `SetSet<T>.Combine` に倣い、**`ToUniverseOf` を名指しで案内する**
- `ToUniverseOf` は両者の `Graph.Edges` が（順序を含めて）一致することを要求し、
  中身は `Zdd.TransferTo(other.Universe.Manager)`（M6-5）。辺順序が違う場合は
  既存の `ToEdgeOrder`（M6-6）で先に揃えるよう例外メッセージで案内する

**検討したが採らなかった案**:

| 案 | 却下の理由 |
|---|---|
| (a) `Graph` インスタンスごとにユニバースを `ConditionalWeakTable` で共有する | `paths \| cycles` がそのまま書けるようになり、**マネージャが 1 個で済むのでメモリも減る**という強い利点がある。しかし 1 つのマネージャに全ての族のノードが溜まるため `Collect()`（M5-3）の意味が変わり、「1 問題ごとにマネージャを捨てる」使い方（B14）が効かなくなる。挙動の変化が大きく、凍結直前に入れるには検証コストが高い。**v1.1 で改めて検討する**（§9） |
| (b) 二項演算の中で暗黙に `TransferTo` する | B18 が明示的に否定した「暗黙昇格」そのもの。メモリ使用量が予測できなくなる |
| (c) `GraphSet` を `SetUniverse<Edge>` を受け取る生成 API に作り替える | 全ジェネレータ（20 個超）にオーバーロードが増え、公開 API の面が倍近くになる |

案 (a) は魅力的だが、**入れるとしても `ToUniverseOf` は無駄にならない**（辺順序違いの載せ替えに
依然として必要）ため、先に (a) 以外を固める順序に問題は無い。

---

## 4. `SetSet<T>` のサイズフィルタと遅延列挙（M8-3）

```csharp
public sealed class SetSet<T>
{
    public SetSet<T> Larger(int n);      // 要素数 > n
    public SetSet<T> Smaller(int n);     // 要素数 < n
    public SetSet<T> LenEquals(int n);   // 要素数 == n

    public IEnumerable<IReadOnlySet<T>> MinIter(IReadOnlyDictionary<T, int> weights);     // long / double 版も
    public IEnumerable<IReadOnlySet<T>> MaxIter(IReadOnlyDictionary<T, int> weights);     // 同上
    public IEnumerable<IReadOnlySet<T>> RandIter(Random random);

    public SetSet<T> Complement();       // 2^Universe \ this
}
```

**実装**: `GraphSet` 側の同名メソッドと同じ。サイズフィルタは
`Zdd.Subset(new CardinalitySpec(...))`、`MinIter` / `MaxIter` は
`LazyWeightEnumeration.Enumerate`、`RandIter` は `Sample` の無限反復。
`Complement` は `Universe.Manager.PowerSetOf(全 item)` との `Difference`
——`Zdd.Complement()` はマネージャの全変数が対象なので、ユニバースが
マネージャの変数数より小さい場合に意味がずれる点に注意する。

**重みの受け方**: `GraphSet` は `Func<Edge, TWeight>`、`SetSet<T>` は
`IReadOnlyDictionary<T, TWeight>` で既に非対称だが、これは各層の既存の
`MaxWeight` / `MinWeight` の受け方に合わせたもので**意図的**。ここでは揃えない。

---

## 5. 残りの層間ギャップ（M8-4）

| 追加するもの | 置き場所 | 実装 |
|---|---|---|
| `AddSomeItem` / `RemoveSomeItem` / `RemoveAddSomeItems`（各 2 オーバーロード） | `DirectedGraphSet` | `GraphSet` の同名メソッドをそのまま写す（`WrapPrecomputed` 経由） |
| `LongCount()` | `Zdd` | `checked((long)Count)`。`docs/api-review-notes.md` §2 の宿題。3 層で揃う |
| `Maximal` / `Minimal` | `DirectedGraphSet` | §3.1 と同じ |

`api-review-notes.md` §2 は「`Zdd` は `BigInteger` のみを正とする方針を明文化する」案も
挙げていたが、`SetSet<T>` / `GraphSet` / `DirectedGraphSet` の 3 つが既に `LongCount` を
持っている以上、**土台にだけ無いほうが説明しづらい**ので追加する側に倒す。

---

## 6. 配列パラメータの `ReadOnlySpan<T>` 化（M8-5）

`Zdd` 層は `params ReadOnlySpan<int>` で統一されているのに、スペックとグラフ層だけ
`int[]` を受けている。**凍結後は完全に破壊的変更**になるため今回で揃える。

| 対象 | 現在 |
|---|---|
| `LinearConstraintSpec` | `(int[] coefficients, LinearConstraintOperator op, long bound)` |
| `KnapsackSpec` | `(int[] weights, long capacity)` |
| `DegreeConstraintSpec` | `(Graph graph, int[] lo, int[] hi)` |
| `DegreeDistributionSpec` | `(Graph graph, int[] counts)` |
| `DirectedDegreeConstraintSpec` | `(DirectedGraph graph, int[] inLo, int[] inHi, int[] outLo, int[] outHi)` |
| `GraphSet.DegreeConstrained` | `(Graph graph, int[] lo, int[] hi)` |
| `GraphSet.Knapsacks` | `(Graph graph, int[] weights, long capacity)` |
| `GraphSet.DegreeDistributions` | `(Graph graph, int[] counts)` |
| `DirectedGraphSet.DegreeConstrained` | `(DirectedGraph graph, int[] ×4)` |

**設計判断**:

- `ReadOnlySpan<int>` に変える。スペックは `struct` で構築後も生き続けるため、
  **コンストラクタ内で必ず自前配列にコピーする**（span をフィールドに保持できないので必然）。
  これにより「呼び出し後に呼び出し側が配列を書き換えたら何が起きるか」という
  現在は未規定の挙動が、**防御的コピーとして仕様化される**——現状は `int[]` を
  そのまま保持しているため、書き換えると構築結果が壊れる
- `params` は付けない。要素数がグラフの頂点数・辺数と一致することを要求する引数であり、
  可変長引数として書き下す使い方は想定しない
- `GraphConstraints.LinearConstraints` の `IReadOnlyList<(int[] Coefficients, ...)>` は
  **`int[]` のまま残す**。`ReadOnlySpan<T>` はタプルの要素にできない（ref struct 制約）ため。
  代わりに「渡した配列は `Graphs()` / `Where()` の呼び出し中にのみ読まれる」ことを doc に書く

---

## 7. `IHybridDdSpec<TScalar>` の決着（M8-6）

`ZDD.Net.Frontier.IHybridDdSpec<TScalar>`（スカラ値 + `int[]` の複合状態）は M2 から public だが、
`FrontierBuilder.Build` に受けるオーバーロードが無い。**public なのに実装しても構築できない型**で、
CHANGELOG の M0〜M4 で毎回「未対応」と書かれ続けている（`api-review-notes.md` §4）。

3 案のうち **(c) `internal` に戻す**を推奨する。

| 案 | 評価 |
|---|---|
| (a) `Build` のハイブリッド版オーバーロードを実装する | 本来あるべき姿だが、`LevelStateTablePair` の実装が要り 400 行級。**凍結直前に入れる規模ではない** |
| (b) `[Experimental]` を付けて public のまま残す | 「呼べるが動かない」型が v1.0 の API 表面に残る。利用者から見て価値が無い |
| (c) **`internal` に戻す**（推奨） | 実装が追いついた時点（v1.1 以降）で改めて public にすればよい。プレリリース期間中なので破壊的変更のコストは最小。`LevelStateTablePair` などの内部実装はそのまま残せる |

(c) を採る場合、v1.1 でハイブリッド版 `Build` を実装するタスクを §9 のバックログに残す。

---

## 8. 別名 4 組の去就（M8-7）

`Zdd` に、同一操作を指す public メソッドが 4 組ある（`api-review-notes.md` §1）。

| SAPPOROBDD / TdZdd 由来 | .NET 的な名前 |
|---|---|
| `Restrict(Zdd)` | `SupersetsOf(Zdd)` |
| `Permit(Zdd)` | `SubsetsOf(Zdd)` |
| `Subset1(int)` | `OnSet(int)` |
| `Subset0(int)` | `OffSet(int)` |

**推奨は (a) 両方 public のまま残し、doc 上の主従を確定させる**。

- `NonSubsetsOf` / `NonSupersetsOf` には対応する別名が無く、削除しても**対称にはならない**
  （SAPPOROBDD 語彙で API 全体を覆えるわけではない）
- TdZdd / SAPPOROBDD からの移植は本ライブラリの主要な流入経路であり、別名は実際に効く
- ただし現状は「どちらが正か」を示すものが無いので、**`OnSet` / `OffSet` / `SupersetsOf` /
  `SubsetsOf` を正とし、別名側の XML doc を `<inheritdoc cref="..."/>` + 1 行の
  「別名。SAPPOROBDD 互換」に統一**し、`<seealso>` で相互参照する。`[Obsolete]` は付けない
  （警告を出してまで一本化する理由が無く、`TreatWarningsAsErrors` の利用者にとっては実質削除になる）

このタスクは実装差分がほぼ doc だけになるので、M8-4 に畳んでもよい。分けているのは
「別名を残すか消すか」がレビューで議論になりうるためで、**議論の結果 (c) 削除になった場合は
破壊的変更として独立した PR が必要**だから。

---

## 9. `GraphSet` / `SetSet<T>` の永続化（M8-8）

現在 `ZddBinaryFormat` は `Zdd` 単体しか読み書きできない。数分かけて構築した経路族を保存して
翌日読み直すには、利用者が「同じ辺順序の `Graph` を再現する」ことを自力でやる必要がある。

```csharp
public static class GraphSetBinaryFormat
{
    public static void Write(GraphSet family, Stream stream);
    public static GraphSet Read(Stream stream, ZddManagerOptions? options = null);

    public static void Write(DirectedGraphSet family, Stream stream);
    public static DirectedGraphSet ReadDirected(Stream stream, ZddManagerOptions? options = null);
}
```

**形式**: 既存の `ZddBinaryFormat` のヘッダに続けて、グラフの節（頂点数・辺リスト・
`SourceOrder` の有無）を持つ独立した形式にする。`ZddBinaryFormat` 自体の版数は上げない
（既存ファイルの互換性を保つ）。

**凍結前に入れる理由**: 後から足すと、`ZddBinaryFormat` の版数を上げるか、
形式を 2 系統に分けるかの選択を v1.0 の利用者に説明する必要が出る。今なら
「v1.0 の形式はこれ」と 1 回言えば済む。

`SetSet<T>` は要素型 `T` の直列化方法が決められない（`T` が任意）ため**対象外**とし、
`SetUniverse<T>` を利用者が復元して `SetSet<T>.FromSets` するか、`GraphSet` 経由を案内する。
この非対称は意図的なものとして doc に明記する。

---

## 10. スコープ外（v1.1 バックログ）

以下は**後から足しても破壊的にならない**ので v1.0 には含めない。個別 issue として登録済み。

| 項目 | 理由 |
|---|---|
| Core 演算のキャンセルとノード上限 | `CancellationToken` と上限は Frontier 構築にしか無く、`Union` / `Product` は巨大 ZDD で OOM しても止められない。`ZddManagerOptions` にプロパティを足すだけなので非破壊 |
| 確率に比例した重み付きサンプリング | `Probability`（M1-15）の対になる操作。新規メソッドの追加のみ |
| `ForestSpec(roots)` | Graphillion の `forests(roots)` は現状「成分数指定」でしか代替できていない（対応表 §1 に記載済み）。新規オーバーロード |
| `Graph` / `DirectedGraph` の値等価性 | 現在は参照等価。`IEquatable<T>` の追加は非破壊 |
| `Graph` インスタンス単位のユニバース共有（§3.2 案 (a)） | メモリ削減効果が大きいが `Collect()` の意味が変わる。単独で検証する価値がある |
| ハイブリッド版 `FrontierBuilder.Build`（§7 案 (a)） | `IHybridDdSpec` を `internal` に戻した後、実装が追いついた時点で再公開する |

## 11. 破壊的変更の有無

| タスク | 破壊的か |
|---|---|
| M8-1 生成 API / M8-2 族代数 / M8-3 `SetSet<T>` 拡充 / M8-4 残りのギャップ | **無し**（追加のみ） |
| M8-5 配列パラメータの Span 化 | **有り**。ただし呼び出し側は `int[]` をそのまま渡せる（暗黙変換）ため、**ソース互換は保たれる**。バイナリ互換は壊れる |
| M8-6 `IHybridDdSpec` を `internal` に | **有り**。ただし実装しても使えなかった型なので実利用者は居ないはず |
| M8-7 別名 4 組 | 推奨案 (a) なら**無し**（doc のみ） |
| M8-8 永続化 | **無し**（新規型の追加） |

v0.8 はプレリリース版として出すため、`docs/PLAN.md` §13 の
「API を早期に固めすぎて後で壊す」対策の枠内に収まる。

## 12. さらに詳しく

- [docs/api-review-notes.md](../api-review-notes.md) — 本書 §5〜§8 のもとになった v0.5 の棚卸し
- [docs/design/m6-api-expansion.md](m6-api-expansion.md) — B17 / B18（写像とユニバースの意味論）
- [docs/design/m7-directed-graphs.md](m7-directed-graphs.md) — B22（`DirectedGraphSet` を薄く保つ決定）
- [docs/graphillion-mapping.md](../graphillion-mapping.md) — 移行対応表（本書の追加で §4 が埋まる）
