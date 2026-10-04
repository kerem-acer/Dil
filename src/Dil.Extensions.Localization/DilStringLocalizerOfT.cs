using System.Runtime.CompilerServices;
using Microsoft.Extensions.Localization;

namespace Dil.Extensions.Localization;

/// <summary>
/// A strongly-typed <see cref="IStringLocalizer{TResources}"/> over the resource set of a Dil-generated
/// class such as <c>Strings</c> or <c>Errors</c>, keyed by its assembly and class name
/// (<c>"MyApp/Strings"</c>) so a same-named set in another assembly never answers. Has a public
/// parameterless constructor so the DI container can activate it through the open-generic registration added by
/// <see cref="DilLocalizationServiceCollectionExtensions.AddDilLocalization(Microsoft.Extensions.DependencyInjection.IServiceCollection)"/>.
/// Constructing it runs <typeparamref name="TResources"/>'s static initializer, so the generated class
/// registers its files with <see cref="Loc"/> even if no member of it has been touched yet.
/// </summary>
/// <typeparam name="TResources">A Dil-generated resource class; its assembly and simple name are the set key.</typeparam>
public class DilStringLocalizer<TResources> : DilStringLocalizer, IStringLocalizer<TResources>
{
    // Computed once per resource class: DI constructs this transient on every resolve.
    static readonly string Key = SetOf(typeof(TResources));

    /// <summary>Create a localizer bound to the set named after <typeparamref name="TResources"/>.</summary>
    public DilStringLocalizer()
        : base(Key) =>
        RuntimeHelpers.RunClassConstructor(typeof(TResources).TypeHandle);
}
