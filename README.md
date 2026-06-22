# Dil

Strongly-typed .NET localization from plain **JSON** files, generated at **build time**.

No resx. No `.Designer.cs`. No IStringLocalizer. No IDE dependency. Just `Resources.Hello`.

```csharp
CultureInfo.CurrentUICulture = new("tr");
Resources.Hello;              // "Merhaba"
Resources.SayHelloTo("Kerem") // "Merhaba Kerem!"
Resources.Items(3)            // "3 öğeniz var"
```

## Why

`.resx` gives you typed access but drags along XML boilerplate, an IDE-bound designer,
and no cross-platform CLI regeneration. Dil keeps the *one good part* of resx — the typed
`Resources.Hello` accessor that respects the ambient `CurrentUICulture` — and drops the rest:

- **JSON, not XML** — readable, diffable, translator-friendly.
- **Source generator** — the typed class is regenerated on every `dotnet build`, on any OS. Nothing checked in.
- **Zero dependencies** — the runtime is a single `netstandard2.0` assembly.
- **Ambient culture** — works exactly like resx: set `CultureInfo.CurrentUICulture`, read `Resources.X`.

## How it works

1. `l.json` defines the **keys** and the **default/fallback** language.
2. Other languages are `xx.json` files (e.g. `tr.json`, `de.json`).
3. The source generator reads `l.json` (added as `AdditionalFiles`) and emits a
   `Resources` class — a property per key, or a method per key that contains `{placeholder}` tokens.
4. At runtime, `Dil.Loc` loads `L/*.json` from the output folder and resolves each key against
   `CurrentUICulture`, falling back to the parent culture, then to `l.json`.

## Setup

```
dotnet add package Dil
```

Add to your `.csproj`:

```xml
<ItemGroup>
  <CompilerVisibleProperty Include="RootNamespace" />
  <AdditionalFiles Include="L/l.json" />
  <None Update="L/*.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

Create your files:

```jsonc
// L/l.json  (default + key definitions)
{ "hello": "Hello", "sayHelloTo": "Hello {name}!" }

// L/tr.json
{ "hello": "Merhaba", "sayHelloTo": "Merhaba {name}!" }
```

Build, then use the generated class:

```csharp
using YourRootNamespace; // Resources lands in your project's RootNamespace

Console.WriteLine(Resources.Hello);
Console.WriteLine(Resources.SayHelloTo("Kerem"));
```

## Setting the culture

Dil reads the ambient `CultureInfo.CurrentUICulture` — set it however your app already does:

- **Console / desktop:** `CultureInfo.CurrentUICulture = new("tr");`
- **ASP.NET Core:** add `app.UseRequestLocalization(...)` — it sets `CurrentUICulture` per request, and `Resources.X` just works inside that request.

## Notes

- Placeholders `{name}` become typed parameters; `{0}`-style numeric tokens are ignored.
- Missing keys fall back: `tr-TR` → `tr` → `l.json` → the key itself.
- JSON values must be strings.
- `Dil.Loc.Configure("L")` lets you point at a different folder or force a reload.

## Project layout

```
src/Dil/             runtime (netstandard2.0, zero deps)
src/Dil.Generator/   incremental source generator
sample/Dil.Sample/   runnable console demo
```

Run the demo: `dotnet run --project sample/Dil.Sample`
