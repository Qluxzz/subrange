using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Subranges;
using Swashbuckle.AspNetCore.Swagger;

// Outside the Subranges namespaces, so AddSubrangesToSwaggerGen is only found the way an
// application finds it.
namespace Subrange.Swashbuckle.Tests;

[Subrange<int>(1, 65535)]
public sealed partial record Port;

[Subrange<byte>(1, 247)]
public sealed partial record DeviceAddress;

[Subrange<double>(-50, 200)]
public sealed partial record Temperature;

[Subrange<int>(1, 10)]
public sealed partial record Overridden;

[Subrange<decimal>(0.01, 1000)]
public sealed partial record Price;

[Subrange<float>(0, 1)]
public sealed partial record Ratio;

[Subrange<long>(0, 9_007_199_254_740_992)]
public sealed partial record Count;

[Subrange<ushort>(1, 65535)]
public sealed partial record Channel;

public sealed record MinimalBody(
    Port Port,
    Port? Fallback,
    DeviceAddress Address,
    Temperature Heat,
    Overridden Overridden,
    Price Price,
    Ratio Ratio,
    Count Count,
    Channel Channel
);

public sealed record ControllerBody(
    Port Port,
    Port? Fallback,
    DeviceAddress Address,
    Temperature Heat
);

[ApiController]
[Route("controller")]
public sealed class SubrangeController : ControllerBase
{
    [HttpGet("ports/{port}")]
    public int GetPort(Port port, [FromQuery] Temperature heat) => port.Value;

    [HttpPost("bodies")]
    public ControllerBody Post(ControllerBody body) => body;
}

public static class OpenApiDocuments
{
    public static async Task<OpenApiDocument> Generate(CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(SubrangeController).Assembly);
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SupportNonNullableReferenceTypes();
            options.MapType<Overridden>(() =>
                new OpenApiSchema { Type = JsonSchemaType.String, Format = "overridden" }
            );
        });
        builder.Services.AddSubrangesToSwaggerGen();

        var app = builder.Build();
        app.MapControllers();
        app.MapGet("/minimal/ports/{port}", (Port port, Temperature heat) => port.Value);
        app.MapPost("/minimal/bodies", (MinimalBody body) => body);
        await app.StartAsync(cancellationToken);

        return app.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
    }
}
