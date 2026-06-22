using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Dil.Generator;

/// <summary>
/// Generates a strongly-typed class per localization JSON file group. A file is treated as a
/// localization resource when it is added as <c>&lt;AdditionalFiles Include="..." DilResource="true" /&gt;</c>.
/// Other JSON files are never read. The base filename is the class name and the trailing dotted segment
/// is the culture: <c>Strings.json</c> is the neutral set <c>Strings</c>, <c>Strings.tr.json</c> is Turkish.
/// </summary>
[Generator]
public sealed class LocalizationGenerator : IIncrementalGenerator
{
    // {name} -> generic parameter; {name:Type} -> a parameter of that exact C# type.
    static readonly Regex PlaceholderRegex = new(@"\{(\w+)(?::([^{}]+))?\}", RegexOptions.Compiled);

    // Shape pre-filter for a culture segment (the real check is membership in KnownCultures).
    static readonly Regex CultureRegex = new("^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$", RegexOptions.Compiled);

    // Allowed characters for a {name:Type} type hint — identifiers, generics, arrays, nullables,
    // tuples, and `global::` qualifiers. Anything else (';', '(', ')', …) is rejected so a malformed
    // hint can't be injected verbatim into a method signature.
    static readonly Regex TypeRegex = new(@"^[\w.\?\[\]<>, :@]+$", RegexOptions.Compiled);

    static readonly HashSet<string> KnownCultures = BuildKnownCultures();

    static readonly DiagnosticDescriptor MissingTranslation = new(
        id: "DIL001",
        title: "Missing translation",
        messageFormat: "Culture '{0}' is missing a translation for key '{1}' (defined in the neutral resource)",
        category: "Dil",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    static readonly DiagnosticDescriptor NoNeutralFile = new(
        id: "DIL002",
        title: "No neutral resource file",
        messageFormat:
        "Resource set '{0}' has no neutral (culture-less) file to define its keys (for example '{0}.json')",
        category: "Dil",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Step 1 (cheap): identify which AdditionalFiles are resources and extract set/culture/path.
        // Combining with the options provider re-runs this per compilation, but the projected ResInfo
        // is value-equatable, so an unchanged file yields an equal ResInfo and step 2 stays cached.
        var infos = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (pair, _) => Identify(pair.Left, pair.Right))
            .Where(static x => x.HasValue)
            .Select(static (x, _) => x!.Value);

        // Step 2: parse the JSON. Cached on ResInfo, so it only re-runs when a resource file's content
        // (or its set/culture/path) actually changes.
        var files = infos.Select(static (info, ct) => ParseFile(info, ct)).Collect();

        var rootNamespace = context.AnalyzerConfigOptionsProvider.Select(static (p, _) =>
            TryGet(p.GlobalOptions, "build_property.RootNamespace", out var ns) && !string.IsNullOrWhiteSpace(ns)
                ? ns!
                : "Dil");

        context.RegisterSourceOutput(files.Combine(rootNamespace),
            static (spc, pair) => Generate(spc, pair.Left, pair.Right));
    }

    static ResInfo? Identify(AdditionalText file, AnalyzerConfigOptionsProvider provider)
    {
        var opts = provider.GetOptions(file);
        if (!TryGet(opts, "build_metadata.AdditionalFiles.DilResource", out var flag) ||
            !string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var fileName = System.IO.Path.GetFileName(file.Path);
        var stem = fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? fileName.Substring(0, fileName.Length - 5)
            : fileName;

        var (set, culture) = SplitSetAndCulture(stem);

        TryGet(provider.GlobalOptions, "build_property.ProjectDir", out var projectDir);
        var relPath = MakeRelative(projectDir, file.Path);

        return new ResInfo(file, set, culture, relPath, ResolveAccessibility(opts, provider.GlobalOptions));
    }

    // Per-resource accessibility for the generated class: per-file metadata wins, else the
    // project-wide build property, else internal. Anything other than "public" normalizes to
    // "internal" so the generated source is always a valid access modifier.
    static string ResolveAccessibility(AnalyzerConfigOptions fileOptions, AnalyzerConfigOptions globalOptions)
    {
        if (!TryGet(fileOptions, "build_metadata.AdditionalFiles.Accessibility", out var value) ||
            string.IsNullOrWhiteSpace(value))
        {
            TryGet(globalOptions, "build_property.DilAccessibility", out value);
        }

        return string.Equals(value?.Trim(), "public", StringComparison.OrdinalIgnoreCase)
            ? "public"
            : "internal";
    }

    // The trailing dotted segment is the culture only when it is a real, known culture name — a shape
    // regex alone would misread `Order.New.json` ("New") or `App.Tax.json" ("Tax") as cultures.
    static (string Set, string Culture) SplitSetAndCulture(string stem)
    {
        var dot = stem.LastIndexOf('.');
        if (dot > 0)
        {
            var candidate = stem.Substring(dot + 1);
            if (CultureRegex.IsMatch(candidate) && KnownCultures.Contains(candidate))
            {
                return (stem.Substring(0, dot), candidate);
            }
        }

        return (stem, string.Empty);
    }

    static ResFile ParseFile(ResInfo info, CancellationToken ct)
    {
        var text = info.File.GetText(ct)?.ToString() ?? string.Empty;
        var entries = FlatJson.Parse(text);
        return new ResFile(info.Set, info.Culture, info.RelPath, info.File.Path, info.Accessibility,
            new EquatableArray<KeyValuePair<string, string>>(entries.ToArray()));
    }

    static void Generate(SourceProductionContext spc, ImmutableArray<ResFile> files, string ns)
    {
        if (files.IsDefaultOrEmpty)
        {
            return;
        }

        // One generated class per resource set (grouped by base filename).
        foreach (var set in files.GroupBy(f => f.Set, StringComparer.Ordinal))
        {
            EmitSet(spc, ns, set.Key, set.ToList());
        }
    }

    static void EmitSet(SourceProductionContext spc, string ns, string setName, List<ResFile> files)
    {
        var className = ToPascal(setName);
        if (className.Length == 0)
        {
            return;
        }

        var neutral = files.Where(f => f.Culture.Length == 0).ToList();
        if (neutral.Count == 0)
        {
            spc.ReportDiagnostic(Diagnostic.Create(NoNeutralFile, FileStart(files[0].Path), setName));
            return;
        }

        // Ordered union of neutral keys (first-seen order); on a duplicate key the last value wins,
        // matching the runtime parser (System.Text.Json last-wins for duplicate property names).
        var keyOrder = new List<string>();
        var keyValues = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in neutral)
        {
            foreach (var kv in f.Entries)
            {
                if (!keyValues.ContainsKey(kv.Key))
                {
                    keyOrder.Add(kv.Key);
                }

                keyValues[kv.Key] = kv.Value;
            }
        }

        // DIL001: every non-neutral culture in this set must cover every neutral key.
        foreach (var group in files.Where(f => f.Culture.Length > 0)
                     .GroupBy(f => f.Culture, StringComparer.OrdinalIgnoreCase))
        {
            var have = new HashSet<string>(group.SelectMany(f => f.Entries.Select(e => e.Key)), StringComparer.Ordinal);
            var anchorPath = group.First().Path;
            foreach (var key in keyOrder)
            {
                if (!have.Contains(key))
                {
                    spc.ReportDiagnostic(Diagnostic.Create(MissingTranslation, FileStart(anchorPath), group.Key, key));
                }
            }
        }

        var manifest = files
            .OrderBy(f => f.Culture.Length == 0 ? 0 : 1)
            .Select(f => (f.Culture, f.RelPath))
            .ToList();

        // Per-key translations for the doc comments: one row per culture (first-seen order, last value
        // wins on a duplicate), so the generated output is deterministic and never lists a culture twice.
        var translations = new Dictionary<string, List<(string Culture, string Value)>>(StringComparer.Ordinal);
        foreach (var f in files.OrderBy(f => f.Culture.Length == 0 ? 0 : 1))
        {
            if (f.Culture.Length == 0)
            {
                continue;
            }

            foreach (var kv in f.Entries)
            {
                if (!translations.TryGetValue(kv.Key, out var rows))
                {
                    translations[kv.Key] = rows = [];
                }

                var idx = rows.FindIndex(t => string.Equals(t.Culture, f.Culture, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                {
                    rows[idx] = (f.Culture, kv.Value);
                }
                else
                {
                    rows.Add((f.Culture, kv.Value));
                }
            }
        }

        // One class per set, but a set spans several files; the neutral file owns it, so its
        // accessibility decides (culture files' accessibility is ignored).
        var accessibility = neutral[0].Accessibility;

        var keys = keyOrder.Select(k => new KeyValuePair<string, string>(k, keyValues[k])).ToList();
        spc.AddSource("Dil." + className + ".g.cs",
            SourceText.From(Emit(ns, className, accessibility, keys, manifest, translations), Encoding.UTF8));
    }

    static string Emit(
        string ns, string className, string accessibility,
        List<KeyValuePair<string, string>> keys,
        List<(string Culture, string RelPath)> manifest,
        Dictionary<string, List<(string Culture, string Value)>> translations)
    {
        var setLiteral = Literal(className);
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("namespace " + ns);
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>Strongly-typed localized strings, generated by Dil.</summary>");
        // A sealed holder (not a static class) so it can be used as IStringLocalizer&lt;T&gt;; the
        // private constructor keeps it non-instantiable, and every member is still static.
        sb.AppendLine("    " + accessibility + " sealed partial class " + className);
        sb.AppendLine("    {");
        sb.AppendLine("        " + className + "() { }");
        sb.AppendLine();

        sb.AppendLine("        static " + className + "()");
        sb.AppendLine("        {");
        sb.AppendLine("            global::Dil.Loc.Register(" + setLiteral + ", new (string, string)[]");
        sb.AppendLine("            {");
        foreach (var (culture, relPath) in manifest)
        {
            sb.AppendLine("                (" + Literal(culture) + ", " + Literal(relPath) + "),");
        }

        sb.AppendLine("            });");
        sb.AppendLine("        }");
        sb.AppendLine();

        // Seed with the class name so a key that PascalCases to it gets disambiguated (a member with
        // the same name as its type is CS0542).
        var used = new HashSet<string>(StringComparer.Ordinal) { className };
        foreach (var kv in keys)
        {
            var member = ToPascal(kv.Key);
            if (member.Length == 0)
            {
                continue;
            }

            var baseName = member;
            var n = 2;
            while (!used.Add(member))
            {
                member = baseName + n++;
            }

            var keyLiteral = Literal(kv.Key);
            var placeholders = ExtractPlaceholders(kv.Value);

            sb.AppendLine("        /// <summary>" + EscapeXml(kv.Value) + "</summary>");
            EmitTranslationDoc(sb, translations, kv.Key);
            if (placeholders.Count == 0)
            {
                sb.AppendLine("        public static string " + member +
                              " => global::Dil.Loc.Get(" + setLiteral + ", " + keyLiteral + ");");
            }
            else
            {
                sb.AppendLine("        public static string " + member + Signature(placeholders) +
                              " => global::Dil.Loc.Format(" + setLiteral + ", " + keyLiteral + ", " + Args(placeholders) + ");");
            }
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    // A bare {name} placeholder becomes its own generic parameter (so int/string/etc. flow without
    // object?); a {name:Type} placeholder uses that exact type. Synthetic type-parameter names avoid
    // colliding with the placeholder identifiers (a param named `T1` next to type param `T1` is CS0412).
    static string Signature(List<(string Raw, string Ident, string? Type)> placeholders)
    {
        var paramIdents = new HashSet<string>(placeholders.Select(p => p.Ident), StringComparer.Ordinal);
        var typeParams = new List<string>();
        var pars = new List<string>();
        var n = 0;
        foreach (var p in placeholders)
        {
            if (p.Type is null)
            {
                string tp;
                do
                {
                    tp = "T" + (++n);
                }
                while (paramIdents.Contains(tp) || typeParams.Contains(tp));

                typeParams.Add(tp);
                pars.Add(tp + " " + p.Ident);
            }
            else
            {
                pars.Add(p.Type + " " + p.Ident);
            }
        }

        var generics = typeParams.Count > 0 ? "<" + string.Join(", ", typeParams) + ">" : string.Empty;
        return generics + "(" + string.Join(", ", pars) + ")";
    }

    // The substitution key passed to Loc.Format is the raw placeholder name (so it matches the
    // "{class}" token in the template), while the C# parameter uses the escaped identifier (@class).
    static string Args(List<(string Raw, string Ident, string? Type)> placeholders) =>
        string.Join(", ", placeholders.Select(p => "(" + Literal(p.Raw) + ", " + p.Ident + ")"));

    // Lists each culture's translation of a key in the member's XML doc (one row per culture), so
    // hovering the generated member in the IDE shows every language at a glance.
    static void EmitTranslationDoc(
        StringBuilder sb,
        Dictionary<string, List<(string Culture, string Value)>> translations,
        string key)
    {
        if (!translations.TryGetValue(key, out var rows) || rows.Count == 0)
        {
            return;
        }

        sb.AppendLine("        /// <remarks>Translations:");
        sb.AppendLine("        /// <list type=\"bullet\">");
        foreach (var (culture, value) in rows)
        {
            sb.AppendLine("        /// <item><c>" + EscapeXml(culture) + "</c>: " + EscapeXml(value) + "</item>");
        }

        sb.AppendLine("        /// </list>");
        sb.AppendLine("        /// </remarks>");
    }

    static List<(string Raw, string Ident, string? Type)> ExtractPlaceholders(string value)
    {
        var list = new List<(string Raw, string Ident, string? Type)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in PlaceholderRegex.Matches(value))
        {
            var name = m.Groups[1].Value;
            if (name.Length == 0 || char.IsDigit(name[0]))
            {
                continue;
            }

            // A {name:Type} hint is honored only when it's a plausible C# type, so a malformed hint can't
            // be injected verbatim into a method signature; otherwise the placeholder stays generic.
            string? type = null;
            if (m.Groups[2].Success)
            {
                var candidate = m.Groups[2].Value.Trim();
                if (candidate.Length > 0 && TypeRegex.IsMatch(candidate))
                {
                    type = candidate;
                }
            }

            if (seen.Add(name))
            {
                list.Add((name, EscapeKeyword(name), type));
            }
        }

        return list;
    }

    static string ToPascal(string key)
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
            else
            {
                upper = true;
            }
        }

        var s = sb.ToString();
        if (s.Length > 0 && char.IsDigit(s[0]))
        {
            s = "_" + s;
        }

        return s;
    }

    static string MakeRelative(string? projectDir, string fullPath)
    {
        if (!string.IsNullOrEmpty(projectDir))
        {
            var dir = projectDir!.Replace('\\', '/');
            if (!dir.EndsWith("/"))
            {
                dir += "/";
            }

            var norm = fullPath.Replace('\\', '/');
            if (norm.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
            {
                return norm.Substring(dir.Length);
            }
        }

        return System.IO.Path.GetFileName(fullPath);
    }

    static Location FileStart(string path) =>
        Location.Create(path, new TextSpan(0, 0),
            new LinePositionSpan(new LinePosition(0, 0), new LinePosition(0, 0)));

    static bool TryGet(AnalyzerConfigOptions options, string key, out string? value)
    {
        if (options.TryGetValue(key, out var v))
        {
            value = v;
            return true;
        }

        // be defensive about casing differences between SDK versions
        var lower = key.Substring(0, key.LastIndexOf('.') + 1) +
                    key.Substring(key.LastIndexOf('.') + 1).ToLowerInvariant();
        if (options.TryGetValue(lower, out v))
        {
            value = v;
            return true;
        }

        value = null;
        return false;
    }

    static HashSet<string> BuildKnownCultures()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var c in CultureInfo.GetCultures(CultureTypes.AllCultures))
            {
                if (!string.IsNullOrEmpty(c.Name))
                {
                    set.Add(c.Name);
                }
            }
        }
        catch (CultureNotFoundException)
        {
            // Extremely constrained runtime without culture data; treat every trailing segment as a set name.
        }

        return set;
    }

    static string EscapeKeyword(string ident) =>
        CsharpKeywords.Contains(ident) ? "@" + ident : ident;

    static string Literal(string s) =>
        "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"")
            .Replace("\r", "\\r").Replace("\n", "\\n") + "\"";

    static string EscapeXml(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
            .Replace("\r", " ").Replace("\n", " ");

    readonly record struct ResInfo(AdditionalText File, string Set, string Culture, string RelPath, string Accessibility);

    sealed record ResFile(
        string Set,
        string Culture,
        string RelPath,
        string Path,
        string Accessibility,
        EquatableArray<KeyValuePair<string, string>> Entries);

    static readonly HashSet<string> CsharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
        "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
        "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof",
        "stackalloc",
        "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked",
        "unsafe", "ushort", "using", "virtual", "void", "volatile", "while"
    };
}
