using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Clio.Command.McpServer;

/// <summary>
/// Reads the reviewed tool facts from the catalog clio ships, <c>Command/McpServer/Data/mcp-tool-facts.json</c>
/// (an embedded resource), keyed by tool name.
/// </summary>
/// <remarks>
/// The catalog uses the published shape (<see cref="McpToolFacts.ToJson"/>), so the file is also what a host
/// receives. A field it leaves out, or a value it does not know, reads back as <c>Unspecified</c>; the coverage test
/// fails on that rather than this reader guessing.
/// </remarks>
public static class McpToolFactsReader {

	/// <summary>The name of the embedded catalog.</summary>
	internal const string ResourceName = "Clio.Command.McpServer.Data.mcp-tool-facts.json";

	private static readonly Lazy<IReadOnlyDictionary<string, McpToolFacts>> Catalog = new(LoadEmbeddedCatalog);

	/// <summary>The facts of every tool in the catalog, by tool name (case-insensitive).</summary>
	public static IReadOnlyDictionary<string, McpToolFacts> Declared => Catalog.Value;

	/// <summary>Looks up the facts of <paramref name="toolName"/>.</summary>
	/// <param name="toolName">The canonical tool name.</param>
	/// <param name="facts">The facts, when the catalog has an entry.</param>
	/// <returns><see langword="true"/> when the catalog has an entry for the tool.</returns>
	public static bool TryGet(string toolName, out McpToolFacts facts) {
		facts = null;
		return !string.IsNullOrWhiteSpace(toolName) && Declared.TryGetValue(toolName.Trim(), out facts);
	}

	/// <summary>
	/// Parses a catalog: an object whose properties are tool names and whose values have the published shape. An
	/// argument entry's fields other than <c>effect</c> may be left out and take the tool's value.
	/// </summary>
	/// <param name="json">The catalog text.</param>
	/// <returns>The facts by tool name (case-insensitive), argument entries ordered by argument and value.</returns>
	internal static IReadOnlyDictionary<string, McpToolFacts> Parse(string json) {
		using JsonDocument document = JsonDocument.Parse(json);
		Dictionary<string, McpToolFacts> map = new(StringComparer.OrdinalIgnoreCase);
		foreach (JsonProperty tool in document.RootElement.EnumerateObject()) {
			JsonElement value = tool.Value;
			McpToolEffect effect = ReadEffect(value);
			McpToolEnvironmentImpact impact = ReadImpact(value);
			McpToolScope scope = ReadScope(value);
			McpToolConnections connections = ReadConnections(value);
			List<McpToolArgumentFacts> byArgument = [];
			if (value.TryGetProperty("by-argument", out JsonElement entries) && entries.ValueKind == JsonValueKind.Array) {
				foreach (JsonElement entry in entries.EnumerateArray()) {
					McpToolEnvironmentImpact entryImpact = ReadImpact(entry);
					McpToolScope entryScope = ReadScope(entry);
					McpToolConnections entryConnections = ReadConnections(entry);
					byArgument.Add(new McpToolArgumentFacts(
						ReadString(entry, "argument"),
						ReadString(entry, "value"),
						entry.TryGetProperty("when-absent", out JsonElement absent) && absent.ValueKind == JsonValueKind.True,
						ReadEffect(entry),
						entryImpact == McpToolEnvironmentImpact.Unspecified ? impact : entryImpact,
						entryScope == McpToolScope.Unspecified ? scope : entryScope,
						entryConnections == McpToolConnections.Unspecified ? connections : entryConnections));
				}
			}
			map[tool.Name] = new McpToolFacts(effect, impact, scope, connections, [.. byArgument
				.OrderBy(entry => entry.Argument, StringComparer.Ordinal)
				.ThenBy(entry => entry.Value, StringComparer.Ordinal)]);
		}
		return map;
	}

	private static IReadOnlyDictionary<string, McpToolFacts> LoadEmbeddedCatalog() {
		using Stream stream = typeof(McpToolFactsReader).Assembly.GetManifestResourceStream(ResourceName)
			?? throw new InvalidOperationException($"The tool facts catalog '{ResourceName}' is not embedded in clio.");
		using StreamReader reader = new(stream);
		return Parse(reader.ReadToEnd());
	}

	private static string ReadString(JsonElement element, string name) =>
		element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	private static McpToolEffect ReadEffect(JsonElement element) => ReadString(element, "effect") switch {
		"read" => McpToolEffect.Read,
		"change" => McpToolEffect.Change,
		"destroy" => McpToolEffect.Destroy,
		_ => McpToolEffect.Unspecified
	};

	private static McpToolEnvironmentImpact ReadImpact(JsonElement element) =>
		!element.TryGetProperty("rebuilds-environment", out JsonElement value) ? McpToolEnvironmentImpact.Unspecified
		: value.ValueKind switch {
			JsonValueKind.True => McpToolEnvironmentImpact.Rebuild,
			JsonValueKind.False => McpToolEnvironmentImpact.None,
			_ => McpToolEnvironmentImpact.Unspecified
		};

	private static McpToolScope ReadScope(JsonElement element) => ReadString(element, "scope") switch {
		"local" => McpToolScope.Local,
		"environment" => McpToolScope.Environment,
		"machine" => McpToolScope.Machine,
		_ => McpToolScope.Unspecified
	};

	private static McpToolConnections ReadConnections(JsonElement element) => ReadString(element, "connections") switch {
		"none" => McpToolConnections.None,
		"read" => McpToolConnections.Read,
		"change" => McpToolConnections.Change,
		_ => McpToolConnections.Unspecified
	};
}
