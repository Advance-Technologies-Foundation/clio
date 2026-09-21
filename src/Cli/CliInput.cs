using System.Globalization;
using System.Text.Json;
using Clio10.AdapterShared;
using Clio10.Contracts;

namespace Clio10.Cli;

/// <summary>Parsed presentation input, independent of workflow implementations.</summary>
public sealed record CliInput(string? Operation, Uri? Target, string User, bool NetCore,
    string EnvironmentName, string? SettingsPath, bool List, bool Help,
    IReadOnlyDictionary<string, object?> Arguments, IReadOnlyDictionary<string, string?> Options);

/// <summary>Parses operation syntax and converts named flags using discovered schemas.</summary>
public interface ICliInputParser {
    /// <summary>Parses common connection options and retains operation flags for discovery-based validation.</summary>
    CliInput Parse(string[] args);
    /// <summary>Converts flags into portable values without introducing domain validation in CLI.</summary>
    IReadOnlyDictionary<string, object?> Bind(CliInput input, OperationDescriptor descriptor);
}

/// <summary>Supports the prototype syntax and conventional named command options.</summary>
public sealed class CliInputParser : ICliInputParser {
    private static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.Ordinal) {
        ["add-pkg-dependency"] = "add-package-dependency", ["add-pkg-dep"] = "add-package-dependency",
        ["remove-pkg-dependency"] = "remove-package-dependency", ["remove-pkg-dep"] = "remove-package-dependency",
        ["apkg"] = "activate-pkg", ["activate-package"] = "activate-pkg", ["enable-package"] = "activate-pkg",
        ["dpkg"] = "deactivate-pkg", ["deactivate-package"] = "deactivate-pkg", ["disable-package"] = "deactivate-pkg",
        ["compress"] = "generate-pkg-zip", ["extract"] = "extract-pkg-zip", ["unzip"] = "extract-pkg-zip",
        ["restart-web-app"] = "restart", ["clear-redis-db"] = "flush-redis", ["flushdb"] = "flush-redis",
        ["ping"] = "ping-app", ["cs"] = "call-service", ["ds"] = "dataservice",
        ["build"] = "build-workspace", ["compile"] = "build-workspace", ["compile-all"] = "build-workspace", ["rebuild"] = "build-workspace",
        ["gsc"] = "generate-source-code", ["lcl"] = "last-compilation-log", ["restore"] = "restore-configuration", ["rc"] = "restore-configuration",
        ["profile-language"] = "get-user-culture", ["get-pkg-list"] = "list-packages", ["packages"] = "list-packages", ["gpv"] = "get-pkg-version", ["spv"] = "set-pkg-version",
        ["get-app-list"] = "list-apps", ["lia"] = "list-apps", ["apps"] = "list-apps", ["app-list"] = "list-apps", ["apps-list"] = "list-apps"
    };
    /// <inheritdoc />
    public CliInput Parse(string[] args) {
        var empty = new Dictionary<string, object?>();
        var flags = new Dictionary<string, string?>(StringComparer.Ordinal);
        var settings = Environment.GetEnvironmentVariable("CLIO10_SETTINGS");
        if (args is ["--list"]) return new(null, null, "", true, "default", settings, true, false, empty, flags);
        if (args is ["--execute", ..]) {
            bool local = args.Length is 3 or 4 && args[2] == "--local";
            if (!local && (args.Length is < 5 or > 6 || args[4] is not ("netcore" or "framework"))) throw new ArgumentException("Invalid execution arguments.");
            return new(args[1], local ? null : Target(args[2]), local ? "" : args[3], local || args[4] == "netcore", "default", settings,
                false, false, JsonArguments.Read(local && args.Length == 4 ? args[3] : args.Length == 6 ? args[5] : "{}"), flags);
        }
        if (args.Length == 0 || args[0].StartsWith('-')) throw new ArgumentException("An operation name is required.");
        string operation = Aliases.TryGetValue(args[0], out var alias) ? alias : args[0];
        int positional = 0;
        for (int index = 1; index < args.Length; index++) {
            string token = args[index];
            if (!token.StartsWith('-')) { flags.Add("@" + positional++, token); continue; }
            int equal = token.IndexOf('=');
            string name = equal < 0 ? token : token[..equal];
            name = name switch { "-e" => "--environment", "-u" => "--uri", "-l" => "--login", "-H" => "--help", _ => name };
            name = (operation, name) switch {
                ("generate-pkg-zip" or "extract-pkg-zip", "-d") => "--destination-path",
                ("generate-pkg-zip" or "extract-pkg-zip", "--DestinationPath") => "--destination-path",
                ("generate-pkg-zip", "-s") => "--skip-pdb",
                ("generate-pkg-zip", "--SkipPdb") => "--skip-pdb",
                ("generate-pkg-zip", "-p" or "--Packages") => "--packages",
                ("get-pkg-version" or "set-pkg-version", "-v") => "--package-version",
                ("list-packages", "-f") => "--filter",
                ("build-workspace", "-o") => "--modified-items",
                ("generate-source-code", "-m") => "--modified",
                ("generate-source-code", "-r") => "--required",
                ("generate-source-code", "-b") => "--background",
                ("call-service", "-m") => "--method",
                ("call-service", "-b") => "--body",
                ("call-service", "-f") => "--input",
                ("call-service", "-d") => "--destination",
                ("ping-app", "-x") => "--endpoint",
                ("restore-configuration", "-d") => "--skip-rollback-data",
                ("restore-configuration", "-f") => "--force",
                ("dataservice", "-t") => "--type",
                _ => name
            };
            if (!name.StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Unknown short option.");
            name = name[2..];
            string? value = equal >= 0 ? token[(equal + 1)..] :
                index + 1 < args.Length && (!args[index + 1].StartsWith('-') ||
                    decimal.TryParse(args[index + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out _)) ? args[++index] : null;
            if (!flags.TryAdd(name, value)) throw new ArgumentException("Duplicate option.");
        }
        string? Take(string name) {
            if (!flags.Remove(name, out var value)) return null;
            return value ?? throw new ArgumentException("An option requires a value.");
        }
        var help = flags.Remove("help", out var helpValue);
        if (helpValue is not null) throw new ArgumentException("Help has no value.");
        var uri = Take("uri");
        var user = Take("login") ?? "";
        var environment = Take("environment") ?? "default";
        settings = Take("settings") ?? settings;
        var platform = Take("platform") ?? "netcore";
        if (platform is not ("netcore" or "framework")) throw new ArgumentException("Unknown platform.");
        if (uri is not null && (environment != "default" || settings is not null)) throw new ArgumentException("Choose settings or direct connection.");
        var arguments = JsonArguments.Read(Take("arguments") ?? "{}");
        return new(operation, uri is null ? null : Target(uri), user, platform == "netcore", environment, settings, false, help, arguments, flags);
    }
    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> Bind(CliInput input, OperationDescriptor descriptor) {
        var arguments = new Dictionary<string, object?>(input.Arguments, StringComparer.Ordinal);
        foreach (var (option, value) in input.Options) {
            string name = option;
            if (option.StartsWith('@')) {
                if (option != "@0") throw new ArgumentException("Unexpected positional value.");
                name = input.Operation switch {
                    "get-pkg-version" or "set-pkg-version" or "generate-pkg-zip" => "package-path",
                    "extract-pkg-zip" => "archive-path",
                    "activate-pkg" or "deactivate-pkg" => "package-name",
                    _ => throw new ArgumentException("Unexpected positional value.")
                };
            }
            var field = descriptor.Arguments?.SingleOrDefault(x => x.Name == name) ?? throw new ArgumentException("Unknown operation option.");
            object? converted = field.Kind switch {
                ArgumentKind.String when value is not null => value,
                ArgumentKind.Boolean when value is null => true,
                ArgumentKind.Boolean when bool.TryParse(value, out var boolean) => boolean,
                ArgumentKind.Integer when long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer) => integer,
                ArgumentKind.Number when decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) => number,
                ArgumentKind.Object or ArgumentKind.Array when value is not null => JsonValue(value),
                _ => throw new ArgumentException("Invalid operation option value.")
            };
            if (!arguments.TryAdd(name, converted)) throw new ArgumentException("Argument supplied twice.");
        }
        if (!ArgumentValues.IsValid(arguments, descriptor.Arguments)) throw new ArgumentException("Arguments do not match the discovered schema.");
        return arguments;
    }
    private static object? JsonValue(string value) { using var document = JsonDocument.Parse(value); return JsonArguments.Convert(document.RootElement); }
    private static Uri Target(string value) {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            !uri.AbsolutePath.EndsWith('/') || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("Supply an HTTP(S) application URL ending in '/'.");
        return uri;
    }
}
