using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Subranges.Generators;

/// <summary>
/// Reports why a <c>[Subrange]</c> type gets no generated code. Kept out of the generator because
/// diagnostics carry locations, which would defeat incremental caching.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SubrangeAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => SubrangeRules.All;

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(
            symbolContext =>
            {
                var type = (INamedTypeSymbol)symbolContext.Symbol;
                foreach (
                    var attribute in type.GetAttributes().Where(SubrangeRules.IsSubrangeAttribute)
                )
                foreach (var diagnostic in SubrangeRules.Violations(type, attribute))
                    symbolContext.ReportDiagnostic(diagnostic);
            },
            SymbolKind.NamedType
        );
    }
}
