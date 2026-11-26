using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// One parsed <c>expand</c> entry of <c>odata-read</c> / <c>odata-read-to-file</c>: a navigation path with an
/// optional nested <c>$select</c> list, as in <c>Account</c> or <c>Account($select=Id,Name)</c>.
/// </summary>
/// <remarks>
/// The entry is parsed once and then used for everything downstream - the request URL, the names echoed on an
/// invalid-query rejection and the metadata check - so the query string is always rebuilt from validated
/// identifiers (<see cref="ToQueryText"/>) and never carries the caller's raw text. Only a nested
/// <c>$select</c> of plain identifiers is accepted; every other nested option is refused (ENG-107610).
/// </remarks>
/// <param name="NavigationPath">The navigation path, an OData member path such as <c>Account</c> or <c>Account/Owner</c>.</param>
/// <param name="Select">The nested <c>$select</c> names, or <see langword="null"/> when the entry has none.</param>
internal sealed record ODataExpandEntry(string NavigationPath, IReadOnlyList<string>? Select) {

	private const string SelectOption = "$select=";

	/// <summary>The accepted nested form, quoted in every refusal so the caller can correct the entry.</summary>
	internal const string AcceptedForm = "Nav($select=A,B)";

	/// <summary>
	/// The nested shapes that are refused, written once for the refusal message, the argument description and
	/// both tool contracts so they cannot drift apart.
	/// </summary>
	internal const string RejectedNestedOptions =
		"$expand, $filter, $orderby, $top, $skip, $count, ';' option lists, '*', an empty select, "
		+ "and text after the closing parenthesis";

	/// <summary>
	/// The canonical OData text of the entry: <c>Account</c>, or <c>Account($select=Id,Name)</c>.
	/// </summary>
	internal string ToQueryText() =>
		Select is { Count: > 0 } ? $"{NavigationPath}({SelectOption}{string.Join(",", Select)})" : NavigationPath;

	/// <summary>
	/// Parses one expand entry. Accepts a member path, optionally followed directly by
	/// <c>($select=A,B)</c> whose names are plain identifiers (letters, digits, underscore).
	/// </summary>
	/// <param name="text">The entry as the caller sent it.</param>
	/// <param name="entry">The parsed entry, or <see langword="null"/> when the text is not accepted.</param>
	/// <returns><see langword="false"/> when the text is not an accepted expand entry.</returns>
	internal static bool TryParse(string? text, [NotNullWhen(true)] out ODataExpandEntry? entry) {
		entry = null;
		if (string.IsNullOrWhiteSpace(text)) {
			return false;
		}
		string trimmed = text.Trim();
		int open = trimmed.IndexOf('(', StringComparison.Ordinal);
		if (open < 0) {
			if (!ODataKeyFormatter.IsValidMemberPath(trimmed)) {
				return false;
			}
			entry = new ODataExpandEntry(trimmed, null);
			return true;
		}
		string path = trimmed[..open];
		if (!ODataKeyFormatter.IsValidMemberPath(path) || trimmed[^1] != ')') {
			return false;
		}
		string options = trimmed[(open + 1)..^1];
		if (!options.StartsWith(SelectOption, StringComparison.OrdinalIgnoreCase)) {
			return false;
		}
		string[] names = options[SelectOption.Length..].Split(',', StringSplitOptions.TrimEntries);
		if (!names.All(ODataKeyFormatter.IsSimpleIdentifier)) {
			return false;
		}
		entry = new ODataExpandEntry(path, names);
		return true;
	}

	/// <summary>
	/// Splits the comma-separated string form of <c>expand</c> on top-level commas only, so the commas of a
	/// nested <c>$select</c> list stay with their entry: <c>"Account($select=Id,Name),Owner"</c> is two entries.
	/// Blank entries are dropped; unbalanced parentheses are left for <see cref="TryParse"/> to refuse.
	/// </summary>
	/// <param name="raw">The comma-separated value.</param>
	/// <returns>The trimmed, non-blank entries in order.</returns>
	internal static IReadOnlyList<string> SplitTopLevel(string raw) {
		List<string> entries = [];
		int depth = 0;
		int start = 0;
		for (int index = 0; index < raw.Length; index++) {
			switch (raw[index]) {
				case '(':
					depth++;
					break;
				case ')':
					depth--;
					break;
				case ',' when depth == 0:
					AddEntry(entries, raw[start..index]);
					start = index + 1;
					break;
			}
		}
		AddEntry(entries, raw[start..]);
		return entries;
	}

	private static void AddEntry(List<string> entries, string candidate) {
		if (!string.IsNullOrWhiteSpace(candidate)) {
			entries.Add(candidate.Trim());
		}
	}
}
