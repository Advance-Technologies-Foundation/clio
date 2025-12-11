using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Clio.UserEnvironment;
using CommandLine;
using ConsoleTables;
using Newtonsoft.Json;

namespace Clio.Command
{
	[Verb("show-web-app-list", Aliases = new string[] { "env", "envs" ,"show-web-app" }, HelpText = "Show the list of web applications and their settings")]
	public class AppListOptions
	{
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
	}

	public class ShowAppListCommand : Command<AppListOptions>
	{
		private readonly ISettingsRepository _settingsRepository;

		public ShowAppListCommand(ISettingsRepository settingsRepository) {
			_settingsRepository = settingsRepository;
		}

		/// <summary>
		/// Masks sensitive data in output
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

		/// <summary>
		/// Output environment settings in JSON format with masked sensitive data and all known fields
		/// </summary>
		private void OutputAsJson(EnvironmentSettings environment, string environmentName = null) {
			// Create a copy with masked sensitive data and include all settings fields
			var sanitized = new {
				name = environmentName,
				uri = environment.Uri,
				dbName = environment.DbName,
				backupFilePath = environment.BackupFilePath,
				login = environment.Login,
				password = MaskSensitiveData("Password", environment.Password),
				maintainer = environment.Maintainer,
				isNetCore = environment.IsNetCore,
				clientId = environment.ClientId,
				clientSecret = MaskSensitiveData("ClientSecret", environment.ClientSecret),
				authAppUri = environment.AuthAppUri,
				simpleLoginUri = environment.SimpleloginUri,
				safe = environment.Safe,
				developerModeEnabled = environment.DeveloperModeEnabled,
				isDevMode = environment.IsDevMode,
				workspacePathes = environment.WorkspacePathes,
				environmentPath = environment.EnvironmentPath,
				dbServerKey = environment.DbServerKey,
				dbServer = environment.DbServer == null ? null : new {
					uri = environment.DbServer.Uri,
					workingFolder = environment.DbServer.WorkingFolder,
					login = environment.DbServer.Login,
					password = MaskSensitiveData("Password", environment.DbServer.Password)
				}
			};
			
			var serializer = new JsonSerializer() {
				Formatting = Formatting.Indented,
				NullValueHandling = NullValueHandling.Ignore
			};
			
			serializer.Serialize(Console.Out, sanitized);
			Console.WriteLine(); // Add newline after JSON
		}

		/// <summary>
		/// Output all environments in table format
		/// </summary>
		private void OutputAsTable(Dictionary<string, EnvironmentSettings> environments) {
			Console.WriteLine($"\"appsetting file path: {_settingsRepository.AppSettingsFilePath}\"");
			Console.WriteLine();
			
			var table = new ConsoleTable();
			table.AddColumn(new[] { "Name", "Url", "Login", "IsNetCore" });
			
			foreach (var env in environments) {
				table.AddRow(
					env.Key,
					env.Value.Uri ?? "",
					env.Value.Login ?? "",
					env.Value.IsNetCore ? "Yes" : "No"
				);
			}
			
			ConsoleLogger.Instance.PrintTable(table);
		}

		/// <summary>
		/// Output environment settings in raw text format
		/// </summary>
		private void OutputAsRaw(EnvironmentSettings environment, string environmentName = null) {
			// Simple text output without formatting, including all fields from EnvironmentSettings
			if (!string.IsNullOrEmpty(environmentName)) {
				Console.WriteLine($"Name: {environmentName}");
			}
			Console.WriteLine($"Uri: {environment.Uri}");
			Console.WriteLine($"DbName: {environment.DbName}");
			Console.WriteLine($"BackupFilePath: {environment.BackupFilePath}");
			Console.WriteLine($"Login: {environment.Login}");
			Console.WriteLine($"Password: {MaskSensitiveData("Password", environment.Password)}");
			Console.WriteLine($"Maintainer: {environment.Maintainer}");
			Console.WriteLine($"IsNetCore: {environment.IsNetCore}");
			Console.WriteLine($"ClientId: {environment.ClientId}");
			Console.WriteLine($"ClientSecret: {MaskSensitiveData("ClientSecret", environment.ClientSecret)}");
			Console.WriteLine($"AuthAppUri: {environment.AuthAppUri}");
			Console.WriteLine($"SimpleLoginUri: {environment.SimpleloginUri}");
			Console.WriteLine($"Safe: {environment.Safe}");
			Console.WriteLine($"DeveloperModeEnabled: {environment.DeveloperModeEnabled}");
			Console.WriteLine($"IsDevMode: {environment.IsDevMode}");
			Console.WriteLine($"WorkspacePathes: {environment.WorkspacePathes}");
			Console.WriteLine($"EnvironmentPath: {environment.EnvironmentPath}");
			Console.WriteLine($"DbServerKey: {environment.DbServerKey}");
			if (environment.DbServer != null) {
				Console.WriteLine("DbServer:");
				Console.WriteLine($"  Uri: {environment.DbServer.Uri}");
				Console.WriteLine($"  WorkingFolder: {environment.DbServer.WorkingFolder}");
				Console.WriteLine($"  Login: {environment.DbServer.Login}");
				Console.WriteLine($"  Password: {MaskSensitiveData("Password", environment.DbServer.Password)}");
			}
		}

		public override int Execute(AppListOptions options) {
			try {
				Console.OutputEncoding = System.Text.Encoding.UTF8;
				
				// Determine format (raw flag takes precedence)
				string format = options.Raw ? "raw" : (options.Format ?? "json");
				string environmentName = string.IsNullOrEmpty(options.Name) ? options.Env : options.Name;
				
				// Handle short format (backward compatibility with -s flag)
				if (options.ShowShort) {
					_settingsRepository.ShowSettingsTo(Console.Out, environmentName, showShort: true);
					return 0;
				}
				
				// Handle format options for specific environment or all environments
				if (!string.IsNullOrEmpty(environmentName)) {
					// Single environment query
					var environment = _settingsRepository.FindEnvironment(environmentName);
					if (environment == null) {
						Console.WriteLine($"Environment '{environmentName}' not found");
						return 1;
					}
					
					switch (format.ToLower()) {
						case "json":
							OutputAsJson(environment, environmentName);
							break;
						case "table":
							// For single environment, output as raw in table-like format
							Console.WriteLine($"Environment: {environmentName}");
							Console.WriteLine(new string('-', 50));
							OutputAsRaw(environment, null);
							break;
						case "raw":
							OutputAsRaw(environment, environmentName);
							break;
						default:
							Console.WriteLine($"Unknown format: {format}. Use: json, table, or raw");
							return 1;
					}
				} else {
					// All environments - need to get them all
					// Use reflection pattern if ShowSettingsTo is not sufficient
					var allEnvs = GetAllEnvironments();
					
					if (allEnvs == null || allEnvs.Count == 0) {
						Console.WriteLine("No environments configured");
						return 0;
					}
					
					switch (format.ToLower()) {
						case "json":
							// Use default behavior for all environments
							_settingsRepository.ShowSettingsTo(Console.Out, null, showShort: false);
							break;
						case "table":
							OutputAsTable(allEnvs);
							break;
						case "raw":
							// Output all environments in raw format
							foreach (var env in allEnvs) {
								Console.WriteLine($"\n=== {env.Key} ===");
								OutputAsRaw(env.Value, null);
							}
							break;
						default:
							Console.WriteLine($"Unknown format: {format}. Use: json, table, or raw");
							return 1;
					}
				}
				
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e.Message);
				return 1;
			}
		}

		/// <summary>
		/// Get all environments using reflection pattern (similar to StartCommand/StopCommand)
		/// </summary>
		private Dictionary<string, EnvironmentSettings> GetAllEnvironments() {
			try {
				// Try to access through ISettingsRepository if a method exists
				var method = _settingsRepository.GetType().GetMethod("GetAllEnvironments", 
					System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
				if (method != null) {
					return (Dictionary<string, EnvironmentSettings>)method.Invoke(_settingsRepository, null);
				}
				
				// Fallback: Try to find environments through reflection
				// This matches the pattern used in StartCommand.cs and StopCommand.cs
				var settings = _settingsRepository.GetType().GetField("_settings", 
					System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
				if (settings != null) {
					var settingsValue = settings.GetValue(_settingsRepository);
					var environmentsProperty = settingsValue.GetType().GetProperty("Environments");
					if (environmentsProperty != null) {
						return (Dictionary<string, EnvironmentSettings>)environmentsProperty.GetValue(settingsValue);
					}
				}
				
				return null;
			} catch {
				return null;
			}
		}
	}
}
