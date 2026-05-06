using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Clio.Command.BusinessRules;

internal interface IPageBusinessRuleElementProvider {
	IReadOnlySet<string> GetElementNames(PageBundleInfo bundle);
}

internal sealed class PageBusinessRuleElementProvider : IPageBusinessRuleElementProvider {

	public IReadOnlySet<string> GetElementNames(PageBundleInfo bundle) {
		ArgumentNullException.ThrowIfNull(bundle);

		HashSet<string> result = new(StringComparer.Ordinal);
		CollectElementNames(bundle.ViewConfig, result);
		return result;
	}

	private static void CollectElementNames(JsonNode? node, ISet<string> sink) {
		Stack<JsonNode?> pending = new();
		pending.Push(node);
		while (pending.Count > 0) {
			JsonNode? current = pending.Pop();
			switch (current) {
				case JsonArray array:
					foreach (JsonNode? item in array) {
						pending.Push(item);
					}
					break;
				case JsonObject obj:
					string name = obj["name"]?.GetValue<string>() ?? string.Empty;
					if (!string.IsNullOrWhiteSpace(name)) {
						sink.Add(name);
					}
					foreach (KeyValuePair<string, JsonNode?> property in obj) {
						pending.Push(property.Value);
					}
					break;
			}
		}
	}
}
