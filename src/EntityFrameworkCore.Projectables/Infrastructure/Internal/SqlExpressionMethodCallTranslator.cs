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
            new Regex(@"^(\w[\w.]*)(?:\s*)\(\s*(\{\d+\}(?:\s*,\s*\{\d+\})*)\s*\)$", RegexOptions.Compiled);

        // Matches individual argument placeholders like {0}, {1}, ...
        private static readonly Regex ArgumentPlaceholderPattern =
            new Regex(@"\{(\d+)\}", RegexOptions.Compiled);

        private readonly ISqlExpressionFactory _sqlExpressionFactory;
        private readonly string? _providerName;

        public SqlExpressionMethodCallTranslator(ISqlExpressionFactory sqlExpressionFactory, string? providerName = null)
        {
            _sqlExpressionFactory = sqlExpressionFactory;
            _providerName = providerName;
        }

        /// <inheritdoc />
        public SqlExpression? Translate(
            SqlExpression? instance,
            MethodInfo method,
            IReadOnlyList<SqlExpression> arguments,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            var sqlExpressionAttrs = method.GetCustomAttributes<SqlExpressionAttribute>().ToArray();
            if (sqlExpressionAttrs.Length == 0)
                return null;

            // Prefer an attribute whose Configuration matches the current provider name.
            SqlExpressionAttribute? selectedAttr = null;
            if (_providerName != null)
            {
                selectedAttr = sqlExpressionAttrs.FirstOrDefault(a =>
                    a.Configuration != null &&
                    _providerName.Contains(a.Configuration, StringComparison.OrdinalIgnoreCase));
            }

            // Fall back to an attribute without a Configuration (provider-agnostic).
            selectedAttr ??= sqlExpressionAttrs.FirstOrDefault(a => a.Configuration is null);

            if (selectedAttr is null)
                return null;

            return TranslateTemplate(selectedAttr.Sql, arguments, method.ReturnType);
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
            var argSection = match.Groups[2].Value;

            var argMatches = ArgumentPlaceholderPattern.Matches(argSection);
            var orderedArgs = new SqlExpression[argMatches.Count];
            for (var i = 0; i < argMatches.Count; i++)
            {
                var index = int.Parse(argMatches[i].Groups[1].Value);
                if (index >= arguments.Count)
                {
                    throw new InvalidOperationException(
                        $"SQL template '{template}' references argument {{index}} but the method only has {arguments.Count} argument(s).");
                }

                orderedArgs[i] = arguments[index];
            }

            return _sqlExpressionFactory.Function(
                functionName,
                orderedArgs,
                nullable: true,
                argumentsPropagateNullability: orderedArgs.Select(_ => true).ToArray(),
                returnType);
        }
    }
}
