using EntityFrameworkCore.Projectables.FunctionalTests.Helpers;
using Microsoft.EntityFrameworkCore;
using VerifyXunit;
using Xunit;

namespace EntityFrameworkCore.Projectables.FunctionalTests;

/// <summary>
/// Verifies that the <c>CteAwareQuerySqlGenerator</c> emits a proper <c>WITH … AS (…)</c>
/// preamble when the same filtered base query is used as a table source more than once in a
/// single SQL statement.
///
/// <para>
/// EF Core assigns fresh table aliases (e.g. <c>[e]</c>, <c>[e0]</c>) to every logical copy of
/// a sub-expression, so two occurrences of the same LINQ query compile to structurally equivalent
/// but alias-different <see cref="Microsoft.EntityFrameworkCore.Query.SqlExpressions.SelectExpression"/>
/// nodes.  The <c>CteDeduplicatingRewriter</c> uses
/// <c>NormalizedSelectExpressionComparer</c> — which ignores alias names — to detect these
/// duplicates and hoists them into a single <c>WITH</c> clause.
/// </para>
/// </summary>
[UsesVerify]
public class CteTests
{
    public record Entity
    {
        public int Id { get; set; }
        public string? Name { get; set; }

        [Projectable]
        public bool IsWithinRange(int min, int max) => Id >= min && Id <= max;

        [Projectable]
        public bool IsActive => Id % 2 == 0;
    }

    /// <summary>
    /// <c>subset.Concat(subset)</c> translates to <c>UNION ALL</c>.
    /// Both halves share the same filtered <see cref="Microsoft.EntityFrameworkCore.Query.SqlExpressions.SelectExpression"/>,
    /// so the <c>CteDeduplicatingRewriter</c> should extract it into a single <c>WITH</c> clause.
    /// </summary>
    [Fact]
    public Task DuplicateSubquery_ViaConcat_IsExtractedToCte()
    {
        using var dbContext = new SampleDbContext<Entity>();

        var subset = dbContext.Set<Entity>()
            .Where(x => x.IsWithinRange(1, 5));

        var query = subset.Concat(subset);

        return Verifier.Verify(query.ToQueryString());
    }

    /// <summary>
    /// <c>subset.Union(subset)</c> translates to <c>UNION</c> (distinct).
    /// Both halves share the same filtered <see cref="Microsoft.EntityFrameworkCore.Query.SqlExpressions.SelectExpression"/>,
    /// so the <c>CteDeduplicatingRewriter</c> should extract it into a single <c>WITH</c> clause.
    /// </summary>
    [Fact]
    public Task DuplicateSubquery_ViaUnion_IsExtractedToCte()
    {
        using var dbContext = new SampleDbContext<Entity>();

        var subset = dbContext.Set<Entity>()
            .Where(x => x.IsActive);

        var query = subset.Union(subset);

        return Verifier.Verify(query.ToQueryString());
    }

    /// <summary>
    /// Self-join with the same filtered query on both sides.
    /// The two filtered table sources for the join are structurally identical, so the
    /// <c>CteDeduplicatingRewriter</c> should detect the duplicate and emit a <c>WITH</c> clause.
    /// </summary>
    [Fact]
    public Task DuplicateSubquery_ViaSelfJoin_IsExtractedToCte()
    {
        using var dbContext = new SampleDbContext<Entity>();

        var subset = dbContext.Set<Entity>()
            .Where(x => x.IsWithinRange(1, 10));

        var query = subset.Join(
            subset,
            outer => outer.Id,
            inner => inner.Id + 1,
            (outer, inner) => new { OuterId = outer.Id, InnerName = inner.Name });

        return Verifier.Verify(query.ToQueryString());
    }
}
