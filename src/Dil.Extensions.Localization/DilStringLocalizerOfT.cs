using System.Runtime.CompilerServices;
using Microsoft.Extensions.Localization;

namespace Dil.Extensions.Localization;

/// <summary>
/// A strongly-typed <see cref="IStringLocalizer{TResources}"/> whose resource set is
/// <c>typeof(TResources).Name</c> — i.e. the name of a Dil-generated resource class such as
/// <c>Strings</c> or <c>Errors</c>. Has a public parameterless constructor so the DI container can
/// activate it through the open-generic registration added by
/// <see cref="DilLocalizationServiceCollectionExtensions.AddDilLocalization(Microsoft.Extensions.DependencyInjection.IServiceCollection)"/>.
/// Constructing it runs <typeparamref name="TResources"/>'s static initializer, so the generated class
/// registers its files with <see cref="Loc"/> even if no member of it has been touched yet.
/// </summary>
/// <typeparam name="TResources">A Dil-generated resource class; its simple name is the set key.</typeparam>
public class DilStringLocalizer<TResources> : DilStringLocalizer, IStringLocalizer<TResources>
{
    /// <summary>Create a localizer bound to the set named after <typeparamref name="TResources"/>.</summary>
    public DilStringLocalizer()
        : base(typeof(TResources).Name) =>
        RuntimeHelpers.RunClassConstructor(typeof(TResources).TypeHandle);
}
