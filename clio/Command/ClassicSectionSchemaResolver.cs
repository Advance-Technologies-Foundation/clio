namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Outcome of a Classic section lookup for one entity.
/// </summary>
/// <param name="SectionSchemaNames">
/// Section (list) schema names bound to the entity through <c>SysModule</c>, in the order the metadata returned
/// them. Empty when the entity has no Classic section — which is a legitimate result, not a failure.
/// </param>
/// <param name="Error">
/// Reason the lookup could not complete (entity not resolvable, DataService failure); <c>null</c> when the lookup
/// ran to completion. An empty <see cref="SectionSchemaNames"/> with a <c>null</c> error means "no section exists".
/// </param>
public sealed record ClassicSectionLookup(IReadOnlyList<string> SectionSchemaNames, string Error);

/// <summary>
/// Resolves the Classic section (list) schema bound to an entity from <c>SysModule</c> metadata rather than from a
/// naming convention.
/// </summary>
/// <remarks>
/// Name derivation (<c>&lt;Entity&gt;Section</c>, <c>&lt;PagePrefix&gt;Section</c>) cannot reach sections whose schema
/// name carries a UId/app-derived infix (e.g. entity <c>ASPContractData</c> -> section
/// <c>ASPContractDatac145c7efSection</c>) or that were simply renamed. The binding is recorded in metadata, so the
/// metadata is the authoritative source and the name conventions are only a fallback.
/// </remarks>
public interface IClassicSectionSchemaResolver {

	/// <summary>Resolves the Classic section schema names bound to <paramref name="entityName"/>.</summary>
	/// <param name="entityName">Entity schema name, e.g. <c>Contact</c>.</param>
	/// <returns>
	/// The resolved section schema names, or an <see cref="ClassicSectionLookup.Error"/> describing why the lookup
	/// could not complete. Never throws: transport and parse failures are reported through the error field so the
	/// caller can degrade to name-derived candidates.
	/// </returns>
	ClassicSectionLookup ResolveSectionSchemaNames(string entityName);
}

/// <inheritdoc />
public sealed class ClassicSectionSchemaResolver : IClassicSectionSchemaResolver {

	private const string EmptyGuid = "00000000-0000-0000-0000-000000000000";
	private const int EntityRowCount = 50;
	private const int SectionRowCount = 100;

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;

	/// <summary>Initializes a new instance of the <see cref="ClassicSectionSchemaResolver"/> class.</summary>
	public ClassicSectionSchemaResolver(IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
	}

	/// <inheritdoc />
	public ClassicSectionLookup ResolveSectionSchemaNames(string entityName) {
		if (string.IsNullOrWhiteSpace(entityName)) {
			return new ClassicSectionLookup(Array.Empty<string>(), "entity name is required");
		}
		try {
			(string entityUId, string entityError) = ResolveEntityUId(entityName);
			if (entityUId == null) {
				return new ClassicSectionLookup(Array.Empty<string>(), entityError);
			}
			JArray moduleRows = Select(BuildSelectSections(entityUId));
			string[] sectionUIds = moduleRows
				.Select(row => row["SectionSchemaUId"]?.ToString())
				.Where(uId => !string.IsNullOrWhiteSpace(uId) && uId != EmptyGuid)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToArray();
			if (sectionUIds.Length == 0) {
				return new ClassicSectionLookup(Array.Empty<string>(), null);
			}
			JArray schemaRows = Select(BuildSelectSchemaNamesByUId(sectionUIds));
			// Preserve the SysModule row order: the first module bound to the entity is the one a migration plan
			// treats as "the" section, and a UId the SysSchema lookup did not return is simply dropped.
			var nameByUId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (JToken row in schemaRows) {
				string uId = row["UId"]?.ToString();
				string name = row["Name"]?.ToString();
				if (!string.IsNullOrWhiteSpace(uId) && !string.IsNullOrWhiteSpace(name)) {
					nameByUId[uId] = name;
				}
			}
			List<string> names = sectionUIds
				.Where(nameByUId.ContainsKey)
				.Select(uId => nameByUId[uId])
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
			return new ClassicSectionLookup(names, null);
		}
		catch (Exception ex) {
			return new ClassicSectionLookup(Array.Empty<string>(), ex.Message);
		}
	}

	private (string uId, string error) ResolveEntityUId(string entityName) {
		JArray rows = Select(BuildSelectEntity(entityName));
		if (rows.Count == 0) {
			return (null, $"Entity '{entityName}' not found (ManagerName='EntitySchemaManager')");
		}
		JToken baseRow = rows.FirstOrDefault(row => row["ExtendParent"]?.Value<bool?>() == false);
		if (baseRow is null) {
			return (null,
				$"Entity '{entityName}' metadata did not include a base row (ExtendParent=false); " +
				"cannot safely resolve the section binding.");
		}
		string uId = baseRow["UId"]?.ToString();
		return string.IsNullOrWhiteSpace(uId)
			? (null, $"Entity '{entityName}' base schema metadata is missing UId")
			: (uId, null);
	}

	private JArray Select(JObject query) {
		string url = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select);
		string json = _applicationClient.ExecutePostRequest(url, query.ToString(Formatting.None));
		return DataServiceSelectResponse.ReadRows(json);
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
		foreach ((string key, JObject filter) in items) {
			jitems[key] = filter;
		}
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
		Group(("byName", Eq("Name", entityName, 1)), ("byManager", Eq("ManagerName", "EntitySchemaManager", 1))),
		EntityRowCount);

	private static JObject BuildSelectSections(string entityUId) => Query("SysModule",
		new JObject { ["SectionSchemaUId"] = Column("SectionSchemaUId") },
		Group(("byEntity", Eq("SysModuleEntity.SysEntitySchemaUId", entityUId, 0))), SectionRowCount);

	private static JObject BuildSelectSchemaNamesByUId(IEnumerable<string> uIds) {
		var expressions = new JArray();
		foreach (string uId in uIds) {
			expressions.Add(new JObject {
				["expressionType"] = 2,
				["parameter"] = new JObject { ["dataValueType"] = 0, ["value"] = uId }
			});
		}
		return Query("SysSchema",
			new JObject { ["UId"] = Column("UId"), ["Name"] = Column("Name") },
			Group(("byUId", new JObject {
				["filterType"] = 4,
				["comparisonType"] = 3,
				["isEnabled"] = true,
				["leftExpression"] = new JObject { ["expressionType"] = 0, ["columnPath"] = "UId" },
				["rightExpressions"] = expressions
			})), expressions.Count);
	}
}
