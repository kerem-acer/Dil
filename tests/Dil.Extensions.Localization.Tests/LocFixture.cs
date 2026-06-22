
namespace Dil.Extensions.Localization.Tests;

/// <summary>Writes temp JSON files and registers them with <see cref="Loc"/> under a named set.</summary>
static class LocFixture
{
    public static string Setup(string set, params (string Name, string Culture, string Json)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "dil-loc-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        var manifest = new (string Culture, string Path)[files.Length];
        for (var i = 0; i < files.Length; i++)
        {
            File.WriteAllText(Path.Combine(dir, files[i].Name), files[i].Json);
            manifest[i] = (files[i].Culture, files[i].Name);
        }

        Loc.LiveReload = false;
        Loc.Register(set, manifest);
        Loc.Configure(dir);
        return dir;
    }
}

/// <summary>Marker type whose simple name ("Strings") is the resource-set key used in the typed tests.</summary>
public sealed class Strings;

/// <summary>Probe flag flipped by <see cref="RegisteringMarker"/>'s static constructor.</summary>
public static class StaticCtorProbe
{
    public static bool Ran { get; set; }
}

/// <summary>
/// Stands in for a Dil-generated resource class: its static constructor has a side effect, so a test
/// can prove that constructing <c>DilStringLocalizer&lt;RegisteringMarker&gt;</c> runs it (the mechanism
/// that lets DI usage self-register without touching the typed members).
/// </summary>
public sealed class RegisteringMarker
{
    static RegisteringMarker() => StaticCtorProbe.Ran = true;
}
