using System.Linq.Expressions;
using EntityFrameworkCore.Projectables.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Projectables.Query;

/// <summary>
/// A decorator <see cref="IQueryTranslationPostprocessorFactory"/> that wraps the provider's own
/// factory and inserts a <see cref="VariableWrapQueryTranslationPostprocessor"/> step.
/// <para>
/// This postprocessor runs <em>before</em> the relational nullability processor so that
/// <see cref="VariableWrapSqlExpression"/> nodes are hoisted into <c>CROSS APPLY</c> /
/// <c>CROSS JOIN LATERAL</c> subqueries before the translation-time nullability processor
/// encounters them.  Multi-use wraps are replaced with <see cref="ColumnExpression"/>
/// references; single-use wraps are lowered to their inner expression.
/// </para>
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "EF1001:Internal EF Core API usage.", Justification = "Needed")]
internal sealed class VariableWrapQueryTranslationPostprocessorFactory(
    IQueryTranslationPostprocessorFactory inner,
    QueryTranslationPostprocessorDependencies dependencies)
    : IQueryTranslationPostprocessorFactory
{
    /// <inheritdoc/>
    public QueryTranslationPostprocessor Create(QueryCompilationContext queryCompilationContext)
    {
        var innerPostprocessor = inner.Create(queryCompilationContext);
        return new VariableWrapQueryTranslationPostprocessor(dependencies, queryCompilationContext, innerPostprocessor);
    }
}

/// <summary>
/// Processes <see cref="VariableWrapSqlExpression"/> nodes in the SQL expression tree before
/// delegating to the inner postprocessor.  Multi-use wraps become <c>CROSS APPLY</c> /
/// <c>CROSS JOIN LATERAL</c> inline subquery table sources.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "EF1001:Internal EF Core API usage.", Justification = "Needed")]
internal sealed class VariableWrapQueryTranslationPostprocessor(
    QueryTranslationPostprocessorDependencies dependencies,
    QueryCompilationContext queryCompilationContext,
    QueryTranslationPostprocessor inner)
    : QueryTranslationPostprocessor(dependencies, queryCompilationContext)
{
    /// <inheritdoc/>
    public override Expression Process(Expression query)
    {
        // Transform Variable.Wrap nodes BEFORE the provider's postprocessor runs the
        // translation-time SqlNullabilityProcessor.  The SQL Server provider overrides
        // VisitCustomSqlExpression to throw on unknown nodes, so both
        // VariableWrapSqlExpression (SqlExpression) and InlineSubqueryExpression
        // (TableExpressionBase) must be gone before that processor runs.
        //
        // For EF Core 8/9, a second nullability pass runs at execution time inside
        // RelationalParameterBasedSqlProcessor.Optimize.  ProjectablesParameterBasedSqlProcessorFactory
        // (registered in the DI container for EF 8/9) intercepts that pass and temporarily
        // hides InlineSubqueryExpression tables so they never reach the processor.
        query = TransformVariableWraps(query);
        return inner.Process(query);
    }

    private static Expression TransformVariableWraps(Expression query)
    {
        if (query is not ShapedQueryExpression shaped
            || shaped.QueryExpression is not SelectExpression selectExpression)
            return query;

        var transformed = ProjectablesQuerySqlGenerator.TransformVariableWrapsOnSelectExpression(selectExpression);
        return ReferenceEquals(transformed, selectExpression)
            ? query
            : shaped.UpdateQueryExpression(transformed);
    }
}
