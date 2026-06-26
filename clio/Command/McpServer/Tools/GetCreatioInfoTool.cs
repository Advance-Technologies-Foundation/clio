using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for the <c>get-info</c> (alias <c>describe</c>) command. Reports a single,
/// SOURCE-INDEPENDENT environment description whose shape is identical with or without cliogate, so an
/// agent can reason about the same field set on every Creatio instance.
/// </summary>
public sealed class GetCreatioInfoTool(
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<GetCreatioInfoCommandOptions>(null, logger, commandResolver) {

	/// <summary>
	/// Stable MCP tool name for describing a Creatio environment.
	/// </summary>
	internal const string ToolName = "describe-environment";

	// Known mis-spellings an LLM tends to emit instead of the kebab-case argument names. Rejected with
	// an actionable rename hint so a camelCase 'environmentName' never silently binds to nothing and
	// describes the wrong (default) environment.
	private static readonly Dictionary<string, string> LegacyAliases = new(StringComparer.Ordinal) {
		["environmentName"] = "environment-name",
		["environment_name"] = "environment-name",
		["clientId"] = "client-id",
		["client_id"] = "client-id",
		["clientSecret"] = "client-secret",
		["client_secret"] = "client-secret",
		["authAppUri"] = "auth-app-uri",
		["auth_app_uri"] = "auth-app-uri"
	};

	/// <summary>
	/// Describes the target Creatio environment and returns the combined report through the standard
	/// <see cref="CommandExecutionResult"/> log channel.
	/// </summary>
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description("""
				 Describe a Creatio environment as ONE source-independent JSON report — the field set is the
				 same with or without cliogate, so an agent can reason about every instance the same way.

				 Read get-guidance name=describe-environment for the full field catalogue before interpreting
				 the output. The report is assembled best-effort from up to three sources and the command still
				 succeeds (exit 0) when an optional source is unavailable:
				   - ALWAYS (no cliogate, authenticated session): coreVersion plus user / culture / workspace /
				     maintainer / environmentType metadata, from ApplicationInfoService.
				   - WITHOUT cliogate (admin-gated, needs CanManageSolution): dbEngineType (MSSql / PostgreSql /
				     Oracle), frameworkKind (Net vs NetFramework) and frameworkDescription (e.g. ".NET 8.0.x").
				   - cliogate ONLY (>= 2.0.0.32 installed): productName and licenseInfo; cliogate also backfills
				     dbEngineType / framework on older Creatio that predate the admin-gated operation.

				 Use it to confirm the platform version before assuming component availability, to read the
				 database engine and executing framework for deploy / troubleshooting decisions, and to check the
				 product and license. Only productName and licenseInfo strictly require cliogate. Read-only:
				 it never mutates the environment.
				 """)]
	public CommandExecutionResult GetInfo(
		[Description("describe-environment parameters")] [Required] GetCreatioInfoArgs args) {
		string? legacyAliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, LegacyAliases, ".",
			"Valid: environment-name, uri, login, password, client-id, client-secret, auth-app-uri, timeout.");
		if (!string.IsNullOrWhiteSpace(legacyAliasError)) {
			return CommandExecutionResult.FromValidationError(legacyAliasError);
		}

		GetCreatioInfoCommandOptions options = new() {
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password,
			ClientId = args.ClientId,
			ClientSecret = args.ClientSecret,
			AuthAppUri = args.AuthAppUri
		};
		if (args.Timeout is { } timeout && timeout > 0) {
			options.TimeOut = timeout;
		}
		return InternalExecute<GetCreatioInfoCommand>(options);
	}
}

/// <summary>
/// MCP arguments for the <c>describe-environment</c> tool.
/// </summary>
public sealed record GetCreatioInfoArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name to describe. PREFER this.")]
	string? EnvironmentName = null,

	[property: JsonPropertyName("uri")]
	[property: Description("Emergency fallback only: direct application URI when no environment is registered.")]
	string? Uri = null,

	[property: JsonPropertyName("login")]
	[property: Description("Emergency fallback only: login paired with 'uri' (basic auth).")]
	string? Login = null,

	[property: JsonPropertyName("password")]
	[property: Description("Emergency fallback only: password paired with 'uri' (basic auth).")]
	string? Password = null,

	[property: JsonPropertyName("client-id")]
	[property: Description("Emergency fallback only: OAuth client id paired with 'uri' and 'auth-app-uri'.")]
	string? ClientId = null,

	[property: JsonPropertyName("client-secret")]
	[property: Description("Emergency fallback only: OAuth client secret paired with 'client-id'.")]
	string? ClientSecret = null,

	[property: JsonPropertyName("auth-app-uri")]
	[property: Description("Emergency fallback only: OAuth authentication app URI paired with 'client-id'.")]
	string? AuthAppUri = null,

	[property: JsonPropertyName("timeout")]
	[property: Description("Optional request timeout in milliseconds (default 100000).")]
	int? Timeout = null
) {
	/// <summary>
	/// Overflow bag for request fields that do not bind to a declared kebab-case parameter (e.g. the
	/// camelCase <c>environmentName</c> an LLM tends to emit), used to reject mis-spelled fields.
	/// </summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
