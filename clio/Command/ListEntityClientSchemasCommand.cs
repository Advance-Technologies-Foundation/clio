namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using CommandLine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[Verb("list-entity-client-schemas", Aliases = ["migration-unit-resolve"],
	HelpText = "Resolve the page-role graph of an entity for a Classic->Freedom migration: its Classic sections, " +
		"edit pages (including per-type/typed pages), and add mini pages, each classified Classic vs Freedom. " +
		"One level only — the skill recurses into detail entities. Pure ESQ; no schema-body parsing.")]
public class ListEntityClientSchemasOptions : EnvironmentOptions {

	[Option("entity-name", Required = true, HelpText = "Entity schema name, e.g. 'Contract' or 'SupportUnit'")]
	public string EntityName { get; set; }
}

public sealed class MigrationSectionInfo {
	[System.Text.Json.Serialization.JsonPropertyName("caption")] public string Caption { get; set; }
	[System.Text.Json.Serialization.JsonPropertyName("code")] public string Code { get; set; }
	[System.Text.Json.Serialization.JsonPropertyName("sectionSchema")] public string SectionSchema { get; set; }
	[System.Text.Json.Serialization.JsonPropertyName("cardSchema")] public string CardSchema { get; set; }
	[System.Text.Json.Serialization.JsonPropertyName("isTyped")] public bool IsTyped { get; set; }
}

public sealed class MigrationEditPageInfo {
	[System.Text.Json.Serialization.JsonPropertyName("typeColumnValue")] public string TypeColumnValue { get; set; }
	[System.Text.Json.Serialization.JsonPropertyName("cardSchema")] public string CardSchema { get; set; }
	[System.Text.Json.Serialization.JsonPropertyName("cardSchemaUId")] public string CardSchemaUId { get; set; }
	[System.Text.Json.Serialization.JsonPropertyName("template")] public string Template { get; set; }
	[System.Text.Json.Serialization.JsonPropertyName("kind")] public string Kind { get; set; } // classic | freedom
	[System.Text.Json.Serialization.JsonPropertyName("miniPageSchema")] public string MiniPageSchema { get; set; }
	[System.Text.Json.Serialization.JsonPropertyName("miniPageModes")] public string MiniPageModes { get; set; }
}

public sealed class ListEntityClientSchemasResponse {
	[System.Text.Json.Serialization.JsonPropertyName("success")] public bool Success { get; set; }
	[System.Text.Json.Serialization.JsonPropertyName("entity")] public string Entity { get; set; }
	[System.Text.Json.Serialization.JsonPropertyName("entityUId")] public string EntityUId { get; set; }
	[System.Text.Json.Serialization.JsonPropertyName("sections")] public List<MigrationSectionInfo> Sections { get; set; }
	[System.Text.Json.Serialization.JsonPropertyName("editPages")] public List<MigrationEditPageInfo> EditPages { get; set; }
	[System.Text.Json.Serialization.JsonPropertyName("note")] public string Note { get; set; }
	[System.Text.Json.Serialization.JsonPropertyName("error")] public string Error { get; set; }
}

public class ListEntityClientSchemasCommand : Command<ListEntityClientSchemasOptions> {

	private const string SelectQueryRoute = "/DataService/json/SyncReply/SelectQuery";
	private const string EmptyGuid = "00000000-0000-0000-0000-000000000000";

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly ILogger _logger;

	public ListEntityClientSchemasCommand(
		IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder, ILogger logger) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_logger = logger;
	}

	public virtual bool TryResolve(ListEntityClientSchemasOptions options, out ListEntityClientSchemasResponse response) {
		try {
			if (string.IsNullOrWhiteSpace(options.EntityName)) {
				response = new ListEntityClientSchemasResponse { Success = false, Error = "entity-name is required" };
				return false;
			}
			string entityUId = ResolveEntityUId(options.EntityName);
			if (entityUId == null) {
				response = new ListEntityClientSchemasResponse {
					Success = false, Error = $"Entity '{options.EntityName}' not found (ManagerName='EntitySchemaManager')" };
				return false;
			}

			JArray moduleRows = Select(BuildSelectSections(entityUId));
			JArray editRows = Select(BuildSelectEditPages(entityUId));

			// resolve page UId -> (name, template) once per unique UId
			var cache = new Dictionary<string, (string name, string template)>(StringComparer.OrdinalIgnoreCase);
			(string name, string template) Meta(string uId) {
				if (string.IsNullOrWhiteSpace(uId) || uId == EmptyGuid) return (null, null);
				if (cache.TryGetValue(uId, out var m)) return m;
				m = ResolveSchemaMeta(uId);
				cache[uId] = m;
				return m;
			}

			var sections = moduleRows.Select(r => {
				string sectionUId = r["SectionSchemaUId"]?.ToString();
				string cardUId = r["CardSchemaUId"]?.ToString();
				string typeColUId = r["TypeColumnUId"]?.ToString();
				return new MigrationSectionInfo {
					Caption = r["Caption"]?.ToString(),
					Code = r["Code"]?.ToString(),
					SectionSchema = Meta(sectionUId).name,
					CardSchema = Meta(cardUId).name,
					IsTyped = !string.IsNullOrWhiteSpace(typeColUId) && typeColUId != EmptyGuid
				};
			}).ToList();

			var editPages = editRows.Select(r => {
				string cardUId = r["CardSchemaUId"]?.ToString();
				string miniUId = r["MiniPageSchemaUId"]?.ToString();
				(string cardName, string template) = Meta(cardUId);
				return new MigrationEditPageInfo {
					TypeColumnValue = r["TypeColumnValue"]?.ToString(),
					CardSchema = cardName,
					CardSchemaUId = cardUId,
					Template = template,
					Kind = ClassifyKind(template),
					MiniPageSchema = Meta(miniUId).name,
					MiniPageModes = r["MiniPageModes"]?.ToString()
				};
			}).ToList();

			bool empty = sections.Count == 0 && editPages.Count == 0;
			response = new ListEntityClientSchemasResponse {
				Success = true,
				Entity = options.EntityName,
				EntityUId = entityUId,
				Sections = sections,
				EditPages = editPages,
				Note = (empty
						? "No SysModule sections or SysModuleEdit pages matched this entity — it may have no Classic UI " +
						  "section, or the entity name/UId is off. This is NOT the same as 'nothing to migrate'; verify before skipping. "
						: "") +
					"One level only. Details on each card and Freedom counterparts are read from the card body/page model " +
					"(pure merge module); recurse into detail entities by calling list-entity-client-schemas per detail entity."
			};
			return true;
		}
		catch (Exception ex) {
			response = new ListEntityClientSchemasResponse { Success = false, Error = ex.Message };
			return false;
		}
	}

	// Freedom edit-page templates carry "Freedom" or are FormPageTemplate; Classic use BaseModulePageV2/BasePageV2.
	internal static string ClassifyKind(string template) {
		if (string.IsNullOrWhiteSpace(template)) return "unknown";
		if (template.Contains("Freedom", StringComparison.OrdinalIgnoreCase) ||
			template.Equals("FormPageTemplate", StringComparison.OrdinalIgnoreCase) ||
			template.Equals("ListPageV3Template", StringComparison.OrdinalIgnoreCase))
			return "freedom";
		return "classic";
	}

	private string ResolveEntityUId(string entityName) {
		JArray rows = Select(BuildSelectEntity(entityName));
		JToken baseRow = rows.FirstOrDefault(r => !(r["ExtendParent"]?.Value<bool?>() ?? false)) ?? rows.FirstOrDefault();
		return baseRow?["UId"]?.ToString();
	}

	private (string name, string template) ResolveSchemaMeta(string uId) {
		JArray rows = Select(BuildSelectSchemaByUId(uId));
		JToken row = rows.FirstOrDefault();
		return (row?["Name"]?.ToString(), row?["ParentName"]?.ToString());
	}

	private JArray Select(JObject query) {
		string url = _serviceUrlBuilder.Build(SelectQueryRoute);
		string json = _applicationClient.ExecutePostRequest(url, query.ToString(Formatting.None));
		return DataServiceSelectResponse.ReadRows(json);
	}

	public override int Execute(ListEntityClientSchemasOptions options) {
		bool success = TryResolve(options, out ListEntityClientSchemasResponse response);
		_logger.WriteInfo(System.Text.Json.JsonSerializer.Serialize(response));
		return success ? 0 : 1;
	}

	// ---- ESQ builders ----
	private static JObject Column(string path) =>
		new() { ["expression"] = new JObject { ["expressionType"] = 0, ["columnPath"] = path } };

	private static JObject Eq(string columnPath, string value, int dataValueType) => new() {
		["filterType"] = 1, ["comparisonType"] = 3, ["isEnabled"] = true,
		["leftExpression"] = new JObject { ["expressionType"] = 0, ["columnPath"] = columnPath },
		["rightExpression"] = new JObject {
			["expressionType"] = 2,
			["parameter"] = new JObject { ["dataValueType"] = dataValueType, ["value"] = value }
		}
	};

	private static JObject Group(params (string key, JObject filter)[] items) {
		var jitems = new JObject();
		foreach (var (key, filter) in items) jitems[key] = filter;
		return new JObject {
			["filterType"] = 6, ["logicalOperation"] = 0, ["isEnabled"] = true, ["items"] = jitems
		};
	}

	private static JObject Query(string root, JObject columns, JObject filters, int rowCount) => new() {
		["rootSchemaName"] = root, ["operationType"] = 0,
		["columns"] = new JObject { ["items"] = columns }, ["filters"] = filters, ["rowCount"] = rowCount
	};

	private static JObject BuildSelectEntity(string entityName) => Query("SysSchema",
		new JObject { ["UId"] = Column("UId"), ["ExtendParent"] = Column("ExtendParent") },
		Group(("byName", Eq("Name", entityName, 1)), ("byManager", Eq("ManagerName", "EntitySchemaManager", 1))), 50);

	private static JObject BuildSelectSchemaByUId(string uId) => Query("SysSchema",
		new JObject { ["Name"] = Column("Name"), ["ParentName"] = Column("Parent.Name") },
		Group(("byUId", Eq("UId", uId, 0))), 1);

	private static JObject BuildSelectSections(string entityUId) => Query("SysModule",
		new JObject {
			["Caption"] = Column("Caption"), ["Code"] = Column("Code"),
			["SectionSchemaUId"] = Column("SectionSchemaUId"), ["CardSchemaUId"] = Column("CardSchemaUId"),
			["TypeColumnUId"] = Column("SysModuleEntity.TypeColumnUId")
		},
		Group(("byEntity", Eq("SysModuleEntity.SysEntitySchemaUId", entityUId, 0))), 100);

	private static JObject BuildSelectEditPages(string entityUId) => Query("SysModuleEdit",
		new JObject {
			["TypeColumnValue"] = Column("TypeColumnValue"), ["CardSchemaUId"] = Column("CardSchemaUId"),
			["MiniPageSchemaUId"] = Column("MiniPageSchemaUId"), ["MiniPageModes"] = Column("MiniPageModes")
		},
		Group(("byEntity", Eq("SysModuleEntity.SysEntitySchemaUId", entityUId, 0))), 100);
}
