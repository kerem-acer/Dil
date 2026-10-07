using System.Collections.Immutable;
using System.Text;
using Dil.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Dil.Tests;

/// <summary>
/// A resource file fed to the generator: a path, its JSON, whether it is marked DilResource, and
/// optional per-file Accessibility, DefaultCulture, Namespace and MemberNames metadata values (null = unset).
/// </summary>
readonly record struct ResourceInput(
    string Path, string Json, bool DilResource = true, string? Accessibility = null, string? DefaultCulture = null,
    string? Namespace = null, string? MemberNames = null);

/// <summary>Runs <see cref="LocalizationGenerator"/> in isolation via <see cref="CSharpGeneratorDriver"/>.</summary>
static class GeneratorHarness
{
    /// <summary>
    /// The assembly the generator runs in unless a test names one; it prefixes every set key and
    /// manifest path in the generated code.
    /// </summary>
    public const string DefaultAssemblyName = "DilGeneratorTests";

    /// <summary>
    /// The project folder the generator sees. A resource path under it is project-relative, so its folder
    /// shows up in the manifest path; a bare file name ("Strings.json") is used as it is.
    /// </summary>
    public const string ProjectDir = "/src/MyApp/";

    /// <summary>Runs the generator and returns the driver, ready to hand to Verify for snapshotting.</summary>
    public static GeneratorDriver RunDriver(string rootNamespace, params ResourceInput[] files) =>
        RunDriver(rootNamespace, null, files);

    /// <summary>
    /// Runs the generator with an optional project-wide <c>DilAccessibility</c> default
    /// (<paramref name="defaultAccessibility"/>, null = unset).
    /// </summary>
    public static GeneratorDriver RunDriver(string rootNamespace, string? defaultAccessibility, params ResourceInput[] files) =>
        RunDriver(rootNamespace, defaultAccessibility, null, files);

    /// <summary>
    /// Runs the generator with optional project-wide <c>DilAccessibility</c> and <c>DilDefaultCulture</c>
    /// values (null = unset).
    /// </summary>
    public static GeneratorDriver RunDriver(
        string rootNamespace, string? defaultAccessibility, string? defaultCulture, params ResourceInput[] files) =>
        Run(DefaultAssemblyName, rootNamespace, defaultAccessibility, defaultCulture, files);

    /// <summary>Runs the generator as if compiling <paramref name="assemblyName"/>.</summary>
    public static GeneratorDriver RunDriverAs(string assemblyName, string rootNamespace, params ResourceInput[] files) =>
        Run(assemblyName, rootNamespace, null, null, files);

    /// <summary>
    /// Runs the generator with extra build properties, named without the <c>build_property.</c> prefix
    /// (for example <c>DilNamespace</c>).
    /// </summary>
    public static GeneratorDriver RunDriverWith(
        string rootNamespace, IReadOnlyDictionary<string, string> properties, params ResourceInput[] files) =>
        Run(DefaultAssemblyName, rootNamespace, null, null, files, properties);

    static GeneratorDriver Run(
        string assemblyName, string rootNamespace, string? defaultAccessibility, string? defaultCulture,
        ResourceInput[] files, IReadOnlyDictionary<string, string>? properties = null)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: null,
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var additionalTexts = files
            .Select(f => (AdditionalText)new InMemoryAdditionalText(f.Path, f.Json))
            .ToImmutableArray();

        var optionsProvider = new TestOptionsProvider(
            rootNamespace,
            defaultAccessibility,
            defaultCulture,
            properties,
            files.ToDictionary(f => f.Path, f => f));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new LocalizationGenerator().AsSourceGenerator()],
            additionalTexts: additionalTexts,
            parseOptions: null,
            optionsProvider: optionsProvider);

        return driver.RunGenerators(compilation);
    }

    /// <summary>
    /// Compiles the generator's output against the real Dil runtime, with doc comments diagnosed (crefs
    /// included), so a test can assert the generated source builds cleanly and then load and run it.
    /// </summary>
    public static CSharpCompilation CompileGenerated(GeneratorDriver driver)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.Diagnose);
        var trees = driver.GetRunResult().GeneratedTrees
            .Select(t => CSharpSyntaxTree.ParseText(t.GetText(), parseOptions, t.FilePath));
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;

        return CSharpCompilation.Create(
            "DilGenerated" + Guid.NewGuid().ToString("N"),
            trees,
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")),
                MetadataReference.CreateFromFile(typeof(Loc).Assembly.Location),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    sealed class InMemoryAdditionalText(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;
        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(text, Encoding.UTF8);
    }

    sealed class TestOptionsProvider : AnalyzerConfigOptionsProvider
    {
        readonly IReadOnlyDictionary<string, ResourceInput> _files;

        public TestOptionsProvider(
            string rootNamespace,
            string? defaultAccessibility,
            string? defaultCulture,
            IReadOnlyDictionary<string, string>? properties,
            IReadOnlyDictionary<string, ResourceInput> files)
        {
            var global = new Dictionary<string, string>
            {
                ["build_property.RootNamespace"] = rootNamespace,
                ["build_property.ProjectDir"] = ProjectDir,
            };
            if (defaultAccessibility is not null)
            {
                global["build_property.DilAccessibility"] = defaultAccessibility;
            }

            if (defaultCulture is not null)
            {
                global["build_property.DilDefaultCulture"] = defaultCulture;
            }

            foreach (var (name, value) in properties ?? new Dictionary<string, string>())
            {
                global["build_property." + name] = value;
            }

            GlobalOptions = new Options(global);
            _files = files;
        }

        public override AnalyzerConfigOptions GlobalOptions { get; }

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Options.Empty;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            var map = new Dictionary<string, string>();
            if (_files.TryGetValue(textFile.Path, out var meta) && meta.DilResource)
            {
                map["build_metadata.AdditionalFiles.DilResource"] = "true";
                if (meta.Accessibility is not null)
                {
                    map["build_metadata.AdditionalFiles.Accessibility"] = meta.Accessibility;
                }

                if (meta.DefaultCulture is not null)
                {
                    map["build_metadata.AdditionalFiles.DefaultCulture"] = meta.DefaultCulture;
                }

                if (meta.Namespace is not null)
                {
                    map["build_metadata.AdditionalFiles.Namespace"] = meta.Namespace;
                }

                if (meta.MemberNames is not null)
                {
                    map["build_metadata.AdditionalFiles.MemberNames"] = meta.MemberNames;
                }
            }

            return new Options(map);
        }

        sealed class Options(Dictionary<string, string> values) : AnalyzerConfigOptions
        {
            public static readonly Options Empty = new([]);

            public override bool TryGetValue(string key, out string value)
            {
                if (values.TryGetValue(key, out var v))
                {
                    value = v;
                    return true;
                }

                value = null!;
                return false;
            }
        }
    }
}
