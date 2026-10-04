using System.Globalization;
using static Dil.Tests.RuntimeFixture;

namespace Dil.Tests;

[NotInParallel("Loc-global-state")]
public sealed class LiveReloadTests
{
    const string Other = "Other";

    [Before(Test)]
    public void ResetCulture() => CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

    [Test]
    [Retry(3)] // FileSystemWatcher latency is environment-dependent (esp. on macOS).
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

    [Test]
    public async Task Sets_in_one_folder_share_one_watcher()
    {
        await SetupTwoSets();
        await Assert.That(Loc.WatcherCount).IsEqualTo(1);

        Loc.LiveReload = false; // last subscriber gone -> watcher disposed
        await Assert.That(Loc.WatcherCount).IsEqualTo(0);
    }

    [Test]
    public async Task A_change_invalidates_only_the_sets_backed_by_that_file()
    {
        var dir = Path.GetFullPath(await SetupTwoSets());

        Loc.OnFileEvent(dir, "appsettings.json");
        await Assert.That(Loc.IsLoaded(Set)).IsTrue();
        await Assert.That(Loc.IsLoaded(Other)).IsTrue();

        Loc.OnFileEvent(dir, "Strings.tr.json");
        await Assert.That(Loc.IsLoaded(Set)).IsFalse();
        await Assert.That(Loc.IsLoaded(Other)).IsTrue();

        Loc.Get(Set, "k");
        Loc.OnFileEvent(dir, null); // lost events (watcher error) -> reload everything in the folder
        await Assert.That(Loc.IsLoaded(Set)).IsFalse();
        await Assert.That(Loc.IsLoaded(Other)).IsFalse();
    }

    [Test]
    [Retry(3)] // FileSystemWatcher latency is environment-dependent (esp. on macOS).
    public async Task Editing_one_sets_file_leaves_the_others_cached()
    {
        var dir = await SetupTwoSets();

        File.WriteAllText(Path.Combine(dir, "appsettings.json"), """{ "Logging": {} }""");
        File.WriteAllText(Path.Combine(dir, "Other.json"), """{ "k": "new" }""");

        var value = await Poll(() => Loc.Get(Other, "k"), v => v == "new");
        await Assert.That(value).IsEqualTo("new");
        await Assert.That(Loc.IsLoaded(Set)).IsTrue();
    }

    // "Strings" (neutral + tr) and "Other" side by side in one folder, both loaded with live reload on.
    static async Task<string> SetupTwoSets()
    {
        var dir = Setup(
            new Res("Strings.json", "", """{ "k": "s" }"""),
            new Res("Strings.tr.json", "tr", """{ "k": "s-tr" }"""));
        File.WriteAllText(Path.Combine(dir, "Other.json"), """{ "k": "old" }""");
        Loc.Register(Other, [("", "Other.json")]);

        Loc.LiveReload = true;
        Loc.Get(Set, "k"); // first access loads + starts watching
        Loc.Get(Other, "k");

        // macOS FSEvents replays writes made just before the watcher started (the files above); let that
        // burst pass, then reload, so only the test's own changes count.
        await Task.Delay(300);
        Loc.Get(Set, "k");
        Loc.Get(Other, "k");
        return dir;
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
