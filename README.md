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
- **Compile-time safety** — missing translations are reported as build warnings (`DIL001`).

## Install

```
dotnet add package Dil
```

That's it — the package wires the generator and the required MSBuild glue in automatically.

## Use

Mark your localization files with `DilResource="true"`. **The culture comes from the
filename**, resx-style: `App.json` is the neutral/default language, `App.tr.json` is Turkish,
`App.de.json` is German, `App.zh-Hans.json` is Simplified Chinese.

```xml
<ItemGroup>
  <AdditionalFiles Include="Strings/App.json"    DilResource="true" />
  <AdditionalFiles Include="Strings/App.tr.json" DilResource="true" />
</ItemGroup>
```

```jsonc
// Strings/App.json  (neutral — defines the keys and is the fallback)
{ "hello": "Hello", "sayHelloTo": "Hello {name}!" }

// Strings/App.tr.json
{ "hello": "Merhaba", "sayHelloTo": "Merhaba {name}!" }
```

Build, then use the generated class (it lands in your project's `RootNamespace`):

```csharp
using YourRootNamespace;

Console.WriteLine(Resources.Hello);
Console.WriteLine(Resources.SayHelloTo("Kerem"));
```

`{placeholder}` tokens in a value become typed method parameters; plain values become properties.

## How file selection works

The generator **only ever sees files you mark `DilResource="true"`** — a source generator
cannot read arbitrary files, only those passed as `AdditionalFiles`. Your `appsettings.json`,
`package.json`, and every other JSON file are invisible to it. There is no folder scan and no
magic filename.

## Setting the culture

Dil reads the ambient `CultureInfo.CurrentUICulture` — set it however your app already does:

- **Console / desktop:** `CultureInfo.CurrentUICulture = new("tr");`
- **ASP.NET Core:** `app.UseRequestLocalization(...)` sets it per request; `Resources.X` just works inside the request.

## Diagnostics

| ID       | Severity | Meaning |
|----------|----------|---------|
| `DIL001` | Warning  | A culture file is missing a key defined in the neutral file (untranslated string). |
| `DIL002` | Warning  | Resource files were found but none is neutral (no file to define the keys). |

Treat them as errors if you want a hard guarantee that every string is translated:

```xml
<PropertyGroup>
  <WarningsAsErrors>$(WarningsAsErrors);DIL001</WarningsAsErrors>
</PropertyGroup>
```

## Notes

- Missing keys fall back: `tr-TR` → `tr` → neutral → the key itself.
- JSON values must be strings.
- `Dil.Loc.Configure(baseDirectory)` overrides where files are loaded from / forces a reload.

## Build from source

```
dotnet build                                   # build everything
dotnet run --project sample/Dil.Sample         # run the demo
dotnet pack src/Dil -c Release -o artifacts     # produce the NuGet package
```

## Project layout

```
src/Dil/             runtime (netstandard2.0, zero deps) + build/ props & targets
src/Dil.Generator/   incremental source generator + DIL001/DIL002 diagnostics
sample/Dil.Sample/   runnable console demo
```

## License

MIT
