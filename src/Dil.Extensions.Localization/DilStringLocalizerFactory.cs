using System;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Localization;

namespace Dil.Extensions.Localization;

/// <summary>
/// Creates <see cref="DilStringLocalizer"/> instances. A set is keyed by its assembly and class name
/// (<c>"MyApp/Strings"</c>): taken from the requested type, or from the location (an assembly name) and
/// the segment after the last <c>'.'</c> in a string base name — matching how Dil names a set after its
/// JSON file's base name.
/// </summary>
public class DilStringLocalizerFactory : IStringLocalizerFactory
{
    /// <summary>Create a localizer for the resource set named after <paramref name="resourceSource"/>.</summary>
    /// <param name="resourceSource">A Dil-generated class; its assembly and simple name are the set key.</param>
    /// <returns>A localizer over that set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resourceSource"/> is <see langword="null"/>.</exception>
    public IStringLocalizer Create(Type resourceSource)
    {
        if (resourceSource is null)
        {
            throw new ArgumentNullException(nameof(resourceSource));
        }

        // Ensure a generated resource class has registered its files with Loc before first lookup.
        RuntimeHelpers.RunClassConstructor(resourceSource.TypeHandle);
        return new DilStringLocalizer(DilStringLocalizer.SetOf(resourceSource));
    }

    /// <summary>Create a localizer for the resource set identified by a base name.</summary>
    /// <param name="baseName">A dotted base name; the segment after the last <c>'.'</c> names the set.</param>
    /// <param name="location">
    /// The name of the assembly that owns the set, as ASP.NET Core passes it (for example the application
    /// name). Empty means an unqualified set key.
    /// </param>
    /// <returns>A localizer over the resolved set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="baseName"/> is <see langword="null"/>.</exception>
    public IStringLocalizer Create(string baseName, string location)
    {
        if (baseName is null)
        {
            throw new ArgumentNullException(nameof(baseName));
        }

        var dot = baseName.LastIndexOf('.');
        var set = dot >= 0 ? baseName.Substring(dot + 1) : baseName;
        // A trailing dot ("My.App.") would yield an empty (dead) set — fall back to the full base name.
        if (set.Length == 0)
        {
            set = baseName;
        }

        return new DilStringLocalizer(string.IsNullOrEmpty(location) ? set : location + "/" + set);
    }
}
