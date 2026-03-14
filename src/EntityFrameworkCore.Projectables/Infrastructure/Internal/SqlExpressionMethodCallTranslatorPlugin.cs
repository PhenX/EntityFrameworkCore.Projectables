using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Projectables.Infrastructure.Internal
{
    /// <summary>
    /// Registers the <see cref="SqlExpressionMethodCallTranslator"/> with EF Core's method call
    /// translation pipeline so that methods decorated with <see cref="SqlExpressionAttribute"/>
    /// are translated to the corresponding SQL expressions.
    /// </summary>
    public class SqlExpressionMethodCallTranslatorPlugin : IMethodCallTranslatorPlugin
    {
        public SqlExpressionMethodCallTranslatorPlugin(ISqlExpressionFactory sqlExpressionFactory)
        {
            Translators = new IMethodCallTranslator[]
            {
                new SqlExpressionMethodCallTranslator(sqlExpressionFactory)
            };
        }

        /// <inheritdoc />
        public IEnumerable<IMethodCallTranslator> Translators { get; }
    }
}
