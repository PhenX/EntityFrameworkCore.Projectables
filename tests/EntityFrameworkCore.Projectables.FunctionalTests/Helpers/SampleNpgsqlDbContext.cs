using System.Text;
using EntityFrameworkCore.Projectables.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Projectables.FunctionalTests.Helpers;

/// <summary>
/// A test <see cref="DbContext"/> that simulates a PostgreSQL / double-quote-delimiter
/// provider by replacing EF Core's <see cref="ISqlGenerationHelper"/> with one that uses
/// <c>"identifier"</c> quoting.
/// <para>
/// <see cref="ProjectablesQuerySqlGenerator"/> detects the provider by checking whether
/// <see cref="ISqlGenerationHelper.DelimitIdentifier(string)"/> starts with a double quote.
/// This makes the generator emit <c>CROSS JOIN LATERAL</c> instead of SQL Server's
/// <c>CROSS APPLY</c>.  No real database connection is required — tests only call
/// <see cref="Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToQueryString{TEntity}"/>.
/// </para>
/// </summary>
public sealed class SampleNpgsqlDbContext<TEntity> : DbContext
    where TEntity : class
{
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Fake connection string — we never actually connect.
        optionsBuilder.UseSqlServer("Server=(localdb)\\v11.0;Integrated Security=true");
        // Replace the SQL generation helper so DelimitIdentifier uses "…" quoting, which
        // causes ProjectablesQuerySqlGenerator.IsPostgres to return true.
        optionsBuilder.ReplaceService<ISqlGenerationHelper, PostgresStyleSqlGenerationHelper>();
        optionsBuilder.UseProjectables();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TEntity>();
    }
}

/// <summary>
/// An <see cref="ISqlGenerationHelper"/> that uses double-quote (<c>"</c>) identifier
/// delimiters to simulate PostgreSQL quoting style for test purposes.
/// </summary>
internal sealed class PostgresStyleSqlGenerationHelper(RelationalSqlGenerationHelperDependencies dependencies)
    : RelationalSqlGenerationHelper(dependencies)
{
    public override string DelimitIdentifier(string identifier)
        => '"' + EscapeIdentifier(identifier) + '"';

    public override void DelimitIdentifier(StringBuilder builder, string identifier)
    {
        builder.Append('"');
        EscapeIdentifier(builder, identifier);
        builder.Append('"');
    }

    public override string DelimitIdentifier(string name, string? schema)
        => schema is null
            ? DelimitIdentifier(name)
            : DelimitIdentifier(schema) + "." + DelimitIdentifier(name);

    public override void DelimitIdentifier(StringBuilder builder, string name, string? schema)
    {
        if (schema is not null)
        {
            DelimitIdentifier(builder, schema);
            builder.Append('.');
        }

        DelimitIdentifier(builder, name);
    }
}
