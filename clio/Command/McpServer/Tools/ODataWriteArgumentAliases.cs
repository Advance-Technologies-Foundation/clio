using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Accepts the unambiguous synonyms an agent reaches for when it names the payload argument of
/// <c>odata-create</c> (<c>rows</c>) or <c>odata-update</c> (<c>data</c>), and executes the call
/// instead of refusing it.
/// </summary>
/// <remarks>
/// Only the payload argument is covered, and only when the call is otherwise unambiguous: the canonical
/// argument, <c>rows-file</c> or a second synonym alongside it is a conflicting request and is rejected,
/// because silently picking one payload for a write is exactly the kind of guess that must not be made.
/// The accepted synonym is reported back in <c>warnings</c> so the caller learns the canonical name.
/// </remarks>
internal static class ODataWriteArgumentAliases {

	/// <summary>Synonyms of <c>odata-create</c> <c>rows</c>.</summary>
	internal static readonly IReadOnlyList<string> CreateRowsAliases = ["payload", "values", "body", "data"];

	/// <summary>Synonyms of <c>odata-update</c> <c>data</c>.</summary>
	internal static readonly IReadOnlyList<string> UpdateDataAliases = ["values", "payload", "body", "fields"];

	/// <summary>Outcome of resolving the synonyms of one payload argument.</summary>
	/// <param name="Value">The payload supplied under a synonym, or null when none was used.</param>
	/// <param name="Remaining">The overflow bag without the consumed synonym.</param>
	/// <param name="Warning">Note naming the canonical argument, or null when no synonym was used.</param>
	/// <param name="Error">Refusal text when the call is ambiguous; otherwise null.</param>
	internal sealed record Resolution(
		JsonElement? Value,
		Dictionary<string, JsonElement>? Remaining,
		string? Warning,
		string? Error);

	/// <summary>Resolves synonyms of <paramref name="canonical"/> found in <paramref name="extensionData"/>.</summary>
	/// <param name="extensionData">The args record's overflow bag.</param>
	/// <param name="canonical">The canonical argument name (rows or data).</param>
	/// <param name="aliases">Accepted synonyms for it.</param>
	/// <param name="canonicalSupplied">Whether the canonical argument was supplied.</param>
	/// <param name="rowsFileSupplied">Whether the rows-file alternative was supplied.</param>
	internal static Resolution Resolve(
		Dictionary<string, JsonElement>? extensionData,
		string canonical,
		IReadOnlyList<string> aliases,
		bool canonicalSupplied,
		bool rowsFileSupplied) {
		if (extensionData is null) {
			return new Resolution(null, extensionData, null, null);
		}
		List<string> used = extensionData.Keys.Where(key => aliases.Contains(key, StringComparer.OrdinalIgnoreCase)).ToList();
		if (used.Count == 0) {
			return new Resolution(null, extensionData, null, null);
		}
		List<string> shown = used.Select(key => $"'{McpToolArgumentSupport.DescribeCallerKey(key)}'").ToList();
		if (canonicalSupplied || rowsFileSupplied || used.Count > 1) {
			string? other = null;
			if (canonicalSupplied) {
				other = $"'{canonical}'";
			} else if (rowsFileSupplied) {
				other = "'rows-file'";
			}
			string together = string.Join(" and ", other is null ? shown : shown.Prepend(other));
			return new Resolution(null, extensionData, null,
				$"Ambiguous payload: {together} were all supplied, and they name the same thing. "
				+ $"Send the payload once, as '{canonical}'. Nothing was written.");
		}
		string alias = used[0];
		Dictionary<string, JsonElement> remaining = extensionData
			.Where(pair => pair.Key != alias)
			.ToDictionary(pair => pair.Key, pair => pair.Value);
		return new Resolution(
			extensionData[alias].Clone(),
			remaining.Count == 0 ? null : remaining,
			$"Argument '{McpToolArgumentSupport.DescribeCallerKey(alias)}' was accepted as '{canonical}'; "
			+ $"use '{canonical}' in future calls.",
			null);
	}
}
