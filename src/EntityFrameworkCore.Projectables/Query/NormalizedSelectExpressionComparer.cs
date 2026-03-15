using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Projectables.Query;

/// <summary>
/// An <see cref="IEqualityComparer{T}"/> for <see cref="SelectExpression"/> that compares
/// expressions structurally after normalising table-alias names.
/// <para>
/// EF Core assigns fresh aliases (e.g. <c>e</c>, <c>e0</c>) to every logical copy of the same
/// LINQ sub-expression, so two occurrences of the same query — such as the two halves of a
/// <c>UNION ALL</c> — are structurally identical apart from their alias assignments.  On EF Core
/// 10 this comparer first rewrites both expressions to use canonical alias names (<c>a0</c>,
/// <c>a1</c>, …) in depth-first traversal order, then delegates to
/// <see cref="ExpressionEqualityComparer"/> for a full structural comparison.  On earlier
/// versions the comparison falls back to a plain structural comparison (no alias normalisation).
/// </para>
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "EF1001:Internal EF Core API usage.", Justification = "Needed")]
internal sealed class NormalizedSelectExpressionComparer : IEqualityComparer<SelectExpression>
{
    public static readonly NormalizedSelectExpressionComparer Instance = new();

    private NormalizedSelectExpressionComparer() { }

    /// <inheritdoc/>
    public bool Equals(SelectExpression? x, SelectExpression? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;
#if !NET8_0 && !NET9_0
        return ExpressionEqualityComparer.Instance.Equals(Normalize(x), Normalize(y));
#else
        return ExpressionEqualityComparer.Instance.Equals(x, y);
#endif
    }

    /// <inheritdoc/>
    public int GetHashCode(SelectExpression obj)
    {
#if !NET8_0 && !NET9_0
        return ExpressionEqualityComparer.Instance.GetHashCode(Normalize(obj));
#else
        return ExpressionEqualityComparer.Instance.GetHashCode(obj);
#endif
    }

#if !NET8_0 && !NET9_0
    /// <summary>
    /// Returns a copy of <paramref name="select"/> where every table alias has been replaced
    /// by a canonical name (<c>a0</c>, <c>a1</c>, …) assigned in depth-first traversal order.
    /// </summary>
    private static SelectExpression Normalize(SelectExpression select)
    {
        var collector = new AliasCollector();
        collector.Visit(select);

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var counter = 0;
        foreach (var alias in collector.AliasesInOrder)
            map.TryAdd(alias, $"a{counter++}");

        return (SelectExpression)new AliasNormalizer(map).Visit(select);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Collects every table-alias string encountered while traversing a
    /// <see cref="SelectExpression"/> tree, preserving depth-first insertion order.
    /// </summary>
    private sealed class AliasCollector : ExpressionVisitor
    {
        private readonly LinkedList<string> _aliases = new();
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

        public IEnumerable<string> AliasesInOrder => _aliases;

        protected override Expression VisitExtension(Expression node)
        {
            if (node is TableExpressionBase table && table.Alias is { } alias)
                AddAlias(alias);

            return base.VisitExtension(node);
        }

        private void AddAlias(string alias)
        {
            if (_seen.Add(alias))
                _aliases.AddLast(alias);
        }
    }

    /// <summary>
    /// Rewrites a <see cref="SelectExpression"/> tree, replacing every table alias with the
    /// canonical name found in <paramref name="aliasMap"/>.
    /// </summary>
    private sealed class AliasNormalizer(IReadOnlyDictionary<string, string> aliasMap) : ExpressionVisitor
    {
        protected override Expression VisitExtension(Expression node)
        {
            switch (node)
            {
                case ColumnExpression col when aliasMap.TryGetValue(col.TableAlias, out var newTableAlias):
                    return new ColumnExpression(col.Name, newTableAlias, col.Type, col.TypeMapping!, col.IsNullable);

                case TableExpressionBase table when table.Alias is { } alias && aliasMap.TryGetValue(alias, out var newAlias):
                    var visited = (TableExpressionBase)base.VisitExtension(table);
                    return visited.WithAlias(newAlias);

                default:
                    return base.VisitExtension(node);
            }
        }
    }
#endif
}
