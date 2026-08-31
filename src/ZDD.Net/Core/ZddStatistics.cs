using System;
using System.Globalization;
using System.Text;

namespace ZDD.Net.Core
{
    /// <summary>
    /// <see cref="ZddManager"/> の内部の表がいまどうなっているかの一覧
    /// （<see cref="ZddManager.GetStatistics"/> が返す）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>何のためにあるか</b>: ZDD の性能問題は「族が本当に大きい」のか
    /// 「表の設定が合っていないだけ」なのかで対処が正反対になるのに、外からは同じ
    /// 「遅い・メモリを食う」に見える。この型はその切り分けのための覗き窓で、
    /// たとえば <see cref="CacheHitRate"/> が低いまま <see cref="CacheCapacity"/> が
    /// 上限に張り付いていれば <see cref="ZddManagerOptions.MaxCacheCapacity"/> を、
    /// <see cref="UniqueTableCollisions"/> だけが突出していれば
    /// <see cref="ZddManagerOptions.InitialUniqueTableCapacity"/> を疑えばよい、と読める。
    /// </para>
    /// <para>
    /// <b>その瞬間の写し</b>: 値は <see cref="ZddManager.GetStatistics"/> を呼んだ時点のもので、
    /// 以後マネージャが変わってもこのインスタンスは変わらない。2 時点で取って差を見る使い方ができる。
    /// </para>
    /// <para>
    /// <b>積算値と現在値が混ざっている</b>: 表の大きさ（<see cref="NodeCount"/> /
    /// <see cref="CacheCapacity"/> など）は現在値、キャッシュと一意化表のカウンタ
    /// （<see cref="CacheLookups"/> / <see cref="UniqueTableCollisions"/> など）は
    /// マネージャを作ってからの積算値である。
    /// </para>
    /// </remarks>
    public readonly struct ZddStatistics : IEquatable<ZddStatistics>
    {
        internal ZddStatistics(
            long nodeCount,
            long peakNodeCount,
            long nodeTableCapacity,
            int uniqueTableCapacity,
            long uniqueTableCollisions,
            int cacheCapacity,
            int maxCacheCapacity,
            long cacheLookups,
            long cacheHits,
            long cacheOverwrites)
        {
            NodeCount = nodeCount;
            PeakNodeCount = peakNodeCount;
            NodeTableCapacity = nodeTableCapacity;
            UniqueTableCapacity = uniqueTableCapacity;
            UniqueTableCollisions = uniqueTableCollisions;
            CacheCapacity = cacheCapacity;
            MaxCacheCapacity = maxCacheCapacity;
            CacheLookups = cacheLookups;
            CacheHits = cacheHits;
            CacheOverwrites = cacheOverwrites;
        }

        /// <summary>
        /// いま確保されている非終端ノードの総数。予約済みの終端 ⊥ / ⊤ は数えない。
        /// </summary>
        /// <remarks>
        /// マネージャが作ったすべての族が共有している合計であって、族 1 つぶんではない
        /// （族ごとの数は <see cref="Zdd.NodeCount"/>）。
        /// </remarks>
        public long NodeCount { get; }

        /// <summary>
        /// <see cref="NodeCount"/> がこれまでに到達した最大値。
        /// </summary>
        /// <remarks>
        /// ノードを解放する手段がまだ無い（ノード GC は M5-3）ので、現状は常に
        /// <see cref="NodeCount"/> と等しい。「途中で一度どこまで膨らんだか」を
        /// GC が入ったあとも同じ名前で読めるようにするために先に置いてある。
        /// </remarks>
        public long PeakNodeCount { get; }

        /// <summary>
        /// ノードの格納庫がいま確保している枠の数（予約済みの終端 2 個を含む）。
        /// 使い切ると倍化される。
        /// </summary>
        public long NodeTableCapacity { get; }

        /// <summary>
        /// ノードの格納庫の使用率（0.0 〜 1.0）。<c>(NodeCount + 終端 2 個) / NodeTableCapacity</c>。
        /// </summary>
        /// <remarks>
        /// 倍化の直後に 0.5 まで落ちて、そこから 1.0 へ向かう鋸歯になる。1.0 に近ければ
        /// 次の演算で倍化（＝一括のコピー）が起きうる、という意味しか持たない。
        /// </remarks>
        public double NodeTableLoadFactor =>
            NodeTableCapacity == 0 ? 0.0 : (double)(NodeCount + NodeTable.FirstNodeId) / NodeTableCapacity;

        /// <summary>一意化表のスロット配列の大きさ（2 の冪）。</summary>
        public int UniqueTableCapacity { get; }

        /// <summary>
        /// 一意化表の負荷率（0.0 〜 1.0）。<c>NodeCount / UniqueTableCapacity</c> で、
        /// <see cref="UniqueTable.MaxLoadFactorPercent"/>% を超えると倍化される。
        /// </summary>
        public double UniqueTableLoadFactor =>
            UniqueTableCapacity == 0 ? 0.0 : (double)NodeCount / UniqueTableCapacity;

        /// <summary>
        /// 一意化表の線形探索が「別のキーが入っていたスロット」を読み飛ばした延べ回数。
        /// </summary>
        /// <remarks>
        /// ノード 1 個あたり（<c>UniqueTableCollisions / NodeCount</c>）で見るのが分かりやすい。
        /// 負荷率が低いのにこれが大きいなら、ハッシュの散り具合の問題である。
        /// </remarks>
        public long UniqueTableCollisions { get; }

        /// <summary>
        /// 演算キャッシュのいまのエントリ数（0 か 2 の冪）。0 ならキャッシュは効いていない。
        /// </summary>
        public int CacheCapacity { get; }

        /// <summary>
        /// 演算キャッシュが広がれるエントリ数の上限
        /// （<see cref="ZddManagerOptions.MaxCacheCapacity"/> を 2 の冪に切り下げたもの）。
        /// </summary>
        public int MaxCacheCapacity { get; }

        /// <summary>演算キャッシュを引いた延べ回数。</summary>
        public long CacheLookups { get; }

        /// <summary>そのうち当たった回数。</summary>
        public long CacheHits { get; }

        /// <summary>そのうち外れた回数。</summary>
        public long CacheMisses => CacheLookups - CacheHits;

        /// <summary>演算キャッシュのヒット率（0.0 〜 1.0）。一度も引いていなければ 0。</summary>
        /// <remarks>
        /// キャッシュは lossy なので、これが低くても答は変わらない（同じ部分問題をもう一度
        /// 計算するだけ）。ただし計算量は跳ね上がりうる（<see cref="OperationCache"/> の解説）。
        /// </remarks>
        public double CacheHitRate => CacheLookups == 0 ? 0.0 : (double)CacheHits / CacheLookups;

        /// <summary>
        /// 書き込みのときに、別の <c>(演算, オペランド)</c> のエントリを上書きした回数。
        /// </summary>
        /// <remarks>
        /// キャッシュは direct-mapped で衝突したら無条件に上書きするので、これが
        /// <see cref="CacheLookups"/> に対して大きいなら表が小さすぎる。
        /// </remarks>
        public long CacheOverwrites { get; }

        /// <inheritdoc/>
        public bool Equals(ZddStatistics other) =>
            NodeCount == other.NodeCount
            && PeakNodeCount == other.PeakNodeCount
            && NodeTableCapacity == other.NodeTableCapacity
            && UniqueTableCapacity == other.UniqueTableCapacity
            && UniqueTableCollisions == other.UniqueTableCollisions
            && CacheCapacity == other.CacheCapacity
            && MaxCacheCapacity == other.MaxCacheCapacity
            && CacheLookups == other.CacheLookups
            && CacheHits == other.CacheHits
            && CacheOverwrites == other.CacheOverwrites;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is ZddStatistics other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            HashCode hash = default;
            hash.Add(NodeCount);
            hash.Add(PeakNodeCount);
            hash.Add(NodeTableCapacity);
            hash.Add(UniqueTableCapacity);
            hash.Add(UniqueTableCollisions);
            hash.Add(CacheCapacity);
            hash.Add(MaxCacheCapacity);
            hash.Add(CacheLookups);
            hash.Add(CacheHits);
            hash.Add(CacheOverwrites);
            return hash.ToHashCode();
        }

        /// <summary>2 つの統計が同じ値かどうか。</summary>
        /// <param name="left">左辺。</param>
        /// <param name="right">右辺。</param>
        public static bool operator ==(ZddStatistics left, ZddStatistics right) => left.Equals(right);

        /// <summary>2 つの統計が違う値かどうか。</summary>
        /// <param name="left">左辺。</param>
        /// <param name="right">右辺。</param>
        public static bool operator !=(ZddStatistics left, ZddStatistics right) => !left.Equals(right);

        /// <summary>
        /// 人が読むための複数行の要約。項目名は英語、数値は不変カルチャで整形する。
        /// </summary>
        /// <remarks>
        /// 出力の形は<b>約束しない</b>（デバッグ表示であって解析用の形式ではない）。
        /// 機械的に読むなら個々のプロパティを使うこと。
        /// </remarks>
        public override string ToString()
        {
            StringBuilder text = new StringBuilder();

            text.Append(CultureInfo.InvariantCulture, $"nodes           : {NodeCount:N0} (peak {PeakNodeCount:N0})\n");
            text.Append(CultureInfo.InvariantCulture, $"node table      : {NodeTableCapacity:N0} slots, {NodeTableLoadFactor:P1} used\n");
            text.Append(CultureInfo.InvariantCulture, $"unique table    : {UniqueTableCapacity:N0} slots, {UniqueTableLoadFactor:P1} load, {UniqueTableCollisions:N0} collision(s)\n");
            text.Append(CultureInfo.InvariantCulture, $"operation cache : {CacheCapacity:N0} / {MaxCacheCapacity:N0} entries\n");
            text.Append(CultureInfo.InvariantCulture, $"cache lookups   : {CacheLookups:N0} ({CacheHits:N0} hit, {CacheMisses:N0} miss, {CacheHitRate:P1} hit rate)\n");
            text.Append(CultureInfo.InvariantCulture, $"cache overwrites: {CacheOverwrites:N0}");

            return text.ToString();
        }
    }
}
