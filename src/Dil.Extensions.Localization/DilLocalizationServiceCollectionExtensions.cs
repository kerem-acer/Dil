using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;

namespace Dil.Extensions.Localization;

/// <summary>Options applied to Dil's process-global runtime when registering the localization adapter.</summary>
public sealed class DilLocalizationOptions
{
    /// <summary>
    /// Directory that relative resource-file paths resolve from. When <see langword="null"/> (the default)
    /// Dil uses the application base directory. Maps to <see cref="Loc.Configure"/>.
    /// </summary>
    public string? BaseDirectory { get; set; }

    /// <summary>
    /// Whether Dil re-reads resource files when they change on disk. Maps to <see cref="Loc.LiveReload"/>.
    /// Defaults to <see langword="true"/>; set <see langword="false"/> in production.
    /// </summary>
    public bool LiveReload { get; set; } = true;
}

/// <summary>Dependency-injection registration helpers for the Dil localization adapter.</summary>
public static class DilLocalizationServiceCollectionExtensions
{
    /// <summary>
    /// Register Dil's <see cref="IStringLocalizer"/> implementation: a singleton
    /// <see cref="IStringLocalizerFactory"/> and the open-generic <see cref="IStringLocalizer{T}"/> →
    /// <see cref="DilStringLocalizer{T}"/> mapping. Uses <c>TryAdd</c>, so it is safe to call alongside
    /// other localization registrations. Consumers normally inject <see cref="IStringLocalizer{T}"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddDilLocalization(this IServiceCollection services)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.TryAddSingleton<IStringLocalizerFactory, DilStringLocalizerFactory>();
        services.TryAddTransient(typeof(IStringLocalizer<>), typeof(DilStringLocalizer<>));
        return services;
    }

    /// <summary>
    /// Register Dil's <see cref="IStringLocalizer"/> implementation and apply <see cref="DilLocalizationOptions"/>
    /// to Dil's process-global runtime (analogous to <c>AddLocalization(options =&gt; …)</c>). Note that the
    /// options configure global state via <see cref="Loc.Configure"/> / <see cref="Loc.LiveReload"/>, not a
    /// DI-scoped instance.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the runtime options.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddDilLocalization(this IServiceCollection services, Action<DilLocalizationOptions> configure)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        var options = new DilLocalizationOptions();
        configure(options);

        if (options.BaseDirectory is not null)
        {
            Loc.Configure(options.BaseDirectory);
        }

        Loc.LiveReload = options.LiveReload;

        return services.AddDilLocalization();
    }
}
