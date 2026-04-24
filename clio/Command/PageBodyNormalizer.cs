namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using JsonhCs;

internal static class PageBodyNormalizer {
	private static readonly string[] BindingProperties = [ "control", "value" ];
	private static readonly HashSet<string> StandardFieldComponentTypes = new(StringComparer.OrdinalIgnoreCase) {
		"crt.Input",
		"crt.NumberInput",
		"crt.Checkbox",
		"crt.DateTimePicker",
		"crt.ComboBox",
		"crt.RichTextEditor",
		"crt.PhoneInput",
		"crt.EmailInput",
		"crt.WebInput",
		"crt.ColorPicker",
		"crt.ImageInput",
		"crt.FileInput",
		"crt.EncryptedInput",
		"crt.Slider"
	};

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
			JsonElement parsedElement = JsonhReader.ParseElement(content).Value;
			if (JsonNode.Parse(parsedElement.GetRawText()) is not JsonArray viewConfigDiff) {
				return body;
			}
			if (!NormalizeElements(viewConfigDiff, modelPaths)) {
				return body;
			}
			string serialized = JsonSerializer.Serialize(viewConfigDiff);
			return ReplaceMarkerContent(body, marker, serialized);
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
		foreach (JsonObject item in viewConfigDiff.OfType<JsonObject>()) {
			changed |= TryNormalizeElement(item, modelPaths);
		}
		return changed;
	}

	private static bool TryNormalizeElement(JsonObject item, IReadOnlyDictionary<string, string> modelPaths) {
		JsonObject values = item["values"] as JsonObject ?? item;
		if (!TryGetString(values, "type", out string type) || !StandardFieldComponentTypes.Contains(type)) {
			return false;
		}
		return BindingProperties.Any(bindingProperty => TryRewriteBinding(values, bindingProperty, modelPaths));
	}

	private static bool TryRewriteBinding(
		JsonObject values,
		string bindingProperty,
		IReadOnlyDictionary<string, string> modelPaths) {
		if (!TryGetString(values, bindingProperty, out string expression) ||
		    !expression.StartsWith("$", StringComparison.Ordinal) ||
		    expression.Length < 2) {
			return false;
		}
		string attributeName = expression[1..];
		if (IsAllowedDirectFieldBinding(attributeName)) {
			return false;
		}
		if (!modelPaths.TryGetValue(attributeName, out string modelPath) ||
		    !modelPath.StartsWith("PDS.", StringComparison.OrdinalIgnoreCase)) {
			return false;
		}
		values[bindingProperty] = BuildExpectedBinding(modelPath);
		return true;
	}

	private static string ReplaceMarkerContent(string body, string markerName, string replacement) {
		string pattern = SchemaValidationService.BuildMarkerPattern(markerName);
		return Regex.Replace(
			body,
			pattern,
			$"/**{markerName}*/{replacement}/**{markerName}*/",
			RegexOptions.Singleline,
			SchemaValidationService.MarkerRegexTimeout);
	}

	private static bool TryGetString(JsonObject obj, string key, out string value) {
		value = string.Empty;
		if (obj[key] is not JsonValue jsonValue) {
			return false;
		}
		try {
			string candidate = jsonValue.GetValue<string>();
			if (string.IsNullOrWhiteSpace(candidate)) {
				return false;
			}
			value = candidate;
			return true;
		} catch (InvalidOperationException) {
			return false;
		}
	}

	private static bool IsAllowedDirectFieldBinding(string bindingAttribute) {
		return string.Equals(bindingAttribute, "Name", StringComparison.OrdinalIgnoreCase)
			|| bindingAttribute.StartsWith("PDS_", StringComparison.OrdinalIgnoreCase);
	}

	private static string BuildExpectedBinding(string modelPath) {
		if (string.Equals(modelPath, "PDS.Name", StringComparison.OrdinalIgnoreCase)) {
			return "$Name";
		}
		return "$" + modelPath.Replace(".", "_", StringComparison.Ordinal);
	}
}
