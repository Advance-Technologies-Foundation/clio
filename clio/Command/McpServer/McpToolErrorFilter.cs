using System;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Tools;
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
		// ENG-95885: classify and (where unambiguous) normalize the caller's argument shape BEFORE every
		// other preflight, so the deserialization diagnostics below run over the REWRITTEN arguments and
		// a flat payload carrying a wrong JSON value type still yields the precise per-argument error
		// instead of falling into the generic catch. It also runs ahead of the ENG-95262 matched-route
		// dispatch below, so a relayed call reaches the worker already normalized. A returned result means
		// the shape was REFUSED.
		if (TryRefuseOrRewriteCallArguments(context, out CallToolResult? normalizationErrorResult)) {
			return normalizationErrorResult!;
		}
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
					.DispatchAsync(decision.Route, context.Params,
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
		// TOP-LEVEL FIRST, and only then the named `args` wrapper — the same precedence
		// ClioRunTool.RecoverWrappedCall uses, which reads `command` off the wrapper it was handed.
		// The earlier version scanned nested objects first, so a mixed payload carrying BOTH — say
		// {"args":{"command":"get-page",…},"command":"compile-creatio"} — made the executor dispatch
		// compile-creatio while this filter loaded get-page's metadata. The sticky compile would then open
		// no completion ledger and hold its worker, its admission slot and the target's
		// configuration-build reservation until the thirty-minute bound. Two readers of one payload must
		// not disagree about which command it names.
		if (arguments.TryGetValue("command", out JsonElement topLevel)
			&& topLevel.ValueKind == JsonValueKind.String) {
			string flat = topLevel.GetString();
			if (!string.IsNullOrWhiteSpace(flat)) {
				return flat;
			}
		}
		if (arguments.TryGetValue("args", out JsonElement wrapper)
			&& TryReadCommand(wrapper, out string nested)) {
			return nested;
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

	/// <summary>
	/// ENG-95885. Classifies the pre-binding argument payload of a MATCHED tool that takes exactly one
	/// bindable composite <c>args</c> parameter, and rewrites it into the wrapped shape when — and only
	/// when — the flat shape is unambiguous:
	/// <list type="bullet">
	/// <item><description><b>already wrapped</b> (only the wrapper key) — untouched.</description></item>
	/// <item><description><b>canonical-flat</b> (EVERY top-level key is a wire property of the args
	/// record) — every key is moved inside the wrapper.</description></item>
	/// <item><description><b>has an unknown key</b> (at least one top-level key is NOT a wire property —
	/// whether the whole payload is unknown or a real field sits next to a typo) — refused with the
	/// canonical field list, unless the tool declares <see cref="McpRecoversUnknownArgumentsAttribute"/>
	/// (then the payload is forwarded so the tool's own overflow-bag diagnosis wins). This is why the
	/// payload is classified instead of wrapped blindly: the resident majority whose args record has no
	/// <c>[JsonExtensionData]</c> overflow bag would otherwise have the typo silently dropped by the
	/// serializer at bind time (it ignores unmapped members), and the tool would answer a validation
	/// mistake with a plausible-but-wrong list/default SUCCESS — strictly worse for an agent than a hard
	/// failure. The refusal deliberately covers the PARTIAL case too: a real field beside a typo is not
	/// made safe by the good field.</description></item>
	/// <item><description><b>hybrid</b> (wrapper object plus extra top-level keys) — refused as ambiguous,
	/// with no silent precedence in either direction.</description></item>
	/// </list>
	/// </summary>
	/// <returns><c>true</c> when the call must be REFUSED (<paramref name="result"/> carries the error);
	/// <c>false</c> when the call proceeds — either untouched or with rewritten arguments. Note the
	/// inverted sense: <c>true</c> is the refusal, not success — callers read the result as
	/// <c>refused</c>.</returns>
	internal static bool TryRefuseOrRewriteCallArguments(
		RequestContext<CallToolRequestParams> context,
		out CallToolResult? result) {
		result = null;
		if (context.Params is not { } parameters) {
			return false;
		}

		// MatchedPrimitive is null for a tool that is not advertised in tools/list, so the DURABLE
		// long-tail path (McpDurableCallToolHandler / IClioRunExecutor.InvokeResolvedAsync) is
		// intentionally NOT normalized: there is no MethodInfo here to reflect a parameter contract from.
		// A long-tail tool is reached through clio-run, which owns its own wrapped/flat recovery
		// (ClioRunExecutor.RecoverWrappedCall). The measured ENG-95885 run contains zero durable-handler
		// outcomes of this error class, so widening the scope would add risk with nothing to fix.
		if (!TryGetToolMethod(context, out MethodInfo? method)) {
			return false;
		}

		return TryRefuseOrRewriteArguments(parameters, method, out result);
	}

	/// <summary>
	/// The reflection-only core of <see cref="TryRefuseOrRewriteCallArguments"/>: classifies (and where
	/// unambiguous, rewrites) <paramref name="parameters"/> against the contract of
	/// <paramref name="method"/>. Split out from the context-bound entry point so a completeness test can
	/// drive it across every resident tool method without constructing MCP primitives. Returns <c>true</c>
	/// when the call must be REFUSED (the inverted sense described on
	/// <see cref="TryRefuseOrRewriteCallArguments"/>).
	/// </summary>
	internal static bool TryRefuseOrRewriteArguments(
		CallToolRequestParams parameters,
		MethodInfo method,
		out CallToolResult? result) {
		result = null;

		// The trigger gate, shared with clio-run by construction (see
		// McpToolArgumentSupport.TryGetSingleCompositeParameter): a multi-parameter tool such as
		// clio-run (command + args) or a single-scalar tool binds top-level keys BY PARAMETER NAME, so
		// its payload is already meaningful flat and must never be rewritten.
		if (!McpToolArgumentSupport.TryGetSingleCompositeParameter(method, out ParameterInfo? wrapper)) {
			return false;
		}

		string wrapperName = GetArgumentName(wrapper);
		if (string.IsNullOrEmpty(wrapperName)) {
			return false;
		}

		List<string> canonicalNames = GetJsonPropertyNames(wrapper.ParameterType);
		if (canonicalNames.Count == 0) {
			return false;
		}

		IDictionary<string, JsonElement>? arguments = parameters.Arguments;
		if (arguments is null || arguments.Count == 0) {
			// Fail-closed: only a tool that has EXPLICITLY declared a natural no-arguments operation gets
			// the empty wrapper synthesized for it. Every other tool keeps today's missing-parameter error.
			if (method.GetCustomAttribute<McpAcceptsEmptyArgumentsAttribute>() is null) {
				return false;
			}
			parameters.Arguments = BuildWrappedArguments(wrapperName, []);
			return false;
		}

		if (arguments.ContainsKey(wrapperName)) {
			if (arguments.Count == 1) {
				// The already-working wrapped shape — byte-compatible pass-through.
				return false;
			}
			result = CreateJsonErrorResult(BuildAmbiguousShapeMessage(
				parameters.Name,
				wrapperName,
				arguments.Keys.Where(key => !string.Equals(key, wrapperName, StringComparison.Ordinal))));
			return true;
		}

		HashSet<string> canonicalNameSet = new(canonicalNames, StringComparer.Ordinal);
		List<string> unknownKeys = arguments.Keys
			.Where(key => !canonicalNameSet.Contains(key))
			.ToList();
		if (unknownKeys.Count > 0
			&& method.GetCustomAttribute<McpRecoversUnknownArgumentsAttribute>() is null) {
			// ANY unknown key is refused with the canonical field list — the whole payload unknown, OR a
			// real field sitting next to a typo. The PARTIAL case is refused for the same reason as the
			// all-unknown case: for the resident majority whose args record has no [JsonExtensionData]
			// overflow bag, wrapping the payload lets System.Text.Json silently DROP the typo at bind time
			// (it ignores unmapped members), so the tool answers a validation mistake with a
			// plausible-but-wrong list/default SUCCESS — strictly worse for an agent than a hard failure.
			// The good field does not make the typo safe. Only an EXPLICIT [McpRecoversUnknownArguments]
			// declaration forwards the payload (so the tool's own overflow-bag diagnosis wins): the mere
			// PRESENCE of a [JsonExtensionData] bag proves a record can SEE an unknown key, never that the
			// tool VALIDATES it, so it is deliberately NOT the forward test.
			result = CreateJsonErrorResult(BuildUnknownArgumentsMessage(
				parameters.Name, wrapperName, canonicalNames, unknownKeys));
			return true;
		}

		parameters.Arguments = BuildWrappedArguments(wrapperName, arguments);
		return false;
	}

	/// <summary>
	/// Moves EVERY top-level key of <paramref name="arguments"/> into a single wrapper object. Replacing
	/// only <see cref="CallToolRequestParams.Arguments"/> on the EXISTING params instance is load-bearing:
	/// building a new <see cref="CallToolRequestParams"/> would drop <c>_meta</c>, the progress token and
	/// task metadata, breaking <c>notifications/progress</c> on long-running tools and the
	/// <c>_meta.clioStageEvent</c> stream ClioRing consumes.
	/// </summary>
	private static Dictionary<string, JsonElement> BuildWrappedArguments(
		string wrapperName, IEnumerable<KeyValuePair<string, JsonElement>> arguments) {
		Dictionary<string, JsonElement> wrapped = new(StringComparer.Ordinal);
		using MemoryStream buffer = new();
		using (Utf8JsonWriter writer = new(buffer)) {
			writer.WriteStartObject();
			foreach (KeyValuePair<string, JsonElement> argument in arguments) {
				writer.WritePropertyName(argument.Key);
				argument.Value.WriteTo(writer);
			}
			writer.WriteEndObject();
		}
		using JsonDocument document = JsonDocument.Parse(buffer.ToArray());
		wrapped[wrapperName] = document.RootElement.Clone();
		return wrapped;
	}

	private static string BuildUnknownArgumentsMessage(
		string? toolName, string wrapperName, List<string> canonicalNames, List<string> unknownKeys) {
		string unknownDisplay = string.Join(", ", unknownKeys.Select(key => $"\"{key}\""));
		string validDisplay = string.Join(", ", canonicalNames.Select(key => $"\"{key}\""));
		return $"Tool '{toolName ?? UnknownToolName}' received unknown argument(s) {unknownDisplay}. "
			+ $"Valid arguments: {validDisplay}. "
			+ $"Use exactly those names, either flat at the top level or wrapped in \"{wrapperName}\" "
			+ $"({{\"{wrapperName}\": {{...}}}}). Nothing ran: the call was refused rather than executed "
			+ "with default values.";
	}

	private static string BuildAmbiguousShapeMessage(
		string? toolName, string wrapperName, IEnumerable<string> extraKeys) {
		string extraDisplay = string.Join(", ", extraKeys.Select(key => $"\"{key}\""));
		return $"Tool '{toolName ?? UnknownToolName}' received an ambiguous argument shape: "
			+ $"a \"{wrapperName}\" object AND top-level key(s) {extraDisplay}. "
			+ $"Send exactly one shape — wrapped {{\"{wrapperName}\": {{...}}}} or flat {{...}} — "
			+ "so there is no doubt which value wins.";
	}

	internal static bool TryCreateArgumentDeserializationError(
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
			//JsonElement.Deserialize happily returns null for a reference type, so {"args":null} never
			//threw and the tool ran with a null composite argument - odata-read answered with a typed
			//NRE-derived success:false while the same call through clio-run reported a missing required
			//argument. A required, non-nullable parameter rejects an explicit JSON null here so both
			//paths return the same invalid-parameter-type contract. Optional and nullable parameters
			//are untouched: null is a legitimate value for them.
			if (argumentValue.ValueKind == JsonValueKind.Null && IsRequiredNonNullable(parameter)) {
				result = CreateJsonErrorResult(BuildNullArgumentErrorMessage(
					context.Params.Name, parameter.ParameterType, argumentName));
				return true;
			}
			// ENG-95885: a JSON-ENCODED argument (the object sent as a quoted string, e.g.
			// clio-run's args: "{\"environment-name\":\"dev\"}") is by far the most common value-kind
			// mistake and its raw serializer text ("... LineNumber: 0 | BytePositionInLine: 31") tells an
			// agent nothing about the required shape. Name the shape once, precisely, before the generic
			// deserialization path can produce that text. The value is deliberately NOT parsed — accepting
			// a stringified object would widen the accepted contract permanently for no benefit.
			if (TryCreateJsonEncodedObjectError(
				context.Params.Name, argumentName, parameter.ParameterType, argumentValue, out result)) {
				return true;
			}
			try {
				argumentValue.Deserialize(parameter.ParameterType, SerializerOptions);
			}
			catch (Exception ex) when (IsDeserializationException(ex)) {
				result = CreateJsonErrorResult(BuildDeserializationErrorMessage(
					context.Params.Name,
					parameter.ParameterType,
					argumentName,
					ex));
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// True when the parameter must carry a value: it is marked <see cref="RequiredAttribute"/>, has no
	/// default, and its type is not a nullable one. A nullable or defaulted parameter accepts null.
	/// </summary>
	private static bool IsRequiredNonNullable(ParameterInfo parameter) {
		if (parameter.GetCustomAttribute<RequiredAttribute>() is null || parameter.HasDefaultValue) {
			return false;
		}
		Type type = parameter.ParameterType;
		if (Nullable.GetUnderlyingType(type) is not null) {
			return false;
		}
		//A reference type declared as nullable (`Args?`) is annotated rather than a distinct CLR type,
		//so the nullable context of the declaration is what tells them apart.
		return new NullabilityInfoContext().Create(parameter).WriteState != NullabilityState.Nullable;
	}

	private static string BuildNullArgumentErrorMessage(string toolName, Type parameterType, string argumentName) =>
		$"invalid-parameter-type: argument '{argumentName}' for MCP tool '{toolName}' must be "
		+ $"{GetExpectedJsonType(parameterType, argumentName)}. Received a JSON null, and the argument "
		+ "is required.";

	/// <summary>
	/// ENG-95885. Produces one precise, shape-naming error when an argument the tool expects as a JSON
	/// OBJECT arrives as a JSON string. Returns <c>false</c> for every other combination so the ordinary
	/// per-argument deserialization diagnostics stay unchanged.
	/// </summary>
	private static bool TryCreateJsonEncodedObjectError(
		string? toolName,
		string argumentName,
		Type parameterType,
		JsonElement argumentValue,
		out CallToolResult? result) {
		result = null;
		if (argumentValue.ValueKind != JsonValueKind.String || !ExpectsJsonObject(parameterType)) {
			return false;
		}
		result = CreateJsonErrorResult(
			$"invalid-parameter-type: argument '{argumentName}' for MCP tool "
			+ $"'{toolName ?? UnknownToolName}' must be a JSON object, "
			+ "not a JSON string. Send the object itself — for example "
			+ $"{{\"{argumentName}\": {{\"<argument-name>\": \"<value>\"}}}} — not a string containing "
			+ "JSON text. The value was not parsed: a JSON-encoded object is refused rather than "
			+ "silently decoded.");
		return true;
	}

	/// <summary>
	/// True when the parameter's declared type can only be bound from a JSON object — a dictionary or a
	/// composite record. <see cref="JsonElement"/> and other permissive types are excluded: they legally
	/// accept a string value, so a string there is not a caller mistake.
	/// </summary>
	private static bool ExpectsJsonObject(Type parameterType) {
		Type type = Nullable.GetUnderlyingType(parameterType) ?? parameterType;
		if (type == typeof(JsonElement) || type == typeof(JsonDocument) || type == typeof(object)) {
			return false;
		}
		if (IsJsonObjectContract(type)) {
			return true;
		}
		return McpToolArgumentSupport.IsCompositeArgsParameter(type)
			&& GetJsonPropertyNames(type).Count > 0;
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

	private static string BuildDeserializationErrorMessage(
		string? toolName,
		Type parameterType,
		string? argumentName,
		Exception exception) {
		string preciseArgumentName = GetPreciseArgumentName(parameterType, argumentName, exception);
		string toolLabel = toolName ?? UnknownToolName;
		string message;
		if (string.IsNullOrWhiteSpace(argumentName)) {
			message = $"invalid-parameter-type: arguments for MCP tool '{toolLabel}' must match the documented shape "
				+ $"(expected {GetExpectedJsonType(parameterType, preciseArgumentName)}).";
		} else if (IsNestedBindingFailure(parameterType, exception)) {
			// The named property's OWN value is not what failed: the caller did send the array or object
			// the contract asks for, and the incompatible value sits somewhere inside it. Reporting the
			// CLR type of the outer property here ("must be an array") names a correction the caller has
			// already made, so the message says where to look instead.
			message = $"invalid-parameter-type: argument '{preciseArgumentName}' for MCP tool '{toolLabel}' "
				+ "contains a value that does not match the documented shape.";
		} else {
			message = $"invalid-parameter-type: argument '{preciseArgumentName}' for MCP tool '{toolLabel}' "
				+ $"must be {GetExpectedJsonType(parameterType, preciseArgumentName)}.";
		}
		return $"{message} Received an incompatible JSON value.";
	}

	/// <summary>
	/// True when the failing JSON path continues BELOW the named property, so the incompatible value is
	/// nested inside it rather than being that property's own value.
	/// </summary>
	/// <remarks>
	/// <see cref="GetPreciseArgumentName"/> reports only the first path segment, so
	/// <c>$.rules[0].actions[0].type</c> is reported as <c>rules</c>. Without this distinction the message
	/// then describes the CLR type of <c>rules</c>, which the caller already supplied correctly.
	/// </remarks>
	private static bool IsNestedBindingFailure(Type parameterType, Exception exception) {
		if (!parameterType.IsClass || parameterType == typeof(string)
			|| exception is not JsonException { Path: { } path }
			|| !path.StartsWith("$.", StringComparison.Ordinal)) {
			return false;
		}
		string remainder = path[2..];
		int boundary = remainder.IndexOfAny(['.', '[']);
		if (boundary < 0) {
			return false;
		}
		// Only when the first segment actually resolved to a documented property. Otherwise the reported
		// name is the outer argument itself and "must be <type>" remains the accurate advice.
		return GetJsonPropertyNames(parameterType)
			.Contains(remainder[..boundary], StringComparer.OrdinalIgnoreCase);
	}

	private static string GetPreciseArgumentName(Type parameterType, string? argumentName, Exception exception) {
		if (parameterType.IsClass && parameterType != typeof(string)
			&& exception is JsonException { Path: { } path }
			&& path.StartsWith("$.", StringComparison.Ordinal)) {
			string propertyName = path[2..].Split(['.', '['], 2)[0];
			if (GetJsonPropertyNames(parameterType).Contains(propertyName, StringComparer.OrdinalIgnoreCase)) {
				return propertyName;
			}
		}
		return argumentName ?? string.Empty;
	}

	private static string GetExpectedJsonType(Type parameterType, string argumentName) {
		PropertyInfo? property = parameterType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.FirstOrDefault(candidate =>
				(GetJsonPropertyName(candidate) ?? candidate.Name).Equals(argumentName, StringComparison.OrdinalIgnoreCase));
		Type type = property?.PropertyType ?? parameterType;
		if (Nullable.GetUnderlyingType(type) is { } underlyingType) {
			type = underlyingType;
		}
		if (type == typeof(string)) return "a string";
		if (type == typeof(bool)) return "a boolean";
		// A dictionary IS an IEnumerable, so it has to be recognized BEFORE the enumerable branch below.
		// Without this, a contract taking Dictionary<string, JsonElement> — clio-run's own `args` — was
		// reported as "an array", telling the caller to resend the very shape that had just failed.
		if (IsJsonObjectContract(type)) return "an object";
		if (type.IsArray || (typeof(System.Collections.IEnumerable).IsAssignableFrom(type) && type != typeof(string))) return "an array";
		if (type.IsPrimitive || type == typeof(decimal)) return "a number";
		return "an object";
	}

	/// <summary>
	/// True when the CLR type is carried on the wire as a JSON object rather than a JSON array.
	/// </summary>
	private static bool IsJsonObjectContract(Type type) =>
		typeof(System.Collections.IDictionary).IsAssignableFrom(type)
		//Type.GetInterfaces() lists the interfaces a type implements, never the type itself, so a
		//property declared AS IReadOnlyDictionary<string,string> - ApplicationCreateArgs'
		//title-localizations, for one - fell through to the IEnumerable branch and was reported as
		//"an array". The declared type has to be tested on its own before the implemented ones.
		|| IsDictionaryDefinition(type)
		|| type.GetInterfaces().Any(IsDictionaryDefinition);

	private static bool IsDictionaryDefinition(Type candidate) =>
		candidate.IsGenericType
		&& (candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>)
			|| candidate.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>));

	private static string? GetJsonPropertyName(PropertyInfo property) =>
		property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name;

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
	/// <remarks>
	/// ENG-95885: for a single-composite-args tool this hint is now MOSTLY SHADOWED — the upstream
	/// <see cref="TryRefuseOrRewriteCallArguments"/> pass has already rewritten a canonical-flat payload
	/// into the wrapper (or refused an unknown/ambiguous one) before this runs. It still covers the
	/// residual MULTI-bindable-parameter tool that carries a composite parameter (which the normalizer's
	/// single-composite trigger gate deliberately skips). Do not delete it as dead code without proving
	/// that residual is unreachable for resident tools.
	/// </remarks>
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

			// Shared definition — see McpToolArgumentSupport.IsBindableToolParameter (ENG-95885). A
			// non-bindable (framework-injected) parameter carries no caller-supplied wire fields.
			if (!McpToolArgumentSupport.IsBindableToolParameter(parameter)) {
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
