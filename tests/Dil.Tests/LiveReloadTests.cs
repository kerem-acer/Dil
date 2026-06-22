using System.Globalization;
using static Dil.Tests.RuntimeFixture;

namespace Dil.Tests;

[NotInParallel("Loc-global-state")]
public sealed class LiveReloadTests
{
    [Before(Test)]
    public void ResetCulture() => CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

    [Test]
    public async Task Picks_up_file_changes_when_enabled()
    {
        var dir = Setup(new Res("Strings.json", "", """{ "k": "old" }"""));
        Loc.LiveReload = true;
        await Assert.That(Loc.Get(Set, "k")).IsEqualTo("old"); // first access loads + starts watching

        File.WriteAllText(Path.Combine(dir, "Strings.json"), """{ "k": "new" }""");

        var value = await Poll(() => Loc.Get(Set, "k"), v => v == "new");
        await Assert.That(value).IsEqualTo("new");
    }

    [Test]
    public async Task Does_not_reload_when_disabled()
    {
        var dir = Setup(new Res("Strings.json", "", """{ "k": "old" }""")); // fixture leaves LiveReload off
        await Assert.That(Loc.Get(Set, "k")).IsEqualTo("old");

        File.WriteAllText(Path.Combine(dir, "Strings.json"), """{ "k": "new" }""");
        await Task.Delay(300);
        await Assert.That(Loc.Get(Set, "k")).IsEqualTo("old"); // no watcher -> still cached

        Loc.Configure(dir); // explicit reload still works
        await Assert.That(Loc.Get(Set, "k")).IsEqualTo("new");
    }

    static async Task<string> Poll(Func<string> read, Func<string, bool> done, int timeoutMs = 5000)
    {
        for (var waited = 0; waited < timeoutMs; waited += 50)
        {
            var value = read();
            if (done(value))
            {
                return value;
            }

            await Task.Delay(50);
        }

        return read();
    }
}
