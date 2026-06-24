namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Normalizes a Freedom UI page body (web OR mobile) so it survives the Creatio diff engine, which does
/// NOT apply a NESTED element config carried inside a <c>merge</c> or <c>insert</c> operation's
/// <c>values</c>. A nested element config is any object that is itself a named element — its identifier is
/// the presence of a <c>name</c> property, e.g. <c>itemLayout: { "name":"ListItem", "type":"crt.ListItem", … }</c>.
/// The differ silently ignores such a config (the child element is addressed separately), so it is lifted
/// into its OWN operation of the SAME kind and removed from the parent:
/// <list type="bullet">
/// <item><c>merge</c> → a <c>merge</c> targeting the child by <c>name</c> (the child already exists).</item>
/// <item><c>insert</c> → an <c>insert</c> placing the child under its parent (<c>parentName</c> + the slot:
/// <c>propertyName</c> for an object slot, <c>path</c> for an array slot) so the parent op creates the
/// element and the child op fills it.</item>
/// </list>
/// Recurses into the lifted children (a grandchild becomes its own op) and handles both single objects and
/// arrays of named objects.
/// <para>
/// Pure, idempotent and tolerant: only <c>merge</c>/<c>insert</c> ops in <c>viewConfigDiff</c> are touched;
/// a body that is not a Freedom page, has no <c>viewConfigDiff</c>, or is unparseable is returned unchanged.
/// Web bodies carry the diff inside the <c>/**SCHEMA_VIEW_CONFIG_DIFF*/…</c> JS marker section; mobile
/// bodies are plain JSON.
/// </para>
/// </summary>
public static class PageDiffNormalizer {

	private const string OperationProperty = "operation";
	private const string NameProperty = "name";
	private const string ValuesProperty = "values";
	private const string ParentNameProperty = "parentName";
	private const string PropertyNameProperty = "propertyName";
	private const string PathProperty = "path";
	private const string MergeOperation = "merge";
	private const string InsertOperation = "insert";
	private const string ViewConfigDiffProperty = "viewConfigDiff";
	private const string ViewConfigDiffMarker = "SCHEMA_VIEW_CONFIG_DIFF";
	private const string DiffMarker = "SCHEMA_DIFF";

	/// <summary>
	/// Normalizes a web or mobile page body. Returns the (possibly unchanged) body plus the list of
	/// <c>"parent → child"</c> splits performed (empty when nothing changed).
	/// </summary>
	public static (string Body, IReadOnlyList<string> Splits) Normalize(string body) {
		if (string.IsNullOrWhiteSpace(body)) {
			return (body, []);
		}
		return PageSchemaTypeExtensions.FromBody(body) == PageSchemaType.Mobile
			? NormalizeMobile(body)
			: NormalizeWeb(body);
	}

	private static (string Body, IReadOnlyList<string> Splits) NormalizeMobile(string body) {
		JObject root;
		try {
			root = JObject.Parse(body);
		} catch {
			return (body, []);
		}
		if (root[ViewConfigDiffProperty] is not JArray viewConfigDiff) {
			return (body, []);
		}
		(JArray normalized, List<string> splits) = NormalizeOps(viewConfigDiff);
		if (splits.Count == 0) {
			return (body, []);
		}
		root[ViewConfigDiffProperty] = normalized;
		return (root.ToString(Formatting.Indented), splits);
	}

	private static (string Body, IReadOnlyList<string> Splits) NormalizeWeb(string body) {
		if (!PageSchemaSectionReader.TryRead(body, out string vcdContent, ViewConfigDiffMarker, DiffMarker)) {
			return (body, []);
		}
		JArray viewConfigDiff;
		try {
			viewConfigDiff = JArray.Parse(vcdContent);
		} catch {
			return (body, []); // the diff section is not a plain JSON array — leave the body untouched
		}
		(JArray normalized, List<string> splits) = NormalizeOps(viewConfigDiff);
		if (splits.Count == 0) {
			return (body, []);
		}
		string replacement = Environment.NewLine + normalized.ToString(Formatting.Indented) + Environment.NewLine;
		return PageSchemaSectionReader.TryReplaceSection(body, replacement, out string updated, ViewConfigDiffMarker, DiffMarker)
			? (updated, splits)
			: (body, []);
	}

	/// <summary>Splits every merge/insert op whose <c>values</c> carry nested named elements. Pure.</summary>
	private static (JArray Normalized, List<string> Splits) NormalizeOps(JArray viewConfigDiff) {
		var result = new JArray();
		var splits = new List<string>();
		foreach (JToken opToken in viewConfigDiff) {
			if (opToken is not JObject op
				|| op[ValuesProperty] is not JObject
				|| ResolveKind(op.Value<string>(OperationProperty)) is not { } kind) {
				result.Add(opToken.DeepClone());
				continue;
			}
			var parent = (JObject)op.DeepClone();
			var childOps = new List<JObject>();
			string parentName = parent.Value<string>(NameProperty)
				?? ((JObject)parent[ValuesProperty]).Value<string>(NameProperty) ?? "(unnamed)";
			ExtractNamedChildren((JObject)parent[ValuesProperty], childOps, splits, parentName, kind);
			// Keep the parent op when: it is an insert (it must exist so child inserts can target it), or it
			// is a merge whose values still carry something after the children were lifted out.
			if (kind == InsertOperation || (parent[ValuesProperty] is JObject parentValues && parentValues.HasValues)) {
				result.Add(parent);
			}
			foreach (JObject child in childOps) {
				result.Add(child);
			}
		}
		return (result, splits);
	}

	private static void ExtractNamedChildren(
		JObject values, List<JObject> ops, List<string> splits, string ownerName, string kind) {
		foreach (JProperty property in values.Properties().ToList()) {
			switch (property.Value) {
				case JObject child when IsNamedElement(child):
					ExtractChild(child, ops, splits, ownerName, kind, slot: property.Name, intoArray: false);
					property.Remove();
					break;
				case JObject nested when kind == MergeOperation:
					// A merge targets by name regardless of depth, so recurse into plain wrappers too.
					ExtractNamedChildren(nested, ops, splits, ownerName, kind);
					break;
				case JArray array:
					NormalizeArray(array, ops, splits, ownerName, kind, slot: property.Name);
					if (array.Count == 0 && kind == MergeOperation) {
						// An emptied array under MergeArrayHandling.Replace would wipe the existing items.
						// (For insert the empty array is kept so the lifted children can be inserted into it.)
						property.Remove();
					}
					break;
			}
		}
	}

	private static void NormalizeArray(
		JArray array, List<JObject> ops, List<string> splits, string ownerName, string kind, string slot) {
		var kept = new List<JToken>();
		foreach (JToken item in array) {
			if (item is JObject obj && IsNamedElement(obj)) {
				ExtractChild(obj, ops, splits, ownerName, kind, slot, intoArray: true);
				continue; // a named element is lifted into its own op, not kept inside the array
			}
			if (kind == MergeOperation) {
				switch (item) {
					case JObject plainObject:
						ExtractNamedChildren(plainObject, ops, splits, ownerName, kind);
						break;
					case JArray innerArray:
						NormalizeArray(innerArray, ops, splits, ownerName, kind, slot);
						break;
				}
			}
			kept.Add(item);
		}
		array.Clear();
		foreach (JToken token in kept) {
			array.Add(token);
		}
	}

	private static void ExtractChild(
		JObject element, List<JObject> ops, List<string> splits, string ownerName, string kind, string slot, bool intoArray) {
		string childName = element.Value<string>(NameProperty);
		var childValues = (JObject)element.DeepClone();
		var childOp = new JObject { [OperationProperty] = kind, [NameProperty] = childName };
		if (kind == MergeOperation) {
			// A merge matches the child by the op's name, so the name need not repeat inside values.
			childValues.Remove(NameProperty);
		} else {
			// An insert creates the child under its parent at the slot it occupied; the name stays inside
			// values as the new element's identity.
			childOp[ParentNameProperty] = ownerName;
			if (intoArray) {
				childOp[PathProperty] = new JArray(slot);
			} else {
				childOp[PropertyNameProperty] = slot;
			}
		}
		childOp[ValuesProperty] = childValues;
		ops.Add(childOp);
		splits.Add($"{ownerName} → {childName}");
		// Pull any grandchildren out of the extracted child too (added AFTER the child so an insert parent
		// always precedes its inserted children). The childOp holds a reference to childValues, so removals
		// done by this recursion are reflected in the already-added op.
		ExtractNamedChildren(childValues, ops, splits, childName, kind);
	}

	private static string ResolveKind(string operation) =>
		string.Equals(operation, MergeOperation, StringComparison.OrdinalIgnoreCase) ? MergeOperation
		: string.Equals(operation, InsertOperation, StringComparison.OrdinalIgnoreCase) ? InsertOperation
		: null;

	private static bool IsNamedElement(JObject obj) =>
		obj[NameProperty] is JValue { Type: JTokenType.String } value
		&& !string.IsNullOrWhiteSpace(value.Value<string>());
}
