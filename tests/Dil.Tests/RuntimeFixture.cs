using System.Text;

namespace Dil.Tests;

/// <summary>
/// Writes localization JSON to a throwaway directory and points <see cref="Loc"/> at it under a
/// fixed resource set. Because <see cref="Loc"/> is process-global, every test that uses it is
/// marked <c>[NotInParallel]</c>.
/// </summary>
static class RuntimeFixture
{
    public const string Set = "Strings";

    public readonly record struct Res(string Name, string Culture, string Json, bool WriteBom = false);

    public static string Setup(params Res[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "dil-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        var manifest = new (string Culture, string Path)[files.Length];
        for (var i = 0; i < files.Length; i++)
        {
            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: files[i].WriteBom);
            File.WriteAllText(Path.Combine(dir, files[i].Name), files[i].Json, encoding);
            manifest[i] = (files[i].Culture, files[i].Name);
        }

        Loc.LiveReload = false; // deterministic by default; live-reload tests opt back in
        Loc.Register(Set, manifest);
        Loc.Configure(dir);
        return dir;
    }
}
