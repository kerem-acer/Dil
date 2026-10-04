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
}
