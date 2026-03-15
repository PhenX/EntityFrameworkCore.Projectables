#if !NET8_0 && !NET9_0
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Projectables.Query.SqlExpressions;

/// <summary>
/// A <see cref="TableExpressionBase"/> that represents a single-column inline subquery used
/// to materialise a reused local variable exactly once per row:
/// <code>
/// (SELECT &lt;Inner&gt; AS [&lt;ColumnName&gt;]) AS [&lt;Alias&gt;]
/// </code>
/// This is emitted by <see cref="ProjectablesQuerySqlGenerator"/> as the body of a
/// <c>CROSS APPLY</c> (SQL Server) or <c>CROSS JOIN LATERAL</c> (PostgreSQL) clause.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "EF1001:Internal EF Core API usage.", Justification = "Needed")]
public sealed class InlineSubqueryExpression : TableExpressionBase
{
    /// <summary>Creates a new instance.</summary>
    /// <param name="alias">The SQL table alias for the inline subquery.</param>
    /// <param name="inner">The SQL expression projected as a single column.</param>
    /// <param name="columnName">The name of the projected column (the original variable name).</param>
    public InlineSubqueryExpression(string alias, SqlExpression inner, string columnName)
        : base(alias)
    {
        Inner = inner;
        ColumnName = columnName;
    }

    private InlineSubqueryExpression(
        string alias,
        SqlExpression inner,
        string columnName,
        IReadOnlyDictionary<string, IAnnotation> annotations)
        : base(alias, annotations)
    {
        Inner = inner;
        ColumnName = columnName;
    }

    /// <summary>The expression computed inside the inline subquery.</summary>
    public SqlExpression Inner { get; }

    /// <summary>The projected column name (the original local-variable name).</summary>
    public string ColumnName { get; }

    /// <inheritdoc/>
    protected override Expression VisitChildren(ExpressionVisitor visitor)
    {
        var newInner = (SqlExpression)visitor.Visit(Inner);
        return ReferenceEquals(newInner, Inner)
            ? this
            : new InlineSubqueryExpression(Alias!, newInner, ColumnName,
                GetAnnotations().ToDictionary(a => a.Name, a => a));
    }

    /// <inheritdoc/>
    public override TableExpressionBase Clone(string? alias, ExpressionVisitor cloningExpressionVisitor)
        => new InlineSubqueryExpression(
            alias ?? Alias!,
            (SqlExpression)cloningExpressionVisitor.Visit(Inner),
            ColumnName,
            GetAnnotations().ToDictionary(a => a.Name, a => a));

    /// <inheritdoc/>
    public override TableExpressionBase WithAlias(string newAlias)
        => new InlineSubqueryExpression(newAlias, Inner, ColumnName,
            GetAnnotations().ToDictionary(a => a.Name, a => a));

    /// <inheritdoc/>
    protected override TableExpressionBase WithAnnotations(IReadOnlyDictionary<string, IAnnotation> annotations)
        => new InlineSubqueryExpression(Alias!, Inner, ColumnName, annotations);

    /// <inheritdoc/>
    public override Expression Quote()
        => throw new NotSupportedException($"{nameof(InlineSubqueryExpression)} does not support pre-compiled queries.");

    /// <inheritdoc/>
    protected override void Print(ExpressionPrinter expressionPrinter)
    {
        expressionPrinter.Append($"(SELECT ");
        expressionPrinter.Visit(Inner);
        expressionPrinter.Append($" AS [{ColumnName}]) AS [{Alias}]");
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj is InlineSubqueryExpression other
            && Alias == other.Alias
            && ColumnName == other.ColumnName
            && Inner.Equals(other.Inner);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Alias, ColumnName, Inner);
}
#endif
