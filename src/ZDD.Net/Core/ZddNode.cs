namespace ZDD.Net.Core
{
    /// <summary>
    /// One internal ZDD node: four <c>int</c> fields (16 bytes fixed), stored as Array-of-Structures
    /// in <see cref="NodeTable"/>'s backing array. Nodes are referenced only by ID (array index),
    /// never as reference types.
    /// </summary>
    /// <remarks>
    /// Adding fields or widening types directly increases node-table memory (16 MB per million
    /// nodes). The 16-byte size is pinned by a unit test.
    /// </remarks>
    internal struct ZddNode
    {
        /// <summary>
        /// The variable level for this node: 1 = lowest (leaf side) up to N = highest (root side),
        /// matching TdZdd's convention. Terminals ⊥/⊤ have level 0.
        /// </summary>
        public int Level;

        /// <summary>0-branch: child node ID for the side that excludes this variable's item.</summary>
        public int Lo;

        /// <summary>
        /// 1-branch: child node ID for the side that includes this variable's item.
        /// The zero-suppression rule requires <c>Hi != <see cref="NodeTable.Bottom"/></c>.
        /// </summary>
        public int Hi;

        /// <summary>
        /// Next-entry ID for the unique table's chaining scheme, when used. Unused under open
        /// addressing, in which case <see cref="NodeTable"/> initializes it to <see cref="NodeTable.NoNext"/>.
        /// </summary>
        public int Next;
    }
}
