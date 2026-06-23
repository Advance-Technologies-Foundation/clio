using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for fast installed-application discovery — returns matching applications
/// together with their sections in a single call.
/// </summary>
public sealed class FindAppTool(
	FindAppCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<FindAppOptions>(command, logger, commandResolver) {

	/// <summary>
	/// Stable MCP tool name for finding installed applications and their sections.
	/// </summary>
	internal const string FindAppToolName = "find-app";

	/// <summary>
	/// Finds installed applications (and their sections) by name, code, or substring pattern.
	/// </summary>
	[McpServerTool(Name = FindAppToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description(
		"Finds installed Creatio applications AND their sections in one call, matching a case-insensitive substring " +
		"against application name/code/description and section captions/codes — use it to resolve an imprecise app name to its real code. " +
		"Omit both filters to enumerate every application with its sections. Returned codes feed get-app-info, list-app-sections, create-app-section.")]
	public FindAppResponse FindApp(
		[Description("environment-name (required); search-pattern (optional substring), code (optional exact app code). Omit both to list all apps with sections.")]
		[Required]
		FindAppArgs args) {
		try {
			// An agent naturally guesses `name`/`query`/`filter` for the substring and `app-code` for the
			// exact code (the description talks about "application name/code/description"). Those land in the
			// [JsonExtensionData] overflow bag instead of binding, so recover the known synonyms into the real
			// args first. After recovery, a genuinely-unknown overflow key (neither a real arg nor a known
			// alias) is reported with a helpful "Rename/Unknown args … Valid: …" hint instead of being
			// silently dropped — matching get-component-info / get-tool-contract.
			string? searchPattern = RecoverValue(args.SearchPattern, args.ExtensionData, SearchPatternAliases);
			string? code = RecoverValue(args.Code, args.ExtensionData, CodeAliases);

			string? aliasError = McpToolArgumentSupport.BuildLegacyAliasError(
				CollectUnrecoveredOverflow(args.ExtensionData),
				EmptyAliases,
				".",
				ValidArgsHint);
			if (!string.IsNullOrWhiteSpace(aliasError)) {
				return new FindAppResponse(false, null, aliasError);
			}

			FindAppOptions options = new() {
				Environment = args.EnvironmentName,
				SearchPattern = searchPattern,
				Code = code
			};
			FindAppCommand resolvedCommand = ResolveCommand<FindAppCommand>(options);
			IReadOnlyList<AppSearchResult> results = resolvedCommand.FindApplications(options);
			return new FindAppResponse(true, results, null);
		} catch (Exception exception) {
			return new FindAppResponse(false, null, SensitiveErrorTextRedactor.Redact(exception.Message));
		}
	}

	/// <summary>
	/// Sentence appended after the unknown-args list, naming the canonical argument shape so an agent that
	/// guessed an unsupported key knows the correct names. Lists <c>search-pattern</c> first so the most
	/// common miss (a substring filter sent as <c>name</c>/<c>query</c>/<c>filter</c>) points at it.
	/// </summary>
	private const string ValidArgsHint =
		"Valid: environment-name, search-pattern, code. Use search-pattern for a substring filter.";

	/// <summary>
	/// Case-insensitive synonyms an LLM is likely to emit for the optional substring filter, recovered into
	/// <see cref="FindAppArgs.SearchPattern"/> when it was not bound. Ordinal-ignore-case so both kebab,
	/// camel, and snake spellings collapse to the same key.
	/// </summary>
	private static readonly HashSet<string> SearchPatternAliases = new(StringComparer.OrdinalIgnoreCase) {
		"name",
		"query",
		"pattern",
		"filter",
		"search",
		"app-name",
		"appName",
		"searchPattern",
		"search_pattern"
	};

	/// <summary>
	/// Case-insensitive synonyms an LLM is likely to emit for the exact application code, recovered into
	/// <see cref="FindAppArgs.Code"/> when it was not bound.
	/// </summary>
	private static readonly HashSet<string> CodeAliases = new(StringComparer.OrdinalIgnoreCase) {
		"app-code",
		"application-code",
		"appCode",
		"applicationCode"
	};

	/// <summary>
	/// Empty alias map for <see cref="McpToolArgumentSupport.BuildLegacyAliasError"/>: every recognized alias
	/// is RECOVERED into a real arg above, so the remaining overflow holds only genuinely-unknown keys, which
	/// the helper lists under "Unknown args" with the <see cref="ValidArgsHint"/>.
	/// </summary>
	private static readonly Dictionary<string, string> EmptyAliases = new(StringComparer.Ordinal);

	/// <summary>
	/// Returns <paramref name="bound"/> when it carries a non-blank value; otherwise recovers the first
	/// matching alias key from the overflow bag (a non-blank string value), trimmed. Returns <c>null</c> when
	/// neither the bound value nor any alias is present.
	/// </summary>
	private static string? RecoverValue(
		string? bound,
		IReadOnlyDictionary<string, JsonElement>? overflow,
		HashSet<string> aliases) {
		if (!string.IsNullOrWhiteSpace(bound)) {
			return bound;
		}
		if (overflow is null) {
			return null;
		}
		foreach (KeyValuePair<string, JsonElement> entry in overflow) {
			if (!aliases.Contains(entry.Key) || entry.Value.ValueKind != JsonValueKind.String) {
				continue;
			}
			string? value = entry.Value.GetString();
			if (!string.IsNullOrWhiteSpace(value)) {
				return value.Trim();
			}
		}
		return null;
	}

	/// <summary>
	/// Filters the overflow bag down to the keys that are NEITHER a known <see cref="SearchPatternAliases"/>
	/// nor <see cref="CodeAliases"/> synonym, i.e. the genuinely-unknown keys that must surface as an error.
	/// Recognized aliases are dropped because they were already recovered into the real args. Returns
	/// <c>null</c> when the overflow is empty so <see cref="McpToolArgumentSupport.BuildLegacyAliasError"/>
	/// passes a clean call straight through.
	/// </summary>
	private static IReadOnlyDictionary<string, JsonElement>? CollectUnrecoveredOverflow(
		IReadOnlyDictionary<string, JsonElement>? overflow) {
		if (overflow is null || overflow.Count == 0) {
			return null;
		}
		Dictionary<string, JsonElement> unknown = overflow
			.Where(entry => !SearchPatternAliases.Contains(entry.Key) && !CodeAliases.Contains(entry.Key))
			.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
		return unknown.Count == 0 ? null : unknown;
	}
}

/// <summary>
/// Arguments for the <c>find-app</c> MCP tool.
/// </summary>
public sealed record FindAppArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("search-pattern")]
	[property: Description("Case-insensitive substring matched against application name, code, description, and section captions/codes. Omit to return all applications.")]
	string? SearchPattern = null,

	[property: JsonPropertyName("code")]
	[property: Description("Exact installed application code to match. Optional.")]
	string? Code = null) {

	/// <summary>
	/// Captures top-level keys the SDK could not bind to a declared argument (for example an agent's guessed
	/// <c>name</c>/<c>query</c>/<c>filter</c> substring or <c>app-code</c>). The tool recovers known synonyms
	/// into <see cref="SearchPattern"/>/<see cref="Code"/> and reports any remaining unknown key, instead of
	/// silently dropping it and returning an unfiltered list.
	/// </summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

/// <summary>
/// Structured envelope returned by the <c>find-app</c> MCP tool.
/// </summary>
public sealed record FindAppResponse(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("applications")] IReadOnlyList<AppSearchResult>? Applications = null,
	[property: JsonPropertyName("error")] string? Error = null);
