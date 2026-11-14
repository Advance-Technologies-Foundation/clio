using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer;

/// <summary>
/// Reads the <see cref="McpToolFactsAttribute"/> and <see cref="McpToolArgumentFactsAttribute"/> declarations off
/// the MCP tool methods, keyed by tool name.
/// </summary>
/// <remarks>
/// Discovery mirrors <see cref="McpToolExecutionMetadataReader"/> and through it the SDK's own <c>WithTools</c> scan:
/// the same attributed types, the same binding flags, inherited tool methods included. So the coverage test's notion
/// of "a tool" cannot be smaller than the runtime's.
/// </remarks>
public static class McpToolFactsReader {

	private static readonly ConcurrentDictionary<Assembly, IReadOnlyDictionary<string, McpToolFacts>> DeclaredByAssembly = new();

	/// <summary>The declared facts of every tool in clio, by tool name (case-insensitive). Tools without a declaration are absent.</summary>
	public static IReadOnlyDictionary<string, McpToolFacts> Declared => ForAssembly(typeof(McpToolFactsReader).Assembly);

	/// <summary>Looks up the declared facts of <paramref name="toolName"/>.</summary>
	/// <param name="toolName">The canonical tool name.</param>
	/// <param name="facts">The facts, when the tool declares them.</param>
	/// <returns><see langword="true"/> when the tool declares facts.</returns>
	public static bool TryGet(string toolName, out McpToolFacts facts) {
		facts = null;
		return !string.IsNullOrWhiteSpace(toolName) && Declared.TryGetValue(toolName.Trim(), out facts);
	}

	internal static IReadOnlyDictionary<string, McpToolFacts> ForAssembly(Assembly assembly) {
		ArgumentNullException.ThrowIfNull(assembly);
		return DeclaredByAssembly.GetOrAdd(assembly, static scanned =>
			ReadDeclaredFactsOrNull(McpFeatureToggleFilter.GetAttributedTypes(scanned, typeof(McpServerToolTypeAttribute)))
				.Where(pair => pair.Value is not null)
				.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Every tool method's facts by tool name, or <see langword="null"/> for a tool that declares none. The coverage
	/// test reads this to name the tools that are missing a declaration.
	/// </summary>
	/// <param name="toolTypes">The tool types to scan.</param>
	internal static IReadOnlyDictionary<string, McpToolFacts> ReadDeclaredFactsOrNull(IEnumerable<Type> toolTypes) {
		ArgumentNullException.ThrowIfNull(toolTypes);
		Dictionary<string, McpToolFacts> map = new(StringComparer.OrdinalIgnoreCase);
		foreach (Type toolType in toolTypes) {
			foreach (MethodInfo method in EnumerateToolMethods(toolType)) {
				string toolName = method.GetCustomAttribute<McpServerToolAttribute>()?.Name;
				if (string.IsNullOrWhiteSpace(toolName)) {
					continue;
				}
				// Keep-first on a duplicate name: McpToolInvokerRegistry already refuses a duplicate at startup.
				map.TryAdd(toolName.Trim(), Read(method));
			}
		}
		return map;
	}

	/// <summary>Reads one method's declaration, or <see langword="null"/> when it has no <see cref="McpToolFactsAttribute"/>.</summary>
	/// <param name="method">The tool method.</param>
	internal static McpToolFacts Read(MethodInfo method) {
		ArgumentNullException.ThrowIfNull(method);
		McpToolFactsAttribute tool = method.GetCustomAttribute<McpToolFactsAttribute>();
		if (tool is null) {
			return null;
		}
		McpToolArgumentFacts[] byArgument = [.. method.GetCustomAttributes<McpToolArgumentFactsAttribute>()
			.Select(entry => new McpToolArgumentFacts(
				entry.Argument,
				entry.Value,
				entry.WhenAbsent,
				entry.Effect,
				entry.EnvironmentImpact == McpToolEnvironmentImpact.Unspecified ? tool.EnvironmentImpact : entry.EnvironmentImpact,
				entry.Scope == McpToolScope.Unspecified ? tool.Scope : entry.Scope,
				entry.Connections == McpToolConnections.Unspecified ? tool.Connections : entry.Connections))
			.OrderBy(entry => entry.Argument, StringComparer.Ordinal)
			.ThenBy(entry => entry.Value, StringComparer.Ordinal)];
		return new McpToolFacts(tool.Effect, tool.EnvironmentImpact, tool.Scope, tool.Connections, byArgument);
	}

	private static IEnumerable<MethodInfo> EnumerateToolMethods(Type toolType) {
		// Sonar S3011: NonPublic is required. It mirrors McpToolInvokerRegistry.EnumerateToolMethods and the SDK's own
		// WithTools scan, without DeclaredOnly, so an inherited tool method is seen; only attributes are read.
#pragma warning disable S3011
		const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
#pragma warning restore S3011
		return toolType.GetMethods(flags).Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null);
	}
}
