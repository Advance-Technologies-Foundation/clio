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
			catch (OperationCanceledException) {
				// Honour cooperative cancellation/timeout — let the host see a cancellation, not a tool error.
				throw;
			}
			catch (Exception ex) {
				// Without this, an unhandled tool-method exception reaches the SDK's default handler, which
				// returns a generic "An error occurred invoking '<tool>'" with no detail — so an agent cannot
				// see WHY the call failed (e.g. "Environment ... not found") and cannot self-correct. Surface
				// the real (inner-most) message as a structured error result for EVERY tool uniformly — but
				// redacted, because this text lands in the model/host transcript and inner-most messages
				// routinely carry absolute paths, request URIs (target hosts), and credentials.
				return CreateJsonErrorResult(
					$"MCP tool '{context.Params?.Name ?? "<unknown>"}' failed: {SensitiveErrorTextRedactor.Redact(GetInnermostMessage(ex))}");
			}
		};

	// Unwraps to the inner-most exception message so the surfaced detail is the actual cause rather than a
	// generic wrapper (e.g. TargetInvocationException) added by the dispatch machinery.
	private static string GetInnermostMessage(Exception exception) {
		Exception current = exception;
		while (current.InnerException is not null) {
			current = current.InnerException;
		}
		return current.Message;
	}

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
		// The serializer message can echo back the offending argument value, so redact it too.
		string detail = SensitiveErrorTextRedactor.Redact(exception.Message);
		string message = string.IsNullOrWhiteSpace(argumentName)
			? $"Failed to deserialize arguments for MCP tool '{toolName ?? "<unknown>"}': {detail}"
			: $"Failed to deserialize argument '{argumentName}' for MCP tool '{toolName ?? "<unknown>"}': {detail}";
		return message;
	}

	private static bool IsDeserializationException(Exception exception) =>
		exception is JsonException or NotSupportedException;
}
