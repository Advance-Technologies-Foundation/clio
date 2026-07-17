using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Shared executor behind the <c>clio-run</c> / <c>clio-run-destructive</c> tools: resolves the target
/// MCP tool by NAME from the full tool catalog, enforces the destructiveness gate for the requesting
/// surface, and dispatches by invoking the target tool through the SDK (reusing its argument binding
/// and per-call DI instantiation). This reaches ANY registered MCP tool — including the long tail
/// hidden from <c>tools/list</c> in lazy mode and the MCP-only tools that have no CLI <c>[Verb]</c>.
/// </summary>
public interface IClioRunExecutor {
	/// <summary>
	/// Runs the MCP tool named <paramref name="command"/> with <paramref name="args"/> on the
	/// requested surface, in the context of the calling <c>clio-run</c> request.
	/// </summary>
	/// <param name="command">
	/// The MCP tool name (as advertised in full-mode <c>tools/list</c>). May be <c>null</c> when the
	/// caller sent the wrapped call shape <c>{"args":{"command":"&lt;tool&gt;", ...}}</c>; the executor
	/// recovers the real command from <paramref name="args"/> in that case.
	/// </param>
	/// <param name="args">Free-form JSON args object forwarded to the target tool.</param>
	/// <param name="destructiveSurface">
	/// <c>true</c> when invoked from <c>clio-run-destructive</c>; <c>false</c> from <c>clio-run</c>.
	/// </param>
	/// <param name="callContext">The calling tool's request context (provides the live MCP server).</param>
	/// <param name="cancellationToken">Cancellation token for the dispatched invocation.</param>
	/// <returns>The target tool's <see cref="CallToolResult"/>, or a structured error result.</returns>
	ValueTask<CallToolResult> RunAsync(
		string? command,
		JsonElement? args,
		bool destructiveSurface,
		RequestContext<CallToolRequestParams> callContext,
		CancellationToken cancellationToken);

	/// <summary>
	/// Dispatches an ALREADY-RESOLVED tool with the caller's NATIVE <c>tools/call</c> arguments — the
	/// entry point for the durable (forgiving) call-tool handler. Unlike <see cref="RunAsync"/>, the
	/// request's <see cref="CallToolRequestParams.Arguments"/> are forwarded verbatim (they are already
	/// in the SDK's bound shape, keyed by parameter name), so a single-complex-parameter tool is never
	/// re-wrapped into <c>{"args":{"args":{…}}}</c>. Request metadata (<c>_meta</c>, progress token,
	/// task metadata) is preserved on the retargeted params, and the original
	/// <c>Params</c>/<c>MatchedPrimitive</c> are restored after the call.
	/// </summary>
	/// <param name="tool">The resolved target tool (from <see cref="IMcpToolInvokerRegistry"/>).</param>
	/// <param name="canonicalName">The canonical tool name the dispatch is recorded under.</param>
	/// <param name="callContext">The original unmatched request's context (native arguments intact).</param>
	/// <param name="cancellationToken">Cancellation token for the dispatched invocation.</param>
	/// <returns>The target tool's <see cref="CallToolResult"/> (failure text redacted), or a structured error result.</returns>
	ValueTask<CallToolResult> InvokeResolvedAsync(
		McpServerTool tool,
		string canonicalName,
		RequestContext<CallToolRequestParams> callContext,
		CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class ClioRunExecutor(
	IMcpToolInvokerRegistry toolRegistry,
	IMcpToolCompatibilityCatalog compatibilityCatalog) : IClioRunExecutor {

	/// <inheritdoc />
	public async ValueTask<CallToolResult> RunAsync(
		string? command,
		JsonElement? args,
		bool destructiveSurface,
		RequestContext<CallToolRequestParams> callContext,
		CancellationToken cancellationToken) {
		ArgumentNullException.ThrowIfNull(callContext);

		// Wrapped-form tolerance: clio-run / clio-run-destructive declare TWO top-level params
		// (command + args), so their real call shape is {"command":"X","args":{...}}. But most clio
		// tools take ONE record param named `args`, so the SDK wraps everything under `args`. An agent
		// habituated to that wrapper sends {"args":{"command":"X", ...}} — leaving top-level `command`
		// null. `command` is now optional (string? = null) so the SDK no longer hard-rejects that call
		// before the method runs; recover the real command/args here so BOTH call shapes work. This runs
		// BEFORE the recursion guard and unknown-tool path, so the RECOVERED command is what those see.
		if (string.IsNullOrWhiteSpace(command)) {
			(command, args) = RecoverWrappedCall(args);
		}

		if (string.IsNullOrWhiteSpace(command)) {
			return Error(
				"Error: 'command' is required — the target clio MCP tool name (kebab-case). " +
				"Call shape: {\"command\":\"<tool>\",\"args\":{...}}.");
		}
		string toolName = command.Trim();

		// Durable-name resolution FIRST: when the literal name misses the registry, consult the
		// compatibility catalog — a deprecated/renamed alias resolves to its canonical tool so guidance
		// written against the old name keeps working. The catalog is the single source of truth for such
		// renames (the MCP analogue of the CLI's hidden-alias policy). Canonicalizing BEFORE the
		// executor guard below means the guard always sees the final dispatch target, so a
		// (mis-)declared alias could never smuggle an executor past it; the catalog constructor
		// additionally rejects executor names outright.
		if (!toolRegistry.TryGetTool(toolName, out McpServerTool _)
			&& compatibilityCatalog.TryResolveAlias(toolName, out string canonicalAlias, out McpToolCompatibilityEntry _)) {
			toolName = canonicalAlias;
		}

		// Reject dispatch to the executors themselves (self- or cross-dispatch). The registry
		// contains clio-run / clio-run-destructive, so without this guard a client could nest
		// clio-run -> clio-run -> ... and recurse until cancellation/resource exhaustion (DoS).
		// Match the registry's own name resolution (OrdinalIgnoreCase + Trim, see McpToolInvokerRegistry)
		// so a different-cased alias (e.g. "Clio-Run") cannot slip past the guard and re-enter RunAsync.
		if (string.Equals(toolName, ClioRunTool.ToolName, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(toolName, ClioRunDestructiveTool.ToolName, StringComparison.OrdinalIgnoreCase)) {
			return Error(
				$"Error: '{toolName}' cannot be a clio-run target (self/cross-dispatch is not allowed). " +
				"Pass a concrete clio MCP tool name as 'command'.");
		}

		if (!toolRegistry.TryGetTool(toolName, out McpServerTool tool)) {
			// The long tail clio-run targets is hidden from tools/list, so agents frequently GUESS the
			// name and miss by a typo. Append a "did you mean" shortlist of the nearest REAL tool names so
			// the agent can self-correct without an extra discovery round-trip. Only the RANKING (Levenshtein
			// distance then ordinal) matches the BuildSuggestions helper in ToolContractGetTool. The candidate
			// SOURCE SET is caller-specific and intentionally divergent — here it is the registry's invokable
			// names + the reflection catalog (the hidden long tail clio-run targets), deduped case-insensitively.
			IReadOnlyList<string> suggestions = BuildSuggestions(toolName);
			string didYouMean = suggestions.Count > 0
				? $" Did you mean: {string.Join(", ", suggestions)}?"
				: string.Empty;
			// Always append the in-band discovery hint (regardless of whether there were near-miss
			// suggestions) so an agent whose guesses missed has an explicit path to the full catalog: the
			// compact index from get-tool-contract (no args). `didYouMean` is empty or starts with its own
			// leading space, so a single space before the hint keeps spacing correct in both shapes.
			return Error(
				$"Error: unknown tool '{toolName}'. It is not a registered clio MCP tool.{didYouMean} {ToolContractGetTool.DiscoveryHint}");
		}

		// No destructive-vs-safe refusal: field testing showed capable models loop indefinitely on the
		// "use the other executor" redirect (20+ redirects, zero actions) and never build. Both clio-run
		// and clio-run-destructive now execute ANY tool directly; destructive safety is enforced at the
		// HOST level via the tools' Destructive=true flag (the host prompts/those gated commands behave
		// exactly as they did in the pre-lazy full catalog). `destructiveSurface` is retained on the
		// signature for back-compat but no longer routes/refuses.
		CallToolRequestParams childParams;
		try {
			childParams = BuildChildParams(toolName, tool, args);
		}
		catch (ArgumentException ex) {
			return Error(SensitiveErrorTextRedactor.Redact($"Error: {ex.Message}"));
		}

		// ENG-93373: bound a retry-safe (read-only, or the get-page local-write read) inner dispatch by the
		// read-response deadline, so a read reached THROUGH clio-run — the PRIMARY long-tail read vector, since
		// non-resident tools are invoked via clio-run per core-rules — is bounded exactly as a direct call is.
		// Uses the SAME gate (McpReadDeadlineGate, via the registry) as the matched filter and the durable
		// handler. clio-run / clio-run-destructive are themselves Destructive=true, so the outer call is never
		// deadline-wrapped by the filter — no double-wrapping here.
		// On a deadline the wrapped DispatchAsync is abandoned; it restores callContext.Params/MatchedPrimitive
		// in its finally on the pool thread AFTER this method returns. This is benign under the single-session
		// model: each request owns a fresh RequestContext, so the late restore writes to a context no live
		// request reads (same accepted detach trade-off documented on McpReadResponseDeadline).
		CallToolResult dispatched = toolRegistry.IsRetrySafe(toolName)
			? await McpReadResponseDeadline.RunAsync(
				toolName,
				token => DispatchAsync(tool, toolName, childParams, callContext, token),
				cancellationToken).ConfigureAwait(false)
			: await DispatchAsync(tool, toolName, childParams, callContext, cancellationToken)
				.ConfigureAwait(false);
		return AttachDispatchAudit(dispatched, toolName);
	}

	/// <inheritdoc />
	public async ValueTask<CallToolResult> InvokeResolvedAsync(
		McpServerTool tool,
		string canonicalName,
		RequestContext<CallToolRequestParams> callContext,
		CancellationToken cancellationToken) {
		ArgumentNullException.ThrowIfNull(tool);
		ArgumentNullException.ThrowIfNull(callContext);

		// The native call's Arguments are ALREADY in the SDK's bound shape (keyed by parameter name), so
		// they are forwarded verbatim — running them through BuildChildParams would re-wrap a
		// single-complex-parameter tool's record under its parameter name a second time
		// ({"args":{"args":{…}}}) and break deserialization. Protocol `_meta` (incl. the progress token,
		// which RequestParams exposes as a read-only view over Meta["progressToken"]) is NOT copied here:
		// DispatchAsync is the single owner of Meta forwarding for BOTH callers, so it carries the caller's
		// Meta onto childParams just before dispatch. Task-augmentation metadata is copied here because
		// DispatchAsync does not touch it.
		CallToolRequestParams originalParams = callContext.Params;
		CallToolRequestParams childParams = new() {
			Name = canonicalName,
			Arguments = originalParams?.Arguments,
#pragma warning disable MCPEXP001 // Task-augmentation metadata is SDK-experimental; copied verbatim so a
			// task-augmented direct call keeps its task identity through the forgiving dispatch. If the SDK
			// removes/renames the property this assignment is the single line to update.
			Task = originalParams?.Task
#pragma warning restore MCPEXP001
		};
		CallToolResult result = await DispatchAsync(tool, canonicalName, childParams, callContext, cancellationToken)
			.ConfigureAwait(false);
		RedactFailureContent(result);
		return result;
	}

	// Dispatches within the SAME request context (same server/session/services), retargeting it at the
	// resolved tool and its arguments. Reusing the caller's context — rather than constructing a new
	// one — carries the live MCP server forward so the SDK's InvokeAsync can build and run the real
	// tool, and avoids the RequestContext constructor's non-null-server guard. The original
	// Params/MatchedPrimitive are restored in `finally`, so the outer pipeline (filters, logging) always
	// observes the caller's own request — not the retargeted child — regardless of success, failure, or
	// cancellation.
	private static async ValueTask<CallToolResult> DispatchAsync(
		McpServerTool tool,
		string toolName,
		CallToolRequestParams childParams,
		RequestContext<CallToolRequestParams> callContext,
		CancellationToken cancellationToken) {
		CallToolRequestParams originalParams = callContext.Params;
		IMcpServerPrimitive originalPrimitive = callContext.MatchedPrimitive;
		// This is the SINGLE owner of _meta forwarding for BOTH callers (the clio-run entry path and
		// InvokeResolvedAsync) — do not delete it, and do not re-add a Meta copy at either call site.
		// clio-run builds childParams via BuildChildParams, which constructs a fresh CallToolRequestParams
		// (Name + Arguments only) with a null Meta; InvokeResolvedAsync builds it with Name/Arguments/Task
		// but deliberately leaves Meta to this line. Without it the caller's ProgressToken — which
		// RequestParams exposes as a read-only view over Meta["progressToken"] — is dropped, and any
		// notifications/progress a dispatched tool emits (e.g. deploy-creatio / uninstall-creatio typed stage
		// events) is silently lost (the tool's forwarder reads Params.ProgressToken and no-ops when it is
		// null). Carrying Meta forward here preserves the progress token and any other _meta for both paths.
		// Read it BEFORE reassigning callContext.Params.
		childParams.Meta = originalParams?.Meta;
		callContext.Params = childParams;
		callContext.MatchedPrimitive = tool;
		try {
			return await tool.InvokeAsync(callContext, cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) {
			// Honour cooperative cancellation/timeout — let it propagate so the host sees a cancellation,
			// not a masked tool error.
			throw;
		}
		catch (Exception ex) when (!McpExceptionPolicy.IsUnrecoverable(ex)) {
			// Without this catch the exception escapes to the outer McpToolErrorFilter, which the agent
			// sees as a generic "An error occurred invoking '<tool>'" with no detail — so it cannot
			// self-correct. Surface the real (inner-most) message as a structured Error result instead
			// (field-test defect #3), redacted so paths/URIs/credentials never reach the transcript
			// (mirrors McpToolErrorFilter; SensitiveErrorTextRedactor is the single redaction rule).
			// A fatal/programming-defect exception (OOM/NRE/…) is NOT masked as a tool failure here — it
			// propagates to the top-level request boundary (McpToolErrorFilter).
			return Error($"Error: tool '{toolName}' failed: {SensitiveErrorTextRedactor.Redact(GetInnermostMessage(ex))}");
		}
		finally {
			callContext.Params = originalParams;
			callContext.MatchedPrimitive = originalPrimitive;
		}
	}

	// Records WHAT was actually dispatched and its resolved destructiveness into the result's out-of-band
	// `_meta` (never the content the model reads), so a host that auto-allows clio-run still has an audit
	// trail of the concrete tool it ran. Because clio-run and clio-run-destructive collapse the
	// safe-versus-destructive distinction, the inner tool's own annotation never reaches the host for a
	// per-call prompt; this echo is the ADR-accepted residual audit mitigation. A dedicated `clio-run`
	// key keeps a tool's own `_meta` preserved.
	private CallToolResult AttachDispatchAudit(CallToolResult result, string toolName) {
		if (result is null) {
			return result;
		}
		RedactFailureContent(result);
		result.Meta ??= new JsonObject();
		result.Meta["clio-run"] = new JsonObject {
			["dispatchedTool"] = toolName,
			["destructive"] = toolRegistry.IsDestructive(toolName)
		};
		return result;
	}

	// Failure-content redaction backstop shared by the clio-run path and the durable (forgiving)
	// handler's native dispatch path. Many clio tools catch internally and RETURN a structured failure
	// rather than throwing, so the throw-path redaction (the DispatchAsync catch block) never sees them.
	// Three shapes leak:
	//   1. CallToolResult { IsError = true } carrying raw text (e.g. ODataWriteResponse-style throws
	//      re-wrapped by a tool, or tools that build the error result by hand).
	//   2. A typed POCO envelope { success: false, error: "<raw IApplicationClient message>" } that the
	//      SDK serialises into a JSON TextContentBlock WITHOUT setting IsError — the long-tail default.
	//   3. The same POCO surfaced via StructuredContent when a tool opts into structured output.
	// Only failure content is touched; a successful payload is never scrubbed (it could carry legitimate
	// host/path data). SensitiveErrorTextRedactor is the single redaction rule.
	internal static void RedactFailureContent(CallToolResult result) {
		if (result is null) {
			return;
		}
		JsonNode structured = ToMutableNode(result.StructuredContent);
		if (!IsFailureResult(result, structured)) {
			return;
		}
		if (result.Content is not null) {
			foreach (TextContentBlock textBlock in result.Content.OfType<TextContentBlock>()) {
				textBlock.Text = SensitiveErrorTextRedactor.Redact(textBlock.Text);
			}
		}
		if (structured is not null && RedactStructuredErrorFields(structured)) {
			// StructuredContent is an immutable JsonElement, so write the scrubbed graph back as a fresh
			// element. Only done when something was actually redacted, so a clean payload is untouched.
			result.StructuredContent = JsonSerializer.SerializeToElement(structured);
		}
	}

	// Converts the SDK's immutable StructuredContent (JsonElement?) into a mutable JsonNode graph so error
	// fields can be rewritten. Returns null when there is no object/array payload to inspect.
	private static JsonNode ToMutableNode(JsonElement? structuredContent) {
		if (structuredContent is not { } element || element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) {
			return null;
		}
		return JsonNode.Parse(element.GetRawText());
	}

	// Conservative failure detector for the audit backstop. A result counts as a failure when any of these
	// hold: the SDK flagged it as an error, its StructuredContent payload carries a failure signal (a false
	// success flag or a non-empty error field), or a JSON text-content block (the long-tail default, where
	// the POCO is serialised into Content rather than StructuredContent) carries the same signal. Anything
	// else — a normal success payload, or one with no such markers — is left untouched so legitimate data
	// is never redacted.
	private static bool IsFailureResult(CallToolResult result, JsonNode structured) {
		if (result.IsError == true) {
			return true;
		}
		if (PayloadSignalsFailure(structured)) {
			return true;
		}
		if (result.Content is null) {
			return false;
		}
		return result.Content.OfType<TextContentBlock>()
			.Any(textBlock => TextContentSignalsFailure(textBlock.Text));
	}

	// Parses a text-content block as JSON (the SDK's default serialised POCO shape) and applies the same
	// success:false / non-empty-error failure test. Non-JSON or unparseable text is never treated as a
	// failure, so plain prose success output is never scrubbed.
	private static bool TextContentSignalsFailure(string text) {
		if (string.IsNullOrWhiteSpace(text)) {
			return false;
		}
		ReadOnlySpan<char> trimmed = text.AsSpan().TrimStart();
		if (trimmed.IsEmpty || trimmed[0] != '{') {
			return false;
		}
		try {
			return PayloadSignalsFailure(JsonNode.Parse(text));
		}
		catch (JsonException) {
			return false;
		}
	}

	private static bool PayloadSignalsFailure(JsonNode payload) {
		if (payload is not JsonObject obj) {
			return false;
		}
		foreach (KeyValuePair<string, JsonNode> property in obj) {
			if (string.Equals(property.Key, "success", StringComparison.OrdinalIgnoreCase)
				&& property.Value is JsonValue successValue
				&& successValue.TryGetValue(out bool success)
				&& !success) {
				return true;
			}
			// A normal successful discovery result commonly carries a non-empty `message` or `detail`.
			// Those fields are redactable AFTER another failure signal is established, but their presence
			// alone must never classify the entire payload as a failure (that turned real build paths into
			// the literal `[redacted-path]` before the Ring could use them).
			if (string.Equals(property.Key, "error", StringComparison.OrdinalIgnoreCase)
				&& property.Value is JsonValue errorValue
				&& errorValue.TryGetValue(out string errorText)
				&& !string.IsNullOrWhiteSpace(errorText)) {
				return true;
			}
		}
		return false;
	}

	// Redacts the string value of every error-like field anywhere in the structured payload graph, so a
	// raw IApplicationClient message that the SDK placed in StructuredContent (rather than only the text
	// Content) is scrubbed too. Only error-named fields are rewritten; all other data is preserved verbatim.
	// Returns true when at least one field was changed.
	private static bool RedactStructuredErrorFields(JsonNode node) => node switch {
		JsonObject obj => RedactObjectErrorFields(obj),
		JsonArray array => RedactArrayErrorFields(array),
		_ => false
	};

	// An error-named string field is redacted in place; any other field is recursed into. `||` short-
	// circuits so a redacted error field is not also recursed (it is a leaf string), matching the prior
	// if/else; `|=` accumulates without a filtering branch.
	private static bool RedactObjectErrorFields(JsonObject obj) {
		bool changed = false;
		foreach (string key in obj.Select(property => property.Key).ToList()) {
			changed |= TryRedactErrorField(obj, key) || RedactStructuredErrorFields(obj[key]);
		}
		return changed;
	}

	private static bool RedactArrayErrorFields(JsonArray array) {
		bool changed = false;
		foreach (JsonNode item in array) {
			changed |= RedactStructuredErrorFields(item);
		}
		return changed;
	}

	// Redacts the value of a single error-named string field in place. Returns false (leaving the caller to
	// recurse) when the key is not an error field, the value is not a non-empty string, or redaction is a no-op.
	private static bool TryRedactErrorField(JsonObject obj, string key) {
		if (!IsErrorFieldName(key)
			|| obj[key] is not JsonValue value
			|| !value.TryGetValue(out string text)
			|| string.IsNullOrEmpty(text)) {
			return false;
		}
		string redacted = SensitiveErrorTextRedactor.Redact(text);
		if (string.Equals(redacted, text, StringComparison.Ordinal)) {
			return false;
		}
		obj[key] = redacted;
		return true;
	}

	// The failure-bearing field names a long-tail tool may use to carry the raw failure detail. A
	// result is classified a failure once (IsFailureResult); the redaction backstop must then scrub
	// EVERY field a tool might have parked that detail in — not just one literally named "error" —
	// because the raw text (hosts, URIs, paths, credentials) leaks the same regardless of the key.
	// Matched case-insensitively.
	private static readonly System.Collections.Generic.HashSet<string> FailureFieldNames =
		new(StringComparer.OrdinalIgnoreCase) {
			"error", "message", "detail", "details", "errorInfo", "exception", "stackTrace", "reason"
		};

	private static bool IsErrorFieldName(string key) => FailureFieldNames.Contains(key);

	// Unwraps to the inner-most exception's message so the surfaced detail is the actual failure cause
	// rather than a generic wrapper (e.g. TargetInvocationException) added by the dispatch machinery.
	private static string GetInnermostMessage(Exception ex) {
		Exception current = ex;
		while (current.InnerException is not null) {
			current = current.InnerException;
		}
		return current.Message;
	}

	// Recovers the real (command, args) pair from the WRAPPED call shape an agent sends when it treats
	// clio-run like a single-`args`-record tool. Two wrapped variants are handled:
	//   * wrapped-with-inner-args: {"args":{"command":"X","args":{...target params...}}}
	//       -> command = "X", target args = the inner "args" object.
	//   * wrapped-flat: {"args":{"command":"X", ...target params...}}
	//       -> command = "X", target args = the SAME object MINUS the "command" key (re-serialized).
	// Recovery only fires when `args` is a JSON object carrying a STRING "command" property. Anything
	// else (primitive/array args, object without a string "command") yields (null, original args) so
	// the caller falls through to the structured "missing command" error and nothing is dispatched.
	private static (string? command, JsonElement? args) RecoverWrappedCall(JsonElement? args) {
		if (args is not { ValueKind: JsonValueKind.Object } wrapper) {
			return (null, args);
		}
		if (!wrapper.TryGetProperty("command", out JsonElement commandElement) ||
			commandElement.ValueKind != JsonValueKind.String) {
			return (null, args);
		}
		string? recoveredCommand = commandElement.GetString();
		if (string.IsNullOrWhiteSpace(recoveredCommand)) {
			return (null, args);
		}

		// Inner-args variant wins when an "args" property is present: forward it verbatim as the target
		// tool's arguments. Otherwise treat the wrapper itself as the flat target args and drop the
		// "command" key so it is not forwarded as a (non-existent) target parameter.
		if (wrapper.TryGetProperty("args", out JsonElement innerArgs)) {
			return (recoveredCommand, innerArgs.Clone());
		}
		return (recoveredCommand, StripCommandKey(wrapper));
	}

	// Rebuilds the wrapped object without its top-level "command" key, so the remaining properties become
	// the flat target args. Returns Null kind (treated downstream as "no args") when nothing else remains.
	private static JsonElement StripCommandKey(JsonElement wrapper) {
		JsonObject stripped = new();
		foreach (JsonProperty property in wrapper.EnumerateObject()) {
			if (string.Equals(property.Name, "command", StringComparison.Ordinal)) {
				continue;
			}
			stripped[property.Name] = JsonNode.Parse(property.Value.GetRawText());
		}
		using JsonDocument document = JsonDocument.Parse(stripped.ToJsonString());
		return document.RootElement.Clone();
	}

	// Maps the caller's free-form `args` object onto the target tool's argument dictionary. Most clio
	// tools take a single typed args record parameter (named `args`); for those the whole object is
	// wrapped under that parameter name. Tools that take multiple named scalar parameters receive the
	// args object passed through as-is (each top-level key is a parameter name).
	private static CallToolRequestParams BuildChildParams(string toolName, McpServerTool tool, JsonElement? args) {
		Dictionary<string, JsonElement> arguments = new(StringComparer.Ordinal);
		if (args is { ValueKind: JsonValueKind.Object } argsObject) {
			MethodInfo method = tool.Metadata.OfType<MethodInfo>().FirstOrDefault();
			IReadOnlyList<ParameterInfo> boundParameters = method is null
				? []
				: method.GetParameters().Where(IsBindableToolParameter).ToArray();

			if (boundParameters.Count == 1 && IsComplexArgsParameter(boundParameters[0].ParameterType)) {
				// Single complex args-record parameter (the common clio tool shape, e.g. SchemaSyncArgs
				// args): wrap the whole object under its name so the SDK binds it as one argument.
				arguments[boundParameters[0].Name!] = argsObject.Clone();
			}
			else {
				// Scalar/multi-parameter tools: each top-level key is an individual parameter.
				foreach (JsonProperty property in argsObject.EnumerateObject()) {
					arguments[property.Name] = property.Value.Clone();
				}
			}
		}
		else if (args is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) }) {
			throw new ArgumentException(
				$"'args' for tool '{toolName}' must be a JSON object whose keys are the tool's argument names.");
		}
		return new CallToolRequestParams { Name = toolName, Arguments = arguments };
	}

	// A complex args parameter is a non-string reference/record type (e.g. SchemaSyncArgs) that the
	// tool expects to receive as a single bound argument object; scalars (string, bool, numbers, enums)
	// are not, so a single scalar parameter is bound by name from the args object's matching key.
	private static bool IsComplexArgsParameter(Type type) {
		Type underlying = Nullable.GetUnderlyingType(type) ?? type;
		return underlying != typeof(string) && !underlying.IsValueType;
	}

	// Parameters the SDK injects from the request context (RequestContext, CancellationToken,
	// IServiceProvider, McpServer, etc.) are not bound from the arguments object, so they are excluded
	// when deciding whether a tool exposes a single user-supplied parameter.
	private static bool IsBindableToolParameter(ParameterInfo parameter) {
		Type type = parameter.ParameterType;
		if (type == typeof(CancellationToken) || type == typeof(IServiceProvider) ||
			typeof(ModelContextProtocol.Server.McpServer).IsAssignableFrom(type)) {
			return false;
		}
		return !(type.IsGenericType && type.GetGenericTypeDefinition() == typeof(RequestContext<>));
	}

	// Top-3 nearest real tool names for an unknown `command`, ordered by Levenshtein distance to the
	// requested name then ordinally by name — the same ranking the BuildSuggestions helper in
	// ToolContractGetTool uses. The candidate source set here is caller-specific (intentionally divergent):
	// it is the FULL invokable name
	// set — the registry's invokable names (the hidden long tail clio-run targets) unioned with the
	// reflection catalog — deduped case-insensitively. The executor names themselves are excluded so a
	// near-miss never suggests re-entering clio-run / clio-run-destructive.
	private IReadOnlyList<string> BuildSuggestions(string requestedName) =>
		BuildSuggestions(requestedName, toolRegistry);

	// Upper bound on the requested-name length fed into the O(n·m) Levenshtein ranking. This is a cold
	// error path (only reached on an unknown tool), but the requested name is caller-supplied and could be
	// arbitrarily long, so it is capped before ranking — mirroring the same 64-char cap the durable handler
	// applies when sanitizing the name for prose reflection.
	private const int MaxRequestedNameLengthForRanking = 64;

	// Static form shared with the durable (forgiving) call-tool handler, so both callers rank the same
	// candidate set with the same algorithm and never drift apart.
	internal static IReadOnlyList<string> BuildSuggestions(string requestedName, IMcpToolInvokerRegistry registry) {
		// Cap the caller-supplied name before it drives the per-candidate Levenshtein computation, so an
		// oversized name cannot inflate the cost of the ranking on this cold error path.
		string rankingName = requestedName is { Length: > MaxRequestedNameLengthForRanking }
			? requestedName[..MaxRequestedNameLengthForRanking]
			: requestedName;
		return registry.ToolNames
			.Concat(McpToolSchemaCatalog.RegisteredToolNames)
			.Where(name => !string.IsNullOrWhiteSpace(name)
				&& !string.Equals(name, ClioRunTool.ToolName, StringComparison.OrdinalIgnoreCase)
				&& !string.Equals(name, ClioRunDestructiveTool.ToolName, StringComparison.OrdinalIgnoreCase))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(name => McpToolArgumentSupport.LevenshteinDistance(rankingName, name))
			.ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
			.Take(3)
			.ToArray();
	}


	private static CallToolResult Error(string message) =>
		new() {
			IsError = true,
			Content = [new TextContentBlock { Text = message }]
		};
}

/// <summary>
/// Single generic MCP executor for the long tail of clio tools hidden from <c>tools/list</c> in lazy
/// mode. Runs ANY tool (read or write/destructive) by name — the caller never has to choose a different
/// executor. Marked <see cref="McpServerToolAttribute.Destructive"/> so the host gates it; never
/// <see cref="McpServerToolAttribute.ReadOnly"/> / auto-approve.
/// </summary>
[McpServerToolType]
public sealed class ClioRunTool(IClioRunExecutor executor) {

	/// <summary>Stable MCP tool name for the generic executor.</summary>
	internal const string ToolName = "clio-run";

	/// <summary>
	/// Runs any clio MCP tool by name with free-form JSON arguments (read or write/destructive).
	/// </summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true)]
	[Description("Generic executor for clio MCP tools hidden from tools/list (the long tail). `command` is an MCP tool name (kebab-case, e.g. \"sync-schemas\", \"create-lookup\", \"execute-esq\", \"odata-read\") and `args` is the JSON arguments object that tool expects. Call shape: {\"command\":\"<tool>\",\"args\":{...}}. The wrapped shape {\"args\":{\"command\":\"<tool>\",\"args\":{...}}} is also accepted. Runs ANY tool — including write/destructive ones — directly; you do NOT need a different executor. Unknown tool or invalid args return a structured Error result with the real cause. Marked destructive so the host can confirm; not auto-approved.")]
	public ValueTask<CallToolResult> Run(
		RequestContext<CallToolRequestParams> context,
		[Description("clio MCP tool name (kebab-case), e.g. \"execute-esq\"")] string? command = null,
		[Description("JSON arguments object the target tool expects")] Dictionary<string, JsonElement>? args = null,
		CancellationToken cancellationToken = default)
		=> executor.RunAsync(command, DictionaryToElement(args), destructiveSurface: false, context, cancellationToken);

	internal static JsonElement? DictionaryToElement(Dictionary<string, JsonElement>? dict)
		=> dict is not null ? JsonSerializer.SerializeToElement(dict) : null;
}

/// <summary>
/// Deprecated alias of <see cref="ClioRunTool"/> kept for back-compat. Behaves identically — runs ANY
/// tool by name (it no longer refuses non-destructive tools), so a caller that picks either executor
/// succeeds. Prefer <c>clio-run</c>.
/// </summary>
[McpServerToolType]
public sealed class ClioRunDestructiveTool(IClioRunExecutor executor) {

	/// <summary>Stable MCP tool name for the (deprecated) destructive alias.</summary>
	internal const string ToolName = "clio-run-destructive";

	/// <summary>
	/// Runs any clio MCP tool by name with free-form JSON arguments. Alias of <c>clio-run</c>.
	/// </summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true)]
	[Description("Deprecated alias of `clio-run` (identical behavior — runs ANY clio MCP tool by name, read or write/destructive). Kept so a caller that picks either executor succeeds. Prefer `clio-run`. `command` is an MCP tool name (kebab-case); `args` is the JSON arguments object that tool expects. Call shape: {\"command\":\"<tool>\",\"args\":{...}}; the wrapped shape {\"args\":{\"command\":\"<tool>\",\"args\":{...}}} is also accepted.")]
	public ValueTask<CallToolResult> Run(
		RequestContext<CallToolRequestParams> context,
		[Description("clio MCP tool name (kebab-case), e.g. \"sync-schemas\"")] string? command = null,
		[Description("JSON arguments object the target tool expects")] Dictionary<string, JsonElement>? args = null,
		CancellationToken cancellationToken = default)
		=> executor.RunAsync(command, ClioRunTool.DictionaryToElement(args), destructiveSurface: true, context, cancellationToken);
}
