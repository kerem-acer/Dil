using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Dil.Extensions.Localization.Tests;

[NotInParallel("Loc-global-state")]
public sealed class DilLocalizerTests
{
    [Before(Test)]
    public void ResetCulture()
    {
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    [Test]
    public async Task Indexer_returns_localized_value()
    {
        LocFixture.Setup("Strings", ("Strings.json", "", """{ "hello": "Hello" }"""));
        var loc = new DilStringLocalizer("Strings");

        var result = loc["hello"];
        await Assert.That(result.Value).IsEqualTo("Hello");
        await Assert.That(result.ResourceNotFound).IsFalse();
    }

    [Test]
    public async Task Missing_key_reports_resource_not_found()
    {
        LocFixture.Setup("Strings", ("Strings.json", "", """{ "hello": "Hello" }"""));
        var loc = new DilStringLocalizer("Strings");

        var result = loc["nope"];
        await Assert.That(result.Value).IsEqualTo("nope");
        await Assert.That(result.ResourceNotFound).IsTrue();
    }

    [Test]
    public async Task Indexer_with_args_uses_positional_string_format()
    {
        LocFixture.Setup("Strings", ("Strings.json", "", """{ "p": "n={0} d={1:N2}" }"""));
        var loc = new DilStringLocalizer("Strings");
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; // deterministic decimal separator

        // string.Format semantics: positional + the N2 specifier (which Dil's named Format would ignore).
        await Assert.That(loc["p", 5, 3.5].Value).IsEqualTo("n=5 d=3.50");
    }

    [Test]
    public async Task Named_template_with_args_is_returned_unformatted()
    {
        LocFixture.Setup("Strings", ("Strings.json", "", """{ "g": "Hi {name}" }"""));
        var loc = new DilStringLocalizer("Strings");

        // {name} is not a positional token -> string.Format would throw; the adapter returns it as-is.
        await Assert.That(loc["g", "Ada"].Value).IsEqualTo("Hi {name}");
    }

    [Test]
    public async Task Typed_localizer_maps_to_set_named_after_T()
    {
        LocFixture.Setup("Strings", ("Strings.json", "", """{ "hello": "Hello" }"""));
        var loc = new DilStringLocalizer<Strings>();

        await Assert.That(loc["hello"].Value).IsEqualTo("Hello");
    }

    [Test]
    public async Task Typed_localizer_runs_resource_class_static_constructor()
    {
        // RegisteringMarker is referenced nowhere else, so its static ctor has not run yet.
        await Assert.That(StaticCtorProbe.Ran).IsFalse();
        _ = new DilStringLocalizer<RegisteringMarker>();
        await Assert.That(StaticCtorProbe.Ran).IsTrue(); // construction forced the static initializer
    }

    [Test]
    public async Task Factory_creates_localizer_from_type_and_base_name()
    {
        LocFixture.Setup("Strings", ("Strings.json", "", """{ "hello": "Hello" }"""));
        var factory = new DilStringLocalizerFactory();

        await Assert.That(factory.Create(typeof(Strings))["hello"].Value).IsEqualTo("Hello");
        await Assert.That(factory.Create("My.App.Strings", "asm")["hello"].Value).IsEqualTo("Hello");
    }

    [Test]
    public async Task GetAllStrings_merges_or_isolates_cultures()
    {
        LocFixture.Setup("Bag",
            ("Bag.json", "", """{ "a": "A", "b": "B" }"""),
            ("Bag.tr.json", "tr", """{ "a": "Ã" }"""));
        var loc = new DilStringLocalizer("Bag");
        CultureInfo.CurrentUICulture = new CultureInfo("tr");

        var merged = loc.GetAllStrings(includeParentCultures: true).ToDictionary(s => s.Name, s => s.Value);
        await Assert.That(merged).Count().IsEqualTo(2);
        await Assert.That(merged["a"]).IsEqualTo("Ã");   // tr overrides neutral
        await Assert.That(merged["b"]).IsEqualTo("B");   // from neutral

        var own = loc.GetAllStrings(includeParentCultures: false).ToDictionary(s => s.Name, s => s.Value);
        await Assert.That(own).Count().IsEqualTo(1);     // only tr's own entries
        await Assert.That(own["a"]).IsEqualTo("Ã");
    }

    [Test]
    public async Task AddDilLocalization_registers_factory_and_typed_localizer()
    {
        LocFixture.Setup("Strings", ("Strings.json", "", """{ "hello": "Hello" }"""));

        var provider = new ServiceCollection().AddDilLocalization().BuildServiceProvider();

        var typed = provider.GetRequiredService<IStringLocalizer<Strings>>();
        await Assert.That(typed["hello"].Value).IsEqualTo("Hello");

        var f1 = provider.GetRequiredService<IStringLocalizerFactory>();
        var f2 = provider.GetRequiredService<IStringLocalizerFactory>();
        await Assert.That(f1).IsSameReferenceAs(f2); // singleton
        await Assert.That(f1.Create(typeof(Strings))["hello"].Value).IsEqualTo("Hello");
    }

    [Test]
    public async Task AddDilLocalization_options_toggle_live_reload()
    {
        try
        {
            new ServiceCollection().AddDilLocalization(o => o.LiveReload = true);
            await Assert.That(Loc.LiveReload).IsTrue();

            new ServiceCollection().AddDilLocalization(o => o.LiveReload = false);
            await Assert.That(Loc.LiveReload).IsFalse();
        }
        finally
        {
            Loc.LiveReload = false; // restore deterministic default for sibling tests
        }
    }

    [Test]
    public async Task AddDilLocalization_options_apply_base_directory()
    {
        var dir = LocFixture.Setup("Strings", ("Strings.json", "", """{ "hello": "Hello" }"""));

        // Exercises the options.BaseDirectory != null branch (-> Loc.Configure(dir)).
        new ServiceCollection().AddDilLocalization(o =>
        {
            o.BaseDirectory = dir;
            o.LiveReload = false;
        });

        await Assert.That(new DilStringLocalizer("Strings")["hello"].Value).IsEqualTo("Hello");
    }

    [Test]
    public async Task Guards_reject_null_arguments()
    {
        var factory = new DilStringLocalizerFactory();
        var loc = new DilStringLocalizer("Strings");
        var services = new ServiceCollection();

        await Assert.That(() => { _ = new DilStringLocalizer(null!); }).Throws<ArgumentNullException>();
        await Assert.That(() => { _ = loc[null!]; }).Throws<ArgumentNullException>();
        await Assert.That(() => { _ = loc[null!, 1]; }).Throws<ArgumentNullException>();
        await Assert.That(() => { _ = factory.Create(null!); }).Throws<ArgumentNullException>();
        await Assert.That(() => { _ = factory.Create(null!, "loc"); }).Throws<ArgumentNullException>();
        await Assert.That(() => { _ = ((IServiceCollection)null!).AddDilLocalization(); }).Throws<ArgumentNullException>();
        await Assert.That(() => { _ = services.AddDilLocalization(null!); }).Throws<ArgumentNullException>();
    }
}
