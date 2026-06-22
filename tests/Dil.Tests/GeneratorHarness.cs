using System.Collections.Immutable;
using System.Text;
using Dil.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Dil.Tests;

/// <summary>
/// A resource file fed to the generator: a path, its JSON, whether it is marked DilResource, and an
/// optional per-file Accessibility metadata value (null = unset).
/// </summary>
readonly record struct ResourceInput(string Path, string Json, bool DilResource = true, string? Accessibility = null);

/// <summary>Runs <see cref="LocalizationGenerator"/> in isolation via <see cref="CSharpGeneratorDriver"/>.</summary>
static class GeneratorHarness
{
    /// <summary>Runs the generator and returns the driver, ready to hand to Verify for snapshotting.</summary>
    public static GeneratorDriver RunDriver(string rootNamespace, params ResourceInput[] files) =>
        RunDriver(rootNamespace, null, files);

    /// <summary>
    /// Runs the generator with an optional project-wide <c>DilAccessibility</c> default
    /// (<paramref name="defaultAccessibility"/>, null = unset).
    /// </summary>
    public static GeneratorDriver RunDriver(string rootNamespace, string? defaultAccessibility, params ResourceInput[] files)
    {
        var compilation = CSharpCompilation.Create(
            "DilGeneratorTests",
            syntaxTrees: null,
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var additionalTexts = files
            .Select(f => (AdditionalText)new InMemoryAdditionalText(f.Path, f.Json))
            .ToImmutableArray();

        var optionsProvider = new TestOptionsProvider(
            rootNamespace,
            defaultAccessibility,
            files.ToDictionary(f => f.Path, f => (f.DilResource, f.Accessibility)));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new LocalizationGenerator().AsSourceGenerator()],
            additionalTexts: additionalTexts,
            parseOptions: null,
            optionsProvider: optionsProvider);

        return driver.RunGenerators(compilation);
    }

    sealed class InMemoryAdditionalText(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;
        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(text, Encoding.UTF8);
    }

    sealed class TestOptionsProvider : AnalyzerConfigOptionsProvider
    {
        readonly IReadOnlyDictionary<string, (bool DilResource, string? Accessibility)> _files;

        public TestOptionsProvider(
            string rootNamespace,
            string? defaultAccessibility,
            IReadOnlyDictionary<string, (bool, string?)> files)
        {
            var global = new Dictionary<string, string>
            {
                ["build_property.RootNamespace"] = rootNamespace,
            };
            if (defaultAccessibility is not null)
            {
                global["build_property.DilAccessibility"] = defaultAccessibility;
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
