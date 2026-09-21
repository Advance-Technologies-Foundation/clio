using System;
using System.Linq;
using System.Text.Json.Serialization;
using Clio.Common;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Structured response shared by the object-rights MCP tools (get-object-rights / set-object-rights):
/// success plus the command's collected output. The error path is redacted here because a structured
/// success-return is not scrubbed by the MCP pipeline, and command/service failures can carry request
/// URIs, paths or session tokens.
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

	/// <summary>Builds the response from a completed command execution (error path redacted).</summary>
	public static ObjectRightsToolResponse From(CommandExecutionResult result) {
		string? messages = ResolveMessages(result);
		return result.ExitCode == 0
			? new ObjectRightsToolResponse { Success = true, Output = messages }
			: new ObjectRightsToolResponse {
				Success = false,
				Error = messages is null ? null : SensitiveErrorTextRedactor.Redact(messages)
			};
	}

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
