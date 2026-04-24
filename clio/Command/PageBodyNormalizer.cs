using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using JsonhCs;

namespace Clio.Command;

internal static class PageBodyNormalizer {

	private static readonly string[] BindingProperties = { "control", "value" };

	/// <summary>
	/// Rewrites legacy direct datasource bindings (for example
	/// <c>"control": "$PDS_UsrStatus"</c>) in <c>SCHEMA_VIEW_CONFIG_DIFF</c> to the
	/// declared view-model attribute binding that targets the same model path when such an
	/// attribute already exists in viewModelConfig/viewModelConfigDiff.
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
			string marker = ResolveViewConfigMarker(body, out string content);
			if (marker is null) {
				return body;
			}
			if (JsonNode.Parse(JsonhReader.ParseElement(content).Value.GetRawText()) is not JsonArray viewConfigDiff) {
				return body;
			}
			if (!NormalizeElements(viewConfigDiff, modelPaths)) {
				return body;
			}
			string serialized = PageBodyEditor.SerializeJson(viewConfigDiff);
			return PageBodyEditor.ReplaceMarkerContent(body, marker, serialized);
		} catch (JsonException) {
			return body;
		} catch (InvalidOperationException) {
			return body;
		} catch (RegexMatchTimeoutException) {
			return body;
		}
	}

	private static string ResolveViewConfigMarker(string body, out string content) {
		foreach (string candidate in new[] { "SCHEMA_VIEW_CONFIG_DIFF", "SCHEMA_DIFF" }) {
			if (PageSchemaSectionReader.TryRead(body, out content, candidate)) {
				return candidate;
			}
		}
		content = null;
		return null;
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
		string bindingAttribute = expression[1..];
		if (!IsDirectPdsBinding(bindingAttribute)) {
			return false;
		}
		string expectedModelPath = "PDS." + bindingAttribute["PDS_".Length..];
		string? declaredAttribute = modelPaths
			.Where(pair => string.Equals(pair.Value, expectedModelPath, StringComparison.OrdinalIgnoreCase))
			.Select(pair => pair.Key)
			.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
			.FirstOrDefault();
		if (string.IsNullOrWhiteSpace(declaredAttribute)) {
			return false;
		}
		values[bindingProp] = "$" + declaredAttribute;
		return true;
	}

	private static bool IsDirectPdsBinding(string bindingAttribute) =>
		bindingAttribute.StartsWith("PDS_", StringComparison.OrdinalIgnoreCase);

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
