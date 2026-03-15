using System.Linq.Expressions;
using System.Reflection;
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
                // Single-use wrap: emit the inner expression directly.
                return Visit(wrap.Inner);

            case InlineSubqueryExpression inlineSub:
                return VisitInlineSubquery(inlineSub);

            default:
                return base.VisitExtension(expression);
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> if this generator is targeting a database that uses
    /// <c>CROSS JOIN LATERAL</c> syntax (e.g. PostgreSQL) rather than SQL Server's
    /// <c>CROSS APPLY</c>.
    /// <para>
    /// Detection uses two signals in combination:
    /// <list type="bullet">
    ///   <item>The <see cref="ISqlGenerationHelper"/> is NOT from the SQL Server provider
    ///         assembly (<c>Microsoft.EntityFrameworkCore.SqlServer</c>).</item>
    ///   <item>The helper uses double-quote identifier delimiters (<c>"x"</c>), which is the
    ///         ISO-SQL standard followed by PostgreSQL, SQLite, and others.</item>
    /// </list>
    /// </para>
    /// </summary>
    protected virtual bool UsesLateralJoin
    {
        get
        {
            var assemblyName = Dependencies.SqlGenerationHelper.GetType().Assembly.GetName().Name
                               ?? string.Empty;
            // SQL Server uses CROSS APPLY; exclude it explicitly.
            if (assemblyName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
                return false;
            // Secondary heuristic: ISO-SQL providers use double-quote identifiers.
            return Dependencies.SqlGenerationHelper.DelimitIdentifier("x").StartsWith('"');
        }
    }

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
    /// PostgreSQL (or any ISO-SQL provider) for <see cref="InlineSubqueryExpression"/>
    /// tables; delegates to the base implementation for all other table types.
    /// </summary>
    protected override Expression VisitCrossApply(CrossApplyExpression crossApplyExpression)
    {
        if (UsesLateralJoin && crossApplyExpression.Table is InlineSubqueryExpression inlineSub)
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
        var inlineSubqueries = new List<InlineSubqueryExpression>();
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
            inlineSubqueries.Add(subquery);
            columnMapping[variableName] = CreateColumnExpr(rootSelect, subquery, variableName, first.Type, first.TypeMapping!);
        }

        // Rewrite: replace VariableWrapSqlExpression with the column references.
        var replacer = new VariableWrapReplacer(columnMapping);
        var rewritten = (SelectExpression)replacer.Visit(rootSelect);

        // Append the new subquery table sources (wrapped in CrossApplyExpression).
        AddInlineSubqueries(rewritten, inlineSubqueries);

        return rewritten;
    }

    // ── version-specific helpers ────────────────────────────────────────────────────────────

    // Reflection fields cached per AppDomain – access pattern depends on EF Core version.

#if NET8_0
    private static readonly FieldInfo TablesField8 =
        typeof(SelectExpression).GetField("_tables", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo TableReferencesField8 =
        typeof(SelectExpression).GetField("_tableReferences", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly Type TableReferenceExpressionType8 =
        typeof(SelectExpression).Assembly.GetType("Microsoft.EntityFrameworkCore.Query.Internal.TableReferenceExpression")!;

    private static readonly ConstructorInfo TableReferenceExpressionCtor8 =
        TableReferenceExpressionType8.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)[0];

    private static readonly MethodInfo AddTableMethod8 =
        typeof(SelectExpression).GetMethod("AddTable", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo CreateColumnExpressionMethod8 =
        typeof(SelectExpression).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .First(m => m.Name == "CreateColumnExpression" && !m.IsStatic);
#elif NET9_0
    private static readonly FieldInfo TablesField9 =
        typeof(SelectExpression).GetField("_tables", BindingFlags.NonPublic | BindingFlags.Instance)!;
#endif

    /// <summary>
    /// Creates a <see cref="ColumnExpression"/> referencing a column inside <paramref name="subquery"/>
    /// in a way that is compatible with all supported EF Core versions.
    /// </summary>
    private static ColumnExpression CreateColumnExpr(
        SelectExpression selectExpr,
        InlineSubqueryExpression subquery,
        string columnName,
        Type type,
        RelationalTypeMapping typeMapping)
    {
#if NET8_0
        // EF Core 8: ColumnExpression has no public ctor (string name, string alias, …).
        // We must use SelectExpression.CreateColumnExpression (public method) which
        // looks up the table by reference in _tables, then maps to _tableReferences.
        // The CrossApplyExpression wrapper is what gets added to _tables, so we pass
        // the wrapper (not the inner InlineSubqueryExpression) to CreateColumnExpression.
        var crossApply = new CrossApplyExpression(subquery);
        var tableRef = TableReferenceExpressionCtor8.Invoke([selectExpr, crossApply.Alias ?? subquery.Alias!]);
        AddTableMethod8.Invoke(selectExpr, [crossApply, tableRef]);
        return (ColumnExpression)CreateColumnExpressionMethod8.Invoke(selectExpr,
            [crossApply, columnName, type, typeMapping, (bool?)false])!;
#else
        // EF Core 9+: public ColumnExpression(name, tableAlias, type, typeMapping, nullable)
        return new ColumnExpression(columnName, subquery.Alias!, type, typeMapping, false);
#endif
    }

    /// <summary>
    /// Appends <see cref="CrossApplyExpression"/> wrappers for each <paramref name="subqueries"/>
    /// to the table list of <paramref name="selectExpr"/>, using the API available in the
    /// current EF Core version.
    /// </summary>
    private static void AddInlineSubqueries(
        SelectExpression selectExpr,
        IReadOnlyList<InlineSubqueryExpression> subqueries)
    {
#if NET8_0
        // Tables were already registered inside CreateColumnExpr (EF8 path uses AddTableMethod8).
        // Nothing additional to do here for EF8.
#elif NET9_0
        // EF Core 9 has no public SetTables; mutate _tables directly via the cached field.
        if (TablesField9.GetValue(selectExpr) is List<TableExpressionBase> tables9)
        {
            foreach (var sub in subqueries)
                tables9.Add(new CrossApplyExpression(sub));
        }
#else
        // EF Core 10+: SetTables is available.
        selectExpr.SetTables([.. selectExpr.Tables, .. subqueries.Select(s => new CrossApplyExpression(s))]);
#endif
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
}
