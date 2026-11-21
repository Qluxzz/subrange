using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Subranges.AspNetCore.Tests;

[Subrange<int>(1, 65535)]
public sealed partial record Port;

[Subrange<double>(-50, 200)]
public sealed partial record Temperature;

public sealed record Settings(Port Port, Port? Fallback, Temperature Temperature);

/// <summary>The same endpoints as <see cref="TestApp.MapMinimalApis"/>, as an MVC controller.</summary>
[ApiController]
[Route("controller")]
public sealed class SubrangeController : ControllerBase
{
    [HttpGet("ports/{port}")]
    public int GetRoutePort(Port port) => port.Value;

    [HttpGet("ports")]
    public int GetQueryPort([FromQuery] Port port) => port.Value;

    [HttpPost("settings")]
    public Settings PostSettings(Settings settings) => settings;
}

public static class TestApp
{
    public static readonly TheoryData<string> Styles = new() { "/minimal", "/controller" };

    /// <summary>A host configured the way the README recommends, on a TestServer.</summary>
    public static async Task<HttpClient> Start(CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        // MVC controllers and minimal APIs each have their own JsonSerializerOptions, and the
        // tests run every case through both, so each needs RespectNullableAnnotations.
        // Without it, a JSON null reaches a non-nullable subrange property, because
        // System.Text.Json never passes null to the type's converter.
        builder
            .Services.AddControllers()
            .AddApplicationPart(typeof(SubrangeController).Assembly)
            .AddJsonOptions(options =>
                options.JsonSerializerOptions.RespectNullableAnnotations = true
            );
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.RespectNullableAnnotations = true
        );

        var app = builder.Build();
        app.MapControllers();
        MapMinimalApis(app);
        await app.StartAsync(cancellationToken);

        return app.GetTestClient();
    }

    private static void MapMinimalApis(WebApplication app)
    {
        app.MapGet("/minimal/ports/{port}", (Port port) => port.Value);
        app.MapGet("/minimal/ports", (Port port) => port.Value);
        app.MapPost("/minimal/settings", (Settings settings) => settings);
    }
}
