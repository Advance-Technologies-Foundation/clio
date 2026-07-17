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

	public IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> GetAttributes(PageBundleInfo bundle, Guid packageUId) {
		ArgumentNullException.ThrowIfNull(bundle);

		JsonObject attributes = bundle.ViewModelConfig["attributes"] as JsonObject ?? [];
		JsonObject dataSources = bundle.ModelConfig["dataSources"] as JsonObject ?? [];
		Dictionary<string, string> dataSourceEntitySchemas = BuildDataSourceEntitySchemas(dataSources);
		Dictionary<string, BusinessRuleAttributeDescriptor> rootAttributes =
			BuildRootAttributes(attributes, dataSources, packageUId);
		Dictionary<string, BusinessRuleAttributeDescriptor> parameters = BuildParameterDescriptors(bundle.Parameters);

		return new PageScopedBusinessRuleAttributeMap(
			rootAttributes,
			parameters,
			dataSourceEntitySchemas,
			entityAttributeProvider,
			packageUId);
	}

	private Dictionary<string, BusinessRuleAttributeDescriptor> BuildRootAttributes(
		JsonObject attributes,
		JsonObject dataSources,
		Guid packageUId) {
		Dictionary<string, BusinessRuleAttributeDescriptor> result = new(StringComparer.Ordinal);
		Dictionary<string, IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor>> entityAttributeMaps =
			new(StringComparer.Ordinal);

		foreach ((string attributeName, JsonNode? attributeNode) in attributes) {
			if (string.IsNullOrWhiteSpace(attributeName)
				|| attributeNode is not JsonObject attribute
				|| IsCollectionAttribute(attribute)) {
				continue;
			}

			// A datasource-bound (surfaced) attribute carries a 2-part modelConfig.path. Resolve it to its
			// entity column as before. An attribute WITHOUT a modelConfig.path is an unbound/technical
			// page-local attribute (root scope, no datasource) — offer it when a data value type can be
			// determined so a handler-computed value can be used as a condition operand.
			if (HasModelConfigPath(attribute)) {
				if (TryResolveDatasourcePath(attribute, out string datasourceName, out string columnName)
					&& TryResolveEntitySchemaName(dataSources, datasourceName, out string entitySchemaName)) {
					IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> entityAttributes =
						GetEntityAttributes(entityAttributeMaps, entitySchemaName, packageUId);
					if (TryGetSupportedAttribute(entityAttributes, columnName, out BusinessRuleAttributeDescriptor? descriptor)) {
						result[attributeName] = descriptor! with { Path = attributeName };
					}
				}

				continue;
			}

			if (TryResolveUnboundAttributeType(attribute, out string dataValueTypeName)) {
				result[attributeName] = new BusinessRuleAttributeDescriptor(attributeName, dataValueTypeName, null);
			}
		}

		return result;
	}

	private static Dictionary<string, BusinessRuleAttributeDescriptor> BuildParameterDescriptors(
		IReadOnlyList<PageParameterInfo> parameters) {
		Dictionary<string, BusinessRuleAttributeDescriptor> result = new(StringComparer.Ordinal);
		foreach (PageParameterInfo parameter in parameters ?? []) {
			if (string.IsNullOrWhiteSpace(parameter?.Name) || parameter!.DataValueType is not int dataValueType) {
				continue;
			}

			string? dataValueTypeName = CreatioDataValueType.GetName(dataValueType);
			if (string.IsNullOrEmpty(dataValueTypeName)) {
				continue;
			}

			result[parameter.Name] = new BusinessRuleAttributeDescriptor(
				parameter.Name,
				dataValueTypeName,
				string.IsNullOrWhiteSpace(parameter.ReferenceSchemaName) ? null : parameter.ReferenceSchemaName);
		}

		return result;
	}

	private static Dictionary<string, string> BuildDataSourceEntitySchemas(JsonObject dataSources) {
		Dictionary<string, string> result = new(StringComparer.Ordinal);
		foreach ((string dataSourceName, JsonNode? _) in dataSources) {
			// "PageParameters" is the reserved page-parameter scope, resolved from bundle.Parameters — never
			// as a datasource-column scope — so a datasource with that name must not shadow it.
			if (!string.IsNullOrWhiteSpace(dataSourceName)
				&& !string.Equals(dataSourceName, BusinessRuleConstants.PageParametersScope, StringComparison.Ordinal)
				&& TryResolveEntitySchemaName(dataSources, dataSourceName, out string entitySchemaName)) {
				result[dataSourceName] = entitySchemaName;
			}
		}

		return result;
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

	private static bool HasModelConfigPath(JsonObject attribute) =>
		!string.IsNullOrWhiteSpace(attribute["modelConfig"]?["path"]?.GetValue<string>());

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
	/// Determines the data value type of an unbound/technical page-local attribute (no
	/// <c>modelConfig.path</c>). An explicit numeric <c>dataValueType</c> is honoured; otherwise a
	/// boolean or string default <c>value</c> infers <c>Boolean</c>/<c>Text</c>. Numeric and date/time
	/// defaults are ambiguous as raw JSON and are skipped unless an explicit <c>dataValueType</c> is set.
	/// </summary>
	private static bool TryResolveUnboundAttributeType(JsonObject attribute, out string dataValueTypeName) {
		dataValueTypeName = string.Empty;
		if (attribute["dataValueType"] is JsonValue typeValue && typeValue.TryGetValue(out int typeInt)) {
			string? name = CreatioDataValueType.GetName(typeInt);
			if (!string.IsNullOrEmpty(name)) {
				dataValueTypeName = name;
				return true;
			}
		}

		if (attribute["value"] is JsonValue defaultValue) {
			if (defaultValue.TryGetValue(out bool _)) {
				dataValueTypeName = "Boolean";
				return true;
			}

			if (defaultValue.TryGetValue(out string? _)) {
				dataValueTypeName = "Text";
				return true;
			}
		}

		return false;
	}
}
