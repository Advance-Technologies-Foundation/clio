using Clio10.Composition;
using Clio10.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Clio10.Mcp;

/// <summary>Reusable MCP server surface, independent of the CLI adapter.</summary>
public interface IMcpAdapter {
    /// <summary>Parses server configuration and runs stdio MCP until stopped.</summary>
    Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default);
}

/// <summary>Registers the server adapter without starting it.</summary>
public sealed record McpCompositionSetup(Action<IServiceCollection>? Configure);

/// <summary>Registers the optional MCP surface.</summary>
public static class McpRegistration {
    /// <summary>Adds the optional MCP surface to a consuming application.</summary>
    public static IServiceCollection AddClioMcp(this IServiceCollection services, Action<IServiceCollection>? configureComposition = null) {
        services.AddSingleton(new McpCompositionSetup(configureComposition));
        services.AddTransient<IMcpAdapter, McpAdapter>();
        return services;
    }
}

/// <summary>Owns MCP configuration and protocol lifecycle; stdout contains protocol traffic only.</summary>
public sealed class McpAdapter(McpCompositionSetup setup) : IMcpAdapter {
    /// <inheritdoc />
    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default) {
        if (args is ["--help"]) {
            Console.WriteLine("clio mcp <https://target/> <user> <netcore|framework>");
            Console.WriteLine("clio mcp --local");
            Console.WriteLine("Uses CLIO10_PASSWORD. Starts stdio MCP; tools: list-operations, execute.");
            Console.WriteLine("Optional updates: CLIO10_UPDATE_SOURCE (NuGet V3 index), CLIO10_UPDATE_PACKAGE, CLIO10_BUNDLES; CLIO10_UPDATE_INTERVAL_SECONDS defaults to 3600.");
            return 0;
        }
        string? password = Environment.GetEnvironmentVariable("CLIO10_PASSWORD");
        bool local = args is ["--local"];
        Uri? target = null;
        if (!local && (args.Length != 3 || args[2] is not ("netcore" or "framework") ||
            string.IsNullOrWhiteSpace(args[1]) ||
            !Uri.TryCreate(args[0], UriKind.Absolute, out target) ||
            target.Scheme is not ("http" or "https") || !target.AbsolutePath.EndsWith('/') ||
            target.UserInfo.Length != 0 || target.Query.Length != 0 || target.Fragment.Length != 0)) {
            Console.Error.WriteLine("Invalid MCP configuration. Use clio mcp --help and set CLIO10_PASSWORD.");
            return 2;
        }
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateScopes = true }));
        builder.Logging.ClearProviders();
        var versionText = Environment.GetEnvironmentVariable("CLIO10_PRIMITIVE_VERSION");
        if (versionText is not null && !Version.TryParse(versionText, out _)) {
            Console.Error.WriteLine("CLIO10_PRIMITIVE_VERSION must be a numeric assembly version.");
            return 2;
        }
        setup.Configure?.Invoke(builder.Services);
        BundleUpdateOptions? updates = null;
        var sourceText = Environment.GetEnvironmentVariable("CLIO10_UPDATE_SOURCE");
        if (sourceText is not null) {
            var package = Environment.GetEnvironmentVariable("CLIO10_UPDATE_PACKAGE");
            var intervalText = Environment.GetEnvironmentVariable("CLIO10_UPDATE_INTERVAL_SECONDS") ?? "3600";
            if (!Uri.TryCreate(sourceText, UriKind.Absolute, out var source) || string.IsNullOrWhiteSpace(package) ||
                !double.TryParse(intervalText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds) ||
                !double.IsFinite(seconds) || seconds < 0.1 || seconds > 86400 || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLIO10_BUNDLES"))) {
                Console.Error.WriteLine("Updates require a source, package, bundle directory and interval from 0.1 to 86400 seconds.");
                return 2;
            }
            updates = new(source, package, TimeSpan.FromSeconds(seconds), new(10, 0), new(11, 0));
        }
        try { builder.Services.AddClioComposition(new CompositionOptions(target, local ? "" : args[1], password ?? "", local || args[2] == "netcore",
            Environment.GetEnvironmentVariable("CLIO10_BUNDLES"), versionText is null ? null : Version.Parse(versionText), Environment.GetEnvironmentVariable("CLIO10_ALLOW_UNTRUSTED_CERTIFICATE") == "true", Updates: updates, RuntimeComposition: Environment.GetEnvironmentVariable("CLIO10_RUNTIME_COMPOSITION") == "true")); }
        catch (ArgumentException) { Console.Error.WriteLine("Invalid composition configuration."); return 2; }
        builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<CreatioTools>();
        using var host = builder.Build();
        await host.RunAsync(cancellationToken);
        return 0;
    }
}
