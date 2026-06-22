namespace Dil.Tests;

/// <summary>
/// Snapshot tests for <see cref="Generator.LocalizationGenerator"/> via Verify.SourceGenerators:
/// each test runs the generator and verifies the full driver result (generated source + diagnostics)
/// against a committed <c>*.verified.txt</c>. Update snapshots with the Verify tooling when the
/// generator output intentionally changes.
/// </summary>
public sealed class GeneratorSnapshotTests
{
    const string Neutral =

                             """{ "hello": "Hello", "greeting": "Hello, {name}!", "inbox": "You have {count} unread messages" }""";

    [Test]
    public Task NeutralAndFullTranslation()
    {
        var driver = GeneratorHarness.RunDriver("MyApp",
            new ResourceInput("Strings.json", Neutral),
            new ResourceInput("Strings.tr.json",

                                     """{ "hello": "Merhaba", "greeting": "Merhaba, {name}!", "inbox": "{count} okunmamış mesajınız var" }"""));

        return Verify(driver);
    }

    [Test]
    public Task PartialTranslationReportsDIL001()
    {
        var driver = GeneratorHarness.RunDriver("MyApp",
            new ResourceInput("Strings.json", Neutral),
            new ResourceInput("Strings.de.json", """{ "hello": "Hallo", "greeting": "Hallo, {name}!" }"""));

        return Verify(driver);
    }

    [Test]
    public Task OnlyCultureFileReportsDIL002()
    {
        var driver = GeneratorHarness.RunDriver("MyApp",
            new ResourceInput("Strings.tr.json", """{ "hello": "Merhaba" }"""));

        return Verify(driver);
    }

    [Test]
    public Task MemberNamingPascalcaseCollisionsKeywordsAndDigits()
    {
        var driver = GeneratorHarness.RunDriver("MyApp",
            new ResourceInput("Strings.json",

                                     """
                {
                  "user.first-name": "First",
                  "save_changes": "Save",
                  "foo-bar": "1",
                  "foo.bar": "2",
                  "1st": "first",
                  "msg": "in {class} for {event}",
                  "dup": "{x} and {x}"
                }
                """));

        return Verify(driver);
    }

    [Test]
    public Task DefaultNamespaceWhenRootNamespaceMissing()
    {
        var driver = GeneratorHarness.RunDriver("", new ResourceInput("Strings.json", Neutral));
        return Verify(driver);
    }

    [Test]
    public Task UnmarkedFilesAreInvisible()
    {
        var driver = GeneratorHarness.RunDriver("MyApp",
            new ResourceInput("appsettings.json", """{ "secret": "value" }""", DilResource: false));

        return Verify(driver);
    }

    [Test]
    public Task NoResourceFilesProducesNothing()
    {
        var driver = GeneratorHarness.RunDriver("MyApp");
        return Verify(driver);
    }

    [Test]
    public Task SkipsNonStringValuesAndPreservesKeyOrder()
    {
        var driver = GeneratorHarness.RunDriver("MyApp",
            new ResourceInput("Strings.json",

                                     """{ "zebra": "Z", "num": 5, "obj": { "x": "y" }, "apple": "A" }"""));

        return Verify(driver);
    }

    [Test]
    public Task TypedPlaceholdersGenerateTypedParameters()
    {
        var driver = GeneratorHarness.RunDriver("MyApp",
            new ResourceInput("Strings.json",

                                     """{ "greet": "Hello, {name:string}!", "mix": "{a:int} then {b}" }"""));

        return Verify(driver);
    }

    [Test]
    public Task MalformedJsonKeepsParsedKeys()
    {
        // Truncated JSON: the parser keeps what it read ("a") and swallows the JsonException.
        var driver = GeneratorHarness.RunDriver("MyApp",
            new ResourceInput("Strings.json", """{ "a": "A", "b": """));

        return Verify(driver);
    }

    [Test]
    public Task NonObjectRootYieldsEmptyClass()
    {
        var driver = GeneratorHarness.RunDriver("MyApp",
            new ResourceInput("Strings.json", "[1, 2, 3]"));

        return Verify(driver);
    }

    [Test]
    public Task CommentsAndTrailingCommasAreParsed()
    {
        var driver = GeneratorHarness.RunDriver("MyApp",
            new ResourceInput("Strings.json", """{ "a": "A", /* note */ "b": "B", }"""));

        return Verify(driver);
    }

    [Test]
    public Task KeyCollidingWithClassNameIsDisambiguated()
    {
        // Key "strings" would PascalCase to "Strings" — the same as the class — which is CS0542.
        var driver = GeneratorHarness.RunDriver("MyApp",
            new ResourceInput("Strings.json", """{ "strings": "all", "hello": "Hello" }"""));

        return Verify(driver);
    }

    [Test]
    public Task NonCultureTrailingSegmentStaysInSetName()
    {
        // "New" is not a real culture, so Order.New.json must be the neutral file of set "Order.New",
        // not culture "New" of set "Order".
        var driver = GeneratorHarness.RunDriver("MyApp",
            new ResourceInput("Order.New.json", """{ "ok": "OK" }"""));

        return Verify(driver);
    }

    [Test]
    public Task InvalidPlaceholderTypeFallsBackToGeneric()
    {
        // A malformed type hint must not be injected verbatim into the signature.
        var driver = GeneratorHarness.RunDriver("MyApp",
            new ResourceInput("Strings.json", """{ "m": "hi {x:int; evil()}" }"""));

        return Verify(driver);
    }

    [Test]
    public Task MultipleSetsGenerateSeparateClasses()
    {
        var driver = GeneratorHarness.RunDriver("MyApp",
            new ResourceInput("Customer.json", """{ "name": "Name", "email": "Email" }"""),
            new ResourceInput("Customer.tr.json", """{ "name": "Ad", "email": "E-posta" }"""),
            new ResourceInput("User.json", """{ "id": "Id" }"""));

        return Verify(driver);
    }

    [Test]
    public Task GlobalDilAccessibilityPublicMakesClassPublic()
    {
        var driver = GeneratorHarness.RunDriver("MyApp", defaultAccessibility: "public",
            new ResourceInput("Strings.json", Neutral));

        return Verify(driver);
    }

    [Test]
    public Task PerFileAccessibilityOverridesGlobalDefault()
    {
        // Global default is public, but this resource opts back to internal per-file.
        var driver = GeneratorHarness.RunDriver("MyApp", defaultAccessibility: "public",
            new ResourceInput("Strings.json", Neutral, Accessibility: "internal"));

        return Verify(driver);
    }

    [Test]
    public Task NeutralFileAccessibilityWinsOverCultureFile()
    {
        // The neutral file owns the set: it stays internal even though the culture file asks for public.
        var driver = GeneratorHarness.RunDriver("MyApp",
            new ResourceInput("Strings.json", Neutral),
            new ResourceInput("Strings.tr.json", """{ "hello": "Merhaba", "greeting": "Merhaba, {name}!", "inbox": "{count} okunmamış mesajınız var" }""", Accessibility: "public"));

        return Verify(driver);
    }
}
