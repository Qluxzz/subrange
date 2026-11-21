using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Subranges;

namespace Subranges.Tests;

public class SubrangeTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(65535)]
    public void Parse_AcceptsTheBounds(int value)
    {
        Assert.Equal(value, Port.Parse(value).Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void Parse_ThrowsForAValueOutsideTheBounds(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Port.Parse(value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void OutOfRangeValues_AreRejected_NeverClamped(int value)
    {
        Assert.False(Port.TryParse(value, out var result));
        Assert.Null(result);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-50.1)]
    [InlineData(200.1)]
    public void TryParse_RejectsNonFiniteAndOutOfRangeNumbers(double value)
    {
        Assert.False(Temperature.TryParse(value, out var result));
        Assert.Null(result);
    }

    [Fact]
    public void Parse_TellsTextThatIsNotANumberApartFromAnOutOfRangeNumber()
    {
        Assert.Equal(
            "'abc' is not a number.",
            Assert
                .Throws<FormatException>(() => Port.Parse("abc", CultureInfo.InvariantCulture))
                .Message
        );
        Assert.Equal(
            "Port must be between 1 and 65535.",
            Assert
                .Throws<FormatException>(() => Port.Parse("0", CultureInfo.InvariantCulture))
                .Message
        );
    }

    [Theory]
    [InlineData("502", true)]
    [InlineData("0", false)]
    [InlineData("abc", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryParse_AcceptsOnlyTextOfNumbersWithinTheBounds(string? text, bool expected)
    {
        Assert.Equal(expected, Port.TryParse(text, CultureInfo.InvariantCulture, out var result));
        Assert.Equal(expected, result is not null);
    }

    [Fact]
    public void Equality_ComparesTheValue()
    {
        Assert.Equal(Port.Parse(502), Port.Parse(502));
        Assert.NotEqual(Port.Parse(502), Port.Parse(503));
    }

    [Fact]
    public void Json_WritesAndReadsTheBareNumber()
    {
        Assert.Equal("12.5", JsonSerializer.Serialize(Temperature.Parse(12.5)));
        Assert.Equal(Port.Parse(502), JsonSerializer.Deserialize<Port>("502"));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("1.5")]
    [InlineData("\"502\"")]
    [InlineData("{\"value\":502}")]
    public void Json_RejectsAnythingButANumberWithinTheBounds(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Port>(json));
    }

    [Fact]
    public void SubrangeTypes_CanOnlyBeConstructedByParsing()
    {
        var reachable = new[] { typeof(Port), typeof(Temperature), typeof(Address) }
            .SelectMany(t =>
                t.GetConstructors(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                )
            )
            .Where(c => !c.IsPrivate)
            .Select(c => $"{c.DeclaringType?.Name}({c.GetParameters().Length} parameters)");

        Assert.Empty(reachable);
    }
}

[Subrange<int>(1, 65535)]
public sealed partial record Port;

[Subrange<double>(-50, 200)]
public sealed partial record Temperature;

[Subrange<byte>(1, byte.MaxValue)]
public sealed partial record Address;
