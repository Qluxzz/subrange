using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Subranges.Generators;

namespace Subranges.Tests;

public class GeneratorDiagnosticsTests
{
    [Theory]
    [InlineData(
        "SR0001",
        """
            [Subrange<int>(1, 10)]
            public sealed partial class NotARecord;
            """
    )]
    [InlineData(
        "SR0001",
        """
            [Subrange<int>(1, 10)]
            public sealed record NotPartial;
            """
    )]
    [InlineData(
        "SR0001",
        """
            public static partial class Outer
            {
                [Subrange<int>(1, 10)]
                public sealed partial record Nested;
            }
            """
    )]
    [InlineData(
        "SR0002",
        """
            [Subrange<int>(1, 10)]
            public sealed partial record PrimaryConstructor(int Value);
            """
    )]
    [InlineData(
        "SR0001",
        """
            [Subrange<int>(1, 10)]
            public partial record NotSealedRange;
            """
    )]
    [InlineData(
        "SR0002",
        """
            [Subrange<int>(1, 10)]
            public sealed partial record RangeWithConstructor
            {
                public RangeWithConstructor(string value) { }
            }
            """
    )]
    [InlineData(
        "SR0003",
        """
            [Subrange<long>(1, 10)]
            public sealed partial record UnsupportedValueType;
            """
    )]
    [InlineData(
        "SR0004",
        """
            [Subrange<int>(10, 1)]
            public sealed partial record MinAboveMax;
            """
    )]
    [InlineData(
        "SR0004",
        """
            [Subrange<int>(0.5, 10)]
            public sealed partial record FractionalIntBound;
            """
    )]
    [InlineData(
        "SR0004",
        """
            [Subrange<byte>(0, 256)]
            public sealed partial record ByteBoundOutOfRange;
            """
    )]
    [InlineData(
        "SR0004",
        """
            [Subrange<double>(double.NegativeInfinity, 1)]
            public sealed partial record InfiniteBound;
            """
    )]
    public async Task MisshapenTypes_AreACompileError(string expectedId, string source)
    {
        var diagnostics = await Compile($"using Subranges;\n{source}");

        Assert.Contains(expectedId, diagnostics.Select(d => d.Id));
    }

    [Fact]
    public async Task WellShapedTypes_CompileWithoutErrors()
    {
        var diagnostics = await Compile(
            """
            using Subranges;

            [Subrange<byte>(1, byte.MaxValue)]
            public sealed partial record FineByte;

            [Subrange<int>(1, 65535)]
            public sealed partial record WithOwnMembers
            {
                public static WithOwnMembers Https { get; } = Parse(443);

                public bool IsWellKnown => Value < 1024;
            }

            [Subrange<int>(int.MinValue, int.MaxValue)]
            public sealed partial record FineInt;

            [Subrange<double>(-0.5, 1e300)]
            public sealed partial record FineDouble;
            """
        );

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    private static async Task<ImmutableArray<Diagnostic>> Compile(string source)
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
            .Split(Path.PathSeparator)
            .Append(typeof(SubrangeAttribute<>).Assembly.Location)
            .Select(path => MetadataReference.CreateFromFile(path));

        var compilation = CSharpCompilation.Create(
            nameof(GeneratorDiagnosticsTests),
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );

        CSharpGeneratorDriver
            .Create(new SubrangeGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var generated, out _);

        return await generated
            .WithAnalyzers([new SubrangeAnalyzer()])
            .GetAllDiagnosticsAsync(TestContext.Current.CancellationToken);
    }
}
