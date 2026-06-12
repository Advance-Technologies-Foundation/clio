namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// One <c>crt.RunBusinessProcessRequest</c> button configuration parsed from a page body.
/// <see cref="ParameterCodes"/> are the process parameter CODES referenced by the button
/// (keys of <c>processParameters</c> / <c>parameterMappings</c> plus <c>recordIdProcessParameterName</c>).
/// </summary>
internal sealed record RunProcessButtonConfig(
	string ButtonName,
	string ProcessName,
	string ProcessRunType,
	IReadOnlyList<string> ParameterCodes);

/// <summary>
/// Extracts <c>crt.RunBusinessProcessRequest</c> button configurations from the
/// <c>SCHEMA_VIEW_CONFIG_DIFF</c> section of a Freedom UI page body (ENG-91168). Best-effort: returns
/// an empty list when the section is missing or is not parseable as JSON, so callers never throw on
/// non-run-process bodies.
/// </summary>
internal static class RunProcessButtonConfigReader {
	private const string ViewConfigDiffMarker = "SCHEMA_VIEW_CONFIG_DIFF";
	internal const string RunBusinessProcessRequestType = "crt.RunBusinessProcessRequest";
	private const string ButtonType = "crt.Button";

	private static readonly JsonDocumentOptions ParseOptions = new() {
		AllowTrailingCommas = true,
		CommentHandling = JsonCommentHandling.Skip
	};

	public static IReadOnlyList<RunProcessButtonConfig> Read(string body) {
		var configs = new List<RunProcessButtonConfig>();
		if (string.IsNullOrWhiteSpace(body)) {
			return configs;
		}
		if (!PageSchemaSectionReader.TryRead(body, out string viewConfigDiff, ViewConfigDiffMarker)
			|| string.IsNullOrWhiteSpace(viewConfigDiff)) {
			return configs;
		}
		JsonDocument document;
		try {
			document = JsonDocument.Parse(viewConfigDiff, ParseOptions);
		}
		catch (JsonException) {
			return configs;
		}
		using (document) {
			Walk(document.RootElement, currentButtonName: null, configs);
		}
		return configs;
	}

	private static void Walk(JsonElement element, string currentButtonName, List<RunProcessButtonConfig> configs) {
		switch (element.ValueKind) {
			case JsonValueKind.Array:
				foreach (JsonElement item in element.EnumerateArray()) {
					Walk(item, currentButtonName, configs);
				}
				return;
			case JsonValueKind.Object:
				string buttonName = ResolveButtonName(element, currentButtonName);
				if (TryReadRequest(element, out string requestType) && requestType == RunBusinessProcessRequestType) {
					configs.Add(BuildConfig(element, buttonName));
				}
				foreach (JsonProperty property in element.EnumerateObject()) {
					Walk(property.Value, buttonName, configs);
				}
				return;
			default:
				return;
		}
	}

	private static string ResolveButtonName(JsonElement objectElement, string currentButtonName) {
		bool isButton = objectElement.TryGetProperty("type", out JsonElement typeElement)
			&& typeElement.ValueKind == JsonValueKind.String
			&& typeElement.GetString() == ButtonType;
		bool isOperation = objectElement.TryGetProperty("values", out _)
			&& objectElement.TryGetProperty("name", out _);
		if ((isButton || isOperation)
			&& objectElement.TryGetProperty("name", out JsonElement nameElement)
			&& nameElement.ValueKind == JsonValueKind.String) {
			return nameElement.GetString();
		}
		return currentButtonName;
	}

	private static bool TryReadRequest(JsonElement objectElement, out string requestType) {
		requestType = null;
		if (objectElement.TryGetProperty("request", out JsonElement requestElement)
			&& requestElement.ValueKind == JsonValueKind.String) {
			requestType = requestElement.GetString();
			return true;
		}
		return false;
	}

	private static RunProcessButtonConfig BuildConfig(JsonElement requestObject, string buttonName) {
		string processName = null;
		string processRunType = null;
		var parameterCodes = new List<string>();
		if (requestObject.TryGetProperty("params", out JsonElement paramsElement)
			&& paramsElement.ValueKind == JsonValueKind.Object) {
			if (paramsElement.TryGetProperty("processName", out JsonElement processNameElement)
				&& processNameElement.ValueKind == JsonValueKind.String) {
				processName = processNameElement.GetString();
			}
			if (paramsElement.TryGetProperty("processRunType", out JsonElement runTypeElement)
				&& runTypeElement.ValueKind == JsonValueKind.String) {
				processRunType = runTypeElement.GetString();
			}
			CollectObjectKeys(paramsElement, "processParameters", parameterCodes);
			CollectObjectKeys(paramsElement, "parameterMappings", parameterCodes);
			if (paramsElement.TryGetProperty("recordIdProcessParameterName", out JsonElement recordIdElement)
				&& recordIdElement.ValueKind == JsonValueKind.String) {
				string code = recordIdElement.GetString();
				if (!string.IsNullOrWhiteSpace(code)) {
					parameterCodes.Add(code);
				}
			}
		}
		return new RunProcessButtonConfig(buttonName, processName, processRunType, parameterCodes);
	}

	private static void CollectObjectKeys(JsonElement paramsElement, string propertyName, List<string> codes) {
		if (paramsElement.TryGetProperty(propertyName, out JsonElement objectElement)
			&& objectElement.ValueKind == JsonValueKind.Object) {
			foreach (JsonProperty property in objectElement.EnumerateObject()) {
				if (!string.IsNullOrWhiteSpace(property.Name)) {
					codes.Add(property.Name);
				}
			}
		}
	}
}
