namespace ZDD.Net.Core
{
    /// <summary>
    /// Evaluator that computes the largest number of elements in any single set of the family.
    /// Backs <see cref="Zdd.MaxSetSize"/>, which is the buffer length <see cref="Zdd.EnumerateInto"/> requires.
    /// </summary>
    /// <remarks>
    /// Recurrence: <c>max(lo, hi + 1)</c> — the hi side gains one element (the node's own item),
    /// the lo side does not. Both terminals evaluate to 0: &#8868; is the empty set (size 0), and
    /// &#8869; contributes nothing to a <c>max</c> either way (its branch simply never wins against
    /// a real, non-negative alternative). Since zero-suppression guarantees no node's hi-edge
    /// points directly at &#8869;, the &#8869; value only ever appears as a node's <c>lo</c> input,
    /// where it is always dominated by a real <c>hi + 1</c> whenever the node's own subfamily is
    /// non-empty — so 0 is safe there too. The empty family (&#8709;, root == &#8869;) evaluates to
    /// 0 directly via <see cref="EvalTerminal"/>, matching <see cref="Zdd.MaxSetSize"/>'s documented
    /// "0 for &#8709;" (a buffer of length 0 correctly enumerates zero sets).
    /// </remarks>
    public readonly struct MaxSetSizeEval : IDdEval<int>
    {
        /// <inheritdoc/>
        public int EvalTerminal(bool isTrue) => 0;

        /// <inheritdoc/>
        public int EvalNode(int item, int lo, int hi) => lo > hi + 1 ? lo : hi + 1;
    }
}
