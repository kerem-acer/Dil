using System.Globalization;

namespace Dil.Sample;

/// <summary>
/// A small custom type, used to show that user-defined classes work as Dil placeholder parameters —
/// both via a bare <c>{x}</c> (generic) and a typed <c>{x:Money}</c>. Implementing
/// <see cref="IFormattable"/> lets Dil render it in the ambient culture.
/// </summary>
public readonly record struct Money(decimal Amount, string Currency) : IFormattable
{
    public string ToString(string? format, IFormatProvider? formatProvider) =>
        Amount.ToString("N2", formatProvider) + " " + Currency;

    public override string ToString() => ToString(null, CultureInfo.CurrentCulture);
}
