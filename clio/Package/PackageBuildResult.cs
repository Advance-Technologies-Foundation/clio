using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clio.Package;

/// <summary>
/// One compiler diagnostic reported for a package build.
/// </summary>
/// <param name="ErrorNumber">Compiler diagnostic code, for example <c>CS0246</c>.</param>
/// <param name="ErrorText">Compiler diagnostic text as Creatio returned it.</param>
/// <param name="FileName">Source file the compiler reported, when supplied.</param>
/// <param name="Line">One-based source line, or <see langword="null"/> when Creatio did not supply one.</param>
/// <param name="Column">One-based source column, or <see langword="null"/> when Creatio did not supply one.</param>
/// <param name="IsWarning">Whether the diagnostic is a warning rather than an error.</param>
public sealed record PackageBuildDiagnostic(string ErrorNumber, string ErrorText, string FileName, int? Line,
	int? Column, bool IsWarning) {

	/// <summary>
	/// Renders the diagnostic in the same shape <c>compile-configuration</c> prints its diagnostics in.
	/// </summary>
	/// <returns>A single-line, human-readable diagnostic.</returns>
	public override string ToString() => Format(static text => text, static text => text);

	/// <summary>
	/// Renders the diagnostic, letting the caller decorate the code and the file name.
	/// </summary>
	/// <param name="decorateCode">Applied to <see cref="ErrorNumber"/>, for example to color it.</param>
	/// <param name="decorateFile">Applied to <see cref="FileName"/>, for example to color it.</param>
	/// <returns>A single-line, human-readable diagnostic.</returns>
	/// <remarks>
	/// The position is shown only when Creatio supplied both the line and the column: a missing one used to
	/// be rendered as <c>(0,0)</c>, a location that does not exist.
	/// </remarks>
	public string Format(Func<string, string> decorateCode, Func<string, string> decorateFile) {
		string code = $"({decorateCode(ErrorNumber)})";
		if (string.IsNullOrWhiteSpace(FileName)) {
			return $"{code}: {ErrorText}";
		}
		string position = Line is { } line && Column is { } column ? $" at ({line},{column})" : string.Empty;
		return $"{code} in {decorateFile(FileName)}{position}: {ErrorText}";
	}

}

/// <summary>
/// The verdict Creatio returned in the body of a <c>BuildPackage</c>/<c>RebuildPackage</c> response.
/// </summary>
/// <param name="Success">Whether Creatio reports the build as successful.</param>
/// <param name="BuildResult">Creatio's numeric build result, when supplied.</param>
/// <param name="Diagnostics">Compiler diagnostics, warnings included.</param>
/// <param name="ErrorMessage">The <c>errorInfo.message</c> text, when Creatio supplied one.</param>
/// <param name="ErrorCode">The <c>errorInfo.errorCode</c> text, when Creatio supplied one.</param>
public sealed record PackageBuildResult(bool Success, int? BuildResult, IReadOnlyList<PackageBuildDiagnostic> Diagnostics,
	string ErrorMessage, string ErrorCode) {

	/// <summary>Gets the diagnostics that are errors, not warnings.</summary>
	public IEnumerable<PackageBuildDiagnostic> Errors => Diagnostics.Where(diagnostic => !diagnostic.IsWarning);

}

/// <summary>
/// Parses the two shapes a build verdict reaches clio in: the body of a build response, and the
/// <c>ErrorsWarnings</c> payload of a <c>CompilationHistory</c> row.
/// </summary>
/// <remarks>
/// A static helper rather than an injected service for the same reason as
/// <see cref="Clio.Common.CompilationDiagnostics"/>: it is a pure parse of one string with no collaborators
/// and no state. The two payloads spell the same diagnostic differently - the response uses camelCase and
/// <c>warning</c>, the history row PascalCase and <c>IsWarning</c> - so both spellings are read.
/// </remarks>
internal static class PackageBuildResultParser {

	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

	/// <summary>
	/// Parses the body of a package-build response.
	/// </summary>
	/// <param name="responseBody">The raw response body.</param>
	/// <returns>
	/// The verdict, or <see langword="null"/> when the body is empty, is not JSON, or carries no
	/// <c>success</c> field - that is, when the environment did not report a result. A caller must reject a
	/// non-JSON body first (see <see cref="IsUnrecognizedBody"/>): it is not an absent result.
	/// </returns>
	/// <remarks>
	/// Each field is read on its own, so a field of an unexpected shape costs only that field: a typed parse of
	/// the whole body used to fail as a unit, and a malformed <c>errors</c> or <c>errorInfo</c> then discarded
	/// the <c>success</c> next to it - a failed build read as no result at all.
	/// </remarks>
	internal static PackageBuildResult TryParseResponse(string responseBody) {
		if (string.IsNullOrWhiteSpace(responseBody) || !responseBody.TrimStart().StartsWith('{')) {
			return null;
		}
		try {
			using JsonDocument document = JsonDocument.Parse(responseBody);
			JsonElement root = document.RootElement;
			if (!TryGetProperty(root, "success", out JsonElement successElement)
				|| successElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) {
				return null;
			}
			int? buildResult = TryGetProperty(root, "buildResult", out JsonElement buildResultElement)
				&& buildResultElement.ValueKind == JsonValueKind.Number
				&& buildResultElement.TryGetInt32(out int number)
					? number
					: null;
			IReadOnlyList<PackageBuildDiagnostic> diagnostics =
				TryGetProperty(root, "errors", out JsonElement errorsElement)
				&& errorsElement.ValueKind == JsonValueKind.Array
					? ReadDiagnostics(errorsElement)
					: [];
			string errorMessage = null;
			string errorCode = null;
			if (TryGetProperty(root, "errorInfo", out JsonElement errorInfo)) {
				errorMessage = ReadString(errorInfo, "message");
				errorCode = ReadString(errorInfo, "errorCode");
			}
			return new PackageBuildResult(successElement.GetBoolean(), buildResult, diagnostics, errorMessage,
				errorCode);
		} catch (JsonException) {
			return null;
		}
	}

	// Case-insensitive, as the typed parse this replaced was.
	private static bool TryGetProperty(JsonElement element, string name, out JsonElement value) {
		if (element.ValueKind == JsonValueKind.Object) {
			foreach (JsonProperty property in element.EnumerateObject()) {
				if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) {
					value = property.Value;
					return true;
				}
			}
		}
		value = default;
		return false;
	}

	private static string ReadString(JsonElement element, string name) =>
		TryGetProperty(element, name, out JsonElement value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	private static IReadOnlyList<PackageBuildDiagnostic> ReadDiagnostics(JsonElement errors) {
		try {
			return ToDiagnostics(errors.Deserialize<List<DiagnosticPayload>>(JsonOptions));
		} catch (JsonException) {
			return [];
		}
	}

	/// <summary>
	/// Determines whether a package-build response body is present but is not a JSON object.
	/// </summary>
	/// <param name="responseBody">The raw response body.</param>
	/// <returns><see langword="true"/> for a non-empty body that does not start with <c>{</c>.</returns>
	/// <remarks>
	/// Such a body is an HTML login, session-expired or proxy error page, not a build verdict. Treating it
	/// like an empty body let the default path warn and exit 0 for a build that never started.
	/// </remarks>
	internal static bool IsUnrecognizedBody(string responseBody) =>
		!string.IsNullOrWhiteSpace(responseBody) && !responseBody.TrimStart().StartsWith('{');

	/// <summary>
	/// Parses the <c>ErrorsWarnings</c> payload of a compilation-history row.
	/// </summary>
	/// <param name="errorsWarnings">The raw payload.</param>
	/// <returns>The diagnostics; empty when the payload is empty or cannot be parsed.</returns>
	internal static IReadOnlyList<PackageBuildDiagnostic> ParseHistoryDiagnostics(string errorsWarnings) {
		if (string.IsNullOrWhiteSpace(errorsWarnings)) {
			return [];
		}
		try {
			return ToDiagnostics(JsonSerializer.Deserialize<List<DiagnosticPayload>>(errorsWarnings, JsonOptions));
		} catch (JsonException) {
			return [];
		}
	}

	private static List<PackageBuildDiagnostic> ToDiagnostics(IEnumerable<DiagnosticPayload> payloads) =>
		payloads?.Where(payload => payload is not null)
			.Select(payload => new PackageBuildDiagnostic(payload.ErrorNumber, payload.ErrorText, payload.FileName,
				payload.Line, payload.Column, payload.Warning || payload.IsWarning))
			.ToList() ?? [];

	private sealed class DiagnosticPayload {

		[JsonPropertyName("errorNumber")]
		public string ErrorNumber { get; set; }

		[JsonPropertyName("errorText")]
		public string ErrorText { get; set; }

		[JsonPropertyName("fileName")]
		public string FileName { get; set; }

		// Nullable so a diagnostic without a position does not make the whole verdict unreadable - an
		// unreadable verdict falls back to history alone, which would hide a failure the answer reported.
		[JsonPropertyName("line")]
		public int? Line { get; set; }

		[JsonPropertyName("column")]
		public int? Column { get; set; }

		[JsonPropertyName("warning")]
		public bool Warning { get; set; }

		[JsonPropertyName("isWarning")]
		public bool IsWarning { get; set; }

	}

}
