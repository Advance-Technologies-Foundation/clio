namespace Clio.Command;

using System.Collections.Generic;
using System.Text.Json.Nodes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class PageBundleMergeHelpers {
	private static readonly JsonMergeSettings MergeSettings = new() {
		MergeArrayHandling = MergeArrayHandling.Replace,
		MergeNullValueHandling = MergeNullValueHandling.Merge
	};

	public static JObject DeepMerge(JObject current, JToken next) {
		JObject result = current?.DeepClone() as JObject ?? new JObject();
		if (next is JObject nextObject) {
			result.Merge(nextObject, MergeSettings);
		}

		return result;
	}

	/// <summary>
	/// Applies one schema layer's <c>viewModelConfig</c> / <c>modelConfig</c> section on top of the config the
	/// layers below it produced: the layer's diff when it has one, otherwise a deep merge of its full config.
	/// The single owner of that rule, shared by the page bundle and the update-page merge check, so the two
	/// cannot resolve one chain differently.
	/// </summary>
	/// <exception cref="JsonDiffApplierException">The applied layer did not resolve to an object, which a
	/// server-valid chain never produces.</exception>
	public static JObject ApplyConfigLayer(IJsonDiffApplier applier, JObject current, JToken diff, JToken config) {
		if (diff is not JArray { Count: > 0 } operations) {
			return DeepMerge(current, config as JObject ?? new JObject());
		}
		JToken applied = applier.Apply(current, operations);
		return applied as JObject ?? throw new JsonDiffApplierException(
			$"Resolved config was {applied?.Type.ToString() ?? "null"}, expected JObject.");
	}

	public static void MergeInPlace(JObject target, JObject next) {
		if (target is null || next is null) {
			return;
		}

		target.Merge(next, MergeSettings);
	}

	public static JsonObject ToJsonObject(JObject token) {
		return ToJsonNode(token) as JsonObject ?? [];
	}

	public static JsonArray ToJsonArray(JArray token) {
		return ToJsonNode(token) as JsonArray ?? [];
	}

	public static JsonNode ToJsonNode(JToken token) {
		if (token is null) {
			return null;
		}

		return JsonNode.Parse(token.ToString(Formatting.None));
	}

	public static IReadOnlyList<object> GetPathSegments(JObject operation) {
		if (operation["path"] is not JArray path) {
			return [];
		}

		var result = new List<object>(path.Count);
		foreach (JToken segment in path) {
			if (segment.Type == JTokenType.Integer) {
				result.Add(segment.Value<int>());
				continue;
			}

			if (int.TryParse(segment.ToString(), out int index)) {
				result.Add(index);
				continue;
			}

			result.Add(segment.ToString());
		}

		return result;
	}

	public static bool HasOperations(JToken operations) {
		return operations is JArray array && array.Count > 0;
	}
}
