# Graphillion 互換 I/O ガイド

`ZDD.Net.Io.GraphillionTextFormat` は、Python [Graphillion](https://github.com/graphillion/graphillion)
の `setset.dump`/`dumps`/`load`/`loads`（内部的には SAPPOROBDD の ZDD ダンプ形式）と互換のテキスト形式で
`Zdd` を読み書きする（[docs/PLAN.md](PLAN.md) §9、[docs/ROADMAP.md](ROADMAP.md) M5-2、issue #54）。
独自バイナリ形式（`ZDD.Net.Io.ZddBinaryFormat`）とは別物で、こちらは **Python の Graphillion と
往復できること**が目的——移行（Python 資産の持ち込み）と検証（独立実装との相互照合）の両方に使える。

- 対象バージョン: v0.5（M5「I/O・メモリ管理」）

---

## 1. 形式の出典

この形式に公式な仕様書は存在しない。ここに書いた内容は、**推測ではなく実データから確定させた**もの:

1. `pip install graphillion`（PyPI、`graphillion==2.1`、Python 3.11）で実際にインストール
2. 既知の族を `dump()` して、生成されたテキストを直接観察
3. Graphillion 自身のソース（`github.com/graphillion/graphillion` の
   `src/graphillion/zdd.cc` の `dump`/`load` 関数、SAPPOROBDD 由来）を突き合わせて確認

`bench/comparison/graphillion/README.md` に書かれているのと同じ手順（`pip install graphillion`）で
再現できる。

## 2. テキスト形式

平文 ASCII、1 行 1 ノード:

- **自明な族**（⊥ = 空族、⊤ = `{∅}`）は `B` または `T` の 1 行 + 終端の `.` 行だけになる:
  ```
  B
  .
  ```
- **それ以外の族**は、根から到達可能な非終端ノードを 1 行ずつ、
  `<id> <elem> <lo> <hi>`（空白区切り）で列挙し、最後に `.` 行で終端する。
  - `id` は、ファイル内で相互参照するためだけの不透明な整数（Graphillion 自身は内部の
    SAPPOROBDD ノード ID を使うが、`GraphillionTextFormat.Write` は本ライブラリ自身の
    ノード ID をそのまま使う——どちらでも形式上は問題ない）
  - `lo`/`hi` は終端なら `B`/`T`、そうでなければ「それより前の行の `id`」
  - ノードは**依存関係の順**（子は親より前）に書かれる。この結果、**根は必ず `.` の直前の行**になる
    ——形式自体には「どれが根か」を明示するフィールドが無く、これは Graphillion 自身の `load()` も
    同じ方法（最後に組み立てたノードをそのまま根として返す）で根を決めている

例（3 要素の族、下記 3 節で使う例と同じ）:

```
4 3 B T
2 2 B T
14 1 2 4
.
```

## 3. レベルの向きの対応 ★最重要

ここを取り違えると「読めるが中身が上下逆」という気付きにくいバグになる（issue #54 の警告どおり）。

- Graphillion の `elem` は **1 始まり、根側から数える**（根から下って最初に判定される変数が
  elem 1。終端に隣接する変数が最大の elem）
- 本ライブラリの `ZddManager` が公開している **0 始まりの item インデックス**も、実は
  **同じ向き**で数えている（item 0 が根から最初に判定される変数——`ZddManager` 自身の doc 参照）。
  内部専用の `Level` フィールド（1 = 葉側 … N = 根側、TdZdd 互換、`docs/PLAN.md` の
  `ZddNode` 定義参照）はこの対応には登場しない——`GraphillionTextFormat` の内部実装が
  `ZddManager.LevelOf`/`ItemOf` 経由で吸収する

したがって対応は単純な **0 始まり/1 始まりのオフセットだけ**:

```
elem = item + 1
item = elem - 1
```

実装では `item` という値を経由せず、`ItemOf`/`LevelOf` にオフセット込みでそのまま渡している:

- `Write`: `elem = manager.ItemOf(node.Level) + 1`（`ItemOf` がノードの `Level` を item に変換し、+1 する）
- `Read`: `level = manager.LevelOf(elem - 1)`（`elem - 1` を item として `LevelOf` に渡し、対応する `Level` を得る——
  戻り値は item ではなく **Level** であることに注意）

**これは「対称な族では検出できない」**。上下を取り違えても、族がひっくり返した形とたまたま
一致してしまう族があるため。`GraphillionTextFormatTests` の `RoundTripsAnAsymmetricHandBuiltFamily`
や、非正方格子（3 行 2 列）の s–t パス族を使ったテストは、意図的に「上下反転すると別の族になる」
族を選んでいる。

## 4. 検証（issue #54 の完了条件）

### 4.1 Python 側の出力を読み込んで一致することの確認

`tests/ZDD.Net.Tests/TestData/Graphillion/` に、実際に Graphillion 2.1 で生成したダンプファイルと、
それを生成した Python スクリプト（`generate_fixtures.py`）を格納している:

- `triangle_family.zdd.txt`: 3 要素の手作り族（上下非対称）。レベルの向きだけを検出する最小ケース
- `grid_3x2_paths.zdd.txt`: 3×2 格子の s–t 単純パス族（7 辺、非正方形なので上下非対称）。
  `GraphillionTextFormatTests.ReadsTheGridPathsFixtureMatchingAnIndependentlyBuiltZddNetFamily` が、
  これを読み込んだ結果と、`Graph.Grid(3, 2)` + `PathSpec` で本ライブラリ側で独立に構築した族を
  `Count` と列挙した族の両方で突き合わせている

再生成する場合（形式が変わることは無い前提なので通常は不要）:

```sh
cd tests/ZDD.Net.Tests/TestData/Graphillion
python3 -m venv .venv && source .venv/bin/activate
pip install graphillion
python3 generate_fixtures.py
```

### 4.2 ラウンドトリップ（本ライブラリ → 本ライブラリ）

`GraphillionTextFormatTests` の `RoundTrips*` 系のテストが、`Write` → `Read` で族が一致することを
確認している（空族・全体集合・非対称な手組み族・大きなパス族・変数 10 万の深い族）。

### 4.3 本ライブラリの出力が Graphillion 側で読めることの確認（手動確認）

CI では Graphillion のインストールを前提にしないため、この方向は手動で確認した手順をここに残す
（issue #54 の完了条件のとおり）。実際に以下を実行し、`gs == expected` が `True` になることを確認済み:

```csharp
// C# 側: 3x2 格子の s-t パス族を組み立てて GraphillionTextFormat で書き出す
Graph grid = Graph.Grid(3, 2);
using ZddManager manager = new ZddManager(grid.EdgeCount);
Zdd paths = FrontierBuilder.Build<PathSpec>(manager, new PathSpec(grid, 0, grid.VertexCount - 1));
File.WriteAllText("zddnet_grid3x2_paths.zdd.txt", GraphillionTextFormat.Write(paths));
```

```python
# Python 側: 同じグラフ・同じ辺順序の universe を用意し、C# 側の出力を Graphillion で読み込む
from graphillion import GraphSet

rows, cols = 3, 2
def v(r, c): return r * cols + c
edges = []
for r in range(rows):
    for c in range(cols - 1):
        edges.append((v(r, c), v(r, c + 1)))
    if r < rows - 1:
        for c in range(cols):
            edges.append((v(r, c), v(r + 1, c)))
GraphSet.set_universe(edges, traversal="as-is")

with open("zddnet_grid3x2_paths.zdd.txt") as f:
    gs = GraphSet.load(f)

expected = GraphSet.paths(v(0, 0), v(rows - 1, cols - 1))
assert gs == expected  # True — 本ライブラリの出力を Graphillion がそのまま読める
```

C# 側の辺順序生成ループ（`Graph.Grid(rows, cols)`）と Python 側の `grid_3x2_paths()`（`generate_fixtures.py`）
は同じ順序で辺を並べるため、`traversal="as-is"` を指定するだけで item ↔ elem の対応が両者で一致する。

## 5. 形式で表現できないケース

- ファイル自体に変数の総数（universe のサイズ）が記録されていない。族が使っている最大の
  `elem` しか分からず、それは universe の実際のサイズより小さいことがある（族が上位の変数を
  一度も使わない場合）。`Read` の `variableCount` 引数で明示的に指定できる。指定した値より
  大きい `elem` がファイルに含まれる場合は `ZddFormatException`（「表現できない」ケースの明確な例外化）
- 壊れた入力（行のフィールド数が違う、`.` 終端が無い、前方参照、`hi` が ⊥ 終端、レベル順序違反など）
  は必ず `ZddFormatException` になり、クラッシュしたり誤った族を静かに構築したりしない
  （`GraphillionTextFormatTests` の該当テスト群を参照）

## 6. 本体の依存

`GraphillionTextFormat` は `System.IO`/`System.Collections.Generic` などの BCL のみを使い、
`src/ZDD.Net` の `PackageReference` は引き続き 0 のまま（`DependencyPolicyTests` で検査）。
