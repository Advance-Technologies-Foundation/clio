namespace Clio.Command.McpServer.Tools.MobilePageConverter;

using System;
using System.Collections.Generic;
using System.Linq;
using JsonArray = System.Text.Json.Nodes.JsonArray;
using JsonNode = System.Text.Json.Nodes.JsonNode;
using JsonObject = System.Text.Json.Nodes.JsonObject;

// ENG-96589 — the mobile crt.Timeline renders its tiles from its own `items` descriptors; a crt.TimelineTile
// element has no widget and is dropped at deserialisation (mobile-app timeline_tile.component.md).
public static partial class WebToMobileAnalysisService {

	private const string TimelineType = "crt.Timeline";
	private const string TimelineTileType = "crt.TimelineTile";

	/// <summary>
	/// The tile keys the mobile runtime reads (<c>TimelineTileConfig.fromJson</c>). Everything else on a web
	/// tile — <c>filters</c> bound to <c>$TimelineTile_*_Items</c>, which no page declares, plus
	/// <c>classes</c>, <c>icon</c>, <c>iconPosition</c>, <c>visible</c> — is web-only.
	/// </summary>
	private static readonly string[] TimelineTileDescriptorKeys =
		["linkedColumn", "sortedByColumn", "ownerColumn", "columnsFlexConfig", "data"];

	/// <summary>
	/// Replaces every <c>crt.TimelineTile</c> insert under a <c>crt.Timeline</c> insert with a descriptor in
	/// that timeline's <c>items</c>, and records the tile as <see cref="ReasonCodes.DropFoldedIntoParent"/>
	/// so every source element stays accounted for.
	/// </summary>
	/// <remarks>
	/// Runs after <see cref="ExcludedComponentsPass"/> so an excluded tile is never folded back in, and before
	/// <see cref="RemoveEmptyContainers"/> and the prune so both see the timeline's final surface.
	/// </remarks>
	private static void FoldTimelineTiles(List<ElementMapEntry> elementMap) {
		Dictionary<string, JsonObject> timelines = elementMap
			.Where(e => IsInsert(e)
				&& string.Equals(e.MobileType, TimelineType, StringComparison.OrdinalIgnoreCase)
				&& e.Name is { Length: > 0 }
				&& e.Values is JsonObject)
			.GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(g => g.Key, g => (JsonObject)g.First().Values, StringComparer.OrdinalIgnoreCase);
		if (timelines.Count == 0) {
			return;
		}
		for (int i = 0; i < elementMap.Count; i++) {
			ElementMapEntry tile = elementMap[i];
			if (!IsInsert(tile)
				|| !string.Equals(tile.MobileType, TimelineTileType, StringComparison.OrdinalIgnoreCase)
				|| tile.ParentName is not { Length: > 0 }
				|| !timelines.TryGetValue(tile.ParentName, out JsonObject timeline)) {
				continue;
			}
			if (timeline[ItemsPropertyName] is not JsonArray items) {
				items = [];
				timeline[ItemsPropertyName] = items;
			}
			items.Add(TimelineTileDescriptor(tile.Values as JsonObject));
			elementMap[i] = Drop(tile.WebName, tile.WebType,
				Reason(ReasonCodes.DropFoldedIntoParent,
					("parentName", tile.ParentName),
					("property", ItemsPropertyName)));
		}
	}

	private static JsonObject TimelineTileDescriptor(JsonObject tileValues) {
		var descriptor = new JsonObject();
		if (tileValues is null) {
			return descriptor;
		}
		foreach (string key in TimelineTileDescriptorKeys) {
			if (tileValues[key] is JsonNode value) {
				descriptor[key] = value.DeepClone();
			}
		}
		return descriptor;
	}
}
