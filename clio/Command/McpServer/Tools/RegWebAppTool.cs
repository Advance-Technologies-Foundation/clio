using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for the <c>reg-web-app</c> command.
/// </summary>
public class RegWebAppTool(RegAppCommand command, ILogger logger) : BaseTool<RegAppOptions>(command, logger) {

	/// <summary>
	/// Registers or updates a local clio web application configuration.
	/// </summary>
	/// <param name="args">Tool arguments that map to <see cref="RegAppOptions"/>.</param>
	/// <returns>Execution result with the captured command log output.</returns>
	[McpServerTool(Name = "reg-web-app", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
	[Description("""
				 Registers or updates a local clio web application configuration.
				 
				 This command updates clio's local environment settings. It does not modify Creatio metadata.
				 The tool also supports setting the active environment and importing environments from IIS.
				 """)]
	public CommandExecutionResult RegisterWebApp(
		[Description("reg-web-app parameters")] [Required] RegWebAppArgs args
	) {
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)
			&& string.IsNullOrWhiteSpace(args.ActiveEnvironment)
			&& !args.AddFromIis) {
			return new CommandExecutionResult(1, [
				new ErrorMessage(
					"Provide `environment-name` to register/update an environment, `active-environment` to switch the default environment, or set `add-from-iis` to import environments from IIS.")
			]);
		}

		RegAppOptions options = new() {
			EnvironmentName = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password,
			Maintainer = args.Maintainer,
			CheckLogin = args.CheckLogin,
			ActiveEnvironment = args.ActiveEnvironment,
			FromIis = args.AddFromIis,
			Host = args.Host,
			IsNetCore = args.IsNetCore,
			DevMode = args.DeveloperModeEnabled?.ToString(),
			Safe = args.Safe?.ToString(),
			ClientId = args.ClientId,
			ClientSecret = args.ClientSecret,
			AuthAppUri = args.AuthAppUri,
			WorkspacePathes = args.WorkspacePaths,
			EnvironmentPath = args.EnvironmentPath
		};
		return InternalExecute(options);
	}
}

/// <summary>
/// Arguments for the <c>reg-web-app</c> MCP tool.
/// </summary>
public record RegWebAppArgs(
	[property:JsonPropertyName("environment-name")]
	[Description("Environment name to register or update. Not required when `active-environment` or `add-from-iis` is used.")]
	string EnvironmentName = null,

	[property:JsonPropertyName("uri")]
	[Description("Creatio application URL.")]
	string Uri = null,

	[property:JsonPropertyName("login")]
	[Description("User login for the environment.")]
	string Login = null,

	[property:JsonPropertyName("password")]
	[Description("User password for the environment.")]
	string Password = null,

	[property:JsonPropertyName("maintainer")]
	[Description("Maintainer name stored in the local environment configuration.")]
	string Maintainer = null,

	[property:JsonPropertyName("check-login")]
	[Description("When true, attempts to log in after registration to validate the credentials.")]
	bool CheckLogin = false,

	[property:JsonPropertyName("active-environment")]
	[Description("Existing environment name to mark as the default environment.")]
	string ActiveEnvironment = null,

	[property:JsonPropertyName("add-from-iis")]
	[Description("When true, imports Creatio environments discovered from IIS.")]
	bool AddFromIis = false,

	[property:JsonPropertyName("host")]
	[Description("Remote host name to scan when `add-from-iis` is true. Defaults to localhost in the command.")]
	string Host = null,

	[property:JsonPropertyName("is-net-core")]
	[Description("Marks the environment as a .NET Core / NET8 deployment.")]
	bool? IsNetCore = null,

	[property:JsonPropertyName("developer-mode-enabled")]
	[Description("Developer mode flag stored in the local environment configuration.")]
	bool? DeveloperModeEnabled = null,

	[property:JsonPropertyName("safe")]
	[Description("Safe mode flag stored in the local environment configuration.")]
	bool? Safe = null,

	[property:JsonPropertyName("client-id")]
	[Description("OAuth client identifier.")]
	string ClientId = null,

	[property:JsonPropertyName("client-secret")]
	[Description("OAuth client secret.")]
	string ClientSecret = null,

	[property:JsonPropertyName("auth-app-uri")]
	[Description("OAuth application URI.")]
	string AuthAppUri = null,

	[property:JsonPropertyName("workspace-paths")]
	[Description("Workspace paths string stored in the local environment configuration.")]
	string WorkspacePaths = null,

	[property:JsonPropertyName("environment-path")]
	[Description("Path to the Creatio application root folder.")]
	string EnvironmentPath = null
);
