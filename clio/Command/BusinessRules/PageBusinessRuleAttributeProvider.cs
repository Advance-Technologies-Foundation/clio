using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Clio.Common;

namespace Clio.Command.BusinessRules;

internal interface IPageBusinessRuleAttributeProvider {
	IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> GetAttributes(PageBundleInfo bundle, Guid packageUId);
}

internal sealed class PageBusinessRuleAttributeProvider(
	IEntityBusinessRuleAttributeProvider entityAttributeProvider,
	IEnumerable<IComponentConditionAttributeSource> componentSources)
	: IPageBusinessRuleAttributeProvider {

	private const string PageParametersScopeName = "PageParameters";

	private readonly Dictionary<string, IComponentConditionAttributeSource> _componentSources =
		componentSources.ToDictionary(source => source.ComponentType, StringComparer.Ordinal);

	public IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> GetAttributes(PageBundleInfo bundle, Guid packageUId) {
		ArgumentNullException.ThrowIfNull(bundle);

		Dictionary<string, BusinessRuleAttributeDescriptor> result = new(StringComparer.Ordinal);
		JsonObject attributes = bundle.ViewModelConfig["attributes"] as JsonObject ?? [];
		JsonObject dataSources = bundle.ModelConfig["dataSources"] as JsonObject ?? [];
		Dictionary<string, PageParameterInfo> parametersByName = BuildParameterMap(bundle);
		Dictionary<string, IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor>> entityAttributeMaps =
			new(StringComparer.Ordinal);

		foreach ((string attributeName, JsonNode? attributeNode) in attributes) {
			if (string.IsNullOrWhiteSpace(attributeName)
				|| attributeNode is not JsonObject attribute
				|| IsCollectionAttribute(attribute)
				|| !TryResolveDatasourcePath(attribute, out string? datasourceName, out string? columnName)) {
				continue;
			}

			// A page parameter surfaced on the page as a bound control: its type comes from
			// bundle.parameters[], not an entity data source. Expose it under the raw view-model
			// attribute name (unscoped, local ViewModel scope).
			if (string.Equals(datasourceName, PageParametersScopeName, StringComparison.Ordinal)) {
				if (parametersByName.TryGetValue(columnName, out PageParameterInfo? parameter)
					&& TryResolveParameterType(parameter, out string parameterType, out string? parameterReferenceSchema)) {
					result[attributeName] = new BusinessRuleAttributeDescriptor(attributeName, parameterType, parameterReferenceSchema);
				}
				continue;
			}

			if (!TryResolveEntitySchemaName(dataSources, datasourceName, out string? entitySchemaName)) {
				continue;
			}

			IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> entityAttributes =
				GetEntityAttributes(entityAttributeMaps, entitySchemaName, packageUId);
			if (!TryGetSupportedAttribute(entityAttributes, columnName, out BusinessRuleAttributeDescriptor? descriptor)) {
				continue;
			}

			result[attributeName] = descriptor with {
				Path = attributeName
			};
		}

		AddDatasourceScopedAttributes(result, dataSources, entityAttributeMaps, packageUId);
		AddPageParameterAttributes(result, parametersByName);
		AddComponentAttributes(result, bundle, attributes);
		return result;
	}

	// Page parameters come from the schema-level bundle.parameters[] (bound or not), exposed as
	// PageParameters-scoped condition sources keyed "PageParameters.<Name>". A parameter that is
	// also bound to a control is additionally exposed under its raw view-model attribute name by
	// the main attribute loop, so both addressing forms resolve.
	private static void AddPageParameterAttributes(
		IDictionary<string, BusinessRuleAttributeDescriptor> result,
		IReadOnlyDictionary<string, PageParameterInfo> parametersByName) {
		foreach ((string name, PageParameterInfo parameter) in parametersByName) {
			if (!TryResolveParameterType(parameter, out string dataValueTypeName, out string? referenceSchemaName)) {
				continue;
			}

			result[$"{PageParametersScopeName}.{name}"] = new BusinessRuleAttributeDescriptor(
				name,
				dataValueTypeName,
				referenceSchemaName,
				PageParametersScopeName);
		}
	}

	// Condition attributes that hold a view-config component's state, one per named element whose type
	// has a source. A declared attribute of the same name keeps its own scoped, typed descriptor.
	private void AddComponentAttributes(
		IDictionary<string, BusinessRuleAttributeDescriptor> result,
		PageBundleInfo bundle,
		JsonObject attributes) {
		if (_componentSources.Count == 0) {
			return;
		}
		foreach ((string? componentType, string componentName, JsonObject element) in
			ViewConfigTraversal.EnumerateNamedElements(bundle.ViewConfig)) {
			if (componentType is null
				|| !_componentSources.TryGetValue(componentType, out IComponentConditionAttributeSource? source)
				|| !source.TryResolve(element, componentName, attributes, out BusinessRuleAttributeDescriptor? descriptor)) {
				continue;
			}
			result.TryAdd(descriptor.Path, descriptor);
		}
	}

	private static Dictionary<string, PageParameterInfo> BuildParameterMap(PageBundleInfo bundle) {
		Dictionary<string, PageParameterInfo> map = new(StringComparer.Ordinal);
		foreach (PageParameterInfo parameter in bundle.Parameters) {
			if (parameter is not null && !string.IsNullOrWhiteSpace(parameter.Name)) {
				map[parameter.Name] = parameter;
			}
		}
		return map;
	}

	private static bool TryResolveParameterType(
		PageParameterInfo parameter,
		out string dataValueTypeName,
		out string? referenceSchemaName) {
		dataValueTypeName = string.Empty;
		referenceSchemaName = null;
		if (parameter is null || !parameter.DataValueType.HasValue) {
			return false;
		}

		string? name = CreatioDataValueType.GetName(parameter.DataValueType.Value);
		if (string.IsNullOrEmpty(name)) {
			return false;
		}

		dataValueTypeName = name;
		referenceSchemaName = string.IsNullOrWhiteSpace(parameter.ReferenceSchemaName) ? null : parameter.ReferenceSchemaName;
		return true;
	}

	private void AddDatasourceScopedAttributes(
		IDictionary<string, BusinessRuleAttributeDescriptor> result,
		JsonObject dataSources,
		IDictionary<string, IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor>> entityAttributeMaps,
		Guid packageUId) {
		foreach ((string datasourceName, JsonNode? _) in dataSources) {
			if (string.IsNullOrWhiteSpace(datasourceName)
				|| !TryResolveEntitySchemaName(dataSources, datasourceName, out string? entitySchemaName)) {
				continue;
			}

			IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> entityAttributes =
				GetEntityAttributes(entityAttributeMaps, entitySchemaName, packageUId);
			foreach (string columnName in entityAttributes.Keys) {
				if (!TryGetSupportedAttribute(entityAttributes, columnName, out BusinessRuleAttributeDescriptor? descriptor)) {
					continue;
				}

				result[$"{datasourceName}.{columnName}"] = descriptor with {
					Path = columnName,
					ScopeName = datasourceName
				};
			}
		}
	}

	private IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> GetEntityAttributes(
		IDictionary<string, IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor>> cache,
		string entitySchemaName,
		Guid packageUId) {
		if (!cache.TryGetValue(entitySchemaName, out IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor>? attributes)) {
			attributes = entityAttributeProvider.GetAttributes(entitySchemaName, packageUId).Attributes;
			cache[entitySchemaName] = attributes;
		}
		return attributes;
	}

	private static bool TryGetSupportedAttribute(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributes,
		string columnName,
		out BusinessRuleAttributeDescriptor? descriptor) {
		try {
			return attributes.TryGetValue(columnName, out descriptor);
		} catch (InvalidOperationException) {
			descriptor = null;
			return false;
		}
	}

	private static bool IsCollectionAttribute(JsonObject attribute) =>
		attribute["isCollection"]?.GetValue<bool>() == true;

	private static bool TryResolveDatasourcePath(
		JsonObject attribute,
		out string datasourceName,
		out string columnName) {
		datasourceName = string.Empty;
		columnName = string.Empty;
		string path = attribute["modelConfig"]?["path"]?.GetValue<string>() ?? string.Empty;
		string[] parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (parts.Length != 2) {
			return false;
		}
		datasourceName = parts[0];
		columnName = parts[1];
		return true;
	}

	private static bool TryResolveEntitySchemaName(
		JsonObject dataSources,
		string datasourceName,
		out string entitySchemaName) {
		entitySchemaName = dataSources[datasourceName]?["config"]?["entitySchemaName"]?.GetValue<string>() ?? string.Empty;
		return !string.IsNullOrWhiteSpace(entitySchemaName);
	}

	/// <summary>
	/// Explains why <paramref name="path"/> is not a page condition attribute, from the page bundle alone.
	/// </summary>
	/// <param name="bundle">The page bundle the rule was validated against.</param>
	/// <param name="path">The attribute path the rule named and the attribute map does not hold.</param>
	/// <returns>One sentence naming the kind of attribute, or <c>null</c> when the bundle does not say.</returns>
	/// <remarks>
	/// The provider exposes only direct columns of a page data source, page parameters and component state.
	/// An agent that names anything else (a virtual view-model attribute, a lookup path, a forward reference)
	/// otherwise gets "unknown attribute" with no reason and tends to fall back to a view-config binding, which
	/// the Freedom UI Designer later overwrites.
	/// </remarks>
	internal static string? DescribeUnsupportedConditionAttribute(PageBundleInfo bundle, string path) {
		if (bundle is null || string.IsNullOrWhiteSpace(path)) {
			return null;
		}

		JsonObject attributes = bundle.ViewModelConfig?["attributes"] as JsonObject ?? [];
		JsonObject dataSources = bundle.ModelConfig?["dataSources"] as JsonObject ?? [];
		if (attributes[path] is JsonObject attribute) {
			return DescribeDeclaredAttribute(path, attribute, dataSources);
		}

		string[] parts = SplitPath(path);
		if (parts.Length >= 2 && dataSources.ContainsKey(parts[0])) {
			return DescribeDatasourcePath(path, parts, dataSources);
		}

		return $"'{path}' is not declared in bundle.viewModelConfig.attributes and is not a "
			+ "'<data source>.<column>' path of a page data source.";
	}

	private static string DescribeDeclaredAttribute(string name, JsonObject attribute, JsonObject dataSources) {
		if (IsCollectionAttribute(attribute)) {
			return $"'{name}' is a collection (list) attribute, which cannot be a condition operand.";
		}

		string modelPath = ReadString(attribute["modelConfig"]?["path"]);
		if (string.IsNullOrWhiteSpace(modelPath)) {
			return $"'{name}' is a virtual view-model attribute: it has no modelConfig.path to a page data source, "
				+ "and page business rules read only data-source-bound attributes (and a stepper's active step).";
		}

		return DescribeDatasourcePath(name, SplitPath(modelPath), dataSources, modelPath);
	}

	private static string DescribeDatasourcePath(
		string name,
		string[] parts,
		JsonObject dataSources,
		string? boundPath = null) {
		string shownPath = boundPath ?? string.Join('.', parts);
		if (parts.Length > 2) {
			return $"'{name}' is a lookup path ('{shownPath}'): page business rules read only direct columns of a page "
				+ "data source, never a column of a related object.";
		}

		if (parts.Length == 2
			&& dataSources[parts[0]]?["config"]?["attributes"]?[parts[1]] is JsonObject datasourceAttribute) {
			string referencePath = ReadString(datasourceAttribute["path"]);
			return $"'{name}' is a forward reference ('{shownPath}'"
				+ (string.IsNullOrWhiteSpace(referencePath) ? string.Empty : $" -> '{referencePath}'")
				+ "): page business rules read only direct columns of a page data source.";
		}

		return $"'{name}' is bound to '{shownPath}', which is not a supported column of a page data source.";
	}

	private static string[] SplitPath(string path) =>
		path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

	private static string ReadString(JsonNode? node) =>
		node is JsonValue value && value.TryGetValue(out string? text) ? text : string.Empty;
}
