namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Clio.Command.Localization;
using Clio.Common;
using CommandLine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Options for the <c>localize-page</c> command.
/// </summary>
[Verb("localize-page", HelpText = "Add or update translations of a Freedom UI page's captions in one culture")]
public sealed class LocalizePageOptions : EnvironmentOptions {

	/// <summary>
	/// Gets or sets the page schema name.
	/// </summary>
	[Option("schema-name", Required = true, HelpText = "Page schema name")]
	public string SchemaName { get; set; }

	/// <summary>
	/// Gets or sets the target culture, for example <c>es-ES</c>. Matched case-insensitively against the
	/// environment's <c>SysCulture</c> names.
	/// </summary>
	[Option("culture", Required = true, HelpText = "Target culture, for example es-ES")]
	public string Culture { get; set; }

	/// <summary>
	/// Gets or sets a JSON object that maps existing resource keys to their value in <see cref="Culture"/>.
	/// </summary>
	[Option("resources", Required = false,
		HelpText = "JSON object mapping existing resource keys to their value in the target culture")]
	public string Resources { get; set; }

	/// <summary>
	/// Gets or sets the page title in <see cref="Culture"/>.
	/// </summary>
	[Option("caption", Required = false, HelpText = "Page title in the target culture")]
	public string Caption { get; set; }

	/// <summary>
	/// Gets or sets the directory that anchors the <c>.clio-pages</c> tree, the same value passed to
	/// <c>get-page --output-directory</c>, so the page baseline it wrote is found and refreshed after a save.
	/// </summary>
	[Option("output-directory", Required = false,
		HelpText = "Directory that anchors the .clio-pages baseline lookup; pass the value given to get-page --output-directory. Does not change where the page is saved")]
	public string OutputDirectory { get; set; }
}

/// <summary>
/// Coverage of a page's captions in one culture.
/// </summary>
public sealed record LocalizePageCoverage {

	/// <summary>Gets the number of keys in the page's <c>localizableStrings</c> (own and inherited).</summary>
	[DataMember(Name = "keys")]
	[JsonProperty("keys")]
	[JsonPropertyName("keys")]
	public int Keys { get; init; }

	/// <summary>Gets the number of keys that have a value in the culture.</summary>
	[DataMember(Name = "translated")]
	[JsonProperty("translated")]
	[JsonPropertyName("translated")]
	public int Translated { get; init; }

	/// <summary>Gets the keys that have no value in the culture.</summary>
	[DataMember(Name = "missing")]
	[JsonProperty("missing")]
	[JsonPropertyName("missing")]
	public IReadOnlyList<string> Missing { get; init; } = [];

	/// <summary>Gets the keys whose value in the culture equals the <c>en-US</c> value.</summary>
	[DataMember(Name = "sameAsDefault")]
	[JsonProperty("sameAsDefault")]
	[JsonPropertyName("sameAsDefault")]
	public IReadOnlyList<string> SameAsDefault { get; init; } = [];

	/// <summary>Gets a value indicating whether the page title in the culture equals the <c>en-US</c> title.</summary>
	[DataMember(Name = "captionSameAsDefault")]
	[JsonProperty("captionSameAsDefault")]
	[JsonPropertyName("captionSameAsDefault")]
	public bool CaptionSameAsDefault { get; init; }
}

/// <summary>
/// Result of <c>localize-page</c>.
/// </summary>
public sealed record LocalizePageResponse {

	/// <summary>Caption outcome: the title was written in the culture.</summary>
	public const string CaptionWritten = "written";

	/// <summary>Caption outcome: the supplied title already equalled the stored one.</summary>
	public const string CaptionUnchanged = "unchanged";

	/// <summary>Gets a value indicating whether the call succeeded (for a save: the values are stored).</summary>
	[DataMember(Name = "success")]
	[JsonProperty("success")]
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	/// <summary>Gets the page schema name.</summary>
	[DataMember(Name = "schemaName")]
	[JsonProperty("schemaName")]
	[JsonPropertyName("schemaName")]
	public string SchemaName { get; init; }

	/// <summary>Gets the UId of the schema that was read (and saved).</summary>
	[DataMember(Name = "schemaUId")]
	[JsonProperty("schemaUId")]
	[JsonPropertyName("schemaUId")]
	public string SchemaUId { get; init; }

	/// <summary>Gets the name of the package that holds the edited schema.</summary>
	[DataMember(Name = "packageName")]
	[JsonProperty("packageName")]
	[JsonPropertyName("packageName")]
	public string PackageName { get; init; }

	/// <summary>Gets the culture in its canonical <c>SysCulture.Name</c> spelling.</summary>
	[DataMember(Name = "culture")]
	[JsonProperty("culture")]
	[JsonPropertyName("culture")]
	public string Culture { get; init; }

	/// <summary>Gets whether the culture is active in the environment; <see langword="null"/> when it was not resolved.</summary>
	[DataMember(Name = "cultureActive")]
	[JsonProperty("cultureActive")]
	[JsonPropertyName("cultureActive")]
	public bool? CultureActive { get; init; }

	/// <summary>Gets a value indicating whether a <c>SaveSchema</c> was sent.</summary>
	[DataMember(Name = "saved")]
	[JsonProperty("saved")]
	[JsonPropertyName("saved")]
	public bool Saved { get; init; }

	/// <summary>Gets the keys whose value in the culture was changed.</summary>
	[DataMember(Name = "written")]
	[JsonProperty("written")]
	[JsonPropertyName("written")]
	public IReadOnlyList<string> Written { get; init; } = [];

	/// <summary>Gets the supplied keys whose stored value already equalled the supplied one.</summary>
	[DataMember(Name = "unchanged")]
	[JsonProperty("unchanged")]
	[JsonPropertyName("unchanged")]
	public IReadOnlyList<string> Unchanged { get; init; } = [];

	/// <summary>Gets the caption outcome: <see cref="CaptionWritten"/>, <see cref="CaptionUnchanged"/>, or <see langword="null"/> when no caption was supplied.</summary>
	[DataMember(Name = "captionOutcome")]
	[JsonProperty("captionOutcome")]
	[JsonPropertyName("captionOutcome")]
	public string CaptionOutcome { get; init; }

	/// <summary>Gets the page's coverage in the culture; <see langword="null"/> when the schema was not read.</summary>
	[DataMember(Name = "coverage")]
	[JsonProperty("coverage")]
	[JsonPropertyName("coverage")]
	public LocalizePageCoverage Coverage { get; init; }

	/// <summary>Gets advisory messages.</summary>
	[DataMember(Name = "warnings")]
	[JsonProperty("warnings")]
	[JsonPropertyName("warnings")]
	public IReadOnlyList<string> Warnings { get; init; } = [];

	/// <summary>Gets the error message of a failed call.</summary>
	[DataMember(Name = "error")]
	[JsonProperty("error")]
	[JsonPropertyName("error")]
	public string Error { get; init; }
}

/// <summary>
/// Writes the values of a Freedom UI page's localizable strings and title in one culture, without
/// changing any other culture (ADR <c>adr-page-localization.md</c>, D1-D8).
/// </summary>
public interface ILocalizePageService {

	/// <summary>
	/// Localizes the page, or only reports its coverage when neither resources nor caption are supplied.
	/// </summary>
	/// <param name="options">Command options.</param>
	/// <returns>The structured result. Never throws for a remote failure; it is reported in <see cref="LocalizePageResponse.Error"/>.</returns>
	LocalizePageResponse Localize(LocalizePageOptions options);
}

/// <summary>
/// CLI entry point and implementation of <c>localize-page</c>.
/// </summary>
public sealed class LocalizePageCommand : Command<LocalizePageOptions>, ILocalizePageService {

	internal const string UnknownKeysMessageFormat =
		"Unknown resource key(s) on page '{0}': {1}. localize-page translates existing keys only; register a new key "
		+ "with update-page first. Available keys: {2}.";

	internal const string DataSourceBoundKeysHintFormat =
		" Key(s) {0} are data-source-bound captions provided by the entity column caption: translate them with "
		+ "update-entity-schema or modify-entity-schema-column title-localizations.";

	internal const string NoEditableSchemaMessageFormat =
		"Page '{0}' has no editable schema in design package '{1}'. localize-page never creates a replacing schema; "
		+ "create one first, for example with the first update-page into the design package.";

	internal const string DefaultCultureRefusedMessageFormat =
		"localize-page does not write the default culture '{0}'. Change {0} values with update-page `resources` "
		+ "(it registers keys and writes {0}); localize-page adds the other cultures.";

	internal const string UnstorableResourceValueMessageFormat =
		"The value of resource key '{0}' contains a character Creatio cannot store in a schema resource (a control "
		+ "character other than tab, LF or CR, U+FFFE, U+FFFF or a lone surrogate half). Remove it and retry; nothing was saved.";

	internal const string UnstorableCaptionMessage =
		"The caption contains a character Creatio cannot store in a schema resource (a control character other than "
		+ "tab, LF or CR, U+FFFE, U+FFFF or a lone surrogate half). Remove it and retry; nothing was saved.";

	internal const string NullResourceValueMessageFormat =
		"resources must map every key to a string value; key(s) with a null value: {0}.";

	// Same meaning as PageUpdateCommand.ResourceWorkspaceCaptureWarning, worded for the command that emits it.
	internal const string WorkspaceCaptureWarning =
		"Page translations were saved on the server; localize-page does not capture your workspace source. "
		+ "A push-workspace from stale metadata/resource XML can revert these changes. Preserve local edits, capture "
		+ "the affected package with restore-workspace (pull-workspace), and review its schema metadata and culture "
		+ "resource XML before pushing. For linked FSM workspaces, follow the workspace's capture instructions.";

	internal const string ReadbackMismatchMessageFormat =
		"The page was saved, but the stored '{0}' value differs from the requested one for: {1}.";

	private const string LocalizableStringsKey = "localizableStrings";
	private const string CaptionKey = "caption";
	private const string CaptionReadbackName = "caption";
	private const string ChecksumColumnName = "Checksum";
	private const string ModifiedOnColumnName = "ModifiedOn";

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly IPageDesignerHierarchyClient _hierarchyClient;
	private readonly ICultureAvailabilityGuard _cultureAvailabilityGuard;
	private readonly IPageBaselineGuard _pageBaselineGuard;
	private readonly ILogger _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="LocalizePageCommand"/> class.
	/// </summary>
	/// <param name="applicationClient">Authenticated client of the target environment.</param>
	/// <param name="serviceUrlBuilder">URL builder of the target environment.</param>
	/// <param name="hierarchyClient">Designer hierarchy client used to resolve the editable schema.</param>
	/// <param name="cultureAvailabilityGuard">Resolves the requested culture against the environment's
	/// <c>SysCulture</c> rows.</param>
	/// <param name="pageBaselineGuard">Refreshes the on-disk page baseline after a save.</param>
	/// <param name="logger">CLI output.</param>
	public LocalizePageCommand(
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder,
		IPageDesignerHierarchyClient hierarchyClient,
		ICultureAvailabilityGuard cultureAvailabilityGuard,
		IPageBaselineGuard pageBaselineGuard,
		ILogger logger) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_hierarchyClient = hierarchyClient;
		_cultureAvailabilityGuard = cultureAvailabilityGuard;
		_pageBaselineGuard = pageBaselineGuard;
		_logger = logger;
	}

	/// <summary>
	/// Executes the command and prints the JSON result.
	/// </summary>
	/// <param name="options">Command options.</param>
	/// <returns>0 on success; 1 otherwise.</returns>
	public override int Execute(LocalizePageOptions options) {
		LocalizePageResponse response = Localize(options);
		_logger.WriteInfo(JsonConvert.SerializeObject(response));
		return response.Success ? 0 : 1;
	}

	/// <inheritdoc />
	public LocalizePageResponse Localize(LocalizePageOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		try {
			return LocalizeCore(options);
		} catch (Exception ex) when (ex is not OperationCanceledException) {
			return Failure(options.SchemaName, ex.Message);
		}
	}

	private LocalizePageResponse LocalizeCore(LocalizePageOptions options) {
		if (string.IsNullOrWhiteSpace(options.SchemaName)) {
			return Failure(options.SchemaName, "schema-name is required");
		}
		if (string.IsNullOrWhiteSpace(options.Culture)) {
			return Failure(options.SchemaName, "culture is required");
		}
		if (string.Equals(options.Culture.Trim(), ResourceStringHelper.DefaultCultureName, StringComparison.OrdinalIgnoreCase)) {
			return Failure(options.SchemaName,
				string.Format(DefaultCultureRefusedMessageFormat, ResourceStringHelper.DefaultCultureName));
		}
		if (!TryParseResources(options.Resources, out Dictionary<string, string> resources, out string parseError)) {
			return Failure(options.SchemaName, parseError);
		}
		string unstorableError = DescribeUnstorableValues(resources, options.Caption);
		if (unstorableError != null) {
			return Failure(options.SchemaName, unstorableError);
		}
		// An absent culture or a failed SysCulture read throws; Localize turns it into the failure response.
		CultureResolution resolution = _cultureAvailabilityGuard.Resolve(options.Culture);
		CreatioCulture culture = resolution.Culture;
		var warnings = new List<string>();
		if (resolution.Warning != null) {
			warnings.Add(resolution.Warning);
		}
		if (!TryResolveEditableSchema(options.SchemaName, out EditableSchema editable, out string resolveError)) {
			return Failure(options.SchemaName, resolveError, culture);
		}
		if (!TryGetSchema(editable.UId, out JObject schema, out string loadError)) {
			return Failure(options.SchemaName, loadError, culture);
		}
		string packageName = schema["package"]?["name"]?.ToString() is { Length: > 0 } name ? name : editable.PackageName;
		if (schema[LocalizableStringsKey] is not JArray localizableStrings) {
			localizableStrings = new JArray();
			schema[LocalizableStringsKey] = localizableStrings;
		}
		Dictionary<string, JArray> knownKeys = ResolveHierarchyKeys(editable.Hierarchy, localizableStrings);
		string unknownKeysError = DescribeUnknownKeys(options.SchemaName, schema, localizableStrings, knownKeys, resources);
		if (unknownKeysError != null) {
			return Failure(options.SchemaName, unknownKeysError, culture) with {
				SchemaUId = editable.UId,
				PackageName = packageName
			};
		}
		var written = new List<string>();
		var unchanged = new List<string>();
		ApplyResources(localizableStrings, knownKeys, resources, culture.Name, written, unchanged);
		string captionOutcome = ApplyCaption(schema, options.Caption, culture.Name);
		bool mustSave = written.Count > 0 || captionOutcome == LocalizePageResponse.CaptionWritten;
		var result = new LocalizePageResponse {
			Success = true,
			SchemaName = options.SchemaName,
			SchemaUId = editable.UId,
			PackageName = packageName,
			Culture = culture.Name,
			CultureActive = culture.Active,
			Saved = false,
			Written = written,
			Unchanged = unchanged,
			CaptionOutcome = captionOutcome,
			// Resolved again on purpose: ApplyResources has just added the target-culture values (and new override
			// entries) to localizableStrings, so the key map built before the write would report them as missing.
			Coverage = BuildCoverage(ResolveHierarchyKeys(editable.Hierarchy, localizableStrings), schema, culture.Name),
			Warnings = warnings
		};
		return mustSave ? SaveAndVerify(options, schema, editable.Hierarchy, result, warnings) : result;
	}

	private LocalizePageResponse SaveAndVerify(
		LocalizePageOptions options,
		JObject schema,
		IReadOnlyList<PageDesignerHierarchySchema> hierarchy,
		LocalizePageResponse result,
		List<string> warnings) {
		// Read right before the save: the on-disk baseline is refreshed only when it still equals this value,
		// i.e. when nobody changed the page after the get-page that wrote it (ADR D8).
		string preSaveChecksum = ReadSchemaMetadata(result.SchemaUId).Checksum;
		if (!TrySaveSchema(schema, out string saveError)) {
			return result with { Success = false, Saved = false, Error = saveError, Coverage = null };
		}
		ResetScriptCache();
		warnings.Add(WorkspaceCaptureWarning);
		string baselineWarning = _pageBaselineGuard.RefreshAfterSave(
			options, options.SchemaName, result.SchemaUId, options.OutputDirectory, preSaveChecksum,
			() => ReadSchemaMetadata(result.SchemaUId));
		if (!string.IsNullOrWhiteSpace(baselineWarning)) {
			warnings.Add(baselineWarning);
		}
		JObject stored;
		List<string> mismatches;
		try {
			if (!TryGetSchema(result.SchemaUId, out stored, out string readbackError)) {
				return ReadbackFailure(result, readbackError);
			}
			mismatches = FindReadbackMismatches(stored, schema, result, options.Caption);
		} catch (Exception ex) when (ex is not OperationCanceledException) {
			return ReadbackFailure(result, ex.Message);
		}
		LocalizePageResponse verified = result with {
			Saved = true,
			Coverage = BuildCoverage(
				ResolveHierarchyKeys(hierarchy, stored[LocalizableStringsKey] as JArray ?? new JArray()), stored, result.Culture)
		};
		return mismatches.Count == 0
			? verified
			: verified with {
				Success = false,
				Error = string.Format(ReadbackMismatchMessageFormat, result.Culture, string.Join(", ", mismatches))
			};
	}

	// The save has landed; only the verification failed, so the result still says saved:true.
	private static LocalizePageResponse ReadbackFailure(LocalizePageResponse result, string reason) =>
		result with {
			Success = false,
			Saved = true,
			Error = $"The page was saved, but reading it back failed: {reason}"
		};

	private static bool TryParseResources(string json, out Dictionary<string, string> resources, out string error) {
		resources = new Dictionary<string, string>(StringComparer.Ordinal);
		error = null;
		if (string.IsNullOrWhiteSpace(json)) {
			return true;
		}
		try {
			resources = JsonConvert.DeserializeObject<Dictionary<string, string>>(json)
				?? new Dictionary<string, string>(StringComparer.Ordinal);
		} catch (JsonException ex) {
			error = $"resources must be a JSON object of key → value strings: {ex.Message}";
			return false;
		}
		List<string> nullKeys = resources.Where(pair => pair.Value is null).Select(pair => pair.Key).ToList();
		if (nullKeys.Count > 0) {
			error = string.Format(NullResourceValueMessageFormat, string.Join(", ", nullKeys));
			return false;
		}
		return true;
	}

	private static string DescribeUnstorableValues(IReadOnlyDictionary<string, string> resources, string caption) {
		string unstorableKey = resources.FirstOrDefault(pair => XmlAttributeText.ContainsUnstorable(pair.Value)).Key;
		if (unstorableKey != null) {
			return string.Format(UnstorableResourceValueMessageFormat, unstorableKey);
		}
		return XmlAttributeText.ContainsUnstorable(caption) ? UnstorableCaptionMessage : null;
	}

	private static string DescribeUnknownKeys(
		string schemaName,
		JObject schema,
		JArray localizableStrings,
		IReadOnlyDictionary<string, JArray> knownKeys,
		IReadOnlyDictionary<string, string> resources) {
		List<string> unknown = resources.Keys.Where(key => !knownKeys.ContainsKey(key)).ToList();
		if (unknown.Count == 0) {
			return null;
		}
		List<string> pageKeys = OrderOwnFirst(schema, localizableStrings).ToList();
		IEnumerable<string> available = pageKeys.Concat(knownKeys.Keys.Where(key => !pageKeys.Contains(key)));
		string message = string.Format(UnknownKeysMessageFormat, schemaName, string.Join(", ", unknown),
			string.Join(", ", available));
		string body = schema["body"]?.ToString();
		HashSet<string> bodyKeys = ResourceStringHelper.ExtractKeys(body);
		HashSet<string> dsBoundKeys = SchemaValidationService.CollectViewModelPaths(body).Keys
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		List<string> dsBoundUnknown = unknown.Where(key => bodyKeys.Contains(key) && dsBoundKeys.Contains(key)).ToList();
		return dsBoundUnknown.Count == 0
			? message
			: message + string.Format(DataSourceBoundKeysHintFormat, string.Join(", ", dsBoundUnknown));
	}

	private static IEnumerable<string> OrderOwnFirst(JObject schema, JArray localizableStrings) {
		string schemaUId = schema["uId"]?.ToString();
		return localizableStrings.Children<JObject>()
			.Select(entry => (
				Name: entry["name"]?.ToString(),
				Own: string.IsNullOrEmpty(entry["parentSchemaUId"]?.ToString())
					|| string.Equals(entry["parentSchemaUId"]?.ToString(), schemaUId, StringComparison.OrdinalIgnoreCase)))
			.Where(entry => !string.IsNullOrEmpty(entry.Name))
			.OrderBy(entry => entry.Own ? 0 : 1)
			.Select(entry => entry.Name);
	}

	/// <summary>
	/// Resolves every localizable-string key of the page the way <c>get-page</c> does: the hierarchy's
	/// <c>localizableStrings</c> merged ancestor-first, with the editable schema's own list on top.
	/// <c>GetSchema(useFullHierarchy:false)</c> alone does not carry every inherited key.
	/// </summary>
	private static Dictionary<string, JArray> ResolveHierarchyKeys(
		IReadOnlyList<PageDesignerHierarchySchema> hierarchy,
		JArray pageStrings) {
		var resolved = new Dictionary<string, JArray>(StringComparer.Ordinal);
		IEnumerable<JArray> layers = (hierarchy ?? [])
			.Reverse()
			.Select(part => part.LocalizableStrings)
			.Append(pageStrings);
		foreach (JObject entry in layers.Where(layer => layer != null).SelectMany(layer => layer.Children<JObject>())) {
			string name = entry["name"]?.ToString();
			if (string.IsNullOrEmpty(name)) {
				continue;
			}
			if (!resolved.TryGetValue(name, out JArray values)) {
				values = new JArray();
				resolved[name] = values;
			}
			foreach (JObject value in (entry["values"] as JArray ?? new JArray()).Children<JObject>()) {
				string cultureName = value["cultureName"]?.ToString();
				if (!string.IsNullOrEmpty(cultureName)) {
					ResourceStringHelper.SetCultureValue(values, cultureName, value["value"]?.ToString());
				}
			}
		}
		return resolved;
	}

	private static void ApplyResources(
		JArray localizableStrings,
		IReadOnlyDictionary<string, JArray> knownKeys,
		IReadOnlyDictionary<string, string> resources,
		string culture,
		List<string> written,
		List<string> unchanged) {
		foreach ((string key, string value) in resources) {
			JObject entry = localizableStrings.Children<JObject>()
				.FirstOrDefault(item => string.Equals(item["name"]?.ToString(), key, StringComparison.Ordinal));
			bool changed;
			if (entry != null) {
				changed = ResourceStringHelper.SetCultureValue(entry, culture, value);
			} else {
				// A key only an ancestor declares: the page gets a new entry holding ONLY the target culture.
				// The platform stores just that override; en-US keeps resolving from the parent (ADR E1-c).
				changed = !string.Equals(ResourceStringHelper.GetCultureValue(knownKeys[key], culture), value, StringComparison.Ordinal);
				if (changed) {
					var newEntry = new JObject {
						["uId"] = Guid.NewGuid().ToString(),
						["name"] = key,
						["values"] = new JArray()
					};
					ResourceStringHelper.SetCultureValue(newEntry, culture, value);
					localizableStrings.Add(newEntry);
				}
			}
			(changed ? written : unchanged).Add(key);
		}
	}

	private static string ApplyCaption(JObject schema, string caption, string culture) {
		if (string.IsNullOrEmpty(caption)) {
			return null;
		}
		if (schema[CaptionKey] is not JArray captionValues) {
			captionValues = new JArray();
			schema[CaptionKey] = captionValues;
		}
		return ResourceStringHelper.SetCultureValue(captionValues, culture, caption)
			? LocalizePageResponse.CaptionWritten
			: LocalizePageResponse.CaptionUnchanged;
	}

	private static LocalizePageCoverage BuildCoverage(
		IReadOnlyDictionary<string, JArray> resolvedKeys,
		JObject schema,
		string culture) {
		var missing = new List<string>();
		var sameAsDefault = new List<string>();
		int keys = 0;
		foreach ((string name, JArray values) in resolvedKeys) {
			keys++;
			string value = ResourceStringHelper.GetCultureValue(values, culture);
			if (string.IsNullOrEmpty(value)) {
				missing.Add(name);
			} else if (string.Equals(value,
				ResourceStringHelper.GetCultureValue(values, ResourceStringHelper.DefaultCultureName), StringComparison.Ordinal)) {
				sameAsDefault.Add(name);
			}
		}
		var captionValues = schema[CaptionKey] as JArray;
		string captionValue = ResourceStringHelper.GetCultureValue(captionValues, culture);
		return new LocalizePageCoverage {
			Keys = keys,
			Translated = keys - missing.Count,
			Missing = missing,
			SameAsDefault = sameAsDefault,
			CaptionSameAsDefault = !string.IsNullOrEmpty(captionValue) && string.Equals(captionValue,
				ResourceStringHelper.GetCultureValue(captionValues, ResourceStringHelper.DefaultCultureName), StringComparison.Ordinal)
		};
	}

	private static List<string> FindReadbackMismatches(
		JObject stored,
		JObject requested,
		LocalizePageResponse result,
		string caption) {
		var mismatches = new List<string>();
		var storedStrings = stored[LocalizableStringsKey] as JArray;
		var requestedStrings = (JArray)requested[LocalizableStringsKey];
		foreach (string key in result.Written) {
			string expected = ResourceStringHelper.GetCultureValue(FindValues(requestedStrings, key), result.Culture);
			string actual = ResourceStringHelper.GetCultureValue(FindValues(storedStrings, key), result.Culture);
			if (!string.Equals(expected, actual, StringComparison.Ordinal)) {
				mismatches.Add(key);
			}
		}
		if (result.CaptionOutcome == LocalizePageResponse.CaptionWritten
			&& !string.Equals(caption,
				ResourceStringHelper.GetCultureValue(stored[CaptionKey] as JArray, result.Culture), StringComparison.Ordinal)) {
			mismatches.Add(CaptionReadbackName);
		}
		return mismatches;
	}

	private static JArray FindValues(JArray localizableStrings, string key) =>
		localizableStrings?.Children<JObject>()
			.FirstOrDefault(entry => string.Equals(entry["name"]?.ToString(), key, StringComparison.Ordinal))?["values"] as JArray;

	private bool TryResolveEditableSchema(string schemaName, out EditableSchema editable, out string error) {
		editable = null;
		(JToken metadata, string queryError) = PageSchemaMetadataHelper.QuerySysSchemaRow(
			_applicationClient, _serviceUrlBuilder, schemaName, ("UId", "UId"));
		if (metadata == null) {
			error = queryError;
			return false;
		}
		string rawSchemaUId = metadata["UId"]?.ToString();
		if (string.IsNullOrWhiteSpace(rawSchemaUId)) {
			error = $"Schema '{schemaName}' metadata is missing UId";
			return false;
		}
		string designPackageUId = _hierarchyClient.GetDesignPackageUId(rawSchemaUId);
		if (string.IsNullOrWhiteSpace(designPackageUId)) {
			error = $"Failed to resolve design package for '{schemaName}': no package returned";
			return false;
		}
		IReadOnlyList<PageDesignerHierarchySchema> hierarchy = _hierarchyClient.GetParentSchemas(rawSchemaUId, designPackageUId);
		if (hierarchy == null || hierarchy.Count == 0) {
			error = $"Schema '{schemaName}' hierarchy is empty";
			return false;
		}
		PageDesignerHierarchySchema head = hierarchy[0];
		if (string.Equals(head.PackageUId, designPackageUId, StringComparison.OrdinalIgnoreCase)) {
			editable = new EditableSchema(head.UId, head.PackageName, hierarchy);
			error = null;
			return true;
		}
		string existingInPackage = PageSchemaMetadataHelper.FindExistingSchemaInPackage(
			_applicationClient, _serviceUrlBuilder, schemaName, designPackageUId);
		if (string.IsNullOrWhiteSpace(existingInPackage)) {
			error = string.Format(NoEditableSchemaMessageFormat, schemaName, designPackageUId);
			return false;
		}
		editable = new EditableSchema(existingInPackage, null, hierarchy);
		error = null;
		return true;
	}

	private bool TryGetSchema(string schemaUId, out JObject schema, out string error) {
		var request = new JObject {
			["schemaUId"] = schemaUId,
			["useFullHierarchy"] = false
		};
		string url = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetClientUnitDesignerSchema);
		JObject response = JObject.Parse(_applicationClient.ExecutePostRequest(url, request.ToString(Formatting.None)));
		if (!(response["success"]?.Value<bool>() ?? false) || response["schema"] is not JObject loaded) {
			schema = null;
			error = response["errorInfo"]?["message"]?.ToString() ?? $"Failed to load schema '{schemaUId}'";
			return false;
		}
		schema = loaded;
		error = null;
		return true;
	}

	private bool TrySaveSchema(JObject schema, out string error) {
		string url = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.SaveClientUnitDesignerSchema);
		JObject response = JObject.Parse(_applicationClient.ExecutePostRequest(url, schema.ToString(Formatting.None)));
		if (response["success"]?.Value<bool>() ?? false) {
			error = null;
			return true;
		}
		error = PageSchemaMetadataHelper.ParseSaveErrorMessage(response, "Failed to save page schema");
		return false;
	}

	/// <summary>
	/// Invalidates the workplace script cache after a save, for the same reason
	/// <c>PageUpdateCommand</c> does: otherwise the runtime and the next hierarchy read serve the
	/// pre-save bundle. Best-effort; a failed reset never fails a save that already landed.
	/// </summary>
	private void ResetScriptCache() {
		try {
			_applicationClient.ExecutePostRequest(
				_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.ResetScriptCache), string.Empty);
		} catch (Exception ex) {
			_logger.WriteDebug($"ResetScriptCache failed: {ex.Message}");
		}
	}

	private (string Checksum, string ModifiedOn) ReadSchemaMetadata(string schemaUId) {
		try {
			(JToken row, _) = PageSchemaMetadataHelper.QuerySysSchemaRowByUId(
				_applicationClient, _serviceUrlBuilder, schemaUId,
				(ChecksumColumnName, ChecksumColumnName), (ModifiedOnColumnName, ModifiedOnColumnName));
			return row is null
				? (null, null)
				: (row[ChecksumColumnName]?.ToString(), row[ModifiedOnColumnName]?.ToString());
		} catch (Exception ex) {
			// Pre-save: a null checksum leaves the baseline untouched with a warning. Post-save: it makes the guard
			// drop the baseline instead of keeping a stale one.
			_logger.WriteDebug($"Schema checksum read failed: {ex.Message}");
			return (null, null);
		}
	}

	private static LocalizePageResponse Failure(string schemaName, string error, CreatioCulture culture = null) =>
		new() {
			Success = false,
			SchemaName = schemaName,
			Culture = culture?.Name,
			CultureActive = culture?.Active,
			Error = error
		};

	private sealed record EditableSchema(
		string UId,
		string PackageName,
		IReadOnlyList<PageDesignerHierarchySchema> Hierarchy);
}
