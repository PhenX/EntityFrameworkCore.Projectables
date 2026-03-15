using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Projectables.Query.SqlExpressions;

/// <summary>
/// A <see cref="SqlExpression"/> that marks a named reused local variable produced by a
/// block-bodied <c>[Projectable]</c> method.
/// <para>
/// The source generator wraps each occurrence of a reused local variable in a call to
/// <see cref="Variable.Wrap{T}(string, T)"/>.  EF Core's method-call translator converts those
/// calls to <see cref="VariableWrapSqlExpression"/> nodes.  The
/// <see cref="CteAwareQuerySqlGenerator"/> then replaces multi-occurrence groups with a
/// <c>CROSS APPLY (SELECT … AS [name]) AS [alias]</c> table source so that the expression is
/// computed exactly once per row.
/// </para>
/// <para>
/// Single-occurrence nodes (where the variable is only used once) are lowered to the plain
/// <see cref="Inner"/> expression by the SQL generator, preserving the original SQL shape.
/// </para>
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "EF1001:Internal EF Core API usage.", Justification = "Needed")]
public sealed class VariableWrapSqlExpression : SqlExpression
{
    /// <summary>Initialises a new instance.</summary>
    /// <param name="variableName">The name of the local variable as it appears in source.</param>
    /// <param name="inner">The SQL expression that computes the variable's value.</param>
    public VariableWrapSqlExpression(string variableName, SqlExpression inner)
        : base(inner.Type, inner.TypeMapping)
    {
        VariableName = variableName;
        Inner = inner;
    }

    /// <summary>The local-variable name the generator used when emitting this marker.</summary>
    public string VariableName { get; }

    /// <summary>The SQL expression that produces the variable's value.</summary>
    public SqlExpression Inner { get; }

    /// <inheritdoc/>
    protected override Expression VisitChildren(ExpressionVisitor visitor)
    {
        var newInner = (SqlExpression)visitor.Visit(Inner);
        return ReferenceEquals(newInner, Inner)
            ? this
            : new VariableWrapSqlExpression(VariableName, newInner);
    }

#if !NET8_0
    /// <inheritdoc/>
    public override Expression Quote()
        => throw new NotSupportedException($"{nameof(VariableWrapSqlExpression)} does not support pre-compiled queries.");
#endif

    /// <inheritdoc/>
    protected override void Print(ExpressionPrinter expressionPrinter)
    {
        expressionPrinter.Append($"Wrap({VariableName}: ");
        expressionPrinter.Visit(Inner);
        expressionPrinter.Append(")");
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj is VariableWrapSqlExpression other
            && VariableName == other.VariableName
            && Inner.Equals(other.Inner);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(VariableName, Inner);
}
