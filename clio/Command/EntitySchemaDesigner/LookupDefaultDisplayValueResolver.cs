using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.Common.EntitySchema;
using Clio.Package;

namespace Clio.Command.EntitySchemaDesigner;

/// <summary>
/// Resolves the display value of the referenced record for a lookup <c>Const</c> default so a
/// machine consumer (AI no-code agent) can verify which record a default GUID points to without
/// issuing its own follow-up query.
/// </summary>
/// <remarks>
/// The designer read path (<see cref="RemoteEntitySchemaColumnManager.GetColumnProperties"/>) returns
/// a lookup <c>Const</c> default as a bare GUID. This resolver performs the one extra data-plane query
/// the readback predicate needs for its display-value component. It is intentionally <b>fail-soft</b>:
/// any expected failure degrades to a record-resolution marker (never an exception), so enrichment can
/// never make the readback fail relative to the GUID-only behavior it augments.
/// </remarks>
internal interface ILookupDefaultDisplayValueResolver
{
	/// <summary>
	/// Resolves the display value of the referenced record for a lookup <c>Const</c> default.
	/// </summary>
	/// <param name="referenceSchemaName">Name of the referenced lookup entity schema.</param>
	/// <param name="recordId">Identifier of the default record.</param>
	/// <param name="options">Remote command options identifying the target environment.</param>
	/// <returns>
	/// A <see cref="LookupDefaultResolution"/> carrying the display value, or an honest marker when it
	/// cannot be resolved. Both properties null means enrichment degraded silently and the readback
	/// stays GUID-only (no regression).
	/// </returns>
	LookupDefaultResolution Resolve(string referenceSchemaName, Guid recordId, RemoteCommandOptions options);

	/// <summary>
	/// Batch counterpart of <see cref="Resolve"/>: resolves many referenced records of ONE reference schema to their
	/// display values in a single chunked <c>IN</c> query per batch — never one query per id — so a caller with dozens
	/// of ids stays within its request budget. Returns one <see cref="LookupDefaultResolution"/> per DISTINCT non-empty
	/// id, keyed by <see cref="Guid"/> (so brace/case lexical form on either side cannot cause a miss), with the same
	/// fail-soft contract and markers as <see cref="Resolve"/>. An empty or all-empty <paramref name="recordIds"/>
	/// yields an empty map.
	/// </summary>
	/// <param name="referenceSchemaName">Name of the referenced lookup entity schema shared by all ids.</param>
	/// <param name="recordIds">Identifiers of the records to resolve.</param>
	/// <param name="options">Remote command options identifying the target environment.</param>
	/// <returns>A map from each distinct non-empty id to its <see cref="LookupDefaultResolution"/>.</returns>
	IReadOnlyDictionary<Guid, LookupDefaultResolution> ResolveMany(
		string referenceSchemaName, IReadOnlyCollection<Guid> recordIds, RemoteCommandOptions options);
}

/// <summary>
/// Outcome of a lookup-default display-value resolution.
/// </summary>
/// <param name="DisplayValue">
/// The referenced record's display value (its primary display column, resolved in the connected user's
/// session culture), or <see langword="null"/> when unavailable.
/// </param>
/// <param name="RecordResolution">
/// Honest marker when the display value is unavailable:
/// <c>no-access</c> (schema-level read denial on the referenced entity),
/// <c>not-found-or-no-access</c> (query succeeded but returned no row — deleted vs row-level-hidden are
/// indistinguishable), or <c>display-column-unavailable</c> (the referenced schema exposes no resolvable
/// display column, e.g. an <c>ImageLookup</c> → <c>SysImage</c> reference). <see langword="null"/> when a
/// display value is present or enrichment did not apply.
/// </param>
internal sealed record LookupDefaultResolution(string? DisplayValue, string? RecordResolution);

/// <summary>
/// Default <see cref="ILookupDefaultDisplayValueResolver"/> implementation. Discovers the referenced
/// schema's display column via the by-name runtime reader, then reads the record's display value with a
/// DataService <c>SelectQuery</c> through <see cref="IApplicationClient"/> (the always-available transport
/// already used by the entity-schema designer module). Results are cached per referenced schema for the
/// lifetime of the resolver (one command execution, since the resolver is registered transient).
/// </summary>
internal sealed class LookupDefaultDisplayValueResolver : ILookupDefaultDisplayValueResolver
{
	/// <summary>Marker: schema-level read denial on the referenced entity.</summary>
	internal const string NoAccessMarker = "no-access";

	/// <summary>Marker: the query succeeded but returned no row (deleted or row-level hidden).</summary>
	internal const string NotFoundMarker = "not-found-or-no-access";

	/// <summary>Marker: the referenced schema exposes no resolvable display column.</summary>
	internal const string DisplayColumnUnavailableMarker = "display-column-unavailable";

	private const string DisplayValueAlias = "DisplayValue";
	private const string IdColumnPath = "Id";

	/// <summary>
	/// Max ids per batched <c>IN</c> query. Every id is one query parameter and MSSql caps a statement at 2100, so
	/// a larger id set is split across queries whose results are unioned. Kept well under the ceiling.
	/// </summary>
	private const int MaxIdsPerQuery = 400;

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly IRuntimeEntitySchemaReader _runtimeEntitySchemaReader;
	private readonly ILogger _logger;
	private readonly Dictionary<string, string?> _displayColumnCache = new(StringComparer.OrdinalIgnoreCase);

	public LookupDefaultDisplayValueResolver(
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder,
		IRuntimeEntitySchemaReader runtimeEntitySchemaReader,
		ILogger logger) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_runtimeEntitySchemaReader = runtimeEntitySchemaReader;
		_logger = logger;
	}

	/// <inheritdoc />
	public LookupDefaultResolution Resolve(string referenceSchemaName, Guid recordId, RemoteCommandOptions options) {
		if (string.IsNullOrWhiteSpace(referenceSchemaName) || recordId == Guid.Empty) {
			return new LookupDefaultResolution(null, null);
		}
		string? displayColumn = TryGetDisplayColumnName(referenceSchemaName);
		if (string.IsNullOrWhiteSpace(displayColumn)) {
			return new LookupDefaultResolution(null, DisplayColumnUnavailableMarker);
		}
		return QueryDisplayValue(referenceSchemaName.Trim(), displayColumn!, recordId, options);
	}

	/// <inheritdoc />
	public IReadOnlyDictionary<Guid, LookupDefaultResolution> ResolveMany(
		string referenceSchemaName, IReadOnlyCollection<Guid> recordIds, RemoteCommandOptions options) {
		var result = new Dictionary<Guid, LookupDefaultResolution>();
		if (string.IsNullOrWhiteSpace(referenceSchemaName) || recordIds is null) {
			return result;
		}
		Guid[] distinctIds = recordIds.Where(id => id != Guid.Empty).Distinct().ToArray();
		if (distinctIds.Length == 0) {
			return result;
		}
		string? displayColumn = TryGetDisplayColumnName(referenceSchemaName);
		if (string.IsNullOrWhiteSpace(displayColumn)) {
			// No resolvable display column: mark every requested id, exactly as the single Resolve does.
			foreach (Guid id in distinctIds) {
				result[id] = new LookupDefaultResolution(null, DisplayColumnUnavailableMarker);
			}
			return result;
		}
		foreach (Guid[] chunk in distinctIds.Chunk(MaxIdsPerQuery)) {
			ResolveChunk(referenceSchemaName.Trim(), displayColumn!, chunk, options, result);
		}
		return result;
	}

	private void ResolveChunk(
		string schemaName, string displayColumn, Guid[] chunk, RemoteCommandOptions options,
		Dictionary<Guid, LookupDefaultResolution> result) {
		object query = SelectQueryHelper.BuildSelectQueryWithOrFilter(
			schemaName,
			[
				new SelectQueryHelper.SelectQueryColumnDefinition(IdColumnPath, IdColumnPath),
				new SelectQueryHelper.SelectQueryColumnDefinition(displayColumn, DisplayValueAlias)
			],
			IdColumnPath,
			chunk.Select(id => id.ToString("D")).ToList(),
			SelectQueryHelper.GuidDataValueType,
			rowCount: chunk.Length);
		try {
			LookupRecordSelectResponse response = SelectQueryHelper.ExecuteSelectQuery<LookupRecordSelectResponse>(
				_applicationClient, _serviceUrlBuilder, query, options.TimeOut);
			var found = new Dictionary<Guid, string>();
			foreach (LookupRecordRow row in response.Rows ?? []) {
				// Parse the returned Id to Guid ourselves (not via a typed Guid property) so ANY lexical form the
				// endpoint returns — braces, parentheses, or upper case — still keys correctly and matches the
				// requested id, instead of missing and silently degrading every record to GUID-only.
				string? display = NormalizeDisplayValue(row.DisplayValue);
				if (display is not null && Guid.TryParse(row.Id, out Guid id)) {
					found[id] = display;
				}
			}
			foreach (Guid id in chunk) {
				result[id] = found.TryGetValue(id, out string? display)
					? new LookupDefaultResolution(display, null)
					: new LookupDefaultResolution(null, NotFoundMarker);
			}
		} catch (NonJsonServiceResponseException ex) {
			// Non-JSON/empty body says nothing about these records; degrade to GUID-only (no marker), as Resolve does.
			_logger.WriteWarning($"Could not resolve lookup display values for '{schemaName}'. {ex.Message}");
			MarkChunk(chunk, result, marker: null);
		} catch (InvalidOperationException ex) when (IsAccessDenied(ex.Message)) {
			MarkChunk(chunk, result, NoAccessMarker);
		} catch (InvalidOperationException ex) {
			_logger.WriteWarning($"Could not resolve lookup display values for '{schemaName}'. {ex.Message}");
			MarkChunk(chunk, result, NotFoundMarker);
		} catch (Exception ex) when (ex is HttpRequestException
				or System.Net.WebException
				or System.Threading.Tasks.TaskCanceledException
				or JsonException) {
			// Transport, timeout, or malformed-response fault: degrade to GUID-only so enrichment never fails.
			_logger.WriteWarning($"Could not resolve lookup display values for '{schemaName}'. {ex.Message}");
			MarkChunk(chunk, result, marker: null);
		}
	}

	private static void MarkChunk(Guid[] chunk, Dictionary<Guid, LookupDefaultResolution> result, string? marker) {
		foreach (Guid id in chunk) {
			result[id] = new LookupDefaultResolution(null, marker);
		}
	}

	private string? TryGetDisplayColumnName(string referenceSchemaName) {
		string key = referenceSchemaName.Trim();
		if (_displayColumnCache.TryGetValue(key, out string? cached)) {
			return cached;
		}
		string? displayColumn = null;
		try {
			RuntimeEntitySchemaResult schema = _runtimeEntitySchemaReader.GetByName(key);
			displayColumn = schema.PrimaryDisplayColumnName;
		} catch (Exception ex) when (ex is InvalidOperationException
				or HttpRequestException
				or System.Net.WebException
				or System.Threading.Tasks.TaskCanceledException
				or JsonException) {
			// Could not read the referenced schema (missing, access-restricted, transport/timeout, or a
			// malformed response); treat as no resolvable display column rather than failing the column readback.
			_logger.WriteWarning(
				$"Could not determine the display column of referenced schema '{key}'. {ex.Message}");
		}
		_displayColumnCache[key] = displayColumn;
		return displayColumn;
	}

	private LookupDefaultResolution QueryDisplayValue(
		string schemaName,
		string displayColumn,
		Guid recordId,
		RemoteCommandOptions options) {
		object query = SelectQueryHelper.BuildSelectQuery(
			schemaName,
			[new SelectQueryHelper.SelectQueryColumnDefinition(displayColumn, DisplayValueAlias)],
			[
				new SelectQueryHelper.SelectQueryFilterDefinition(
					IdColumnPath,
					recordId.ToString("D"),
					SelectQueryHelper.GuidDataValueType)
			],
			rowCount: 1);
		try {
			LookupRecordSelectResponse response = SelectQueryHelper.ExecuteSelectQuery<LookupRecordSelectResponse>(
				_applicationClient,
				_serviceUrlBuilder,
				query,
				options.TimeOut);
			LookupRecordRow? row = response.Rows?.FirstOrDefault();
			if (row is null) {
				return new LookupDefaultResolution(null, NotFoundMarker);
			}
			return new LookupDefaultResolution(NormalizeDisplayValue(row.DisplayValue), null);
		} catch (NonJsonServiceResponseException ex) {
			// The endpoint answered with a non-JSON or empty body, so it said nothing about this record.
			// Degrade to a GUID-only resolution (no marker) exactly as a transport fault does — claiming
			// not-found-or-no-access here would assert something the response never stated.
			_logger.WriteWarning(
				$"Could not resolve the lookup default display value for '{schemaName}' record '{recordId:D}'. {ex.Message}");
			return new LookupDefaultResolution(null, null);
		} catch (InvalidOperationException ex) when (IsAccessDenied(ex.Message)) {
			return new LookupDefaultResolution(null, NoAccessMarker);
		} catch (InvalidOperationException ex) {
			_logger.WriteWarning(
				$"Could not resolve the lookup default display value for '{schemaName}' record '{recordId:D}'. {ex.Message}");
			return new LookupDefaultResolution(null, NotFoundMarker);
		} catch (Exception ex) when (ex is HttpRequestException
				or System.Net.WebException
				or System.Threading.Tasks.TaskCanceledException
				or JsonException) {
			// Transport, timeout, or malformed-response fault: degrade to a GUID-only resolution so enrichment
			// can never make the column readback fail.
			_logger.WriteWarning(
				$"Could not resolve the lookup default display value for '{schemaName}' record '{recordId:D}'. {ex.Message}");
			return new LookupDefaultResolution(null, null);
		}
	}

	private static string? NormalizeDisplayValue(string? value) {
		return string.IsNullOrWhiteSpace(value) ? null : value;
	}

	private static bool IsAccessDenied(string? message) {
		if (string.IsNullOrEmpty(message)) {
			return false;
		}
		return message.IndexOf("does not have permission", StringComparison.OrdinalIgnoreCase) >= 0
			|| message.IndexOf("SecurityException", StringComparison.OrdinalIgnoreCase) >= 0
			|| message.IndexOf("not have rights", StringComparison.OrdinalIgnoreCase) >= 0
			|| message.IndexOf("access denied", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private sealed class LookupRecordSelectResponse : SelectQueryHelper.SelectQueryResponseBaseDto
	{
		[JsonPropertyName("rows")]
		public LookupRecordRow[] Rows { get; set; } = [];
	}

	private sealed class LookupRecordRow
	{
		// Kept as a string (not a typed Guid) because System.Text.Json only parses the plain 'D' Guid form; the batch
		// path parses this with Guid.TryParse so a braced/parenthesised/upper-case Id from the endpoint still matches.
		[JsonPropertyName("Id")]
		public string? Id { get; set; }

		[JsonPropertyName("DisplayValue")]
		public string? DisplayValue { get; set; }
	}
}
