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

	private string? TryGetDisplayColumnName(string referenceSchemaName) {
		string key = referenceSchemaName.Trim();
		if (_displayColumnCache.TryGetValue(key, out string? cached)) {
			return cached;
		}
		string? displayColumn = null;
		try {
			RuntimeEntitySchemaResult schema = _runtimeEntitySchemaReader.GetByName(key);
			displayColumn = schema.PrimaryDisplayColumnName;
		} catch (InvalidOperationException ex) {
			// Could not read the referenced schema (missing or access-restricted); treat as no resolvable
			// display column rather than failing the column readback.
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
			LookupRecordRow? row = response.Rows.FirstOrDefault();
			if (row is null) {
				return new LookupDefaultResolution(null, NotFoundMarker);
			}
			return new LookupDefaultResolution(NormalizeDisplayValue(row.DisplayValue), null);
		} catch (InvalidOperationException ex) when (IsAccessDenied(ex.Message)) {
			return new LookupDefaultResolution(null, NoAccessMarker);
		} catch (InvalidOperationException ex) {
			_logger.WriteWarning(
				$"Could not resolve the lookup default display value for '{schemaName}' record '{recordId:D}'. {ex.Message}");
			return new LookupDefaultResolution(null, NotFoundMarker);
		} catch (HttpRequestException ex) {
			_logger.WriteWarning(
				$"Could not resolve the lookup default display value for '{schemaName}' record '{recordId:D}'. {ex.Message}");
			return new LookupDefaultResolution(null, null);
		} catch (JsonException ex) {
			_logger.WriteWarning(
				$"Could not parse the lookup default display value for '{schemaName}' record '{recordId:D}'. {ex.Message}");
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
		[JsonPropertyName("Id")]
		public Guid Id { get; set; }

		[JsonPropertyName("DisplayValue")]
		public string? DisplayValue { get; set; }
	}
}
