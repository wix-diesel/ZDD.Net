using System;

namespace ZDD.Net.Core
{
    /// <summary>
    /// Thrown when a <see cref="Zdd"/> handle obtained before a <see cref="ZddManager.Collect()"/>
    /// call is used afterward, without having been registered in <see cref="ZddManager.RootSet"/>
    /// at collection time.
    /// </summary>
    /// <remarks>
    /// Collection compacts the node table and reassigns ids to the surviving nodes (see
    /// docs/PLAN.md &#167;4.4), so a handle's old id may now name a different family, or nothing at
    /// all. Silently trusting it would return wrong answers instead of failing loudly, so it is
    /// rejected here as soon as it is passed to any operation. Handles read back from
    /// <see cref="ZddManager.RootSet"/> after collection are unaffected — they carry the current
    /// generation and keep working.
    /// </remarks>
    public sealed class ZddCollectedException : InvalidOperationException
    {
        /// <summary>Creates an exception describing which handle was invalidated.</summary>
        /// <param name="message">A message explaining that the handle predates a <see cref="ZddManager.Collect()"/> call.</param>
        public ZddCollectedException(string message)
            : base(message)
        {
        }
    }
}
