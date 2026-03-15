#if NET8_0 || NET9_0
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using EntityFrameworkCore.Projectables.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Projectables.Query;

/// <summary>
/// A decorator <see cref="IRelationalParameterBasedSqlProcessorFactory"/> for EF Core 8/9 that
/// ensures <see cref="InlineSubqueryExpression"/> table sources are temporarily hidden from the
/// execution-time <c>SqlNullabilityProcessor</c>.
/// <para>
/// In EF Core 8 and 9 the SQL nullability processor visits every <see cref="TableExpressionBase"/>
/// in a <see cref="SelectExpression"/> and throws on unknown subtypes.  Our
/// <see cref="InlineSubqueryExpression"/> is added to the table list during translation, so without
/// this decorator the second nullability pass (run by
/// <see cref="RelationalParameterBasedSqlProcessor.Optimize"/> at query execution time) would
/// throw.  EF Core 10 changed the nullability processor to be lenient about unknown table types,
/// so this decorator is not needed there.
/// </para>
/// <para>
/// The strategy:
/// <list type="number">
///   <item>Clone the root <see cref="SelectExpression"/> so the compiled-query cache is not
///         mutated.</item>
///   <item>Remove every <see cref="CrossApplyExpression"/> wrapping an
///         <see cref="InlineSubqueryExpression"/> from the clone's table list.</item>
///   <item>Run the inner provider's <c>Optimize</c> on the clean clone — no custom table
///         types remain, so the nullability pass succeeds.</item>
///   <item>Re-append the removed <see cref="CrossApplyExpression"/> entries to the result's
///         table list so the SQL generator can still emit the <c>CROSS APPLY</c> clauses.</item>
/// </list>
/// </para>
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "EF1001:Internal EF Core API usage.", Justification = "Needed")]
internal sealed class ProjectablesParameterBasedSqlProcessorFactory(
    IRelationalParameterBasedSqlProcessorFactory inner,
    RelationalParameterBasedSqlProcessorDependencies dependencies)
    : IRelationalParameterBasedSqlProcessorFactory
{
#if NET8_0
    /// <inheritdoc/>
    public RelationalParameterBasedSqlProcessor Create(bool useRelationalNulls)
        => new ProjectablesParameterBasedSqlProcessor(dependencies, useRelationalNulls, inner.Create(useRelationalNulls));
#else
    /// <inheritdoc/>
    public RelationalParameterBasedSqlProcessor Create(RelationalParameterBasedSqlProcessorParameters parameters)
        => new ProjectablesParameterBasedSqlProcessor(dependencies, parameters, inner.Create(parameters));
#endif
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "EF1001:Internal EF Core API usage.", Justification = "Needed")]
internal sealed class ProjectablesParameterBasedSqlProcessor(
    RelationalParameterBasedSqlProcessorDependencies dependencies,
#if NET8_0
    bool useRelationalNulls,
#else
    RelationalParameterBasedSqlProcessorParameters parameters,
#endif
    RelationalParameterBasedSqlProcessor inner)
#if NET8_0
    : RelationalParameterBasedSqlProcessor(dependencies, useRelationalNulls)
#else
    : RelationalParameterBasedSqlProcessor(dependencies, parameters)
#endif
{
    // Reflection accessor for SelectExpression._tables (private List<TableExpressionBase>).
    private static readonly FieldInfo TablesField =
        typeof(SelectExpression).GetField("_tables", BindingFlags.NonPublic | BindingFlags.Instance)!;

    /// <inheritdoc/>
    public override Expression Optimize(
        Expression queryExpression,
        IReadOnlyDictionary<string, object?> parametersValues,
        out bool canCache)
    {
        if (queryExpression is not ShapedQueryExpression { QueryExpression: SelectExpression selectExpr })
            return inner.Optimize(queryExpression, parametersValues, out canCache);

        var selectTables = (List<TableExpressionBase>)TablesField.GetValue(selectExpr)!;

        // Extract CrossApplyExpression wrappers whose inner table is an InlineSubqueryExpression.
        var inlineSubqueries = selectTables
            .OfType<CrossApplyExpression>()
            .Where(ca => ca.Table is InlineSubqueryExpression)
            .ToList();

        if (inlineSubqueries.Count == 0)
            return inner.Optimize(queryExpression, parametersValues, out canCache);

        // Temporarily remove them so the inner processor's nullability pass does not
        // encounter our custom TableExpressionBase subtype (EF 8/9 throw on unknown types).
        // We restore them afterwards so the compiled-query cache remains valid.
        foreach (var ca in inlineSubqueries)
            selectTables.Remove(ca);

        Expression result;
        try
        {
            result = inner.Optimize(queryExpression, parametersValues, out canCache);
        }
        finally
        {
            // Always restore the original SelectExpression's table list.
            foreach (var ca in inlineSubqueries)
                selectTables.Add(ca);
        }

        // If Optimize returned a different SelectExpression (e.g. due to nullability rewrites),
        // append our CROSS APPLY tables to that result as well.
        if (result is ShapedQueryExpression { QueryExpression: SelectExpression resultSelectExpr }
            && !ReferenceEquals(resultSelectExpr, selectExpr))
        {
            var resultTables = (List<TableExpressionBase>)TablesField.GetValue(resultSelectExpr)!;
            foreach (var ca in inlineSubqueries)
                resultTables.Add(ca);
        }

        return result;
    }
}
#endif
