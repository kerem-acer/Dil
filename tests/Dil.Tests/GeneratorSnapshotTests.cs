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
    public Task CultureFilesWithoutNeutralFileGenerateTheSet()
    {
        // No Strings.json: the keys are the union of the culture files (hello, greeting, bye), each
        // culture is checked against all of them (DIL001), and a summary comes from the first file
        // that has the key.
        var driver = GeneratorHarness.RunDriver("MyApp",
            new ResourceInput("Strings.en.json", """{ "hello": "Hello", "greeting": "Hello, {name}!" }"""),
            new ResourceInput("Strings.tr.json", """{ "hello": "Merhaba", "bye": "Hoşça kal" }"""));

        return Verify(driver);
    }

    [Test]
    public Task DefaultCultureFileOwnsASetWithoutNeutralFile()
    {
        // DefaultCulture metadata (tr) wins over the project-wide property (de, which has no file). The
        // tr file then supplies the key order, the summaries, and the class's accessibility.
        var driver = GeneratorHarness.RunDriver("MyApp", defaultAccessibility: null, defaultCulture: "de",
            new ResourceInput("Strings.en.json", """{ "hello": "Hello", "bye": "Goodbye" }"""),
            new ResourceInput("Strings.tr.json", """{ "bye": "Hoşça kal", "hello": "Merhaba" }""",
                Accessibility: "public", DefaultCulture: "tr"));

        return Verify(driver);
    }

    [Test]
    public Task ProjectDefaultCultureComesBeforeNeutralFile()
    {
        // DilDefaultCulture en-US has no file of its own, so its parent en supplies the summaries. The
        // neutral file still defines the keys and fills in the summary for a key en lacks.
        var driver = GeneratorHarness.RunDriver("MyApp", defaultAccessibility: null, defaultCulture: "en-US",
            new ResourceInput("Strings.json", """{ "hello": "Hello (neutral)", "bye": "Bye (neutral)" }"""),
            new ResourceInput("Strings.en.json", """{ "hello": "Hello" }"""));

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
    public Task TemplateStructNamesNeverRenameKeys()
    {
        // The real "greetingTemplate" key keeps its name; greeting's struct is numbered instead
        // (GreetingTemplate2). Compilation of the same input is covered by GeneratedCodeTests.
        var driver = GeneratorHarness.RunDriver("MyApp",
            new ResourceInput("Strings.json", GeneratedCodeTests.ClashingKeys));

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
    public Task FolderDoesNotChangeTheNamespaceByDefault()
    {
        // Without DilNamespaceFromFolder a set stays in RootNamespace wherever its files live; the folder
        // only shows up in the manifest path.
        var driver = GeneratorHarness.RunDriver("MyApp",
            new ResourceInput(GeneratorHarness.ProjectDir + "Localization/Resources/Strings.json", """{ "hello": "Hello" }"""));

        return Verify(driver);
    }

    [Test]
    public Task NamespaceMetadataWinsOverDilNamespace()
    {
        // Strings names its namespace on the item (siblings copy it, so both files carry it); Errors has
        // none, so the project-wide DilNamespace applies. Both beat the folder, even with it opted in.
        var driver = GeneratorHarness.RunDriverWith("MyApp",
            new Dictionary<string, string>
            {
                ["DilNamespace"] = "MyApp.Localization.Resources",
                ["DilNamespaceFromFolder"] = "true",
            },
            new ResourceInput(GeneratorHarness.ProjectDir + "AgentUserManagement/Strings.json",
                """{ "hello": "Hello, {name}!" }""", Namespace: "InsuranceUp.Modules.AgentUserManagement.Application.Resources"),
            new ResourceInput(GeneratorHarness.ProjectDir + "AgentUserManagement/Strings.tr.json",
                """{ "hello": "Merhaba, {name}!" }""", Namespace: "InsuranceUp.Modules.AgentUserManagement.Application.Resources"),
            new ResourceInput(GeneratorHarness.ProjectDir + "Localization/Errors.json", """{ "oops": "Oops" }"""));

        return Verify(driver);
    }

    [Test]
    public Task NamespaceFromFolderAppendsTheFolderLikeResx()
    {
        // RootNamespace plus the file's folder; a file at the project root (or outside it) stays in
        // RootNamespace, and folder names that aren't identifiers are fixed up the way VS does for resx.
        var driver = GeneratorHarness.RunDriverWith("MyApp",
            new Dictionary<string, string> { ["DilNamespaceFromFolder"] = "true" },
            new ResourceInput(GeneratorHarness.ProjectDir + "Localization/Resources/Strings.json",
                """{ "greeting": "Hello, {name}!" }"""),
            new ResourceInput(GeneratorHarness.ProjectDir + "Localization/Resources/Strings.tr.json",
                """{ "greeting": "Merhaba, {name}!" }"""),
            new ResourceInput(GeneratorHarness.ProjectDir + "Errors.json", """{ "oops": "Oops" }"""),
            new ResourceInput(GeneratorHarness.ProjectDir + "My Folder/2nd.Level/class/Odd.json", """{ "note": "Note" }"""));

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
