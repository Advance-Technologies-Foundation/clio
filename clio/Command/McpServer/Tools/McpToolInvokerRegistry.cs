using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Maps every registered MCP tool NAME to an invokable <see cref="McpServerTool"/> built over the
/// full feature-enabled tool catalog, so the generic <c>clio-run</c> / <c>clio-run-destructive</c>
/// executors can reach ANY MCP tool by name — including the long tail hidden from <c>tools/list</c>
/// in lazy mode and the ~42 MCP-only tools that have no CLI <c>[Verb]</c> (for example
/// <c>sync-schemas</c>, <c>odata-read</c>, <c>execute-esq</c>, <c>create-user-task</c>).
/// </summary>
/// <remarks>
/// This is the dispatch layer the executors resolve against, replacing the CLI-<c>[Verb]</c>-only
/// <c>ICommandOptionsRegistry</c> abstraction (which could never reach MCP-only tools). Lookups are
/// case-insensitive on the tool name.
/// </remarks>
public interface IMcpToolInvokerRegistry {
	/// <summary>
	/// Attempts to resolve the invokable <see cref="McpServerTool"/> for an MCP tool name.
	/// </summary>
	/// <param name="toolName">The MCP tool name (the same name advertised in full-mode <c>tools/list</c>).</param>
	/// <param name="tool">The resolved tool when the lookup hits; otherwise <c>null</c>.</param>
	/// <returns><c>true</c> when a registered tool matches; otherwise <c>false</c> (no throw on miss).</returns>
	bool TryGetTool(string toolName, out McpServerTool tool);

	/// <summary>
	/// Determines whether the named tool is destructive, derived from its
	/// <c>[McpServerTool(Destructive = ...)]</c> annotation. An unknown tool fails CLOSED (treated as
	/// destructive) so it is refused on the safe <c>clio-run</c> surface.
	/// </summary>
	/// <param name="toolName">The MCP tool name.</param>
	/// <returns><c>true</c> when the tool is destructive or unknown; otherwise <c>false</c>.</returns>
	bool IsDestructive(string toolName);

	/// <summary>
	/// Determines whether the named tool is retry-safe (read-only / idempotent non-destructive), derived
	/// from its <c>[McpServerTool]</c> annotations via <see cref="McpReadDeadlineGate"/>. Used to decide
	/// whether an unmatched long-tail dispatch is eligible for the read-response deadline (ENG-93373). An
	/// unknown tool fails CLOSED (treated as NOT retry-safe) so it is never bounded on the assumption it is
	/// safe to abandon.
	/// </summary>
	/// <param name="toolName">The MCP tool name.</param>
	/// <returns><c>true</c> when the tool is retry-safe; otherwise <c>false</c> (including unknown).</returns>
	bool IsRetrySafe(string toolName);

	/// <summary>All registered MCP tool names, in discovery order.</summary>
	IReadOnlyCollection<string> ToolNames { get; }
}

/// <summary>
/// Builds the tool-name → <see cref="McpServerTool"/> map by reflecting <c>[McpServerTool]</c> methods
/// on the feature-enabled <c>[McpServerToolType]</c> classes and creating each tool through the SDK's
/// own <see cref="McpServerTool.Create(MethodInfo, object, McpServerToolCreateOptions)"/>. Because the
/// tools are SDK-built, invoking one reuses the SDK's argument binding and per-call DI instantiation —
/// the executor reaches the real tool method exactly as a direct <c>tools/call</c> would.
/// </summary>
public sealed class McpToolInvokerRegistry : IMcpToolInvokerRegistry {
	private readonly Dictionary<string, McpServerTool> _tools;
	private readonly Dictionary<string, bool> _destructive;
	private readonly Dictionary<string, bool> _retrySafe;

	/// <summary>
	/// Builds the registry over the executing assembly using the supplied feature predicate and MCP
	/// serializer options, constructing tool instances from <paramref name="serviceProvider"/>.
	/// </summary>
	/// <param name="serviceProvider">
	/// The MCP root service provider used by the SDK to construct each tool-type instance per call
	/// (the same provider the SDK uses for the flat <c>WithTools</c> registration).
	/// </param>
	/// <param name="featureToggleService">The shared feature toggle rule gating each tool type.</param>
	public McpToolInvokerRegistry(
		IServiceProvider serviceProvider,
		IFeatureToggleService featureToggleService)
		: this(
			serviceProvider,
			Assembly.GetExecutingAssembly(),
			featureToggleService,
			BindingsModule.CreateMcpSerializerOptions()) {
	}

	/// <summary>
	/// Builds the registry from an explicit assembly + predicate + serializer options. Exposed for
	/// testability so a synthetic tool set can be injected without scanning the production assembly.
	/// </summary>
	/// <param name="serviceProvider">The provider used to construct tool-type instances per call.</param>
	/// <param name="assembly">The assembly to scan for <c>[McpServerToolType]</c> tool methods.</param>
	/// <param name="featureToggleService">The feature toggle rule gating each tool type.</param>
	/// <param name="serializerOptions">Serializer options governing tool argument marshalling.</param>
	/// <exception cref="ArgumentNullException">When any required argument is <c>null</c>.</exception>
	internal McpToolInvokerRegistry(
		IServiceProvider serviceProvider,
		Assembly assembly,
		IFeatureToggleService featureToggleService,
		JsonSerializerOptions serializerOptions) {
		ArgumentNullException.ThrowIfNull(serviceProvider);
		ArgumentNullException.ThrowIfNull(assembly);
		ArgumentNullException.ThrowIfNull(featureToggleService);
		ArgumentNullException.ThrowIfNull(serializerOptions);

		_tools = new Dictionary<string, McpServerTool>(StringComparer.OrdinalIgnoreCase);
		_destructive = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
		_retrySafe = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

		// Mirror the SDK's WithTools discovery (feature-enabled [McpServerToolType] classes, all their
		// [McpServerTool]-attributed instance/static methods), but always over the FULL enabled catalog
		// — never the lazy subset — so the long tail stays invokable even while hidden from tools/list.
		Type[] enabledToolTypes = McpFeatureToggleFilter.GetEnabledTypes(
			assembly, typeof(McpServerToolTypeAttribute), featureToggleService.IsEnabled);

		McpServerToolCreateOptions createOptions = new() {
			Services = serviceProvider,
			SerializerOptions = serializerOptions
		};

		foreach (Type toolType in enabledToolTypes) {
			foreach (MethodInfo method in EnumerateToolMethods(toolType)) {
				McpServerTool tool = CreateTool(method, toolType, serviceProvider, createOptions);
				string toolName = tool.ProtocolTool.Name;
				if (string.IsNullOrWhiteSpace(toolName)) {
					continue;
				}
				// Fail fast on a duplicate tool NAME rather than silently keeping the first one: two
				// [McpServerTool] methods advertising the same name make dispatch ambiguous and would let a
				// rename land as a silent second definition instead of a catalog alias. There are no
				// duplicate names in the production catalog today (verified), so this only fires on a
				// genuine authoring mistake — and because the MCP host resolves the registry EAGERLY right
				// after the container is built (BindingsModule.Register, registerMcpHost path; ValidateOnBuild
				// alone does not instantiate services), that mistake aborts host startup, not first dispatch.
				if (_tools.ContainsKey(toolName)) {
					throw new InvalidOperationException(
						$"Duplicate MCP tool name '{toolName}' is declared by more than one [McpServerTool] method. " +
						"Tool names must be unique; represent a renamed/legacy name as an entry in " +
						$"{nameof(IMcpToolCompatibilityCatalog)} instead of a second [McpServerTool] method.");
				}
				_tools[toolName] = tool;
				_destructive[toolName] = tool.ProtocolTool.Annotations?.DestructiveHint ?? true;
				_retrySafe[toolName] = McpReadDeadlineGate.IsRetrySafe(toolName, tool.ProtocolTool.Annotations);
			}
		}
	}

	// Static tool methods need no target; instance methods are constructed per call from the request's
	// service provider (falling back to the registry's root provider), exactly as the SDK's WithTools
	// registration does, so the tool runs against the correct per-call DI graph.
	private static McpServerTool CreateTool(
		MethodInfo method,
		Type toolType,
		IServiceProvider rootProvider,
		McpServerToolCreateOptions createOptions) {
		if (method.IsStatic) {
			return McpServerTool.Create(method, target: null, createOptions);
		}
		return McpServerTool.Create(
			method,
			request => ActivatorUtilities.CreateInstance(
				request.Services ?? rootProvider, toolType),
			createOptions);
	}

	private static IEnumerable<MethodInfo> EnumerateToolMethods(Type toolType) {
		// Sonar S3011: BindingFlags.NonPublic is a deliberate, required accessibility bypass — NOT a leak.
		// This registry must enumerate EXACTLY the [McpServerTool] methods the SDK's own
		// WithTools(IEnumerable<Type>) registers, otherwise a tool reachable via tools/call would be
		// unreachable via clio-run dispatch (a parity regression). The SDK discovers tool methods with
		// `toolType.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
		// BindingFlags.NonPublic)` (verified against ModelContextProtocol 1.4.0
		// McpServerBuilderExtensions.WithTools) — note the SDK does NOT pass DeclaredOnly, so a
		// [McpServerTool] method inherited from a base [McpServerToolType] is registered by the SDK; this
		// flag set is mirrored exactly (no DeclaredOnly) so inherited tools stay reachable via clio-run.
		// The reflected members are only filtered for [McpServerTool] and handed back to the SDK's
		// McpServerTool.Create; no private state is read or mutated.
#pragma warning disable S3011
		const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
			BindingFlags.Instance | BindingFlags.Static;
#pragma warning restore S3011
		return toolType.GetMethods(flags)
			.Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null);
	}

	/// <inheritdoc />
	public IReadOnlyCollection<string> ToolNames => _tools.Keys;

	/// <inheritdoc />
	public bool TryGetTool(string toolName, out McpServerTool tool) {
		if (string.IsNullOrWhiteSpace(toolName)) {
			tool = null;
			return false;
		}
		return _tools.TryGetValue(toolName.Trim(), out tool);
	}

	/// <inheritdoc />
	public bool IsDestructive(string toolName) {
		if (string.IsNullOrWhiteSpace(toolName)) {
			// No tool to classify safely → fail closed.
			return true;
		}
		// Unknown tool → fail closed so the safe surface refuses it.
		return !_destructive.TryGetValue(toolName.Trim(), out bool isDestructive) || isDestructive;
	}

	/// <inheritdoc />
	public bool IsRetrySafe(string toolName) {
		if (string.IsNullOrWhiteSpace(toolName)) {
			// No tool to classify → fail closed (not retry-safe).
			return false;
		}
		// Unknown tool → fail closed so it is never bounded on the assumption it is safe to abandon.
		return _retrySafe.TryGetValue(toolName.Trim(), out bool isRetrySafe) && isRetrySafe;
	}
}
