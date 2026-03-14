using System;
using System.Linq;
using System.Threading.Tasks;
using EntityFrameworkCore.Projectables.FunctionalTests.Helpers;
using Microsoft.EntityFrameworkCore;
using VerifyXunit;
using Xunit;

namespace EntityFrameworkCore.Projectables.FunctionalTests
{
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
    }
}
