# スペックの書き方（フロンティア法）

「利用者は**状態遷移だけ**を書き、ZDD は自動で構築される」——これが本ライブラリの中核価値であり、
その契約が `ZDD.Net.Frontier` の**スペック**インタフェースである。この資料はその規約をまとめる。

- 対象バージョン: v0.2
- 公開の構築器 `FrontierBuilder.Build` は `IDdSpec<TState>` と `IArrayDdSpec` の両方に対応する
  オーバーロードを持ち、ここに載っているコードはそのまま呼べる。組み込みスペックの一覧や
  `Graph`/`FrontierManager`/`BuildOptions` の使い方は [docs/frontier-guide.md](frontier-guide.md) を参照。
  `IHybridDdSpec<TScalar>` 版のオーバーロードは未対応（v0.3 以降）。
- 設計の背景は [docs/PLAN.md](PLAN.md) §6

---

## 1. スペックとは何か

フロンティア法は、「アイテムを 1 個ずつ『入れる／入れない』と決めていき、**以降の判定に必要な情報だけ**を
状態として持ち回る」という探索である。同じ状態に行き着いた枝はそこから先が完全に同じなので、1 つに
まとめられる。これを水準（level）ごとに幅優先で行い、まとめた結果をそのまま DAG にすると ZDD ができる。

利用者が書くのは、次の 2 つだけである。

1. **根の状態**（`GetRoot`）
2. **状態 + 枝（0/1）→ 次の状態**（`GetChild`）

集合を 1 つも展開しないため、10^24 個の解を持つ族でも、状態の種類の数だけの手間で構築できる。

## 2. 水準（level）と item の対応

| 用語 | 意味 |
|---|---|
| item | 公開 API 側の 0 始まりのアイテム番号（`0 .. VariableCount-1`） |
| level | スペック側の水準。`VariableCount`（根側）から `1`（末端側）へ下る |

対応は `item = VariableCount - level`（Core が内部で使っている規約と同じ）。
つまり **level `VariableCount` が最初に決めるアイテム（item 0）**、level `1` が最後のアイテムである。

グラフ問題では「item 0 = 辺 0」と割り当てるので、辺順序の先頭が根側に来る。

## 3. 戻り値の規約

`GetRoot` / `GetChild` の戻り値は 1 つの `int` に 3 つの意味を詰めている。TdZdd と同じ符号化で、
C++ のスペックをほぼそのまま移植できる。

| 戻り値 | 意味 | 定数 |
|---|---|---|
| `0` | ⊥ 終端（∅）。この枝は解を持たないので刈る | `DdResult.False` |
| `-1` | ⊤ 終端（`{∅}`）。ここまでの選択で 1 つの解が確定 | `DdResult.True` |
| 正数 | 子の状態の水準。**現在の水準より必ず小さい** | — |

判定には `DdResult.IsTerminal(result)` が使える。

### ⊤ を返すときの注意（ゼロサプレス）

⊤ を返すと、**残りのアイテムはすべて「入れない」に確定する**。ZDD のゼロサプレス規則がそう働くためで、
「残りは自由」という意味には**ならない**。「もう何を選んでもよい」を表したいときは、⊤ に飛ばさず、
残りの水準を素通しする状態を返す（組み込みの `PowerSetSpec`（M2-5）がまさにそれ）。

### 水準を飛ばすとき

`GetChild` が `level - 1` より小さい水準を返してもよい。飛ばされた水準のアイテムは「入れない」に確定する。
逆に `level` 以上を返してはならない（構築が終わらなくなる）。

## 4. `IDdSpec<TState>` の契約

```csharp
public interface IDdSpec<TState>
{
    int  GetRoot(ref TState state);
    int  GetChild(ref TState state, int level, int value);
    bool StateEquals(in TState left, in TState right);
    int  StateHashCode(in TState state);
}
```

### 状態の寿命と書き換えてよい範囲

- `GetRoot` の `state` は**既定値で初期化済み**。以降読む可能性のあるフィールドは全部書くこと。
- `GetChild` の `state` は**親状態のコピー**。呼び出し側（構築器）が枝ごとにコピーを渡すので、
  **その場で書き換えてよい**。0 枝の呼び出しが 1 枝の結果に影響することはない。
  ただしこれが成り立つのは `TState` が `struct` のときだけである。参照型にするとコピーされるのは
  参照だけなので、指す先を書き換えると兄弟の枝を壊す（7 節のとおり `struct` を強く勧める理由の 1 つ）。
- 状態の記憶域は構築器が所有する。**呼び出しの外へ持ち出してはならない**
  （`IArrayDdSpec` の `Span<int>` はとくに、呼び出し中のみ有効）。
- 終端を返したときの状態の中身は読まれない。書き換えたままで構わない。

### `GetChild` が返してよい水準

`1 <= 戻り値 < level`、または `DdResult.False` / `DdResult.True`。

### `StateEquals` / `StateHashCode` の整合性

- 等しい状態は**必ず**同じハッシュを返すこと。破ると同じ状態が別々のノードとして残り、
  同じ部分図を何度も作ることになる（結果は正しいが遅く、大きい）。
- `StateEquals` は同値関係（反射・対称・推移）であること。
- 呼ばれるのは**同じ水準の状態どうし**だけ。水準をまたいだ比較は起きない。
- どちらもスペック外の可変な状態に依存しないこと。構築器は呼ぶ順序を約束しない。

### 状態は「以降の遷移に影響する情報だけ」を持つ

これが**幅（＝計算量とメモリ）を決める唯一の要因**である。余分な情報を 1 bit 持つだけで状態が分裂し、
まとめられるはずの枝が別々に育つ。

- すでに使い終わった情報は捨てる（フロンティアから出た頂点のスロットはクリアする）。
- 表現は**正準化**する。「以降の振る舞いが同じ状態」は、必ず同じ表現になるようにする
  （例: 連結成分の番号は、出現順に振り直してから比較する）。
- 「これまでに選んだアイテムの集合」のような履歴そのものは、**絶対に状態に入れない**。
- `IArrayDdSpec` のスロットに入れる値は**小さく保つ**。状態は内部で 1 スロット 1／2／4 バイトに
  詰められ、幅は値域で決まる（M3-2）。既定の窓は `-8..247` なので、フロンティア内のスロット番号と
  小さな番兵（`-1` / `-2` など）で表せば 1 バイトに収まり、`int[]` 時代の 1/4 のメモリで済む。
  頂点 ID や大きなカウンタを直接入れると幅が広がる。

## 5. 例: ちょうど k 個選ぶ

状態は「これまでに選んだ個数」だけでよい。どのアイテムを選んだかは、以降の判定に影響しない。

```csharp
using ZDD.Net.Frontier;

// n 個のアイテムから、ちょうど k 個を選ぶ集合すべて（= 二項係数 C(n, k) 個）。
public readonly struct ExactlyKSpec : IDdSpec<int>
{
    private readonly int _itemCount;
    private readonly int _k;

    public ExactlyKSpec(int itemCount, int k)
    {
        _itemCount = itemCount;
        _k = k;
    }

    public int GetRoot(ref int taken)
    {
        taken = 0;
        return _itemCount;                                  // level n が item 0 を決める
    }

    public int GetChild(ref int taken, int level, int value)
    {
        taken += value;

        if (taken > _k) { return DdResult.False; }          // 超えた: 刈る
        if (taken == _k) { return DdResult.True; }          // 揃った: 残りは「入れない」に確定

        int remaining = level - 1;                          // まだ決めていないアイテムの個数
        if (taken + remaining < _k) { return DdResult.False; }  // 先読み枝刈り: もう届かない

        return remaining;                                   // 次の水準へ
    }

    public bool StateEquals(in int left, in int right) => left == right;
    public int StateHashCode(in int state) => state;
}
```

読みどころ:

- **状態が小さい**。水準ごとに現れる状態は高々 `k+1` 種類なので、ZDD の幅も `k+1` で収まる。
- **`taken == _k` で ⊤ に飛ばす**のは、残りを全部「入れない」にするのと同じ意味（3 節）。
- **先読み枝刈り**（`taken + remaining < _k`）は正しさには不要だが、⊥ に落ちるだけの部分木を
  作らずに済む。フロンティア法では、この手の枝刈りが効くかどうかが実行時間を左右する。
- `k > n` でも枝刈りが全部の枝を ⊥ に落とすので、族は空になる（特別扱いは不要）。
  ただし `n == 0` は根が水準 0（＝⊥）になってしまうので、この例はアイテムが 1 個以上あることを前提にしている。

## 6. `IArrayDdSpec` と `IHybridDdSpec<TScalar>` の使い分け

| インタフェース | 状態 | 使いどころ |
|---|---|---|
| `IDdSpec<TState>` | 固定長の struct | 大きさがコンパイル時に決まる。基数制約・線形制約・オートマトン |
| `IArrayDdSpec` | `int` の可変長配列 | 大きさが実行時に決まる。mate 配列・comp 配列 |
| `IHybridDdSpec<TScalar>` | スカラ + `int` 配列 | 上の 2 つの複合。「mate 配列 + 残り辺数のカウンタ」など |

配列部分の等価判定とハッシュは**構築器が要素ごとに行う**ので、利用者は書かなくてよい。
その代わり、**意味を持たなくなったスロットは決まった値（通常 0）に戻す**こと。
ゴミが残っていると、同じ意味の状態が別物として扱われる。

配列の長さは `ArrayLength` で 1 度だけ問い合わせる。構築中に変えてはならない。

## 7. スペックは `struct` で書く

構築器はスペックを**型引数 + `struct` 制約**で受ける（`where TSpec : struct, IDdSpec<TState>`）。

```csharp
// 構築器の署名（M2-4 で実装済み）
public static Zdd Build<TSpec, TState>(ZddManager manager, TSpec spec, BuildOptions? options = null)
    where TSpec : struct, IDdSpec<TState>;

public static Zdd Build<TSpec>(ZddManager manager, TSpec spec, BuildOptions? options = null)
    where TSpec : struct, IArrayDdSpec;
```

`class` で書いても動くが、`GetChild` は**状態 1 個 × 枝 2 本ごと**に呼ばれる最も内側のループなので、
仮想呼び出しになると全体が数倍遅くなる。`struct` にしておけば JIT がスペックごとに特殊化し、
`GetChild` はインライン展開される。同じ方針の背景は
[docs/api-guide.md](api-guide.md) §5.1（`IDdEval` / `IWeightOps`）にも書いてある。

## 8. 参考

- [docs/frontier-guide.md](frontier-guide.md) — フロンティア法フレームワーク全体の使い方
  （組み込みスペック一覧・`Graph`/`FrontierManager`/`BuildOptions`・性能の勘所）
- [TdZdd ユーザガイド](https://github.com/kunisura/TdZdd/blob/master/userguide.md) — 戻り値の規約の出典
- [docs/PLAN.md](PLAN.md) §6（フロンティア法フレームワーク）・§7（組み込みスペック）
- [docs/ROADMAP.md](ROADMAP.md) M2（このフレームワークの PR 分割）
