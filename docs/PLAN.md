# ZDD.Net 開発計画書

C# ネイティブ実装による ZDD（Zero-suppressed Decision Diagram）＋フロンティア法ライブラリの
機能・仕様・実装計画。

- ドキュメント版数: v1 (2026-08-29)
- 対象リポジトリ: `wix-diesel/ZDD.Net`（Apache-2.0）

---

## 0. エグゼクティブサマリ（結論だけ先に）

| 項目 | 結論 |
|---|---|
| ターゲット | **`netstandard2.0` + `net8.0` のマルチターゲット**（ns2.0 で全機能提供、net8 は高速パスのみ差し替え。net10 以降の利用者は net8.0 アセットがそのまま動く） |
| ネイティブ度 | 100% managed C#（P/Invoke なし・NativeAOT 互換）。**外部 NuGet 依存ゼロ** |
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

### 結論: `<TargetFrameworks>netstandard2.0;net8.0</TargetFrameworks>`

「可能なら .NET Standard」という要望に対し、**netstandard2.0 で全機能を実装可能**と判断した。
ZDD エンジンの本質は `int[]` 配列上のハッシュ表とループであり、モダン API に依存しない。

第 2 の TFM を `net10.0` ではなく **`net8.0`** にした理由:

1. **`#if` で欲しかった高速化 API はすべて net8.0 に揃っている** —
   `Span<T>` / `System.Runtime.Intrinsics` / `BitOperations` / `CollectionsMarshal` /
   `GC.AllocateUninitializedArray` / `ref` フィールド。net10 固有で必要なものが実質ない。
2. **net10 利用者は net8.0 アセットをそのまま使える**（上位互換）。TFM を増やしても得るものが少ない。
3. **開発環境の制約**（後述）。この remote 環境では .NET 10 SDK を取得できない。

net10 専用の最適化が実測で効くと分かった時点で `net10.0` を足す。コード変更は不要な設計にしておく。

### 開発環境について（実測）

現 remote 環境の egress ポリシーでは:

| ホスト | 結果 | 影響 |
|---|---|---|
| `builds.dotnet.microsoft.com` | **403（ポリシー拒否）** | `dotnet-install.sh` による SDK 取得が不可 |
| `aka.ms` / `dotnetcli.azureedge.net` | 到達不可 | 同上 |
| `packages.microsoft.com` | 200（ただし SDK パッケージなし） | .NET 9/10 は取得不可 |
| **Ubuntu noble リポジトリ** | **OK** | **`apt-get install dotnet-sdk-8.0` で .NET 8 SDK を導入できる** |
| `api.nuget.org` / `www.nuget.org` | OK | NuGet 復元は問題なく動作 |

→ `apt-get install -y dotnet-sdk-8.0` で **ビルド・テストとも実行可能**であることを確認済み
（`netstandard2.0` ビルド成功、xUnit の復元と実行成功）。
`scripts/setup-dev-env.sh` と SessionStart フックで自動化する。

net10 を正式サポートしたくなった場合は、環境のネットワークポリシーに
`builds.dotnet.microsoft.com` を追加するか、GitHub Actions 側（`actions/setup-dotnet`）でのみ
net10 をビルド・検証する。

### 外部依存: **ゼロを厳守**

`PackageReference` を 1 つも持たない。これは `netstandard2.0` 側の書き方を強く縛る:

| 使えないもの | 対処 |
|---|---|
| `Span<T>` / `Memory<T>`（`System.Memory` が必要） | **公開 API から排除**。`IArrayDdSpec` は `int[] state, int offset` の形にする。内部も素の配列 + index で書く（ZDD エンジンは元々この形なので実害は小さい）。net8 側では `#if NET` で `Span` オーバーロードを追加 |
| `ArrayPool<T>`（`System.Buffers` が必要） | `Internal/SimpleArrayPool`（`int[][]` のスタック）を自前実装。net8 では `ArrayPool` に委譲 |
| `System.HashCode`（`Microsoft.Bcl.HashCode` が必要） | `Internal/Hashing`（64bit mix・FNV）を自前実装。どのみち独自ハッシュが要る |
| `System.Numerics.BitOperations` | `Internal/BitOps` に polyfill。net8 では本家に委譲 |
| ジェネリック数学（`INumber<T>`, `static abstract`） | **使わない**。重み型は `IWeightOps<T>` を struct ジェネリック制約で受ける戦略パターン（JIT が devirtualize するので ns2.0 でも高速） |
| `[NotNullWhen]` 等の nullable 属性 | `Internal/NullableAttributes.cs` に internal で polyfill |

`netstandard2.0` の参照アセンブリに含まれるものは依存に数えない。以下は問題なく使える:

- `System.Numerics.BigInteger`（濃度計算）
- `System.Threading.Tasks.Parallel` / `CancellationToken`（並列構築・キャンセル）
- `IProgress<T>`（進捗通知）

### 共通ビルド設定

```xml
<PropertyGroup>
  <TargetFrameworks>netstandard2.0;net8.0</TargetFrameworks>
  <LangVersion>latest</LangVersion>
  <Nullable>enable</Nullable>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  <AllowUnsafeBlocks>true</AllowUnsafeBlocks>   <!-- net8 の高速パスのみ -->
  <IsAotCompatible Condition="'$(TargetFramework)'=='net8.0'">true</IsAotCompatible>
  <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
</PropertyGroup>
```

### この選択で広がる対象

.NET Framework 4.6.1+ / Unity（Mono・IL2CPP）/ Xamarin / .NET Core 2.0+ / .NET 5〜10 以降。
特に **Unity での経路列挙・組合せ最適化用途**は ns2.0 でしか届かない層であり、価値が大きい。

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
    Internal/                 … polyfill・ハッシュ・ビット操作
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
public readonly struct Zdd : IEquatable<Zdd>
{
    public ZddManager Manager { get; }
    public bool IsEmpty { get; }
    public bool IsBase { get; }
    public long NodeCount { get; }          // このZDDが参照するノード数
    public BigInteger Count { get; }        // 要素（部分集合）の個数
    public double CountApprox { get; }      // double 近似（高速）
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
// 依存ゼロ方針のため ns2.0 でも使える「配列 + オフセット」形式にする（Span は net8 側の追加 API）
public interface IArrayDdSpec
{
    int ArrayLength { get; }                                        // 状態配列の要素数
    int GetRoot(int[] state, int offset);
    int GetChild(int[] state, int offset, int level, int value);
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
    TValue EvalNode(int level, TValue lo, TValue hi);
}
public static TValue Evaluate<TEval, TValue>(this Zdd zdd, TEval eval) where TEval : IDdEval<TValue>;
```

`Count` / `Probability` / `MaxWeight` などは全てこの上に実装し、
ユーザ定義の評価（期待値、多項式、モーメント等）も同じ枠組みで書けるようにする。

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
   net8 では `Unsafe.Add` / `ref` 経由の高速パスを `#if NET` で用意。
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
| 依存ゼロ方針により ns2.0 側のコードが素の配列で二重化する | 中 | `#if NET` の分岐点を `Internal/` の少数のユーティリティに閉じ込め、アルゴリズム本体は 1 本にする |
| `BigInteger` がボトルネックになる | 中 | `double` 近似版を既定に、厳密版は明示 API |
| 参考 OSS のコード混入によるライセンス問題 | 中 | 論文からの再実装を原則化。`THIRD-PARTY-NOTICES.md` を整備 |
| ZDD の理論を知らない利用者が使えない | 中 | Graphillion 語彙の高レベル API を用意し、ZDD を知らなくても使える入口を作る |
| API を早期に固めすぎて後で壊す | 小 | v1.0 まで `[Experimental]`／プレリリース版で明示 |

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
