using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZDD.Net.Tests.Harness
{
    /// <summary>
    /// 集合の族（family of sets）を圧縮なしで持つ素朴な実装。ZDD の演算結果を照合する相手。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>役割</b>: ZDD の演算は結果を目視で確かめられない。そこで「定義をそのままループで書いた実装」を
    /// 別に用意し、小さい変数数で総当たり照合する（docs/PLAN.md §11-1）。
    /// ここの実装は <b>ZDD 側を見ずに定義から書く</b>のが約束事で、
    /// 速さではなく「読んで定義と一致していると確認できること」を優先する。
    /// </para>
    /// <para>
    /// <b>表現</b>: 1 つの集合は int のビットマスク（bit i が item i の有無）で、族はその
    /// <see cref="SortedSet{T}"/>。ビットマスクにしたのは、集合の等値・包含・和・積が
    /// 1 命令で書けて誤りが混ざりにくいため。並び順を固定しているのは、
    /// 失敗時のメッセージと列挙順を再現可能にするため。
    /// </para>
    /// <para>
    /// <b>変数の個数</b>: マスクを int に収めるので <see cref="MaxVariableCount"/> まで。
    /// さらに冪集合を実体化する演算（<see cref="Complement"/> / <see cref="HittingSets"/> /
    /// <see cref="PowerSet"/> / 密度指定のランダム生成）は 2^n 個を走査するため
    /// <see cref="MaxPowerSetVariableCount"/> までに制限する。
    /// </para>
    /// <para>
    /// <b>不変</b>: インスタンスは生成後に変化しない。演算はすべて新しい族を返す。
    /// </para>
    /// </remarks>
    internal sealed class BruteForceFamily : IEquatable<BruteForceFamily>
    {
        /// <summary>集合をビットマスク（int）で表すための変数の個数の上限。</summary>
        public const int MaxVariableCount = 30;

        /// <summary>冪集合 2^n を実際に走査する演算で許す変数の個数の上限。</summary>
        public const int MaxPowerSetVariableCount = 16;

        /// <summary><see cref="ToString"/> が並べる集合の最大個数。</summary>
        private const int MaxSetsInText = 12;

        private readonly SortedSet<int> _masks;

        private BruteForceFamily(int variableCount, SortedSet<int> masks)
        {
            VariableCount = variableCount;
            _masks = masks;
        }

        /// <summary>この族が使う変数（item）の個数。有効な item は 0 … <see cref="VariableCount"/> - 1。</summary>
        public int VariableCount { get; }

        /// <summary>族に属する集合のビットマスク。昇順で、重複はない。</summary>
        public IReadOnlyCollection<int> Masks => _masks;

        /// <summary>族に属する集合の個数（族の濃度）。</summary>
        public int Count => _masks.Count;

        /// <summary>この族が空の族 ∅ かどうか。</summary>
        public bool IsEmpty => _masks.Count == 0;

        /// <summary>全変数を含む集合のビットマスク（＝全体集合 U）。</summary>
        public int UniverseMask => MaskOfUniverse(VariableCount);

        // ---- 生成 ----

        /// <summary>空の族 ∅。</summary>
        public static BruteForceFamily Empty(int variableCount) =>
            new BruteForceFamily(Validate(variableCount), new SortedSet<int>());

        /// <summary>空集合だけを持つ族 {∅}。</summary>
        public static BruteForceFamily Base(int variableCount) =>
            new BruteForceFamily(Validate(variableCount), new SortedSet<int> { 0 });

        /// <summary>1 要素集合だけを持つ族 {{item}}。</summary>
        public static BruteForceFamily Singleton(int variableCount, int item)
        {
            Validate(variableCount);
            ValidateItem(variableCount, item);
            return new BruteForceFamily(variableCount, new SortedSet<int> { 1 << item });
        }

        /// <summary>全体集合の冪集合 2^U（部分集合をすべて持つ族）。</summary>
        public static BruteForceFamily PowerSet(int variableCount)
        {
            ValidatePowerSet(variableCount);
            return new BruteForceFamily(variableCount, new SortedSet<int>(AllMasks(variableCount)));
        }

        /// <summary>集合をビットマスクで与えて族を作る。</summary>
        public static BruteForceFamily FromMasks(int variableCount, IEnumerable<int> masks)
        {
            Validate(variableCount);
            ArgumentNullException.ThrowIfNull(masks);

            SortedSet<int> set = new SortedSet<int>();
            int universe = MaskOfUniverse(variableCount);

            foreach (int mask in masks)
            {
                if ((mask & ~universe) != 0 || mask < 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(masks),
                        mask,
                        $"The mask must fit in {variableCount} variable(s).");
                }

                set.Add(mask);
            }

            return new BruteForceFamily(variableCount, set);
        }

        /// <summary>
        /// 集合を item の並びで与えて族を作る（<c>FromSets(3, [0, 2], [1])</c> = <c>{{0, 2}, {1}}</c>）。
        /// </summary>
        /// <remarks>
        /// 集合を 1 つも渡さなければ空の族 ∅ になる。<c>{∅}</c> が欲しいときは <see cref="Base"/> を使う
        /// （<c>FromSets(3, [])</c> は「空集合 1 個」ではなく「集合 0 個」に解釈される）。
        /// </remarks>
        public static BruteForceFamily FromSets(int variableCount, params int[][] sets)
        {
            Validate(variableCount);
            ArgumentNullException.ThrowIfNull(sets);

            return FromMasks(variableCount, sets.Select(set => MaskOf(variableCount, set)));
        }

        /// <summary>
        /// ランダムな族を作る。2^n 個の部分集合それぞれを確率 <paramref name="density"/> で採る。
        /// </summary>
        /// <param name="variableCount">変数の個数（<see cref="MaxPowerSetVariableCount"/> 以下）。</param>
        /// <param name="density">1 つの部分集合を採る確率。0 なら ∅、1 なら冪集合になる。</param>
        /// <param name="random">乱数源。シードを固定すれば同じ族が再現される。</param>
        public static BruteForceFamily Random(int variableCount, double density, Random random)
        {
            ValidatePowerSet(variableCount);
            ArgumentNullException.ThrowIfNull(random);

            if (density is < 0.0 or > 1.0 || double.IsNaN(density))
            {
                throw new ArgumentOutOfRangeException(nameof(density), density, "The density must be in [0, 1].");
            }

            SortedSet<int> masks = new SortedSet<int>();

            foreach (int mask in AllMasks(variableCount))
            {
                if (random.NextDouble() < density)
                {
                    masks.Add(mask);
                }
            }

            return new BruteForceFamily(variableCount, masks);
        }

        /// <summary>シードを指定してランダムな族を作る。同じシードなら必ず同じ族になる。</summary>
        public static BruteForceFamily Random(int variableCount, double density, int seed) =>
            Random(variableCount, density, new Random(seed));

        /// <summary>
        /// 部分集合を <paramref name="setCount"/> 個だけ引いて族を作る。
        /// 冪集合を走査しないので、変数が多くても使える（重複した集合は 1 個に潰れる）。
        /// </summary>
        public static BruteForceFamily RandomSets(int variableCount, int setCount, Random random)
        {
            Validate(variableCount);
            ArgumentOutOfRangeException.ThrowIfNegative(setCount);
            ArgumentNullException.ThrowIfNull(random);

            SortedSet<int> masks = new SortedSet<int>();
            int bound = MaskOfUniverse(variableCount) + 1;

            for (int i = 0; i < setCount; i++)
            {
                masks.Add(random.Next(bound));
            }

            return new BruteForceFamily(variableCount, masks);
        }

        // ---- 集合演算（M1-7）----

        /// <summary>和 f ∪ g = { a : a ∈ f または a ∈ g }。</summary>
        public BruteForceFamily Union(BruteForceFamily other)
        {
            SortedSet<int> result = new SortedSet<int>(_masks);
            result.UnionWith(Compatible(other)._masks);
            return new BruteForceFamily(VariableCount, result);
        }

        /// <summary>積 f ∩ g = { a : a ∈ f かつ a ∈ g }。</summary>
        public BruteForceFamily Intersect(BruteForceFamily other)
        {
            SortedSet<int> result = new SortedSet<int>(_masks);
            result.IntersectWith(Compatible(other)._masks);
            return new BruteForceFamily(VariableCount, result);
        }

        /// <summary>差 f \ g = { a : a ∈ f かつ a ∉ g }。</summary>
        public BruteForceFamily Difference(BruteForceFamily other)
        {
            SortedSet<int> result = new SortedSet<int>(_masks);
            result.ExceptWith(Compatible(other)._masks);
            return new BruteForceFamily(VariableCount, result);
        }

        /// <summary>対称差 f △ g = (f \ g) ∪ (g \ f)。</summary>
        public BruteForceFamily SymmetricDifference(BruteForceFamily other)
        {
            SortedSet<int> result = new SortedSet<int>(_masks);
            result.SymmetricExceptWith(Compatible(other)._masks);
            return new BruteForceFamily(VariableCount, result);
        }

        // ---- 積・商・剰余（M1-8）----

        /// <summary>積 f * g = { a ∪ b : a ∈ f, b ∈ g }（unate product）。</summary>
        public BruteForceFamily Product(BruteForceFamily other)
        {
            Compatible(other);

            SortedSet<int> result = new SortedSet<int>();

            foreach (int a in _masks)
            {
                foreach (int b in other._masks)
                {
                    result.Add(a | b);
                }
            }

            return new BruteForceFamily(VariableCount, result);
        }

        /// <summary>
        /// 商 f / g = { a : ∀ b ∈ g, a ∩ b = ∅ かつ a ∪ b ∈ f }。
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>g が空の族のとき</b>: 「∀ b ∈ ∅」は真なので、定義どおりなら答えは冪集合 2^U になる。
        /// 実装の都合で別の値を返す流儀もあるため、ここでは定義に従い、その旨を明記しておく。
        /// </para>
        /// <para>
        /// <b>候補の絞り込み</b>: g から 1 つ b0 を取ると、a ∈ f/g は a ∩ b0 = ∅ かつ a ∪ b0 ∈ f を満たす。
        /// よって a = (a ∪ b0) \ b0 の形、すなわち候補は { c \ b0 : c ∈ f } に限られる。
        /// これは定義からの言い換えなので、照合の独立性は損なわれない。
        /// </para>
        /// </remarks>
        public BruteForceFamily Quotient(BruteForceFamily other)
        {
            Compatible(other);

            if (other.IsEmpty)
            {
                return PowerSet(VariableCount);
            }

            int pivot = other._masks.Min;
            SortedSet<int> candidates = new SortedSet<int>(_masks.Select(mask => mask & ~pivot));
            SortedSet<int> result = new SortedSet<int>();

            foreach (int a in candidates)
            {
                bool ok = true;

                foreach (int b in other._masks)
                {
                    if ((a & b) != 0 || !_masks.Contains(a | b))
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok)
                {
                    result.Add(a);
                }
            }

            return new BruteForceFamily(VariableCount, result);
        }

        /// <summary>剰余 f % g = f \ ((f / g) * g)。<c>f = f/g * g + f%g</c> が成り立つ。</summary>
        public BruteForceFamily Remainder(BruteForceFamily other) =>
            Difference(Quotient(other).Product(other));

        // ---- 包含系（M1-9）----

        /// <summary>Meet f ⊓ g = { a ∩ b : a ∈ f, b ∈ g }。</summary>
        public BruteForceFamily Meet(BruteForceFamily other)
        {
            Compatible(other);

            SortedSet<int> result = new SortedSet<int>();

            foreach (int a in _masks)
            {
                foreach (int b in other._masks)
                {
                    result.Add(a & b);
                }
            }

            return new BruteForceFamily(VariableCount, result);
        }

        /// <summary>Restrict（SupersetsOf）= { a ∈ f : ∃ b ∈ g, b ⊆ a }。</summary>
        public BruteForceFamily Restrict(BruteForceFamily other) =>
            Filter(other, (a, b) => (a & b) == b, keepWhenFound: true);

        /// <summary>Permit（SubsetsOf）= { a ∈ f : ∃ b ∈ g, a ⊆ b }。</summary>
        public BruteForceFamily Permit(BruteForceFamily other) =>
            Filter(other, (a, b) => (a & b) == a, keepWhenFound: true);

        /// <summary>NonSubsetsOf = { a ∈ f : どの b ∈ g についても a ⊆ b でない } = f \ Permit(g)。</summary>
        public BruteForceFamily NonSubsetsOf(BruteForceFamily other) =>
            Filter(other, (a, b) => (a & b) == a, keepWhenFound: false);

        /// <summary>NonSupersetsOf = { a ∈ f : どの b ∈ g についても b ⊆ a でない } = f \ Restrict(g)。</summary>
        public BruteForceFamily NonSupersetsOf(BruteForceFamily other) =>
            Filter(other, (a, b) => (a & b) == b, keepWhenFound: false);

        // ---- 極大・極小（M1-10）----

        /// <summary>極大な集合だけを残す = { a ∈ f : a ⊊ b となる b ∈ f がない }。</summary>
        public BruteForceFamily Maximal() =>
            Extremal(strictlyContainsCandidate: true);

        /// <summary>極小な集合だけを残す = { a ∈ f : b ⊊ a となる b ∈ f がない }。</summary>
        public BruteForceFamily Minimal() =>
            Extremal(strictlyContainsCandidate: false);

        /// <summary>
        /// ヒッティング集合（横断）= { a ⊆ U : ∀ b ∈ f, a ∩ b ≠ ∅ }。
        /// </summary>
        /// <remarks>
        /// 極小なものだけが要るときは <c>HittingSets().Minimal()</c> と書く。
        /// f が ∅ を含むと、∅ と交わる集合はないので答えは空の族。
        /// f 自身が空の族なら条件は空虚に真なので冪集合になる。
        /// </remarks>
        public BruteForceFamily HittingSets()
        {
            EnsurePowerSetIsAffordable();

            SortedSet<int> result = new SortedSet<int>();

            foreach (int candidate in AllMasks(VariableCount))
            {
                bool hitsEverything = true;

                foreach (int b in _masks)
                {
                    if ((candidate & b) == 0)
                    {
                        hitsEverything = false;
                        break;
                    }
                }

                if (hitsEverything)
                {
                    result.Add(candidate);
                }
            }

            return new BruteForceFamily(VariableCount, result);
        }

        /// <summary>補 2^U \ f = { a ⊆ U : a ∉ f }（族の補集合であって、集合ごとの補ではない）。</summary>
        public BruteForceFamily Complement()
        {
            EnsurePowerSetIsAffordable();

            SortedSet<int> result = new SortedSet<int>(AllMasks(VariableCount));
            result.ExceptWith(_masks);
            return new BruteForceFamily(VariableCount, result);
        }

        // ---- 単項演算（M1-5）----

        /// <summary>Change: すべての集合について item の有無を反転する。</summary>
        public BruteForceFamily Change(int item)
        {
            ValidateItem(VariableCount, item);
            int bit = 1 << item;
            return new BruteForceFamily(VariableCount, new SortedSet<int>(_masks.Select(mask => mask ^ bit)));
        }

        /// <summary>OnSet（Subset1）: item を含む集合だけを残し、その item を取り除く。</summary>
        public BruteForceFamily OnSet(int item)
        {
            ValidateItem(VariableCount, item);
            int bit = 1 << item;

            return new BruteForceFamily(
                VariableCount,
                new SortedSet<int>(_masks.Where(mask => (mask & bit) != 0).Select(mask => mask & ~bit)));
        }

        /// <summary>OffSet（Subset0）: item を含まない集合だけを残す。</summary>
        public BruteForceFamily OffSet(int item)
        {
            ValidateItem(VariableCount, item);
            int bit = 1 << item;

            return new BruteForceFamily(
                VariableCount,
                new SortedSet<int>(_masks.Where(mask => (mask & bit) == 0)));
        }

        // ---- 問い合わせ ----

        /// <summary>ビットマスクで与えた集合が族に属するか。</summary>
        public bool Contains(int mask) => _masks.Contains(mask);

        /// <summary>item の並びで与えた集合が族に属するか。</summary>
        public bool ContainsSet(params int[] items) => _masks.Contains(MaskOf(VariableCount, items));

        public bool Equals(BruteForceFamily? other) =>
            other is not null && VariableCount == other.VariableCount && _masks.SetEquals(other._masks);

        public override bool Equals(object? obj) => Equals(obj as BruteForceFamily);

        public override int GetHashCode()
        {
            // 並び順に依らない畳み込み。集合が同じなら同じ値になる。
            int hash = VariableCount;

            foreach (int mask in _masks)
            {
                hash ^= mask * -1521134295;
            }

            return hash;
        }

        /// <summary>族を <c>{{0, 2}, {1}}</c> の形で表す。長い族は途中で打ち切る。</summary>
        public override string ToString() => Describe(MaxSetsInText);

        /// <summary>族を <c>{{0, 2}, {1}}</c> の形で表す。<paramref name="maxSets"/> 個で打ち切る。</summary>
        public string Describe(int maxSets)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(maxSets);

            if (_masks.Count == 0)
            {
                return "{} (empty family)";
            }

            StringBuilder text = new StringBuilder();
            text.Append('{');

            int written = 0;

            foreach (int mask in _masks)
            {
                if (written == maxSets)
                {
                    text.Append(", … (+").Append(_masks.Count - written).Append(" more)");
                    break;
                }

                if (written > 0)
                {
                    text.Append(", ");
                }

                text.Append(FormatSet(mask));
                written++;
            }

            return text.Append('}').ToString();
        }

        /// <summary>1 つの集合を <c>{0, 2}</c> の形で表す。空集合は ∅。</summary>
        public static string FormatSet(int mask)
        {
            if (mask == 0)
            {
                return "∅";
            }

            StringBuilder text = new StringBuilder();
            text.Append('{');

            bool first = true;

            for (int item = 0; item < MaxVariableCount; item++)
            {
                if ((mask & (1 << item)) == 0)
                {
                    continue;
                }

                if (!first)
                {
                    text.Append(", ");
                }

                text.Append(item);
                first = false;
            }

            return text.Append('}').ToString();
        }

        /// <summary>item の並びをビットマスクに直す。</summary>
        public static int MaskOf(int variableCount, params int[] items)
        {
            Validate(variableCount);
            ArgumentNullException.ThrowIfNull(items);

            int mask = 0;

            foreach (int item in items)
            {
                ValidateItem(variableCount, item);
                mask |= 1 << item;
            }

            return mask;
        }

        // ---- 内部 ----

        /// <summary>2^n 個のビットマスクを 0 から順に返す。</summary>
        private static IEnumerable<int> AllMasks(int variableCount)
        {
            int bound = MaskOfUniverse(variableCount) + 1;

            for (int mask = 0; mask < bound; mask++)
            {
                yield return mask;
            }
        }

        private static int MaskOfUniverse(int variableCount) =>
            variableCount == 0 ? 0 : (1 << variableCount) - 1;

        /// <summary>「g のどれかと関係を持つか」で f をふるいにかける共通部分。</summary>
        private BruteForceFamily Filter(BruteForceFamily other, Func<int, int, bool> matches, bool keepWhenFound)
        {
            Compatible(other);

            SortedSet<int> result = new SortedSet<int>();

            foreach (int a in _masks)
            {
                bool found = false;

                foreach (int b in other._masks)
                {
                    if (matches(a, b))
                    {
                        found = true;
                        break;
                    }
                }

                if (found == keepWhenFound)
                {
                    result.Add(a);
                }
            }

            return new BruteForceFamily(VariableCount, result);
        }

        /// <summary>極大／極小の共通部分。「自分を真に含む／に真に含まれる」相手がいない集合を残す。</summary>
        private BruteForceFamily Extremal(bool strictlyContainsCandidate)
        {
            SortedSet<int> result = new SortedSet<int>();

            foreach (int a in _masks)
            {
                bool dominated = false;

                foreach (int b in _masks)
                {
                    if (a == b)
                    {
                        continue;
                    }

                    // 極大なら「a ⊊ b」、極小なら「b ⊊ a」の相手を探す。
                    if (strictlyContainsCandidate ? (a & b) == a : (a & b) == b)
                    {
                        dominated = true;
                        break;
                    }
                }

                if (!dominated)
                {
                    result.Add(a);
                }
            }

            return new BruteForceFamily(VariableCount, result);
        }

        /// <summary>相手が同じ変数の個数かを確かめ、そのまま返す。</summary>
        private BruteForceFamily Compatible(BruteForceFamily other)
        {
            ArgumentNullException.ThrowIfNull(other);

            if (other.VariableCount != VariableCount)
            {
                throw new ArgumentException(
                    $"The families must use the same number of variables ({VariableCount} vs {other.VariableCount}).",
                    nameof(other));
            }

            return other;
        }

        private static int Validate(int variableCount)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(variableCount);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(variableCount, MaxVariableCount);
            return variableCount;
        }

        /// <summary>冪集合を走査する演算が現実的な大きさに収まるかを確かめる。</summary>
        private void EnsurePowerSetIsAffordable()
        {
            if (VariableCount > MaxPowerSetVariableCount)
            {
                throw new InvalidOperationException(
                    $"The operation walks all 2^n subsets and is limited to {MaxPowerSetVariableCount} variable(s), " +
                    $"but this family has {VariableCount}.");
            }
        }

        private static void ValidatePowerSet(int variableCount)
        {
            Validate(variableCount);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(variableCount, MaxPowerSetVariableCount);
        }

        private static void ValidateItem(int variableCount, int item)
        {
            if ((uint)item >= (uint)variableCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(item),
                    item,
                    $"The item must be in [0, {variableCount}).");
            }
        }
    }
}
