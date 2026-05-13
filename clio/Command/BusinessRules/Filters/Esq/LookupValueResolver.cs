using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using static Clio.Package.SelectQueryHelper;

namespace Clio.Command.BusinessRules.Filters.Esq;

/// <summary>
/// Resolves a Lookup filter value supplied as a display name to its GUID record Id by
/// querying the Creatio DataService SelectQuery endpoint with a filter on the schema's
/// primary display column. Always available on any Creatio instance (no CrtCopilot package
/// required). Caches successful resolutions per instance to avoid duplicate round-trips
/// during a single rule validation.
/// </summary>
internal sealed class LookupValueResolver(
	IFilterSchemaProvider schemaProvider,
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder)
	: ILookupValueResolver {

	private readonly Dictionary<(string Schema, string Value), Guid> _cache = new();

	public Guid Resolve(string schemaName, string displayValue, string fieldPath) {
		ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);
		ArgumentException.ThrowIfNullOrWhiteSpace(displayValue);
		(string Schema, string Value) key = (schemaName, displayValue);
		if (_cache.TryGetValue(key, out Guid cached)) {
			return cached;
		}
		string? displayColumnName = schemaProvider.GetPrimaryDisplayColumnName(schemaName);
		if (string.IsNullOrWhiteSpace(displayColumnName)) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.LookupRecordNotFound,
				fieldPath,
				$"Cannot resolve display-name lookup value on '{schemaName}': the schema has no primary display column. " +
				$"Pass the record Id (GUID) instead of '{displayValue}'.");
		}
		object selectQuery = BuildSelectQuery(
			schemaName,
			[new SelectQueryColumnDefinition("Id", "Id")],
			[new SelectQueryFilterDefinition(displayColumnName, displayValue, TextDataValueType)]);
		LookupResolutionResponseDto response = ExecuteSelectQuery<LookupResolutionResponseDto>(
			applicationClient,
			serviceUrlBuilder,
			selectQuery);
		List<LookupResolutionRowDto> rows = response.Rows ?? [];
		if (rows.Count == 0) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.LookupRecordNotFound,
				fieldPath,
				$"No record found on schema '{schemaName}' where {displayColumnName} = '{displayValue}'. " +
				"Verify the display name or pass the record Id (GUID) directly.");
		}
		if (rows.Count > 1) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.LookupRecordNotFound,
				fieldPath,
				$"Multiple records ({rows.Count}) found on schema '{schemaName}' where {displayColumnName} = '{displayValue}'. " +
				"Display-name resolution is ambiguous; pass the record Id (GUID) directly.");
		}
		if (!Guid.TryParse(rows[0].Id, out Guid resolved)) {
			throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.LookupRecordNotFound,
				fieldPath,
				$"Resolved row for '{displayValue}' on '{schemaName}' did not contain a parsable Id.");
		}
		_cache[key] = resolved;
		return resolved;
	}

	private sealed class LookupResolutionResponseDto : SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<LookupResolutionRowDto>? Rows { get; set; }
	}

	private sealed class LookupResolutionRowDto {
		[JsonPropertyName("Id")]
		public string? Id { get; set; }
	}
}
