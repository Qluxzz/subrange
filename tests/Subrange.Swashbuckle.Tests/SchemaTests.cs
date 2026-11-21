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

    [Theory]
    [InlineData("price", "number", "decimal", "0.01", "1000")]
    [InlineData("ratio", "number", "float", "0", "1")]
    [InlineData("count", "integer", "int64", "0", "9007199254740992")]
    [InlineData("channel", "integer", "int32", "1", "65535")]
    public async Task EveryNumberType_IsDocumentedWithItsFormatAndBounds(
        string property,
        string type,
        string format,
        string minimum,
        string maximum
    )
    {
        var document = await OpenApiDocuments.Generate(TestContext.Current.CancellationToken);

        var schema = document.Components?.Schemas?[nameof(MinimalBody)].Properties?[property];

        Assert.NotNull(schema);
        Assert.Equal(
            type == "integer" ? JsonSchemaType.Integer : JsonSchemaType.Number,
            schema.Type
        );
        Assert.Equal(format, schema.Format);
        Assert.Equal(minimum, schema.Minimum);
        Assert.Equal(maximum, schema.Maximum);
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
