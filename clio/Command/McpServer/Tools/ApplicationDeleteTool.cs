using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using Clio.Command.PackageCommand;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for uninstalling Creatio applications.
/// </summary>
[McpServerToolType]
public sealed class ApplicationDeleteTool(
	UninstallAppCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<UninstallAppOptions>(command, logger, commandResolver) {

	internal const string ToolName = "delete-app";

	/// <summary>
	/// Uninstalls a Creatio application by name or code.
	/// </summary>
	/// <param name="args">MCP arguments describing the target application and connection.</param>
	/// <returns>A structured response that reports whether the uninstall completed successfully.</returns>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
	[Description("Uninstall (delete) a Creatio application by name or code. Prefer `environment-name`; keep direct connection args only for bootstrap or emergency fallback flows.")]
	public ApplicationDeleteResponse DeleteApplication(
		[Description("Parameters: app-name (required); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required]
		ApplicationDeleteArgs args) {
		try {
			if (string.IsNullOrWhiteSpace(args.AppName)) {
				return new ApplicationDeleteResponse {
					Success = false,
					Error = "app-name is required. Provide the application name or code."
				};
			}
			UninstallAppOptions options = new() {
				Name = args.AppName,
				Environment = args.EnvironmentName,
				Uri = args.Uri,
				Login = args.Login,
				Password = args.Password
			};
			lock (CommandExecutionSyncRoot) {
				CommandExecutionResult result = InternalExecute<UninstallAppCommand>(options);
				return new ApplicationDeleteResponse {
					Success = result.ExitCode == 0,
					Error = ResolveError(result)
				};
			}
		} catch (Exception ex) {
			return new ApplicationDeleteResponse {
				Success = false,
				Error = ex.Message
			};
		}
	}

	private static string? ResolveError(CommandExecutionResult result) {
		if (result.ExitCode == 0) {
			return null;
		}
		string[] outputMessages = result.Output
			.Select(message => message.Value?.ToString())
			.Where(message => !string.IsNullOrWhiteSpace(message))
			.Distinct()
			.ToArray();
		return outputMessages.Length > 0
			? string.Join("; ", outputMessages)
			: "Application uninstall failed.";
	}
}

/// <summary>
/// Arguments for the <c>delete-app</c> MCP tool.
/// </summary>
public sealed record ApplicationDeleteArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name, e.g. 'local'. Preferred for normal MCP work.")]
	string? EnvironmentName,

	[property: JsonPropertyName("app-name")]
	[property: Description("Application name or code to uninstall, e.g. 'UsrMyApp'")]
	[property: Required]
	string AppName,

	[property: JsonPropertyName("uri")]
	[property: Description("Direct Creatio URL. Use only when bootstrap is broken or before the environment can be registered through reg-web-app.")]
	string? Uri = null,
	[property: JsonPropertyName("login")]
	[property: Description("Direct Creatio login paired with `uri`. Emergency fallback only.")]
	string? Login = null,
	[property: JsonPropertyName("password")]
	[property: Description("Direct Creatio password paired with `uri`. Emergency fallback only.")]
	string? Password = null
);

/// <summary>
/// Structured response from the <c>delete-app</c> MCP tool.
/// </summary>
public sealed class ApplicationDeleteResponse {
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Error { get; init; }
}
