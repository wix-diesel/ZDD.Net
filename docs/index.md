# ZDD.Net

C# ネイティブ実装の ZDD（Zero-suppressed Decision Diagram）／フロンティア法ライブラリ。

- 100% managed C#（P/Invoke なし・NativeAOT 対応・外部 NuGet 依存ゼロ）
- ターゲット: `net10.0`
- 構成: Core（ZDD エンジン）／ Frontier（フロンティア法フレームワーク）／ Graphs（グラフ問題 API）

集合の族（family of sets）を 1 つの DAG に圧縮して表す ZDD を使うと、「10^24 個の解を数える」
「一様ランダムに 1 つ選ぶ」「重みが最大の集合を求める」といった操作を、族を展開せずノード数に
比例する手間で行える。.NET にはこれのネイティブ実装が事実上存在しない（CUDD の P/Invoke ラッパ
しか選択肢がない）ことが、このライブラリの動機になっている。

**API はまだ確定していない**（プレリリース版）。v1.0 まではブレーキングチェンジがあり得る。
リポジトリ本体は [github.com/wix-diesel/ZDD.Net](https://github.com/wix-diesel/ZDD.Net)。

## インストール

```sh
dotnet add package ZDD.Net --prerelease
```

## 最小サンプル

`GraphSet` を使った 5 行サンプル（5&times;5 格子の対角 s&ndash;t 単純パスを 1 本も展開せずに数える）:

```csharp
using System;
using ZDD.Net.Graphs;

Graph grid = Graph.Grid(5, 5);
GraphSet paths = GraphSet.Paths(grid, from: 0, to: grid.VertexCount - 1);
Console.WriteLine(paths.Count); // 8512（OEIS A007764）
```

## このサイトの歩き方

- 初めて触るなら [チュートリアル](tutorial.md) から。「格子グラフの s&ndash;t パスを数える」から
  「実グラフを読み込んで解く」までを一直線に辿れる一本道の入門
- `ZddManager` / `Zdd` の Core API を体系的に知りたいなら [API ガイド](api-guide.md)
- フロンティア法フレームワーク（`FrontierBuilder` / `IDdSpec<TState>` / 組み込みスペック一覧 /
  独自スペックの書き方）は [フロンティア法ガイド](frontier-guide.md)、独自スペックを書く際の
  契約の詳細は [フロンティア仕様ガイド](frontier-spec-guide.md)
- Python [Graphillion](https://github.com/graphillion/graphillion) との相互運用は
  [Graphillion 互換 I/O ガイド](graphillion-io.md)
- 性能の実測値（構築時間・メモリ・Graphillion / TdZdd との比較）は [ベンチマーク](benchmarks.md)
- 型・メンバ単位の詳しいリファレンスは [API リファレンス](xref:ZDD.Net.Core.ZddManager)（自動生成、
  全 public API に XML doc 付き）
- 設計の背景や意思決定は [実装計画（PLAN）](PLAN.md) と [未解決の論点](OPEN-QUESTIONS.md)、
  マイルストーンごとの進め方は [タスク分割・ロードマップ](ROADMAP.md)
- バージョンごとの変更点は [リリースノート](release-notes/v0.4.0.md)
