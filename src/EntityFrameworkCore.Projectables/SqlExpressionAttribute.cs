using System;

namespace EntityFrameworkCore.Projectables
{
    /// <summary>
    /// Decorates a static method with a SQL template string that will be used to translate
    /// the method call into a SQL expression when used in a LINQ query against EF Core.
    /// Use positional placeholders {0}, {1}, etc. to refer to the method arguments.
    /// </summary>
    /// <example>
    /// <code>
    /// [SqlExpression("SOUNDEX({0})")]
    /// public static string Soundex(string value) => throw new NotImplementedException();
    ///
    /// [SqlExpression("COALESCE({0}, {1})")]
    /// public static string Coalesce(string value, string fallback) => throw new NotImplementedException();
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class SqlExpressionAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of <see cref="SqlExpressionAttribute"/> with the given SQL template.
        /// </summary>
        /// <param name="sql">
        /// The SQL template. Use {0}, {1}, etc. as positional placeholders for method arguments.
        /// </param>
        public SqlExpressionAttribute(string sql)
        {
            Sql = sql;
        }

        /// <summary>
        /// The SQL template string with positional argument placeholders ({0}, {1}, etc.).
        /// </summary>
        public string Sql { get; }

        /// <summary>
        /// When <c>true</c> (the default), the method can only be evaluated server-side and must
        /// throw <see cref="NotImplementedException"/> in its body.
        /// </summary>
        public bool ServerSideOnly { get; set; } = true;
    }
}
