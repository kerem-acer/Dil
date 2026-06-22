using System.Globalization;
using Dil.Extensions.Localization;
using Dil.Localization.Sample;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

Console.OutputEncoding = System.Text.Encoding.UTF8;

// Wire Dil into DI as IStringLocalizer. Injecting IStringLocalizer<Messages> "just works":
// constructing it runs the generated Messages class's static initializer, which registers the files.
var services = new ServiceCollection()
    .AddDilLocalization()
    .AddTransient<MessageService>()
    .BuildServiceProvider();

var messages = services.GetRequiredService<MessageService>();
var orderDate = new DateTime(2021, 8, 3);

// Region-specific cultures so currency resolves to ₺ / € (resource lookup still falls back tr-TR -> tr).
foreach (var culture in new[] { "", "tr-TR", "de-DE" })
{
    CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture =
        culture.Length == 0 ? CultureInfo.InvariantCulture : new CultureInfo(culture);

    Console.WriteLine($"=== {(culture.Length == 0 ? "(invariant)" : culture)} ===");
    Console.WriteLine("  " + messages.Greeting());
    Console.WriteLine("  " + messages.OrderSummary(orderDate, 37.63m)); // string.Format positional {0:D}/{1:C}
}

// IStringLocalizerFactory + a missing key (falls back to the key, ResourceNotFound = true).
CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
var factory = services.GetRequiredService<IStringLocalizerFactory>();
var missing = factory.Create(typeof(Messages))["nope"];
Console.WriteLine();
Console.WriteLine($"factory + missing key -> ResourceNotFound={missing.ResourceNotFound}, value=\"{missing.Value}\"");
