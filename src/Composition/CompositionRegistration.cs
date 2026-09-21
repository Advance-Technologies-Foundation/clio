using Clio10.Contracts;
using Clio10.PrimitiveContracts;
using Clio10.Core;
using Clio10.Primitives;
using Clio10.Composition.Services;
using Clio10.Composition.Packages;
using Clio10.Composition.Files;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Clio10.Composition;

/// <summary>Application settings for the standard composition; no primitive implementation types are exposed.</summary>
public sealed record CompositionOptions(Uri? BaseUri = null, string UserName = "", string Password = "", bool IsNetCore = true,
    string? BundleDirectory = null, Version? PrimitiveVersion = null, bool AllowUntrustedCertificate = false,
    string? SettingsPath = null, bool IncludeDefaultPrimitives = true, BundleUpdateOptions? Updates = null, bool RuntimeComposition = false) {
    /// <summary>Redacts credentials from diagnostic formatting.</summary>
    public override string ToString() => "CompositionOptions [redacted]";
}

/// <summary>Owns default primitive wiring separately from workflow execution.</summary>
public static class CompositionRegistration {
    /// <summary>Registers workflows and HTTP defaults, preserving previously registered overrides.</summary>
    /// <param name="services">The host's service collection.</param>
    /// <param name="options">The target application URL, ending in a slash.</param>
    /// <returns>The service collection. Composition creates one execution scope per root and identity.</returns>
    public static IServiceCollection AddClioComposition(this IServiceCollection services, CompositionOptions options) {
        ArgumentNullException.ThrowIfNull(options);
        return services.AddClioComposition(new CoreOptions(new Dictionary<string, ClioEnvironment> {
            ["default"] = new(options.BaseUri, options.UserName, options.Password, options.IsNetCore, options.AllowUntrustedCertificate)
        }, options.BundleDirectory, options.PrimitiveVersion, options.SettingsPath, options.Updates, options.RuntimeComposition), options.IncludeDefaultPrimitives);
    }
    /// <summary>Configures named environments directly; callers may explicitly omit bundled defaults.</summary>
    public static IServiceCollection AddClioComposition(this IServiceCollection services, CoreOptions options, bool includeDefaultPrimitives = true) {
        services.AddLogging();
        if (!options.RuntimeComposition)
            options = options with { SharedCapabilityAssemblies = options.SharedCapabilityAssemblies
                .Append(typeof(IClioPrimitive).Assembly).Distinct().ToArray() };
        services.AddClioCore(options);
        if (options.RuntimeComposition) {
            if (options.BundleDirectory is null) throw new ArgumentException("A complete runtime cache is required.", nameof(options));
            if (services.Any(x => x.ServiceType == typeof(WorkflowRegistration)))
                throw new ArgumentException("Register partner workflows inside the complete runtime release.", nameof(services));
            services.TryAddSingleton<IClioComposition>(provider => {
                if (provider.GetServices<WorkflowRegistration>().Any()) throw new CoreResolutionException("host-workflows-not-supported", "Register workflows inside the runtime release.");
                return provider.GetRequiredService<IRuntimeComposition>();
            });
            return services;
        }
        if (includeDefaultPrimitives) services.TryAddEnumerable(ServiceDescriptor.Singleton<IPrimitiveBundle, HttpPrimitiveBundle>());
        return services.AddClioWorkflows();
    }
    /// <summary>Registers vendor workflows and the dispatcher using an already supplied ICompositionHost.</summary>
    /// <remarks>Runtime releases use this without installing Core, settings, a catalog or an updater.</remarks>
    public static IServiceCollection AddClioWorkflows(this IServiceCollection services) {
        services.AddLogging();
        services.AddKeyedScoped<IClioWorkflow, RestartWorkflow>(RestartWorkflow.Descriptor.Name);
        services.AddSingleton(new WorkflowRegistration(RestartWorkflow.Descriptor, MaintenanceWorkflow.Requirement));
        services.AddKeyedScoped<IClioWorkflow, FlushRedisWorkflow>(FlushRedisWorkflow.Descriptor.Name);
        services.AddSingleton(new WorkflowRegistration(FlushRedisWorkflow.Descriptor, MaintenanceWorkflow.Requirement));
        services.AddServiceWorkflows();
        services.AddPackageMetadataWorkflows();
        services.AddPackageDependencyWorkflows();
        services.AddPackageActivationWorkflows();
        services.AddPackageArchiveWorkflows();
        services.AddKeyedScoped<IClioWorkflow, VerifyFileWorkflow>(VerifyFileWorkflow.Descriptor.Name);
        services.AddSingleton(new WorkflowRegistration(VerifyFileWorkflow.Descriptor, VerifyFileWorkflow.Requirement));
        services.AddKeyedScoped<IClioWorkflow, CompareDirectoriesWorkflow>(CompareDirectoriesWorkflow.Descriptor.Name);
        services.AddSingleton(new WorkflowRegistration(CompareDirectoriesWorkflow.Descriptor, CompareDirectoriesWorkflow.Requirement));
        services.TryAddSingleton<IClioComposition, CreatioComposition>();
        return services;
    }
}
