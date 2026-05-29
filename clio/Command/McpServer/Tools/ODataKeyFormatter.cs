using System;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Shared OData v4 literal and entity-key formatting used by the odata-* MCP tools.
/// Centralizes GUID detection, Id-field heuristics, and value quoting so that read and
/// write tools build identical OData syntax.
/// </summary>
internal static class ODataKeyFormatter {
	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

	private static readonly Regex GuidPattern = new(
		@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$",
		RegexOptions.Compiled,
		RegexTimeout);

	private static readonly Regex EntityNamePattern = new(
		@"^[A-Za-z_][A-Za-z0-9_]*$",
		RegexOptions.Compiled,
		RegexTimeout);

	/// <summary>True when <paramref name="s"/> is a canonical GUID.</summary>
	public static bool IsGuid(string s) => !string.IsNullOrEmpty(s) && GuidPattern.IsMatch(s);

	/// <summary>
	/// True when <paramref name="entity"/> is a simple OData entity-set identifier
	/// (letters, digits, underscore; not starting with a digit). Rejects names that could
	/// inject query options or extra path segments into the OData URL.
	/// </summary>
	public static bool IsValidEntityName(string entity) =>
		!string.IsNullOrWhiteSpace(entity) && EntityNamePattern.IsMatch(entity.Trim());

	/// <summary>
	/// True when the last segment of <paramref name="field"/> is a foreign-key field named
	/// with the conventional capital-I "Id" suffix on a word boundary — <c>Id</c>, <c>AccountId</c>,
	/// or the navigation path <c>Account/Id</c> — but not plain words that merely end in "id"
	/// (e.g. <c>Paid</c>, <c>Void</c>, <c>Grid</c>).
	/// </summary>
	public static bool IsIdish(string field) {
		int slash = field.LastIndexOf('/');
		string lastSegment = slash >= 0 ? field[(slash + 1)..] : field;
		if (!lastSegment.EndsWith("Id", StringComparison.Ordinal)) {
			return false;
		}
		if (lastSegment.Length == 2) {
			return true;
		}
		char preceding = lastSegment[^3];
		return char.IsLower(preceding) || char.IsDigit(preceding);
	}

	/// <summary>
	/// Builds the OData literal for a filter value. GUIDs in Id-suffixed fields are emitted
	/// unquoted; strings are single-quoted (with <c>'</c> escaped); numbers/booleans/null are raw.
	/// </summary>
	public static string LiteralFor(string field, JsonElement value) {
		switch (value.ValueKind) {
			case JsonValueKind.Null:
				return "null";
			case JsonValueKind.Number:
				return value.GetRawText();
			case JsonValueKind.True:
				return "true";
			case JsonValueKind.False:
				return "false";
			case JsonValueKind.String:
				string s = value.GetString()!;
				return IsGuid(s) && IsIdish(field) ? s : $"'{s.Replace("'", "''")}'";
			default:
				return $"'{value.GetRawText().Replace("'", "''")}'";
		}
	}

	/// <summary>
	/// Formats an entity key for an addressed OData segment <c>{Entity}({key})</c>.
	/// GUIDs and numbers pass through unquoted; everything else is single-quoted with
	/// <c>'</c> escaped, matching Creatio OData v4 key syntax.
	/// </summary>
	public static string FormatEntityKey(string id) {
		if (string.IsNullOrWhiteSpace(id)) {
			throw new ArgumentException("Entity key must be a non-empty value.", nameof(id));
		}
		string trimmed = id.Trim();
		if (IsGuid(trimmed) || IsNumeric(trimmed)) {
			return trimmed;
		}
		return $"'{trimmed.Replace("'", "''")}'";
	}

	/// <summary>OData path for an entity-set collection, e.g. <c>odata/Contact</c>.</summary>
	public static string CollectionPath(string entity) => $"odata/{entity.Trim()}";

	/// <summary>OData path for an addressed record, e.g. <c>odata/Contact(&lt;key&gt;)</c>.</summary>
	public static string KeyPath(string entity, string id) =>
		$"odata/{entity.Trim()}({FormatEntityKey(id.Trim())})";

	private static bool IsNumeric(string s) =>
		long.TryParse(s, out _) || double.TryParse(s, NumberStyles.Float,
			CultureInfo.InvariantCulture, out _);
}
