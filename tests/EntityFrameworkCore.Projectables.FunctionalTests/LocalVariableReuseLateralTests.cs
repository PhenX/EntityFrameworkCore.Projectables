using EntityFrameworkCore.Projectables.FunctionalTests.Helpers;
using Microsoft.EntityFrameworkCore;
using VerifyXunit;
using Xunit;

namespace EntityFrameworkCore.Projectables.FunctionalTests;

/// <summary>
/// Verifies that local variables declared in block-bodied <c>[Projectable]</c> methods are
/// hoisted into <c>CROSS JOIN LATERAL (SELECT … AS "variableName") AS "variableName"</c>
/// subqueries when using a PostgreSQL provider (Npgsql) and the variable is referenced more than
/// once.
/// </summary>
[UsesVerify]
public class LocalVariableReuseLateralTests
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
    /// A single local variable used twice generates one CROSS JOIN LATERAL.
    /// </summary>
    [Fact]
    public Task SingleVariable_UsedTwice_GeneratesLateral()
    {
        using var dbContext = new SampleNpgsqlDbContext<Entity>();
        var query = dbContext.Set<Entity>().Select(x => x.Lateral_DoubledTwice());
        return Verifier.Verify(query.ToQueryString());
    }

    /// <summary>
    /// A single local variable used only once is inlined — no CROSS JOIN LATERAL.
    /// </summary>
    [Fact]
    public Task SingleVariable_UsedOnce_IsInlined()
    {
        using var dbContext = new SampleNpgsqlDbContext<Entity>();
        var query = dbContext.Set<Entity>().Select(x => x.Lateral_DoubledOnce());
        return Verifier.Verify(query.ToQueryString());
    }

    /// <summary>
    /// Two local variables both used twice generate two CROSS JOIN LATERAL clauses.
    /// </summary>
    [Fact]
    public Task TwoVariables_EachUsedTwice_GeneratesTwoLaterals()
    {
        using var dbContext = new SampleNpgsqlDbContext<Entity>();
        var query = dbContext.Set<Entity>().Select(x => x.Lateral_TwoReuseVariables());
        return Verifier.Verify(query.ToQueryString());
    }

    /// <summary>
    /// A reused variable in a WHERE clause generates a CROSS JOIN LATERAL.
    /// </summary>
    [Fact]
    public Task VariableReuseInWhere_GeneratesLateral()
    {
        using var dbContext = new SampleNpgsqlDbContext<Entity>();
        var query = dbContext.Set<Entity>().Where(x => x.Lateral_IsHighScorer());
        return Verifier.Verify(query.ToQueryString());
    }

    /// <summary>
    /// A block-bodied method that calls another projectable and stores the result in a local
    /// variable used twice generates a CROSS JOIN LATERAL for the composed expression.
    /// </summary>
    [Fact]
    public Task NestedProjectableCall_ResultReused_GeneratesLateral()
    {
        using var dbContext = new SampleNpgsqlDbContext<Entity>();
        var query = dbContext.Set<Entity>().Select(x => x.Lateral_ReuseNestedProjectable());
        return Verifier.Verify(query.ToQueryString());
    }

    /// <summary>
    /// A conditional expression stored in a reused local variable.
    /// </summary>
    [Fact]
    public Task ConditionalExpressionInVariable_Reused_GeneratesLateral()
    {
        using var dbContext = new SampleNpgsqlDbContext<Entity>();
        var query = dbContext.Set<Entity>().Select(x => x.Lateral_ReuseConditional());
        return Verifier.Verify(query.ToQueryString());
    }
}

// ── projectable method definitions ────────────────────────────────────────────────────────

public static class LateralVariableReuseExtensions
{
    [Projectable(AllowBlockBody = true)]
    public static int Lateral_DoubledTwice(this LocalVariableReuseLateralTests.Entity e)
    {
        var doubled = e.Value * 2;
        return doubled + doubled;
    }

    [Projectable(AllowBlockBody = true)]
    public static int Lateral_DoubledOnce(this LocalVariableReuseLateralTests.Entity e)
    {
        var doubled = e.Value * 2;
        return doubled + 1;
    }

    [Projectable(AllowBlockBody = true)]
    public static int Lateral_TwoReuseVariables(this LocalVariableReuseLateralTests.Entity e)
    {
        var doubled = e.Value * 2;
        var tripled = e.Value * 3;
        return doubled * doubled + tripled * tripled;
    }

    [Projectable(AllowBlockBody = true)]
    public static bool Lateral_IsHighScorer(this LocalVariableReuseLateralTests.Entity e)
    {
        var adjusted = e.Score * 2;
        return adjusted > 50 && adjusted < 200;
    }

    [Projectable(AllowBlockBody = true)]
    public static int Lateral_GetDoubledValue(this LocalVariableReuseLateralTests.Entity e)
        => e.Value * 2;

    [Projectable(AllowBlockBody = true)]
    public static int Lateral_ReuseNestedProjectable(this LocalVariableReuseLateralTests.Entity e)
    {
        var inner = e.Lateral_GetDoubledValue();
        return inner + inner;
    }

    [Projectable(AllowBlockBody = true)]
    public static int Lateral_ReuseConditional(this LocalVariableReuseLateralTests.Entity e)
    {
        var capped = e.Value > 100 ? 100 : e.Value;
        return capped + capped;
    }
}
