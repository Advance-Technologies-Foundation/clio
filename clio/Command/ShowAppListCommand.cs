using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Clio.Common;
using Clio.UserEnvironment;
using CommandLine;
using ConsoleTables;
using Newtonsoft.Json;

namespace Clio.Command;

/// <summary>
/// Structured environment payload used by list-environments JSON projections.
/// </summary>
public sealed record ShowWebAppSettingsResult(
	[property: JsonProperty("name")] string Name,
	[property: JsonProperty("isActive")] bool IsActive,
	[property: JsonProperty("uri")] string Uri,
	[property: JsonProperty("dbName")] string DbName,
	[property: JsonProperty("backupFilePath")] string BackupFilePath,
	[property: JsonProperty("login")] string Login,
	[property: JsonProperty("password")] string Password,
	[property: JsonProperty("maintainer")] string Maintainer,
	[property: JsonProperty("isNetCore")] bool IsNetCore,
	[property: JsonProperty("clientId")] string ClientId,
	[property: JsonProperty("clientSecret")] string ClientSecret,
	[property: JsonProperty("authAppUri")] string AuthAppUri,
	[property: JsonProperty("simpleLoginUri")] string SimpleLoginUri,
	[property: JsonProperty("safe")] bool? Safe,
	[property: JsonProperty("developerModeEnabled")] bool? DeveloperModeEnabled,
	[property: JsonProperty("isDevMode")] bool IsDevMode,
	[property: JsonProperty("workspacePathes")] string WorkspacePathes,
	[property: JsonProperty("environmentPath")] string EnvironmentPath,
	[property: JsonProperty("dbServerKey")] string DbServerKey,
	[property: JsonProperty("dbServer")] ShowWebAppDbServerResult DbServer);

/// <summary>
/// Structured database server payload used by list-environments JSON projections.
/// </summary>
public sealed record ShowWebAppDbServerResult(
	[property: JsonProperty("uri")] string Uri,
	[property: JsonProperty("workingFolder")] string WorkingFolder,
	[property: JsonProperty("login")] string Login,
	[property: JsonProperty("password")] string Password);

[Verb("list-environments", Aliases = ["show-web-app-list", "env", "envs", "show-web-app"],
	HelpText = "Show the list of web applications and their settings")]
public class AppListOptions{
	#region Properties: Public

	[Value(0, MetaName = "App name", Required = false, HelpText = "Name of application")]
	public string Name { get; set; }

	[Option('e', "env", Required = false, HelpText = "Environment name (alias for positional)")]
	public string Env { get; set; }

	[Option('s', "short", Required = false, HelpText = "Show short list")]
	public bool ShowShort { get; set; }

	[Option('f', "format", Default = "json", HelpText = "Output format: json, table, raw. Default: json")]
	public string Format { get; set; }

	[Option("raw", Required = false, HelpText = "Raw output (no formatting) - shorthand for --format raw")]
	public bool Raw { get; set; }

	#endregion
}

public class ShowAppListCommand(ISettingsRepository settingsRepository, ILogger logger) : Command<AppListOptions>{

	#region Methods: Private

	/// <summary>
	///     Get all environments using a reflection pattern (similar to StartCommand/StopCommand)
	/// </summary>
	private Dictionary<string, EnvironmentSettings> GetAllEnvironments() {
		try {
			// Try to access through ISettingsRepository if a method exists
			MethodInfo method = settingsRepository.GetType().GetMethod("GetAllEnvironments",
				BindingFlags.Public | BindingFlags.Instance);
			if (method != null) {
				return (Dictionary<string, EnvironmentSettings>)method.Invoke(settingsRepository, null);
			}

			// Fallback: Try to find environments through reflection
			// This matches the pattern used in StartCommand.cs and StopCommand.cs
			FieldInfo settings = settingsRepository.GetType().GetField("_settings",
				BindingFlags.NonPublic | BindingFlags.Instance);
			if (settings != null) {
				object settingsValue = settings.GetValue(settingsRepository);
				PropertyInfo? environmentsProperty = settingsValue?.GetType().GetProperty("Environments");
				if (environmentsProperty != null) {
					return (Dictionary<string, EnvironmentSettings>)environmentsProperty.GetValue(settingsValue);
				}
			}

			return null;
		}
		catch {
			return null;
		}
	}

	/// <summary>
	///     Masks sensitive data in output
	/// </summary>
	private string MaskSensitiveData(string fieldName, string value) {
		if (string.IsNullOrEmpty(value)) {
			return value;
		}

		// Mask sensitive fields
		if (fieldName.Equals("Password", StringComparison.OrdinalIgnoreCase) ||
			fieldName.Equals("ClientSecret", StringComparison.OrdinalIgnoreCase)) {
			return "****";
		}

		return value;
	}

	private string MaybeMaskSensitiveData(string fieldName, string value, bool maskSensitiveData) {
		return maskSensitiveData ? MaskSensitiveData(fieldName, value) : value;
	}

	private ShowWebAppSettingsResult BuildEnvironmentResult(
		EnvironmentSettings environment,
		string environmentName,
		bool maskSensitiveData,
		bool isActive = false) {
		ShowWebAppDbServerResult dbServer = environment.DbServer == null
			? null
			: new ShowWebAppDbServerResult(
				environment.DbServer.Uri?.ToString(),
				environment.DbServer.WorkingFolder,
				environment.DbServer.Login,
				MaybeMaskSensitiveData("Password", environment.DbServer.Password, maskSensitiveData));

		return new ShowWebAppSettingsResult(
			environmentName,
			isActive,
			environment.Uri,
			environment.DbName,
			environment.BackupFilePath,
			environment.Login,
			MaybeMaskSensitiveData("Password", environment.Password, maskSensitiveData),
			environment.Maintainer,
			environment.IsNetCore,
			environment.ClientId,
			MaybeMaskSensitiveData("ClientSecret", environment.ClientSecret, maskSensitiveData),
			environment.AuthAppUri,
			environment.SimpleloginUri,
			environment.Safe,
			environment.DeveloperModeEnabled,
			environment.IsDevMode,
			environment.WorkspacePathes,
			environment.EnvironmentPath,
			environment.DbServerKey,
			dbServer);
	}

	/// <summary>
	///     Output environment settings in JSON format with masked sensitive data and all known fields
	/// </summary>
	private void OutputAsJson(EnvironmentSettings environment, string environmentName = null) {
		JsonSerializer serializer = new() {
			Formatting = Formatting.Indented,
			NullValueHandling = NullValueHandling.Ignore
		};

		serializer.Serialize(Console.Out, BuildEnvironmentResult(environment, environmentName, maskSensitiveData: true));
		logger.WriteLine(); // Add a newline after JSON
	}

	/// <summary>
	///     Output environment settings in raw text format
	/// </summary>
	private void OutputAsRaw(EnvironmentSettings environment, string environmentName = null) {
		// Simple text output without formatting, including all fields from EnvironmentSettings
		if (!string.IsNullOrEmpty(environmentName)) {
			logger.WriteLine($"Name: {environmentName}");
		}

		logger.WriteLine($"Uri: {environment.Uri}");
		logger.WriteLine($"DbName: {environment.DbName}");
		logger.WriteLine($"BackupFilePath: {environment.BackupFilePath}");
		logger.WriteLine($"Login: {environment.Login}");
		logger.WriteLine($"Password: {MaskSensitiveData("Password", environment.Password)}");
		logger.WriteLine($"Maintainer: {environment.Maintainer}");
		logger.WriteLine($"IsNetCore: {environment.IsNetCore}");
		logger.WriteLine($"ClientId: {environment.ClientId}");
		logger.WriteLine($"ClientSecret: {MaskSensitiveData("ClientSecret", environment.ClientSecret)}");
		logger.WriteLine($"AuthAppUri: {environment.AuthAppUri}");
		logger.WriteLine($"SimpleLoginUri: {environment.SimpleloginUri}");
		logger.WriteLine($"Safe: {environment.Safe}");
		logger.WriteLine($"DeveloperModeEnabled: {environment.DeveloperModeEnabled}");
		logger.WriteLine($"IsDevMode: {environment.IsDevMode}");
		logger.WriteLine($"WorkspacePathes: {environment.WorkspacePathes}");
		logger.WriteLine($"EnvironmentPath: {environment.EnvironmentPath}");
		logger.WriteLine($"DbServerKey: {environment.DbServerKey}");
		if (environment.DbServer != null) {
			logger.WriteLine("DbServer:");
			logger.WriteLine($"  Uri: {environment.DbServer.Uri}");
			logger.WriteLine($"  WorkingFolder: {environment.DbServer.WorkingFolder}");
			logger.WriteLine($"  Login: {environment.DbServer.Login}");
			logger.WriteLine($"  Password: {MaskSensitiveData("Password", environment.DbServer.Password)}");
		}
	}

	/// <summary>
	///     Output all environments in table format
	/// </summary>
	private void OutputAsTable(Dictionary<string, EnvironmentSettings> environments) {
		logger.WriteLine($"\"appsetting file path: {settingsRepository.AppSettingsFilePath}\"");
		logger.WriteLine();

		ConsoleTable table = new();
		table.AddColumn(new[] { "Name", "Url", "Login", "IsNetCore" });

		foreach (KeyValuePair<string, EnvironmentSettings> env in environments) {
			table.AddRow(
				env.Key,
				env.Value.Uri ?? "",
				env.Value.Login ?? "",
				env.Value.IsNetCore ? "Yes" : "No"
			);
		}

		ConsoleLogger.Instance.PrintTable(table);
	}

	#endregion

	#region Methods: Public

	/// <summary>
	/// Returns all registered web application settings using the structured JSON projection shape.
	/// </summary>
	/// <param name="maskSensitiveData">Whether password and client secret fields should be masked.</param>
	/// <returns>The registered environments ordered by name.</returns>
	public IReadOnlyList<ShowWebAppSettingsResult> GetAllWebAppSettings(bool maskSensitiveData) {
		string activeEnvironmentName = settingsRepository.GetDefaultEnvironmentName();
		return settingsRepository.GetAllEnvironments()
			.OrderBy(environment => environment.Key, StringComparer.OrdinalIgnoreCase)
			.Select(environment => BuildEnvironmentResult(
				environment.Value,
				environment.Key,
				maskSensitiveData,
				string.Equals(environment.Key, activeEnvironmentName, StringComparison.OrdinalIgnoreCase)))
			.ToList();
	}

	public override int Execute(AppListOptions options) {
		try {
			Console.OutputEncoding = Encoding.UTF8;

			// Determine format (raw flag takes precedence)
			string format = options.Raw ? "raw" : options.Format ?? "json";
			string environmentName = string.IsNullOrEmpty(options.Name) ? options.Env : options.Name;

			// Handle short format (backward compatibility with -s flag)
			if (options.ShowShort) {
				settingsRepository.ShowSettingsTo(Console.Out, environmentName, true);
				return 0;
			}

			// Handle format options for a specific environment or all environments
			if (!string.IsNullOrEmpty(environmentName)) {
				// Single environment query
				EnvironmentSettings environment = settingsRepository.FindEnvironment(environmentName);
				if (environment == null) {
					logger.WriteError($"Environment '{environmentName}' not found");
					return 1;
				}

				// Get the actual environment name with correct casing
				string actualEnvironmentName = settingsRepository.GetActualEnvironmentName(environmentName) ?? environmentName;

				switch (format.ToLower()) {
					case "json":
						OutputAsJson(environment, actualEnvironmentName);
						break;
					case "table":
						// For a single environment, output as raw in table-like format
						logger.WriteLine($"Environment: {actualEnvironmentName}");
						logger.WriteLine(new string('-', 50));
						OutputAsRaw(environment);
						break;
					case "raw":
						OutputAsRaw(environment, actualEnvironmentName);
						break;
					default:
						logger.WriteWarning($"Unknown format: {format}. Use: json, table, or raw");
						return 1;
				}
			}
			else {
				// All environments - need to get them all
				// Use a reflection pattern if ShowSettingsTo is not enough
				Dictionary<string, EnvironmentSettings> allEnvs = GetAllEnvironments();

				if (allEnvs == null || allEnvs.Count == 0) {
					logger.WriteError("No environments registered. Run 'clio reg-web-app' to add one.");
					return 1;
				}

				switch (format.ToLower()) {
					case "json":
						// Use default behavior for all environments
						settingsRepository.ShowSettingsTo(Console.Out, null);
						break;
					case "table":
						OutputAsTable(allEnvs);
						break;
					case "raw":
						// Output all environments in raw format
						foreach (KeyValuePair<string, EnvironmentSettings> env in allEnvs) {
							logger.WriteLine($"\n=== {env.Key} ===");
							OutputAsRaw(env.Value);
						}

						break;
					default:
						logger.WriteWarning($"Unknown format: {format}. Use: json, table, or raw");
						return 1;
				}
			}

			return 0;
		}
		catch (Exception e) {
			logger.WriteError(e.Message);
			return 1;
		}
	}

	#endregion
}
