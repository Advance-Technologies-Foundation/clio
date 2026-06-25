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
/// The diffs are applied against an empty base (<c>[]</c> for the view-config items tree, <c>{}</c> for the
/// path-addressed configs, unless the body carries a <c>viewModelConfig</c> / <c>modelConfig</c> base object).
/// The real base is the resolved mobile template, which is not available client-side; an empty base still
/// reproduces the differ's <i>self-consistency</i> errors, because an insert whose parent is missing is
/// physically appended to the root, so a subsequent child insert can still observe the in-diff parent and
/// trip the not-a-container check exactly as the server does.
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
	/// </summary>
	public static SchemaValidationResult Validate(string body) {
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
		ApplyPathDiff(root, ViewModelConfigDiff, ViewModelConfig, result);
		ApplyPathDiff(root, ModelConfigDiff, ModelConfig, result);
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

	private static void ApplyPathDiff(JObject root, string diffName, string baseName, SchemaValidationResult result) {
		if (root[diffName] is not JArray operations || operations.Count == 0) {
			return;
		}
		JToken baseObject = root[baseName] is JObject baseConfig ? baseConfig : new JObject();
		try {
			new JsonPathDiffApplier().Apply(baseObject, operations);
		} catch (JsonDiffApplierException ex) {
			result.IsValid = false;
			result.Errors.Add($"'{diffName}' cannot be applied by the Creatio differ: {ex.Message}.");
		} catch (Exception) {
			// See ApplyViewConfigDiff: non-differ failures are covered by the structural validators.
		}
	}
}
