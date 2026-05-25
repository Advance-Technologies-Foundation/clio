using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using static Clio.Package.SelectQueryHelper;

namespace Clio.Command.BusinessRules.Filters.Esq;

/// <summary>
/// Resolves Lookup filter values against the Creatio DataService SelectQuery endpoint.
/// Two directions:
/// <list type="bullet">
///   <item><description>display name → <see cref="LookupResolution"/> by filtering the primary
///     display column;</description></item>
///   <item><description>GUID → display name by filtering on <c>Id</c>, used to enrich GUID
///     inputs so the generated business rule's UI renders the lookup name.</description></item>
/// </list>
/// Always available on any Creatio instance (no CrtCopilot package required). Caches both
/// directions independently per instance lifetime to avoid duplicate round-trips during a
/// single rule validation.
/// </summary>
internal sealed class LookupValueResolver(
	IFilterSchemaProvider schemaProvider,
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder)
	: ILookupValueResolver {

	private const string IdColumnName = "Id";

	private readonly Dictionary<(string Schema, string Value), LookupResolution> _displayToIdCache =
		new();
	private readonly Dictionary<(string Schema, Guid Id), string?> _idToDisplayCache =
		new();

	public LookupResolution Resolve(string schemaName, string displayValue, string fieldPath) {
		ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);
		ArgumentException.ThrowIfNullOrWhiteSpace(displayValue);
		(string Schema, string Value) key = (schemaName, displayValue);
		if (_displayToIdCache.TryGetValue(key, out LookupResolution? cached)) {
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
			[new SelectQueryColumnDefinition(IdColumnName, IdColumnName)],
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
		LookupResolution resolution = new(resolved, displayValue);
		_displayToIdCache[key] = resolution;
		_idToDisplayCache[(schemaName, resolved)] = displayValue;
		return resolution;
	}

	public string? TryResolveDisplayName(string schemaName, Guid id) {
		ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);
		if (id == Guid.Empty) {
			return null;
		}
		(string Schema, Guid Id) key = (schemaName, id);
		if (_idToDisplayCache.TryGetValue(key, out string? cached)) {
			return cached;
		}
		string? displayColumnName = schemaProvider.GetPrimaryDisplayColumnName(schemaName);
		if (string.IsNullOrWhiteSpace(displayColumnName)) {
			_idToDisplayCache[key] = null;
			return null;
		}
		// Note: SelectQueryFilterDefinition default ComparisonType=3 (Equal), DataValueType
		// argument here is the column type, not the value type — Id columns are GuidDataValueType.
		object selectQuery = BuildSelectQuery(
			schemaName,
			[
				new SelectQueryColumnDefinition(IdColumnName, IdColumnName),
				new SelectQueryColumnDefinition(displayColumnName, "DisplayName")
			],
			[new SelectQueryFilterDefinition(IdColumnName, id.ToString(), GuidDataValueType)]);
		IdToDisplayResponseDto response;
		try {
			response = ExecuteSelectQuery<IdToDisplayResponseDto>(
				applicationClient,
				serviceUrlBuilder,
				selectQuery);
		} catch {
			// Display-name enrichment is best-effort: a network blip or transient server error
			// must not block rule creation when we already have a valid GUID. Cache miss and
			// fall through; the lookup will still match by Id at runtime.
			_idToDisplayCache[key] = null;
			return null;
		}
		List<IdToDisplayRowDto> rows = response.Rows ?? [];
		string? displayName = rows.Count == 1 ? rows[0].DisplayName : null;
		_idToDisplayCache[key] = displayName;
		return displayName;
	}

	private sealed class LookupResolutionResponseDto : SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<LookupResolutionRowDto>? Rows { get; set; }
	}

	private sealed class LookupResolutionRowDto {
		[JsonPropertyName("Id")]
		public string? Id { get; set; }
	}

	private sealed class IdToDisplayResponseDto : SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<IdToDisplayRowDto>? Rows { get; set; }
	}

	private sealed class IdToDisplayRowDto {
		[JsonPropertyName("Id")]
		public string? Id { get; set; }

		[JsonPropertyName("DisplayName")]
		public string? DisplayName { get; set; }
	}
}
