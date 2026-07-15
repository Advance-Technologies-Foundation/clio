namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using CommandLine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[Verb("list-schema-hierarchy", Aliases = ["schema-hierarchy-list"],
	HelpText = "List every package schema of a client unit schema by name (base + all replacing schemas), " +
		"with package, maintainer, InstallType and an is-client-editable flag. " +
		"Foundation for Classic->Freedom migration: pick the schema to read (get-classic-schema-by-uid) and the editable package to write into.")]
public class ListSchemaHierarchyOptions : EnvironmentOptions {

	[Option("schema-name", Required = true, HelpText = "Client unit schema name shared across all schemas, e.g. 'ContractPageV2'")]
	public string SchemaName { get; set; }

	[Option("manager-name", Required = false, Default = "ClientUnitSchemaManager",
		HelpText = "SysSchema.ManagerName to filter by (default ClientUnitSchemaManager).")]
	public string ManagerName { get; set; }
}

public sealed class SchemaHierarchyEntry {

	[System.Text.Json.Serialization.JsonPropertyName("package")]
	public string Package { get; set; }

	[System.Text.Json.Serialization.JsonPropertyName("uId")]
	public string UId { get; set; }

	[System.Text.Json.Serialization.JsonPropertyName("maintainer")]
	public string Maintainer { get; set; }

	[System.Text.Json.Serialization.JsonPropertyName("installType")]
	public int InstallType { get; set; }

	[System.Text.Json.Serialization.JsonPropertyName("isBase")]
	public bool IsBase { get; set; }

	[System.Text.Json.Serialization.JsonPropertyName("isClientEditable")]
	public bool IsClientEditable { get; set; }

	[System.Text.Json.Serialization.JsonPropertyName("baseTemplate")]
	public string BaseTemplate { get; set; }

	// SysPackage.HierarchyLevel — Creatio's materialized topological depth of the package in the
	// global dependency DAG. Ascending order across a schema's schemas == dependency (merge) order.
	// Null when the platform did not report it. Exposed as provenance so the merge module and the
	// skill can see the ordering key and detect ties independently.
	[System.Text.Json.Serialization.JsonPropertyName("hierarchyLevel")]
	public int? HierarchyLevel { get; set; }
}

public sealed class ListSchemaHierarchyResponse {

	[System.Text.Json.Serialization.JsonPropertyName("success")]
	public bool Success { get; set; }

	[System.Text.Json.Serialization.JsonPropertyName("schemaName")]
	public string SchemaName { get; set; }

	[System.Text.Json.Serialization.JsonPropertyName("count")]
	public int Count { get; set; }

	[System.Text.Json.Serialization.JsonPropertyName("schemas")]
	public List<SchemaHierarchyEntry> Schemas { get; set; }

	// Non-fatal ordering caveats: ≥2 replacing schemas that share a HierarchyLevel, or ≥2 with no
	// reported level — in both cases their relative order is a stable-but-arbitrary tiebreak, not
	// dependency-determined. Null/absent == no such caveat (order fully determined by depth).
	[System.Text.Json.Serialization.JsonPropertyName("warnings")]
	public List<string> Warnings { get; set; }

	[System.Text.Json.Serialization.JsonPropertyName("error")]
	public string Error { get; set; }
}

public class ListSchemaHierarchyCommand : Command<ListSchemaHierarchyOptions> {

	private const string SelectQueryRoute = "/DataService/json/SyncReply/SelectQuery";

	// Product maintainers are never client-editable regardless of InstallType.
	private static readonly HashSet<string> ProductMaintainers =
		new(StringComparer.OrdinalIgnoreCase) { "Creatio", "Terrasoft" };

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly ILogger _logger;

	public ListSchemaHierarchyCommand(
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder,
		ILogger logger) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_logger = logger;
	}

	public virtual bool TryListHierarchy(ListSchemaHierarchyOptions options, out ListSchemaHierarchyResponse response) {
		try {
			if (string.IsNullOrWhiteSpace(options.SchemaName)) {
				response = new ListSchemaHierarchyResponse { Success = false, Error = "schema-name is required" };
				return false;
			}
			string managerName = string.IsNullOrWhiteSpace(options.ManagerName)
				? "ClientUnitSchemaManager" : options.ManagerName;
			JObject query = BuildSelectHierarchyByName(options.SchemaName, managerName);
			string url = _serviceUrlBuilder.Build(SelectQueryRoute);
			string json = _applicationClient.ExecutePostRequest(url, query.ToString(Formatting.None));
			JArray rows = JObject.Parse(json)["rows"] as JArray ?? [];

			List<SchemaHierarchyEntry> schemas = rows.Select(MapHierarchyEntry).ToList();

			// Order == Creatio's own dependency-composition order.
			// SysPackage.HierarchyLevel is the platform-materialized topological DEPTH of a package
			// within the global package-dependency DAG (recomputed whenever dependencies change),
			// so ascending HierarchyLevel across one schema's schemas is a valid dependency order.
			// Empirically strict for real stacks (e.g. Contract's 9 schemas: 299 < 320 < ... < 607).
			// (SysPackageInDependency — the raw edge table — is not exposed as an ESQ ObjectSchema,
			// so HierarchyLevel is the authoritative ordering signal available here, not a mere proxy.)
			// Base pinned first; a stable tiebreak by package name keeps output reproducible when two
			// schemas share a level (independent packages replacing one schema — surfaced in Warnings).
			schemas = schemas
				.OrderByDescending(l => l.IsBase)
				.ThenBy(l => l.HierarchyLevel ?? int.MaxValue)
				.ThenBy(l => l.Package, StringComparer.OrdinalIgnoreCase)
				.ToList();

			List<string> warnings = DetectOrderingAmbiguity(schemas);
			response = new ListSchemaHierarchyResponse {
				Success = true,
				SchemaName = options.SchemaName,
				Count = schemas.Count,
				Schemas = schemas,
				// empty → null, matching the codebase convention (e.g. PageUpdateTool) so consumers can
				// treat absent/null uniformly as "no ordering caveats".
				Warnings = warnings.Count > 0 ? warnings : null
			};
			return true;
		}
		catch (Exception ex) {
			response = new ListSchemaHierarchyResponse { Success = false, Error = ex.Message };
			return false;
		}
	}

	private static SchemaHierarchyEntry MapHierarchyEntry(JToken row) {
		string maintainer = row["Maintainer"]?.ToString();
		int installType = row["InstallType"]?.Value<int?>() ?? 0;
		bool isClientEditable = IsClientEditable(maintainer, installType);
		return new SchemaHierarchyEntry {
			Package = row["PackageName"]?.ToString(),
			UId = row["UId"]?.ToString(),
			Maintainer = maintainer,
			InstallType = installType,
			IsBase = !(row["ExtendParent"]?.Value<bool?>() ?? false),
			IsClientEditable = isClientEditable,
			BaseTemplate = row["ParentName"]?.ToString(),
			HierarchyLevel = row["HierarchyLevel"]?.Value<int?>()
		};
	}

	// Two replacing schemas at the same HierarchyLevel are not ordered by dependency depth; Creatio
	// composes such siblings by install order (effectively undefined). Surface it so the caller knows
	// the merge order between them is a stable-but-arbitrary tiebreak, not authoritative.
	internal static List<string> DetectOrderingAmbiguity(List<SchemaHierarchyEntry> schemas) {
		List<SchemaHierarchyEntry> nonBase = schemas.Where(l => !l.IsBase).ToList();
		List<string> warnings = nonBase
			.Where(l => l.HierarchyLevel.HasValue)
			.GroupBy(l => l.HierarchyLevel.Value)
			.Where(g => g.Count() > 1)
			.Select(g =>
				$"Schemas [{string.Join(", ", g.Select(l => l.Package ?? "(unknown)"))}] share HierarchyLevel {g.Key}; " +
				"their relative merge order is not determined by dependency depth — applied a stable " +
				"tiebreak by package name. Verify against actual package dependencies if these schemas " +
				"modify the same elements.")
			.ToList();
		// Schemas whose HierarchyLevel the platform did not report all collapse to the same "unknown"
		// bucket and get an arbitrary name tiebreak — that is ALSO undetermined order, so it must warn
		// too (otherwise empty Warnings would falsely imply a fully dependency-determined order).
		List<SchemaHierarchyEntry> unknown = nonBase.Where(l => !l.HierarchyLevel.HasValue).ToList();
		if (unknown.Count > 1)
			warnings.Add(
				$"Schemas [{string.Join(", ", unknown.Select(l => l.Package ?? "(unknown)"))}] have no HierarchyLevel; " +
				"their relative merge order is undetermined — applied a stable tiebreak by package name. " +
				"Verify against actual package dependencies.");
		return warnings;
	}

	// Client-editable ⇔ a non-product maintainer AND developed-here InstallType (0).
	// Lesson from PoC: Maintainer='Customer' alone is NOT enough — installed (InstallType=1)
	// customer packages are read-only for schema creation.
	internal static bool IsClientEditable(string maintainer, int installType) =>
		!string.IsNullOrWhiteSpace(maintainer)
		&& !ProductMaintainers.Contains(maintainer)
		&& installType == 0;

	public override int Execute(ListSchemaHierarchyOptions options) {
		bool success = TryListHierarchy(options, out ListSchemaHierarchyResponse response);
		_logger.WriteInfo(System.Text.Json.JsonSerializer.Serialize(response));
		return success ? 0 : 1;
	}

	private static JObject Column(string path) =>
		new() { ["expression"] = new JObject { ["expressionType"] = 0, ["columnPath"] = path } };

	private static JObject EqFilter(string columnPath, string value) => new() {
		["filterType"] = 1,
		["comparisonType"] = 3,
		["isEnabled"] = true,
		["leftExpression"] = new JObject { ["expressionType"] = 0, ["columnPath"] = columnPath },
		["rightExpression"] = new JObject {
			["expressionType"] = 2,
			["parameter"] = new JObject { ["dataValueType"] = 1, ["value"] = value }
		}
	};

	private static JObject BuildSelectHierarchyByName(string schemaName, string managerName) => new() {
		["rootSchemaName"] = "SysSchema",
		["operationType"] = 0,
		["columns"] = new JObject {
			["items"] = new JObject {
				["Name"] = Column("Name"),
				["UId"] = Column("UId"),
				["ExtendParent"] = Column("ExtendParent"),
				["ParentName"] = Column("Parent.Name"),
				["PackageName"] = Column("SysPackage.Name"),
				["Maintainer"] = Column("SysPackage.Maintainer"),
				["InstallType"] = Column("SysPackage.InstallType"),
				["HierarchyLevel"] = Column("SysPackage.HierarchyLevel")
			}
		},
		["filters"] = new JObject {
			["filterType"] = 6,
			["logicalOperation"] = 0,
			["isEnabled"] = true,
			["items"] = new JObject {
				["byName"] = EqFilter("Name", schemaName),
				["byManager"] = EqFilter("ManagerName", managerName)
			}
		},
		["rowCount"] = 200
	};
}
