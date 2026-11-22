using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Subranges.Generators;

namespace Subranges.Tests;

public class GeneratedDocumentationTests
{
    [Fact]
    public void TheValidRange_IsDocumented_OnTheTypeAndItsMembers()
    {
        var compilation = Compile(
            """
            using Subranges;

            [Subrange<decimal>(0.01, 1000)]
            public sealed partial record Price;
            """
        );
        var type = compilation.GetTypeByMetadataName("Price");
        Assert.NotNull(type);

        Assert.Contains(
            "from 0.01 to 1000 inclusive",
            type.GetDocumentationCommentXml(
                cancellationToken: TestContext.Current.CancellationToken
            )
        );
        Assert.Contains("0.01", Docs(type, "Min"));
        Assert.Contains("1000", Docs(type, "Max"));
        Assert.All(
            type.GetMembers("Parse").Concat(type.GetMembers("TryParse")),
            member =>
                Assert.Contains(
                    "from 0.01 to 1000 inclusive",
                    member.GetDocumentationCommentXml(
                        cancellationToken: TestContext.Current.CancellationToken
                    )
                )
        );
    }

    [Fact]
    public void TheUsersSummary_IsKept_AlongsideTheGeneratedRange()
    {
        var compilation = Compile(
            """
            using Subranges;

            /// <summary>How many items a page holds.</summary>
            [Subrange<int>(1, 100)]
            public sealed partial record PageSize;
            """
        );
        var type = compilation.GetTypeByMetadataName("PageSize");
        Assert.NotNull(type);

        var docs = type.GetDocumentationCommentXml(
            cancellationToken: TestContext.Current.CancellationToken
        );
        Assert.Contains("How many items a page holds.", docs);
        Assert.Contains("from 1 to 100 inclusive", docs);
        Assert.Empty(
            compilation
                .GetDiagnostics(TestContext.Current.CancellationToken)
                .Where(d => d.Severity >= DiagnosticSeverity.Warning)
        );
    }

    private static string? Docs(INamedTypeSymbol type, string member) =>
        Assert
            .Single(type.GetMembers(member))
            .GetDocumentationCommentXml(cancellationToken: TestContext.Current.CancellationToken);

    private static Compilation Compile(string source)
    {
        var parseOptions = new CSharpParseOptions(
            LanguageVersion.Latest,
            DocumentationMode.Diagnose
        );
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
            .Split(Path.PathSeparator)
            .Append(typeof(SubrangeAttribute<>).Assembly.Location)
            .Select(path => MetadataReference.CreateFromFile(path));

        var compilation = CSharpCompilation.Create(
            nameof(GeneratedDocumentationTests),
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );

        CSharpGeneratorDriver
            .Create([new SubrangeGenerator().AsSourceGenerator()], parseOptions: parseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out var generated, out _);

        return generated;
    }
}
