using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Shared helpers for MCP tools that bind a single <c>args</c> record with kebab-case fields.
/// Centralizes two pieces of logic several tools used to copy verbatim — legacy-alias rejection
/// over the <c>[JsonExtensionData]</c> overflow bag, and the edit-distance ranking used for
/// "did you mean" suggestions — so the behavior (and the SonarCloud duplication budget) stays in
/// one place. Each caller keeps its own canonical alias map and wording via the parameters.
/// </summary>
internal static class McpToolArgumentSupport {
	/// <summary>
	/// The camelCase / snake_case mis-spellings of <c>environment-name</c> an LLM tends to emit, each mapped to
	/// the canonical kebab-case name so a wrong spelling is rejected with a rename hint instead of silently
	/// binding to nothing. Shared by every environment-scoped tool so the pair is defined once; a tool with extra
	/// fields seeds its own map from this and adds them.
	/// </summary>
	public static readonly IReadOnlyDictionary<string, string> EnvironmentNameAliases =
		new Dictionary<string, string>(StringComparer.Ordinal) {
			["environmentName"] = "environment-name",
			["environment_name"] = "environment-name"
		};

	/// <summary>
	/// Builds a single actionable rename hint from the fields an MCP arg record could not bind
	/// (captured in its <c>[JsonExtensionData]</c> bag). Known camelCase/snake_case spellings are
	/// reported as <c>'alias' -&gt; 'canonical'</c> renames; everything else is listed as unknown
	/// with the caller-supplied valid-field hint. Returns <c>null</c> when there is nothing to
	/// flag (no overflow), so a clean call passes straight through.
	/// </summary>
	/// <param name="extensionData">The arg record's overflow bag (unbound JSON fields).</param>
	/// <param name="aliases">Canonical map of known mis-spelling -&gt; canonical kebab-case name.</param>
	/// <param name="renameSuffix">Trailing text appended after the rename list (e.g. <c>"."</c> or a type reminder); use <c>""</c> for none.</param>
	/// <param name="unknownHint">Sentence appended after the unknown-args list, e.g. <c>"Valid: a, b, c."</c>.</param>
	public static string? BuildLegacyAliasError(
		IReadOnlyDictionary<string, JsonElement>? extensionData,
		IReadOnlyDictionary<string, string> aliases,
		string renameSuffix,
		string unknownHint) {
		if (extensionData is null || extensionData.Count == 0) {
			return null;
		}
		List<string> mapped = [];
		List<string> unknown = [];
		foreach (string key in extensionData.Keys) {
			if (aliases.TryGetValue(key, out string? canonical)) {
				mapped.Add($"'{key}' -> '{canonical}'");
			} else {
				unknown.Add($"'{key}'");
			}
		}
		List<string> parts = [];
		if (mapped.Count > 0) {
			parts.Add("Rename: " + string.Join(", ", mapped) + renameSuffix);
		}
		if (unknown.Count > 0) {
			parts.Add("Unknown args: " + string.Join(", ", unknown) + ". " + unknownHint);
		}
		return parts.Count > 0 ? string.Join(" ", parts) : null;
	}

	/// <summary>
	/// Case-insensitive Levenshtein edit distance between two identifiers. Drives the "closest
	/// match" ranking for unknown tool names and unknown component types. Equal strings score 0.
	/// </summary>
	public static int LevenshteinDistance(string? source, string? target) {
		string left = (source ?? string.Empty).ToLowerInvariant();
		string right = (target ?? string.Empty).ToLowerInvariant();
		if (left.Length == 0) {
			return right.Length;
		}
		if (right.Length == 0) {
			return left.Length;
		}
		int[,] matrix = new int[left.Length + 1, right.Length + 1];
		for (int i = 0; i <= left.Length; i++) {
			matrix[i, 0] = i;
		}
		for (int j = 0; j <= right.Length; j++) {
			matrix[0, j] = j;
		}
		for (int i = 1; i <= left.Length; i++) {
			for (int j = 1; j <= right.Length; j++) {
				int cost = left[i - 1] == right[j - 1] ? 0 : 1;
				matrix[i, j] = Math.Min(
					Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
					matrix[i - 1, j - 1] + cost);
			}
		}
		return matrix[left.Length, right.Length];
	}
}
