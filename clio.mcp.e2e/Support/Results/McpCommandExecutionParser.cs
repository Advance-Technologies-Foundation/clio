using System.Text.Json;
using System.Text.RegularExpressions;
using Clio.Common;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Results;

internal static class McpCommandExecutionParser {
	public static CommandExecutionEnvelope Extract(CallToolResult callResult) {
		if (TrySerializeToJsonElement(callResult.StructuredContent, out JsonElement structuredContent) &&
			TryExtractExecutionFromElement(structuredContent, callResult.IsError == true, out CommandExecutionEnvelope? structuredExecution)) {
			return structuredExecution!;
		}

		if (TrySerializeToJsonElement(callResult.Content, out JsonElement content) &&
			TryExtractExecutionFromElement(content, callResult.IsError == true, out CommandExecutionEnvelope? contentExecution)) {
			return contentExecution!;
		}

		if (callResult.IsError == true) {
			return new CommandExecutionEnvelope(
				1,
				[
					new CommandLogMessageEnvelope(
						LogDecoratorType.Error,
						"MCP tool call returned an error result without a parsable execution payload.")
				]);
		}

		return new CommandExecutionEnvelope(0);
	}

	private static bool TrySerializeToJsonElement(object? value, out JsonElement element) {
		if (value is null) {
			element = default;
			return false;
		}

		element = JsonSerializer.SerializeToElement(value);
		return true;
	}

	private static bool TryExtractExecutionFromElement(
		JsonElement element,
		bool isErrorResult,
		out CommandExecutionEnvelope? execution) {
		if (TryParseExecutionEnvelope(element, out execution)) {
			return true;
		}

		if (element.ValueKind == JsonValueKind.Array) {
			List<CommandLogMessageEnvelope> messages = [];
			int? exitCode = null;
			string? logFilePath = null;

			foreach (JsonElement item in element.EnumerateArray()) {
				if (TryParseExecutionEnvelope(item, out CommandExecutionEnvelope? nestedExecution)) {
					CommandExecutionEnvelope parsedNestedExecution = nestedExecution!;
					exitCode ??= parsedNestedExecution.ExitCode;
					logFilePath ??= parsedNestedExecution.LogFilePath;
					if (parsedNestedExecution.Output is not null) {
						messages.AddRange(parsedNestedExecution.Output);
					}
					continue;
				}

				if (!TryGetTextPayload(item, out string? textPayload) || string.IsNullOrWhiteSpace(textPayload)) {
					continue;
				}

				if (TryParseJson(textPayload, out JsonElement textPayloadElement) &&
					TryParseExecutionEnvelope(textPayloadElement, out CommandExecutionEnvelope? textExecution)) {
					CommandExecutionEnvelope parsedTextExecution = textExecution!;
					exitCode ??= parsedTextExecution.ExitCode;
					logFilePath ??= parsedTextExecution.LogFilePath;
					if (parsedTextExecution.Output is not null) {
						messages.AddRange(parsedTextExecution.Output);
					}
					continue;
				}

				if (TryExtractExitCode(textPayload, out int parsedExitCode)) {
					exitCode ??= parsedExitCode;
				}

				messages.Add(new CommandLogMessageEnvelope(
					isErrorResult ? LogDecoratorType.Error : LogDecoratorType.Info,
					textPayload));
			}

			if (exitCode.HasValue || messages.Count > 0) {
				logFilePath ??= TryExtractLogFilePath(messages);
				execution = new CommandExecutionEnvelope(
					exitCode ?? (isErrorResult ? 1 : 0),
					messages.Count > 0 ? messages : null,
					logFilePath);
				return true;
			}
		}

		if (element.ValueKind == JsonValueKind.String) {
			string? textPayload = element.GetString();
			if (!string.IsNullOrWhiteSpace(textPayload)) {
				if (TryParseJson(textPayload, out JsonElement textPayloadElement) &&
					TryParseExecutionEnvelope(textPayloadElement, out CommandExecutionEnvelope? textExecution)) {
					execution = textExecution;
					return true;
				}

				if (TryExtractExitCode(textPayload, out int parsedExitCode)) {
					execution = new CommandExecutionEnvelope(parsedExitCode, [
						new CommandLogMessageEnvelope(
							isErrorResult ? LogDecoratorType.Error : LogDecoratorType.Info,
							textPayload)
					]);
					return true;
				}

				execution = new CommandExecutionEnvelope(
					isErrorResult ? 1 : 0,
					[
						new CommandLogMessageEnvelope(
							isErrorResult ? LogDecoratorType.Error : LogDecoratorType.Info,
							textPayload)
					]);
				return true;
			}
		}

		execution = null;
		return false;
	}

	private static bool TryParseExecutionEnvelope(JsonElement element, out CommandExecutionEnvelope? execution) {
		execution = null;
		if (element.ValueKind != JsonValueKind.Object) {
			return false;
		}

		if (!TryGetProperty(element, "exit-code", "exitCode", "ExitCode", out JsonElement exitCodeElement) ||
			!TryReadInt32(exitCodeElement, out int exitCode)) {
			return false;
		}

		IReadOnlyList<CommandLogMessageEnvelope>? output = null;
		string? logFilePath = null;
		if (TryGetProperty(
			element,
			"execution-log-messages",
			"executionLogMessages",
			"output",
			"Output",
			out JsonElement outputElement)) {
			output = ParseLogMessages(outputElement);
		}

		if (TryGetProperty(
			element,
			"log-file-path",
			"logFilePath",
			"LogFilePath",
			out JsonElement logFilePathElement) &&
			logFilePathElement.ValueKind == JsonValueKind.String) {
			logFilePath = logFilePathElement.GetString();
		}

		logFilePath ??= TryExtractLogFilePath(output);
		execution = new CommandExecutionEnvelope(exitCode, output, logFilePath);
		return true;
	}

	private static IReadOnlyList<CommandLogMessageEnvelope>? ParseLogMessages(JsonElement element) {
		if (element.ValueKind != JsonValueKind.Array) {
			return null;
		}

		List<CommandLogMessageEnvelope> messages = [];
		foreach (JsonElement item in element.EnumerateArray()) {
			if (item.ValueKind != JsonValueKind.Object) {
				continue;
			}

			LogDecoratorType messageType = ParseMessageType(item);
			string? value = TryGetProperty(item, "value", "Value", out JsonElement valueElement)
				? valueElement.ToString()
				: null;
			messages.Add(new CommandLogMessageEnvelope(messageType, value));
		}

		return messages.Count > 0 ? messages : null;
	}

	private static LogDecoratorType ParseMessageType(JsonElement element) {
		if (!TryGetProperty(element, "message-type", "messageType", "logDecoratorType", "LogDecoratorType", out JsonElement messageTypeElement)) {
			return LogDecoratorType.None;
		}

		if (messageTypeElement.ValueKind == JsonValueKind.Number &&
			messageTypeElement.TryGetInt32(out int numericMessageType) &&
			Enum.IsDefined(typeof(LogDecoratorType), numericMessageType)) {
			return (LogDecoratorType)numericMessageType;
		}

		if (messageTypeElement.ValueKind == JsonValueKind.String &&
			Enum.TryParse(messageTypeElement.GetString(), ignoreCase: true, out LogDecoratorType parsedMessageType)) {
			return parsedMessageType;
		}

		return LogDecoratorType.None;
	}

	private static string? TryExtractLogFilePath(IReadOnlyList<CommandLogMessageEnvelope>? output) {
		if (output is null) {
			return null;
		}

		const string prefix = "Database operation log: ";
		return output
			.Select(message => message.Value)
			.LastOrDefault(value => !string.IsNullOrWhiteSpace(value) && value.StartsWith(prefix, StringComparison.Ordinal))
			?.Substring(prefix.Length)
			.Trim();
	}

	private static bool TryGetTextPayload(JsonElement element, out string? textPayload) {
		textPayload = null;
		if (element.ValueKind != JsonValueKind.Object) {
			return false;
		}

		if (TryGetProperty(element, "text", "Text", out JsonElement textElement) &&
			textElement.ValueKind == JsonValueKind.String) {
			textPayload = textElement.GetString();
			return true;
		}

		return false;
	}

	private static bool TryParseJson(string value, out JsonElement element) {
		try {
			element = JsonSerializer.SerializeToElement(JsonSerializer.Deserialize<JsonElement>(value));
			return true;
		}
		catch (JsonException) {
			element = default;
			return false;
		}
	}

	private static bool TryExtractExitCode(string value, out int exitCode) {
		Match jsonExitCode = Regex.Match(value, "\"exit-code\"\\s*:\\s*(\\d+)", RegexOptions.IgnoreCase);
		if (jsonExitCode.Success && int.TryParse(jsonExitCode.Groups[1].Value, out exitCode)) {
			return true;
		}

		Match textExitCode = Regex.Match(value, "ExitCode\\s*[=:]\\s*(\\d+)", RegexOptions.IgnoreCase);
		if (textExitCode.Success && int.TryParse(textExitCode.Groups[1].Value, out exitCode)) {
			return true;
		}

		exitCode = default;
		return false;
	}

	private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement propertyValue) {
		if (element.TryGetProperty(propertyName, out propertyValue)) {
			return true;
		}

		propertyValue = default;
		return false;
	}

	private static bool TryGetProperty(JsonElement element, string propertyName, string alternatePropertyName, out JsonElement propertyValue) {
		if (TryGetProperty(element, propertyName, out propertyValue)) {
			return true;
		}

		return TryGetProperty(element, alternatePropertyName, out propertyValue);
	}

	private static bool TryGetProperty(
		JsonElement element,
		string propertyName,
		string alternatePropertyName,
		string secondAlternatePropertyName,
		out JsonElement propertyValue) {
		if (TryGetProperty(element, propertyName, alternatePropertyName, out propertyValue)) {
			return true;
		}

		return TryGetProperty(element, secondAlternatePropertyName, out propertyValue);
	}

	private static bool TryGetProperty(
		JsonElement element,
		string propertyName,
		string alternatePropertyName,
		string secondAlternatePropertyName,
		string thirdAlternatePropertyName,
		out JsonElement propertyValue) {
		if (TryGetProperty(element, propertyName, alternatePropertyName, secondAlternatePropertyName, out propertyValue)) {
			return true;
		}

		return TryGetProperty(element, thirdAlternatePropertyName, out propertyValue);
	}

	private static bool TryReadInt32(JsonElement element, out int value) {
		if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value)) {
			return true;
		}

		if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out value)) {
			return true;
		}

		value = default;
		return false;
	}
}
