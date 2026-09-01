# ZDD.Net API ガイド

`ZddManager` と `Zdd` の使い方をまとめた資料。ZDD を初めて触る読者が最初の 1 本を書けるところまでを
狙っている。仕様の全体像は [docs/PLAN.md](PLAN.md)、マイルストーンの分割は
[docs/ROADMAP.md](ROADMAP.md) を参照。

このガイドに載っているコード片は、すべて [`samples/Zdd.ApiGuide/Program.cs`](../samples/Zdd.ApiGuide/Program.cs)
にそのまま置いてあり、CI が毎回ビルドして実行している（`.github/workflows/ci.yml` の
「api-guide サンプルの実行」）。手元で確かめたいときは:

```sh
dotnet run --project samples/Zdd.ApiGuide
```

- 対象バージョン: v0.2（Core エンジン。フロンティア法・グラフ API は [docs/frontier-guide.md](frontier-guide.md) を参照）

---

## 1. ZDD とは何か

ZDD（Zero-suppressed Decision Diagram）は、**集合の族**（family of sets、集合を要素に持つ集合）を
1 つの DAG（有向非巡回グラフ）に圧縮して表す構造。たとえば `{{0}, {1,2}, {0,1,2}}` のような
「複数の集合をまとめて 1 つ持つ」ものを表現する。

- 決定木のように、1 つの item（変数）ごとに「含む／含まない」で枝分かれする。
- 同じ形の部分木は 1 つに共有される（正準形）ので、族に属する集合が指数的に多くても
  ノード数はずっと小さく収まることが多い。
- 「族に属する集合の個数を数える」「一様ランダムに 1 つ選ぶ」「重みが最大の集合を求める」
  といった操作が、**族を展開せずノード数に比例する手間**で行える。これが ZDD の価値であり、
  「10^24 個の解の中から 1 つ」を実用速度で扱える理由になっている。

ZDD.Net では、この「族」を `Zdd` という値型ハンドルで表す。`Zdd` 自体は
「所有マネージャへの参照」と「ノード ID」だけを持つ 16 バイトの構造体で、実体は
`ZddManager` が抱えるノード表にある。

```csharp
using ZDD.Net.Core;

using ZddManager manager = new ZddManager(variableCount: 3);

// 2^{0,1,2} = {∅, {0}, {1}, {2}, {0,1}, {0,2}, {1,2}, {0,1,2}}
Zdd powerSet = manager.Empty.Complement();
Console.WriteLine(powerSet.Count); // 8

// item 0 を含む集合だけを残す。
Zdd containingItem0 = powerSet.OnSet(0);
Console.WriteLine(containingItem0.Count); // 4
```

## 2. `ZddManager`: 族の生成と所有

`ZddManager` はノード表・一意化表・演算キャッシュを抱え、そこから生まれるすべての `Zdd` を
所有する。使い方の要点:

- コンストラクタで **変数（item）の個数を固定**する。`new ZddManager(variableCount: n)` の
  `n` は、扱う item index の個数（有効な item index は `0 .. n-1`）。生成後に増やすことはできない。
- `IDisposable`。`using` で破棄すると内部の表への参照を手放し、GC が回収できるようになる。
  アンマネージド資源は持たないので、破棄を忘れてもリークはしない（大きな配列の回収が
  GC 任せになるだけ）。
- **スレッドセーフではない**。1 つのマネージャを複数スレッドから同時に触ってはならない。
- 異なるマネージャの `Zdd` を混ぜて演算すると `ArgumentException` になる。ノード ID の意味は
  マネージャごとに違うため。

基本の族:

| メンバ | 意味 |
|---|---|
| `manager.Empty` | 空の族 ∅（要素を 1 つも持たない） |
| `manager.Base` | `{∅}`（空集合だけを要素に持つ族） |
| `manager.Singleton(item)` | 1 要素集合 `{{item}}` だけを持つ族 |
| `manager.VariableCount` | この manager が扱う item の個数 |
| `manager.NodeCount` | manager 全体で共有している非終端ノードの総数 |
| `manager.GetStatistics()` | ノード表・一意化表・演算キャッシュの統計（`ZddStatistics`） |

```csharp
using ZddManager manager = new ZddManager(variableCount: 4);

Zdd a = manager.Singleton(0) | manager.Singleton(1); // {{0}, {1}}
Zdd b = manager.Singleton(1) | manager.Singleton(2); // {{1}, {2}}
```

必要ならノード表・演算キャッシュの初期容量を `ZddManagerOptions` で調整できるが、
既定値のままで実用上は問題ない（族の規模が事前に分かっているときだけ触ればよい）。

## 3. 家族代数: 演算一覧

`Zdd` の演算はすべて `F op G` の形（族どうしの演算）か `F.Op(item)` の形（1 つの item に対する操作）で、
どちらも実装は明示スタックによる反復であり、再帰しない（ZDD の深さは変数の個数そのものなので、
再帰では大規模な族で `StackOverflowException` になり得るため）。

### 3.1 集合演算

| メソッド | 演算子 | 意味 |
|---|---|---|
| `F.Union(G)` | `F \| G` | 和 `F ∪ G` |
| `F.Intersect(G)` | `F & G` | 積 `F ∩ G` |
| `F.Difference(G)` | `F - G` | 差 `F ∖ G`（族としての差） |
| `F.SymmetricDifference(G)` | `F ^ G` | 対称差 `F △ G` |
| `F.Complement()` | `~F` | 補 `2^U ∖ F`（`U` は manager の全変数） |
| `F.IsSubsetOf(G)` | — | `F` のすべての集合が `G` にも属するか |
| `F.Overlaps(G)` | — | `F` と `G` に共通の集合があるか |

### 3.2 ZDD 固有の演算（Minato の基本演算）

| メソッド | 別名 | 意味 |
|---|---|---|
| `F.Product(G)` | `F * G` | `{ a ∪ b : a ∈ F, b ∈ G }`（直積結合／join） |
| `F.Quotient(G)` | `F / G` | `G` のどの集合とも重ならず、足しても `F` に入る集合 |
| `F.Remainder(G)` | `F % G` | `F ∖ (G * (F / G))`（`G` でくくり出せなかった残り） |
| `F.Meet(G)` | `F ⊓ G` | `{ a ∩ b : a ∈ F, b ∈ G }` |
| `F.SupersetsOf(G)` | `F.Restrict(G)` | `G` のいずれかを含む集合だけを残す |
| `F.SubsetsOf(G)` | `F.Permit(G)` | `G` のいずれかに含まれる集合だけを残す |
| `F.NonSubsetsOf(G)` | — | `G` のどれの部分集合でもない集合だけを残す |
| `F.NonSupersetsOf(G)` | — | `G` のどれの上位集合でもない集合だけを残す |
| `F.Change(item)` | — | 各集合の `item` の有無を反転する（対合） |
| `F.OnSet(item)` | `F.Subset1(item)` | `item` を含む集合だけ取り出し、`item` を除く |
| `F.OffSet(item)` | `F.Subset0(item)` | `item` を含まない集合だけを残す |
| `F.Flip(items...)` | — | `Change` の一般化（複数 item をまとめて反転） |
| `F.Maximal()` | — | 包含関係で極大な集合だけを残す（結果は反鎖） |
| `F.Minimal()` | — | 包含関係で極小な集合だけを残す（結果は反鎖） |
| `F.HittingSets()` | `F.Blocking()` | `F` のどの集合とも交わる集合をすべて集める |

`F == F / G * G + F % G`（`+` は `Union`）が恒等式として常に成り立つ。

```csharp
Zdd product = a * b;                     // {{0,1}, {0,2}, {1}, {1,2}}
Zdd quotient = product / b;
Zdd remainder = product % b;
Zdd reconstructed = quotient * b | remainder;
// reconstructed == product
```

`SupersetsOf`/`Restrict`、`SubsetsOf`/`Permit`、`OnSet`/`Subset1`、`OffSet`/`Subset0` は
それぞれ**同じ演算の別名**（前者が .NET 的な名前、後者が SAPPOROBDD／Minato の記法）。
どちらの名前で探しても見つかるように両方を用意してある。

### 3.3 問い合わせ

| メソッド | 意味 | 計算量 |
|---|---|---|
| `F.Contains(items)` | この集合が族に属するか | O(変数の個数)。族を作らず 1 本の経路を降りるだけ |
| `F.Count` | 族に属する集合の個数（`BigInteger`、厳密） | O(ノード数) |
| `F.CountApprox` | `Count` の `double` 近似（2^53 以下なら厳密一致） | O(ノード数)、`Count` より軽い |
| `F.CountBySize()` | 要素数ごとの個数分布 | O(ノード数 × 最大要素数) |
| `F.Support()` | 族が実際に使っている item の一覧 | 族の走査 |
| `F.NodeCount` | この族から到達できる非終端ノード数 | 族の走査 |

## 4. `Count` / 列挙 / `ElementAt` / `Sample` / `MaxWeight` の実例

### 4.1 `Count`: 展開せずに数える

```csharp
using ZddManager manager = new ZddManager(variableCount: 20);

// 20 要素の冪集合。厳密な濃度は 2^20 = 1,048,576。
Zdd powerSet = manager.Empty.Complement();

BigInteger exact = powerSet.Count;         // 1,048,576（厳密）
double approx = powerSet.CountApprox;      // 1048576.0（近似、この規模では厳密と一致）
```

`Count` はノード数に比例する手間で求まるので、集合の個数が `10^24` を超える族でも一瞬で返る。
桁数だけが要らない・速さ優先なら `CountApprox`（`double`）を使う。

### 4.2 `Sets()`: 遅延列挙

```csharp
// Sets() は遅延列挙。族が大きくても LINQ の Take や break で先頭だけ舐められる。
foreach (int[] set in powerSet.Sets())
{
    if (/* 十分見た */ false)
    {
        break;
    }
}
```

`Count` と違い、列挙の手間は**返す集合の個数**に比例する。族全体を舐めるには
「集合の個数 × 変数の個数」かかるので、大きな族を丸ごと `ToList()` するのは避け、
個数だけが要るなら `Count` を使う。

### 4.3 `ElementAt` / `IndexOf`: unranking / ranking

```csharp
using ZddManager manager = new ZddManager(variableCount: 40);

// 40 要素の冪集合。濃度は 2^40 ≈ 10^12 で、列挙して数えるのは非現実的。
Zdd powerSet = manager.Empty.Complement();

int[] first = powerSet.ElementAt(BigInteger.Zero);      // 0 番目（Default 順で空集合）
int[] last = powerSet.ElementAt(powerSet.Count - 1);    // 最後の 1 つ

BigInteger rank = powerSet.IndexOf(last);               // ElementAt の逆（unranking の逆）
```

`ElementAt(k)` は「濃度なみに大きい族から k 番目を取り出す」機能で、先頭から k 回舐める代わりに
「ノードごとの部分濃度を先に求め、根から 1 本の経路を降りるだけ」で答えを出す
（手間は `Count` と同程度 ＋ O(変数の個数)）。

### 4.4 `Sample`: 一様ランダム抽出

```csharp
Random random = new Random(Seed: 42);
int[] sample = powerSet.Sample(random); // 族に属するどの集合も等しい確率で選ばれる

// まとめて複数個ほしいときは、濃度の表を 1 回だけ作って使い回すこちらのほうが速い。
int[][] samples = powerSet.Sample(count: 10, random);
```

「10^24 個の解から一様に 1 つ」が ZDD の目玉機能である。内部は `ElementAt` に一様乱数を
食わせて実現しており、剰余を取る素朴な方法ではなく棄却法で偏りをなくしている。

### 4.5 `MaxWeight` / `MinWeight` / `TopK`: 全解を並べない最適化

```csharp
// pairs: {0,1,2,3} の冪集合のうち、要素数がちょうど 2 の集合だけを残した族
// （組み立て方は samples/Zdd.ApiGuide/Program.cs の BuildExactlyTwo を参照）。
int[] weights = { 3, 1, 4, 1 }; // item 0..3 の重み
WeightedSet<int> best = pairs.MaxWeight(weights);

Console.WriteLine(best.Weight); // 7  ({0, 2} の重み 3 + 4)
Console.WriteLine(string.Join(",", best.Items)); // 0,2
```

「重みが最大の集合」は ZDD 上の**最長路**そのもので、ノードを 1 度ずつ見るボトムアップ DP で
求まる。全解を並べる必要はなく、集合が `10^24` 個ある族でも手間はノード数に比例する。
組み込みの重み型は `int` / `long` / `double` / `BigInteger`（利用者定義の重み型は §5.2 を参照）。

## 5. 性能上の注意

### 5.1 `IDdEval<TValue>` / `IWeightOps<TWeight>` を interface 型で受けないこと

`Count` / `MaxWeight` などの内部実装は、いずれも「終端の値」と「ノードごとの合成」だけを
差し替え可能にした共通の枠組みの上に乗っている:

- ボトムアップの畳み込みは `IDdEval<TValue>`（[`ZddEvaluation.Evaluate<TEval, TValue>`](../src/ZDD.Net/Core/ZddEvaluation.cs)）
- 重み最適化の「0」「足す」「比べる」は `IWeightOps<TWeight>`

利用者が独自の評価器や重み型を書くときも、**必ず `struct` として実装**し、呼び出し側は
`where TEval : struct, IDdEval<TValue>` のように**型引数として**受け取ること。interface 型
（`IDdEval<TValue> eval` のような変数）で受けると、ノード 1 個ごとに起きる呼び出しが
仮想呼び出しになり、実測で数倍遅くなる。ZDD.Net の公開 API はこの制約を型シグネチャで
強制しており、`Evaluate` はジェネリック型引数でしか呼べない。

```csharp
// 族に属する集合の個数を数える評価器（CardinalityEval と同じもの）。struct であることが重要。
public readonly struct CountingEval : IDdEval<BigInteger>
{
    public BigInteger EvalTerminal(bool isTrue) => isTrue ? BigInteger.One : BigInteger.Zero;
    public BigInteger EvalNode(int item, BigInteger lo, BigInteger hi) => lo + hi;
}

BigInteger count = family.Evaluate<CountingEval, BigInteger>(default);
```

同様に `IWeightOps<TWeight>` の実装（`Int32WeightOps` / `Int64WeightOps` / `DoubleWeightOps` /
`BigIntegerWeightOps` や利用者定義の型）も `struct` にする。

### 5.2 `BigInteger` と `double` の使い分け

- `Count`（`BigInteger`）は**厳密**だが、`BigInteger` の加算は桁数に比例した時間とアロケーションを
  伴う。桁数だけが要らない・速さ優先なら `CountApprox`（`double`）を使う（濃度が `2^53` 以下なら
  厳密と一致し、それを超えると下位桁が丸められる。`double.MaxValue` を超えると
  `double.PositiveInfinity` になり、例外にはならない）。
- 重み最適化も同様: `Int32WeightOps` / `Int64WeightOps` は速いが桁溢れで `OverflowException` に
  なり得る（checked 加算）。溢れうる規模の重みには `BigIntegerWeightOps` を使う。
  `DoubleWeightOps` は速いが加算が結合的でないため、同点に近い重みでは求め方によって
  最後の 1 bit が変わり得る。厳密な比較が要るなら整数系か `BigInteger` を使う。
- `Probability` / `ExpectedValue` / `ItemFrequency` は最終的な答えが `double` でも、
  内部の個数の勘定は `BigInteger` で行う（`10^24` 個規模の族でも下位桁が失われないようにするため）。

### 5.3 ServerGC / TieredPGO の推奨

ZDD.Net は大量の小さな配列（ノード表・一意化表・演算キャッシュ）を長時間保持し、演算のたびに
それらを読み書きする。実行時設定として、アプリの `.csproj`（または `runtimeconfig.json`）に
以下を推奨する:

```xml
<PropertyGroup>
  <ServerGarbageCollection>true</ServerGarbageCollection>
  <TieredPGO>true</TieredPGO>
</PropertyGroup>
```

- **ServerGC**: ワークステーション GC よりもスループット重視のポリシーで、大きなノード表の
  GC 一時停止時間を抑えやすい。特にサーバ用途・バッチ処理での利用を想定する場合に効果が出やすい。
- **TieredPGO**（.NET 8 以降既定で有効）: プロファイル駆動の再 JIT により、`IDdEval` /
  `IWeightOps` の実装呼び出し（§5.1 のとおり型引数で受けている前提）がより積極的にインライン化・
  最適化される。

ベンチマークはこの設定で測定する方針（`docs/PLAN.md` §10-7）。

## 6. さらに詳しく

- フロンティア法フレームワーク・グラフ問題 API の使い方: [docs/frontier-guide.md](frontier-guide.md)
- スペックの規約の詳しい説明: [docs/frontier-spec-guide.md](frontier-spec-guide.md)
- 仕様・アーキテクチャの全体像: [docs/PLAN.md](PLAN.md)
- マイルストーン別の実装計画: [docs/ROADMAP.md](ROADMAP.md)
- 未確定事項: [docs/OPEN-QUESTIONS.md](OPEN-QUESTIONS.md)
- 実行できるサンプル: [`samples/Zdd.Cli`](../samples/Zdd.Cli)（CLI）、
  [`samples/Zdd.ApiGuide`](../samples/Zdd.ApiGuide)（このガイドのコード片）
