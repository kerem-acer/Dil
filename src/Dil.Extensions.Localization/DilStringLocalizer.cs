using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Localization;

namespace Dil.Extensions.Localization;

/// <summary>
/// An <see cref="IStringLocalizer"/> backed by a single Dil resource set. Lookups delegate to
/// <see cref="Loc.Get"/> (so culture fallback matches the rest of Dil); a value equal to the requested
/// name is reported as <see cref="LocalizedString.ResourceNotFound"/>. The arguments overload formats
/// with <see cref="string.Format(IFormatProvider, string, object[])"/> in the current culture — i.e. the
/// standard positional <c>{0}</c>/<c>{1:C}</c> contract of <see cref="IStringLocalizer"/> — so author
/// resource values resx-style for this API; Dil's named <c>{placeholder}</c> tokens are for the generated
/// typed members instead.
/// </summary>
public class DilStringLocalizer : IStringLocalizer
{
    /// <summary>The Dil resource-set key this localizer reads from (the generated class name).</summary>
    protected string Set { get; }

    /// <summary>Create a localizer over the given Dil resource set.</summary>
    /// <param name="set">The resource-set key (the generated class name, e.g. <c>"Strings"</c>).</param>
    /// <exception cref="ArgumentNullException"><paramref name="set"/> is <see langword="null"/>.</exception>
    public DilStringLocalizer(string set) => Set = set ?? throw new ArgumentNullException(nameof(set));

    /// <summary>Look up a string by name for the current UI culture.</summary>
    /// <param name="name">The resource key.</param>
    /// <returns>
    /// The resolved value, with <see cref="LocalizedString.ResourceNotFound"/> set to <see langword="true"/>
    /// when the key is absent (Dil returns the key itself when missing).
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public virtual LocalizedString this[string name]
    {
        get
        {
            if (name is null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            var value = Loc.Get(Set, name);
            return new LocalizedString(name, value, ResourceNotFound(name, value), Set);
        }
    }

    /// <summary>Look up and format a string, substituting positional arguments into <c>{0}</c>…<c>{n}</c> tokens.</summary>
    /// <param name="name">The resource key.</param>
    /// <param name="arguments">Positional values formatted via <see cref="string.Format(IFormatProvider, string, object[])"/>.</param>
    /// <returns>The formatted value; <see cref="LocalizedString.ResourceNotFound"/> reflects whether the key existed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public virtual LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            if (name is null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            // Detect existence on the raw template (Format never returns the key), then format positionally.
            var template = Loc.Get(Set, name);
            if (ResourceNotFound(name, template))
            {
                // Match ResourceManagerStringLocalizer: a not-found result's value is the name itself.
                return new LocalizedString(name, name, true, Set);
            }

            string value;
            try
            {
                value = string.Format(CultureInfo.CurrentCulture, template, arguments ?? []);
            }
            catch (FormatException)
            {
                // Template uses Dil's named {placeholder} tokens (not positional); return it unformatted
                // rather than throwing during e.g. a view render.
                value = template;
            }

            return new LocalizedString(name, value, false, Set);
        }
    }

    /// <summary>Enumerate the strings in this set for the current UI culture.</summary>
    /// <param name="includeParentCultures">
    /// When <see langword="true"/>, the merged resolved view (neutral overlaid by the culture chain);
    /// when <see langword="false"/>, only the current UI culture's own entries.
    /// </param>
    /// <returns>One <see cref="LocalizedString"/> per key (never reported as not found).</returns>
    public virtual IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        foreach (var pair in Loc.GetAllStrings(Set, includeParentCultures))
        {
            yield return new LocalizedString(pair.Key, pair.Value, false, Set);
        }
    }

    static bool ResourceNotFound(string name, string value) => string.Equals(value, name, StringComparison.Ordinal);
}
