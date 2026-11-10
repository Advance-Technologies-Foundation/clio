using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Clio.Common;

namespace Clio.Command.BusinessRules;

internal interface IPageBusinessRuleAttributeProvider {
	IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> GetAttributes(PageBundleInfo bundle, Guid packageUId);
}

internal sealed class PageBusinessRuleAttributeProvider(
	IEntityBusinessRuleAttributeProvider entityAttributeProvider)
	: IPageBusinessRuleAttributeProvider {

	private const string PageParametersScopeName = "PageParameters";

	// Attributes a view-config component generates at runtime. The page body never declares them, so
	// they are absent from the view-model attributes; each rule exposes "<component name><suffix>" as an
	// unscoped attribute of the given type for every named component of that type. Add a row to support
	// another component that behaves the same way.
	private static readonly ComponentRuntimeAttribute[] ComponentRuntimeAttributes = [
		new("crt.Stepper", "_ActiveStep", "Text") // the open step's name, compared with a step name
	];

	private sealed record ComponentRuntimeAttribute(string ComponentType, string Suffix, string DataValueTypeName);

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
		AddComponentRuntimeAttributes(result, bundle);
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

	private static void AddComponentRuntimeAttributes(
		IDictionary<string, BusinessRuleAttributeDescriptor> result,
		PageBundleInfo bundle) {
		foreach ((string componentType, string componentName) in FindNamedComponents(bundle.ViewConfig)) {
			foreach (ComponentRuntimeAttribute rule in ComponentRuntimeAttributes) {
				if (!string.Equals(rule.ComponentType, componentType, StringComparison.Ordinal)) {
					continue;
				}
				string attributeName = componentName + rule.Suffix;
				result[attributeName] = new BusinessRuleAttributeDescriptor(attributeName, rule.DataValueTypeName, null);
			}
		}
	}

	private static IEnumerable<(string Type, string Name)> FindNamedComponents(JsonNode? viewConfig) {
		Stack<JsonNode?> pending = new();
		pending.Push(viewConfig);
		while (pending.Count > 0) {
			switch (pending.Pop()) {
				case JsonArray array:
					foreach (JsonNode? item in array) {
						pending.Push(item);
					}
					break;
				case JsonObject obj:
					string? type = GetString(obj, "type");
					string? name = GetString(obj, "name");
					if (!string.IsNullOrWhiteSpace(type) && !string.IsNullOrWhiteSpace(name)) {
						yield return (type, name);
					}
					foreach (KeyValuePair<string, JsonNode?> property in obj) {
						pending.Push(property.Value);
					}
					break;
			}
		}
	}

	private static string? GetString(JsonObject obj, string propertyName) =>
		obj[propertyName] is JsonValue value && value.TryGetValue(out string? text) ? text : null;

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
}
