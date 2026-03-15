using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Projectables.Query.SqlExpressions;

/// <summary>
/// Represents a SQL Common Table Expression (CTE) definition:
/// <c>WITH <see cref="CteName"/> AS (<see cref="Inner"/>)</c>.
/// Acts as a table source; references to it are <c>ColumnExpression</c>s
/// whose <c>TableAlias</c> matches this expression's <c>Alias</c>.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "EF1001:Internal EF Core API usage.", Justification = "Needed")]
public sealed class CteTableExpression : TableExpressionBase
#if NET8_0
    , IClonableTableExpressionBase
#endif
{
    /// <summary>Creates a new <see cref="CteTableExpression"/> with the given name and body.</summary>
    public CteTableExpression(string alias, SelectExpression inner)
        : base(alias)
    {
        Inner = inner;
    }

    private CteTableExpression(string alias, SelectExpression inner, IReadOnlyDictionary<string, IAnnotation> annotations)
#if NET8_0
        : base(alias, annotations.Values)
#else
        : base(alias, annotations)
#endif
    {
        Inner = inner;
    }

    /// <summary>The alias used as the CTE name in the <c>WITH</c> clause.</summary>
    public string CteName => Alias!;

    /// <summary>The <see cref="SelectExpression"/> that defines the CTE body.</summary>
    public SelectExpression Inner { get; }

    protected override Expression VisitChildren(ExpressionVisitor visitor)
    {
        var newInner = (SelectExpression)visitor.Visit(Inner);
        return newInner != Inner
            ? new CteTableExpression(CteName, newInner, GetAnnotations().ToDictionary(a => a.Name, a => a))
            : this;
    }

#if NET8_0
    /// <summary>Creates a clone of this expression.</summary>
    public TableExpressionBase Clone()
        => new CteTableExpression(CteName, Inner, GetAnnotations().ToDictionary(a => a.Name, a => a));

    /// <inheritdoc/>
    protected override TableExpressionBase CreateWithAnnotations(IEnumerable<IAnnotation> annotations)
        => new CteTableExpression(CteName, Inner, annotations.ToDictionary(a => a.Name, a => a));
#else
    /// <inheritdoc/>
    public override TableExpressionBase Clone(string? alias, ExpressionVisitor cloningExpressionVisitor)
        => new CteTableExpression(alias ?? CteName, (SelectExpression)cloningExpressionVisitor.Visit(Inner), GetAnnotations().ToDictionary(a => a.Name, a => a));

    /// <inheritdoc/>
    public override TableExpressionBase WithAlias(string newAlias)
        => new CteTableExpression(newAlias, Inner, GetAnnotations().ToDictionary(a => a.Name, a => a));

    /// <inheritdoc/>
    protected override TableExpressionBase WithAnnotations(IReadOnlyDictionary<string, IAnnotation> annotations)
        => new CteTableExpression(CteName, Inner, annotations);

    /// <inheritdoc/>
    public override Expression Quote()
        => throw new NotSupportedException($"{nameof(CteTableExpression)} does not support pre-compiled queries.");
#endif

    /// <inheritdoc/>
    protected override void Print(ExpressionPrinter expressionPrinter)
    {
        expressionPrinter.Append($"CTE:{CteName}(");
        expressionPrinter.Visit(Inner);
        expressionPrinter.Append(")");
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj is CteTableExpression other
            && CteName == other.CteName
            && Inner.Equals(other.Inner);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(CteName, Inner);
}
