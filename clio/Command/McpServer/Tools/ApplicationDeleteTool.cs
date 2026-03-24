using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Command.PackageCommand;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class ApplicationDeleteTool(
	UninstallAppCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<UninstallAppOptions>(command, logger, commandResolver) {

	internal const string ToolName = "application-delete";

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
	[Description("Uninstall (delete) a Creatio application by name or code")]
	public ApplicationDeleteResponse DeleteApplication(
		[Description("Parameters: environment-name, app-name (required)")]
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
				CommandExecutionResult result = InternalExecute(options);
				return new ApplicationDeleteResponse {
					Success = result.ExitCode == 0,
					Error = result.ExitCode != 0
						? string.Join("; ", result.Output)
						: null
				};
			}
		} catch (Exception ex) {
			return new ApplicationDeleteResponse {
				Success = false,
				Error = ex.Message
			};
		}
	}
}

public sealed record ApplicationDeleteArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name, e.g. 'local'")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("app-name")]
	[property: Description("Application name or code to uninstall, e.g. 'UsrMyApp'")]
	[property: Required]
	string AppName,

	[property: JsonPropertyName("uri")] string? Uri = null,
	[property: JsonPropertyName("login")] string? Login = null,
	[property: JsonPropertyName("password")] string? Password = null
);

public sealed class ApplicationDeleteResponse {
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Error { get; init; }
}
