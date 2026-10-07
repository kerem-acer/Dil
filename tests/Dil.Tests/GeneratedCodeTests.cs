using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Dil.Tests;

/// <summary>
/// Compiles the generator's output against the real runtime: it must build without warnings (doc crefs
/// included), and the template structs must resolve through <see cref="Loc"/> like any other member.
/// </summary>
[NotInParallel("Loc-global-state")]
public sealed class GeneratedCodeTests
{
    /// <summary>
    /// Keys and placeholders chosen to collide with the generated names: a real "greetingTemplate" key,
    /// keys named after the struct's own members, and placeholders shadowing the class and helpers.
    /// </summary>
    internal const string ClashingKeys =
        """
        {
          "greeting": "Hi {name}",
          "greetingTemplate": "plain",
          "render": "{x}",
          "template": "{y}",
          "shadow": "{Strings} {Template} {__Format} {T1}"
        }
        """;

    [Before(Test)]
    public void ResetCulture() => CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

    [Test]
    public async Task ClashingNamesCompileWithoutWarnings()
    {
        // Public, so a missing or unresolvable doc comment on any member would warn.
        var driver = GeneratorHarness.RunDriver("MyApp", defaultAccessibility: "public",
            new ResourceInput("Strings.json", ClashingKeys));

        var diagnostics = GeneratorHarness.CompileGenerated(driver).GetDiagnostics()
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .Select(d => d.ToString())
            .ToList();

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task TemplateAndRenderResolveForTheCurrentCulture()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dil-tests", Guid.NewGuid().ToString("N"));
        var files = Path.Combine(dir, "Dil", GeneratorHarness.DefaultAssemblyName); // the build copies them here
        Directory.CreateDirectory(files);
        File.WriteAllText(Path.Combine(files, "Greetings.json"), """{ "welcome": "Hello, {name}!" }""");
        File.WriteAllText(Path.Combine(files, "Greetings.tr.json"), """{ "welcome": "Merhaba, {name}!" }""");
        Loc.LiveReload = false;
        Loc.Configure(dir);

        var driver = GeneratorHarness.RunDriver("MyApp",
            new ResourceInput("Greetings.json", """{ "welcome": "Hello, {name}!" }"""),
            new ResourceInput("Greetings.tr.json", """{ "welcome": "Merhaba, {name}!" }"""));
        using var pe = new MemoryStream();
        var emit = GeneratorHarness.CompileGenerated(driver).Emit(pe);
        await Assert.That(emit.Success).IsTrue();

        // A default(WelcomeTemplate) that never touched Greetings itself: the struct must still run the
        // class's static constructor, or the set is never registered and every lookup returns the key.
        var type = System.Reflection.Assembly.Load(pe.ToArray()).GetType("MyApp.Greetings+WelcomeTemplate", throwOnError: true)!;
        var welcome = Activator.CreateInstance(type)!;
        var template = type.GetProperty("Template")!;
        var render = type.GetMethod("Render")!.MakeGenericMethod(typeof(string));

        await Assert.That(template.GetValue(welcome)).IsEqualTo("Hello, {name}!");
        await Assert.That(render.Invoke(welcome, ["Ada"])).IsEqualTo("Hello, Ada!");

        CultureInfo.CurrentUICulture = new CultureInfo("tr-TR"); // falls back tr-TR -> tr
        await Assert.That(template.GetValue(welcome)).IsEqualTo("Merhaba, {name}!");
        await Assert.That(render.Invoke(welcome, ["Ada"])).IsEqualTo("Merhaba, Ada!");
    }

    [Test]
    public async Task SameNamedSetsInDifferentAssembliesStayApart()
    {
        // Two libraries, each with Strings.json at its project root, loaded into one app: each class
        // must keep reading its own file, however many times the other class registers in between.
        var dir = Path.Combine(Path.GetTempPath(), "dil-tests", Guid.NewGuid().ToString("N"));
        foreach (var lib in new[] { "LibA", "LibB" })
        {
            Directory.CreateDirectory(Path.Combine(dir, "Dil", lib));
            File.WriteAllText(Path.Combine(dir, "Dil", lib, "Strings.json"), $$"""{ "hello": "from {{lib}}" }""");
        }

        Loc.LiveReload = false;
        Loc.Configure(dir);

        var libA = await Load("LibA");
        var libB = await Load("LibB");

        await Assert.That(libA.GetValue(null)).IsEqualTo("from LibA");
        await Assert.That(libB.GetValue(null)).IsEqualTo("from LibB");
        await Assert.That(libA.GetValue(null)).IsEqualTo("from LibA");

        static async Task<System.Reflection.PropertyInfo> Load(string lib)
        {
            var driver = GeneratorHarness.RunDriverAs(lib, lib,
                new ResourceInput("Strings.json", """{ "hello": "unused at runtime" }"""));
            using var pe = new MemoryStream();
            await Assert.That(GeneratorHarness.CompileGenerated(driver).Emit(pe).Success).IsTrue();
            var type = System.Reflection.Assembly.Load(pe.ToArray()).GetType(lib + ".Strings", throwOnError: true)!;
            return type.GetProperty("Hello")!;
        }
    }

    [Test]
    public async Task ClassesInFolderNamespacesCompileAndResolve()
    {
        // The namespace moves the class, not its set: doc crefs and the template struct follow the class
        // into its namespace, and it still reads the file the build copied to Dil/<assembly>/<folder>.
        var dir = Path.Combine(Path.GetTempPath(), "dil-tests", Guid.NewGuid().ToString("N"));
        var files = Path.Combine(dir, "Dil", GeneratorHarness.DefaultAssemblyName, "Localization", "Resources");
        Directory.CreateDirectory(files);
        File.WriteAllText(Path.Combine(files, "Strings.json"), """{ "greeting": "Hi, {name}!" }""");
        Loc.LiveReload = false;
        Loc.Configure(dir);

        // Public, so a missing or unresolvable doc comment on any member would warn.
        var driver = GeneratorHarness.RunDriverWith("MyApp",
            new Dictionary<string, string> { ["DilAccessibility"] = "public", ["DilNamespaceFromFolder"] = "true" },
            new ResourceInput(GeneratorHarness.ProjectDir + "Localization/Resources/Strings.json",
                """{ "greeting": "Hello, {name}!" }"""),
            new ResourceInput(GeneratorHarness.ProjectDir + "My Folder/2nd.Level/class/Odd.json", """{ "note": "{x}" }"""));
        var compilation = GeneratorHarness.CompileGenerated(driver);

        var diagnostics = compilation.GetDiagnostics()
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .Select(d => d.ToString())
            .ToList();
        await Assert.That(diagnostics).IsEmpty();

        using var pe = new MemoryStream();
        await Assert.That(compilation.Emit(pe).Success).IsTrue();
        var assembly = System.Reflection.Assembly.Load(pe.ToArray());
        await Assert.That(assembly.GetType("MyApp.My_Folder._2nd.Level.class.Odd")).IsNotNull();

        var greeting = assembly.GetType("MyApp.Localization.Resources.Strings+GreetingTemplate", throwOnError: true)!;
        var render = greeting.GetMethod("Render")!.MakeGenericMethod(typeof(string));
        await Assert.That(render.Invoke(Activator.CreateInstance(greeting), ["Ada"])).IsEqualTo("Hi, Ada!");
    }

    [Test]
    public async Task VerbatimMembersResolveByTheirKeyName()
    {
        // The reason for verbatim names: attributes that find a resource by its member name in a string.
        var dir = Path.Combine(Path.GetTempPath(), "dil-tests", Guid.NewGuid().ToString("N"));
        var files = Path.Combine(dir, "Dil", GeneratorHarness.DefaultAssemblyName); // the build copies them here
        Directory.CreateDirectory(files);
        File.WriteAllText(Path.Combine(files, "Resources.json"),
            """{ "Validation_ContactRequired": "{0} is required", "Passkeys_RemoveConfirm": "Remove passkey" }""");
        Loc.LiveReload = false;
        Loc.Configure(dir);

        // "a\u00ADb" is a valid identifier, but C# drops the soft hyphen from it, so it would be a second
        // member named ab: it must be PascalCased (AB) instead. Public, so a missing doc comment would warn.
        var driver = GeneratorHarness.RunDriverWith("MyApp",
            new Dictionary<string, string> { ["DilAccessibility"] = "public", ["DilMemberNames"] = "Verbatim" },
            new ResourceInput("Resources.json",
                """
                {
                  "Validation_ContactRequired": "{0} is required",
                  "Passkeys_RemoveConfirm": "Remove passkey",
                  "ab": "ab",
                  "a\u00ADb": "a-b",
                  "var": "contextual keyword",
                  "_": "underscore"
                }
                """));
        var compilation = GeneratorHarness.CompileGenerated(driver);

        var diagnostics = compilation.GetDiagnostics()
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .Select(d => d.ToString())
            .ToList();
        await Assert.That(diagnostics).IsEmpty();

        using var pe = new MemoryStream();
        await Assert.That(compilation.Emit(pe).Success).IsTrue();
        var type = System.Reflection.Assembly.Load(pe.ToArray()).GetType("MyApp.Resources", throwOnError: true)!;
        await Assert.That(type.GetProperty("AB")).IsNotNull();

        var required = new RequiredAttribute
        {
            ErrorMessageResourceName = "Validation_ContactRequired",
            ErrorMessageResourceType = type,
        };
        await Assert.That(required.FormatErrorMessage("Contact")).IsEqualTo("Contact is required");

        var display = new DisplayAttribute { Name = "Passkeys_RemoveConfirm", ResourceType = type };
        await Assert.That(display.GetName()).IsEqualTo("Remove passkey");
    }

    [Test]
    public async Task SetWithoutNeutralFileFallsBackToItsDefaultCulture()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dil-tests", Guid.NewGuid().ToString("N"));
        var files = Path.Combine(dir, "Dil", GeneratorHarness.DefaultAssemblyName); // the build copies them here
        Directory.CreateDirectory(files);
        File.WriteAllText(Path.Combine(files, "Menu.en.json"), """{ "open": "Open" }""");
        File.WriteAllText(Path.Combine(files, "Menu.tr.json"), """{ "open": "Aç" }""");
        Loc.LiveReload = false;
        Loc.Configure(dir);

        // The generated static constructor must pass the default culture on to Loc.Register.
        var driver = GeneratorHarness.RunDriver("MyApp",
            new ResourceInput("Menu.en.json", """{ "open": "Open" }""", DefaultCulture: "en"),
            new ResourceInput("Menu.tr.json", """{ "open": "Aç" }"""));
        using var pe = new MemoryStream();
        var emit = GeneratorHarness.CompileGenerated(driver).Emit(pe);
        await Assert.That(emit.Success).IsTrue();

        var open = System.Reflection.Assembly.Load(pe.ToArray()).GetType("MyApp.Menu", throwOnError: true)!
            .GetProperty("Open")!;

        CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");
        await Assert.That(open.GetValue(null)).IsEqualTo("Aç");

        CultureInfo.CurrentUICulture = new CultureInfo("fr-FR"); // no fr file -> the default culture, en
        await Assert.That(open.GetValue(null)).IsEqualTo("Open");
    }
}
