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
