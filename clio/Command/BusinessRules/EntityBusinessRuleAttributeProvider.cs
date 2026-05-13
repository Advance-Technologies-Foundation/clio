using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.EntitySchemaDesigner;

namespace Clio.Command.BusinessRules;

internal interface IEntityBusinessRuleAttributeProvider {
	EntityBusinessRuleAttributeContext GetAttributes(string entitySchemaName, Guid packageUId);
}

internal sealed record EntityBusinessRuleAttributeContext(
	EntityDesignSchemaDto EntitySchema,
	IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> Attributes);

internal sealed class EntityBusinessRuleAttributeProvider(
	IEntityBusinessRuleSchemaProvider schemaProvider)
	: IEntityBusinessRuleAttributeProvider {

	public EntityBusinessRuleAttributeContext GetAttributes(string entitySchemaName, Guid packageUId) {
		EntityDesignSchemaDto entitySchema = schemaProvider.GetSchema(entitySchemaName, packageUId);
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributes =
			new EntityBusinessRuleAttributeDescriptorMap(entitySchema, packageUId, schemaProvider);
		return new EntityBusinessRuleAttributeContext(entitySchema, attributes);
	}
}

internal sealed class EntityBusinessRuleAttributeDescriptorMap(
	EntityDesignSchemaDto rootSchema,
	Guid packageUId,
	IEntityBusinessRuleSchemaProvider schemaProvider)
	: IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> {

	private readonly Dictionary<string, IReadOnlyDictionary<string, EntitySchemaColumnDto>> _columnMaps =
		new(StringComparer.Ordinal) {
			[rootSchema.Name ?? string.Empty] = BusinessRuleHelpers.BuildColumnMap(rootSchema)
		};

	private readonly Dictionary<string, EntityDesignSchemaDto> _schemas =
		new(StringComparer.Ordinal) {
			[rootSchema.Name ?? string.Empty] = rootSchema
		};

	private IReadOnlyDictionary<string, EntitySchemaColumnDto> RootColumns =>
		_columnMaps[rootSchema.Name ?? string.Empty];

	public IEnumerable<string> Keys => RootColumns.Keys;

	public IEnumerable<BusinessRuleAttributeDescriptor> Values =>
		RootColumns.Select(pair => BusinessRuleHelpers.BuildAttributeDescriptor(pair.Key, pair.Value));

	public int Count => RootColumns.Count;

	public BusinessRuleAttributeDescriptor this[string key] =>
		TryGetValue(key, out BusinessRuleAttributeDescriptor? value)
			? value
			: throw new KeyNotFoundException($"Attribute '{key}' was not found.");

	public bool ContainsKey(string key) => TryGetValue(key, out _);

	public bool TryGetValue(string key, out BusinessRuleAttributeDescriptor value) {
		if (string.IsNullOrWhiteSpace(key)) {
			value = default!;
			return false;
		}

		string[] pathSegments = key.Split('.', StringSplitOptions.RemoveEmptyEntries);
		if (pathSegments.Length == 0 || string.Join(".", pathSegments) != key) {
			value = default!;
			return false;
		}

		IReadOnlyDictionary<string, EntitySchemaColumnDto> currentColumns = RootColumns;
		for (int index = 0; index < pathSegments.Length; index++) {
			string segment = pathSegments[index];
			if (!currentColumns.TryGetValue(segment, out EntitySchemaColumnDto? column)) {
				value = default!;
				return false;
			}

			bool isLastSegment = index == pathSegments.Length - 1;
			if (isLastSegment) {
				value = BusinessRuleHelpers.BuildAttributeDescriptor(key, column);
				return true;
			}

			string? referenceSchemaName = column.ReferenceSchema?.Name;
			if (string.IsNullOrWhiteSpace(referenceSchemaName)) {
				value = default!;
				return false;
			}

			currentColumns = GetColumnMap(referenceSchemaName);
		}

		value = default!;
		return false;
	}

	public IEnumerator<KeyValuePair<string, BusinessRuleAttributeDescriptor>> GetEnumerator() {
		foreach (KeyValuePair<string, EntitySchemaColumnDto> pair in RootColumns) {
			yield return new KeyValuePair<string, BusinessRuleAttributeDescriptor>(
				pair.Key,
				BusinessRuleHelpers.BuildAttributeDescriptor(pair.Key, pair.Value));
		}
	}

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	private IReadOnlyDictionary<string, EntitySchemaColumnDto> GetColumnMap(string schemaName) {
		if (_columnMaps.TryGetValue(schemaName, out IReadOnlyDictionary<string, EntitySchemaColumnDto>? columns)) {
			return columns;
		}

		EntityDesignSchemaDto schema = GetSchema(schemaName);
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = BusinessRuleHelpers.BuildColumnMap(schema);
		_columnMaps[schemaName] = columnMap;
		return columnMap;
	}

	private EntityDesignSchemaDto GetSchema(string schemaName) {
		if (_schemas.TryGetValue(schemaName, out EntityDesignSchemaDto? schema)) {
			return schema;
		}

		schema = schemaProvider.GetSchema(schemaName, packageUId);
		_schemas[schemaName] = schema;
		return schema;
	}
}
