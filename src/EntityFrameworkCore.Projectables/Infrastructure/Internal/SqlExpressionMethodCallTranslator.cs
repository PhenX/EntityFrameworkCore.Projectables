using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace EntityFrameworkCore.Projectables.Infrastructure.Internal
{
    /// <summary>
    /// Translates calls to methods decorated with <see cref="SqlExpressionAttribute"/> into
    /// the corresponding SQL expressions.
    /// </summary>
    public class SqlExpressionMethodCallTranslator : IMethodCallTranslator
    {
        // Matches patterns like: FUNCNAME({0}, {1}, ...)
        private static readonly Regex FunctionCallPattern =
            new Regex(@"^(\w[\w.]*)(\s*)\(\s*(\{\d+\}(\s*,\s*\{\d+\})*)\s*\)$", RegexOptions.Compiled);

        // Matches individual argument placeholders like {0}, {1}, ...
        private static readonly Regex ArgumentPlaceholderPattern =
            new Regex(@"\{(\d+)\}", RegexOptions.Compiled);

        private readonly ISqlExpressionFactory _sqlExpressionFactory;

        public SqlExpressionMethodCallTranslator(ISqlExpressionFactory sqlExpressionFactory)
        {
            _sqlExpressionFactory = sqlExpressionFactory;
        }

        /// <inheritdoc />
        public SqlExpression? Translate(
            SqlExpression? instance,
            MethodInfo method,
            IReadOnlyList<SqlExpression> arguments,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            var sqlExpressionAttr = method.GetCustomAttribute<SqlExpressionAttribute>();
            if (sqlExpressionAttr is null)
                return null;

            return TranslateTemplate(sqlExpressionAttr.Sql, arguments, method.ReturnType);
        }

        private SqlExpression? TranslateTemplate(
            string template,
            IReadOnlyList<SqlExpression> arguments,
            Type returnType)
        {
            var match = FunctionCallPattern.Match(template.Trim());
            if (!match.Success)
                return null;

            var functionName = match.Groups[1].Value;
            var argSection = match.Groups[3].Value;

            var argMatches = ArgumentPlaceholderPattern.Matches(argSection);
            var orderedArgs = argMatches
                .Select(m => arguments[int.Parse(m.Groups[1].Value)])
                .ToArray();

            return _sqlExpressionFactory.Function(
                functionName,
                orderedArgs,
                nullable: true,
                argumentsPropagateNullability: orderedArgs.Select(_ => true).ToArray(),
                returnType);
        }
    }
}
