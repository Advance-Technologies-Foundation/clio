namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

public static class SchemaValidationService
{
	private const string SchemaViewConfigDiff = "SCHEMA_VIEW_CONFIG_DIFF";
	private const string SchemaViewModelConfigDiff = "SCHEMA_VIEW_MODEL_CONFIG_DIFF";
	private const string SchemaViewModelConfig = "SCHEMA_VIEW_MODEL_CONFIG";

	public static readonly string[] RequiredMarkerNames = {
		"SCHEMA_DEPS",
		"SCHEMA_ARGS",
		SchemaViewConfigDiff,
		"SCHEMA_HANDLERS",
		"SCHEMA_CONVERTERS",
		"SCHEMA_VALIDATORS"
	};

	public static readonly string[][] AlternateMarkerPairs = {
		new[] { SchemaViewModelConfigDiff, SchemaViewModelConfig },
		new[] { "SCHEMA_MODEL_CONFIG_DIFF", "SCHEMA_MODEL_CONFIG" }
	};

	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);
	private static readonly Regex ResourceStringPattern = new(
		@"^#ResourceString\(([^)]+)\)#$",
		RegexOptions.Compiled,
		RegexTimeout);
	private const string DatasourceCaptionPrefix = "$Resources.Strings.";
	private static readonly Regex CustomFieldResourcePattern = new(
		@"^Usr[A-Za-z0-9_]*_(label|caption)$",
		RegexOptions.Compiled | RegexOptions.IgnoreCase,
		RegexTimeout);
	internal static readonly HashSet<string> StandardFieldComponentTypes = new(StringComparer.OrdinalIgnoreCase) {
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

	public static string BuildMarkerPattern(string markerName) {
		return @"/\*\*" + Regex.Escape(markerName) + @"\*/(.*?)/\*\*" + Regex.Escape(markerName) + @"\*/";
	}

	public static SchemaValidationResult ValidateMarkerIntegrity(string jsBody) {
		var result = new SchemaValidationResult { IsValid = true };
		if (string.IsNullOrEmpty(jsBody)) {
			result.IsValid = false;
			result.Errors.Add("JS body is null or empty.");
			return result;
		}
		foreach (string markerName in RequiredMarkerNames) {
			string pattern = BuildMarkerPattern(markerName);
			if (!Regex.IsMatch(jsBody, pattern, RegexOptions.Singleline, RegexTimeout)) {
				result.IsValid = false;
				result.Errors.Add(markerName);
			}
		}
		foreach (string[] pair in AlternateMarkerPairs) {
			string pattern0 = BuildMarkerPattern(pair[0]);
			string pattern1 = BuildMarkerPattern(pair[1]);
			bool hasFirst = Regex.IsMatch(jsBody, pattern0, RegexOptions.Singleline, RegexTimeout);
			bool hasSecond = Regex.IsMatch(jsBody, pattern1, RegexOptions.Singleline, RegexTimeout);
			if (!hasFirst && !hasSecond) {
				result.IsValid = false;
				result.Errors.Add($"{pair[0]} or {pair[1]}");
			}
		}
		return result;
	}

	private static readonly string[] JsonArrayMarkers = {
		SchemaViewConfigDiff,
		"SCHEMA_DIFF",
		SchemaViewModelConfigDiff,
		"SCHEMA_MODEL_CONFIG_DIFF",
		"SCHEMA_DEPS"
	};

	private static readonly string[] JsonObjectMarkers = {
		SchemaViewModelConfig,
		"SCHEMA_MODEL_CONFIG",
		"SCHEMA_CONVERTERS",
		"SCHEMA_VALIDATORS"
	};

	public static bool TryParseResources(
		string? resources,
		out Dictionary<string, string> parsedResources,
		out string errorMessage) {
		parsedResources = null!;
		errorMessage = string.Empty;
		if (string.IsNullOrWhiteSpace(resources)) {
			parsedResources = null!;
			return true;
		}
		if (!TryParseJsonDocument(resources, out JsonDocument document, out errorMessage)) {
			return false;
		}
		using (document) {
			if (document.RootElement.ValueKind != JsonValueKind.Object) {
				errorMessage = "resources must be a valid JSON object string";
				return false;
			}
			var result = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (JsonProperty property in document.RootElement.EnumerateObject()) {
				if (property.Value.ValueKind != JsonValueKind.String) {
					errorMessage = "resources must be a valid JSON object string";
					return false;
				}
				result[property.Name] = property.Value.GetString() ?? string.Empty;
			}
			parsedResources = result;
			return true;
		}
	}

	public static SchemaValidationResult ValidateMarkerContent(string jsBody) {
		var result = new SchemaValidationResult { IsValid = true };
		if (string.IsNullOrEmpty(jsBody)) {
			result.IsValid = false;
			result.Errors.Add("JS body is null or empty.");
			return result;
		}
		if (!ValidateMarkers(jsBody, JsonArrayMarkers, result)) {
			return result;
		}
		ValidateMarkers(jsBody, JsonObjectMarkers, result);
		return result;
	}

	private static bool ValidateMarkers(
		string jsBody,
		IEnumerable<string> markers,
		SchemaValidationResult result) {
		foreach (string marker in markers) {
			if (!PageSchemaSectionReader.TryRead(jsBody, out string content, marker)) {
				continue;
			}
			if (!TryParseJson(content, marker, result)) {
				return false;
			}
		}
		return true;
	}

	private static bool TryParseJson(string content, string marker, SchemaValidationResult result) {
		if (TryParseJsonDocument(content, out JsonDocument document, out string errorMessage)) {
			using (document) {
				return true;
			}
		}
		result.IsValid = false;
		result.Errors.Add($"Invalid JSON in {marker}: {errorMessage}");
		return false;
	}

	public static SchemaValidationResult ValidateColumnBindings(string jsBody) {
		var result = new SchemaValidationResult { IsValid = true };
		if (string.IsNullOrEmpty(jsBody)) {
			return result;
		}
		if (!PageSchemaSectionReader.TryRead(jsBody, out string vcdContent, "SCHEMA_VIEW_CONFIG_DIFF", "SCHEMA_DIFF")) {
			return result;
		}
		var columnCodes = ExtractDataTableColumnCodes(vcdContent);
		if (columnCodes.Count == 0) {
			return result;
		}
		HashSet<string> boundPaths = new(StringComparer.OrdinalIgnoreCase);
		if (PageSchemaSectionReader.TryRead(jsBody, out string vmContent, "SCHEMA_VIEW_MODEL_CONFIG_DIFF")) {
			CollectModelPaths(vmContent, boundPaths);
		}
		if (PageSchemaSectionReader.TryRead(jsBody, out string vmContent2, "SCHEMA_VIEW_MODEL_CONFIG")) {
			CollectModelPaths(vmContent2, boundPaths);
		}
		foreach (string code in columnCodes) {
			string expectedPath = code.Replace("_", ".", StringComparison.Ordinal);
			if (!boundPaths.Contains(expectedPath)) {
				result.Errors.Add($"DataTable column '{code}' has no matching binding (expected path '{expectedPath}')");
			}
		}
		if (result.Errors.Count > 0) {
			result.IsValid = false;
		}
		return result;
	}

	public static SchemaValidationResult ValidateStandardFieldBindings(
		string jsBody,
		IReadOnlyDictionary<string, string>? explicitResources = null) {
		var result = new SchemaValidationResult { IsValid = true };
		if (string.IsNullOrEmpty(jsBody)) {
			return result;
		}
		if (!PageSchemaSectionReader.TryRead(jsBody, out string viewConfigContent, "SCHEMA_VIEW_CONFIG_DIFF", "SCHEMA_DIFF")) {
			return result;
		}
		if (!TryParseJsonDocument(viewConfigContent, out JsonDocument viewConfigDocument, out _)) {
			return result;
		}
		Dictionary<string, string> modelPaths = CollectViewModelPaths(jsBody);
		using (viewConfigDocument) {
			ValidateFieldComponents(viewConfigDocument.RootElement, modelPaths, explicitResources, result);
		}
		if (result.Errors.Count > 0) {
			result.IsValid = false;
		}
		return result;
	}

	private static List<string> ExtractDataTableColumnCodes(string vcdContent) {
		if (!TryParseJsonDocument(vcdContent, out JsonDocument document, out _)) {
			return [];
		}
		using (document) {
			if (document.RootElement.ValueKind != JsonValueKind.Array) {
				return [];
			}
			var codes = new List<string>();
			foreach (JsonElement item in document.RootElement.EnumerateArray()) {
				if (TryGetDataTableColumns(item, out JsonElement columns)) {
					AddColumnCodes(columns, codes);
				}
			}
			return codes;
		}
	}

	private static void CollectModelPaths(string markerContent, HashSet<string> paths) {
		if (!TryParseJsonDocument(markerContent, out JsonDocument document, out _)) {
			return;
		}
		using (document) {
			CollectPathsFromElement(document.RootElement, paths);
		}
	}

	internal static Dictionary<string, string> CollectViewModelPaths(string jsBody) {
		var modelPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		CollectViewModelPathsFromMarker(jsBody, modelPaths, "SCHEMA_VIEW_MODEL_CONFIG_DIFF");
		CollectViewModelPathsFromMarker(jsBody, modelPaths, "SCHEMA_VIEW_MODEL_CONFIG");
		return modelPaths;
	}

	private static void CollectViewModelPathsFromMarker(
		string jsBody,
		Dictionary<string, string> modelPaths,
		string markerName) {
		if (!PageSchemaSectionReader.TryRead(jsBody, out string markerContent, markerName)) {
			return;
		}
		if (!TryParseJsonDocument(markerContent, out JsonDocument document, out _)) {
			return;
		}
		using (document) {
			CollectNamedModelPaths(document.RootElement, modelPaths);
		}
	}

	private static void CollectNamedModelPaths(JsonElement element, Dictionary<string, string> modelPaths) {
		if (element.ValueKind == JsonValueKind.Object) {
			foreach (JsonProperty property in element.EnumerateObject()) {
				if (property.Value.ValueKind == JsonValueKind.Object &&
				    property.Value.TryGetProperty("modelConfig", out JsonElement modelConfig) &&
				    modelConfig.TryGetProperty("path", out JsonElement pathElement) &&
				    pathElement.ValueKind == JsonValueKind.String) {
					string? path = pathElement.GetString();
					if (!string.IsNullOrWhiteSpace(path)) {
						modelPaths[property.Name] = path;
					}
				}
				CollectNamedModelPaths(property.Value, modelPaths);
			}
		} else if (element.ValueKind == JsonValueKind.Array) {
			foreach (JsonElement item in element.EnumerateArray()) {
				CollectNamedModelPaths(item, modelPaths);
			}
		}
	}

	private static void ValidateFieldComponents(
		JsonElement element,
		IReadOnlyDictionary<string, string> modelPaths,
		IReadOnlyDictionary<string, string>? explicitResources,
		SchemaValidationResult result,
		bool checkSelf = true) {
		if (element.ValueKind == JsonValueKind.Object) {
			bool wrappedValues = false;
			if (checkSelf && TryResolveFieldComponent(element, out JsonElement componentValues, out string fieldName, out string componentType, out wrappedValues)) {
				ValidateFieldComponent(componentValues, fieldName, componentType, modelPaths, explicitResources, result);
			}
			foreach (JsonProperty property in element.EnumerateObject()) {
				bool childCheckSelf = !(wrappedValues && property.NameEquals("values"));
				ValidateFieldComponents(property.Value, modelPaths, explicitResources, result, childCheckSelf);
			}
		} else if (element.ValueKind == JsonValueKind.Array) {
			foreach (JsonElement item in element.EnumerateArray()) {
				ValidateFieldComponents(item, modelPaths, explicitResources, result);
			}
		}
	}

	private static bool TryResolveFieldComponent(
		JsonElement element,
		out JsonElement componentValues,
		out string fieldName,
		out string componentType,
		out bool wrappedValues) {
		componentValues = default;
		fieldName = string.Empty;
		componentType = string.Empty;
		wrappedValues = false;
		if (element.ValueKind != JsonValueKind.Object) {
			return false;
		}
		if (element.TryGetProperty("values", out JsonElement valuesElement) &&
		    valuesElement.ValueKind == JsonValueKind.Object &&
		    TryGetFieldType(valuesElement, out componentType)) {
			componentValues = valuesElement;
			fieldName = GetFieldName(element, valuesElement);
			wrappedValues = true;
			return true;
		}
		if (TryGetFieldType(element, out componentType)) {
			componentValues = element;
			fieldName = GetFieldName(element, element);
			return true;
		}
		return false;
	}

	private static string GetFieldName(JsonElement wrapperElement, JsonElement valuesElement) {
		if (wrapperElement.TryGetProperty("name", out JsonElement wrapperName) &&
		    wrapperName.ValueKind == JsonValueKind.String &&
		    !string.IsNullOrWhiteSpace(wrapperName.GetString())) {
			return wrapperName.GetString()!;
		}
		if (valuesElement.TryGetProperty("name", out JsonElement valuesName) &&
		    valuesName.ValueKind == JsonValueKind.String &&
		    !string.IsNullOrWhiteSpace(valuesName.GetString())) {
			return valuesName.GetString()!;
		}
		return string.Empty;
	}

	private static bool TryGetFieldType(JsonElement element, out string componentType) {
		componentType = string.Empty;
		if (!element.TryGetProperty("type", out JsonElement typeElement) || typeElement.ValueKind != JsonValueKind.String) {
			return false;
		}
		string? type = typeElement.GetString();
		if (string.IsNullOrWhiteSpace(type) || !StandardFieldComponentTypes.Contains(type)) {
			return false;
		}
		componentType = type;
		return true;
	}

	private static void ValidateFieldComponent(
		JsonElement componentValues,
		string fieldName,
		string componentType,
		IReadOnlyDictionary<string, string> modelPaths,
		IReadOnlyDictionary<string, string>? explicitResources,
		SchemaValidationResult result) {
		string fieldDisplayName = !string.IsNullOrWhiteSpace(fieldName) ? fieldName : componentType;
		if (TryGetBindingAttribute(componentValues, out string bindingProperty, out string bindingExpression, out string bindingAttribute) &&
		    !IsAllowedDirectFieldBinding(bindingAttribute) &&
		    modelPaths.TryGetValue(bindingAttribute, out string modelPath) &&
		    modelPath.StartsWith("PDS.", StringComparison.OrdinalIgnoreCase)) {
			result.Errors.Add(
				$"Standard field '{fieldDisplayName}' uses proxy binding '{bindingExpression}' via '{bindingProperty}' for datasource path '{modelPath}'. Use '{BuildExpectedBinding(modelPath)}' instead.");
		}
		if (TryGetStringProperty(componentValues, "label", out string labelExpression) &&
		    TryGetDatasourceCaptionKey(labelExpression, out string datasourceKey) &&
		    explicitResources != null &&
		    !explicitResources.ContainsKey(datasourceKey)) {
			result.Warnings.Add(
				$"Standard field '{fieldDisplayName}' has label '{labelExpression}' but resource key '{datasourceKey}' is not in the provided resources — the label will render blank in the designer.");
		}
		if (!TryGetCaptionExpression(componentValues, out string captionExpression) ||
		    !TryGetResourceStringKey(captionExpression, out string resourceKey) ||
		    !CustomFieldResourcePattern.IsMatch(resourceKey)) {
			return;
		}
		string preferredCaption = TryResolvePreferredCaption(modelPaths, bindingAttribute, out string preferredCaptionBinding)
			? preferredCaptionBinding
			: "$Resources.Strings.<datasource-caption>";
		if (explicitResources == null || !explicitResources.TryGetValue(resourceKey, out string explicitValue) || string.IsNullOrWhiteSpace(explicitValue)) {
			result.Errors.Add(
				$"Standard field '{fieldDisplayName}' uses '{captionExpression}' without an explicit resources entry. Prefer datasource caption '{preferredCaption}' for data-bound fields.");
			return;
		}
		result.Warnings.Add(
			$"Standard field '{fieldDisplayName}' uses custom resource key '{resourceKey}'. Prefer datasource caption '{preferredCaption}' for data-bound fields.");
	}

	private static bool TryResolvePreferredCaption(
		IReadOnlyDictionary<string, string> modelPaths,
		string bindingAttribute,
		out string preferredCaptionBinding) {
		preferredCaptionBinding = string.Empty;
		if (!string.IsNullOrWhiteSpace(bindingAttribute) && bindingAttribute.StartsWith("PDS_", StringComparison.OrdinalIgnoreCase)) {
			preferredCaptionBinding = $"$Resources.Strings.{bindingAttribute}";
			return true;
		}
		if (!string.IsNullOrWhiteSpace(bindingAttribute) &&
		    modelPaths.TryGetValue(bindingAttribute, out string modelPath) &&
		    modelPath.StartsWith("PDS.", StringComparison.OrdinalIgnoreCase)) {
			preferredCaptionBinding = $"$Resources.Strings.{modelPath.Replace(".", "_", StringComparison.Ordinal)}";
			return true;
		}
		return false;
	}

	private static bool TryGetBindingAttribute(
		JsonElement componentValues,
		out string bindingProperty,
		out string bindingExpression,
		out string bindingAttribute) {
		bindingProperty = string.Empty;
		bindingExpression = string.Empty;
		bindingAttribute = string.Empty;
		if (TryGetStringProperty(componentValues, "control", out bindingExpression)) {
			bindingProperty = "control";
		} else if (TryGetStringProperty(componentValues, "value", out bindingExpression)) {
			bindingProperty = "value";
		} else {
			return false;
		}
		if (!bindingExpression.StartsWith("$", StringComparison.Ordinal) || bindingExpression.Length == 1) {
			return false;
		}
		bindingAttribute = bindingExpression[1..];
		return true;
	}

	private static bool TryGetCaptionExpression(JsonElement componentValues, out string captionExpression) {
		return TryGetStringProperty(componentValues, "label", out captionExpression)
			|| TryGetStringProperty(componentValues, "caption", out captionExpression);
	}

	private static bool TryGetStringProperty(JsonElement element, string propertyName, out string value) {
		value = string.Empty;
		if (!element.TryGetProperty(propertyName, out JsonElement propertyValue) ||
		    propertyValue.ValueKind != JsonValueKind.String) {
			return false;
		}
		string? candidate = propertyValue.GetString();
		if (string.IsNullOrWhiteSpace(candidate)) {
			return false;
		}
		value = candidate;
		return true;
	}

	private static bool TryGetResourceStringKey(string expression, out string resourceKey) {
		resourceKey = string.Empty;
		Match match = ResourceStringPattern.Match(expression);
		if (!match.Success) {
			return false;
		}
		resourceKey = match.Groups[1].Value;
		return true;
	}

	private static bool TryGetDatasourceCaptionKey(string expression, out string resourceKey) {
		resourceKey = string.Empty;
		if (!expression.StartsWith(DatasourceCaptionPrefix, StringComparison.Ordinal) ||
		    expression.Length <= DatasourceCaptionPrefix.Length) {
			return false;
		}
		resourceKey = expression[DatasourceCaptionPrefix.Length..];
		return !string.IsNullOrWhiteSpace(resourceKey);
	}

	internal static bool IsAllowedDirectFieldBinding(string bindingAttribute) {
		return string.Equals(bindingAttribute, "Name", StringComparison.OrdinalIgnoreCase)
			|| bindingAttribute.StartsWith("PDS_", StringComparison.OrdinalIgnoreCase);
	}

	internal static string BuildExpectedBinding(string modelPath) {
		if (string.Equals(modelPath, "PDS.Name", StringComparison.OrdinalIgnoreCase)) {
			return "$Name";
		}
		return "$" + modelPath.Replace(".", "_", StringComparison.Ordinal);
	}

	private static bool TryGetDataTableColumns(JsonElement item, out JsonElement columns) {
		columns = default;
		return item.TryGetProperty("name", out JsonElement nameElement)
			&& string.Equals(nameElement.GetString(), "DataTable", StringComparison.Ordinal)
			&& item.TryGetProperty("values", out JsonElement values)
			&& values.TryGetProperty("columns", out columns)
			&& columns.ValueKind == JsonValueKind.Array;
	}

	private static void AddColumnCodes(JsonElement columns, List<string> codes) {
		foreach (JsonElement column in columns.EnumerateArray()) {
			if (TryGetColumnCode(column, out string code)) {
				codes.Add(code);
			}
		}
	}

	private static bool TryGetColumnCode(JsonElement column, out string code) {
		code = string.Empty;
		if (!column.TryGetProperty("code", out JsonElement codeElement)) {
			return false;
		}
		string? candidate = codeElement.GetString();
		if (string.IsNullOrEmpty(candidate) || !candidate.StartsWith("PDS_", StringComparison.Ordinal)) {
			return false;
		}
		code = candidate;
		return true;
	}

	private static bool TryParseJsonDocument(string content, out JsonDocument document, out string errorMessage) {
		try {
			document = JsonDocument.Parse(NormalizeJson(content));
			errorMessage = string.Empty;
			return true;
		} catch (Exception ex) {
			document = null!;
			errorMessage = ex.Message;
			return false;
		}
	}

	internal static string NormalizeJson(string content) {
		return Regex.Replace(content, @",(\s*[\]\}])", "$1", RegexOptions.None, RegexTimeout);
	}

	private static void CollectPathsFromElement(JsonElement element, HashSet<string> paths) {
		if (element.ValueKind == JsonValueKind.Object) {
			if (element.TryGetProperty("modelConfig", out var mc) &&
			    mc.TryGetProperty("path", out var pathEl) &&
			    pathEl.ValueKind == JsonValueKind.String) {
				paths.Add(pathEl.GetString());
			}
			foreach (var prop in element.EnumerateObject()) {
				CollectPathsFromElement(prop.Value, paths);
			}
		} else if (element.ValueKind == JsonValueKind.Array) {
			foreach (var item in element.EnumerateArray()) {
				CollectPathsFromElement(item, paths);
			}
		}
	}

	public static SchemaValidationResult ValidateJsSyntax(string jsBody) {
		var result = new SchemaValidationResult { IsValid = true };
		if (string.IsNullOrEmpty(jsBody)) {
			result.IsValid = false;
			result.Errors.Add("JS body is null or empty.");
			return result;
		}
		var stack = new Stack<(char bracket, int position)>();
		int i = 0;
		int length = jsBody.Length;
		while (i < length) {
			char c = jsBody[i];
			int next = TrySkipToken(jsBody, i, length, c, stack, result);
			if (!result.IsValid) {
				return result;
			}
			if (next != i) {
				i = next;
				continue;
			}
			i++;
		}
		if (stack.Count > 0) {
			var (bracket, pos) = stack.Pop();
			result.IsValid = false;
			result.Errors.Add($"Unclosed '{bracket}' at position {pos}.");
		}
		return result;
	}

	private static int TrySkipToken(string jsBody, int i, int length, char c,
		Stack<(char bracket, int position)> stack, SchemaValidationResult result) {
		if (c == '/' && i + 1 < length) {
			if (jsBody[i + 1] == '/') {
				return SkipLineComment(jsBody, i, length);
			}
			if (jsBody[i + 1] == '*') {
				return SkipBlockComment(jsBody, i, length, result);
			}
		}
		if (c == '\'' || c == '"') {
			return SkipStringLiteral(jsBody, i, length, c, result);
		}
		if (c == '`') {
			return SkipTemplateLiteral(jsBody, i, length, result);
		}
		if (c == '(' || c == '{' || c == '[') {
			stack.Push((c, i));
			return i + 1;
		}
		if (c == ')' || c == '}' || c == ']') {
			return ProcessClosingBracket(c, i, stack, result);
		}
		return i;
	}

	private static int SkipLineComment(string jsBody, int i, int length) {
		i += 2;
		while (i < length && jsBody[i] != '\n' && jsBody[i] != '\r') {
			i++;
		}
		return i;
	}

	private static int SkipBlockComment(string jsBody, int i, int length, SchemaValidationResult result) {
		int commentStart = i;
		i += 2;
		while (i + 1 < length) {
			if (jsBody[i] == '*' && jsBody[i + 1] == '/') {
				return i + 2;
			}
			i++;
		}
		result.IsValid = false;
		result.Errors.Add($"Unterminated block comment at position {commentStart}.");
		return i;
	}

	private static int SkipStringLiteral(string jsBody, int i, int length, char quote, SchemaValidationResult result) {
		int stringStart = i;
		i++;
		while (i < length) {
			if (jsBody[i] == '\\') {
				i += 2;
				continue;
			}
			if (jsBody[i] == quote) {
				return i + 1;
			}
			if (jsBody[i] == '\n' || jsBody[i] == '\r') {
				result.IsValid = false;
				result.Errors.Add($"Unterminated string literal at position {stringStart}.");
				return i;
			}
			i++;
		}
		return i;
	}

	private static int SkipTemplateLiteral(string jsBody, int i, int length, SchemaValidationResult result) {
		int templateStart = i;
		i++;
		while (i < length) {
			if (jsBody[i] == '\\') {
				i += 2;
				continue;
			}
			if (jsBody[i] == '`') {
				return i + 1;
			}
			i++;
		}
		if (i > length || (i == length && jsBody[length - 1] != '`')) {
			result.IsValid = false;
			result.Errors.Add($"Unterminated template literal at position {templateStart}.");
		}
		return i;
	}

	private static int ProcessClosingBracket(char c, int i,
		Stack<(char bracket, int position)> stack, SchemaValidationResult result) {
		if (stack.Count == 0) {
			result.IsValid = false;
			result.Errors.Add($"Unexpected closing '{c}' at position {i} with no matching opening bracket.");
			return i + 1;
		}
		var (openBracket, openPos) = stack.Pop();
		char expected = c switch {
			')' => '(',
			'}' => '{',
			']' => '[',
			_ => '\0'
		};
		if (openBracket != expected) {
			result.IsValid = false;
			result.Errors.Add($"Mismatched bracket: '{openBracket}' at position {openPos} closed by '{c}' at position {i}.");
		}
		return i + 1;
	}
}

public class SchemaValidationResult
{
	public bool IsValid { get; set; }
	public List<string> Errors { get; set; } = new List<string>();
	public List<string> Warnings { get; set; } = new List<string>();
}
