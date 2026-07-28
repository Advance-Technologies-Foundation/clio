namespace Clio.Command;

using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Shared DataService/ESQ building blocks for the Classic-&gt;Freedom flow's entity resolution, single-sourced so
/// <see cref="ListEntityClientSchemasCommand"/> and <see cref="ClassicSectionSchemaResolver"/> cannot drift on the
/// base-row selection rule or a filter's <c>dataValueType</c> and end up resolving a different entity UId.
/// Column-set-specific selects (sections, edit pages, per-UId name lookups) stay local to each caller; only the
/// generic DSL, the entity select, the row-based <c>ResolveEntityUId</c>, and <c>Select</c> live here.
/// </summary>
internal static class ClassicEntitySchemaQuery {

	/// <summary>Row cap for the entity-name lookup; reaching it signals an ambiguous/over-broad result.</summary>
	internal const int EntityRowCount = 50;

	/// <summary>The all-zero GUID sentinel returned by DataService for an unset UId reference.</summary>
	internal const string EmptyGuid = "00000000-0000-0000-0000-000000000000";

	/// <summary>Row cap for the section (SysModuleEntity) lookup; reaching it signals a truncated result.</summary>
	internal const int SectionRowCount = 100;

	/// <summary>Runs a SelectQuery and returns its rows, keyed off the shared failure detector.</summary>
	internal static JArray Select(IApplicationClient client, IServiceUrlBuilder urlBuilder, JObject query) {
		string url = urlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select);
		string json = client.ExecutePostRequest(url, query.ToString(Formatting.None));
		return DataServiceSelectResponse.ReadRows(json);
	}

	/// <summary>Selects the entity's <c>SysSchema</c> rows (UId + ExtendParent) by name and manager.</summary>
	internal static JObject BuildSelectEntity(string entityName) => Query("SysSchema",
		new JObject { ["UId"] = Column("UId"), ["ExtendParent"] = Column("ExtendParent") },
		Group(("byName", Eq("Name", entityName, 1)), ("byManager", Eq("ManagerName", "EntitySchemaManager", 1))),
		EntityRowCount);

	/// <summary>
	/// Picks the base row (<c>ExtendParent == false</c>) UId from entity SysSchema rows. The base row — not a
	/// replacing layer — is the stable migration unit; refusing to guess when it is absent avoids resolving the
	/// wrong physical schema.
	/// </summary>
	internal static (string uId, string error) ResolveEntityUId(string entityName, JArray rows) {
		if (rows.Count == 0) {
			return (null, $"Entity '{entityName}' not found (ManagerName='EntitySchemaManager')");
		}
		JToken baseRow = rows.FirstOrDefault(row => row["ExtendParent"]?.Value<bool?>() == false);
		if (baseRow is null) {
			return (null,
				$"Entity '{entityName}' metadata did not include a base row (ExtendParent=false); " +
				"cannot safely resolve the entity's schema.");
		}
		string uId = baseRow["UId"]?.ToString();
		return string.IsNullOrWhiteSpace(uId)
			? (null, $"Entity '{entityName}' base schema metadata is missing UId")
			: (uId, null);
	}

	/// <summary>Resolves the entity UId in one call: runs <see cref="BuildSelectEntity"/> then the row pick.</summary>
	internal static (string uId, string error) ResolveEntityUId(
		IApplicationClient client, IServiceUrlBuilder urlBuilder, string entityName) =>
		ResolveEntityUId(entityName, Select(client, urlBuilder, BuildSelectEntity(entityName)));

	// ---- ESQ DSL ----
	internal static JObject Column(string path) =>
		new() { ["expression"] = new JObject { ["expressionType"] = 0, ["columnPath"] = path } };

	internal static JObject Eq(string columnPath, string value, int dataValueType) => new() {
		["filterType"] = 1, ["comparisonType"] = 3, ["isEnabled"] = true,
		["leftExpression"] = new JObject { ["expressionType"] = 0, ["columnPath"] = columnPath },
		["rightExpression"] = new JObject {
			["expressionType"] = 2,
			["parameter"] = new JObject { ["dataValueType"] = dataValueType, ["value"] = value }
		}
	};

	internal static JObject Group(params (string key, JObject filter)[] items) {
		var jitems = new JObject();
		foreach ((string key, JObject filter) in items) {
			jitems[key] = filter;
		}
		return new JObject {
			["filterType"] = 6, ["logicalOperation"] = 0, ["isEnabled"] = true, ["items"] = jitems
		};
	}

	internal static JObject Query(string root, JObject columns, JObject filters, int rowCount) => new() {
		["rootSchemaName"] = root, ["operationType"] = 0,
		["columns"] = new JObject { ["items"] = columns }, ["filters"] = filters, ["rowCount"] = rowCount
	};

	/// <summary>An <c>In</c> filter (filterType 4) over <paramref name="columnPath"/> against many values.</summary>
	internal static JObject InFilter(string columnPath, IEnumerable<string> values, int dataValueType) {
		var expressions = new JArray();
		foreach (string value in values) {
			expressions.Add(new JObject {
				["expressionType"] = 2,
				["parameter"] = new JObject { ["dataValueType"] = dataValueType, ["value"] = value }
			});
		}
		return new JObject {
			["filterType"] = 4,
			["comparisonType"] = 3,
			["isEnabled"] = true,
			["leftExpression"] = new JObject { ["expressionType"] = 0, ["columnPath"] = columnPath },
			["rightExpressions"] = expressions
		};
	}
}
