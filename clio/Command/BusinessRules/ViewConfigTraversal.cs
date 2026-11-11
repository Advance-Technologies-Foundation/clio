using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Clio.Command.BusinessRules;

/// <summary>
/// Walks a Freedom UI page view config and yields every element declaration (an object with a non-blank
/// <c>name</c>) with its component <c>type</c>. Shared so element and attribute discovery agree on what
/// counts as a named element.
/// </summary>
internal static class ViewConfigTraversal {
	public static IEnumerable<(string? Type, string Name)> EnumerateNamedElements(JsonNode? viewConfig) {
		Stack<JsonNode?> pending = new();
		pending.Push(viewConfig);
		while (pending.Count > 0) {
			switch (pending.Pop()) {
				case JsonArray array:
					foreach (JsonNode? item in array) {
						pending.Push(item);
					}
					break;
				case JsonObject obj:
					// In Freedom UI viewConfig, "name" is reserved for element declarations.
					// Traversing every branch keeps discovery aligned with valid nested element placements.
					string? name = GetString(obj, "name");
					if (!string.IsNullOrWhiteSpace(name)) {
						yield return (GetString(obj, "type"), name);
					}
					foreach (KeyValuePair<string, JsonNode?> property in obj) {
						pending.Push(property.Value);
					}
					break;
			}
		}
	}

	private static string? GetString(JsonObject obj, string propertyName) =>
		obj[propertyName] is JsonValue value && value.TryGetValue(out string? text) ? text : null;
}
