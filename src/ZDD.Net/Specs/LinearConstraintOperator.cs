namespace ZDD.Net.Specs
{
    /// <summary>The comparison a <see cref="LinearConstraintSpec"/> enforces between the weighted sum and its bound.</summary>
    public enum LinearConstraintOperator
    {
        /// <summary><c>Σ a[i] x[i] &lt;= b</c>.</summary>
        LessOrEqual,

        /// <summary><c>Σ a[i] x[i] == b</c>.</summary>
        Equal,

        /// <summary><c>Σ a[i] x[i] &gt;= b</c>.</summary>
        GreaterOrEqual,
    }
}
