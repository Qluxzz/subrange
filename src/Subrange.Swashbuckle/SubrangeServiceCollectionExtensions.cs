using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Subranges.Swashbuckle;

public static class SubrangeServiceCollectionExtensions
{
    /// <summary>
    /// Documents every <see cref="SubrangeAttribute{TValue}"/> type as the number it is on the
    /// wire: an integer or number with its format, minimum and maximum. Types are recognised as
    /// Swashbuckle meets them, from any assembly, so nothing has to be listed.
    /// </summary>
    /// <remarks>
    /// Types are documented inline rather than as shared components, so every property and
    /// parameter carries its own bounds, which is where client generators and validators read
    /// them. A mapping registered with <c>MapType</c> takes precedence.
    /// </remarks>
    public static IServiceCollection AddSubrangesToSwaggerGen(this IServiceCollection services)
    {
        // Post-configured because Swashbuckle copies SwaggerGenOptions' mappings into these options
        // entry by entry; a dictionary set any earlier would be replaced by that copy.
        services.PostConfigure<SchemaGeneratorOptions>(options =>
            options.CustomTypeMappings = new SubrangeTypeMappings(options.CustomTypeMappings)
        );
        services.ConfigureSwaggerGen(options => options.ParameterFilter<SubrangeParameterFilter>());
        return services;
    }

    internal static Func<IOpenApiSchema>? Schema(Type type)
    {
        var subrange = type.GetCustomAttributes(inherit: false)
            .FirstOrDefault(a =>
                a.GetType().IsGenericType
                && a.GetType().GetGenericTypeDefinition() == typeof(SubrangeAttribute<>)
            );
        if (subrange is null)
            return null;

        var valueType = subrange.GetType().GetGenericArguments()[0];
        var isInteger = valueType
            .GetInterfaces()
            .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IBinaryInteger<>));
        var minimum = Bound(subrange, nameof(SubrangeAttribute<int>.Min), isInteger);
        var maximum = Bound(subrange, nameof(SubrangeAttribute<int>.Max), isInteger);
        var format = Type.GetTypeCode(valueType) switch
        {
            TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 => "int64",
            _ when isInteger => "int32",
            TypeCode.Single => "float",
            TypeCode.Decimal => "decimal",
            _ => "double",
        };

        return () =>
            new OpenApiSchema
            {
                Type = isInteger ? JsonSchemaType.Integer : JsonSchemaType.Number,
                Format = format,
                Minimum = minimum,
                Maximum = maximum,
            };
    }

    private static string? Bound(object attribute, string name, bool isInteger) =>
        attribute.GetType().GetProperty(name)?.GetValue(attribute) is double value
            ? isInteger
                ? ((long)value).ToString(CultureInfo.InvariantCulture)
                : value.ToString("R", CultureInfo.InvariantCulture)
            : null;
}

/// <summary>
/// Swashbuckle's type mappings, extended so that a lookup of any subrange type answers with its schema.
/// Explicit mappings are kept and win; only lookups are extended, enumeration lists them alone.
/// </summary>
internal sealed class SubrangeTypeMappings(IDictionary<Type, Func<IOpenApiSchema>> explicitMappings)
    : IDictionary<Type, Func<IOpenApiSchema>>
{
    private readonly ConcurrentDictionary<Type, Func<IOpenApiSchema>?> _subranges = new();

    public bool TryGetValue(Type key, [MaybeNullWhen(false)] out Func<IOpenApiSchema> value)
    {
        if (explicitMappings.TryGetValue(key, out value))
            return true;

        value = _subranges.GetOrAdd(key, SubrangeServiceCollectionExtensions.Schema);
        return value is not null;
    }

    public bool ContainsKey(Type key) => TryGetValue(key, out _);

    public Func<IOpenApiSchema> this[Type key]
    {
        get =>
            TryGetValue(key, out var value)
                ? value
                : throw new KeyNotFoundException($"No schema mapping for {key}.");
        set => explicitMappings[key] = value;
    }

    public ICollection<Type> Keys => explicitMappings.Keys;

    public ICollection<Func<IOpenApiSchema>> Values => explicitMappings.Values;

    public int Count => explicitMappings.Count;

    public bool IsReadOnly => explicitMappings.IsReadOnly;

    public void Add(Type key, Func<IOpenApiSchema> value) => explicitMappings.Add(key, value);

    public void Add(KeyValuePair<Type, Func<IOpenApiSchema>> item) => explicitMappings.Add(item);

    public bool Remove(Type key) => explicitMappings.Remove(key);

    public bool Remove(KeyValuePair<Type, Func<IOpenApiSchema>> item) =>
        explicitMappings.Remove(item);

    public void Clear() => explicitMappings.Clear();

    public bool Contains(KeyValuePair<Type, Func<IOpenApiSchema>> item) =>
        explicitMappings.Contains(item);

    public void CopyTo(KeyValuePair<Type, Func<IOpenApiSchema>>[] array, int arrayIndex) =>
        explicitMappings.CopyTo(array, arrayIndex);

    public IEnumerator<KeyValuePair<Type, Func<IOpenApiSchema>>> GetEnumerator() =>
        explicitMappings.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Route and query values bound through <c>TryParse</c> can be reported by ApiExplorer as plain
/// strings, so the type mapping never applies to them. This points those parameters at the
/// subrange type's schema instead.
/// </summary>
internal sealed class SubrangeParameterFilter : IParameterFilter
{
    public void Apply(IOpenApiParameter parameter, ParameterFilterContext context)
    {
        var type = context.ParameterInfo?.ParameterType ?? context.PropertyInfo?.PropertyType;

        if (
            parameter is OpenApiParameter concreteParameter
            && type is not null
            && SubrangeServiceCollectionExtensions.Schema(type) is not null
        )
            concreteParameter.Schema = context.SchemaGenerator.GenerateSchema(
                type,
                context.SchemaRepository
            );
    }
}
