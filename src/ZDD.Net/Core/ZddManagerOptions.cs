using ZDD.Net.Internal;

namespace ZDD.Net.Core
{
    /// <summary>
    /// Tuning knobs for creating a <see cref="ZddManager"/>. The defaults work fine
    /// in practice; only override these when the family's expected size is known in advance.
    /// </summary>
    /// <remarks>
    /// Values are copied into the manager by its constructor, so one instance can be reused
    /// across managers, and later changes don't affect managers already created.
    /// </remarks>
    public sealed class ZddManagerOptions
    {
        /// <summary>Default value of <see cref="InitialNodeCapacity"/>.</summary>
        public const int DefaultInitialNodeCapacity = 1024;

        /// <summary>Default value of <see cref="InitialUniqueTableCapacity"/>.</summary>
        public const int DefaultInitialUniqueTableCapacity = 1024;

        /// <summary>Default value of <see cref="InitialCacheCapacity"/>.</summary>
        public const int DefaultInitialCacheCapacity = OperationCache.DefaultInitialCapacity;

        /// <summary>Default value of <see cref="MaxCacheCapacity"/>.</summary>
        public const int DefaultMaxCacheCapacity = OperationCache.DefaultMaxCapacity;

        private int _initialNodeCapacity = DefaultInitialNodeCapacity;
        private int _initialUniqueTableCapacity = DefaultInitialUniqueTableCapacity;
        private int _initialCacheCapacity = DefaultInitialCacheCapacity;
        private int _maxCacheCapacity = DefaultMaxCacheCapacity;

        /// <summary>
        /// Number of node slots to preallocate. The store doubles automatically when full,
        /// so this only saves a few resize passes.
        /// </summary>
        /// <value>At least 1. Defaults to <see cref="DefaultInitialNodeCapacity"/>.</value>
        /// <exception cref="System.ArgumentOutOfRangeException">Value is not positive or exceeds the allowed maximum.</exception>
        public int InitialNodeCapacity
        {
            get => _initialNodeCapacity;
            set
            {
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
        /// Initial slot-array size of the unique table (rounded up to a power of two).
        /// Doubles automatically once node count exceeds 70% of capacity.
        /// </summary>
        /// <value>At least 1. Defaults to <see cref="DefaultInitialUniqueTableCapacity"/>.</value>
        /// <exception cref="System.ArgumentOutOfRangeException">Value is not positive or exceeds the slot-array maximum.</exception>
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
        /// Initial entry count of the operation cache (16 bytes per entry, rounded to a power
        /// of two and clamped to <see cref="MaxCacheCapacity"/>). Grows automatically with node
        /// count, so this only saves a few resize passes.
        /// </summary>
        /// <value>0 or more; 0 defers allocation until auto-growth needs it. Defaults to <see cref="DefaultInitialCacheCapacity"/>.</value>
        /// <exception cref="System.ArgumentOutOfRangeException">Value is negative or exceeds <see cref="OperationCache.CapacityLimit"/>.</exception>
        public int InitialCacheCapacity
        {
            get => _initialCacheCapacity;
            set
            {
                if (value < 0 || value > OperationCache.CapacityLimit)
                {
                    ThrowHelper.ThrowArgumentOutOfRangeException(
                        nameof(value),
                        $"'{nameof(InitialCacheCapacity)}' must be between 0 and {OperationCache.CapacityLimit}, but was {value}.");
                }

                _initialCacheCapacity = value;
            }
        }

        /// <summary>
        /// Upper bound on the operation cache's entry count. Auto-growth targets
        /// 1/<see cref="OperationCache.NodesPerEntry"/> of the node count but never exceeds this,
        /// and is rounded down to a power of two.
        /// </summary>
        /// <value>0 to <see cref="OperationCache.CapacityLimit"/>; 0 disables the cache. Defaults to <see cref="DefaultMaxCacheCapacity"/>.</value>
        /// <exception cref="System.ArgumentOutOfRangeException">Value is negative or exceeds <see cref="OperationCache.CapacityLimit"/>.</exception>
        public int MaxCacheCapacity
        {
            get => _maxCacheCapacity;
            set
            {
                if (value < 0 || value > OperationCache.CapacityLimit)
                {
                    ThrowHelper.ThrowArgumentOutOfRangeException(
                        nameof(value),
                        $"'{nameof(MaxCacheCapacity)}' must be between 0 and {OperationCache.CapacityLimit}, but was {value}.");
                }

                _maxCacheCapacity = value;
            }
        }

        /// <summary>
        /// Largest value allowed for <see cref="InitialNodeCapacity"/>: the node table's
        /// array-length limit minus the 2 reserved terminal slots.
        /// </summary>
        internal static int MaxInitialNodeCapacity => NodeTable.MaxCapacity - NodeTable.FirstNodeId;
    }
}
