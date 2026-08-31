# AGENT.md

Guidance for AI coding agents (and human contributors) working in this repository.

## Comment and documentation rules / コメント・ドキュメントのルール

### Source code comments / ソースコードのコメント

- Write all source code comments (including XML doc comments) in **English**.
  ソースコード内のコメント（XML ドキュメントコメントを含む）はすべて**英語**で書く。
- Keep class/method (and property/type) summaries to **3 lines or fewer**. Only
  go to 4+ lines when it is genuinely unavoidable, and even then keep it short.
  クラス・メソッド（プロパティ・型を含む）の説明は**3行以下**に収める。どうしても
  難しい場合のみ4行以上を許容するが、その場合も長くし過ぎない。
- Prefer a compact summary over exhaustive `<remarks>` blocks. Explain only the
  non-obvious "why" (invariants, perf tradeoffs, gotchas) — skip what the
  signature already makes clear.
  網羅的な `<remarks>` より簡潔な要約を優先する。シグネチャから自明でない
  「なぜ」（不変条件・性能上のトレードオフ・注意点）だけを書き、自明な内容は省く。

### Documentation (docs/, README, etc.) / ドキュメント

- Specification and guideline documents (e.g. `docs/PLAN.md`, and any future
  design/spec/guideline doc such as this `AGENT.md`) are written bilingually:
  **English first, Japanese second** (English → 日本語 の順で併記する).
  仕様書・ガイドライン文書（`docs/PLAN.md` や本 `AGENT.md` のような設計・仕様・
  ガイドライン文書）は英語→日本語の順で併記する。
- Other documentation (e.g. `README.md`, `docs/ROADMAP.md`,
  `docs/OPEN-QUESTIONS.md`) stays **Japanese only**.
  それ以外のドキュメント（`README.md`、`docs/ROADMAP.md`、`docs/OPEN-QUESTIONS.md` など）
  は日本語のみでよい。
- Source code comments are always English only (see above) regardless of
  which category the surrounding file falls into.
  ソースコードのコメントは、どちらの分類であっても常に英語のみとする（上記の通り）。

### Pull requests / プルリクエスト

- PR titles and descriptions stay **Japanese only** — do not apply the
  bilingual rule to PRs.
  PR のタイトル・本文は**日本語のみ**とし、上記の併記ルールは適用しない。
