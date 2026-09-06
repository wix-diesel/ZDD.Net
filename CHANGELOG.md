# Changelog

このプロジェクトの変更点は本ファイルに記載する。フォーマットは
[Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) に、バージョニングは
[Semantic Versioning](https://semver.org/lang/ja/) に準拠する。

v1.0 までは API 未確定のプレリリース版として公開する（[docs/PLAN.md](docs/PLAN.md) §13）。

## [Unreleased]

### Added

- `DirectedEdge` / `DirectedGraph`: 有向グラフのデータ構造（M7-1、issue #152、
  [docs/design/m7-directed-graphs.md](docs/design/m7-directed-graphs.md) §2）。既存の `Edge` は
  `GetHashCode` を `HashCode.Combine(Math.Min(U,V), Math.Max(U,V))` で組んでおり、`Graph` は自己ループと
  多重辺を拒否するため、一方通行のある道路網や依存関係グラフの経路列挙が原理的に書けなかった
  （D1「主用途 = 経路列挙・数え上げ」の半分が欠けている状態）。`DirectedEdge` は `Edge` と違い
  `GetHashCode` が `HashCode.Combine(From, To)` で**向きを区別する**（`DirectedEdge(0,1) != DirectedEdge(1,0)`）
  ——これが `Edge` と型を分ける唯一かつ十分な理由で、`Edge` に `IsDirected` フラグを足す案は採らなかった
  （等価性の意味がフラグで変わる型は事故のもと）。`DirectedGraph` は逆平行辺 `u→v` / `v→u` の共存を
  許可しつつ、自己ループと多重辺は無向側と揃えて拒否する。`ToUndirected()` は逆平行辺が 1 本に潰れる
  ため辺数が変わりうるので、辺 index の対応が壊れないよう戻り値の `Graph.SourceOrder` は必ず
  `null` にしてある（`GraphSet.ToEdgeOrder`（M6-6）を誤って通せないようにするため）。生成ショートカット
  `Grid`（= `Bidirected(Graph.Grid(...))`）/ `Complete`（全順序対）/ `Cycle`（一方向の閉路、`n = 2` の
  逆平行 2-閉路も許容——無向版の `Graph.Cycle` は `n < 3` を拒否するのに対しここは `n < 2` が下限）/
  `Path` を用意。`Bidirected(g).ToUndirected()` が `g` と（辺順序を除いて）一致することを検証済み。
  `Graph` と同じ辺順序 API（`WithEdgeOrder` / `Optimize` / `EstimateMaxFrontierSize`）は
  `EdgeOrdering` / `FrontierManager` を有向対応させる M7-2 で繋ぐため、このリリースにはまだ無い。
- `FrontierManager(DirectedGraph)`: `FrontierManager` が有向グラフからも構築できるようになった
  （M7-2、issue #153、[docs/design/m7-directed-graphs.md](docs/design/m7-directed-graphs.md) §2.3）。
  `FrontierManager` は元々 `graph.VertexCount` / `graph.EdgeCount` / 各辺の端点対しか見ておらず、
  `EdgeOrdering` も `IncidentEdges` / `Degree` / グリッド判定までで、どちらも辺の向きを必要としない。
  「無向の影グラフを作って使い回す」案は採れない（逆平行辺があると影グラフに多重辺が生じ、`Graph`
  のコンストラクタが拒否するため）ので、頂点数・端点対・接続辺リストだけを持つ internal 型
  `EdgeTopology` を切り出し、`Graph` と `DirectedGraph` の双方がこれを internal に公開する形にした。
  `FrontierManager` と `EdgeOrdering`（`BeamSearchPathWidth` 含む）を `EdgeTopology` を受け取るように
  付け替え、その上で `DirectedGraph.WithEdgeOrder` / `Optimize` / `EstimateMaxFrontierSize` /
  `SourceOrder`（新設の `DirectedEdgeOrderMapping` 経由。`EdgeOrderMapping.Source` を `Graph` のまま
  保つため、共通の基底クラスにはせず薄いラッパを複製した）を実際に接続した。**振る舞い不変のリファクタ**
  であり、既存テストは無変更のまま全て通る（`FrontierManager(Graph)` の結果は一切変わらない）。
  `EdgeOrdering` のグリッド判定は辺の生の本数ではなく端点対の distinct 集合で判定するよう一般化した
  （`Graph` は多重辺を拒否するので既存の挙動と完全に一致し、逆平行 2 本を持つ有向グリッドも同じロジック
  で認識できる）。`Bidirected(g)` のフロンティア構造（`MaxFrontierSize` を含む）が `g` のそれと一致する
  ことを検証済み。`EdgeTopology` は internal のままで、public API の面は増えていない
  （`FrontierManager` のコンストラクタ追加と `DirectedGraph` への 4 メンバー追加のみ）。
- `DirectedPathSpec`: 有向 s–t 単純パス列挙（M7-3、issue #154、
  [docs/design/m7-directed-graphs.md](docs/design/m7-directed-graphs.md) §3.2）。有向の制約は
  「無向としての形（連結性・閉路の有無）」＋「頂点ごとの入出次数」に分解できるため、既存の
  `MateChainState` の mate 配列をそのまま流用し、新たに必要な向きの情報だけをフロンティア頂点
  1 つにつき 1 スロット（`IArrayDdSpec` が値の範囲に応じて自動でバイト詰めするので、2 値のスロット
  は実質 1 バイト）追加する形で実装した。ビット単位への再詰め直しは状態サイズが実際にボトルネックに
  なってから検討する後回しの最適化とし、理由を XML doc に残してある。弧 `u→v` を採用するときは
  `u == to`（`to` に出る弧は無い）／`v == from`（`from` に入る弧は無い）を先に弾き、次に `u` が
  既に出向きの弧を、`v` が既に入向きの弧を持っていないかを向きビットで確認してから
  `MateChainState.Splice` を呼ぶ（閉路になれば `PathSpec` と同様に不採用）。頂点がフロンティアから
  外れるときは `from` が (出 1, 入 0)、`to` が (入 1, 出 0)、その他が (0, 0) または (1, 1) を要求する。
  `allowAnyEndpoints = true` のときは弧ごとの `to`/`from` 制約を外し、代わりに「入 0・出 1 の頂点が
  ちょうど 1 つ、入 1・出 0 の頂点がちょうど 1 つ」を 2 本の使い切りカウンタスロットで数える
  （`PathSpec.AllowAnyEndpoints` の 1 本のカウンタを、向きで区別が要るぶん 2 本に分けた形）。
  受け入れ条件として **`DirectedGraph.Bidirected(格子)` 上の有向 s→t 単純パス数が OEIS A007764 と
  厳密に一致する**こと（無向パスは向きが一意に定まるため）を 7×7 まで CI で検証し、頂点数 8 以下の
  ランダム有向グラフ・逆平行辺を含むグラフ・一方通行のみで到達不能なグラフに対する総当たり照合、
  `AllowAnyEndpoints` と無向版 `PathSpec` の対応、`.And` によるスペック合成（`CardinalitySpec` との
  直接合成が事後フィルタと一致すること）もテスト済み。`DirectedGraph` には
  `EdgeIndexToVariableIndex` / `VariableIndexToEdgeIndex` / `EdgeIndexToLevel` / `LevelToEdgeIndex`
  （`Graph` の同名メンバーと同じ恒等写像／レベル変換）をあわせて追加した——フロンティア方式のスペックが
  レベルと弧インデックスを相互変換するのに必要だが、M7-1/M7-2 の時点ではまだ無かったため。
- `DirectedCycleSpec` / `DirectedHamiltonianPathSpec` / `DirectedHamiltonianCycleSpec`: 有向単純閉路・
  有向ハミルトンパス・有向ハミルトン閉路の列挙（M7-4、issue #155、
  [docs/design/m7-directed-graphs.md](docs/design/m7-directed-graphs.md) §3.3）。`DirectedCycleSpec` は
  `CycleSpec` の mate 配列に `DirectedPathSpec` と同じ向き 1 ビットを足しただけでは済まなかった。
  `Graph` と違い `DirectedGraph` は同じ 2 頂点間に逆平行辺 `u→v` / `v→u` の 2 本を許すため、
  この 2 本を続けて採用すると `MateChainState.Splice` は 1 辺だけの鎖をその場で「閉じた」と報告して
  しまう——2 頂点の "digon" であって閉路ではない。受け入れ条件の「`Bidirected(g)` の有向単純閉路数が
  `g` の無向単純閉路数のちょうど 2 倍」は無向単純グラフに長さ 2 の閉路が存在しない以上、digon を除外
  しないと成立しない（`K_3` で試算すると digon を数えれば 2 ではなく 5 になる）。そこでフロンティア
  頂点ごとに向きビットとは別の「鎖がまだ元の 2 頂点のままか」を示す鮮度ビットを追加し、`Splice` の
  各分岐（新規ペア／延伸／合流）に合わせて更新し、鎖が閉じる瞬間に両端がまだ鮮度ビット付きなら
  不採用にする。`DirectedHamiltonianCycleSpec` は全頂点が入次数・出次数ともに 1 に達することを要求する
  ため、この鮮度追跡は不要——`VertexCount < 3` の頂点数ガードさえあれば、途中で digon が閉じても
  残りの頂点が入次数 0 のまま `MateChainState.ForgetRequireVisited` に弾かれる。`DirectedHamiltonianPathSpec`
  は鎖を一切閉じない（`Splice` が `Closed` を返した時点で不採用、digon かどうかに関わらず）ため、
  やはり鮮度追跡が要らない。受け入れ条件として、完全グラフ・5/6-閉路・格子・Petersen グラフでの
  ×2 関係、`K_n`（n = 4..8）の有向ハミルトン閉路数が `(n-1)!` と一致すること、`Bidirected(Petersen)`
  に有向ハミルトン閉路が存在しないこと、`DirectedGraph.Cycle(n)`（n ≥ 3）がちょうど 1 つの閉路を
  返す一方 `Cycle(2)`（逆平行ペアそのもの）はどのモードでも空であることをテスト済み。加えて頂点数 8
  以下の小規模・ランダム有向グラフでの総当たり照合、`DirectedCycleSpec.Single` が非 `Single` 族の
  部分集合であることも検証した。

## [0.6.0] - 2026-09-05

M6「API 拡充と相互運用」マイルストーン（[docs/PLAN.md](docs/PLAN.md) §12）の完了リリース。
v1.0 に向けたプレリリースとして公開する。他ライブラリ（Graphillion / TdZdd / SAPPOROBDD /
CUDD+EXTRA）との比較で見つかった、「エンジンはあるのに入口が無い」類の欠落を埋めた:
決定済み未実装 API の穴埋め（`ComplementWithin` / `EnumerateInto` / `TryBuild`）、項目写像と
マネージャ間転送（`MapItems` / `MapItemsTo` / `TransferTo`）、ユニバース／辺順序をまたぐ族の移送
（`SetUniverse.Extend` / `SetSet.ToUniverse` / `GraphSet.ToEdgeOrder`）、Graphillion 由来の族操作
（`AddSomeItem` ほか、`CostAtMost` ほか）、**`GraphSet` の露出**（`Specs/` のうち高レベル API から
一切触れていなかった 13 スペックが使えるようになった）、新規スペック 4 つ（`InducedSubgraphSpec` /
`BicliqueSpec` / `DegreeDistributionSpec` / `VertexGroupSpec`）、統合ビルダ `GraphSet.Graphs()`。
Graphillion からの移行者向けに、対応表の暫定版を [docs/graphillion-mapping.md](docs/graphillion-mapping.md)
に用意した（本番の移行ガイドは M8-4）。次の M7 は有向グラフ対応（v0.7）。

### Added

- `Zdd.ComplementWithin(items)` / `ZddManager.PowerSetOf(items)`: 部分ユニバースでの補集合
  （M6-1、issue #136、`docs/OPEN-QUESTIONS.md` B8 で決めたまま未実装だった穴埋め）。
  `Complement()` はマネージャの全変数に対する `2^U \ f` を返すが、`ComplementWithin(items)` は
  注目している要素だけを動かした `2^items \ f` を返す。`PowerSetOf` は葉側から 1 パスで
  `items` の個数だけノードを積むので、変数がどれだけ多いマネージャでも O(items) で終わる
  （マネージャの `VariableCount` には依らない）。`items` の重複は正規化して無視し、空なら
  `2^∅ = {∅}`（`Base`）を返す。`f` が `items` の外側の要素を含む集合を持っていても例外には
  ならず、その集合はそもそも `2^items` に無いので単に無視される（support の検査はしない）。
  `Complement()` は `ComplementWithin(全変数)` と一致することを回帰テストで確認済み。
- `Zdd.EnumerateInto(Span<int>, ZddEnumerationOrder)` / `Zdd.MaxSetSize`: アロケーションなしの
  集合列挙（M6-2、issue #137）。既存の `Sets()` は 1 集合ごとに `new int[]` するため、
  数百万集合を舐める用途では GC 圧が支配的になる（B9 で暫定案のまま残っていた (b)）。
  `EnumerateInto` は呼び出し側が渡したバッファへ書き込みながら使い回す `ref struct` 列挙子
  （`SetSpanEnumerator`）を返すので、`Sets()` と違って集合ごとに配列をアロケーションしない
  （内部の明示スタック/0-枝チェーンがバッファ長を超えて伸びるときは別で、そちらは
  `Array.Resize` による散発的なアロケーションが残る）。
  `IEnumerable<T>` を実装しない `ref struct` にしてあるのは制約ではなく安全装置——使い回される
  バッファが LINQ に渡ったり 1 反復を超えて保持されたりするのを型で防いでいる（これこそが
  `Sets()` を配列アロケーションする既定のままにした理由そのもの）。必要なバッファ長は新しい
  `IDdEval<int>`（`MaxSetSizeEval`）で求める `MaxSetSize`——この族に含まれる集合の最大要素数で、
  ∅ と `{∅}` はどちらも 0 になる（B20: `MaxSetSize` 未満のバッファは切り詰めて黙って壊れる代わりに、
  列挙を始める前に `ArgumentException` になる）。走査本体は既存 `SetEnumeration` の深さ優先探索を
  明示スタックの手書き状態機械に書き直したもの（`ref struct` はイテレータのローカル変数になれない
  ため `yield return` 版をそのまま再利用できない）。`Sets()` 側は変更しておらず、スタック/パスの
  ヘルパーを両者で共有する。変数 16 以下の全網羅・ランダム生成の両方で `Sets()` と要素・順序が
  完全一致すること、変数 10 万本の深い ZDD でスタックオーバーフローしないことを確認済み。
- `FrontierBuilder.TryBuild`: 上限超過を例外にしない構築（M6-3、issue #138）。
  `BuildOptions.MaxNodeCount` / `MaxFrontierSize` を超えたときだけ `false` を返し、
  `CancellationToken` によるキャンセルとスペック自身が投げた例外は `Build` と同じく
  例外のまま伝わる。`false` のとき `result` は `default(Zdd)` で、トップダウン展開は
  一時ノード表にしか書かないため `ZddManager` の状態（`NodeCount` を含む）は呼び出し前と
  不変。`options` は必須引数（`null` 不可）。固定 `struct` 状態スペック（`IDdSpec<TState>`）と
  配列状態スペック（`IArrayDdSpec`）の両方にオーバーロードを用意。
- `Zdd.MapItems(itemMap)`: 同じマネージャ内での項目写像、順序保存の高速経路（M6-4、issue #139、
  `docs/OPEN-QUESTIONS.md` B17）。CUDD の `Cudd_bddPermute` に相当する「変数の張り替え」が無く、
  辺順序を変えた `Graph` で組み直した族を元の順序で解釈する、といったことができなかった穴埋め。
  `level = VariableCount - item` なので item の大小関係がそのまま level の順序を決める——`itemMap`
  が **support 上で狭義単調増加**なら親子の level 順序が保たれるので、ボトムアップの明示スタックに
  よる 1 パス再構築で済む（`MapItemsOperation`、ノード id をキーにメモ化、O(ノード数)、非再帰）。
  `itemMap` は「旧 item → 新 item」の全域かつ単射な写像（長さ `Manager.VariableCount`）で、重複が
  あれば `ArgumentException`、範囲外なら `ArgumentOutOfRangeException`。support 外の要素の写像先は
  検査しない。単調でない `itemMap` は一般経路（M6-5）に回る。恒等写像は新しいノードを作らずそのまま
  自分自身を返す。変数 12 以下の総当たり照合（写像後の族が「各集合の要素を写した族」と一致すること）、
  `Count` が写像の前後で不変であること、変数 10 万の深い ZDD でスタックオーバーフローしないことを
  確認済み。
- `Zdd.MapItemsTo(target, itemMap)` / `Zdd.TransferTo(target)`: 一般（非単調）の項目写像とマネージャ間
  転送（M6-5、issue #140、`docs/OPEN-QUESTIONS.md` B19）。`itemMap` が support 上で狭義単調増加でない
  場合、M6-4 のボトムアップ再構築ではノードをそのまま作り直せない（子の level が親より大きくなって
  しまう）ので、ZDD の再帰的定義 `f = f0 ∪ (f1 × {v})` をそのまま使い、`map(f) = map(f0) ∪
  Change(map(f1), σ(v))` をノード id をキーにメモ化しながら後行順の明示スタックで計算する
  （`GeneralMapItemsOperation`、非再帰）。正当性は `σ` の単射性から従う: `f1` の部分木に現れる item は
  すべて `v` より大きく、`σ` は単射なので `σ(v)` は `map(f1)` の support に現れない——したがって
  `Change` は反転ではなく常に追加として振る舞う。計算量はノード数 ×（`Union` + `Change`）回で、
  順序保存経路の O(ノード数) より重いが指数的ではない。`Union` / `Change` を `target` マネージャ上で
  呼ぶだけで一般経路がそのままマネージャ間転送になるので、`MapItemsTo` 1 つで
  同一マネージャの一般置換とマネージャ間コピーの両方を賄う——同一マネージャ版 `MapItems` は
  `MapItemsTo(Manager, itemMap)` に委譲する。`TransferTo(target)` は `MapItemsTo(target, 恒等写像)` で、
  `target.VariableCount >= Manager.VariableCount` を要求し、足りなければ `ArgumentException`
  （B7「変数数は固定」の実質的な回避策——変数を増やしたければ大きいマネージャを新しく作って
  `TransferTo` する）。変数 12 以下の総当たり照合、ランダム置換での往復テスト
  （`MapItems(σ).MapItems(σ⁻¹) == f`）、順序保存経路と一般経路が同じ単調写像に対して完全に同じ結果
  （同じノード id）を返すこと、`TransferTo` した族が転送先マネージャで元と同じ `Count` /
  列挙結果を返すこと、反復実装であることの確認済み。
- `Zdd.AddSomeItem` / `RemoveSomeItem` / `RemoveAddSomeItems`（引数なし版と、対象を絞れる
  `items` 版）: Graphillion の `add_some_element` / `remove_some_element` /
  `remove_add_some_elements` 相当（M6-7、issue #142）。局所探索や「1 手違いの解」を数える
  用途で使う。新しい演算は足さず、既存の単項演算の合成で実装した:
  `RemoveSomeItem(f) = ⋃_{e∈items} OnSet(f, e)`、
  `AddSomeItem(f) = ⋃_{e∈items} Change(OffSet(f, e), e)`、
  `RemoveAddSomeItems(f) = ⋃_{e≠e'∈items} Change(OffSet(OnSet(f, e), e'), e')`。
  前 2 者は `items` の個数に線形（`O(|items|)` 回の族演算）だが、`RemoveAddSomeItems` は
  `e ≠ e'` の組ぶんだけ回すため `O(|items|²)` 回になる（Graphillion の実装も同じオーダーで、
  XML doc に明記した）。数千要素のユニバースでは既定の引数なし版（マネージャの全変数を使う）
  を避け、`items` 版で対象を絞ることを想定している。`SetSet<T>` と `GraphSet` にも同名の
  ラッパを追加した——`GraphSet` 側は直接 `Zdd` 代数で組み立てた族を、新しい
  `PrecomputedZddSpec`（既存 ZDD ノードをそのまま辿るだけの `IErasedGraphSpec`）でラップして
  返すので、`AddSomeItem()` などの結果にさらに `Including` / `Excluding` / `Larger` /
  `Smaller` を連鎖させても、フロンティア方式のフィルタと矛盾なく合成される（回帰テストで
  事後 `Intersect` と一致することを確認済み）。変数 12 以下の総当たり照合、`items` 版に全変数を
  渡したときの引数なし版との一致、`RemoveSomeItem(Base) == Empty` などの境界を確認済み。
- `Zdd.CostAtMost` / `CostAtLeast` / `CostEquals`: Graphillion の `cost_le` 相当のコストフィルタ
  （M6-8、issue #143）。既存の族に対して「重み合計が閾値以下／以上／ちょうど」の集合だけを残す
  操作で、新しいアルゴリズムは追加していない——`zdd.Subset(new LinearConstraintSpec(costs, op,
  bound))`（M3-5 の `ZddSubsetting`）そのものを 3 つの演算子ぶん薄くラップしただけ。事後フィルタ
  （族を丸ごと構築してから `Intersect`）と違い、フロンティア走査中に閾値を外れた枝を切るので、
  中間状態が「既存の族が実際に到達できる重みの組」だけに絞られ、コスト制約を単独で（＝族の外側で）
  構築するより中間 ZDD が小さくなる（回帰テストで確認済み）。`LinearConstraintSpec` は係数を
  `int[]` としてしか受け付けなかったため、新たに `ReadOnlySpan<long>` 版のコンストラクタを追加し
  （内部の係数配列自体を `long[]` に一般化——`int[]` 版はそこへ委譲するだけになった)、辺のコストが
  `int` に収まらない場合にも対応した（`double` 係数は丸めの扱いが自明でないため見送り、
  PLAN §8 の「Graphillion の語彙を .NET 命名規約に直して踏襲」の方針どおり `cost_le` ではなく
  `CostAtMost` と命名）。`GraphSet.CostAtMost(Func<Edge, long>, long)` / `CostAtLeast` /
  `CostEquals` は既存の `Filter(IErasedGraphSpec)` の仕組みに乗せてあるので、`Including` /
  `Excluding` / `Larger` / `Smaller` と同じフィルタ連鎖に組み込める。`SetSet<T>` にも同名の
  ラッパを追加した。負の係数、`bound` が到達可能な最小値／最大値ちょうどの境界、3 演算子すべてで
  事後フィルタ・`LinearConstraintSpec` を直接 `Subset` した結果との一致を確認済み。
- `SetUniverse<T>.Extend` / `SetSet<T>.ToUniverse` / `GraphSet.ToEdgeOrder`: ユニバース／辺順序を
  またぐ族の移送（M6-6、issue #141）。`SetSet<T>` の二項演算は `ReferenceEquals(Universe,
  other.Universe)` を要求する（B18: 暗黙昇格はしない——メモリ使用量が予測不能になるため）ので、
  別々に作った 2 つの族はこれまで合成不可能だった。`Extend(additionalElements)` は要素を追加した
  新しい `SetUniverse<T>` を返す——`ZddManager` の変数数は固定（B7）なので既存のマネージャは
  広げられず、新しいマネージャを作る（元のユニバースと族はそのまま生き続ける）。`ToUniverse(target)`
  は `Universe.Elements` の各要素を `target.IndexOf` で引いて `itemMap` を作り、
  `Zdd.MapItemsTo(target.Manager, itemMap)` を呼ぶだけ（M6-5 の土台の上）——`target` に無い要素が
  あれば、足りない要素名を列挙した `ArgumentException`。二項演算のユニバース不一致の例外メッセージも
  `ToUniverse` を案内するよう更新した。`GraphSet.ToEdgeOrder(target)` は `Graph.SourceOrder`
  （`WithEdgeOrder` / `Optimize` が残す辺の対応）から `itemMap` を作る——「`Optimize()` した順序で
  構築し、結果を元の順序で扱う」という実運用で最も多いパターンを 1 行にする。`Graph` に
  `SourceOrder` が無ければ `InvalidOperationException`、`target` の辺が対応関係と食い違えば
  （辺数不一致も含め）どこが食い違うかを添えた `ArgumentException`。別々に作った 2 つの
  `SetSet<T>` が `Extend` + `ToUniverse` 経由で合成できること、`Extend` 後の族を `ToUniverse` で
  戻すと元と同じ集合を表すこと、`Optimize()` した族を `ToEdgeOrder` で元の辺順序に戻すと辺集合が
  完全に一致することを確認済み。
- `GraphSet.ConnectedSubgraphs` / `SteinerTrees` / `Cuts` / `DegreeConstrained`（`int[]` 版と
  一様な `int` 版）/ `EdgeCovers` / `Knapsacks`: `Specs/` の 22 スペックのうち高レベル API から
  一切触れなかった辺の族を露出（M6-9、issue #144）。いずれも既存の `ConnectedSubgraphSpec` /
  `SteinerTreeSpec` / `CutSpec` / `DegreeConstraintSpec` / `KnapsackSpec` を、既存の
  `Generate<TSpec>`（`IArrayDdSpec` 用）にそのまま渡すだけの薄いラッパー。`KnapsackSpec` だけは
  `IDdSpec<long>` なので、`Filter` が使っている `StructSpecErased<TSpec, TState>` を生成側にも
  使う `Generate<TSpec, TState>` オーバーロードを新設した。`EdgeCovers` は専用スペックを新設せず
  `DegreeConstrained(graph, lo: 1, hi: graph.EdgeCount)` の別名にした——辺被覆は「全頂点の次数 ≥ 1」
  であり次数制約の特殊形にすぎないため（どの頂点の次数も `graph.EdgeCount` を超えることはないので、
  この上限は実質的にどの頂点も制約しない——無限大として使える）。いずれも `FrontierBuilder.Build` で
  対応するスペックを直接使った結果と一致すること、`EdgeCovers` は小グラフでの総当たり照合（全辺部分集合のうち全頂点を被覆する
  ものと一致）、`SteinerTrees` の `MinWeight` が M4-5 の既知の最小シュタイナー木と、`Cuts` の
  `MinWeight` が M4-6 の最大流最小カット定理による照合とそれぞれ一致すること、
  `Including` / `Excluding` / `Larger` / `Smaller` / `CostAtMost` と連鎖できることを確認済み。
- `GraphSet.VertexCovers` / `DominatingSets` / `Partitions` / `BalancedPartitions` / `Colorings`:
  `GraphSet` 露出②（M6-10、issue #145）。M6-9 の続きで、頂点の族と彩色を高レベル API から使える
  ようにした。`VertexCovers` / `DominatingSets` は `Cliques` / `IndependentSets` と同じ
  `GenerateVertexFamily` に流すだけの薄いラッパー（既存の `VertexCoverSpec` / `DominatingSetSpec`）。
  `Partitions(graph, k, minBlockSize, maxBlockSize)` は既存の `GraphPartitionSpec`（M4-6）をそのまま
  `Generate` に渡すだけ。`BalancedPartitions(graph, k, tolerance)` は `Partitions` の糖衣で、
  Graphillion の `balanced_partitions` 相当——`minBlockSize = floor(n/k · (1-tolerance))`、
  `maxBlockSize = ceil(n/k · (1+tolerance))`（`n` は頂点数）を計算して `Partitions` に委譲する。
  `minBlockSize` は 1 未満に丸まっても 1 に切り上げる（`tolerance` が大きいときの退化を防ぐ）。
  `tolerance` は非負かつ有限でなければ `ArgumentOutOfRangeException`。`Colorings(graph, k,
  representativesOnly)` は既存の `ColoringSpec`（M4-7）を使うが、`ColoringSpec` の変数エンコーディング
  が「頂点 × 色」（変数 index = `v * k + c`、vertex-major・color-minor）であるため、`SetSet<int>` で
  そのまま返すと利用者が復号しなければならない——そこで `SetUniverse<(int Vertex, int Color)>` を
  組んで返し、`foreach (var coloring in colorings) foreach (var (v, c) in coloring)` と自然に書ける
  ようにした。`VertexCovers` / `DominatingSets` は対応するスペックを直接 `FrontierBuilder.Build` した
  結果と `Count` が一致すること、`Partitions` は M4-6 の `GraphPartitionSpec` を直接構築した結果と
  一致すること、`BalancedPartitions` の境界計算（`n` が `k` で割り切れる／割り切れない、
  `tolerance = 0`、丸めが上下双方に効くケース、`minBlockSize` が 1 に切り上がるケース）と非負・有限で
  ない `tolerance` の例外を単体テストで固定したこと、`Colorings` の復号が「頂点ごとにちょうど 1 色」
  「隣接頂点が同色でない」正しい彩色になっていること（小グラフの総当たりと一致）、完全グラフでの
  彩色数が彩色多項式（下降階乗冪）と一致することを確認済み。
- `DegreeDistributionSpec` / `GraphSet.RegularGraphs` / `GraphSet.DegreeDistributions`: 次数系スペックの
  拡充（M6-11、issue #146）。`RegularGraphs(graph, k)` は専用スペックを新設せず
  `DegreeConstrained(graph, lo: k, hi: k)` の別名にした——k 正則は「全頂点の次数がちょうど k」であり
  次数制約の特殊形にすぎないため。`DegreeDistributionSpec(graph, counts)` は本物の新規スペックで、
  「次数 `d` の頂点がちょうど `counts[d]` 個」という制約（Graphillion の `degree_distribution_graphs`
  相当）。状態はフロンティア頂点ごとの現在次数に加え、確定済み頂点の次数分布を直接持たず
  `counts` の**残数**を減らす形で持つ——ヒストグラムをそのまま持つと状態が組合せ的に爆発するため。
  頂点がフロンティアから退場するとき、その頂点の確定次数 `d` について残数 `remaining[d]` を 1 減らし、
  負になったら ⊥（この判定が「残ヒストグラムが負になったら枝刈り」に当たる）。次数の上限は
  `counts.Length - 1`（それを超える次数にはバケツが無いため、辺を採る時点で即座に ⊥）。受理条件は
  全辺を決め終えた時点で全ての `remaining[d] == 0`——ただし `counts` の合計が頂点数と一致していて
  かつ一度も負にならなければ、頂点は 1 度だけ退場するので算数的に自動的に満たされる（このチェックは
  仕様書どおりの安全網として残した）。`counts` の合計が頂点数と一致しない場合は例外にせず空族を返す
  （`KnapsackSpec` が負の容量を空族として扱うのと同じ選択）。`IArrayDdSpec` として実装したので
  M3-2 の bit-packing にそのまま乗る。素朴な総当たり・独立実装した素朴 DP との一致、`K4` と
  Petersen グラフの既知の 3 正則部分グラフ（いずれも自分自身 1 個だけ）との照合、`counts` の合計が
  頂点数と食い違う場合の空族、残ヒストグラムが負になるケースの枝刈り、6×6 格子での代表的なフロン
  ティア幅の実測、`GetChild` が無割り当てであることを確認済み。
- `InducedSubgraphSpec` / `GraphSet.InducedSubgraphs`: 頂点誘導部分グラフ（M6-12、issue #147）。
  Graphillion の `induced_graphs` 相当——頂点部分集合 `S` を選んだとき、`S` 内の両端点を持つ辺は
  **すべて**選ばれていなければならない族（普通の部分グラフと違い「`S` 内に辺があるのに選ばない」を
  許さない）。`S` 自体はパラメータではなく、族は辺集合 `F` として一意に定まる——`F` が触れる頂点の
  集合が唯一あり得る `S` で、`F` に触れられない孤立頂点は `S` のどちら側にあっても結果は変わらない
  ため実質的に「圏外（`Out`）」扱いになる。連結性は要求しない（Graphillion 同様）——連結な誘導部分
  グラフが欲しければ `ConnectedSubgraphs` を別に構築して `Zdd.Intersect` で合成する。状態はフロン
  ティア頂点ごとに `InducedVertexState`（`Unknown` / `In` / `Out` の 3 値、bit-packing に乗る
  2 ビット）。辺を選ぶ場合は両端点を `In` に確定（すでに `Out` なら ⊥）。選ばない場合、両端点が
  ともに `In` になることだけが禁止だが、この判定を即座に行うと `Unknown` の内訳で状態が分岐して
  しまうため、判定を頂点が忘却されるまで遅延させる——忘却時に `Unknown` のままなら `Out` として
  確定させる。頂点 8 以下の総当たり照合（全頂点部分集合の誘導辺集合と一致）、遅延判定が正しいこと
  （選ばない辺の両端が後から `In` になるケースを含む）、孤立頂点・辺を持たないグラフの境界、
  `ConnectedSubgraphs` との `And` 合成が事後 `Intersect` と一致すること、`IArrayDdSpec` として
  反復実装であることを確認済み。
- `BicliqueSpec` / `GraphSet.Bicliques`（無指定版とサイズ固定 `(a, b)` 版）: 完全二部部分グラフ
  （M6-13、issue #148）。Graphillion の `bicliques` 相当——頂点を `SideA` / `SideB` / 未使用の
  3 値に分け、両側の**全ての**頂点対の間に辺があり、それが全て選ばれていることを要求する族。
  `InducedSubgraphSpec`（M6-12）と同じ 3 値状態の構造を再利用し、判定を忘却時まで遅延させる方針も
  共通（辺を選ぶ場合は両端点が異なる側でなければ ⊥、選ばない場合は両端点が異なる側に確定した時点で
  ⊥）。状態はパリティ付きの union-find（`BicliqueVertexState`）で頂点ごとの所属グループと相対サイド、
  連結性チェック用のグループ数を持つ。空の辺集合（両側 0 頂点の自明な biclique）は族に含まれる——
  `CliqueSpec` / `IndependentSetSpec` が空の頂点集合を含むのと同じ扱い。サイズ固定オーバーロード
  `BicliqueSpec(graph, a, b)` はグループごとの両側の残り人数を追加でカウントし、非固定版より状態・
  フロンティア幅が小さい（`a`/`b` の入れ替えはどちらの割り当ても受理する——2 つの側はラベル自体に
  意味がないため）。小グラフでの総当たり照合、完全二部グラフ `K_{a,b}` の既知値との照合、サイズ固定
  版が非固定版の部分族になっていること、`IArrayDdSpec` として反復実装であることを確認済み。
- `VertexGroupSpec` / `GraphSet.VertexGroups`: 頂点グループ連結制約（M6-14、issue #149）。
  Graphillion の `graphs(vertex_groups=...)` 相当——同じグループの頂点は必ず同じ連結成分に入り、
  違うグループの頂点は決して同じ連結成分に入らない、という制約。`ConnectedSubgraphSpec` の単一終端
  集合を複数の互いに排他な終端集合へ一般化したもので、複数端子対の同時配線や、各地区が分断されず
  かつ他地区と混ざらない地域割り制約に使う。どのグループにも属さない頂点は自由——単独でも、どれか
  1 つのグループの成分に加わってもよいが、2 つの異なるグループを橋渡しすることだけは禁止
  （Graphillion の `vertex_groups` の挙動に合わせた）。状態は `GraphPartitionSpec` と同系の comp
  配列に、各成分が確定しているグループ（未定を含む）を持たせたもの（`VertexGroupComponentState`）に
  加え、グループごとに「フロンティアに導入済みのメンバ数」と「現在そのグループに束縛されている独立
  した成分数」の 2 つのカウンタを持つ。2 つの成分が併合されるとき、異なるグループに確定した者同士
  なら ⊥、片方だけ確定していれば併合後の成分がそのグループを引き継ぐ。頂点が忘却されて成分が閉じる
  とき、その成分がグループに束縛されていれば、そのグループの全メンバがすでに登場済みかつこれが唯一
  の開いた成分でない限り ⊥ にする（そうでなければグループの誰かが二度と辿り着けない成分に取り残
  される）。グループが 0 個または全て空なら全部分グラフ（`PowerSetSpec` と同じ族）に、グループが
  1 個だけなら `ConnectedSubgraphSpec`（その終端集合）と一致する。小グラフでの総当たり照合、
  Graphillion の `vertex_groups` との結果一致（M5-2 の Graphillion 互換 I/O 経由で族そのものを突き
  合わせ）、グループが 1 個のときの `ConnectedSubgraphSpec` との一致、空グループ・1 頂点だけの
  グループの境界、`IArrayDdSpec` として反復実装であることを確認済み。
- `GraphConstraints` / `GraphSet.Graphs(graph, constraints)` / `GraphSet.Where(constraints)`: 統合ビルダ
  （M6-15、issue #150）。Graphillion の単一入口 `graphs(degree_constraints=, num_edges=, num_comps=,
  no_loop=, vertex_groups=, graphset=, ...)` 相当——今までも `spec.And(other)` で書けたが、Graphillion
  から移ってくる利用者にとって入口が分散していること自体が学習コストだった。`DegreeConstraints` /
  `EdgeCount` / `ComponentCount` / `NoLoop` / `VertexGroups` / `LinearConstraints` の各フィールドを、
  非デフォルトのものだけ既存の `IErasedGraphSpec`（`DegreeConstraintSpec` / `CardinalitySpec` /
  `ForestSpec` / `VertexGroupSpec` / `LinearConstraintSpec`）でラップし、`AndErasedSpec` で畳み込むだけ
  ——`Including` / `Excluding` / `Larger` / `Smaller` が既に使っている型消去の連鎖にそのまま乗るので、
  新しい合成基盤は要らなかった。`ComponentCount` だけは対応する既存スペックが無く、本物の新規スペック
  `ComponentCountSpec` を追加した——「連結成分数」という定義自体、Graphillion の `num_comps` は
  **孤立頂点を数えない**（辺を 1 本も持たない頂点は無視する）という、`ForestSpec(components:)`
  の「森の木の本数」（孤立頂点も 1 本の木として数える、スパニング前提の定義）とは異なる非自明な仕様
  で、状態はサイズではなく「一度でも辺を取ったか」を表す符号ビット（`ComponentCountComponentState`）
  だけで足りる——閉じた成分がその符号を持っているときだけ数える。母集合つきの `Where` は
  `Zdd.Subset`（M3-5）に落としてあるので、既存の族の構造と制約スペックを同じフロンティア走査で
  同時に辿る（母集合を全部作ってから絞るより中間 ZDD が小さい）。矛盾する制約（`EdgeCount = (5, 3)`
  など）は個々のスペックのコンストラクタ検証にそのまま乗せて例外にし、空族を黙って返すことはしない
  （`CardinalitySpec` 等、既存スペックの流儀と同じ）。制約を 1 つも指定しなければ全部分グラフ
  （`PowerSetSpec` と同じ族）になること、個別スペックを `And` で合成した結果と完全一致すること、
  `ComponentCount` の孤立頂点除外の定義、矛盾する範囲が例外になること、`Where` が事後フィルタと
  一致しつつ中間 ZDD が小さいことを確認済み。

## [0.5.0] - 2026-09-04

M5「I/O・メモリ管理」マイルストーン（[docs/PLAN.md](docs/PLAN.md) §12）の完了リリース。独自
バイナリ形式・Graphillion 互換テキスト形式によるシリアライズ、mark & sweep 方式のノード GC、
DOT 出力の拡張（状態ラベル・レベルラベル・部分表示）、CLI サンプルの拡充、DocFX による API
ドキュメントサイトの公開をもって、機能セットが一区切りついた。v1.0 に向けた API レビューの
下準備（public API 一覧の棚卸しと命名・一貫性メモ）は
[docs/api-review-notes.md](docs/api-review-notes.md) に記録し、公開 API の凍結（issue #60）に
引き継ぐ。

なおリリース直後に他ライブラリとの機能比較を行い、**v1.0 の API 凍結より前に入れておかないと
破壊的変更になる欠落**が見つかったため、v0.6「API 拡充と相互運用」と v0.7「有向グラフ対応」を
追加した。従来 M6 だった「安定化と公開」は M8 に繰り下がっている。

### Added

- `ZDD.Net.Io.ZddBinaryFormat`: 独自バイナリ形式による ZDD のシリアライズ（M5-1、issue #53）。
  `Write(Zdd, Stream)` / `Read(Stream, ZddManagerOptions?)`。ノード表（`Level`/`Lo`/`Hi`）を
  varint 圧縮してほぼそのまま書き出すため、構築よりも 1〜2 桁速く（`Write`）／小〜中規模の族では
  同様に速く（`Read`）保存・復元できる。読み込みは一意化表への再登録によって正準性を保証する
  ため、**フレッシュなマネージャへの読み込みならノード ID まで含めて元の族と一致する**
  （族としての一致だけでなく、正準形が保たれることをテストで確認済み）。マジックナンバー・
  版数・不正なノード参照（範囲外・循環・ゼロサプレス規則違反・レベル順序違反）はすべて
  `ZddFormatException` になり、クラッシュしない。版数管理あり（`FormatVersion`、将来の版は
  過去の版を読める方針を doc に明記）。本体 `PackageReference` は引き続き 0。詳細は
  [docs/benchmarks.md](docs/benchmarks.md) の M5-1 節（構築時間との比較、ファイルサイズの実測）
- `ZddManager.Collect()` / `Collect(params Zdd[] roots)` / `ZddManager.RootSet`: ノード GC
  （mark & sweep + コンパクション + ID リマップ、M5-3、issue #55）。参照カウントは採らず
  （ユーザ API が重くなるため）、明示 GC 方式にした（docs/PLAN.md §4.4）。`RootSet` に登録した
  族だけが `Collect()` を生き延び、ID が振り直された新しいハンドルとして読み直せる。登録していない
  古いハンドルを GC 後に使うと `ZddCollectedException` になる（マネージャの世代番号を
  `Zdd` ハンドルに焼き込んで検出——黙って壊れた結果を返さない）。mark は明示スタックによる反復実装
  （再帰しない）なので、変数 10 万本の深い ZDD でもスタックオーバーフローしない。コンパクション後は
  一意化表を再構築し、演算キャッシュは無効化、`2^U`（`PowerSetRoot`）のキャッシュは生存していれば
  リマップ、生存していなければ次回遅延再計算する。GC の統計（回収ノード数・削減率・所要時間）は
  `ZddStatistics` に追加した
- `ZDD.Net.Io.DotOptions`: DOT 出力の拡張——状態ラベル・レベルラベル・部分表示・スタイル
  （M5-4、issue #56）。`Zdd.ToDot(DotOptions?)` / `Zdd.WriteDot(TextWriter, DotOptions?)` の
  引数として渡す。既定（`null` または新規インスタンス）は今までの `ToDot()` と完全に同じ出力
  （`DotWriterTests.ADefaultDotOptionsInstanceReproducesThePlainOutput` で確認）
  - `StateLabels`（node id → ラベルの辞書）: 各ノードが「フロンティアのどの状態に対応するのか」を
    レベルラベルの下にもう 1 行として表示する。`FrontierBuilder.Build<TSpec, TState>` の状態記録版
    オーバーロードの `stateLabels` 出力をそのまま渡せる
  - `LevelLabel`（`Func<int, string>`）: レベル番号の代わりに意味のある名前（辺・頂点名など）を表示。
    `GraphSet.ToDot()` / `SetSet<T>.ToDot()` は明示しなければ `Universe.ElementAt` から自動的に
    供給する（辺なら `(u, v)`）
  - `MaxLevels` / `MaxNodes` / `FocusNodeId`: 上位 N レベルのみ・ノード数上限・指定ノードから
    到達可能な部分のみの部分表示。打ち切られた枝は単一の `truncated` マーカーへ張り替えられる
    ——巨大な ZDD でも出力サイズが上限内に収まる（走査自体を打ち切るので、上限を超えた分の
    ノードは訪問すらしない）
  - `NonTerminalShape` / `NonTerminalColor` / `ZeroEdgeStyle` / `OneEdgeStyle`: 色・形状・0-枝/1-枝の
    描き分けのカスタマイズ
  - ラベル文字列中の `"` `\` 改行は正しくエスケープされる（`DotWriterTests.StateAndLevelLabelsEscapeQuotesBackslashesAndNewlines`）。生成した DOT が実際に Graphviz
    (`dot -Tsvg`) に通ることをローカルで確認済み
- `ZDD.Net.Io.GraphillionTextFormat`: Python Graphillion の `setset.dump`/`dumps`/`load`/`loads`
  互換のテキスト形式による ZDD のシリアライズ（M5-2、issue #54）。`Write(Zdd, TextWriter)` /
  `Write(Zdd)`（`dumps` 相当）/ `Read(TextReader, int?, ZddManagerOptions?)` / `Read(string, ...)`。
  公式仕様が存在しないため、`pip install graphillion`（2.1）で実際にインストールして生成した
  出力を観察し、Graphillion 自身のソース（SAPPOROBDD 由来の `zdd.cc`）と突き合わせて形式を確定
  させた（推測で実装していない）。**レベルの向きの対応**（issue が最重要視した「読めるが中身が
  上下逆」バグの回避）は、Graphillion の 1 始まり根側基準の `elem` が本ライブラリの 0 始まり
  `item` インデックスと同じ向きであることに気づいたことで、単純なオフセット
  （`elem = item + 1`）に単純化できた。上下非対称な族（非正方格子の s–t パス族など、対称な族
  では検出できない）でテスト済み。ファイルには universe のサイズが記録されないため、`Read` の
  `variableCount` 引数で明示的に指定可能（省略時はファイルが実際に使っている最大の `elem` から
  推定）。壊れた入力（フィールド数不正・前方参照・`hi` が ⊥ 終端・レベル順序違反・重複ノード
  ID・終端 `.` 行の欠落や後続の余計な内容（`B`/`T` 単独ダンプでも同様——Graphillion 自身の
  loader は `B`/`T` の直後で読み込みを止めて `.` の有無を確認しないが、本形式では常に
  `.` で終わる約束にしている）・`variableCount` を超える `elem` など）はすべて
  `ZddFormatException` になる。実際に
  Graphillion 2.1 で生成したダンプを読み込んで本ライブラリ側の独立構築（`Count`・列挙した族の
  両方）と一致することを確認し、逆方向（本ライブラリの出力を Graphillion 側の `load()` で
  読めること）も手動確認済み——テストデータ・検証用 Python スクリプト・向きの対応・手動確認の
  手順はすべて [docs/graphillion-io.md](docs/graphillion-io.md) と
  `tests/ZDD.Net.Tests/TestData/Graphillion/` に記録した。本体 `PackageReference` は引き続き 0
- `ZDD.Net.Frontier.BuildOptions.RecordStates`: フロンティア構築時に「一時ノード → 状態」の対応を
  保持するオプション（M5-4、issue #56）。既定は無効で、無効時は `AddState` の `null` 判定 1 回以外
  オーバーヘッドが無い（`TopDownExpanderTests.DescribingStatesOnlyAllocatesWhenRequested`）。
  有効にして `FrontierBuilder.Build<TSpec, TState>(manager, spec, options, out stateLabels, describeState)`
  を呼ぶと、状態を文字列化した `stateLabels`（node id → ラベル）が返る。`describeState` を渡さなければ
  状態の `ToString()` を使う。記録の有無で構築結果（できあがる `Zdd`）は完全に一致する
  （`FrontierBuilderTests.RecordingStatesDoesNotChangeTheBuiltFamily` /
  `TopDownExpanderTests.RecordingStatesDoesNotChangeTheExpandedTable`）。並列展開でも記録は
  マージスレッド上の `AddState` だけを通るため決定的（`ParallelFrontierTests.RecordedStateLabelsAreTheSameRegardlessOfDegreeOfParallelism`）
- `samples/Zdd.Cli` にサブコマンドを追加した（M5-5、issue #57）: `grid-path <rows> <cols>`
  （`PathSpec`。既定の端点は対角なので OEIS A007764 が出る——`grid-path 7 7` は `575780564`）、
  `spanning-tree <graph-file>`（`SpanningTreeSpec`）、`partition <graph-file> <k>`
  （`GraphPartitionSpec`、`--min-block`/`--max-block`）、`matching <graph-file>`
  （`MatchingSpec`、`--perfect`）。既存の `--family` デモは `family` サブコマンドに移した。
  グラフ系サブコマンドは共通オプションを持つ: `--edge-order`（`bfs`/`dfs`/`grid`/`beam`）、
  `--progress`（レベルごとのフロンティア幅を stderr へ）、`--dot`、`--estimate`
  （構築せずに `Graph.EstimateMaxFrontierSize` だけを出す）、`--save`/`--load`
  （`ZddBinaryFormat` のラウンドトリップ、M5-1）、`--sample n`、`--min-weight <path>`
  （`Zdd.MinWeight` を叩く）。グラフファイルは拡張子から DIMACS / 素な辺リスト / 本ライブラリの
  簡易テキスト形式を判別して読む。不正な引数・存在しないファイル・壊れた形式はすべて
  `error: <message>` 1 行で終わり、スタックトレースを出さない。CI のスモークテストで
  `grid-path 7 7 → 575780564` と `--save`/`--load` ラウンドトリップを検証している
- API ドキュメントサイト（M5-6、issue #58）: `docs/docfx.json` により、手書きガイド
  （`api-guide.md`/`frontier-guide.md`/`tutorial.md`/`benchmarks.md` ほか docs/ 配下全体）と、
  `src/ZDD.Net` の XML doc コメントから `docfx metadata` が起こす全 public API のリファレンスを
  1 つのサイトにまとめた。`.github/workflows/docs.yml` が main への push ごとに再生成して
  GitHub Pages（<https://wix-diesel.github.io/ZDD.Net/>）へ公開し、pull request ではビルドだけ
  行う（公開はしない）。`docfx build --warningsAsErrors` により、手書きガイド・API リファレンス
  のリンク切れ（DocFX の既定のリンク検証が拾う）はビルド自体を失敗させる。CS1591（XML doc
  コメント欠落）は M0-1 から `GenerateDocumentationFile=true` と `TreatWarningsAsErrors=true` が
  効いており、`.editorconfig` の抑制は `tests/**/*.cs` に限定されている（テスト以外に
  suppress は無い）ため、全 public API に doc があることは棚卸しするまでもなく既にビルドで
  強制されていた。今回の作業は主要な型・メソッド（`Zdd`/`ZddManager`/`SetSet<T>`/
  `SetUniverse<T>`/`FrontierBuilder`/`IDdSpec<TState>`/`Graph`/`ZddBinaryFormat`/
  `GraphillionTextFormat`、`GraphSet` は M3-8 で既に持っていた）に動作確認済みのコード片
  （各ガイド・サンプルプロジェクトで CI が実行しているものと同じ内容）で `<example>` を追加した
  こと。ドキュメント本文からソースへの相対リンク（`../samples/...`・`../src/...`・
  `../bench/...`・`../tests/...`・`../../CHANGELOG.md`）は、DocFX が生成する別サイトの URL 構造
  では解決できないため、GitHub の `blob`/`tree` の絶対 URL に置き換えた（GitHub 上でリポジトリを
  直接読む場合も同じリンクがそのまま機能する）。本体 `PackageReference` は引き続き 0
  （DocFX はドキュメント生成専用のビルドツールとしてグローバルにインストールするだけで、
  どの `.csproj` からも参照しない）

### Changed

- `Zdd` ハンドルにマネージャの世代番号を追加した（引き続き 16 バイト固定）。同じ ID でも世代が
  違えば別の族として扱う（`Equals`/`GetHashCode` が世代も見る）ので、GC で ID が再利用されても
  古いハンドルが偶然一致してしまうことがない

## [0.4.0] - 2026-09-03

M4「性能と残りのスペック」マイルストーン（[docs/PLAN.md](docs/PLAN.md) §12）の完了リリース。
性能改善（キャッシュ調整・SIMD 化・並列構築）を実測付きで積み上げ、残っていたグラフ系スペック
（連結部分グラフ・シュタイナー木・分割・カット・彩色・オートマトン）で M0〜M4 の機能面が出揃った。
Graphillion・TdZdd との比較で `docs/PLAN.md` §10 の性能目標を全て達成し（M4-8）、v0.5（I/O・GC）と
v1.0（安定化・公開）へ進む前提が整った状態になる。

### Added

- `bench/comparison`: Graphillion（Python + C++ コア）・TdZdd（生 C++・ヘッダオンリー）との性能比較
  （M4-8、issue #51）。`docs/PLAN.md` §10 の 3 つの性能目標——9×9 格子 1 秒以内・11×11 格子
  60 秒以内/メモリ 8 GB 以内・Graphillion 比 3 倍以内（最終的に 2 倍以内）——を全て達成:
  9×9 格子（3,266,598,486,981,642 通り）206.12 ms（目標の約 1/5）、11×11 格子
  （1,568,758,030,464,750,013,214,100 通り）3,666.40 ms・プロセスピーク RSS 約 476 MB（目標の
  約 6%）、Graphillion 比は測定した 8 ケース全てで 0.017x〜0.75x（3 倍どころか全ケースで
  Graphillion を下回った）。一方 TdZdd（P/Invoke・GC を持たない生 C++）には全ケースで 2.0x〜21.4x
  遅い——「C++ に勝つ」ではなく「同じオーダーで .NET から依存なしに使える」という PLAN.md §0 の
  位置づけと整合する結果として正直に記録した。8×8 格子で Graphillion が外れ値的に遅くなる現象
  （複数回再実行で再現、原因は Graphillion 内部のブラックボックス）も分析付きで記載。
  詳細は [docs/benchmarks.md](docs/benchmarks.md) の M4-8 節、比較対象の入手・ビルド手順は
  `bench/comparison/README.md`
- `ZDD.Net.Specs.ColoringSpec` / `ZDD.Net.Specs.DfaSpec`: 彩色とオートマトン（M4-7、issue #50）
  - `ColoringSpec(graph, k, representativesOnly: false)`: グラフの `k` 彩色の族。**変数は「頂点 × 色」の
    組**（変数 `v*K+c`）——このネームスペースの他のグラフ系スペックが変数を辺または頂点にしているのとは
    異なる割り当て。フロンティアの導入・忘却は `VertexFrontierManager` をそのまま再利用する
    （1 頂点 1 変数のときとトポロジが同じため）。`representativesOnly: true` にすると、色の付け替えで
    同型になる `k!` 通りの解を代表解 1 つに絞れる（`Complete(n)` のように全彩色が `k` 色すべてを使う
    グラフでは、解数がちょうど元の `1/k!` になることをテストで確認）
  - `DfaSpec(transitions, initialState, acceptStates, length, pruneDeadStates: true)`: 決定性有限
    オートマトンが受理する固定長 2 値文字列の族。状態遷移表がそのまま `GetChild` になる、グラフに
    限らない汎用スペック（docs/PLAN.md §7.2）。既定で有効な `pruneDeadStates` は、受理状態に到達不能な
    状態への遷移を構築前に逆向き到達可能性で前計算し、`GetChild` がそこへ着地した時点で即座に
    `DdResult.False` を返す——できあがる族は変わらず、構築中の一時ノード数だけが減る
  - どちらも `readonly struct`・`GetChild` 非アロケーション。`ColoringSpec` は `Complete`/`Cycle`/`Path`
    の彩色多項式の閉形式、`DfaSpec` は全入力列の総当たりシミュレーションと照合済み
    （`tests/ZDD.Net.Tests/Specs/ColoringSpecTests.cs` / `DfaSpecTests.cs`）
- `ZDD.Net.Specs.GraphPartitionSpec` / `ZDD.Net.Specs.CutSpec`: 分割・カット（M4-6、issue #49）
  - `GraphPartitionSpec(graph, k, minBlockSize, maxBlockSize)`: 「残す」辺で連結な `K` 個のブロックに
    分割し、各ブロックの頂点数が `[minBlockSize, maxBlockSize]` に収まる辺集合の族（区割り問題）。
    次数 0 の孤立頂点はそれぞれ独立したサイズ 1 のブロックとして自動的に数える。`K == 1` かつ
    バランス範囲が非拘束のときは、全頂点を terminal にした `ConnectedSubgraphSpec` と一致する族になる
  - `CutSpec(graph, s, t, minimalOnly: false)`: `s`–`t` カット（除去すると `s`/`t` が別成分になる辺集合）
    の族。既定は全てのカット、`minimalOnly: true` で極小カット（真部分集合がカットにならないもの）
    だけに絞れる——極小カットは `(S, T)` 頂点二分割の辺境界と一対一対応することを利用し、
    成分＋`s`/`t` 側フラグの状態に加えて、決定済みの全ての辺（採用・カット問わず）にわたる
    パリティ Union-Find で「他の辺経由で到達可能と分かっている冗長な決定」を検出する
  - どちらも小規模グラフでの総当たり照合、`CutSpec` はさらに `minimalOnly` の族が全カットの族の
    `Minimal()` と一致すること、最小重みカットが独立実装した最大流（最大流最小カット定理）と
    一致することを確認済み（`tests/ZDD.Net.Tests/Specs/GraphPartitionSpecTests.cs` / `CutSpecTests.cs`）
- `ZDD.Net.Specs.SteinerTreeSpec`: シュタイナー木（M4-5、issue #48）
  - `SteinerTreeSpec(graph, terminals)`: terminals を含む連結・非巡回な部分グラフで、全ての葉が
    terminal であるもの（標準的なシュタイナー木の定義）の族。状態は `ConnectedSubgraphSpec`
    （M4-4）の成分配列＋端子カウンタに、フロンティア頂点ごとの飽和次数カウンタ
    （`DegreeConstraintSpec` と同様の設計）を足したもの。辺を採るとき両端が既に同一成分なら
    閉路として即棄却し、頂点を忘れるとき最終次数がちょうど 1（葉）かつ非 terminal なら棄却する
    ——「非 terminal の葉を禁止する」この 1 本の条件だけで、terminal を含まない孤立した枝も
    自動的に弾かれる（そのような枝は必ず非 terminal の葉を 2 つ以上持つため）
  - `Zdd.MinWeight` と組み合わせれば最小シュタイナー木が求まる（族自体は重み最小とは限らない
    シュタイナー木も全て含む）。terminal が 2 個の全族は `PathSpec` の `s`–`t` パスと、全頂点が
    terminal の全族は `SpanningTreeSpec` の全域木と**厳密に一致**することをテストで確認済み
    （`ConnectedSubgraphSpec.Minimal()` 経由の比較より強い等価性チェック。
    `tests/ZDD.Net.Tests/Specs/SteinerTreeSpecTests.cs`）
- `ZDD.Net.Specs.ConnectedSubgraphSpec`: 連結部分グラフ（M4-4、issue #47）
  - `ConnectedSubgraphSpec(graph, terminals)`: terminals が全て同じ連結成分に入る辺集合の族。
    `SpanningTreeSpec`（M2-9）の「全頂点が 1 つの成分」を「指定した頂点だけが 1 つの成分」へ
    一般化したもので、`SteinerTreeSpec`（M4-5）・`GraphPartitionSpec`（M4-6）が積み上がる基礎になる。
    状態は `SpanningTreeSpec` と同じ成分配列（`ConnectedComponentState`。符号ビットで
    「その成分が terminal を含むか」を表す）に、terminal の入り繰りを数える 2 つの末尾カウンタを
    足したもの。`SpanningTreeSpec` と異なり同一成分どうしの辺は棄却しない（閉路も連結部分グラフとして
    正当）
  - terminal 0〜1 個では全ての辺部分集合が族に含まれ（`PowerSetSpec` と一致）、全頂点が terminal な
    らば `SpanningTreeSpec` の全域木が `Zdd.Minimal()` の要素になり、terminal が 2 個ならば
    `Zdd.Minimal()` は `PathSpec` の `s`–`t` パスと一致することをテストで確認済み
    （`tests/ZDD.Net.Tests/Specs/ConnectedSubgraphSpecTests.cs`）
- `ZDD.Net.Frontier.BuildOptions.MaxDegreeOfParallelism`: フロンティア構築のレベル内展開を
  `Parallel.For` で並列化（M4-3、issue #46）。既定値は `Environment.ProcessorCount`、`1` で常に
  逐次実行になる。幅が閾値（既定 2048 状態）を超えた水準だけが並列パスを通り、それ未満は従来どおり
  逐次実行——**並列度をいくつにしても、できあがる ZDD のノード ID は逐次実行と完全に一致する**
  （`tests/ZDD.Net.Tests/Frontier/ParallelFrontierTests.cs` で検証）。`CancellationToken` は
  並列実行中も観測され、`GetChild` の例外は 1 パーティションだけの失敗なら元の例外型のまま、
  複数パーティションが同時に失敗すれば `AggregateException` のまま伝播する。実測（4 コア）では、
  このライブラリの組み込みスペックは状態表への登録コストが `GetChild` 自体より重いため大きな
  高速化は見込めない（0.9x 前後）一方、`GetChild` 自体が重いスペックでは実際に効く（合成ベンチで
  2.4x）——詳細と設計判断は [docs/benchmarks.md](docs/benchmarks.md) の M4-3 節、使い方は
  [docs/frontier-guide.md](docs/frontier-guide.md) §6.3 を参照

### Changed

- `ZDD.Net.Core.OperationCache`: サイズを自動拡大する際、旧実装は全エントリを捨てて空の配列に
  差し替えていたが、生きているエントリを新しいテーブルへ移行（rehash）するように変更（M4-1、
  issue #44）。`bench/ZDD.Net.Benchmarks -- cache-tuning`（複数のトップレベル演算が呼び出しを
  またいで部分問題を共有するワークロード）で実測: ノード数が増え続けるインクリメンタルな構築では
  `Tune` がほぼ毎回拡大を発動し、旧実装は拡大のたびにヒット率をほぼ 0 に戻していた。移行に
  変えた結果、代表ケースで実行時間 15〜20% 改善、全ケースでヒット率が改善（詳細は
  [docs/benchmarks.md](docs/benchmarks.md) の M4-1 節）
- `ZDD.Net.Core.OperationCache`: スロット計算を、下位ビットの直接マスクから
  `UniqueTable` / `OperationWorkspace` と同じ Fibonacci hashing
  （`Hashing.IndexForPowerOfTwo`）に統一。実測ではヒット率・実行時間とも有意差はなかった
  （`Hashing.Mix64` が既に十分な雪崩効果を持つため）が、3 つの表で流儀を揃えた
- `ZDD.Net.Internal.Hashing.Combine(ReadOnlySpan<byte>)`: フロンティア法の状態表
  （`ArrayLevelStateTable.GetOrAdd`）が呼ぶ状態ハッシュを、256 バイト以上の入力では
  `Vector256`/`Vector128`（`System.Runtime.Intrinsics`、ハードウェア非対応環境では自動的に
  元のスカラー実装へフォールバック）で計算するように変更（M4-2、issue #45）。256 バイト未満は
  ベクトル化のオーバーヘッドが上回ると実測されたため、元のスカラー経路のまま。代表ベンチで
  フロンティアが広いケースは実行時間 7〜17% 改善、狭いケースは分岐自体が変わらないため無変化
  （詳細は [docs/benchmarks.md](docs/benchmarks.md) の M4-2 節）。状態比較
  （`ReadOnlySpan<byte>.SequenceEqual`）は .NET ランタイム自体が既にベクトル化しており、
  手書き実装を測っても上回らなかったため変更なし

## [0.3.0] - 2026-09-03

M3「数千辺への対応と高レベル API」マイルストーン（[docs/PLAN.md](docs/PLAN.md) §12）の完了リリース。
ROADMAP の受け入れ条件「数千辺の実グラフで経路数え上げが完走する」を実データ規模で確認し
（[docs/benchmarks.md](docs/benchmarks.md) の M3-11 節）、機能面の中核が揃った。M4 以降は
性能改善・スペック拡充・パッケージング整備が中心になる。

### Added

- `docs/tutorial.md`: 「格子グラフの s–t パスを数える」から始まり、フィルタ・サンプリング・
  辺順序の最適化を経て、**実グラフ（DIMACS 形式）を読み込んで解くところまで**を一直線に辿れる
  チュートリアル（M3-11、issue #43）。コード片は `samples/Zdd.Tutorial` として実際に動き、
  CI が毎回実行する
- `bench/ZDD.Net.Benchmarks`: `-- real-graph` モード（`RealGraphReport`）——数千〜数万辺の
  道路網・電力網に近い実データ規模のグラフで s–t パス数え上げが完走するかどうかの記録
  （M3-11、issue #43）。`ZDD.Net.Io.DimacsGraph` で実際にテキストへ書き出してから読み直す
  ラウンドトリップを経由する。結果は [docs/benchmarks.md](docs/benchmarks.md) の M3-11 節:
  疎な道路網（k=2 最近傍）は 4 万辺を超えても数秒で完走する一方、密な道路網（k=4）は
  `Bfs` で幅を狭くしても迂回路の多さから 2,430 辺の時点で完走しない——フロンティア幅の見積りは
  状態数が指数的に増えうる「肩」の広さであって状態数そのものではない、という境界を正直に記録した
- `ZDD.Net.Io`: グラフ入出力 `DimacsGraph` / `EdgeListGraph` / `SimpleTextGraph`（M3-10、issue #42）
  - いずれも `TextReader` / `TextWriter` を受ける形（`string` を受け取る簡易オーバーロードも用意）。
    `System.Text.Json` 等への依存は追加せず、本体プロジェクトの `PackageReference` は 0 のまま
  - `DimacsGraph`: DIMACS 形式（`p edge <頂点数> <辺数>` ヘッダ、`e` 辺行、`c` コメント行）の読み書き。
    **DIMACS は頂点 1 始まり**、本ライブラリは 0 始まりなので、この変換を `Read`/`Write` の 1 箇所に
    閉じ込めている
  - `EdgeListGraph`: 頂点数のヘッダ行＋ 1 行 1 辺（空白/カンマ区切り、0 始まり）のエッジリスト形式。
    ヘッダ行があるのは、辺を持たない末尾の頂点までラウンドトリップさせるため
  - `SimpleTextGraph`: 本ライブラリ独自の簡易テキスト形式（`graph`/`vertex`/`edge` 行）。頂点ラベルを
    保持できる唯一の形式で、`Read` は `Graph` とラベル列を束ねた `LabeledGraph` を返す
  - パースエラーは行番号付きの `GraphFormatException`（ヘッダと実際の辺数の不一致、範囲外頂点、
    壊れた行を検出）。コメント行・余分な空白・CRLF/LF 混在・末尾改行なしは全形式で許容
  - ラウンドトリップ（頂点数・辺数・辺の順序が一致）と数千辺規模の読み込みをテストで確認。
    読み込んだグラフで `GraphSet.Paths` / `Trees` / `Matchings` が動くことも統合テストで確認済み
- `ZDD.Net.Sets`: `SetSet<T>` / `SetUniverse<T>` — 任意要素型の族ラッパ（M3-8、issue #40）
  - `Zdd` は変数が `int` index だが、`SetSet<T>` は要素 `T` ↔ index の対応を `SetUniverse<T>`
    （`IEqualityComparer<T>` 付き）に肩代わりさせ、「ZDD を知らない .NET 開発者が使える入口」にする
  - 同じ `SetUniverse<T>` インスタンスを共有する `SetSet<T>` 同士でしか演算できない
    （別マッピング同士は `ArgumentException`。`ZddManager` 不一致と同じ扱い）
  - `IEnumerable<IReadOnlySet<T>>` を実装するが `ICollection` は実装しない。`Count`（`BigInteger`
    プロパティ）が LINQ の `Count()` 拡張メソッドと曖昧参照にならないことをテストで確認済み
    （`LongCount()` / `CountApprox` も用意）
  - 生成: `SetSet<T>.FromSets(...)` / `SetSet<T>.PowerSet(...)` / `SetSet<T>.Empty(universe)`
  - 集合演算（`Union` / `Intersect` / `Difference` / `SymmetricDifference` と演算子）、
    家族代数（`Product` / `Quotient` / `Meet` / `SupersetsOf` / `SubsetsOf` / `Maximal` / `Minimal`）、
    問い合わせ（`Contains` / `ElementAt` / `IndexOf` / `Sample` / `MaxWeight` / `MinWeight` /
    `TopK` / `Probability`）はすべて対応する `Zdd` の操作へ委譲する薄いラッパー
  - 元の `Zdd`（`SetSet<T>.Zdd`）と要素マッピング（`SetSet<T>.Universe`）へのアクセスも公開
- `ZDD.Net.Specs`: 頂点系スペック `IndependentSetSpec` / `CliqueSpec` / `VertexCoverSpec` /
  `DominatingSetSpec`（M3-6、issue #38）
  - ここまでの辺の族（変数 = 辺）とは違い、**変数は頂点**。`ZddManager` の `variableCount` は
    `graph.EdgeCount` ではなく `graph.VertexCount` に合わせる
  - `ZDD.Net.Graphs.VertexFrontierManager`: `FrontierManager` の頂点版。頂点は決定された瞬間に
    フロンティアへ入り（下位隣接頂点との照合・更新に使うため）、自分の最大添字の隣接頂点が
    決定された直後（隣接頂点がすべて自分より小さい添字なら自分の決定の直後）に抜ける。
    スロットの再利用と `MaxFrontierSize` の求め方は `FrontierManager` と同じ考え方
  - `IndependentSetSpec` / `VertexCoverSpec`: フロンティア頂点ごとに選択フラグ 1 bit。
    `VertexCoverSpec` は `IndependentSetSpec` の状態・判定を反転させた形（補集合が独立集合になる）
  - `CliqueSpec`: 独自のフロンティア判定は持たず、補グラフ上の `IndependentSetSpec` に委譲する
    薄いラッパー（頂点番号・変数順序は元のグラフのまま、辺だけが補グラフのものになる）
  - `DominatingSetSpec`: フロンティア頂点ごとに「選択／未選択だが被支配済み／未選択で未支配」の
    3 値。頂点が forgotten になる瞬間、未支配のままなら ⊥ に落とす
  - 素朴な総当たり列挙・bitmask DP との一致に加え、`Path(n)` の独立集合数がフィボナッチ数、
    `Cycle(n)` がリュカ数、`Complete(n)` が `n + 1`、`Complete(n)` のクリーク数が `2^n` になる
    ことを確認。`VertexCoverSpec` の補集合が `IndependentSetSpec` と一致することもテスト済み
- `ZDD.Net.Frontier`: スペック合成 `AndSpec` / `OrSpec` と `Zdd.Subset(spec)`（M3-5、issue #37）
  - `spec1.And<SpecA, StateA, SpecB, StateB>(spec2)` / `.Or<...>(...)`: 2 つのスペックの状態を
    タプルにして同時展開し、中間 ZDD を経由せずに交差／和の族を直接構築する
    （`TState` は `where` 節にしか現れないため型引数の明示が必要——`FrontierBuilder.Build<TSpec, TState>`
    と同じ事情）。合成スペック自体も `IDdSpec<TState>` なので `a.And(b).And(c)` のように何段でも積める
  - **水準の同期**: 2 つのスペックが互いに異なる水準を次の決定点にしている（どちらかが水準を
    飛ばした）ときの規約を決めた——飛ばした側にとってその間のアイテムは暗黙に「入れない」なので、
    もう一方がそこで「入れる」を選ぶと、`And` は全体を ⊥ に、`Or` はその側だけを以降ずっと
    不成立（dead）にする
  - `ZddSpec`: 既存の `Zdd` をスペックとして扱うアダプタ。`zdd.Subset(spec)`
    （TdZdd の `zddSubset` 相当）は `ZddSpec` と `spec` の `AndSpec` として実装されている
  - `ArrayDdSpecAdapter<TSpec>`: 可変長状態の `IArrayDdSpec`（`PathSpec` など）を
    `IDdSpec<int[]>` へ橋渡しし、固定長 struct 状態のスペックと合成できるようにする
    （分岐ごとに配列を複製することで、参照型状態のフィールドコピーが枝を共有してしまう問題を防ぐ）
  - `TSpec` はどこも型引数 + `struct` 制約で受けており、interface 型としては受けていない
    （docs/frontier-guide.md §6.2 の方針どおり）
  - 効果（[docs/benchmarks.md](docs/benchmarks.md) の M3-5 節。
    `dotnet run -c Release --project bench/ZDD.Net.Benchmarks -- spec-composition` で再現できる）:
    固定長 struct どうしの合成はピーク幅・一時ノード数・実行時間のすべてで事後フィルタに勝る
    （実行時間で 3.4 倍）。可変長配列を橋渡しする場合は一時ノードのピークで事後フィルタに
    劣ることがあるが、**構築後にマネージャへ残る中間結果**は 10×10 格子で約 1,900 倍小さい
    ——事後フィルタが最後まで保持する「使われなかった大きな中間 ZDD」を直接構築は作らずに済む
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
