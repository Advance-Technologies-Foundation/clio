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
/// carries; (2) the target page's own merged config, when the caller can supply it (<c>update-page</c> /
/// <c>sync-pages</c> resolve the page's merged config — see <c>MobilePageMergedConfigResolver</c>) — this is the
/// faithful runtime base, so an <c>insert</c> that appends to an array the TEMPLATE owns (e.g. a converted quick
/// filter appended to <c>Items.modelConfig.filterAttributes</c>) resolves and validates; (3) otherwise an empty
/// base SEEDED with an empty container at every insert target path, so a template-owned-array insert does not
/// false-positive as "not a container" when no base is available (<c>validate-page</c>, which receives only a
/// body). The base is resolved LAZILY through the supplied delegate — it is invoked at most once, and only when a
/// non-empty path diff carries no own base object, so a <c>viewConfigDiff</c>-only body spends no read. In all
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
	/// <paramref name="templateViewModelConfigJson"/> / <paramref name="templateModelConfigJson"/> are the target
	/// page's own merged <c>viewModelConfig</c> / <c>modelConfig</c> (the base the page's diff layers over at
	/// runtime), as JSON. When null the path-diff base falls back to an insert-path-seeded empty object; see the
	/// type remarks. This eager overload is for callers that already hold the base (tests); callers that must
	/// READ it should use the lazy <see cref="Validate(string, Func{ValueTuple{string, string}})"/> overload so a
	/// body with no path diff spends no read.
	/// </summary>
	public static SchemaValidationResult Validate(
		string body, string templateViewModelConfigJson = null, string templateModelConfigJson = null) =>
		Validate(body, () => (templateViewModelConfigJson, templateModelConfigJson));

	/// <summary>
	/// Applies the body's diff sections, resolving the path-diff base LAZILY through
	/// <paramref name="resolveTemplateBase"/>. The delegate is invoked at most once and ONLY when a non-empty
	/// <c>viewModelConfigDiff</c> / <c>modelConfigDiff</c> carries no own base object — so a
	/// <c>viewConfigDiff</c>-only body (or one with an inline base) triggers no resolution (no get-page read). A
	/// null delegate means "no base available"; the oracle then seeds its own from the insert paths.
	/// </summary>
	public static SchemaValidationResult Validate(
		string body, Func<(string ViewModelConfigJson, string ModelConfigJson)> resolveTemplateBase) {
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
		// Memoize so viewModelConfigDiff and modelConfigDiff share a single resolution.
		Lazy<(string ViewModelConfigJson, string ModelConfigJson)> lazyBase =
			resolveTemplateBase is null ? null : new(resolveTemplateBase);
		ApplyViewConfigDiff(root, result);
		ApplyPathDiff(root, ViewModelConfigDiff, ViewModelConfig, () => lazyBase is null ? null : lazyBase.Value.ViewModelConfigJson, result);
		ApplyPathDiff(root, ModelConfigDiff, ModelConfig, () => lazyBase is null ? null : lazyBase.Value.ModelConfigJson, result);
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
		JObject root, string diffName, string baseName, Func<string> templateConfigProvider, SchemaValidationResult result) {
		if (root[diffName] is not JArray operations || operations.Count == 0) {
			// Empty / absent path diff: return BEFORE resolving the base, so a body with no path diff never
			// invokes the (potentially I/O-bound) base provider.
			return;
		}
		JToken baseObject = ResolvePathDiffBase(root[baseName], templateConfigProvider, operations);
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
	/// otherwise the page's merged config (resolved LAZILY via <paramref name="templateConfigProvider"/> — invoked
	/// only here, after the body carries no own base, so a body with an inline base spends no read); otherwise an
	/// empty object seeded with an empty container at every insert target path (so an insert that appends to an
	/// array the template owns does not false-positive when no base is available). Always returns a fresh, mutable
	/// token — the applier mutates the base in place.
	/// </summary>
	private static JToken ResolvePathDiffBase(JToken bodyBase, Func<string> templateConfigProvider, JArray operations) {
		if (bodyBase is JObject bodyConfig) {
			return bodyConfig;
		}
		string templateConfigJson = templateConfigProvider?.Invoke();
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
			if (operationToken is JObject operation
				&& string.Equals(operation["operation"]?.Value<string>(), "insert", StringComparison.OrdinalIgnoreCase)
				&& operation["path"] is JArray path && path.Count > 0) {
				SeedInsertPath(baseObject, path);
			}
		}
		return baseObject;
	}

	/// <summary>
	/// Seeds a single insert operation's target path into <paramref name="baseObject"/>: descends/creates an
	/// object at each intermediate segment, then seeds the leaf as an empty array (an insert appends to an
	/// array). Leaves any existing leaf value untouched — a shared array is reused; a non-array leaf is a conflict
	/// the applier will surface. Does nothing when the path collides with a non-object along the way.
	/// </summary>
	private static void SeedInsertPath(JObject baseObject, JArray path) {
		JObject parent = DescendToLeafParent(baseObject, path);
		if (parent is null) {
			return;
		}
		string leaf = path[^1]?.Value<string>();
		if (leaf is not null && parent[leaf] is null) {
			parent[leaf] = new JArray();
		}
	}

	/// <summary>
	/// Walks the intermediate segments (all but the last), creating an empty object where one is absent, and
	/// returns the container the leaf lives in. Returns null when a segment is null or is already seeded as a
	/// non-object (another insert's array/leaf) — the two insert paths conflict, so the path is left unseeded and
	/// the applier surfaces the genuine self-consistency error rather than a masked one.
	/// </summary>
	private static JObject DescendToLeafParent(JObject cursor, JArray path) {
		for (int i = 0; i < path.Count - 1; i++) {
			string segment = path[i]?.Value<string>();
			if (segment is null) {
				return null;
			}
			switch (cursor[segment]) {
				case null:
					var created = new JObject();
					cursor[segment] = created;
					cursor = created;
					break;
				case JObject child:
					cursor = child;
					break;
				default:
					return null;
			}
		}
		return cursor;
	}
}
