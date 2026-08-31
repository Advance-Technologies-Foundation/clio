using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clio.Common;

/// <summary>
/// Parses Creatio compilation-result payloads.
/// </summary>
public interface ICompilationLogParser {

	/// <summary>
	/// Deserializes a Creatio compilation-result payload.
	/// </summary>
	/// <param name="jsonInput">The JSON payload returned by Creatio.</param>
	/// <returns>The typed compilation result.</returns>
	CreatioCompilationLogResponse DeserializeCreatioCompilationLog(string jsonInput);

	/// <summary>
	/// Parses the Creatio compilation log from a JSON input string.
	/// </summary>
	/// <param name="jsonInput">The JSON input string containing the compilation log.</param>
	/// <returns>A formatted string with the compilation log details.</returns>
	string ParseCreatioCompilationLog(string jsonInput);

}

/// <summary>
/// Parses and formats Creatio compilation-result payloads.
/// </summary>
public class CompilationLogParser : ICompilationLogParser {

	private sealed class CompilationLogPayload {

		private CreatioCompilationError[] _errors;

		[JsonPropertyName("errors")]
		public CreatioCompilationError[] Errors {
			get => _errors;
			set {
				_errors = value;
				ErrorsPresent = true;
			}
		}

		[JsonIgnore]
		public bool ErrorsPresent { get; private set; }

		[JsonPropertyName("buildResult")]
		public int? BuildResult { get; set; }

		[JsonPropertyName("success")]
		public bool? Success { get; set; }
	}

	#region Methods: Public

	/// <inheritdoc />
	public CreatioCompilationLogResponse DeserializeCreatioCompilationLog(string jsonInput){
		CompilationLogPayload payload = JsonSerializer.Deserialize<CompilationLogPayload>(jsonInput)
			?? throw new JsonException("Creatio returned an empty compilation-result payload.");
		if (!payload.ErrorsPresent || !payload.BuildResult.HasValue || !payload.Success.HasValue) {
			throw new JsonException(
				"Creatio returned an unexpected compilation-result payload. Expected errors, buildResult, and success fields.");
		}
		return new CreatioCompilationLogResponse(
			payload.Errors ?? Array.Empty<CreatioCompilationError>(),
			payload.BuildResult.Value,
			payload.Success.Value);
	}

	/// <inheritdoc />
	public string ParseCreatioCompilationLog(string jsonInput){
		CreatioCompilationLogResponse response = DeserializeCreatioCompilationLog(jsonInput);

		List<string> diagnosticMessages = response.errors
			.Select(diagnostic =>
				$"{diagnostic.fileName}({diagnostic.line},{diagnostic.column}): "
				+ $"{(diagnostic.warning ? "Warning" : "Error")} {diagnostic.errorNumber} : {diagnostic.errorText}")
			.ToList();
		int errorCount = response.errors.Count(diagnostic => !diagnostic.warning);
		int warningCount = response.errors.Count(diagnostic => diagnostic.warning);
		string resultMessage
			= $"------- Finished building project: Succeeded: {response.success}. Errors: {errorCount}. Warnings: {warningCount}.";
		return (string.Join("\r\n", diagnosticMessages) + "\r\n" + resultMessage).Trim();
	}

	#endregion

}

/// <summary>
/// Creatio's last compilation-result payload.
/// </summary>
/// <param name="errors">Compilation diagnostics returned by Creatio.</param>
/// <param name="buildResult">Creatio's numeric build-result value.</param>
/// <param name="success">Whether Creatio reports the compilation as successful.</param>
public record CreatioCompilationLogResponse(CreatioCompilationError[] errors,
	int buildResult,
	bool success);

/// <summary>
/// A diagnostic in Creatio's compilation-result payload.
/// </summary>
/// <param name="line">One-based source line when supplied by the compiler.</param>
/// <param name="column">One-based source column when supplied by the compiler.</param>
/// <param name="errorNumber">Compiler diagnostic code.</param>
/// <param name="errorText">Compiler diagnostic description.</param>
/// <param name="warning">Whether the diagnostic is a warning rather than an error.</param>
/// <param name="fileName">Source file reported by the compiler.</param>
public record CreatioCompilationError(int line,
	int column,
	string errorNumber,
	string errorText,
	bool warning,
	string fileName);
