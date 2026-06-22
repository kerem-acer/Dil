using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Dil.Generator
{
    /// <summary>
    /// Generates a strongly-typed <c>Resources</c> class from localization JSON files.
    /// A file is treated as a localization resource when it is added as
    /// <c>&lt;AdditionalFiles Include="..." DilResource="true" /&gt;</c>. Other JSON files
    /// are never read. Culture is taken from the filename: <c>Strings.json</c> is the
    /// neutral/default language, <c>Strings.tr.json</c> is Turkish, etc.
    /// </summary>
    [Generator]
    public sealed class LocalizationGenerator : IIncrementalGenerator
    {
        private static readonly Regex PlaceholderRegex = new Regex(@"\{(\w+)\}", RegexOptions.Compiled);
        private static readonly Regex CultureRegex = new Regex(@"^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$", RegexOptions.Compiled);

        private static readonly DiagnosticDescriptor MissingTranslation = new DiagnosticDescriptor(
            id: "DIL001",
            title: "Missing translation",
            messageFormat: "Culture '{0}' is missing a translation for key '{1}' (defined in the neutral resource)",
            category: "Dil",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor NoNeutralFile = new DiagnosticDescriptor(
            id: "DIL002",
            title: "No neutral resource file",
            messageFormat: "Dil found localization files but no neutral (culture-less) file to define the keys; add e.g. 'Strings.json'",
            category: "Dil",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Each AdditionalFile, paired with its analyzer options, projected to a ResFile (or null).
            var files = context.AdditionalTextsProvider
                .Combine(context.AnalyzerConfigOptionsProvider)
                .Select(static (pair, ct) => Project(pair.Left, pair.Right, ct))
                .Where(static f => f is not null)
                .Collect();

            var rootNamespace = context.AnalyzerConfigOptionsProvider.Select(static (p, _) =>
                TryGet(p.GlobalOptions, "build_property.RootNamespace", out var ns) && !string.IsNullOrWhiteSpace(ns)
                    ? ns!
                    : "Dil");

            context.RegisterSourceOutput(files.Combine(rootNamespace),
                static (spc, pair) => Generate(spc, pair.Left!, pair.Right));
        }

        private static ResFile? Project(AdditionalText file, AnalyzerConfigOptionsProvider provider, CancellationToken ct)
        {
            var opts = provider.GetOptions(file);
            if (!TryGet(opts, "build_metadata.AdditionalFiles.DilResource", out var flag) ||
                !string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase))
                return null;

            var text = file.GetText(ct)?.ToString() ?? string.Empty;
            var entries = FlatJson.Parse(text);

            var fileName = System.IO.Path.GetFileName(file.Path);
            var stem = fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? fileName.Substring(0, fileName.Length - 5)
                : fileName;
            var dot = stem.LastIndexOf('.');
            var culture = dot > 0 && CultureRegex.IsMatch(stem.Substring(dot + 1))
                ? stem.Substring(dot + 1)
                : string.Empty;

            TryGet(provider.GlobalOptions, "build_property.ProjectDir", out var projectDir);
            var relPath = MakeRelative(projectDir, file.Path);

            return new ResFile(culture, relPath, entries, file);
        }

        private static void Generate(SourceProductionContext spc, ImmutableArray<ResFile> files, string ns)
        {
            if (files.IsDefaultOrEmpty) return;

            var neutral = files.Where(f => f.Culture.Length == 0).ToList();
            if (neutral.Count == 0)
            {
                spc.ReportDiagnostic(Diagnostic.Create(NoNeutralFile, Location.None));
                return;
            }

            // Ordered union of keys from neutral files (first value wins for docs/placeholders).
            var keys = new List<KeyValuePair<string, string>>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var f in neutral)
                foreach (var kv in f.Entries)
                    if (seen.Add(kv.Key))
                        keys.Add(kv);

            // DIL001: every non-neutral culture must cover every neutral key.
            foreach (var group in files.Where(f => f.Culture.Length > 0).GroupBy(f => f.Culture, StringComparer.OrdinalIgnoreCase))
            {
                var have = new HashSet<string>(group.SelectMany(f => f.Entries.Select(e => e.Key)), StringComparer.Ordinal);
                var anchor = group.First().File;
                foreach (var kv in keys)
                    if (!have.Contains(kv.Key))
                        spc.ReportDiagnostic(Diagnostic.Create(
                            MissingTranslation, FileStart(anchor), group.Key, kv.Key));
            }

            var manifest = files
                .OrderBy(f => f.Culture.Length == 0 ? 0 : 1)
                .Select(f => (f.Culture, f.RelPath))
                .ToList();

            spc.AddSource("Dil.Resources.g.cs", SourceText.From(Emit(ns, "Resources", keys, manifest), Encoding.UTF8));
        }

        private static string Emit(
            string ns, string className,
            List<KeyValuePair<string, string>> keys,
            List<(string Culture, string RelPath)> manifest)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("namespace " + ns);
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>Strongly-typed localized strings, generated by Dil.</summary>");
            sb.AppendLine("    public static partial class " + className);
            sb.AppendLine("    {");

            sb.AppendLine("        static " + className + "()");
            sb.AppendLine("        {");
            sb.AppendLine("            global::Dil.Loc.Register(new (string, string)[]");
            sb.AppendLine("            {");
            foreach (var (culture, relPath) in manifest)
                sb.AppendLine("                (" + Literal(culture) + ", " + Literal(relPath) + "),");
            sb.AppendLine("            });");
            sb.AppendLine("        }");
            sb.AppendLine();

            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kv in keys)
            {
                var member = ToPascal(kv.Key);
                if (member.Length == 0) continue;
                var baseName = member;
                var n = 2;
                while (!used.Add(member)) member = baseName + n++;

                var keyLiteral = Literal(kv.Key);
                var placeholders = ExtractPlaceholders(kv.Value);

                sb.AppendLine("        /// <summary>" + EscapeXml(kv.Value) + "</summary>");
                if (placeholders.Count == 0)
                {
                    sb.AppendLine("        public static string " + member +
                                  " => global::Dil.Loc.Get(" + keyLiteral + ");");
                }
                else
                {
                    var pars = string.Join(", ", placeholders.Select(p => "object? " + p));
                    var args = string.Join(", ", placeholders.Select(p => "(" + Literal(p) + ", " + p + ")"));
                    sb.AppendLine("        public static string " + member + "(" + pars + ")" +
                                  " => global::Dil.Loc.Format(" + keyLiteral + ", " + args + ");");
                }
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static List<string> ExtractPlaceholders(string value)
        {
            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in PlaceholderRegex.Matches(value))
            {
                var name = m.Groups[1].Value;
                if (name.Length == 0 || char.IsDigit(name[0])) continue;
                var ident = EscapeKeyword(name);
                if (seen.Add(ident)) list.Add(ident);
            }
            return list;
        }

        private static string ToPascal(string key)
        {
            var sb = new StringBuilder();
            var upper = true;
            foreach (var c in key)
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(upper ? char.ToUpperInvariant(c) : c);
                    upper = false;
                }
                else upper = true;
            }
            var s = sb.ToString();
            if (s.Length > 0 && char.IsDigit(s[0])) s = "_" + s;
            return s;
        }

        private static string MakeRelative(string? projectDir, string fullPath)
        {
            if (!string.IsNullOrEmpty(projectDir))
            {
                var dir = projectDir!.Replace('\\', '/');
                if (!dir.EndsWith("/")) dir += "/";
                var norm = fullPath.Replace('\\', '/');
                if (norm.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                    return norm.Substring(dir.Length);
            }
            return System.IO.Path.GetFileName(fullPath);
        }

        private static Location FileStart(AdditionalText file) =>
            Location.Create(file.Path, new TextSpan(0, 0),
                new LinePositionSpan(new LinePosition(0, 0), new LinePosition(0, 0)));

        private static bool TryGet(AnalyzerConfigOptions options, string key, out string? value)
        {
            if (options.TryGetValue(key, out var v)) { value = v; return true; }
            // be defensive about casing differences between SDK versions
            var lower = key.Substring(0, key.LastIndexOf('.') + 1) +
                        key.Substring(key.LastIndexOf('.') + 1).ToLowerInvariant();
            if (options.TryGetValue(lower, out v)) { value = v; return true; }
            value = null;
            return false;
        }

        private static string EscapeKeyword(string ident) =>
            CsharpKeywords.Contains(ident) ? "@" + ident : ident;

        private static string Literal(string s) =>
            "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\r", "\\r").Replace("\n", "\\n") + "\"";

        private static string EscapeXml(string s) =>
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
             .Replace("\r", " ").Replace("\n", " ");

        private sealed class ResFile
        {
            public ResFile(string culture, string relPath, List<KeyValuePair<string, string>> entries, AdditionalText file)
            {
                Culture = culture;
                RelPath = relPath;
                Entries = entries;
                File = file;
            }

            public string Culture { get; }
            public string RelPath { get; }
            public List<KeyValuePair<string, string>> Entries { get; }
            public AdditionalText File { get; }
        }

        private static readonly HashSet<string> CsharpKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "abstract","as","base","bool","break","byte","case","catch","char","checked","class","const",
            "continue","decimal","default","delegate","do","double","else","enum","event","explicit","extern",
            "false","finally","fixed","float","for","foreach","goto","if","implicit","in","int","interface",
            "internal","is","lock","long","namespace","new","null","object","operator","out","override","params",
            "private","protected","public","readonly","ref","return","sbyte","sealed","short","sizeof","stackalloc",
            "static","string","struct","switch","this","throw","true","try","typeof","uint","ulong","unchecked",
            "unsafe","ushort","using","virtual","void","volatile","while"
        };
    }
}
