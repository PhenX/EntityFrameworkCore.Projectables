using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Projectables.Query;

/// <summary>
/// An <see cref="IQuerySqlGeneratorFactory"/> implementation that creates
/// <see cref="CteAwareQuerySqlGenerator"/> instances, enabling CTE-based SQL deduplication
/// for local variables that are referenced more than once in projectable block-bodied methods.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "EF1001:Internal EF Core API usage.", Justification = "Needed")]
public sealed class CteAwareQuerySqlGeneratorFactory : IQuerySqlGeneratorFactory
{
    private readonly QuerySqlGeneratorDependencies _dependencies;

    /// <inheritdoc cref="CteAwareQuerySqlGeneratorFactory"/>
    public CteAwareQuerySqlGeneratorFactory(QuerySqlGeneratorDependencies dependencies)
        => _dependencies = dependencies;

    /// <inheritdoc/>
    public QuerySqlGenerator Create() => new CteAwareQuerySqlGenerator(_dependencies);
}
