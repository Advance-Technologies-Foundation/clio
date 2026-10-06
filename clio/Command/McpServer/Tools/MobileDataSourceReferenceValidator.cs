using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Validates that every data source a mobile page body binds to is declared, either by the body's own
/// <c>modelConfigDiff</c> / <c>modelConfig</c> or by the page's inherited <c>modelConfig</c>. A reference counts
/// when it is the first segment of an attribute's <c>modelConfig.path</c> in <c>viewModelConfigDiff</c>, a
/// <c>dataSourceName</c> in <c>viewConfigDiff</c>, or the <c>primaryDataSourceName</c>.
/// </summary>
/// <remarks>
/// Without this check a replace write that sends <c>"modelConfigDiff": []</c> saves cleanly while dropping the
/// template's <c>PDS</c>, and every field renders as "Column removed" in Mobile Designer (ENG-102161).
/// When the inherited <c>modelConfig</c> cannot be resolved the check passes: the template may declare the source.
/// </remarks>
internal static class MobileDataSourceReferenceValidator {

	private const string ViewConfigDiff = "viewConfigDiff";
	private const string ViewModelConfigDiff = "viewModelConfigDiff";
	private const string ModelConfigDiff = "modelConfigDiff";
	private const string ModelConfig = "modelConfig";
	private const string DataSources = "dataSources";
	private const string DataSourceName = "dataSourceName";
	private const string PrimaryDataSourceName = "primaryDataSourceName";
	private const int MaxReferrersPerError = 3;

	/// <summary>
	/// Reports each referenced but undeclared data source as an error. The delegate returns the page's inherited
	/// <c>modelConfig</c> JSON; it is invoked at most once, and only when the body itself does not declare every
	/// referenced source. A <see langword="null"/> delegate or result means the base is unknown.
	/// </summary>
	public static SchemaValidationResult Validate(string body, Func<string> resolveTemplateModelConfig) {
		var result = new SchemaValidationResult { IsValid = true };
		if (!TryParseObject(body, out JObject root)) {
			return result;
		}
		Dictionary<string, List<string>> references = CollectReferences(root);
		if (references.Count == 0) {
			return result;
		}
		HashSet<string> declared = CollectBodyDeclarations(root);
		if (references.Keys.All(declared.Contains)) {
			return result;
		}
		if (root[ModelConfig] is not JObject) {
			if (!TryParseObject(resolveTemplateModelConfig?.Invoke(), out JObject templateModelConfig)) {
				return result;
			}
			AddDeclaredDataSources(templateModelConfig[DataSources], declared);
		}
		foreach ((string dataSource, List<string> referrers) in references.Where(r => !declared.Contains(r.Key))) {
			result.Errors.Add(FormatError(dataSource, referrers));
		}
		result.IsValid = result.Errors.Count == 0;
		return result;
	}

	/// <summary>
	/// True when the body references a data source it does not declare itself and carries no inline
	/// <c>modelConfig</c> — the only case in which <see cref="Validate"/> reads the inherited base.
	/// </summary>
	internal static bool NeedsResolvedBase(string body) {
		if (!TryParseObject(body, out JObject root) || root[ModelConfig] is JObject) {
			return false;
		}
		HashSet<string> declared = CollectBodyDeclarations(root);
		return CollectReferences(root).Keys.Any(name => !declared.Contains(name));
	}

	private static Dictionary<string, List<string>> CollectReferences(JObject root) {
		var references = new Dictionary<string, List<string>>(StringComparer.Ordinal);
		if (root[ViewModelConfigDiff] is JArray viewModelOperations) {
			foreach (JToken operation in viewModelOperations) {
				CollectAttributePathReferences(operation, ownerName: null, references);
			}
		}
		if (root[ViewConfigDiff] is JArray viewOperations) {
			foreach (JObject operation in viewOperations.OfType<JObject>()) {
				string elementName = StringValue(operation["name"]);
				CollectDataSourceNameReferences(operation, elementName, references);
			}
		}
		if (root[ModelConfigDiff] is JArray modelOperations) {
			foreach (JObject operation in modelOperations.OfType<JObject>()) {
				if (IsRootOperation(operation) && operation["values"] is JObject values) {
					AddPrimaryDataSourceReference(values, references);
				}
			}
		}
		return references;
	}

	private static void CollectAttributePathReferences(
		JToken token, string ownerName, Dictionary<string, List<string>> references) {
		switch (token) {
			case JObject obj:
				foreach (JProperty property in obj.Properties()) {
					if (property.Name == ModelConfig && property.Value is JObject modelConfig) {
						string path = StringValue(modelConfig["path"]);
						if (TryGetDataSourceSegment(path, out string dataSource)) {
							AddReference(references, dataSource, $"attribute '{ownerName}' (modelConfig.path '{path}')");
						}
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

	private static void CollectDataSourceNameReferences(
		JToken token, string elementName, Dictionary<string, List<string>> references) {
		switch (token) {
			case JObject obj:
				foreach (JProperty property in obj.Properties()) {
					if (property.Name == DataSourceName
						&& StringValue(property.Value) is { } dataSource
						&& !string.IsNullOrWhiteSpace(dataSource)) {
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

	/// <summary>
	/// Collects names the body's <c>modelConfigDiff</c> (or inline <c>modelConfig</c>) declares. Deliberately a
	/// union over every operation rather than a differ replay: a merge into <c>["dataSources"]</c> against an
	/// unknown base would be dropped by the differ and produce a false "undeclared" error.
	/// </summary>
	private static HashSet<string> CollectBodyDeclarations(JObject root) {
		var declared = new HashSet<string>(StringComparer.Ordinal);
		if (root[ModelConfig] is JObject inlineModelConfig) {
			AddDeclaredDataSources(inlineModelConfig[DataSources], declared);
		}
		if (root[ModelConfigDiff] is not JArray operations) {
			return declared;
		}
		foreach (JObject operation in operations.OfType<JObject>()) {
			List<string> path = (operation["path"] as JArray)?.Select(StringValue).ToList() ?? [];
			if (path.Count == 0) {
				AddDeclaredDataSources((operation["values"] as JObject)?[DataSources], declared);
			} else if (path[0] == DataSources) {
				if (path.Count == 1) {
					AddDeclaredDataSources(operation["values"], declared);
				} else if (!string.IsNullOrWhiteSpace(path[1])) {
					declared.Add(path[1]);
				}
			}
		}
		return declared;
	}

	private static bool IsRootOperation(JObject operation) =>
		operation["path"] is null or JArray { Count: 0 };

	private static void AddDeclaredDataSources(JToken dataSources, HashSet<string> declared) {
		if (dataSources is JObject map) {
			foreach (JProperty property in map.Properties()) {
				declared.Add(property.Name);
			}
		}
	}

	private static string StringValue(JToken token) =>
		token?.Type == JTokenType.String ? token.Value<string>() : null;

	private static bool TryGetDataSourceSegment(string path, out string dataSource) {
		dataSource = path?.Split('.')[0].Trim();
		return !string.IsNullOrEmpty(dataSource);
	}

	private static void AddReference(Dictionary<string, List<string>> references, string dataSource, string referrer) {
		if (!references.TryGetValue(dataSource, out List<string> referrers)) {
			referrers = [];
			references[dataSource] = referrers;
		}
		referrers.Add(referrer);
	}

	private static string FormatError(string dataSource, List<string> referrers) {
		string shown = string.Join(", ", referrers.Take(MaxReferrersPerError));
		string more = referrers.Count > MaxReferrersPerError
			? $" and {referrers.Count - MaxReferrersPerError} more"
			: string.Empty;
		return $"Data source '{Sanitize(dataSource)}' is referenced by {Sanitize(shown)}{more}, but neither the body's " +
			"modelConfigDiff nor the page's inherited modelConfig declares it, so these bindings resolve to nothing " +
			"(Mobile Designer shows \"Column removed\"). A replace write (sync-pages, update-page mode=replace) " +
			"overwrites the page's own modelConfigDiff: carry the dataSources and primaryDataSourceName operations " +
			"over from get-page raw.body, or add fields with update-page mode=append. Unless the page already lacked " +
			"this data source before your edit, do not re-run with validate=false: that saves the page with these " +
			"bindings unresolved.";
	}

	// Body-sourced text reaches the MCP transcript; a control character could forge a message boundary there.
	private static string Sanitize(string value) {
		string flat = new(value.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
		return flat.Length <= 300 ? flat : flat[..300] + "…";
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
