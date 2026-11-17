using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Clio.Package;

namespace Clio.Common.ObjectRights;

/// <summary>
/// The default column permission row: what the service stores for a column of the object that a save with a column
/// list does not name — one row, "All employees" with edit, at position 0. A column whose only row is this one is not
/// restricted; <c>GetAdministratedObject</c> omits it while column permissions are on.
/// </summary>
public static class ColumnRightsDefaults {

	/// <summary>The default row: All employees, edit, position 0.</summary>
	public static ColumnRightRow Row { get; } = new(SysAdminUnitIds.AllEmployees, "All employees", 0, ColumnRightLevel.Edit);

	/// <summary>Whether <paramref name="rows"/> is exactly the default row (its position is not compared).</summary>
	/// <param name="rows">A column's stored rows.</param>
	/// <returns><see langword="true"/> for one row of All employees with edit.</returns>
	public static bool IsDefaultOnly(IReadOnlyList<ColumnRightRow> rows) =>
		rows.Count == 1 && rows[0].GranteeId == SysAdminUnitIds.AllEmployees && rows[0].Level == ColumnRightLevel.Edit;
}

/// <summary>
/// Reads the column permission rows an object has in STORAGE: the view <c>VwSysEntitySchemaColumnRight</c>, through
/// DataService. <c>GetAdministratedObject</c> filters what it returns (a column whose only row is the default is omitted
/// while the switch is on, and no column at all while it is off), so storage is what a save is built from and checked
/// against. The switch itself is read from <c>GetAdministratedObject</c>.
/// </summary>
public interface IColumnRightsStorageReader {
	/// <summary>
	/// Reads every stored column permission row of the object whose base schema is <paramref name="schemaUId"/>,
	/// grouped per column (by <c>SubjectColumnUId</c>), each column's rows in stored position order. A column with no
	/// stored row is not listed. Captions are not stored; they are left empty.
	/// </summary>
	/// <param name="schemaUId">The uId <c>GetAdministratedObject</c> returned for the object, which is the stored rows'
	/// <c>SubjectSchemaUId</c> (measured on multi-layer schemas too).</param>
	/// <param name="requestOptions">Timeout, retry and deadline settings.</param>
	/// <param name="verifyCount">Count the rows first (one more request) and refuse an answer with fewer rows than the
	/// count — a cap the server applies below the requested one — or more than <c>10000</c>. A read that plans a save
	/// counts; a read-back may skip it, since a short read-back fails the comparison anyway.</param>
	/// <returns>The stored rows per column.</returns>
	/// <exception cref="Exception">A service failure is thrown: an empty answer never stands for a read that failed.
	/// <see cref="InvalidOperationException"/> when the answer may be cut off.</exception>
	IReadOnlyList<ColumnRights> ReadStoredColumns(Guid schemaUId, CreatioRequestOptions requestOptions,
		bool verifyCount = true);
}

/// <inheritdoc />
public sealed class ColumnRightsStorageReader : IColumnRightsStorageReader {

	internal const string StorageView = "VwSysEntitySchemaColumnRight";
	internal const int RowCap = 10000;

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _urlBuilder;

	/// <summary>Creates the reader.</summary>
	/// <param name="applicationClient">The authenticated application client.</param>
	/// <param name="urlBuilder">Builds the DataService route.</param>
	public ColumnRightsStorageReader(IApplicationClient applicationClient, IServiceUrlBuilder urlBuilder) {
		_applicationClient = applicationClient;
		_urlBuilder = urlBuilder;
	}

	/// <inheritdoc />
	public IReadOnlyList<ColumnRights> ReadStoredColumns(Guid schemaUId, CreatioRequestOptions requestOptions,
		bool verifyCount = true) {
		int? count = verifyCount ? CountStoredRows(schemaUId, requestOptions) : null;
		if (count > RowCap) {
			throw new InvalidOperationException($"{StorageView} holds {count} rows for schema {schemaUId}, more than "
				+ $"the {RowCap} this read takes.");
		}
		// The level is read as the lookup's Value (0/1/2): the lookup's ids differ per site. One row more than the cap is
		// asked for, so an answer at the cap is told from one cut off by it.
		object query = SelectQueryHelper.BuildSelectQuery(
			StorageView,
			new[] {
				new SelectQueryHelper.SelectQueryColumnDefinition("SubjectColumnUId", "SubjectColumnUId",
					OrderDirection: 1, OrderPosition: 0),
				new SelectQueryHelper.SelectQueryColumnDefinition("Position", "Position", OrderDirection: 1,
					OrderPosition: 1),
				new SelectQueryHelper.SelectQueryColumnDefinition("SysAdminUnit", "SysAdminUnit"),
				new SelectQueryHelper.SelectQueryColumnDefinition("RightLevel.Value", "RightLevelValue")
			},
			new[] { SchemaFilter(schemaUId) },
			RowCap + 1);
		CreatioRequestOptions sendOptions = requestOptions.ForNextRequest();
		StoredRowsResponse response = SelectQueryHelper.ExecuteSelectQuery<StoredRowsResponse>(
			_applicationClient, _urlBuilder, query, sendOptions.TimeOut, sendOptions.MaxAttempts, sendOptions.RetryDelay);
		List<StoredRow> rows = response.Rows ?? new List<StoredRow>();
		if (rows.Count > RowCap) {
			throw new InvalidOperationException($"{StorageView} returned more than {RowCap} rows for schema {schemaUId}, "
				+ "so the stored rows may be cut off.");
		}
		// The server may cap an answer below the rows asked for: then the rows are fewer than the count.
		if (count is { } counted && rows.Count != counted) {
			throw new InvalidOperationException($"{StorageView} returned {rows.Count} of the {counted} rows it counts for "
				+ $"schema {schemaUId}, so the stored rows are not all read.");
		}
		return rows
			.GroupBy(row => row.SubjectColumnUId)
			.Select(column => new ColumnRights(column.Key, null, column
				.Select(row => new ColumnRightRow(row.SysAdminUnit?.Value ?? Guid.Empty,
					row.SysAdminUnit?.DisplayValue ?? "(unknown)", row.Position,
					row.RightLevelValue is { } level ? (ColumnRightLevel)level : null,
					row.RightLevelValue is null ? "(none)" : null))
				.OrderBy(row => row.Position is null).ThenBy(row => row.Position ?? 0)
				.ToList()))
			.ToList();
	}

	private static SelectQueryHelper.SelectQueryFilterDefinition SchemaFilter(Guid schemaUId) =>
		new("SubjectSchemaUId", schemaUId.ToString(), SelectQueryHelper.GuidDataValueType);

	// COUNT(Id) over the object's stored rows: the same filter as the rows, one aggregation column.
	private int CountStoredRows(Guid schemaUId, CreatioRequestOptions requestOptions) {
		JsonObject query = JsonSerializer.SerializeToNode(SelectQueryHelper.BuildSelectQuery(StorageView,
			new[] { new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id") }, new[] { SchemaFilter(schemaUId) },
			1))!.AsObject();
		query["columns"] = JsonSerializer.SerializeToNode(new {
			items = new Dictionary<string, object> {
				["Count"] = new {
					expression = new {
						expressionType = 1,
						functionType = 2,
						aggregationType = 1,
						functionArgument = new { expressionType = 0, columnPath = "Id" }
					},
					isVisible = true
				}
			}
		});
		CreatioRequestOptions sendOptions = requestOptions.ForNextRequest();
		CountResponse response = SelectQueryHelper.ExecuteSelectQuery<CountResponse>(_applicationClient, _urlBuilder,
			query, sendOptions.TimeOut, sendOptions.MaxAttempts, sendOptions.RetryDelay);
		return response.Rows?.FirstOrDefault()?.Count
			?? throw new InvalidOperationException($"{StorageView}: the row count for schema {schemaUId} came back empty.");
	}

	private sealed class CountResponse : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<CountRow> Rows { get; set; }
	}

	private sealed class CountRow {
		[JsonPropertyName("Count")]
		public int? Count { get; set; }
	}

	private sealed class StoredRowsResponse : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<StoredRow> Rows { get; set; }
	}

	private sealed class StoredRow {
		[JsonPropertyName("SubjectColumnUId")]
		public Guid SubjectColumnUId { get; set; }

		[JsonPropertyName("Position")]
		public int? Position { get; set; }

		[JsonPropertyName("SysAdminUnit")]
		public LookupValue SysAdminUnit { get; set; }

		[JsonPropertyName("RightLevelValue")]
		public int? RightLevelValue { get; set; }
	}

	private sealed class LookupValue {
		[JsonPropertyName("value")]
		public Guid Value { get; set; }

		[JsonPropertyName("displayValue")]
		public string DisplayValue { get; set; }
	}
}
