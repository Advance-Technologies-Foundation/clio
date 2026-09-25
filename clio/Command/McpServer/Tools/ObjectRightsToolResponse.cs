using System;
using System.Linq;
using System.Text.Json.Serialization;
using Clio.Common;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Structured response shared by the object-rights MCP tools (get-object-rights / set-object-rights):
/// success plus the command's collected output. BOTH paths are redacted here, because a structured
/// return is not scrubbed by the MCP pipeline and a SUCCESSFUL run can still carry raw service text: a
/// per-object read failure is reported as a warning with exit 0, and the connected-object fallback logs the
/// exception message — either can embed the request URI or an HTML error body from the service.
/// </summary>
public sealed class ObjectRightsToolResponse {

	[JsonPropertyName("success")]
	public bool Success { get; init; }

	[JsonPropertyName("output")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Output { get; init; }

	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Error { get; init; }

	/// <summary>Builds the response from a completed command execution (output and error both redacted).</summary>
	public static ObjectRightsToolResponse From(CommandExecutionResult result) {
		string? messages = ResolveMessages(result);
		string? redacted = messages is null ? null : SensitiveErrorTextRedactor.Redact(messages);
		return result.ExitCode == 0
			? new ObjectRightsToolResponse { Success = true, Output = redacted }
			: new ObjectRightsToolResponse { Success = false, Error = redacted };
	}

	/// <summary>Builds a failure response from an argument-validation message (already caller-safe text).</summary>
	public static ObjectRightsToolResponse FromValidationError(string message) =>
		new() { Success = false, Error = message };

	/// <summary>Builds a failure response from an exception, with the message redacted.</summary>
	public static ObjectRightsToolResponse FromError(Exception exception) =>
		new() { Success = false, Error = SensitiveErrorTextRedactor.Redact(exception.Message) };

	private static string? ResolveMessages(CommandExecutionResult result) {
		string[] messages = result.Output
			.Select(message => message.Value?.ToString())
			.Where(message => !string.IsNullOrWhiteSpace(message))
			.ToArray();
		return messages.Length > 0 ? string.Join("\n", messages) : null;
	}
}
