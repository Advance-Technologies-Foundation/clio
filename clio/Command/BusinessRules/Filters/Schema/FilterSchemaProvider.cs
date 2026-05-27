using System;
using System.Collections.Generic;
using Clio.Command.EntitySchemaDesigner;
using static Clio.Command.BusinessRules.BusinessRuleHelpers;

namespace Clio.Command.BusinessRules.Filters.Schema;

/// <summary>
/// Caches per-instance column metadata for schemas reachable through the apply-static-filter root.
/// </summary>
internal sealed class FilterSchemaProvider : IFilterSchemaProvider {

	private readonly IEntityBusinessRuleSchemaProvider _schemaProvider;
	private Guid _packageUId;
	private bool _initialized;
	private readonly Dictionary<string, IReadOnlyDictionary<string, FilterSchemaColumn>> _columnCache =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, string?> _primaryDisplayColumnCache = new(StringComparer.Ordinal);

	public FilterSchemaProvider(IEntityBusinessRuleSchemaProvider schemaProvider) {
		_schemaProvider = schemaProvider ?? throw new ArgumentNullException(nameof(schemaProvider));
	}

	/// <summary>Initialize with the package scope and an optional pre-fetched root schema to avoid an extra fetch.</summary>
	public void Initialize(Guid packageUId, EntityDesignSchemaDto? rootSchema = null) {
		_packageUId = packageUId;
		_initialized = true;
		if (rootSchema is not null) {
			_columnCache[rootSchema.Name ?? string.Empty] = BuildColumns(rootSchema);
			_primaryDisplayColumnCache[rootSchema.Name ?? string.Empty] = rootSchema.PrimaryDisplayColumn?.Name;
		}
	}

	public IReadOnlyDictionary<string, FilterSchemaColumn> GetColumns(string schemaName) {
		if (_columnCache.TryGetValue(schemaName, out IReadOnlyDictionary<string, FilterSchemaColumn>? cached)) {
			return cached;
		}

		EnsureInitialized();
		EntityDesignSchemaDto schema = _schemaProvider.GetSchema(schemaName, _packageUId);
		IReadOnlyDictionary<string, FilterSchemaColumn> columns = BuildColumns(schema);
		_columnCache[schemaName] = columns;
		_primaryDisplayColumnCache[schemaName] = schema.PrimaryDisplayColumn?.Name;
		return columns;
	}

	public string? GetPrimaryDisplayColumnName(string schemaName) {
		if (!_primaryDisplayColumnCache.TryGetValue(schemaName, out string? cached)) {
			_ = GetColumns(schemaName);
			cached = _primaryDisplayColumnCache[schemaName];
		}

		return cached;
	}

	private void EnsureInitialized() {
		if (!_initialized) {
			throw new InvalidOperationException(
				"FilterSchemaProvider must be initialized with the package context before remote schema fetches.");
		}
	}

	private static IReadOnlyDictionary<string, FilterSchemaColumn> BuildColumns(EntityDesignSchemaDto schema) {
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = BuildColumnMap(schema);
		Dictionary<string, FilterSchemaColumn> result = new(StringComparer.Ordinal);
		foreach (KeyValuePair<string, EntitySchemaColumnDto> pair in columnMap) {
			result[pair.Key] = new FilterSchemaColumn {
				Name = pair.Key,
				DataValueTypeName = MapDataValueTypeName(pair.Value.DataValueType),
				ReferenceSchemaName = pair.Value.ReferenceSchema?.Name
			};
		}

		return result;
	}
}
