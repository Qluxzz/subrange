using Subranges;
using Subranges.Swashbuckle;
using Swashbuckle.AspNetCore.SwaggerGen;

// In the namespace of IServiceCollection, like AddSwaggerGen, so the method needs no using.
namespace Microsoft.Extensions.DependencyInjection;

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
}
