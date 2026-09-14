using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Mcp;

/// <summary>
/// Pulls the graph <c>describe-business-process</c> reports out of an MCP tool envelope.
/// </summary>
/// <remarks>
/// <para>
/// Shared by every process-designer fixture that reads a graph back, because the alternative failed: a
/// substring assertion over <c>JsonSerializer.Serialize(callResult)</c> cannot contain a raw quote. The
/// envelope escapes each one as <c>"</c> — which is why the neighbouring exit-code assertions in those
/// same fixtures search for <c>"exit-code":0</c> — so <c>Contain("\"isActiveVersion\": true")</c>
/// searches for a sequence that cannot occur and fails on a correct system. Three assertions were written
/// that way, and each was the entire proof of a load-bearing claim of the feature.
/// </para>
/// <para>
/// The graph is not a field of the envelope: describe writes it through <c>ILogger.WriteInfo</c>, so it
/// arrives as an escaped STRING inside one of the reported log messages. It is located by content rather
/// than by property path, so a rename inside the envelope shape cannot silently turn an assertion into a
/// scan of the wrong text — and parsed rather than substring-matched, because <c>DescribeProcessResult</c>
/// carries a <c>[JsonExtensionData]</c> bag that would let a server-sent key satisfy a naive
/// <c>Contain</c>.
/// </para>
/// </remarks>
internal static class DescribedProcessGraph {

	/// <summary>
	/// Returns the described graph, failing the calling test when the envelope carries none.
	/// </summary>
	/// <param name="callResult">The result of a describe-business-process tool call.</param>
	/// <returns>The graph as a parsed object.</returns>
	internal static JsonObject Read(CallToolResult callResult) {
		string text = string.Concat(callResult.Content.OfType<TextContentBlock>().Select(block => block.Text));
		JsonNode envelope = JsonNode.Parse(text);
		envelope.Should().NotBeNull(because: "the MCP tool must answer with a parsable envelope");
		JsonObject graph = EmbeddedStrings(envelope!)
			.Select(TryParseObject)
			.FirstOrDefault(candidate => candidate is not null && candidate.ContainsKey("schemaUId"));
		graph.Should().NotBeNull(
			because: $"describe-business-process must report a graph carrying schemaUId; the envelope was: {text}");
		return graph!;
	}

	private static IEnumerable<string> EmbeddedStrings(JsonNode node) {
		switch (node) {
			case JsonObject jsonObject:
				foreach (KeyValuePair<string, JsonNode> property in jsonObject) {
					if (property.Value is null) { continue; }
					foreach (string nested in EmbeddedStrings(property.Value)) { yield return nested; }
				}
				break;
			case JsonArray jsonArray:
				foreach (JsonNode item in jsonArray) {
					if (item is null) { continue; }
					foreach (string nested in EmbeddedStrings(item)) { yield return nested; }
				}
				break;
			case JsonValue jsonValue when jsonValue.TryGetValue(out string value) && value is not null:
				yield return value;
				break;
		}
	}

	private static JsonObject TryParseObject(string candidate) {
		if (!candidate.TrimStart().StartsWith('{')) { return null; }
		try {
			return JsonNode.Parse(candidate) as JsonObject;
		} catch (JsonException) {
			// A log message that merely opens with a brace is not the graph; keep looking.
			return null;
		}
	}
}
