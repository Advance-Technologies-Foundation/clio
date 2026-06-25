using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;
using Clio.Common;

namespace Clio.Command.McpServer.Tools;

public record CommandExecutionResult(

	[property: JsonPropertyName("exit-code"), Description("Command execution exit code")]
	int ExitCode,

	[property: JsonPropertyName("execution-log-messages"), Description("Command execution output")]
	IEnumerable<LogMessage> Output,

	[property: JsonPropertyName("log-file-path"), Description("Optional path to the generated database operation log file")]
	string LogFilePath = null,

	[property: JsonPropertyName("dataforge")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("Optional Data Forge enrichment diagnostics returned when DataForge was queried during the operation.")]
	ApplicationDataForgeResult DataForge = null,

	[property: JsonPropertyName("correlation-id")]
	[property: Description("Unique identifier for tracing this tool execution across logs and diagnostics.")]
	string CorrelationId = null,

	[property: JsonPropertyName("note")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("Optional deterministic post-operation hint, e.g. that compile-creatio is not required after this tool.")]
	string Note = null
) {

	public const string CompileNotRequiredNote = "compile-creatio not required";

	// MCP exit-code contract (ENG-91825):
	//   • exit code  1  → EXPECTED, caller-actionable failure: input/argument validation, a missing
	//                     environment, or a refused precondition (e.g. a required package is absent).
	//                     Use FromValidationError(...) or FromResolverError(...).
	//   • exit code -1  → UNEXPECTED runtime failure: an exception the caller cannot have anticipated
	//                     (DI/bootstrap/wiring bugs, a failed HTTP/verification call). Use FromError(...)
	//                     for a message or FromException(...) for the full exception chain.
	//   • exit code  0  → success.
	// Keeping the two failure classes on distinct codes lets MCP callers / e2e tell "you passed bad
	// input" apart from "clio itself broke". See docs/McpCapabilityMap.md → "MCP tool exit codes".

	/// <summary>
	/// Creates a failed <see cref="CommandExecutionResult"/> with exit code -1 (an UNEXPECTED runtime
	/// failure). For an EXPECTED, caller-actionable validation error use <see cref="FromValidationError"/>.
	/// </summary>
	public static CommandExecutionResult FromError(string message) =>
		new(-1, [new ErrorMessage(message)]);

	/// <summary>
	/// Creates a failed <see cref="CommandExecutionResult"/> with exit code 1 for an EXPECTED,
	/// caller-actionable validation error (missing/empty argument, refused precondition). This is the
	/// message-based sibling of <see cref="FromResolverError"/>; contrast with <see cref="FromError"/>,
	/// which signals an unexpected runtime failure (-1).
	/// </summary>
	public static CommandExecutionResult FromValidationError(string message) =>
		new(1, [new ErrorMessage(message)]);

	/// <summary>
	/// Validates that url, userName and password are non-empty.
	/// Returns a failed result (exit code 1 — an expected validation error) on the first missing value,
	/// or <c>null</c> when all values are valid.
	/// </summary>
	public static CommandExecutionResult ValidateCredentials(string url, string userName, string password) {
		if (string.IsNullOrWhiteSpace(url)) {
			return FromValidationError("url is required and cannot be empty.");
		}
		if (string.IsNullOrWhiteSpace(userName)) {
			return FromValidationError("userName is required and cannot be empty.");
		}
		if (string.IsNullOrWhiteSpace(password)) {
			return FromValidationError("password is required and cannot be empty.");
		}
		return null;
	}

	/// <summary>
	/// Creates a failed <see cref="CommandExecutionResult"/> with exit code 1 and the formatted
	/// exception message. Use for <see cref="EnvironmentResolutionException"/> — expected, caller-actionable
	/// environment/resolver failures, not unexpected runtime exceptions (those use <see cref="FromException"/>).
	/// </summary>
	public static CommandExecutionResult FromResolverError(Exception exception) {
		return new CommandExecutionResult(1, [new ErrorMessage(FormatExceptionChain(exception))]);
	}

	/// <summary>
	/// Creates a failed <see cref="CommandExecutionResult"/> with exit code -1 and full exception details
	/// including inner exception chain, preserving diagnostic information for debugging. Use for UNEXPECTED
	/// runtime failures; for an expected validation error use <see cref="FromValidationError"/>.
	/// </summary>
	public static CommandExecutionResult FromException(Exception exception, IEnumerable<LogMessage> priorLogs = null, string correlationId = null) {
		var messages = new List<LogMessage>();
		if (priorLogs != null) {
			messages.AddRange(priorLogs);
		}
		messages.Add(new ErrorMessage(FormatExceptionChain(exception)));
		return new CommandExecutionResult(-1, messages, CorrelationId: correlationId);
	}

	/// <summary>
	/// Formats an exception and its inner exception chain into a single diagnostic string.
	/// </summary>
	private static string FormatExceptionChain(Exception ex) {
		if (ex.InnerException == null) {
			return $"[{ex.GetType().Name}] {ex.Message}";
		}
		var parts = new List<string>();
		var current = ex;
		int depth = 0;
		while (current != null && depth < 5) {
			parts.Add($"[{current.GetType().Name}] {current.Message}");
			current = current.InnerException;
			depth++;
		}
		return string.Join(" → ", parts);
	}
}
