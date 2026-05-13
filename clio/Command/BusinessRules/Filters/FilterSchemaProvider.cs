using System;
using System.Collections.Generic;
using Clio.Command.EntitySchemaDesigner;

namespace Clio.Command.BusinessRules.Filters;

/// <summary>
/// Default <see cref="IFilterSchemaProvider"/> backed by <see cref="IRemoteEntitySchemaDesignerClient"/>.
/// Uses <c>UseFullHierarchy = true</c> and an empty <c>PackageUId</c> so any schema can be resolved by
/// name regardless of which package owns it (filter root schemas typically live in base packages,
/// not the caller's package). Caches results per instance — wired transient so a single rule
/// validation reuses one cache.
/// </summary>
internal sealed class FilterSchemaProvider(
	IRemoteEntitySchemaDesignerClient entitySchemaDesignerClient)
	: IFilterSchemaProvider {

	private readonly Dictionary<string, IReadOnlyDictionary<string, EntitySchemaColumnDto>> _cache =
		new(StringComparer.Ordinal);

	public IReadOnlyDictionary<string, EntitySchemaColumnDto> GetSchemaColumns(string schemaName) {
		ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);
		if (_cache.TryGetValue(schemaName, out IReadOnlyDictionary<string, EntitySchemaColumnDto>? cached)) {
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
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columns =
			BusinessRuleHelpers.BuildColumnMap(response.Schema);
		_cache[schemaName] = columns;
		return columns;
	}
}
