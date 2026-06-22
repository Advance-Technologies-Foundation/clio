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
	/// <param name="command">The MCP tool name (as advertised in full-mode <c>tools/list</c>).</param>
	/// <param name="args">Free-form JSON args object forwarded to the target tool.</param>
	/// <param name="destructiveSurface">
	/// <c>true</c> when invoked from <c>clio-run-destructive</c>; <c>false</c> from <c>clio-run</c>.
	/// </param>
	/// <param name="callContext">The calling tool's request context (provides the live MCP server).</param>
	/// <param name="cancellationToken">Cancellation token for the dispatched invocation.</param>
	/// <returns>The target tool's <see cref="CallToolResult"/>, or a structured error result.</returns>
	ValueTask<CallToolResult> RunAsync(
		string command,
		JsonElement? args,
		bool destructiveSurface,
		RequestContext<CallToolRequestParams> callContext,
		CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class ClioRunExecutor(IMcpToolInvokerRegistry toolRegistry) : IClioRunExecutor {

	/// <inheritdoc />
	public async ValueTask<CallToolResult> RunAsync(
		string command,
		JsonElement? args,
		bool destructiveSurface,
		RequestContext<CallToolRequestParams> callContext,
		CancellationToken cancellationToken) {
		ArgumentNullException.ThrowIfNull(callContext);
		if (string.IsNullOrWhiteSpace(command)) {
			return Error("Error: 'command' is required.");
		}
		string toolName = command.Trim();

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
			return Error($"Error: unknown tool '{toolName}'. It is not a registered clio MCP tool.");
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
			return Error($"Error: {ex.Message}");
		}

		// Dispatch within the SAME request context (same server/session/services), retargeting it at the
		// resolved tool and its arguments. Reusing the caller's context — rather than constructing a new
		// one — carries the live MCP server forward so the SDK's InvokeAsync can build and run the real
		// tool, and avoids the RequestContext constructor's non-null-server guard.
		callContext.Params = childParams;
		callContext.MatchedPrimitive = tool;
		try {
			CallToolResult result = await tool.InvokeAsync(callContext, cancellationToken).ConfigureAwait(false);
			return AttachDispatchAudit(result, toolName);
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
		result.Meta ??= new JsonObject();
		result.Meta["clio-run"] = new JsonObject {
			["dispatchedTool"] = toolName,
			["destructive"] = toolRegistry.IsDestructive(toolName)
		};
		return result;
	}

	// Unwraps to the inner-most exception's message so the surfaced detail is the actual failure cause
	// rather than a generic wrapper (e.g. TargetInvocationException) added by the dispatch machinery.
	private static string GetInnermostMessage(Exception ex) {
		Exception current = ex;
		while (current.InnerException is not null) {
			current = current.InnerException;
		}
		return current.Message;
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
	[Description("Generic executor for clio MCP tools hidden from tools/list (the long tail). `command` is an MCP tool name (kebab-case, e.g. \"sync-schemas\", \"create-lookup\", \"execute-esq\", \"odata-read\") and `args` is the JSON arguments object that tool expects. Runs ANY tool — including write/destructive ones — directly; you do NOT need a different executor. Unknown tool or invalid args return a structured Error result with the real cause. Marked destructive so the host can confirm; not auto-approved.")]
	public ValueTask<CallToolResult> Run(
		RequestContext<CallToolRequestParams> context,
		[Description("clio MCP tool name (kebab-case), e.g. \"execute-esq\"")] string command,
		[Description("JSON arguments object the target tool expects")] JsonElement? args = null,
		CancellationToken cancellationToken = default)
		=> executor.RunAsync(command, args, destructiveSurface: false, context, cancellationToken);
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
	[Description("Deprecated alias of `clio-run` (identical behavior — runs ANY clio MCP tool by name, read or write/destructive). Kept so a caller that picks either executor succeeds. Prefer `clio-run`. `command` is an MCP tool name (kebab-case); `args` is the JSON arguments object that tool expects.")]
	public ValueTask<CallToolResult> Run(
		RequestContext<CallToolRequestParams> context,
		[Description("clio MCP tool name (kebab-case), e.g. \"sync-schemas\"")] string command,
		[Description("JSON arguments object the target tool expects")] JsonElement? args = null,
		CancellationToken cancellationToken = default)
		=> executor.RunAsync(command, args, destructiveSurface: true, context, cancellationToken);
}
