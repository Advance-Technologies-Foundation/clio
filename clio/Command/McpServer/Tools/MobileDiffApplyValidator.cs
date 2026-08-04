using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Validates a mobile page body by <b>applying</b> its diff sections through the faithful client-engine clones
/// (<see cref="JsonDiffApplier"/> for <c>viewConfigDiff</c>, <see cref="JsonPathDiffApplier"/> for
/// <c>viewModelConfigDiff</c> / <c>modelConfigDiff</c>) and surfacing any exception the Creatio differ would
/// raise. This is the mobile validate path's diff check: instead of silently injecting a missing slot
/// (the former heuristic auto-repair), it reproduces the server error
/// — most importantly <c>Item "X" is not a container for other items</c>, raised when a child insert targets a
/// slot the parent (also created in the same diff) does not declare — and returns it to the caller for analysis.
/// </summary>
/// <remarks>
/// The path-addressed diffs (<c>viewModelConfigDiff</c> / <c>modelConfigDiff</c>) are applied against a base
/// resolved in this priority: (1) a <c>viewModelConfig</c> / <c>modelConfig</c> base object the body itself
/// carries; (2) the resolved mobile template's own merged config, when the caller can supply it
/// (<c>update-page</c> / <c>sync-pages</c> resolve the page's parent template) — this is the faithful runtime
/// base, so an <c>insert</c> that appends to an array the TEMPLATE owns (e.g. a converted quick filter appended
/// to <c>Items.modelConfig.filterAttributes</c>) resolves and validates; (3) otherwise an empty base SEEDED
/// with an empty container at every insert target path, so a template-owned-array insert does not false-positive
/// as "not a container" when no template is available (<c>validate-page</c>, which receives only a body). In all
/// cases a genuine self-consistency error still surfaces — an insert whose parent the same diff declares as a
/// non-container (or a <c>viewConfigDiff</c> child insert into an undeclared slot) still trips the differ.
/// </remarks>
internal static class MobileDiffApplyValidator {

	private const string ViewConfigDiff = "viewConfigDiff";
	private const string ViewModelConfigDiff = "viewModelConfigDiff";
	private const string ViewModelConfig = "viewModelConfig";
	private const string ModelConfigDiff = "modelConfigDiff";
	private const string ModelConfig = "modelConfig";

	/// <summary>
	/// Applies the body's diff sections and reports any differ exception. Returns a valid result when the body
	/// cannot be parsed (the malformed-JSON case is already reported by <c>ValidateMobileBody</c>) or when every
	/// section applies cleanly. Never throws — an unexpected (non-differ) apply failure is swallowed, since
	/// malformed diff shapes are already covered by the structural mobile validators.
	/// <paramref name="templateViewModelConfigJson"/> / <paramref name="templateModelConfigJson"/> are the mobile
	/// template's own merged <c>viewModelConfig</c> / <c>modelConfig</c> (the base the page's diff layers over at
	/// runtime), as JSON. When null (no template context, or the template could not be read) the path-diff base
	/// falls back to an insert-path-seeded empty object; see the type remarks.
	/// </summary>
	public static SchemaValidationResult Validate(
		string body, string templateViewModelConfigJson = null, string templateModelConfigJson = null) {
		var result = new SchemaValidationResult { IsValid = true };
		if (string.IsNullOrWhiteSpace(body)) {
			return result;
		}
		JObject root;
		try {
			root = JObject.Parse(body);
		} catch (JsonException) {
			return result;
		}
		ApplyViewConfigDiff(root, result);
		ApplyPathDiff(root, ViewModelConfigDiff, ViewModelConfig, templateViewModelConfigJson, result);
		ApplyPathDiff(root, ModelConfigDiff, ModelConfig, templateModelConfigJson, result);
		return result;
	}

	private static void ApplyViewConfigDiff(JObject root, SchemaValidationResult result) {
		if (root[ViewConfigDiff] is not JArray operations || operations.Count == 0) {
			return;
		}
		try {
			new JsonDiffApplier().Apply(new JArray(), operations);
		} catch (JsonDiffApplierException ex) {
			result.IsValid = false;
			result.Errors.Add(
				$"'{ViewConfigDiff}' cannot be applied by the Creatio differ: {ex.Message}. " +
				"Fix the diff so each insert targets a slot (propertyName) the parent declares.");
		} catch (Exception) {
			// Malformed diff shapes (non-object entries, wrong value kinds) are already reported by the
			// structural mobile validators; only the faithful differ exceptions above are actionable here.
		}
	}

	private static void ApplyPathDiff(
		JObject root, string diffName, string baseName, string templateConfigJson, SchemaValidationResult result) {
		if (root[diffName] is not JArray operations || operations.Count == 0) {
			return;
		}
		JToken baseObject = ResolvePathDiffBase(root[baseName], templateConfigJson, operations);
		try {
			new JsonPathDiffApplier().Apply(baseObject, operations);
		} catch (JsonDiffApplierException ex) {
			result.IsValid = false;
			result.Errors.Add($"'{diffName}' cannot be applied by the Creatio differ: {ex.Message}.");
		} catch (Exception) {
			// See ApplyViewConfigDiff: non-differ failures are covered by the structural validators.
		}
	}

	/// <summary>
	/// Resolves the base a path-addressed diff is applied against: the body's own base section if present;
	/// otherwise the resolved mobile template's merged config (parsed from <paramref name="templateConfigJson"/>);
	/// otherwise an empty object seeded with an empty container at every insert target path (so an insert that
	/// appends to an array the template owns does not false-positive when no template is available). Always
	/// returns a fresh, mutable token — the applier mutates the base in place.
	/// </summary>
	private static JToken ResolvePathDiffBase(JToken bodyBase, string templateConfigJson, JArray operations) {
		if (bodyBase is JObject bodyConfig) {
			return bodyConfig;
		}
		if (!string.IsNullOrWhiteSpace(templateConfigJson)) {
			try {
				if (JToken.Parse(templateConfigJson) is JObject templateConfig) {
					return templateConfig;
				}
			} catch (JsonException) {
				// Unparseable template config → fall through to the seeded empty base.
			}
		}
		return SeedBaseForInserts(operations);
	}

	/// <summary>
	/// Builds an empty base pre-seeded with an empty container along every insert operation's target path: each
	/// intermediate path segment becomes an object, the last becomes an empty array (an insert appends to an
	/// array). This lets an insert that targets an array the mobile template owns apply cleanly when no template
	/// base is available, WITHOUT masking a genuine self-consistency error — an insert whose parent another
	/// operation in the same diff sets to a non-container still trips the differ, because the seed is only the
	/// starting shape and later operations overwrite it.
	/// </summary>
	private static JObject SeedBaseForInserts(JArray operations) {
		var baseObject = new JObject();
		foreach (JToken operationToken in operations) {
			if (operationToken is not JObject operation
				|| !string.Equals(operation["operation"]?.Value<string>(), "insert", StringComparison.OrdinalIgnoreCase)
				|| operation["path"] is not JArray path || path.Count == 0) {
				continue;
			}
			JObject cursor = baseObject;
			for (int i = 0; i < path.Count - 1; i++) {
				string segment = path[i]?.Value<string>();
				if (segment is null) {
					cursor = null;
					break;
				}
				if (cursor[segment] is null) {
					var created = new JObject();
					cursor[segment] = created;
					cursor = created;
				} else if (cursor[segment] is JObject child) {
					cursor = child;
				} else {
					// This segment is already seeded as a non-object (another insert's array/leaf), so the two
					// insert paths conflict. Do NOT overwrite it — leave this path unseeded so the applier
					// surfaces the genuine self-consistency error rather than a masked one.
					cursor = null;
					break;
				}
			}
			if (cursor is null) {
				continue;
			}
			// Seed the leaf as an empty array (an insert appends to an array). Leave any existing value
			// untouched: a shared array is reused; a non-array leaf is a conflict the applier will surface.
			string leaf = path[^1]?.Value<string>();
			if (leaf is not null && cursor[leaf] is null) {
				cursor[leaf] = new JArray();
			}
		}
		return baseObject;
	}
}
