using System;
using System.Collections.Generic;
using Clio.Command.EntitySchemaDesigner;

namespace Clio.Command.BusinessRules.Filters;

/// <summary>
/// Default <see cref="IFilterSchemaProvider"/> backed by <see cref="IRemoteEntitySchemaDesignerClient"/>.
/// Uses <c>UseFullHierarchy = true</c> and an empty <c>PackageUId</c> so any schema can be resolved by
/// name regardless of which package owns it (filter root schemas typically live in base packages,
/// not the caller's package). Caches the full <see cref="EntityDesignSchemaDto"/> per instance and
/// derives both the column map and primary-display-column name from the same fetch.
/// </summary>
internal sealed class FilterSchemaProvider(
	IRemoteEntitySchemaDesignerClient entitySchemaDesignerClient)
	: IFilterSchemaProvider {

	private readonly Dictionary<string, EntityDesignSchemaDto> _schemaCache =
		new(StringComparer.Ordinal);

	public IReadOnlyDictionary<string, EntitySchemaColumnDto> GetSchemaColumns(string schemaName) {
		EntityDesignSchemaDto schema = LoadSchema(schemaName);
		return BusinessRuleHelpers.BuildColumnMap(schema);
	}

	public string? GetPrimaryDisplayColumnName(string schemaName) {
		EntityDesignSchemaDto schema = LoadSchema(schemaName);
		return schema.PrimaryDisplayColumn?.Name;
	}

	private EntityDesignSchemaDto LoadSchema(string schemaName) {
		ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);
		if (_schemaCache.TryGetValue(schemaName, out EntityDesignSchemaDto? cached)) {
			return cached;
		}
		Clio.Command.EntitySchemaDesigner.DesignerResponse<EntityDesignSchemaDto>? response =
			entitySchemaDesignerClient.TryGetSchemaDesignItem(
				new GetSchemaDesignItemRequestDto {
					Name = schemaName,
					PackageUId = Guid.Empty,
					UseFullHierarchy = true,
					Cultures = [EntitySchemaDesignerSupport.DefaultCultureName]
				},
				new RemoteCommandOptions());
		if (response?.Schema is null) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.PathUnknown,
				"filter.rootSchemaName",
				$"Schema '{schemaName}' could not be resolved on the target environment.");
		}
		_schemaCache[schemaName] = response.Schema;
		return response.Schema;
	}
}
