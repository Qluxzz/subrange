# Subrange

Subrange types for .NET 10 and later: a number type that only holds values within its bounds, whose only constructor is private, so every value has been checked.

```csharp
using Subranges;

[Subrange<int>(1, 100)]
public sealed partial record PageSize;

[Subrange<double>(-90, 90)]
public sealed partial record Latitude;

[Subrange<double>(-180, 180)]
public sealed partial record Longitude;

[Subrange<double>(0, 100)]
public sealed partial record Percentage;

var pageSize = PageSize.Parse(50);
PageSize.TryParse(100_000, out _); // false
```

`Latitude` and `Longitude` are both doubles, but a method taking `(Latitude, Longitude)` can't be called with them swapped.

The name comes from Pascal and Ada, where a *subrange type* like `type Percent = 0..100` is a type whose values are a range of another type's.

## Why

A field typed `int` accepts every int, including `0`, negative numbers and `int.MaxValue`. When the domain only allows some of them, every caller has to re-check the bounds, and eventually one forgets. A subrange type checks them once, when the value is created, and carries that guarantee everywhere the value goes.

**A value outside the bounds is rejected, never clamped.** A percentage of 120 is an error to report, not something to quietly turn into 100. That makes these types for the edges of an application, where input arrives and has to be checked, rather than for game-style meters that saturate.

The type is a `sealed record` with a private constructor, not a struct: a struct always has a `default` value that skips any constructor, so the only way to get a `Percentage` is to parse one. The cost is one small allocation per value.

## When a subrange fits

A subrange fits a value whose valid range is fixed and doesn't depend on any other value. Protocol and hardware limits are typical:

```csharp
// Modbus reserves 0 for broadcast and 248-255.
[Subrange<byte>(1, 247)]
public sealed partial record ModbusDeviceAddress;
```

It doesn't fit a value whose range depends on another one. A day of the month is 1 to 31 only in some months, and 29 February only in some years, so `[Subrange<int>(1, 31)]` would accept 31 February. Model the values together instead, for example as a `DateOnly`.

## What you get

Both bounds are inclusive: `[Subrange<int>(1, 100)]` accepts 1 and 100. The value type is `byte`, `int` or `double`. A bound the type can't hold, like `0.5` for an `int` or `256` for a `byte`, is a compile error.

The generator adds:

- `Value`, the static `Min` and `Max`, `OutOfRangeMessage` and `IsInRange`
- `TryParse(TValue, out T?)` and `Parse(TValue)`, which throws `ArgumentOutOfRangeException`
- `IParsable<T>` for text, so ASP.NET Core binds route and query values and returns 400 for invalid ones. Text that isn't a number is told apart from a number out of range
- a JSON converter that accepts only a JSON number within the bounds (not a string, and not a fraction for integer types), writes a bare number, and throws `JsonException`, which ASP.NET Core turns into a 400. It also works as a dictionary key, written as the number's text: `{"443": "https"}`
- value equality, as for any record

## In ASP.NET Core

Nothing needs registering. Minimal APIs and MVC controllers bind subrange types through `IParsable<T>` and the JSON converter, and reject a value outside the bounds with 400 before the handler runs:

```csharp
app.MapGet("/items", (PageSize pageSize) => pageSize.Value);                // query: ?pageSize=50
app.MapGet("/devices/{address}", (ModbusDeviceAddress address) => address.Value); // route: /devices/17
app.MapGet("/readings", (ModbusDeviceAddress[] addresses) => addresses.Length); // ?addresses=1&addresses=17
// body: {"17": "pump"}
app.MapPost("/devices", (Dictionary<ModbusDeviceAddress, string> names) => names.Count);
```

The same holds for `[FromRoute]`, `[FromQuery]` and `[FromBody]` in controllers. Repeated query values bind to an array, which is the collection type minimal APIs support there; a JSON body can hold them anywhere, including in lists and as dictionary keys or values.

You may add your own members, such as a named default (`public static PageSize Default { get; } = Parse(25);`), but not a constructor.

## Null

A property typed `PageSize?` accepts a missing value; one typed `PageSize` should not. System.Text.Json doesn't call converters for JSON `null`, so turn on `RespectNullableAnnotations` in your `JsonSerializerOptions`. Otherwise a `null` lands in a non-nullable property.

## OpenAPI with Swashbuckle

The separate `Subrange.Swashbuckle` package documents every subrange type as the number it is on the wire:

```csharp
builder.Services.AddSwaggerGen(options => options.SupportNonNullableReferenceTypes());
builder.Services.AddSubrangesToSwaggerGen();
```

Without `SupportNonNullableReferenceTypes()`, Swashbuckle documents every reference-typed property as nullable, and subrange types are records, so they're included.

Every `[Subrange<TValue>]` type is documented inline, as `integer` (`int32`) or `number` (`double`) with its `minimum` and `maximum`, wherever Swashbuckle meets it and from any assembly. There's nothing to list or register per type. This applies to body properties and to route and query parameters, in both minimal APIs and MVC controllers. A type you map yourself with `options.MapType<T>(...)` keeps your mapping.

## Compile errors

| Code | Meaning |
|---|---|
| `SR0001` | The type must be a top-level, non-generic `sealed partial record` |
| `SR0002` | The type must not declare a constructor or primary constructor |
| `SR0003` | Only `byte`, `int` and `double` are supported |
| `SR0004` | Bounds must be finite, `min <= max`, and representable as the value type |

## Using it

Requirements:

- **Target framework:** .NET 10 or later.
- **SDK:** .NET 10 or later, or a Visual Studio version that ships the Roslyn 5.0 compiler. The generator is built against Roslyn 5.0 and doesn't load in an older compiler.

| Package | Contains |
|---|---|
| `Subrange` | The attribute (`lib/net10.0/Subrange.dll`) and the generator (`analyzers/dotnet/cs/Subrange.Generators.dll`) |
| `Subrange.Swashbuckle` | `AddSubrangesToSwaggerGen`. It depends on `Subrange`, including its generator |

The package is `Subrange` and the namespace is `Subranges`, like `StronglyTypedId` and `StronglyTypedIds`: a namespace and a type in it shouldn't share a name.

They aren't published to a feed yet. Build them locally:

```bash
dotnet pack src/Subrange -c Release -o ./artifacts
dotnet pack src/Subrange.Swashbuckle -c Release -o ./artifacts
```

and add `./artifacts` as a package source.

If you reference `src/Subrange` as a project instead, also reference the generator. A `ProjectReference` doesn't pass analyzers on:

```xml
<ProjectReference Include="path/to/src/Subrange/Subrange.csproj" />
<ProjectReference
  Include="path/to/src/Subrange.Generators/Subrange.Generators.csproj"
  OutputItemType="Analyzer"
  ReferenceOutputAssembly="false"
/>
```

## Development

```bash
dotnet tool restore
dotnet tool run csharpier check .
dotnet test
```

| Project | Tests |
|---|---|
| `tests/Subrange.Tests` | Generated behaviour, and every compile error |
| `tests/Subrange.AspNetCore.Tests` | Values outside the bounds are rejected with 400 before the handler runs, in the route, the query, repeated query values and JSON bodies with lists and dictionaries, in minimal APIs and MVC controllers, through a `TestServer` |
| `tests/Subrange.Swashbuckle.Tests` | The OpenAPI schema `AddSubrangesToSwaggerGen` produces, for parameters and body properties |

`Microsoft.CodeAnalysis.CSharp` must not be newer than the compiler in the SDK that `global.json` pins, or the generator fails to load in consuming projects.
