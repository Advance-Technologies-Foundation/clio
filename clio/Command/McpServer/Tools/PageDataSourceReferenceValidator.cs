using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Checks that every data source a Freedom UI page body binds to is declared, by the body itself or by the page's
/// inherited model config, for web (AMD) and mobile (JSON) bodies alike.
/// </summary>
public interface IPageDataSourceReferenceValidator {

	/// <summary>
	/// Reports each referenced but undeclared data source as an error. A reference is the first segment of an
	/// attribute's <c>modelConfig.path</c>, a <c>dataSourceName</c> in the view config diff, or the
	/// <c>primaryDataSourceName</c>. <paramref name="resolveTemplateModelConfig"/> returns the inherited
	/// <c>modelConfig</c> JSON the write layers over; it is invoked at most once, and only when the body does not
	/// settle every reference itself. A null delegate or result means the base is unknown: the check then passes and
	/// adds a warning that it did not run.
	/// </summary>
	SchemaValidationResult Validate(string body, Func<string> resolveTemplateModelConfig);

	/// <summary>True when <see cref="Validate"/> would read the inherited model config for this body.</summary>
	bool NeedsResolvedBase(string body);
}

/// <inheritdoc />
/// <remarks>
/// Declarations follow the bundle builder (ENG-102161): a non-empty <c>modelConfigDiff</c> is applied over the base and
/// the full <c>modelConfig</c> is ignored, otherwise the full config is deep-merged into the base. Templates never
/// declare <c>PDS</c>, so a generated page holds it in its own body and a replace write without it drops it.
/// </remarks>
internal sealed class PageDataSourceReferenceValidator(IPageSchemaBodyParser parser) : IPageDataSourceReferenceValidator {

	private const string ModelConfig = "modelConfig";
	private const string DataSources = "dataSources";
	private const string Values = "values";
	private const string DataSourceName = "dataSourceName";
	private const string PrimaryDataSourceName = "primaryDataSourceName";
	private const int MaxReferrersPerError = 3;
	private const int MaxErrors = 10;
	private const int MaxEchoLength = 300;

	/// <inheritdoc />
	public SchemaValidationResult Validate(string body, Func<string> resolveTemplateModelConfig) {
		var result = new SchemaValidationResult { IsValid = true };
		if (!TryParse(body, out PageParsedSchemaBody parsed)) {
			return result;
		}
		Dictionary<string, List<string>> references = CollectReferences(parsed);
		List<string> unsettled = Undeclared(parsed, references, baseModelConfig: null);
		if (unsettled.Count == 0) {
			return result;
		}
		if (!TryParseObject(resolveTemplateModelConfig?.Invoke(), out JObject baseModelConfig)) {
			result.Warnings.Add(
				$"Data-source bindings to {Sanitize(string.Join(", ", unsettled))} were not checked: the page's " +
				"inherited modelConfig could not be read, so a data source this write drops may have been missed.");
			return result;
		}
		bool isMobile = PageSchemaTypeExtensions.FromBody(body) == PageSchemaType.Mobile;
		List<string> undeclared = Undeclared(parsed, references, baseModelConfig);
		foreach (string dataSource in undeclared.Take(MaxErrors)) {
			result.Errors.Add(FormatError(dataSource, references[dataSource], isMobile));
		}
		if (undeclared.Count > MaxErrors) {
			result.Errors.Add($"{undeclared.Count - MaxErrors} more undeclared data sources are not listed.");
		}
		result.IsValid = result.Errors.Count == 0;
		return result;
	}

	/// <inheritdoc />
	public bool NeedsResolvedBase(string body) =>
		TryParse(body, out PageParsedSchemaBody parsed)
		&& Undeclared(parsed, CollectReferences(parsed), baseModelConfig: null).Count > 0;

	private bool TryParse(string body, out PageParsedSchemaBody parsed) {
		parsed = null;
		if (string.IsNullOrWhiteSpace(body)) {
			return false;
		}
		try {
			parsed = parser.Parse(body);
		} catch (Exception) {
			// A body that does not parse is reported by the structural validators.
			return false;
		}
		return parsed is not null;
	}

	private static Dictionary<string, List<string>> CollectReferences(PageParsedSchemaBody parsed) {
		var references = new Dictionary<string, List<string>>(StringComparer.Ordinal);
		if (parsed.ViewModelConfigDiff is JArray { Count: > 0 } viewModelOperations) {
			foreach (JObject operation in viewModelOperations.OfType<JObject>()) {
				CollectViewModelOperationReferences(operation, references);
			}
		} else {
			CollectAttributePathReferences(parsed.ViewModelConfig, ownerName: null, references);
		}
		if (parsed.ViewConfigDiff is JArray viewOperations) {
			foreach (JObject operation in viewOperations.OfType<JObject>()) {
				CollectDataSourceNameReferences(operation, StringValue(operation["name"]), references);
			}
		}
		if (parsed.ModelConfigDiff is JArray { Count: > 0 } modelOperations) {
			foreach (JObject operation in modelOperations.OfType<JObject>()) {
				if (IsRootOperation(operation) && operation[Values] is JObject values) {
					AddPrimaryDataSourceReference(values, references);
				}
			}
		} else if (parsed.ModelConfig is JObject inlineModelConfig) {
			AddPrimaryDataSourceReference(inlineModelConfig, references);
		}
		return references;
	}

	private static void CollectViewModelOperationReferences(
		JObject operation, Dictionary<string, List<string>> references) {
		List<string> path = PathOf(operation);
		JToken values = operation[Values];
		if (path.Count >= 2 && path[^1] == ModelConfig && values is JObject modelConfig) {
			AddPathReference(modelConfig, path[^2], references);
			return;
		}
		CollectAttributePathReferences(values, path.LastOrDefault(), references);
	}

	private static void CollectAttributePathReferences(
		JToken token, string ownerName, Dictionary<string, List<string>> references) {
		switch (token) {
			case JObject obj:
				foreach (JProperty property in obj.Properties()) {
					if (property.Name == ModelConfig && property.Value is JObject modelConfig) {
						AddPathReference(modelConfig, ownerName, references);
					}
					CollectAttributePathReferences(property.Value, property.Name, references);
				}
				break;
			case JArray array:
				foreach (JToken item in array) {
					CollectAttributePathReferences(item, ownerName, references);
				}
				break;
		}
	}

	private static void AddPathReference(JObject modelConfig, string ownerName, Dictionary<string, List<string>> references) {
		string path = StringValue(modelConfig["path"]);
		string dataSource = path?.Split('.')[0].Trim();
		if (!string.IsNullOrEmpty(dataSource) && !IsMacro(dataSource)) {
			AddReference(references, dataSource, $"attribute '{ownerName}' (modelConfig.path '{path}')");
		}
	}

	private static void CollectDataSourceNameReferences(
		JToken token, string elementName, Dictionary<string, List<string>> references) {
		switch (token) {
			case JObject obj:
				foreach (JProperty property in obj.Properties()) {
					if (property.Name == DataSourceName
						&& StringValue(property.Value) is { } dataSource
						&& !string.IsNullOrWhiteSpace(dataSource)
						&& !IsMacro(dataSource)) {
						AddReference(references, dataSource, $"viewConfigDiff element '{elementName}' (dataSourceName)");
					}
					CollectDataSourceNameReferences(property.Value, elementName, references);
				}
				break;
			case JArray array:
				foreach (JToken item in array) {
					CollectDataSourceNameReferences(item, elementName, references);
				}
				break;
		}
	}

	private static void AddPrimaryDataSourceReference(JObject config, Dictionary<string, List<string>> references) {
		string primary = StringValue(config[PrimaryDataSourceName]);
		if (!string.IsNullOrWhiteSpace(primary)) {
			AddReference(references, primary, PrimaryDataSourceName);
		}
	}

	private static List<string> Undeclared(
		PageParsedSchemaBody parsed, Dictionary<string, List<string>> references, JObject baseModelConfig) {
		HashSet<string> declared = Declared(parsed, baseModelConfig);
		return references.Keys.Where(name => !declared.Contains(name)).ToList();
	}

	/// <summary>
	/// The data sources the page declares after this body is applied over <paramref name="baseModelConfig"/>, in the
	/// differ's order: merges, then removes and inserts. With a null base (unknown) only declarations that cannot depend
	/// on the base count, so a body that needs the base to settle a reference is never treated as self-sufficient.
	/// </summary>
	private static HashSet<string> Declared(PageParsedSchemaBody parsed, JObject baseModelConfig) {
		var declared = new HashSet<string>(StringComparer.Ordinal);
		JObject baseDataSources = baseModelConfig?[DataSources] as JObject;
		AddKeys(baseDataSources, declared);
		if (parsed.ModelConfigDiff is not JArray { Count: > 0 } operations) {
			AddKeys((parsed.ModelConfig as JObject)?[DataSources] as JObject, declared);
			return declared;
		}
		List<JObject> ops = operations.OfType<JObject>().ToList();
		bool dataSourcesExist = ApplyMerges(ops, declared, baseDataSources is not null);
		ApplyRemoves(ops, declared);
		ApplyInserts(ops, declared, dataSourcesExist);
		return declared;
	}

	/// <returns>Whether a <c>dataSources</c> object exists once the merges have run.</returns>
	private static bool ApplyMerges(List<JObject> ops, HashSet<string> declared, bool dataSourcesExist) {
		foreach (JObject operation in ops.Where(op => KindOf(op) == "merge")) {
			if (IsRootOperation(operation) && (operation[Values] as JObject)?[DataSources] is JObject rootSources) {
				AddKeys(rootSources, declared);
				dataSourcesExist = true;
			} else if (dataSourcesExist && PathOf(operation) is [DataSources] && operation[Values] is JObject sources) {
				AddKeys(sources, declared);
			}
		}
		return dataSourcesExist;
	}

	private static void ApplyRemoves(List<JObject> ops, HashSet<string> declared) {
		foreach (JObject operation in ops.Where(op => KindOf(op) == "remove")) {
			List<string> path = PathOf(operation);
			if (path.Count == 2 && path[0] == DataSources && path[1] is { } removedName) {
				declared.Remove(removedName);
			} else if (path is [DataSources] && operation["properties"] is JArray properties) {
				declared.ExceptWith(properties.Select(StringValue).Where(name => name is not null));
			}
		}
	}

	private static void ApplyInserts(List<JObject> ops, HashSet<string> declared, bool dataSourcesExist) {
		if (!dataSourcesExist) {
			return;
		}
		foreach (JObject operation in ops.Where(op => KindOf(op) == "insert" && PathOf(op) is [DataSources])) {
			if (StringValue(operation["propertyName"]) is { } insertedName && operation[Values] is JObject) {
				declared.Add(insertedName);
			}
		}
	}

	private static string KindOf(JObject operation) => StringValue(operation["operation"])?.ToLowerInvariant();

	// Runtime macros such as #PrimaryDataSourceName()# resolve to a data source at render time, not to a key in
	// dataSources, so they are not references this check can resolve.
	private static bool IsMacro(string dataSource) => dataSource.StartsWith('#') && dataSource.EndsWith('#');

	private static void AddKeys(JObject map, HashSet<string> target) {
		if (map is null) {
			return;
		}
		foreach (JProperty property in map.Properties()) {
			target.Add(property.Name);
		}
	}

	private static bool IsRootOperation(JObject operation) =>
		operation["path"] is JArray { Count: 0 };

	private static List<string> PathOf(JObject operation) =>
		(operation["path"] as JArray)?.Select(StringValue).ToList() ?? [];

	private static string StringValue(JToken token) =>
		token?.Type == JTokenType.String ? token.Value<string>() : null;

	private static void AddReference(Dictionary<string, List<string>> references, string dataSource, string referrer) {
		if (!references.TryGetValue(dataSource, out List<string> referrers)) {
			referrers = [];
			references[dataSource] = referrers;
		}
		referrers.Add(referrer);
	}

	private static string FormatError(string dataSource, List<string> referrers, bool isMobile) {
		string shown = string.Join(", ", referrers.Take(MaxReferrersPerError));
		string more = referrers.Count > MaxReferrersPerError
			? $" and {referrers.Count - MaxReferrersPerError} more"
			: string.Empty;
		string head = $"Data source '{Sanitize(dataSource)}' is referenced by {Sanitize(shown)}{more}, but ";
		string tail = " Unless the page already lacked this data source before your edit, do not re-run with " +
			"validate=false: that saves the page with these bindings unresolved.";
		return isMobile
			? head + "neither the body's modelConfigDiff nor the page's inherited modelConfig declares it, so these " +
				"bindings resolve to nothing (Mobile Designer shows \"Column removed\"). A replace write (sync-pages, " +
				"update-page mode=replace) overwrites the page's own modelConfigDiff: carry the dataSources and " +
				"primaryDataSourceName operations over from get-page raw.body, or add fields with update-page " +
				"mode=append." + tail
			: head + "neither the body's SCHEMA_MODEL_CONFIG / SCHEMA_MODEL_CONFIG_DIFF nor the page's inherited " +
				"modelConfig declares it, so these bindings resolve to nothing in Freedom UI Designer. A replace write " +
				"(sync-pages, update-page mode=replace) overwrites the page's own body: keep its SCHEMA_MODEL_CONFIG " +
				"section from get-page raw.body. A non-empty SCHEMA_MODEL_CONFIG_DIFF makes SCHEMA_MODEL_CONFIG " +
				"ignored, so a diff-form body must carry the dataSources and primaryDataSourceName operations " +
				"instead; update-page mode=append works only on a page whose current body is in diff form." + tail;
	}

	// Body-sourced text reaches the MCP transcript; a control character could forge a message boundary there.
	private static string Sanitize(string value) {
		string flat = new(value.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
		return flat.Length <= MaxEchoLength ? flat : flat[..MaxEchoLength] + "…";
	}

	private static bool TryParseObject(string json, out JObject value) {
		value = null;
		if (string.IsNullOrWhiteSpace(json)) {
			return false;
		}
		try {
			value = JToken.Parse(json) as JObject;
		} catch (JsonException) {
			return false;
		}
		return value is not null;
	}
}
