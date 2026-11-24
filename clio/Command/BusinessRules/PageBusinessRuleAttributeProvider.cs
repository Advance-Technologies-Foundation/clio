using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Clio.Common;

namespace Clio.Command.BusinessRules;

internal interface IPageBusinessRuleAttributeProvider {
	/// <summary>
	/// Builds the page condition attributes keyed by the path a rule names them by.
	/// </summary>
	/// <param name="bundle">The page bundle.</param>
	/// <param name="packageUId">The package whose hierarchy resolves the entity columns.</param>
	/// <param name="rejected">When given, receives every declared view-model attribute the provider does not expose,
	/// keyed by its name, with one sentence saying why (virtual, lookup path, forward reference, ...).</param>
	IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> GetAttributes(
		PageBundleInfo bundle,
		Guid packageUId,
		IDictionary<string, string>? rejected = null);
}

internal sealed class PageBusinessRuleAttributeProvider(
	IEntityBusinessRuleAttributeProvider entityAttributeProvider,
	IEnumerable<IComponentConditionAttributeSource> componentSources)
	: IPageBusinessRuleAttributeProvider {

	private const string PageParametersScopeName = "PageParameters";

	private readonly Dictionary<string, IComponentConditionAttributeSource> _componentSources =
		componentSources.ToDictionary(source => source.ComponentType, StringComparer.Ordinal);

	public IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> GetAttributes(
		PageBundleInfo bundle,
		Guid packageUId,
		IDictionary<string, string>? rejected = null) {
		ArgumentNullException.ThrowIfNull(bundle);

		Dictionary<string, BusinessRuleAttributeDescriptor> result = new(StringComparer.Ordinal);
		Dictionary<string, string> rejections = new(StringComparer.Ordinal);
		JsonObject attributes = bundle.ViewModelConfig["attributes"] as JsonObject ?? [];
		JsonObject dataSources = bundle.ModelConfig["dataSources"] as JsonObject ?? [];
		Dictionary<string, PageParameterInfo> parametersByName = BuildParameterMap(bundle);
		Dictionary<string, IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor>> entityAttributeMaps =
			new(StringComparer.Ordinal);

		foreach ((string attributeName, JsonNode? attributeNode) in attributes) {
			if (string.IsNullOrWhiteSpace(attributeName) || attributeNode is not JsonObject attribute) {
				continue;
			}

			if (IsCollectionAttribute(attribute)) {
				rejections[attributeName] = $"'{attributeName}' is a collection (list) attribute, which cannot be a condition operand.";
				continue;
			}

			string modelPath = ReadModelPath(attribute);
			if (!TryResolveDatasourcePath(modelPath, out string? datasourceName, out string? columnName)) {
				rejections[attributeName] = string.IsNullOrWhiteSpace(modelPath)
					? $"'{attributeName}' is a virtual view-model attribute: it has no modelConfig.path to a page data source, "
						+ "and page business rules read only data-source-bound attributes (and a stepper's active step)."
					: $"'{attributeName}' is a lookup path ('{modelPath}'): page business rules read only direct columns of a "
						+ "page data source, never a column of a related object.";
				continue;
			}

			// A page parameter surfaced on the page as a bound control: its type comes from
			// bundle.parameters[], not an entity data source. Expose it under the raw view-model
			// attribute name (unscoped, local ViewModel scope).
			if (string.Equals(datasourceName, PageParametersScopeName, StringComparison.Ordinal)) {
				if (parametersByName.TryGetValue(columnName, out PageParameterInfo? parameter)
					&& TryResolveParameterType(parameter, out string parameterType, out string? parameterReferenceSchema)) {
					result[attributeName] = new BusinessRuleAttributeDescriptor(attributeName, parameterType, parameterReferenceSchema);
				} else {
					rejections[attributeName] =
						$"'{attributeName}' is bound to page parameter '{columnName}', which bundle.parameters[] does not type.";
				}
				continue;
			}

			if (!TryResolveEntitySchemaName(dataSources, datasourceName, out string? entitySchemaName)) {
				rejections[attributeName] =
					$"'{attributeName}' is bound to '{modelPath}', but '{datasourceName}' is not an entity data source of the page.";
				continue;
			}

			IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> entityAttributes =
				GetEntityAttributes(entityAttributeMaps, entitySchemaName, packageUId);
			if (!TryGetSupportedAttribute(entityAttributes, columnName, out BusinessRuleAttributeDescriptor? descriptor)) {
				rejections[attributeName] = DescribeUnsupportedColumn(attributeName, modelPath, entitySchemaName,
					dataSources[datasourceName]?["config"]?["attributes"]?[columnName] as JsonObject);
				continue;
			}

			result[attributeName] = descriptor with {
				Path = attributeName
			};
		}

		AddDatasourceScopedAttributes(result, dataSources, entityAttributeMaps, packageUId);
		AddPageParameterAttributes(result, parametersByName);
		AddComponentAttributes(result, bundle, attributes);
		if (rejected is not null) {
			// A component source (a stepper's bound active step) may still expose a rejected virtual attribute.
			foreach ((string name, string reason) in rejections) {
				if (!result.ContainsKey(name)) {
					rejected[name] = reason;
				}
			}
		}
		return result;
	}

	// A data-source attribute the entity does not expose: a forward reference declared on the data source
	// (a column of a related object), or a column the entity lacks or whose type rules cannot use.
	private static string DescribeUnsupportedColumn(
		string attributeName,
		string modelPath,
		string entitySchemaName,
		JsonObject? datasourceAttribute) {
		if (datasourceAttribute is null) {
			return $"'{attributeName}' is bound to '{modelPath}', which is not a supported column of '{entitySchemaName}'.";
		}
		string referencePath = ReadString(datasourceAttribute["path"]);
		return $"'{attributeName}' is a forward reference ('{modelPath}'"
			+ (string.IsNullOrWhiteSpace(referencePath) ? string.Empty : $" -> '{referencePath}'")
			+ "): page business rules read only direct columns of a page data source.";
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

	private static string ReadModelPath(JsonObject attribute) => ReadString(attribute["modelConfig"]?["path"]);

	private static bool TryResolveDatasourcePath(
		string path,
		out string datasourceName,
		out string columnName) {
		datasourceName = string.Empty;
		columnName = string.Empty;
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

	private static string ReadString(JsonNode? node) =>
		node is JsonValue value && value.TryGetValue(out string? text) ? text : string.Empty;
}
