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
        "'{0}' is a subrange of '{1}', but only built-in number types such as int, long, double and decimal are supported, not char, Int128, Half or nint",
        _category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor InvalidBounds = new(
        "SR0004",
        "Subrange bounds are invalid",
        "'{0}' must have finite bounds where min <= max, both within '{1}' and stored by it unchanged, and whole numbers no larger than 2^53 in magnitude if '{1}' is an integer type, since the bounds are written as doubles",
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

        if (bounds.MinText is null || bounds.MaxText is null)
            yield return Diagnostic.Create(InvalidBounds, location, type.Name, bounds.ValueType);
    }

    /// <summary>
    /// The value type as C# code and the bounds as invariant text it parses, or null texts when a
    /// bound isn't valid for it. Null when the value type isn't supported.
    /// </summary>
    /// <remarks>
    /// Supported types are those whose <c>MinValue</c> and <c>MaxValue</c> are constants of the
    /// type itself, which the build reads to check the bounds. C# only allows constants of built-in
    /// types, so these are the built-in number types, except <c>char</c>, which isn't used as a
    /// number, and <c>Int128</c>, <c>Half</c> and <c>nint</c>, whose limits are properties.
    /// </remarks>
    public static (string ValueType, string? MinText, string? MaxText)? Bounds(
        AttributeData attribute
    )
    {
        var valueType = attribute.AttributeClass?.TypeArguments.FirstOrDefault();
        if (
            valueType is null
            || valueType.SpecialType == SpecialType.System_Char
            || Limit(valueType, "MinValue") is not { } lowest
            || Limit(valueType, "MaxValue") is not { } highest
        )
            return null;

        var name = valueType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (
            attribute.ConstructorArguments.Length != 2
            || attribute.ConstructorArguments[0].Value is not double min
            || attribute.ConstructorArguments[1].Value is not double max
            || min > max
        )
            return (name, null, null);

        var isInteger = valueType.AllInterfaces.Any(i =>
            i.OriginalDefinition.ToDisplayString() == "System.Numerics.IBinaryInteger<TSelf>"
        );
        string? Text(double value) =>
            double.IsNaN(value) || double.IsInfinity(value) || value < lowest || value > highest
                ? null
            : !isInteger
                ? SurvivesAs(valueType.SpecialType, value)
                        ? value.ToString("R", CultureInfo.InvariantCulture)
                    : null
            // A double holds every whole number up to 2^53 exactly; past that, the bound the
            // build sees may not be the one that was written.
            : Math.Floor(value) == value && Math.Abs(value) <= _largestExactInteger
                ? ((long)value).ToString(CultureInfo.InvariantCulture)
            : null;

        return (name, Text(min), Text(max));
    }

    private const double _largestExactInteger = 9007199254740992;

    /// <summary>
    /// Whether the value type stores the bound as written. The generated code parses the bound's
    /// text into the value type, and a type with less range or precision than a double would
    /// otherwise move it silently: 1e-29 becomes 0 as a decimal, and 16777217 becomes 16777216
    /// as a float. The parser would then accept values outside the declared bounds.
    /// </summary>
    private static bool SurvivesAs(SpecialType valueType, double bound)
    {
        var text = bound.ToString("R", CultureInfo.InvariantCulture);
        var stored = valueType switch
        {
            SpecialType.System_Single => float.Parse(
                    text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture
                )
                .ToString("R", CultureInfo.InvariantCulture),
            SpecialType.System_Decimal => decimal.Parse(
                    text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture
                )
                .ToString(CultureInfo.InvariantCulture),
            _ => text,
        };
        return double.Parse(stored, NumberStyles.Float, CultureInfo.InvariantCulture) == bound;
    }

    private static double? Limit(ITypeSymbol type, string name) =>
        type.GetMembers(name)
            .OfType<IFieldSymbol>()
            .FirstOrDefault(field =>
                field.IsStatic
                && field.HasConstantValue
                && SymbolEqualityComparer.Default.Equals(field.Type, type)
            )
            ?.ConstantValue
            is { } value
            ? Convert.ToDouble(value, CultureInfo.InvariantCulture)
            : null;

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
