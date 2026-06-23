using System.Text.Json;
using CommandLine;

namespace Clio.Command;

#region Enum: IdentityOutputFormat

/// <summary>
///     Output format shared by the identity-assertion command family.
/// </summary>
public enum IdentityOutputFormat
{

	/// <summary>
	///     Plain-text output: prints the single most useful value (e.g. the assertion token).
	/// </summary>
	Text = 0,

	/// <summary>
	///     JSON output: prints the full structured payload returned by Creatio.
	/// </summary>
	Json = 1

}

#endregion

#region Class: IdentityCommandOptions

/// <summary>
///     Base options for identity-assertion / identity-service commands.
///     Adds a shared <c>--format</c> switch so every command can return either the raw JSON
///     payload (<see cref="IdentityOutputFormat.Json" />) or a plain-text value
///     (<see cref="IdentityOutputFormat.Text" />).
/// </summary>
public abstract class IdentityCommandOptions : RemoteCommandOptions
{

	private const string TextFormat = "text";
	private const string JsonFormat = "json";

	private string _formatRaw = TextFormat;

	/// <summary>
	///     Raw <c>--format</c> CLI value. Bound as a string (not an enum) because the command-line
	///     parser does not support enum-typed options; the parsed value is exposed via
	///     <see cref="Format" />.
	/// </summary>
	[Option("format", Required = false, Default = TextFormat,
		HelpText = "Output format: text (plain value, default) or json (full payload)")]
	public string FormatRaw {
		get => _formatRaw;
		set => _formatRaw = value;
	}

	/// <summary>
	///     Controls how the command renders the server response: <c>text</c> (default) prints the
	///     primary value as plain text; <c>json</c> prints the full structured payload.
	/// </summary>
	public IdentityOutputFormat Format {
		get => string.Equals(_formatRaw, JsonFormat, System.StringComparison.OrdinalIgnoreCase)
			? IdentityOutputFormat.Json
			: IdentityOutputFormat.Text;
		set => _formatRaw = value == IdentityOutputFormat.Json ? JsonFormat : TextFormat;
	}

}

#endregion

#region Class: IdentityOutput

/// <summary>
///     Helpers for rendering identity command responses in a stable, scriptable way.
/// </summary>
internal static class IdentityOutput
{

	private static readonly JsonSerializerOptions PrettyOptions = new() { WriteIndented = true };

	/// <summary>
	///     Shared deserializer options. The Creatio server differs by runtime: .NET Framework Web API
	///     serializes DTO properties in PascalCase, .NET Core in camelCase. Case-insensitive matching
	///     lets a single contract bind both.
	/// </summary>
	public static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

	/// <summary>
	///     Re-serializes a raw JSON response with indentation. Falls back to the original
	///     string when the payload is not valid JSON.
	/// </summary>
	public static string Pretty(string rawJson) {
		if (string.IsNullOrWhiteSpace(rawJson)) {
			return string.Empty;
		}
		try {
			using JsonDocument document = JsonDocument.Parse(rawJson);
			return JsonSerializer.Serialize(document.RootElement, PrettyOptions);
		}
		catch (JsonException) {
			return rawJson;
		}
	}

	/// <summary>
	///     Detects a Creatio <c>ErrorInfo</c> payload (<c>{ "error": "...", "error_description": "..." }</c>)
	///     in the response. The match is case-insensitive on the property name so it works against both
	///     the .NET Framework (PascalCase) and .NET Core (camelCase) serializers.
	/// </summary>
	/// <returns><c>true</c> when the response root is a JSON object carrying an <c>error</c> string.</returns>
	public static bool TryParseError(string rawJson, out string error, out string description) {
		error = null;
		description = null;
		if (string.IsNullOrWhiteSpace(rawJson)) {
			return false;
		}
		try {
			using JsonDocument document = JsonDocument.Parse(rawJson);
			if (document.RootElement.ValueKind != JsonValueKind.Object) {
				return false;
			}
			if (!TryGetStringProperty(document.RootElement, "error", out error)) {
				return false;
			}
			TryGetStringProperty(document.RootElement, "error_description", out description);
			return true;
		}
		catch (JsonException) {
			return false;
		}
	}

	private static bool TryGetStringProperty(JsonElement element, string name, out string value) {
		foreach (JsonProperty property in element.EnumerateObject()) {
			if (string.Equals(property.Name, name, System.StringComparison.OrdinalIgnoreCase)
				&& property.Value.ValueKind == JsonValueKind.String) {
				value = property.Value.GetString();
				return true;
			}
		}
		value = null;
		return false;
	}

}

#endregion
