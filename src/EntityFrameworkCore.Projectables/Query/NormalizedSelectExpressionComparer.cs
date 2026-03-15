using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Projectables.Query;

/// <summary>
/// An <see cref="IEqualityComparer{T}"/> for <see cref="SelectExpression"/> that compares
/// expressions using EF Core's structural <see cref="ExpressionEqualityComparer"/>.
/// <para>
/// This comparer is used by <see cref="CteDeduplicatingRewriter"/> to detect when the same
/// <see cref="SelectExpression"/> subtree (including its alias assignments) appears in more
/// than one table-source position within a query.  Two <see cref="SelectExpression"/> nodes
/// are considered equal only if they are fully structurally identical — including the aliases
/// EF Core assigned to their tables.
/// </para>
/// <para>
/// <b>Note:</b> In practice EF Core assigns fresh aliases (e.g. <c>[e]</c>, <c>[e0]</c>) to
/// every translation of the same LINQ sub-expression, so most logically duplicate sub-queries
/// will still compare as unequal here.  A future improvement could normalise alias names before
/// comparison to detect these alias-differing duplicates.
/// </para>
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "EF1001:Internal EF Core API usage.", Justification = "Needed")]
internal sealed class NormalizedSelectExpressionComparer : IEqualityComparer<SelectExpression>
{
    public static readonly NormalizedSelectExpressionComparer Instance = new();

    private NormalizedSelectExpressionComparer() { }

    /// <inheritdoc/>
    public bool Equals(SelectExpression? x, SelectExpression? y)
        => ExpressionEqualityComparer.Instance.Equals(x, y);

    /// <inheritdoc/>
    public int GetHashCode(SelectExpression obj)
        => ExpressionEqualityComparer.Instance.GetHashCode(obj);
}
