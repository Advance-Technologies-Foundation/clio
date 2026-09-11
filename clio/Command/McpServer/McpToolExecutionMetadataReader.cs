using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Clio.Command.McpServer.Tools;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer;

/// <summary>
/// Reads the declared <see cref="McpToolExecutionAttribute"/> of an MCP tool by NAME.
/// </summary>
/// <remarks>
/// <para>
/// Two name-resolution steps happen before the lookup, and both are load-bearing (ADR rule 7):
/// </para>
/// <list type="number">
/// <item>
/// <description>
/// The generic executors are UNWRAPPED. A long-running tool is normally reached as
/// <c>clio-run {"command": "compile-creatio", …}</c>, so keying on the outer name would give every
/// long-running call the executor's own row.
/// </description>
/// </item>
/// <item>
/// <description>
/// A deprecated name is canonicalised through <see cref="IMcpToolCompatibilityCatalog"/>, so a call that
/// arrives under an alias resolves the canonical tool's metadata rather than missing.
/// </description>
/// </item>
/// </list>
/// <para>
/// Stage 1 declares and asserts this metadata; nothing reads it to route yet
/// (<c>spec/stories/story-mcp-worker-execution-boundary-1.md</c>).
/// </para>
/// </remarks>
public interface IMcpToolExecutionMetadataReader {

	/// <summary>
	/// Resolves the name execution metadata is keyed on: the inner command when
	/// <paramref name="toolName"/> is <c>clio-run</c> / <c>clio-run-destructive</c> and
	/// <paramref name="innerCommand"/> is supplied, canonicalised through the compatibility catalog.
	/// </summary>
	/// <param name="toolName">The tool name the call arrived under (may be an executor or a deprecated alias).</param>
	/// <param name="innerCommand">
	/// The <c>command</c> argument of an executor call, or <c>null</c> for a direct call. Ignored when
	/// <paramref name="toolName"/> is not an executor.
	/// </param>
	/// <returns>
	/// The canonical routing key, or <c>null</c> when <paramref name="toolName"/> is null/blank. An
	/// executor call with no inner command resolves to the executor's own name (there is nothing else to
	/// key on), which is correct: the executor itself runs in-process.
	/// </returns>
	string ResolveRoutingKey(string toolName, string innerCommand);

	/// <summary>
	/// Attempts to read the execution metadata declared for a tool, applying the executor unwrap and alias
	/// canonicalisation described by <see cref="ResolveRoutingKey"/>.
	/// </summary>
	/// <param name="toolName">The tool name the call arrived under.</param>
	/// <param name="innerCommand">The <c>command</c> argument of an executor call, or <c>null</c>.</param>
	/// <param name="metadata">The declared metadata when the tool carries the attribute; otherwise <c>null</c>.</param>
	/// <returns>
	/// <c>true</c> when the resolved tool declares <see cref="McpToolExecutionAttribute"/>; otherwise
	/// <c>false</c> (no throw on miss — an unclassified or unknown tool simply has no row).
	/// </returns>
	bool TryGetMetadata(string toolName, string innerCommand, out McpToolExecutionMetadata metadata);

	/// <summary>
	/// Every tool name that declares execution metadata, mapped to it. Case-insensitive on the tool name.
	/// Tools with no attribute are absent.
	/// </summary>
	IReadOnlyDictionary<string, McpToolExecutionMetadata> DeclaredMetadataByToolName { get; }
}

/// <summary>
/// Default <see cref="IMcpToolExecutionMetadataReader"/>. Builds the tool-name → metadata map by
/// reflecting <see cref="McpToolExecutionAttribute"/> off the <c>[McpServerTool]</c> methods of every
/// <c>[McpServerToolType]</c> class in the clio assembly.
/// </summary>
/// <remarks>
/// The map is a pure function of the assembly's attributes, so it is cached per assembly in a static
/// table: the reflection cost is paid once per process even though the reflection interface-scan in
/// <c>BindingsModule</c> also registers this type as a transient for non-MCP-host containers.
/// The map is deliberately NOT feature-gated — an attribute is a property of the declaration, and a
/// feature-disabled tool cannot be called at all, so there is nothing for a gate to protect here. The
/// coverage test applies the feature gate itself, because "which tools MUST be classified" is a different
/// question from "what does this tool declare".
/// </remarks>
public sealed class McpToolExecutionMetadataReader : IMcpToolExecutionMetadataReader {

	// One entry per assembly ever scanned (in practice: clio, plus the test assembly under test).
	private static readonly ConcurrentDictionary<Assembly, IReadOnlyDictionary<string, McpToolExecutionMetadata>>
		DeclaredMetadataByAssembly = new();

	private static readonly string[] ExecutorToolNames = [ClioRunTool.ToolName, ClioRunDestructiveTool.ToolName];

	private readonly IMcpToolCompatibilityCatalog _compatibilityCatalog;

	/// <summary>
	/// Builds the reader over the clio assembly. Used by DI.
	/// </summary>
	/// <param name="compatibilityCatalog">The alias → canonical name authority.</param>
	public McpToolExecutionMetadataReader(IMcpToolCompatibilityCatalog compatibilityCatalog)
		: this(Assembly.GetExecutingAssembly(), compatibilityCatalog) {
	}

	/// <summary>
	/// Builds the reader over an explicit assembly. Exposed for testability so a synthetic tool set can be
	/// classified without touching the production catalog.
	/// </summary>
	/// <param name="assembly">The assembly to scan for <c>[McpServerToolType]</c> tool methods.</param>
	/// <param name="compatibilityCatalog">The alias → canonical name authority.</param>
	/// <exception cref="ArgumentNullException">When any argument is <c>null</c>.</exception>
	internal McpToolExecutionMetadataReader(Assembly assembly, IMcpToolCompatibilityCatalog compatibilityCatalog) {
		ArgumentNullException.ThrowIfNull(assembly);
		ArgumentNullException.ThrowIfNull(compatibilityCatalog);
		_compatibilityCatalog = compatibilityCatalog;
		DeclaredMetadataByToolName = DeclaredMetadataByAssembly.GetOrAdd(
			assembly,
			static scanned => BuildDeclaredMetadata(
				McpFeatureToggleFilter.GetAttributedTypes(scanned, typeof(McpServerToolTypeAttribute))));
	}

	/// <inheritdoc />
	public IReadOnlyDictionary<string, McpToolExecutionMetadata> DeclaredMetadataByToolName { get; }

	/// <inheritdoc />
	public string ResolveRoutingKey(string toolName, string innerCommand) {
		if (string.IsNullOrWhiteSpace(toolName)) {
			return null;
		}
		string resolved = toolName.Trim();
		// Single-level unwrap only: the compatibility catalog refuses to declare an executor as either a
		// canonical or an alias (McpToolCompatibilityCatalog.ExecutorToolNames), and the executors refuse to
		// dispatch to themselves, so an inner command can never be another executor.
		if (ExecutorToolNames.Contains(resolved, StringComparer.OrdinalIgnoreCase)
			&& !string.IsNullOrWhiteSpace(innerCommand)) {
			resolved = innerCommand.Trim();
		}
		if (_compatibilityCatalog.TryResolveAlias(resolved, out string canonicalName, out _)) {
			resolved = canonicalName;
		}
		return resolved;
	}

	/// <inheritdoc />
	public bool TryGetMetadata(string toolName, string innerCommand, out McpToolExecutionMetadata metadata) {
		string routingKey = ResolveRoutingKey(toolName, innerCommand);
		if (routingKey is null) {
			metadata = null;
			return false;
		}
		return DeclaredMetadataByToolName.TryGetValue(routingKey, out metadata);
	}

	/// <summary>
	/// Reads every discovered tool name in <paramref name="toolTypes"/> and maps it to its declared
	/// execution metadata, or to <c>null</c> when the tool method carries no
	/// <see cref="McpToolExecutionAttribute"/>. The <c>null</c>-valued entries are what makes this the
	/// catalog coverage test's input: the key set is "every tool", and the null values are "not classified".
	/// </summary>
	/// <param name="toolTypes">The <c>[McpServerToolType]</c> classes to scan.</param>
	/// <returns>Tool name → declared metadata (or <c>null</c>), case-insensitive on the name.</returns>
	/// <exception cref="ArgumentNullException">When <paramref name="toolTypes"/> is <c>null</c>.</exception>
	internal static IReadOnlyDictionary<string, McpToolExecutionMetadata> ReadDeclaredMetadataOrNull(
		IEnumerable<Type> toolTypes) {
		ArgumentNullException.ThrowIfNull(toolTypes);
		Dictionary<string, McpToolExecutionMetadata> map = new(StringComparer.OrdinalIgnoreCase);
		foreach (Type toolType in toolTypes) {
			foreach (MethodInfo method in EnumerateToolMethods(toolType)) {
				string toolName = method.GetCustomAttribute<McpServerToolAttribute>()?.Name;
				if (string.IsNullOrWhiteSpace(toolName)) {
					continue;
				}
				// Keep-first on a duplicate NAME rather than throwing: uniqueness is already enforced (and
				// fails host startup) by McpToolInvokerRegistry, so duplicating the guard here would only
				// make this reader unusable over a test assembly that declares a collision on purpose.
				map.TryAdd(toolName.Trim(), ToMetadata(method.GetCustomAttribute<McpToolExecutionAttribute>()));
			}
		}
		return map;
	}

	private static IReadOnlyDictionary<string, McpToolExecutionMetadata> BuildDeclaredMetadata(
		IEnumerable<Type> toolTypes) {
		return ReadDeclaredMetadataOrNull(toolTypes)
			.Where(pair => pair.Value is not null)
			.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
	}

	private static McpToolExecutionMetadata ToMetadata(McpToolExecutionAttribute attribute) {
		return attribute is null
			? null
			: new McpToolExecutionMetadata(
				attribute.Location,
				attribute.Lifetime,
				attribute.OperationFamily,
				attribute.BudgetPolicy,
				attribute.RequiresClientRequests,
				attribute.SharedFileResource,
				attribute.AliasOf,
				attribute.StartsOperation);
	}

	private static IEnumerable<MethodInfo> EnumerateToolMethods(Type toolType) {
		// Sonar S3011: BindingFlags.NonPublic is a deliberate, required accessibility bypass — it mirrors
		// McpToolInvokerRegistry.EnumerateToolMethods (and through it the SDK's own WithTools scan) EXACTLY,
		// including the absence of DeclaredOnly so an inherited [McpServerTool] method is still seen. A
		// narrower flag set would make this reader's notion of "a tool" smaller than the runtime's, and the
		// coverage test would then pass vacuously for the tools it silently dropped.
#pragma warning disable S3011
		const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
			BindingFlags.Instance | BindingFlags.Static;
#pragma warning restore S3011
		return toolType.GetMethods(flags)
			.Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null);
	}
}
