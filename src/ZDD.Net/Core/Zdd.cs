using System;
using System.Runtime.CompilerServices;
using ZDD.Net.Internal;

namespace ZDD.Net.Core
{
    /// <summary>
    /// 集合の族（family of sets）を表す値型ハンドル。所有する <see cref="ZddManager"/> への参照と
    /// ノード ID だけを持ち、大きさは 16 バイト。族の実体はマネージャ側のノード表にある。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>値型である理由</b>: 族は演算のたびに大量に生まれるので、ハンドルがクラスだと
    /// 演算 1 回ごとにヒープ割り当てが発生する。マネージャ参照を持たせているのは、
    /// <c>a | b</c> のような演算子が書ける・別マネージャの族を混ぜた誤用を検出できるため
    /// （docs/OPEN-QUESTIONS.md B4）。
    /// </para>
    /// <para>
    /// <b>等値</b>: ZDD は正準形なので「族が等しい ⇔ ノード ID が等しい」が成り立つ。
    /// よって等値比較は所有マネージャの参照一致とノード ID の一致だけで、族の走査は要らない。
    /// 別のマネージャで作った同じ内容の族は<b>等しくない</b>（ノード ID が別物のため）。
    /// </para>
    /// <para>
    /// <b><c>default(Zdd)</c></b>: どのマネージャにも属さない無効なハンドルで、
    /// <see cref="IsDefault"/> が <see langword="true"/> を返す。族としての操作は
    /// <see cref="InvalidOperationException"/> になる。等値比較と <see cref="GetHashCode"/> だけは
    /// 例外を投げずに使える（コレクションに入れても壊れないようにするため）。
    /// </para>
    /// </remarks>
    public readonly struct Zdd : IEquatable<Zdd>
    {
        private readonly ZddManager? _manager;
        private readonly int _id;

        internal Zdd(ZddManager manager, int id)
        {
            _manager = manager;
            _id = id;
        }

        /// <summary>この族を所有するマネージャ。</summary>
        /// <exception cref="InvalidOperationException">
        /// <c>default(Zdd)</c> の場合（どのマネージャにも属さないため）。
        /// </exception>
        public ZddManager Manager
        {
            get
            {
                EnsureNotDefault();
                return _manager!;
            }
        }

        /// <summary>
        /// <c>default(Zdd)</c>（どのマネージャにも属さない無効なハンドル）かどうか。
        /// </summary>
        public bool IsDefault => _manager is null;

        /// <summary>この族が空の族 ∅ かどうか。</summary>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        public bool IsEmpty
        {
            get
            {
                EnsureNotDefault();
                return _id == NodeTable.Bottom;
            }
        }

        /// <summary>この族が <c>{∅}</c>（空集合だけを持つ族）かどうか。</summary>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        public bool IsBase
        {
            get
            {
                EnsureNotDefault();
                return _id == NodeTable.Top;
            }
        }

        /// <summary>
        /// この族の根から到達できる非終端ノードの個数。終端 ⊥ / ⊤ は数えないので、
        /// <see cref="ZddManager.Empty"/> と <see cref="ZddManager.Base"/> はともに 0 になる。
        /// </summary>
        /// <remarks>
        /// 呼ぶたびに族を走査する（<see cref="ZddManager.NodeCount"/> と違い、キャッシュした値ではない）。
        /// 走査は明示スタックで、再帰しない（docs/PLAN.md §4.5）。
        /// </remarks>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        /// <exception cref="ObjectDisposedException">所有マネージャが破棄済みの場合。</exception>
        public long NodeCount => Manager.CountReachableNodes(_id);

        /// <summary>
        /// この族が実際に使っている item（変数）を昇順で返す。
        /// 族の記述に一度も現れない item は含まれない。
        /// </summary>
        /// <returns>
        /// item index の昇順配列。呼び出しごとに新しい配列を返すので、書き換えても族には影響しない。
        /// 終端だけの族（∅ と <c>{∅}</c>）では空配列。
        /// </returns>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        /// <exception cref="ObjectDisposedException">所有マネージャが破棄済みの場合。</exception>
        public int[] Support() => Manager.CollectSupport(_id);

        /// <summary>
        /// この族のすべての集合について、<paramref name="item"/> の有無を反転した族を返す。
        /// </summary>
        /// <param name="item">0 以上 <see cref="ZddManager.VariableCount"/> 未満の item index。</param>
        /// <returns>
        /// <c>{ s △ {item} : s ∈ this }</c>。たとえば <c>{∅, {1}}</c> に <c>Change(1)</c> をかけると
        /// <c>{{1}, ∅}</c>、すなわち同じ族に戻る。<b>集合の個数は変わらない</b>ので、
        /// <c>Change(i)</c> を 2 回かければ必ず元の族になる。
        /// </returns>
        /// <remarks>
        /// 実装は明示スタックによる反復で、再帰しない（docs/PLAN.md §4.5）。
        /// 途中結果はマネージャの演算キャッシュに載るので、同じ族に同じ演算を繰り返しても安い。
        /// </remarks>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="item"/> が範囲外の場合。</exception>
        /// <exception cref="ObjectDisposedException">所有マネージャが破棄済みの場合。</exception>
        public Zdd Change(int item) => Manager.Change(this, item);

        /// <summary>
        /// <paramref name="item"/> を含む集合だけを取り出し、そこから <paramref name="item"/> を
        /// 除いた族を返す（Minato の <c>Subset1</c>）。
        /// </summary>
        /// <param name="item">0 以上 <see cref="ZddManager.VariableCount"/> 未満の item index。</param>
        /// <returns>
        /// <c>{ s ∖ {item} : s ∈ this, item ∈ s }</c>。<see cref="OffSet"/> と対になっていて、
        /// <c>OffSet(i)</c> と <c>OnSet(i).Change(i)</c> は元の族を重複なく 2 つに分ける。
        /// </returns>
        /// <remarks>
        /// 実装は明示スタックによる反復で、再帰しない（docs/PLAN.md §4.5）。
        /// </remarks>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="item"/> が範囲外の場合。</exception>
        /// <exception cref="ObjectDisposedException">所有マネージャが破棄済みの場合。</exception>
        public Zdd OnSet(int item) => Manager.OnSet(this, item);

        /// <summary><see cref="OnSet"/> の別名（Minato の記法）。</summary>
        /// <param name="item">0 以上 <see cref="ZddManager.VariableCount"/> 未満の item index。</param>
        public Zdd Subset1(int item) => Manager.OnSet(this, item);

        /// <summary>
        /// <paramref name="item"/> を含まない集合だけを残した族を返す（Minato の <c>Subset0</c>）。
        /// </summary>
        /// <param name="item">0 以上 <see cref="ZddManager.VariableCount"/> 未満の item index。</param>
        /// <returns><c>{ s : s ∈ this, item ∉ s }</c>。集合そのものは変わらない。</returns>
        /// <remarks>
        /// 実装は明示スタックによる反復で、再帰しない（docs/PLAN.md §4.5）。
        /// </remarks>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="item"/> が範囲外の場合。</exception>
        /// <exception cref="ObjectDisposedException">所有マネージャが破棄済みの場合。</exception>
        public Zdd OffSet(int item) => Manager.OffSet(this, item);

        /// <summary><see cref="OffSet"/> の別名（Minato の記法）。</summary>
        /// <param name="item">0 以上 <see cref="ZddManager.VariableCount"/> 未満の item index。</param>
        public Zdd Subset0(int item) => Manager.OffSet(this, item);

        /// <summary>2 つのハンドルが同じマネージャの同じ族を指すかどうか。</summary>
        /// <param name="other">比較相手。</param>
        public bool Equals(Zdd other) => ReferenceEquals(_manager, other._manager) && _id == other._id;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is Zdd other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            HashCode.Combine(_manager is null ? 0 : RuntimeHelpers.GetHashCode(_manager), _id);

        /// <summary>2 つのハンドルが同じマネージャの同じ族を指すかどうか。</summary>
        /// <param name="left">左辺。</param>
        /// <param name="right">右辺。</param>
        public static bool operator ==(Zdd left, Zdd right) => left.Equals(right);

        /// <summary>2 つのハンドルが異なる族（または異なるマネージャ）を指すかどうか。</summary>
        /// <param name="left">左辺。</param>
        /// <param name="right">右辺。</param>
        public static bool operator !=(Zdd left, Zdd right) => !left.Equals(right);

        /// <summary>デバッグ用の短い表現。族の中身は展開しない。</summary>
        public override string ToString()
        {
            if (_manager is null)
            {
                return "Zdd(default)";
            }

            return _id switch
            {
                NodeTable.Bottom => "Zdd(empty)",
                NodeTable.Top => "Zdd(base)",
                _ => $"Zdd(#{_id})",
            };
        }

        /// <summary>所有マネージャ。<c>default(Zdd)</c> なら <see langword="null"/>。</summary>
        internal ZddManager? Owner => _manager;

        /// <summary>この族の根のノード ID。<c>default(Zdd)</c> では意味を持たない。</summary>
        internal int Id => _id;

        private void EnsureNotDefault()
        {
            if (_manager is null)
            {
                ThrowHelper.ThrowInvalidOperationException(
                    "This is a default Zdd handle, which does not belong to any manager. Obtain a Zdd from a ZddManager instead.");
            }
        }
    }
}
