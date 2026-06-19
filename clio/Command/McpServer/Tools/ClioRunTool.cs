using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text.Json;
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

		if (!toolRegistry.TryGetTool(toolName, out McpServerTool tool)) {
			return Error($"Error: unknown tool '{toolName}'. It is not a registered clio MCP tool.");
		}

		CallToolResult gateFailure = EnforceDestructivenessGate(toolName, destructiveSurface);
		if (gateFailure is not null) {
			return gateFailure;
		}

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
		return await tool.InvokeAsync(callContext, cancellationToken).ConfigureAwait(false);
	}

	private CallToolResult EnforceDestructivenessGate(string toolName, bool destructiveSurface) {
		bool isDestructive = toolRegistry.IsDestructive(toolName);
		if (destructiveSurface && !isDestructive) {
			return Error(
				$"Error: tool '{toolName}' is not destructive; run it via 'clio-run' instead of 'clio-run-destructive'.");
		}
		if (!destructiveSurface && isDestructive) {
			return Error(
				$"Error: tool '{toolName}' is destructive (or its safety is unknown); run it via 'clio-run-destructive'.");
		}
		return null;
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
/// Generic, non-destructive MCP executor for the long tail of clio tools. Never
/// <see cref="McpServerToolAttribute.ReadOnly"/> / auto-approve. Refuses destructive tools.
/// </summary>
[McpServerToolType]
public sealed class ClioRunTool(IClioRunExecutor executor) {

	/// <summary>Stable MCP tool name for the safe generic executor.</summary>
	internal const string ToolName = "clio-run";

	/// <summary>
	/// Runs a non-destructive clio MCP tool by name with free-form JSON arguments.
	/// </summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
	[Description("Generic executor for the non-destructive long tail of clio MCP tools hidden from tools/list. `command` is an MCP tool name (kebab-case, e.g. \"sync-pages\", \"execute-esq\") and `args` is the JSON arguments object that tool expects. Refuses destructive tools (use `clio-run-destructive`). Unknown tool or invalid args return a structured Error result. NOT auto-approved.")]
	public ValueTask<CallToolResult> Run(
		RequestContext<CallToolRequestParams> context,
		[Description("clio MCP tool name (kebab-case), e.g. \"execute-esq\"")] string command,
		[Description("JSON arguments object the target tool expects")] JsonElement? args = null,
		CancellationToken cancellationToken = default)
		=> executor.RunAsync(command, args, destructiveSurface: false, context, cancellationToken);
}

/// <summary>
/// Generic MCP executor for destructive clio tools. Routes only tools classified as destructive;
/// refuses non-destructive ones.
/// </summary>
[McpServerToolType]
public sealed class ClioRunDestructiveTool(IClioRunExecutor executor) {

	/// <summary>Stable MCP tool name for the destructive generic executor.</summary>
	internal const string ToolName = "clio-run-destructive";

	/// <summary>
	/// Runs a destructive clio MCP tool by name with free-form JSON arguments.
	/// </summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description("Generic executor for DESTRUCTIVE clio MCP tools hidden from tools/list (sync-schemas, odata-create/update/delete, delete-app, restore-creatio, compile-creatio, create/update-sys-setting, etc.). `command` is an MCP tool name (kebab-case) and `args` is the JSON arguments object that tool expects. Refuses non-destructive tools (use `clio-run`). Unknown tool or invalid args return a structured Error result. Hosts should require confirmation.")]
	public ValueTask<CallToolResult> Run(
		RequestContext<CallToolRequestParams> context,
		[Description("clio MCP tool name (kebab-case), e.g. \"sync-schemas\"")] string command,
		[Description("JSON arguments object the target tool expects")] JsonElement? args = null,
		CancellationToken cancellationToken = default)
		=> executor.RunAsync(command, args, destructiveSurface: true, context, cancellationToken);
}
