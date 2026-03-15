using System.Linq.Expressions;
using EntityFrameworkCore.Projectables.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Projectables.Query;

/// <summary>
/// A <see cref="QuerySqlGenerator"/> subclass that emits <c>CROSS APPLY</c> (SQL Server) or
/// <c>CROSS JOIN LATERAL</c> (PostgreSQL) subquery clauses for local variables declared inside
/// block-bodied <c>[Projectable]</c> methods that are referenced more than once.
/// <para>
/// When the source generator detects that a local variable is used more than once, it wraps
/// every occurrence in a call to <see cref="Variable.Wrap{T}(string,T)"/>.  EF Core's method
/// translator converts those calls to <see cref="VariableWrapSqlExpression"/> nodes.  This
/// generator then hoists each multi-use variable into an inline subquery so that its expression
/// is evaluated exactly once per row.
/// </para>
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "EF1001:Internal EF Core API usage.", Justification = "Needed")]
public class ProjectablesQuerySqlGenerator : QuerySqlGenerator
{
    /// <inheritdoc/>
    public ProjectablesQuerySqlGenerator(QuerySqlGeneratorDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <inheritdoc/>
    protected override Expression VisitExtension(Expression expression)
    {
        switch (expression)
        {
            case VariableWrapSqlExpression wrap:
                // Single-use wrap or unrecognised platform: emit the inner expression directly.
                return Visit(wrap.Inner);

#if !NET8_0 && !NET9_0
            case InlineSubqueryExpression inlineSub:
                return VisitInlineSubquery(inlineSub);
#endif

            default:
                return base.VisitExtension(expression);
        }
    }

#if !NET8_0 && !NET9_0
    /// <summary>
    /// Returns <see langword="true"/> if this generator is targeting PostgreSQL, which uses
    /// <c>CROSS JOIN LATERAL</c> instead of SQL Server's <c>CROSS APPLY</c>.
    /// Detection is based on the SQL identifier delimiter: SQL Server uses <c>[…]</c>,
    /// PostgreSQL uses <c>"…"</c>.
    /// </summary>
    protected virtual bool IsPostgres
        => Dependencies.SqlGenerationHelper.DelimitIdentifier("x").StartsWith('"');

    /// <summary>
    /// Emits the inline subquery that materialises a reused local variable exactly once per row.
    /// The JOIN keyword itself (<c>CROSS APPLY</c> vs <c>CROSS JOIN LATERAL</c>) is controlled
    /// by <see cref="VisitCrossApply"/>.
    /// </summary>
    private Expression VisitInlineSubquery(InlineSubqueryExpression inlineSub)
    {
        Sql.AppendLine("(");
        using (Sql.Indent())
        {
            Sql.Append("SELECT ");
            Visit(inlineSub.Inner);
            Sql.Append(AliasSeparator);
            Sql.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(inlineSub.ColumnName));
        }

        Sql.AppendLine();
        Sql.Append(")");
        Sql.Append(AliasSeparator);
        Sql.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(inlineSub.Alias!));
        return inlineSub;
    }

    /// <summary>
    /// Overrides cross-apply emission to use <c>CROSS JOIN LATERAL</c> when targeting
    /// PostgreSQL (or any provider that uses double-quote identifiers) for <see cref="InlineSubqueryExpression"/>
    /// tables; delegates to the base implementation for all other table types.
    /// </summary>
    protected override Expression VisitCrossApply(CrossApplyExpression crossApplyExpression)
    {
        if (IsPostgres && crossApplyExpression.Table is InlineSubqueryExpression inlineSub)
        {
            Sql.Append("CROSS JOIN LATERAL ");
            return VisitInlineSubquery(inlineSub);
        }

        return base.VisitCrossApply(crossApplyExpression);
    }

    /// <summary>
    /// Rewrites any <see cref="VariableWrapSqlExpression"/> nodes that appear more than once
    /// in the root <see cref="SelectExpression"/> into inline subquery table sources.
    /// Single-use nodes are lowered to their inner expression.
    /// </summary>
    internal static SelectExpression TransformVariableWrapsOnSelectExpression(SelectExpression rootSelect)
    {
        // Scan the entire expression for VariableWrap occurrences, grouped by variable name.
        var scanner = new VariableWrapScanner();
        scanner.Visit(rootSelect);

        // Only process names that appear more than once (single-use wraps are emitted inline).
        var multiUse = scanner.Groups
            .Where(kv => kv.Value.Count > 1)
            .ToList();

        if (multiUse.Count == 0)
            return rootSelect;

        // Build column mapping: variableName → ColumnExpression referencing the subquery table.
        var columnMapping = new Dictionary<string, ColumnExpression>(StringComparer.Ordinal);
        var inlineSubqueryTables = new List<CrossApplyExpression>();
        var counter = 0;

        foreach (var (variableName, wraps) in multiUse)
        {
            // Use "v" (for "variable") as the table-alias prefix to satisfy EF Core's
            // alias normalization (letter + optional counter: "v", "v0", "v1", …).
            // The original variable name is used as the column name inside the subquery so
            // the SQL still shows the meaningful name (e.g. SELECT … AS [doubled]).
            var tableAlias = counter == 0 ? "v" : $"v{counter}";
            counter++;
            var first = wraps[0];
            var subquery = new InlineSubqueryExpression(tableAlias, first.Inner, variableName);
            inlineSubqueryTables.Add(new CrossApplyExpression(subquery));
            columnMapping[variableName] = new ColumnExpression(variableName, tableAlias, first.Type, first.TypeMapping!, false);
        }

        // Rewrite: replace VariableWrapSqlExpression with the column references.
        var replacer = new VariableWrapReplacer(columnMapping);
        var rewritten = (SelectExpression)replacer.Visit(rootSelect);

        // Append the new subquery table sources.
        rewritten.SetTables([.. rewritten.Tables, .. inlineSubqueryTables]);

        return rewritten;
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Scans a SQL expression tree for <see cref="VariableWrapSqlExpression"/> nodes,
    /// collecting them grouped by <see cref="VariableWrapSqlExpression.VariableName"/>.
    /// </summary>
    private sealed class VariableWrapScanner : ExpressionVisitor
    {
        public Dictionary<string, List<VariableWrapSqlExpression>> Groups { get; } = new(StringComparer.Ordinal);

        protected override Expression VisitExtension(Expression node)
        {
            if (node is VariableWrapSqlExpression wrap)
            {
                if (!Groups.TryGetValue(wrap.VariableName, out var list))
                    Groups[wrap.VariableName] = list = [];
                list.Add(wrap);
                // Don't recurse into the inner — we only care about top-level occurrences.
                return node;
            }

            return base.VisitExtension(node);
        }
    }

    /// <summary>
    /// Replaces <see cref="VariableWrapSqlExpression"/> nodes with
    /// <see cref="ColumnExpression"/> references from the provided mapping.
    /// Nodes whose name is not in the mapping (single-use) are lowered to the inner expression.
    /// Does not recurse into nested <see cref="SelectExpression"/> nodes that are table sources,
    /// since the inline subquery is only added at the root level.
    /// </summary>
    private sealed class VariableWrapReplacer(IReadOnlyDictionary<string, ColumnExpression> mapping) : ExpressionVisitor
    {
        // 0 = not yet inside any SelectExpression; 1 = in the root SELECT (replace here);
        // 2+ = in a nested SelectExpression (leave Variable.Wrap untouched — it belongs to a
        //      different scope and would have its own subquery if it were at the root).
        private int _depth;

        protected override Expression VisitExtension(Expression node)
        {
            if (node is VariableWrapSqlExpression wrap && _depth == 1)
            {
                if (mapping.TryGetValue(wrap.VariableName, out var col))
                    return col;
                // Single-use: strip to inner expression.
                return Visit(wrap.Inner);
            }

            if (node is SelectExpression)
            {
                if (_depth >= 1)
                    return node; // Nested SelectExpression — do not recurse.

                _depth++;
                try { return base.VisitExtension(node); }
                finally { _depth--; }
            }

            return base.VisitExtension(node);
        }
    }
#endif
}
