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
/// (non-destructive, reproducing the host's pre-lazy non-prompt behavior) or answered with a structured
/// <c>confirmation-required</c> retry shape (destructive, reproducing the host's pre-lazy prompt, which
/// the host can no longer raise for an unadvertised tool). Unresolvable names return structured,
/// machine-readable errors with did-you-mean suggestions and a discovery hint instead of a dead end.
/// </summary>
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
	IClioRunExecutor executor) : IMcpDurableCallToolHandler {

	// Stable machine-readable outcome codes (mirrored in StructuredContent, never only prose) so an
	// agent — or a downstream harness — can branch on the outcome without parsing English.
	internal const string CodeUnknownTool = "unknown-tool";
	internal const string CodeDeprecatedToolAlias = "deprecated-tool-alias";
	internal const string CodeCliVerbNotMcpTool = "cli-verb-not-mcp-tool";
	internal const string CodeForeignCommand = "foreign-command";
	internal const string CodeConfirmationRequired = "confirmation-required";
	internal const string CodeFeatureDisabled = "feature-disabled";

	// Every CLI [Verb] name and alias in the assembly, for classifying a requested name that is a real
	// clio CLI verb but has no MCP tool. Deliberately unfiltered by feature toggles: the classification
	// message only says "this is a CLI verb, not an MCP tool", which is true either way.
	private static readonly Lazy<HashSet<string>> CliVerbNames = new(BuildCliVerbNames);

	/// <inheritdoc />
	public async ValueTask<CallToolResult> HandleAsync(
		RequestContext<CallToolRequestParams> request,
		CancellationToken cancellationToken) {
		ArgumentNullException.ThrowIfNull(request);
		string correlationId = Guid.NewGuid().ToString();
		string requestedName = request.Params?.Name?.Trim();
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

		// Reproduce the pre-lazy per-tool gate: the host used to read this tool's own Destructive flag
		// from tools/list and prompt accordingly. For an unadvertised tool the host cannot see the flag,
		// so the server self-enforces it — non-destructive executes without a prompt (exactly as
		// pre-lazy), destructive is never silently executed and returns a ready-to-retry
		// clio-run-destructive shape instead (the advertised, host-gated executor).
		if (toolRegistry.IsDestructive(canonicalName)) {
			return ConfirmationRequiredResult(requestedName, canonicalName, tool, request.Params?.Arguments, correlationId);
		}

		CallToolResult result = await executor
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

	// Appends the model-visible advisory to Content (the channel the model actually reads — result
	// `_meta` is protocol metadata the host may drop) plus an out-of-band audit block, so a successful
	// forgiving execution both returns the payload AND steers the agent to the discoverable path next time.
	private static CallToolResult AttachAdvisory(
		CallToolResult result,
		string requestedName,
		string canonicalName,
		bool viaAlias,
		string correlationId) {
		if (result is null) {
			return result;
		}
		string aliasNote = viaAlias
			? $" Note: '{requestedName}' is a deprecated alias — use '{canonicalName}'."
			: string.Empty;
		string advisory =
			$"[clio] Executed '{canonicalName}' directly; it is not advertised in tools/list.{aliasNote} " +
			$"Prefer the advertised executor next time: clio-run {{\"command\":\"{canonicalName}\",\"args\":{{…}}}} " +
			"(discover contracts via get-tool-contract).";
		List<ContentBlock> content = result.Content is null ? [] : [.. result.Content];
		content.Add(new TextContentBlock { Text = advisory });
		result.Content = content;
		result.Meta ??= new JsonObject();
		result.Meta["durable-invocation"] = new JsonObject {
			["requested-name"] = requestedName,
			["dispatched-tool"] = canonicalName,
			["via-alias"] = viaAlias,
			["destructive"] = false,
			["correlation-id"] = correlationId
		};
		return result;
	}

	private static CallToolResult ConfirmationRequiredResult(
		string requestedName,
		string canonicalName,
		McpServerTool tool,
		IDictionary<string, JsonElement> nativeArguments,
		string correlationId) {
		JsonObject retryArguments = new() {
			["command"] = canonicalName
		};
		JsonNode retryArgs = BuildRetryArgs(tool, nativeArguments);
		if (retryArgs is not null) {
			retryArguments["args"] = retryArgs;
		}
		string text =
			$"Tool '{canonicalName}' is destructive and was NOT executed: it is not advertised in tools/list, " +
			"so the host cannot show its own confirmation prompt. To proceed, call the advertised executor " +
			$"`clio-run-destructive` with {{\"command\":\"{canonicalName}\",\"args\":{{…}}}} — the host gates that call.";
		return StructuredOutcome(CodeConfirmationRequired, text, correlationId, payload => {
			payload["requested-name"] = requestedName;
			payload["canonical-name"] = canonicalName;
			payload["destructive"] = true;
			payload["retry"] = new JsonObject {
				["tool"] = ClioRunDestructiveTool.ToolName,
				["arguments"] = retryArguments
			};
		});
	}

	// Converts the native call's SDK-bound arguments into the payload `clio-run`'s `args` parameter
	// expects: for the common single-complex-parameter tool shape the native arguments carry the record
	// under the parameter name (clio-run re-wraps it on dispatch), so that inner object IS the args
	// payload; scalar/multi-parameter tools pass their arguments object through as-is. Returns null when
	// the caller sent no arguments.
	private static JsonNode BuildRetryArgs(
		McpServerTool tool,
		IDictionary<string, JsonElement> nativeArguments) {
		if (nativeArguments is null || nativeArguments.Count == 0) {
			return null;
		}
		if (ClioRunExecutor.ExpectsSingleComplexArgsParameter(tool, out string parameterName)
			&& nativeArguments.Count == 1
			&& nativeArguments.TryGetValue(parameterName, out JsonElement record)) {
			return JsonNode.Parse(record.GetRawText());
		}
		JsonObject args = new();
		foreach (KeyValuePair<string, JsonElement> argument in nativeArguments) {
			args[argument.Key] = JsonNode.Parse(argument.Value.GetRawText());
		}
		return args;
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
			payload["requested-name"] = requestedName;
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
			payload["requested-name"] = requestedName;
			payload["owner"] = aliasEntry.Owner.ToString();
		});
	}

	private CallToolResult UnknownToolResult(
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
			payload["requested-name"] = requestedName;
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
			foreach (string alias in verb.Aliases ?? []) {
				if (!string.IsNullOrWhiteSpace(alias)) {
					names.Add(alias);
				}
			}
		}
		return names;
	}
}
