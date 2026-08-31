# ZDD.Net

C# ネイティブ実装の ZDD（Zero-suppressed Decision Diagram）／フロンティア法ライブラリ。

- 100% managed C#（P/Invoke なし・NativeAOT 対応・外部 NuGet 依存ゼロ）
- ターゲット: `net10.0`
- 構成: Core（ZDD エンジン）／ Frontier（フロンティア法フレームワーク）／ Graphs（グラフ問題 API）

集合の族（family of sets）を 1 つの DAG に圧縮して表す ZDD を使うと、「10^24 個の解を数える」
「一様ランダムに 1 つ選ぶ」「重みが最大の集合を求める」といった操作を、族を展開せずノード数に
比例する手間で行える。.NET にはこれのネイティブ実装が事実上存在しない（CUDD の P/Invoke ラッパ
しか選択肢がない）ことが、このライブラリの動機になっている。

## 到達点（v0.1 = Core のみ）

現在のバージョンは **Core レイヤ（ZDD エンジン）のみ**を提供する:

- `ZddManager` / `Zdd` によるノード表・一意化表・演算キャッシュと、家族代数の全演算
  （和・積・差・対称差・積(`*`)・商・剰余・Meet・`SupersetsOf`/`SubsetsOf` などのふるい・
  `Change`/`OnSet`/`OffSet`・`Maximal`/`Minimal`/`HittingSets`/`Complement`）
- 濃度（`Count` / `CountApprox` / `CountBySize`）・列挙（`Sets`）・unranking/ranking
  （`ElementAt` / `IndexOf`）・一様ランダムサンプリング（`Sample`）
- 重み最適化（`MaxWeight` / `MinWeight` / `TopK`）、確率・期待値（`Probability` /
  `ExpectedValue` / `ItemFrequency`）
- Graphviz DOT 出力（`ToDot` / `WriteDot`）

**フロンティア法フレームワーク（Frontier）とグラフ問題 API（Graphs）は v0.2 以降**。
「経路列挙・数え上げ」のような高レベルなグラフ操作が要る場合は、それらが揃うまで待つか、
Core の家族代数を直接組み合わせて構築する必要がある。

**API はまだ確定していない**（プレリリース版）。v1.0 まではブレーキングチェンジがあり得る。

## インストール

NuGet パッケージは v0.1.0 のプレリリースタグから生成される（プレリリース版のため `--prerelease` が要る）。

```sh
dotnet add package ZDD.Net --prerelease
```

## 最小サンプル

```csharp
using ZDD.Net.Core;

using ZddManager manager = new ZddManager(variableCount: 3);

// 2^{0,1,2} = {∅, {0}, {1}, {2}, {0,1}, {0,2}, {1,2}, {0,1,2}}
Zdd powerSet = manager.Empty.Complement();
Console.WriteLine(powerSet.Count); // 8

// item 0 を含む集合だけを残す。
Zdd containingItem0 = powerSet.OnSet(0);
Console.WriteLine(containingItem0.Count); // 4

foreach (int[] set in containingItem0.Sets())
{
    Console.WriteLine(string.Join(",", set));
}
```

もう少し長い例（家族代数演算・列挙・unranking・一様サンプリング・重み最適化・カスタム評価器）は
[docs/api-guide.md](docs/api-guide.md) と、実際に動く [`samples/Zdd.ApiGuide`](samples/Zdd.ApiGuide) を参照。
CLI から触ってみたい場合は [`samples/Zdd.Cli`](samples/Zdd.Cli)（`dotnet run --project samples/Zdd.Cli -- --help`）。

## ドキュメント

- **[docs/api-guide.md](docs/api-guide.md)** — API ガイド（`ZddManager`/`Zdd` の使い方、演算一覧、性能上の注意）
- **[docs/PLAN.md](docs/PLAN.md)** — 機能・仕様・アーキテクチャ
- **[docs/ROADMAP.md](docs/ROADMAP.md)** — マイルストーン別の PR 単位タスク分割
- **[docs/OPEN-QUESTIONS.md](docs/OPEN-QUESTIONS.md)** — 未確定事項
- **[CHANGELOG.md](CHANGELOG.md)** — 変更履歴

## ライセンス

Apache-2.0

参考にしたアルゴリズムの出典は [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) を参照。
