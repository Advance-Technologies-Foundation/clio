using System;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Clio.Command;

/// <summary>
/// TODO(ENG-91251, ENG-92198): DELETE THIS ENTIRE FILE — and its registration in <see cref="PageBodyBeforeSavePreprocessingPipeline"/> —
/// once the Freedom UI json-differ <c>needFlatten</c> fix ships.
/// <para>
/// Standalone, temporary workaround for an order-dependent crash in the json-differ. Its <c>needFlatten</c>
/// decides whether to flatten an object from the object's LAST key (a last-key-wins fold) and descends into
/// nameless payload wrappers; when that last-key chain reaches a non-empty <c>name</c>
/// (e.g. <c>config -&gt; scales -&gt; yAxis.name</c>) the nameless chart <c>config</c> wrapper is flattened
/// to an empty key and the designer throws "Required parameter name not found" on every open/save/delete —
/// the page becomes unsavable AND undeletable. The crash is purely order-dependent: the same values in a
/// different key order do not crash.
/// </para>
/// <para>
/// This is a before-save body preprocessor (an <see cref="IPageBodyPreprocessor"/>), deliberately SEPARATE
/// from the read-only registry-driven chart validator in <see cref="SchemaValidationService"/>. Rather than
/// ask the AI agent to reorder, <see cref="Preprocess"/> reorders the keys itself: for every
/// <c>crt.ChartWidget</c> config whose key order would crash, it moves a <b>name-free</b> key (one with no
/// <c>name</c> slot anywhere in its last-key chain — canonically <c>series</c>) to the end, so a later-filled
/// <c>name</c> can never re-introduce the crash. Values are preserved; only key order changes.
/// </para>
/// </summary>
internal sealed class ChartConfigKeyOrderPreprocessor : IPageBodyPreprocessor {

	private const string ChartWidgetType = "crt.ChartWidget";
	private const string SeriesKey = "series";

	// viewConfigDiff section markers (web AMD bodies). Mirrors SchemaValidationService's section names.
	private static readonly string[] ViewConfigDiffMarkers = { "SCHEMA_VIEW_CONFIG_DIFF", "SCHEMA_DIFF" };

	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

	// Preserve characters (Cyrillic captions, < > &) instead of \uXXXX-escaping them on re-serialization.
	private static readonly JsonSerializerOptions SerializerOptions =
		new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

	/// <summary>
	/// Returns <paramref name="body"/> with every inserted <c>crt.ChartWidget</c> config reordered so its key
	/// order cannot trigger the json-differ crash (TODO(ENG-91251, ENG-92198)). Fail-safe and conservative: returns the
	/// input unchanged when there is nothing to fix, when the body is not a web page body with a viewConfigDiff
	/// section, or when anything goes wrong — it never blocks or corrupts a save. Only the viewConfigDiff
	/// section is re-serialized, and only when at least one chart config actually needed reordering.
	/// </summary>
	public string Preprocess(string body) {
		if (string.IsNullOrEmpty(body)) {
			return body;
		}
		try {
			foreach (string marker in ViewConfigDiffMarkers) {
				if (!TryReadSection(body, marker, out string content, out int start, out int end)) {
					continue;
				}
				JsonNode root = JsonNode.Parse(content);
				if (root is null || !FixChartConfigs(root)) {
					return body; // nothing to fix → leave the body (and its formatting) untouched
				}
				string fixedContent = root.ToJsonString(SerializerOptions);
				return body[..start] + fixedContent + body[end..];
			}
			return body;
		} catch {
			return body; // never let a preprocessing hiccup block or corrupt a save
		}
	}

	private static bool TryReadSection(string body, string marker, out string content, out int contentStart, out int contentEnd) {
		content = null;
		contentStart = -1;
		contentEnd = -1;
		string pattern = $@"/\*\*{Regex.Escape(marker)}\*/(?<content>[\s\S]*?)/\*\*{Regex.Escape(marker)}\*/";
		Match match = Regex.Match(body, pattern, RegexOptions.CultureInvariant, RegexTimeout);
		if (!match.Success) {
			return false;
		}
		Group group = match.Groups["content"];
		content = group.Value;
		contentStart = group.Index;
		contentEnd = group.Index + group.Length;
		return true;
	}

	// Walks the viewConfigDiff tree; reorders every crash-shaped chart config in place. Returns whether any changed.
	private static bool FixChartConfigs(JsonNode? node) {
		bool changed = false;
		if (node is JsonObject obj) {
			if (IsChartWidgetNode(obj, out JsonObject config) && WouldNeedFlatten(config)) {
				changed |= TryMoveNameFreeKeyLast(config);
			}
			foreach (KeyValuePair<string, JsonNode?> property in obj) {
				changed |= FixChartConfigs(property.Value);
			}
		} else if (node is JsonArray array) {
			foreach (JsonNode? item in array) {
				changed |= FixChartConfigs(item);
			}
		}
		return changed;
	}

	private static bool IsChartWidgetNode(JsonObject node, out JsonObject config) {
		config = null;
		if (node.TryGetPropertyValue("type", out JsonNode? typeNode) &&
		    typeNode is JsonValue typeValue &&
		    typeValue.TryGetValue(out string? type) &&
		    string.Equals(type, ChartWidgetType, StringComparison.OrdinalIgnoreCase) &&
		    node.TryGetPropertyValue("config", out JsonNode? configNode) &&
		    configNode is JsonObject configObject) {
			config = configObject;
			return true;
		}
		return false;
	}

	// Moves a property that is crash-safe REGARDLESS of future edits to the end, so config's last key can
	// never drive needFlatten true — not even if an empty `name` slot is later populated. "Crash-safe
	// regardless" means the property's last-key chain carries no `name` slot AT ALL: an EMPTY `name` does not
	// qualify (it could be filled later and re-introduce the crash). 'series' (a nameless array) is the
	// canonical choice; a primitive such as `title` also qualifies. Returns false only if no such property
	// exists (unreachable for a real chart, which always carries a nameless series array and a string title).
	private static bool TryMoveNameFreeKeyLast(JsonObject config) {
		string? targetKey = null;
		if (config.TryGetPropertyValue(SeriesKey, out JsonNode? series) && HasNoNameSlotInLastKeyChain(series)) {
			targetKey = SeriesKey;
		} else {
			foreach (KeyValuePair<string, JsonNode?> property in config) {
				if (HasNoNameSlotInLastKeyChain(property.Value)) {
					targetKey = property.Key;
					break;
				}
			}
		}
		if (targetKey is null) {
			return false;
		}
		JsonNode? value = config[targetKey]?.DeepClone();
		config.Remove(targetKey);
		config[targetKey] = value; // re-adding appends to the end → the name-free key is now last
		return true;
	}

	// True when the value's last-key chain carries NO `name` slot at any level — so placing it last keeps
	// config crash-proof even if empty `name` slots elsewhere are populated later. Stricter than the inverse
	// of WouldNeedFlatten, which treats an EMPTY `name` as safe: here a `name` slot (empty or filled)
	// disqualifies. Primitives carry no slot and are always safe.
	private static bool HasNoNameSlotInLastKeyChain(JsonNode? node) {
		if (node is JsonObject obj) {
			if (obj.ContainsKey("name")) {
				return false; // a slot exists → it is, or could later become, a non-empty name
			}
			JsonNode? lastValue = null;
			bool hasProperty = false;
			foreach (KeyValuePair<string, JsonNode?> property in obj) {
				lastValue = property.Value;
				hasProperty = true;
			}
			return !hasProperty || HasNoNameSlotInLastKeyChain(lastValue);
		}
		if (node is JsonArray array) {
			// needFlatten judges an array by its first element's name only.
			return array.Count == 0 || array[0] is not JsonObject first || !first.ContainsKey("name");
		}
		return true; // primitive / null — cannot carry a name
	}

	// Mirrors the (buggy) json-differ needFlatten verdict: own non-empty name wins; otherwise the verdict
	// follows the object's LAST property (last-key-wins); an array is judged by its first element's name.
	private static bool WouldNeedFlatten(JsonNode? node) {
		if (node is JsonObject obj) {
			if (HasNonEmptyName(obj)) {
				return true;
			}
			JsonNode? lastValue = null;
			bool hasProperty = false;
			foreach (KeyValuePair<string, JsonNode?> property in obj) {
				lastValue = property.Value;
				hasProperty = true;
			}
			return hasProperty && WouldNeedFlatten(lastValue);
		}
		if (node is JsonArray array) {
			return array.Count > 0 && array[0] is JsonObject first && HasNonEmptyName(first);
		}
		return false; // primitive / null
	}

	// Mirrors the differ's !isEmpty(node.name): a lowercase 'name' that is not absent, null, an empty string,
	// or an empty array.
	private static bool HasNonEmptyName(JsonObject node) {
		if (!node.TryGetPropertyValue("name", out JsonNode? name) || name is null) {
			return false;
		}
		if (name is JsonValue value) {
			return !value.TryGetValue(out string? text) || !string.IsNullOrEmpty(text);
		}
		if (name is JsonArray array) {
			return array.Count > 0;
		}
		return name is JsonObject; // non-empty object name → non-empty (mirrors !isEmpty)
	}
}
