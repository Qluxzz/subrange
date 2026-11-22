using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Subranges.AspNetCore.Tests;

public sealed class ServerOptions
{
    public Port? Port { get; set; }
    public Temperature? MaxTemperature { get; set; }
    public List<Port> Allowed { get; set; } = [];
}

/// <summary>
/// Options bound from configuration must hold the configured value, or fail to bind when it is
/// outside the bounds, never be silently left null or clamped.
/// </summary>
public class ConfigurationTests
{
    [Fact]
    public void ConfiguredValues_WithinTheBounds_AreBound()
    {
        var options = Bind(
            new()
            {
                ["Server:Port"] = "502",
                ["Server:MaxTemperature"] = "21.5",
                ["Server:Allowed:0"] = "80",
                ["Server:Allowed:1"] = "443",
            }
        );

        Assert.Equal(502, options.Port?.Value);
        Assert.Equal(21.5, options.MaxTemperature?.Value);
        Assert.Equal([80, 443], options.Allowed.Select(port => port.Value));
    }

    [Theory]
    [InlineData("Server:Port", "0")]
    [InlineData("Server:Port", "65536")]
    [InlineData("Server:Port", "1.5")]
    [InlineData("Server:Port", "abc")]
    [InlineData("Server:MaxTemperature", "200.1")]
    public void ConfiguredValues_OutsideTheBounds_FailToBind(string key, string value)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Bind(new() { [key] = value }));

        Assert.Contains(key, error.Message);
    }

    // The binder skips any collection element it can't convert, for every type including int.
    [Fact]
    public void ConfiguredListElements_OutsideTheBounds_AreLeftOut_NeverClamped()
    {
        var options = Bind(
            new()
            {
                ["Server:Allowed:0"] = "80",
                ["Server:Allowed:1"] = "0",
                ["Server:Allowed:2"] = "65536",
            }
        );

        Assert.Equal([80], options.Allowed.Select(port => port.Value));
    }

    private static ServerOptions Bind(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        using var services = new ServiceCollection()
            .Configure<ServerOptions>(configuration.GetSection("Server"))
            .BuildServiceProvider();

        return services.GetRequiredService<IOptions<ServerOptions>>().Value;
    }
}
