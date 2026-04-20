using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Clio.Command;

internal static class PageBodyNormalizer {

	private static readonly string[] BindingProperties = { "control", "value" };

	/// <summary>
	/// Rewrites proxy-style bindings (e.g. <c>"control": "$UsrStatus"</c>) in
	/// <c>SCHEMA_VIEW_CONFIG_DIFF</c> to their canonical <c>$PDS_*</c> / <c>$Name</c> form
	/// so the body passes <see cref="SchemaValidationService.ValidateStandardFieldBindings"/>
	/// without a manual rewrite loop.
	/// Returns the original body unchanged on any parse or write failure.
	/// </summary>
	internal static string NormalizeProxyBindings(string body) {
		if (string.IsNullOrEmpty(body)) {
			return body;
		}
		try {
			Dictionary<string, string> modelPaths = SchemaValidationService.CollectViewModelPaths(body);
			if (modelPaths.Count == 0) {
				return body;
			}
			if (!PageSchemaSectionReader.TryRead(body, out string content, "SCHEMA_VIEW_CONFIG_DIFF")) {
				return body;
			}
			if (JsonNode.Parse(SchemaValidationService.NormalizeJson(content)) is not JsonArray viewConfigDiff) {
				return body;
			}
			if (!NormalizeElements(viewConfigDiff, modelPaths)) {
				return body;
			}
			string serialized = PageBodyEditor.SerializeJson(viewConfigDiff);
			return PageBodyEditor.ReplaceMarkerContent(body, "SCHEMA_VIEW_CONFIG_DIFF", serialized);
		} catch (JsonException) {
			return body;
		} catch (InvalidOperationException) {
			return body;
		} catch (RegexMatchTimeoutException) {
			return body;
		}
	}

	private static bool NormalizeElements(JsonArray viewConfigDiff, IReadOnlyDictionary<string, string> modelPaths) {
		bool changed = false;
		foreach (JsonObject item in viewConfigDiff.OfType<JsonObject>())
			changed |= TryNormalizeElement(item, modelPaths);
		return changed;
	}

	private static bool TryNormalizeElement(JsonObject item, IReadOnlyDictionary<string, string> modelPaths) {
		JsonObject values = item["values"] as JsonObject ?? item;
		if (!TryGetString(values, "type", out string type) ||
		    !SchemaValidationService.StandardFieldComponentTypes.Contains(type)) {
			return false;
		}
		return BindingProperties.Any(bindingProp => TryRewriteBinding(values, bindingProp, modelPaths));
	}

	private static bool TryRewriteBinding(JsonObject values, string bindingProp,
		IReadOnlyDictionary<string, string> modelPaths) {
		if (!TryGetString(values, bindingProp, out string expression) ||
		    !expression.StartsWith("$", StringComparison.Ordinal) ||
		    expression.Length < 2) {
			return false;
		}
		string attr = expression[1..];
		if (SchemaValidationService.IsAllowedDirectFieldBinding(attr)) {
			return false;
		}
		if (!modelPaths.TryGetValue(attr, out string? modelPath) ||
		    !modelPath.StartsWith("PDS.", StringComparison.OrdinalIgnoreCase)) {
			return false;
		}
		values[bindingProp] = SchemaValidationService.BuildExpectedBinding(modelPath);
		return true;
	}

	private static bool TryGetString(JsonObject obj, string key, out string value) {
		value = string.Empty;
		if (obj[key] is not JsonValue jsonValue) {
			return false;
		}
		try {
			string? candidate = jsonValue.GetValue<string>();
			if (string.IsNullOrWhiteSpace(candidate)) {
				return false;
			}
			value = candidate;
			return true;
		} catch (InvalidOperationException) {
			return false;
		}
	}
}
