namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Newtonsoft.Json.Linq;

/// <summary>
/// One child page a detail entity registers in <c>SysModuleEdit</c>: either the edit card or its add mini page.
/// </summary>
/// <param name="EntityName">
/// The detail entity the page is registered for, as the caller spelled it — so the caller can attribute the page
/// back to the detail it came from without re-resolving anything.
/// </param>
/// <param name="SchemaName">The client-unit schema name of the page.</param>
/// <param name="IsMiniPage">
/// <c>true</c> when the page came from <c>MiniPageSchemaUId</c> (an add mini page), <c>false</c> for the edit card
/// from <c>CardSchemaUId</c>.
/// </param>
public sealed record ClassicChildPage(string EntityName, string SchemaName, bool IsMiniPage);

/// <summary>Outcome of a child-page lookup over a set of detail entities.</summary>
/// <param name="ChildPages">
/// The resolved child pages in <c>SysModuleEdit</c> row order, deduplicated per entity. Empty when none of the
/// entities registers an edit page — a legitimate result (many detail entities have no Classic edit page at all),
/// not a failure.
/// </param>
/// <param name="Warnings">
/// Non-fatal gaps the caller must surface (a row cap reached, an entity whose metadata did not resolve). Empty when
/// the lookup was complete.
/// </param>
/// <param name="Error">
/// Reason the lookup could not complete (DataService failure); <c>null</c> when it ran to completion. An empty
/// <see cref="ChildPages"/> with a <c>null</c> error means "these entities register no child pages".
/// </param>
public sealed record ClassicChildPageLookup(
	IReadOnlyList<ClassicChildPage> ChildPages,
	IReadOnlyList<string> Warnings,
	string Error);

/// <summary>
/// Resolves the child pages (edit card + add mini page) that Classic details' entities register in
/// <c>SysModuleEdit</c>, so a migration manifest can nest them.
/// </summary>
/// <remarks>
/// This replaces resolving a detail's child page by scanning the detail body for a
/// <c>getEditPageName</c>/<c>editPageName</c>/<c>EditPageSchemaName</c> token. That token belongs to the pre-V2
/// <c>*Detail</c> generation and no shipped page references a schema carrying it, so the body-scan route yields a
/// measured ZERO on the product (0 of 845 page-detail pairs; re-measured live as 0 of 24 gathered
/// <c>AccountPageV2</c> details) — see ENG-94401. The registration lives in metadata, so the metadata is the
/// authoritative source.
/// </remarks>
public interface IClassicDetailEditPageResolver {

	/// <summary>Resolves the child pages every entity in <paramref name="entityNames"/> registers.</summary>
	/// <param name="entityNames">
	/// Detail entity schema names, e.g. <c>Contract</c>. Blank entries and duplicates are ignored. The whole set is
	/// resolved in three batched queries regardless of its size, so callers should pass every detail entity at once
	/// rather than calling per detail.
	/// </param>
	/// <returns>
	/// The resolved child pages, or an <see cref="ClassicChildPageLookup.Error"/> describing why the lookup could not
	/// complete. Never throws: transport and parse failures are reported through the error field so the caller can
	/// degrade to its own heuristics.
	/// </returns>
	ClassicChildPageLookup ResolveChildPages(IReadOnlyCollection<string> entityNames);
}

/// <inheritdoc />
public sealed class ClassicDetailEditPageResolver : IClassicDetailEditPageResolver {

	// Single-sourced with ClassicEntitySchemaQuery so the sentinel cannot drift between the shared query builder and
	// its callers.
	private const string EmptyGuid = ClassicEntitySchemaQuery.EmptyGuid;

	// Per-input row caps. The lookups are batched, so the cap scales with the input size instead of being a fixed
	// number that a large detail set would silently truncate against; reaching one is reported as a warning.
	private const int EntityRowsPerName = ClassicEntitySchemaQuery.EntityRowCount;
	private const int EditPageRowsPerEntity = 20;

	// SysModuleEdit/SysSchema column names, single-sourced so the selects and the row reads cannot drift on a column
	// name (a typo there reads as "no page registered" rather than failing).
	private const string CardSchemaUIdColumn = "CardSchemaUId";
	private const string MiniPageSchemaUIdColumn = "MiniPageSchemaUId";
	private const string EntitySchemaUIdColumn = "SysEntitySchemaUId";
	private const string EntitySchemaUIdColumnPath = "SysModuleEntity.SysEntitySchemaUId";

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;

	/// <summary>Initializes a new instance of the <see cref="ClassicDetailEditPageResolver"/> class.</summary>
	/// <param name="applicationClient">Client used to call the DataService on the target environment.</param>
	/// <param name="serviceUrlBuilder">Builds the absolute service URLs for the target environment.</param>
	public ClassicDetailEditPageResolver(IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
	}

	/// <inheritdoc />
	public ClassicChildPageLookup ResolveChildPages(IReadOnlyCollection<string> entityNames) {
		string[] names = (entityNames ?? Array.Empty<string>())
			.Where(name => !string.IsNullOrWhiteSpace(name))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (names.Length == 0) {
			return Empty(null);
		}
		try {
			var warnings = new List<string>();
			// 1. Every detail entity name -> its base-schema UId, in ONE query. The per-name base-row rule
			//    (ExtendParent == false) is reused from the shared resolver so a batch cannot resolve a different
			//    physical schema than the single-name path would.
			IReadOnlyDictionary<string, string> uIdByEntity = ResolveEntityUIds(names, warnings);
			if (uIdByEntity.Count == 0) {
				return new ClassicChildPageLookup(Array.Empty<ClassicChildPage>(), warnings, null);
			}
			// 2. Every SysModuleEdit registration for those entities, in ONE query. The entity UId travels back as a
			//    column so each row can be attributed to the entity it belongs to.
			string[] entityUIds = uIdByEntity.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
			int editCap = entityUIds.Length * EditPageRowsPerEntity;
			JArray editRows = Select(BuildSelectEditPages(entityUIds, editCap));
			if (editRows.Count >= editCap) {
				warnings.Add(
					$"Child-page lookup reached the rowCount cap ({editCap}); the child-page list may be truncated.");
			}
			if (editRows.Count == 0) {
				return new ClassicChildPageLookup(Array.Empty<ClassicChildPage>(), warnings, null);
			}
			// 3. Page UId -> schema name, in ONE query. Only real references are looked up: an unset reference comes
			//    back as the all-zero GUID, NOT as null, so filtering on "not null" alone would inflate the set
			//    (measured 17.8x on MiniPageSchemaUId) and then resolve nothing for the surplus.
			IReadOnlyDictionary<string, string> nameByUId = ResolveSchemaNames(editRows);
			return new ClassicChildPageLookup(
				BuildChildPages(editRows, uIdByEntity, nameByUId), warnings, null);
		}
		catch (Exception ex) {
			return Empty(ex.Message);
		}
	}

	private static ClassicChildPageLookup Empty(string error) =>
		new(Array.Empty<ClassicChildPage>(), Array.Empty<string>(), error);

	// Groups the entity rows by name and applies the shared base-row rule per group. An entity the metadata does not
	// answer for is reported as a warning rather than dropped silently: the caller would otherwise read "no child
	// pages registered" for what is really "we could not look".
	private IReadOnlyDictionary<string, string> ResolveEntityUIds(IReadOnlyCollection<string> names, List<string> warnings) {
		int entityCap = names.Count * EntityRowsPerName;
		JArray entityRows = Select(BuildSelectEntitiesByName(names, entityCap));
		if (entityRows.Count >= entityCap) {
			warnings.Add(
				$"Detail-entity lookup reached the rowCount cap ({entityCap}); some detail entities may be unresolved.");
		}
		var uIdByEntity = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (IGrouping<string, JToken> group in entityRows
			.GroupBy(row => row["Name"]?.ToString(), StringComparer.OrdinalIgnoreCase)) {
			if (string.IsNullOrWhiteSpace(group.Key)) {
				continue;
			}
			(string uId, string _) = ClassicEntitySchemaQuery.ResolveEntityUId(group.Key, new JArray(group));
			if (uId != null) {
				uIdByEntity[group.Key] = uId;
			}
		}
		string[] unresolved = names
			.Where(name => !uIdByEntity.ContainsKey(name))
			.ToArray();
		if (unresolved.Length > 0) {
			warnings.Add(
				"No entity metadata resolved for detail " + (unresolved.Length == 1 ? "entity" : "entities") + " " +
				string.Join(", ", unresolved) +
				"; their child pages could not be looked up, which is NOT the same as 'they have none'.");
		}
		return uIdByEntity;
	}

	// The card/mini-page UIds referenced by the rows, resolved to schema names in one query. The Guid.Empty sentinel
	// is dropped here — an unset CardSchemaUId/MiniPageSchemaUId is "no page registered", not a page to resolve.
	private IReadOnlyDictionary<string, string> ResolveSchemaNames(JArray editRows) {
		string[] pageUIds = editRows
			.SelectMany(row => new[] { row[CardSchemaUIdColumn]?.ToString(), row[MiniPageSchemaUIdColumn]?.ToString() })
			.Where(IsRealReference)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		var nameByUId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (pageUIds.Length == 0) {
			return nameByUId;
		}
		foreach (JToken row in Select(BuildSelectSchemaNamesByUId(pageUIds))) {
			string uId = row["UId"]?.ToString();
			string name = row["Name"]?.ToString();
			if (!string.IsNullOrWhiteSpace(uId) && !string.IsNullOrWhiteSpace(name)) {
				nameByUId[uId] = name;
			}
		}
		return nameByUId;
	}

	// Projects the rows into child pages, preserving SysModuleEdit row order (the default registration comes first on
	// a typed entity) and deduplicating per entity: one entity commonly registers the same card across several
	// TypeColumnValue rows, and the caller must not fold the same page twice.
	private static List<ClassicChildPage> BuildChildPages(
		JArray editRows,
		IReadOnlyDictionary<string, string> uIdByEntity,
		IReadOnlyDictionary<string, string> nameByUId) {
		// Entity UId -> the entity name as the caller spelled it, so a page is attributed with the caller's spelling.
		var entityByUId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<string, string> entry in uIdByEntity) {
			entityByUId[entry.Value] = entry.Key;
		}
		var childPages = new List<ClassicChildPage>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (JToken row in editRows) {
			string entityUId = row[EntitySchemaUIdColumn]?.ToString();
			if (!IsRealReference(entityUId) || !entityByUId.TryGetValue(entityUId, out string entityName)) {
				continue; // a row we cannot attribute back to a requested entity contributes nothing
			}
			AddChildPage(childPages, seen, entityName, row[CardSchemaUIdColumn]?.ToString(), nameByUId, false);
			AddChildPage(childPages, seen, entityName, row[MiniPageSchemaUIdColumn]?.ToString(), nameByUId, true);
		}
		return childPages;
	}

	private static void AddChildPage(
		List<ClassicChildPage> childPages,
		HashSet<string> seen,
		string entityName,
		string pageUId,
		IReadOnlyDictionary<string, string> nameByUId,
		bool isMiniPage) {
		if (!IsRealReference(pageUId) || !nameByUId.TryGetValue(pageUId, out string schemaName)) {
			return; // unset reference, or a UId SysSchema did not answer for: omit, never fabricate a page name
		}
		if (seen.Add(entityName + "|" + schemaName)) {
			childPages.Add(new ClassicChildPage(entityName, schemaName, isMiniPage));
		}
	}

	// A reference is real only when it is set AND not the all-zero GUID sentinel DataService returns for an unset one.
	private static bool IsRealReference(string uId) =>
		!string.IsNullOrWhiteSpace(uId) && !string.Equals(uId, EmptyGuid, StringComparison.OrdinalIgnoreCase);

	private JArray Select(JObject query) =>
		ClassicEntitySchemaQuery.Select(_applicationClient, _serviceUrlBuilder, query);

	// Column-set-specific selects (kept local); the DSL + the base-row rule are single-sourced in
	// ClassicEntitySchemaQuery so this resolver, ClassicSectionSchemaResolver, and ListEntityClientSchemasCommand
	// cannot drift on a filter's dataValueType or on which SysSchema row is the entity.
	private static JObject BuildSelectEntitiesByName(IReadOnlyCollection<string> names, int rowCount) =>
		ClassicEntitySchemaQuery.Query("SysSchema",
			new JObject {
				["Name"] = ClassicEntitySchemaQuery.Column("Name"),
				["UId"] = ClassicEntitySchemaQuery.Column("UId"),
				["ExtendParent"] = ClassicEntitySchemaQuery.Column("ExtendParent")
			},
			ClassicEntitySchemaQuery.Group(
				("byName", ClassicEntitySchemaQuery.InFilter("Name", names, 1)),
				("byManager", ClassicEntitySchemaQuery.Eq("ManagerName", "EntitySchemaManager", 1))),
			rowCount);

	private static JObject BuildSelectEditPages(IReadOnlyCollection<string> entityUIds, int rowCount) =>
		ClassicEntitySchemaQuery.Query("SysModuleEdit",
			new JObject {
				[EntitySchemaUIdColumn] = ClassicEntitySchemaQuery.Column(EntitySchemaUIdColumnPath),
				[CardSchemaUIdColumn] = ClassicEntitySchemaQuery.Column(CardSchemaUIdColumn),
				[MiniPageSchemaUIdColumn] = ClassicEntitySchemaQuery.Column(MiniPageSchemaUIdColumn)
			},
			ClassicEntitySchemaQuery.Group(
				("byEntity", ClassicEntitySchemaQuery.InFilter(EntitySchemaUIdColumnPath, entityUIds, 0))),
			rowCount);

	private static JObject BuildSelectSchemaNamesByUId(IReadOnlyCollection<string> uIds) =>
		ClassicEntitySchemaQuery.Query("SysSchema",
			new JObject {
				["UId"] = ClassicEntitySchemaQuery.Column("UId"),
				["Name"] = ClassicEntitySchemaQuery.Column("Name")
			},
			ClassicEntitySchemaQuery.Group(("byUId", ClassicEntitySchemaQuery.InFilter("UId", uIds, 0))),
			uIds.Count);
}
