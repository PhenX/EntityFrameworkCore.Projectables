#if !NET8_0 && !NET9_0
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Projectables.Query.SqlExpressions;

/// <summary>
/// A <see cref="TableExpressionBase"/> that represents the inner subquery of a
/// <c>CROSS APPLY (SELECT <see cref="Inner"/> AS [<see cref="ColumnName"/>]) AS [alias]</c>
/// expression added by <see cref="CteAwareQuerySqlGenerator"/> to materialise a
/// <see cref="VariableWrapSqlExpression"/> exactly once per row.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "EF1001:Internal EF Core API usage.", Justification = "Needed")]
public sealed class VariableWrapCrossApplyExpression : TableExpressionBase
{
    /// <summary>Creates a new instance.</summary>
    /// <param name="alias">The SQL table alias (e.g. <c>_cv0</c>) for the CROSS APPLY table.</param>
    /// <param name="inner">The SQL expression to project as a single column.</param>
    /// <param name="columnName">The name to give the projected column (the variable name).</param>
    public VariableWrapCrossApplyExpression(string alias, SqlExpression inner, string columnName)
        : base(alias)
    {
        Inner = inner;
        ColumnName = columnName;
    }

    private VariableWrapCrossApplyExpression(
        string alias,
        SqlExpression inner,
        string columnName,
        IReadOnlyDictionary<string, IAnnotation> annotations)
        : base(alias, annotations)
    {
        Inner = inner;
        ColumnName = columnName;
    }

    /// <summary>The expression computed inside the CROSS APPLY subquery.</summary>
    public SqlExpression Inner { get; }

    /// <summary>The projected column name inside the CROSS APPLY subquery.</summary>
    public string ColumnName { get; }

    /// <inheritdoc/>
    protected override Expression VisitChildren(ExpressionVisitor visitor)
    {
        var newInner = (SqlExpression)visitor.Visit(Inner);
        return ReferenceEquals(newInner, Inner)
            ? this
            : new VariableWrapCrossApplyExpression(Alias!, newInner, ColumnName,
                GetAnnotations().ToDictionary(a => a.Name, a => a));
    }

    /// <inheritdoc/>
    public override TableExpressionBase Clone(string? alias, ExpressionVisitor cloningExpressionVisitor)
        => new VariableWrapCrossApplyExpression(
            alias ?? Alias!,
            (SqlExpression)cloningExpressionVisitor.Visit(Inner),
            ColumnName,
            GetAnnotations().ToDictionary(a => a.Name, a => a));

    /// <inheritdoc/>
    public override TableExpressionBase WithAlias(string newAlias)
        => new VariableWrapCrossApplyExpression(newAlias, Inner, ColumnName,
            GetAnnotations().ToDictionary(a => a.Name, a => a));

    /// <inheritdoc/>
    protected override TableExpressionBase WithAnnotations(IReadOnlyDictionary<string, IAnnotation> annotations)
        => new VariableWrapCrossApplyExpression(Alias!, Inner, ColumnName, annotations);

    /// <inheritdoc/>
    public override Expression Quote()
        => throw new NotSupportedException($"{nameof(VariableWrapCrossApplyExpression)} does not support pre-compiled queries.");

    /// <inheritdoc/>
    protected override void Print(ExpressionPrinter expressionPrinter)
    {
        expressionPrinter.Append($"(SELECT ");
        expressionPrinter.Visit(Inner);
        expressionPrinter.Append($" AS [{ColumnName}]) AS [{Alias}]");
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj is VariableWrapCrossApplyExpression other
            && Alias == other.Alias
            && ColumnName == other.ColumnName
            && Inner.Equals(other.Inner);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Alias, ColumnName, Inner);
}
#endif
