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
/// <see cref="VariableWrapSqlExpression"/> nodes — which EF Core's nullability processor does
/// not understand — are either replaced by <c>CROSS APPLY</c> table sources (on .NET 10 / EF
/// Core 10) or lowered to their inner expressions (on earlier versions) before they can cause
/// an exception.
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
/// delegating to the inner postprocessor.
/// <list type="bullet">
///   <item>On .NET 10 / EF Core 10: Replaces multi-use <see cref="VariableWrapSqlExpression"/>
///         groups with <c>CROSS APPLY (SELECT … AS [name]) AS [alias]</c> table sources
///         and <see cref="ColumnExpression"/> references.</item>
///   <item>On earlier versions: Lowers every <see cref="VariableWrapSqlExpression"/> to its
///         plain inner expression (identity semantics).</item>
/// </list>
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
        // Apply Variable.Wrap transformation before the provider's postprocessor (which
        // eventually runs SqlNullabilityProcessor — an EF Core internal processor that throws
        // on unknown custom SQL expressions).
        query = TransformVariableWraps(query);
        return inner.Process(query);
    }

    private static Expression TransformVariableWraps(Expression query)
    {
        if (query is not ShapedQueryExpression shaped
            || shaped.QueryExpression is not SelectExpression selectExpression)
            return query;

#if !NET8_0 && !NET9_0
        var transformed = CteAwareQuerySqlGenerator.TransformVariableWrapsOnSelectExpression(selectExpression);
        return ReferenceEquals(transformed, selectExpression)
            ? query
            : shaped.UpdateQueryExpression(transformed);
#else
        // Lower Variable.Wrap → inner expression so the nullability processor is unaffected.
        var stripped = (SelectExpression)new VariableWrapStripper().Visit(selectExpression);
        return ReferenceEquals(stripped, selectExpression) ? query : shaped.UpdateQueryExpression(stripped);
#endif
    }

#if NET8_0 || NET9_0
    /// <summary>Removes <see cref="VariableWrapSqlExpression"/> by replacing each with its inner expression.</summary>
    private sealed class VariableWrapStripper : ExpressionVisitor
    {
        protected override Expression VisitExtension(Expression node)
            => node is VariableWrapSqlExpression wrap
                ? Visit(wrap.Inner)
                : base.VisitExtension(node);
    }
#endif
}
