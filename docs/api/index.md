# API リファレンス

`src/ZDD.Net` の全 public API から自動生成されたリファレンス（`GenerateDocumentationFile` の
XML doc コメントが元データ）。まず触るべき型は左のツリーの `ZDD.Net.Core` 名前空間から:

- `ZddManager`: 族の生成と所有（[API ガイド](../api-guide.md) §2）
- `Zdd`: 族を表すハンドル本体、家族代数の全演算（[API ガイド](../api-guide.md) §3）

グラフ問題を高レベル API で解きたいなら `ZDD.Net.Graphs` 名前空間の `GraphSet` / `Graph` から
（[フロンティア法ガイド](../frontier-guide.md) §9）。任意の要素型の族を扱いたいなら
`ZDD.Net.Sets` 名前空間の `SetSet<T>` / `SetUniverse<T>`。独自の組み合わせ問題を解きたいなら
`ZDD.Net.Frontier` 名前空間の `FrontierBuilder` / `IDdSpec<TState>`
（[フロンティア法ガイド](../frontier-guide.md) §7、[フロンティア仕様ガイド](../frontier-spec-guide.md)）。

グラフ・ZDD の読み書きは `ZDD.Net.Io` 名前空間（`DimacsGraph` / `EdgeListGraph` /
`SimpleTextGraph` / `ZddBinaryFormat` / `GraphillionTextFormat` / `DotOptions`）を参照。
