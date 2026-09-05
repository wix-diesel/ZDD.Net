# Changelog

このプロジェクトの変更点は本ファイルに記載する。フォーマットは
[Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) に、バージョニングは
[Semantic Versioning](https://semver.org/lang/ja/) に準拠する。

v1.0 までは API 未確定のプレリリース版として公開する（[docs/PLAN.md](docs/PLAN.md) §13）。

## [Unreleased]

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
  辺順序を変えた `Graph` で組み直した族を元の順序で解釈する、といったことができなかった穴埋め
  （一般の置換とマネージャ間転送は別 PR にする M6-5 で追加）。`level = VariableCount - item` なので
  item の大小関係がそのまま level の順序を決める——`itemMap` が **support 上で狭義単調増加**なら
  親子の level 順序が保たれるので、ボトムアップの明示スタックによる 1 パス再構築で済む
  （`MapItemsOperation`、ノード id をキーにメモ化、O(ノード数)、非再帰）。`itemMap` は「旧 item →
  新 item」の全域かつ単射な写像（長さ `Manager.VariableCount`）で、重複があれば `ArgumentException`、
  範囲外なら `ArgumentOutOfRangeException`。support 外の要素の写像先は検査しない。単調でない
  `itemMap` を渡すと `NotSupportedException`（一般経路は M6-5 で追加予定）。恒等写像は新しいノードを
  作らずそのまま自分自身を返す。変数 12 以下の総当たり照合（写像後の族が「各集合の要素を写した族」と
  一致すること）、`Count` が写像の前後で不変であること、変数 10 万の深い ZDD でスタックオーバー
  フローしないことを確認済み。
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
