using System;
using System.Linq;
using System.Threading.Tasks;
using EntityFrameworkCore.Projectables.FunctionalTests.Helpers;
using Microsoft.EntityFrameworkCore;
using VerifyXunit;
using Xunit;

namespace EntityFrameworkCore.Projectables.FunctionalTests
{
    /// <summary>
    /// Extension-method-style SQL functions – same SQL templates as <see cref="SqlExpressionTests.Functions"/>,
    /// but declared with a <c>this</c> parameter so they can be called with instance syntax.
    /// Must be a top-level (non-nested) static class because C# requires extension methods there.
    /// </summary>
    public static class SqlExtensionFunctions
    {
        [SqlExpression("UPPER({0})")]
        public static string Upper(this string value) => throw new NotImplementedException();

        [SqlExpression("STRFTIME('%Y', {0})", Configuration = "Sqlite")]
        [SqlExpression("YEAR({0})", Configuration = "SqlServer")]
        public static int Year(this DateTime date) => throw new NotImplementedException();
    }

    [UsesVerify]
    public class SqlExpressionTests
    {
        public static class Functions
        {
            [SqlExpression("UPPER({0})")]
            public static string Upper(string value) => throw new NotImplementedException();

            [SqlExpression("COALESCE({0}, {1})")]
            public static string Coalesce(string? value, string? fallback) => throw new NotImplementedException();

            [SqlExpression("STRFTIME('%Y', {0})", Configuration = "Sqlite")]
            [SqlExpression("YEAR({0})", Configuration = "SqlServer")]
            [SqlExpression("EXTRACT(YEAR FROM {0})", Configuration = "Npgsql")]
            public static int Year(DateTime date) => throw new NotImplementedException();

            [SqlExpression("GENERIC_YEAR({0})")]
            [SqlExpression("YEAR({0})", Configuration = "SqlServer")]
            public static int YearWithFallback(DateTime date) => throw new NotImplementedException();
        }

        public record Entity
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public string? NickName { get; set; }
        }

        public record DateEntity
        {
            public int Id { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        [Fact]
        public Task WhereWithSqlExpressionUpper()
        {
            using var dbContext = new SampleDbContext<Entity>();

            var query = dbContext.Set<Entity>()
                .Where(x => Functions.Upper(x.Name) == "ALICE");

            return Verifier.Verify(query.ToQueryString());
        }

        [Fact]
        public Task SelectWithSqlExpressionCoalesce()
        {
            using var dbContext = new SampleDbContext<Entity>();

            var query = dbContext.Set<Entity>()
                .Select(x => Functions.Coalesce(x.NickName, x.Name));

            return Verifier.Verify(query.ToQueryString());
        }

        [Fact]
        public Task SelectWithProviderSpecificSqlExpression()
        {
            using var dbContext = new SampleDbContext<DateEntity>();

            var query = dbContext.Set<DateEntity>()
                .Select(x => Functions.Year(x.CreatedAt));

            return Verifier.Verify(query.ToQueryString());
        }

        [Fact]
        public Task SelectWithFallbackSqlExpression()
        {
            using var dbContext = new SampleDbContext<DateEntity>();

            var query = dbContext.Set<DateEntity>()
                .Select(x => Functions.YearWithFallback(x.CreatedAt));

            return Verifier.Verify(query.ToQueryString());
        }

        /// <summary>
        /// Verifies that <see cref="SqlExtensionFunctions.Upper"/> is translated correctly when
        /// called with extension-method syntax (<c>x.Name.Upper()</c>).
        /// </summary>
        [Fact]
        public Task WhereWithExtensionMethodSqlExpression()
        {
            using var dbContext = new SampleDbContext<Entity>();

            var query = dbContext.Set<Entity>()
                .Where(x => x.Name.Upper() == "ALICE");

            return Verifier.Verify(query.ToQueryString());
        }

        /// <summary>
        /// Verifies that a provider-specific template (<c>STRFTIME('%Y', {0})</c>) with a
        /// literal SQL fragment mixed into the argument list is translated correctly on SQLite.
        /// </summary>
        [Fact]
        public Task SelectWithStrftimeOnSqlite()
        {
            using var dbContext = new SqliteSampleDbContext<DateEntity>();

            var query = dbContext.Set<DateEntity>()
                .Select(x => Functions.Year(x.CreatedAt));

            return Verifier.Verify(query.ToQueryString());
        }

        /// <summary>
        /// Verifies the same scenario via extension-method syntax on SQLite.
        /// </summary>
        [Fact]
        public Task SelectWithExtensionMethodStrftimeOnSqlite()
        {
            using var dbContext = new SqliteSampleDbContext<DateEntity>();

            var query = dbContext.Set<DateEntity>()
                .Select(x => x.CreatedAt.Year());

            return Verifier.Verify(query.ToQueryString());
        }
    }
}
