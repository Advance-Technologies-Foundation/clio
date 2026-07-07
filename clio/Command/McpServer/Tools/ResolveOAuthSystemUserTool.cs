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
/// MCP tool surface for the <c>resolve-oauth-system-user</c> command.
/// </summary>
[McpServerToolType]
[FeatureToggle("deploy-identity")]
public sealed class ResolveOAuthSystemUserTool(
	ResolveOAuthSystemUserCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<ResolveOAuthSystemUserOptions>(command, logger, commandResolver)
{
	/// <summary>
	/// Stable MCP tool name for resolving a Creatio system user.
	/// </summary>
	internal const string ResolveOAuthSystemUserToolName = "resolve-oauth-system-user";

	/// <summary>
	/// Resolves a Creatio system user (SysAdminUnit) by name or id over DataService REST.
	/// </summary>
	[McpServerTool(Name = ResolveOAuthSystemUserToolName, ReadOnly = true, Destructive = false,
		Idempotent = true, OpenWorld = false)]
	[Description("""
				 Resolves a Creatio system user (SysAdminUnit) by name (default Supervisor) or by id over DataService REST
				 (no DB access). Returns systemUserId, name, and found. Use this to obtain the systemUserId that
				 create-server-to-server-oauth-app binds the OAuth app to.
				 """)]
	public ResolveOAuthSystemUserResponse ResolveOAuthSystemUser(
		[Description("Parameters: environment-name (required); name (optional, defaults to Supervisor); id (optional, takes precedence over name).")]
		[Required]
		ResolveOAuthSystemUserArgs args) {
		try {
			ResolveOAuthSystemUserOptions options = new() {
				Environment = args.EnvironmentName,
				Name = args.Name,
				Id = args.Id
			};
			ResolveOAuthSystemUserCommand resolvedCommand = ResolveCommand<ResolveOAuthSystemUserCommand>(options);
			ResolveOAuthSystemUserResult result = resolvedCommand.ResolveSystemUser(options);
			return new ResolveOAuthSystemUserResponse(true, result, null);
		}
		catch (Exception exception) {
			return new ResolveOAuthSystemUserResponse(false, null, exception.Message);
		}
	}
}

/// <summary>
/// Arguments for the <c>resolve-oauth-system-user</c> MCP tool.
/// </summary>
public sealed record ResolveOAuthSystemUserArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Creatio environment name")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("name")]
	[property: Description("System user (SysAdminUnit) name to resolve. Defaults to Supervisor.")]
	string? Name = null,

	[property: JsonPropertyName("id")]
	[property: Description("System user (SysAdminUnit) id to resolve. Takes precedence over name when supplied.")]
	string? Id = null);

/// <summary>
/// Structured envelope returned by the <c>resolve-oauth-system-user</c> MCP tool.
/// </summary>
public sealed record ResolveOAuthSystemUserResponse(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("user")] ResolveOAuthSystemUserResult? User = null,
	[property: JsonPropertyName("error")] string? Error = null);
