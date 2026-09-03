using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Tools;
using CommandLine;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer;

/// <summary>
/// The durable (forgiving) unmatched-name handler behind <c>WithCallToolHandler</c> on the stdio MCP
/// host. The SDK invokes it only when a <c>tools/call</c> names a tool absent from the advertised
/// <c>tools/list</c> (a <c>ToolCollection</c> miss) — i.e. exactly the calls that used to dead-end with
/// an opaque "Unknown tool" after the lazy-schema split (PR #743) hid the long tail. It restores the
/// pre-lazy invocation contract: a real clio tool named directly is resolved (through the
/// <see cref="IMcpToolCompatibilityCatalog"/> for renamed/deprecated names) and either executed
/// (read-only) or answered with a structured <c>confirmation-required</c> retry shape (write-capable,
/// standing in for the host prompt it can no longer raise for an unadvertised tool). Unresolvable names
/// return structured, machine-readable errors with did-you-mean suggestions and a discovery hint instead
/// of a dead end.
/// </summary>
/// <remarks>
/// The gate keys on <c>readOnlyHint</c>, not <c>destructiveHint</c> (issue #953). Gating on
/// destructiveness let additive-only writes — <c>odata-create</c> and friends, correctly annotated
/// <c>Destructive=false</c> because the MCP contract reserves that flag for updates which can destroy
/// existing state — insert durable rows into a live environment with no confirmation at all. The tool
/// annotations were deliberately left spec-conformant; the gate is what changed.
/// </remarks>
public interface IMcpDurableCallToolHandler {
	/// <summary>
	/// Handles a <c>tools/call</c> whose name missed the advertised tool collection.
	/// </summary>
	/// <param name="request">The unmatched request's context (<c>MatchedPrimitive</c> is <c>null</c>).</param>
	/// <param name="cancellationToken">Cancellation token for the dispatched invocation.</param>
	/// <returns>The executed tool's result, or a structured outcome that self-corrects the caller.</returns>
	ValueTask<CallToolResult> HandleAsync(
		RequestContext<CallToolRequestParams> request,
		CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class McpDurableCallToolHandler(
	IMcpToolInvokerRegistry toolRegistry,
	IMcpToolCompatibilityCatalog compatibilityCatalog,
	IClioRunExecutor executor,
	IMcpExecutionRouter executionRouter,
	Relay.IMcpWorkerCallDispatcher workerCallDispatcher = null) : IMcpDurableCallToolHandler {

	// Stable machine-readable outcome codes (mirrored in StructuredContent, never only prose) so an
	// agent — or a downstream harness — can branch on the outcome without parsing English.
	internal const string CodeUnknownTool = "unknown-tool";
	internal const string CodeDeprecatedToolAlias = "deprecated-tool-alias";
	internal const string CodeCliVerbNotMcpTool = "cli-verb-not-mcp-tool";
	internal const string CodeForeignCommand = "foreign-command";
	internal const string CodeConfirmationRequired = "confirmation-required";
	internal const string CodeFeatureDisabled = "feature-disabled";

	// StructuredContent payload key shared by every outcome that echoes the caller's requested name.
	private const string RequestedNameKey = "requested-name";

	// Every CLI [Verb] name and alias in the assembly, for classifying a requested name that is a real
	// clio CLI verb but has no MCP tool. Deliberately unfiltered by feature toggles: the classification
	// message only says "this is a CLI verb, not an MCP tool", which is true either way.
	private static readonly Lazy<HashSet<string>> CliVerbNames = new(BuildCliVerbNames);

	/// <inheritdoc />
	public ValueTask<CallToolResult> HandleAsync(
		RequestContext<CallToolRequestParams> request,
		CancellationToken cancellationToken) =>
		HandleAsync(request, cancellationToken, readDeadline: null);

	// Same handler as the interface method, with an optional read-response deadline override. The
	// override exists ONLY as a test seam: production always calls the 2-arg interface method (see
	// BindingsModule's WithCallToolHandler wiring), which passes null so the retry-safe branch uses
	// McpReadResponseDeadline.DefaultReadDeadline. Tests pass a tiny deadline to exercise the timeout
	// branch of the durable/raw-name dispatch vector directly, without a 120 s wall-clock wait (ENG-93373).
	internal async ValueTask<CallToolResult> HandleAsync(
		RequestContext<CallToolRequestParams> request,
		CancellationToken cancellationToken,
		TimeSpan? readDeadline) {
		// This throw is intentional and does NOT contradict the return-not-thrown invariant below: a null
		// request is a genuine SDK-contract violation (the SDK never invokes the handler with one), not a
		// tool OUTCOME. Only expected outcomes are returned as structured results; a broken precondition
		// stays a fail-fast programming error.
		ArgumentNullException.ThrowIfNull(request);
		// The "unmatched only" property this handler is built on — and which the execution router now
		// DEPENDS on, because it is what makes the alias resolution below the authoritative canonical name —
		// was SDK behaviour documented in prose (see the type remarks and the alias comment below) and
		// enforced nowhere in code. A router that depends on an invariant must assert it rather than inherit
		// it: a matched primitive here would mean the SDK invoked the unmatched seam for an advertised tool,
		// which would route the call twice (dispatch site (a) already decided it) on a name this handler is
		// about to re-resolve. Same class as the null-request throw above: a broken precondition, not a tool
		// OUTCOME, so it fails fast instead of returning a structured result.
		if (request.MatchedPrimitive is not null) {
			throw new InvalidOperationException(
				"McpDurableCallToolHandler was invoked for a MATCHED primitive "
				+ $"('{request.MatchedPrimitive.Id}'). The SDK invokes this handler only on a tool-collection "
				+ "miss; a matched call is routed and bounded by McpToolErrorFilter instead.");
		}
		string correlationId = Guid.NewGuid().ToString();
		// Sanitized for prose reflection: an arbitrary caller-supplied name is later interpolated into
		// model-visible Content, so control characters are stripped and the length capped — a
		// newline/instruction-bearing "tool name" must not be able to inject trusted-looking text.
		string requestedName = SanitizeName(request.Params?.Name);
		if (string.IsNullOrWhiteSpace(requestedName)) {
			return UnknownToolResult(string.Empty, [], correlationId);
		}

		// Alias resolution first (the catalog is authoritative for renamed/deprecated names): the SDK
		// only invokes this handler on a ToolCollection miss, so a resident tool can never be shadowed,
		// and after the legacy duplicate methods are gone the registry no longer carries alias names.
		bool viaAlias = compatibilityCatalog.TryResolveAlias(
			requestedName, out string aliasCanonical, out McpToolCompatibilityEntry aliasEntry);
		if (viaAlias && aliasEntry.Owner == McpToolSurfaceOwner.Foreign) {
			return ForeignCommandResult(requestedName, aliasEntry, correlationId);
		}
		string canonicalName = viaAlias ? aliasCanonical : requestedName;

		if (!toolRegistry.TryGetTool(canonicalName, out McpServerTool tool)) {
			return ClassifyUnresolved(requestedName, canonicalName, viaAlias, aliasEntry, correlationId);
		}

		// The gate keys on WRITE-CAPABILITY (readOnlyHint), not on destructiveness. Those answer different
		// questions: an additive-only write such as odata-create is correctly Destructive=false per the MCP
		// contract ("false if the tool performs only additive updates"), yet it still inserts durable rows
		// into a live environment. Gating on Destructive therefore let every additive write run unprompted
		// on this path (issue #953). The annotations stay spec-conformant and untouched; only the gate
		// moved, so nothing but this path changed: read-only tools execute silently, every write-capable
		// tool returns a ready-to-retry executor shape instead (the advertised, host-gated executor).
		if (!toolRegistry.IsReadOnly(canonicalName)) {
			return ConfirmationRequiredResult(
				requestedName,
				canonicalName,
				request.Params?.Arguments,
				toolRegistry.IsDestructive(canonicalName),
				correlationId);
		}

		// ENG-95262 dispatch site (b) of three — the UNMATCHED path, and the only one of the three with a
		// real ordering constraint (ADR rule 9). It sits AFTER alias/registry resolution, so the router keys
		// on the canonical name rather than on the alias the caller happened to use, and AFTER the
		// write-capability gate above, which IS the destructive-confirmation seam rule 9 names — it keys on
		// readOnlyHint, not destructiveHint (issue #953), so an additive write such as odata-create is gated
		// too. Routing before it would hand a write to a worker and bypass host gating entirely.
		McpExecutionRoute route = executionRouter.Resolve(canonicalName, innerCommand: null);
		if (!route.ExecutesInProcess) {
			if (workerCallDispatcher is null) {
				// Fail-closed: a site with no dispatcher refuses rather than running a worker-routed call in
				// the host process, which would silently bypass the execution boundary.
				return McpExecutionRouter.WorkerPathNotWiredResult(route);
			}
			// Relayed under the CANONICAL name rather than the alias the caller used, so the worker resolves
			// the same tool this handler resolved (TC-U-402: both seams route, and they agree). When the
			// caller already used the canonical name the caller's own params object is handed over
			// untouched, which is the only way to keep every field it carries — `_meta` and its progress
			// token, and 2.2.0's InputResponses / RequestState — rather than the three this handler knows
			// to copy. The advisory is still attached afterwards, so a deprecated alias behaves the same
			// whether its tool ran in the host or in a worker.
			CallToolResult relayed = await workerCallDispatcher
				.DispatchAsync(route, RelayParams(request.Params, canonicalName),
					new Relay.McpServerParentSession(request.Server), cancellationToken)
				.ConfigureAwait(false);
			return AttachAdvisory(relayed, requestedName, canonicalName, viaAlias, correlationId);
		}

		// ENG-93373: bound a retry-safe (read-only, or the get-page local-write read) long-tail dispatch by
		// the read-response deadline — the fallback path for a non-resident read invoked by RAW name rather
		// than via clio-run. Uses the SAME gate (McpReadDeadlineGate, via the registry) so classification
		// never drifts across paths. Destructive tools never reach here — they returned confirmation-required
		// above — so a false here is simply a non-read non-destructive tool, intentionally left unbounded.
		// The abandon-restores-context caveat documented on the clio-run path applies equally here (both go
		// through DispatchAsync) and is benign under the single-session, fresh-per-request-context model.
		CallToolResult result = toolRegistry.IsRetrySafe(canonicalName)
			? await McpReadResponseDeadline.RunAsync(
				canonicalName,
				token => executor.InvokeResolvedAsync(tool, canonicalName, request, token),
				cancellationToken,
				readDeadline).ConfigureAwait(false)
			: await executor
				.InvokeResolvedAsync(tool, canonicalName, request, cancellationToken)
				.ConfigureAwait(false);
		return AttachAdvisory(result, requestedName, canonicalName, viaAlias, correlationId);
	}

	// An unresolved (post-alias) name is classified into the most actionable outcome rather than a
	// generic miss: a declared alias whose canonical is gone, a tool that exists but is feature-gated
	// off, a CLI-only verb, or a genuinely unknown name with did-you-mean candidates.
	private CallToolResult ClassifyUnresolved(
		string requestedName,
		string canonicalName,
		bool viaAlias,
		McpToolCompatibilityEntry aliasEntry,
		string correlationId) {
		if (viaAlias) {
			return DeprecatedAliasResult(requestedName, canonicalName, aliasEntry, correlationId);
		}
		// The schema catalog reflects the FULL assembly (no feature filter) while the invoker registry is
		// feature-filtered — so "in the catalog but not the registry" means the tool exists and is gated off.
		if (McpToolSchemaCatalog.RegisteredToolNames.Contains(canonicalName, StringComparer.OrdinalIgnoreCase)) {
			return FeatureDisabledResult(canonicalName, correlationId);
		}
		if (CliVerbNames.Value.Contains(canonicalName)) {
			return CliVerbResult(canonicalName, correlationId);
		}
		return UnknownToolResult(
			requestedName,
			ClioRunExecutor.BuildSuggestions(requestedName, toolRegistry),
			correlationId);
	}

	/// <summary>
	/// Returns the params to relay to a worker: the caller's own object when the name it carries is
	/// already canonical, or a minimal copy renamed to the canonical tool when the caller used an alias.
	/// </summary>
	/// <remarks>
	/// The identity case is the one worth protecting. Rebuilding params costs every field this handler does
	/// not know about — the SDK's own additions grow with each release — so it is done ONLY when something
	/// must actually change, and then it copies <c>Meta</c> explicitly, because dropping it drops the
	/// caller's progress token and ClioRing correlates on that token ordinally and fails silently.
	/// </remarks>
	/// <param name="original">The caller's params.</param>
	/// <param name="canonicalName">The canonical tool name the worker must execute.</param>
	/// <returns>The params to relay.</returns>
	private static CallToolRequestParams RelayParams(CallToolRequestParams original, string canonicalName) {
		if (original is null) {
			return new CallToolRequestParams { Name = canonicalName };
		}
		if (string.Equals(original.Name, canonicalName, StringComparison.Ordinal)) {
			return original;
		}
		// Carries every settable property across, for the same reason
		// McpWorkerCallDispatcher.WithoutParentSessionMetadata does: rebuilding only Name/Arguments/Meta
		// drops the retry payload, so an elicitation or retry resume that reaches a cohort tool through a
		// deprecated alias would silently lose its state.
		return new CallToolRequestParams {
			Name = canonicalName,
			Arguments = original.Arguments,
			InputResponses = original.InputResponses,
			RequestState = original.RequestState,
			Meta = original.Meta
		};
	}

	// Appends the model-visible advisory to Content (the channel the model actually reads — result
	// `_meta` is protocol metadata the host may drop) plus an out-of-band audit block, so a successful
	// forgiving execution both returns the payload AND steers the agent to the discoverable path next time.
	private static CallToolResult AttachAdvisory(
		CallToolResult result,
		string requestedName,
		string canonicalName,
		bool viaAlias,
		string correlationId) {
		// Defensive floor: the handler's contract is to always RETURN a non-null CallToolResult (the whole
		// return-not-thrown design rests on it). A well-behaved executor never returns null, but if it does
		// synthesize a structured error rather than propagating null up to the SDK.
		result ??= new CallToolResult {
			IsError = true,
			Content = [new TextContentBlock {
				Text = $"Tool '{canonicalName}' was dispatched but produced no result."
			}]
		};
		string aliasNote = viaAlias
			? $" Note: '{requestedName}' is a deprecated alias — use '{canonicalName}'."
			: string.Empty;
		// Word the advisory to the actual outcome: a dispatched tool can return IsError=true, and claiming it
		// "Executed" cleanly would mislead the agent about what happened.
		string outcomeVerb = result.IsError == true ? "Attempted" : "Executed";
		string advisory =
			$"[clio] {outcomeVerb} '{canonicalName}' directly; it is not advertised in tools/list.{aliasNote} " +
			$"Prefer the advertised executor next time: clio-run {{\"command\":\"{canonicalName}\",\"args\":{{…}}}} " +
			"(discover contracts via get-tool-contract).";
		List<ContentBlock> content = result.Content is null ? [] : [.. result.Content];
		content.Add(new TextContentBlock { Text = advisory });
		result.Content = content;
		result.Meta ??= new JsonObject();
		result.Meta["durable-invocation"] = new JsonObject {
			[RequestedNameKey] = requestedName,
			["dispatched-tool"] = canonicalName,
			["via-alias"] = viaAlias,
			["destructive"] = false,
			["correlation-id"] = correlationId
		};
		return result;
	}

	// The retry shape deliberately carries NO argument VALUES: the caller already holds its own
	// arguments (it just sent them), while echoing them back would create a second credential-bearing
	// copy in the response (e.g. restart-by-credentials would reflect the password into the transcript).
	// Only the argument NAMES are listed so the agent knows which keys to re-supply under `args`.
	private static CallToolResult ConfirmationRequiredResult(
		string requestedName,
		string canonicalName,
		IDictionary<string, JsonElement> nativeArguments,
		bool isDestructive,
		string correlationId) {
		JsonObject retryArguments = new() {
			["command"] = canonicalName
		};
		// The reason is spelled out per-outcome because the two cases are genuinely different and an agent
		// that cannot tell them apart cannot explain the refusal to a user: a destructive tool may overwrite
		// or delete existing state, while an additive-only write still creates durable state in the target.
		string reason = isDestructive
			? "can overwrite or delete existing state"
			: "writes durable state to the target (additive)";
		string text =
			$"Tool '{canonicalName}' {reason} and was NOT executed: it is not advertised in tools/list, " +
			"so the host cannot show its own confirmation prompt. To proceed, call the advertised executor " +
			$"`{ClioRunTool.ToolName}` with {{\"command\":\"{canonicalName}\",\"args\":{{…}}}} (re-supply your " +
			"own arguments under `args`) — the host gates that call.";
		return StructuredOutcome(CodeConfirmationRequired, text, correlationId, payload => {
			payload[RequestedNameKey] = requestedName;
			payload["canonical-name"] = canonicalName;
			// Reports the tool's ACTUAL destructiveHint rather than a hardcoded true: since the gate moved to
			// write-capability this outcome also covers additive-only writes, and flattening both into
			// "destructive: true" would misreport the annotation the host reads from tools/list.
			payload["destructive"] = isDestructive;
			payload["write-capable"] = true;
			if (nativeArguments is { Count: > 0 }) {
				payload["argument-names"] = ToJsonArray(nativeArguments.Keys.ToArray());
			}
			payload["retry"] = new JsonObject {
				["tool"] = ClioRunTool.ToolName,
				["arguments"] = retryArguments
			};
		});
	}

	private CallToolResult DeprecatedAliasResult(
		string requestedName,
		string canonicalName,
		McpToolCompatibilityEntry aliasEntry,
		string correlationId) {
		string replacement = aliasEntry.Replacement ?? canonicalName;
		IReadOnlyList<string> suggestions = ClioRunExecutor.BuildSuggestions(replacement, toolRegistry);
		string text =
			$"'{requestedName}' is a deprecated name; its replacement is '{replacement}', which is not " +
			$"currently invokable. {ToolContractGetTool.DiscoveryHint}";
		return StructuredOutcome(CodeDeprecatedToolAlias, text, correlationId, payload => {
			payload[RequestedNameKey] = requestedName;
			payload["replacement"] = replacement;
			payload["candidates"] = ToJsonArray(suggestions);
		});
	}

	private static CallToolResult FeatureDisabledResult(string canonicalName, string correlationId) {
		string text =
			$"Tool '{canonicalName}' exists but its feature is disabled on this installation. " +
			"Enable it first: clio experimental --name <feature-key> --enable (list keys with `clio experimental`).";
		return StructuredOutcome(CodeFeatureDisabled, text, correlationId, payload => {
			payload["canonical-name"] = canonicalName;
		});
	}

	private static CallToolResult CliVerbResult(string requestedName, string correlationId) {
		string text =
			$"'{requestedName}' is a clio CLI verb, not an MCP tool. Run it from a terminal " +
			$"(`clio {requestedName}`), or discover the MCP tool surface via get-tool-contract.";
		return StructuredOutcome(CodeCliVerbNotMcpTool, text, correlationId, payload => {
			payload["cli-verb"] = requestedName;
		});
	}

	private static CallToolResult ForeignCommandResult(
		string requestedName,
		McpToolCompatibilityEntry aliasEntry,
		string correlationId) {
		string text =
			$"'{requestedName}' is not a clio tool (owner: {aliasEntry.Owner}). " +
			$"{ToolContractGetTool.DiscoveryHint}";
		return StructuredOutcome(CodeForeignCommand, text, correlationId, payload => {
			payload[RequestedNameKey] = requestedName;
			payload["owner"] = aliasEntry.Owner.ToString();
		});
	}

	private static CallToolResult UnknownToolResult(
		string requestedName,
		IReadOnlyList<string> suggestions,
		string correlationId) {
		string didYouMean = suggestions.Count > 0
			? $" Did you mean: {string.Join(", ", suggestions)}?"
			: string.Empty;
		string text =
			$"Unknown tool '{requestedName}'. It is not a registered clio MCP tool.{didYouMean} " +
			ToolContractGetTool.DiscoveryHint;
		return StructuredOutcome(CodeUnknownTool, text, correlationId, payload => {
			payload[RequestedNameKey] = requestedName;
			payload["candidates"] = ToJsonArray(suggestions);
		});
	}

	// All expected handler outcomes are RETURNED as results (never thrown): a thrown exception would be
	// flattened by McpToolErrorFilter into a text-only error and lose the machine-readable code. The
	// code + correlation-id live in StructuredContent; the concise text mirror serves older clients.
	private static CallToolResult StructuredOutcome(
		string code,
		string text,
		string correlationId,
		Action<JsonObject> enrich) {
		JsonObject payload = new() {
			["code"] = code,
			["correlation-id"] = correlationId
		};
		enrich?.Invoke(payload);
		return new CallToolResult {
			IsError = true,
			Content = [new TextContentBlock { Text = text }],
			StructuredContent = JsonSerializer.SerializeToElement(payload)
		};
	}

	// Strips control characters and caps the length of a caller-supplied name before it is reflected
	// into model-visible prose. Tool names are short kebab-case tokens; anything longer or containing
	// non-printable characters is not a plausible name and only serves injection/noise.
	private static string SanitizeName(string rawName) {
		if (string.IsNullOrWhiteSpace(rawName)) {
			return rawName?.Trim();
		}
		const int maxLength = 64;
		string trimmed = rawName.Trim();
		char[] printable = trimmed
			.Where(character => !char.IsControl(character))
			.Take(maxLength)
			.ToArray();
		return new string(printable);
	}

	private static JsonArray ToJsonArray(IReadOnlyList<string> values) {
		JsonArray array = [];
		foreach (string value in values) {
			array.Add(value);
		}
		return array;
	}

	private static HashSet<string> BuildCliVerbNames() {
		HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
		foreach (Type type in typeof(McpDurableCallToolHandler).Assembly.GetTypes()) {
			VerbAttribute verb = type.GetCustomAttribute<VerbAttribute>();
			if (verb is null) {
				continue;
			}
			names.Add(verb.Name);
			foreach (string alias in (verb.Aliases ?? []).Where(alias => !string.IsNullOrWhiteSpace(alias))) {
				names.Add(alias);
			}
		}
		return names;
	}
}
