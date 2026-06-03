using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.Package;

namespace Clio.Command.BusinessRules.Filters.Schema;

/// <summary>
/// Resolves a non-GUID Lookup display name to a record GUID by querying the lookup's primary display column
/// via DataService/json/SyncReply/SelectQuery. Caches positive resolutions per instance.
/// </summary>
internal sealed class LookupValueResolver : ILookupValueResolver {

	private readonly IFilterSchemaProvider _schemaProvider;
	private readonly IApplicationClient _client;
	private readonly IServiceUrlBuilder _urlBuilder;
	private readonly Dictionary<(string Schema, string Display), Guid> _cache = [];
	private readonly Dictionary<(string Schema, Guid Id), string?> _reverseCache = [];

	public LookupValueResolver(
		IFilterSchemaProvider schemaProvider,
		IApplicationClient client,
		IServiceUrlBuilder urlBuilder) {
		_schemaProvider = schemaProvider ?? throw new ArgumentNullException(nameof(schemaProvider));
		_client = client ?? throw new ArgumentNullException(nameof(client));
		_urlBuilder = urlBuilder ?? throw new ArgumentNullException(nameof(urlBuilder));
	}

	public Guid ResolveIdByDisplayName(string referenceSchemaName, string displayName) {
		(string Schema, string Display) cacheKey = (referenceSchemaName, displayName);
		if (_cache.TryGetValue(cacheKey, out Guid cached)) {
			return cached;
		}

		string? primaryDisplayColumn = _schemaProvider.GetPrimaryDisplayColumnName(referenceSchemaName);
		if (string.IsNullOrEmpty(primaryDisplayColumn)) {
			throw new ArgumentException(
				$"filter: cannot resolve display name '{displayName}' on schema '{referenceSchemaName}' — primary display column is not defined.");
		}

		object query = SelectQueryHelper.BuildSelectQuery(
			referenceSchemaName,
			[new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id")],
			[new SelectQueryHelper.SelectQueryFilterDefinition(
				primaryDisplayColumn,
				displayName,
				SelectQueryHelper.TextDataValueType,
				ComparisonType: 3)]);

		LookupResolveResponseDto response = SelectQueryHelper.ExecuteSelectQuery<LookupResolveResponseDto>(
			_client, _urlBuilder, query);

		List<LookupResolveRow> rows = response.Rows?.ToList() ?? [];
		if (rows.Count == 0) {
			throw new ArgumentException(
				$"filter: lookup value '{displayName}' was not found on schema '{referenceSchemaName}' (column '{primaryDisplayColumn}'). "
				+ "The match is exact against the stored display value — a localized or differently-spelled term (e.g. a non-English prompt) will not match. "
				+ $"Use odata-read on '{referenceSchemaName}' to find the actual stored value or its Id, then pass that exact value or the GUID.");
		}

		if (rows.Count > 1) {
			throw new ArgumentException(
				$"filter: lookup value '{displayName}' is ambiguous on schema '{referenceSchemaName}' ({rows.Count} matches found). "
				+ $"Use odata-read on '{referenceSchemaName}' to pick the intended record and pass its GUID.");
		}

		if (!Guid.TryParse(rows[0].Id, out Guid id)) {
			throw new ArgumentException(
				$"filter: lookup value '{displayName}' on schema '{referenceSchemaName}' returned a non-GUID Id.");
		}

		_cache[cacheKey] = id;
		return id;
	}

	public bool TryResolveDisplayNameById(string referenceSchemaName, Guid id, out string? displayName) {
		(string Schema, Guid Id) cacheKey = (referenceSchemaName, id);
		if (_reverseCache.TryGetValue(cacheKey, out string? cached)) {
			displayName = cached;
			return displayName is not null;
		}

		string? primaryDisplayColumn = _schemaProvider.GetPrimaryDisplayColumnName(referenceSchemaName);
		if (string.IsNullOrEmpty(primaryDisplayColumn)) {
			_reverseCache[cacheKey] = null;
			displayName = null;
			return false;
		}

		object query = SelectQueryHelper.BuildSelectQuery(
			referenceSchemaName,
			[new SelectQueryHelper.SelectQueryColumnDefinition("Display", primaryDisplayColumn)],
			[new SelectQueryHelper.SelectQueryFilterDefinition(
				"Id",
				id.ToString("D"),
				SelectQueryHelper.GuidDataValueType,
				ComparisonType: 3)]);

		try {
			LookupReverseResolveResponseDto response = SelectQueryHelper.ExecuteSelectQuery<LookupReverseResolveResponseDto>(
				_client, _urlBuilder, query);
			string? value = response.Rows?.FirstOrDefault()?.Display;
			_reverseCache[cacheKey] = value;
			displayName = value;
			return !string.IsNullOrEmpty(value);
		} catch {
			// Reverse lookup is opportunistic enrichment; on transport / parse failure we fall back to the GUID-only form.
			_reverseCache[cacheKey] = null;
			displayName = null;
			return false;
		}
	}

	private sealed class LookupReverseResolveResponseDto : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<LookupReverseResolveRow>? Rows { get; set; }
	}

	private sealed class LookupReverseResolveRow {
		[JsonPropertyName("Display")]
		public string? Display { get; set; }
	}

	private sealed class LookupResolveResponseDto : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<LookupResolveRow>? Rows { get; set; }
	}

	private sealed class LookupResolveRow {
		[JsonPropertyName("Id")]
		public string? Id { get; set; }
	}
}
