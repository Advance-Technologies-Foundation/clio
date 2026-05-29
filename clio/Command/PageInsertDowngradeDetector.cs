namespace Clio.Command;

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Detects view-config operations that are downgraded from <c>insert</c> to an operation that cannot
/// stand on its own once the <c>insert</c> is dropped, between a page schema's prior body and the body
/// about to be saved, and reports them as advisory warnings.
/// </summary>
/// <remarks>
/// A <c>merge</c> patches, a <c>move</c> relocates, and a <c>remove</c> deletes an element that must
/// already exist. When a component this schema's own body introduces with an <c>insert</c> is replaced
/// (no <c>insert</c> kept):
/// <list type="bullet">
/// <item><c>merge</c> / <c>move</c> orphan the component — it has no element to patch/relocate and
/// disappears at runtime, UNLESS a parent schema also inserts it (a valid, if unusual, way to resolve
/// a duplicate insert).</item>
/// <item><c>remove</c> leaves a dangling delete that targets nothing — the component ends up absent
/// (which may be intended) but the leftover op misleadingly implies the element comes from a parent
/// schema. The clean way to drop a self-inserted component is to omit its insert.</item>
/// </list>
/// All findings are WARNINGS, not errors: the detector inspects only this schema's own prior body, so
/// it cannot tell an orphaning downgrade from a legitimate one where a parent schema inserts the same
/// name (full-hierarchy resolution is out of scope). It therefore advises rather than blocks. This is
/// the failure that append-mode dedupe (<see cref="PageBodyMerger.MergeArrayByName"/>, incoming wins by
/// <c>name</c>) and a hand-authored replace body both produce; the detector compares the resolved final
/// body against the prior body, so it covers <c>replace</c> and <c>append</c> identically.
/// </remarks>
internal static class PageInsertDowngradeDetector {

	private const string InsertOperation = "insert";
	private const string MergeOperation = "merge";
	private const string MoveOperation = "move";
	private const string RemoveOperation = "remove";
	private const string ViewConfigDiffMarker = "SCHEMA_VIEW_CONFIG_DIFF";
	private const string ViewConfigDiffProperty = "viewConfigDiff";

	/// <summary>
	/// Compares the schema's prior body with the body about to be written and returns an advisory warning
	/// for every component the prior body introduced with an <c>insert</c> whose <c>insert</c> the final
	/// body drops in favour of a <c>merge</c>, <c>move</c>, or <c>remove</c>.
	/// </summary>
	/// <param name="priorBody">
	/// Schema body currently stored on the server. May be <c>null</c> or empty for a brand-new replacing
	/// schema, in which case no downgrade can be detected.
	/// </param>
	/// <param name="finalBody">Body about to be saved (post-merge for append, verbatim for replace).</param>
	/// <returns>A read-only list of warning messages; empty when no downgrade is detected.</returns>
	public static IReadOnlyList<string> Detect(string priorBody, string finalBody) {
		var warnings = new List<string>();
		if (string.IsNullOrWhiteSpace(priorBody) || string.IsNullOrWhiteSpace(finalBody)) {
			return warnings;
		}
		Dictionary<string, HashSet<string>> priorOps = TryExtractOperationsByName(priorBody);
		Dictionary<string, HashSet<string>> finalOps = TryExtractOperationsByName(finalBody);
		if (priorOps == null || finalOps == null) {
			// One of the bodies is not parseable as a known page body. Skip the heuristic rather than
			// guess — the save must not be affected by a parse hiccup (fail open).
			return warnings;
		}
		foreach (KeyValuePair<string, HashSet<string>> prior in priorOps) {
			if (!prior.Value.Contains(InsertOperation)) {
				continue;
			}
			if (!finalOps.TryGetValue(prior.Key, out HashSet<string> finalNameOps)) {
				continue;
			}
			if (finalNameOps.Contains(InsertOperation)) {
				// The insert is preserved (e.g. updated, or kept alongside a sibling op) — not a downgrade.
				continue;
			}
			string transform = FirstTransformOperation(finalNameOps);
			if (transform != null) {
				warnings.Add(BuildOrphanMessage(prior.Key, transform));
			} else if (finalNameOps.Contains(RemoveOperation)) {
				warnings.Add(BuildDanglingRemoveMessage(prior.Key));
			}
		}
		return warnings;
	}

	private static string FirstTransformOperation(HashSet<string> operations) {
		if (operations.Contains(MergeOperation)) {
			return MergeOperation;
		}
		return operations.Contains(MoveOperation) ? MoveOperation : null;
	}

	private static string BuildOrphanMessage(string name, string transform) =>
		$"Component '{name}' is introduced by an 'insert' in this page's own body, but the submitted " +
		$"body changes it to a '{transform}' with no insert kept. A '{transform}' targets an element " +
		"that must already exist, so unless a parent schema inserts the same name the component is " +
		"orphaned and disappears at runtime. Edit the existing insert's values, or keep the insert, " +
		"instead of replacing it. See docs://mcp/guides/page-modification.";

	private static string BuildDanglingRemoveMessage(string name) =>
		$"Component '{name}' is introduced by an 'insert' in this page's own body, but the submitted " +
		"body replaces that insert with a 'remove'. Unless a parent schema inserts the same name, the " +
		"remove targets no element: it is a dangling op that misleadingly implies '" + name + "' comes " +
		"from a parent schema. To drop a self-inserted component, omit its insert instead of adding a " +
		"remove. See docs://mcp/guides/page-modification.";

	private static Dictionary<string, HashSet<string>> TryExtractOperationsByName(string body) {
		try {
			JArray viewConfigDiff = ReadViewConfigDiff(body);
			var operationsByName = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
			foreach (JToken item in viewConfigDiff) {
				if (item is not JObject obj) {
					continue;
				}
				string name = obj["name"]?.ToString();
				string operation = obj["operation"]?.ToString();
				if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(operation)) {
					continue;
				}
				if (!operationsByName.TryGetValue(name, out HashSet<string> operations)) {
					operations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
					operationsByName[name] = operations;
				}
				operations.Add(operation);
			}
			return operationsByName;
		} catch (JsonException) {
			return null;
		}
	}

	private static JArray ReadViewConfigDiff(string body) {
		if (PageSchemaTypeExtensions.FromBody(body) == PageSchemaType.Mobile) {
			JObject json = JObject.Parse(body);
			return json[ViewConfigDiffProperty] as JArray ?? new JArray();
		}
		if (!PageSchemaSectionReader.TryRead(body, out string content, ViewConfigDiffMarker)) {
			return new JArray();
		}
		string trimmed = content.Trim();
		return string.IsNullOrEmpty(trimmed) || trimmed == "[]" ? new JArray() : JArray.Parse(trimmed);
	}
}
