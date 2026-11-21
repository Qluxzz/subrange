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
    [InlineData(
        "SR0003",
        """
            [Subrange<char>('a', 'z')]
            public sealed partial record Letter;
            """
    )]
    [InlineData(
        "SR0003",
        """
            [Subrange<System.Int128>(0, 10)]
            public sealed partial record Huge;
            """
    )]
    [InlineData(
        "SR0003",
        """
            [Subrange<System.Half>(0, 1)]
            public sealed partial record Tiny;
            """
    )]
    [InlineData(
        "SR0004",
        """
            [Subrange<long>(0, 9_007_199_254_740_994)]
            public sealed partial record PastExactDoubles;
            """
    )]
    [InlineData(
        "SR0004",
        """
            [Subrange<ulong>(-1, 10)]
            public sealed partial record NegativeUnsigned;
            """
    )]
    [InlineData(
        "SR0004",
        """
            [Subrange<ushort>(0.5, 10)]
            public sealed partial record FractionalUshort;
            """
    )]
    [InlineData(
        "SR0004",
        """
            [Subrange<float>(0, 1e39)]
            public sealed partial record BeyondFloat;
            """
    )]
    [InlineData(
        "SR0004",
        """
            [Subrange<decimal>(0, 1e30)]
            public sealed partial record BeyondDecimal;
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

            [Subrange<int>(1, 100)]
            public sealed partial record PageSize
            {
                public static PageSize Default { get; } = Parse(25);
            }

            [Subrange<int>(int.MinValue, int.MaxValue)]
            public sealed partial record FineInt;

            [Subrange<double>(-0.5, 1e300)]
            public sealed partial record FineDouble;

            [Subrange<sbyte>(sbyte.MinValue, sbyte.MaxValue)]
            public sealed partial record FineSbyte;

            [Subrange<short>(-1, 1)]
            public sealed partial record FineShort;

            [Subrange<ushort>(1, ushort.MaxValue)]
            public sealed partial record FineUshort;

            [Subrange<uint>(0, uint.MaxValue)]
            public sealed partial record FineUint;

            [Subrange<long>(-9_007_199_254_740_992, 9_007_199_254_740_992)]
            public sealed partial record FineLong;

            [Subrange<ulong>(0, 9_007_199_254_740_992)]
            public sealed partial record FineUlong;

            [Subrange<float>(-0.5, 3e38)]
            public sealed partial record FineFloat;

            [Subrange<decimal>(0.01, 1e20)]
            public sealed partial record FineDecimal;
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
