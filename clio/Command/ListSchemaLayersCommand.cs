namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using CommandLine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[Verb("list-schema-layers", Aliases = ["schema-layers-list"],
	HelpText = "List every package layer of a client unit schema by name (base + all replacing layers), " +
		"with package, maintainer, InstallType and an is-client-editable flag. " +
		"Foundation for Classic->Freedom migration: pick the layer to read (get-classic-schema) and the editable package to write into.")]
public class ListSchemaLayersOptions : EnvironmentOptions {

	[Option("schema-name", Required = true, HelpText = "Client unit schema name shared across all layers, e.g. 'ContractPageV2'")]
	public string SchemaName { get; set; }

	[Option("manager-name", Required = false, Default = "ClientUnitSchemaManager",
		HelpText = "SysSchema.ManagerName to filter by (default ClientUnitSchemaManager).")]
	public string ManagerName { get; set; }
}

public sealed class SchemaLayerInfo {

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
}

public sealed class ListSchemaLayersResponse {

	[System.Text.Json.Serialization.JsonPropertyName("success")]
	public bool Success { get; set; }

	[System.Text.Json.Serialization.JsonPropertyName("schemaName")]
	public string SchemaName { get; set; }

	[System.Text.Json.Serialization.JsonPropertyName("count")]
	public int Count { get; set; }

	[System.Text.Json.Serialization.JsonPropertyName("layers")]
	public List<SchemaLayerInfo> Layers { get; set; }

	[System.Text.Json.Serialization.JsonPropertyName("error")]
	public string Error { get; set; }
}

public class ListSchemaLayersCommand : Command<ListSchemaLayersOptions> {

	private const string SelectQueryRoute = "/DataService/json/SyncReply/SelectQuery";

	// Product maintainers are never client-editable regardless of InstallType.
	private static readonly HashSet<string> ProductMaintainers =
		new(StringComparer.OrdinalIgnoreCase) { "Creatio", "Terrasoft" };

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly ILogger _logger;

	public ListSchemaLayersCommand(
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder,
		ILogger logger) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_logger = logger;
	}

	public virtual bool TryListLayers(ListSchemaLayersOptions options, out ListSchemaLayersResponse response) {
		try {
			if (string.IsNullOrWhiteSpace(options.SchemaName)) {
				response = new ListSchemaLayersResponse { Success = false, Error = "schema-name is required" };
				return false;
			}
			string managerName = string.IsNullOrWhiteSpace(options.ManagerName)
				? "ClientUnitSchemaManager" : options.ManagerName;
			JObject query = BuildSelectLayersByName(options.SchemaName, managerName);
			string url = _serviceUrlBuilder.Build(SelectQueryRoute);
			string json = _applicationClient.ExecutePostRequest(url, query.ToString(Formatting.None));
			JArray rows = JObject.Parse(json)["rows"] as JArray ?? [];

			List<SchemaLayerInfo> layers = rows.Select(MapLayer).ToList();
			// base first, then by HierarchyLevel ascending (proxy for dependency order;
			// authoritative order is the SysPackageInDependency DAG — resolved by the effective-merge tool).
			layers = layers
				.OrderByDescending(l => l.IsBase)
				.ThenBy(l => rows.FirstOrDefault(r => r["UId"]?.ToString() == l.UId)?["HierarchyLevel"]
					?.Value<int?>() ?? int.MaxValue)
				.ToList();

			response = new ListSchemaLayersResponse {
				Success = true,
				SchemaName = options.SchemaName,
				Count = layers.Count,
				Layers = layers
			};
			return true;
		}
		catch (Exception ex) {
			response = new ListSchemaLayersResponse { Success = false, Error = ex.Message };
			return false;
		}
	}

	private static SchemaLayerInfo MapLayer(JToken row) {
		string maintainer = row["Maintainer"]?.ToString();
		int installType = row["InstallType"]?.Value<int?>() ?? 0;
		bool isClientEditable = IsClientEditable(maintainer, installType);
		return new SchemaLayerInfo {
			Package = row["PackageName"]?.ToString(),
			UId = row["UId"]?.ToString(),
			Maintainer = maintainer,
			InstallType = installType,
			IsBase = !(row["ExtendParent"]?.Value<bool?>() ?? false),
			IsClientEditable = isClientEditable,
			BaseTemplate = row["ParentName"]?.ToString()
		};
	}

	// Client-editable ⇔ a non-product maintainer AND developed-here InstallType (0).
	// Lesson from PoC: Maintainer='Customer' alone is NOT enough — installed (InstallType=1)
	// customer packages are read-only for schema creation.
	internal static bool IsClientEditable(string maintainer, int installType) =>
		!string.IsNullOrWhiteSpace(maintainer)
		&& !ProductMaintainers.Contains(maintainer)
		&& installType == 0;

	public override int Execute(ListSchemaLayersOptions options) {
		bool success = TryListLayers(options, out ListSchemaLayersResponse response);
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

	private static JObject BuildSelectLayersByName(string schemaName, string managerName) => new() {
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
