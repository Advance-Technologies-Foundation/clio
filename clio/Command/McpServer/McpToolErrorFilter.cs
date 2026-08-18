using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer;

/// <summary>
/// Converts MCP tool invocation failures that happen before tool method execution into readable tool results.
/// </summary>
public static class McpToolErrorFilter
{
	// Placeholder surfaced in error text when the MCP request carries no tool name (context.Params?.Name is null).
	private const string UnknownToolName = "<unknown>";

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
			if (TryCreateMissingCompositeArgumentHint(context, out CallToolResult? hintResult)) {
				return hintResult;
			}
			try {
				// ENG-95262 dispatch site (a) of three — the MATCHED path. The routing question is asked here,
				// inside the try, so anything unexpected it raises still leaves through the redacted catch
				// below rather than reaching the SDK's default handler as raw text (threat model R-7 — the same
				// reason a SECOND call-tool filter is not added: filter composition order is SDK-defined and
				// unverified, so a router outside this catch could emit unredacted text into the transcript).
				if (TryCreateWorkerRouteRefusal(context, out CallToolResult? routeRefusal)) {
					return routeRefusal;
				}
				// ENG-93373: bound retry-safe (read-only, or the get-page local-write read; never idempotent server writes) tools by a wall-clock
				// response deadline so a stalled Creatio read can never hang indefinitely. On expiry the helper
				// returns a structured error-class=creatio-timeout result telling the agent the call is safe to
				// retry. Destructive tools are excluded — they own their own timeout contract — and fall through
				// to the unbounded call below. Only MATCHED (advertised) tools are classified here; the unmatched
				// long-tail is bounded by the durable handler (McpDurableCallToolHandler) using the same gate.
				if (IsRetrySafeMatchedTool(context)) {
					return await McpReadResponseDeadline.RunAsync(
						context.Params?.Name ?? UnknownToolName,
						token => next(context, token),
						cancellationToken).ConfigureAwait(false);
				}
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
					$"MCP tool '{context.Params?.Name ?? UnknownToolName}' failed: {SensitiveErrorTextRedactor.Redact(GetSurfacedMessage(ex))}");
			}
		};

	// True when the matched (advertised) tool is retry-safe and therefore eligible for the read-response
	// deadline (ENG-93373). MatchedPrimitive is null for an unmatched name — those are bounded by the
	// durable handler instead, so this returns false and the call falls through unbounded here.
	private static bool IsRetrySafeMatchedTool(RequestContext<CallToolRequestParams> context) =>
		context.MatchedPrimitive is McpServerTool tool
		&& McpReadDeadlineGate.IsRetrySafe(tool.ProtocolTool.Name, tool.ProtocolTool.Annotations);

	/// <summary>
	/// Asks the single execution-routing authority where this MATCHED call executes, and refuses the call
	/// when it routes to a worker this seam cannot reach (ENG-95262, ADR §9).
	/// </summary>
	/// <remarks>
	/// <para>
	/// Two named branches, neither of them an implicit fallthrough:
	/// </para>
	/// <list type="number">
	/// <item><description>
	/// <b>Unmatched name.</b> <c>MatchedPrimitive</c> is <c>null</c>, deliberately left alone: at filter
	/// time such a name has no canonical yet (alias resolution runs later, in the durable handler) and the
	/// write-capability confirmation gate has not run, so routing here would key on an unresolved alias and
	/// miss. Dispatch site (b) routes it, after both. This is the ONLY branch that continues without
	/// consulting the authority, and it continues because another site consults it.
	/// </description></item>
	/// <item><description>
	/// <b>No router reachable.</b> FAIL-CLOSED, deliberately — this seam used to continue in-process here,
	/// which made it the one asymmetric site: its two siblings take the router by constructor injection and
	/// simply cannot exist without it. A silent in-process continuation is harmless only for as long as
	/// nothing routes to a worker; the moment the relay is wired it becomes "run the whole worker cohort in
	/// the host process", invisibly — the exact wedge this work removes. Refusing instead makes the wiring
	/// defect say its own name. It is unreachable in a healthy process: the router is registered on the
	/// transport-neutral <c>BindingsModule.RegisterInto</c> path, so stdio, mcp-http and the per-request
	/// tenant containers all resolve it; only a hand-built fixture reaches this branch.
	/// </description></item>
	/// </list>
	/// <para>
	/// Keyed on <c>tool.ProtocolTool.Name</c> and NOT on the inner command of a <c>clio-run</c> call: the
	/// wrapper itself runs in-process, and its inner tool is routed at dispatch site (c), the only place the
	/// unwrapped name exists (ADR rule 7).
	/// </para>
	/// </remarks>
	private static bool TryCreateWorkerRouteRefusal(
		RequestContext<CallToolRequestParams> context,
		out CallToolResult? result) {
		result = null;
		if (context.MatchedPrimitive is not McpServerTool tool) {
			return false;
		}
		if (context.Services?.GetService(typeof(IMcpExecutionRouter)) is not IMcpExecutionRouter router) {
			// Do NOT turn this back into `return false`. That reads as harmless today (nothing routes to a
			// worker, so continuing and routing agree byte for byte) and stops being harmless the moment the
			// relay is wired, at which point it silently runs the worker cohort in the host process. See the
			// remarks above and McpExecutionRouter.RoutingAuthorityUnreachableResult.
			result = McpExecutionRouter.RoutingAuthorityUnreachableResult(tool.ProtocolTool.Name);
			return true;
		}
		McpExecutionRoute route = router.Resolve(tool.ProtocolTool.Name, innerCommand: null);
		if (route.ExecutesInProcess) {
			// The in-process branch, taken by every call today. THIS is the line Stage 6 replaces with the
			// relay invocation for the worker cohort — it is explicit rather than a fallthrough so the
			// replacement has one obvious place to happen.
			return false;
		}
		result = McpExecutionRouter.WorkerPathNotWiredResult(route);
		return true;
	}

	// Message selection lives in Clio.Common.SurfacedExceptionMessage, shared with the nested clio-run
	// dispatcher so both MCP error paths surface the same text (ENG-93365).
	private static string GetSurfacedMessage(Exception exception) =>
		Clio.Common.SurfacedExceptionMessage.Resolve(exception);

	private static bool TryCreateArgumentDeserializationError(
		RequestContext<CallToolRequestParams> context,
		out CallToolResult? result) {
		result = null;
		if (context.Params?.Arguments is not { } arguments) {
			return false;
		}

		if (!TryGetToolMethod(context, out MethodInfo? method)) {
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
			? $"Failed to deserialize arguments for MCP tool '{toolName ?? UnknownToolName}': {detail}"
			: $"Failed to deserialize argument '{argumentName}' for MCP tool '{toolName ?? UnknownToolName}': {detail}";
		return message;
	}

	private static bool IsDeserializationException(Exception exception) =>
		exception is JsonException or NotSupportedException;

	/// <summary>
	/// Detects when a caller sends arguments flat (e.g. <c>{"environment-name": "..."}</c>) instead of
	/// wrapping them in the composite parameter object (e.g. <c>{"args": {"environment-name": "..."}}</c>).
	/// </summary>
	internal static bool TryCreateMissingCompositeArgumentHint(
		RequestContext<CallToolRequestParams> context,
		out CallToolResult? result) {
		result = null;
		if (context.Params?.Arguments is not { Count: > 0 } arguments) {
			return false;
		}

		if (!TryGetToolMethod(context, out MethodInfo? method)) {
			return false;
		}

		return TryDetectFlatArgsMismatch(context.Params.Name, method, arguments, out result);
	}

	/// <summary>
	/// Extracts the tool implementation <see cref="MethodInfo"/> from the matched MCP primitive.
	/// </summary>
	private static bool TryGetToolMethod(
		RequestContext<CallToolRequestParams> context,
		[NotNullWhen(true)] out MethodInfo? method) {
		method = context.MatchedPrimitive is McpServerTool tool
			? tool.Metadata.OfType<MethodInfo>().FirstOrDefault()
			: null;
		return method is not null;
	}

	/// <summary>
	/// Core detection: checks whether <paramref name="arguments"/> contains flat keys that belong
	/// inside a composite method parameter instead of at the top level.
	/// </summary>
	internal static bool TryDetectFlatArgsMismatch(
		string? toolName,
		MethodInfo method,
		IDictionary<string, JsonElement> arguments,
		out CallToolResult? result) {
		result = null;

		foreach (ParameterInfo parameter in method.GetParameters()) {
			string argumentName = GetArgumentName(parameter);

			if (arguments.ContainsKey(argumentName)) {
				continue;
			}

			if (IsFrameworkParameter(parameter.ParameterType)) {
				continue;
			}

			List<string> propertyNames = GetJsonPropertyNames(parameter.ParameterType);
			if (propertyNames.Count == 0) {
				continue;
			}

			List<string> matchedKeys = propertyNames
				.Where(arguments.ContainsKey)
				.ToList();

			if (matchedKeys.Count > 0) {
				result = CreateJsonErrorResult(
					BuildMissingWrapperMessage(toolName, argumentName, propertyNames, matchedKeys));
				return true;
			}
		}

		return false;
	}

	private static bool IsFrameworkParameter(Type type) =>
		type == typeof(CancellationToken)
		|| type.Namespace?.StartsWith("ModelContextProtocol", StringComparison.Ordinal) == true;

	private static List<string> GetJsonPropertyNames(Type type) {
		if (!type.IsClass || type == typeof(string)) {
			return [];
		}
		return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(IsWireContractProperty)
			.Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? p.Name)
			.ToList();
	}

	private static bool IsWireContractProperty(PropertyInfo property) =>
		property.GetCustomAttribute<JsonExtensionDataAttribute>() is null
		&& property.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition != JsonIgnoreCondition.Always;

	private static string BuildMissingWrapperMessage(
		string? toolName, string wrapperName, List<string> allProperties, List<string> matchedKeys) {
		string flatKeysDisplay = string.Join(", ", matchedKeys.Select(k => $"\"{k}\""));
		string exampleInner = string.Join(", ", allProperties.Select(k => $"\"{k}\": \"...\""));
		return $"Tool '{toolName ?? UnknownToolName}' expects arguments wrapped inside "
			+ $"an \"{wrapperName}\" object, but received {flatKeysDisplay} at the top level. "
			+ $"Correct format: {{\"{wrapperName}\": {{{exampleInner}}}}}";
	}
}
