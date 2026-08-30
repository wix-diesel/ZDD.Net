using System.Runtime.CompilerServices;

namespace ZDD.Net.Core
{
    /// <summary>
    /// 二項演算の部分問題（ノード ID の対）を、<see cref="OperationWorkspace"/> が扱える
    /// <c>long</c> のキー 1 個に詰める。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>非負であること</b>: ノード ID は非負なので、上位 32bit に左オペランドを置いた値も必ず非負になる。
    /// これは <see cref="OperationWorkspace"/> のキーの約束（負値は「合成」の印）を満たすために要る。
    /// </para>
    /// <para>
    /// <b>可換演算の正規化</b>: <see cref="ZddOperations.IsCommutative"/> が真の演算では
    /// オペランドを昇順に並べ替えてからキーにする。二項の分解は <c>(f₀, g)</c> と <c>(g, f₀)</c> のように
    /// 左右が入れ替わった同じ部分問題へ何度も到達するので、正規化しておくと途中結果表も
    /// 演算キャッシュもそのぶん当たるようになる。<see cref="OperationCache"/> も同じ述語で
    /// 正規化しているので、両者のキーの意味は常に一致する。
    /// </para>
    /// <para>
    /// <b>1 箇所にまとめてある理由</b>: 詰め方と正規化が演算ごとにずれると、
    /// 「積むときと読むときで別のキーになる」という静かな取り違えが起きる。
    /// 二項演算はすべてここを通す。
    /// </para>
    /// </remarks>
    internal static class OperationKey
    {
        /// <summary>そこに部分問題が無いことを表す番兵。キーは常に非負なので取り違えない。</summary>
        public const long None = -1;

        /// <summary>2 つのノード ID を 1 個の非負の <c>long</c> に詰める。</summary>
        /// <param name="op">演算の種別。可換ならオペランドを昇順に正規化する。</param>
        /// <param name="f">左オペランドのノード ID。</param>
        /// <param name="g">右オペランドのノード ID。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Of(ZddOperation op, int f, int g)
        {
            if (f > g && ZddOperations.IsCommutative(op))
            {
                (f, g) = (g, f);
            }

            return (long)(((ulong)(uint)f << 32) | (uint)g);
        }

        /// <summary>キーに詰めた左オペランド。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int LeftOf(long key) => (int)((ulong)key >> 32);

        /// <summary>キーに詰めた右オペランド。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int RightOf(long key) => (int)key;
    }
}
