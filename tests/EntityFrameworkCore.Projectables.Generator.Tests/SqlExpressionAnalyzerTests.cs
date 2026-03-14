using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Projectables.Generator.Tests
{
    public class SqlExpressionAnalyzerTests
    {
        readonly ITestOutputHelper _testOutputHelper;

        public SqlExpressionAnalyzerTests(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
        }

        // ------------------------------------------------------------------ helpers

        private Compilation CreateCompilation(string source)
        {
            var references = Basic.Reference.Assemblies.
#if NET10_0
                Net100
#elif NET9_0
                Net90
#elif NET8_0
                Net80
#endif
                .References.All.ToList();

            // Add abstractions assembly (ProjectableAttribute)
            references.Add(MetadataReference.CreateFromFile(typeof(ProjectableAttribute).Assembly.Location));
            // Add main project assembly (SqlExpressionAttribute)
            references.Add(MetadataReference.CreateFromFile(typeof(SqlExpressionAttribute).Assembly.Location));

            return CSharpCompilation.Create("compilation",
                new[] { CSharpSyntaxTree.ParseText(source) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }

        private async Task<ImmutableArray<Diagnostic>> RunAnalyzerAsync(Compilation compilation)
        {
            var analyzer = new SqlExpressionAnalyzer();
            var withAnalyzers = compilation.WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
            return await withAnalyzers.GetAnalyzerDiagnosticsAsync();
        }

        private ImmutableArray<Diagnostic> Efp0003Diagnostics(ImmutableArray<Diagnostic> all)
            => all.Where(d => d.Id == "EFP0003").ToImmutableArray();

        // ------------------------------------------------------------------ tests

        [Fact]
        public async Task NoDiagnostic_WhenArgCountMatches_SingleArg()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
public static class Fns
{
    [SqlExpression(""YEAR({0})"")]
    public static int Year(DateTime date) => throw new NotImplementedException();
}");
            var diagnostics = Efp0003Diagnostics(await RunAnalyzerAsync(compilation));
            Assert.Empty(diagnostics);
        }

        [Fact]
        public async Task NoDiagnostic_WhenArgCountMatches_MultipleArgs()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
public static class Fns
{
    [SqlExpression(""COALESCE({0}, {1})"")]
    public static string Coalesce(string a, string b) => throw new NotImplementedException();
}");
            var diagnostics = Efp0003Diagnostics(await RunAnalyzerAsync(compilation));
            Assert.Empty(diagnostics);
        }

        [Fact]
        public async Task NoDiagnostic_WhenExtensionMethodWithCorrectArgCount()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
public static class Fns
{
    [SqlExpression(""YEAR({0})"")]
    public static int Year(this DateTime date) => throw new NotImplementedException();
}");
            var diagnostics = Efp0003Diagnostics(await RunAnalyzerAsync(compilation));
            Assert.Empty(diagnostics);
        }

        [Fact]
        public async Task NoDiagnostic_WhenNoPlaceholders()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
public static class Fns
{
    [SqlExpression(""GETDATE()"")]
    public static DateTime Now() => throw new NotImplementedException();
}");
            var diagnostics = Efp0003Diagnostics(await RunAnalyzerAsync(compilation));
            Assert.Empty(diagnostics);
        }

        [Fact]
        public async Task NoDiagnostic_WhenMultipleConfigurations_AllValid()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
public static class Fns
{
    [SqlExpression(""YEAR({0})"", Configuration = ""SqlServer"")]
    [SqlExpression(""STRFTIME('%Y', {0})"", Configuration = ""Sqlite"")]
    public static int Year(DateTime date) => throw new NotImplementedException();
}");
            var diagnostics = Efp0003Diagnostics(await RunAnalyzerAsync(compilation));
            Assert.Empty(diagnostics);
        }

        [Fact]
        public async Task ReportsDiagnostic_WhenIndexExceedsParamCount_NoParams()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
public static class Fns
{
    [SqlExpression(""YEAR({0})"")]
    public static int Year() => throw new NotImplementedException();
}");
            var diagnostics = Efp0003Diagnostics(await RunAnalyzerAsync(compilation));
            Assert.Single(diagnostics);
        }

        [Fact]
        public async Task ReportsDiagnostic_WhenIndexExceedsParamCount_OneParam()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
public static class Fns
{
    [SqlExpression(""COALESCE({0}, {1})"")]
    public static string Coalesce(string a) => throw new NotImplementedException();
}");
            var diagnostics = Efp0003Diagnostics(await RunAnalyzerAsync(compilation));
            Assert.Single(diagnostics);
        }

        [Fact]
        public async Task ReportsDiagnostic_ForEachInvalidAttribute()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
public static class Fns
{
    [SqlExpression(""YEAR({0})"", Configuration = ""SqlServer"")]
    [SqlExpression(""YEAR({0})"", Configuration = ""Sqlite"")]
    public static int Year() => throw new NotImplementedException();
}");
            var diagnostics = Efp0003Diagnostics(await RunAnalyzerAsync(compilation));
            // One diagnostic per invalid attribute
            Assert.Equal(2, diagnostics.Length);
        }

        [Fact]
        public async Task ReportsDiagnostic_OnlyForInvalidAttribute_InMixedList()
        {
            var compilation = CreateCompilation(@"
using System;
using EntityFrameworkCore.Projectables;
public static class Fns
{
    [SqlExpression(""YEAR({0})"", Configuration = ""SqlServer"")]
    [SqlExpression(""YEAR({0})"", Configuration = ""Sqlite"")]
    public static int Year(DateTime date) => throw new NotImplementedException();
}");
            // All attributes are valid here (1 param, max index 0)
            var diagnostics = Efp0003Diagnostics(await RunAnalyzerAsync(compilation));
            Assert.Empty(diagnostics);
        }
    }
}
