using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace EntityFrameworkCore.Projectables.Generator
{
    /// <summary>
    /// Validates that <c>[SqlExpression]</c> SQL templates do not reference argument indices that
    /// are out of range for the decorated method's parameter list.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class SqlExpressionAnalyzer : DiagnosticAnalyzer
    {
        private const string SqlExpressionAttributeFullName = "EntityFrameworkCore.Projectables.SqlExpressionAttribute";

        // Matches any {N} placeholder in the SQL template
        private static readonly Regex PlaceholderPattern =
            new Regex(@"\{(\d+)\}", RegexOptions.Compiled);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(Diagnostics.SqlExpressionArgumentCountMismatch);

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
        }

        private static void AnalyzeMethod(SymbolAnalysisContext context)
        {
            var method = (IMethodSymbol)context.Symbol;

            var sqlExprAttrType = context.Compilation.GetTypeByMetadataName(SqlExpressionAttributeFullName);
            if (sqlExprAttrType is null)
                return;

            var paramCount = method.Parameters.Length;

            foreach (var attr in method.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, sqlExprAttrType))
                    continue;

                if (attr.ConstructorArguments.Length == 0)
                    continue;

                var sqlTemplate = attr.ConstructorArguments[0].Value as string;
                if (sqlTemplate is null)
                    continue;

                var maxIndex = -1;
                foreach (Match m in PlaceholderPattern.Matches(sqlTemplate))
                {
                    var idx = int.Parse(m.Groups[1].Value);
                    if (idx > maxIndex)
                        maxIndex = idx;
                }

                if (maxIndex >= paramCount)
                {
                    var location = attr.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                        ?? method.Locations[0];

                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.SqlExpressionArgumentCountMismatch,
                        location,
                        maxIndex,          // {0} – the out-of-range index referenced
                        paramCount,        // {1} – how many parameters the method has
                        paramCount - 1));  // {2} – the maximum valid index
                }
            }
        }
    }
}
