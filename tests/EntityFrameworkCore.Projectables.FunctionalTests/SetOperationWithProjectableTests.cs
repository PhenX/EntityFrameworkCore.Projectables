using EntityFrameworkCore.Projectables.FunctionalTests.Helpers;
using Microsoft.EntityFrameworkCore;
using VerifyXunit;
using Xunit;

namespace EntityFrameworkCore.Projectables.FunctionalTests;

/// <summary>
/// Verifies that projectable properties and methods work correctly when the same queryable is
/// used as a table source more than once in a single SQL statement (union, concat, self-join).
/// </summary>
[UsesVerify]
public class SetOperationWithProjectableTests
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
    /// Both halves use the same filtered projectable condition.
    /// </summary>
    [Fact]
    public Task ProjectableInConcat_BothSidesUseProjectable()
    {
        using var dbContext = new SampleDbContext<Entity>();

        var subset = dbContext.Set<Entity>()
            .Where(x => x.IsWithinRange(1, 5));

        var query = subset.Concat(subset);

        return Verifier.Verify(query.ToQueryString());
    }

    /// <summary>
    /// <c>subset.Union(subset)</c> translates to <c>UNION</c> (distinct).
    /// Both halves use the same filtered projectable property.
    /// </summary>
    [Fact]
    public Task ProjectableInUnion_BothSidesUseProjectable()
    {
        using var dbContext = new SampleDbContext<Entity>();

        var subset = dbContext.Set<Entity>()
            .Where(x => x.IsActive);

        var query = subset.Union(subset);

        return Verifier.Verify(query.ToQueryString());
    }

    /// <summary>
    /// Self-join with the same filtered query on both sides where both sides use a projectable.
    /// </summary>
    [Fact]
    public Task ProjectableInSelfJoin_BothSidesUseProjectable()
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
