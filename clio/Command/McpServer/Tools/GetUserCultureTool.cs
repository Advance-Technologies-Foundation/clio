using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Read-only MCP tool that resolves the logged-in Creatio user's profile culture for an
/// environment and returns a structured <c>{ culture, resolvedFrom, success, reason }</c> signal.
/// On <c>success:false</c> the agent must ask the user which language to use — it must NOT fall
/// back to the host locale or a silent <c>en-US</c>. Mirrors <see cref="ComponentInfoTool"/>'s
/// environment-aware, factory-based resolution pattern.
/// </summary>
[McpServerToolType]
public sealed class GetUserCultureTool(
	ICurrentUserCultureResolverFactory resolverFactory,
	IToolCommandResolver commandResolver) {

	internal const string ToolName = "get-user-culture";
	internal const string ResolvedFromEnvironment = "environment";
	internal const string ResolvedFromFailed = "failed";

	private static readonly Dictionary<string, string> LegacyAliases = new(StringComparer.Ordinal) {
		["environmentName"] = "environment-name",
		["environment_name"] = "environment-name"
	};

	/// <summary>
	/// Resolves the connected user's profile culture from
	/// <c>ApplicationInfoService.svc/GetApplicationInfo</c>.
	/// </summary>
	/// <param name="args">Tool arguments selecting the target environment.</param>
	/// <param name="cancellationToken">Cancellation token propagated by the MCP host.</param>
	/// <returns>A structured culture-resolution signal.</returns>
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Resolve the logged-in Creatio user's profile culture (e.g. en-US, uk-UA) for an environment, " +
		"read from ApplicationInfoService.svc/GetApplicationInfo (no cliogate required). " +
		"Call this ONCE per session before creating applications, objects, pages, sections, lookups, or columns, " +
		"and reuse the result for all generated names, labels, and captions. " +
		"Use the returned culture as the LANGUAGE of the generated text, not only the localization key: author " +
		"captions in that language (an en-US profile means English captions) and keep en-US localization values in " +
		"English — clio rejects a caption whose script does not match a Latin-script culture key. " +
		"On success returns { success:true, culture }. On failure returns { success:false, reason } — " +
		"in that case ASK the user which language to use; do NOT fall back to the host locale or a silent en-US.")]
	public async Task<GetUserCultureResponse> GetUserCulture(
		[Description("Parameters: environment-name (PREFERRED — the registered environment to read the profile culture from). " +
			"uri/login/password: emergency fallback only when no environment is registered.")]
		[Required] GetUserCultureArgs args,
		CancellationToken cancellationToken = default) {
		string? legacyAliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, LegacyAliases, ".",
			"Valid: environment-name, uri, login, password.");
		if (!string.IsNullOrWhiteSpace(legacyAliasError)) {
			return GetUserCultureResponse.Failure(legacyAliasError);
		}

		try {
			EnvironmentSettings settings = ResolveEnvironmentSettings(args);
			ICurrentUserCultureResolver resolver = resolverFactory.Create(settings);
			CultureResolution resolution = await resolver.ResolveAsync(cancellationToken).ConfigureAwait(false);

			// Branch on Success first (CultureResolution invariant, NEW-6): never surface the
			// en-US fallback Culture as if it were the resolved profile culture on failure.
			return resolution.Success
				? GetUserCultureResponse.Resolved(resolution.Culture)
				: GetUserCultureResponse.Failure(resolution.FailureReason);
		}
		catch (Exception ex) {
			return GetUserCultureResponse.Failure(SensitiveErrorTextRedactor.Redact(ex.Message));
		}
	}

	private EnvironmentSettings ResolveEnvironmentSettings(GetUserCultureArgs args) {
		EnvironmentOptions options = new() {
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		return commandResolver.Resolve<EnvironmentSettings>(options);
	}
}

/// <summary>
/// Arguments for the <c>get-user-culture</c> MCP tool.
/// </summary>
public sealed record GetUserCultureArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered environment name to read the logged-in user's profile culture from. PREFER this.")]
	string? EnvironmentName = null,

	[property: JsonPropertyName("uri")]
	[property: Description(McpToolDescriptions.Uri)]
	string? Uri = null,

	[property: JsonPropertyName("login")]
	[property: Description(McpToolDescriptions.Login)]
	string? Login = null,

	[property: JsonPropertyName("password")]
	[property: Description(McpToolDescriptions.Password)]
	string? Password = null
) {
	/// <summary>
	/// Overflow bag for request fields that do not bind to a declared kebab-case parameter (e.g. the
	/// camelCase <c>environmentName</c> an LLM tends to emit), used to reject mis-spelled fields.
	/// </summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

/// <summary>
/// Structured result of the <c>get-user-culture</c> MCP tool.
/// </summary>
public sealed record GetUserCultureResponse {
	/// <summary>Whether the profile culture was resolved and validated.</summary>
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	/// <summary>The resolved, validated culture name (e.g. <c>uk-UA</c>); omitted on failure.</summary>
	[JsonPropertyName("culture")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Culture { get; init; }

	/// <summary>The resolution tier: <c>environment</c> on success, <c>failed</c> otherwise.</summary>
	[JsonPropertyName("resolvedFrom")]
	public string ResolvedFrom { get; init; } = GetUserCultureTool.ResolvedFromFailed;

	/// <summary>The machine-readable failure reason; omitted on success.</summary>
	[JsonPropertyName("reason")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Reason { get; init; }

	/// <summary>Creates a success signal for the resolved culture.</summary>
	public static GetUserCultureResponse Resolved(string culture) => new() {
		Success = true,
		Culture = culture,
		ResolvedFrom = GetUserCultureTool.ResolvedFromEnvironment
	};

	/// <summary>Creates a failure signal carrying the reason; never surfaces a fallback culture.</summary>
	public static GetUserCultureResponse Failure(string? reason) => new() {
		Success = false,
		ResolvedFrom = GetUserCultureTool.ResolvedFromFailed,
		Reason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason
	};
}
