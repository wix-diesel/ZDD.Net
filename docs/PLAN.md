# ZDD.Net 開発計画書

C# ネイティブ実装による ZDD（Zero-suppressed Decision Diagram）＋フロンティア法ライブラリの
機能・仕様・実装計画。

- ドキュメント版数: v1 (2026-08-29)
- 対象リポジトリ: `wix-diesel/ZDD.Net`（Apache-2.0）

---

## 0. エグゼクティブサマリ（結論だけ先に）

| 項目 | 結論 |
|---|---|
| ターゲット | **`net10.0` 単独** |
| ネイティブ度 | 100% managed C#（P/Invoke なし・NativeAOT 互換）。**外部 NuGet 依存ゼロ**（必要な API は全てフレームワーク同梱） |
| 主用途 | **経路列挙・数え上げ**（s–t パス／サイクル）＋ **汎用の組合せ数え上げ**（基数制約・線形制約・独立集合） |
| 想定規模 | **数千辺**。状態の bit-packing と辺順序最適化を v0.3 までに前倒しする |
| アーキテクチャ | 3 レイヤ: **Core（ZDD エンジン）/ Frontier（フロンティア法フレームワーク）/ Graphs（グラフ問題 API）** |
| 主参考 OSS | TdZdd (MIT)・Graphillion (MIT)・SAPPOROBDD (MIT)・CUDD/EXTRA・Knuth *TAOCP* 4A 7.1.4 |
| 差別化 | **.NET には ZDD／フロンティア法のネイティブ実装が事実上存在しない**（CUDD の P/Invoke ラッパしか選択肢がない）。ここが本ライブラリの価値。 |
| 最初の到達目標 | 11×11 格子の自己回避パス数 `1568758030464750013214100` を数十秒級で算出できること |

---

## 1. 既存 OSS 調査と参考にする点

| OSS | 言語 / ライセンス | 何を参考にするか | 注意点 |
|---|---|---|---|
| **TdZdd** (kunisura/TdZdd, ERATO MINATO Project) | C++ ヘッダオンリー / MIT | **設計の主軸**。`DdSpec` によるフロンティア法の抽象化、レベル単位の幅優先構築 → 削減、`DdEval` によるボトムアップ評価、`zddSubset` によるスペック合成 | テンプレートメタプロ前提の設計。C# では struct ジェネリクス + インタフェース制約に読み替える |
| **Graphillion** (graphillion/graphillion) | Python + C++ / MIT | **高レベル API の主軸**。`GraphSet` / `SetSet` の集合ライクなインタフェース、`paths()` `trees()` `matchings()` などの語彙、`rand_iter` / `max_iter` / `probability`、辺順序ヒューリスティクス | 内部は SAPPOROBDD 依存。API 語彙のみ参考にして実装は独自 |
| **SAPPOROBDD** (Shin-ichi Minato) | C / MIT | ZDD の**家族代数演算の定義そのもの**（`Change` / `OnSet` / `OffSet` / `Product` / `Quotient` / `Remainder` / `Meet` / `Permit` 等）、一意化表と演算キャッシュの古典的構成 | 32bit ノード ID 前提の実装詳細は踏襲しない |
| **CUDD + EXTRA** | C / BSD 系 | 演算キャッシュ（lossy direct-mapped cache）、動的リサイズ、GC（mark & sweep）の実装知見 | 動的変数順序変更（sifting）は v1.0 スコープ外 |
| **Knuth, TAOCP Vol.4A 7.1.4 / BDD14 プログラム群** | 教育用 C / 教育目的公開 | SIMPATH（単純パス列挙）のアルゴリズム、mate 配列の定義、ZDD 上のカウント・ランダム抽出・最適化アルゴリズム | 実装をコピーせず、記述されたアルゴリズムを再実装 |
| **Kawahara–Saitoh–Yoshinaka ほかの論文群** | — | フロンティア法の一般化、グラフ分割・連結成分制約・次数制約の状態設計 | — |
| **JDD / Sylvan / OxiDD** | Java / C / Rust | 並列 DD 構築、ノード表のロックフリー化の参考 | 並列化は v0.4 以降 |

### ライセンス方針

本リポジトリは **Apache-2.0**。MIT の OSS からコードを直接移植することは法的に可能だが、
**「アルゴリズムは論文・ドキュメントから再実装、コードはコピーしない」** を原則とする。理由:

1. Apache-2.0 と MIT の混在によるヘッダ管理コストを避ける
2. C++ テンプレート／C マクロ前提の実装をそのまま持ってきても .NET では速くならない
3. 「ネイティブ実装」という当初の目的に合致する

参考にした事実（アルゴリズムの出典）は `THIRD-PARTY-NOTICES.md` と各ソースの XML doc コメントに
論文・リポジトリへの参照として記載する。

---

## 2. ターゲットフレームワークの決定

### 結論: `<TargetFramework>net10.0</TargetFramework>`（単一ターゲット）

当初は `netstandard2.0` を主ターゲットとして検討したが、**net10.0 単独**に決定した。

**得られるもの**

- **polyfill が全て不要**（`BitOps` / `HashCode` / `SimpleArrayPool` / nullable 属性）
- **`#if NET` 分岐がゼロ** — コードが 1 本になり、テストプロジェクトも 1 本で済む
- 素で使える API: `Span<T>` / `ReadOnlySpan<T>`、`ArrayPool<T>`、`System.HashCode`、
  `BitOperations`、`System.Runtime.Intrinsics`（SIMD）、`CollectionsMarshal`、
  `GC.AllocateUninitializedArray`（POH）、`ref` フィールド、`[InlineArray]`、
  ジェネリック数学（`INumber<T>` / `static abstract` インタフェースメンバ）、
  コレクション式、`params ReadOnlySpan<T>`
- NativeAOT / トリミングを最初からフルサポートできる

**失うもの（承知の上）**

- .NET Framework 4.x / Unity（Mono・IL2CPP）/ Xamarin が対象外
- .NET 8 / 9 の利用者も対象外（net10 へのアップグレードが必要）

後から `netstandard2.0` を足すことは可能だが、その時点で polyfill 一式と `#if` 分岐が必要になる。
そのため **内部実装は「素の配列 + `int` インデックス」を基本にしておく**方針は維持する
（移植性のためだけでなく、そもそも性能上その方が良い）。

### 設計への具体的な影響

| 項目 | net10 単独での決定 |
|---|---|
| `IArrayDdSpec` | **`Span<int>` を使う**（ns2.0 の制約が消えたので「配列 + オフセット」にしない） |
| 重み型 `IWeightOps<T>` | **`static abstract` インタフェースメンバ**で定義（ダミーインスタンスが不要になる） |
| 配列プール | `ArrayPool<int>.Shared` を直接利用 |
| ハッシュ | 一意化表は独自ハッシュのまま（`HashCode` は汎用すぎて hot path には重い）。`BitOperations` は素で使う |
| フロンティア状態 | **`[InlineArray]`** による固定長インライン状態を検討（M3-2） |
| SIMD | 状態比較・ハッシュに `System.Runtime.Intrinsics` を検討（M4-2） |

### 外部依存: **ゼロを維持**

`PackageReference` を 1 つも持たない。ただし net10 単独になったことで、これは**制約ではなくなった**
（`Span` も `ArrayPool` も `BitOperations` もフレームワーク同梱）。
方針としては維持し、テストで機械的に検証する。

### 共通ビルド設定

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <LangVersion>latest</LangVersion>
  <Nullable>enable</Nullable>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  <IsAotCompatible>true</IsAotCompatible>
  <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
</PropertyGroup>
```

### 開発環境について（実測）

現 remote 環境の egress ポリシーでは:

| ホスト | 結果 | 影響 |
|---|---|---|
| `builds.dotnet.microsoft.com` | **403（ポリシー拒否）** | `dotnet-install.sh` による SDK 取得が不可 |
| `download.visualstudio.microsoft.com` / `aka.ms` / `dotnetcli.blob.core.windows.net` | 到達不可 | 同上 |
| `packages.microsoft.com` | 200（ただし noble prod に SDK パッケージなし） | この経路では取得できない |
| **Ubuntu noble リポジトリ** | **OK** | **`apt-get install -y dotnet-sdk-10.0` で .NET 10 SDK（10.0.111）を導入できる** |
| `api.nuget.org` / `www.nuget.org` | OK | NuGet 復元は問題なく動作 |

→ `dotnet-sdk-10.0` に加えて `dotnet-sdk-aot-10.0`（NativeAOT コンポーネント）も導入でき、
**M6-2 の NativeAOT 検証もこの環境で実施できる**。
`scripts/setup-dev-env.sh` と SessionStart フックで自動化する。

## 3. リポジトリ構成

```
ZDD.Net.sln
Directory.Build.props / Directory.Packages.props   … 共通設定・中央パッケージ管理
.editorconfig
src/
  ZDD.Net/                    … NuGet パッケージ本体（Core + Frontier + Graphs を同梱）
    Core/                     … ノード表・一意化表・演算キャッシュ・家族代数
    Frontier/                 … フロンティア法フレームワーク（IDdSpec / 構築器 / 評価器）
    Specs/                    … 組み込みスペック（パス・木・マッチング・基数制約 …）
    Graphs/                   … Graph / GraphSet 高レベル API・辺順序最適化
    Io/                       … シリアライズ・DOT 出力・Graphillion 互換 I/O
    Internal/                 … ハッシュ・ビット操作・共通ユーティリティ
tests/
  ZDD.Net.Tests/              … xUnit（単体・総当たり照合・既知値）
  ZDD.Net.Tests.Properties/   … CsCheck によるプロパティテスト
bench/
  ZDD.Net.Benchmarks/         … BenchmarkDotNet
samples/
  Zdd.Cli/                    … 動作確認用 CLI（grid path / spanning tree など）
docs/
  PLAN.md（本書）/ algorithms.md / api-guide.md / benchmarks.md
```

**NuGet パッケージは 1 つに集約**（`ZDD.Net`）。Graphs レイヤは Core の薄い上物なのでサイズ増は小さく、
「参照が 3 つ必要」は導入障壁になる。可視化（DOT→SVG レンダリング等）を将来足すなら別パッケージ。

---

## 4. Core レイヤ: ZDD エンジン

### 4.1 データ構造

```csharp
// 16 バイト固定のノード。AoS（配列内連続）で持つ。
internal struct ZddNode
{
    public int Level;   // 1 = 最下位（葉側）… N = 最上位（根側）。TdZdd と同じ向き
    public int Lo;      // 0-枝（要素を含まない側）
    public int Hi;      // 1-枝（要素を含む側）。ZDD 削減規則より Hi != 0
    public int Next;    // 一意化表のチェーン（オープンアドレス法にするなら未使用）
}
```

- ノード ID は `int`。ID `0` = 終端 ⊥（空集合族 ∅）、ID `1` = 終端 ⊤（{∅}）。
- 上限 2^31 ノード ≒ 32 GB。実用上十分なので **64bit ID 版は作らない**（複雑さに見合わない）。
- ノードは 1 本の `ZddNode[]` に連続確保し、倍々でリサイズ。レベルごとの分割はしない
  （フロンティア法側は別の一時テーブルを使うため、Core は単一表で良い）。

### 4.2 一意化表（Unique Table）

- **オープンアドレス法（線形探索、2 の冪サイズ、負荷率 0.7 で倍化）**。
  `Dictionary<K,V>` は使わない（キーが 3 整数、ボクシング・比較器呼び出しを避けるため）。
- ハッシュ: `(level, lo, hi)` を 64bit に混ぜて Fibonacci hashing でスロット決定。
- レベルごとの表に分けるか、全体で 1 表にするかは**全体 1 表**を採る
  （動的変数順序変更をやらないため、レベル分割の利点が小さい）。

### 4.3 演算キャッシュ

- CUDD 流の **direct-mapped lossy cache**（衝突は上書き）。固定サイズ配列、`ArrayPool` は使わない。
- エントリ: `struct { long Key; int Op; int Result; }`（16 バイト）。
- 既定サイズはノード数に連動して自動調整（ノード数の 1/4 程度、上限は設定可能）。
- 二項演算（Union/Intersect/Diff/Product/…）と単項演算（Change/OnSet/…）でキーの作り方を分ける。

### 4.4 メモリ管理

- **参照カウントは採らない**（ユーザ API が重くなる）。
- 明示 GC: `ZddManager.Collect(params Zdd[] roots)` で mark & sweep + コンパクション + ID 再割当。
  再割当により既存の `Zdd` ハンドルが無効化されるので、`ZddManager.RootSet` に登録した
  ハンドルのみを生き残らせて再マップする方式にする。
- 実際には「1 回の問題を解いてマネージャごと捨てる」使い方が大半なので、GC は v0.4 以降の課題。

### 4.5 再帰の扱い（.NET 固有の重要事項）

家族代数演算は自然には再帰実装になるが、**深い ZDD（変数数 10 万規模）で `StackOverflowException`
を起こす**。.NET は C++ と違い、スタックオーバーフローを catch できずプロセスが即死するため致命的。

対策:
1. 全ての二項演算を**明示スタック（`int` の配列）を用いた反復実装**にする。
2. あるいは「深さがしきい値未満なら再帰、超えたら反復に切替」のハイブリッド。
3. 既定は反復実装。ベンチで再帰版が有意に速い場合のみ、深さ上限つきで再帰を使う。

これは C++ 実装の移植では見落とされがちな点で、**設計初期から反復で書く**方針を採る。

### 4.6 公開型

```csharp
public sealed class ZddManager : IDisposable
{
    public ZddManager(int variableCount, ZddManagerOptions? options = null);
    public int VariableCount { get; }
    public long NodeCount { get; }
    public Zdd Empty { get; }        // ∅
    public Zdd Base { get; }         // {∅}
    public Zdd Singleton(int item);  // {{item}}
    public Zdd PowerSet(...);        // 2^S
    public Zdd FromSets(IEnumerable<IEnumerable<int>> sets);
    public void Collect();
    public ZddStatistics GetStatistics();
}

// 値型ハンドル。マネージャ参照 + ノード ID
public readonly struct Zdd : IEquatable<Zdd>, IEnumerable<int[]>
{
    public ZddManager Manager { get; }
    public bool IsEmpty { get; }
    public bool IsBase { get; }
    public long NodeCount { get; }          // このZDDが参照するノード数
    public BigInteger Count { get; }        // 要素（部分集合）の個数
    public double CountApprox { get; }      // double 近似（高速）
    public IEnumerable<int[]> Sets(ZddEnumerationOrder order = default);  // 遅延列挙
    public bool Contains(IEnumerable<int> set);
    public bool IsSubsetOf(Zdd g);
    public bool Overlaps(Zdd g);
    public int[] ElementAt(BigInteger index, ZddEnumerationOrder order = default);   // unranking
    public BigInteger IndexOf(IEnumerable<int> set, ZddEnumerationOrder order = default);  // ranking（無ければ -1）
    public int[] Sample(Random rng);        // 一様ランダムに 1 個
    public int[][] Sample(int n, Random rng);  // 一様ランダムに n 個（復元抽出）
    public WeightedSet<T> MaxWeight<T, TOps>(params ReadOnlySpan<T> w) where TOps : struct, IWeightOps<T>;
    public WeightedSet<T> MinWeight<T, TOps>(params ReadOnlySpan<T> w) where TOps : struct, IWeightOps<T>;
    public WeightedSet<T>[] TopK<T, TOps>(ReadOnlySpan<T> w, int k) where TOps : struct, IWeightOps<T>;
    // int / long / double は型引数を書かない短い形でも呼べる（MaxWeight(w) など）
    public double Probability(params ReadOnlySpan<double> p);   // 各 item が独立に p で選ばれるとき
    public double ExpectedValue(params ReadOnlySpan<double> w); // 族の上の一様分布での期待値
    public double[] ItemFrequency();                            // 同上、item ごとの出現確率
    // 演算子: | & - ^ * / % ~
}
```

`Zdd` を `struct` にすることでアロケーションを避ける。マネージャ参照を持つので 12〜16 バイト。
異なるマネージャ間の演算は例外にする。

---

## 5. 家族代数 API（機能一覧）

### 5.1 集合演算

| API | 記号 | 意味 |
|---|---|---|
| `Union(g)` | `f \| g` | F ∪ G |
| `Intersect(g)` | `f & g` | F ∩ G |
| `Difference(g)` | `f - g` | F \ G |
| `SymmetricDifference(g)` | `f ^ g` | 対称差 |
| `Complement()` | `~f` | 2^U \ F（全体集合 U に対して） |

### 5.2 ZDD 固有（Minato の基本演算）

| API | 意味 |
|---|---|
| `OnSet(item)` / `Subset1` | item を含む集合を取り出し、item を除去 |
| `OffSet(item)` / `Subset0` | item を含まない集合を取り出す |
| `Change(item)` | 各集合の item の有無を反転 |
| `Product(g)` / `f * g` | 直積結合（join）: `{ a ∪ b : a∈F, b∈G }` |
| `Quotient(g)` / `f / g` | 商 |
| `Remainder(g)` / `f % g` | 剰余: `f - g*(f/g)` |
| `Meet(g)` | `{ a ∩ b : a∈F, b∈G }` |
| `Restrict(g)` / `SupersetsOf(g)` | G のいずれかを含む F の要素 |
| `Permit(g)` / `SubsetsOf(g)` | G のいずれかに含まれる F の要素 |
| `NonSubsetsOf(g)` / `NonSupersetsOf(g)` | 上記の否定版 |
| `Maximal()` / `Minimal()` | 包含関係で極大／極小な要素のみ |
| `HittingSets()` / `Blocking()` | ブロッキング集合族（横断超グラフ） |
| `Flip(items)` | 複数要素の一括反転 |
| `Support()` | 実際に使われている変数の集合 |

**割り算の境界**: `f / g` は `{ a : ∀ b ∈ g, a ∩ b = ∅ かつ a ∪ b ∈ f }`。
`g` が空の族 ∅ のとき条件は空虚に真になるので、定義に従い **`f / ∅` は全体集合の冪集合 2^U**
（`Complement()` の全体集合と同じく、マネージャの全変数を取る。B8 と揃えてある）。
エラーにする実装もあるが、`f % ∅ == f` と合わせれば `f == f/g*g + f%g` はそのまま成り立つ
（`2^U * ∅ == ∅` のため）。

**包含系の別名**: `Restrict` / `Permit` は SAPPOROBDD 由来の名前、`SupersetsOf` / `SubsetsOf` は
「何が残るか」をそのまま言い表した .NET 的な名前で、**同じ実装を指す**。
どちらの語彙で探しても見つかるように両方を公開する。
否定版（`NonSubsetsOf` / `NonSupersetsOf`）は差を取らずに 1 回の走査で求めるが、
`f.NonSupersetsOf(g) == f - f.Restrict(g)`、`f.NonSubsetsOf(g) == f - f.Permit(g)` が成り立つ。

**ふるいの境界**: 右オペランドが ∅ のとき「∃ b」は偽、「∀ b」は空虚に真になるので、
`f.Restrict(∅) == f.Permit(∅) == ∅` かつ `f.NonSubsetsOf(∅) == f.NonSupersetsOf(∅) == f`。
`{∅}` は `Restrict` の単位元（∅ はどの集合にも含まれる）で、
`f.Permit({∅})` は `f` が空集合を持つときだけ `{∅}` になる。

**ヒッティング集合は極小化しない**: `HittingSets()` が返すのは
`{ a ⊆ U : ∀ b ∈ F, a ∩ b ≠ ∅ }`、すなわち**交わる集合すべて**（上に閉じた族）である。
極小なものだけが要るときは `HittingSets().Minimal()` と書く。したがって Berge の双対定理は
`F.Minimal().HittingSets().Minimal().HittingSets().Minimal() == F.Minimal()` の形になる。
結果は元の族に対して指数的に大きくなりうる。

**極大・極小とヒッティング集合の境界**: `∅.Maximal() == ∅.Minimal() == ∅`、
`{∅}.Minimal() == {∅}` で、`F` が空集合を持てば `F.Minimal() == {∅}`。
`∅.HittingSets() == 2^U`（条件が空虚に真）、空集合を持つ族の `HittingSets()` は ∅
（∅ と交われる集合は無い）。`HittingSets()` と `Complement()` の全体集合 `U` はどちらも
**マネージャの全変数**であって `Support()` ではない（B8）。族が一度も使っていない item も
候補に自由に入れてよいためで、同じ内容の族でも変数の個数が違えば答えが変わる。

### 5.3 問い合わせ・列挙

| API | 備考 |
|---|---|
| `Count` (`BigInteger`) / `CountApprox` (`double`) | ボトムアップ DP、キャッシュ付き |
| `CountBySize()` | 要素数別のカウント分布（`BigInteger[]`） |
| `Contains(IEnumerable<int> set)` | メンバシップ判定 O(変数数) |
| `IsSubsetOf(g)` / `Overlaps(g)` | |
| `GetEnumerator()` → `IEnumerable<int[]>` | 遅延列挙（明示スタックで DFS、辞書順オプション） |
| `ElementAt(BigInteger index)` | **unranking**。k 番目の集合を O(変数数) で直接取得 |
| `IndexOf(int[] set)` | ranking（上の逆） |
| `Sample(Random rng)` / `Sample(n)` | **一様ランダム抽出**（濃度 DP を利用） |
| `MaxWeight(w)` / `MinWeight(w)` | 重み最大／最小の集合（DAG 上の最長・最短路 DP） |
| `TopK(w, k)` | 上位 k 個列挙 |
| `Probability(p)` | 各要素の独立確率 p から族の確率を計算（ネットワーク信頼性） |
| `ExpectedValue(w)` | 期待値 |
| `ItemFrequency()` | 各変数が集合に含まれる確率／頻度 |

`ElementAt` と `Sample` は「10^20 個の解から一様サンプリング」という ZDD の目玉機能なので
**v0.1 から入れる**。

**列挙は遅延で、返す配列は毎回新しい**。数え上げがノード数に比例するのに対し、列挙は
**返す集合の個数**に比例するので、`Take(10)` や途中の `break` が族の大きさに関係なく即座に
終わることが要る。経路そのものは 1 本の作業配列で持ち回るが、終端 ⊤ に着くたびに写した
`int[]` を返す（使い回すと `ToList()` した全要素が同じ配列になるという静かな罠が生まれる）。
バッファを使い回す高速版が要るなら `EnumerateInto(Span<int>)` のような別 API として足す。

**順序は 2 つ**（`ZddEnumerationOrder`）。既定は 0-枝優先の深さ優先で、これは
**指示ベクトルの辞書順**になる（item 0 が根側にあるため。B5）。`Lexicographic` は集合を
**昇順の item 列**と見たときの辞書順で、空列が最小、以降は先頭要素の小さい順。
`{0,2}` と `{1}` の前後が入れ替わるので、2 つは別の全順序である。どちらも 1 回の深さ優先走査で出せる。

**`IsSubsetOf` / `Overlaps` は族を作らない**。`(F - G).IsEmpty` / `(F & G) != Empty` と同じ答だが、
差や交わりの ZDD を組み立てない。どちらも分解すると合成が 1 種類しか出てこない
（`Overlaps` は ∨ だけの木、`IsSubsetOf` は ∧ だけの木）ので、答は到達できる終端条件の
∨／∧ そのものであり、決着する値が 1 つ出た時点で打ち切ってよい。

**順位づけは「ノードごとの部分濃度」の上に乗る**。`Count` が根の値 1 つを返すのに対し、
`ElementAt` は経路上のノードごとに「0-枝の先に集合がいくつあるか」を問うので、
ノード ID → `BigInteger` の**表**が要る（`CardinalityTable`）。表さえあれば、`k` 番目の集合は
根から 1 本の経路を降りるだけで出る: `k < |lo|` なら 0-枝、そうでなければ `k -= |lo|` して 1-枝。
手間は「濃度の走査（`Count` と同じ）＋ O(変数数)」で、10^20 番目でも変わらない。
表は**呼び出しごとに作って捨てる**（マネージャに覚えさせると、ノード ID の意味が変わる操作の
たびに捨てる約束が増える。M5-3）。まとめて引く `Sample(n, rng)` は表を 1 本だけ作って n 回引く。

**`ElementAt` の順序は列挙と一致する**。同じ `ZddEnumerationOrder` を渡す限り
`ElementAt(k) == Sets(order).ElementAt(k)` で、一致しないと利用者は順位づけを信用できない。
`Lexicographic` では空集合が最小なので、ノードごとに「0-枝の連なりの先が ⊤ か」も要る。
これも濃度と同じ 1 回の走査で `hasEmptySet(n) = hasEmptySet(n.Lo)` として求めておく
（毎回 0-枝を辿り直すと O(変数数^2) になる）。`IndexOf` はその逆写像で、族に属さない集合には
順位が無いので **-1 を返す**（`IList.IndexOf` と同じ流儀。範囲外の item は渡し間違いなので例外）。

**一様サンプリングで剰余は使わない**。`Sample` は `ElementAt` に `[0, Count)` の一様乱数を
食わせるだけだが、`rng` の返すビットの剰余を取ると必ず偏る（範囲が 2 の冪でない限り）。
`Count - 1` を表せるビット数ぶんだけ引いて、範囲外なら捨てて引き直す**棄却法**を使う。
1 回で当たる確率は必ず 1/2 より大きいので、桁数がいくら大きくても引き直しの期待回数は 2 未満。

**重み最適化は DAG の最長路 DP**。ZDD は閉路を持たないので、`MaxWeight` は
「⊤ は `Zero`、⊥ は候補にならない、ノードでは `max(lo, hi + w[i])`」というボトムアップ DP
そのものになる。どちらの枝を選んだかを覚えておけば、根から 1 本降りるだけで**最適集合**が
復元できるので、最適値と最適集合は 1 回の呼び出しで一緒に返す（`WeightedSet<T>`）。
重みが負でも成り立つ（閉路が無いため）。同点のときは 0-枝側を採ると決めてあり、
これは既定の列挙順で最初に来る集合に一致する。`TopK` は「良い方を 1 つ」を「良い方から k 個」に
広げたもので、ノードごとに整列済みの上位 k 個を併合する。**費用は k に比例する**
（時間 O(m·k)、メモリ O(m·k)）ので、doc に明記して小さい k での利用を勧める。
同じ重みの集合が複数あるときに何番目に来るかは規定せず、規定するのは重みの並びだけとする。

**重み型は `IWeightOps<T>` の `static abstract` メンバで抽象化する**（B10・§2）。DP に要るのは
`Zero` / `Add` / `Compare` の 3 つだけなので、この 3 つを型で渡せば `int` / `long` / `double` /
`BigInteger` の同梱実装のほか、有理数や辞書順タプルのような利用者定義の重みもそのまま乗る。
`IDdEval` と同じく **interface 型では受け取らない**（`where TOps : struct, IWeightOps<T>`）。
ノードごとに走る `Add` / `Compare` が仮想呼び出しになると数倍遅くなるためで、
この約束はテストで機械的に確かめる。

**`Probability` の宇宙はマネージャの全変数**（`Support()` ではない。B8）。すなわち
`Σ_{A∈F} Π_{i∈A} p[i] · Π_{i∉A} (1-p[i])` で、ZDD が飛ばした段（その部分族が使っていない変数）は
「必ず選ばれていない」ことを意味するので、子へ降りるたびに `1-p[j]` を補う。これを補わないと
確率にならない別の量になる（各排他事象の和が 1 にならない）。定義どおりなので、
すべての `p[i]` を 1 にした答は「全体集合 U が族に属するか」であって、族が空でないことでは 1 にならない。
一方 `ExpectedValue` / `ItemFrequency` の分布は**族の上の一様分布**（`Sample` と同じ）で、
`Probability` とは別物である。頻度は「item i を含む集合の個数 ÷ 濃度」で、前者は
「根からの経路数（トップダウン）× 1-枝の先の濃度（ボトムアップ）」の和として 1 回ずつの走査で出る。
個数は `BigInteger` で厳密に数え、`double` にするのは最後の割り算だけにする
（10^308 を超える濃度で `inf / inf` にしないため）。

---

## 6. Frontier レイヤ: フロンティア法フレームワーク

TdZdd の `DdSpec` を C# に読み替えた汎用フレームワーク。ユーザが「状態遷移」を書けば
ZDD が自動構築される、というのが本ライブラリの中核価値。

### 6.1 スペックのインタフェース

```csharp
/// <summary>フロンティア法の DD 仕様。TState は struct を強く推奨（devirtualize される）。</summary>
public interface IDdSpec<TState>
{
    /// <summary>根の状態を初期化し、その水準を返す。0=⊥, -1=⊤。</summary>
    int GetRoot(ref TState state);

    /// <summary>level の枝 value(0/1) をたどった子の状態と水準を返す。0=⊥, -1=⊤。</summary>
    int GetChild(ref TState state, int level, int value);

    bool StateEquals(in TState a, in TState b);
    int  StateHashCode(in TState state);
}

// 可変長・配列状態用（TdZdd の PodArrayDdSpec 相当）
public interface IArrayDdSpec
{
    int ArrayLength { get; }                                  // 状態配列の要素数
    int GetRoot(Span<int> state);
    int GetChild(Span<int> state, int level, int value);
}

// スカラ + 配列の複合（HybridDdSpec 相当）
public interface IHybridDdSpec<TScalar> { ... }
```

戻り値の規約（`0` = ⊥、`-1` = ⊤、正数 = 次の水準）は **TdZdd と互換**にする。
C++ の既存スペックを移植する人がそのまま書けるメリットが大きい。
可読性のため `DdResult.False` / `DdResult.True` 定数も用意する。

### 6.2 構築器

```csharp
public static class FrontierBuilder
{
    public static Zdd Build<TSpec, TState>(ZddManager manager, TSpec spec, BuildOptions? options = null)
        where TSpec : IDdSpec<TState>;                        // struct 制約でインライン化
}
```

アルゴリズム（TdZdd の 2 パス方式を踏襲）:

1. **幅優先展開（トップダウン）**: レベル N → 1 の順に、各レベルの状態集合を
   「状態 → 一時ノード ID」のハッシュ表で管理しつつ `GetChild` を適用。
   - 状態表はレベルごとに作って捨てる（ピークメモリ = 最大 2 レベル分）。
   - 状態が固定長 struct なら、状態の配列 + オープンアドレス表でノーアロケーション。
2. **削減（ボトムアップ）**: レベル 1 → N の順に ZDD 削減規則を適用
   - 規則 A: `Hi == ⊥` のノードは `Lo` に置換（ゼロサプレス規則）
   - 規則 B: `(Level, Lo, Hi)` が同一のノードを共有
   - 同時に Core の一意化表へ登録し、返り値を `Zdd` ハンドルにする。
3. （任意）**先読み枝刈り**: `GetChild` が ⊥ を返す枝が確定する状態を早期に潰す。

### 6.3 スペックの合成

```csharp
spec1.And(spec2)          // 交差スペック（同時に両制約を満たす）
spec1.Or(spec2)
zdd.Subset(spec)          // 既存 ZDD をスペックで絞り込み（TdZdd の zddSubset 相当）
```

「s-t パス かつ 辺数 10 以下 かつ 特定の辺を通る」のような合成を、
巨大な中間 ZDD を作らずに直接構築できる。これが Graphillion に対する明確な優位点になる。

### 6.4 ボトムアップ評価器

```csharp
public interface IDdEval<TValue>
{
    TValue EvalTerminal(bool isTrue);
    TValue EvalNode(int item, TValue lo, TValue hi);
}
public static TValue Evaluate<TEval, TValue>(this in Zdd zdd, TEval eval)
    where TEval : struct, IDdEval<TValue>;
```

`Count` / `Probability` / `MaxWeight` などは全てこの上に実装し、
ユーザ定義の評価（期待値、多項式、モーメント等）も同じ枠組みで書けるようにする。

**`EvalNode` が受け取るのは item**（内部のレベルではない）。評価器が変数ごとの情報を引くのは
`w[item]` のような形なので、公開する側は 0 始まりの item index で統一する（B5）。
レベル ↔ item の変換は `ZddManager` の 1 箇所だけが行う、という約束もそのまま守れる。

**`TEval` は `struct` 制約で受ける**（§10-2）。`IDdEval` を interface 型で受けると
ノード 1 個ごとの `EvalNode` が仮想呼び出しになる。制約を `where TEval : struct, IDdEval<TValue>`
にしておけば、interface 型で受ける書き方は**そもそもコンパイルが通らない**。
`TValue` は型引数から推論できないので、呼び出しでは 2 つとも明示する。

**走査とメモ化**: 明示スタックによるポストオーダー（再帰しない。§4.5）＋ノード ID ごとのメモ化で、
`EvalNode` の呼び出しは**到達できるノード 1 個につき 1 回**。10^24 個の集合を持つ族でも
ノード数ぶんの呼び出しで済む。メモ化には演算と同じ `OperationWorkspace` の途中結果表を使い、
そこには評価値そのものではなく値表の添字を入れる（表の値は結果ノード ID 用の `int` 固定のため）。
演算キャッシュは `int` しか覚えられないので、メモ化は評価 1 回のうちに閉じる。

**濃度の 3 つの入口**: `Count`（`BigInteger`、厳密）/ `CountApprox`（`double`、速いが
2^53 を超えると丸め、`double.MaxValue` を超えると `+∞` に飽和する）/
`CountBySize()`（要素数別の分布）。分布の配列の長さは**族に属する最大の集合の要素数 + 1**で、
空の族では長さ 0 になる。マネージャの変数の個数に合わせないのは、変数 10 万のマネージャで
小さな族を数えるときにノードごとに 10 万要素の配列を作らないため。

---

## 7. 組み込みスペック

### 7.1 グラフ用の共通基盤

```csharp
public sealed class FrontierManager
{
    // 辺順序に沿って、各辺 i で「新たに登場する頂点」「以降現れなくなる頂点」を前計算
    public IReadOnlyList<int> IntroducedVertices(int edgeIndex);
    public IReadOnlyList<int> ForgottenVertices(int edgeIndex);
    public int MaxFrontierSize { get; }         // 状態サイズ = ここが計算量の肩
    public int MateIndex(int edgeIndex, int vertex);   // 状態配列内のスロット
}
```

状態は `mate` 配列（パス・サイクル用）または `comp` 配列（連結性用）で表現し、
フロンティアから出る頂点のスロットを再利用してサイズを最小化する。

### 7.2 実装するスペック一覧

**辺の族（GraphSet 系）**

| スペック | 内容 |
|---|---|
| `PathSpec(s, t)` | s–t 単純パス（`SIMPATH`）。`allowAnyEndpoints` で全単純パス |
| `HamiltonianPathSpec` / `HamiltonianCycleSpec` | 全頂点を通る |
| `CycleSpec` | 単純サイクル（単一／複数） |
| `SpanningTreeSpec` / `ForestSpec` | 全域木・全域森・k 成分森 |
| `ConnectedSubgraphSpec(terminals)` | 指定頂点群を連結にする部分グラフ（シュタイナー木の基礎） |
| `SteinerTreeSpec` | 端子集合を連結する木 |
| `MatchingSpec(perfect:)` | マッチング・完全マッチング |
| `DegreeConstraintSpec(lo[], hi[])` | 各頂点の次数制約（上の多くを一般化） |
| `GraphPartitionSpec(k, balance)` | k 分割（選挙区割り・地域分割） |
| `CutSpec(s, t)` | s-t カット |
| `EdgeCoverSpec` | 辺被覆 |

**頂点の族（SetSet 系）**

| スペック | 内容 |
|---|---|
| `IndependentSetSpec` / `CliqueSpec` | 独立集合・クリーク |
| `VertexCoverSpec` / `DominatingSetSpec` | 頂点被覆・支配集合 |
| `ColoringSpec(k)` | k 彩色（頂点 × 色を変数にする） |

**汎用の組合せ制約**

| スペック | 内容 |
|---|---|
| `CardinalitySpec(min, max)` | 要素数 k 個ちょうど / 範囲 |
| `LinearConstraintSpec(a[], op, b)` | 線形制約（部分和・ナップサック） |
| `KnapsackSpec` | 容量制約 |
| `LookaheadSpec` | 枝刈り補助 |
| `DfaSpec` / `SequenceSpec` | DFA を ZDD に（正規言語による制約） |
| `SortedSetsSpec`, `CombinationSpec`, `PowerSetSpec` | 基本形 |

段階的に増やす。v0.2 で「s–t パス」「全域木」「マッチング」「基数制約」の 4 つが動けば
フレームワークの妥当性は検証できる。

---

## 8. Graphs レイヤ（Graphillion 相当の高レベル API）

```csharp
var g = Graph.Grid(9, 9);                      // 格子グラフ
var paths = GraphSet.Paths(g, from: 0, to: 80);

Console.WriteLine(paths.Count);                // BigInteger
var shortest = paths.MinWeight(e => 1);        // 最短
var sample   = paths.Sample(new Random(42));   // 一様サンプリング

var filtered = paths.Including(edge).Excluding(other).Smaller(20);
foreach (var p in filtered.Take(10)) { ... }
```

- `GraphSet` は `Zdd` の薄いラッパ（辺 index ↔ 変数 index のマッピングを保持）。
- `SetSet<T>` は任意の要素型の族を扱う汎用ラッパ（`T` ↔ 変数 index の辞書を保持）。
- **`IEnumerable<...>` は実装するが `ICollection` は実装しない**
  （`Count` が `int` に収まらないため。`Count` プロパティは `BigInteger`、
  LINQ の `Count()` と衝突しないよう名前を `Count` / `LongCount` / `CountApprox` で整理する）。
- Graphillion の語彙（`paths` `cycles` `trees` `forests` `matchings` `cliques`
  `including` `excluding` `larger` `smaller` `rand_iter` `max_iter`）を
  .NET 命名規約に直して踏襲 → Python から移ってくる利用者の学習コストがゼロになる。

### 辺順序の最適化

フロンティア幅は計算量の指数の肩に乗るため、**辺順序が性能の 9 割を決める**。

```csharp
public enum EdgeOrderStrategy { AsGiven, Bfs, Dfs, BeamSearchPathWidth, Grid }
graph.Optimize(EdgeOrderStrategy.BeamSearchPathWidth);
graph.EstimateMaxFrontierSize();   // 実行前に見積り、大きすぎるなら警告
```

- 既定は BFS 順（Graphillion と同等）。
- ビームサーチによるパス幅近似最小化を v0.4 で追加。
- 格子グラフは専用の蛇行順序を用意。
- 「見積り API」を用意して、無謀な計算を始める前にユーザに警告できるようにする（実用上かなり重要）。

---

## 9. I/O・可視化・相互運用

| 機能 | 内容 |
|---|---|
| DOT 出力 | `zdd.ToDot()` → Graphviz。レベルラベル・状態ラベルのカスタマイズ可 |
| 独自バイナリ形式 | ノード表を直接シリアライズ。高速・コンパクト |
| テキスト形式 | Graphillion の `dumps`/`loads` 互換 → **Python の Graphillion と往復できる**（移行・検証の両面で有用） |
| Knuth 形式 | 参考実装との比較検証用（任意） |
| 集合列挙のストリーム出力 | 巨大な結果を `TextWriter` に流す |

`System.Text.Json` などへの依存は入れない（依存ゼロを保つ）。

---

## 10. 性能設計（.NET 固有の勘所）

1. **アロケーションを出さない**: ノード表・状態表は全て `int[]` / struct 配列。列挙以外で GC を起こさない。
2. **struct ジェネリクス + インタフェース制約**でスペック呼び出しを devirtualize・インライン化。
   `IDdSpec<T>` を interface 型で受けると仮想呼び出しになり数倍遅くなるので、
   **必ず `where TSpec : IDdSpec<TState>` の型引数で受ける**。
3. **境界チェック除去**: ループを `for (int i = 0; i < arr.Length; i++)` の形に揃える。
   必要に応じて `Unsafe.Add` / `ref` 経由でさらに詰める。
4. **ハッシュ表はオープンアドレス法**（`Dictionary` の 2 段間接を避ける）。
5. **`BigInteger` は遅い**ので、濃度計算は `double`（近似・高速）と `BigInteger`（厳密）の 2 系統。
   さらに「128bit 整数で足りる場合の高速パス」を検討。
6. **再帰を避ける**（§4.5）。
7. **ServerGC / `TieredPGO`** をドキュメントで推奨。ベンチはこの設定で測る。
8. **並列化**（v0.4）: フロンティア構築のレベル内展開を `Parallel.For` +
   パーティション別状態表 → 結合。演算キャッシュはスレッドローカルに分割。

### 性能目標

| ベンチ | 目標 |
|---|---|
| 9×9 格子の s–t 単純パス（3266598486981642 通り） | 1 秒以内 |
| 11×11 格子（1568758030464750013214100 通り） | 60 秒以内 / メモリ 8 GB 以内 |
| Graphillion（C++ コア）との比 | **3 倍以内**、最終的に 2 倍以内 |

「C++ に勝つ」は目標にしない。「同じオーダーで、.NET から依存なしに使える」ことが価値。

---

## 11. テスト戦略

1. **総当たり照合**: 変数数 ≤ 16 で全部分集合を素朴に列挙し、全ての家族代数演算の結果と一致するか検証。
   ランダムな族を大量生成してプロパティテスト（CsCheck）。
2. **代数法則**: 交換則・結合則・分配則・ド・モルガン、`f = f/g * g + f%g` など。
3. **正準性**: 同じ族を異なる手順で作っても、ノード ID が一致すること。
4. **既知の値との照合**
   - n×n 頂点格子の対角自己回避パス数 = **OEIS A007764**
     （2×2:2, 3×3:12, 4×4:184, 5×5:8512, 6×6:1262816, 7×7:575780564,
     8×8:789360053252, 9×9:3266598486981642, 11×11:1568758030464750013214100）
   - 全域木の個数 = **行列木定理**（Kirchhoff）で独立に計算して照合
   - 完全マッチング数 = パーマネント／bitmask DP で照合
   - 独立集合数 = 素朴 DP で照合
5. **列挙とカウントの整合**: 小規模ケースで `Count` と実際の列挙数、`ElementAt` の全走査が一致。
6. **一様サンプリングの検定**: カイ二乗検定で偏りがないこと。
7. **ラウンドトリップ**: シリアライズ／デシリアライズ、Graphillion 形式との相互変換。
8. **ストレス**: 深い ZDD（変数数 10 万）でスタックオーバーフローしないこと（§4.5 の回帰テスト）。
9. **CI**: GitHub Actions で ubuntu / windows × 2 TFM のマトリクス、カバレッジ計測。

### 11.1 プロパティテストの置き場と再現性

プロパティテストは `tests/ZDD.Net.Tests.Properties/`（CsCheck）に置く。総当たり照合
（`tests/ZDD.Net.Tests/`）とは別プロジェクトにするのは、**CsCheck の `PackageReference` を
そこだけに閉じ込める**ため。本体 `src/ZDD.Net` の依存ゼロ（`docs/OPEN-QUESTIONS.md` B1）は
`DependencyPolicyTests` が csproj と出来上がったアセンブリの両方で検査する。

- **入力**は ZDD ではなくビットマスクの並び（`FamilySpec`）で生成する。反例を縮めた結果が
  「変数 1 個・集合 2 個」のように読める形で出るのは、生成器がこの形だからである。
- **再現性**: CsCheck の `seed` は最初の 1 回ぶんの種でしかないので、`PropertyCheck` が
  種から `PCG` を 1 本立て、各回の生成直前の状態を種として控えながら自分で回す。
  よって種を固定すれば入力列全体が再現する。種はプロパティ名から決まり、
  環境変数 `ZDD_PROPERTY_SEED` で差し替えられる。
- **シュリンク**: 失敗した回の種で CsCheck の `Sample` を回し直し、残りの試行を反例の縮小に使う。
  縮んだ反例とそれを再生する種は例外メッセージとテスト出力の両方に出る。
- **実行時間**: 既定は 1 プロパティ 100 回で、CI の追加時間は 1 秒未満。
  重く回したいときは `ZDD_PROPERTY_ITER` を上げる（20000 回でおよそ 1 分）。

---

## 12. マイルストーン

| 版 | 内容 | 目安 |
|---|---|---|
| **v0.1** | Core エンジン（ノード表・一意化表・演算キャッシュ・家族代数全演算・カウント・列挙・サンプリング・unranking）＋総当たりテスト | 2〜3 週 |
| **v0.2** | Frontier フレームワーク（`IDdSpec` / 構築器 / 評価器）＋ `FrontierManager` ＋ s–t パス・全域木・マッチング・基数制約。格子パス数で正当性検証 | 2〜3 週 |
| **v0.3** | スペック群の拡充（連結・分割・シュタイナー・次数制約・独立集合ほか）＋ `Graph` / `GraphSet` / `SetSet<T>` 高レベル API | 3 週 |
| **v0.4** | 性能: 辺順序最適化（ビームサーチ）・並列構築・キャッシュ調整・BenchmarkDotNet・Graphillion 比較レポート | 3 週 |
| **v0.5** | I/O（DOT・バイナリ・Graphillion 互換）・ノード GC・サンプル CLI・DocFX ドキュメント | 2 週 |
| **v1.0** | API 凍結・NativeAOT 検証・NuGet 公開・README/チュートリアル整備 | 1〜2 週 |

各版でリリースノートとベンチ結果を `docs/benchmarks.md` に追記する。

---

## 13. リスクと対策

| リスク | 影響 | 対策 |
|---|---|---|
| フロンティア幅の爆発でメモリ枯渇（**数千辺想定なので現実的なリスク**） | 大 | 事前見積り API・辺順序最適化・上限設定と graceful な例外・進捗コールバック |
| 深い再帰による `StackOverflowException`（プロセス即死） | 大 | 設計初期から**全演算を反復実装**（§4.5）。回帰テストで担保 |
| `IDdSpec` を interface 型で受けて仮想呼び出しになり大幅低速化 | 中 | struct ジェネリック制約を API で強制。アナライザまたはベンチで検出 |
| net10 単独にしたことで .NET Framework / Unity 利用者に届かない | 中 | 承知の上での決定。内部実装を「素の配列 + int index」に保ち、必要になれば `netstandard2.0` を後から足せる状態にしておく |
| `BigInteger` がボトルネックになる | 中 | `double` 近似版を既定に、厳密版は明示 API |
| 参考 OSS のコード混入によるライセンス問題 | 中 | 論文からの再実装を原則化。`THIRD-PARTY-NOTICES.md` を整備 |
| ZDD の理論を知らない利用者が使えない | 中 | Graphillion 語彙の高レベル API を用意し、ZDD を知らなくても使える入口を作る |
| API を早期に固めすぎて後で壊す | 小 | v1.0 まで `[Experimental]` 属性で明示（NuGet バージョン自体は正式版タグ `vX.Y.Z` から生成する） |

---

## 14. 最初の一歩（v0.1 の実装順）

1. `Directory.Build.props`・`.editorconfig`・ソリューション・CI ワークフローの雛形
2. `Internal/`: ハッシュ関数・ビット操作・nullable 属性 polyfill
3. `Core/NodeTable`（ノード配列 + オープンアドレス一意化表 + リサイズ）
4. `Core/OperationCache`
5. `Zdd` struct と `ZddManager`、終端・`Singleton`・`Change`・`OnSet`/`OffSet`
6. 二項演算（反復実装）: `Union` / `Intersect` / `Difference` / `Product` / `Quotient` / `Remainder`
7. `IDdEval` とボトムアップ評価 → `Count` / `CountApprox` / `MaxWeight`
8. 列挙・`ElementAt`・`Sample`
9. 総当たり照合テストとプロパティテスト
10. `ToDot()`（デバッグに必須なので早めに）

---

## 付録: 参考リンク

- TdZdd — https://github.com/kunisura/TdZdd (MIT, ERATO MINATO Project)
- TdZdd ユーザガイド — https://github.com/kunisura/TdZdd/blob/master/userguide.md
- Graphillion — https://github.com/graphillion/graphillion (MIT)
- Graphillion 論文 — Inoue et al., *Graphillion: software library for very large sets of labeled graphs*, STTT 2016
- SAPPOROBDD — https://github.com/Shin-ichi-Minato/SAPPOROBDD (MIT)
- frontier_basic_tdzdd（フロンティア法の最小実装例）— https://github.com/junkawahara/frontier_basic_tdzdd
- Minato, *Zero-suppressed BDDs for Set Manipulation in Combinatorial Problems*, DAC 1993
- Kawahara, Inoue, Iwashita, Minato, *Frontier-based Search for Enumerating All Constrained Subgraphs with Compressed Representation*, IEICE Trans. 2017
- Knuth, *The Art of Computer Programming* Vol.4A, §7.1.4（BDD/ZDD, SIMPATH）
- OEIS A007764（格子の自己回避パス数）— https://oeis.org/A007764
