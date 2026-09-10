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
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description("""
				 Verifies a server-to-server OAuth app end to end over REST: acquires a client_credentials access token from the
				 IdentityService token endpoint, then runs a minimal bearer-authenticated Creatio DataService smoke request with that
				 token. Returns tokenAcquired, dataServiceStatus (HTTP status of the smoke request), and ok (token acquired AND
				 dataServiceStatus 200 with a successful DataService response). The access token text is never returned or logged.
				 Defaults to the registered environment OAuth credentials and AuthAppUri. Supply both client-id and client-secret
				 to override credentials, and identity-server-url to override the IdentityService URL. Without AuthAppUri, the URL
				 is read from OAuth20IdentityServerUrl or derived from the Creatio host. No configuration or credentials are changed.
				 """)]
	public VerifyOAuthAppResponse VerifyOAuthApp(
		[Description("Parameters: environment-name (required); client-id and client-secret (optional pair); identity-server-url (optional override).")]
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
		catch (Exception) {
			return new VerifyOAuthAppResponse(false, null, VerifyOAuthAppCommand.VerificationFailureMessage);
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
	[property: Description("OAuth client id override. Omit both credentials to use the registered environment.")]
	string? ClientId = null,

	[property: JsonPropertyName("client-secret")]
	[property: Description("OAuth client secret to verify. Never returned or logged.")]
	string? ClientSecret = null,

	[property: JsonPropertyName("identity-server-url")]
	[property: Description("Explicit IdentityService base URL. Defaults to registered AuthAppUri, then OAuth20IdentityServerUrl, then a derived -is host.")]
	string? IdentityServerUrl = null);

/// <summary>
/// Structured envelope returned by the <c>verify-oauth-app</c> MCP tool.
/// </summary>
public sealed record VerifyOAuthAppResponse(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("result")] VerifyOAuthAppResult? Result = null,
	[property: JsonPropertyName("error")] string? Error = null);
