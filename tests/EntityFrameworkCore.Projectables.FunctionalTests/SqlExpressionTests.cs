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
        }

        public record Entity
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public string? NickName { get; set; }
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
    }
}
