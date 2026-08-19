using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
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
		async (context, cancellationToken) =>
			// ENG-95262 story 7 — the completion-signal choke point, and it is deliberately the OUTERMOST
			// thing here rather than a wrapper around tool execution. A sticky worker is registered by the
			// parent (with the target's configuration-build reservation taken) BEFORE the tool method is
			// invoked, so every exit below strands it if it returns unsignalled — including the two
			// argument diagnostics and the routing refusals, which answer before any tool runs at all. A
			// scope opened further in would reproduce the very defect class it exists to remove, one layer
			// up. Inert unless this process is a worker AND the call is a sticky operation-STARTER, so a
			// status poll (also sticky, starts nothing) and the whole in-process host are untouched.
			// Installed inside this filter rather than as a second call-tool filter for the reason stated
			// below on the routing question: filter composition order is SDK-defined and unverified.
			await Tools.WorkerOperationCompletionSignal.RunToolCallAsync(
				context.Server,
				ResolveExecutionMetadata(context),
				() => HandleCallToolErrorsCore(next, context, cancellationToken)).ConfigureAwait(false);

	// The filter's original body, unchanged. Extracted so the completion-signal scope above can wrap every
	// one of its exits without indenting the whole method.
	private static async Task<CallToolResult> HandleCallToolErrorsCore(
		McpRequestHandler<CallToolRequestParams, CallToolResult> next,
		RequestContext<CallToolRequestParams> context,
		CancellationToken cancellationToken) {
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
			MatchedRouteDecision decision = ResolveMatchedRoute(context);
			if (decision.Refusal is not null) {
				return decision.Refusal;
			}
			if (decision.Dispatcher is not null) {
				// The call is relayed VERBATIM: the matched primitive's name is already the canonical
				// one, so the caller's own params object goes to the worker unchanged — `_meta` and its
				// progress token included.
				return await decision.Dispatcher
					.DispatchAsync(decision.Route!, context.Params!,
						new Relay.McpServerParentSession(context.Server), cancellationToken)
					.ConfigureAwait(false);
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
	}

	/// <summary>
	/// Reads the declared execution metadata of the tool this call names, for the completion-signal choke
	/// point above.
	/// </summary>
	/// <param name="context">The call being served.</param>
	/// <returns>The metadata, or <see langword="null"/> when the name is unclassified or unresolvable.</returns>
	/// <remarks>
	/// <para>
	/// Service-located for the same reason the router is: this seam is a static delegate with no
	/// constructor. The reader is registered on the transport-neutral <c>BindingsModule.RegisterInto</c>
	/// path, so a worker always has it.
	/// </para>
	/// <para>
	/// Keyed on the raw request name with no inner command, which is exactly right for the direct and the
	/// deprecated-alias vectors (the reader canonicalises aliases itself). A sticky family reached through
	/// <c>clio-run</c> is NOT covered here: the executor's own name is what arrives, and unwrapping it
	/// would mean re-implementing that tool's two accepted wrapper shapes in this filter.
	/// </para>
	/// </remarks>
	private static McpToolExecutionMetadata? ResolveExecutionMetadata(
		RequestContext<CallToolRequestParams> context) {
		if (context.Services?.GetService(typeof(IMcpToolExecutionMetadataReader))
			is not IMcpToolExecutionMetadataReader reader) {
			return null;
		}
		// The INNER command, not just the dialled name. Every sticky tool is NON-RESIDENT, so the real
		// caller reaches it through clio-run and the worker sees `clio-run` here — whose own metadata is
		// not sticky. The ledger then never opens, the completion notification is never sent, and the
		// worker keeps its admission slot and the target's configuration-build reservation until the
		// thirty-minute lifetime bound, refusing every later long operation for that target. In other
		// words the choke point would have covered every path except the one real callers use.
		//
		// This is the same shape as the sticky-KEY defect found earlier on this branch, and it is
		// unwrapped the same way rather than by a third convention.
		return reader.TryGetMetadata(context.Params?.Name, ReadWrappedCommand(context.Params),
			out McpToolExecutionMetadata metadata)
			? metadata
			: null;
	}

	/// <summary>
	/// Reads the inner command name from an executor-wrapped call, or <see langword="null"/> for an
	/// ordinary one.
	/// </summary>
	/// <remarks>
	/// Mirrors <c>ClioRunTool.RecoverWrappedCall</c>: the command is a string property named
	/// <c>command</c>, which sits either at the top of the arguments or one level down under <c>args</c>.
	/// Only the two shapes that tool accepts are recognised, and only for the two executor names — an
	/// ordinary tool that happens to carry a <c>command</c> argument must never be re-read as a wrapper.
	/// </remarks>
	/// <param name="parameters">The call parameters.</param>
	/// <returns>The inner command name, or <see langword="null"/>.</returns>
	private static string ReadWrappedCommand(CallToolRequestParams parameters) {
		string dialled = parameters?.Name?.Trim();
		if (!string.Equals(dialled, Tools.ClioRunTool.ToolName, StringComparison.OrdinalIgnoreCase)
			&& !string.Equals(dialled, Tools.ClioRunDestructiveTool.ToolName,
				StringComparison.OrdinalIgnoreCase)) {
			return null;
		}
		if (parameters?.Arguments is not { } arguments) {
			return null;
		}
		foreach (KeyValuePair<string, JsonElement> argument in arguments) {
			if (TryReadCommand(argument.Value, out string command)) {
				return command;
			}
			if (string.Equals(argument.Key, "command", StringComparison.OrdinalIgnoreCase)
				&& argument.Value.ValueKind == JsonValueKind.String) {
				string flat = argument.Value.GetString();
				if (!string.IsNullOrWhiteSpace(flat)) {
					return flat;
				}
			}
		}
		return null;
	}

	private static bool TryReadCommand(JsonElement candidate, out string command) {
		command = null;
		if (candidate.ValueKind != JsonValueKind.Object
			|| !candidate.TryGetProperty("command", out JsonElement element)
			|| element.ValueKind != JsonValueKind.String) {
			return false;
		}
		string value = element.GetString();
		if (string.IsNullOrWhiteSpace(value)) {
			return false;
		}
		command = value;
		return true;
	}

	// True when the matched (advertised) tool is retry-safe and therefore eligible for the read-response
	// deadline (ENG-93373). MatchedPrimitive is null for an unmatched name — those are bounded by the
	// durable handler instead, so this returns false and the call falls through unbounded here.
	private static bool IsRetrySafeMatchedTool(RequestContext<CallToolRequestParams> context) =>
		context.MatchedPrimitive is McpServerTool tool
		&& McpReadDeadlineGate.IsRetrySafe(tool.ProtocolTool.Name, tool.ProtocolTool.Annotations);

	/// <summary>
	/// Asks the single execution-routing authority where this MATCHED call executes, and answers with what
	/// this seam must do: continue in the host process, relay to a worker, or refuse (ENG-95262, ADR §9).
	/// </summary>
	/// <remarks>
	/// <para>
	/// Three named branches, none of them an implicit fallthrough:
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
	/// <item><description>
	/// <b>Worker route.</b> The call is relayed to a supervised child through
	/// <see cref="Relay.IMcpWorkerCallDispatcher"/>, which is service-located here for the same reason the
	/// router is — this seam is a static delegate with no constructor. An absent dispatcher refuses too,
	/// rather than continuing: silently running a cohort tool in the host process is exactly the wedge the
	/// worker path removes.
	/// </description></item>
	/// </list>
	/// <para>
	/// Note the ORDER against <see cref="McpReadResponseDeadline"/> below: a relayed call is bounded by the
	/// parent killing its worker, so it must never also be wrapped in the in-process read deadline — which
	/// bounds the ANSWER while the work runs on, keeping the per-tenant monitor. Routing therefore happens
	/// first, and a worker-routed call leaves this filter before the deadline wrapper is reached.
	/// </para>
	/// <para>
	/// Keyed on <c>tool.ProtocolTool.Name</c> and NOT on the inner command of a <c>clio-run</c> call: the
	/// wrapper itself runs in-process, and its inner tool is routed at dispatch site (c), the only place the
	/// unwrapped name exists (ADR rule 7).
	/// </para>
	/// </remarks>
	private static MatchedRouteDecision ResolveMatchedRoute(RequestContext<CallToolRequestParams> context) {
		if (context.MatchedPrimitive is not McpServerTool tool) {
			return MatchedRouteDecision.ContinueInProcess;
		}
		if (context.Services?.GetService(typeof(IMcpExecutionRouter)) is not IMcpExecutionRouter router) {
			// Do NOT turn this back into "continue". That reads as harmless (routing and continuing agree
			// for every in-process tool) right up until a cohort tool arrives, at which point it silently
			// runs in the host process. See the remarks above and
			// McpExecutionRouter.RoutingAuthorityUnreachableResult.
			return MatchedRouteDecision.Refuse(
				McpExecutionRouter.RoutingAuthorityUnreachableResult(tool.ProtocolTool.Name));
		}
		McpExecutionRoute route = router.Resolve(tool.ProtocolTool.Name, innerCommand: null);
		if (route.ExecutesInProcess) {
			return MatchedRouteDecision.ContinueInProcess;
		}
		// Service-located for the same reason the router is: this seam is a static delegate, so it has no
		// constructor to inject into. Absent dispatcher ⇒ refuse, never continue — the two remaining
		// possibilities stay symmetric with the router branch above.
		if (context.Services?.GetService(typeof(Relay.IMcpWorkerCallDispatcher))
			is not Relay.IMcpWorkerCallDispatcher dispatcher) {
			return MatchedRouteDecision.Refuse(McpExecutionRouter.WorkerPathNotWiredResult(route));
		}
		return MatchedRouteDecision.Relay(route, dispatcher);
	}

	/// <summary>
	/// What the matched seam must do with one call: continue in the host process, refuse with a named
	/// result, or relay it to a worker.
	/// </summary>
	/// <remarks>
	/// A three-way answer expressed as one value rather than as two <c>out</c> parameters, so "no router"
	/// and "worker route" cannot be confused at the call site — they are different words here, and the
	/// refusal text differs between them.
	/// </remarks>
	private readonly record struct MatchedRouteDecision(
		McpExecutionRoute? Route,
		Relay.IMcpWorkerCallDispatcher? Dispatcher,
		CallToolResult? Refusal) {

		internal static MatchedRouteDecision ContinueInProcess => default;

		internal static MatchedRouteDecision Refuse(CallToolResult refusal) =>
			new(Route: null, Dispatcher: null, refusal);

		internal static MatchedRouteDecision Relay(
			McpExecutionRoute route, Relay.IMcpWorkerCallDispatcher dispatcher) =>
			new(route, dispatcher, Refusal: null);
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
