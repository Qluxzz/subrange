# Subrange

A number type that only holds values within its bounds. A value outside them is rejected, never clamped.

```bash
dotnet add package Subrange
```

```csharp
using Subranges;

[Subrange<int>(1, 100)]
public sealed partial record PageSize;

var pageSize = PageSize.Parse(50);           // pageSize.Value is 50
var valid = PageSize.TryParse(500, out _);   // false
PageSize.Parse(500);                         // throws ArgumentOutOfRangeException
```

Bounds are inclusive, and the value type is `byte`, `int` or `double`. The generated constructor is private, so parsing is the only way to get a value. Each type gets `Value`, `Min`, `Max`, `Parse` and `TryParse` for numbers and text, and a JSON converter that reads and writes a plain number.

## ASP.NET Core

Route, query and JSON body values bind without any registration, and a value outside the bounds is a 400 before the handler runs:

```csharp
app.MapGet("/items", (PageSize pageSize) => pageSize.Value); // ?pageSize=500 is a 400
```

## OpenAPI

```bash
dotnet add package Subrange.Swashbuckle
```

```csharp
builder.Services.AddSubrangesToSwaggerGen();
```

Every subrange type is documented as the `integer` or `number` it is on the wire, with its `minimum` and `maximum`.
