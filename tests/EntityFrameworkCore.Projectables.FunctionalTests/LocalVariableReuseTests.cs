using EntityFrameworkCore.Projectables.FunctionalTests.Helpers;
using Microsoft.EntityFrameworkCore;
using VerifyXunit;
using Xunit;

namespace EntityFrameworkCore.Projectables.FunctionalTests;

/// <summary>
/// Verifies that local variables declared in block-bodied <c>[Projectable]</c> methods are
/// hoisted into <c>CROSS APPLY (SELECT … AS [variableName]) AS [variableName]</c> inline
/// subqueries when the same variable is referenced more than once.  This ensures that complex
/// expressions are computed exactly once per row instead of being inlined multiple times.
/// </summary>
[UsesVerify]
public class LocalVariableReuseTests
{
    public record Entity
    {
        public int Id { get; set; }
        public int Value { get; set; }
        public bool IsActive { get; set; }
        public string? Name { get; set; }
        public int Score { get; set; }
    }

    // ── single reused variable ─────────────────────────────────────────────────────────

    /// <summary>
    /// A single local variable used twice generates one CROSS APPLY.
    /// </summary>
    [Fact]
    public Task SingleVariable_UsedTwice_GeneratesCrossApply()
    {
        using var dbContext = new SampleDbContext<Entity>();
        var query = dbContext.Set<Entity>().Select(x => x.DoubledTwice());
        return Verifier.Verify(query.ToQueryString());
    }

    /// <summary>
    /// A single local variable used three times still generates one CROSS APPLY.
    /// </summary>
    [Fact]
    public Task SingleVariable_UsedThreeTimes_GeneratesOneCrossApply()
    {
        using var dbContext = new SampleDbContext<Entity>();
        var query = dbContext.Set<Entity>().Select(x => x.TripleReuse());
        return Verifier.Verify(query.ToQueryString());
    }

    /// <summary>
    /// A local variable used only once is inlined — no CROSS APPLY.
    /// </summary>
    [Fact]
    public Task SingleVariable_UsedOnce_IsInlined()
    {
        using var dbContext = new SampleDbContext<Entity>();
        var query = dbContext.Set<Entity>().Select(x => x.DoubledOnce());
        return Verifier.Verify(query.ToQueryString());
    }

    // ── multiple reused variables ──────────────────────────────────────────────────────

    /// <summary>
    /// Two local variables both used twice generate two CROSS APPLY clauses.
    /// </summary>
    [Fact]
    public Task TwoVariables_EachUsedTwice_GeneratesTwoCrossApplies()
    {
        using var dbContext = new SampleDbContext<Entity>();
        var query = dbContext.Set<Entity>().Select(x => x.TwoReuseVariables());
        return Verifier.Verify(query.ToQueryString());
    }

    /// <summary>
    /// First variable used twice and second variable used once — only the reused one gets a
    /// CROSS APPLY.
    /// </summary>
    [Fact]
    public Task MixedReuse_OnlyReusedVariableGetsCrossApply()
    {
        using var dbContext = new SampleDbContext<Entity>();
        var query = dbContext.Set<Entity>().Select(x => x.MixedReuse());
        return Verifier.Verify(query.ToQueryString());
    }

    // ── reuse in WHERE clause ──────────────────────────────────────────────────────────

    /// <summary>
    /// A reused variable in a WHERE clause generates a CROSS APPLY.
    /// </summary>
    [Fact]
    public Task VariableReuseInWhere_GeneratesCrossApply()
    {
        using var dbContext = new SampleDbContext<Entity>();
        var query = dbContext.Set<Entity>().Where(x => x.IsHighScorer());
        return Verifier.Verify(query.ToQueryString());
    }

    /// <summary>
    /// A reused variable shared between SELECT projection and WHERE filter.
    /// </summary>
    [Fact]
    public Task VariableReuseAcrossSelectAndWhere_GeneratesCrossApply()
    {
        using var dbContext = new SampleDbContext<Entity>();
        var query = dbContext.Set<Entity>()
            .Where(x => x.IsPositiveAdjusted())
            .Select(x => x.GetAdjustedValue());
        return Verifier.Verify(query.ToQueryString());
    }

    // ── nested / chained projectable calls ────────────────────────────────────────────

    /// <summary>
    /// A block-bodied method that calls another projectable and stores the result in a local
    /// variable used twice generates a CROSS APPLY for the composed expression.
    /// </summary>
    [Fact]
    public Task NestedProjectableCall_ResultReused_GeneratesCrossApply()
    {
        using var dbContext = new SampleDbContext<Entity>();
        var query = dbContext.Set<Entity>().Select(x => x.ReuseNestedProjectable());
        return Verifier.Verify(query.ToQueryString());
    }

    /// <summary>
    /// A deeply nested chain: outer calls middle which calls inner; the middle result is
    /// reused in the outer body.
    /// </summary>
    [Fact]
    public Task DeeplyNestedChain_WithReuse_GeneratesCrossApply()
    {
        using var dbContext = new SampleDbContext<Entity>();
        var query = dbContext.Set<Entity>().Select(x => x.DeepChainWithReuse());
        return Verifier.Verify(query.ToQueryString());
    }

    /// <summary>
    /// A block-bodied method stores a conditional expression in a reused local variable.
    /// </summary>
    [Fact]
    public Task ConditionalExpressionInVariable_Reused_GeneratesCrossApply()
    {
        using var dbContext = new SampleDbContext<Entity>();
        var query = dbContext.Set<Entity>().Select(x => x.ReuseConditional());
        return Verifier.Verify(query.ToQueryString());
    }

    /// <summary>
}

// ── projectable method definitions ────────────────────────────────────────────────────────

public static class LocalVariableReuseExtensions
{
    // ── basic numeric reuse ────────────────────────────────────────────────────────────

    [Projectable(AllowBlockBody = true)]
    public static int DoubledTwice(this LocalVariableReuseTests.Entity e)
    {
        var doubled = e.Value * 2;
        return doubled + doubled;
    }

    [Projectable(AllowBlockBody = true)]
    public static int TripleReuse(this LocalVariableReuseTests.Entity e)
    {
        var score = e.Value * 10;
        return score + score + score;
    }

    [Projectable(AllowBlockBody = true)]
    public static int DoubledOnce(this LocalVariableReuseTests.Entity e)
    {
        var doubled = e.Value * 2;
        return doubled + 1;
    }

    // ── two reused variables ───────────────────────────────────────────────────────────

    [Projectable(AllowBlockBody = true)]
    public static int TwoReuseVariables(this LocalVariableReuseTests.Entity e)
    {
        var doubled = e.Value * 2;
        var tripled = e.Value * 3;
        return doubled * doubled + tripled * tripled;
    }

    [Projectable(AllowBlockBody = true)]
    public static int MixedReuse(this LocalVariableReuseTests.Entity e)
    {
        var multiplied = e.Value * 4;   // reused twice
        var offset = e.Score + 10;      // used once
        return multiplied + multiplied + offset;
    }

    // ── WHERE clause ──────────────────────────────────────────────────────────────────

    [Projectable(AllowBlockBody = true)]
    public static bool IsHighScorer(this LocalVariableReuseTests.Entity e)
    {
        var adjusted = e.Score * 2;
        return adjusted > 50 && adjusted < 200;
    }

    [Projectable(AllowBlockBody = true)]
    public static bool IsPositiveAdjusted(this LocalVariableReuseTests.Entity e)
    {
        var adjusted = e.Value - 5;
        return adjusted > 0 && adjusted < 100;
    }

    [Projectable(AllowBlockBody = true)]
    public static int GetAdjustedValue(this LocalVariableReuseTests.Entity e)
    {
        var adjusted = e.Value - 5;
        return adjusted * 2;
    }

    // ── nested projectable ────────────────────────────────────────────────────────────

    [Projectable(AllowBlockBody = true)]
    public static int GetDoubledValue(this LocalVariableReuseTests.Entity e)
        => e.Value * 2;

    [Projectable(AllowBlockBody = true)]
    public static int ReuseNestedProjectable(this LocalVariableReuseTests.Entity e)
    {
        var inner = e.GetDoubledValue();   // calls another projectable
        return inner + inner;              // reuse → CROSS APPLY
    }

    [Projectable(AllowBlockBody = true)]
    public static int GetAdjustedScore(this LocalVariableReuseTests.Entity e)
        => e.Score + 100;

    [Projectable(AllowBlockBody = true)]
    public static int DeepChainWithReuse(this LocalVariableReuseTests.Entity e)
    {
        var mid = e.GetAdjustedScore();      // calls GetAdjustedScore
        var doubled = mid * 2;              // further computation
        return doubled + doubled + mid;     // doubled reused, mid once
    }

    // ── conditional / string ──────────────────────────────────────────────────────────

    [Projectable(AllowBlockBody = true)]
    public static int ReuseConditional(this LocalVariableReuseTests.Entity e)
    {
        var capped = e.Value > 100 ? 100 : e.Value;
        return capped + capped;
    }
}
