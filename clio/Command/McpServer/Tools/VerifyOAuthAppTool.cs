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
/// MCP tool surface for the <c>verify-oauth-app</c> command.
/// </summary>
[McpServerToolType]
[FeatureToggle("deploy-identity")]
public sealed class VerifyOAuthAppTool(
	VerifyOAuthAppCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<VerifyOAuthAppOptions>(command, logger, commandResolver)
{
	/// <summary>
	/// Stable MCP tool name for verifying a server-to-server OAuth app.
	/// </summary>
	internal const string VerifyOAuthAppToolName = "verify-oauth-app";

	/// <summary>
	/// Verifies a server-to-server OAuth app end to end over REST.
	/// </summary>
	[McpServerTool(Name = VerifyOAuthAppToolName, ReadOnly = true, Destructive = false,
		Idempotent = true, OpenWorld = false)]
	[Description("""
				 Verifies a server-to-server OAuth app end to end over REST: acquires a client_credentials access token from the
				 IdentityService token endpoint, then runs a minimal bearer-authenticated Creatio DataService smoke request with that
				 token. Returns tokenAcquired, dataServiceStatus (HTTP status of the smoke request), and ok (token acquired AND
				 dataServiceStatus 200). The access token text is never returned or logged. Supply identity-server-url to override the
				 IdentityService URL otherwise read from the OAuth20IdentityServerUrl system setting or derived from the Creatio host.
				 """)]
	public VerifyOAuthAppResponse VerifyOAuthApp(
		[Description("Parameters: environment-name (required); client-id (required); client-secret (required); identity-server-url (optional override).")]
		[Required]
		VerifyOAuthAppArgs args) {
		try {
			VerifyOAuthAppOptions options = new() {
				Environment = args.EnvironmentName,
				ClientId = args.ClientId,
				ClientSecret = args.ClientSecret,
				IdentityServerUrl = args.IdentityServerUrl
			};
			VerifyOAuthAppCommand resolvedCommand = ResolveCommand<VerifyOAuthAppCommand>(options);
			VerifyOAuthAppResult result = resolvedCommand.Verify(options);
			return new VerifyOAuthAppResponse(true, result, null);
		}
		catch (Exception exception) {
			return new VerifyOAuthAppResponse(false, null, exception.Message);
		}
	}
}

/// <summary>
/// Arguments for the <c>verify-oauth-app</c> MCP tool.
/// </summary>
public sealed record VerifyOAuthAppArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Creatio environment name")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("client-id")]
	[property: Description("OAuth client id to verify")]
	[property: Required]
	string ClientId,

	[property: JsonPropertyName("client-secret")]
	[property: Description("OAuth client secret to verify. Never returned or logged.")]
	[property: Required]
	string ClientSecret,

	[property: JsonPropertyName("identity-server-url")]
	[property: Description("Explicit IdentityService base URL. Defaults to the OAuth20IdentityServerUrl system setting, then a derived -is host.")]
	string? IdentityServerUrl = null);

/// <summary>
/// Structured envelope returned by the <c>verify-oauth-app</c> MCP tool.
/// </summary>
public sealed record VerifyOAuthAppResponse(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("result")] VerifyOAuthAppResult? Result = null,
	[property: JsonPropertyName("error")] string? Error = null);
