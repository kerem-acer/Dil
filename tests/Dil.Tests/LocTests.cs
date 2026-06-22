using System.Globalization;
using static Dil.Tests.RuntimeFixture;

namespace Dil.Tests;

[NotInParallel("Loc-global-state")]
public sealed class LocTests
{
    [Before(Test)]
    public void ResetCulture()
    {
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    // ---- culture resolution & fallback -------------------------------------------------

    [Test]
    public async Task NeutralCultureReturnsDefaultValue()
    {
        Setup(new Res("Strings.json", "", """{ "hello": "Hello" }"""));
        await Assert.That(Loc.Get(Set, "hello")).IsEqualTo("Hello");
    }

    [Test]
    public async Task SpecificCultureOverridesDefault()
    {
        Setup(
            new Res("Strings.json", "", """{ "hello": "Hello" }"""),
            new Res("Strings.tr.json", "tr", """{ "hello": "Merhaba" }"""));

        CultureInfo.CurrentUICulture = new CultureInfo("tr");
        await Assert.That(Loc.Get(Set, "hello")).IsEqualTo("Merhaba");
    }

    [Test]
    public async Task MissingKeyInCultureFallsBackToDefault()
    {
        Setup(
            new Res("Strings.json", "", """{ "hello": "Hello", "bye": "Bye" }"""),
            new Res("Strings.de.json", "de", """{ "hello": "Hallo" }"""));

        CultureInfo.CurrentUICulture = new CultureInfo("de");
        await Assert.That(Loc.Get(Set, "hello")).IsEqualTo("Hallo");
        await Assert.That(Loc.Get(Set, "bye")).IsEqualTo("Bye"); // not translated -> neutral
    }

    [Test]
    public async Task RegionCultureFallsBackToParentLanguage()
    {
        Setup(
            new Res("Strings.json", "", """{ "hello": "Hello" }"""),
            new Res("Strings.tr.json", "tr", """{ "hello": "Merhaba" }"""));

        CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");
        await Assert.That(Loc.Get(Set, "hello")).IsEqualTo("Merhaba"); // tr-TR -> tr
    }

    [Test]
    public async Task UnknownKeyReturnsTheKeyItself()
    {
        Setup(new Res("Strings.json", "", """{ "hello": "Hello" }"""));
        await Assert.That(Loc.Get(Set, "does.not.exist")).IsEqualTo("does.not.exist");
    }

    [Test]
    public async Task LaterFileForSameCultureOverridesEarlierKey()
    {
        Setup(
            new Res("A.json", "", """{ "k": "first" }"""),
            new Res("B.json", "", """{ "k": "second" }"""));

        await Assert.That(Loc.Get(Set, "k")).IsEqualTo("second");
    }

    // ---- JSON parsing (via the STJ-backed loader) --------------------------------------

    [Test]
    public async Task ParsesEscapeSequences()
    {
        Setup(new Res("Strings.json", "",

                                 """{ "nl": "a\nb", "tab": "a\tb", "quote": "a\"b", "slash": "a\/b" }"""));

        await Assert.That(Loc.Get(Set, "nl")).IsEqualTo("a\nb");
        await Assert.That(Loc.Get(Set, "tab")).IsEqualTo("a\tb");
        await Assert.That(Loc.Get(Set, "quote")).IsEqualTo("a\"b");
        await Assert.That(Loc.Get(Set, "slash")).IsEqualTo("a/b");
    }

    [Test]
    public async Task ParsesUnicodeEscapeAndRawNonAscii()
    {
        Setup(new Res("Strings.json", "",

                                 """{ "u": "café", "raw": "öğ日本" }"""));

        await Assert.That(Loc.Get(Set, "u")).IsEqualTo("café");
        await Assert.That(Loc.Get(Set, "raw")).IsEqualTo("öğ日本");
    }

    [Test]
    public async Task ToleratesUtf8Bom()
    {
        Setup(new Res("Strings.json", "", """{ "hello": "Hello" }""", WriteBom: true));
        await Assert.That(Loc.Get(Set, "hello")).IsEqualTo("Hello");
    }

    [Test]
    public async Task ToleratesCommentsAndTrailingCommas()
    {
        Setup(new Res("Strings.json", "",

                          """
            {
              // a line comment
              "hello": "Hello",
              "bye": "Bye",
            }
            """));

        await Assert.That(Loc.Get(Set, "hello")).IsEqualTo("Hello");
        await Assert.That(Loc.Get(Set, "bye")).IsEqualTo("Bye");
    }

    [Test]
    public async Task SkipsNonStringAndNestedValues()
    {
        Setup(new Res("Strings.json", "",

                                 """{ "num": 5, "obj": { "x": "y" }, "arr": [1, 2], "ok": "yes" }"""));

        await Assert.That(Loc.Get(Set, "ok")).IsEqualTo("yes");
        await Assert.That(Loc.Get(Set, "num")).IsEqualTo("num");   // skipped -> key fallback
        await Assert.That(Loc.Get(Set, "obj")).IsEqualTo("obj");
        await Assert.That(Loc.Get(Set, "arr")).IsEqualTo("arr");
    }

    [Test]
    public async Task EmptyOrMissingFileIsIgnored()
    {
        var dir = Setup(new Res("Strings.json", "", ""));
        await Assert.That(Loc.Get(Set, "anything")).IsEqualTo("anything");
        await Assert.That(Directory.Exists(dir)).IsTrue();
    }

    // ---- Format ------------------------------------------------------------------------

    [Test]
    public async Task FormatSubstitutesNamedPlaceholder()
    {
        Setup(new Res("Strings.json", "", """{ "greet": "Hello {name}!" }"""));
        await Assert.That(Loc.Format(Set, "greet", ("name", "World"))).IsEqualTo("Hello World!");
    }

    [Test]
    public async Task FormatSubstitutesMultipleAndAdjacentPlaceholders()
    {
        Setup(new Res("Strings.json", "", """{ "s": "{a}{b} {a}-{c}" }"""));
        await Assert.That(Loc.Format(Set, "s", ("a", 1), ("b", 2), ("c", 3))).IsEqualTo("12 1-3");
    }

    [Test]
    public async Task FormatRepeatedPlaceholderSubstitutedEverywhere()
    {
        Setup(new Res("Strings.json", "", """{ "s": "{x} and {x} and {x}" }"""));
        await Assert.That(Loc.Format(Set, "s", ("x", "A"))).IsEqualTo("A and A and A");
    }

    [Test]
    public async Task FormatUnknownPlaceholderIsLeftIntact()
    {
        Setup(new Res("Strings.json", "", """{ "s": "Hi {name}, bye {other}" }"""));
        await Assert.That(Loc.Format(Set, "s", ("name", "X"))).IsEqualTo("Hi X, bye {other}");
    }

    [Test]
    public async Task FormatNullValueBecomesEmpty()
    {
        Setup(new Res("Strings.json", "", """{ "s": "[{x}]" }"""));
        await Assert.That(Loc.Format(Set, "s", ("x", null))).IsEqualTo("[]");
    }

    [Test]
    public async Task FormatWithoutPlaceholdersReturnsTemplate()
    {
        Setup(new Res("Strings.json", "", """{ "s": "no tokens here" }"""));
        await Assert.That(Loc.Format(Set, "s", ("x", "ignored"))).IsEqualTo("no tokens here");
    }

    [Test]
    public async Task FormatUnclosedBraceIsPreserved()
    {
        Setup(new Res("Strings.json", "", """{ "s": "Hi {name" }"""));
        await Assert.That(Loc.Format(Set, "s", ("name", "X"))).IsEqualTo("Hi {name");
    }

    [Test]
    public async Task FormatUsesInvariantValueFormatting()
    {
        Setup(new Res("Strings.json", "", """{ "s": "n={n}" }"""));
        await Assert.That(Loc.Format(Set, "s", ("n", 42))).IsEqualTo("n=42");
    }

    [Test]
    public async Task FormatRendersFormattableWithCurrentCulture()
    {
        Setup(new Res("Strings.json", "", """{ "s": "{n}" }"""));
        CultureInfo.CurrentCulture = new CultureInfo("de-DE"); // decimal comma
        await Assert.That(Loc.Format(Set, "s", ("n", 3.5))).IsEqualTo("3,5");
    }

    [Test]
    public async Task FormatSubstitutesTypedPlaceholders()
    {
        Setup(new Res("Strings.json", "", """{ "s": "Hi {name:string}, you have {count:int} msgs" }"""));
        await Assert.That(Loc.Format(Set, "s", ("name", "Ada"), ("count", 3)))
            .IsEqualTo("Hi Ada, you have 3 msgs");
    }

    [Test]
    public async Task FormatResolvesCultureValueBeforeSubstituting()
    {
        Setup(
            new Res("Strings.json", "", """{ "s": "Hello {name}" }"""),
            new Res("Strings.tr.json", "tr", """{ "s": "Merhaba {name}" }"""));

        CultureInfo.CurrentUICulture = new CultureInfo("tr");
        await Assert.That(Loc.Format(Set, "s", ("name", "Ada"))).IsEqualTo("Merhaba Ada");
    }

    // ---- reload ------------------------------------------------------------------------

    [Test]
    public async Task ConfigureForcesReloadAfterFileChange()
    {
        var dir = Setup(new Res("Strings.json", "", """{ "k": "old" }"""));
        await Assert.That(Loc.Get(Set, "k")).IsEqualTo("old");

        File.WriteAllText(Path.Combine(dir, "Strings.json"), """{ "k": "new" }""");
        Loc.Configure(dir); // same dir, but resets the loaded flag
        await Assert.That(Loc.Get(Set, "k")).IsEqualTo("new");
    }

    [Test]
    public async Task GetAllStringsExcludingParentsIsEmptyWithoutCultureTable()
    {
        Setup(new Res("Strings.json", "", """{ "a": "A" }"""));
        // Invariant current UI culture from [Before]: there is no culture-specific table.
        await Assert.That(Loc.GetAllStrings(Set, includeParentCultures: false).Any()).IsFalse();
        await Assert.That(Loc.GetAllStrings(Set, includeParentCultures: true).Count()).IsEqualTo(1);
    }

    [Test]
    public async Task LockedResourceFileIsSkippedOnReload()
    {
        var dir = Setup(new Res("Strings.json", "", """{ "k": "v" }"""));
        await Assert.That(Loc.Get(Set, "k")).IsEqualTo("v");

        var path = Path.Combine(dir, "Strings.json");
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Loc.Configure(dir); // force a reload while the file is exclusively locked
            // The reader can't open the locked file -> IOException is swallowed -> key falls back.
            await Assert.That(Loc.Get(Set, "k")).IsEqualTo("k");
        }
    }
}
