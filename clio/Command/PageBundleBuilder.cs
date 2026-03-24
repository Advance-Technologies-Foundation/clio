namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Newtonsoft.Json.Linq;

/// <summary>
/// Builds merged Freedom UI page bundles from designer hierarchy parts.
/// </summary>
public interface IPageBundleBuilder {
	/// <summary>
	/// Builds a merged page bundle.
	/// </summary>
	/// <param name="parts">Hierarchy parts in designer-service order: current page first, then parent schemas.</param>
	/// <returns>Merged bundle information.</returns>
	PageBundleInfo Build(IReadOnlyList<PageSchemaBundlePart> parts);
}

internal sealed class PageBundleBuilder : IPageBundleBuilder {
	private readonly IPageJsonDiffApplier _jsonDiffApplier;
	private readonly IPageJsonPathDiffApplier _jsonPathDiffApplier;

	public PageBundleBuilder(
		IPageJsonDiffApplier jsonDiffApplier,
		IPageJsonPathDiffApplier jsonPathDiffApplier) {
		_jsonDiffApplier = jsonDiffApplier;
		_jsonPathDiffApplier = jsonPathDiffApplier;
	}

	public PageBundleInfo Build(IReadOnlyList<PageSchemaBundlePart> parts) {
		if (parts.Count == 0) {
			return new PageBundleInfo();
		}

		PageSchemaBundlePart currentPart = parts[0];
		List<PageSchemaBundlePart> mergeOrder = parts.Reverse().ToList();
		JArray viewConfig = _jsonDiffApplier.ApplyDiff(
			new JArray(),
			mergeOrder.Select(part => part.ParsedBody.ViewConfigDiff as JArray ?? new JArray()).ToList(),
			mergeOrder.Select(part => new PageJsonDiffApplyOptions(part.Schema.SchemaVersion >= 1)).ToList());
		JObject viewModelConfig = BuildConfig(
			mergeOrder,
			part => part.ParsedBody.ViewModelConfig as JObject ?? new JObject(),
			part => part.ParsedBody.ViewModelConfigDiff as JArray ?? new JArray());
		JObject modelConfig = BuildConfig(
			mergeOrder,
			part => part.ParsedBody.ModelConfig as JObject ?? new JObject(),
			part => part.ParsedBody.ModelConfigDiff as JArray ?? new JArray());

		return new PageBundleInfo {
			Name = currentPart.Schema.Name,
			ViewConfig = PageBundleMergeHelpers.ToJsonArray(viewConfig),
			ViewModelConfig = PageBundleMergeHelpers.ToJsonObject(viewModelConfig),
			ModelConfig = PageBundleMergeHelpers.ToJsonObject(modelConfig),
			Resources = new PageResourceInfo {
				Strings = BuildResources(mergeOrder)
			},
			Handlers = currentPart.ParsedBody.Handlers,
			Converters = currentPart.ParsedBody.Converters,
			Validators = currentPart.ParsedBody.Validators,
			Parameters = BuildParameters(mergeOrder, currentPart.Schema.UId),
			Deps = currentPart.ParsedBody.Deps,
			Args = currentPart.ParsedBody.Args,
			OptionalProperties = BuildOptionalProperties(mergeOrder)
		};
	}

	private JObject BuildConfig(
		IReadOnlyList<PageSchemaBundlePart> parts,
		Func<PageSchemaBundlePart, JObject> configSelector,
		Func<PageSchemaBundlePart, JArray> diffSelector) {
		JObject result = new();
		foreach (PageSchemaBundlePart part in parts) {
			JArray diff = diffSelector(part);
			result = diff.Count > 0
				? _jsonPathDiffApplier.Apply(result, diff)
				: PageBundleMergeHelpers.DeepMerge(result, configSelector(part));
		}

		return result;
	}

	private static JsonObject BuildResources(IReadOnlyList<PageSchemaBundlePart> parts) {
		var result = new JsonObject();
		foreach (PageSchemaBundlePart part in parts) {
			foreach (JObject localizableString in part.Schema.LocalizableStrings.Children<JObject>()) {
				string name = localizableString["name"]?.ToString();
				if (string.IsNullOrWhiteSpace(name)) {
					continue;
				}

				if (result[name] is not null and not JsonObject) {
					continue;
				}

				JsonObject values = result[name] as JsonObject ?? new JsonObject();
				if (localizableString["values"] is JArray localizableValues) {
					foreach (JObject value in localizableValues.Children<JObject>()) {
						string cultureName = value["cultureName"]?.ToString();
						if (string.IsNullOrWhiteSpace(cultureName)) {
							continue;
						}

						values[cultureName] = value["value"]?.ToString();
					}
				}

				result[name] = values;
			}
		}

		return result;
	}

	private static IReadOnlyList<PageParameterInfo> BuildParameters(
		IReadOnlyList<PageSchemaBundlePart> parts,
		string currentSchemaUId) {
		Dictionary<string, PageParameterInfo> result = new();
		foreach (PageSchemaBundlePart part in parts) {
			foreach (JObject parameter in part.Schema.Parameters.Children<JObject>()) {
				string name = parameter["name"]?.ToString();
				if (string.IsNullOrWhiteSpace(name)) {
					continue;
				}

				result[name] = new PageParameterInfo {
					UId = parameter["uId"]?.ToString(),
					Name = name,
					Caption = PageBundleMergeHelpers.ToJsonNode(parameter["caption"]),
					DataValueType = parameter["type"]?.Value<int?>(),
					Required = parameter["required"]?.Value<bool>() ?? false,
					IsOwnParameter = string.Equals(
						parameter["parentSchemaUId"]?.ToString(),
						currentSchemaUId,
						System.StringComparison.OrdinalIgnoreCase),
					ReferenceSchemaUId = parameter["lookup"]?.ToString(),
					ReferenceSchemaName = parameter["schema"]?.ToString()
				};
			}
		}

		return result.Values.ToList();
	}

	private static JsonArray BuildOptionalProperties(IReadOnlyList<PageSchemaBundlePart> parts) {
		Dictionary<string, JToken> uniqueProperties = new();
		foreach (PageSchemaBundlePart part in parts) {
			foreach (JObject property in part.Schema.OptionalProperties.Children<JObject>()) {
				string key = property["key"]?.ToString();
				if (string.IsNullOrWhiteSpace(key)) {
					continue;
				}

				uniqueProperties[key] = property.DeepClone();
			}
		}

		JsonArray result = [];
		foreach (JToken value in uniqueProperties.Values) {
			result.Add(PageBundleMergeHelpers.ToJsonNode(value));
		}

		return result;
	}
}
