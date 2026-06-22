# Dil

Strongly-typed .NET localization from plain **JSON** files, generated at **build time**.

No resx. No `.Designer.cs`. No IStringLocalizer. No IDE dependency. Just `Strings.Hello`.

```csharp
CultureInfo.CurrentUICulture = new("tr");
Strings.Hello;             // "Merhaba"
Strings.Greeting("Ada");   // "Merhaba, Ada!"
Strings.Inbox(3);          // "3 okunmamış mesajınız var"
```

The class name comes from the JSON file's base name, resx-style: `Strings.json` → `Strings`,
`Errors.json` → `Errors`, `CustomerResources.json` → `CustomerResources`. Each file group is an
independent set, so two sets can use the same key names without clashing.

## Why

`.resx` gives you typed access but drags along XML boilerplate, an IDE-bound designer,
and no cross-platform CLI regeneration. Dil keeps the *one good part* of resx — the typed
`Strings.Hello` accessor that respects the ambient `CurrentUICulture` — and drops the rest:

- **JSON, not XML** — readable, diffable, translator-friendly.
- **Source generator** — the typed classes are regenerated on every `dotnet build`, on any OS. Nothing checked in.
- **Many resource sets** — one generated class per file group (`Strings`, `Errors`, …), each independent.
- **Generic, formattable params** — `{placeholder}` values are generic (`Greeting<T>(T name)`), so `int`/`string`/etc. flow without `object?`, and `IFormattable` values render in the current culture.
- **Live reload** — edits to the JSON files are picked up at runtime (on by default; toggle with `Dil.Loc.LiveReload`).
- **Translations in IntelliSense** — every member's doc comment lists all its translations.
- **Lean runtime** — multi-targets `netstandard2.0`, `net8.0`, and `net10.0`; parses JSON with `System.Text.Json` (in-box on modern .NET) straight into UTF-8 [Glot](https://github.com/kerem-acer/Glot) `Text`, and builds formatted strings with Glot's pooled `TextBuilder`.
- **Ambient culture** — works exactly like resx: set `CultureInfo.CurrentUICulture`, read `Strings.X`.
- **Compile-time safety** — missing translations are reported as build warnings (`DIL001`).

## Install

```
dotnet add package Dil
```

That's it — the package wires the generator and the required MSBuild glue in automatically.

## Use

Mark your localization files with `DilResource="true"`. **The base name becomes the class** and
**the trailing segment is the culture**, resx-style: `Strings.json` is the neutral/default language
of the `Strings` set, `Strings.tr.json` is Turkish, `Strings.de.json` is German,
`Strings.zh-Hans.json` is Simplified Chinese.

```xml
<ItemGroup>
  <AdditionalFiles Include="Resources/Strings.json"    DilResource="true" />
  <AdditionalFiles Include="Resources/Strings.tr.json" DilResource="true" />
  <!-- A second file group -> a second class, `Errors` -->
  <AdditionalFiles Include="Resources/Errors.json"     DilResource="true" />
  <AdditionalFiles Include="Resources/Errors.tr.json"  DilResource="true" />
</ItemGroup>
```

```jsonc
// Resources/Strings.json  (neutral — defines the keys and is the fallback)
{ "hello": "Hello", "greeting": "Hello, {name}!" }

// Resources/Strings.tr.json
{ "hello": "Merhaba", "greeting": "Merhaba, {name}!" }
```

Build, then use the generated classes (they land in your project's `RootNamespace`):

```csharp
using YourRootNamespace;

Console.WriteLine(Strings.Hello);
Console.WriteLine(Strings.Greeting("Ada"));   // generic param: Greeting<T>(T name)
Console.WriteLine(Errors.NotFound("a.json")); // a separate set
```

`{placeholder}` tokens in a value become generic method parameters; plain values become properties.
Add a type to pin a parameter: `{name:string}` generates `string name`, `{count:int}` generates
`int count`. Bare and typed placeholders can mix in one string (`Items<T>(int count, T thing)`).

```jsonc
{ "greet": "Hello, {name:string}!", "items": "{count:int} items" }
// -> string Greet(string name);  string Items(int count);
```

## How file selection works

The generator **only ever sees files you mark `DilResource="true"`** — a source generator
cannot read arbitrary files, only those passed as `AdditionalFiles`. Your `appsettings.json`,
`package.json`, and every other JSON file are invisible to it. There is no folder scan and no
magic filename.

## Setting the culture

Dil reads the ambient `CultureInfo.CurrentUICulture` — set it however your app already does:

- **Console / desktop:** `CultureInfo.CurrentUICulture = new("tr");`
- **ASP.NET Core:** `app.UseRequestLocalization(...)` sets it per request; `Strings.X` just works inside the request.

## IStringLocalizer interop (optional)

Prefer the typed `Strings.Greeting("Ada")` API. But when a framework or library expects the
`Microsoft.Extensions.Localization` abstractions, install the optional **`Dil.Extensions.Localization`**
package — it adapts a Dil resource set to `IStringLocalizer`, `IStringLocalizer<T>`, and
`IStringLocalizerFactory`, with a DI extension:

```csharp
builder.Services.AddDilLocalization();
// optionally: AddDilLocalization(o => { o.BaseDirectory = "..."; o.LiveReload = false; });

public class HomeController(IStringLocalizer<Strings> loc) // T is your generated class -> the "Strings" set
{
    public string Hi() => loc["hello"];          // -> "Merhaba"
    public string Cost() => loc["price", 37.63];  // string.Format positional: "{0:C}" etc.
}
```

The set is `typeof(T).Name`, so `IStringLocalizer<Strings>` reads the `Strings.json` group. Note the
formatting difference: the `IStringLocalizer["key", args]` overload uses **positional** `string.Format`
(`{0}`, `{1:C}`) like resx — so author those values positionally. Dil's **named** `{name}` placeholders
are for the generated typed members; a named template passed through the indexer is returned unformatted
(never throws).

## Diagnostics

| ID       | Severity | Meaning |
|----------|----------|---------|
| `DIL001` | Warning  | A culture file is missing a key defined in its set's neutral file (untranslated string). |
| `DIL002` | Warning  | A resource set has culture files but no neutral file to define its keys. |

Treat them as errors if you want a hard guarantee that every string is translated:

```xml
<PropertyGroup>
  <WarningsAsErrors>$(WarningsAsErrors);DIL001</WarningsAsErrors>
</PropertyGroup>
```

## Notes

- Missing keys fall back: `tr-TR` → `tr` → neutral → the key itself. Fallback is per set.
- Only string values are used; numbers, objects, and arrays are ignored. Comments and trailing commas are tolerated, so `.jsonc` works.
- **Live reload** is on by default — editing a resource file is picked up at runtime via a `FileSystemWatcher`. Turn it off with `Dil.Loc.LiveReload = false` (e.g. in production).
- `Dil.Loc.Configure(baseDirectory)` overrides where files are loaded from / forces a reload.
- Placeholder values are formatted with the current culture via `IFormattable`; a `null` value becomes the empty string.

## Build from source

```
dotnet build                                    # build everything
dotnet run --project sample/Dil.Sample          # run the demo
dotnet run --project tests/Dil.Tests            # run the TUnit tests
dotnet pack src/Dil -c Release -o artifacts     # produce the NuGet package
```

## Project layout

```
src/Dil/                       runtime (netstandard2.0/net8.0/net10.0) + build/ props & targets
src/Dil.Generator/             incremental source generator + DIL001/DIL002 diagnostics
src/Dil.Extensions.Localization/  optional IStringLocalizer / DI adapter
sample/Dil.Sample/             runnable console demo
sample/Dil.Localization.Sample/  IStringLocalizer + DI demo
tests/                         TUnit tests for the runtime, generator, and the adapter
```

## License

MIT
