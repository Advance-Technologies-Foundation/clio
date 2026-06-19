using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
///     Output-format parsing shared by the identity-assertion MCP tools.
/// </summary>
internal static class IdentityToolFormat
{

	public const string DefaultFormat = "text";

	public static IdentityOutputFormat Parse(string format) =>
		string.Equals(format, "json", StringComparison.OrdinalIgnoreCase)
			? IdentityOutputFormat.Json
			: IdentityOutputFormat.Text;

}

#region Class: GetIdentityAssertionTool

/// <summary>
///     MCP tool that issues a short-lived signed identity assertion (JWT) for the current user,
///     resolving the command for the per-call environment.
/// </summary>
public class GetIdentityAssertionTool(
	GetIdentityAssertionCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<GetIdentityAssertionOptions>(command, logger, commandResolver) {

	internal const string ToolName = "get-identity-assertion";

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
	 Description("Issue a short-lived signed identity assertion (JWT) for the current authorized user. " +
		"This is the token the Creatio frontend passes to the AI chat to start the Identity Service V3 " +
		"token-exchange flow. Requires the EnableIdentityAssertionIssuer feature on the environment.")]
	public CommandExecutionResult GetIdentityAssertion(
		[Description("Target Environment name")] [Required] string environmentName,
		[DefaultValue(IdentityToolFormat.DefaultFormat)]
		[Description("Output format: 'text' (assertion token) or 'json' (full payload)")]
		string format = IdentityToolFormat.DefaultFormat) {
		if (string.IsNullOrWhiteSpace(environmentName)) {
			return CommandExecutionResult.FromError("environment-name is required and cannot be empty.");
		}
		GetIdentityAssertionOptions options = new() {
			Environment = environmentName,
			Format = IdentityToolFormat.Parse(format),
			TimeOut = 30_000
		};
		return InternalExecute<GetIdentityAssertionCommand>(options);
	}

}

#endregion

#region Class: GetIdentityPublicJwkTool

/// <summary>
///     MCP tool that reads the instance public key (JWK) for registration with Identity Service V3.
/// </summary>
public class GetIdentityPublicJwkTool(
	GetIdentityPublicJwkCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<GetIdentityPublicJwkOptions>(command, logger, commandResolver) {

	internal const string ToolName = "get-identity-public-jwk";

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
	 Description("Get the instance public key (JWK) used to verify identity assertions in Identity Service V3. " +
		"Register this key once with Identity Service V3 at onboarding. Requires the " +
		"EnableIdentityAssertionIssuer feature and the CanManageIdentityAssertionIssuer permission.")]
	public CommandExecutionResult GetIdentityPublicJwk(
		[Description("Target Environment name")] [Required] string environmentName,
		[DefaultValue(IdentityToolFormat.DefaultFormat)]
		[Description("Output format: 'text' (compact JWK) or 'json' (indented JWK)")]
		string format = IdentityToolFormat.DefaultFormat) {
		if (string.IsNullOrWhiteSpace(environmentName)) {
			return CommandExecutionResult.FromError("environment-name is required and cannot be empty.");
		}
		GetIdentityPublicJwkOptions options = new() {
			Environment = environmentName,
			Format = IdentityToolFormat.Parse(format),
			TimeOut = 30_000
		};
		return InternalExecute<GetIdentityPublicJwkCommand>(options);
	}

}

#endregion

#region Class: RegenerateIdentitySigningKeyTool

/// <summary>
///     MCP tool that regenerates the instance identity-assertion signing key pair (destructive).
/// </summary>
public class RegenerateIdentitySigningKeyTool(
	RegenerateIdentitySigningKeyCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<RegenerateIdentitySigningKeyOptions>(command, logger, commandResolver) {

	internal const string ToolName = "regenerate-identity-signing-key";

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false),
	 Description("Regenerate the instance identity-assertion signing key pair. DESTRUCTIVE: assertions signed " +
		"with the previous key stop validating and the new public key must be re-registered with Identity " +
		"Service V3. Requires the EnableIdentityAssertionIssuer feature and the " +
		"CanManageIdentityAssertionIssuer permission.")]
	public CommandExecutionResult RegenerateIdentitySigningKey(
		[Description("Target Environment name")] [Required] string environmentName,
		[DefaultValue(IdentityToolFormat.DefaultFormat)]
		[Description("Output format: 'text' (OK) or 'json' (status object)")]
		string format = IdentityToolFormat.DefaultFormat) {
		if (string.IsNullOrWhiteSpace(environmentName)) {
			return CommandExecutionResult.FromError("environment-name is required and cannot be empty.");
		}
		RegenerateIdentitySigningKeyOptions options = new() {
			Environment = environmentName,
			Format = IdentityToolFormat.Parse(format),
			TimeOut = 30_000
		};
		return InternalExecute<RegenerateIdentitySigningKeyCommand>(options);
	}

}

#endregion

#region Class: CheckAuthCodeFlowTool

/// <summary>
///     MCP tool that reports whether the environment can use the OAuth authorization code flow.
/// </summary>
public class CheckAuthCodeFlowTool(
	CheckAuthCodeFlowCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<CheckAuthCodeFlowOptions>(command, logger, commandResolver) {

	internal const string ToolName = "check-auth-code-flow";

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
	 Description("Check whether the environment can use the OAuth authorization code flow with the Identity " +
		"Service. Useful as a quick diagnostic when setting up the AI chat identity flow.")]
	public CommandExecutionResult CheckAuthCodeFlow(
		[Description("Target Environment name")] [Required] string environmentName,
		[DefaultValue(IdentityToolFormat.DefaultFormat)]
		[Description("Output format: 'text' (true/false) or 'json' (flag object)")]
		string format = IdentityToolFormat.DefaultFormat) {
		if (string.IsNullOrWhiteSpace(environmentName)) {
			return CommandExecutionResult.FromError("environment-name is required and cannot be empty.");
		}
		CheckAuthCodeFlowOptions options = new() {
			Environment = environmentName,
			Format = IdentityToolFormat.Parse(format),
			TimeOut = 30_000
		};
		return InternalExecute<CheckAuthCodeFlowCommand>(options);
	}

}

#endregion
