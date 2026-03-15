using System.Linq.Expressions;
using System.Reflection;
using EntityFrameworkCore.Projectables.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Projectables.Query;

/// <summary>
/// Translates calls to <see cref="Variable.Wrap{T}(string,T)"/> — the reuse-marker inserted by
/// the source generator for block-bodied projectable methods — into
/// <see cref="VariableWrapSqlExpression"/> nodes so that the SQL generator can later decide
/// whether to inline them or factor them out via a <c>CROSS APPLY</c>.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "EF1001:Internal EF Core API usage.", Justification = "Needed")]
internal sealed class VariableWrapTranslatorPlugin : IMethodCallTranslatorPlugin
{
    public IEnumerable<IMethodCallTranslator> Translators { get; } = [new VariableWrapTranslator()];

    private sealed class VariableWrapTranslator : IMethodCallTranslator
    {
        private static readonly MethodInfo _variableWrapMethod =
            ((MethodCallExpression)((Expression<Func<int, int>>)(v => Variable.Wrap("x", v))).Body)
                .Method.GetGenericMethodDefinition();

        public SqlExpression? Translate(
            SqlExpression? instance,
            MethodInfo method,
            IReadOnlyList<SqlExpression> arguments,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            if (!method.IsGenericMethod || method.GetGenericMethodDefinition() != _variableWrapMethod)
                return null;

            // arguments[0] is the constant name string, arguments[1] is the inner SQL expression.
            var variableName = (string)((SqlConstantExpression)arguments[0]).Value!;
            var inner = arguments[1];
            return new VariableWrapSqlExpression(variableName, inner);
        }
    }
}
