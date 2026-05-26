using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer;

/// <summary>
/// Converts MCP tool invocation failures that happen before tool method execution into readable tool results.
/// </summary>
public static class McpToolErrorFilter
{
	private static readonly JsonSerializerOptions SerializerOptions = BindingsModule.CreateMcpSerializerOptions();

	/// <summary>
	/// Wraps call-tool execution and returns deserialization diagnostics as an MCP error result.
	/// </summary>
	/// <param name="next">Next call-tool handler in the MCP request pipeline.</param>
	/// <returns>Wrapped call-tool handler.</returns>
	public static McpRequestHandler<CallToolRequestParams, CallToolResult> HandleCallToolErrors(
		McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
		async (context, cancellationToken) => {
			if (TryCreateArgumentDeserializationError(context, out CallToolResult? argumentErrorResult)) {
				return argumentErrorResult;
			}
			try {
				return await next(context, cancellationToken);
			}
			catch (Exception ex) when (TryGetDeserializationException(ex, out Exception? deserializationException)) {
				return CreateJsonErrorResult(BuildDeserializationErrorMessage(context.Params?.Name, null, deserializationException!));
			}
		};

	private static bool TryCreateArgumentDeserializationError(
		RequestContext<CallToolRequestParams> context,
		out CallToolResult? result) {
		result = null;
		if (context.Params?.Arguments is not { } arguments) {
			return false;
		}

		if (context.MatchedPrimitive is not McpServerTool tool
				|| tool.Metadata.OfType<MethodInfo>().FirstOrDefault() is not { } method) {
			return false;
		}

		foreach (ParameterInfo parameter in method.GetParameters()) {
			string argumentName = GetArgumentName(parameter);
			if (!arguments.TryGetValue(argumentName, out JsonElement argumentValue)) {
				continue;
			}
			try {
				argumentValue.Deserialize(parameter.ParameterType, SerializerOptions);
			}
			catch (Exception ex) when (IsDeserializationException(ex)) {
				result = CreateJsonErrorResult(BuildDeserializationErrorMessage(context.Params.Name, argumentName, ex));
				return true;
			}
		}

		return false;
	}

	private static CallToolResult CreateJsonErrorResult(string? toolName, JsonException exception) {
		return CreateJsonErrorResult(BuildDeserializationErrorMessage(toolName, null, exception));
	}

	private static CallToolResult CreateJsonErrorResult(string message) {
		return new CallToolResult {
			IsError = true,
			Content = [
				new TextContentBlock {
					Text = message
				}
			]
		};
	}

	private static string GetArgumentName(ParameterInfo parameter) =>
		parameter.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
		?? parameter.Name
		?? string.Empty;

	private static string BuildDeserializationErrorMessage(string? toolName, string? argumentName, Exception exception) {
		string message = string.IsNullOrWhiteSpace(argumentName)
			? $"Failed to deserialize arguments for MCP tool '{toolName ?? "<unknown>"}': {exception.Message}"
			: $"Failed to deserialize argument '{argumentName}' for MCP tool '{toolName ?? "<unknown>"}': {exception.Message}";
		return message;
	}

	private static bool IsDeserializationException(Exception exception) =>
		exception is JsonException or NotSupportedException;

	private static bool TryGetDeserializationException(Exception exception, out Exception? deserializationException) {
		for (Exception? current = exception; current is not null; current = current.InnerException) {
			if (IsDeserializationException(current)) {
				deserializationException = current;
				return true;
			}

			if (current is AggregateException aggregateException) {
				foreach (Exception innerException in aggregateException.Flatten().InnerExceptions) {
					if (TryGetDeserializationException(innerException, out deserializationException)) {
						return true;
					}
				}
			}
		}

		deserializationException = null;
		return false;
	}
}
