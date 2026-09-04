# ZDD.Net タスク分割・PR ロードマップ

[docs/PLAN.md](PLAN.md) の各マイルストーンを、**1 PR = 1 レビュー単位**に分解したもの。

- ドキュメント版数: v2 (2026-09-04) — M6「API 拡充と相互運用」・M7「有向グラフ対応」を追加し、
  従来の M6「安定化と公開」を M8 に繰り下げた（末尾の「M6 / M7 を差し込んだ理由」を参照）

各タスク表の ID 列頭のチェックボックスは完了状況を表す（`[x]` = 完了）。

---

## 0. PR 運用ルール

| 項目 | ルール |
|---|---|
| 1 PR の規模 | **本体コード差分 400 行以内**を目安、上限 600 行。超えそうなら分割する（テスト・自動生成ファイルは行数に数えない） |
| 1 PR の関心事 | **1 つだけ**。「ついでにリファクタ」は別 PR |
| テスト | 全 PR にテストを同梱。テストのない PR は原則マージしない（設定ファイルのみの PR を除く） |
| CI | 全 PR で build + test がグリーンであること。`TreatWarningsAsErrors` なので警告も落ちる |
| ブランチ | `feature/<id>-<短い説明>`（例: `feature/m1-06-binary-ops`） |
| コミット | Conventional Commits（`feat:` `fix:` `test:` `docs:` `perf:` `refactor:` `chore:`） |
| マージ | squash merge。PR タイトルがそのままコミットメッセージになる |
| 依存関係 | 積み上げ（stacked）PR にせず、**前の PR がマージされてから次を切る**。並行可能な PR は表の「依存」欄で明示 |
| 言語 | **PR の本文・コメントは必ず日本語で記載する** |

### 「まだ公開 API から到達できないコード」の扱い

Core レイヤは下から積むため、序盤の PR は `internal` のコードだけが増えてレビューしづらい。対策:

1. `internal` 型には **`InternalsVisibleTo` でテストから直接テストを書く**（レビュー時に振る舞いが読める）
2. **`public` API はそれが実際に動くようになった PR で初めて公開する**。中途半端な public API を先に出さない
3. 各 PR の説明に「この PR が完成させる縦の一貫性」を 1 行で書く（例:「これで `Union` がテストから呼べる」）

### レビュー観点のチェックリスト（PR テンプレートに入れる）

- [ ] 再帰していないか（§PLAN 4.5 — 深い ZDD でスタックオーバーフロー即死）
- [ ] hot path でアロケーションしていないか
- [ ] `IDdSpec` を interface 型で受けていないか（struct ジェネリック制約になっているか）
- [ ] `PackageReference` を増やしていないか（**外部依存ゼロが方針**）
- [ ] AOT / トリミング警告を出していないか（リフレクション・動的コード生成を使っていないか）
- [ ] 総当たり照合テストが追加されているか

---

## M0: リポジトリ基盤（v0.0）

| 完了 | ID | タイトル | 内容 | 受け入れ条件 | 目安 | 依存 |
|---|---|---|---|---|---|---|
| [x] | **M0-1** | ソリューション骨格と共通ビルド設定 | `ZDD.Net.sln`、`src/ZDD.Net`（`net10.0`）、`tests/ZDD.Net.Tests`（xUnit）、`Directory.Build.props`、`Directory.Packages.props`、`.editorconfig`、`InternalsVisibleTo` | ビルド成功、スモークテストが通る | 設定のみ | — |
| [x] | **M0-2** | 開発環境セットアップ | `scripts/setup-dev-env.sh`（`apt-get install dotnet-sdk-10.0`）、`.claude/settings.json` の SessionStart フック、`global.json` | 新規セッションで `dotnet build` が通る | 設定のみ | M0-1 |
| [x] | **M0-3** | CI ワークフロー | GitHub Actions: ubuntu-latest（Linux）での build + test、カバレッジ収集、PR テンプレート（レビュー観点チェックリスト入り） | PR で CI が回りグリーン | 設定のみ | M0-1 |
| [x] | **M0-4** | 内部ユーティリティ | `Internal/Hashing`（一意化表向けの 64bit mix。`System.HashCode` は hot path には汎用すぎる）、`Internal/ThrowHelper` | 単体テスト（分布・衝突率の確認） | 〜120 | M0-1 |

---

## M1: Core エンジン（v0.1）

| 完了 | ID | タイトル | 内容 | 受け入れ条件 | 目安 | 依存 |
|---|---|---|---|---|---|---|
| [x] | **M1-1** | ノード表 | `ZddNode` struct、ノード配列、倍化リサイズ、終端 ID(0/1) の予約、**2^31 到達時の明示的例外** | 100 万ノード追加の単体テスト、リサイズ境界のテスト | 〜200 | M0-4 |
| [x] | **M1-2** | 一意化表 | オープンアドレス法ハッシュ表、`GetNode(level,lo,hi)`（**ゼロサプレス規則 `hi==0 → lo` をここで適用**）、負荷率 0.7 で倍化 | 同一 (level,lo,hi) が同一 ID を返す。衝突多発ケースのテスト | 〜250 | M1-1 |
| [x] | **M1-3** | `ZddManager` / `Zdd` の骨格 | 公開型、`Empty`/`Base`/`Singleton(item)`、`Level`↔`Item` 変換、等値比較、`NodeCount`、`Support` | `Singleton` の組合せで簡単な族が作れる | 〜250 | M1-2 |
| [x] | **M1-4** | 演算キャッシュ | direct-mapped lossy cache、キー生成、サイズ自動調整、ヒット率統計 | キャッシュ有無で結果が一致すること（乱数テスト） | 〜200 | M1-2 |
| [x] | **M1-5** | 単項演算 | `Change` / `OnSet(Subset1)` / `OffSet(Subset0)`。**反復（明示スタック）実装の雛形をここで確立する** | 総当たり照合（変数 ≤ 12） | 〜300 | M1-3, M1-4 |
| [x] | **M1-6** | 総当たり照合テスト基盤 | `BruteForceFamily`（集合をビットマスクで表した素朴実装）、ランダム族生成、`AssertSameFamily` | M1-5 の全演算を照合できる | テストのみ | M1-5 |
| [x] | **M1-7** | 集合演算 | `Union` / `Intersect` / `Difference` / `SymmetricDifference`（反復実装、キャッシュ利用） | 総当たり照合＋代数法則（交換・結合・分配。ド・モルガンは `Complement` が要るので M1-10 で追加） | 〜350 | M1-6 |
| [x] | **M1-8** | 積・商・剰余 | `Product(*)` / `Quotient(/)` / `Remainder(%)`（反復実装、キャッシュ利用） | 総当たり照合、`f == f/g*g + f%g` の検証、積の代数法則（交換・結合・分配）、境界入力（`f / ∅ == 2^U`） | 〜350 | M1-7 |
| [x] | **M1-9** | 包含系演算 | `Meet` / `Restrict(SupersetsOf)` / `Permit(SubsetsOf)` / `NonSubsetsOf` / `NonSupersetsOf` | 総当たり照合 | 〜350 | M1-8 |
| [x] | **M1-10** | 極大・極小 | `Maximal` / `Minimal` / `HittingSets` / `Complement`（`Flip` も同時に追加） | 総当たり照合＋ド・モルガン則（`Complement` が要るため M1-7 から持ち越し） | 〜300 | M1-9 |
| [x] | **M1-11** | プロパティテスト | CsCheck による全演算のランダム検証、シュリンク付き（`tests/ZDD.Net.Tests.Properties`） | CI に組み込み、シード固定で再現可能 | テストのみ | M1-10 |
| [x] | **M1-12** | ボトムアップ評価基盤 | `IDdEval<TValue>`、`Evaluate<TEval,TValue>`（反復・メモ化、`struct` 制約）、`Count`(BigInteger) / `CountApprox`(double) / `CountBySize` | 既知の族で濃度が一致 | 〜300 | M1-7 |
| [x] | **M1-13** | 列挙とメンバシップ | `GetEnumerator()`（遅延・明示スタック・辞書順オプション）、`Contains(set)`、`IsSubsetOf`、`Overlaps` | 列挙数と `Count` が一致（変数 ≤ 16 全網羅） | 〜300 | M1-12 |
| [x] | **M1-14** | ランキング／サンプリング | `ElementAt(BigInteger)`（unranking）、`IndexOf(set)`（ranking）、`Sample(Random)`、`Sample(n)` | 全走査で列挙と一致。一様性のカイ二乗検定 | 〜300 | M1-13 |
| [x] | **M1-15** | 重み最適化 | `MaxWeight` / `MinWeight` / `TopK` / `Probability` / `ExpectedValue` / `ItemFrequency`、`IWeightOps<T>` 戦略 | 総当たり照合（小規模での最適解一致） | 〜350 | M1-12 |
| [x] | **M1-16** | 可視化・統計・ストレス | `ToDot()`、`ZddStatistics`、**深い ZDD（変数 10 万）でスタックオーバーフローしない回帰テスト**、`samples/Zdd.Cli` の最小版 | 変数 10 万のテストが CI で通る | 〜250 | M1-15 |
| [ ] | **M1-17** | v0.1 リリース | `docs/api-guide.md`、README 更新、CHANGELOG、プレリリース版タグ（`v0.1.0-preview.1` 等） | タグ push で NuGet プレリリースパッケージと GitHub Pre-release が生成される | ドキュメント | M1-16, M1-18 |
| [x] | **M1-18** | `IsSubsetOf` の二乗の解消 | `QueryOperations.HasEmptySet` が 0-枝の連なりを毎回辿り直すのをやめる（走査 1 回のあいだ覚える）。`Overlaps` も同じ経路。M1-16 のストレステストで発覚 | 変数 10 万で線形の時間で終わる。ストレステストにお題を戻し、増え方の回帰テストを追加する | 〜100 | M1-16 |

**M1 の並行可能性**: M1-4 は M1-3 と並行可。M1-12〜M1-16 の系列は M1-7 完了後、M1-8〜M1-11 の系列と並行可能。

**M1-18 について**: M1-13 で入ったコードの性能上の欠陥で、M1-16 のストレステストが見つけたもの。
番号は後ろに付いているが、v0.1 として出す前に直したいので M1-17（リリース）はこれを待つ。
既存の ID を振り直すと issue との対応が崩れるため、順序は依存の列で表している。
解消済み（#90）: 「空集合を持つか」の答を走査 1 回のあいだ覚えるようにして線形になった
（変数 10 万で 24 秒 → 約 80 ミリ秒）。増え方の回帰テストは
`tests/ZDD.Net.Tests/Stress/QueryScalingTests.cs` にあり、`Overlaps` 側は打ち切りの効かない
お題（1 要素集合の族と 2 要素集合の族）で見ている。

---

## M2: フロンティア法フレームワーク（v0.2）

| 完了 | ID | タイトル | 内容 | 受け入れ条件 | 目安 | 依存 |
|---|---|---|---|---|---|---|
| [x] | **M2-1** | スペックのインタフェース定義 | `IDdSpec<TState>`、`IArrayDdSpec`、`IHybridDdSpec`、`DdResult`（⊥/⊤ 定数）、規約のドキュメント | コンパイルとドキュメントのみ（実装は次 PR） | 〜150 | M1-17 |
| [x] | **M2-2** | レベル単位の状態表 | 固定長 struct 状態／可変長配列状態のオープンアドレス表、レベルごとの生成・破棄 | 状態の重複除去が正しいこと、大量状態の単体テスト | 〜300 | M2-1 |
| [x] | **M2-3** | トップダウン幅優先展開 | レベル N→1 の展開、一時ノード表の生成、`BuildOptions`（ノード数上限・キャンセル） | 手書きの簡単なスペックで一時ノード表が期待通り | 〜350 | M2-2 |
| [x] | **M2-4** | ボトムアップ削減と Core への取り込み | ZDD 削減規則の適用、`ZddManager` の一意化表への登録、`FrontierBuilder.Build` の完成 | `PowerSetSpec` を構築して `Count == 2^n` | 〜300 | M2-3 |
| [x] | **M2-5** | 基本スペック | `PowerSetSpec` / `CardinalitySpec(min,max)` / `LinearConstraintSpec` / `KnapsackSpec` | 二項係数・部分和 DP と照合 | 〜300 | M2-4 |
| [ ] | **M2-6** | グラフデータ構造 | `Graph`（無向・辺リスト・辺順序）、`Graph.Grid/Complete/Cycle/Path`、辺 index ↔ 変数 index | 生成グラフの構造テスト | 〜250 | M1-17（M2 と並行可） |
| [ ] | **M2-7** | `FrontierManager` | 各辺の introduced / forgotten 頂点、mate スロット割当、`MaxFrontierSize`、**構築前の見積り API** | 手計算できる小グラフでフロンティアが一致 | 〜300 | M2-6 |
| [ ] | **M2-8** | s–t 単純パス | `PathSpec(s,t)`（mate 配列）、`allowAnyEndpoints` | **OEIS A007764 と一致**（〜7×7 を CI、8×8 以上は手動） | 〜300 | M2-5, M2-7 |
| [ ] | **M2-9** | 全域木・全域森 | `SpanningTreeSpec` / `ForestSpec`（comp 配列の正準化） | **行列木定理で独立計算した値と一致** | 〜300 | M2-8 |
| [ ] | **M2-10** | マッチング | `MatchingSpec(perfect:)` | bitmask DP のパーマネント計算と一致 | 〜200 | M2-9 |
| [ ] | **M2-11** | 進捗・診断・ベンチ基準値 | `IProgress` 通知、フロンティア幅のログ、`bench/ZDD.Net.Benchmarks` の初版、**代表 10 ケースの基準値を記録**（以降の PR が改善率を数値で示せるようにする） | ベンチが実行でき、基準値が `docs/benchmarks.md` に記録される | 〜250 | M2-10 |
| [ ] | **M2-12** | v0.2 リリース | ドキュメント、CHANGELOG、タグ | — | ドキュメント | M2-11 |

---

## M3: 数千辺への対応と高レベル API（v0.3）

> **優先順位の根拠**: 用途が「経路列挙・数え上げ＋汎用の組合せ数え上げ」、規模が「数千辺」と確定したため、
> 当初 M4 に置いていた **辺順序最適化と状態の bit-packing を M3 に前倒し**した。
> 数千辺ではフロンティア幅が支配的で、これらが無いと実用に届かない。
> 逆に、分割・カット・シュタイナー・彩色は用途外なので M4 に後送りした。

| 完了 | ID | タイトル | 内容 | 受け入れ条件 | 目安 | 依存 |
|---|---|---|---|---|---|---|
| [x] | **M3-1** | 辺順序（基本） | `EdgeOrderStrategy.Bfs/Dfs/Grid`、`Graph.Optimize()`、`EstimateMaxFrontierSize()` の実用化 | 数千辺の実グラフでフロンティア幅が既定順より改善 | 〜250 | M2-12 |
| [x] | **M3-2** | フロンティア状態の bit-packing | 状態を `byte`/`short`/ビットフィールドに圧縮、状態表のインライン格納 | **メモリ 50% 以上削減、結果は不変**（M2 のテストが全て通る） | 〜350 | M3-1 |
| [x] | **M3-3** | 辺順序（ビームサーチ） | パス幅近似最小化。幅の見積りに基づく探索 | 主要ベンチで M3-1 比 20% 以上の改善 | 〜350 | M3-2 |
| [x] | **M3-4** | サイクル・ハミルトン | `CycleSpec`（単一／複数）、`HamiltonianPathSpec`、`HamiltonianCycleSpec` | 完全グラフ・Petersen グラフの既知値と一致 | 〜300 | M2-12 |
| [x] | **M3-5** | スペック合成 | `spec.And(other)` / `.Or(other)`、`zdd.Subset(spec)`（ZddSubsetting） | 「パス かつ 辺数 ≤ k」が直接構築でき、事後フィルタと結果一致。中間 ZDD が小さいこと | 〜300 | M3-4 |
| [ ] | **M3-6** | 頂点系スペック | `IndependentSetSpec` / `CliqueSpec` / `VertexCoverSpec` / `DominatingSetSpec` | 素朴 DP と一致 | 〜350 | M2-12 |
| [x] | **M3-7** | 次数制約 | `DegreeConstraintSpec(lo[], hi[])` | マッチング・パスを次数制約で再現でき、結果が一致 | 〜250 | M3-4 |
| [x] | **M3-8** | `SetSet<T>` | 任意要素型の族ラッパ、要素 ↔ 変数のマッピング、LINQ 連携 | 文字列要素の族で一通り動く | 〜300 | M2-12 |
| [x] | **M3-9** | `GraphSet` | `Paths`/`Cycles`/`Trees`/`Forests`/`Matchings`、`Including`/`Excluding`/`Larger`/`Smaller`、`MinIter`/`MaxIter`/`RandIter` | Graphillion のチュートリアル相当のシナリオが再現できる | 〜400 | M3-8, M3-5 |
| [x] | **M3-10** | グラフ入出力 | DIMACS / エッジリスト / 簡易テキストの読み書き | ラウンドトリップ。数千辺の実データを読み込める | 〜200 | M3-9 |
| [ ] | **M3-11** | v0.3 リリース | ドキュメント、チュートリアル、CHANGELOG | **数千辺の実グラフで経路数え上げが完走する** | ドキュメント | M3-10 |

---

## M4: 性能と残りのスペック（v0.4）

| 完了 | ID | タイトル | 内容 | 受け入れ条件 | 目安 | 依存 |
|---|---|---|---|---|---|---|
| [ ] | **M4-1** | キャッシュ調整 | 演算キャッシュのサイズ自動調整、キー分布の改善、ヒット率の計測 | 代表ベンチで 10% 以上改善 | 〜200 | M3-11 |
| [ ] | **M4-2** | SIMD・低レベル最適化 | 状態比較とハッシュの `System.Runtime.Intrinsics` 化、`ref`/`Unsafe` による境界チェック除去 | 結果が完全一致し、代表ベンチで改善 | 〜300 | M4-1 |
| [x] | **M4-3** | 並列フロンティア構築 | レベル内展開の `Parallel.For` 化、パーティション別状態表 | **決定的な結果**（並列でもノード ID が一致、達成）。4 コアで 2.5 倍以上は `GetChild` 自体が重いスペックでのみ達成（合成ベンチで 2.44x）——組み込みスペックは状態表への登録がボトルネックのため非達成（0.9x 前後）。詳細は docs/benchmarks.md の M4-3 節 | 〜400 | M4-2 |
| [x] | **M4-4** | 連結部分グラフ | `ConnectedSubgraphSpec(terminals)` | 小グラフで総当たり照合 | 〜300 | M3-11 |
| [x] | **M4-5** | シュタイナー木 | `SteinerTreeSpec` | 既知の最小シュタイナー木と一致 | 〜250 | M4-4 |
| [x] | **M4-6** | 分割・カット | `GraphPartitionSpec(k, balance)` / `CutSpec(s,t)` | 小グラフで総当たり照合 | 〜350 | M4-4 |
| [x] | **M4-7** | 彩色・オートマトン | `ColoringSpec(k)` / `DfaSpec` | 彩色多項式と一致 | 〜300 | M3-11 |
| [x] | **M4-8** | 比較レポート | Graphillion / TdZdd との比較を `docs/benchmarks.md` に記載 | **達成**。PLAN §10 の 3 目標すべて達成（9×9 1秒以内・11×11 60秒/8GB以内・Graphillion 比 3倍以内、いずれも実測は目標に対し大きな余裕あり）。8×8 格子での Graphillion の外れ値、TdZdd（生 C++）との定数倍差は正直に分析・記録。詳細は docs/benchmarks.md の M4-8 節、比較コードは bench/comparison/ | ドキュメント | M4-3 |
| [x] | **M4-9** | v0.4 リリース | — | — | ドキュメント | M4-8 |

## M5: I/O・メモリ管理（v0.5）

| 完了 | ID | タイトル | 内容 | 受け入れ条件 | 目安 | 依存 |
|---|---|---|---|---|---|---|
| [x] | **M5-1** | バイナリシリアライズ | 独自形式の読み書き、版数管理 | ラウンドトリップ、巨大 ZDD で高速 | 〜300 | M4-9 |
| [x] | **M5-2** | Graphillion 互換 I/O | `dumps`/`loads` 互換のテキスト形式 | Python 側の出力を読み込んで結果一致 | 〜250 | M5-1 |
| [x] | **M5-3** | ノード GC | mark & sweep + コンパクション + ID リマップ、`RootSet` | GC 後も全ハンドルが正しく動く。メモリが実際に減る | 〜400 | M4-9 |
| [x] | **M5-4** | DOT 出力の拡張 | スペックの状態ラベル、レベルラベル、部分表示 | 目視確認用スナップショットテスト | 〜200 | M4-9 |
| [x] | **M5-5** | サンプル拡充 | CLI（格子パス・全域木・分割）、コードサンプル集 | 動作する | 〜300 | M5-2 |
| [x] | **M5-6** | API ドキュメント | DocFX / GitHub Pages、全 public API の XML doc | 生成が CI で回る | ドキュメント | M5-5 |
| [x] | **M5-7** | v0.5 リリース | — | — | ドキュメント | M5-6 |

---

## M6: API 拡充と相互運用（v0.6）

> **背景**: 他ライブラリ（Graphillion / TdZdd / SAPPOROBDD / CUDD+EXTRA）との比較で見つかった欠落のうち、
> **v1.0 の API 凍結（M8-1）の後に足すと破壊的変更になるもの**を先に片付ける。
> 設計の詳細は [docs/design/m6-api-expansion.md](design/m6-api-expansion.md)。
>
> 内訳は 4 系統。(a) `docs/OPEN-QUESTIONS.md` で「提供する」と決めたのに未実装だった API、
> (b) 族をユニバース／マネージャをまたいで移す手段、(c) 実装済みスペックの高レベル API への露出、
> (d) Graphillion にあって無いスペック。

| 完了 | ID | タイトル | 内容 | 受け入れ条件 | 目安 | 依存 |
|---|---|---|---|---|---|---|
| [ ] | **M6-1** | `ComplementWithin` / `PowerSetOf` | 部分ユニバースでの補集合（B8 の未実装分）。`ZddManager.PowerSetOf(items)` を葉側から 1 パスで構築 | 総当たり照合（変数 ≤ 12）。`Complement()` == `ComplementWithin(全変数)` の回帰テスト | 〜150 | M5-7 |
| [x] | **M6-2** | バッファ列挙 `EnumerateInto` | アロケーションなしの `ref struct` 列挙子（B9 の (b)）、`Zdd.MaxSetSize`（新 `IDdEval<int>`） | 変数 ≤ 16 全網羅で `Sets()` と要素・順序が一致。列挙ループが 0 アロケーション | 〜250 | M5-7 |
| [ ] | **M6-3** | `TryBuild` | 上限超過を `false` で返す構築（B11 の未実装分）。キャンセルは例外のまま | 上限超過で `false` かつ**マネージャの `NodeCount` が不変**であること | 〜200 | M5-7 |
| [ ] | **M6-4** | 項目写像（順序保存） | `Zdd.MapItems` / `MapItemsTo`。単調写像はボトムアップ 1 パス O(ノード数) | 総当たり照合。恒等写像は自分自身を返す | 〜300 | M6-1 |
| [ ] | **M6-5** | 一般置換とマネージャ間転送 | 非単調な単射写像を `map(f) = map(f0) ∪ Change(map(f1), σ(v))` の反復＋メモ化で。`TransferTo(manager)` | ランダム置換の往復 `σ→σ⁻¹` で元に戻る。順序保存経路と結果が完全一致 | 〜300 | M6-4 |
| [ ] | **M6-6** | ユニバース／辺順序をまたぐ移送 | `SetUniverse<T>.Extend`、`SetSet<T>.ToUniverse`、`GraphSet.ToEdgeOrder` | 別々に作った 2 つの `SetSet<T>` が `ToUniverse` 経由で合成できる。`Optimize()` 後の族を元の辺順序で解釈できる | 〜300 | M6-5 |
| [ ] | **M6-7** | 1 要素変種 | `AddSomeItem` / `RemoveSomeItem` / `RemoveAddSomeItems`（既存単項演算の合成で実装、対象要素を絞る `items` 版つき） | 総当たり照合（変数 ≤ 12）。計算量を XML doc に明記 | 〜250 | M6-1 |
| [ ] | **M6-8** | コストフィルタ | `CostAtMost` / `CostAtLeast` / `CostEquals`（`Subset` + `LinearConstraintSpec`）を `Zdd` / `GraphSet` / `SetSet<T>` に | 事後フィルタと結果一致。中間 ZDD が小さいこと | 〜200 | M5-7 |
| [ ] | **M6-9** | `GraphSet` 露出①（辺の族） | `ConnectedSubgraphs` / `SteinerTrees` / `Cuts` / `DegreeConstrained` / `EdgeCovers` / `Knapsacks` | 対応するスペックを直接使った結果と一致。`EdgeCovers` は次数制約の別名（PLAN §7.2 の `EdgeCoverSpec` 相当） | 〜300 | M6-8 |
| [ ] | **M6-10** | `GraphSet` 露出②（頂点の族・彩色） | `VertexCovers` / `DominatingSets` / `Partitions` / `BalancedPartitions` / `Colorings`（`SetSet<(int Vertex, int Color)>` で返す） | 同上。`BalancedPartitions` の境界計算の単体テスト | 〜300 | M6-9 |
| [ ] | **M6-11** | 次数系スペックの拡充 | `GraphSet.RegularGraphs(k)`（次数制約の別名）、`DegreeDistributionSpec`（残ヒストグラムを状態に持つ新規スペック） | 素朴 DP と一致。3 正則グラフの既知値と照合 | 〜300 | M6-9 |
| [ ] | **M6-12** | 頂点誘導部分グラフ | `InducedSubgraphSpec`。フロンティア頂点を 3 値（`Unknown`/`In`/`Out`）で持ち、判定を忘却時まで遅延させる | 頂点 ≤ 8 の総当たり（全頂点部分集合の誘導辺集合と一致） | 〜350 | M6-9 |
| [ ] | **M6-13** | biclique | `BicliqueSpec`（`SideA`/`SideB`/`Unused` の 3 値状態）、サイズ固定オーバーロード | 完全二部グラフ `K_{a,b}` の既知値と照合。小グラフで総当たり | 〜300 | M6-12 |
| [ ] | **M6-14** | 頂点グループ連結制約 | `VertexGroupSpec`（同グループは連結・別グループは非連結）。comp 配列に所属グループ（未定を含む）を持たせる | 小グラフで総当たり照合。Graphillion の `vertex_groups` と結果一致 | 〜300 | M6-9 |
| [ ] | **M6-15** | 統合ビルダ `Graphs()` | `GraphConstraints` と `GraphSet.Graphs(graph, constraints)` / `gs.Where(constraints)`。`AndErasedSpec` で畳み込む | Graphillion の `graphs()` の代表シナリオが再現できる。個別スペックを `And` した結果と一致 | 〜350 | M6-14 |
| [ ] | **M6-16** | v0.6 リリース | CHANGELOG / README / `docs/api-guide.md` / 移行対応表 | — | ドキュメント | M6-15 |

**M6 の並行可能性**: M6-1 / M6-2 / M6-3 / M6-8 は互いに独立で、M5-7 の直後に並行して切れる。
M6-4→M6-5→M6-6 は直列。M6-9 以降は M6-8 の後に一列。

---

## M7: 有向グラフ対応（v0.7）

> **背景**: `Edge` は無向固定（ハッシュが `min`/`max`）、`Graph` は自己ループと多重辺を拒否しており、
> 有向 s–t パス・有向閉路・有向全域木が**原理的に書けない**。D1（主用途 = 経路列挙）を踏まえると
> 一方通行のある道路網や依存関係グラフが扱えないことになる。
> 設計の詳細は [docs/design/m7-directed-graphs.md](design/m7-directed-graphs.md)。

| 完了 | ID | タイトル | 内容 | 受け入れ条件 | 目安 | 依存 |
|---|---|---|---|---|---|---|
| [ ] | **M7-1** | `DirectedEdge` / `DirectedGraph` | 向きを区別する等値性、逆平行辺の許容（自己ループ・多重辺は拒否）、`ToUndirected` / `Bidirected`、`Grid`/`Complete`/`Cycle`/`Path` | 構造テスト。`Bidirected(g).ToUndirected()` が `g` と一致 | 〜350 | M6-16 |
| [ ] | **M7-2** | フロンティア基盤の有向化 | 内部型 `EdgeTopology` を切り出し、`FrontierManager` / `EdgeOrdering` を付け替え。`DirectedGraph.Optimize` / `EstimateMaxFrontierSize` | **振る舞い不変のリファクタ**。既存テストが全て通ること | 〜400 | M7-1 |
| [ ] | **M7-3** | 有向 s–t 単純パス | `DirectedPathSpec`。mate 配列＋向き 1 ビット／頂点。忘却時に (入次数, 出次数) を検査 | **`Bidirected(格子)` の有向パス数が OEIS A007764 と一致**（7×7 まで CI）。頂点 ≤ 8 の総当たり | 〜350 | M7-2 |
| [ ] | **M7-4** | 有向閉路・有向ハミルトン | `DirectedCycleSpec` / `DirectedHamiltonianPathSpec` / `DirectedHamiltonianCycleSpec` | `Bidirected(g)` の有向単純閉路数が無向単純閉路数の**ちょうど 2 倍**。`K_n` の有向ハミルトン閉路が `(n-1)!` | 〜350 | M7-3 |
| [ ] | **M7-5** | 有向次数制約・arborescence | `DirectedDegreeConstraintSpec`、`ArborescenceSpec`（根つき有向全域木） | **有向行列木定理**（有向ラプラシアンの余因子）で独立計算した値と一致 | 〜350 | M7-4 |
| [ ] | **M7-6** | `DirectedGraphSet` | `SetSet<DirectedEdge>` の上に載せる薄いラッパ。`GraphSet` と同じフィルタ・列挙・重み API | `GraphSet` と同じシナリオが有向で再現できる | 〜350 | M7-5 |
| [ ] | **M7-7** | 有向グラフ I/O | 有向エッジリスト、簡易テキストの `directed` ヘッダ（後方互換）、DIMACS の `p arc`、DOT の `digraph` 出力 | ラウンドトリップ。既存の無向ファイルがそのまま読めること | 〜250 | M7-6 |
| [ ] | **M7-8** | 有向のベンチ基準値 | 双方向格子・一方通行混在格子・`K_n` ハミルトン・arborescence を `docs/benchmarks.md` に記録 | **性能目標は置かない**。無向比の倍率を測って記録することが目的 | 〜200 | M7-7 |
| [ ] | **M7-9** | v0.7 リリース | CHANGELOG / README / チュートリアルへの有向の節 | — | ドキュメント | M7-8 |

**M7 の並行可能性**: ほぼ直列。M7-2 のリファクタが全ての前提になる。

---

## M8: 安定化と公開（v1.0）

| 完了 | ID | タイトル | 内容 | 受け入れ条件 | 目安 | 依存 |
|---|---|---|---|---|---|---|
| [ ] | **M8-1** | 公開 API の凍結 | `PublicApiGenerator` + `Verify` による API 承認テスト、命名の最終レビュー | API 差分が意図せず入らない | 〜200 | M7-9 |
| [ ] | **M8-2** | trim / NativeAOT 検証 | 警告ゼロ化、AOT サンプルの実行 | AOT で全サンプルが動く | 〜150 | M8-1 |
| [ ] | **M8-3** | パッケージング | SourceLink、決定的ビルド、シンボルパッケージ、`README.md` の NuGet 表示 | `dotnet pack` の成果物を検証 | 設定のみ | M8-2 |
| [ ] | **M8-4** | チュートリアル | Getting Started、Graphillion からの移行ガイド、性能チューニング指針 | — | ドキュメント | M8-3 |
| [ ] | **M8-5** | v1.0 リリース | NuGet 公開、GitHub Release | — | — | M8-4 |

---

## 合計

| マイルストーン | PR 数 | 目安期間 |
|---|---|---|
| M0 | 4 | 2〜3 日 |
| M1 (v0.1) | 17 | 2〜3 週 |
| M2 (v0.2) | 12 | 2〜3 週 |
| M3 (v0.3) | 11 | 3 週 |
| M4 (v0.4) | 9 | 3 週 |
| M5 (v0.5) | 7 | 2 週 |
| M6 (v0.6) | 16 | 3〜4 週 |
| M7 (v0.7) | 9 | 2〜3 週 |
| M8 (v1.0) | 5 | 1〜2 週 |
| **合計** | **90** | **18〜24 週** |

1 PR あたり平均 250〜300 行、レビュー時間 15〜30 分を想定。

### M6 / M7 を差し込んだ理由（2026-09-04 の改訂）

当初 M6 だった「安定化と公開（v1.0）」を M8 に繰り下げ、その前に 2 つのマイルストーンを挟んだ。

- **M6 は API 凍結の前でなければならない**。ユニバースをまたぐ族の移送（M6-6）は
  `SetSet<T>` / `GraphSet` の不変条件に手を入れるため、凍結後に足すと破壊的変更になる。
  実装済みスペックの露出（M6-9〜M6-11）も、公開 API の面が確定してからでは追加のたびに
  API 承認テストの差分が出る。
- **M7 も凍結の前が望ましい**。`FrontierManager` を有向対応にするリファクタ（M7-2）が
  公開コンストラクタの追加を伴うため。
- v0.6 / v0.7 はいずれもプレリリース扱いなので、`docs/PLAN.md` §13 の
  「API を早期に固めすぎて後で壊す」対策（v1.0 まではプレリリース版で明示）の枠内に収まる。
