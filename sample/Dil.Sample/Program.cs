using System.Globalization;
using Dil.Sample; // generated Resources class lives in the project's RootNamespace

void Show(string culture)
{
    CultureInfo.CurrentUICulture = culture == "invariant"
        ? CultureInfo.InvariantCulture
        : new CultureInfo(culture);

    // Resources.Hello and Resources.SayHelloTo are generated from L/l.json at build time.
    Console.WriteLine($"[{culture,-9}] {Resources.Hello,-8} | {Resources.SayHelloTo("Kerem"),-16} | {Resources.Items(3)}");
}

Show("invariant"); // default (l.json)
Show("tr");        // full translation
Show("de");        // 'items' missing -> falls back to default
