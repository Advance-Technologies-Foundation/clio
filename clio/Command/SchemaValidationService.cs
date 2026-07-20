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
	private const string SchemaConvertersMarker = "SCHEMA_CONVERTERS";
	internal const string SchemaHandlersMarker = "SCHEMA_HANDLERS";
	private const string ValuesPropertyName = "values";
	private const string OperationPropertyName = "operation";
	private const string AttributesPropertyName = "attributes";
	private const string ValidatorsPropertyName = "validators";
	private const string ParamsPropertyName = "params";
	private const string TypePropertyName = "type";
	private const string LabelPropertyName = "label";
	private const string ViewConfigDiffPropertyName = "viewConfigDiff";
	private const string ViewModelConfigDiffPropertyName = "viewModelConfigDiff";
	private const string ModelConfigDiffPropertyName = "modelConfigDiff";
	private const string ViewModelConfigPropertyName = "viewModelConfig";
	private const string ModelConfigPropertyName = "modelConfig";

	private static readonly string[] DiffPropertyNames = {
		ViewConfigDiffPropertyName, ViewModelConfigDiffPropertyName, ModelConfigDiffPropertyName
	};

	private static readonly string[] ConfigPropertyNames = {
		ViewModelConfigPropertyName, ModelConfigPropertyName
	};

	private static readonly HashSet<string> AllowedMobileRootProperties = new(StringComparer.Ordinal) {
		ViewConfigDiffPropertyName, ViewModelConfigDiffPropertyName, ModelConfigDiffPropertyName,
		ViewModelConfigPropertyName, ModelConfigPropertyName
	};

	private static readonly HashSet<string> DisallowedMobileRootProperties = new(StringComparer.Ordinal) {
		"validators", "converters", "handlers"
	};


	public static readonly string[] RequiredMarkerNames = {
		"SCHEMA_DEPS",
		"SCHEMA_ARGS",
		SchemaViewConfigDiff,
		SchemaHandlersMarker,
		SchemaConvertersMarker,
		SchemaValidatorsMarker
	};

	public static readonly string[][] AlternateMarkerPairs = {
		new[] { SchemaViewModelConfigDiff, SchemaViewModelConfig },
		new[] { "SCHEMA_MODEL_CONFIG_DIFF", "SCHEMA_MODEL_CONFIG" }
	};

	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

	private static readonly Regex SdkUsagePattern = new(@"\bsdk\s*[.\[]", RegexOptions.Compiled, RegexTimeout);

	/// <summary>
	/// Matches a reactive view-model context attribute read of the form
	/// <c>$context["Attr"]</c> or <c>request.$context["Attr"]</c>, capturing an optional
	/// immediately-preceding <c>await</c> keyword so the validator can tell awaited reads
	/// from un-awaited ones. The <c>name</c> group holds the attribute name.
	/// </summary>
	private static readonly Regex ContextBracketReadPattern = new(
		@"(?<await>\bawait\s+)?(?:request\s*\.\s*)?\$context\s*\[\s*(?<quote>[""'])(?<name>[^""']+)\k<quote>\s*\]",
		RegexOptions.Compiled | RegexOptions.CultureInvariant,
		RegexTimeout);

	private static readonly Regex ResourceStringPattern = new(
		@"^#ResourceString\(([^)]+)\)#$",
		RegexOptions.Compiled,
		RegexTimeout);

	/// <summary>
	/// Matches a <c>#ResourceString(Key)#</c> localization macro anywhere within a value (unanchored).
	/// Unlike <see cref="ResourceStringPattern"/> (which requires the whole value to be exactly the
	/// macro), this recognises a resource reference that is concatenated with other text or wrapped in
	/// another macro — most notably the platform's <c>#MacrosTemplateString(…#ResourceString(Key)#…)#</c>
	/// form, the dominant OOTB caption shape. Used by <see cref="IsInlineUserVisibleTextLiteral"/> so a
	/// localized-but-wrapped value is not misclassified as a hardcoded inline literal.
	/// </summary>
	private static readonly Regex ResourceStringReferencePattern = new(
		@"#ResourceString\(([^)]+)\)#",
		RegexOptions.Compiled,
		RegexTimeout);
	private const string ResourceBindingPrefix = "$Resources.Strings.";
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

	/// <summary>
	/// Canonical clause stating the binding half of the inserted-field contract. Authored ONCE here
	/// and reused by <see cref="InsertedFieldContractSummary"/> and the per-field diagnostic in
	/// <c>AppendBindingDeclarationError</c>, so the rule an agent is told is identical to the rule
	/// <see cref="ValidateInsertedFieldSelfConsistency"/> rejects on.
	/// </summary>
	internal const string InsertedFieldBindingClause =
		"the body must declare the control's binding attribute in viewModelConfigDiff with a " +
		"DS-bound modelConfig.path, or the control has no data source";

	/// <summary>
	/// Canonical clause stating the label half of the inserted-field contract. Authored ONCE here
	/// and reused by <see cref="InsertedFieldContractSummary"/>. The resource nuance (prefixes, and
	/// why the bare column code is not the key unless it equals the attribute name) lives in the
	/// <c>page-schema-resources</c> guide, not here.
	/// </summary>
	internal const string InsertedFieldLabelClause =
		"the label must resolve — prefer the auto-provided form: set it to " +
		"$Resources.Strings.<bindingAttribute> (the control's own DS-bound attribute) and the platform " +
		"supplies the caption with no registration; pass the key via the 'resources' parameter only to " +
		"override the caption or for a non-DS-bound key. See the page-schema-resources guide for the full rule";

	/// <summary>
	/// Canonical statement of the inserted-field contract enforced by
	/// <see cref="ValidateInsertedFieldSelfConsistency"/>, composed from the shared
	/// <see cref="InsertedFieldBindingClause"/> and <see cref="InsertedFieldLabelClause"/> so it
	/// cannot drift from the diagnostics that reuse the same clauses. Surfaced to MCP agents from
	/// <c>get-component-info</c>, <c>update-page</c> tool [Description], and the
	/// <c>page-modification</c> guidance resource so all describe the same rule in identical words.
	/// Keep this <c>const</c> so it stays usable inside <c>[Description]</c> attributes (which only
	/// accept compile-time constant expressions).
	/// </summary>
	internal const string InsertedFieldContractSummary =
		"Standard field components (crt.Input, crt.NumberInput, crt.Checkbox, crt.ComboBox, " +
		"crt.PhoneInput, crt.EmailInput, crt.DateTimePicker, crt.WebInput, crt.RichTextEditor, " +
		"crt.ColorPicker, crt.ImageInput, crt.FileInput, crt.EncryptedInput, crt.Slider) inserted " +
		"via operation:\"insert\" in viewConfigDiff are validated for self-consistency in the SAME " +
		"update-page call: (a) " + InsertedFieldBindingClause + "; and (b) " + InsertedFieldLabelClause +
		". Violations are rejected at update-page validation time; the diagnostic names the offending " +
		"field, attribute, and section. This contract does NOT apply to operation:\"merge\" — a parent " +
		"schema or the current body may legitimately provide the attribute and resource.";

	/// <summary>
	/// User-visible text properties on Freedom UI view-config nodes whose values must be authored as
	/// localizable-string bindings (<c>$Resources.Strings.&lt;Key&gt;</c> or the
	/// <c>#ResourceString(&lt;Key&gt;)#</c> macro), never as inline string literals. Enforced by
	/// <see cref="ValidateLocalizableTextLiterals"/> (web) and
	/// <see cref="ValidateMobileLocalizableTextLiterals"/> (mobile). The set is deliberately limited to
	/// properties that are unambiguously rendered to the user; overloaded keys such as <c>description</c>
	/// (which also names non-display metadata on entity columns, components, and APIs) are intentionally
	/// excluded from the hard reject and covered by the <c>page-schema-resources</c> guidance only.
	/// </summary>
	internal static readonly HashSet<string> LocalizableTextProperties = new(StringComparer.OrdinalIgnoreCase) {
		LabelPropertyName,
		"caption",
		"title",
		"tooltip",
		"placeholder"
	};

	/// <summary>
	/// Canonical clause describing the localizable-text rule enforced by
	/// <see cref="ValidateLocalizableTextLiterals"/>. Authored ONCE here and reused by the per-occurrence
	/// diagnostic (<see cref="BuildTextLiteralError"/>) and surfaced verbatim to MCP agents through the
	/// update-page / sync-pages / validate-page tool descriptions and the <c>page-schema-resources</c>
	/// guidance, so the rule the validator rejects on is stated in identical words everywhere. Kept
	/// <c>const</c> so it stays usable inside <c>[Description]</c> attributes (compile-time constants only).
	/// </summary>
	internal const string LocalizableTextLiteralClause =
		"user-visible text on a view node (label, caption, title, tooltip, placeholder) must be a " +
		"localizable-string binding, not an inline literal: bind it via $Resources.Strings.<Key> and " +
		"register the key with its default-language value through the 'resources' parameter " +
		"(e.g. resources: '{\"<Key>\": \"<text>\"}'), or use the #ResourceString(<Key>)# macro form for " +
		"data-grid column captions and validator messages";

	/// <summary>
	/// Runs all mobile page validators and returns errors and warnings as separate lists.
	/// </summary>
	/// <param name="body">Plain-JSON mobile page body.</param>
	/// <param name="allowedMobileTypes">Component types supported by the mobile runtime.</param>
	/// <param name="webOnlyTypes">Component types that exist in the web registry but not mobile.</param>
	/// <returns>A tuple of blocking errors and non-blocking warnings.</returns>
	public static (List<string> Errors, List<string> Warnings) ValidateMobilePage(
		string body, IReadOnlySet<string> allowedMobileTypes, IReadOnlySet<string> webOnlyTypes,
		IReadOnlyDictionary<string, string>? explicitResources = null) {
		var errors = new List<string>();
		var warnings = new List<string>();

		SchemaValidationResult bodyResult = ValidateMobileBody(body);
		if (!bodyResult.IsValid) errors.AddRange(bodyResult.Errors);

		SchemaValidationResult validatorRefsResult = ValidateMobileNoValidatorReferences(body);
		if (!validatorRefsResult.IsValid) errors.AddRange(validatorRefsResult.Errors);

		SchemaValidationResult structureResult = ValidateMobileViewConfigDiffStructure(body);
		if (!structureResult.IsValid) errors.AddRange(structureResult.Errors);

		SchemaValidationResult componentResult = ValidateMobileComponentTypes(body, allowedMobileTypes, webOnlyTypes);
		warnings.AddRange(componentResult.Warnings);

		SchemaValidationResult bindingResult = ValidateMobileFieldBindings(body);
		if (!bindingResult.IsValid) errors.AddRange(bindingResult.Errors);

		SchemaValidationResult labelBindingResult = ValidateMobileStandardFieldBindings(body, explicitResources);
		if (!labelBindingResult.IsValid) errors.AddRange(labelBindingResult.Errors);
		warnings.AddRange(labelBindingResult.Warnings);

		SchemaValidationResult textLiteralResult = ValidateMobileLocalizableTextLiterals(body);
		if (!textLiteralResult.IsValid) errors.AddRange(textLiteralResult.Errors);

		return (errors, warnings);
	}

	/// <summary>
	/// Validates a mobile page body and reports errors for any AMD-only constructs
	/// (<c>validators</c>, <c>handlers</c>, custom <c>converters</c> sections) that are
	/// not supported in mobile JSON bodies.
	/// </summary>
	/// <param name="body">Plain-JSON mobile page body to validate.</param>
	/// <returns>
	/// A <see cref="SchemaValidationResult"/> that is invalid when disallowed top-level keys are found.
	/// </returns>
	public static SchemaValidationResult ValidateMobileBody(string body) {
		var result = new SchemaValidationResult { IsValid = true };
		if (string.IsNullOrWhiteSpace(body)) {
			result.IsValid = false;
			result.Errors.Add("Mobile page body is null or empty.");
			return result;
		}
		JsonDocument document;
		try {
			document = JsonDocument.Parse(body);
		} catch (JsonException ex) {
			result.IsValid = false;
			result.Errors.Add($"Mobile page body is not valid JSON: {ex.Message}");
			return result;
		}
		using (document) {
			if (document.RootElement.ValueKind != JsonValueKind.Object) {
				result.IsValid = false;
				result.Errors.Add("Mobile page body must be a JSON object.");
				return result;
			}
			if (document.RootElement.TryGetProperty("validators", out _)) {
				result.IsValid = false;
				result.Errors.Add("Mobile pages do not support validators. Remove the 'validators' section.");
			}
			if (document.RootElement.TryGetProperty("converters", out _)) {
				result.IsValid = false;
				result.Errors.Add("Mobile pages do not support custom converters. Use only OOTB converter references in binding expressions.");
			}
			if (document.RootElement.TryGetProperty("handlers", out _)) {
				result.IsValid = false;
				result.Errors.Add("Mobile pages do not support handlers. Remove the 'handlers' section.");
			}
			ValidateMobileDiffArrayProperties(document.RootElement, result);
			ValidateMobileConfigObjectProperties(document.RootElement, result);
			ValidateMobileNoUnknownRootProperties(document.RootElement, result);
		}
		return result;
	}

	private static void ValidateMobileDiffArrayProperties(JsonElement root, SchemaValidationResult result) {
		foreach (string name in DiffPropertyNames) {
			if (root.TryGetProperty(name, out JsonElement value) && value.ValueKind != JsonValueKind.Array) {
				result.IsValid = false;
				result.Errors.Add($"'{name}' must be a JSON array, but got {value.ValueKind}.");
			}
		}
	}

	private static void ValidateMobileConfigObjectProperties(JsonElement root, SchemaValidationResult result) {
		foreach (string name in ConfigPropertyNames) {
			if (root.TryGetProperty(name, out JsonElement value) && value.ValueKind != JsonValueKind.Object) {
				result.IsValid = false;
				result.Errors.Add($"'{name}' must be a JSON object, but got {value.ValueKind}.");
			}
		}
	}

	private static void ValidateMobileNoUnknownRootProperties(JsonElement root, SchemaValidationResult result) {
		foreach (string propertyName in root.EnumerateObject().Select(property => property.Name)) {
			if (AllowedMobileRootProperties.Contains(propertyName) ||
				DisallowedMobileRootProperties.Contains(propertyName)) {
				continue;
			}
			result.IsValid = false;
			result.Errors.Add(
				$"Unknown root property '{propertyName}'. " +
				$"Mobile page bodies may only contain: {string.Join(", ", AllowedMobileRootProperties)}.");
		}
	}

	/// <summary>
	/// Validates that no attribute inside <c>viewModelConfigDiff</c> or <c>viewModelConfig</c>
	/// binds a <c>validators</c> property. Mobile pages do not support validator usages —
	/// neither custom nor OOTB — so any such reference must be reported as an error.
	/// The actual validator definitions live in <c>SCHEMA_VALIDATORS</c> on web (forbidden at
	/// the body level by <see cref="ValidateMobileBody"/>) or in a remote module. This check
	/// covers the per-attribute binding form where a validator is applied to a specific attribute.
	/// Field-level validation on mobile must be implemented via entity-level business rules.
	/// </summary>
	/// <param name="body">Plain-JSON mobile page body to validate.</param>
	/// <returns>
	/// A <see cref="SchemaValidationResult"/> that is invalid when any attribute binds a <c>validators</c> property.
	/// </returns>
	public static SchemaValidationResult ValidateMobileNoValidatorReferences(string body) {
		var result = new SchemaValidationResult { IsValid = true };
		if (string.IsNullOrWhiteSpace(body)) {
			return result;
		}
		JsonDocument document;
		try {
			document = JsonDocument.Parse(body);
		} catch {
			return result; // JSON errors reported by ValidateMobileBody
		}
		using (document) {
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object) {
				return result;
			}
			ScanMobileAttributeValidatorsFromDiff(root, result);
			ScanMobileAttributeValidatorsFromConfig(root, result);
			if (result.Errors.Count > 0) {
				result.IsValid = false;
			}
		}
		return result;
	}

	private static void ScanMobileAttributeValidatorsFromDiff(JsonElement root, SchemaValidationResult result) {
		if (!root.TryGetProperty(ViewModelConfigDiffPropertyName, out JsonElement diff) ||
			diff.ValueKind != JsonValueKind.Array) {
			return;
		}
		foreach (JsonElement entry in diff.EnumerateArray()) {
			if (entry.ValueKind != JsonValueKind.Object) {
				continue;
			}
			if (!ShouldScanAsAttributesContainer(entry)) {
				continue;
			}
			if (!entry.TryGetProperty(ValuesPropertyName, out JsonElement values) ||
				values.ValueKind != JsonValueKind.Object) {
				continue;
			}
			foreach (JsonProperty attr in values.EnumerateObject()) {
				ReportIfAttributeHasValidators(attr, result);
			}
		}
	}

	private static void ScanMobileAttributeValidatorsFromConfig(JsonElement root, SchemaValidationResult result) {
		if (!root.TryGetProperty(ViewModelConfigPropertyName, out JsonElement config) ||
			config.ValueKind != JsonValueKind.Object) {
			return;
		}
		if (!config.TryGetProperty(AttributesPropertyName, out JsonElement attrs) ||
			attrs.ValueKind != JsonValueKind.Object) {
			return;
		}
		foreach (JsonProperty attr in attrs.EnumerateObject()) {
			ReportIfAttributeHasValidators(attr, result);
		}
	}

	private static void ReportIfAttributeHasValidators(JsonProperty attr, SchemaValidationResult result) {
		if (attr.Value.ValueKind != JsonValueKind.Object) {
			return;
		}
		if (attr.Value.TryGetProperty(ValidatorsPropertyName, out _)) {
			result.Errors.Add(
				$"Attribute '{attr.Name}' binds a 'validators' property. " +
				"Mobile pages do not support validator usages in any form (custom or OOTB). " +
				"Remove the validators binding and implement field-level validation via " +
				"entity-level business rules (create-entity-business-rules).");
		}
	}

	/// <summary>
	/// Validates component <c>type</c> references in a mobile page body's <c>viewConfigDiff</c>
	/// against the mobile and web component registries.
	/// <list type="bullet">
	///   <item>Type in mobile registry → OK.</item>
	///   <item>Type in web registry but not mobile → <b>warning</b> (web-only component;
	///     may also be a custom mobile component with the same type string).</item>
	///   <item>Type in neither registry → OK (assumed custom component).</item>
	/// </list>
	/// </summary>
	/// <param name="body">Plain-JSON mobile page body.</param>
	/// <param name="allowedMobileTypes">
	/// Set of component type strings that the mobile runtime supports (e.g. <c>crt.Input</c>).
	/// </param>
	/// <param name="webOnlyTypes">
	/// Set of component type strings that exist in the web registry but not in the mobile registry.
	/// </param>
	/// <returns>
	/// A <see cref="SchemaValidationResult"/> whose warnings list components that are web-only.
	/// </returns>
	public static SchemaValidationResult ValidateMobileComponentTypes(
		string body, IReadOnlySet<string> allowedMobileTypes, IReadOnlySet<string> webOnlyTypes) {
		var result = new SchemaValidationResult { IsValid = true };
		if (string.IsNullOrWhiteSpace(body) || allowedMobileTypes.Count == 0) {
			return result;
		}
		JsonDocument document;
		try {
			document = JsonDocument.Parse(body);
		} catch {
			return result; // JSON errors reported by ValidateMobileBody
		}
		using (document) {
			if (!document.RootElement.TryGetProperty(ViewConfigDiffPropertyName, out JsonElement vcd) ||
				vcd.ValueKind != JsonValueKind.Array) {
				return result;
			}
			foreach (JsonElement entry in vcd.EnumerateArray()) {
				if (entry.ValueKind != JsonValueKind.Object) {
					continue;
				}
				string? type = GetMobileEntryType(entry);
				if (type == null || allowedMobileTypes.Contains(type)) {
					continue;
				}
				if (webOnlyTypes.Contains(type)) {
					result.Warnings.Add(
						$"Component type '{type}' exists in the web registry but not in the mobile registry. " +
						"Do NOT use web-only or unknown components on a mobile page without explicit approval from the user. " +
						"If this is a custom mobile component with the same type name, ignore this warning; " +
						"otherwise use get-component-info to find a supported mobile alternative.");
				}
			}
		}
		return result;
	}

	/// <summary>
	/// Validates that every entry in a mobile page body's <c>viewConfigDiff</c> array
	/// has the required <c>operation</c> and <c>name</c> properties.
	/// </summary>
	/// <param name="body">Plain-JSON mobile page body.</param>
	/// <returns>
	/// A <see cref="SchemaValidationResult"/> that is invalid when entries are missing
	/// required structural properties.
	/// </returns>
	public static SchemaValidationResult ValidateMobileViewConfigDiffStructure(string body) {
		var result = new SchemaValidationResult { IsValid = true };
		if (string.IsNullOrWhiteSpace(body)) {
			return result;
		}
		JsonDocument document;
		try {
			document = JsonDocument.Parse(body);
		} catch {
			return result;
		}
		using (document) {
			if (!document.RootElement.TryGetProperty(ViewConfigDiffPropertyName, out JsonElement vcd) ||
				vcd.ValueKind != JsonValueKind.Array) {
				return result;
			}
			int index = 0;
			foreach (JsonElement entry in vcd.EnumerateArray()) {
				ValidateViewConfigDiffEntry(entry, index, result);
				index++;
			}
		}
		return result;
	}

	private static void ValidateViewConfigDiffEntry(JsonElement entry, int index, SchemaValidationResult result) {
		if (entry.ValueKind != JsonValueKind.Object) {
			return;
		}
		bool hasOperation = entry.TryGetProperty(OperationPropertyName, out _);
		bool hasName = entry.TryGetProperty("name", out _);
		if (hasOperation && hasName) {
			return;
		}
		result.IsValid = false;
		var missing = new List<string>(2);
		if (!hasOperation) missing.Add(OperationPropertyName);
		if (!hasName) missing.Add("name");
		result.Errors.Add(
			$"viewConfigDiff entry at index {index} is missing required " +
			$"{(missing.Count == 1 ? "property" : "properties")}: {string.Join(", ", missing)}.");
	}

	/// <summary>
	/// Validates that every <c>$AttributeName</c> binding in a mobile page body's
	/// <c>viewConfigDiff</c> corresponds to a declared attribute in
	/// <c>viewModelConfigDiff</c> or <c>viewModelConfig</c>.
	/// </summary>
	/// <param name="body">Plain-JSON mobile page body.</param>
	/// <returns>
	/// A <see cref="SchemaValidationResult"/> that is invalid when an undeclared attribute binding is found.
	/// </returns>
	public static SchemaValidationResult ValidateMobileFieldBindings(string body) {
		var result = new SchemaValidationResult { IsValid = true };
		if (string.IsNullOrWhiteSpace(body)) {
			return result;
		}
		JsonDocument document;
		try {
			document = JsonDocument.Parse(body);
		} catch {
			return result;
		}
		using (document) {
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object) {
				return result;
			}
			HashSet<string> declaredAttributes = CollectMobileViewModelAttributes(root);
			if (declaredAttributes.Count == 0) {
				return result; // nothing to cross-check against
			}
			HashSet<string> referencedAttributes = CollectMobileViewBindings(root);
			foreach (string attr in referencedAttributes.Where(a => !declaredAttributes.Contains(a))) {
				result.Errors.Add(
					$"viewConfigDiff binds to '${attr}' but no matching attribute is declared in viewModelConfigDiff/viewModelConfig.");
			}
			if (result.Errors.Count > 0) {
				result.IsValid = false;
			}
		}
		return result;
	}

	/// <summary>
	/// Validates label/resource bindings on standard field components in a mobile page body.
	/// Mirrors <see cref="ValidateStandardFieldBindings"/> for the web flow, but operates on
	/// the plain-JSON mobile schema instead of marker-delimited JavaScript: it reads
	/// <c>viewConfigDiff</c> / <c>viewModelConfigDiff</c> directly from the JSON root.
	/// Mobile pages do not support handlers, so the control-vs-handler attribute cross-check
	/// is skipped (the handler-writes set is empty).
	/// </summary>
	/// <param name="body">Plain-JSON mobile page body.</param>
	/// <param name="explicitResources">Optional explicit resources dictionary used to suppress
	/// "label will render blank" warnings when a resource key is provided explicitly.</param>
	/// <returns>
	/// A <see cref="SchemaValidationResult"/> with warnings for labels whose resource key is
	/// neither explicitly provided nor auto-provided by the platform via a DS-bound attribute.
	/// </returns>
	public static SchemaValidationResult ValidateMobileStandardFieldBindings(
		string body,
		IReadOnlyDictionary<string, string>? explicitResources = null) {
		var result = new SchemaValidationResult { IsValid = true };
		if (string.IsNullOrWhiteSpace(body)) {
			return result;
		}
		JsonDocument document;
		try {
			document = JsonDocument.Parse(body);
		} catch {
			return result;
		}
		using (document) {
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object) {
				return result;
			}
			if (!root.TryGetProperty(ViewConfigDiffPropertyName, out JsonElement viewConfigDiff)) {
				return result;
			}
			Dictionary<string, string> modelPaths = CollectMobileViewModelPaths(body);
			HashSet<string> declaredAttributes = CollectMobileViewModelAttributes(root);
			var ctx = new FieldValidationContext(
				declaredAttributes,
				modelPaths,
				explicitResources,
				new HashSet<string>(StringComparer.OrdinalIgnoreCase),
				result);
			ValidateFieldComponents(viewConfigDiff, in ctx);
		}
		if (result.Errors.Count > 0) {
			result.IsValid = false;
		}
		return result;
	}
	/// the handler bodies reference the SDK argument injected through <c>SCHEMA_ARGS</c>.
	/// </summary>
	/// <param name="jsBody">Raw JavaScript body of a Freedom UI page schema.</param>
	/// <returns>
	/// A <see cref="SchemaValidationResult"/> whose warnings list an entry when the SDK
	/// dependency appears to be missing. Uses warnings (not errors) because the check is
	/// heuristic — the handler may reference a local variable named <c>sdk</c> unrelated
	/// to the Creatio SDK.
	/// </returns>
	public static SchemaValidationResult ValidateSchemaDepsCompleteness(string jsBody) {
		var result = new SchemaValidationResult { IsValid = true };
		if (string.IsNullOrEmpty(jsBody) || !HandlersOrConvertersMentionSdk(jsBody)) {
			return result;
		}
		if (!PageSchemaSectionReader.TryRead(jsBody, out string depsContent, "SCHEMA_DEPS")) {
			return result;
		}
		if (!depsContent.Contains("@creatio-devkit/common", StringComparison.Ordinal)) {
			result.Warnings.Add(
				"Handlers or converters reference 'sdk.' but SCHEMA_DEPS does not include '@creatio-devkit/common'. " +
				"Add it to SCHEMA_DEPS and declare a matching parameter (e.g. 'sdk') in SCHEMA_ARGS. " +
				"Call get-guidance with name 'page-schema-creatio-devkit-common' for the correct pattern.");
		}
		return result;
	}

	private static bool HandlersOrConvertersMentionSdk(string jsBody) {
		if (!PageSchemaSectionReader.TryRead(jsBody, out string handlersContent, SchemaHandlersMarker)) {
			return false;
		}
		if (string.IsNullOrWhiteSpace(handlersContent) || handlersContent.Trim() == "[]") {
			return false;
		}
		if (SdkUsagePattern.IsMatch(handlersContent)) {
			return true;
		}
		// Also check converters — async converters can use SDK
		if (!PageSchemaSectionReader.TryRead(jsBody, out string convertersContent, SchemaConvertersMarker)) {
			return false;
		}
		if (string.IsNullOrWhiteSpace(convertersContent) || convertersContent.Trim() == "{}") {
			return false;
		}
		return SdkUsagePattern.IsMatch(convertersContent);
	}

	/// <summary>
	/// Detects reactive context attribute reads of the form <c>$context["Attr"]</c> or
	/// <c>request.$context["Attr"]</c> that are NOT preceded by <c>await</c>, anywhere in the
	/// page body (handler bodies, converters, and free module-scope helper functions alike).
	/// </summary>
	/// <remarks>
	/// In a Freedom UI page body the bracket accessor on <c>$context</c> is asynchronous: it
	/// returns a <c>Promise</c>, so the read MUST be awaited (<c>await request.$context["Attr"]</c>).
	/// An un-awaited read yields a <c>Promise</c> object instead of the value; because a Promise is
	/// always truthy and never nullish, it silently breaks the surrounding expression — most often a
	/// <c>?? fallback</c> chain that never reaches its fallback, a comparison that is always false, or
	/// an argument passed on un-resolved. The mistake is valid JavaScript, so neither marker, JSON,
	/// nor JS-syntax validation catches it; this heuristic does.
	/// <para>
	/// All findings are WARNINGS, not errors. The check is a regex heuristic over raw text, so it
	/// cannot exclude an occurrence inside a string literal or comment, and it does not resolve a
	/// <c>$context</c> handle aliased to another variable. It therefore advises rather than blocks,
	/// matching the fail-open posture of <see cref="ValidateSchemaDepsCompleteness"/>. Bracket reads
	/// used as an assignment target (<c>$context["X"] = ...</c>) are skipped because that is a write,
	/// not a read; the dedicated write API is <c>$context.set(...)</c>.
	/// </para>
	/// </remarks>
	/// <param name="jsBody">Raw JavaScript body of a Freedom UI page schema.</param>
	/// <returns>
	/// A <see cref="SchemaValidationResult"/> that is always valid; its warnings list one entry per
	/// distinct attribute name read without <c>await</c>.
	/// </returns>
	public static SchemaValidationResult ValidateContextAccessAwait(string jsBody) {
		var result = new SchemaValidationResult { IsValid = true };
		if (string.IsNullOrEmpty(jsBody)) {
			return result;
		}
		var reported = new HashSet<string>(StringComparer.Ordinal);
		try {
			foreach (Match match in ContextBracketReadPattern.Matches(jsBody)) {
				if (match.Groups["await"].Success) {
					continue;
				}
				if (IsAssignmentTarget(jsBody, match.Index + match.Length)) {
					continue;
				}
				string attributeName = match.Groups["name"].Value;
				if (!reported.Add(attributeName)) {
					continue;
				}
				result.Warnings.Add(
					$"Page body reads '$context[\"{attributeName}\"]' without 'await'. Reactive context attribute reads " +
					"are asynchronous and return a Promise; an un-awaited read yields a Promise object — always truthy and " +
					"never nullish — which silently breaks '??' fallbacks, comparisons, and arguments built from it. " +
					$"Change it to 'await $context[\"{attributeName}\"]' (e.g. 'const x = arg ?? (await $context[\"{attributeName}\"]) ?? fallback;'). " +
					"Call get-guidance with name 'page-schema-handlers' for the read/write contract.");
			}
		} catch (RegexMatchTimeoutException) {
			// Advisory heuristic: a pathological body that trips the regex timeout must fail open, not
			// surface as a hard error. update-page's ValidateWebPageBody calls this directly (RunContentValidation
			// is only a short-circuit, not a timeout guard), so the fail-open guard belongs here.
		}
		return result;
	}

	/// <summary>
	/// Reports whether the first non-whitespace character at or after <paramref name="indexAfterRead"/>
	/// begins a plain assignment (<c>=</c> not followed by <c>=</c> or <c>&gt;</c>), which marks the
	/// preceding bracket access as a write target rather than an un-awaited read.
	/// </summary>
	private static bool IsAssignmentTarget(string jsBody, int indexAfterRead) {
		int i = indexAfterRead;
		while (i < jsBody.Length && char.IsWhiteSpace(jsBody[i])) {
			i++;
		}
		if (i >= jsBody.Length || jsBody[i] != '=') {
			return false;
		}
		// Distinguish assignment '=' from comparison '=='/'===' and arrow '=>', which are reads.
		return i + 1 >= jsBody.Length || (jsBody[i + 1] != '=' && jsBody[i + 1] != '>');
	}

	private static string? GetMobileEntryType(JsonElement entry) {
		// Type can be in entry.values.type or entry.type
		if (entry.TryGetProperty(ValuesPropertyName, out JsonElement values) &&
			values.ValueKind == JsonValueKind.Object &&
			values.TryGetProperty(TypePropertyName, out JsonElement typeEl) &&
			typeEl.ValueKind == JsonValueKind.String) {
			return typeEl.GetString();
		}
		if (entry.TryGetProperty(TypePropertyName, out JsonElement directType) &&
			directType.ValueKind == JsonValueKind.String) {
			return directType.GetString();
		}
		return null;
	}

	private static HashSet<string> CollectMobileViewModelAttributes(JsonElement root) {
		var attributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		CollectMobileAttributesFrom(root, ViewModelConfigDiffPropertyName, isArray: true, attributes);
		CollectMobileAttributesFrom(root, ViewModelConfigPropertyName, isArray: false, attributes);
		return attributes;
	}

	private static void CollectMobileAttributesFrom(
		JsonElement root, string propertyName, bool isArray,
		HashSet<string> attributes) {
		if (!root.TryGetProperty(propertyName, out JsonElement section)) {
			return;
		}
		if (isArray && section.ValueKind == JsonValueKind.Array) {
			CollectAttributesFromArraySection(section, attributes);
		} else if (!isArray && section.ValueKind == JsonValueKind.Object) {
			CollectAttributesFromObjectSection(section, attributes);
		}
	}

	private static void CollectAttributesFromArraySection(JsonElement section, HashSet<string> attributes) {
		foreach (JsonElement entry in section.EnumerateArray()) {
			if (entry.ValueKind != JsonValueKind.Object || !ShouldScanAsAttributesContainer(entry)) {
				continue;
			}
			// Merge-into-attributes pattern: { "operation":"merge", "path":["attributes"], "values":{...} }
			if (entry.TryGetProperty(ValuesPropertyName, out JsonElement values) &&
				values.ValueKind == JsonValueKind.Object) {
				AddObjectPropertyNames(values, attributes);
			}
		}
	}

	private static void CollectAttributesFromObjectSection(JsonElement section, HashSet<string> attributes) {
		if (section.TryGetProperty(AttributesPropertyName, out JsonElement attrs) &&
			attrs.ValueKind == JsonValueKind.Object) {
			AddObjectPropertyNames(attrs, attributes);
		}
	}

	private static void AddObjectPropertyNames(JsonElement obj, HashSet<string> attributes) {
		foreach (JsonProperty attr in obj.EnumerateObject()) {
			attributes.Add(attr.Name);
		}
	}

	private static HashSet<string> CollectMobileViewBindings(JsonElement root) {
		var bindings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (!root.TryGetProperty(ViewConfigDiffPropertyName, out JsonElement vcd) ||
			vcd.ValueKind != JsonValueKind.Array) {
			return bindings;
		}
		foreach (JsonElement entry in vcd.EnumerateArray()) {
			if (entry.ValueKind != JsonValueKind.Object) {
				continue;
			}
			JsonElement values = entry.TryGetProperty(ValuesPropertyName, out JsonElement v)
				? v
				: entry;
			if (values.ValueKind != JsonValueKind.Object) {
				continue;
			}
			ExtractDollarBindings(values, bindings);
		}
		return bindings;
	}

	private static void ExtractDollarBindings(JsonElement obj, HashSet<string> bindings) {
		foreach (JsonElement value in obj.EnumerateObject().Select(p => p.Value)) {
			if (value.ValueKind == JsonValueKind.String) {
				if (TryNormalizeDollarBinding(value.GetString(), out string? attrName) && attrName != null) {
					bindings.Add(attrName);
				}
			} else if (value.ValueKind == JsonValueKind.Object) {
				ExtractDollarBindings(value, bindings);
			}
		}
	}

	private static bool TryNormalizeDollarBinding(string? raw, out string? attrName) {
		attrName = null;
		if (raw == null || raw.Length <= 1 || !raw.StartsWith("$", StringComparison.Ordinal)) {
			return false;
		}
		string candidate = raw[1..];
		// Strip converter pipe: "$AttrName | crt.InvertBooleanValue" → "AttrName"
		int pipeIndex = candidate.IndexOf('|', StringComparison.Ordinal);
		if (pipeIndex > 0) {
			candidate = candidate[..pipeIndex].TrimEnd();
		}
		// Skip resource bindings like $Resources.Strings.X
		if (candidate.StartsWith("Resources.", StringComparison.Ordinal)) {
			return false;
		}
		attrName = candidate;
		return true;
	}

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
		MergeResult(result, ValidateHandlerStructure(jsBody));
		return result;
	}

	/// <summary>
	/// Validates the structure of the JavaScript handler array in <paramref name="jsBody"/>.
	/// </summary>
	/// <param name="jsBody">Raw JavaScript body of a Freedom UI page schema.</param>
	/// <returns>
	/// A <see cref="SchemaValidationResult"/> accumulating all structural violations found,
	/// or a passing result when no violations are detected.
	/// </returns>
	public static SchemaValidationResult ValidateHandlerStructure(string jsBody) {
		return SchemaHandlerValidationService.Validate(jsBody);
	}

	/// <summary>
	/// Body-only structural validation for <c>crt.RunBusinessProcessRequest</c> buttons:
	/// every such button must carry a non-empty <c>processName</c> AND a non-empty
	/// <c>processRunType</c>. Both are required by the request contract; omitting
	/// <c>processRunType</c> does not error at runtime but silently runs the process without the
	/// intended record context (the same silent-misbehavior class as a wrong parameter code).
	/// Parameter-code correctness is validated separately against the live process signature
	/// (it needs the environment).
	/// </summary>
	public static SchemaValidationResult ValidateRunProcessButtonStructure(string jsBody) {
		SchemaValidationResult result = new() { IsValid = true };
		foreach (RunProcessButtonConfig config in RunProcessButtonConfigReader.Read(jsBody)) {
			string buttonLabel = string.IsNullOrWhiteSpace(config.ButtonName)
				? "a crt.RunBusinessProcessRequest button"
				: $"run-process button '{config.ButtonName}'";
			if (string.IsNullOrWhiteSpace(config.ProcessName)) {
				result.IsValid = false;
				result.Errors.Add(
					$"{buttonLabel} is missing the required 'processName' (the process schema code). "
					+ "Resolve it with get-process-signature and set params.processName.");
			}
			if (string.IsNullOrWhiteSpace(config.ProcessRunType)) {
				result.IsValid = false;
				result.Errors.Add(
					$"{buttonLabel} is missing the required 'processRunType' "
					+ "('RegardlessOfThePage', 'ForTheSelectedPage', or 'ForTheSelectedRecords'). "
					+ "Without it the process does not run against the intended record context.");
			}
		}
		return result;
	}

	#region Registry-driven chart-widget validation
	// Walks the registry's typeDefinitions for each inserted chart and enforces ONLY the data-block
	// nesting (data.providing.aggregation.column), never cosmetic fields (color/formatting/legend/title).
	// Full contract on ValidateChartWidgetConfig below; the two registry-content bridges are TODO(ENG-92174).

	private const string ChartWidgetType = "crt.ChartWidget";
	private const string ChartWidgetRootType = "ChartWidgetConfig";

	/// <summary>
	/// TODO(ENG-92174, registry-bridge-1): remove once the producer emits the real key. The registry references the
	/// chart series type as "SeriesConfig", but the typeDefinitions dictionary defines it under
	/// "ChartSeriesConfig"; without this alias the walk dead-ends at the series and never reaches the
	/// data/aggregation fields. <see cref="ResolveTypeAlias"/> applies it only when the wire name is
	/// absent AND the alias target exists, so it self-deactivates once the producer emits the real name.
	/// </summary>
	private static readonly IReadOnlyDictionary<string, string> ChartTypeNameAliases =
		new Dictionary<string, string>(StringComparer.Ordinal) {
			["SeriesConfig"] = "ChartSeriesConfig"
		};

	/// <summary>
	/// TODO(ENG-92174, registry-bridge-2): remove once the registry records generic type-parameter order. A generic
	/// field type is stored as the raw string "WidgetDataConfig&lt;WidgetDataProvidingConfig, ...&gt;",
	/// but the registry never names the type's parameters, so "providing" (typed as the placeholder
	/// "TProvidingConfig") cannot be bound to its concrete argument. This maps the parameter order for
	/// the one generic on the chart path. <see cref="BuildGenericSubstitution"/> applies it only when a
	/// known order exists, so it self-deactivates once the registry carries the metadata itself.
	/// </summary>
	private static readonly IReadOnlyDictionary<string, string[]> ChartGenericParameterOrder =
		new Dictionary<string, string[]>(StringComparer.Ordinal) {
			["WidgetDataConfig"] = new[] { "TProvidingConfig", "TFormat" }
		};

	/// <summary>
	/// The data-providing type whose subtree the validator actually checks. The walk navigates from the
	/// chart config down to here (through the bridges), but only reports a missing required field once it
	/// has entered this type — so it validates the data structure (aggregation / schemaName / column) and
	/// leaves the cosmetic fields above it (color, formatting, legend, title) alone.
	/// </summary>
	private const string ChartDataProvidingType = "WidgetDataProvidingConfig";

	private static readonly IReadOnlyDictionary<string, string> EmptyTypeSubstitution =
		new Dictionary<string, string>(StringComparer.Ordinal);

	/// <summary>
	/// Registry-driven validation of <c>crt.ChartWidget</c> inserts in the page's <c>viewConfigDiff</c>.
	/// Walks <paramref name="typeDefinitions"/> (the merged per-component + document-level registry type
	/// schemas) from <c>ChartWidgetConfig</c> down to each series' <c>data.providing</c> block and reports
	/// required fields missing INSIDE that block — most importantly <c>aggregation.column</c>, whose
	/// absence renders an empty chart. Cosmetic fields above the data block (color, formatting, legend,
	/// title) are intentionally NOT checked, so the registry over-marking them required cannot false-positive.
	/// <para>
	/// Scope: only <c>operation:"insert"</c> entries are checked (a <c>merge</c> legitimately omits
	/// fields supplied by the base schema). Fail-open: an empty/absent registry, a missing marker, or an
	/// unparseable body yields a passing result so an offline run never blocks a save.
	/// </para>
	/// </summary>
	public static SchemaValidationResult ValidateChartWidgetConfig(
		string jsBody,
		IReadOnlyDictionary<string, JsonElement>? typeDefinitions) {
		var result = new SchemaValidationResult { IsValid = true };
		if (string.IsNullOrEmpty(jsBody)) {
			return result;
		}
		// Fail-open: registry unavailable (offline / not yet cached) — skip, never block. Mirrors
		// ValidateMobileComponentTypes' empty-catalog behaviour.
		if (typeDefinitions is null || typeDefinitions.Count == 0) {
			return result;
		}
		if (!PageSchemaSectionReader.TryRead(jsBody, out string vcdContent, SchemaViewConfigDiff, SchemaDiffMarker)) {
			return result;
		}
		if (!TryParseJsonDocument(vcdContent, out JsonDocument vcdDoc, out _)) {
			return result;
		}
		using (vcdDoc) {
			if (vcdDoc.RootElement.ValueKind == JsonValueKind.Array) {
				foreach (JsonElement entry in vcdDoc.RootElement.EnumerateArray()) {
					// Only freshly-inserted charts are self-contained; merges/removes legitimately omit
					// fields and are skipped.
					if (IsInsertOperation(entry)) {
						ScanInsertedChartWidgets(entry, string.Empty, typeDefinitions, result);
					}
				}
			}
		}
		if (result.Errors.Count > 0) {
			result.IsValid = false;
		}
		return result;
	}

	private static void ScanInsertedChartWidgets(
		JsonElement node, string ownerName,
		IReadOnlyDictionary<string, JsonElement> typeDefinitions, SchemaValidationResult result) {
		switch (node.ValueKind) {
			case JsonValueKind.Object:
				string currentName = TryGetNodeName(node, out string nodeName) ? nodeName : ownerName;
				if (IsChartWidgetNode(node, out JsonElement config)) {
					string label = string.IsNullOrWhiteSpace(currentName)
						? "a crt.ChartWidget config"
						: $"chart widget '{currentName}' config";
					ValidateValueAgainstType(config, ChartWidgetRootType, EmptyTypeSubstitution, typeDefinitions, label, result, report: false);
				}
				foreach (JsonProperty property in node.EnumerateObject()) {
					ScanInsertedChartWidgets(property.Value, currentName, typeDefinitions, result);
				}
				break;
			case JsonValueKind.Array:
				foreach (JsonElement item in node.EnumerateArray()) {
					ScanInsertedChartWidgets(item, ownerName, typeDefinitions, result);
				}
				break;
		}
	}

	private static bool IsChartWidgetNode(JsonElement node, out JsonElement config) {
		config = default;
		if (!node.TryGetProperty(TypePropertyName, out JsonElement typeElement) ||
		    typeElement.ValueKind != JsonValueKind.String ||
		    !string.Equals(typeElement.GetString(), ChartWidgetType, StringComparison.OrdinalIgnoreCase)) {
			return false;
		}
		if (!node.TryGetProperty("config", out JsonElement configElement) ||
		    configElement.ValueKind != JsonValueKind.Object) {
			return false;
		}
		config = configElement;
		return true;
	}

	/// <summary>
	/// Validates <paramref name="value"/> against the registry type <paramref name="typeName"/>: every
	/// field that type marks <c>required</c> must be present, and present values recurse into their own
	/// types. Stops without error at a type absent from the dictionary — an opaque/external leaf the
	/// registry does not describe, so nothing deeper can be checked. Recursion is bounded by the (finite)
	/// page-body value tree: it only descends into fields the value actually carries.
	/// </summary>
	private static void ValidateValueAgainstType(
		JsonElement value, string typeName,
		IReadOnlyDictionary<string, string> substitution,
		IReadOnlyDictionary<string, JsonElement> typeDefinitions,
		string path, SchemaValidationResult result, bool report) {
		string resolvedName = ResolveTypeAlias(typeName, typeDefinitions);
		if (!typeDefinitions.TryGetValue(resolvedName, out JsonElement definition) ||
		    !definition.TryGetProperty("fields", out JsonElement fields) ||
		    fields.ValueKind != JsonValueKind.Object) {
			return;
		}
		// Begin reporting once we enter the data-providing block; before that we only navigate, so the
		// cosmetic fields higher up (color/formatting/legend/title) are never flagged.
		bool reportHere = report || string.Equals(resolvedName, ChartDataProvidingType, StringComparison.Ordinal);
		ValidateFieldsMap(value, fields, substitution, typeDefinitions, path, result, reportHere);
	}

	private static void ValidateFieldsMap(
		JsonElement value, JsonElement fields,
		IReadOnlyDictionary<string, string> substitution,
		IReadOnlyDictionary<string, JsonElement> typeDefinitions,
		string path, SchemaValidationResult result, bool report) {
		if (value.ValueKind != JsonValueKind.Object) {
			return;
		}
		foreach (JsonProperty field in fields.EnumerateObject()) {
			bool required = field.Value.TryGetProperty("required", out JsonElement requiredElement) &&
				requiredElement.ValueKind == JsonValueKind.True;
			bool present = value.TryGetProperty(field.Name, out JsonElement fieldValue);
			if (!present) {
				// Only flag inside the data-providing block; cosmetic fields above it are out of scope.
				if (report && required) {
					result.Errors.Add(
						$"{path}.{field.Name} is required by the component registry but is missing. " +
						"Call get-guidance with name 'chart-widget-guidance' for the full contract.");
				}
				continue;
			}
			RecurseIntoFieldValue(fieldValue, field.Value, substitution, typeDefinitions, $"{path}.{field.Name}", result, report);
		}
	}

	private static void RecurseIntoFieldValue(
		JsonElement value, JsonElement fieldDefinition,
		IReadOnlyDictionary<string, string> substitution,
		IReadOnlyDictionary<string, JsonElement> typeDefinitions,
		string path, SchemaValidationResult result, bool report) {
		// Inline object shape (e.g. aggregation: { column: { required } }).
		if (fieldDefinition.TryGetProperty("shape", out JsonElement shape) && shape.ValueKind == JsonValueKind.Object) {
			ValidateFieldsMap(value, shape, substitution, typeDefinitions, path, result, report);
			return;
		}
		if (!fieldDefinition.TryGetProperty("type", out JsonElement typeElement) ||
		    typeElement.ValueKind != JsonValueKind.String) {
			return;
		}
		string typeExpression = typeElement.GetString() ?? string.Empty;
		// Array: validate each element against the item type.
		if (string.Equals(typeExpression, "array", StringComparison.Ordinal)) {
			if (value.ValueKind == JsonValueKind.Array &&
			    fieldDefinition.TryGetProperty("items", out JsonElement items)) {
				int index = 0;
				foreach (JsonElement element in value.EnumerateArray()) {
					RecurseIntoFieldValue(element, items, substitution, typeDefinitions, $"{path}[{index}]", result, report);
					index++;
				}
			}
			return;
		}
		// Resolve a generic placeholder ("TProvidingConfig") to the concrete argument bound above.
		typeExpression = ApplyTypeSubstitution(typeExpression, substitution);
		// Generic instantiation: WidgetDataConfig<WidgetDataProvidingConfig, ...>.
		if (TryParseGenericType(typeExpression, out string genericName, out string[] genericArguments)) {
			ValidateValueAgainstType(
				value, genericName, BuildGenericSubstitution(genericName, genericArguments),
				typeDefinitions, path, result, report);
			return;
		}
		// Union ("A | B | C"): cannot pick a branch deterministically — stop rather than false-positive.
		if (typeExpression.Contains('|')) {
			return;
		}
		ValidateValueAgainstType(value, typeExpression, EmptyTypeSubstitution, typeDefinitions, path, result, report);
	}

	private static string ResolveTypeAlias(string typeName, IReadOnlyDictionary<string, JsonElement> typeDefinitions) {
		// Self-deactivating bridge 1: alias only when the wire name is absent and the alias target exists.
		if (!typeDefinitions.ContainsKey(typeName) &&
		    ChartTypeNameAliases.TryGetValue(typeName, out string alias) &&
		    typeDefinitions.ContainsKey(alias)) {
			return alias;
		}
		return typeName;
	}

	private static IReadOnlyDictionary<string, string> BuildGenericSubstitution(string genericName, string[] arguments) {
		// Self-deactivating bridge 2: only when we know this generic's parameter order.
		if (!ChartGenericParameterOrder.TryGetValue(genericName, out string[] parameterNames)) {
			return EmptyTypeSubstitution;
		}
		var map = new Dictionary<string, string>(StringComparer.Ordinal);
		for (int i = 0; i < parameterNames.Length && i < arguments.Length; i++) {
			map[parameterNames[i]] = arguments[i];
		}
		return map;
	}

	private static string ApplyTypeSubstitution(string typeExpression, IReadOnlyDictionary<string, string> substitution) {
		return substitution.TryGetValue(typeExpression, out string concrete) ? concrete : typeExpression;
	}

	private static bool TryParseGenericType(string expression, out string name, out string[] arguments) {
		name = string.Empty;
		arguments = System.Array.Empty<string>();
		int open = expression.IndexOf('<');
		if (open <= 0 || !expression.EndsWith(">", StringComparison.Ordinal)) {
			return false;
		}
		name = expression.Substring(0, open).Trim();
		string inner = expression.Substring(open + 1, expression.Length - open - 2);
		arguments = SplitTopLevelGenericArguments(inner);
		return arguments.Length > 0;
	}

	private static string[] SplitTopLevelGenericArguments(string inner) {
		var parts = new List<string>();
		int depth = 0;
		int start = 0;
		for (int i = 0; i < inner.Length; i++) {
			char c = inner[i];
			if (c == '<') {
				depth++;
			} else if (c == '>') {
				depth--;
			} else if (c == ',' && depth == 0) {
				parts.Add(inner.Substring(start, i - start).Trim());
				start = i + 1;
			}
		}
		parts.Add(inner.Substring(start).Trim());
		return parts.ToArray();
	}

	#endregion

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
			ValidateJavaScriptObjectSection(content, marker, result);
		}
	}

	private static void ValidateJavaScriptObjectSection(
		string content,
		string marker,
		SchemaValidationResult result) {
		string trimmedContent = content.Trim();
		if (string.IsNullOrWhiteSpace(trimmedContent) ||
		    !trimmedContent.StartsWith("{", StringComparison.Ordinal) ||
		    !trimmedContent.EndsWith("}", StringComparison.Ordinal)) {
			result.IsValid = false;
			result.Errors.Add($"Invalid JavaScript object section in {marker}: section must remain an object literal.");
			return;
		}

		SchemaValidationResult syntaxResult = ValidateJsSyntax($"const __clioSection = {trimmedContent};");
		if (syntaxResult.IsValid) {
			return;
		}

		result.IsValid = false;
		result.Errors.Add($"Invalid JavaScript object section in {marker}: {string.Join("; ", syntaxResult.Errors)}");
	}

	private static void MergeResult(SchemaValidationResult target, SchemaValidationResult source) {
		target.Warnings.AddRange(source.Warnings);
		if (source.IsValid) {
			return;
		}

		target.IsValid = false;
		target.Errors.AddRange(source.Errors);
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
		HashSet<string> declaredAttributes = CollectDeclaredViewModelAttributes(jsBody);
		HashSet<string> attributesWrittenByHandlers = CollectAttributesWrittenByHandlers(jsBody);
		var ctx = new FieldValidationContext(
			declaredAttributes,
			modelPaths,
			explicitResources,
			attributesWrittenByHandlers,
			result);
		using (viewConfigDocument) {
			ValidateFieldComponents(viewConfigDocument.RootElement, in ctx);
		}
		if (result.Errors.Count > 0) {
			result.IsValid = false;
		}
		return result;
	}

	/// <summary>
	/// Validates that every <c>operation: "insert"</c> entry in the page body's
	/// <c>viewConfigDiff</c> that introduces a standard field component is self-consistent —
	/// i.e. the same body either declares the control's binding attribute in
	/// <c>viewModelConfigDiff</c> / <c>viewModelConfig</c>, and any label that uses
	/// <c>$Resources.Strings.X</c> is either passed in <paramref name="explicitResources"/>
	/// or is auto-provided by a DS-bound binding attribute.
	/// </summary>
	/// <remarks>
	/// This guards the common bug where an AI agent adds a control insert for a freshly created
	/// entity column without registering the matching view-model attribute and resource string.
	/// The result is a control with no data source and a blank caption. Unlike
	/// <see cref="ValidateStandardFieldBindings"/>, this validator does NOT tolerate
	/// undeclared bindings or unregistered labels for <em>insert</em> operations, because a
	/// newly-inserted control cannot legitimately inherit either piece from a parent schema —
	/// if it did, the agent would use <c>merge</c>, not <c>insert</c>.
	/// </remarks>
	public static SchemaValidationResult ValidateInsertedFieldSelfConsistency(
		string jsBody,
		IReadOnlyDictionary<string, string>? explicitResources = null) {
		var result = new SchemaValidationResult { IsValid = true };
		if (string.IsNullOrEmpty(jsBody)) {
			return result;
		}
		if (!PageSchemaSectionReader.TryRead(jsBody, out string vcdContent, SchemaViewConfigDiff, SchemaDiffMarker)) {
			return result;
		}
		if (!TryParseJsonDocument(vcdContent, out JsonDocument vcdDoc, out _)) {
			return result;
		}
		HashSet<string> declaredAttributes = CollectDeclaredViewModelAttributes(jsBody);
		HashSet<string> properlyNestedAttributes = CollectProperlyNestedViewModelAttributes(jsBody);
		Dictionary<string, string> modelPaths = CollectViewModelPaths(jsBody);
		using (vcdDoc) {
			if (vcdDoc.RootElement.ValueKind != JsonValueKind.Array) {
				return result;
			}
			foreach (JsonElement entry in vcdDoc.RootElement.EnumerateArray()) {
				ValidateInsertedFieldEntry(entry, declaredAttributes, properlyNestedAttributes, modelPaths, explicitResources, result);
			}
		}
		if (result.Errors.Count > 0) {
			result.IsValid = false;
		}
		return result;
	}

	/// <summary>
	/// Validates that user-visible text properties (<see cref="LocalizableTextProperties"/>) inside a web
	/// page body's <c>viewConfigDiff</c> are authored as localizable-string bindings rather than inline
	/// literals. Walks every <c>insert</c>/<c>merge</c> entry's <c>values</c> subtree (including nested
	/// child components) so a panel title, tab caption, or input placeholder set as a plain string is
	/// rejected regardless of nesting depth.
	/// </summary>
	/// <param name="jsBody">Raw JavaScript body of a Freedom UI page schema (marker-delimited).</param>
	/// <returns>
	/// A <see cref="SchemaValidationResult"/> that is invalid when any targeted text property carries an
	/// inline literal. Binding forms (<c>$Resources.Strings.*</c> and any other <c>$</c>-prefixed
	/// expression) and any value that references a <c>#ResourceString(Key)#</c> macro — bare,
	/// concatenated, or wrapped (e.g. <c>#MacrosTemplateString(#ResourceString(Key)#)#</c>) — are
	/// accepted; non-string and empty values are ignored.
	/// </returns>
	public static SchemaValidationResult ValidateLocalizableTextLiterals(string jsBody) {
		var result = new SchemaValidationResult { IsValid = true };
		if (string.IsNullOrEmpty(jsBody)) {
			return result;
		}
		if (!PageSchemaSectionReader.TryRead(jsBody, out string vcdContent, SchemaViewConfigDiff, SchemaDiffMarker)) {
			return result;
		}
		if (!TryParseJsonDocument(vcdContent, out JsonDocument vcdDoc, out _)) {
			return result;
		}
		using (vcdDoc) {
			ScanViewConfigDiffForTextLiterals(vcdDoc.RootElement, result);
		}
		if (result.Errors.Count > 0) {
			result.IsValid = false;
		}
		return result;
	}

	/// <summary>
	/// Mobile counterpart of <see cref="ValidateLocalizableTextLiterals"/>. Reads <c>viewConfigDiff</c>
	/// directly from the plain-JSON mobile page root instead of a marker-delimited section; the literal
	/// rule is identical.
	/// </summary>
	/// <param name="body">Plain-JSON mobile page body.</param>
	/// <returns>A <see cref="SchemaValidationResult"/> with the same contract as the web variant.</returns>
	public static SchemaValidationResult ValidateMobileLocalizableTextLiterals(string body) {
		var result = new SchemaValidationResult { IsValid = true };
		if (string.IsNullOrWhiteSpace(body)) {
			return result;
		}
		JsonDocument document;
		try {
			document = JsonDocument.Parse(body);
		} catch {
			return result;
		}
		using (document) {
			JsonElement root = document.RootElement;
			if (root.ValueKind == JsonValueKind.Object &&
			    root.TryGetProperty(ViewConfigDiffPropertyName, out JsonElement viewConfigDiff)) {
				ScanViewConfigDiffForTextLiterals(viewConfigDiff, result);
			}
		}
		if (result.Errors.Count > 0) {
			result.IsValid = false;
		}
		return result;
	}

	private static void ScanViewConfigDiffForTextLiterals(JsonElement viewConfigDiff, SchemaValidationResult result) {
		if (viewConfigDiff.ValueKind != JsonValueKind.Array) {
			return;
		}
		foreach (JsonElement entry in viewConfigDiff.EnumerateArray()) {
			if (entry.ValueKind != JsonValueKind.Object) {
				continue;
			}
			if (!entry.TryGetProperty(ValuesPropertyName, out JsonElement values) ||
			    values.ValueKind != JsonValueKind.Object) {
				continue;
			}
			string ownerName = TryGetNodeName(entry, out string entryName) ? entryName : string.Empty;
			ScanNodeForTextLiterals(values, ownerName, result);
		}
	}

	private static void ScanNodeForTextLiterals(JsonElement node, string ownerName, SchemaValidationResult result) {
		switch (node.ValueKind) {
			case JsonValueKind.Object:
				string currentName = TryGetNodeName(node, out string nodeName) ? nodeName : ownerName;
				foreach (JsonProperty property in node.EnumerateObject()) {
					if (property.Value.ValueKind == JsonValueKind.String &&
					    LocalizableTextProperties.Contains(property.Name) &&
					    IsInlineUserVisibleTextLiteral(property.Value.GetString())) {
						result.Errors.Add(BuildTextLiteralError(currentName, property.Name, property.Value.GetString()!));
					}
					ScanNodeForTextLiterals(property.Value, currentName, result);
				}
				break;
			case JsonValueKind.Array:
				foreach (JsonElement item in node.EnumerateArray()) {
					ScanNodeForTextLiterals(item, ownerName, result);
				}
				break;
		}
	}

	private static bool TryGetNodeName(JsonElement element, out string name) {
		name = string.Empty;
		if (element.TryGetProperty("name", out JsonElement nameElement) &&
		    nameElement.ValueKind == JsonValueKind.String &&
		    !string.IsNullOrWhiteSpace(nameElement.GetString())) {
			name = nameElement.GetString()!;
			return true;
		}
		return false;
	}

	private static bool IsInlineUserVisibleTextLiteral(string? value) {
		if (string.IsNullOrWhiteSpace(value)) {
			return false;
		}
		if (IsBindingExpression(value)) {
			return false;
		}
		// A value that references a #ResourceString(Key)# macro anywhere is localized — bare,
		// concatenated, or wrapped (e.g. #MacrosTemplateString(#ResourceString(Key)#)#, the dominant
		// OOTB caption form). Only a value with no resource reference at all is a hardcoded literal.
		return !ResourceStringReferencePattern.IsMatch(value);
	}

	/// <summary>
	/// Returns <c>true</c> when <paramref name="value"/> is a Freedom UI binding expression — a <c>$</c>
	/// immediately followed by an identifier character (covers <c>$Resources.Strings.*</c>, <c>$Email</c>,
	/// <c>$PageParameters_*</c>, converter pipelines, …). A bare <c>$</c> followed by a space or digit
	/// (for example a literal price placeholder "<c>$120</c>") is NOT a binding and stays subject to the
	/// inline-literal check.
	/// </summary>
	private static bool IsBindingExpression(string value) =>
		value.Length >= 2 && value[0] == '$' && (char.IsLetter(value[1]) || value[1] == '_');

	private static string BuildTextLiteralError(string ownerName, string property, string value) {
		string node = string.IsNullOrWhiteSpace(ownerName) ? "a view node" : $"'{ownerName}'";
		string shown = value.Length > 60 ? value[..60] + "…" : value;
		return $"View node {node} sets user-visible text property '{property}' to the inline literal " +
			$"\"{shown}\" instead of a localizable string. Rule: {LocalizableTextLiteralClause}. " +
			"See the page-schema-resources guide.";
	}

	private static void ValidateInsertedFieldEntry(
		JsonElement entry,
		IReadOnlySet<string> declaredAttributes,
		IReadOnlySet<string> properlyNestedAttributes,
		IReadOnlyDictionary<string, string> modelPaths,
		IReadOnlyDictionary<string, string>? explicitResources,
		SchemaValidationResult result) {
		if (!TryGetInsertedFieldDescriptor(entry, out InsertedFieldDescriptor descriptor)) {
			return;
		}
		AppendBindingDeclarationError(descriptor, declaredAttributes, properlyNestedAttributes, modelPaths, result);
		AppendLabelResourceError(descriptor, modelPaths, explicitResources, result);
	}

	private static bool TryGetInsertedFieldDescriptor(JsonElement entry, out InsertedFieldDescriptor descriptor) {
		descriptor = default;
		if (entry.ValueKind != JsonValueKind.Object) {
			return false;
		}
		if (!IsInsertOperation(entry)) {
			return false;
		}
		if (!entry.TryGetProperty(ValuesPropertyName, out JsonElement values) ||
		    values.ValueKind != JsonValueKind.Object) {
			return false;
		}
		if (!TryGetFieldType(values, out string componentType)) {
			return false;
		}
		if (!TryGetBindingAttribute(values, out _, out _, out string bindingAttribute)) {
			return false;
		}
		string fieldName = GetFieldName(entry, values);
		string displayName = !string.IsNullOrWhiteSpace(fieldName) ? fieldName : componentType;
		descriptor = new InsertedFieldDescriptor(values, displayName, componentType, bindingAttribute);
		return true;
	}

	private static bool IsInsertOperation(JsonElement entry) {
		if (!entry.TryGetProperty(OperationPropertyName, out JsonElement operation) ||
		    operation.ValueKind != JsonValueKind.String) {
			return false;
		}
		return string.Equals(operation.GetString(), "insert", StringComparison.OrdinalIgnoreCase);
	}

	private static void AppendBindingDeclarationError(
		InsertedFieldDescriptor descriptor,
		IReadOnlySet<string> declaredAttributes,
		IReadOnlySet<string> properlyNestedAttributes,
		IReadOnlyDictionary<string, string> modelPaths,
		SchemaValidationResult result) {
		string attr = descriptor.BindingAttribute;
		if (properlyNestedAttributes.Contains(attr)) {
			return;
		}
		string canonicalEntry =
			"{\"operation\":\"merge\",\"path\":[],\"values\":{\"attributes\":{\"" + attr + "\":{\"modelConfig\":{\"path\":\"<DataSource>.<Column>\"}}}}}";
		if (declaredAttributes.Contains(attr)) {
			// Attribute declared in flat form (no "path":[] + "attributes" nesting).
			// The platform save accepts it, but at runtime the attribute ends up at
			// viewModelConfig.<name> instead of viewModelConfig.attributes.<name>, which the
			// Freedom UI runtime ignores — controls render but read and write no data.
			string expectedBindingHint = string.Empty;
			if (modelPaths.TryGetValue(attr, out string modelPath)
			    && modelPath.Contains('.', StringComparison.Ordinal)) {
				string expectedBinding = BuildExpectedBinding(modelPath);
				if (!string.Equals(expectedBinding, "$" + attr, StringComparison.Ordinal)) {
					expectedBindingHint = " The expected datasource binding for '" + modelPath + "' is '" + expectedBinding + "'.";
				}
			}
			result.Errors.Add(
				"inserted field controls: field '" + descriptor.DisplayName + "' (type '" + descriptor.ComponentType + "') binds to '$" + attr + "' " +
				"which is declared in viewModelConfigDiff without the required nesting. " +
				"The attribute must be nested under values.attributes with \"path\":[] so the platform places it at " +
				"viewModelConfig.attributes." + attr + " (required for runtime data binding). " +
				"The current flat form puts the attribute at viewModelConfig." + attr + " which the runtime ignores — " +
				"the control will render but read and write no data." + expectedBindingHint + " " +
				"Use: " + canonicalEntry + ".");
			return;
		}
		result.Errors.Add(
			"inserted field controls: field '" + descriptor.DisplayName + "' (type '" + descriptor.ComponentType + "') has an undeclared attribute binding — " +
			"the body does not declare attribute '" + attr + "' in viewModelConfigDiff. " +
			"The control will have no data source. Add a viewModelConfigDiff entry such as " +
			canonicalEntry + " so the control binds to the entity column. " +
			"If the attribute is already provided by a parent schema or the current body, " +
			"use operation 'merge' for the viewConfigDiff entry instead of 'insert'. " +
			"Rule: " + InsertedFieldBindingClause + ".");
	}

	private static void AppendLabelResourceError(
		InsertedFieldDescriptor descriptor,
		IReadOnlyDictionary<string, string> modelPaths,
		IReadOnlyDictionary<string, string>? explicitResources,
		SchemaValidationResult result) {
		if (!TryGetStringProperty(descriptor.Values, LabelPropertyName, out string labelExpression) ||
		    !TryGetReactiveResourceKey(labelExpression, out string resourceKey)) {
			return;
		}
		bool hasExplicit = explicitResources != null && explicitResources.ContainsKey(resourceKey);
		bool isAutoProvided = IsAutoProvidedLabelResourceKey(resourceKey, descriptor.BindingAttribute, modelPaths);
		if (hasExplicit || isAutoProvided) {
			return;
		}
		string suggestion = BuildAutoProvideSuggestion(descriptor.BindingAttribute, modelPaths);
		result.Errors.Add(
			$"Inserted field '{descriptor.DisplayName}' has label '$Resources.Strings.{resourceKey}' but resource '{resourceKey}' " +
			$"is neither auto-provided by a DS-bound attribute nor registered in the 'resources' parameter. " +
			$"The label will render blank. {suggestion}; or register it by passing {{\"{resourceKey}\": \"<Display name>\"}} in 'resources'.");
	}

	/// <summary>
	/// Returns <c>true</c> when <paramref name="bindingAttribute"/> is a non-empty view-model
	/// attribute whose <c>modelConfig.path</c> is DS-bound (resolves to <c>DataSource.Column</c>).
	/// Single definition of "DS-bound" shared by the auto-provide gate
	/// (<see cref="IsAutoProvidedLabelResourceKey"/>), the suggestion text
	/// (<see cref="BuildAutoProvideSuggestion"/>), and the preferred-label resolver
	/// (<see cref="TryResolvePreferredLabelBinding"/>) so the rule the validator enforces and the
	/// remedy it suggests cannot disagree.
	/// </summary>
	private static bool IsDsBoundAttribute(
		string bindingAttribute,
		IReadOnlyDictionary<string, string> modelPaths) =>
		!string.IsNullOrWhiteSpace(bindingAttribute)
		&& modelPaths.TryGetValue(bindingAttribute, out string boundPath)
		&& boundPath.Contains('.', StringComparison.Ordinal);

	private static string BuildAutoProvideSuggestion(
		string bindingAttribute,
		IReadOnlyDictionary<string, string> modelPaths) {
		if (!IsDsBoundAttribute(bindingAttribute, modelPaths)) {
			return "Give the control's binding attribute a DS-bound modelConfig.path and point the label at it via '$Resources.Strings.<bindingAttribute>' so the platform auto-provides the caption";
		}
		return $"Set the label to '$Resources.Strings.{bindingAttribute}' (the control's DS-bound binding attribute) so the platform auto-provides the caption from the entity column it points to";
	}

	private readonly record struct InsertedFieldDescriptor(
		JsonElement Values,
		string DisplayName,
		string ComponentType,
		string BindingAttribute);

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

	/// <summary>
	/// Collects view-model attribute names whose <c>modelConfig.path</c> binds them to a
	/// data source column, scanning a mobile (plain JSON) page body. Used to suppress
	/// auto-derivation of <c>$Resources.Strings.Usr*</c> captions for keys that the
	/// platform already auto-provides from the entity column caption.
	/// </summary>
	/// <param name="body">Plain-JSON mobile page body.</param>
	/// <returns>
	/// A dictionary of attribute name to <c>modelConfig.path</c> value. Empty when the
	/// body is not valid JSON or contains no DS-bound attributes.
	/// </returns>
	internal static Dictionary<string, string> CollectMobileViewModelPaths(string body) {
		var modelPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (string.IsNullOrWhiteSpace(body)) {
			return modelPaths;
		}
		JsonDocument document;
		try {
			document = JsonDocument.Parse(body);
		} catch {
			return modelPaths;
		}
		using (document) {
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object) {
				return modelPaths;
			}
			CollectMobileModelPathsFromDiff(root, ViewModelConfigDiffPropertyName, modelPaths);
			CollectMobileModelPathsFromDiff(root, ModelConfigDiffPropertyName, modelPaths);
			CollectMobileModelPathsFromConfig(root, ViewModelConfigPropertyName, modelPaths);
			CollectMobileModelPathsFromConfig(root, ModelConfigPropertyName, modelPaths);
		}
		return modelPaths;
	}

	private static void CollectMobileModelPathsFromDiff(
		JsonElement root, string propertyName, Dictionary<string, string> modelPaths) {
		if (!root.TryGetProperty(propertyName, out JsonElement diff) ||
			diff.ValueKind != JsonValueKind.Array) {
			return;
		}
		foreach (JsonElement entry in diff.EnumerateArray()) {
			if (entry.ValueKind != JsonValueKind.Object) {
				continue;
			}
			if (!ShouldScanAsAttributesContainer(entry)) {
				continue;
			}
			if (!entry.TryGetProperty(ValuesPropertyName, out JsonElement values) ||
				values.ValueKind != JsonValueKind.Object) {
				continue;
			}
			CollectNamedModelPaths(values, modelPaths);
		}
	}

	private static void CollectMobileModelPathsFromConfig(
		JsonElement root, string propertyName, Dictionary<string, string> modelPaths) {
		if (!root.TryGetProperty(propertyName, out JsonElement config) ||
			config.ValueKind != JsonValueKind.Object) {
			return;
		}
		if (!config.TryGetProperty(AttributesPropertyName, out JsonElement attrs) ||
			attrs.ValueKind != JsonValueKind.Object) {
			return;
		}
		CollectNamedModelPaths(attrs, modelPaths);
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
					property.Value.TryGetProperty(ModelConfigPropertyName, out JsonElement modelConfig) &&
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

	private static HashSet<string> CollectDeclaredViewModelAttributes(string jsBody) {
		var attributeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		CollectDeclaredViewModelAttributesFromMarker(jsBody, SchemaViewModelConfig, false, attributeNames);
		CollectDeclaredViewModelAttributesFromMarker(jsBody, SchemaViewModelConfigDiff, true, attributeNames);
		return attributeNames;
	}

	/// <summary>
	/// Collects view-model attribute names that are declared in the properly-nested form:
	/// either under <c>viewModelConfig.attributes</c> (static) or inside a
	/// <c>viewModelConfigDiff</c> entry that uses <c>"path":[]</c> + <c>values.attributes</c>
	/// nesting or the older <c>"path":["attributes"]</c> form. Attributes declared in the flat
	/// form (no <c>path</c> property, attribute directly in <c>values</c>) are intentionally
	/// excluded because they land at <c>viewModelConfig.&lt;name&gt;</c> (root level) instead of
	/// <c>viewModelConfig.attributes.&lt;name&gt;</c>, which the Freedom UI runtime ignores.
	/// </summary>
	private static HashSet<string> CollectProperlyNestedViewModelAttributes(string jsBody) {
		// Case-EXACT (Ordinal), unlike declaredAttributes (OrdinalIgnoreCase): the Freedom UI
		// runtime keys attributes under their literal name, so a properly-nested 'PDS_UsrX' must
		// NOT mask a flat-form 'pds_usrx'. With OrdinalIgnoreCase the two would collide and the
		// flat-form rejection in AppendBindingDeclarationError would be wrongly suppressed.
		var names = new HashSet<string>(StringComparer.Ordinal);
		// Static viewModelConfig.attributes — always nested correctly.
		CollectDeclaredViewModelAttributesFromMarker(jsBody, SchemaViewModelConfig, false, names);
		// Diff form: only path:[] + values.attributes AND path:["attributes"] forms.
		if (!TryReadMarkerRootElement(jsBody, SchemaViewModelConfigDiff, out JsonDocument? doc)) {
			return names;
		}
		using (doc) {
			if (doc.RootElement.ValueKind != JsonValueKind.Array) {
				return names;
			}
			foreach (JsonElement op in doc.RootElement.EnumerateArray()) {
				if (TryGetDiffAttributesContainer(op, out JsonElement container, out bool isProperlyNested) &&
				    isProperlyNested) {
					foreach (JsonProperty attr in container.EnumerateObject()) {
						names.Add(attr.Name);
					}
				}
				// Flat / no-path entries are excluded intentionally (isProperlyNested = false).
			}
		}
		return names;
	}

	private static void CollectDeclaredViewModelAttributesFromMarker(
		string jsBody,
		string markerName,
		bool isArray,
		HashSet<string> attributeNames) {
		ForEachMarkerAttributesContainer(jsBody, markerName, isArray, attributes => {
			foreach (JsonProperty attribute in attributes.EnumerateObject()) {
				attributeNames.Add(attribute.Name);
			}
		});
	}

	/// <summary>Captures the shared validation context for field-component validation passes.</summary>
	private readonly record struct FieldValidationContext(
		IReadOnlySet<string> DeclaredAttributes,
		IReadOnlyDictionary<string, string> ModelPaths,
		IReadOnlyDictionary<string, string>? ExplicitResources,
		IReadOnlySet<string> AttributesWrittenByHandlers,
		SchemaValidationResult Result);

	private static void ValidateFieldComponents(
		JsonElement element,
		in FieldValidationContext ctx,
		bool checkSelf = true) {
		if (element.ValueKind == JsonValueKind.Object) {
			bool wrappedValues = false;
			if (checkSelf && TryResolveFieldComponent(element, out JsonElement componentValues, out string fieldName, out string componentType, out wrappedValues)) {
				ValidateFieldComponent(componentValues, fieldName, componentType, in ctx);
			}
			foreach (JsonProperty property in element.EnumerateObject()) {
				bool childCheckSelf = !(wrappedValues && property.NameEquals(ValuesPropertyName));
				ValidateFieldComponents(property.Value, in ctx, childCheckSelf);
			}
		} else if (element.ValueKind == JsonValueKind.Array) {
			foreach (JsonElement item in element.EnumerateArray()) {
				ValidateFieldComponents(item, in ctx);
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
		in FieldValidationContext ctx) {
		string fieldDisplayName = !string.IsNullOrWhiteSpace(fieldName) ? fieldName : componentType;
		if (!TryGetBindingAttribute(componentValues, out _, out _, out string bindingAttribute)) {
			return;
		}
		if (ctx.DeclaredAttributes.Contains(bindingAttribute) &&
		    TryFindAlternativeAttributesForSamePath(bindingAttribute, ctx.ModelPaths, ctx.AttributesWrittenByHandlers, out string handlerAttribute)) {
			ctx.Result.Errors.Add(
				$"Control '{fieldDisplayName}' binds to '${bindingAttribute}' but handlers write attribute '{handlerAttribute}' through $context.set(...). " +
				$"Bind the control to '${handlerAttribute}' or move the handler writes to '{bindingAttribute}' so the control and handler use the same declared view-model attribute.");
		}
		if (TryGetStringProperty(componentValues, LabelPropertyName, out string labelExpression) &&
		    TryGetReactiveResourceKey(labelExpression, out string resourceBindingKey) &&
		    ctx.ExplicitResources != null &&
		    !ctx.ExplicitResources.ContainsKey(resourceBindingKey) &&
		    !IsAutoProvidedLabelResourceKey(resourceBindingKey, bindingAttribute, ctx.ModelPaths)) {
			ctx.Result.Warnings.Add(
				$"Standard field '{fieldDisplayName}' has label '{labelExpression}' but resource key '{resourceBindingKey}' is neither auto-provided by a DS-bound attribute nor in the provided resources — the label will render blank. " +
				$"Rule: {InsertedFieldLabelClause}.");
		}
		if (!TryGetCaptionExpression(componentValues, out string captionExpression) ||
		    !TryGetMacroResourceKey(captionExpression, out string resourceKey) ||
		    !CustomFieldResourcePattern.IsMatch(resourceKey)) {
			return;
		}
		string preferredLabel = TryResolvePreferredLabelBinding(ctx.ModelPaths, bindingAttribute, out string preferredLabelBinding)
			? preferredLabelBinding
			: "$Resources.Strings.<DS_ColumnName>";
		if (ctx.ExplicitResources == null || !ctx.ExplicitResources.TryGetValue(resourceKey, out string explicitValue) || string.IsNullOrWhiteSpace(explicitValue)) {
			ctx.Result.Errors.Add(
				$"Standard field '{fieldDisplayName}' uses '{captionExpression}' without an explicit resources entry. Prefer auto-provided label '{preferredLabel}' for data-bound fields.");
			return;
		}
		ctx.Result.Warnings.Add(
			$"Standard field '{fieldDisplayName}' uses custom resource key '{resourceKey}'. Prefer auto-provided label '{preferredLabel}' for data-bound fields.");
	}

	/// <summary>
	/// Resolves the canonical auto-provided label binding for a DS-bound control. Used to suggest
	/// the preferred label in validator error/warning messages. The platform auto-provides the
	/// caption keyed by the VIEW-MODEL ATTRIBUTE NAME — the control's binding attribute — and
	/// resolves the caption from the column that attribute's <c>modelConfig.path</c> points to. So
	/// the suggestion is <c>$Resources.Strings.&lt;bindingAttribute&gt;</c> (for example,
	/// <c>$Resources.Strings.PDS_UsrStatus</c> for a <c>PDS_UsrStatus</c> attribute bound to
	/// <c>PDS.UsrStatus</c>). The entity column code is NOT auto-provided unless it equals the
	/// attribute name.
	/// </summary>
	private static bool TryResolvePreferredLabelBinding(
		IReadOnlyDictionary<string, string> modelPaths,
		string bindingAttribute,
		out string preferredLabelBinding) {
		preferredLabelBinding = string.Empty;
		if (!IsDsBoundAttribute(bindingAttribute, modelPaths)) {
			return false;
		}
		preferredLabelBinding = $"$Resources.Strings.{bindingAttribute}";
		return true;
	}

	/// <summary>
	/// Returns <c>true</c> when <paramref name="resourceKey"/> is the auto-provided DS caption
	/// resource for the control's binding attribute. The platform auto-provides captions keyed by
	/// the VIEW-MODEL ATTRIBUTE NAME: the label key must equal the control's binding attribute
	/// (<paramref name="bindingAttribute"/>), and that attribute must be DS-bound (its
	/// <c>modelConfig.path</c> resolves to <c>DataSource.Column</c>). The caption is then resolved
	/// from the bound column. The attribute name is arbitrary — <c>UsrStatus</c>,
	/// <c>PDS_UsrStatus</c>, <c>Name123</c> all auto-provide when the label key equals them. The
	/// entity column code (the last segment of the path) is NOT a valid key unless it happens to
	/// equal the attribute name.
	/// </summary>
	private static bool IsAutoProvidedLabelResourceKey(
		string resourceKey,
		string bindingAttribute,
		IReadOnlyDictionary<string, string> modelPaths) {
		if (string.IsNullOrWhiteSpace(resourceKey)) {
			return false;
		}
		// The platform auto-provides the caption for a DS-bound view-model attribute under a
		// resource key equal to the ATTRIBUTE NAME itself — e.g. label
		// "$Resources.Strings.AccountDS_Name_xxx" for a control bound to "$AccountDS_Name_xxx".
		// This is the only form the Freedom UI Designer emits: verified against shipped FormPage
		// schemas, the label key always equals the control's attribute name. Auto-provide is keyed
		// by the attribute name, NOT by the entity column code (the last path segment) — a bare
		// column-code label is never emitted and is not auto-provided.
		if (!string.Equals(resourceKey, bindingAttribute, StringComparison.Ordinal)) {
			return false;
		}
		return IsDsBoundAttribute(bindingAttribute, modelPaths);
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

	internal static string BuildExpectedBinding(string modelPath) {
		int dot = modelPath.IndexOf('.', StringComparison.Ordinal);
		if (dot >= 0 && string.Equals(modelPath[(dot + 1)..], "Name", StringComparison.OrdinalIgnoreCase)) {
			return "$Name";
		}
		return "$" + modelPath.Replace(".", "_", StringComparison.Ordinal);
	}

	private static bool TryGetCaptionExpression(JsonElement componentValues, out string captionExpression) {
		return TryGetStringProperty(componentValues, LabelPropertyName, out captionExpression)
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

	private static bool TryGetMacroResourceKey(string expression, out string resourceKey) {
		resourceKey = string.Empty;
		Match match = ResourceStringPattern.Match(expression);
		if (!match.Success) {
			return false;
		}
		resourceKey = match.Groups[1].Value;
		return true;
	}

	private static bool TryGetReactiveResourceKey(string expression, out string resourceKey) {
		resourceKey = string.Empty;
		if (!expression.StartsWith(ResourceBindingPrefix, StringComparison.Ordinal) ||
		    expression.Length <= ResourceBindingPrefix.Length) {
			return false;
		}
		resourceKey = expression[ResourceBindingPrefix.Length..];
		return !string.IsNullOrWhiteSpace(resourceKey);
	}

	private static bool TryFindAlternativeAttributesForSamePath(
		string bindingAttribute,
		IReadOnlyDictionary<string, string> modelPaths,
		IReadOnlySet<string> candidateAttributes,
		out string alternativeAttributeDisplay) {
		alternativeAttributeDisplay = string.Empty;
		if (string.IsNullOrWhiteSpace(bindingAttribute) ||
		    !modelPaths.TryGetValue(bindingAttribute, out string bindingPath) ||
		    string.IsNullOrWhiteSpace(bindingPath)) {
			return false;
		}

		string[] matches = candidateAttributes
			.Where(attribute => !string.Equals(attribute, bindingAttribute, StringComparison.OrdinalIgnoreCase))
			.Where(attribute => modelPaths.TryGetValue(attribute, out string candidatePath) &&
				string.Equals(candidatePath, bindingPath, StringComparison.OrdinalIgnoreCase))
			.OrderBy(attribute => attribute, StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (matches.Length == 0) {
			return false;
		}

		alternativeAttributeDisplay = matches.Length == 1
			? matches[0]
			: $"one of: {string.Join(", ", matches)}";
		return true;
	}

	/// <summary>
	/// Validates that field controls stay bound to the same declared view-model attribute
	/// that carries the <c>validators</c> object. If validators are moved to a different
	/// attribute for the same underlying model path, the control must be rebound as well.
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
		Dictionary<string, string> modelPaths = CollectViewModelPaths(jsBody);
		if (!PageSchemaSectionReader.TryRead(jsBody, out string vcdContent, SchemaViewConfigDiff, SchemaDiffMarker)) {
			return result;
		}
		if (!TryParseJsonDocument(vcdContent, out JsonDocument viewConfigDocument, out _)) {
			return result;
		}
		using (viewConfigDocument) {
			CheckValidatorControlBindings(viewConfigDocument.RootElement, attributesWithValidators, modelPaths, result);
		}
		if (result.Errors.Count > 0) {
			result.IsValid = false;
		}
		return result;
	}

	/// <summary>
	/// Validates that validator bindings are not declared directly on UI elements inside
	/// <c>viewConfigDiff</c>. Creatio evaluates validators from the view-model attribute
	/// definition, so placing a <c>validators</c> property on the control is ignored at runtime.
	/// </summary>
	public static SchemaValidationResult ValidateValidatorBindingPlacement(string jsBody) {
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
		using (viewConfigDocument) {
			CheckInlineValidatorPlacement(viewConfigDocument.RootElement, result);
		}
		if (result.Errors.Count > 0) {
			result.IsValid = false;
		}
		return result;
	}

	/// <summary>
	/// Validates the shape of every <c>validators</c> binding declared on a view-model attribute
	/// (under <c>viewModelConfig.attributes.&lt;name&gt;.validators</c> or the equivalent merge
	/// inside <c>viewModelConfigDiff</c>). Catches anti-shapes that the other validator checks
	/// silently skip because their helpers gate on the property already being a well-shaped object:
	/// <list type="bullet">
	///   <item><c>validators: [...]</c> — array instead of object map.</item>
	///   <item><c>validators: { "required": [...] }</c> — named entry as array.</item>
	///   <item><c>validators: { "required": "usr.NotEmpty" }</c> — named entry as bare string instead of <c>{ type: "usr.NotEmpty" }</c>.</item>
	///   <item>entry object missing the required string <c>type</c> property.</item>
	/// </list>
	/// </summary>
	public static SchemaValidationResult ValidateValidatorBindingShape(string jsBody) {
		var result = new SchemaValidationResult { IsValid = true };
		if (string.IsNullOrEmpty(jsBody)) {
			return result;
		}
		CheckValidatorBindingShapeInMarker(jsBody, SchemaViewModelConfig, false, result);
		CheckValidatorBindingShapeInMarker(jsBody, SchemaViewModelConfigDiff, true, result);
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
	/// Validates that standard built-in validator bindings (<c>crt.MaxLength</c>, <c>crt.MinLength</c>, etc.)
	/// use the correct parameter names and that custom validator bindings do not reference undeclared parameters.
	/// </summary>
	public static SchemaValidationResult ValidateStandardValidatorUsage(string jsBody) {
		var result = new SchemaValidationResult { IsValid = true };
		if (string.IsNullOrEmpty(jsBody)) {
			return result;
		}
		IReadOnlyDictionary<string, HashSet<string>> validatorContracts = BuildValidatorParameterContracts(jsBody);
		ValidateValidatorBindingContractsInMarker(jsBody, SchemaViewModelConfig, false, validatorContracts, result);
		ValidateValidatorBindingContractsInMarker(jsBody, SchemaViewModelConfigDiff, true, validatorContracts, result);
		if (PageSchemaSectionReader.TryRead(jsBody, out string validatorsSection, SchemaValidatorsMarker)) {
			foreach ((string validatorType, _) in ExtractCustomValidatorContracts(validatorsSection)) {
				if (validatorType.Contains("maxlength", StringComparison.OrdinalIgnoreCase)) {
					result.Errors.Add(
						$"Custom validator '{validatorType}' re-implements the built-in 'crt.MaxLength'. " +
						"Replace it with a 'crt.MaxLength' binding in viewModelConfig/viewModelConfigDiff: " +
						"{\"type\": \"crt.MaxLength\", \"params\": {\"maxLength\": <N>}}. " +
						"Read docs://mcp/guides/page-schema-validators for the canonical validator shape.");
				}
			}
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

	private const string ValidatorFactoryShapeGuidanceHint =
		"Call get-guidance with name 'page-schema-validators' for the canonical factory shape.";

	private static readonly HashSet<string> MisleadingValidatorAliases = new(StringComparer.Ordinal) {
		"validate", "validateFn", "validation", "validatorFn",
		"fn", "check", "rule", "test", "isValid"
	};

	private static readonly Regex ReturnFunctionPattern = new(
		@"return\s+(?:async\s+)?function\b|return\s+(?:async\s+)?\([^)]*\)\s*=>|return\s+(?:async\s+)?[A-Za-z_$][\w$]*\s*=>",
		RegexOptions.Compiled,
		RegexTimeout);

	/// <summary>
	/// Validates that each custom validator entry in <c>SCHEMA_VALIDATORS</c> declares the
	/// canonical factory shape: a top-level <c>validator</c> key whose value is a function
	/// that returns another function (the inner validator). Catches two common Creatio runtime
	/// traps the prior checks miss: (a) the wrong outer key (<c>validate</c>, <c>fn</c>,
	/// <c>check</c>) — the runtime ignores any key other than <c>validator</c>, so the body
	/// never executes; (b) a flat <c>function(value, config)</c> instead of a factory —
	/// the runtime calls the outer function expecting a returned validator function and silently
	/// fails when none is returned.
	/// </summary>
	public static SchemaValidationResult ValidateCustomValidatorFactoryShape(string jsBody) {
		var result = new SchemaValidationResult { IsValid = true };
		if (string.IsNullOrEmpty(jsBody)) {
			return result;
		}
		if (!PageSchemaSectionReader.TryRead(jsBody, out string validatorsContent, SchemaValidatorsMarker)) {
			return result;
		}
		string trimmed = validatorsContent.Trim();
		if (string.IsNullOrWhiteSpace(trimmed) || trimmed == "{}" || !trimmed.StartsWith("{", StringComparison.Ordinal)) {
			return result;
		}
		foreach (string validatorType in EnumerateTopLevelDeclarationKeys(trimmed)) {
			if (!validatorType.Contains('.', StringComparison.Ordinal)) {
				continue; // malformed names handled by ValidateValidatorDeclarations
			}
			if (validatorType.StartsWith("crt.", StringComparison.OrdinalIgnoreCase)) {
				continue; // OOTB validators are referenced, not declared in SCHEMA_VALIDATORS
			}
			ValidateSingleCustomValidatorShape(trimmed, validatorType, result);
		}
		if (result.Errors.Count > 0) {
			result.IsValid = false;
		}
		return result;
	}

	/// <summary>
	/// Validates that each custom converter entry in <c>SCHEMA_CONVERTERS</c> declares
	/// a callable function value. Models occasionally write an object literal, an array,
	/// or a string literal in place of a function; the runtime then silently fails to apply
	/// the converter at the binding site.
	/// </summary>
	public static SchemaValidationResult ValidateConverterFunctionShape(string jsBody) {
		var result = new SchemaValidationResult { IsValid = true };
		if (string.IsNullOrEmpty(jsBody)) {
			return result;
		}
		if (!PageSchemaSectionReader.TryRead(jsBody, out string convertersContent, SchemaConvertersMarker)) {
			return result;
		}
		string trimmed = convertersContent.Trim();
		if (string.IsNullOrWhiteSpace(trimmed) || trimmed == "{}" || !trimmed.StartsWith("{", StringComparison.Ordinal)) {
			return result;
		}
		foreach ((string converterType, string expression) in EnumerateTopLevelObjectEntries(trimmed)) {
			if (!converterType.Contains('.', StringComparison.Ordinal)) {
				continue; // malformed names handled by ValidateConverterDeclarations
			}
			if (converterType.StartsWith("crt.", StringComparison.OrdinalIgnoreCase)) {
				continue; // OOTB converters are referenced, not declared in SCHEMA_CONVERTERS
			}
			if (!IsCallableFunctionExpression(expression)) {
				result.Errors.Add(
					$"Converter '{converterType}' must be a function value but the assigned expression is not callable. " +
					$"Each entry in {SchemaConvertersMarker} must be a function expression that takes the bound attribute value and returns the transformed display value. " +
					"Call get-guidance with name 'page-schema-converters' for the canonical converter shape.");
			}
		}
		if (result.Errors.Count > 0) {
			result.IsValid = false;
		}
		return result;
	}

	private static void ValidateSingleCustomValidatorShape(
		string validatorsContent, string validatorType, SchemaValidationResult result) {
		string entryBody = ExtractValidatorBody(validatorsContent, validatorType);
		if (string.IsNullOrEmpty(entryBody)) {
			return;
		}
		int braceStart = entryBody.IndexOf('{');
		if (braceStart < 0) {
			return;
		}
		string innerObject = entryBody.Substring(braceStart);

		HashSet<string> topLevelKeys = new(StringComparer.Ordinal);
		foreach ((string key, _) in EnumerateTopLevelObjectEntries(innerObject)) {
			topLevelKeys.Add(key);
		}
		if (!topLevelKeys.Contains("validator")) {
			string? alias = topLevelKeys.FirstOrDefault(k => MisleadingValidatorAliases.Contains(k));
			if (alias != null) {
				result.Errors.Add(
					$"Validator '{validatorType}' uses key '{alias}' instead of 'validator'. " +
					$"The Creatio runtime looks up the 'validator' key on each entry in {SchemaValidatorsMarker} and ignores any other key, so this validator never executes. " +
					$"Rename '{alias}' to 'validator' and structure its value as a factory: an outer function that returns the inner validator function. " +
					ValidatorFactoryShapeGuidanceHint);
				return;
			}
			result.Errors.Add(
				$"Validator '{validatorType}' is missing the required 'validator' key. " +
				$"Each entry in {SchemaValidatorsMarker} must declare 'validator' as a factory function that returns the inner validator. " +
				ValidatorFactoryShapeGuidanceHint);
			return;
		}

		if (!TryGetTopLevelObjectPropertyValue(innerObject, "validator", out string validatorExpression)) {
			result.Errors.Add(
				$"Validator '{validatorType}' has a 'validator' key, but its value could not be parsed as a JavaScript expression. " +
				ValidatorFactoryShapeGuidanceHint);
			return;
		}

		if (!IsCallableFunctionExpression(validatorExpression)) {
			result.Errors.Add(
				$"Validator '{validatorType}' has a 'validator' key whose value is not a function. " +
				"It must be a factory function — an outer function that returns an inner validator function. " +
				ValidatorFactoryShapeGuidanceHint);
			return;
		}

		if (!LooksLikeFactoryShape(validatorExpression)) {
			result.Errors.Add(
				$"Validator '{validatorType}' uses a flat 'validator' function instead of the required factory shape. " +
				"The outer function must return another function (the actual validator that receives a control). " +
				"Wrap the existing logic so the outer function takes only the config parameter and returns an inner function that performs the check via control.value. " +
				ValidatorFactoryShapeGuidanceHint);
		}
	}

	private static bool TryGetTopLevelObjectPropertyValue(string objectContent, string propertyName, out string expression) {
		foreach ((string key, string valueExpression) in EnumerateTopLevelObjectEntries(objectContent)) {
			if (string.Equals(key, propertyName, StringComparison.Ordinal)) {
				expression = valueExpression;
				return !string.IsNullOrWhiteSpace(expression);
			}
		}
		expression = string.Empty;
		return false;
	}

	private static IEnumerable<(string Key, string ValueExpression)> EnumerateTopLevelObjectEntries(string objectContent) {
		if (string.IsNullOrEmpty(objectContent) || objectContent[0] != '{') {
			yield break;
		}
		int depth = 0;
		bool inString = false;
		char stringChar = '"';
		int index = 0;
		while (index < objectContent.Length) {
			if (JsParserHelper.TryConsumeStringLiteralCharacter(objectContent, ref index, ref inString, stringChar)) {
				continue;
			}
			if (JsParserHelper.TrySkipComment(objectContent, index, out int afterComment)) {
				index = afterComment;
				continue;
			}
			char current = objectContent[index];
			if (depth == 1 &&
				TryReadDeclarationEntry(objectContent, index, current, out string key, out int valueStart, out int afterEntry)) {
				string raw = objectContent.Substring(valueStart, afterEntry - valueStart);
				yield return (key, raw.TrimEnd(',').Trim());
				index = afterEntry;
				continue;
			}
			if (JsParserHelper.TryHandleStructuralCharacter(current, ref index, ref depth, ref inString, ref stringChar)) {
				continue;
			}
			index++;
		}
	}

	private static bool IsCallableFunctionExpression(string expression) {
		string trimmed = TrimAndStripAsync(expression);
		if (string.IsNullOrEmpty(trimmed)) {
			return false;
		}
		return trimmed.StartsWith("function", StringComparison.Ordinal)
			|| LooksLikeMethodShorthandValue(trimmed)
			|| JsParserHelper.FindFirstTopLevelArrow(trimmed) >= 0;
	}

	private static bool LooksLikeFactoryShape(string expression) {
		string trimmed = TrimAndStripAsync(expression);
		if (string.IsNullOrEmpty(trimmed)) {
			return false;
		}
		if (trimmed.StartsWith("function", StringComparison.Ordinal) || LooksLikeMethodShorthandValue(trimmed)) {
			return ReturnFunctionPattern.IsMatch(JsParserHelper.StripStringsAndComments(trimmed));
		}
		int arrowIndex = JsParserHelper.FindFirstTopLevelArrow(trimmed);
		if (arrowIndex < 0) {
			return false;
		}
		string afterArrow = trimmed.Substring(arrowIndex + 2).TrimStart();
		if (string.IsNullOrEmpty(afterArrow)) {
			return false;
		}
		if (afterArrow[0] == '{') {
			return ReturnFunctionPattern.IsMatch(JsParserHelper.StripStringsAndComments(afterArrow));
		}
		string body = TrimAndStripAsync(afterArrow);
		return body.StartsWith("function", StringComparison.Ordinal)
			|| JsParserHelper.FindFirstTopLevelArrow(body) >= 0;
	}

	private static string TrimAndStripAsync(string expression) {
		string trimmed = expression.Trim().TrimEnd(',').Trim();
		if (trimmed.StartsWith("async ", StringComparison.Ordinal)) {
			trimmed = trimmed["async ".Length..].TrimStart();
		}
		return trimmed;
	}

	/// <summary>
	/// Returns <see langword="true"/> when the expression begins with a method-shorthand
	/// value: a balanced parameter list directly followed by a brace-delimited body
	/// (e.g. <c>(config) { return ...; }</c>). Used to recognise <c>{ validator(config){...} }</c>
	/// shorthand entries that omit the explicit <c>function</c> keyword.
	/// </summary>
	private static bool LooksLikeMethodShorthandValue(string expression) {
		if (string.IsNullOrEmpty(expression) || expression[0] != '(') {
			return false;
		}
		int parenDepth = 0;
		bool inString = false;
		char stringChar = '"';
		int index = 0;
		while (index < expression.Length) {
			if (JsParserHelper.TryConsumeStringLiteralCharacter(expression, ref index, ref inString, stringChar)) {
				continue;
			}
			char current = expression[index];
			if (current is '"' or '\'' or '`') {
				inString = true;
				stringChar = current;
				index++;
				continue;
			}
			if (current == '(') {
				parenDepth++;
			} else if (current == ')') {
				parenDepth--;
				if (parenDepth == 0) {
					int afterParen = JsParserHelper.SkipWhitespace(expression, index + 1);
					return afterParen < expression.Length && expression[afterParen] == '{';
				}
			}
			index++;
		}
		return false;
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
		if (!TryExtractReturnedErrorObject(snippet, validatorType, out string errorObject)) {
			return props;
		}

		return ExtractTopLevelObjectPropertyNames(errorObject);
	}

	private static IReadOnlyDictionary<string, HashSet<string>> BuildValidatorParameterContracts(string jsBody) {
		var contracts = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
		foreach ((string validatorType, string[] paramNames) in StandardValidatorContracts.GetContracts()) {
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
			// Guard ValueKind before TryGetProperty: System.Text.Json THROWS
			// InvalidOperationException (it does not return false) when the element is not an
			// object. A SCHEMA_VIEW_MODEL_CONFIG block that parses as [] / null / a string / a
			// number would otherwise crash validation instead of being skipped cleanly.
			if (root.ValueKind == JsonValueKind.Object &&
			    root.TryGetProperty(AttributesPropertyName, out JsonElement attributes)) {
				yield return attributes;
			}
			yield break;
		}

		if (root.ValueKind != JsonValueKind.Array) {
			yield break;
		}

		foreach (JsonElement op in root.EnumerateArray()) {
			if (TryGetDiffAttributesContainer(op, out JsonElement container, out _)) {
				yield return container;
			}
		}
	}

	private static bool ShouldScanAsAttributesContainer(JsonElement operation) {
		if (!operation.TryGetProperty("path", out JsonElement pathElement)) {
			return true;
		}

		if (pathElement.ValueKind != JsonValueKind.Array) {
			return false;
		}

		using JsonElement.ArrayEnumerator segments = pathElement.EnumerateArray();
		if (!segments.MoveNext() ||
		    segments.Current.ValueKind != JsonValueKind.String ||
		    !string.Equals(segments.Current.GetString(), AttributesPropertyName, StringComparison.OrdinalIgnoreCase)) {
			return false;
		}
		// Require EXACTLY one segment. A multi-segment path like ["attributes","UsrX"] targets a
		// sub-property of a specific attribute, so `values` is that attribute's BODY
		// (modelConfig, validators, ...), NOT a map of attribute names. Treating it as an
		// attributes container would collect body keys as attribute names and miss the real one.
		return !segments.MoveNext();
	}

	/// <summary>
	/// Returns <c>true</c> when <paramref name="operation"/> targets the root of the view-model
	/// config — i.e. its <c>path</c> property is an empty JSON array. This is the form emitted
	/// by the Freedom UI Designer for <c>viewModelConfigDiff</c> entries where attribute
	/// declarations are nested under <c>values.attributes</c>.
	/// </summary>
	private static bool IsRootPathOperation(JsonElement operation) {
		if (!operation.TryGetProperty("path", out JsonElement pathElement) ||
		    pathElement.ValueKind != JsonValueKind.Array) {
			return false;
		}
		using JsonElement.ArrayEnumerator segments = pathElement.EnumerateArray();
		return !segments.MoveNext();
	}

	/// <summary>
	/// Single shared classifier for a <c>viewModelConfigDiff</c> array entry. Resolves the JSON
	/// element that acts as the attributes container for the entry and indicates whether that
	/// container is in a properly-nested form (attributes land at
	/// <c>viewModelConfig.attributes.{name}</c> at runtime) or the flat / no-path form
	/// (attributes land at <c>viewModelConfig.{name}</c>, silently accepted on save but ignored
	/// at runtime).
	/// </summary>
	/// <param name="op">A single element from the <c>viewModelConfigDiff</c> array.</param>
	/// <param name="container">Receives the resolved attributes container when the method returns <c>true</c>.</param>
	/// <param name="isProperlyNested">
	/// <c>true</c> for <c>path:["attributes"]</c> and <c>path:[]</c> + <c>values.attributes</c> entries
	/// (attributes reach <c>viewModelConfig.attributes</c>).
	/// <c>false</c> for no-path flat entries (attributes land at <c>viewModelConfig</c> root).
	/// </param>
	/// <returns><c>true</c> when <paramref name="container"/> was resolved; otherwise <c>false</c>.</returns>
	private static bool TryGetDiffAttributesContainer(
		JsonElement op,
		out JsonElement container,
		out bool isProperlyNested) {
		container = default;
		isProperlyNested = false;
		if (op.ValueKind != JsonValueKind.Object) {
			return false;
		}
		// A remove operation deletes an attribute rather than declaring one — its values must
		// not be collected as declared/properly-nested attribute names (that would make a binding
		// to a just-removed attribute appear valid).
		if (op.TryGetProperty(OperationPropertyName, out JsonElement opKind) &&
		    opKind.ValueKind == JsonValueKind.String &&
		    string.Equals(opKind.GetString(), "remove", StringComparison.OrdinalIgnoreCase)) {
			return false;
		}
		if (!op.TryGetProperty(ValuesPropertyName, out JsonElement values) ||
		    values.ValueKind != JsonValueKind.Object) {
			return false;
		}
		if (IsRootPathOperation(op)) {
			if (values.TryGetProperty(AttributesPropertyName, out JsonElement nestedAttrs) &&
			    nestedAttrs.ValueKind == JsonValueKind.Object) {
				container = nestedAttrs;
				isProperlyNested = true;
				return true;
			}
			// path:[] but no attributes key — not a valid attributes container.
			return false;
		}
		if (ShouldScanAsAttributesContainer(op)) {
			container = values;
			// A present path property means the legacy single-segment attributes form, which the
			// runtime treats as properly nested. With no path property it is the flat form, whose
			// attribute names land at the viewModelConfig root and are ignored by the runtime.
			isProperlyNested = op.TryGetProperty("path", out _);
			return true;
		}
		return false;
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

	private static void CheckValidatorBindingShapeInMarker(
		string jsBody, string markerName, bool isArray, SchemaValidationResult result) {
		ForEachMarkerAttributesContainer(
			jsBody,
			markerName,
			isArray,
			attributes => ScanAttributesForValidatorBindingShape(attributes, result));
	}

	private static void ScanAttributesForValidatorBindingShape(
		JsonElement attributesElement, SchemaValidationResult result) {
		if (attributesElement.ValueKind != JsonValueKind.Object) {
			return;
		}
		foreach (JsonProperty attr in attributesElement.EnumerateObject()) {
			CheckSingleAttributeValidatorBindingShape(attr, result);
		}
	}

	private static void CheckSingleAttributeValidatorBindingShape(
		JsonProperty attr, SchemaValidationResult result) {
		if (attr.Value.ValueKind != JsonValueKind.Object ||
			!attr.Value.TryGetProperty(ValidatorsPropertyName, out JsonElement validators)) {
			return;
		}
		if (validators.ValueKind != JsonValueKind.Object) {
			result.Errors.Add(
				$"Attribute '{attr.Name}' has 'validators' declared as {DescribeJsonKindForValidatorError(validators.ValueKind)}; declare it as an object map keyed by validator name, e.g. \"validators\": {{ \"required\": {{ \"type\": \"usr.NotEmpty\" }} }}.");
			return;
		}
		foreach (JsonProperty entry in validators.EnumerateObject()) {
			CheckSingleValidatorEntryShape(attr.Name, entry, result);
		}
	}

	private static void CheckSingleValidatorEntryShape(
		string attributeName, JsonProperty entry, SchemaValidationResult result) {
		if (entry.Value.ValueKind != JsonValueKind.Object) {
			result.Errors.Add(
				$"Attribute '{attributeName}' validator '{entry.Name}' is declared as {DescribeJsonKindForValidatorError(entry.Value.ValueKind)}; each named validator entry must be an object such as {{ \"type\": \"usr.NotEmpty\" }}.");
			return;
		}
		if (!entry.Value.TryGetProperty(TypePropertyName, out JsonElement typeEl) ||
			typeEl.ValueKind != JsonValueKind.String ||
			string.IsNullOrWhiteSpace(typeEl.GetString())) {
			result.Errors.Add(
				$"Attribute '{attributeName}' validator '{entry.Name}' is missing a non-empty string 'type' property; declare it as {{ \"type\": \"<ValidatorType>\" }} pointing at a SCHEMA_VALIDATORS entry.");
		}
	}

	private static string DescribeJsonKindForValidatorError(JsonValueKind kind) =>
		kind switch {
			JsonValueKind.Array => "an array",
			JsonValueKind.String => "a string",
			JsonValueKind.Number => "a number",
			JsonValueKind.True or JsonValueKind.False => "a boolean",
			JsonValueKind.Null => "null",
			_ => "a non-object value"
		};

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
				index = JsParserHelper.ConsumeStringLiteralCharacter(validatorsContent, index, ref inString, stringChar);
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

	private static bool TryExtractReturnedErrorObject(string content, string validatorType, out string objectContent) {
		string typeEscaped = Regex.Escape(validatorType);
		Match match = Regex.Match(
			content,
			"return\\s*\\{\\s*\"" + typeEscaped + "\"\\s*:\\s*\\{",
			RegexOptions.Singleline,
			RegexTimeout);
		if (!match.Success) {
			objectContent = string.Empty;
			return false;
		}

		int braceStart = match.Index + match.Length - 1;
		return JsParserHelper.TryExtractBalancedJavaScriptObject(content, braceStart, out objectContent, out _);
	}

	private static HashSet<string> ExtractTopLevelObjectPropertyNames(string objectContent) {
		var props = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (string.IsNullOrWhiteSpace(objectContent) || objectContent[0] != '{') {
			return props;
		}

		int depth = 0;
		bool inString = false;
		char stringChar = '"';
		int index = 0;
		while (index < objectContent.Length) {
			if (JsParserHelper.TryConsumeStringLiteralCharacter(objectContent, ref index, ref inString, stringChar)) {
				continue;
			}

			char current = objectContent[index];
			if (TryReadTopLevelPropertyName(objectContent, depth, current, ref index, props)) {
				continue;
			}

			if (JsParserHelper.TryHandleStructuralCharacter(current, ref index, ref depth, ref inString, ref stringChar)) {
				continue;
			}

			index++;
		}

		return props;
	}


	private static bool TryReadTopLevelPropertyName(
		string content,
		int depth,
		char current,
		ref int index,
		HashSet<string> props) {
		if (current is '"' or '\'' or '`' &&
		    depth == 1 &&
		    JsParserHelper.TryReadQuotedPropertyName(content, index, current, out string propertyName, out int nextIndex)) {
			props.Add(propertyName);
			index = nextIndex;
			return true;
		}

		if (depth == 1 &&
		    JsParserHelper.IsIdentifierStart(current) &&
		    TryReadIdentifierPropertyName(content, index, out string identifierName, out int identifierNextIndex)) {
			props.Add(identifierName);
			index = identifierNextIndex;
			return true;
		}

		return false;
	}


	private static bool TryReadIdentifierPropertyName(
		string content,
		int startIndex,
		out string propertyName,
		out int nextIndex) {
		propertyName = string.Empty;
		nextIndex = startIndex;
		int index = startIndex + 1;
		while (index < content.Length && JsParserHelper.IsIdentifierPart(content[index])) {
			index++;
		}

		int colonIndex = JsParserHelper.SkipWhitespace(content, index);
		if (colonIndex >= content.Length || content[colonIndex] != ':') {
			return false;
		}

		propertyName = content.Substring(startIndex, index - startIndex);
		nextIndex = colonIndex + 1;
		return true;
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
			"(?:\"params\"|params)\\s*:\\s*\\[(?<params>.*?)\\]",
			RegexOptions.Singleline,
			RegexTimeout);
		if (!paramsMatch.Success) {
			return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		}

		return Regex.Matches(
				paramsMatch.Groups["params"].Value,
				"(?:\"name\"|name)\\s*:\\s*\"(?<name>[^\"]+)\"",
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

	/// <summary>
	/// Collects attribute names written through direct <c>$context.set(...)</c> calls inside handlers.
	/// </summary>
	/// <remarks>
	/// This helper intentionally uses a narrow regex heuristic that only recognizes direct
	/// <c>request.$context.set("Attr", ...)</c> and <c>$context.set("Attr", ...)</c> forms.
	/// It does not resolve aliases, destructured variables, or chained expressions, so false
	/// negatives are possible. The collected names are used only to enrich diagnostics when a
	/// control and a handler appear to target different declared attributes for the same model path.
	/// </remarks>
	private static HashSet<string> CollectAttributesWrittenByHandlers(string jsBody) {
		var attributeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (!PageSchemaSectionReader.TryRead(jsBody, out string handlersContent, SchemaHandlersMarker)) {
			return attributeNames;
		}

		foreach (Match match in Regex.Matches(
			         handlersContent,
			         @"(?:request\.\$context|\$context)\.set\s*\(\s*[""'](?<name>[A-Za-z_][A-Za-z0-9_]*)[""']",
			         RegexOptions.Singleline,
			         RegexTimeout)) {
			string attributeName = match.Groups["name"].Value;
			if (!string.IsNullOrWhiteSpace(attributeName)) {
				attributeNames.Add(attributeName);
			}
		}

		return attributeNames;
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
		IReadOnlyDictionary<string, string> modelPaths,
		SchemaValidationResult result) {
		if (element.ValueKind == JsonValueKind.Array) {
			foreach (JsonElement item in element.EnumerateArray()) {
				CheckValidatorControlBindings(item, attributesWithValidators, modelPaths, result);
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
		AddValidatorControlBindingErrorIfNeeded(element, target, attributesWithValidators, modelPaths, result);
		foreach (JsonProperty property in element.EnumerateObject()
		             .Where(property => !property.NameEquals(ValuesPropertyName))) {
			CheckValidatorControlBindings(property.Value, attributesWithValidators, modelPaths, result);
		}
	}

	private static void CheckInlineValidatorPlacement(
		JsonElement element,
		SchemaValidationResult result,
		bool checkSelf = true) {
		if (element.ValueKind == JsonValueKind.Array) {
			foreach (JsonElement item in element.EnumerateArray()) {
				CheckInlineValidatorPlacement(item, result);
			}
			return;
		}
		if (element.ValueKind != JsonValueKind.Object) {
			return;
		}
		bool wrappedValues = false;
		if (checkSelf &&
		    TryResolveFieldComponent(element, out JsonElement componentValues, out string fieldName, out string componentType, out wrappedValues)) {
			AddInlineValidatorPlacementErrorIfNeeded(componentValues, fieldName, componentType, result);
		}
		foreach (JsonProperty property in element.EnumerateObject()) {
			bool childCheckSelf = !(wrappedValues && property.NameEquals(ValuesPropertyName));
			CheckInlineValidatorPlacement(property.Value, result, childCheckSelf);
		}
	}

	private static void AddInlineValidatorPlacementErrorIfNeeded(
		JsonElement target,
		string fieldName,
		string componentType,
		SchemaValidationResult result) {
		if (!target.TryGetProperty(ValidatorsPropertyName, out _)) {
			return;
		}

		string fieldDisplayName = !string.IsNullOrWhiteSpace(fieldName) ? fieldName : componentType;
		string bindingHint = TryGetBindingAttribute(target, out _, out _, out string bindingAttribute)
			? $"Declare validators on attribute '{bindingAttribute}' in viewModelConfig/viewModelConfigDiff and keep the control bound to '${bindingAttribute}'."
			: "Declare validators on the bound attribute in viewModelConfig/viewModelConfigDiff, not on the UI element.";
		result.Errors.Add(
			$"Control '{fieldDisplayName}' declares 'validators' inside viewConfigDiff. Validators must be declared on the view-model attribute in viewModelConfig/viewModelConfigDiff, not on the UI element. {bindingHint}");
	}

	private static string? FindViewModelAttributeForDirectBinding(
		string directBindingAttr,
		IReadOnlyDictionary<string, string> modelPaths) {
		foreach (var (attrName, path) in modelPaths) {
			if (string.Equals("$" + directBindingAttr, BuildExpectedBinding(path), StringComparison.OrdinalIgnoreCase)) {
				return attrName;
			}
		}
		return null;
	}

	private static void AddValidatorControlBindingErrorIfNeeded(
		JsonElement element,
		JsonElement target,
		HashSet<string> attributesWithValidators,
		IReadOnlyDictionary<string, string> modelPaths,
		SchemaValidationResult result) {
		if (!TryGetBindingAttribute(target, out _, out _, out string bindingAttribute)) {
			return;
		}
		string fieldName = element.TryGetProperty("name", out JsonElement nameEl) &&
		                   nameEl.ValueKind == JsonValueKind.String &&
		                   !string.IsNullOrWhiteSpace(nameEl.GetString())
			? nameEl.GetString() ?? bindingAttribute
			: bindingAttribute;
		if (modelPaths.ContainsKey(bindingAttribute)) {
			if (!TryFindAlternativeAttributesForSamePath(bindingAttribute, modelPaths, attributesWithValidators, out string validatorAttribute)) {
				return;
			}
			result.Errors.Add(
				$"Control '{fieldName}' binds to '${bindingAttribute}' but validators are declared on attribute '{validatorAttribute}'. " +
				$"Bind the control to '${validatorAttribute}' or move the validators to '{bindingAttribute}' so the control and validators use the same declared view-model attribute.");
			return;
		}
		string? vmAttribute = FindViewModelAttributeForDirectBinding(bindingAttribute, modelPaths);
		if (vmAttribute == null || !attributesWithValidators.Contains(vmAttribute)) {
			return;
		}
		result.Errors.Add(
			$"Control '{fieldName}' binds to '${bindingAttribute}' but attribute " +
			$"'{vmAttribute}' has validators. Validators only fire on view-model attribute " +
			$"bindings — use '${vmAttribute}' instead of '${bindingAttribute}'.");
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
		if (string.IsNullOrEmpty(candidate) || !candidate.Contains('_')) {
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
			if (element.TryGetProperty(ModelConfigPropertyName, out var mc) &&
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

	internal static readonly Regex VendorPrefixedNamePattern = new(
		@"^[A-Za-z_][A-Za-z0-9_]*\.[A-Za-z_][A-Za-z0-9_]*$",
		RegexOptions.Compiled,
		RegexTimeout);

	/// <summary>
	/// Validates that all keys in <c>SCHEMA_CONVERTERS</c> follow the required
	/// <c>VendorPrefix.ConverterName</c> format. A malformed key causes a Creatio runtime error:
	/// "Error when register X. Type property should have format VendorPrefix.ConverterTypeName".
	/// </summary>
	public static SchemaValidationResult ValidateConverterDeclarations(string jsBody) =>
		ValidatePrefixedDeclarations(jsBody, SchemaConvertersMarker, "Converter", "page-schema-converters");

	/// <summary>
	/// Validates that all keys in <c>SCHEMA_VALIDATORS</c> follow the required
	/// <c>VendorPrefix.ValidatorName</c> format. A malformed key causes a Creatio runtime error:
	/// "Error when register X. Type property should have format VendorPrefix.ValidatorTypeName".
	/// </summary>
	public static SchemaValidationResult ValidateValidatorDeclarations(string jsBody) =>
		ValidatePrefixedDeclarations(jsBody, SchemaValidatorsMarker, "Validator", "page-schema-validators");

	private static SchemaValidationResult ValidatePrefixedDeclarations(
		string jsBody, string markerName, string elementType, string guidanceName) {
		var result = new SchemaValidationResult { IsValid = true };
		if (string.IsNullOrEmpty(jsBody)) {
			return result;
		}
		if (!PageSchemaSectionReader.TryRead(jsBody, out string sectionContent, markerName)) {
			return result;
		}
		string trimmed = sectionContent.Trim();
		if (string.IsNullOrWhiteSpace(trimmed) || trimmed == "{}" || !trimmed.StartsWith("{", StringComparison.Ordinal)) {
			return result;
		}
		foreach (string key in EnumerateTopLevelDeclarationKeys(trimmed)) {
			if (VendorPrefixedNamePattern.IsMatch(key)) {
				continue;
			}
			result.IsValid = false;
			result.Errors.Add(
				$"{elementType} '{key}' in {markerName} does not follow the required 'VendorPrefix.{elementType}Name' format. " +
				$"Rename it to '<vendor>.{key}' (for example 'usr.{key}') — a malformed key causes a Creatio runtime error: " +
				$"\"Error when register {key}. Type property should have format \\\"VendorPrefix.{elementType}TypeName\\\"\". " +
				$"Call get-guidance with name '{guidanceName}' for the correct format.");
		}
		return result;
	}

	private static List<string> EnumerateTopLevelDeclarationKeys(string objectContent) =>
		EnumerateTopLevelObjectEntries(objectContent).Select(entry => entry.Key).ToList();

	private static bool TryReadDeclarationEntry(
		string content, int startIndex, char current,
		out string keyName, out int valueStartIndex, out int afterEntryIndex) {
		keyName = string.Empty;
		valueStartIndex = startIndex;
		afterEntryIndex = startIndex;
		int afterName;
		string name;
		if (current == '"' || current == '\'') {
			if (!TryReadQuotedDeclarationKey(content, startIndex, current, out name, out afterName)) {
				return false;
			}
		} else if (JsParserHelper.IsIdentifierStart(current)) {
			ReadIdentifierDeclarationKey(content, startIndex, out name, out afterName);
		} else {
			return false;
		}
		int afterWhitespace = JsParserHelper.SkipWhitespace(content, afterName);
		if (afterWhitespace >= content.Length) {
			return false;
		}
		char delimiter = content[afterWhitespace];
		if (delimiter != ':' && delimiter != '(') {
			return false;
		}
		int valueStart = delimiter == ':'
			? JsParserHelper.SkipWhitespace(content, afterWhitespace + 1)
			: afterWhitespace;
		SkipBalancedDeclarationValue(content, valueStart, out int afterValue);
		keyName = name;
		valueStartIndex = valueStart;
		afterEntryIndex = afterValue;
		return true;
	}

	private static bool TryReadQuotedDeclarationKey(
		string content, int startIndex, char quote,
		out string keyName, out int afterNameIndex) {
		keyName = string.Empty;
		afterNameIndex = startIndex;
		int endQuoteIndex = startIndex + 1;
		while (endQuoteIndex < content.Length) {
			if (content[endQuoteIndex] == '\\') {
				endQuoteIndex += endQuoteIndex + 1 < content.Length ? 2 : 1;
				continue;
			}
			if (content[endQuoteIndex] == quote) {
				break;
			}
			endQuoteIndex++;
		}
		if (endQuoteIndex >= content.Length || content[endQuoteIndex] != quote) {
			return false;
		}
		keyName = content.Substring(startIndex + 1, endQuoteIndex - startIndex - 1);
		afterNameIndex = endQuoteIndex + 1;
		return true;
	}

	private static void ReadIdentifierDeclarationKey(
		string content, int startIndex,
		out string keyName, out int afterNameIndex) {
		int index = startIndex + 1;
		while (index < content.Length && JsParserHelper.IsIdentifierPart(content[index])) {
			index++;
		}
		keyName = content.Substring(startIndex, index - startIndex);
		afterNameIndex = index;
	}

	private static void SkipBalancedDeclarationValue(string content, int valueStart, out int afterValueIndex) {
		int braceDepth = 0;
		int bracketDepth = 0;
		int parenDepth = 0;
		bool inString = false;
		char stringChar = '"';
		int index = valueStart;
		while (index < content.Length) {
			if (JsParserHelper.TryConsumeStringLiteralCharacter(content, ref index, ref inString, stringChar)) {
				continue;
			}
			if (JsParserHelper.TrySkipComment(content, index, out int afterComment)) {
				index = afterComment;
				continue;
			}
			char current = content[index];
			bool atTopLevel = braceDepth == 0 && bracketDepth == 0 && parenDepth == 0;
			if (atTopLevel && (current == ',' || current == '}')) {
				afterValueIndex = index;
				return;
			}
			if (current is '"' or '\'' or '`') {
				inString = true;
				stringChar = current;
				index++;
				continue;
			}
			switch (current) {
				case '{': braceDepth++; break;
				case '}': braceDepth--; break;
				case '[': bracketDepth++; break;
				case ']': bracketDepth--; break;
				case '(': parenDepth++; break;
				case ')': parenDepth--; break;
			}
			index++;
		}
		afterValueIndex = index;
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
				return JsParserHelper.SkipLineComment(jsBody, i, length);
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
