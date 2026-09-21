using Clio10.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Clio10.Core;

/// <summary>Resolves a fresh immutable environment snapshot for each root workflow.</summary>
public interface IEnvironmentResolver {
    /// <summary>Returns a configured target without exposing storage details to workflows.</summary>
    Task<ClioEnvironment> ResolveAsync(string name, CancellationToken cancellationToken);
}

/// <summary>Reads an explicitly selected settings file, or uses the supplied startup defaults.</summary>
public sealed class EnvironmentResolver(CoreOptions options) : IEnvironmentResolver {
    /// <inheritdoc />
    public async Task<ClioEnvironment> ResolveAsync(string name, CancellationToken cancellationToken) {
        IReadOnlyDictionary<string, ClioEnvironment> environments = options.Environments;
        if (options.SettingsPath is not null) {
            try {
                using var stream = File.OpenRead(options.SettingsPath);
                environments = await JsonSerializer.DeserializeAsync<Dictionary<string, ClioEnvironment>>(stream,
                    cancellationToken: cancellationToken) ?? throw new JsonException();
            }
            catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) {
                throw new CoreResolutionException("invalid-settings", "The selected settings file could not be read.");
            }
        }
        if (!environments.TryGetValue(name, out var environment))
            throw new CoreResolutionException("unknown-environment", "The requested environment is not registered.");
        if (environment is null || (environment.BaseUri is not null && (!environment.BaseUri.IsAbsoluteUri || environment.BaseUri.Scheme is not ("http" or "https") ||
            !environment.BaseUri.AbsolutePath.EndsWith('/') || environment.BaseUri.Query.Length != 0 ||
            environment.BaseUri.Fragment.Length != 0 || environment.BaseUri.UserInfo.Length != 0)))
            throw new CoreResolutionException("invalid-environment", "The environment requires an HTTP(S) application URL ending in '/'.");
        return environment;
    }
}

/// <summary>Opens a managed workflow context; coordination is local to this Core instance.</summary>
public interface IClioCore : ICompositionHost {
    /// <summary>Creates a borrowed host view pinned to the exact loaded release, retaining shared gates and settings.</summary>
    ICompositionHost Bind(IPrimitiveBundle bundle);
    /// <summary>Safe cleanup diagnostics.</summary>
    IReadOnlyList<string> Diagnostics { get; }
}

/// <summary>Coordinates same-target workflows and creates isolated primitive sessions.</summary>
public sealed class ClioCore(CoreOptions options, IPrimitiveCatalog catalog, IEnvironmentResolver environments, ILogger<ClioCore> logger) : IClioCore {
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);
    private readonly AsyncLocal<bool> _executing = new();
    private readonly ConcurrentQueue<string> _diagnostics = new();
    /// <inheritdoc />
    public IReadOnlyList<string> Diagnostics => _diagnostics.ToArray();
    /// <inheritdoc />
    public async Task<T> RunAsync<T>(string environmentName, PrimitiveRequirement requirement,
        Func<IOperationContext, CancellationToken, Task<T>> execute, CancellationToken cancellationToken) {
        return await RunPinnedAsync(environmentName, requirement, null, execute, cancellationToken);
    }
    /// <inheritdoc />
    public ICompositionHost Bind(IPrimitiveBundle bundle) => new BoundHost(this, bundle);
    private sealed class BoundHost(ClioCore owner, IPrimitiveBundle bundle) : ICompositionHost {
        public Task<T> RunAsync<T>(string environmentName, PrimitiveRequirement requirement,
            Func<IOperationContext, CancellationToken, Task<T>> execute, CancellationToken cancellationToken) =>
            owner.RunPinnedAsync(environmentName, requirement, bundle, execute, cancellationToken);
    }
    private async Task<T> RunPinnedAsync<T>(string environmentName, PrimitiveRequirement requirement, IPrimitiveBundle? bundle,
        Func<IOperationContext, CancellationToken, Task<T>> execute, CancellationToken cancellationToken) {
        if (_executing.Value) throw new CoreResolutionException("nested-root-not-supported", "Use child invocation inside a single-target root.");
        cancellationToken.ThrowIfCancellationRequested();
        _executing.Value = true;
        try {
            var context = await BeginAsync(environmentName, requirement, bundle, cancellationToken);
            try { return await execute(context, cancellationToken); }
            finally {
                try { await context.DisposeAsync(); }
                catch (Exception error) { RecordCleanup(error); }
            }
        }
        finally { _executing.Value = false; }
    }
    private void RecordCleanup(Exception error) {
        _diagnostics.Enqueue("session-cleanup-failed");
        // Never pass the exception object/message: provider errors may contain secrets.
        try { logger.LogWarning("Session cleanup failed ({ExceptionType}).", error.GetType().FullName); }
        catch (Exception) { /* A diagnostic provider cannot replace the execution outcome. */ }
    }
    private async Task<Context> BeginAsync(string environmentName, PrimitiveRequirement requirement, IPrimitiveBundle? pinned, CancellationToken cancellationToken) {
        var environment = await environments.ResolveAsync(environmentName, cancellationToken);
        var selection = requirement with { Exact = requirement.Exact ?? options.ExactVersion };
        var bundle = pinned ?? catalog.Select(selection);
        if (!selection.Matches(bundle.Version, bundle.Capabilities))
            throw new CoreResolutionException("incompatible-runtime-workflow", "The workflow cannot use its selected runtime release.");
        var gate = _gates.GetOrAdd(environment.BaseUri?.AbsoluteUri ?? "environment:" + environmentName, _ => new SemaphoreSlim(1));
        await gate.WaitAsync(cancellationToken);
        IPrimitiveSession? session = null;
        try {
            session = bundle.OpenSession(new(environment.BaseUri, environment.UserName, environment.Password,
                environment.IsNetCore, environment.AllowUntrustedCertificate));
            if (bundle.Capabilities.Any(name => !CapabilityAccess.Implements(name, session.GetCapability(name)))) {
                throw new CoreResolutionException("invalid-capability-provider", "Bundle does not provide its declared capabilities.");
            }
            return new Context(environment, BundleVersion.Normalize(bundle.Version), bundle.Capabilities, session, gate);
        }
        catch {
            try { if (session is not null) await session.DisposeAsync(); }
            catch (Exception error) { RecordCleanup(error); }
            finally { gate.Release(); }
            throw;
        }
    }
    // Gates have no native wait handles. They are collected with Core; disposing them while
    // an existing context drains would break that context's release path.
    private sealed class Context(ClioEnvironment environment, Version version, IReadOnlyCollection<string> capabilities,
        IPrimitiveSession session, SemaphoreSlim gate) : IOperationContext, IAsyncDisposable {
        private int _disposed;
        public ClioEnvironment Environment => environment;
        public Version PrimitiveVersion => version;
        public IReadOnlyCollection<string> Capabilities => capabilities;
        public object? GetCapability(string name) => session.GetCapability(name);
        public async ValueTask DisposeAsync() {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { await session.DisposeAsync(); } finally { gate.Release(); }
        }
    }
}

/// <summary>Registers reusable Core services without any concrete primitive dependency.</summary>
public static class CoreRegistration {
    /// <summary>Preserves caller overrides and scopes coordination to this provider.</summary>
    public static IServiceCollection AddClioCore(this IServiceCollection services, CoreOptions options) {
        services.AddLogging();
        if (options.Updates is not null) {
            if (options.BundleDirectory is null) throw new ArgumentException("Automatic updates require an explicit bundle directory.", nameof(options));
            services.AddHostedService<BundleUpdateService>();
        }
        services.AddSingleton(options);
        services.TryAddSingleton<IEnvironmentResolver, EnvironmentResolver>();
        services.TryAddSingleton<IPrimitiveCatalog, PrimitiveCatalog>();
        services.TryAddSingleton<IClioCore, ClioCore>();
        services.TryAddSingleton<ICompositionHost>(provider => provider.GetRequiredService<IClioCore>());
        if (options.RuntimeComposition) services.TryAddSingleton<IRuntimeComposition, UpdatingComposition>();
        return services;
    }
}
