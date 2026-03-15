using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using EntityFrameworkCore.Projectables.Query.SqlExpressions;

namespace EntityFrameworkCore.Projectables.Query;

/// <summary>
/// An <see cref="ExpressionVisitor"/> that rewrites duplicate <see cref="SelectExpression"/>
/// subtrees (used as table sources in <c>FROM</c> clauses) into <see cref="CteTableExpression"/>
/// references, so that the SQL generator can emit a single <c>WITH … AS (…)</c> clause instead
/// of repeating the body.
/// <para>
/// <b>Scope:</b> Only <see cref="SelectExpression"/> nodes that appear as table sources are
/// considered for CTE factoring. Scalar subqueries (those wrapped in a
/// <see cref="ScalarSubqueryExpression"/>) are intentionally left untouched because they have a
/// different SQL shape and cannot be substituted with a plain table reference.
/// </para>
/// <para>
/// <b>Alias-neutral equality:</b> EF Core assigns fresh aliases (e.g. <c>e</c>, <c>e0</c>) to
/// each occurrence of a sub-expression, so two logically identical <see cref="SelectExpression"/>
/// trees typically differ only in their alias names.  This rewriter therefore uses
/// <see cref="NormalizedSelectExpressionComparer"/> instead of
/// <see cref="ExpressionEqualityComparer"/> to detect structural equivalence regardless of alias
/// names.
/// </para>
/// <para>
/// Two passes are performed:
/// <list type="number">
///   <item>Count occurrences of every table-source <see cref="SelectExpression"/> subtree using
///         alias-neutral structural equality.</item>
///   <item>Replace all occurrences of any subtree that appears more than once with a
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
        = new(NormalizedSelectExpressionComparer.Instance);

    private readonly Dictionary<SelectExpression, CteTableExpression> _cteMap
        = new(NormalizedSelectExpressionComparer.Instance);

    private int _cteCounter;
    private bool _isCounting = true;

    // When > 0 we are inside a ScalarSubqueryExpression and must not replace the inner
    // SelectExpression with a CteTableExpression (the types are incompatible).
    private int _scalarSubqueryDepth;

    /// <summary>
    /// CTEs collected during the rewrite pass, in depth-first order so nested
    /// CTEs appear before their consumers.
    /// </summary>
    public IReadOnlyList<CteTableExpression> CollectedCtes => [.. _cteMap.Values];

    /// <summary>
    /// Rewrites the given <paramref name="expression"/>, replacing duplicate table-source
    /// <see cref="SelectExpression"/> subtrees with <see cref="CteTableExpression"/> references.
    /// </summary>
    public Expression Rewrite(Expression expression)
    {
        // Pass 1: count occurrences
        _isCounting = true;
        _scalarSubqueryDepth = 0;
        Visit(expression);

        // Pass 2: replace duplicates with CTE references
        _isCounting = false;
        _scalarSubqueryDepth = 0;
        return Visit(expression);
    }

    /// <inheritdoc/>
    protected override Expression VisitExtension(Expression node)
    {
        if (node is ScalarSubqueryExpression scalarSubquery)
        {
            return VisitScalarSubqueryExpression(scalarSubquery);
        }

        if (node is SelectExpression select && _scalarSubqueryDepth == 0)
        {
            return VisitTableSourceSelectExpression(select);
        }

        return base.VisitExtension(node);
    }

    /// <summary>
    /// Visits a <see cref="ScalarSubqueryExpression"/>, tracking depth so that the inner
    /// <see cref="SelectExpression"/> is not replaced with a <see cref="CteTableExpression"/>.
    /// </summary>
    private Expression VisitScalarSubqueryExpression(ScalarSubqueryExpression scalarSubquery)
    {
        _scalarSubqueryDepth++;
        try
        {
            return base.VisitExtension(scalarSubquery);
        }
        finally
        {
            _scalarSubqueryDepth--;
        }
    }

    /// <summary>
    /// Handles a <see cref="SelectExpression"/> that appears as a table source (not inside a
    /// scalar subquery).
    /// </summary>
    private Expression VisitTableSourceSelectExpression(SelectExpression select)
    {
        if (_isCounting)
        {
            // Count this subtree before visiting children so we count the original form.
            _occurrenceCount[select] = _occurrenceCount.TryGetValue(select, out var existing)
                ? existing + 1
                : 1;

            // Visit children to count nested SelectExpressions.
            base.VisitExtension(select);
            return select;
        }

        // Rewrite pass: check whether this subtree appears more than once.
        if (_occurrenceCount.TryGetValue(select, out var count) && count > 1)
        {
            if (!_cteMap.TryGetValue(select, out var cte))
            {
                // First time we see this duplicate — visit children first (depth-first) so that
                // nested CTEs appear before their consumers in CollectedCtes.
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
