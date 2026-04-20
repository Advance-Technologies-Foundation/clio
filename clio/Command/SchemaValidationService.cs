namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using McpServer.Resources;

public static class SchemaValidationService
{
	private const string SchemaViewConfigDiff = "SCHEMA_VIEW_CONFIG_DIFF";
	private const string SchemaViewModelConfigDiff = "SCHEMA_VIEW_MODEL_CONFIG_DIFF";
	private const string SchemaViewModelConfig = "SCHEMA_VIEW_MODEL_CONFIG";
	private const string SchemaDiffMarker = "SCHEMA_DIFF";
	private const string SchemaValidatorsMarker = "SCHEMA_VALIDATORS";
	private const string ValuesPropertyName = "values";
	private const string AttributesPropertyName = "attributes";
	private const string ValidatorsPropertyName = "validators";
	private const string ParamsPropertyName = "params";
	private const string TypePropertyName = "type";

	public static readonly string[] RequiredMarkerNames = {
		"SCHEMA_DEPS",
		"SCHEMA_ARGS",
		SchemaViewConfigDiff,
		"SCHEMA_HANDLERS",
		"SCHEMA_CONVERTERS",
		SchemaValidatorsMarker
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
	private static readonly string[] MaxLengthTypeMarkers = ["MaxLength"];
	private static readonly string[] MinLengthTypeMarkers = ["MinLength"];
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
		SchemaDiffMarker,
		SchemaViewModelConfigDiff,
		"SCHEMA_MODEL_CONFIG_DIFF",
		"SCHEMA_DEPS"
	};

	private static readonly string[] JsonObjectMarkers = {
		SchemaViewModelConfig,
		"SCHEMA_MODEL_CONFIG"
	};

	private static readonly string[] JavaScriptObjectMarkers = {
		"SCHEMA_CONVERTERS",
		SchemaValidatorsMarker
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
		if (!ValidateMarkers(jsBody, JsonObjectMarkers, result)) {
			return result;
		}
		ValidateJavaScriptObjectMarkers(jsBody, JavaScriptObjectMarkers, result);
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

	private static void ValidateJavaScriptObjectMarkers(
		string jsBody,
		IEnumerable<string> markers,
		SchemaValidationResult result) {
		foreach (string marker in markers) {
			if (!PageSchemaSectionReader.TryRead(jsBody, out string content, marker)) {
				continue;
			}
			if (!TryValidateJavaScriptObjectSection(content, marker, result)) {
				return;
			}
		}
	}

	private static bool TryValidateJavaScriptObjectSection(
		string content,
		string marker,
		SchemaValidationResult result) {
		string trimmedContent = content.Trim();
		if (string.IsNullOrWhiteSpace(trimmedContent) ||
		    !trimmedContent.StartsWith("{", StringComparison.Ordinal) ||
		    !trimmedContent.EndsWith("}", StringComparison.Ordinal)) {
			result.IsValid = false;
			result.Errors.Add($"Invalid JavaScript object section in {marker}: section must remain an object literal.");
			return false;
		}

		SchemaValidationResult syntaxResult = ValidateJsSyntax($"const __clioSection = {trimmedContent};");
		if (syntaxResult.IsValid) {
			return true;
		}

		result.IsValid = false;
		result.Errors.Add($"Invalid JavaScript object section in {marker}: {string.Join("; ", syntaxResult.Errors)}");
		return false;
	}

	public static SchemaValidationResult ValidateColumnBindings(string jsBody) {
		var result = new SchemaValidationResult { IsValid = true };
		if (string.IsNullOrEmpty(jsBody)) {
			return result;
		}
		if (!PageSchemaSectionReader.TryRead(jsBody, out string vcdContent, SchemaViewConfigDiff, SchemaDiffMarker)) {
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
		if (!PageSchemaSectionReader.TryRead(jsBody, out string viewConfigContent, SchemaViewConfigDiff, SchemaDiffMarker)) {
			return result;
		}
		if (!TryParseJsonDocument(viewConfigContent, out JsonDocument viewConfigDocument, out _)) {
			return result;
		}
		Dictionary<string, string> modelPaths = CollectViewModelPaths(jsBody);
		var attributesWithValidators = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		CollectAttributesWithValidatorsFromMarker(jsBody, SchemaViewModelConfig, false, attributesWithValidators);
		CollectAttributesWithValidatorsFromMarker(jsBody, SchemaViewModelConfigDiff, true, attributesWithValidators);
		using (viewConfigDocument) {
			ValidateFieldComponents(viewConfigDocument.RootElement, modelPaths, explicitResources, attributesWithValidators, result);
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

	private static Dictionary<string, string> CollectViewModelPaths(string jsBody) {
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
		IReadOnlySet<string> attributesWithValidators,
		SchemaValidationResult result,
		bool checkSelf = true) {
		if (element.ValueKind == JsonValueKind.Object) {
			bool wrappedValues = false;
			if (checkSelf && TryResolveFieldComponent(element, out JsonElement componentValues, out string fieldName, out string componentType, out wrappedValues)) {
				ValidateFieldComponent(componentValues, fieldName, componentType, modelPaths, explicitResources, attributesWithValidators, result);
			}
			foreach (JsonProperty property in element.EnumerateObject()) {
				bool childCheckSelf = !(wrappedValues && property.NameEquals(ValuesPropertyName));
				ValidateFieldComponents(property.Value, modelPaths, explicitResources, attributesWithValidators, result, childCheckSelf);
			}
		} else if (element.ValueKind == JsonValueKind.Array) {
			foreach (JsonElement item in element.EnumerateArray()) {
				ValidateFieldComponents(item, modelPaths, explicitResources, attributesWithValidators, result);
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
		if (element.TryGetProperty(ValuesPropertyName, out JsonElement valuesElement) &&
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
		IReadOnlySet<string> attributesWithValidators,
		SchemaValidationResult result) {
		string fieldDisplayName = !string.IsNullOrWhiteSpace(fieldName) ? fieldName : componentType;
		if (TryGetBindingAttribute(componentValues, out string bindingProperty, out string bindingExpression, out string bindingAttribute) &&
		    !IsAllowedDirectFieldBinding(bindingAttribute) &&
		    !attributesWithValidators.Contains(bindingAttribute) &&
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

	/// <summary>
	/// Validates that UI controls bound to <c>$PDS_AttrName</c> do not belong to view-model
	/// attributes that carry <c>validators</c>. Validators only fire on view-model attribute
	/// bindings (<c>$AttrName</c>), never on raw PDS data-source bindings (<c>$PDS_AttrName</c>).
	/// </summary>
	public static SchemaValidationResult ValidateValidatorControlBindings(string jsBody) {
		var result = new SchemaValidationResult { IsValid = true };
		if (string.IsNullOrEmpty(jsBody)) {
			return result;
		}
		var attributesWithValidators = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		CollectAttributesWithValidatorsFromMarker(jsBody, SchemaViewModelConfig, false, attributesWithValidators);
		CollectAttributesWithValidatorsFromMarker(jsBody, SchemaViewModelConfigDiff, true, attributesWithValidators);
		if (attributesWithValidators.Count == 0) {
			return result;
		}
		if (!PageSchemaSectionReader.TryRead(jsBody, out string vcdContent, SchemaViewConfigDiff, SchemaDiffMarker)) {
			return result;
		}
		if (!TryParseJsonDocument(vcdContent, out JsonDocument viewConfigDocument, out _)) {
			return result;
		}
		using (viewConfigDocument) {
			CheckValidatorControlBindings(viewConfigDocument.RootElement, attributesWithValidators, result);
		}
		if (result.Errors.Count > 0) {
			result.IsValid = false;
		}
		return result;
	}

	/// <summary>
	/// Validates that validator <c>params</c> values do not use the reactive binding syntax
	/// <c>$Resources.Strings.KeyName</c>. Validator params are evaluated as plain JavaScript values
	/// and are not processed by the reactive binding engine — the correct format is
	/// <c>#ResourceString(KeyName)#</c> which is substituted server-side when the schema is compiled.
	/// </summary>
	public static SchemaValidationResult ValidateValidatorParamResourceBindings(string jsBody) {
		var result = new SchemaValidationResult { IsValid = true };
		if (string.IsNullOrEmpty(jsBody)) {
			return result;
		}
		CheckValidatorParamResourceBindingsInMarker(jsBody, SchemaViewModelConfig, false, result);
		CheckValidatorParamResourceBindingsInMarker(jsBody, SchemaViewModelConfigDiff, true, result);
		if (result.Errors.Count > 0) {
			result.IsValid = false;
		}
		return result;
	}

	/// <summary>
	/// Validates that obvious custom validator implementations are not used when a standard built-in
	/// validator already matches the rule. This targets high-confidence cases only, such as custom
	/// string-length validators that duplicate <c>crt.MaxLength</c> or <c>crt.MinLength</c>.
	/// </summary>
	public static SchemaValidationResult ValidateStandardValidatorUsage(string jsBody) {
		var result = new SchemaValidationResult { IsValid = true };
		if (string.IsNullOrEmpty(jsBody)) {
			return result;
		}
		IReadOnlyDictionary<string, HashSet<string>> validatorContracts = BuildValidatorParameterContracts(jsBody);
		ValidateValidatorBindingContractsInMarker(jsBody, SchemaViewModelConfig, false, validatorContracts, result);
		ValidateValidatorBindingContractsInMarker(jsBody, SchemaViewModelConfigDiff, true, validatorContracts, result);
		if (!PageSchemaSectionReader.TryRead(jsBody, out string validatorsContent, SchemaValidatorsMarker)) {
			if (result.Errors.Count > 0) {
				result.IsValid = false;
			}
			return result;
		}
		var customValidatorTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		CollectValidatorTypesFromMarker(jsBody, SchemaViewModelConfig, false, customValidatorTypes);
		CollectValidatorTypesFromMarker(jsBody, SchemaViewModelConfigDiff, true, customValidatorTypes);
		foreach (string customValidatorType in customValidatorTypes) {
			if (!customValidatorType.StartsWith("usr.", StringComparison.OrdinalIgnoreCase)) {
				continue;
			}
			string equivalentBuiltIn = TryGetEquivalentBuiltInValidator(customValidatorType, validatorsContent);
			if (string.IsNullOrEmpty(equivalentBuiltIn)) {
				continue;
			}
			result.Errors.Add(
				$"Custom validator '{customValidatorType}' duplicates built-in validator '{equivalentBuiltIn}'. " +
				$"Use '{equivalentBuiltIn}' in the attribute validators binding instead of defining '{customValidatorType}' in SCHEMA_VALIDATORS. " +
				"Read docs://mcp/guides/page-schema-validators before authoring validator changes.");
		}
		if (result.Errors.Count > 0) {
			result.IsValid = false;
		}
		return result;
	}

	/// <summary>
	/// Validates that each custom validator in <c>SCHEMA_VALIDATORS</c> declares all properties
	/// that appear in its returned error object as named entries in its <c>params</c> array.
	/// Catches the common mistake of returning <c>{message: "..."}</c> while leaving
	/// <c>params: []</c>, which causes a Creatio runtime error.
	/// </summary>
	public static SchemaValidationResult ValidateCustomValidatorParamCompleteness(string jsBody) {
		var result = new SchemaValidationResult { IsValid = true };
		if (string.IsNullOrEmpty(jsBody)) {
			return result;
		}
		if (!PageSchemaSectionReader.TryRead(jsBody, out string validatorsContent, SchemaValidatorsMarker)) {
			return result;
		}
		foreach ((string validatorType, HashSet<string> declaredParams) in ExtractCustomValidatorContracts(validatorsContent)) {
			string snippet = ExtractValidatorBody(validatorsContent, validatorType);
			if (string.IsNullOrEmpty(snippet)) {
				continue;
			}
			if (HasPrimitiveErrorReturn(snippet, validatorType)) {
				result.Errors.Add(
					$"Validator '{validatorType}' returns a primitive error value instead of an error object. " +
					$"Replace with {{ \"{validatorType}\": {{ message: config.message }} }}, " +
					"add {\"name\": \"message\"} to params, and use \"#ResourceString(KeyName)#\" for the message binding in viewModelConfig. " +
					"Read docs://mcp/guides/page-schema-validators for the canonical validator shape.");
				continue;
			}
			if (!declaredParams.Contains("message")) {
				result.Errors.Add(
					$"Validator '{validatorType}' does not declare a 'message' param. " +
					"Every custom validator must include {{\"name\": \"message\"}} in its params array so the error message is visible to the user. " +
					"Pass the value via config.message and use \"#ResourceString(KeyName)#\" for the message binding in viewModelConfig. " +
					"Read docs://mcp/guides/page-schema-validators for the canonical validator shape.");
				continue;
			}
			HashSet<string> returnedProps = ExtractReturnErrorProperties(snippet, validatorType);
			var undeclared = returnedProps
				.Where(p => !declaredParams.Contains(p))
				.OrderBy(p => p)
				.ToList();
			if (undeclared.Count > 0) {
				result.Errors.Add(
					$"Validator '{validatorType}' returns error properties [{string.Join(", ", undeclared)}] " +
					$"that are not declared in its params array. " +
					$"Add {{name: \"{undeclared[0]}\"}} (and any others) to params and pass the value via config.{undeclared[0]}. " +
					"Read docs://mcp/guides/page-schema-validators for the canonical validator shape.");
			}
		}
		if (result.Errors.Count > 0) {
			result.IsValid = false;
		}
		return result;
	}

	/// <summary>
	/// Returns <see langword="true"/> when the validator body contains a primitive return value
	/// for the error object, e.g. <c>return { "usr.Type": true }</c>.
	/// The runtime requires an object, not a boolean/number/string.
	/// </summary>
	private static bool HasPrimitiveErrorReturn(string snippet, string validatorType) {
		string typeEscaped = Regex.Escape(validatorType);
		return Regex.IsMatch(
			snippet,
			"\\{\\s*\"" + typeEscaped + "\"\\s*:\\s*(?:true|false|\\d[\\d.]*|\"[^\"]*\")\\s*\\}",
			RegexOptions.Singleline,
			RegexTimeout);
	}

	/// <summary>
	/// Extracts property names from the error object returned inside a custom validator body.
	/// Matches patterns like <c>return { "usr.Type": { propA: ..., propB: ... } }</c>.
	/// </summary>
	private static HashSet<string> ExtractReturnErrorProperties(string snippet, string validatorType) {
		var props = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		// Match the inner error object: { "usr.Type": { prop: value, ... } }
		string typeEscaped = Regex.Escape(validatorType);
		Match inner = Regex.Match(
			snippet,
			"\"" + typeEscaped + "\"\\s*:\\s*\\{(?<inner>[^{}]+)\\}",
			RegexOptions.Singleline,
			RegexTimeout);
		if (!inner.Success) {
			return props;
		}
		MatchCollection propMatches = Regex.Matches(
			inner.Groups["inner"].Value,
			"(?<name>[a-zA-Z_$][a-zA-Z0-9_$]*)\\s*:",
			RegexOptions.Singleline,
			RegexTimeout);
		foreach (Match m in propMatches) {
			props.Add(m.Groups["name"].Value);
		}
		return props;
	}

	private static IReadOnlyDictionary<string, HashSet<string>> BuildValidatorParameterContracts(string jsBody) {
		var contracts = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
		foreach ((string validatorType, string[] paramNames) in StandardValidatorContractParser.GetContracts()) {
			contracts[validatorType] = new HashSet<string>(paramNames, StringComparer.OrdinalIgnoreCase);
		}
		if (PageSchemaSectionReader.TryRead(jsBody, out string validatorsContent, SchemaValidatorsMarker)) {
			foreach ((string validatorType, HashSet<string> paramNames) in ExtractCustomValidatorContracts(validatorsContent)) {
				contracts[validatorType] = paramNames;
			}
		}
		return contracts;
	}

	private static void ForEachMarkerAttributesContainer(
		string jsBody,
		string markerName,
		bool isArray,
		Action<JsonElement> action) {
		if (!TryReadMarkerRootElement(jsBody, markerName, out JsonDocument? document)) {
			return;
		}

		using (document) {
			foreach (JsonElement attributes in EnumerateAttributesContainers(document.RootElement, isArray)) {
				action(attributes);
			}
		}
	}

	private static bool TryReadMarkerRootElement(
		string jsBody,
		string markerName,
		out JsonDocument? document) {
		document = null;
		if (!PageSchemaSectionReader.TryRead(jsBody, out string content, markerName) ||
		    !TryParseJsonDocument(content, out JsonDocument parsedDocument, out _)) {
			return false;
		}

		document = parsedDocument;
		return true;
	}

	private static IEnumerable<JsonElement> EnumerateAttributesContainers(JsonElement root, bool isArray) {
		if (!isArray) {
			if (root.TryGetProperty(AttributesPropertyName, out JsonElement attributes)) {
				yield return attributes;
			}
			yield break;
		}

		if (root.ValueKind != JsonValueKind.Array) {
			yield break;
		}

		foreach (JsonElement op in root.EnumerateArray()) {
			if (op.TryGetProperty(ValuesPropertyName, out JsonElement values)) {
				yield return values;
			}
		}
	}

	private static void ValidateValidatorBindingContractsInMarker(
		string jsBody,
		string markerName,
		bool isArray,
		IReadOnlyDictionary<string, HashSet<string>> validatorContracts,
		SchemaValidationResult result) {
		ForEachMarkerAttributesContainer(
			jsBody,
			markerName,
			isArray,
			attributes => ScanAttributesForValidatorBindingContractViolations(attributes, validatorContracts, result));
	}

	private static void CheckValidatorParamResourceBindingsInMarker(
		string jsBody, string markerName, bool isArray, SchemaValidationResult result) {
		ForEachMarkerAttributesContainer(
			jsBody,
			markerName,
			isArray,
			attributes => ScanAttributesForInvalidParamBindings(attributes, result));
	}

	private static void ScanAttributesForInvalidParamBindings(
		JsonElement attributesElement, SchemaValidationResult result) {
		foreach ((string attributeName, string validatorName, JsonProperty param, string key, string value) in EnumerateInvalidParamBindings(attributesElement)) {
			result.Errors.Add(
				$"Validator '{validatorName}' param '{param.Name}' on attribute '{attributeName}' " +
				$"uses reactive binding '{value}'. Validator params are not processed by the reactive engine — " +
				$"use '#ResourceString({key})#' instead.");
		}
	}

	private static IEnumerable<(string AttributeName, string ValidatorName, JsonProperty Param, string Key, string Value)> EnumerateInvalidParamBindings(
		JsonElement attributesElement) {
		foreach (JsonProperty attr in EnumerateAttributesWithValidatorObjects(attributesElement)) {
			foreach (JsonProperty validator in EnumerateValidatorObjects(attr)) {
				if (!TryGetValidatorParams(validator, out JsonElement paramsElement)) {
					continue;
				}

				foreach (JsonProperty param in paramsElement.EnumerateObject()) {
					if (TryGetReactiveResourceBinding(param, out string key, out string value)) {
						yield return (attr.Name, validator.Name, param, key, value);
					}
				}
			}
		}
	}

	private static bool TryGetValidatorBindings(JsonProperty attr, out JsonElement validators) =>
		attr.Value.TryGetProperty(ValidatorsPropertyName, out validators) &&
		validators.ValueKind == JsonValueKind.Object;

	private static bool TryGetValidatorParams(JsonProperty validator, out JsonElement paramsElement) =>
		validator.Value.TryGetProperty(ParamsPropertyName, out paramsElement) &&
		paramsElement.ValueKind == JsonValueKind.Object;

	private static bool TryGetReactiveResourceBinding(
		JsonProperty param,
		out string key,
		out string value) {
		key = string.Empty;
		value = string.Empty;
		if (param.Value.ValueKind != JsonValueKind.String) {
			return false;
		}

		string? rawValue = param.Value.GetString();
		if (string.IsNullOrEmpty(rawValue) ||
		    !rawValue.StartsWith("$Resources.Strings.", StringComparison.OrdinalIgnoreCase)) {
			return false;
		}

		value = rawValue;
		key = rawValue["$Resources.Strings.".Length..];
		return true;
	}

	// "message" is universally optional on all crt.* validators via ValidatorParametersValues.message — never treat it as unknown for standard validators.
	private static readonly HashSet<string> StandardValidatorUniversalParams =
		new(StringComparer.OrdinalIgnoreCase) { "message" };

	private static IEnumerable<JsonProperty> EnumerateAttributesWithValidatorObjects(JsonElement attributesElement) {
		if (attributesElement.ValueKind != JsonValueKind.Object) {
			yield break;
		}

		foreach (JsonProperty attr in attributesElement.EnumerateObject()) {
			if (TryGetValidatorBindings(attr, out _)) {
				yield return attr;
			}
		}
	}

	private static IEnumerable<JsonProperty> EnumerateValidatorObjects(JsonProperty attr) =>
		TryGetValidatorBindings(attr, out JsonElement validators)
			? validators.EnumerateObject()
			: Enumerable.Empty<JsonProperty>();

	private static bool TryGetValidatorType(JsonProperty validator, out string validatorType) {
		validatorType = string.Empty;
		if (validator.Value.ValueKind != JsonValueKind.Object ||
		    !validator.Value.TryGetProperty(TypePropertyName, out JsonElement typeElement) ||
		    typeElement.ValueKind != JsonValueKind.String) {
			return false;
		}

		validatorType = typeElement.GetString() ?? string.Empty;
		return !string.IsNullOrWhiteSpace(validatorType);
	}

	private static void ScanAttributesForValidatorBindingContractViolations(
		JsonElement attributesElement,
		IReadOnlyDictionary<string, HashSet<string>> validatorContracts,
		SchemaValidationResult result) {
		foreach ((string attributeName, string validatorType, JsonElement paramsElement, HashSet<string> allowedParams) in EnumerateValidatorBindingContracts(attributesElement, validatorContracts)) {
			if (!ValidateParamsPresence(attributeName, validatorType, paramsElement, allowedParams, result)) {
				continue;
			}

			List<string> unknownParams = CollectUnknownValidatorParams(validatorType, paramsElement, allowedParams);
			if (unknownParams.Count > 0) {
				result.Errors.Add(
					$"Validator '{validatorType}' on attribute '{attributeName}' uses unsupported params [{string.Join(", ", unknownParams)}]. " +
					$"Allowed params: [{string.Join(", ", allowedParams)}].");
				continue;
			}

			if (allowedParams.Count > 0 && !paramsElement.EnumerateObject().Any()) {
				result.Errors.Add(
					$"Validator '{validatorType}' on attribute '{attributeName}' must use params [{string.Join(", ", allowedParams)}].");
			}
		}
	}

	private static IEnumerable<(string AttributeName, string ValidatorType, JsonElement ParamsElement, HashSet<string> AllowedParams)> EnumerateValidatorBindingContracts(
		JsonElement attributesElement,
		IReadOnlyDictionary<string, HashSet<string>> validatorContracts) {
		foreach (JsonProperty attr in EnumerateAttributesWithValidatorObjects(attributesElement)) {
			foreach (JsonProperty validator in EnumerateValidatorObjects(attr)) {
				if (!TryGetValidatorType(validator, out string validatorType) ||
				    !validatorContracts.TryGetValue(validatorType, out HashSet<string>? allowedParams)) {
					continue;
				}

				bool hasParamsObject = TryGetValidatorParams(validator, out JsonElement paramsElement);
				yield return (attr.Name, validatorType, hasParamsObject ? paramsElement : default, allowedParams);
			}
		}
	}

	private static bool ValidateParamsPresence(
		string attributeName,
		string validatorType,
		JsonElement paramsElement,
		HashSet<string> allowedParams,
		SchemaValidationResult result) {
		if (paramsElement.ValueKind == JsonValueKind.Object) {
			return true;
		}

		if (allowedParams.Count == 0) {
			return false;
		}

		result.Errors.Add(
			$"Validator '{validatorType}' on attribute '{attributeName}' must declare params object " +
			$"with [{string.Join(", ", allowedParams)}].");
		return false;
	}

	private static List<string> CollectUnknownValidatorParams(
		string validatorType,
		JsonElement paramsElement,
		HashSet<string> allowedParams) {
		bool isStandardValidator = validatorType.StartsWith("crt.", StringComparison.OrdinalIgnoreCase);
		return paramsElement
			.EnumerateObject()
			.Where(param => !allowedParams.Contains(param.Name) &&
			                !(isStandardValidator && StandardValidatorUniversalParams.Contains(param.Name)))
			.Select(param => param.Name)
			.ToList();
	}

	private static void CollectValidatorTypesFromMarker(
		string jsBody,
		string markerName,
		bool isArray,
		HashSet<string> validatorTypes) {
		ForEachMarkerAttributesContainer(jsBody, markerName, isArray, attributes => ExtractValidatorTypes(attributes, validatorTypes));
	}

	private static void ExtractValidatorTypes(JsonElement attributesObject, HashSet<string> validatorTypes) {
		foreach (string validatorType in EnumerateAttributesWithValidatorObjects(attributesObject)
			         .SelectMany(EnumerateValidatorObjects)
			         .Select(GetValidatorTypeOrEmpty)
			         .Where(type => !string.IsNullOrWhiteSpace(type))) {
			validatorTypes.Add(validatorType);
		}
	}

	private static string GetValidatorTypeOrEmpty(JsonProperty validator) =>
		TryGetValidatorType(validator, out string validatorType)
			? validatorType
			: string.Empty;

	private static string TryGetEquivalentBuiltInValidator(string customValidatorType, string validatorsContent) {
		string validatorBody = ExtractValidatorBody(validatorsContent, customValidatorType);
		if (string.IsNullOrEmpty(validatorBody)) {
			return string.Empty;
		}
		if (ContainsAny(customValidatorType, MaxLengthTypeMarkers) &&
			validatorBody.Contains(".length", StringComparison.Ordinal)) {
			return "crt.MaxLength";
		}
		if (ContainsAny(customValidatorType, MinLengthTypeMarkers) &&
			validatorBody.Contains(".length", StringComparison.Ordinal)) {
			return "crt.MinLength";
		}
		return string.Empty;
	}

	/// <summary>
	/// Extracts the full body of a named validator entry from the SCHEMA_VALIDATORS section
	/// using brace-depth tracking so that long validators are never truncated.
	/// Returns the substring from the opening quote of <paramref name="customValidatorType"/>
	/// through the closing <c>}</c> of its top-level object, or an empty string when not found.
	/// </summary>
	private static string ExtractValidatorBody(string validatorsContent, string customValidatorType) {
		string marker = "\"" + customValidatorType + "\"";
		int markerIndex = validatorsContent.IndexOf(marker, StringComparison.Ordinal);
		if (markerIndex < 0) {
			return string.Empty;
		}

		if (!TryFindOpeningBrace(validatorsContent, markerIndex + marker.Length, out int braceStart)) {
			return string.Empty;
		}

		return TryExtractValidatorObject(validatorsContent, markerIndex, braceStart, out string validatorBody)
			? validatorBody
			: string.Empty;
	}

	private static bool TryFindOpeningBrace(string content, int startIndex, out int braceStart) {
		braceStart = content.IndexOf('{', startIndex);
		return braceStart >= 0;
	}

	private static bool TryExtractValidatorObject(
		string validatorsContent,
		int markerIndex,
		int braceStart,
		out string validatorBody) {
		int depth = 0;
		bool inString = false;
		char stringChar = '"';
		int index = braceStart;
		while (index < validatorsContent.Length) {
			char current = validatorsContent[index];
			if (inString) {
				index = ConsumeStringLiteralCharacter(validatorsContent, index, ref inString, stringChar);
				continue;
			}

			if (TryCloseValidatorBody(validatorsContent, markerIndex, ref depth, current, index, out validatorBody)) {
				return true;
			}

			if (current is '"' or '\'' or '`') {
				inString = true;
				stringChar = current;
			} else if (current == '{') {
				depth++;
			}

			index++;
		}

		validatorBody = string.Empty;
		return false;
	}

	private static bool TryCloseValidatorBody(
		string validatorsContent,
		int markerIndex,
		ref int depth,
		char current,
		int index,
		out string validatorBody) {
		validatorBody = string.Empty;
		if (current != '}') {
			return false;
		}

		depth--;
		if (depth != 0) {
			return false;
		}

		validatorBody = validatorsContent.Substring(markerIndex, index - markerIndex + 1);
		return true;
	}

	private static int ConsumeStringLiteralCharacter(
		string validatorsContent,
		int index,
		ref bool inString,
		char stringChar) {
		if (validatorsContent[index] == '\\') {
			return index + 1 < validatorsContent.Length ? index + 2 : index + 1;
		}

		if (validatorsContent[index] == stringChar) {
			inString = false;
		}

		return index + 1;
	}

	private static bool ContainsAny(string source, IEnumerable<string> values) {
		return values.Any(value => source.Contains(value, StringComparison.OrdinalIgnoreCase));
	}

	private static IEnumerable<(string ValidatorType, HashSet<string> ParamNames)> ExtractCustomValidatorContracts(
		string validatorsContent) {
		const string validatorPattern = "\"(?<type>usr\\.[^\"]+)\"\\s*:\\s*\\{";
		MatchCollection matches = Regex.Matches(validatorsContent, validatorPattern, RegexOptions.Singleline, RegexTimeout);
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (Match match in matches) {
			string validatorType = match.Groups["type"].Value;
			if (!seen.Add(validatorType)) {
				continue;
			}
			string snippet = ExtractValidatorBody(validatorsContent, validatorType);
			if (string.IsNullOrEmpty(snippet)) {
				continue;
			}
			yield return (validatorType, ExtractDeclaredParamNames(snippet));
		}
	}

	private static HashSet<string> ExtractDeclaredParamNames(string snippet) {
		Match paramsMatch = Regex.Match(
			snippet,
			"\"params\"\\s*:\\s*\\[(?<params>.*?)\\]",
			RegexOptions.Singleline,
			RegexTimeout);
		if (!paramsMatch.Success) {
			return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		}

		return Regex.Matches(
				paramsMatch.Groups["params"].Value,
				"\"name\"\\s*:\\s*\"(?<name>[^\"]+)\"",
				RegexOptions.Singleline,
				RegexTimeout)
			.Select(match => match.Groups["name"].Value)
			.Where(paramName => !string.IsNullOrWhiteSpace(paramName))
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
	}


	private static void CollectAttributesWithValidatorsFromMarker(
		string jsBody,
		string markerName,
		bool isArray,
		HashSet<string> attributeNames) {
		ForEachMarkerAttributesContainer(jsBody, markerName, isArray, attributes => ExtractAttributesWithValidators(attributes, attributeNames));
	}

	private static void ExtractAttributesWithValidators(JsonElement attributesObject, HashSet<string> names) {
		foreach (JsonProperty attr in EnumerateAttributesWithValidatorObjects(attributesObject)
			         .Where(attr => EnumerateValidatorObjects(attr).Any())) {
			names.Add(attr.Name);
		}
	}

	private static void CheckValidatorControlBindings(
		JsonElement element,
		HashSet<string> attributesWithValidators,
		SchemaValidationResult result) {
		if (element.ValueKind == JsonValueKind.Array) {
			foreach (JsonElement item in element.EnumerateArray()) {
				CheckValidatorControlBindings(item, attributesWithValidators, result);
			}
			return;
		}
		if (element.ValueKind != JsonValueKind.Object) {
			return;
		}
		JsonElement target = element.TryGetProperty(ValuesPropertyName, out JsonElement valuesElement) &&
		                     valuesElement.ValueKind == JsonValueKind.Object
			? valuesElement
			: element;
		AddValidatorControlBindingErrorIfNeeded(element, target, attributesWithValidators, result);
		foreach (JsonProperty property in element.EnumerateObject()
		             .Where(property => !property.NameEquals(ValuesPropertyName))) {
			CheckValidatorControlBindings(property.Value, attributesWithValidators, result);
		}
	}

	private static void AddValidatorControlBindingErrorIfNeeded(
		JsonElement element,
		JsonElement target,
		HashSet<string> attributesWithValidators,
		SchemaValidationResult result) {
		if (!TryGetStringProperty(target, "control", out string controlBinding) ||
		    !controlBinding.StartsWith("$PDS_", StringComparison.OrdinalIgnoreCase)) {
			return;
		}

		string attributeName = controlBinding["$PDS_".Length..];
		if (!attributesWithValidators.Contains(attributeName)) {
			return;
		}

		string fieldName = element.TryGetProperty("name", out JsonElement nameEl) &&
		                   nameEl.ValueKind == JsonValueKind.String
			? nameEl.GetString() ?? attributeName
			: attributeName;
		result.Errors.Add(
			$"Control '{fieldName}' binds to '$PDS_{attributeName}' but attribute " +
			$"'{attributeName}' has validators. Validators only fire on view-model attribute " +
			"bindings \u2014 use '$" + attributeName +
			"' instead of '$PDS_" + attributeName + "'.");
	}

	private static bool TryGetDataTableColumns(JsonElement item, out JsonElement columns) {
		columns = default;
		return item.TryGetProperty("name", out JsonElement nameElement)
			&& string.Equals(nameElement.GetString(), "DataTable", StringComparison.Ordinal)
			&& item.TryGetProperty(ValuesPropertyName, out JsonElement values)
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

	private static string NormalizeJson(string content) {
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
