# Subrange

[![NuGet](https://img.shields.io/nuget/v/Subrange.svg)](https://www.nuget.org/packages/Subrange)

A number type that only holds values within its bounds. A value outside them is rejected, never clamped. Check a number once where it enters your application, and no code after that has to remember its bounds.

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

Bounds are inclusive, and the value type can be a built-in number type such as `int`, `ushort`, `long`, `float`, `double` or `decimal`. The generated constructor is private, so parsing is the only way to get a value. Each type gets `Value`, `Min`, `Max`, `Parse` and `TryParse` for numbers and text, and a JSON converter that reads and writes a plain number.

## ASP.NET Core

Route, query and JSON body values bind without any registration, and a value outside the bounds is a 400 before the handler runs:

```csharp
app.MapGet("/items", (PageSize pageSize) => pageSize.Value); // ?pageSize=500 is a 400
```

## OpenAPI

[Subrange.Swashbuckle](https://www.nuget.org/packages/Subrange.Swashbuckle) documents every subrange type in Swashbuckle's OpenAPI schemas, with its bounds.
