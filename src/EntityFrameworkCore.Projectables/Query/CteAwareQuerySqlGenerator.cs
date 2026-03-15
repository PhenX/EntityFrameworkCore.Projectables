using System.Linq.Expressions;
using EntityFrameworkCore.Projectables.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Projectables.Query;

/// <summary>
/// A <see cref="QuerySqlGenerator"/> subclass that supports <see cref="CteTableExpression"/> nodes.
/// <para>
/// Before generating the main <c>SELECT</c>, it:
/// <list type="number">
///   <item>Runs <see cref="CteDeduplicatingRewriter"/> to detect duplicate <see cref="SelectExpression"/>
///         subtrees and replaces them with <see cref="CteTableExpression"/> references.</item>
///   <item>Emits a <c>WITH cteName AS (…)</c> preamble for each collected CTE, in depth-first order.</item>
/// </list>
/// </para>
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "EF1001:Internal EF Core API usage.", Justification = "Needed")]
public class CteAwareQuerySqlGenerator : QuerySqlGenerator
{
    /// <inheritdoc/>
    public CteAwareQuerySqlGenerator(QuerySqlGeneratorDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <inheritdoc/>
    protected override void GenerateRootCommand(Expression queryExpression)
    {
        var rewriter = new CteDeduplicatingRewriter();
        var rewritten = rewriter.Rewrite(queryExpression);
        var ctes = rewriter.CollectedCtes;

        if (ctes.Count == 0)
        {
            // No CTEs found — fall through to base behaviour using the original expression
            // (avoids any observable difference in SQL formatting caused by a no-op tree visit).
            base.GenerateRootCommand(queryExpression);
            return;
        }

        Sql.Append("WITH ");

        for (var i = 0; i < ctes.Count; i++)
        {
            if (i > 0)
            {
                Sql.AppendLine(",");
            }

            var cte = ctes[i];
            Sql.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(cte.CteName));
            Sql.AppendLine(" AS (");
            using (Sql.Indent())
            {
                Visit(cte.Inner);
            }

            Sql.AppendLine();
            Sql.Append(")");
        }

        Sql.AppendLine();
        base.GenerateRootCommand(rewritten);
    }

    /// <inheritdoc/>
    protected override Expression VisitExtension(Expression expression)
    {
        if (expression is CteTableExpression cteTable)
        {
            return VisitCteTable(cteTable);
        }

        return base.VisitExtension(expression);
    }

    /// <summary>
    /// Emits the CTE reference as a bare name with alias separator (e.g., <c>[cte_0] AS [cte_0]</c>).
    /// The CTE body is already emitted in the <c>WITH</c> preamble.
    /// </summary>
    private Expression VisitCteTable(CteTableExpression cteTable)
    {
        Sql.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(cteTable.CteName));
        Sql.Append(AliasSeparator);
        Sql.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(cteTable.Alias!));
        return cteTable;
    }
}
