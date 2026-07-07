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
/// MCP tool surface for the <c>get-identity-service-config</c> command.
/// </summary>
[McpServerToolType]
[FeatureToggle("deploy-identity")]
public sealed class GetIdentityServiceConfigTool(
	GetIdentityServiceConfigCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<GetIdentityServiceConfigOptions>(command, logger, commandResolver)
{
	/// <summary>
	/// Stable MCP tool name for reading the OAuth IdentityService configuration.
	/// </summary>
	internal const string GetIdentityServiceConfigToolName = "get-identity-service-config";

	/// <summary>
	/// Reads (or derives) the OAuth IdentityService configuration of a Creatio environment over REST.
	/// </summary>
	[McpServerTool(Name = GetIdentityServiceConfigToolName, ReadOnly = true, Destructive = false,
		Idempotent = true, OpenWorld = false)]
	[Description("""
				 Reads the OAuth IdentityService configuration of a Creatio environment over REST (no filesystem or DB access required).
				 Reads the OAuth20IdentityServerUrl and OAuth20IdentityServerClientId system settings; when the URL is empty it derives
				 the IdentityService host by inserting "-is" into the Creatio host (e.g. 186843-crm-bundle.creatio.com ->
				 186843-crm-bundle-is.creatio.com). Reports identityServerUrl, source (setting|derived|none), the token endpoint
				 ({base}/connect/token), the discovery endpoint ({base}/.well-known/openid-configuration), and whether the discovery
				 document is reachable. Use this first when configuring server-to-server OAuth on a remote Creatio.
				 """)]
	public GetIdentityServiceConfigResponse GetIdentityServiceConfig(
		[Description("Parameters: environment-name (required).")]
		[Required]
		GetIdentityServiceConfigArgs args) {
		try {
			GetIdentityServiceConfigOptions options = new() {
				Environment = args.EnvironmentName
			};
			GetIdentityServiceConfigCommand resolvedCommand = ResolveCommand<GetIdentityServiceConfigCommand>(options);
			GetIdentityServiceConfigResult result = resolvedCommand.GetConfig(options);
			return new GetIdentityServiceConfigResponse(true, result, null);
		}
		catch (Exception exception) {
			return new GetIdentityServiceConfigResponse(false, null, exception.Message);
		}
	}
}

/// <summary>
/// Arguments for the <c>get-identity-service-config</c> MCP tool.
/// </summary>
public sealed record GetIdentityServiceConfigArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Creatio environment name")]
	[property: Required]
	string EnvironmentName);

/// <summary>
/// Structured envelope returned by the <c>get-identity-service-config</c> MCP tool.
/// </summary>
public sealed record GetIdentityServiceConfigResponse(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("config")] GetIdentityServiceConfigResult? Config = null,
	[property: JsonPropertyName("error")] string? Error = null);
