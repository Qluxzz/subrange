using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Subranges.Generators;

/// <summary>
/// The shape a <c>[Subrange]</c> type must have for the generated code to compile and to keep the
/// guarantee that every instance went through its parser. Shared by the generator, which skips
/// types that break a rule, and the analyzer, which reports why.
/// </summary>
internal static class SubrangeRules
{
    public const string MetadataName = "Subranges.SubrangeAttribute`1";

    private const string _category = "Subrange";

    public static readonly DiagnosticDescriptor MustBeSealedPartialRecord = new(
        "SR0001",
        "Subrange type must be a sealed partial record",
        "'{0}' must be a top-level, non-generic 'sealed partial record' to use [Subrange]",
        _category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor MustNotDeclareConstructor = new(
        "SR0002",
        "Subrange type must not declare a constructor",
        "'{0}' must not declare a constructor or primary constructor, the private one is generated so instances can only come from parsing",
        _category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor UnsupportedValueType = new(
        "SR0003",
        "Subrange value type is not supported",
        "'{0}' is a subrange of '{1}', only byte, int and double are supported",
        _category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor InvalidBounds = new(
        "SR0004",
        "Subrange bounds are invalid",
        "'{0}' must have finite bounds where min <= max, both representable as '{1}'",
        _category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static ImmutableArray<DiagnosticDescriptor> All =>
        [MustBeSealedPartialRecord, MustNotDeclareConstructor, UnsupportedValueType, InvalidBounds];

    public static bool IsSubrangeAttribute(AttributeData attribute) =>
        attribute.AttributeClass is { } attributeClass
        && attributeClass.MetadataName == "SubrangeAttribute`1"
        && attributeClass.ContainingNamespace.ToDisplayString() == "Subranges";

    public static IEnumerable<Diagnostic> Violations(INamedTypeSymbol type, AttributeData attribute)
    {
        var location = type.Locations.FirstOrDefault();
        var valueType = attribute.AttributeClass?.TypeArguments.FirstOrDefault();

        foreach (
            var diagnostic in ShapeViolations(type, valueType?.SpecialType ?? SpecialType.None)
        )
            yield return diagnostic;

        if (Bounds(attribute) is not { } bounds)
        {
            yield return Diagnostic.Create(
                UnsupportedValueType,
                location,
                type.Name,
                valueType?.ToDisplayString() ?? "?"
            );
            yield break;
        }

        if (bounds.MinLiteral is null || bounds.MaxLiteral is null)
            yield return Diagnostic.Create(InvalidBounds, location, type.Name, bounds.Keyword);
    }

    /// <summary>
    /// The C# keyword of the value type and the bounds as literals of it, or null literals when a
    /// bound isn't representable. Null when the value type isn't supported.
    /// </summary>
    public static (string Keyword, string? MinLiteral, string? MaxLiteral)? Bounds(
        AttributeData attribute
    )
    {
        var keyword = attribute.AttributeClass?.TypeArguments.FirstOrDefault()?.SpecialType switch
        {
            SpecialType.System_Byte => "byte",
            SpecialType.System_Int32 => "int",
            SpecialType.System_Double => "double",
            _ => null,
        };
        if (keyword is null)
            return null;

        if (
            attribute.ConstructorArguments.Length != 2
            || attribute.ConstructorArguments[0].Value is not double min
            || attribute.ConstructorArguments[1].Value is not double max
            || min > max
        )
            return (keyword, null, null);

        return (keyword, Literal(keyword, min), Literal(keyword, max));
    }

    private static string? Literal(string keyword, double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return null;

        var (lowest, highest) = keyword switch
        {
            "byte" => (byte.MinValue, byte.MaxValue),
            "int" => ((double)int.MinValue, (double)int.MaxValue),
            _ => (double.MinValue, double.MaxValue),
        };
        if (value < lowest || value > highest)
            return null;

        if (keyword == "double")
            return value.ToString("R", CultureInfo.InvariantCulture);

        return Math.Floor(value) == value
            ? ((long)value).ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static IEnumerable<Diagnostic> ShapeViolations(
        INamedTypeSymbol type,
        SpecialType generatedConstructorParameter
    )
    {
        var location = type.Locations.FirstOrDefault();
        var declarations = type
            .DeclaringSyntaxReferences.Select(r => r.GetSyntax())
            .OfType<RecordDeclarationSyntax>()
            .ToList();

        if (
            !type.IsRecord
            || type.TypeKind != TypeKind.Class
            || !type.IsSealed
            || type.IsGenericType
            || type.ContainingType is not null
            || declarations.Count == 0
            || declarations.Any(d => !d.Modifiers.Any(SyntaxKind.PartialKeyword))
        )
            yield return Diagnostic.Create(MustBeSealedPartialRecord, location, type.Name);

        // The analyzer sees the compilation after generation, so the generated private
        // single-value constructor is skipped. A hand-written one with that signature still
        // fails to compile as a duplicate of the generated one.
        if (
            type.InstanceConstructors.Any(c =>
                !c.IsImplicitlyDeclared
                && !(
                    c.DeclaredAccessibility == Accessibility.Private
                    && c.Parameters.Length == 1
                    && c.Parameters[0].Type.SpecialType == generatedConstructorParameter
                )
            ) || declarations.Any(d => d.ParameterList is not null)
        )
            yield return Diagnostic.Create(MustNotDeclareConstructor, location, type.Name);
    }
}
