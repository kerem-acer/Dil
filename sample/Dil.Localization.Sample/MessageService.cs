using Microsoft.Extensions.Localization;

namespace Dil.Localization.Sample;

/// <summary>
/// A demo service that consumes localized strings through <see cref="IStringLocalizer{Messages}"/>
/// (Dil's generated <c>Messages</c> class is the resource set) — exactly how a controller or service
/// would use the Microsoft.Extensions.Localization abstractions.
/// </summary>
public sealed class MessageService(IStringLocalizer<Messages> localizer)
{
    /// <summary>The localized greeting (simple key lookup).</summary>
    public string Greeting() => localizer["greeting"];

    /// <summary>A localized, positionally formatted order summary (<c>{0:D}</c> date, <c>{1:C}</c> currency).</summary>
    public string OrderSummary(DateTime date, decimal price) => localizer["orderSummary", date, price];
}
