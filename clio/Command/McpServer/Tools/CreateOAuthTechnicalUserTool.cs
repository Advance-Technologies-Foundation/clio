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
/// MCP tool surface for the <c>create-oauth-technical-user</c> command.
/// </summary>
[McpServerToolType]
[FeatureToggle("deploy-identity")]
public sealed class CreateOAuthTechnicalUserTool(
	CreateOAuthTechnicalUserCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<CreateOAuthTechnicalUserOptions>(command, logger, commandResolver)
{
	/// <summary>
	/// Stable MCP tool name for creating a Creatio technical user.
	/// </summary>
	internal const string CreateOAuthTechnicalUserToolName = "create-oauth-technical-user";

	/// <summary>
	/// Creates a Creatio technical user for a server-to-server OAuth app over REST.
	/// </summary>
	[McpServerTool(Name = CreateOAuthTechnicalUserToolName, ReadOnly = false, Destructive = true,
		Idempotent = false, OpenWorld = false)]
	[Description("""
				 Creates a Creatio technical user via OAuthConfigService/CreateTechnicalUser over REST and returns its systemUserId.
				 ROLE GRANT IS DEFERRED: this REST-only path does NOT assign any Creatio role to the new user, because the
				 deploy-identity role grant is database-direct and cannot run against a remote environment. The roleGranted flag is
				 always false and roleGrantNotice explains the follow-up. If the server-to-server app needs elevated permissions,
				 grant the role manually in Creatio or use the local deploy-identity --create-tech-user path. Prefer
				 resolve-oauth-system-user (bind to an existing user such as Supervisor) unless a dedicated technical user is required.
				 """)]
	public CreateOAuthTechnicalUserResponse CreateOAuthTechnicalUser(
		[Description("Parameters: environment-name (required); name (optional technical user name, defaults to clio_oauth_technical_user).")]
		[Required]
		CreateOAuthTechnicalUserArgs args) {
		try {
			CreateOAuthTechnicalUserOptions options = new() {
				Environment = args.EnvironmentName,
				Name = args.Name
			};
			CreateOAuthTechnicalUserCommand resolvedCommand = ResolveCommand<CreateOAuthTechnicalUserCommand>(options);
			CreateOAuthTechnicalUserResult result = resolvedCommand.CreateTechnicalUser(options);
			return new CreateOAuthTechnicalUserResponse(true, result, null);
		}
		catch (Exception exception) {
			return new CreateOAuthTechnicalUserResponse(false, null, exception.Message);
		}
	}
}

/// <summary>
/// Arguments for the <c>create-oauth-technical-user</c> MCP tool.
/// </summary>
public sealed record CreateOAuthTechnicalUserArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Creatio environment name")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("name")]
	[property: Description("Technical user name to create. Defaults to clio_oauth_technical_user.")]
	string? Name = null);

/// <summary>
/// Structured envelope returned by the <c>create-oauth-technical-user</c> MCP tool.
/// </summary>
public sealed record CreateOAuthTechnicalUserResponse(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("user")] CreateOAuthTechnicalUserResult? User = null,
	[property: JsonPropertyName("error")] string? Error = null);
