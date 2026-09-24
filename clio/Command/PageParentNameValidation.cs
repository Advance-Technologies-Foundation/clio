namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using JsonhCs;
using Newtonsoft.Json.Linq;

/// <summary>Checks explicit web diff parents against local elements and a supplied inherited element set.</summary>
internal static class PageParentNameValidation {
    private const string OperationKey = "operation";
	internal static JArray ReadDiff(string body) {
		if (!PageSchemaSectionReader.TryRead(body, out string text, "SCHEMA_VIEW_CONFIG_DIFF", "SCHEMA_DIFF")) return [];
		return JArray.Parse(JsonhReader.ParseElement(text).Value.GetRawText());
	}

	internal static HashSet<string> Names(JToken tree) {
		var names = new HashSet<string>(StringComparer.Ordinal);
		Collect(tree, names);
		return names;
	}

	private static void Collect(JToken node, HashSet<string> names) {
		if (node is JObject obj && obj["name"]?.Type == JTokenType.String) names.Add(obj.Value<string>("name"));
		if (node is JContainer container) foreach (JToken child in container.Children()) Collect(child, names);
	}

	internal static IEnumerable<JObject> ParentOperations(JArray diff) {
		var removed = diff.OfType<JObject>().Where(x => x.Value<string>(OperationKey) == "remove" && x["properties"] is not JArray)
			.Select(x => x.Value<string>("name")).ToHashSet(StringComparer.Ordinal);
		return diff.OfType<JObject>().Where(x => x.Value<string>(OperationKey) == "insert"
			|| (x.Value<string>(OperationKey) == "move" && !removed.Contains(x.Value<string>("name"))));
	}

	internal static SchemaValidationResult Validate(string body, IReadOnlyList<string> inheritedNames = null) {
		var result = new SchemaValidationResult { IsValid = true };
		if (PageSchemaTypeExtensions.FromBody(body) == PageSchemaType.Mobile) return result;
		JArray diff = ReadDiff(body);
		HashSet<string> known = CollectKnownNames(diff, inheritedNames);
		foreach (JObject op in ParentOperations(diff)) {
            if (inheritedNames is not null && op.Value<string>(OperationKey) == "move" &&
                !known.Contains(op.Value<string>("name") ?? "")) continue;
			string parent = op.Value<string>("parentName");
			if (string.IsNullOrEmpty(parent) || known.Contains(parent)) continue;
			string message = Diagnostic(op.Value<string>("name"), parent, known);
			if (inheritedNames is null) result.Warnings.Add(message + " Supply known-containers from the page's inherited bundle to verify template parents.");
			else { result.IsValid = false; result.Errors.Add(message); }
		}
		return result;
	}

	private static HashSet<string> CollectKnownNames(JArray diff, IReadOnlyList<string> inheritedNames) {
		var known = new HashSet<string>(inheritedNames ?? [], StringComparer.Ordinal);
		foreach (JObject op in diff.OfType<JObject>().Where(x => x.Value<string>(OperationKey) == "remove" && x["properties"] is not JArray))
			known.Remove(op.Value<string>("name") ?? "");
		foreach (JObject op in diff.OfType<JObject>().Where(x => x.Value<string>(OperationKey) == "insert")) {
			if (op.Value<string>("name") is string name) known.Add(name);
			if (op["values"] is JObject values) {
                foreach (JProperty property in values.Properties().Where(x => x.Name != "name")) known.UnionWith(Names(property.Value));
            }
		}
		return known;
	}

	internal static string Diagnostic(string child, string parent, IEnumerable<string> known) {
		string closest = known.Where(x => !string.IsNullOrEmpty(x) && x != child).Take(512).OrderBy(x => Distance(parent[..Math.Min(parent.Length, 256)], x[..Math.Min(x.Length, 256)]))
			.ThenBy(x => x, StringComparer.Ordinal).FirstOrDefault();
		string message = $"Element '{child}' has unresolved parentName '{parent}'.";
		return closest is null ? message : message + $" Closest known element: '{closest}'.";
	}

	private static int Distance(string left, string right) {
		int[] row = Enumerable.Range(0, right.Length + 1).ToArray();
		for (int i = 1; i <= left.Length; i++) {
			int diagonal = row[0]; row[0] = i;
			for (int j = 1; j <= right.Length; j++) {
				int old = row[j];
				row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), diagonal + (left[i - 1] == right[j - 1] ? 0 : 1));
				diagonal = old;
			}
		}
		return row[right.Length];
	}
}
