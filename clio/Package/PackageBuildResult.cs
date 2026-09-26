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
/// <param name="Line">One-based source line, when supplied.</param>
/// <param name="Column">One-based source column, when supplied.</param>
/// <param name="IsWarning">Whether the diagnostic is a warning rather than an error.</param>
public sealed record PackageBuildDiagnostic(string ErrorNumber, string ErrorText, string FileName, int Line,
	int Column, bool IsWarning) {

	/// <summary>
	/// Renders the diagnostic in the same shape <c>compile-configuration</c> prints its diagnostics in.
	/// </summary>
	/// <returns>A single-line, human-readable diagnostic.</returns>
	public override string ToString() =>
		string.IsNullOrWhiteSpace(FileName)
			? $"({ErrorNumber}): {ErrorText}"
			: $"({ErrorNumber}) in {FileName} at ({Line},{Column}): {ErrorText}";

}

/// <summary>
/// The verdict Creatio returned in the body of a <c>BuildPackage</c>/<c>RebuildPackage</c> response.
/// </summary>
/// <param name="Success">Whether Creatio reports the build as successful.</param>
/// <param name="BuildResult">Creatio's numeric build result, when supplied.</param>
/// <param name="Diagnostics">Compiler diagnostics, warnings included.</param>
/// <param name="ErrorMessage">The <c>errorInfo.message</c> text, when Creatio supplied one.</param>
public sealed record PackageBuildResult(bool Success, int? BuildResult, IReadOnlyList<PackageBuildDiagnostic> Diagnostics,
	string ErrorMessage) {

	/// <summary>Gets the diagnostics that are errors, not warnings.</summary>
	public IEnumerable<PackageBuildDiagnostic> Errors => Diagnostics.Where(diagnostic => !diagnostic.IsWarning);

}

/// <summary>
/// Parses the two shapes a package-build verdict reaches clio in: the body of the package-build response,
/// and the <c>ErrorsWarnings</c> payload of a <c>CompilationHistory</c> row.
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
	/// The verdict, or <see langword="null"/> when the body is empty, is not JSON (an HTML login or error page),
	/// or carries no <c>success</c> field - that is, when the environment did not report a result.
	/// </returns>
	internal static PackageBuildResult TryParseResponse(string responseBody) {
		if (string.IsNullOrWhiteSpace(responseBody) || !responseBody.TrimStart().StartsWith('{')) {
			return null;
		}
		try {
			ResponsePayload payload = JsonSerializer.Deserialize<ResponsePayload>(responseBody, JsonOptions);
			if (payload?.Success is not { } success) {
				return null;
			}
			return new PackageBuildResult(success, payload.BuildResult, ToDiagnostics(payload.Errors),
				payload.ErrorInfo?.Message);
		} catch (JsonException) {
			return null;
		}
	}

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

	private sealed class ResponsePayload {

		[JsonPropertyName("success")]
		public bool? Success { get; set; }

		[JsonPropertyName("buildResult")]
		public int? BuildResult { get; set; }

		[JsonPropertyName("errors")]
		public List<DiagnosticPayload> Errors { get; set; }

		[JsonPropertyName("errorInfo")]
		public ErrorInfoPayload ErrorInfo { get; set; }

	}

	private sealed class ErrorInfoPayload {

		[JsonPropertyName("message")]
		public string Message { get; set; }

	}

	private sealed class DiagnosticPayload {

		[JsonPropertyName("errorNumber")]
		public string ErrorNumber { get; set; }

		[JsonPropertyName("errorText")]
		public string ErrorText { get; set; }

		[JsonPropertyName("fileName")]
		public string FileName { get; set; }

		[JsonPropertyName("line")]
		public int Line { get; set; }

		[JsonPropertyName("column")]
		public int Column { get; set; }

		[JsonPropertyName("warning")]
		public bool Warning { get; set; }

		[JsonPropertyName("isWarning")]
		public bool IsWarning { get; set; }

	}

}
