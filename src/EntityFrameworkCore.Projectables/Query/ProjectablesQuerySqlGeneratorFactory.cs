using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Projectables.Query;

/// <summary>
/// An <see cref="IQuerySqlGeneratorFactory"/> that produces
/// <see cref="ProjectablesQuerySqlGenerator"/> instances, which add <c>CROSS APPLY</c> /
/// <c>CROSS JOIN LATERAL</c> subquery support for reused local variables in block-bodied
/// <c>[Projectable]</c> methods.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "EF1001:Internal EF Core API usage.", Justification = "Needed")]
public class ProjectablesQuerySqlGeneratorFactory(QuerySqlGeneratorDependencies dependencies)
    : IQuerySqlGeneratorFactory
{
    /// <inheritdoc/>
    public QuerySqlGenerator Create()
        => new ProjectablesQuerySqlGenerator(dependencies);
}
