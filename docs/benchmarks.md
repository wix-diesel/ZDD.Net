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
