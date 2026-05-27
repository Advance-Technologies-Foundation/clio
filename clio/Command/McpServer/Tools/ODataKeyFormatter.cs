using System;
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

	/// <summary>True when <paramref name="s"/> is a canonical GUID.</summary>
	public static bool IsGuid(string s) => !string.IsNullOrEmpty(s) && GuidPattern.IsMatch(s);

	/// <summary>
	/// True when the last segment of <paramref name="field"/> ends with "Id"
	/// (e.g. <c>AccountId</c> or the navigation path <c>Account/Id</c>).
	/// </summary>
	public static bool IsIdish(string field) {
		int slash = field.LastIndexOf('/');
		string lastSegment = slash >= 0 ? field[(slash + 1)..] : field;
		return lastSegment.EndsWith("Id", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Builds the OData literal for a filter value. GUIDs in Id-suffixed fields are emitted
	/// unquoted; strings are single-quoted (with <c>'</c> escaped); numbers/booleans/null are raw.
	/// </summary>
	public static string LiteralFor(string field, JsonElement value) =>
		value.ValueKind switch {
			JsonValueKind.Null => "null",
			JsonValueKind.Number => value.GetRawText(),
			JsonValueKind.True => "true",
			JsonValueKind.False => "false",
			JsonValueKind.String => IsGuid(value.GetString()!) && IsIdish(field)
				? value.GetString()!
				: $"'{value.GetString()!.Replace("'", "''")}'",
			_ => $"'{value.GetRawText().Replace("'", "''")}'",
		};

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

	private static bool IsNumeric(string s) =>
		long.TryParse(s, out _) || double.TryParse(s, System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture, out _);
}
