**Purpose:** How to build and test this repo, what the package exists to guarantee, and the design rules that keep new features in line with that guarantee. The package READMEs (`README.md` for `Subrange`, `src/Subrange.Swashbuckle/README.md`) are the user-facing spec; read them before changing behaviour.

# Commands

CSharpier is a local tool, so it is never on `PATH`; always go through `dotnet tool run`.

```bash
dotnet tool restore                          # once per clone
dotnet tool run csharpier check .            # CI gate; run before you call anything done
dotnet tool run csharpier format .           # fix formatting in place
dotnet test                                  # builds and runs every test project
dotnet test --project tests/Subrange.Tests   # one project; a bare directory argument no longer works
```

CI (`.github/workflows/ci.yml`) runs exactly these checks. Publishing a GitHub release runs `publish.yml`, which calls `ci.yml`, so a check added there gates releases too, then packs each package with the release's tag as the version and pushes it to nuget.org. Packages are only built there, so `publish.yml` is the one list of what ships: a new package needs a `dotnet pack` line there, and a change to packaging should be tried locally with `dotnet pack` before it's released. Restore runs in locked mode on CI only, so after a package change run `dotnet restore --force-evaluate` and commit the lock files. Dependency versions are central in `Directory.Packages.props`; never put a `Version=` on a `PackageReference`. The version of the packages this repo ships comes from the release tag.

While iterating, run the narrowest test project. Before finishing, run the full `dotnet test` and `csharpier check .`. **Report what you didn't run** instead of implying the work is verified.

# What the build enforces

- **`TreatWarningsAsErrors`** is on everywhere, so an analyzer warning fails `dotnet test`.
- **No `!` (null-forgiving operator)**, enforced by `Nullable.Extended.Analyzer`. In tests use `Assert.NotNull` or `Assert.IsType<T>`, which narrow the type; elsewhere fix the type, or use `?? throw` with enough context to debug the broken invariant.
- **The generator targets `netstandard2.0`** with `LangVersion` latest, because it loads inside the compiler. `Microsoft.CodeAnalysis.CSharp` must not be newer than the compiler in the SDK that `global.json` pins, or the generator fails to load in consuming projects.

# What this package is for

Subrange does one thing: **a number type that can only hold values within its bounds**, so no caller has to remember the bounds. Every feature is judged by whether it strengthens that guarantee. Things that aren't about bounded numbers, such as validated strings or non-empty collections, belong in other packages.

Background reading: [Parse, don't validate](https://lexi-lambda.github.io/blog/2019/11/05/parse-don-t-validate/), [Making illegal states unrepresentable](https://fsharpforfunandprofit.com/posts/designing-with-types-making-illegal-states-unrepresentable/), [The wrong abstraction](https://sandimetz.com/blog/2016/1/20/the-wrong-abstraction).

The guarantees every generated type keeps:

- **Parse, don't validate.** The only constructor is private and generated; the only way to get an instance is `Parse`/`TryParse` (or the JSON converter, which calls it). The type is a `sealed record`, not a struct, because a struct's `default` would be a value nobody checked.
- **Reject, never clamp.** A value outside the bounds is an error. Silently moving it to the nearest bound would change what the caller asked for.
- **Parse once, at the edge.** The generated `IParsable<T>` and `JsonConverter` are the framework's own boundary hooks, so ASP.NET Core rejects invalid route, query and body values with a 400 before a handler runs. Invalid JSON input throws `JsonException`, never another exception type, because anything else becomes a 500.
- **Keep the published schema honest.** A subrange type is documented as the `integer` or `number` it is on the wire, with the same `minimum` and `maximum` the parser enforces.

# Design decisions

These are settled; don't reopen them without a new reason.

- **A `sealed record`, not a struct.** A struct always has a `default` value that skips any constructor, so a struct type could hold a value nobody checked. The cost is one small allocation per value, which the GC makes cheap for short-lived values.
- **When a subrange fits.** A value whose valid range is fixed and doesn't depend on any other value: page sizes, coordinates, percentages, protocol limits like a Modbus device address (1 to 247). Not a value whose range depends on another one: a day of the month is 1 to 31 only in some months, so it belongs in a type that models the whole date.
- **The built-in number types.** Supported value types are found through generic math rather than listed: a type implementing `INumber<T>` whose `MinValue` and `MaxValue` are constants of the type itself, which the build reads to check the bounds. C# only allows constants of built-in types, so in practice these are the built-in number types: every one except `char`, which isn't used as a number, and `Int128`, `Half` and `nint`, whose limits are properties the build can't read. The generated code calls the number's members through private generic helpers, so types that implement them explicitly work too.
- **Bounds are doubles.** An attribute can't take a `TValue` argument. A double holds every whole number up to 2^53 exactly, so integer bounds beyond that are rejected rather than silently rounded. Fractional bounds become the shortest text that round-trips the double, which is what was written for anything up to 15 significant digits, so `[Subrange<decimal>(0.01, 1000)]` has a `Min` of exactly `0.01m`.
- **Naming.** The term comes from Pascal and Ada (`type Percent = 0..100`). The package is `Subrange` and the namespace `Subranges`, like `StronglyTypedId` and `StronglyTypedIds`: a namespace and a type in it shouldn't share a name (CA1724).
- **The READMEs are for users.** Each package has its own, packed into it, showing how it works and how to install it, assuming it is on NuGet. Reasoning, diagnostics and contributor details belong here.

# Design rules

## Each fact has one owner

The attribute's bounds own what a valid value is. The parser, `Min`/`Max`, the error message and the OpenAPI schema are all derived from them, never written twice.

## Enforce a rule at the earliest point that can catch it

1. **The type system**: breaking the rule doesn't compile (the private constructor).
2. **The generator's analyzer**: an `SR` diagnostic at build time, for every consumer.
3. **A test**: only reaches this repo, never consumers.
4. **A runtime guard**: last resort, with a comment stating the invariant.

**If the build has to check a setting, make it data on the attribute, not user code.** The generator reads attribute arguments but can never run the consumer's methods. That is why bounds are attribute arguments and there's no validation hook.

## A feature has to earn its complexity across many types

Model the domain, not one caller's need. If one consumer needs something the package doesn't do, prefer a few lines in their code to a general mechanism here.

## Nothing is released yet

There are no consumers to migrate. Change APIs and behaviour directly; don't add backward compatibility, deprecation paths, or diagnostics that point old code at new APIs.

# How the generator is organised

- **`SubrangeRules`** owns every rule: the `SR` descriptors and the checks that read an attribute into what the generator needs. The generator and the analyzer both call it, so a rule is written once.
- **The generator skips** any type with a violation, so broken input never produces code that fails to compile in confusing ways.
- **`SubrangeAnalyzer` reports** the violations. Diagnostics carry locations, which would defeat incremental caching if the generator reported them. Advice that shouldn't block generation must not go through `SubrangeRules.Violations`, or the generator would skip the type.
- **The generator model is a record of strings** so incremental caching compares it by value. Never put a symbol, syntax node or location in a model.
- **Generated code** uses `global::`-qualified names, documents every exception it can throw with `<exception cref>`, and its comments explain why.

Every new `SR` code needs a test in `GeneratorDiagnosticsTests` that triggers it, a row in the table below, and, if it constrains valid types, a case in `WellShapedTypes_CompileWithoutErrors` showing the valid shape still compiles.

| Code | Meaning |
|---|---|
| `SR0001` | The type must be a top-level, non-generic `sealed partial record` |
| `SR0002` | The type must not declare a constructor or primary constructor |
| `SR0003` | The value type must be a built-in number type, other than `char`, `Int128`, `Half` or `nint` |
| `SR0004` | Bounds must be finite, `min <= max`, within the value type, and for integer types whole numbers no larger than 2^53 in magnitude |

# Packaging

| Package | Contains |
|---|---|
| `Subrange` | The attribute (`lib/net10.0/Subrange.dll`) and the generator (`analyzers/dotnet/cs/Subrange.Generators.dll`), so one `PackageReference` brings both and NuGet passes the generator on to every project that references it |
| `Subrange.Swashbuckle` | `AddSubrangesToSwaggerGen`. It depends on `Subrange`, including its generator |

The generator is built against Roslyn 5.0, so consumers need the .NET 10 SDK or a Visual Studio version with that compiler. A project referencing `src/Subrange` directly, like the test projects, must also reference `src/Subrange.Generators` with `OutputItemType="Analyzer"`, because a `ProjectReference` doesn't pass analyzers on. To try a package locally, `dotnet pack src/Subrange -c Release -o ./artifacts` and add `./artifacts` as a package source.

# How to work in this repo

- **Read before you write.** Look at the existing implementation, its tests and the package's README before changing behaviour.
- **Make the smallest coherent change.** If you spot something worth fixing elsewhere, mention it rather than folding it in.
- **Update each affected package's README in the same change** as any behaviour change. They are the spec users read. Their examples introduce the API rather than form complete programs: they must be valid C# syntax and use the API as it really is, but they may be ordered for reading, such as declaring a type before the code that uses it. Public extension methods live in the namespace of the type they extend (`Microsoft.Extensions.DependencyInjection` for `IServiceCollection`), so examples need no extra `using`.
- **A function's name has to earn the jump.** Inline single-use helpers unless the name tells the reader something the code doesn't, or isolates an invariant.

# Tests

| Project | Tests |
|---|---|
| `tests/Subrange.Tests` | Generated behaviour through its public API, and every `SR` diagnostic |
| `tests/Subrange.AspNetCore.Tests` | Values outside the bounds are a 400 before the handler, through a `TestServer`: route, query, repeated query values, and JSON bodies with lists and dictionaries |
| `tests/Subrange.Swashbuckle.Tests` | The published OpenAPI schema |

- **Test at the highest level that still reaches the cases.** For binding, that is an HTTP request through `TestServer`; for the schema, the generated OpenAPI document. Drive generated types through `Parse`, `TryParse` and JSON, never their internals.
- **Name tests after the rule they protect** (`OutOfRangeValues_AreRejected_NeverClamped`), not the method under test.
- **Check both directions.** A diagnostic needs a test that triggers it and a valid shape that doesn't. Before finishing, break the implementation briefly, watch the test fail, then restore it. A break that stops the code compiling proves nothing; break it in a way that still builds.

# Comments

A comment earns its place only by explaining a why the code can't show: a library or protocol constraint, a deliberate trade-off, an invariant the types can't carry. Never restate the code, and never narrate the change in the source; that belongs in the commit message.
