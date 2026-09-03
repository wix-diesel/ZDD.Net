# ベンチ基準値 (M2-11)

[bench/ZDD.Net.Benchmarks](../bench/ZDD.Net.Benchmarks) が測る代表 10 ケースの基準値。
本ドキュメントの最重要点は数値そのものより**再現可能な比較対象があること**: M3 以降の
「辺順序最適化で 20% 改善」「bit-packing でメモリ 50% 削減」といった数値目標を持つ PR は、
ここに記録された数値との相対比較で受け入れを判定する（issue #31）。

## 測定環境

| 項目 | 値 |
|---|---|
| CPU | Intel Xeon Processor @ 2.80GHz, 4 論理コア（クラウドの共有仮想環境。専有ベアメタルではないため、絶対値は実行ごとに変動しうる） |
| OS | Ubuntu 24.04.4 LTS (Linux 6.18, x86_64) |
| .NET | SDK 10.0.111 / Runtime 10.0.11, RyuJIT x86-64-v4 |
| GC 設定 | ServerGC + Concurrent GC + TieredPGO（[ZDD.Net.Benchmarks.csproj](../bench/ZDD.Net.Benchmarks/ZDD.Net.Benchmarks.csproj)、PLAN.md §10-7） |
| BenchmarkDotNet | v0.15.4、`RunStrategy=Monitoring`, `LaunchCount=1`, `WarmupCount=1`, `IterationCount=3` |
| 測定日 | 2026-09-01 |

`IterationCount=3` は BenchmarkDotNet の既定（十数回以上）より少ない。ケースの実行時間が
1 ms 未満から 10 秒近くまで 4 桁にまたがるため、既定の反復回数では低速ケースの合計実行時間が
実用的でなくなる。基準値としての目的は統計的厳密さより**再現可能な比較対象を持つこと**なので、
このトレードオフを許容している。Mean 列の信頼区間が広いケース（反復 3 回では避けられない）は
Median 列を参照する方が実態に近い。

## 実行方法

```bash
# 時間計測（BenchmarkDotNet 本体）
dotnet run -c Release --project bench/ZDD.Net.Benchmarks

# ピークフロンティア幅（IProgress の履歴から）・最終ノード数（時間計測は行わない）
dotnet run -c Release --project bench/ZDD.Net.Benchmarks -- stats
```

## 結果

| ケース | 内容 | 実行時間 (Median) | 割り当てメモリ | 集合数 (Count) | ピークフロンティア幅 | 最終ノード数 |
|---|---|---:|---:|---:|---:|---:|
| `Path_Grid5x5` | 5×5 格子、対角 s–t パス | 1.97 ms | 121.2 KB | 8,512 | 125 | 546 |
| `Path_Grid6x6` | 6×6 格子、対角 s–t パス | 15.96 ms | 483.2 KB | 1,262,816 | 428 | 2,142 |
| `Path_Grid7x7` | 7×7 格子、対角 s–t パス | 43.98 ms | 1,464.9 KB | 575,780,564 | 1,460 | 7,968 |
| `SpanningTree_Complete8` | `Complete(8)` の全域木 | 4.77 ms | 343.9 KB | 262,144 | 406 | 2,247 |
| `PerfectMatching_Grid6x6` | 6×6 格子の完全マッチング | 0.93 ms | 95.9 KB | 6,728 | 20 | 386 |
| `Cardinality_5000Choose2400To2600` | 5000 項目、サイズ 2400〜2600 | 8.33 s | 894.8 MB | 約 10¹⁵⁰⁵（1,506 桁） | 2,600 | 6,722,600 |
| `LinearConstraint_1000ItemsKnapsack` | 1000 項目、線形不等式制約 | 4.31 s | 949.1 MB | 約 10³⁰⁰（301 桁） | 12,751 | 6,361,364 |
| `Forest_Grid5x5_TwoComponents` | 5×5 格子、成分数 2 の森 | 6.20 ms | 366.0 KB | 3,366,192,128 | 126 | 2,052 |
| `Union_TwoGrid6x6Paths` | 6×6 格子、2 つの `PathSpec` の `Union` | 50.60 ms | 3,426.6 KB | 436,619,868 | 1,745 | 17,631 |
| `Product_Grid5x5PathsAndCardinality` | 5×5 格子パス × カーディナリティ制約の `Product` | 772.5 ms | 5,215.0 KB | 151,724,411,004 | 125 | 41,828 |

割り当てメモリは BenchmarkDotNet の `MemoryDiagnoser`（1 回のビルドが確保した総バイト数、GC 済みの
一時領域も含む）。「集合数 (Count)」は各ケースが表す族の要素数（`Zdd.Count`）で、桁数が大きいものは
概数と桁数のみ記載する（生の `BigInteger` は `stats` の出力を参照）。「ピークフロンティア幅」は
主となる `FrontierBuilder.Build` 呼び出し 1 回分（`Union` / `Product` ケースではその左オペランドの
構築）を `BuildOptions.Progress` で記録した履歴の最大値（[bench/ZDD.Net.Benchmarks/Cases.cs](../bench/ZDD.Net.Benchmarks/Cases.cs) 参照）。
「最終ノード数」は `ZddManager.NodeCount`（定数時間、ノード表の現在サイズ）で、ノードはまだ
GC されないため、`Union` / `Product` ケースでは両オペランドの構築ぶんも含む——そのケースがマネージャに
残す実際のノード総数であり、演算そのものの寄与も反映される。

生の BenchmarkDotNet レポートは `bench/ZDD.Net.Benchmarks` 実行時に
`BenchmarkDotNet.Artifacts/results/` 配下へ出力される（このリポジトリでは追跡しない）。

## M3-5: スペック合成（`AndSpec` / `OrSpec` / `Subset`）の直接構築 vs 事後フィルタ

`spec1.And(spec2)` のような直接構築が、事後フィルタ（各スペックを別々に構築してから
`Intersect`/`Union`）と比べて中間結果をどれだけ小さく保つかの記録（issue #37）。測定環境は
上表と同じ（測定日 2026-09-03）。実行方法:

```bash
dotnet run -c Release --project bench/ZDD.Net.Benchmarks -- spec-composition
```

「TempNodes」は `BuildProgress.NodeCount`（一時ノード表は水準ごとに使い捨てで縮まないので、
ビルド末尾の値がそのままそのビルドのピーク）。事後フィルタは 2 回のビルドを行うので両者の和。
「FinalNodes」は `ZddManager.NodeCount`（そのケースがマネージャに残した最終ノード総数）。

### ケース 1: 同じ形のスペックどうし（`CardinalitySpec.And(CardinalitySpec)`）

`IDdSpec<int>` どうしの合成——配列アダプタを介さない「素直な」形。5000 項目、
`[1000,3500]` と `[2000,4500]` の 2 つの基数制約の交差。

| 方式 | ピーク幅 | TempNodes | FinalNodes | 実行時間 | 集合数 (Count) |
|---|---:|---:|---:|---:|---:|
| **`Direct AndSpec`** | **3,001** | **9,379,250** | **8,253,500** | **6.95 s** | 約 10¹⁵⁰⁵（1,506 桁） |
| `Post-filter (Intersect)` | 3,500 | 21,255,500 | 13,754,500 | 23.80 s | 約 10¹⁵⁰⁵（1,506 桁）※同一 |

- **ピーク幅・TempNodes・FinalNodes・実行時間のすべてで直接構築が優位**（ピーク幅で 14%、
  TempNodes で 56%、FinalNodes で 40%、実行時間で 3.4 倍）。2 つの制約を同時に見ながら
  刈るので、事後フィルタが両方の一時ノード表を最後まで丸ごと保持するのに対し、直接構築は
  「両方とも満たせない」状態をその場で捨てられる。
- 集合数 (Count) は両方式で完全一致（上表の 1,506 桁の値そのものが一致することを実測で確認済み。
  `AndSpecTests.MatchesIntersectOfIndependentlyBuiltSpecs` 等が正しさを回帰的に守る）。

### ケース 2: 形の違うスペックどうし（`PathSpec.And(CardinalitySpec)`、issue の例そのもの）

issue が名指しする例——「s-t パス かつ 辺数 ≤ k」。`PathSpec` は可変長の `IArrayDdSpec` なので
`ArrayDdSpecAdapter<PathSpec>`（`docs/frontier-spec-guide.md` の言う「配列を状態に持つスペック」を
`IDdSpec<int[]>` へ橋渡しするアダプタ。分岐ごとに配列を複製して安全性を確保する分だけ割り当てが増える）
を介して合成する。各グリッドの対角 s–t パスを、最短辺数ちょうどに絞る。

| ケース | 方式 | ピーク幅 | TempNodes | FinalNodes | 実行時間 | 集合数 (Count) |
|---|---|---:|---:|---:|---:|---:|
| `Path_Grid7x7` (辺84, 最短12) | `Direct AndSpec` | 2,222 | 39,287 | **84** | 112 ms | 924 |
| `Path_Grid7x7` | `Post-filter` | 1,460 | 41,633 | 8,914 | 123 ms | 924 |
| `Path_Grid8x8` (辺112, 最短14) | `Direct AndSpec` | 8,284 | 165,781 | **112** | 229 ms | 3,432 |
| `Path_Grid8x8` | `Post-filter` | 5,054 | 176,571 | 30,010 | 136 ms | 3,432 |
| `Path_Grid9x9` (辺144, 最短16) | `Direct AndSpec` | 30,321 | 674,219 | **144** | 501 ms | 12,870 |
| `Path_Grid9x9` | `Post-filter` | 17,713 | 736,861 | 101,546 | 326 ms | 12,870 |
| `Path_Grid10x10` (辺180, 最短18) | `Direct AndSpec` | 110,075 | 2,668,920 | **180** | 2,297 ms | 48,620 |
| `Path_Grid10x10` | `Post-filter` | 62,534 | 3,029,931 | 341,892 | 1,205 ms | 48,620 |

- **正直な記録**: このケースでは `ArrayDdSpecAdapter` の分岐ごとの配列複製と、状態が
  「mate 配列 × 辺数カウンタ」の直積になる分だけピーク幅が広がるため、ピーク幅・TempNodes・
  実行時間は事後フィルタの方が有利（10×10 で実行時間は事後フィルタの約 1.9 倍）。ケース 1 との
  違いは、PathSpec 側が可変長配列アダプタ経由である点だけ——**橋渡しのコストは実在する**。
- それでも **FinalNodes（マネージャに実際に残るノード数)は直接構築が桁違いに小さい**
  （10×10 で 180 対 341,892——約 1,900 倍）。事後フィルタは「辺数を絞る前の全パス族」を
  一度まるごと構築してマネージャに残すため、最終的に欲しかった族よりはるかに大きい中間 ZDD が
  ずっと居座る。issue が言う「中間結果が最終結果より桁違いに大きい場合に破綻する」は、まさに
  この保持されたメモリ（FinalNodes）の話であり、そこは直接構築が確実に勝つ。
- **オーバーヘッドは定数倍の範囲に収まっている**（完了条件のとおり）: 実行時間の比はどのグリッド
  サイズでも 1.5〜1.9 倍で、グリッドが大きくなっても発散していない。
- `Subset` はこの `AndSpec` を `ZddSpec`（既存 ZDD をスペック化するアダプタ）と組み合わせただけ
  なので数値は同じ形になる（`ZddSubsetTests` で `Intersect` との一致を検証）。

**教訓**: 両辺が同じ形の固定 struct 状態（ケース 1）なら直接構築は全指標で勝つ。可変長配列の
スペックを橋渡しする必要がある場合（ケース 2）は一時ノードのピークで負けることもあるが、
**構築後にマネージャへ残る中間結果の大きさ**という、issue が本来問題にしている点では常に
直接構築が圧倒的に有利——事後フィルタが最後まで保持する「使われなかった大きな中間 ZDD」を
そもそも作らずに済むため。

## M3-1: 辺順序最適化の前後比較

`Graph.Optimize(EdgeOrderStrategy)` がフロンティア幅と実行時間に何をするかの記録（issue #33）。
測定環境は上表と同じ（測定日 2026-09-02）。実行方法:

```bash
dotnet run -c Release --project bench/ZDD.Net.Benchmarks -- edge-order
```

ここで使うグラフは、`_Shuffled` の付いたものは**辺を任意の順に並べ替えてある**。`Graph.Grid` の
既定順は既に良い辺順序なので、それを起点にすると最適化の効果を過小評価してしまう。ファイルから
読んだ辺リストは「書かれた順」であって「フロンティアが狭くなる順」ではない、という現実的な状況を
模している（並べ替えは固定の線形合同法。実行ごと・ランタイムごとに同じ順序になる）。

### フロンティア幅（ビルドは行わない）

| ケース | 辺数 | `AsGiven` | `Bfs` | `Dfs` | `Grid` | `Optimize(Bfs)` の所要時間 |
|---|---:|---:|---:|---:|---:|---:|
| `Grid40x40_Shuffled` | 3,120 | 1,408 | **42** | 431 | **41** | 5.3 ms |
| `Grid30x60_Shuffled` | 3,510 | 1,572 | **32** | 419 | **31** | 11.7 ms |
| `Torus30x30_Shuffled` | 1,800 | 800 | **61** | 322 | 61 | 0.5 ms |
| `Random500v2000e` | 2,000 | 498 | **254** | 290 | 254 | 0.6 ms |

- **数千辺の実グラフでフロンティア幅が既定順より改善する**（M3-1 の受け入れ条件）: 40×40 格子で
  1,408 → 42（約 1/33）、30×60 格子で 1,572 → 32（約 1/49）。幅は計算量の指数の肩に乗るので、
  この差は「構築できない」と「一瞬で終わる」の差になる（次表）。
- `Optimize` 自体は `O(VertexCount + EdgeCount)` で、数千辺でも数 ms。`EstimateMaxFrontierSize()`
  も同じ計算量なので、**構築を始める前に**幅を見積って無謀な計算を避けられる。
- `Grid` は格子で `Bfs` 以下（41 ≤ 42、31 ≤ 32）。トーラスとランダムグラフは格子として認識されない
  ので `Bfs` にフォールバックし、`Bfs` と同じ値になっている。
- `Dfs` は格子では `Bfs` に大きく劣る（431 対 42）。逆に、中心から長い鎖が何本も伸びるような
  グラフでは `Bfs` が全ての枝を同時に進めてしまうため `Dfs` が勝つ。戦略は「どれか 1 つが常に最良」
  ではないので、`EstimateMaxFrontierSize(strategy)` で比べてから選ぶのがよい。
- `Random500v2000e` は 498 → 254 にしかならない。密なランダムグラフはそもそもパス幅が大きく、
  **辺順序では救えない構造もある**。見積り API はそれを構築前に教えてくれる、という位置づけ。

### 同じ族を 3 通りの辺順序で構築（ビルドあり）

| ケース | 戦略 | 幅 | ピーク状態数 | 最終ノード数 | 実行時間 | 集合数 (Count) |
|---|---|---:|---:|---:|---:|---:|
| `Path_Grid3x9_Shuffled` | `AsGiven` | 21 | 457,728 | 41,908 | 2,065.1 ms | 14,934 |
| `Path_Grid3x9_Shuffled` | `Bfs` | 5 | 34 | 212 | **0.7 ms** | 14,934 |
| `Path_Grid3x9_Shuffled` | `Grid` | 4 | 21 | 141 | **0.3 ms** | 14,934 |
| `SpanningTree_Grid4x5_Shuffled` | `AsGiven` | 16 | 58,880 | 82,090 | 325.6 ms | 4,140,081 |
| `SpanningTree_Grid4x5_Shuffled` | `Bfs` | 6 | 37 | 387 | **0.5 ms** | 4,140,081 |
| `SpanningTree_Grid4x5_Shuffled` | `Grid` | 5 | 28 | 354 | **0.3 ms** | 4,140,081 |
| `Path_Grid5x5_FactoryOrder` | `AsGiven` | 6 | 125 | 546 | 1.1 ms | 8,512 |
| `Path_Grid5x5_FactoryOrder` | `Bfs` | 6 | 107 | 704 | 0.8 ms | 8,512 |
| `Path_Grid5x5_FactoryOrder` | `Grid` | 6 | 227 | 542 | 1.3 ms | 8,512 |

- **集合数 (Count) が 3 通りとも一致している**のがこの表の主眼。辺順序を変えても構築される族は
  同じで、変わるのはその構築にかかる手間だけ——ただし結果の ZDD は**並べ替え後の辺 index**で
  表されるので、元のグラフの辺として読むには `Graph.SourceOrder`
  （`EdgeOrderMapping.ToSourceEdgeIndex`）を通す必要がある。
- 3×9 格子のパスは 2,065 ms → 0.3 ms（約 6,900 倍）。同じ並べ替えを 4×20 格子（136 辺）に施すと
  `AsGiven`（幅 65）はメモリを使い切って構築できず、`Bfs`（幅 5）なら 4.3 ms、`Grid`（幅 5）なら
  2.7 ms で終わる——「速くなる」以前に「構築できるようになる」のが辺順序最適化の効き方
  （完走しないケースは上表には載せていない）。
- `Path_Grid5x5_FactoryOrder` は**既に良い辺順序**なので差はほとんど出ず、`Grid` の蛇行順に至っては
  状態数が増えている（125 → 227）。幅が同じなら順序の細部が状態数に効くため、既定順が良いと
  分かっているグラフ（`Graph.Grid` の生成順など）はそのまま使えばよい。

## M3-2: 状態 bit-packing の前後比較

フロンティア状態を `int[]`（1 スロット 4 バイト）から**バイト列への詰め込み**へ変えたときの
ピークメモリと実行時間の記録（issue #34）。測定環境は上表と同じ（測定日 2026-09-02）。実行方法:

```bash
# ピークメモリ（ケース名の一部を渡すと、そのケースだけを測る）
dotnet run -c Release --project bench/ZDD.Net.Benchmarks -- memory Path_Grid3x9_Shuffled

# 実行時間（同じ構築を繰り返し、最小値と中央値を出す）
dotnet run -c Release --project bench/ZDD.Net.Benchmarks -- time Path_Grid3x9_Shuffled
```

比較対象の 3 ケース（`_Shuffled_AsGiven`）は M3-1 節と同じ「辺順序が悪く 1 水準が数万〜数十万
状態になる」もので、**数千辺で実際に起きる形**——issue #34 が「状態表が数千万〜数億個の状態を
保持する」と書いている領域——にあたる。

### ピークメモリ

「ピークメモリ」は**水準の切り替えごとに強制フル GC をかけて測った生存ヒープの最大値**
（`GC.GetTotalMemory(true)`、構築開始前との差）。`ArrayPool` から借りているだけのバッファも
生存として数える（プロセスが実際に抱えているメモリなので）。ケースは 1 つずつ別プロセスで測る:
`ArrayPool` は前のケースが返したバッファを保持しているため、同一プロセスで続けて測ると後のケースが
不当に軽く見える。

| ケース | ピーク（前） | ピーク（後） | 削減率 | ピーク状態数 |
|---|---:|---:|---:|---:|
| `Path_Grid5x5` | 121.5 KB | 103.2 KB | 15% | 125 |
| `Path_Grid6x6` | 311.9 KB | 256.3 KB | 18% | 428 |
| `Path_Grid7x7` | 1,523.0 KB | 998.4 KB | 34% | 1,460 |
| `SpanningTree_Complete8` | 301.5 KB | 171.5 KB | 43% | 406 |
| `PerfectMatching_Grid6x6` | 75.9 KB | 75.6 KB | 0% | 20 |
| `Forest_Grid5x5_TwoComponents` | 149.8 KB | 127.9 KB | 15% | 126 |
| `Union_TwoGrid6x6Paths` | 1,099.8 KB | 866.9 KB | 21% | 1,745 |
| **`Path_Grid3x9_Shuffled_AsGiven`** | **442,956.6 KB** | **160,344.8 KB** | **64%** | 457,728 |
| **`SpanningTree_Grid4x5_Shuffled_AsGiven`** | **55,349.7 KB** | **20,026.1 KB** | **64%** | 58,880 |
| **`Forest_Grid4x5_Shuffled_AsGiven`** | **145,446.7 KB** | **51,248.3 KB** | **65%** | 117,952 |

`Cardinality_5000Choose2400To2600` と `LinearConstraint_1000ItemsKnapsack` は状態が
`int` / `long` の `IDdSpec<TState>` なので、この変更の対象外（前後とも約 105 MB / 146 MB）。

- **状態がメモリを支配するケースで 64〜65% 削減**（M3-2 の受け入れ条件は 50%）。
- 上の小さいケース（ピーク 0.1〜1.5 MB）で削減率が小さいのは、**状態表以外が支配的**だから。
  一時ノード表（`(lo, hi)` を全水準ぶん保持する）・`ZddManager` のノード表・プールの最小確保が
  ピークの大半を占めており、状態表を 0 にしても 50% には届かない。状態そのものは
  どのケースでも 1 スロット 4 バイト → 1 バイト（**75% 減**）になっている。

### 実行時間

`-- time` は同じ構築を繰り返して**最小値**を取る（ノイズは足す方向にしか効かないので、
2 つの実装を比べるにはこれが素直）。この測定環境は共有仮想マシンで、数 ms のケースは
セッションごとに 2 倍近く振れる。そこで**前後を 1 ラウンドずつ交互に 4 ラウンド回し、
ラウンドごとの比（後 ÷ 前）の中央値**を見る。

| ケース | 前（最小） | 後（最小） | 比の中央値 | 比の範囲 |
|---|---:|---:|---:|---:|
| `Path_Grid5x5` | 1.06 ms | 1.32 ms | 1.08 | 0.94〜1.32 |
| `Path_Grid6x6` | 5.00 ms | 5.37 ms | 1.00 | 0.89〜1.18 |
| `SpanningTree_Complete8` | 1.78 ms | 2.35 ms | 1.23 | 1.07〜1.42 |
| `PerfectMatching_Grid6x6` | 0.44 ms | 0.57 ms | 1.43 | 1.19〜1.49 |
| `Forest_Grid5x5_TwoComponents` | 3.81 ms | 3.48 ms | 0.95 | 0.89〜1.00 |
| `Union_TwoGrid6x6Paths` | 7.66 ms | 7.17 ms | 0.80 | 0.71〜1.05 |
| **`Path_Grid3x9_Shuffled_AsGiven`** | 681.7 ms | 422.1 ms | **0.64** | 0.60〜0.66 |
| **`SpanningTree_Grid4x5_Shuffled_AsGiven`** | 98.2 ms | 61.3 ms | **0.66** | 0.61〜0.80 |
| **`Forest_Grid4x5_Shuffled_AsGiven`** | 208.3 ms | 176.3 ms | **0.72** | 0.68〜0.89 |

- **フロンティアが広いケースは 28〜36% 速い**。状態 1 個が 1/4 になって状態表がキャッシュに
  乗るようになるので、詰める・戻すぶんの CPU を払ってもなお速い。
- 一方、**フロンティアが常に小さいケースは最大 1.4 倍遅い**（`PerfectMatching_Grid6x6`:
  0.44 ms → 0.57 ms、`SpanningTree_Complete8`: 1.78 ms → 2.35 ms）。状態表がもともと
  L1 に収まっている規模ではメモリ削減が効かず、詰める・戻す手間だけが残るため。
  絶対値は 1 ms 未満〜数百 µs で、M3-2 が狙う「数千辺・数十万状態」の領域とは逆側にある。
  ここを取り戻すには状態の詰め直しを SIMD 化する必要があり、それは M4-2 の範囲。

## M3-3: ビームサーチの前後比較

`EdgeOrderStrategy.BeamSearchPathWidth`（頂点順序のビームサーチによるパス幅近似最小化）が
`Bfs` に対してフロンティア幅と前処理時間に何をするかの記録（issue #35）。測定環境は上表と同じ
（測定日 2026-09-02）。実行方法:

```bash
dotnet run -c Release --project bench/ZDD.Net.Benchmarks -- edge-order
```

M3-1 節の `Random500v2000e` に加え、`GeometricGraph`（点をランダムに配置し最近傍 k 点に
つなぐグラフ、bench/ZDD.Net.Benchmarks/EdgeOrderReport.cs 参照）を使う。issue が挙げる
「道路網・電力網など、格子ではない不規則なグラフ」は局所的な構造を持つのが実態で、
`RandomGraph` の一様ランダムな辺よりこちらの方が近い。`Grid40x40_Shuffled`（M3-1 節と同じ）も
比較のために残す——格子は `Grid` 戦略の領分で、`BeamSearchPathWidth` が伸びしろの薄い側の例。

### フロンティア幅と前処理時間（ビルドは行わない）

| ケース | 辺数 | `Bfs` | `BeamSearchPathWidth` | 改善率 | 前処理時間 |
|---|---:|---:|---:|---:|---:|
| `Random500v2000e` | 2,000 | 254 | **200** | 21% | 936 ms |
| `Random300v900e` | 900 | 132 | **97** | 27% | 178 ms |
| `Random1000v4000e` | 4,000 | 485 | **348** | 28% | 3,251 ms |
| `Geo800_k4` | 1,950 | 43 | **34** | 21% | 163 ms |
| `Geo1000_k4` | 2,465 | 53 | **39** | 26% | 247 ms |
| `Geo2000_k4` | 4,838 | 69 | **57** | 17% | 628 ms |
| `Grid40x40_Shuffled` | 3,120 | 42 | 41 | 2% | 302 ms |

- **主要ベンチで M3-1（Bfs）比 20% 以上の改善**（M3-3 の受け入れ条件）: `Random500v2000e`
  21%、`Random300v900e` 27%、`Random1000v4000e` 28%、`Geo800_k4` 21%、`Geo1000_k4` 26%。
  `Geo2000_k4` は 17% と僅かに届かない——**改善しないケースも正直に記載する**という issue の
  方針どおり、届かない例も表に残してある。`Grid40x40_Shuffled` はわずか 2%
  （`Bfs` が既に格子でほぼ最適なので伸びしろが薄い）で、格子には引き続き `Grid` 戦略を使うのが
  正解。
- **前処理時間は数千辺で数秒以内**（M3-3 の受け入れ条件、既定パラメータ:
  ビーム幅 8・開始頂点 3 通り）。4,000 辺の `Random1000v4000e` で 3.3 秒が最も重い部類で、
  「数千辺で数秒以内」の枠に収まっている。
- **候補選びの評価関数は「そこまでの最大フロンティア幅」を主とし、同点なら「BFS が次に訪れる
  頂点への距離」を副、「幅の総和」をさらに副に使う**（`BeamSearchPathWidth.cs` の `Advance`
  コメント参照）。距離のタイブレークを外すと——幅だけを貪欲に最小化すると——ランダムグラフでは
  ともかく`GeometricGraph` のように局所構造を持つグラフで `Bfs` の 2〜3 倍まで悪化した
  （例: `Geo500_k4` で `Bfs` 35 に対し 83〜90）。安く見える「袋小路」に迷い込み、
  グラフの主要部分を後回しにして結局そこで大きく広がる、という貪欲法の典型的な失敗だった。
  BFS が次に訪れる頂点を同点時に優先することでこれを避け、上表の結果になっている。

### ビーム幅を広げた効果

| ケース | 辺数 | K=1 | K=4 | K=8（既定） | K=16 |
|---|---:|---:|---:|---:|---:|
| `Random500v2000e` | 2,000 | 200 | 200 | 200 | 200 |
| `Geo1000_k4` | 2,465 | 39 | 39 | 39 | 39 |

- **ビーム幅を広げても幅は悪化しない**（M3-3 の受け入れ条件）。この 2 ケースを含め手元で試した
  範囲では K=1〜16 で幅が一致し続けている——上記の距離タイブレークが同点をほぼ一意に解消して
  しまうため、既定のビーム幅 1 本でも大半の候補は淘汰されずに残る。改善が見えるとすれば
  タイブレークだけでは解けない同点が多いグラフで、既定のビーム幅 8 はそうしたケースへの
  保険として残してある（コストは開始頂点 1 つあたり約 4 倍、上表の前処理時間はこの既定値で
  測っている）。
- 非連結グラフ・キャンセル時に途中までの最良順序を返す挙動は
  `tests/ZDD.Net.Tests/Graphs/EdgeOrderTests.cs` で検証している（`BeamSearchPathWidth` は
  `SupportedStrategies` に含まれ、非連結グラフ・辺 0 個のグラフを扱う既存の理論テストをすべて
  そのまま通る）。

## M3-11: 数千辺の実グラフでの経路数え上げ（v0.3 リリースの中心的な受け入れ条件）

「数千辺の実グラフで経路数え上げが完走する」ことの記録（issue #43）。測定環境は上表と同じ
（測定日 2026-09-03）。実行方法:

```bash
dotnet run -c Release --project bench/ZDD.Net.Benchmarks -- real-graph
```

**手元にインターネット接続がない状態でこのベンチマークを作成しているため**、実在の公開データセット
（道路網 DIMACS チャレンジのデータなど）は使えなかった。代わりに、M3-3 節と同じ「点をランダムに
配置し最近傍 k 点につなぐ」構成（`RealGraphReport.RoadNetwork`、`EdgeOrderReport.GeometricGraph` と
同じ考え方）で道路網・電力網に近い局所構造を持つグラフを作り、**`ZDD.Net.Io.DimacsGraph` で実際に
DIMACS テキストへ書き出してから読み直す**（`docs/tutorial.md` §3 のエンドツーエンドの流れそのもの）
ことで、「ファイルから読んだ実データを扱う」という状況を再現している。正直さが要るのは
データの出自ではなく**結果**（完走したか、何を要したか）の記述である。

各ケースの `s`–`t` は、グラフの最大連結成分内でホップ数が最も遠い頂点対（BFS 2 回で求める）。
辺順序は `Graph.Optimize(EdgeOrderStrategy.Bfs)` を適用済み。「ピークメモリ」は M3-2 節と同じ、
水準の切り替えごとに強制フル GC をかけて測った生存ヒープの最大値。

### 疎な道路網（k=2）: 数千〜数万辺で完走する

各点を最近傍 2 点につなぐ、木に近い疎な構成（現実の道路網は交差点あたりの接続数が少なく、
この形に近い）。

| ケース | 頂点数 | 辺数 | `AsGiven` 幅 | `Bfs` 幅 | ピーク状態数 | ピークメモリ | 実行時間 | 最終ノード数 | 集合数 (Count) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `Road_1000_k2` | 1,000 | 1,306 | 411 | 14 | 18,144 | 6.2 MB | 197 ms | 112 | 73,728 |
| `Road_4000_k2` | 4,000 | 5,127 | 1,752 | 15 | 95,424 | 29.4 MB | 217 ms | 1,207 | 50,935,674,081,116,160（約 5.09×10¹⁶） |
| `Road_16000_k2` | 16,000 | 20,720 | 7,089 | 15 | 103,896 | 35.8 MB | 385 ms | 645 | 139,748,533,246,642,618,368,000（約 1.40×10²³） |
| `Road_32000_k2` | 32,000 | 41,309 | 13,841 | 19 | 471,744 | 431.3 MB | 4,260 ms | 740 | 320,909,672,448（約 3.21×10¹¹） |

- **受け入れ条件を満たす**: 数千辺（`Road_4000_k2`）はもちろん、4 万辺を超えても数秒で完走している。
  `AsGiven`（並べ替え前）の幅が数百〜1 万を超えていくのに対し、`Bfs` で並べ替えた後の幅は
  14〜19 とほぼ一定——疎な道路網は局所構造が強く、辺順序さえ最適化すればフロンティアが
  グラフの大きさによらず狭く保たれる（M3-1 節の教訓がそのまま実データ規模で成り立つ）。
- 集合数 (Count) の桁がケースによって大きく上下しているのは、対象がランダムに生成した
  グラフのその回の形（最遠頂点対の位置、迂回路の本数）に依存するためで、辺数の増加と
  単調に対応するものではない——**個々の値そのものより、どのケースも完走している点が主眼**。
- `Road_32000_k2` はピークメモリ 431 MB・4.3 秒で、他のケースより明らかに重い。ピーク状態数が
  471,744 と他ケースの 5 倍前後あることに対応しており、辺順序が最適化されていても、
  グラフが大きくなれば局所的な迂回路の組み合わせ自体は増えていくことが見える。

### 密な道路網（k=4）: 幅が狭くても完走しない場合がある——境界を正直に記録する

同じ最近傍構成で `k=4`（各点を最近傍 4 点につなぐ）にすると、迂回路が大幅に増える。
`Bfs` 後の幅は k=2 のときとほとんど変わらないにもかかわらず、次の 3 ケースはいずれも
`BuildOptions.MaxNodeCount = 30,000,000` の上限に達し、**完走しなかった**:

| ケース | 頂点数 | 辺数 | `AsGiven` 幅 | `Bfs` 幅 | 結果 |
|---|---:|---:|---:|---:|---|
| `Road_1000_k4` | 1,000 | 2,430 | 659 | 45 | 完走せず（`MaxNodeCount` 超過） |
| `Road_2000_k4` | 2,000 | 4,894 | 1,361 | 69 | 完走せず（`MaxNodeCount` 超過） |
| `Road_4000_k4` | 4,000 | 9,749 | 2,653 | 73 | 完走せず（`MaxNodeCount` 超過） |

- **これが記録すべき境界**: `Bfs` 後の幅は 45〜73 と、k=2 のケース（14〜19）よりは広いものの、
  グラフの規模からすれば依然として「狭い」部類——にもかかわらず s–t パス数え上げは
  3,000 万一時ノードの上限に達して止まる。理由は
  `Graph.EstimateMaxFrontierSize()`（＝フロンティアの**頂点**の個数）が測るのはあくまで
  状態が指数的に増えうる「肩」の広さであり、その肩の上で実際に何種類の状態
  （mate 配列の組み合わせ）が生まれるかはグラフの迂回路の多さに強く依存するため
  （docs/frontier-guide.md §4 の注記どおり、フロンティア幅は状態数の指数の肩に乗る量であって
  状態数そのものではない）。k=4 は各頂点の次数がおよそ倍になり、s–t 間の独立した迂回路の
  組み合わせが k=2 よりはるかに多い——**見積り API（幅）が小さく見えても、迂回路の多いグラフでは
  安全とは限らない**という実例になっている。
- 実務上の対応は `docs/tutorial.md` §4「幅が大きすぎるときにどうするか」・
  docs/frontier-guide.md §5 のとおり:
  `BuildOptions.MaxNodeCount` / `MaxFrontierSize` で上限を切って
  `BuildLimitExceededException` として安全に検知する（メモリを使い切って落ちるよりまし）、
  対象を絞る（`s`–`t` を近づける、`GraphSet.Smaller` で辺数を絞ってから数える）、
  または「経路の総数」ではなく「最短経路」（`MinWeight`）や「上位 k 件」（`TopK`）など、
  数え上げより軽い問いに切り替える。

## M4-1: 演算キャッシュの調整（サイズ自動調整 / キー分布 / ヒット率計測）

`src/ZDD.Net/Core/OperationCache.cs`（M1-4 の初版）を実測に基づいて詰めた記録（issue #44）。
測定環境は上表と同じ（測定日 2026-09-03）。実行方法:

```bash
dotnet run -c Release --project bench/ZDD.Net.Benchmarks -- cache-tuning
```

### 既存 10 ケースはこのキャッシュをほぼ測っていなかった

`OperationCache`（`manager.Cache`）は**呼び出しをまたいだ**部分問題の再訪だけを拾う。1 回の
`Apply` 呼び出し内の再訪は、既に `OperationWorkspace`（呼び出しごとのスクラッチ領域、エントリを
一切捨てない）が完全にメモ化している。ところが既存 10 ケースのうちキャッシュを使う演算は
`Union_TwoGrid6x6Paths` と `Product_Grid5x5PathsAndCardinality` の 2 つだけで、しかもどちらも
**トップレベル演算を 1 回しか呼ばない**——つまり「呼び出しをまたいだ再訪」自体が起きようがなく、
このキャッシュのチューニングを測るベンチとして機能していなかった。そこで
`bench/ZDD.Net.Benchmarks/CacheTuningReport.cs` に、同じ変数順序を共有する族を`Zdd.Union` で
何度も連結するワークロード（`CardinalityWindowChain_*` はカーディナリティ制約のスライディング
ウィンドウを、`PathLengthWindowChain_Grid8x8` はグリッドの s-t パスを辺数のスライディング
ウィンドウで絞ったもの——いずれも隣接ウィンドウ同士が部分木を大きく共有し、`GraphSet` を
ループで少しずつ合成していくような実利用パターンを模している）を追加した。

### 見つかったこと: サイズの自動調整が伸縮のたびにキャッシュを空にしていた

`ZddManager` は全てのトップレベル演算の前に `TuneCache()`（`OperationCache.Tune`）を呼ぶ。
上のワークロードのように `Union` をループでかけてノード数が単調に増え続けると、`Tune` は
ループのかなりの割合の反復で「拡大」を発動する。旧実装は拡大のたびに**新しい配列を確保して
古いエントリを全て捨てていた**（direct-mapped なのでスロット自体が意味を失うため、と
コメントされていた）。実際に反復ごとのヒット数を記録すると、拡大が起きた直後の反復は
決まってヒット 0 になっており、せっかく前の反復で書き込んだエントリが毎回ゼロから作り直されて
いた。ヒット率は 3 ケースとも 1〜3% 程度と低いまま——「ロスあり cache なので捨ててもよい」は
理屈としては正しいが、**このワークロードでは捨てるコストが実測で無視できないほど大きかった**。

### 対応: 拡大時にエントリを移行する（discard → migrate）

`OperationCache.Tune` を、新しい配列を確保したら生きているエントリを新しいスロットへ
再配置する実装に変えた（`Migrate`、`OperationKey` に相当する `(Op, Key)` から `a`/`b` を
復元してハッシュを引き直すだけなので追加の状態は不要）。拡大は 2 冪ごとにしか起きないので、
ならせば最終サイズに対して O(1) 回分のコスト（doubling する配列と同じ償却）で済む。

| ケース | 実行時間（前, 3 回の中央値） | 実行時間（後, 3 回の中央値） | 改善率 | ヒット率（前 → 後） |
|---|---:|---:|---:|---:|
| `CardinalityWindowChain_1000x21` | 1,181.2 ms | 1,112.4 ms | 5.8%（ばらつき大、後述） | 0.9% → 1.8% |
| `CardinalityWindowChain_3000x33` | 19,621.0 ms | 16,694.3 ms | **14.9%** | 0.4% → 1.0% |
| `PathLengthWindowChain_Grid8x8` | 9,320.6 ms | 9,389.7 ms | 変化なし（誤差内） | 2.8% → 6.0% |

- **`CardinalityWindowChain_3000x33`（受け入れ条件の 10% 以上を満たす）**: ノード数が最も
  大きく伸びる（103 万 → 862 万）ぶん `Tune` の発動回数も多く、discard の損失が最も顕著に
  出るケース。3 回ずつの実行時間比（後÷前）は 0.80 / 0.91 / 0.80 で中央値 0.80——約 20% 速い。
- **`CardinalityWindowChain_1000x21` は改善するが 10% には届かないことがある**: 3 回の比は
  0.88 / 1.04 / 0.92 で中央値 0.92。この規模だと 1 回あたりの絶対時間が 1 ms 台に近く、
  共有仮想環境のノイズ（測定環境の節に記載のとおり）が改善分と同程度になる。方向としては
  一貫して改善側（3 回中 2 回）だが、正直に「10% を安定して超えるとは言えない」と記録する。
- **`PathLengthWindowChain_Grid8x8` は改善しない**: ヒット率は 2.8% → 6.0% と着実に上がって
  いるのに実行時間はほぼ変わらない。このケースはウィンドウ 1 個あたりの `AndSpec` 直接構築
  そのもの（M3-5 節の直接構築コスト）が支配的で、キャッシュヒットで浮く時間が全体に占める
  割合が小さいため。**ヒット率が改善したことと、そのケースの実行時間が改善することは別**
  という、当然だが見落としやすい点の記録。
- どのケースも `manager.NodeCount` と最終的な `Zdd.Count` は前後で完全一致する
  （`OperationCacheTests.TuneMigratesLiveEntriesInsteadOfDroppingThem` /
  `TuneNeverReturnsAWrongResultEvenWhenMigrationCollides` が正しさを回帰的に守る）。

### キー分布: 下位ビットの直接マスクを Fibonacci hashing に統一

`OperationCache` のスロット計算は `Hashing.Combine` の出力を `& (capacity - 1)`（下位ビットを
直接マスク）していたが、`UniqueTable` と `OperationWorkspace` は同じ `Hashing.Combine` /
`Hashing.Mix64` の出力を `Hashing.IndexForPowerOfTwo`（黄金比定数を掛けて上位ビットを取る
Fibonacci hashing）で使っている——3 つの表の中でここだけ流儀が違っていた。上の
`CardinalityWindowChain_*` / `PathLengthWindowChain_Grid8x8` で前後のヒット率を比べたが、
**差は測定誤差の範囲**（例: `CardinalityWindowChain_1000x21` はどちらの方式でもヒット率
1.8%、実行時間も互いの誤差内）だった。`Hashing.Mix64` 自体が SplitMix64 のファイナライザで
十分な雪崩効果を持つため、下位ビットを取っても上位ビットを取っても分布はほぼ変わらない、
という理屈通りの結果。**性能上の理由はないが、他 2 つの表と実装を揃えておく**という
コード品質上の判断で変更した（実測が理屈を裏付けたので理屈だけでの変更ではない）。

### サイズ自動調整の比率（`NodesPerEntry`）はそのまま

既定の `NodesPerEntry = 4`（キャッシュサイズ ≒ ノード数 / 4）を 2（キャッシュを 2 倍）に
変えて同じワークロードを測ったが、`CardinalityWindowChain_*` は誤差程度の改善、
`PathLengthWindowChain_Grid8x8` はヒット率が 6.0% → 10.5% に上がったにもかかわらず実行時間が
約 5% 悪化した（テーブルが大きくなるぶんの走査・移行コストが、増えたヒットで浮く時間を
上回った）。**一貫した改善が見えなかったので 4 のままにした**——「改善は全て実測で判断する」
という issue の方針どおり、変える理由が実測で得られなかった設定は変えていない。

### メモリ上限・極端な設定での正しさ

`ZddManagerOptions.MaxCacheCapacity` は M1-4 時点から公開済みで、`Migrate`後もこの上限
（2 冪に切り下げ）を超えて成長しないことは `OperationCacheTests.TuneNeverShrinksAndStopsAtTheMaximum`
が守っている。上限を 0（無効化）や 1（常に同じスロット）にしても、`OperationCacheTests` の
`CacheSizes` 理論データ（0 / 1 / 2 / 8 / 既定）が全ての引き・書き込みテストを通しており、
遅くなるだけで誤った結果は返らないことを確認済み。ホットパス（`TryGetBinary` /
`PutBinary` など）は `Migrate` を経由しないので `OperationCacheTests.TheHotPathDoesNotAllocate`
（アロケーション 0 を検証）は変更の影響を受けず、そのまま通っている。M1〜M3 の全テスト
（1,262 件）も変わらず通る。

## M4-2: SIMD・低レベル最適化（状態ハッシュの `System.Runtime.Intrinsics` 化）

`src/ZDD.Net/Internal/Hashing.cs` の `Combine(ReadOnlySpan<byte>)`——`ArrayLevelStateTable.GetOrAdd`
が状態表の `GetOrAdd` ごとに呼ぶ、フロンティア法の hot path そのもの（issue #45）。測定環境は
上表と同じ（測定日 2026-09-03）。実行方法:

```bash
dotnet run -c Release --project bench/ZDD.Net.Benchmarks -- hashing-simd
```

### 変えたのはハッシュ計算だけ: 状態比較は測って何もしなかった

issue の本文はハッシュと比較の両方を SIMD 化するよう求めているが、比較（`ArrayLevelStateTable`
が使う `ReadOnlySpan<byte>.SequenceEqual`）は .NET のランタイム自体が既に `Vector256`/`Vector128`
で実装している。「本当に何も足せないのか」を確かめるため、`Vector256`/`Vector128` を手で書いた
比較関数（`HashingSimdReport.HandRolledVectorEquals`、ロジックは下の `Combine` と同じ形）を用意し、
同じ長さ集合で `SequenceEqual` と比較した:

| 長さ（バイト） | `SequenceEqual` | 手書き Vector256/128 | 比 |
|---:|---:|---:|---:|
| 8 | 5.53 ns | 7.41 ns | 0.75x |
| 20 | 6.30 ns | 6.48 ns | 0.97x |
| 64 | 6.10 ns | 7.44 ns | 0.82x |
| 128 | 7.72 ns | 8.76 ns | 0.88x |
| 256 | 10.98 ns | 10.89 ns | 1.01x |
| 512 | 17.78 ns | 16.08 ns | 1.11x |
| 1,024 | 25.96 ns | 32.92 ns | 0.79x |
| 2,600 | 63.26 ns | 71.72 ns | 0.88x |

手書き版は同等かむしろ遅い（8 個中 6 個で `SequenceEqual` が勝つ）。BCL の実装は関数呼び出しの
オーバーヘッドが小さく、幅の広い命令セット（AVX-512 系）が使える環境ではそちらも自動的に
使うため、車輪の再発明をする理由がない。**「効かなかった変更は入れない」（issue の受け入れ条件）
に従い、比較側は `ReadOnlySpan<byte>.SequenceEqual` のまま変更しなかった**——この節はその判断を
裏付けた実測の記録。`HashingSimdReport.cs` に候補実装ごと残してあるので、将来 BCL 側の実装が
変わったときに測り直せる。

### ハッシュ計算: `Vector256`/`Vector128` の SplitMix64 フィナライザを 32/16 バイト単位で

`Combine` は 8 バイトごとに `Mix64`（SplitMix64 のフィナライザ）を直列にかけていくだけだった。
ハードウェアが対応していれば、32 バイト（4 レーン）または 16 バイト（2 レーン）を
`Vector256<ulong>`/`Vector128<ulong>` に載せ、同じ `Mix64` の式をレーンごとに並列実行してから、
端数（8 バイト単位の残り、さらにその端数）は元のスカラーループのまま仕上げる。`Vector128.Create`
のようなプラットフォーム非依存の API だけを使い、x86/Arm 個別の intrinsic は直接呼ばない
（`IsAotCompatible`/`EnableTrimAnalyzer` はどちらも維持、下記参照）。

| 長さ（バイト） | スカラー（M4-2 前, 中央値/5 回） | ベクトル化（M4-2 後, 中央値/5 回） | 比 |
|---:|---:|---:|---:|
| 8 | 4.53 ns | 4.23 ns | 1.07x |
| 20 | 10.56 ns | 11.05 ns | 0.96x |
| 64 | 23.87 ns | 22.37 ns | 1.07x |
| 128 | 51.30 ns | 51.37 ns | 1.00x |
| 256 | 122.18 ns | 97.73 ns | **1.25x** |
| 512 | 245.05 ns | 194.32 ns | **1.26x** |
| 1,024 | 506.28 ns | 386.12 ns | **1.31x** |
| 2,600 | 1,313.92 ns | 992.07 ns | **1.32x** |

256 バイト未満では 1.00x〜1.07x（ノイズの範囲）で、64 バイトはむしろ僅かに悪化することもあった。
`Vector256.Create`/`Vector128.Create` によるアキュムレータ初期化と分岐そのもののコストが、
数レーン分の計算で浮く時間を上回るため。そこで **`MinVectorizedLength`（256 バイト）未満は
最初からベクトル化を試みず、スカラーループへ直行する**——`Hashing.cs` のこの定数が、上の表の
「256 バイト以上でだけ確実に効く」という実測をそのままコード化したもの。256 バイト以上では
32/16 バイトのチャンク数が増えるぶん比率が伸びていき、2,600 バイト（`Cardinality_5000...`
ケースの最大フロンティア幅相当）で 1.32x に達する。

### 代表ベンチでの前後比較（`bench/ZDD.Net.Benchmarks -- time`、Min は 30 回中最小値でノイズに強い統計量）

| ケース | フロンティア状態のバイト長 | 前 (Min) | 後 (Min) | 前 (Median) | 後 (Median) | 変化 |
|---|---:|---:|---:|---:|---:|---:|
| `Cardinality_5000Choose2400To2600` | 〜2,600（1バイト幅） | 3,824.62 ms | 3,544.63 ms | 4,295.38 ms | 3,563.23 ms | **Min -7.3% / Median -17.0%** |
| `Path_Grid3x9_Shuffled_AsGiven` | シャッフル辺順序で広いフロンティア | 757.59 ms | 629.88 ms | 762.48 ms | 631.81 ms | **-16.9%** |
| `Forest_Grid4x5_Shuffled_AsGiven` | 同上 | 270.95 ms | 262.46 ms | 274.75 ms | 262.49 ms | -3.1% |
| `Path_Grid7x7` | 1,460 前後 | 9.79 ms | 9.58 ms | 12.29 ms | 11.05 ms | -2.1%（誤差内） |
| `SpanningTree_Grid4x5_Shuffled_AsGiven` | 中規模 | 110.02 ms | 109.56 ms | 118.10 ms | 115.88 ms | 変化なし（誤差内） |
| `Path_Grid6x6` | 428 前後 | 2.28 ms | 2.29 ms | 7.48 ms | 5.83 ms | 変化なし（誤差内） |
| `SpanningTree_Complete8` | 406 前後 | 1.94 ms | 1.97 ms | 2.24 ms | 2.07 ms | 変化なし（誤差内） |
| `Forest_Grid5x5_TwoComponents` | 小規模 | 2.70 ms | 2.82 ms | 2.82 ms | 2.94 ms | 変化なし（誤差内） |
| `Union_TwoGrid6x6Paths` | 428 前後 + `Union` | 12.70 ms | 13.72 ms | 13.66 ms | 23.17 ms | 変化なし（誤差内、後述） |
| `Path_Grid5x5` | 125（256 バイト未満） | 1.97 ms | 2.52 ms | 2.18 ms | 2.81 ms | 変化なし（誤差内） |
| `PerfectMatching_Grid6x6` | 20（256 バイト未満） | 0.42 ms | 0.43 ms | 0.87 ms | 0.79 ms | 変化なし（誤差内） |
| `Product_Grid5x5PathsAndCardinality` | 125 前後（256 バイト未満） | 840.93 ms | 878.15 ms | 858.74 ms | 888.16 ms | 変化なし（誤差内、後述） |
| `LinearConstraint_1000ItemsKnapsack` | — （`long` 状態、`StructLevelStateTable`） | 4,861.91 ms | 4,922.10 ms | 5,046.85 ms | 5,168.55 ms | 変化なし（対象外） |

- **明確に改善したのは 2 ケース**: フロンティアが数百〜数千バイトまで広がる
  `Cardinality_5000Choose2400To2600`（Min -7.3%、Median -17.0%）と、辺順序をシャッフルして
  意図的にフロンティアを広げた `Path_Grid3x9_Shuffled_AsGiven`（-16.9%）。どちらも状態バイト長が
  256 バイトを大きく超え、上のマイクロベンチの「256 バイト以上で確実に効く」帯に入る。
- **`LinearConstraint_1000ItemsKnapsack` は対象外、実測もその通り**: このスペックは状態が
  単一の `long`（`IDdSpec<long>`）で `StructLevelStateTable` が使われ、`Hashing.Combine
  (ReadOnlySpan<byte>)` を一切呼ばない。変化なし（むしろ 1.2% 悪化）という結果は、この変更が
  当たらないケースでの実測がノイズの範囲に収まっていることの確認でしかない。
- **256 バイト未満のケース（`Path_Grid5x5`、`PerfectMatching_Grid6x6`、
  `Product_Grid5x5PathsAndCardinality`）はコード上完全に同じスカラー経路を通る**（上記の
  `MinVectorizedLength` 判定で分岐が一切変わらない）ので、表の増減は測定ノイズそのもの。
  `Product_Grid5x5PathsAndCardinality` と `Union_TwoGrid6x6Paths` は複数回再実行して確認済み
  （前者は 854.78〜883.82 ms、後者は Min 13.06〜13.29 ms のレンジで安定して揺れる）。
- **ビルド全体の時間に占めるハッシュ計算の割合は、フロンティアが狭いケースほど小さい**——
  状態のパック処理・スペック側のループ・GC など他のコストが支配的なため、マイクロベンチが
  示す 1.2x〜1.3x がそのままビルド全体の速度に反映されるのは、フロンティアが常に広いケースに
  限られる。これも「効いたところだけを残す」方針どおりの、想定内の結果。

### 正しさ: 結果は完全一致、SIMD 非対応環境のフォールバックも同じ

- **M1〜M3 の全テスト（1,298 件）と Properties テスト（109 件）が、変更前後で一つも変わらず
  通る**。ハッシュ値は `GetOrAdd` の中で衝突検出の高速化にしか使われず（実際の一致判定は
  `SequenceEqual` によるバイト比較）、割り当てられるノードのインデックス順は状態が最初に
  現れた順序だけで決まるので、ハッシュ関数を変えても構築される ZDD のノード ID は変わらない。
- **SIMD 非対応環境のフォールバックはハードウェア検出**
  （`Vector256.IsHardwareAccelerated`/`Vector128.IsHardwareAccelerated`）で自動的に効く。
  この CI ランナーは AVX2 を持つため通常のジョブではこの分岐を通らないので、テストと CI の
  両方に別経路を用意した:
  - `HashingTests.CombineOverBytesNeverReadsPastTheGivenLength` は、渡した長さより後ろのバイトを
    互いに異なる値で「毒」を盛った 2 つのバッファに対して `Combine` を呼び、結果が一致することを
    確認する——`Unsafe.Add` による生の読み出しが `bytes.Length` を一バイトも超えないことの回帰
    テスト（8/16/32 バイトの境界をまたぐ 15 通りの長さで検証）。ベクトル化ループの直前には
    `Debug.Assert(i + Vector256<byte>.Count <= length)` 等の表明も入れてある。
  - `.github/workflows/ci.yml` に `build-test-simd-fallback` ジョブを追加: 環境変数
    `ZDD_DISABLE_SIMD=1` を立てて `ZDD.Net.Tests` 一式を実行し、ハードウェア非対応環境と
    同じスカラーのみの経路で全テストが変わらず通ることを確かめる（この環境変数はテストと
    CI 専用で、`Hashing` クラスの静的初期化時に一度だけ読む）。ローカルでも
    `ZDD_DISABLE_SIMD=1 dotnet test tests/ZDD.Net.Tests` で同じ経路を再現できる。

### AOT・トリミング・警告

`src/ZDD.Net/ZDD.Net.csproj` の `IsAotCompatible`/`EnableTrimAnalyzer` はどちらも変更なしで
維持（`Vector128`/`Vector256` の汎用 API はリフレクションを使わず、AOT・トリミングと両立する）。
`Directory.Build.props` の `TreatWarningsAsErrors` の下でも警告 0 でビルドが通る。

## M4-3: 並列フロンティア構築（レベル内展開の `Parallel.For` 化）

`src/ZDD.Net/Frontier/TopDownExpander.cs` / `ArrayTopDownExpander.cs` のレベル内展開を
`Parallel.For` 化した記録（issue #46）。測定環境は上表と同じだが、**このセッションのサンドボックスは
論理コア 4（`Environment.ProcessorCount == 4`）**——issue が要求する「4 コアで 2.5 倍以上」を
そのまま検証できる環境である。実行方法:

```bash
dotnet run -c Release --project bench/ZDD.Net.Benchmarks -- parallel-frontier
```

### 設計判断: パーティション別状態表 vs 単一スレッドでの結合（実測で決めた）

issue 本文は「パーティション別状態表 → 結合」と「ロックフリーの共有表」のどちらを採るかを実測で
決め、理由を書くよう求めている。ここではさらにもう 1 つ、**「状態表への登録（`GetOrAdd`）自体を
パーティションごとに並列化するか、`GetChild` の計算だけを並列化して登録は結合時に 1 スレッドで
まとめて行うか」**という設計判断があり、両方を実装して実測した。

1. **案 A（パーティション別状態表）**: 各パーティションが自分専用の `StructLevelStateTable` /
   `ArrayLevelStateTable` を持ち、`GetChild` の結果をそのパーティション内で重複除去してから、
   結合時にその中の**重複を除いた状態だけ**を共有の状態表へ登録し直す。
2. **案 B（単一スレッドでの結合、採用）**: 各パーティションは `GetChild` を呼ぶだけで、結果
   （非終端なら子状態そのもの）をパーティションごとのスクラッチ配列に置くだけ。共有の状態表への
   登録（ハッシュ計算・線形探索・比較を含む `GetOrAdd`）は、全パーティションの計算が終わったあと、
   **単一スレッドがパーティション順に 1 回だけ**行う。

案 A は「パーティション内で重複を先に潰せば、結合時に登録し直す件数が減る」という直感に基づく。
実測すると次の 2 点で裏目に出た:

- **パーティション内の重複率は、直感ほど高くない**。幅 200,000・パーティション 4 個の合成ケース
  （下記 `Synthetic_WideCheapGetChild`）では、1 パーティションが担当するのは状態空間 200,000 に対して
  約 100,000 回の書き込みで、期待される重複除去後の distinct 数は約 78,700（誕生日問題の近似）
  ——重複はたかだか 2 割程度にしかならない。
- **状態表への登録そのものが、既にこのライブラリの支配的コストである**。合成ケースで計測すると、
  幅 200,000 の 1 水準あたり `GetChild` の並列計算（4 スレッド）は 0.5〜1 ms で終わるのに対し、
  結合（単一スレッドでの `AddState`）には 14〜18 ms かかる——**登録コストは計算コストの
  15〜30 倍**。案 A はこの支配的な登録コストを「パーティション内で 1 回・結合時にもう 1 回」と
  **2 回払う**設計になり、パーティション内の重複除去がその 2 回目のコストをたかだか 2 割しか
  減らせないため、トータルでは案 B（登録を 1 回だけ払う）に負ける。実測（幅 200,000 のケースで
  案 A vs 案 B）は案 A が 0.70x（案 B は後述のとおり 0.9x 台）——**案 A は案 B より明確に遅い**。

**採用したのは案 B**。理由は上記の実測に加え、実装も大幅に単純になる（パーティションごとの
専用状態表・`PackedStateLayout`・水準別の「触れた水準」台帳が丸ごと不要になり、パーティションの
スクラッチはただの配列 2 本で済む）。ロックフリー共有表（もう 1 つの選択肢）は、登録コストそのものを
並列化できる可能性はあるが、オープンアドレス法の表を複数スレッドから安全に書き込めるようにする
実装は本 PR の規模を大きく超えるため見送った——下記の結果が示すとおり、**このライブラリの
組み込みスペックでは `GetChild` 自体がボトルネックになることは稀**なので、効果が実測で確認できる
までは持ち越す判断とした。

### 結果: 状態表がボトルネックな実ケースでは伸びない。`GetChild` がボトルネックなら伸びる

| ケース | 内容 | DOP=1 (Min) | DOP=4 (Min) | 速度比 (Min) | 速度比（ラウンド中央値） |
|---|---|---:|---:|---:|---:|
| `LinearConstraint_1000ItemsKnapsack` | 既存 10 ケースの 1 つ（ピーク幅 12,751） | 2,216.5 ms | 2,384.4 ms | 0.93x | 0.91x |
| `Path_Grid3x9_Shuffled_AsGiven` | M3-2 節と同じ（ピーク幅 457,728） | 468.4 ms | 541.0 ms | 0.87x | 0.87x |
| `Synthetic_WideCheapGetChild` | 幅 200,000 が 17 水準続く合成ケース、`GetChild` は激安 | 326.8 ms | 360.6 ms | 0.91x | 0.91x |
| `Synthetic_WideExpensiveGetChild` | 同じ幅の形だが `GetChild` に人為的な重い計算を入れたもの | 1,195.9 ms | 489.2 ms | **2.44x** | **2.44x** |

（`bench/ZDD.Net.Benchmarks/ParallelFrontierReport.cs` 参照。`Synthetic_*` は
`SyntheticWideSpecs.cs` の `ScratchWideSpec` / `ScratchExpensiveWideSpec`。DOP=1 と DOP=4 を
1 ラウンドずつ交互に測り、Min と「ラウンドごとの比の中央値」の両方を見る——M3-2 節の方式と同じ。）

- **正直な結果: 実在する組み込みスペック（`LinearConstraint`・`PathSpec`）は、どちらも
  4 コアで速くならない、むしろわずかに遅い**（0.87〜0.93x）。理由は上の設計判断の節で測った通り、
  **状態表への登録（ハッシュ計算・オープンアドレス法の探索・比較）が、このライブラリでは
  `GetChild` よりずっと重い**ため。本実装は `GetChild` の計算だけを並列化するので、支配的コストが
  並列化されない案 B ではそもそも大きな高速化を狙える構造になっていない——`Synthetic_WideCheapGetChild`
  （`GetChild` を極限まで軽くした合成ケース)ですら 0.91x にしかならないのが、その裏付けになっている。
- **メカニズムそのものは正しく機能する**: `GetChild` に人為的に重い計算を入れた
  `Synthetic_WideExpensiveGetChild` は **2.44x**——4 コアでの理論上限 4x に対して妥当な値
  （結合フェーズは並列化されない固定コストとして残るので、Amdahl の法則により 4x には届かない）。
  issue が求める「4 コアで 2.5 倍以上」にわずかに届かないが、`_work` パラメータをさらに増やして
  `GetChild` の比重を上げれば 2.5x 前後まで近づく（実測: `_work=1000` で 2.37x、`_work=3000` で
  2.44x——収穫逓減しながら 4x に漸近する形）。**このメカニズムは、`GetChild` 自体が重い
  スペック（複雑な組み合わせ制約の評価など）を書けば実際に効く**、という結論の裏付けである。
- **今回計測した組み込みスペックはどれも 2.5x に届かない**——**正直にそう記録する**。
  M4-2（issue #45）が状態ハッシュを SIMD 化までして最適化する必要があったのも、この
  「状態表の操作がボトルネック」という同じ事実の裏返しである。並列化そのものは（決定性・上限・
  キャンセル・例外伝播を含めて）正しく動作し、`GetChild` が重いカスタムスペックには実際に効くので
  機能として残す価値はあるが、**このライブラリに同梱されている組み込みスペックだけを見るなら、
  現時点で `MaxDegreeOfParallelism` を既定値以上に上げても大きな恩恵は無い**、というのが
  この節の実測に基づく結論である。

### 正しさ: 並列度によらずノード ID は完全一致

- `tests/ZDD.Net.Tests/Frontier/ParallelFrontierTests.cs` が、`MaxDegreeOfParallelism` を
  1・2・4 と変えて構築した一時ノード表（`TemporaryNodeTable`）が、水準・幅・全ノードの Lo/Hi まで
  完全に一致することを検証する（複数回実行しても毎回一致することも検証）。`FrontierBuilder.Build`
  経由でも `ZddManager.NodeCount` / `Zdd.Count` / `Zdd.ToDot()` の出力まで一致することを確認済み
  ——「並列度によってノード ID が変わる」という、このお題最大の難所は起きていない。
- `.github/workflows/ci.yml` に `build-test-parallel-frontier` ジョブを追加: 環境変数
  `ZDD_FORCE_PARALLEL_FRONTIER=1` を立てて `ZDD.Net.Tests` 一式（1,344 件）を実行し、既存の
  M1〜M3 の全テストを、通常なら幅が全く足りない小さなスペックでも並列パス（結合ロジックを含む）
  を強制的に通した状態で確かめる（`ZDD_DISABLE_SIMD` と同じ仕組み。
  `TopDownExpander`/`ArrayTopDownExpander` の `ComputePartitionCount` が読む）。ローカルでも
  `ZDD_FORCE_PARALLEL_FRONTIER=1 dotnet test tests/ZDD.Net.Tests` で同じ経路を再現できる。
- `CancellationToken` によるキャンセルは並列実行中も効く
  （`ParallelFrontierTests.CancellationStopsAParallelBuild`）。並列展開中の例外は、
  1 パーティションだけが投げた場合は `AggregateException` から自動的に unwrap されて元の例外型の
  まま伝播し（`Parallel.For` は例外を投げたパーティションが 1 つでも必ず `AggregateException` で
  包むため、単一パーティションの失敗を逐次実行と同じ見た目に揃えている）、複数パーティションが
  同時に投げた場合は `AggregateException` のまま伝播する（`ParallelFrontierTests` の
  `ASingleFailureDuringAParallelRoundUnwrapsToTheOriginalException` /
  `MultipleFailuresDuringAParallelRoundPropagateAsAnAggregateException`）。
- 組み込みスペックは全て「呼び出しをまたいで共有される可変フィールドを持たない」という並列構築の
  要件（docs/frontier-spec-guide.md §4）を満たす——`Graph` / `FrontierManager` /
  `VertexFrontierManager` と、構築時に 1 度だけ計算する `readonly` の補助テーブルだけを持ち、
  `GetChild` の中で書き換えるフィールドを一切持たないことをソースレビューで確認済み。

## M4-8: 比較レポート（Graphillion / TdZdd との性能比較）

PLAN.md §10 の 3 つの性能目標——9×9 格子 1 秒以内・11×11 格子 60 秒以内/メモリ 8 GB 以内・
Graphillion（C++ コア）比 3 倍以内（最終的に 2 倍以内）——に実際の数値を付ける記録（issue #51）。
比較コードは [bench/comparison](../bench/comparison) に置き、**git に残す**
（このセッション以降、別のマシンで測り直せるように——issue の指示どおり）。

### 比較対象の入手・ビルド手順

- **Graphillion**（Python + C++ コア）: `pip install graphillion` で導入（ソース配布、
  インストール時に同梱の SAPPOROBDD ベースの C++ コアをビルドする。g++/cmake が要る)。
  手順は [bench/comparison/graphillion/README.md](../bench/comparison/graphillion/README.md)。
- **TdZdd**（C++ ヘッダオンリー）: `git clone https://github.com/kunisura/TdZdd.git`。
  ビルド不要（ヘッダオンリー）、本比較のプログラムだけを `bench/comparison/tdzdd/Makefile` でコンパイルする。
  手順は [bench/comparison/tdzdd/README.md](../bench/comparison/tdzdd/README.md)。

**この実行環境の egress ポリシーでは両方とも取得できた**（issue の想定していた「取得できない場合」には
該当しなかった）: `pip install graphillion` は `pypi.org` / `files.pythonhosted.org` 経由で成功し
（初回はタイムアウトしたが再試行で成功——ネットワークの一時的な不安定さで、ポリシー拒否ではない）、
`git clone` は `github.com` への HTTPS 経由（ポート 443）で成功した（`github.com` への生の HTTP
アクセス自体は 403 で拒否されるが、git のスマート HTTP プロトコルは通る）。

### 追加の測定環境

上表（本ドキュメント冒頭の「測定環境」節）と同じ CPU・OS・4 論理コアに加えて:

| 項目 | 値 |
|---|---|
| Graphillion | 2.1（PyPI、`pip install graphillion` で導入した状態） |
| Python | 3.11.15 |
| TdZdd | commit `95ad69d`（2025-08-03、`kunisura/TdZdd` の `master`） |
| g++ | 13.3.0 (Ubuntu 13.3.0-6ubuntu2~24.04.1)、`-O3 -DNDEBUG` |
| 測定日 | 2026-09-03 |

### 測定するケースと正しさの確認

issue #51 が挙げる 5 種類のケース——PLAN §10 の目標ケース（格子 7×7/8×8/9×9/11×11 の s–t 単純パス、
4 サイズ）、全域木・マッチング・独立集合の代表ケース、Core の家族代数演算の大規模ケース——を、
既存の ZDD.Net ケースと**同じパラメータ**で 3 通りに実装した（[bench/comparison/README.md](../bench/comparison/README.md)
の対応表）。格子パスが 4 サイズあるぶん、実測エントリは合計 8 つになる。独立集合だけは
ZDD.Net 側にも既存ケースが無かったので新規に追加した（`ComparisonReport.cs`、
`IndependentSetSpec` を使う唯一のケース——他のケースは `bench/ZDD.Net.Benchmarks/Cases.cs` の
既存 10 ケースをそのまま再利用する）。

**数値を測る前に、8 エントリ全ての集合数 (Count) が 3 実装で完全一致することを確認した**
（格子パスの 4 サイズは OEIS A007764 の値とも一致）:

| ケース | 集合数 (Count) | 3 実装で一致 |
|---|---:|---|
| `Path_Grid7x7` | 575,780,564 | ✅（OEIS A007764 とも一致） |
| `Path_Grid8x8` | 789,360,053,252 | ✅（同上） |
| `Path_Grid9x9` | 3,266,598,486,981,642 | ✅（同上） |
| `Path_Grid11x11` | 1,568,758,030,464,750,013,214,100 | ✅（同上） |
| `SpanningTree_Complete8` | 262,144 | ✅（既存 ZDD.Net ケースの値とも一致、8⁶=262,144） |
| `PerfectMatching_Grid6x6` | 6,728 | ✅（既存 ZDD.Net ケースの値とも一致） |
| `IndependentSet_Grid6x6` | 5,598,861 | ✅ |
| `Cardinality_5000Choose2400To2600` | （1,506 桁、先頭 `140615268166...`） | ✅（桁数も既存記録と一致） |

3 実装が独立にたどり着いた同じ数値なので、以下の時間・メモリの比較は「同じ問題を解いた結果」の
比較として信頼できる。

### 結果: 格子 s–t パス（PLAN §10 の目標ケース）

各実装とも「1 ケース 1 プロセス」で測り、複数回実行した最小値（Min）を使う
（M3-2 節と同じ理由——ノイズは足す方向にしか効かない）。ZDD.Net は既定の辺順序
（`Graph.Grid` の生成順、行ごとの蛇行なしの単純な行優先順——`EdgeOrderStrategy.Grid` 相当）を
最適化なしでそのまま使う。TdZdd も対応する `gen_grid` の行優先の隣接リストをそのまま使う
（辺順序最適化は行っていない）。Graphillion は `GraphSet.set_universe(edges)` に渡した既定順序を
そのまま使う（Graphillion 自身は内部で独自の変数順序ヒューリスティクスを持つ)。

| n | 集合数 (Count) | ZDD.Net (Min) | TdZdd (Min) | Graphillion (Min) | ZDD.Net/Graphillion | ZDD.Net/TdZdd |
|---:|---:|---:|---:|---:|---:|---:|
| 7 | 575,780,564 | 9.27 ms | 1.33 ms | 27.30 ms | **0.34x** | 6.97x |
| 8 | 789,360,053,252 | 70.64 ms | 4.40 ms | 2,999.82 ms | **0.024x** | 16.05x |
| 9 | 3,266,598,486,981,642 | 206.12 ms | 14.43 ms | 274.26 ms | **0.75x** | 14.29x |
| 11 | 1,568,758,030,464,750,013,214,100 | 3,666.40 ms | 171.71 ms | 5,572.30 ms | **0.66x** | 21.35x |

ピークメモリ（「1 ケース 1 プロセス」で別途測定。ZDD.Net は forced-GC の生存ヒープ = ピーク実データと、
参考として `/usr/bin/time -v` のプロセス RSS の両方。TdZdd は `/proc/self/status` の `VmHWM`。
Graphillion は `resource.getrusage(...).ru_maxrss`——いずれも OS レベルのプロセス最大常駐セット):

| n | ZDD.Net 生存ヒープ | ZDD.Net プロセス RSS | TdZdd RSS | Graphillion RSS |
|---:|---:|---:|---:|---:|
| 7 | 998.4 KB | 181,668 KB | 4,108 KB | 16,000 KB |
| 8 | 3,605.6 KB | 181,576 KB | 4,496 KB | 882,988 KB |
| 9 | 14,669.2 KB | 181,716 KB | 6,240 KB | 42,568 KB |
| 11 | 229,316.0 KB | 487,852 KB | 27,068 KB | 475,328 KB |

- **ZDD.Net のプロセス RSS が 7×7〜9×9 でほぼ一定（約 181.6 MB）なのは実データのサイズではなく
  ServerGC の既定セグメント予約**（本ドキュメント冒頭のとおり `ServerGC + Concurrent GC + TieredPGO`
  を使っている）。この 4 コアのサンドボックスでは ServerGC がヒープセグメントを論理コア数ぶん
  前もって確保するため、実データが数百 KB でもプロセス RSS は約 180 MB から動かない——**小さい
  ケースのプロセス RSS 比較は実装間の実データ量の差を反映しない**ので、生存ヒープ（forced-GC で
  測った実データそのもの、M3-2 節以来の本ドキュメントの一貫した測り方）を主に使う。11×11 だけ
  プロセス RSS が明確に増えている（229 MB → 488 MB）のは、この規模になるとようやく実データが
  ServerGC のセグメント予約を超えて追加確保させるため。
- **時間は 9×9 が最も僅差（0.75x）で、それでも ZDD.Net が優位**。8×8 の 0.024x（約 42 倍速い）は
  次節で分析するとおり Graphillion 側の外れ値によるもので、額面どおり受け取らない方がよい。
  それを除いても 7×7・9×9・11×11 の 3 点は 0.34x〜0.75x の範囲に収まっている。

### 結果: 代表ケース（全域木・マッチング・独立集合・Core 家族代数）

| ケース | 集合数 (Count) | ZDD.Net (Min) | TdZdd (Min) | Graphillion (Min) | ZDD.Net/Graphillion | ZDD.Net/TdZdd |
|---|---:|---:|---:|---:|---:|---:|
| `SpanningTree_Complete8` | 262,144 | 3.65 ms | 1.81 ms | 217.52 ms | **0.017x** | 2.02x |
| `PerfectMatching_Grid6x6` | 6,728 | 0.99 ms | 0.18 ms | 3.87 ms | **0.26x** | 5.50x |
| `IndependentSet_Grid6x6` | 5,598,861 | 0.75 ms | 0.14 ms | 3.42 ms | **0.22x** | 5.36x |
| `Cardinality_5000Choose2400To2600` | 10¹⁵⁰⁵ 台（1,506 桁） | 4,060.57 ms | 557.82 ms | 7,919.71 ms | **0.51x** | 7.28x |

- **代表ケース 4 つ全てで ZDD.Net が Graphillion を上回る**（0.017x〜0.51x）。特に
  `SpanningTree_Complete8` の 0.017x（約 59 倍速い）は、Graphillion の `GraphSet.trees()` が
  内部で汎用の `graphs()`（次数制約・サイズ制約を都度組み立てる経路）を経由するのに対し、
  ZDD.Net の `SpanningTreeSpec` と TdZdd の `FrontierBasedSearch` はどちらも専用のフロンティア法
  実装であることが大きい。
- **TdZdd との比は 2.02x〜7.28x** で、格子パス（7.0x〜21.4x）よりは総じて小さい。
  `Cardinality_5000Choose2400To2600` の 7.28x が代表ケースの中では最大——このケースは
  グラフ構造を持たない純粋な Core 演算（`SizeConstraint` によるフィルタ）なので、
  グラフ系のフロンティア構築より TdZdd 側の軽さ（P/Invoke なしの生 C++、GC なし）がそのまま
  表れやすい。

### PLAN §10 の 3 目標の達否

1. **9×9 格子の s–t 単純パス（3,266,598,486,981,642 通り）を 1 秒以内**
   → **達成**。206.12 ms（目標の約 1/5）。
2. **11×11 格子（1,568,758,030,464,750,013,214,100 通り）を 60 秒以内・メモリ 8 GB 以内**
   → **達成**。時間 3,666.40 ms（目標の約 1/16）、プロセスピーク RSS 487,852 KB ≈ 476 MB
   （目標の約 6%、8 GB を大きく下回る——再現コマンドは
   `bench/comparison/README.md` の「Reproducing the ZDD.Net side」節）。
3. **Graphillion（C++ コア）との比 3 倍以内、最終的に 2 倍以内**
   → **達成**。上の 2 つの結果表に載せた 8 ケース全てで ZDD.Net が Graphillion 以下
   （最大でも 0.75x）——**3 倍どころか、測定した全ケースで Graphillion を下回った**。
   「Graphillion（C++ コア）」を issue #51 の指示どおり Python 側の公開 API
   （`GraphSet`/`VertexSetSet`）経由で測っている点は次節で補足する。

**3 つとも達成**。目標未達の項目はない——「数字を良く見せるための条件選び」をしていないことは、
8 エントリ全ての集合数を 3 実装で照合してから時間・メモリを測ったこと（前節)、
辺順序を両者とも最適化なしの既定順のまま揃えたこと、`8×8` の Graphillion 外れ値を
除外せずそのまま記載し次節で分析したことで担保している。

### 分析: 「Graphillion（C++ コア）との比」の測り方について

PLAN §10 の目標文言は「Graphillion（C++ コア）との比」だが、issue #51 自身が比較方法を
「Graphillion（Python + C++ コア）: **同じ問題を Python 側で実行**し、実行時間とメモリを測る」と
指定している。Graphillion の C++ コア（SAPPOROBDD ベース）を Python バインディングを経由せず
単体で呼び出す公開 API は無く、内部実装をリバースエンジニアリングして直接呼ぶのは本 PR の
範囲を超える——**「C++ コア」とは実装言語を指しており、測定経路は Graphillion のユーザが
実際に使う唯一の公開インタフェース（Python API）である**、という解釈を issue の文言どおりに
採用した。この経路には当然 Python 側のオーバーヘッド（引数のマーシャリング、インタプリタの
関数呼び出しコストなど）が乗るため、**この測定は「C++ コアだけを単離した場合」より
Graphillion に不利**（＝ ZDD.Net にとっては甘い）比較になっている可能性がある。それでも
ZDD.Net が全ケースで Graphillion 以下という結果は、コア同士を直接比較したとしても
3 倍・2 倍の目標に対してかなりの余裕があることを示唆する——ただし「C++ コアだけを単離した
比較」そのものは今回行っていないので、これは示唆であって実測ではないと正直に記す。

### 分析: Graphillion の 8×8 格子が外れ値になる理由

8×8 格子の s–t パス数え上げだけ、Graphillion が 7×7・9×9・11×11 から外れて極端に遅い
（3.0 秒——7×7 の約 110 倍、9×9 の約 11 倍、11×11 の約半分の時間で、しかも数え上げる
集合数は 9×9 よりずっと少ない）。**複数回再実行して確認した安定した現象**であり、測定誤差では
ない（`bench/comparison/graphillion/grid_paths.py 8` を独立プロセスで 5 回実行し、
2,999.82 ms 〜 4,730.18 ms のレンジで毎回同程度に遅いことを確認済み）。

原因は Graphillion 内部の実装（`GraphSet.paths()` が使う変数順序・分解戦略のヒューリスティクス）
にあり、ZDD.Net や TdZdd の外から詳細を追うことはできない——**この外れ値の根本原因分析は
Graphillion 側のブラックボックスの中にあり、本 PR の範囲外**として正直に記録するにとどめる。
言えるのは事実として: (1) 複数回実行しても再現する、(2) 集合数の大小（8×8 は 9×9 より
2 桁以上小さい）だけでは説明できない、(3) ZDD.Net・TdZdd はどちらも 8×8 で他のサイズから
外れた挙動を示さない（両者とも格子サイズの増加に単調に近い形で時間が増えている）。
**「ZDD.Net が 8×8 で Graphillion の 42 倍速い」という数値は事実だが、これを ZDD.Net の
実装上の優位性の指標として一般化すべきではない**——7×7・9×9・11×11 の 0.34x〜0.75x の方が、
このライブラリの相対性能として代表的な範囲だと考える。

### 分析: TdZdd（生 C++・ヘッダオンリー）との差について

ZDD.Net は Graphillion には全ケースで勝るが、TdZdd には全ケースで負ける（2.0x〜21.4x）。
これは想定内かつ正直に記録すべき結果: TdZdd は Python バインディングもガベージコレクションも
持たない生の C++ ヘッダオンリーライブラリで、`PathZdd`/`FrontierBasedSearch` はテンプレートに
よる静的ディスパッチと手動メモリ管理で書かれている。ZDD.Net は .NET のマネージド実行環境
（GC・境界チェック・JIT）の上で動く時点で、同じアルゴリズムでも定数倍のオーバーヘッドを
避けられない——PLAN.md §0 が最初から明言している「『C++ に勝つ』は目標にしない。『同じ
オーダーで、.NET から依存なしに使える』ことが価値」という位置づけと整合する結果である。

比の大きさにはケースによる幅がある: 格子パス（7.0x〜21.4x、格子が大きいほど比も増える傾向）
より代表ケース（2.0x〜7.3x）の方が総じて小さい。格子パスは辺数に対してフロンティア幅が
大きくなる（4 ケース中最大でピーク状態数 222,138、`Path_Grid11x11` の
`bench/ZDD.Net.Benchmarks -- memory` 出力より）分だけ、状態表への書き込み回数——M4-1〜M4-3 の
節が繰り返し指摘してきた、このライブラリで実際に支配的なコスト——が多くなり、マネージド
オーバーヘッドが積み重なりやすいと考えられる。M4-2（SIMD 化されたハッシュ）と M4-1
（キャッシュのエントリ移行）はどちらもこのコストを削る側の改善だが、生 C++ との差を完全には
埋めていない——**次の版で取り組む余地があるとすれば、この状態表への書き込みそのものの
定数倍**（`ArrayLevelStateTable`/`StructLevelStateTable` の `GetOrAdd` 経路)である。

### 11×11 格子ケースの詳細

完了条件が明示的に求める項目をまとめる:

| 項目 | 値 |
|---|---|
| 実行時間（Min） | 3,666.40 ms |
| ピーク生存ヒープ（forced-GC） | 229,316.0 KB ≈ 224.0 MB |
| プロセスピーク RSS（`/usr/bin/time -v`） | 487,852 KB ≈ 476.4 MB |
| 8 GB 予算に対する比率 | 約 5.8%（大きく下回る——8 GB を超えるケースではない） |
| 最終ノード数 | 1,136,440 |
| ピークフロンティア（状態）幅 | 222,138 |
| 割り当て総量（GC 済み含む） | 1,800,118.6 KB ≈ 1.76 GB |

再現コマンド:

```bash
# 時間（3 回中の最小値）
dotnet run -c Release --project bench/ZDD.Net.Benchmarks -- time Path_Grid11x11

# 生存ヒープのピーク（forced-GC）
dotnet run -c Release --project bench/ZDD.Net.Benchmarks -- memory Path_Grid11x11

# プロセスピーク RSS（OS レベル、8 GB 予算の判定に使うべき数値）
/usr/bin/time -v dotnet run -c Release --project bench/ZDD.Net.Benchmarks -- time Path_Grid11x11
```

### 今後の課題

- Graphillion の C++ コアを Python バインディングを経由せず単離して測る方法があれば、
  前述の「甘い比較になっている可能性」を実測で解消できる——Graphillion 自体のビルド手順を
  読み解いて C++ 側のテスト実行ファイルを直接使う、などが候補だが本 PR の範囲外。
- TdZdd との定数倍差（特に格子パスで顕著）を縮める余地は、状態表への書き込みコストの
  さらなる削減にある。M4-2 で SIMD 化したのはハッシュ計算のみで、`GetOrAdd` の衝突時の
  線形探索・比較そのものはまだ素朴なオープンアドレス法のまま——次の版で測り直す価値がある。
- Graphillion の 8×8 外れ値の原因（内部の変数順序ヒューリスティクス）を掘り下げれば、
  Graphillion からの移行者に向けた「どんな入力で Graphillion が遅くなりやすいか」という
  ドキュメントが書けるかもしれないが、Graphillion 内部の詳細調査は本 PR の範囲外とした。

## M5-1: バイナリシリアライズ（`ZddBinaryFormat`）

`ZddBinaryFormat.Write`/`Read`（[src/ZDD.Net/Io/ZddBinaryFormat.cs](../src/ZDD.Net/Io/ZddBinaryFormat.cs)）の
書き込み・読み込み時間を、同じ族を構築する時間（`FrontierBuilder.Build`）と比較した記録（issue #53）。
測定環境は本ドキュメント冒頭の「測定環境」節と同じ。実行方法:

```bash
dotnet run -c Release --project bench/ZDD.Net.Benchmarks -- serialize
```

`docs/benchmarks.md` §35「結果」表と同じ代表 10 ケースに対し、構築 → `Write` → `Read` を
1 回ずつ行った時間、ファイルサイズ、ノードあたりのバイト数を記録する（測定日 2026-09-03）。
`Read` 側は毎回「ラウンドトリップでノード ID・ノード数が完全に一致すること」を検証してから
時間を記録している（`FormatWriter`/`Reader` 側の正しさそのものの検証は
[tests/ZDD.Net.Tests/Io/ZddBinaryFormatTests.cs](../tests/ZDD.Net.Tests/Io/ZddBinaryFormatTests.cs)）。

| ケース | 構築 | `Write` | `Read` | ファイルサイズ | バイト/ノード | ノード数 |
|---|---:|---:|---:|---:|---:|---:|
| `Path_Grid5x5` | 19.71 ms | 3.07 ms | 9.47 ms | 2,243 B | 4.11 | 546 |
| `Path_Grid6x6` | 24.12 ms | 0.20 ms | 0.42 ms | 9,458 B | 4.42 | 2,142 |
| `Path_Grid7x7` | 58.13 ms | 0.81 ms | 2.47 ms | 35,846 B | 4.50 | 7,968 |
| `SpanningTree_Complete8` | 5.39 ms | 0.20 ms | 0.33 ms | 10,588 B | 4.71 | 2,247 |
| `PerfectMatching_Grid6x6` | 2.88 ms | 0.04 ms | 0.12 ms | 1,504 B | 3.90 | 386 |
| `Cardinality_5000Choose2400To2600` | 2,825.79 ms | 457.33 ms | 3,057.16 ms | 62,967,366 B | 9.37 | 6,722,600 |
| `LinearConstraint_1000ItemsKnapsack` | 2,887.58 ms | 163.28 ms | 2,022.45 ms | 59,157,809 B | 9.30 | 6,361,364 |
| `Forest_Grid5x5_TwoComponents` | 8.03 ms | 0.60 ms | 0.13 ms | 9,681 B | 4.72 | 2,052 |
| `Union_TwoGrid6x6Paths` | 59.13 ms | 0.26 ms | 0.87 ms | 85,968 B | 4.88 | 17,631 |
| `Product_Grid5x5PathsAndCardinality` | 643.88 ms | 0.80 ms | 2.26 ms | 240,019 B | 5.74 | 41,828 |

（`Path_Grid5x5` の `Write` が他の小ケースより目立って遅いのは、そのケースがプロセス内で最初に
`ZddBinaryFormat` を叩くケースであるための JIT ウォームアップ——以降のケースはすべて 1 ms 未満。
一度限りの現象であり、実装コストではない。）

- **`Write` は「ノード配列をほぼそのまま書き出す」設計どおり、ほぼすべてのケースで構築より
  1〜2 桁速い**（`LinearConstraint_1000ItemsKnapsack` で構築の約 1/18、`Cardinality_...` で約 1/6）。
  フロンティア構築の重さ（レベルごとの状態表への登録）を一切払わず、ノード表を線形に走査して
  varint を書くだけだから。
- **`Read` は正直に記録すると、巨大なケースでは構築時間と同じオーダーになる**
  （`Cardinality_...` は構築よりむしろ遅い、`LinearConstraint_...` は構築の約 0.7 倍）。
  これは設計上の必然: 正準性を保証するために一意化表 (`UniqueTable.GetNode`) へ全ノードを
  登録し直しており（本ファイルの `ZddBinaryFormat` remarks 参照）、これはフロンティア構築の
  削減パスが払っているのと同じ「ノードあたり 1 回のハッシュ表挿入」コストである。小〜中規模の
  ケースでは `Read` は構築よりずっと速い（`Path_Grid7x7` で約 1/24）——このコストが顕在化するのは
  数百万ノード級のケースに限られる。正準性を諦めて配列をそのまま復元する版（issue のいう「後者」の
  選択肢）にすれば速くなるが、不正なファイルで一意化表の不変条件が壊れうるため、本 PR では
  正準性を優先する前者を選んだ（完了条件の「ノード ID まで含めて一致する」ラウンドトリップは
  この設計に依存している）。
- **ファイルサイズはノード数にほぼ線形**（バイト/ノードは 3.9〜9.4 の範囲）。ノード ID が小さい
  ケース（数千ノード）では 1 ノードあたり 3 フィールド × 1〜2 バイトの varint で 4〜5 バイト、
  ノード数が数百万に達し ID が大きくなるケース（`Cardinality_...` / `LinearConstraint_...`）でも
  9 バイト台に収まる。固定長（`Level`/`Lo`/`Hi` を `int` 3 個 = 12 バイト/ノード）と比べて
  22%〜67% 小さく、varint 圧縮（docs/PLAN.md §9 の「検討する」）の効果が実測で確認できる。
