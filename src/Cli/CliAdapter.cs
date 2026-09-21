using System.Text.Json;
using Clio10.Contracts;
using Clio10.Composition;
using Clio10.AdapterShared;
using Microsoft.Extensions.DependencyInjection;
namespace Clio10.Cli;

/// <summary>Reusable CLI surface independent of product lifetime.</summary>
public interface ICliAdapter {
    /// <summary>Parses generic operation input and presents results.</summary>
    Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default);
}

/// <summary>Optional application-owned composition registrations.</summary>
public sealed record CliCompositionSetup(Action<IServiceCollection>? Configure);

/// <summary>Registers the adapter; hosts may add partner workflows without changing adapter code.</summary>
public static class CliRegistration {
    /// <summary>Adds CLI and an optional composition customization callback.</summary>
    public static IServiceCollection AddClioCli(this IServiceCollection services, Action<IServiceCollection>? configureComposition = null) {
        services.AddSingleton(new CliCompositionSetup(configureComposition));
        services.AddTransient<ICliInputParser, CliInputParser>();
        services.AddTransient<ICliAdapter, CliAdapter>();
        return services;
    }
}

/// <summary>Generic operation discovery and execution. Command identities belong to descriptors.</summary>
public sealed class CliAdapter(CliCompositionSetup setup, ICliInputParser parser) : ICliAdapter {
    /// <inheritdoc />
    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default) {
        try { return await RunCoreAsync(args, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            Console.Error.WriteLine("Cancelled; dispatched server work may still complete.");
            return 130;
        }
        catch (CoreResolutionException error) {
            Console.WriteLine(ResultOutput.Render(new(false, error.Code)).Json);
            return 1;
        }
        catch (WorkflowContractException error) {
            Console.WriteLine(ResultOutput.Render(new(false, error.Code)).Json);
            return 1;
        }
        catch (Exception) {
            Console.WriteLine(ResultOutput.Render(ResultOutput.UnknownOutcome()).Json);
            return 1;
        }
    }
    private async Task<int> RunCoreAsync(string[] args, CancellationToken cancellationToken) {
        if (args is [] or ["--help"]) {
            Console.WriteLine("clio --list");
            Console.WriteLine("clio <operation> -e <environment> --settings <clio10-settings.json> [--argument value]");
            Console.WriteLine("clio <operation> --uri <https://target/> --login <user> [--platform framework] [--argument value]");
            Console.WriteLine("clio <operation> --help");
            Console.WriteLine("clio --execute <operation> <https://target/> <user> <netcore|framework> [arguments-json]");
            Console.WriteLine("clio --execute <operation> --local [arguments-json]");
            Console.WriteLine("HTTP workflows use CLIO10_PASSWORD; --list includes operation input schemas.");
            return 0;
        }
        CliInput input;
        try { input = parser.Parse(args); }
        catch (Exception error) when (error is ArgumentException or JsonException) {
            Console.Error.WriteLine(args.FirstOrDefault() == "--execute" ? "Invalid arguments JSON or execution options. Use clio --help." : "Invalid CLI arguments. Use clio --help.");
            return 2;
        }
        var versionText = Environment.GetEnvironmentVariable("CLIO10_PRIMITIVE_VERSION");
        if (versionText is not null && !Version.TryParse(versionText, out _)) {
            Console.Error.WriteLine("CLIO10_PRIMITIVE_VERSION must be a numeric assembly version.");
            return 2;
        }
        var services = new ServiceCollection();
        setup.Configure?.Invoke(services);
        try { services.AddClioComposition(new CompositionOptions(input.Target, input.User, Environment.GetEnvironmentVariable("CLIO10_PASSWORD") ?? "",
            input.NetCore, Environment.GetEnvironmentVariable("CLIO10_BUNDLES"),
            versionText is null ? null : Version.Parse(versionText), Environment.GetEnvironmentVariable("CLIO10_ALLOW_UNTRUSTED_CERTIFICATE") == "true", SettingsPath: input.SettingsPath, RuntimeComposition: Environment.GetEnvironmentVariable("CLIO10_RUNTIME_COMPOSITION") == "true")); }
        catch (ArgumentException) { Console.Error.WriteLine("Invalid composition configuration."); return 2; }
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        ResultOutput.Rendered output;
        try {
            var scope = provider.CreateAsyncScope();
            try {
                var composition = scope.ServiceProvider.GetRequiredService<IClioComposition>();
                if (input.List) output = new(JsonSerializer.Serialize(composition.Operations, JsonArguments.Output), false);
                else {
                    var descriptor = composition.Operations.SingleOrDefault(x => x.Name == input.Operation);
                    if (descriptor is null) output = ResultOutput.Render(new(false, "unsupported-operation"));
                    else if (input.Help) output = new(JsonSerializer.Serialize(descriptor, JsonArguments.Output), false);
                    else {
                        IReadOnlyDictionary<string, object?> arguments;
                        try { arguments = args[0] == "--execute" ? input.Arguments : parser.Bind(input, descriptor); }
                        catch (Exception error) when (error is ArgumentException or JsonException) {
                            Console.Error.WriteLine("Invalid operation arguments. Use clio <operation> --help.");
                            return 2;
                        }
                        output = ResultOutput.Render(await composition.ExecuteAsync(new(input.Operation!, input.EnvironmentName, arguments), cancellationToken: cancellationToken));
                    }
                }
            }
            finally { await ResultOutput.DisposeAsync(scope); }
        }
        finally { await ResultOutput.DisposeAsync(provider); }
        Console.WriteLine(output.Json);
        return output.IsError ? 1 : 0;
    }
}
