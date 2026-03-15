using System.Linq.Expressions;
using EntityFrameworkCore.Projectables.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Projectables.Query;

/// <summary>
/// A <see cref="QuerySqlGenerator"/> subclass that supports <see cref="CteTableExpression"/> nodes
/// and generates <c>CROSS APPLY</c> clauses for reused local variables.
/// <para>
/// Before generating the main <c>SELECT</c>, it:
/// <list type="number">
///   <item>On .NET 10 (EF Core 10): Rewrites multi-use <see cref="VariableWrapSqlExpression"/>
///         nodes into a <c>CROSS APPLY (SELECT … AS [name]) AS [alias]</c> table source, so
///         that the expression is computed exactly once per row.</item>
///   <item>Runs <see cref="CteDeduplicatingRewriter"/> to detect duplicate <see cref="SelectExpression"/>
///         subtrees and replaces them with <see cref="CteTableExpression"/> references.</item>
///   <item>Emits a <c>WITH cteName AS (…)</c> preamble for each collected CTE, in depth-first order.</item>
/// </list>
/// </para>
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "EF1001:Internal EF Core API usage.", Justification = "Needed")]
public class CteAwareQuerySqlGenerator : QuerySqlGenerator
{
    /// <inheritdoc/>
    public CteAwareQuerySqlGenerator(QuerySqlGeneratorDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <inheritdoc/>
    protected override void GenerateRootCommand(Expression queryExpression)
    {
#if !NET8_0 && !NET9_0
        if (queryExpression is SelectExpression selectExpr)
            queryExpression = TransformVariableWrapsOnSelectExpression(selectExpr);
#endif

        var rewriter = new CteDeduplicatingRewriter();
        var rewritten = rewriter.Rewrite(queryExpression);
        var ctes = rewriter.CollectedCtes;

        if (ctes.Count == 0)
        {
            // No CTEs found — fall through to base behaviour using the original expression
            // (avoids any observable difference in SQL formatting caused by a no-op tree visit).
            base.GenerateRootCommand(queryExpression);
            return;
        }

        Sql.Append("WITH ");

        for (var i = 0; i < ctes.Count; i++)
        {
            if (i > 0)
            {
                Sql.AppendLine(",");
            }

            var cte = ctes[i];
            Sql.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(cte.CteName));
            Sql.AppendLine(" AS (");
            using (Sql.Indent())
            {
                Visit(cte.Inner);
            }

            Sql.AppendLine();
            Sql.Append(")");
        }

        Sql.AppendLine();
        base.GenerateRootCommand(rewritten);
    }

    /// <inheritdoc/>
    protected override Expression VisitExtension(Expression expression)
    {
        switch (expression)
        {
            case CteTableExpression cteTable:
                return VisitCteTable(cteTable);

            case VariableWrapSqlExpression wrap:
                // Fall-through: emit the inner expression directly.
                // This handles single-use wraps (not transformed to CROSS APPLY) and any
                // platform where the CROSS APPLY transformation is not applied.
                return Visit(wrap.Inner);

#if !NET8_0 && !NET9_0
            case VariableWrapCrossApplyExpression crossApply:
                return VisitVariableWrapCrossApply(crossApply);
#endif

            default:
                return base.VisitExtension(expression);
        }
    }

    /// <summary>
    /// Emits the CTE reference as a bare name with alias separator (e.g., <c>[cte_0] AS [cte_0]</c>).
    /// The CTE body is already emitted in the <c>WITH</c> preamble.
    /// </summary>
    private Expression VisitCteTable(CteTableExpression cteTable)
    {
        Sql.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(cteTable.CteName));
        Sql.Append(AliasSeparator);
        Sql.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(cteTable.Alias!));
        return cteTable;
    }

#if !NET8_0 && !NET9_0
    /// <summary>
    /// Emits the CROSS APPLY subquery body, e.g.
    /// <c>(SELECT [e].[Bar] * 2 AS [temp]) AS [_cv0]</c>.
    /// </summary>
    private Expression VisitVariableWrapCrossApply(VariableWrapCrossApplyExpression crossApply)
    {
        Sql.AppendLine("(");
        using (Sql.Indent())
        {
            Sql.Append("SELECT ");
            Visit(crossApply.Inner);
            Sql.Append(AliasSeparator);
            Sql.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(crossApply.ColumnName));
        }

        Sql.AppendLine();
        Sql.Append(")");
        Sql.Append(AliasSeparator);
        Sql.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(crossApply.Alias!));
        return crossApply;
    }

    /// <summary>
    /// Rewrites any <see cref="VariableWrapSqlExpression"/> nodes that appear more than once
    /// in the root <see cref="SelectExpression"/> into <c>CROSS APPLY</c> table sources.
    /// Single-use nodes are lowered to their inner expression.
    /// </summary>
    /// <remarks>
    /// This is called from both <see cref="CteAwareQuerySqlGenerator.GenerateRootCommand"/> and
    /// from the postprocessor (<see cref="VariableWrapQueryTranslationPostprocessor"/>) so that
    /// the transformation runs before <c>SqlNullabilityProcessor</c> sees the expression tree.
    /// </remarks>
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

        // Build the column mapping: variableName → ColumnExpression referencing the CROSS APPLY.
        var columnMapping = new Dictionary<string, ColumnExpression>(StringComparer.Ordinal);
        var crossApplyTables = new List<CrossApplyExpression>();
        var counter = 0;

        foreach (var (name, wraps) in multiUse)
        {
            var tableAlias = $"w{counter++}";
            var first = wraps[0];
            var body = new VariableWrapCrossApplyExpression(tableAlias, first.Inner, name);
            crossApplyTables.Add(new CrossApplyExpression(body));
            columnMapping[name] = new ColumnExpression(name, tableAlias, first.Type, first.TypeMapping!, false);
        }

        // Rewrite: replace VariableWrapSqlExpression with the column references.
        var replacer = new VariableWrapReplacer(columnMapping);
        var rewritten = (SelectExpression)replacer.Visit(rootSelect);

        // Append the new CROSS APPLY table sources.
        rewritten.SetTables([.. rewritten.Tables, .. crossApplyTables]);

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
    /// since the CROSS APPLY is only added at the root level.
    /// </summary>
    private sealed class VariableWrapReplacer(IReadOnlyDictionary<string, ColumnExpression> mapping) : ExpressionVisitor
    {
        // 0 = not yet inside any SelectExpression; 1 = in the root SELECT (replace here);
        // 2+ = in a nested SelectExpression (leave Variable.Wrap untouched — it belongs to a
        //      different scope and would have its own CROSS APPLY if it were at the root).
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
