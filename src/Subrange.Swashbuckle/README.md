# Subrange.Swashbuckle

[![NuGet](https://img.shields.io/nuget/v/Subrange.Swashbuckle.svg)](https://www.nuget.org/packages/Subrange.Swashbuckle)

Documents [Subrange](https://www.nuget.org/packages/Subrange) types in Swashbuckle's OpenAPI schemas as the numbers they are on the wire, with their bounds.

```bash
dotnet add package Subrange.Swashbuckle
```

```csharp
builder.Services.AddSwaggerGen();
builder.Services.AddSubrangesToSwaggerGen();
```

A type like `[Subrange<int>(1, 100)] PageSize` is then documented as:

```json
{ "type": "integer", "format": "int32", "minimum": 1, "maximum": 100 }
```

For the built-in number types, the format comes from the value type: `int32`, `int64`, `float`, `double` or `decimal`. Every subrange type is documented inline wherever it appears, in route, query and body values, from any assembly, so there's nothing to register per type. A mapping you add yourself with `MapType<T>` takes precedence.
