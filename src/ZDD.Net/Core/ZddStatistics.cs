using System;
using System.Globalization;
using System.Text;

namespace ZDD.Net.Core
{
    /// <summary>
    /// A point-in-time snapshot of a <see cref="ZddManager"/>'s internal tables
    /// (returned by <see cref="ZddManager.GetStatistics"/>), useful for diagnosing
    /// whether slowness comes from family size or table sizing.
    /// </summary>
    /// <remarks>
    /// Table sizes (<see cref="NodeCount"/>, <see cref="CacheCapacity"/>, etc.) are current
    /// values; cache and unique-table counters (<see cref="CacheLookups"/>,
    /// <see cref="UniqueTableCollisions"/>, etc.) are cumulative since the manager was created.
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
            long cacheOverwrites,
            long collectionCount,
            long lastCollectionRemovedNodeCount,
            double lastCollectionReductionRatio,
            TimeSpan lastCollectionDuration)
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
            CollectionCount = collectionCount;
            LastCollectionRemovedNodeCount = lastCollectionRemovedNodeCount;
            LastCollectionReductionRatio = lastCollectionReductionRatio;
            LastCollectionDuration = lastCollectionDuration;
        }

        /// <summary>
        /// Number of non-terminal nodes currently allocated, across every family the
        /// manager owns (not just one family; see <see cref="Zdd.NodeCount"/> for that).
        /// </summary>
        public long NodeCount { get; }

        /// <summary>
        /// Highest value <see cref="NodeCount"/> has reached. Equal to <see cref="NodeCount"/>
        /// until the manager's first <see cref="ZddManager.Collect()"/>, since collection can lower
        /// <see cref="NodeCount"/> without lowering this high-water mark.
        /// </summary>
        public long PeakNodeCount { get; }

        /// <summary>Number of slots currently allocated in the node store, including the 2 reserved terminals.</summary>
        public long NodeTableCapacity { get; }

        /// <summary>Node store load factor (0.0-1.0): <c>(NodeCount + 2 terminals) / NodeTableCapacity</c>.</summary>
        public double NodeTableLoadFactor =>
            NodeTableCapacity == 0 ? 0.0 : (double)(NodeCount + NodeTable.FirstNodeId) / NodeTableCapacity;

        /// <summary>Slot-array size of the unique table (a power of two).</summary>
        public int UniqueTableCapacity { get; }

        /// <summary>
        /// Unique-table load factor (0.0-1.0): <c>NodeCount / UniqueTableCapacity</c>. Doubles
        /// once this exceeds <see cref="UniqueTable.MaxLoadFactorPercent"/>%.
        /// </summary>
        public double UniqueTableLoadFactor =>
            UniqueTableCapacity == 0 ? 0.0 : (double)NodeCount / UniqueTableCapacity;

        /// <summary>
        /// Total number of slots skipped by the unique table's linear probing while
        /// searching. High values with a low load factor point to poor hash distribution.
        /// </summary>
        public long UniqueTableCollisions { get; }

        /// <summary>Current entry count of the operation cache (0 or a power of two); 0 means the cache is inactive.</summary>
        public int CacheCapacity { get; }

        /// <summary>Ceiling the operation cache can grow to (<see cref="ZddManagerOptions.MaxCacheCapacity"/>, rounded down to a power of two).</summary>
        public int MaxCacheCapacity { get; }

        /// <summary>Total number of operation-cache lookups.</summary>
        public long CacheLookups { get; }

        /// <summary>Of those, how many hit.</summary>
        public long CacheHits { get; }

        /// <summary>Of those, how many missed.</summary>
        public long CacheMisses => CacheLookups - CacheHits;

        /// <summary>Operation-cache hit rate (0.0-1.0); 0 if never queried.</summary>
        /// <remarks>The cache is lossy, so a low rate never affects correctness — only how much work is redone.</remarks>
        public double CacheHitRate => CacheLookups == 0 ? 0.0 : (double)CacheHits / CacheLookups;

        /// <summary>Number of times a write overwrote a different <c>(operation, operands)</c> entry.</summary>
        /// <remarks>The cache is direct-mapped and overwrites unconditionally on collision; a high value relative to <see cref="CacheLookups"/> means the table is too small.</remarks>
        public long CacheOverwrites { get; }

        /// <summary>Total number of completed <see cref="ZddManager.Collect()"/> calls. 0 means <see cref="ZddManager.Collect()"/> has never run.</summary>
        public long CollectionCount { get; }

        /// <summary>
        /// Nodes reclaimed by the most recent <see cref="ZddManager.Collect()"/> call.
        /// </summary>
        /// <remarks>
        /// 0 either means <see cref="ZddManager.Collect()"/> has never run, or it has and simply
        /// reclaimed nothing (e.g. every node was still reachable from <see cref="ZddManager.RootSet"/>);
        /// check <see cref="CollectionCount"/> to tell those two apart.
        /// </remarks>
        public long LastCollectionRemovedNodeCount { get; }

        /// <summary>
        /// Fraction of nodes the most recent <see cref="ZddManager.Collect()"/> call reclaimed,
        /// relative to the node count right before that call ran (0.0-1.0).
        /// </summary>
        /// <remarks>
        /// Fixed at the moment of collection, unlike <see cref="LastCollectionRemovedNodeCount"/>
        /// combined with the current <see cref="NodeCount"/> — nodes built after that collection
        /// would otherwise skew a ratio computed from today's count. Like
        /// <see cref="LastCollectionRemovedNodeCount"/>, 0 can mean either "never collected" or "the
        /// last collection reclaimed nothing (or ran on an empty table)"; check
        /// <see cref="CollectionCount"/> to tell those apart.
        /// </remarks>
        public double LastCollectionReductionRatio { get; }

        /// <summary>
        /// Wall-clock time the most recent <see cref="ZddManager.Collect()"/> call took;
        /// <see cref="TimeSpan.Zero"/> before the first collection.
        /// </summary>
        public TimeSpan LastCollectionDuration { get; }

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
            && CacheOverwrites == other.CacheOverwrites
            && CollectionCount == other.CollectionCount
            && LastCollectionRemovedNodeCount == other.LastCollectionRemovedNodeCount
            && LastCollectionReductionRatio.Equals(other.LastCollectionReductionRatio)
            && LastCollectionDuration == other.LastCollectionDuration;

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
            hash.Add(CollectionCount);
            hash.Add(LastCollectionRemovedNodeCount);
            hash.Add(LastCollectionReductionRatio);
            hash.Add(LastCollectionDuration);
            return hash.ToHashCode();
        }

        /// <summary>Whether two statistics snapshots hold the same values.</summary>
        /// <param name="left">The left-hand operand.</param>
        /// <param name="right">The right-hand operand.</param>
        public static bool operator ==(ZddStatistics left, ZddStatistics right) => left.Equals(right);

        /// <summary>Whether two statistics snapshots hold different values.</summary>
        /// <param name="left">The left-hand operand.</param>
        /// <param name="right">The right-hand operand.</param>
        public static bool operator !=(ZddStatistics left, ZddStatistics right) => !left.Equals(right);

        /// <summary>A multi-line human-readable summary. Labels are in English, numbers use the invariant culture.</summary>
        /// <remarks>The output format is not a stable contract; read the properties directly for programmatic use.</remarks>
        public override string ToString()
        {
            StringBuilder text = new StringBuilder();

            text.Append(CultureInfo.InvariantCulture, $"nodes           : {NodeCount:N0} (peak {PeakNodeCount:N0})\n");
            text.Append(CultureInfo.InvariantCulture, $"node table      : {NodeTableCapacity:N0} slots, {NodeTableLoadFactor:P1} used\n");
            text.Append(CultureInfo.InvariantCulture, $"unique table    : {UniqueTableCapacity:N0} slots, {UniqueTableLoadFactor:P1} load, {UniqueTableCollisions:N0} collision(s)\n");
            text.Append(CultureInfo.InvariantCulture, $"operation cache : {CacheCapacity:N0} / {MaxCacheCapacity:N0} entries\n");
            text.Append(CultureInfo.InvariantCulture, $"cache lookups   : {CacheLookups:N0} ({CacheHits:N0} hit, {CacheMisses:N0} miss, {CacheHitRate:P1} hit rate)\n");
            text.Append(CultureInfo.InvariantCulture, $"cache overwrites: {CacheOverwrites:N0}\n");
            text.Append(CultureInfo.InvariantCulture, $"collections     : {CollectionCount:N0}");
            if (CollectionCount > 0)
            {
                text.Append(CultureInfo.InvariantCulture,
                    $" (last: -{LastCollectionRemovedNodeCount:N0} nodes, {LastCollectionReductionRatio:P1}, {LastCollectionDuration.TotalMilliseconds:N1} ms)");
            }

            return text.ToString();
        }
    }
}
