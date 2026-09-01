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

# ピークフロンティア幅・最終ノード数（IProgress の履歴から。時間計測は行わない）
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
| `Union_TwoGrid6x6Paths` | 6×6 格子、2 つの `PathSpec` の `Union` | 50.60 ms | 3,426.6 KB | 436,619,868 | 1,745 | 15,520 |
| `Product_Grid5x5PathsAndCardinality` | 5×5 格子パス × カーディナリティ制約の `Product` | 772.5 ms | 5,215.0 KB | 151,724,411,004 | 125 | 13,373 |

割り当てメモリは BenchmarkDotNet の `MemoryDiagnoser`（1 回のビルドが確保した総バイト数、GC 済みの
一時領域も含む）。「集合数 (Count)」は各ケースが表す族の要素数（`Zdd.Count`）で、桁数が大きいものは
概数と桁数のみ記載する（生の `BigInteger` は `stats` の出力を参照）。「ピークフロンティア幅」と
「最終ノード数」は主となる `FrontierBuilder.Build` 呼び出し 1 回分（`Union` / `Product` ケースでは
その左オペランドの構築）を `BuildOptions.Progress` で記録した履歴の最大値・[bench/ZDD.Net.Benchmarks/Cases.cs](../bench/ZDD.Net.Benchmarks/Cases.cs)
参照。

生の BenchmarkDotNet レポートは `bench/ZDD.Net.Benchmarks` 実行時に
`BenchmarkDotNet.Artifacts/results/` 配下へ出力される（このリポジトリでは追跡しない）。
