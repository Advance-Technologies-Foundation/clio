using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Clio.Common;
using static Clio.Package.SelectQueryHelper;

namespace Clio.Command.BusinessRules.Filters;

/// <summary>
/// Resolves the owning package UId for a schema by querying <c>SysSchema</c> joined to
/// <c>SysPackage</c> via DataService SelectQuery. Picks the root row — the one whose parent
/// schema name differs from its own (or which has no parent) — to skip extension overlays
/// like SSP/CrtGoogleAnalytics that may also redefine the same name.
/// </summary>
internal sealed class SchemaPackageDiscovery(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder)
	: ISchemaPackageDiscovery {

	private const string EntitySchemaManagerName = "EntitySchemaManager";

	private static readonly IReadOnlyList<SelectQueryColumnDefinition> Columns =
	[
		new("Name", "Name"),
		new("SysPackage.UId", "PackageUId"),
		new("SysPackage.Name", "PackageName"),
		new("[SysSchema:Id:Parent].Name", "ParentSchemaName")
	];

	public Guid? TryFindRootPackageUId(string schemaName) {
		ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);
		object query = BuildSelectQuery(
			"SysSchema",
			Columns,
			[
				new SelectQueryFilterDefinition("ManagerName", EntitySchemaManagerName, TextDataValueType),
				new SelectQueryFilterDefinition("Name", schemaName, TextDataValueType)
			]);
		FindSchemasResponse response = ExecuteSelectQuery<FindSchemasResponse>(
			applicationClient,
			serviceUrlBuilder,
			query);
		List<FindSchemasRowDto> rows = response.Rows;
		if (rows.Count == 0) {
			return null;
		}
		// Prefer the row whose parent schema name is different (the root definition);
		// fall back to whichever row Creatio returned first when none qualifies as root.
		FindSchemasRowDto root = rows.FirstOrDefault(row =>
			!string.Equals(row.ParentSchemaName, row.Name, StringComparison.Ordinal))
			?? rows[0];
		if (!Guid.TryParse(root.PackageUId, out Guid packageUId) || packageUId == Guid.Empty) {
			return null;
		}
		return packageUId;
	}

	private sealed class FindSchemasResponse : SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<FindSchemasRowDto> Rows { get; set; } = [];
	}

	private sealed class FindSchemasRowDto {
		[JsonPropertyName("Name")]
		public string? Name { get; set; }

		[JsonPropertyName("PackageUId")]
		public string? PackageUId { get; set; }

		[JsonPropertyName("PackageName")]
		public string? PackageName { get; set; }

		[JsonPropertyName("ParentSchemaName")]
		public string? ParentSchemaName { get; set; }
	}
}
