using System.Globalization;
using Dil.Sample;   // generated Strings + Errors classes (one per JSON file group, in the RootNamespace)

Console.OutputEncoding = System.Text.Encoding.UTF8;

// Two independent resource sets, each generated from its own file group:
//   Resources/Strings*.json -> class Strings
//   Resources/Errors*.json  -> class Errors
// They don't share keys, so the same key name could mean different things in each.
(string Culture, string Label)[] cultures =
[
    ("",      "neutral (en)"),
    ("tr",    "Turkish"),
    ("fr",    "French"),
    ("de",    "German  (partial — falls back)"),
    ("tr-TR", "tr-TR (region → language)"),
];

foreach (var (culture, label) in cultures)
{
    CultureInfo.CurrentUICulture = culture.Length == 0
        ? CultureInfo.InvariantCulture
        : new CultureInfo(culture);

    Console.WriteLine($"=== {label} ===");
    Console.WriteLine(Strings.AppName);                       // Strings set
    Console.WriteLine("  " + Strings.Greeting("Ada"));        // placeholder
    Console.WriteLine("  " + Strings.Inbox(3));               // de lacks this -> neutral fallback
    Console.WriteLine("  " + Strings.Balance(new Money(1234.5m, "₺"))); // custom type, {amount:Money}
    Console.WriteLine("  " + Errors.NotFound("config.json")); // Errors set (only en + tr exist)
    Console.WriteLine("  " + Errors.Denied);
    Console.WriteLine();
}

// Live reload (on by default): edits to the JSON files are picked up at runtime, no restart.
// Here we rewrite the copy in the output folder and watch the value change.
CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
var stringsPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Strings.json");
var original = File.ReadAllText(stringsPath);
try
{
    Console.WriteLine("Live reload:");
    Console.WriteLine("  before: " + Strings.Greeting("Ada"));

    File.WriteAllText(stringsPath, original.Replace("Hello, {name}!", "Hey there, {name}!"));

    // FileSystemWatcher fires asynchronously; poll briefly until the edit is visible.
    for (var i = 0; i < 50 && !Strings.Greeting("Ada").StartsWith("Hey", StringComparison.Ordinal); i++)
    {
        Thread.Sleep(100);
    }

    Console.WriteLine("  after:  " + Strings.Greeting("Ada"));
}
finally
{
    File.WriteAllText(stringsPath, original); // restore so re-runs start clean
}
