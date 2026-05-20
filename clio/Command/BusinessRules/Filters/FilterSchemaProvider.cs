using System;
using System.Collections.Generic;
using Clio.Command.EntitySchemaDesigner;

namespace Clio.Command.BusinessRules.Filters;

/// <summary>
/// Default <see cref="IFilterSchemaProvider"/> backed by <see cref="IRemoteEntitySchemaDesignerClient"/>.
/// Uses <c>UseFullHierarchy = true</c> and an empty <c>PackageUId</c> so any schema can be resolved by
/// name regardless of which package owns it (filter root schemas typically live in base packages,
/// not the caller's package). When the empty-package call yields no schema (e.g. multi-package base
/// lookups like <c>City</c> which exist in CrtCoreBase, SSP, and CrtGoogleAnalytics simultaneously),
/// falls back to <see cref="ISchemaPackageDiscovery"/> to resolve the owning package UId via a
/// SysSchema SelectQuery and retries with that explicit context. Caches the full
/// <see cref="EntityDesignSchemaDto"/> per instance and derives both the column map and primary-
/// display-column name from the same fetch.
/// </summary>
internal sealed class FilterSchemaProvider(
	IRemoteEntitySchemaDesignerClient entitySchemaDesignerClient,
	ISchemaPackageDiscovery schemaPackageDiscovery)
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
		EntityDesignSchemaDto? schema =
			TryFetchSchema(schemaName, Guid.Empty)
			?? TryFetchWithDiscoveredPackage(schemaName);
		if (schema is null) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.PathUnknown,
				"filter.rootSchemaName",
				$"Schema '{schemaName}' could not be resolved on the target environment.");
		}
		_schemaCache[schemaName] = schema;
		return schema;
	}

	private EntityDesignSchemaDto? TryFetchSchema(string schemaName, Guid packageUId) {
		Clio.Command.EntitySchemaDesigner.DesignerResponse<EntityDesignSchemaDto>? response =
			entitySchemaDesignerClient.TryGetSchemaDesignItem(
				new GetSchemaDesignItemRequestDto {
					Name = schemaName,
					PackageUId = packageUId,
					UseFullHierarchy = true,
					Cultures = [EntitySchemaDesignerSupport.DefaultCultureName]
				},
				new RemoteCommandOptions());
		return response?.Schema;
	}

	private EntityDesignSchemaDto? TryFetchWithDiscoveredPackage(string schemaName) {
		Guid? discoveredPackageUId = schemaPackageDiscovery.TryFindRootPackageUId(schemaName);
		if (!discoveredPackageUId.HasValue) {
			return null;
		}
		return TryFetchSchema(schemaName, discoveredPackageUId.Value);
	}
}
