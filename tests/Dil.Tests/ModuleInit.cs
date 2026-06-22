using System.Runtime.CompilerServices;

namespace Dil.Tests;

static class ModuleInit
{
    [ModuleInitializer]
    public static void Init()
    {
        VerifySourceGenerators.Initialize();

        // Keep all *.verified.* / *.received.* snapshots in a Snapshots/ folder next to the tests.
        DerivePathInfo((sourceFile, _, type, method) =>
            new PathInfo(
                directory: Path.Combine(Path.GetDirectoryName(sourceFile)!, "Snapshots"),
                typeName: type.Name,
                methodName: method.Name));
    }
}
