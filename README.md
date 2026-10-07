# Dil

[![CI](https://github.com/kerem-acer/Dil/actions/workflows/ci.yml/badge.svg)](https://github.com/kerem-acer/Dil/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Dil.svg)](https://www.nuget.org/packages/Dil)

Strongly-typed .NET localization from plain **JSON** files, generated at **build time**.

No resx. No `.Designer.cs`. No stringly-typed lookups. No IDE dependency. Just `Strings.Hello`.

```csharp
CultureInfo.CurrentUICulture = new("tr");
Strings.Hello;                  // "Merhaba"
Strings.Greeting.Render("Ada"); // "Merhaba, Ada!"
Strings.Greeting.Template;      // "Merhaba, {name}!"
Strings.Inbox.Render(3);        // "3 okunmamış mesajınız var"
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
- **Generic, formattable params** — `{placeholder}` values are generic (`Greeting.Render<T>(T name)`), so `int`/`string`/etc. flow without `object?`, and `IFormattable` values render in the current culture.
- **Live reload** — edits to the JSON files are picked up at runtime (on in Debug builds; toggle with `Dil.Loc.LiveReload`).
- **Translations in IntelliSense** — every member's doc comment lists all its translations.
- **Lean runtime** — multi-targets `netstandard2.0`, `net8.0`, `net10.0`, and `net11.0`; parses JSON with `System.Text.Json` (in-box on modern .NET) and assembles formatted strings with [Glot](https://github.com/kerem-acer/Glot)'s pooled `TextBuilder`.
- **Ambient culture** — works exactly like resx: set `CultureInfo.CurrentUICulture`, read `Strings.X`.
- **Compile-time safety** — missing translations are reported as build warnings (`DIL001`).

## Install

```
dotnet add package Dil
```

That's it — the package wires the generator and the required MSBuild glue in automatically.

## Use

Register one file of each set with `<DilResource Include="..." />`; the rest of the set on disk is
pulled in automatically. **The base name becomes the class** and **the trailing segment is the
culture**, resx-style: `Strings.json` is the neutral (culture-free) file of the `Strings` set,
`Strings.tr.json` is Turkish, `Strings.de.json` is German, `Strings.zh-Hans.json` is Simplified Chinese.
The neutral file is optional (see [Sets without a neutral file](#sets-without-a-neutral-file)).

```xml
<ItemGroup>
  <DilResource Include="Resources/Strings.json" /> <!-- also picks up Strings.tr.json, Strings.de.json, … -->
  <!-- A second file group -> a second class, `Errors` -->
  <DilResource Include="Resources/Errors.json" />
</ItemGroup>
```

A wildcard (`Resources/Strings.*.json`) or a single culture file (`Resources/Strings.en.json`) works
just as well.

```jsonc
// Resources/Strings.json  (neutral — defines the keys and is the fallback)
{ "hello": "Hello", "greeting": "Hello, {name}!" }

// Resources/Strings.tr.json
{ "hello": "Merhaba", "greeting": "Merhaba, {name}!" }
```

Build, then use the generated classes (they land in your project's `RootNamespace`, unless you
[pick another namespace](#namespace)):

```csharp
using YourRootNamespace;

Console.WriteLine(Strings.Hello);
Console.WriteLine(Strings.Greeting.Render("Ada"));   // generic param: Render<T>(T name)
Console.WriteLine(Strings.Greeting.Template);        // "Hello, {name}!" — the raw template
Console.WriteLine(Errors.NotFound.Render("a.json")); // a separate set
```

Plain values become `string` properties. A value with `{placeholder}` tokens becomes a property returning
a small generated struct with two members:

- `Render(...)` fills in the placeholders. Each token becomes a generic method parameter.
- `Template` returns the raw value for the current UI culture with the tokens left in, for callers that
  do their own formatting (for example, an error model that returns both the template and the message).

Both resolve through the same culture fallback as everything else. The struct is an empty
`readonly struct`, so the property allocates nothing and `Strings.Greeting.Render(...)` costs the same
as a plain method call. Its type is named `<Member>Template` (e.g. `Strings.GreetingTemplate`); if that
clashes with a key's member, the key keeps its name and the struct gets a number (`GreetingTemplate2`).

Add a type to pin a parameter: `{name:string}` generates `string name`, `{count:int}` generates
`int count`. Bare and typed placeholders can mix in one string (`Items.Render<T>(int count, T thing)`).

```jsonc
{ "greet": "Hello, {name:string}!", "items": "{count:int} items" }
// -> Greet.Render(string name);  Items.Render(int count);
```

Any C# type works as a typed parameter — including your own (`{total:Money}` → `Money total`), rendered
via `IFormattable`/`ToString()`. The type must be resolvable in the generated class's namespace, or fully-qualified
(`{total:global::MyApp.Money}`). The bare `{total}` form is generic, so it accepts any type without that.

## Sets without a neutral file

A set doesn't need a neutral file. One file per language, each named after its language, is a set too:

```jsonc
// Resources/Strings.en.json
{ "hello": "Hello" }

// Resources/Strings.tr.json
{ "hello": "Merhaba" }
```

Without a neutral file, the set's keys are all the keys found across its culture files, and `DIL001`
reports any culture file that's missing one of them. If neither the current UI culture nor its parents
have a file, a lookup returns the key itself (`"hello"`), unless the set has a default culture.

## Default culture

Name the culture to use when the current one has no value. With `en` as the default, `fr-FR` resolves
as `fr-FR` → `fr` → `en` → key, instead of going straight to the key:

```xml
<PropertyGroup>
  <DilDefaultCulture>en</DilDefaultCulture>                     <!-- project-wide -->
</PropertyGroup>

<ItemGroup>
  <DilResource Include="Resources/Strings.en.json" DefaultCulture="en" /> <!-- per set, overrides the property -->
</ItemGroup>
```

The default is per set, like everything else. If a set has both a neutral file and a default culture,
the default culture comes first, like resx's `NeutralResourcesLanguage`. A regional default falls back
to its parent (`en-US` → `en`). The default culture's file also supplies the `<summary>` text of the
generated members; otherwise it comes from the neutral file.

## Class accessibility

Generated classes are **`internal` by default** — localization tables are usually an implementation
detail of one assembly. Make them `public` project-wide with the `DilAccessibility` property, or per
resource with `Accessibility` metadata on the `<DilResource>` item:

```xml
<PropertyGroup>
  <!-- project-wide default: internal (default) or public -->
  <DilAccessibility>public</DilAccessibility>
</PropertyGroup>

<ItemGroup>
  <!-- override one set back to internal -->
  <DilResource Include="Resources/Strings.json" Accessibility="internal" />
</ItemGroup>
```

A set spans several files (neutral + cultures) but produces one class, so one file decides the set's
accessibility: the **neutral (cultureless) file**, or without one, the default culture's file. Files
pulled in automatically take the `Accessibility`, `DefaultCulture`, `Namespace` and `MemberNames` of the
file you registered. The members themselves stay `public static` — their visibility is already capped by the class.

## Namespace

Generated classes go in the project's `RootNamespace`. To put them somewhere else, name the namespace
project-wide with the `DilNamespace` property, or per set with `Namespace` metadata:

```xml
<PropertyGroup>
  <DilNamespace>MyApp.Localization.Resources</DilNamespace>     <!-- project-wide -->
</PropertyGroup>

<ItemGroup>
  <DilResource Include="Localization/Resources/Strings.json" Namespace="MyApp.Localization.Resources" /> <!-- per set -->
</ItemGroup>
```

Or let the folder decide, like resx does. With `DilNamespaceFromFolder`, a set's namespace is
`RootNamespace` plus the folder its files are in, so a folder of resx files moves to Dil without changing
any `using`:

```xml
<PropertyGroup>
  <!-- Localization/Resources/Strings.json -> MyApp.Localization.Resources.Strings -->
  <DilNamespaceFromFolder>true</DilNamespaceFromFolder>
</PropertyGroup>
```

The first one that's set wins: `Namespace` on the item, then `DilNamespace`, then the folder (when opted
in), then `RootNamespace`. A folder name that isn't a valid identifier is fixed up the way Visual Studio
does it for resx (`My Folder` → `My_Folder`, `2nd` → `_2nd`); an explicit namespace is used as written.
As with accessibility, the file that owns a set decides its namespace.

The namespace only moves the class. A set is still known by its assembly and class name, so
`IStringLocalizer<T>` finds it wherever it lands. It also means that two sets with the same file name in
different folders of one project are still one set, so give them different names.

## Member names

Each key becomes a PascalCase member: `hello` → `Hello`, `save_changes` → `SaveChanges`,
`user.first-name` → `UserFirstName`, `1st` → `_1st`. When two keys come out the same, the later one gets
a number (`FooBar`, `FooBar2`).

resx does it differently: its designer uses a key as the member name whenever the key is a valid C#
identifier. To keep those names when a resx file moves to Dil, set the member names to `Verbatim`,
project-wide with the `DilMemberNames` property, or per set with `MemberNames` metadata:

```xml
<PropertyGroup>
  <DilMemberNames>Verbatim</DilMemberNames>                    <!-- project-wide; default: PascalCase -->
</PropertyGroup>

<ItemGroup>
  <DilResource Include="Localization/Resources.json" MemberNames="Verbatim" /> <!-- per set -->
</ItemGroup>
```

With `Verbatim`, a key that's a valid C# identifier is the member name as written: `Common_Cancel` stays
`Common_Cancel`, and `hello` stays `hello`. Besides call sites, this keeps attributes that name a resource
in a string working, since they find it by its member name at runtime:

```csharp
[Required(ErrorMessageResourceName = "Validation_ContactRequired", ErrorMessageResourceType = typeof(Resources))]
```

A key that isn't an identifier (`hello-world`, `2fa`, or a keyword like `class`) is PascalCased as
usual, so the generated class always compiles. Identifier keys pick their names first, so a
PascalCased key never takes one: `hello-world` becomes `HelloWorld2` if there's also a `HelloWorld` key.
A template struct follows its member (`Login2fa_EnterCode_EmailTemplate`).

The first one that's set wins: `MemberNames` on the item, then `DilMemberNames`, then PascalCase. As with
accessibility, the file that owns a set decides. Only the member's name changes: the set is still looked
up by the JSON key, so `IStringLocalizer` and the files themselves are unaffected.

## How file selection works

The generator **only ever sees files you register with `<DilResource>`** — a source generator
cannot read arbitrary files, only those passed to it as `AdditionalFiles` (which `<DilResource>`
becomes under the hood). Your `appsettings.json`, `package.json`, and every other JSON file are
invisible to it. There is no folder scan and no magic filename.

As a convenience, registering a file also pulls in the rest of its set on disk — the build drops the
culture from the registered name and adds `<dir>/<stem>.json` and every `<dir>/<stem>.<culture>.json`
next to it. So registering `Strings.json` or `Strings.en.json` also picks up `Strings.tr.json`,
`Strings.de.json`, and so on; you don't list each culture. (This is an MSBuild-side expansion, so the
files must exist on disk at build time.)

## Setting the culture

Dil reads the ambient `CultureInfo.CurrentUICulture` — set it however your app already does:

- **Console / desktop:** `CultureInfo.CurrentUICulture = new("tr");`
- **ASP.NET Core:** `app.UseRequestLocalization(...)` sets it per request; `Strings.X` just works inside the request.

## IStringLocalizer interop (optional)

Prefer the typed `Strings.Greeting.Render("Ada")` API. But when a framework or library expects the
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

`IStringLocalizer<Strings>` reads the `Strings.json` group of the assembly that defines `Strings`. Note
the formatting difference: the `IStringLocalizer["key", args]` overload uses **positional** `string.Format`
(`{0}`, `{1:C}`) like resx — so author those values positionally. Dil's **named** `{name}` placeholders
are for the generated typed members; a named template passed through the indexer is returned unformatted
(never throws).

## Diagnostics

| ID       | Severity | Meaning |
|----------|----------|---------|
| `DIL001` | Warning  | A culture file is missing one of its set's keys (untranslated string). The keys come from the neutral file, or without one, from all the culture files. |

Treat it as an error if you want a hard guarantee that every string is translated:

```xml
<PropertyGroup>
  <WarningsAsErrors>$(WarningsAsErrors);DIL001</WarningsAsErrors>
</PropertyGroup>
```

## Notes

- Missing keys fall back: `tr-TR` → `tr` → default culture → neutral → the key itself. Fallback is per set.
- Each assembly's resource files are copied to `Dil/<AssemblyName>/` in the output (keeping their path
  in the project), and each set is keyed by its assembly and class name. Two libraries can both have a
  `Strings` set, even at the same path, and an app that references both keeps them apart.
- Only string values are used; numbers, objects, and arrays are ignored. Comments and trailing commas are tolerated, so `.jsonc` works.
- **Live reload** — editing a resource file is picked up at runtime. It is on for Debug builds and off otherwise, so a published Release app runs no file watchers. Override the build default with `<DilLiveReload>true|false</DilLiveReload>` in the app project (it reaches the runtime via `runtimeconfig.json`, so the app must reference Dil directly), or at runtime with `Dil.Loc.LiveReload`. All sets share one watcher per folder, and a change reloads only the sets that own the changed file — `appsettings.json` and other JSON files next to them are ignored.
- `Dil.Loc.Configure(baseDirectory)` overrides where files are loaded from / forces a reload.
- Placeholder values are formatted with the current culture via `IFormattable`; a `null` value becomes the empty string.

## Build from source

Needs the .NET 11 SDK (pinned in `global.json`); the tests also run on .NET 10, so have its runtime too.

```
dotnet build                                    # build everything
dotnet run --project sample/Dil.Sample          # run the demo
dotnet test                                     # run the TUnit tests
dotnet pack Dil.slnx -c Release -o artifacts    # produce both NuGet packages
```

## Project layout

```
src/Dil/                       runtime (netstandard2.0/net8.0/net10.0/net11.0) + build/ props & targets
src/Dil.Generator/             incremental source generator + DIL001 diagnostics
src/Dil.Extensions.Localization/  optional IStringLocalizer / DI adapter
sample/Dil.Sample/             runnable console demo
sample/Dil.Localization.Sample/  IStringLocalizer + DI demo
tests/                         TUnit tests for the runtime, generator, and the adapter
```

## License

MIT
