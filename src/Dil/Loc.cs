using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Dil
{
    /// <summary>
    /// Runtime backing the generated <c>Resources</c> class. Loads <c>L/*.json</c>
    /// from the output directory and resolves keys against the ambient
    /// <see cref="CultureInfo.CurrentUICulture"/>, exactly like resx does.
    /// </summary>
    public static class Loc
    {
        // culture name (e.g. "tr") -> (key -> value)
        private static readonly Dictionary<string, Dictionary<string, string>> _tables =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        private static Dictionary<string, string> _default = new Dictionary<string, string>();
        private static readonly object _gate = new object();
        private static string _directory = "L";
        private static bool _loaded;

        /// <summary>
        /// Optional: point the loader at a different folder (relative to the app
        /// base directory) or force a reload after editing files on disk.
        /// </summary>
        public static void Configure(string directory = "L")
        {
            lock (_gate)
            {
                _directory = directory;
                _loaded = false;
                _tables.Clear();
                _default = new Dictionary<string, string>();
            }
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            lock (_gate)
            {
                if (_loaded) return;
                var dir = Path.Combine(AppContext.BaseDirectory, _directory);
                if (Directory.Exists(dir))
                {
                    foreach (var file in Directory.GetFiles(dir, "*.json"))
                    {
                        var name = Path.GetFileNameWithoutExtension(file);
                        var map = FlatJson.Parse(File.ReadAllText(file));
                        if (string.Equals(name, "l", StringComparison.OrdinalIgnoreCase))
                            _default = map;
                        else
                            _tables[name] = map;
                    }
                }
                _loaded = true;
            }
        }

        /// <summary>Resolve a key for the current UI culture, falling back to parent cultures then the default file.</summary>
        public static string Get(string key)
        {
            EnsureLoaded();
            for (var c = CultureInfo.CurrentUICulture;
                 c != null && !string.IsNullOrEmpty(c.Name);
                 c = c.Parent)
            {
                if (_tables.TryGetValue(c.Name, out var table) &&
                    table.TryGetValue(key, out var value))
                    return value;
            }
            return _default.TryGetValue(key, out var def) ? def : key;
        }

        /// <summary>Resolve a key and substitute named <c>{placeholder}</c> tokens.</summary>
        public static string Format(string key, params (string Name, object? Value)[] args)
        {
            var s = Get(key);
            if (args == null) return s;
            foreach (var (name, value) in args)
                s = s.Replace("{" + name + "}", value?.ToString() ?? string.Empty);
            return s;
        }

        /// <summary>Minimal allocation-light parser for a flat JSON object of string -&gt; string.</summary>
        private static class FlatJson
        {
            public static Dictionary<string, string> Parse(string text)
            {
                var result = new Dictionary<string, string>(StringComparer.Ordinal);
                int i = 0;
                SkipWs(text, ref i);
                if (i >= text.Length || text[i] != '{') return result;
                i++;
                while (i < text.Length)
                {
                    SkipWs(text, ref i);
                    if (i >= text.Length || text[i] == '}') break;
                    if (text[i] == ',') { i++; continue; }
                    if (text[i] != '"') { i++; continue; }
                    var key = ReadString(text, ref i);
                    SkipWs(text, ref i);
                    if (i < text.Length && text[i] == ':') i++;
                    SkipWs(text, ref i);
                    if (i < text.Length && text[i] == '"')
                        result[key] = ReadString(text, ref i);
                    else
                        SkipValue(text, ref i);
                }
                return result;
            }

            private static void SkipWs(string s, ref int i)
            {
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            }

            private static string ReadString(string s, ref int i)
            {
                var sb = new StringBuilder();
                i++; // opening quote
                while (i < s.Length)
                {
                    var c = s[i++];
                    if (c == '"') break;
                    if (c == '\\' && i < s.Length)
                    {
                        var e = s[i++];
                        switch (e)
                        {
                            case 'n': sb.Append('\n'); break;
                            case 't': sb.Append('\t'); break;
                            case 'r': sb.Append('\r'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'u':
                                if (i + 4 <= s.Length &&
                                    int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber,
                                        CultureInfo.InvariantCulture, out var code))
                                {
                                    sb.Append((char)code);
                                    i += 4;
                                }
                                break;
                            default: sb.Append(e); break;
                        }
                    }
                    else sb.Append(c);
                }
                return sb.ToString();
            }

            private static void SkipValue(string s, ref int i)
            {
                int depth = 0;
                while (i < s.Length)
                {
                    var c = s[i];
                    if (c == '{' || c == '[') depth++;
                    else if (c == '}' || c == ']') { if (depth == 0) break; depth--; }
                    else if (c == ',' && depth == 0) break;
                    i++;
                }
            }
        }
    }
}
