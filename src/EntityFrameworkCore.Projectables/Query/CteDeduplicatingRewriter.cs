using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using EntityFrameworkCore.Projectables.Query.SqlExpressions;

namespace EntityFrameworkCore.Projectables.Query;

/// <summary>
/// An <see cref="ExpressionVisitor"/> that rewrites duplicate <see cref="SelectExpression"/>
/// subtrees into <see cref="CteTableExpression"/> references, so that the SQL generator can
/// emit a single <c>WITH … AS (…)</c> clause instead of repeating the body.
/// <para>
/// Two passes are performed:
/// <list type="number">
///   <item>Count occurrences of every <see cref="SelectExpression"/> subtree by structural equality.</item>
///   <item>Replace all but the first occurrence of any subtree that appears more than once with a
///         <see cref="CteTableExpression"/> and add the defining expression to
///         <see cref="CollectedCtes"/> in depth-first order so that dependent CTEs are emitted
///         before their consumers.</item>
/// </list>
/// </para>
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "EF1001:Internal EF Core API usage.", Justification = "Needed")]
public sealed class CteDeduplicatingRewriter : ExpressionVisitor
{
    private readonly Dictionary<SelectExpression, int> _occurrenceCount
        = new(ExpressionEqualityComparer.Instance);

    private readonly Dictionary<SelectExpression, CteTableExpression> _cteMap
        = new(ExpressionEqualityComparer.Instance);

    private int _cteCounter;
    private bool _isCounting = true;

    /// <summary>
    /// CTEs collected during the rewrite pass, in depth-first order so nested
    /// CTEs appear before their consumers.
    /// </summary>
    public IReadOnlyList<CteTableExpression> CollectedCtes => [.. _cteMap.Values];

    /// <summary>
    /// Rewrites the given <paramref name="expression"/>, replacing duplicate
    /// <see cref="SelectExpression"/> subtrees with <see cref="CteTableExpression"/> references.
    /// </summary>
    public Expression Rewrite(Expression expression)
    {
        // Pass 1: count occurrences
        _isCounting = true;
        Visit(expression);

        // Pass 2: replace duplicates with CTE references
        _isCounting = false;
        return Visit(expression);
    }

    /// <inheritdoc/>
    protected override Expression VisitExtension(Expression node)
    {
        if (node is SelectExpression select)
        {
            return VisitSelectExpression(select);
        }

        return base.VisitExtension(node);
    }

    private Expression VisitSelectExpression(SelectExpression select)
    {
        if (_isCounting)
        {
            // Count this subtree (before visiting children so we count the original, un-rewritten form)
            _occurrenceCount[select] = _occurrenceCount.TryGetValue(select, out var existing)
                ? existing + 1
                : 1;

            // Still visit children to count nested SelectExpressions
            base.VisitExtension(select);
            return select;
        }

        // Rewrite pass: check if this subtree appears more than once
        if (_occurrenceCount.TryGetValue(select, out var count) && count > 1)
        {
            if (!_cteMap.TryGetValue(select, out var cte))
            {
                // First time we see this duplicate — visit children first (depth-first)
                var rewrittenInner = (SelectExpression)base.VisitExtension(select);

                var cteName = $"cte_{_cteCounter++}";
                cte = new CteTableExpression(cteName, rewrittenInner);
                _cteMap[select] = cte;
            }

            return cte;
        }

        return base.VisitExtension(select);
    }
}
