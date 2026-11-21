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

    [Fact]
    public void Json_CanUseItAsADictionaryKey()
    {
        var services = new Dictionary<Port, string> { [Port.Parse(443)] = "https" };

        var json = JsonSerializer.Serialize(services);

        Assert.Equal("""{"443":"https"}""", json);
        Assert.Equal(services, JsonSerializer.Deserialize<Dictionary<Port, string>>(json));
    }

    [Fact]
    public void Json_DoubleKeys_RoundTrip_AndRejectNaN()
    {
        var readings = new Dictionary<Temperature, string> { [Temperature.Parse(-12.5)] = "cold" };

        var json = JsonSerializer.Serialize(readings);

        Assert.Equal("""{"-12.5":"cold"}""", json);
        Assert.Equal(readings, JsonSerializer.Deserialize<Dictionary<Temperature, string>>(json));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<Dictionary<Temperature, string>>("""{"NaN":"?"}""")
        );
    }

    [Theory]
    [InlineData("""{"0":"none"}""")]
    [InlineData("""{"1.5":"half"}""")]
    [InlineData("""{"abc":"text"}""")]
    public void Json_RejectsADictionaryKeyOutsideTheBounds(string json)
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<Dictionary<Port, string>>(json)
        );
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
    public void Decimal_BoundsAreExact_AsWritten()
    {
        Assert.Equal(0.01m, Price.Min);
        Assert.True(Price.TryParse(0.01m, out _));
        Assert.False(Price.TryParse(0.009m, out _));
        Assert.False(Price.TryParse(1000.01m, out _));
    }

    [Theory]
    [InlineData(typeof(Price), "19.99")]
    [InlineData(typeof(Ratio), "0.1")]
    [InlineData(typeof(Count), "9007199254740992")]
    [InlineData(typeof(Channel), "65535")]
    [InlineData(typeof(Offset), "-10")]
    public void Json_RoundTripsEveryNumberType_AsABareNumber(Type type, string json)
    {
        var value = JsonSerializer.Deserialize(json, type);

        Assert.NotNull(value);
        Assert.Equal(json, JsonSerializer.Serialize(value, type));
    }

    [Theory]
    [InlineData(typeof(Price), "1000.01")]
    [InlineData(typeof(Ratio), "1.5")]
    [InlineData(typeof(Count), "9007199254740993")]
    [InlineData(typeof(Channel), "0")]
    [InlineData(typeof(Offset), "11")]
    [InlineData(typeof(Channel), "1.5")]
    public void Json_RejectsEveryNumberType_OutsideItsBounds(Type type, string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, type));
    }

    [Fact]
    public void Float_RejectsNaN()
    {
        Assert.False(Ratio.TryParse(float.NaN, out _));
    }

    [Fact]
    public void Text_IsParsedByTheValueType()
    {
        Assert.Equal(19.99m, Price.Parse("19.99", CultureInfo.InvariantCulture).Value);
        Assert.Equal((ushort)443, Channel.Parse("443", CultureInfo.InvariantCulture).Value);
        Assert.False(Channel.TryParse("70000", CultureInfo.InvariantCulture, out _));
    }

    [Fact]
    public void SubrangeTypes_CanOnlyBeConstructedByParsing()
    {
        var reachable = new[]
        {
            typeof(Port),
            typeof(Temperature),
            typeof(Address),
            typeof(Price),
            typeof(Ratio),
            typeof(Count),
            typeof(Channel),
            typeof(Offset),
        }
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

[Subrange<decimal>(0.01, 1000)]
public sealed partial record Price;

[Subrange<float>(0, 1)]
public sealed partial record Ratio;

[Subrange<long>(0, 9_007_199_254_740_992)]
public sealed partial record Count;

[Subrange<ushort>(1, 65535)]
public sealed partial record Channel;

[Subrange<sbyte>(-10, 10)]
public sealed partial record Offset;
