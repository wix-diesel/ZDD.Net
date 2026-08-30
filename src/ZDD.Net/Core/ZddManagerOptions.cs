using ZDD.Net.Internal;

namespace ZDD.Net.Core
{
    /// <summary>
    /// <see cref="ZddManager"/> を生成するときの調整項目。既定値のままで実用上問題ないよう選んであり、
    /// 「作る族の規模が事前に分かっている」場合にだけ触ればよい。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 値は <see cref="ZddManager"/> のコンストラクタで読み取られ、その時点でマネージャ側に写し取られる。
    /// 同じインスタンスを複数のマネージャに使い回してよく、生成後にプロパティを変えても
    /// 既存のマネージャには影響しない。
    /// </para>
    /// <para>
    /// 演算キャッシュ（docs/PLAN.md §4.3）の設定は M1-4 でここに追加する。
    /// </para>
    /// </remarks>
    public sealed class ZddManagerOptions
    {
        /// <summary><see cref="InitialNodeCapacity"/> の既定値。</summary>
        public const int DefaultInitialNodeCapacity = 1024;

        /// <summary><see cref="InitialUniqueTableCapacity"/> の既定値。</summary>
        public const int DefaultInitialUniqueTableCapacity = 1024;

        private int _initialNodeCapacity = DefaultInitialNodeCapacity;
        private int _initialUniqueTableCapacity = DefaultInitialUniqueTableCapacity;

        /// <summary>
        /// ノードの格納庫にあらかじめ確保しておくノード数。足りなくなれば自動で倍化されるので、
        /// これは「倍化を何回か省くための助言」でしかない。
        /// </summary>
        /// <value>
        /// 1 以上。既定は <see cref="DefaultInitialNodeCapacity"/>。
        /// </value>
        /// <exception cref="System.ArgumentOutOfRangeException">
        /// 0 以下、または一度に確保できるノード数の上限を超える値を設定した場合。
        /// </exception>
        public int InitialNodeCapacity
        {
            get => _initialNodeCapacity;
            set
            {
                // ParamName はプロパティ名ではなく "value"（セッターの実引数名）にする。BCL の
                // プロパティセッターと同じ規約で、CA2208 の「実在する引数名か」の検査にも通る。
                // どのプロパティかはメッセージ側で名指しする。
                if (value <= 0 || value > MaxInitialNodeCapacity)
                {
                    ThrowHelper.ThrowArgumentOutOfRangeException(
                        nameof(value),
                        $"'{nameof(InitialNodeCapacity)}' must be between 1 and {MaxInitialNodeCapacity}, but was {value}.");
                }

                _initialNodeCapacity = value;
            }
        }

        /// <summary>
        /// 一意化表のスロット配列の初期サイズ。内部で 2 の冪に切り上げられる。
        /// ノード数が容量の 70% を超えると自動で倍化される。
        /// </summary>
        /// <value>
        /// 1 以上。既定は <see cref="DefaultInitialUniqueTableCapacity"/>。
        /// </value>
        /// <exception cref="System.ArgumentOutOfRangeException">
        /// 0 以下、またはスロット配列の上限を超える値を設定した場合。
        /// </exception>
        public int InitialUniqueTableCapacity
        {
            get => _initialUniqueTableCapacity;
            set
            {
                if (value <= 0 || value > UniqueTable.MaxCapacity)
                {
                    ThrowHelper.ThrowArgumentOutOfRangeException(
                        nameof(value),
                        $"'{nameof(InitialUniqueTableCapacity)}' must be between 1 and {UniqueTable.MaxCapacity}, but was {value}.");
                }

                _initialUniqueTableCapacity = value;
            }
        }

        /// <summary>
        /// <see cref="InitialNodeCapacity"/> に指定できる最大値。ノード表は予約済みの終端 2 個を
        /// 同じ配列に持つため、配列長の上限からその分を引いた値になる。
        /// </summary>
        internal static int MaxInitialNodeCapacity => NodeTable.MaxCapacity - NodeTable.FirstNodeId;
    }
}
