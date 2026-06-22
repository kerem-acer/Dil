using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Glot;

namespace Dil;

/// <summary>
/// Runtime backing the generated resource classes. Each generated class is a <em>resource set</em>
/// (named after its JSON file's base name) that registers its files via <see cref="Register"/> and
/// resolves keys against the ambient <see cref="CultureInfo.CurrentUICulture"/> — exactly like resx,
/// with parent-culture and default fallback. Sets are independent, so two sets may share key names.
/// Values are stored as UTF-8 <see cref="Text"/> (parsed straight from the JSON bytes, no UTF-16
/// transcode at parse time). When <see cref="LiveReload"/> is on (the default) edits to the JSON
/// files are picked up at runtime.
/// </summary>
public static class Loc
{
    static readonly Dictionary<string, ResourceSet> Sets = new(StringComparer.Ordinal);
    static readonly object Gate = new();
    static string? _baseDirectory;
    static bool _liveReload = true;

    /// <summary>
    /// Re-read resource files when they change on disk. On by default. Setting it invalidates every
    /// loaded set so the new mode takes effect on the next access.
    /// </summary>
    public static bool LiveReload
    {
        get
        {
            lock (Gate)
            {
                return _liveReload;
            }
        }
        set
        {
            lock (Gate)
            {
                if (_liveReload == value)
                {
                    return;
                }

                _liveReload = value;
                foreach (var rs in Sets.Values)
                {
                    rs.Loaded = false;
                    DisposeWatchers(rs);
                }
            }
        }
    }

    /// <summary>
    /// Called by generated code to declare which files back a resource set.
    /// Paths are relative to the application base directory.
    /// </summary>
    public static void Register(string set, (string Culture, string Path)[]? manifest)
    {
        lock (Gate)
        {
            if (!Sets.TryGetValue(set, out var rs))
            {
                Sets[set] = rs = new ResourceSet();
            }

            rs.Manifest = manifest ?? [];
            rs.Loaded = false;
            DisposeWatchers(rs);
        }
    }

    /// <summary>Override where relative file paths are resolved from, and/or force a reload after editing files.</summary>
    public static void Configure(string? baseDirectory = null)
    {
        lock (Gate)
        {
            _baseDirectory = baseDirectory;
            foreach (var rs in Sets.Values)
            {
                rs.Loaded = false;
                DisposeWatchers(rs);
            }
        }
    }

    /// <summary>Resolve a key for the current UI culture, falling back to parent cultures then the default (neutral) file.</summary>
    public static string Get(string set, string key)
    {
        Dictionary<string, Dictionary<string, Text>> tables;
        Dictionary<string, Text> def;
        lock (Gate)
        {
            var rs = GetOrCreate(set);
            if (!rs.Loaded)
            {
                Load(rs);
            }

            tables = rs.Tables;
            def = rs.Default;
        }

        for (var c = CultureInfo.CurrentUICulture;
             c != null && !string.IsNullOrEmpty(c.Name);
             c = c.Parent)
        {
            if (tables.TryGetValue(c.Name, out var table) &&
                table.TryGetValue(key, out var value))
            {
                return value.ToString();
            }
        }

        return def.TryGetValue(key, out var d) ? d.ToString() : key;
    }

    /// <summary>
    /// Enumerate every key/value pair visible for the current UI culture. When
    /// <paramref name="includeParentCultures"/> is <see langword="true"/> (the default) the result is the
    /// fully resolved view: the neutral (default) table overlaid by each culture up the
    /// <see cref="CultureInfo.CurrentUICulture"/> chain, with the most-specific culture winning — the same
    /// values <see cref="Get"/> would return. When <see langword="false"/> only the current UI culture's
    /// own table entries are returned, with no neutral or parent-culture merge (an empty sequence if that
    /// culture has no table). Uses the same snapshot pattern as <see cref="Get"/>: the lock is held only to
    /// grab the table snapshot; the returned dictionary is built and enumerated off the lock.
    /// </summary>
    /// <param name="set">The resource-set key.</param>
    /// <param name="includeParentCultures">Whether to merge neutral and parent-culture entries.</param>
    /// <returns>The resolved key/value pairs for the current UI culture.</returns>
    public static IEnumerable<KeyValuePair<string, string>> GetAllStrings(string set, bool includeParentCultures = true)
    {
        Dictionary<string, Dictionary<string, Text>> tables;
        Dictionary<string, Text> def;
        lock (Gate)
        {
            var rs = GetOrCreate(set);
            if (!rs.Loaded)
            {
                Load(rs);
            }

            tables = rs.Tables;
            def = rs.Default;
        }

        if (!includeParentCultures)
        {
            var own = new Dictionary<string, string>(StringComparer.Ordinal);
            if (tables.TryGetValue(CultureInfo.CurrentUICulture.Name, out var table))
            {
                foreach (var pair in table)
                {
                    own[pair.Key] = pair.Value.ToString();
                }
            }

            return own;
        }

        // Seed with the neutral default, then overlay each culture in the chain, most-specific last.
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in def)
        {
            merged[pair.Key] = pair.Value.ToString();
        }

        var chain = new List<string>();
        for (var c = CultureInfo.CurrentUICulture;
             c != null && !string.IsNullOrEmpty(c.Name);
             c = c.Parent)
        {
            chain.Add(c.Name);
        }

        for (var i = chain.Count - 1; i >= 0; i--)
        {
            if (tables.TryGetValue(chain[i], out var table))
            {
                foreach (var pair in table)
                {
                    merged[pair.Key] = pair.Value.ToString();
                }
            }
        }

        return merged;
    }

    /// <summary>
    /// Resolve a key and substitute named <c>{placeholder}</c> tokens in a single pass. Values are
    /// rendered with <see cref="IFormattable"/> (current culture) when supported, else
    /// <see cref="object.ToString"/>; a <c>null</c> value becomes the empty string.
    /// </summary>
    public static string Format(string set, string key, params (string Name, object? Value)[] args)
    {
        var template = Get(set, key);
        if (args is null || args.Length == 0 || template.IndexOf('{') < 0)
        {
            return template;
        }

        var span = template.AsSpan();
        // Glot's pooled builder assembles the result; literal runs are appended as spans.
        var builder = new TextBuilder(template.Length + 16, TextEncoding.Utf16);
        try
        {
            int i = 0, runStart = 0;
            while (i < span.Length)
            {
                if (span[i] == '{')
                {
                    var close = span.Slice(i + 1).IndexOf('}');
                    if (close >= 0)
                    {
                        // Token may carry a type hint, {name:Type}; match on the name part only.
                        var inner = span.Slice(i + 1, close);
                        var colon = inner.IndexOf(':');
                        var name = colon >= 0 ? inner.Slice(0, colon) : inner;
                        if (TryResolve(args, name, out var replacement))
                        {
                            builder.Append(span.Slice(runStart, i - runStart)); // literal run before '{'
                            builder.Append(replacement);
                            i += close + 2; // skip '{' … '}'
                            runStart = i;
                            continue;
                        }
                    }
                }

                i++;
            }

            builder.Append(span.Slice(runStart)); // trailing literal run
            return builder.ToString();
        }
        finally
        {
            builder.Dispose();
        }
    }

    static bool TryResolve((string Name, object? Value)[] args, ReadOnlySpan<char> name, out string replacement)
    {
        foreach (var (argName, value) in args)
        {
            if (name.SequenceEqual(argName.AsSpan()))
            {
                replacement = Render(value);
                return true;
            }
        }

        replacement = string.Empty;
        return false;
    }

    static string Render(object? value) => value switch
    {
        null => string.Empty,
        string s => s,
        IFormattable f => f.ToString(null, CultureInfo.CurrentCulture),
        _ => value.ToString() ?? string.Empty,
    };

    static ResourceSet GetOrCreate(string set)
    {
        if (!Sets.TryGetValue(set, out var rs))
        {
            Sets[set] = rs = new ResourceSet();
        }

        return rs;
    }

    // Builds fresh dictionaries (never mutates the live ones) so readers holding a snapshot are safe.
    static void Load(ResourceSet rs)
    {
        var baseDir = _baseDirectory ?? AppContext.BaseDirectory;
        var tables = new Dictionary<string, Dictionary<string, Text>>(StringComparer.OrdinalIgnoreCase);
        var def = new Dictionary<string, Text>(StringComparer.Ordinal);
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (culture, relPath) in rs.Manifest)
        {
            var full = Path.Combine(baseDir, relPath.Replace('/', Path.DirectorySeparatorChar));
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir))
            {
                dirs.Add(dir);
            }

            if (!File.Exists(full))
            {
                continue;
            }

            Dictionary<string, Text> target;
            if (string.IsNullOrEmpty(culture))
            {
                target = def;
            }
            else if (tables.TryGetValue(culture, out var existing))
            {
                target = existing;
            }
            else
            {
                tables[culture] = target = new Dictionary<string, Text>(StringComparer.Ordinal);
            }

            TryLoadFile(full, target);
        }

        rs.Tables = tables;
        rs.Default = def;

        if (_liveReload && rs.Watchers is null)
        {
            SetupWatchers(rs, dirs);
        }

        rs.Loaded = true;
    }

    static void SetupWatchers(ResourceSet rs, HashSet<string> dirs)
    {
        var watchers = new List<FileSystemWatcher>();
        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            var watcher = new FileSystemWatcher(dir, "*.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            watcher.Changed += OnResourceFileChanged(rs);
            watcher.Created += OnResourceFileChanged(rs);
            watcher.Deleted += OnResourceFileChanged(rs);
            watcher.Renamed += (_, _) => Invalidate(rs);
            watcher.EnableRaisingEvents = true;
            watchers.Add(watcher);
        }

        rs.Watchers = watchers;
    }

    static FileSystemEventHandler OnResourceFileChanged(ResourceSet rs) => (_, _) => Invalidate(rs);

    static void Invalidate(ResourceSet rs)
    {
        lock (Gate)
        {
            rs.Loaded = false;
        }
    }

    static void DisposeWatchers(ResourceSet rs)
    {
        if (rs.Watchers is null)
        {
            return;
        }

        foreach (var watcher in rs.Watchers)
        {
            watcher.Dispose();
        }

        rs.Watchers = null;
    }

    /// <summary>
    /// Reads a flat JSON object of string-&gt;string with <see cref="Utf8JsonReader"/>, copying each
    /// value's unescaped UTF-8 directly into a Glot <see cref="Text"/> (no UTF-16 transcode). Non-string
    /// values are skipped; comments and trailing commas are tolerated. A file that is briefly locked
    /// (e.g. mid-save) is skipped — the next change event triggers another reload.
    /// </summary>
    static void TryLoadFile(string path, Dictionary<string, Text> into)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var length = (int)stream.Length;
            if (length == 0)
            {
                return;
            }

            var buffer = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                var read = 0;
                int n;
                while (read < length && (n = stream.Read(buffer, read, length - read)) > 0)
                {
                    read += n;
                }

                var utf8 = new ReadOnlySpan<byte>(buffer, 0, read);
                // Strip a UTF-8 BOM if present — Utf8JsonReader does not.
                if (utf8.Length >= 3 && utf8[0] == 0xEF && utf8[1] == 0xBB && utf8[2] == 0xBF)
                {
                    utf8 = utf8.Slice(3);
                }

                Parse(utf8, into);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (IOException)
        {
            // File is locked or vanished mid-read; leave its keys to fall back until the next reload.
        }
    }

    static void Parse(ReadOnlySpan<byte> utf8, Dictionary<string, Text> into)
    {
        var reader = new Utf8JsonReader(utf8, new JsonReaderOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            return;
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var key = reader.GetString()!;
            if (!reader.Read())
            {
                break;
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                into[key] = ReadUtf8Value(ref reader);
            }
            else
            {
                reader.Skip();
            }
        }
    }

    static Text ReadUtf8Value(ref Utf8JsonReader reader)
    {
        var max = reader.HasValueSequence ? checked((int)reader.ValueSequence.Length) : reader.ValueSpan.Length;
        if (max == 0)
        {
            return Text.From(string.Empty);
        }

        var buffer = ArrayPool<byte>.Shared.Rent(max);
        try
        {
            var written = reader.CopyString(buffer);
            return Text.FromUtf8(new ReadOnlySpan<byte>(buffer, 0, written));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    sealed class ResourceSet
    {
        public (string Culture, string Path)[] Manifest = [];
        public Dictionary<string, Dictionary<string, Text>> Tables = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Text> Default = new(StringComparer.Ordinal);
        public bool Loaded;
        public List<FileSystemWatcher>? Watchers;
    }
}
