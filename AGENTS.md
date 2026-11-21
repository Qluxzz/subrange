**Purpose:** How to build and test this repo, what the package exists to guarantee, and the design rules that keep new features in line with that guarantee. `README.md` is the user-facing spec; read it before changing behaviour.

# Commands

CSharpier is a local tool, so it is never on `PATH`; always go through `dotnet tool run`.

```bash
dotnet tool restore                          # once per clone
dotnet tool run csharpier check .            # CI gate; run before you call anything done
dotnet tool run csharpier format .           # fix formatting in place
dotnet test                                  # builds and runs every test project
dotnet test --project tests/Subrange.Tests   # one project; a bare directory argument no longer works
```

CI (`.github/workflows/ci.yml`) runs exactly these, then `dotnet pack` for each package. Restore runs in locked mode on CI only, so after a package change run `dotnet restore --force-evaluate` and commit the lock files. Packages are versioned centrally in `Directory.Packages.props`; never put a `Version=` on a `PackageReference`.

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

Every new `SR` code needs a test in `GeneratorDiagnosticsTests` that triggers it, a row in the README's "Compile errors" table, and, if it constrains valid types, a case in `WellShapedTypes_CompileWithoutErrors` showing the valid shape still compiles.

# How to work in this repo

- **Read before you write.** Look at the existing implementation, its tests and the README section before changing behaviour.
- **Make the smallest coherent change.** If you spot something worth fixing elsewhere, mention it rather than folding it in.
- **Update `README.md` in the same change** as any behaviour change. It is the spec users read, and its examples must compile.
- **A function's name has to earn the jump.** Inline single-use helpers unless the name tells the reader something the code doesn't, or isolates an invariant.

# Tests

| Project | Tests |
|---|---|
| `tests/Subrange.Tests` | Generated behaviour through its public API, and every `SR` diagnostic |
| `tests/Subrange.AspNetCore.Tests` | Values outside the bounds are a 400 before the handler, through a `TestServer` |
| `tests/Subrange.Swashbuckle.Tests` | The published OpenAPI schema |

- **Test at the highest level that still reaches the cases.** For binding, that is an HTTP request through `TestServer`; for the schema, the generated OpenAPI document. Drive generated types through `Parse`, `TryParse` and JSON, never their internals.
- **Name tests after the rule they protect** (`OutOfRangeValues_AreRejected_NeverClamped`), not the method under test.
- **Check both directions.** A diagnostic needs a test that triggers it and a valid shape that doesn't. Before finishing, break the implementation briefly, watch the test fail, then restore it. A break that stops the code compiling proves nothing; break it in a way that still builds.

# Comments

A comment earns its place only by explaining a why the code can't show: a library or protocol constraint, a deliberate trade-off, an invariant the types can't carry. Never restate the code, and never narrate the change in the source; that belongs in the commit message.
