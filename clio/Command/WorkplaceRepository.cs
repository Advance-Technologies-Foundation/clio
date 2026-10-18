using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.Package;

namespace Clio.Command;

/// <summary>A navigation workplace (<c>SysWorkplace</c>) as read from the environment.</summary>
/// <param name="Id">Workplace identifier.</param>
/// <param name="Name">Workplace name in the current user's culture.</param>
/// <param name="Position">Order in the workplace switcher.</param>
/// <param name="ClientTypeId">The <c>SysApplicationClientType</c> the workplace belongs to.</param>
/// <param name="TypeId">The <c>SysWorkplaceType</c> lookup value; <see cref="Guid.Empty"/> when not set.</param>
/// <param name="LoaderId">The loader schema identifier; <see cref="Guid.Empty"/> when not set.</param>
public sealed record WorkplaceRecord(Guid Id, string Name, int Position, Guid ClientTypeId, Guid TypeId,
	Guid LoaderId);

/// <summary>A workplace type (<c>SysWorkplaceType</c>).</summary>
/// <param name="Id">Type identifier.</param>
/// <param name="Name">Type name in the current user's culture.</param>
/// <param name="Code">The culture-independent type code (0 General, 1 Portal, 3 Self-service).</param>
public sealed record WorkplaceTypeRecord(Guid Id, string Name, int Code);

/// <summary>A section registration (<c>SysModule</c>).</summary>
/// <param name="Id">Section identifier.</param>
/// <param name="Code">Section code.</param>
public sealed record SectionModuleRecord(Guid Id, string Code);

/// <summary>
/// A row of a workplace junction table: <c>SysModuleInWorkplace</c> (<paramref name="TargetId"/> is the
/// <c>SysModule</c>) or <c>SysAdminUnitInWorkplace</c> (<paramref name="TargetId"/> is the <c>SysAdminUnit</c>).
/// </summary>
/// <param name="Id">Row identifier.</param>
/// <param name="WorkplaceId">The workplace the row belongs to.</param>
/// <param name="TargetId">The section or role the row links to the workplace.</param>
/// <param name="WorkplaceName">The workplace's name, read in the same query; null when not read.</param>
public sealed record WorkplaceJunctionRecord(Guid Id, Guid WorkplaceId, Guid TargetId, string WorkplaceName = null);

/// <summary>A role or user (<c>SysAdminUnit</c>).</summary>
/// <param name="Id">Identifier.</param>
/// <param name="Name">Name.</param>
public sealed record AdminUnitRecord(Guid Id, string Name);

/// <summary>A data binding (<c>SysPackageSchemaData</c>) and the package that ships it.</summary>
/// <param name="PackageName">Package name.</param>
/// <param name="BindingName">Binding name.</param>
/// <param name="Unverified">
/// The bound data could not be parsed, so whether the binding ships the row is unknown; read the binding to decide.
/// </param>
public sealed record PackageBindingRecord(string PackageName, string BindingName, bool Unverified = false);

/// <summary>
/// Read-only access to the navigation workplace tables (<c>SysWorkplace</c>, <c>SysModuleInWorkplace</c>,
/// <c>SysAdminUnitInWorkplace</c>) and the lookups they reference, for one environment.
/// </summary>
public interface IWorkplaceRepository {

	/// <summary>Finds the <c>SysApplicationClientType</c> whose name is <paramref name="name"/> (e.g. Web, Mobile).</summary>
	/// <returns>The client type id, or <see langword="null"/> when none matches.</returns>
	Guid? FindClientTypeId(string name);

	/// <summary>Reads every <c>SysWorkplaceType</c> row.</summary>
	IReadOnlyList<WorkplaceTypeRecord> FindWorkplaceTypes();

	/// <summary>Finds every <c>SysModule</c> with the given code. Codes are not unique.</summary>
	IReadOnlyList<SectionModuleRecord> FindSectionsByCode(string code);

	/// <summary>Finds every workplace with the given name and client type, oldest first. Names are not unique.</summary>
	IReadOnlyList<WorkplaceRecord> FindWorkplaces(string name, Guid clientTypeId);

	/// <summary>Reads one workplace by id.</summary>
	/// <returns>The workplace, or <see langword="null"/> when it does not exist.</returns>
	WorkplaceRecord GetWorkplace(Guid id);

	/// <summary>Reads every <c>SysModuleInWorkplace</c> row that places the section, in any workplace.</summary>
	IReadOnlyList<WorkplaceJunctionRecord> FindSectionPlacements(Guid sectionId);

	/// <summary>Reads every <c>SysAdminUnitInWorkplace</c> row of the workplace.</summary>
	IReadOnlyList<WorkplaceJunctionRecord> FindRoleGrants(Guid workplaceId);

	/// <summary>Finds every <c>SysAdminUnit</c> with the given name. Names are not unique.</summary>
	IReadOnlyList<AdminUnitRecord> FindAdminUnitsByName(string name);

	/// <summary>Reads one <c>SysAdminUnit</c> by id.</summary>
	/// <returns>The role or user, or <see langword="null"/> when it does not exist.</returns>
	AdminUnitRecord GetAdminUnit(Guid id);

	/// <summary>
	/// Lists the names of the data bindings (<c>SysPackageSchemaData</c>) the package ships for an entity schema.
	/// </summary>
	IReadOnlyList<string> FindBindingNames(Guid packageUId, string entitySchemaName);

	/// <summary>Lists the names of the packages that ship a data binding with the given name.</summary>
	IReadOnlyList<string> FindPackagesShippingBinding(string bindingName);

	/// <summary>
	/// Lists every data binding, in any package, that ships the row <paramref name="rowId"/> of an entity schema.
	/// One query reads the bound data of every binding of the schema, so the cost does not grow with the number of
	/// packages. A binding whose bound data cannot be parsed is listed with
	/// <see cref="PackageBindingRecord.Unverified"/> set, never dropped.
	/// </summary>
	IReadOnlyList<PackageBindingRecord> FindBindingsShippingRow(string entitySchemaName, Guid rowId);
}

/// <inheritdoc />
internal sealed class WorkplaceRepository(IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder)
	: IWorkplaceRepository {

	private const string IdColumn = "Id";
	private const string PackageNameAlias = "PackageName";

	/// <summary>The <c>BaseEntity.Id</c> column UId, which every bound row carries its key under.</summary>
	private const string IdColumnUId = "ae0e45ca-c495-4fe7-a39d-3ab7278e1617";

	/// <summary>
	/// Platform-seeded client type ids, used when the name does not match because the profile culture translates
	/// <c>SysApplicationClientType.Name</c>.
	/// </summary>
	private static readonly Dictionary<string, Guid> WellKnownClientTypeIds = new(StringComparer.OrdinalIgnoreCase) {
		["Web"] = new Guid("195785B4-F55A-4E72-ACE3-6480B54C8FA5"),
		["Mobile"] = new Guid("BE8AB9EE-344E-4A56-8C97-41C84F22BD88")
	};

	/// <inheritdoc />
	public Guid? FindClientTypeId(string name) {
		List<Dictionary<string, JsonElement>> rows = Select("SysApplicationClientType", [Column(IdColumn), Column("Name")], []);
		Guid? byName = rows
			.Where(row => string.Equals(ReadString(row, "Name"), name, StringComparison.OrdinalIgnoreCase))
			.Select(row => (Guid?)ReadGuid(row, IdColumn))
			.FirstOrDefault();
		if (byName is not null || !WellKnownClientTypeIds.TryGetValue(name ?? string.Empty, out Guid wellKnown)) {
			return byName;
		}
		return rows.Any(row => ReadGuid(row, IdColumn) == wellKnown) ? wellKnown : null;
	}

	/// <inheritdoc />
	public IReadOnlyList<WorkplaceTypeRecord> FindWorkplaceTypes() {
		return Select("SysWorkplaceType", [Column(IdColumn), Column("Name"), Column("Code")], [])
			.Select(row => new WorkplaceTypeRecord(ReadGuid(row, IdColumn), ReadString(row, "Name"), ReadInt(row, "Code")))
			.ToArray();
	}

	/// <inheritdoc />
	public IReadOnlyList<SectionModuleRecord> FindSectionsByCode(string code) {
		return Select("SysModule", [Column(IdColumn), Column("Code")],
				[TextFilter("Code", code)])
			.Select(row => new SectionModuleRecord(ReadGuid(row, IdColumn), ReadString(row, "Code")))
			.ToArray();
	}

	/// <inheritdoc />
	public IReadOnlyList<WorkplaceRecord> FindWorkplaces(string name, Guid clientTypeId) {
		return Select("SysWorkplace", WorkplaceColumns(),
				[TextFilter("Name", name), GuidFilter("SysApplicationClientType.Id", clientTypeId)])
			.Select(ToWorkplace)
			.ToArray();
	}

	/// <inheritdoc />
	public WorkplaceRecord GetWorkplace(Guid id) {
		return Select("SysWorkplace", WorkplaceColumns(), [GuidFilter(IdColumn, id)])
			.Select(ToWorkplace)
			.FirstOrDefault();
	}

	/// <inheritdoc />
	public IReadOnlyList<WorkplaceJunctionRecord> FindSectionPlacements(Guid sectionId) {
		return SelectJunction("SysModuleInWorkplace", "SysModule", GuidFilter("SysModule.Id", sectionId));
	}

	/// <inheritdoc />
	public IReadOnlyList<WorkplaceJunctionRecord> FindRoleGrants(Guid workplaceId) {
		return SelectJunction("SysAdminUnitInWorkplace", "SysAdminUnit", GuidFilter("SysWorkplace.Id", workplaceId));
	}

	/// <inheritdoc />
	public IReadOnlyList<AdminUnitRecord> FindAdminUnitsByName(string name) {
		return Select("SysAdminUnit", [Column(IdColumn), Column("Name")], [TextFilter("Name", name)])
			.Select(row => new AdminUnitRecord(ReadGuid(row, IdColumn), ReadString(row, "Name")))
			.ToArray();
	}

	/// <inheritdoc />
	public AdminUnitRecord GetAdminUnit(Guid id) {
		return Select("SysAdminUnit", [Column(IdColumn), Column("Name")], [GuidFilter(IdColumn, id)])
			.Select(row => new AdminUnitRecord(ReadGuid(row, IdColumn), ReadString(row, "Name")))
			.FirstOrDefault();
	}

	/// <inheritdoc />
	public IReadOnlyList<string> FindBindingNames(Guid packageUId, string entitySchemaName) {
		return Select("SysPackageSchemaData", [Column("Name")],
				[GuidFilter("SysPackage.UId", packageUId), TextFilter("SysSchema.Name", entitySchemaName)])
			.Select(row => ReadString(row, "Name"))
			.Where(name => !string.IsNullOrWhiteSpace(name))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}

	/// <inheritdoc />
	public IReadOnlyList<string> FindPackagesShippingBinding(string bindingName) {
		return Select("SysPackageSchemaData",
				[new SelectQueryHelper.SelectQueryColumnDefinition("SysPackage.Name", PackageNameAlias)],
				[TextFilter("Name", bindingName)])
			.Select(row => ReadString(row, PackageNameAlias))
			.Where(name => !string.IsNullOrWhiteSpace(name))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}

	/// <inheritdoc />
	public IReadOnlyList<PackageBindingRecord> FindBindingsShippingRow(string entitySchemaName, Guid rowId) {
		return Select("SysPackageSchemaData",
				[
					new SelectQueryHelper.SelectQueryColumnDefinition("SysPackage.Name", PackageNameAlias),
					Column("Name"),
					Column("Data")
				],
				[TextFilter("SysSchema.Name", entitySchemaName)])
			.Select(row => (Row: row, Ships: BindingDataShipsRow(ReadString(row, "Data"), rowId)))
			.Where(match => match.Ships != false)
			.Select(match => new PackageBindingRecord(ReadString(match.Row, PackageNameAlias),
				ReadString(match.Row, "Name"), match.Ships is null))
			.Where(binding => !string.IsNullOrWhiteSpace(binding.PackageName)
				&& !string.IsNullOrWhiteSpace(binding.BindingName))
			.Distinct()
			.ToArray();
	}

	/// <summary>
	/// Whether the bound data of a binding (<c>SysPackageSchemaData.Data</c>: <c>{"PackageData":[{"Row":[...]}]}</c>)
	/// has a row whose <c>Id</c> column is <paramref name="rowId"/>. DataService returns the binary column as its
	/// JSON text; base64 text is decoded first.
	/// </summary>
	/// <returns>Whether the row is shipped; null when the data is missing or cannot be parsed.</returns>
	internal static bool? BindingDataShipsRow(string data, Guid rowId) {
		string json = DecodeBindingData(data);
		if (json is null) {
			return null;
		}
		try {
			using JsonDocument document = JsonDocument.Parse(json);
			if (!document.RootElement.TryGetProperty("PackageData", out JsonElement packageData)
				|| packageData.ValueKind != JsonValueKind.Array) {
				return null;
			}
			return packageData.EnumerateArray()
				.Where(item => item.ValueKind == JsonValueKind.Object
					&& item.TryGetProperty("Row", out JsonElement cells) && cells.ValueKind == JsonValueKind.Array)
				.SelectMany(item => item.GetProperty("Row").EnumerateArray())
				.Any(cell => IsIdCell(cell, rowId));
		}
		catch (JsonException) {
			return null;
		}
	}

	private static bool IsIdCell(JsonElement cell, Guid rowId) =>
		cell.ValueKind == JsonValueKind.Object
		&& cell.TryGetProperty("SchemaColumnUId", out JsonElement columnUId)
		&& string.Equals(columnUId.ToString(), IdColumnUId, StringComparison.OrdinalIgnoreCase)
		&& cell.TryGetProperty("Value", out JsonElement value)
		&& Guid.TryParse(value.ToString(), out Guid id) && id == rowId;

	private static string DecodeBindingData(string data) {
		if (string.IsNullOrWhiteSpace(data)) {
			return null;
		}
		string trimmed = data.Trim().TrimStart('\uFEFF').TrimStart();
		if (trimmed.StartsWith('{')) {
			return trimmed;
		}
		try {
			return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(trimmed)).TrimStart('\uFEFF');
		}
		catch (FormatException) {
			return null;
		}
	}

	private IReadOnlyList<WorkplaceJunctionRecord> SelectJunction(string schemaName, string targetColumn,
		SelectQueryHelper.SelectQueryFilterDefinition filter) {
		return Select(schemaName,
				[
					Column(IdColumn),
					new SelectQueryHelper.SelectQueryColumnDefinition("SysWorkplace.Id", "WorkplaceId"),
					new SelectQueryHelper.SelectQueryColumnDefinition("SysWorkplace.Name", "WorkplaceName"),
					new SelectQueryHelper.SelectQueryColumnDefinition($"{targetColumn}.Id", "TargetId")
				],
				[filter])
			.Select(row => new WorkplaceJunctionRecord(
				ReadGuid(row, IdColumn), ReadGuid(row, "WorkplaceId"), ReadGuid(row, "TargetId"),
				ReadString(row, "WorkplaceName")))
			.ToArray();
	}

	private static SelectQueryHelper.SelectQueryColumnDefinition[] WorkplaceColumns() => [
		// Oldest first, so "reuse the first same-named workplace" is stable across runs.
		new(IdColumn, IdColumn),
		new("Name", "Name"),
		new("Position", "Position"),
		new("SysApplicationClientType.Id", "ClientTypeId"),
		new("Type.Id", "TypeId"),
		new("LoaderId", "LoaderId"),
		new("CreatedOn", "CreatedOn", OrderDirection: 1, OrderPosition: 0)
	];

	private static WorkplaceRecord ToWorkplace(Dictionary<string, JsonElement> row) =>
		new(ReadGuid(row, IdColumn), ReadString(row, "Name"), ReadInt(row, "Position"),
			ReadGuid(row, "ClientTypeId"), ReadGuid(row, "TypeId"), ReadGuid(row, "LoaderId"));

	private List<Dictionary<string, JsonElement>> Select(string schemaName,
		IReadOnlyList<SelectQueryHelper.SelectQueryColumnDefinition> columns,
		IReadOnlyList<SelectQueryHelper.SelectQueryFilterDefinition> filters) {
		RowsResponse response = SelectQueryHelper.ExecuteSelectQuery<RowsResponse>(applicationClient,
			serviceUrlBuilder, SelectQueryHelper.BuildSelectQuery(schemaName, columns, filters));
		return response.Rows ?? [];
	}

	private static SelectQueryHelper.SelectQueryColumnDefinition Column(string name) => new(name, name);

	private static SelectQueryHelper.SelectQueryFilterDefinition TextFilter(string path, string value) =>
		new(path, value, SelectQueryHelper.TextDataValueType);

	private static SelectQueryHelper.SelectQueryFilterDefinition GuidFilter(string path, Guid value) =>
		new(path, value.ToString(), SelectQueryHelper.GuidDataValueType);

	private static string ReadString(Dictionary<string, JsonElement> row, string key) {
		if (!row.TryGetValue(key, out JsonElement value) || value.ValueKind == JsonValueKind.Null) {
			return null;
		}
		if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("value", out JsonElement inner)) {
			return inner.ValueKind == JsonValueKind.Null ? null : inner.ToString();
		}
		return value.ToString();
	}

	private static Guid ReadGuid(Dictionary<string, JsonElement> row, string key) =>
		Guid.TryParse(ReadString(row, key), out Guid id) ? id : Guid.Empty;

	private static int ReadInt(Dictionary<string, JsonElement> row, string key) =>
		int.TryParse(ReadString(row, key), out int value) ? value : 0;


	private sealed class RowsResponse : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<Dictionary<string, JsonElement>> Rows { get; init; } = [];
	}
}
