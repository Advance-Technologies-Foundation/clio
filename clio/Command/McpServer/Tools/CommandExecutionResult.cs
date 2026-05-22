using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
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
	string CorrelationId = null
) {
	/// <summary>
	/// Creates a failed <see cref="CommandExecutionResult"/> with a single error message.
	/// </summary>
	public static CommandExecutionResult FromError(string message) =>
		new(-1, [new ErrorMessage(message)]);

	/// <summary>
	/// Validates that a mode/discriminator value is non-empty and matches one of the allowed values
	/// (case-insensitive). Returns a failed result with an explicit list of allowed values, or
	/// <c>null</c> when the value is valid.
	/// </summary>
	public static CommandExecutionResult ValidateExactlyOneMode(string fieldName, string actual, params string[] allowed) {
		if (string.IsNullOrWhiteSpace(actual)) {
			return FromError($"{fieldName} is required. Allowed values: {string.Join(", ", allowed)}.");
		}
		if (!allowed.Contains(actual, StringComparer.OrdinalIgnoreCase)) {
			return FromError($"{fieldName} must be one of: {string.Join(", ", allowed)}. Got: '{actual}'.");
		}
		return null;
	}

	/// <summary>
	/// Validates that <paramref name="value"/> is non-empty when <paramref name="mode"/> is the active
	/// discriminator. Used inside consolidated tools to enforce per-mode required fields.
	/// </summary>
	public static CommandExecutionResult ValidateRequiredForMode(string fieldName, string value, string mode) {
		if (string.IsNullOrWhiteSpace(value)) {
			return FromError($"{fieldName} is required when mode='{mode}' and cannot be empty.");
		}
		return null;
	}

	/// <summary>
	/// Validates that url, userName and password are non-empty.
	/// Returns a failed result on the first missing value, or <c>null</c> when all values are valid.
	/// </summary>
	public static CommandExecutionResult ValidateCredentials(string url, string userName, string password) {
		if (string.IsNullOrWhiteSpace(url)) {
			return FromError("url is required and cannot be empty.");
		}
		if (string.IsNullOrWhiteSpace(userName)) {
			return FromError("userName is required and cannot be empty.");
		}
		if (string.IsNullOrWhiteSpace(password)) {
			return FromError("password is required and cannot be empty.");
		}
		return null;
	}

	/// <summary>
	/// Creates a failed <see cref="CommandExecutionResult"/> with full exception details
	/// including inner exception chain, preserving diagnostic information for debugging.
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
