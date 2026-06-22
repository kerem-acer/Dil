using System;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Localization;

namespace Dil.Extensions.Localization;

/// <summary>
/// Creates <see cref="DilStringLocalizer"/> instances. The resource-set key is derived from the requested
/// type's <see cref="System.Reflection.MemberInfo.Name"/>, or from the segment after the last <c>'.'</c>
/// in a string base name — matching how Dil names a set after its JSON file's base name.
/// </summary>
public class DilStringLocalizerFactory : IStringLocalizerFactory
{
    /// <summary>Create a localizer for the resource set named after <paramref name="resourceSource"/>.</summary>
    /// <param name="resourceSource">A type whose simple name is the resource-set key.</param>
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
        return new DilStringLocalizer(resourceSource.Name);
    }

    /// <summary>Create a localizer for the resource set identified by a base name.</summary>
    /// <param name="baseName">A dotted base name; the segment after the last <c>'.'</c> is the set key.</param>
    /// <param name="location">Ignored; Dil resolves files via its own generated manifest.</param>
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
        return new DilStringLocalizer(set);
    }
}
