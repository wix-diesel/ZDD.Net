# API レビューメモ（M5-7 → M8-1 引き継ぎ）

> **改番の注記 (2026-09-04)**: 本メモ作成時点で「公開 API の凍結」は M6-1 だったが、
> その後 v0.6「API 拡充と相互運用」・v0.7「有向グラフ対応」を前に挟んだため、
> **凍結は M8-1（issue #60）に繰り下がった**。本文中の参照は M8-1 に更新済み。
> 凍結を後ろにずらした理由は [docs/ROADMAP.md](ROADMAP.md) 末尾の
> 「M6 / M7 を差し込んだ理由」を参照。

> M5-7（v0.5 リリース、issue #59）の完了条件「v1.0 に向けた API レビューの下準備」として、
> public API の一覧を棚卸しし、命名・一貫性の気になる箇所をここに記録する。**実際の凍結・
> 命名変更・`[Experimental]` の確定は M8-1（公開 API の凍結、issue #60）で行う**。このメモは
> 提案であって決定ではない。

## 棚卸しの方法

`src/ZDD.Net` を `dotnet build -c Release` し、生成された `ZDD.Net.dll` に対して
`Assembly.GetExportedTypes()` ＋ `Type.GetMembers(Public | Instance | Static | DeclaredOnly)` で
全 public 型・メンバーを列挙する一回限りのスクリプトで棚卸しした（M8-1 で入る
`PublicApiGenerator` + `Verify` による恒久的な API 承認テストとは別物で、このメモを作るための
使い捨てツール。リポジトリには含めていない）。

結果: **public 型 79 個**（`ZDD.Net.Core` / `ZDD.Net.Frontier` / `ZDD.Net.Graphs` /
`ZDD.Net.Io` / `ZDD.Net.Sets` / `ZDD.Net.Specs` の 6 namespace）。全メンバーの一覧は
本ファイル末尾の付録を参照。

## 命名・一貫性で気になった箇所

### 1. 同じ操作に 2 つの public 名前が付いている（`Zdd`）

`src/ZDD.Net/Core/Zdd.cs` に、同一操作を指す public メソッドが完全に重複して存在する:

| SAPPOROBDD/TdZdd 由来の名前 | .NET 的な別名 | 実装 |
|---|---|---|
| `Restrict(Zdd g)` (L248) | `SupersetsOf(Zdd g)` (L244) | どちらも `Manager.SupersetsOf(this, g)` を呼ぶだけ |
| `Permit(Zdd g)` (L264) | `SubsetsOf(Zdd g)` (L260) | どちらも `Manager.SubsetsOf(this, g)` を呼ぶだけ |
| `Subset1(int item)` (L308) | `OnSet(int item)` (L304) | どちらも `Manager.OnSet(this, item)` を呼ぶだけ |
| `Subset0(int item)` (L320) | `OffSet(int item)` (L316) | どちらも `Manager.OffSet(this, item)` を呼ぶだけ |

これは実装ミスではなく意図した設計（TdZdd/SAPPOROBDD を知っている利用者への互換名）だが、
**public API の表面積が実質 2 倍**になっている。`docs/frontier-guide.md` や XML doc では
「`OnSet`/`OffSet`/`SupersetsOf`/`SubsetsOf` が主、`Subset1`/`Subset0`/`Restrict`/`Permit` は
別名」という位置づけで書かれている箇所はあるが、**API 上はどちらが正でどちらが別名かを示す
属性・doc 上の統一表記が無い**。M8-1 の issue 本文が挙げている論点そのものなので、ここでは
選択肢だけ提示する:

- (a) 両方 public のまま残す。ただし XML doc で「推奨はこちら」を明記し、`<seealso>` で相互参照する
- (b) 別名側を `[Obsolete("Use XxxOf instead", error: false)]` にして緩やかに一本化へ誘導する
- (c) 別名側を削除する（プレリリース期間中なので破壊的変更のコストは低い。TdZdd 由来の名前に
  馴染みがある利用者への配慮は失われる）

`NonSubsetsOf` / `NonSupersetsOf`（L273, L282）には別名が無く、上記 4 組だけが対象。

### 2. `LongCount` が層によって非対称

- `Zdd`: `Count`（`BigInteger`、厳密） + `CountApprox`（`double`、近似）はあるが **`LongCount` が無い**
- `ZDD.Net.Graphs.GraphSet` / `ZDD.Net.Sets.SetSet<T>`: `Count` + `CountApprox` に加えて
  **`LongCount()`**（`checked((long)Count)`、`GraphSet.cs` L88 / `SetSet.cs` L160）がある

`SetSet<T>` の XML doc（`SetSet.cs` L47）は「`Count`（厳密 `BigInteger`）・`LongCount`（厳密
`long`、範囲外は例外）・`CountApprox`（近似 `double`）」という 3 段構えを謳っているが、この
3 段構えは `SetSet<T>` と `GraphSet` だけのもので、**土台となる `Zdd` 自身には無い**。
「まず `Zdd` にあるべき機能が高レベルラッパーにだけ追加されている」逆転が起きている。
M8-1 での論点: `Zdd.LongCount()` を追加して 3 層で揃えるか、`Zdd` は `BigInteger` のみを
正とする方針を明文化して `GraphSet`/`SetSet<T>` 側の `LongCount` を「利便のための例外」と
doc で位置づけるか。

### 3. `FrontierManager` と `VertexFrontierManager` で語彙が揃っていない

`ZDD.Net.Graphs.FrontierManager`（辺が変数の問題向け）と
`ZDD.Net.Graphs.VertexFrontierManager`（頂点が変数の問題向け、M3-6 で追加）は、同じ「フロン
ティア法の頂点の出入りを追跡する」役割を持つ姉妹クラスだが、対応する概念に別の名前が
付いている:

| 概念 | `FrontierManager` | `VertexFrontierManager` |
|---|---|---|
| このレベルで新規にフロンティアへ入る頂点 | `IntroducedVertices(level)` | （無し。導入は暗黙） |
| このレベルでフロンティアから抜ける頂点 | `ForgottenVertices(level)` | `ForgottenSlots(level)` |
| フロンティア内の頂点の相方（辺の対）を引く添字 | `MateIndex(vertex, other)` | `Slot(vertex)` |
| 頂点→レベルの逆引き | （無し） | `VertexToLevel(vertex)` / `LevelToVertex(level)` |
| 直前に決定済みの隣接頂点のスロット | （無し。`MateIndex` で都度引く） | `EarlierNeighborSlots(vertex)` |

`Mate`（辺の両端点を指す語）と `Slot`（フロンティア配列上の位置を指す語）は指している対象が
違う（前者は「辺の相方」、後者は「状態配列の場所」）ため単純な統一はできないが、**戻り値の
複数形/単数形の付け方**（`ForgottenVertices` は複数形でレベルに出入りする頂点集合、
`ForgottenSlots` も複数形で同じ役割）は揃っている一方、**`IntroducedVertices` に対応するものが
`VertexFrontierManager` に無い**（頂点スペック側は「導入」を明示的に問い合わせる必要が今の
ところ無いため)。M8-1 での論点: 2 つのマネージャの API 表を並べて、意図的な差なのか埋め忘れなの
かを確定させる。

### 4. `IHybridDdSpec<TScalar>` は事実上使えない public 型

`ZDD.Net.Frontier.IHybridDdSpec<TScalar>`（スカラ値 + `int[]` の複合状態を表す契約）は M2 から
public だが、`FrontierBuilder.Build` にはこの契約を受けるオーバーロードが無い
（`IDdSpec<TState>` と `IArrayDdSpec` の 2 つだけ対応）。つまり**この interface を実装しても、
現状の public API だけでは ZDD を構築する手段が無い**。CHANGELOG の M0〜M4 の「Notes」節で
毎回「未対応（vX.Y 以降）」と繰り返されてきた既知の制約で、v0.5 でも状況は変わっていない。

現時点で `[Experimental]` を付与している public API は**0 件**。M8-1 の完了条件が「`[Experimental]`
の付け外しを確定させる」ことを求めているので、ここでの推奨は次のいずれか:

- `FrontierBuilder.Build` にハイブリッド版オーバーロードを実装してから通常の public API として
  確定させる
- 実装できないまま v1.0 を迎えるなら、`System.Diagnostics.CodeAnalysis.ExperimentalAttribute`
  （.NET 8+ 標準。追加の `PackageReference` 不要）を付けて「呼び出し可能だが構築の入口が無い、
  将来変わりうる契約」であることを明示する
- あるいは v1.0 の対象から一旦外し、`internal` に戻して実装が追いついてから再公開する

### 5. その他、確認したが特に問題を見つけなかった箇所

- 例外型のコンストラクタ引数の非対称（`ZddCollectedException(string)` /
  `ZddFormatException(string)` に対し `GraphFormatException(int lineNumber, string)` /
  `BuildLimitExceededException(BuildLimit, int, int, string)` は追加のコンテキストを取る）は、
  各例外が持つ文脈情報の違いを反映したもので、意図的な設計として妥当と判断した
- `Zdd.MaxWeight`/`MinWeight`/`TopK` は「`<TWeight, TOps>` を明示する完全形」＋
  「`int`/`long`/`double` 用の型引数省略ショートカット」で 4 オーバーロードずつあるが、
  `GraphSet`/`SetSet<T>` 側は `Func<Edge/T, weight>` を受ける 3 オーバーロード（int/long/double）
  のみで完全形を公開していない。これは「`Zdd` は利用者定義の `IWeightOps<TWeight>` に開いている
  が、高レベルラッパーは組み込み型だけで十分」という一貫した設計判断であり、指摘事項としない
- `EdgeOrderMapping.ToSourceEdgeIndex` / `FromSourceEdgeIndex` はラウンドトリップの対として
  名前が揃っている
- `Graph.Optimize` の 4 戦略（`AsGiven`/`Bfs`/`Dfs`/`Grid`/`BeamSearchPathWidth`）や
  `ZDD.Net.Io` の 3 形式（`DimacsGraph`/`EdgeListGraph`/`SimpleTextGraph`）はいずれも
  `Read`/`Write`（`TextReader`/`TextWriter` オーバーロード＋ `string` 簡易版）で API 形が揃っている

## `[Experimental]` の扱い（完了条件チェックリスト）

- 現在 `[Experimental]`（またはそれに相当する独自属性）を付けている public API は**存在しない**
- v1.0 に持っていく前提で `[Experimental]` を検討すべき候補は上記 §4 の
  `IHybridDdSpec<TScalar>` のみ。ほかの M5 追加分（`ZddBinaryFormat` / `GraphillionTextFormat` /
  `ZddManager.Collect()`/`RootSet` / `DotOptions`）はいずれも自動テスト・実データでの相互運用
  確認が済んでいるため、`[Experimental]` を付けずに通常の public API として v1.0 へ持っていく
  想定でよいと判断した

## 付録: public API 一覧（型ごとのメンバー、`dotnet build -c Release` 後の `ZDD.Net.dll` から生成）

<details>
<summary>クリックで展開（79 型）</summary>

```
## ZDD.Net.Core.ApproximateCardinalityEval
  - EvalNode(Int32, Double, Double)
  - EvalTerminal(Boolean)
## ZDD.Net.Core.BigIntegerWeightOps
  - Add(BigInteger, BigInteger)
  - Compare(BigInteger, BigInteger)
  - BigInteger Zero
## ZDD.Net.Core.CardinalityEval
  - EvalNode(Int32, BigInteger, BigInteger)
  - EvalTerminal(Boolean)
## ZDD.Net.Core.DoubleWeightOps
  - Add(Double, Double)
  - Compare(Double, Double)
  - Double Zero
## ZDD.Net.Core.IDdEval`1
  - EvalNode(Int32, TValue, TValue)
  - EvalTerminal(Boolean)
## ZDD.Net.Core.IWeightOps`1
  - Add(TWeight, TWeight)
  - Compare(TWeight, TWeight)
  - TWeight Zero
## ZDD.Net.Core.Int32WeightOps
  - Add(Int32, Int32)
  - Compare(Int32, Int32)
  - Int32 Zero
## ZDD.Net.Core.Int64WeightOps
  - Add(Int64, Int64)
  - Compare(Int64, Int64)
  - Int64 Zero
## ZDD.Net.Core.SizeDistributionEval
  - EvalNode(Int32, BigInteger[], BigInteger[])
  - EvalTerminal(Boolean)
## ZDD.Net.Core.WeightedSet`1
  - Int32[] Items
  - Int32 Size
  - ToString()
  - TWeight Weight
## ZDD.Net.Core.Zdd
  - Blocking()
  - Change(Int32)
  - Complement()
  - Contains(IEnumerable`1)
  - Contains(ReadOnlySpan`1)
  - BigInteger Count
  - Double CountApprox
  - CountBySize()
  - Difference(Zdd)
  - ElementAt(BigInteger, ZddEnumerationOrder)
  - Equals(Zdd)
  - Equals(Object)
  - ExpectedValue(ReadOnlySpan`1)
  - Flip(ReadOnlySpan`1)
  - GetEnumerator()
  - GetHashCode()
  - HittingSets()
  - IndexOf(IEnumerable`1, ZddEnumerationOrder)
  - IndexOf(ReadOnlySpan`1)
  - Intersect(Zdd)
  - Boolean IsBase
  - Boolean IsDefault
  - Boolean IsEmpty
  - IsSubsetOf(Zdd)
  - ItemFrequency()
  - ZddManager Manager
  - MaxWeight(ReadOnlySpan`1) [x4: <TWeight,TOps>, int, long, double]
  - Maximal()
  - Meet(Zdd)
  - MinWeight(ReadOnlySpan`1) [x4: <TWeight,TOps>, int, long, double]
  - Minimal()
  - Int64 NodeCount
  - NonSubsetsOf(Zdd)
  - NonSupersetsOf(Zdd)
  - OffSet(Int32)
  - OnSet(Int32)
  - Overlaps(Zdd)
  - Permit(Zdd)
  - Probability(ReadOnlySpan`1)
  - Product(Zdd)
  - Quotient(Zdd)
  - Remainder(Zdd)
  - Restrict(Zdd)
  - Sample(Random)
  - Sample(Int32, Random)
  - Sets(ZddEnumerationOrder)
  - Subset0(Int32)
  - Subset1(Int32)
  - SubsetsOf(Zdd)
  - SupersetsOf(Zdd)
  - Support()
  - SymmetricDifference(Zdd)
  - ToDot() / ToDot(DotOptions)
  - ToString()
  - TopK(ReadOnlySpan`1, Int32) [x4: <TWeight,TOps>, int, long, double]
  - Union(Zdd)
  - WriteDot(TextWriter) / WriteDot(TextWriter, DotOptions)
  - operators: & | / == ^ != % * ~ -
## ZDD.Net.Core.ZddCollectedException
  - .ctor(String)
## ZDD.Net.Core.ZddEnumerationOrder
  - ZddEnumerationOrder Default
  - ZddEnumerationOrder Lexicographic
## ZDD.Net.Core.ZddEvaluation
  - Evaluate(Zdd&, TEval)
## ZDD.Net.Core.ZddManager
  - .ctor(Int32, ZddManagerOptions)
  - Zdd Base
  - Collect() / Collect(Zdd[])
  - Dispose()
  - Zdd Empty
  - GetStatistics()
  - Boolean IsDisposed
  - Int64 NodeCount
  - ZddRootSet RootSet
  - Singleton(Int32)
  - Int32 VariableCount
## ZDD.Net.Core.ZddManagerOptions
  - .ctor()
  - Int32 DefaultInitialCacheCapacity
  - Int32 DefaultInitialNodeCapacity
  - Int32 DefaultInitialUniqueTableCapacity
  - Int32 DefaultMaxCacheCapacity
  - Int32 InitialCacheCapacity
  - Int32 InitialNodeCapacity
  - Int32 InitialUniqueTableCapacity
  - Int32 MaxCacheCapacity
## ZDD.Net.Core.ZddRootSet
  - Add(Zdd)
  - Clear()
  - Contains(Zdd)
  - Int32 Count
  - GetEnumerator()
  - Remove(Zdd)
## ZDD.Net.Core.ZddStatistics
  - Int32 CacheCapacity
  - Double CacheHitRate
  - Int64 CacheHits / CacheLookups / CacheMisses / CacheOverwrites
  - Int64 CollectionCount
  - Equals(ZddStatistics) / Equals(Object) / GetHashCode()
  - TimeSpan LastCollectionDuration
  - Double LastCollectionReductionRatio
  - Int64 LastCollectionRemovedNodeCount
  - Int32 MaxCacheCapacity
  - Int64 NodeCount / NodeTableCapacity
  - Double NodeTableLoadFactor
  - Int64 PeakNodeCount
  - ToString()
  - Int32 UniqueTableCapacity
  - Int64 UniqueTableCollisions
  - Double UniqueTableLoadFactor
  - operators: == !=
## ZDD.Net.Frontier.AndSpec`4 / AndState`2
  - .ctor(TSpecA, TSpecB), GetChild, GetRoot, StateEquals, StateHashCode
## ZDD.Net.Frontier.ArrayDdSpecAdapterExtensions
  - AsDdSpec(TSpec)
## ZDD.Net.Frontier.ArrayDdSpecAdapter`1
  - .ctor(TSpec), GetChild, GetRoot, StateEquals, StateHashCode
## ZDD.Net.Frontier.BuildLimit
  - BuildLimit FrontierSize / NodeCount
## ZDD.Net.Frontier.BuildLimitExceededException
  - .ctor(BuildLimit, Int32, Int32, String)
  - Int32 Level, BuildLimit Limit, Int32 LimitValue
## ZDD.Net.Frontier.BuildOptions
  - .ctor()
  - CancellationToken CancellationToken
  - Int32 MaxDegreeOfParallelism / MaxFrontierSize / MaxNodeCount
  - IProgress`1 Progress
  - Boolean RecordStates
  - Int32 Unlimited
## ZDD.Net.Frontier.BuildProgress
  - .ctor(Int32, Int32, Int32, Int64)
  - Int32 FrontierSize / Level, Int64 NodeCount, Int32 RootLevel
## ZDD.Net.Frontier.DdResult
  - Int32 False / True, IsTerminal(Int32)
## ZDD.Net.Frontier.FrontierBuilder
  - Build(ZddManager, TSpec, BuildOptions) [IDdSpec<TState> 版]
  - Build(ZddManager, TSpec, BuildOptions, IReadOnlyDictionary`2&, Func`2) [状態記録版]
  - Build(ZddManager, TSpec, BuildOptions) [IArrayDdSpec 版]
## ZDD.Net.Frontier.IArrayDdSpec
  - Int32 ArrayLength, GetChild(Span`1, Int32, Int32), GetRoot(Span`1)
## ZDD.Net.Frontier.IDdSpec`1
  - GetChild(TState&, Int32, Int32), GetRoot(TState&), StateEquals, StateHashCode
## ZDD.Net.Frontier.IHybridDdSpec`1
  - Int32 ArrayLength
  - GetChild(TScalar&, Span`1, Int32, Int32), GetRoot(TScalar&, Span`1)
  - ScalarEquals(TScalar&, TScalar&), ScalarHashCode(TScalar&)
  - ※ FrontierBuilder.Build に対応オーバーロード無し（§4 参照）
## ZDD.Net.Frontier.OrSpec`4 / OrState`2
  - .ctor(TSpecA, TSpecB), GetChild, GetRoot, StateEquals, StateHashCode
## ZDD.Net.Frontier.SpecExtensions
  - And(TSpecA, TSpecB), Or(TSpecA, TSpecB)
## ZDD.Net.Frontier.ZddExtensions
  - Subset(Zdd, TSpec, BuildOptions)
## ZDD.Net.Frontier.ZddSpec
  - .ctor(Zdd), GetChild, GetRoot, StateEquals, StateHashCode
## ZDD.Net.Graphs.Edge
  - .ctor(Int32, Int32), Equals, GetHashCode, Other(Int32), ToString
  - Int32 U / V, operators: == !=
## ZDD.Net.Graphs.EdgeOrderMapping
  - Int32 Count
  - FromSourceEdgeIndex(Int32) / ToSourceEdgeIndex(Int32)
  - Graph Source
  - IReadOnlyList`1 ToSourceEdgeIndices
  - ToSourceEdgeSet(IEnumerable`1)
## ZDD.Net.Graphs.EdgeOrderOptions
  - Int32 BeamWidth, BestOfCandidates(Int32)
  - CancellationToken CancellationToken
  - EdgeOrderOptions Default
  - FromVertex(Int32)
  - Int32 MaxCandidates
  - StartVertexSelection Selection, Int32 StartVertex
  - WithBeamWidth(Int32), WithCancellationToken(CancellationToken)
## ZDD.Net.Graphs.EdgeOrderStrategy
  - AsGiven / BeamSearchPathWidth / Bfs / Dfs / Grid
## ZDD.Net.Graphs.FrontierManager
  - .ctor(Graph)
  - ForgottenVertices(Int32) / IntroducedVertices(Int32)
  - FrontierSize(Int32), Graph Graph
  - MateIndex(Int32, Int32), Int32 MaxFrontierSize
## ZDD.Net.Graphs.Graph
  - .ctor(Int32, IEnumerable`1)
  - Complete(Int32) / Cycle(Int32) / Grid(Int32, Int32) / Path(Int32)
  - Degree(Int32), Int32 EdgeCount / VertexCount
  - EdgeIndexToLevel(Int32) / EdgeIndexToVariableIndex(Int32) / LevelToEdgeIndex(Int32) /
    VariableIndexToEdgeIndex(Int32)
  - IReadOnlyList`1 Edges, GetEdge(Int32), IncidentEdges(Int32)
  - EstimateMaxFrontierSize() / EstimateMaxFrontierSize(EdgeOrderStrategy, EdgeOrderOptions)
  - Optimize(EdgeOrderStrategy, EdgeOrderOptions)
  - EdgeOrderMapping SourceOrder
  - WithEdgeOrder(IReadOnlyList`1)
## ZDD.Net.Graphs.GraphSet
  - Cliques(Graph) / Cycles(Graph, Boolean) / Forests(Graph, Nullable`1) /
    HamiltonianCycles(Graph) / HamiltonianPaths(Graph, Int32, Int32) /
    IndependentSets(Graph) / Matchings(Graph, Boolean) / Paths(Graph, Int32, Int32, Boolean) /
    Trees(Graph)
  - Contains(IEnumerable`1), BigInteger Count, Double CountApprox
  - ElementAt / IndexOf, Equals / GetHashCode
  - Excluding(Edge) / Excluding(Int32) / Including(Edge) / Including(Int32)
  - Boolean IsEmpty, Larger(Int32) / Smaller(Int32), LenEquals(Int32)
  - LongCount()
  - MaxIter/MinIter(Func`2) [x3], MaxWeight/MinWeight(Func`2) [x3 each], TopK(Func`2, Int32) [x3]
  - Probability(Func`2), RandIter(Random), Sample(Random) / Sample(Int32, Random)
  - ToDot(DotOptions) / WriteDot(TextWriter, DotOptions), ToString()
  - Graph Graph, SetUniverse`1 Universe, Zdd Zdd
  - operators: == !=
## ZDD.Net.Graphs.StartVertexSelection
  - BestOfCandidates / MinimumDegree / Specified
## ZDD.Net.Graphs.VertexFrontierManager
  - .ctor(Graph)
  - EarlierNeighborSlots(Int32) / ForgottenSlots(Int32)
  - Graph Graph, Int32 MaxFrontierSize
  - LevelToVertex(Int32) / VertexToLevel(Int32), Slot(Int32)
## ZDD.Net.Io.DimacsGraph / EdgeListGraph / SimpleTextGraph
  - Read(TextReader) / Read(String), Write(Graph) / Write(Graph, TextWriter)
  - SimpleTextGraph のみ Write に IReadOnlyList`1（頂点ラベル）を追加で取る
## ZDD.Net.Io.DotOptions
  - .ctor()
  - Nullable`1 FocusNodeId, Func`2 LevelLabel, Int32 MaxLevels / MaxNodes
  - String NonTerminalColor / NonTerminalShape / OneEdgeStyle / ZeroEdgeStyle
  - IReadOnlyDictionary`2 StateLabels
## ZDD.Net.Io.GraphFormatException
  - .ctor(Int32, String), Int32 LineNumber
## ZDD.Net.Io.GraphillionTextFormat
  - Read(String, Nullable`1, ZddManagerOptions) / Read(TextReader, Nullable`1, ZddManagerOptions)
  - Write(Zdd&) / Write(Zdd&, TextWriter)
## ZDD.Net.Io.LabeledGraph
  - .ctor(Graph, IReadOnlyList`1), Graph Graph, IReadOnlyList`1 VertexLabels
## ZDD.Net.Io.ZddBinaryFormat
  - UInt32 FormatVersion
  - Read(Stream, ZddManagerOptions), Write(Zdd&, Stream)
## ZDD.Net.Io.ZddFormatException
  - .ctor(String)
## ZDD.Net.Sets.SetSet`1
  - Contains(IEnumerable`1), BigInteger Count, Double CountApprox, LongCount()
  - Difference/Intersect/Product/Quotient/SubsetsOf/SupersetsOf/SymmetricDifference/Union(SetSet`1)
  - ElementAt / IndexOf, Empty(SetUniverse`1)
  - Equals(SetSet`1) / Equals(Object) / GetHashCode()
  - FromSets(SetUniverse`1, IEnumerable`1) / FromSets(IEnumerable`1, IEqualityComparer`1)
  - GetEnumerator(), Boolean IsEmpty
  - MaxWeight/MinWeight(IReadOnlyDictionary`2) [x3 each], Maximal() / Minimal()
  - Meet(SetSet`1)
  - PowerSet(SetUniverse`1) / PowerSet(IEnumerable`1, IEqualityComparer`1)
  - Probability(IReadOnlyDictionary`2)
  - Sample(Random) / Sample(Int32, Random)
  - ToDot(DotOptions) / WriteDot(TextWriter, DotOptions), ToString()
  - TopK(IReadOnlyDictionary`2, Int32) [x3]
  - SetUniverse`1 Universe, Zdd Zdd
  - operators: & | == ^ != -
## ZDD.Net.Sets.SetUniverse`1
  - .ctor(IEnumerable`1, IEqualityComparer`1, ZddManagerOptions)
  - IEqualityComparer`1 Comparer, Contains(T), Int32 Count
  - ElementAt(Int32), IReadOnlyList`1 Elements, IndexOf(T), ZddManager Manager
## ZDD.Net.Specs.* （17 スペック型。いずれも readonly struct + 非アロケーション GetChild）
  - CardinalitySpec / KnapsackSpec / LinearConstraintSpec: スカラ状態（Int32 or Int64 参照渡し）
  - PowerSetSpec: スカラ状態（Byte 参照渡し）
  - DfaSpec: スカラ状態（Int32 参照渡し）+ Transition(Int32,Int32) / IsAccepting(Int32) / StateCount
  - 残り 12 個（CliqueSpec / ColoringSpec / ConnectedSubgraphSpec / CutSpec / CycleSpec /
    DegreeConstraintSpec / DominatingSetSpec / ForestSpec / GraphPartitionSpec /
    HamiltonianCycleSpec / HamiltonianPathSpec / IndependentSetSpec / MatchingSpec / PathSpec /
    SpanningTreeSpec / SteinerTreeSpec / VertexCoverSpec）: IArrayDdSpec 実装（Int32 ArrayLength +
    GetChild(Span`1,...) / GetRoot(Span`1)）、いずれも `Graph Graph` を公開プロパティとして持つ
  - コンストラクタ引数の粒度は種目ごとに異なる（`.ctor(Graph)` のみのものから
    `.ctor(Graph, Int32, Int32, Int32)` まで）が、すべて「グラフ + 問題固有パラメータ」という
    共通の形は守られている
```

</details>
