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
        // Matches FUNCNAME(content) — captures function name and full argument list
        private static readonly Regex FunctionCallPattern =
            new Regex(@"^(\w[\w.]*)\s*\((.+)\)\s*$", RegexOptions.Compiled | RegexOptions.Singleline);

        // Matches a standalone {N} placeholder (the entire token)
        private static readonly Regex StandaloneArgumentPlaceholderPattern =
            new Regex(@"^\{(\d+)\}$", RegexOptions.Compiled);

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
            var argsSection = match.Groups[2].Value;

            var sqlArgs = new List<SqlExpression>();
            var nullPropagation = new List<bool>();

            foreach (var token in SplitArguments(argsSection))
            {
                var t = token.Trim();
                var placeholderMatch = StandaloneArgumentPlaceholderPattern.Match(t);
                if (placeholderMatch.Success)
                {
                    var index = int.Parse(placeholderMatch.Groups[1].Value);
                    if (index >= arguments.Count)
                    {
                        throw new InvalidOperationException(
                            $"SQL template '{template}' references argument {{{index}}} but the method only has {arguments.Count} argument(s). Valid indices are 0 to {arguments.Count - 1}.");
                    }
                    sqlArgs.Add(arguments[index]);
                    nullPropagation.Add(true);
                }
                else
                {
                    // Literal SQL fragment (e.g. '%Y' in STRFTIME('%Y', {0}))
                    sqlArgs.Add(_sqlExpressionFactory.Fragment(t));
                    nullPropagation.Add(false);
                }
            }

            return _sqlExpressionFactory.Function(
                functionName,
                sqlArgs,
                nullable: true,
                argumentsPropagateNullability: nullPropagation,
                returnType);
        }

        /// <summary>
        /// Splits a SQL argument list string on top-level commas, respecting
        /// single-quoted string literals and nested parentheses.
        /// </summary>
        private static IEnumerable<string> SplitArguments(string args)
        {
            var depth = 0;
            var inSingleQuote = false;
            var start = 0;

            for (var i = 0; i < args.Length; i++)
            {
                var c = args[i];

                if (c == '\'' && !inSingleQuote)
                {
                    inSingleQuote = true;
                }
                else if (c == '\'' && inSingleQuote)
                {
                    // Handle escaped single quotes ('')
                    if (i + 1 < args.Length && args[i + 1] == '\'')
                        i++;
                    else
                        inSingleQuote = false;
                }
                else if (!inSingleQuote)
                {
                    if (c == '(') depth++;
                    else if (c == ')') depth--;
                    else if (c == ',' && depth == 0)
                    {
                        yield return args[start..i];
                        start = i + 1;
                    }
                }
            }

            yield return args[start..];
        }
    }
}
