using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Command;
using Clio.Command.OAuthAppConfiguration;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for the <c>create-server-to-server-oauth-app</c> command.
/// </summary>
[McpServerToolType]
[FeatureToggle("deploy-identity")]
public sealed class CreateServerToServerOAuthAppTool(
	CreateServerToServerOAuthAppCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<CreateServerToServerOAuthAppOptions>(command, logger, commandResolver)
{
	/// <summary>
	/// Stable MCP tool name for creating a server-to-server OAuth app.
	/// </summary>
	internal const string CreateServerToServerOAuthAppToolName = "create-server-to-server-oauth-app";

	/// <summary>
	/// Creates a server-to-server (client_credentials) OAuth app in Creatio over REST.
	/// </summary>
	[McpServerTool(Name = CreateServerToServerOAuthAppToolName, ReadOnly = false, Destructive = true,
		Idempotent = false, OpenWorld = false)]
	[Description("""
				 Creates a server-to-server (client_credentials) OAuth app in Creatio via OAuthConfigService/AddClient over REST,
				 binding it to a system user. Supply systemUserId (from resolve-oauth-system-user or create-oauth-technical-user)
				 or systemUser by name (defaults to Supervisor). Returns clientId and clientSecret in the structured result ONLY:
				 the secret is never written to logs and is NOT persisted to clio settings by this tool. Capture the secret from
				 the response immediately - it cannot be retrieved again.
				 """)]
	public CreateServerToServerOAuthAppResponse CreateServerToServerOAuthApp(
		[Description("Parameters: environment-name (required); system-user-id (optional); system-user (optional name, defaults to Supervisor); client-name, client-application-url, client-description (optional).")]
		[Required]
		CreateServerToServerOAuthAppArgs args) {
		try {
			CreateServerToServerOAuthAppOptions options = new() {
				Environment = args.EnvironmentName,
				SystemUserId = args.SystemUserId,
				SystemUser = args.SystemUser,
				ClientName = args.ClientName ?? "clio s2s",
				ClientApplicationUrl = args.ClientApplicationUrl
					?? "https://github.com/Advance-Technologies-Foundation/clio.git",
				ClientDescription = args.ClientDescription ?? "server-to-server integration for clio cli"
			};
			CreateServerToServerOAuthAppCommand resolvedCommand =
				ResolveCommand<CreateServerToServerOAuthAppCommand>(options);
			CreateServerToServerOAuthAppResult result = resolvedCommand.CreateApp(options);
			return new CreateServerToServerOAuthAppResponse(true, result, null);
		}
		catch (Exception exception) {
			return new CreateServerToServerOAuthAppResponse(false, null, exception.Message);
		}
	}
}

/// <summary>
/// Arguments for the <c>create-server-to-server-oauth-app</c> MCP tool.
/// </summary>
public sealed record CreateServerToServerOAuthAppArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Creatio environment name")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("system-user-id")]
	[property: Description("System user id to bind the OAuth app to. Resolve it first with resolve-oauth-system-user or create-oauth-technical-user.")]
	string? SystemUserId = null,

	[property: JsonPropertyName("system-user")]
	[property: Description("System user name to bind the OAuth app to when system-user-id is omitted. Defaults to Supervisor.")]
	string? SystemUser = null,

	[property: JsonPropertyName("client-name")]
	[property: Description("OAuth client display name. Defaults to 'clio s2s'.")]
	string? ClientName = "clio s2s",

	[property: JsonPropertyName("client-application-url")]
	[property: Description("OAuth client application URL.")]
	string? ClientApplicationUrl = "https://github.com/Advance-Technologies-Foundation/clio.git",

	[property: JsonPropertyName("client-description")]
	[property: Description("OAuth client description.")]
	string? ClientDescription = "server-to-server integration for clio cli");

/// <summary>
/// Structured envelope returned by the <c>create-server-to-server-oauth-app</c> MCP tool. The
/// <c>app.clientSecret</c> is the only place the secret is surfaced; it is never logged.
/// </summary>
public sealed record CreateServerToServerOAuthAppResponse(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("app")] CreateServerToServerOAuthAppResult? App = null,
	[property: JsonPropertyName("error")] string? Error = null);
