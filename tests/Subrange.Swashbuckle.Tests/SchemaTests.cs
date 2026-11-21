using Microsoft.OpenApi;

namespace Subranges.Swashbuckle.Tests;

/// <summary>
/// Every subrange type must be documented as the number it is on the wire, with the bounds a
/// client needs to validate it, wherever it appears: route, query or body, in minimal APIs and in
/// MVC controllers.
/// </summary>
public class SchemaTests
{
    [Theory]
    [InlineData("/minimal/ports/{port}")]
    [InlineData("/controller/ports/{port}")]
    public async Task RouteAndQueryParameters_AreDocumentedWithTheirBounds(string path)
    {
        var document = await OpenApiDocuments.Generate(TestContext.Current.CancellationToken);

        var parameters = document.Paths[path].Operations?[HttpMethod.Get].Parameters ?? [];

        AssertInteger(Assert.Single(parameters, p => p.Name == "port").Schema, "1", "65535");
        AssertDouble(Assert.Single(parameters, p => p.Name == "heat").Schema, "-50", "200");
    }

    [Theory]
    [InlineData(nameof(MinimalBody))]
    [InlineData(nameof(ControllerBody))]
    public async Task BodyProperties_AreDocumentedInlineWithTheirBounds(string schemaName)
    {
        var document = await OpenApiDocuments.Generate(TestContext.Current.CancellationToken);

        var properties =
            document.Components?.Schemas?[schemaName].Properties
            ?? new Dictionary<string, IOpenApiSchema>();

        AssertInteger(properties["port"], "1", "65535");
        Assert.Equal(JsonSchemaType.Integer | JsonSchemaType.Null, properties["fallback"].Type);
        Assert.Equal("65535", properties["fallback"].Maximum);
        AssertInteger(properties["address"], "1", "247");
        AssertDouble(properties["heat"], "-50", "200");
    }

    [Fact]
    public async Task SubrangeTypes_AreNotDocumentedAsSharedComponents()
    {
        var document = await OpenApiDocuments.Generate(TestContext.Current.CancellationToken);

        var componentNames = document.Components?.Schemas?.Keys ?? [];

        Assert.DoesNotContain(nameof(Port), componentNames);
        Assert.DoesNotContain(nameof(DeviceAddress), componentNames);
        Assert.DoesNotContain(nameof(Temperature), componentNames);
    }

    [Fact]
    public async Task AnExplicitMapType_TakesPrecedence()
    {
        var document = await OpenApiDocuments.Generate(TestContext.Current.CancellationToken);

        var overridden = document
            .Components
            ?.Schemas
            ?[nameof(MinimalBody)]
            .Properties
            ?["overridden"];

        Assert.Equal(JsonSchemaType.String, overridden?.Type);
        Assert.Equal("overridden", overridden?.Format);
    }

    private static void AssertInteger(IOpenApiSchema? schema, string minimum, string maximum)
    {
        Assert.NotNull(schema);
        Assert.Equal(JsonSchemaType.Integer, schema.Type);
        Assert.Equal("int32", schema.Format);
        Assert.Equal(minimum, schema.Minimum);
        Assert.Equal(maximum, schema.Maximum);
    }

    private static void AssertDouble(IOpenApiSchema? schema, string minimum, string maximum)
    {
        Assert.NotNull(schema);
        Assert.Equal(JsonSchemaType.Number, schema.Type);
        Assert.Equal("double", schema.Format);
        Assert.Equal(minimum, schema.Minimum);
        Assert.Equal(maximum, schema.Maximum);
    }
}
