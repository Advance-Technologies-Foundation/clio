namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using Clio.Common.EntitySchema;
using CommandLine;
using Newtonsoft.Json.Linq;
using static Clio.Command.ClassicEntitySchemaQuery;

/// <summary>Options for the <c>list-entity-client-schemas</c> command.</summary>
[Verb("list-entity-client-schemas", Aliases = ["migration-unit-resolve"],
	HelpText = "Resolve the page-role graph of an entity for a Classic->Freedom migration: its Classic sections, " +
		"edit pages (including per-type/typed pages), and add mini pages, each classified classic, freedom, or unknown. " +
		"Per-type edit pages also carry the Type's display name (typeColumnDisplayValue) when it resolves on-stand. " +
		"One level only — the skill recurses into detail entities. Pure ESQ; no schema-body parsing.")]
public class ListEntityClientSchemasOptions : EnvironmentOptions {

	/// <summary>Entity schema name whose Classic UI page-role graph is resolved, e.g. <c>Contract</c>.</summary>
	[Option("entity-name", Required = true, HelpText = "Entity schema name, e.g. 'Contract' or 'SupportUnit'")]
	public string EntityName { get; set; }
}

/// <summary>A Classic <c>SysModule</c> section bound to the entity, with its card page and migration classification.</summary>
public sealed class MigrationSectionInfo {
	/// <summary>Section caption (display name).</summary>
	[System.Text.Json.Serialization.JsonPropertyName("caption")] public string Caption { get; set; }
	/// <summary>Section code.</summary>
	[System.Text.Json.Serialization.JsonPropertyName("code")] public string Code { get; set; }
	/// <summary>Name of the section (list) schema.</summary>
	[System.Text.Json.Serialization.JsonPropertyName("sectionSchema")] public string SectionSchema { get; set; }
	/// <summary>Name of the card (edit page) schema.</summary>
	[System.Text.Json.Serialization.JsonPropertyName("cardSchema")] public string CardSchema { get; set; }
	/// <summary>UId of the card schema.</summary>
	[System.Text.Json.Serialization.JsonPropertyName("cardSchemaUId")] public string CardSchemaUId { get; set; }
	/// <summary>Parent template name of the card schema (drives the <see cref="Kind"/> classification).</summary>
	[System.Text.Json.Serialization.JsonPropertyName("template")] public string Template { get; set; }
	/// <summary>Migration classification of the card: <c>classic</c>, <c>freedom</c>, or <c>unknown</c>.</summary>
	[System.Text.Json.Serialization.JsonPropertyName("kind")] public string Kind { get; set; }
	/// <summary>Whether the section is typed (its module entity declares a type column).</summary>
	[System.Text.Json.Serialization.JsonPropertyName("isTyped")] public bool IsTyped { get; set; }
}

/// <summary>A Classic <c>SysModuleEdit</c> edit page (optionally per type) bound to the entity, with its add mini page.</summary>
public sealed class MigrationEditPageInfo {
	/// <summary>The type-column value this edit page is registered for (empty for the default page).</summary>
	[System.Text.Json.Serialization.JsonPropertyName("typeColumnValue")] public string TypeColumnValue { get; set; }
	/// <summary>
	/// The display name (Type-lookup caption) of <see cref="TypeColumnValue"/> for a per-type page, resolved by a
	/// deterministic GUID-&gt;caption join against the entity's Type-column reference lookup. <c>null</c> when the
	/// entity is not typed, the page is the default page, or the name could not be resolved on-stand — the offline
	/// migration engine then renders the raw GUID with a "resolve the type name on-stand" note (ENG-96327).
	/// </summary>
	[System.Text.Json.Serialization.JsonPropertyName("typeColumnDisplayValue")] public string TypeColumnDisplayValue { get; set; }
	/// <summary>Name of the card (edit page) schema.</summary>
	[System.Text.Json.Serialization.JsonPropertyName("cardSchema")] public string CardSchema { get; set; }
	/// <summary>UId of the card schema.</summary>
	[System.Text.Json.Serialization.JsonPropertyName("cardSchemaUId")] public string CardSchemaUId { get; set; }
	/// <summary>Parent template name of the card schema (drives the <see cref="Kind"/> classification).</summary>
	[System.Text.Json.Serialization.JsonPropertyName("template")] public string Template { get; set; }
	/// <summary>Migration classification of the card: <c>classic</c>, <c>freedom</c>, or <c>unknown</c>.</summary>
	[System.Text.Json.Serialization.JsonPropertyName("kind")] public string Kind { get; set; }
	/// <summary>Name of the add mini page schema, when one is registered.</summary>
	[System.Text.Json.Serialization.JsonPropertyName("miniPageSchema")] public string MiniPageSchema { get; set; }
	/// <summary>UId of the add mini page schema.</summary>
	[System.Text.Json.Serialization.JsonPropertyName("miniPageSchemaUId")] public string MiniPageSchemaUId { get; set; }
	/// <summary>Parent template name of the mini page schema.</summary>
	[System.Text.Json.Serialization.JsonPropertyName("miniPageTemplate")] public string MiniPageTemplate { get; set; }
	/// <summary>Migration classification of the mini page: <c>classic</c>, <c>freedom</c>, or <c>unknown</c>.</summary>
	[System.Text.Json.Serialization.JsonPropertyName("miniPageKind")] public string MiniPageKind { get; set; }
	/// <summary>The mini page modes registration string.</summary>
	[System.Text.Json.Serialization.JsonPropertyName("miniPageModes")] public string MiniPageModes { get; set; }
}

/// <summary>Response of the <c>list-entity-client-schemas</c> command: the entity's resolved page-role graph.</summary>
public sealed class ListEntityClientSchemasResponse {
	/// <summary>Whether the page-role graph was resolved.</summary>
	[System.Text.Json.Serialization.JsonPropertyName("success")] public bool Success { get; set; }
	/// <summary>The resolved entity schema name.</summary>
	[System.Text.Json.Serialization.JsonPropertyName("entity")] public string Entity { get; set; }
	/// <summary>UId of the entity's base schema.</summary>
	[System.Text.Json.Serialization.JsonPropertyName("entityUId")] public string EntityUId { get; set; }
	/// <summary>Classic sections bound to the entity.</summary>
	[System.Text.Json.Serialization.JsonPropertyName("sections")] public List<MigrationSectionInfo> Sections { get; set; }
	/// <summary>Classic edit pages (including per-type pages) bound to the entity.</summary>
	[System.Text.Json.Serialization.JsonPropertyName("editPages")] public List<MigrationEditPageInfo> EditPages { get; set; }
	/// <summary>Non-fatal warnings (e.g. a lookup that hit its rowCount cap); <c>null</c> when none.</summary>
	[System.Text.Json.Serialization.JsonPropertyName("warnings")] public List<string> Warnings { get; set; }
	/// <summary>Advisory note describing scope and how to interpret an empty result.</summary>
	[System.Text.Json.Serialization.JsonPropertyName("note")] public string Note { get; set; }
	/// <summary>Failure reason when <see cref="Success"/> is <c>false</c>; <c>null</c> otherwise.</summary>
	[System.Text.Json.Serialization.JsonPropertyName("error")] public string Error { get; set; }
}

/// <summary>
/// Resolves the Classic UI page-role graph of an entity (sections, edit pages, add mini pages) via DataService
/// ESQ and classifies each page as <c>classic</c>, <c>freedom</c>, or <c>unknown</c> for a Classic-&gt;Freedom
/// migration. One level only — callers recurse into detail entities by invoking the command per detail entity.
/// </summary>
internal class ListEntityClientSchemasCommand : Command<ListEntityClientSchemasOptions> {

	// Single-sourced with ClassicEntitySchemaQuery so the sentinel/cap cannot drift between the shared query
	// builder and its callers.
	private const string EmptyGuid = ClassicEntitySchemaQuery.EmptyGuid;
	// Migration classification values emitted in MigrationSectionInfo.Kind / MigrationEditPageInfo.Kind /
	// MigrationEditPageInfo.MiniPageKind. `unknown` covers a missing template or one in neither known set.
	private const string KindClassic = "classic";
	private const string KindFreedom = "freedom";
	private const string KindUnknown = "unknown";
	private const int SectionRowCount = ClassicEntitySchemaQuery.SectionRowCount;
	private const int EditPageRowCount = 100;

	// SysModuleEntity/SysModuleEdit column names, single-sourced so the selects and the row reads below cannot
	// drift on a column name (a typo there reads as "no schema bound" rather than failing).
	private const string SectionSchemaUIdColumn = "SectionSchemaUId";
	private const string CardSchemaUIdColumn = "CardSchemaUId";
	private const string MiniPageSchemaUIdColumn = "MiniPageSchemaUId";
	private const string TypeColumnUIdColumn = "TypeColumnUId";

	private static readonly HashSet<string> FreedomTemplates = new(StringComparer.OrdinalIgnoreCase) {
		"PageWithTabsAndProgressBarTemplate",
		"PageWithTabsFreedomTemplate",
		"PageWithRightAreaAndTabsFreedomTemplate",
		"PageWithTopAreaAndTabsFreedomTemplate",
		"BaseMiniPageTemplate",
		"PageWithAreaFreedomTemplate",
		"BaseHomePage",
		"BaseDashboardTemplate",
		"BaseSidebarTemplate",
		"ListPageV3Template",
		"ListPageV2Template",
		"BlankPageTemplate",
		"FormPageTemplate",
		"MobilePageWithTabsFreedomTemplate",
		"BaseMobilePageTemplate",
		"BaseMobileListTemplate",
		"BlankMobilePageTemplate"
	};

	private static readonly HashSet<string> ClassicTemplates = new(StringComparer.OrdinalIgnoreCase) {
		"BaseModulePageV2",
		"BasePageV2"
	};

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly ILogger _logger;
	private readonly IRuntimeEntitySchemaReader _runtimeEntitySchemaReader;
	private readonly ILookupDefaultDisplayValueResolver _lookupDisplayValueResolver;

	/// <summary>Initializes a new instance of the <see cref="ListEntityClientSchemasCommand"/> class.</summary>
	public ListEntityClientSchemasCommand(
		IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder, ILogger logger,
		IRuntimeEntitySchemaReader runtimeEntitySchemaReader,
		ILookupDefaultDisplayValueResolver lookupDisplayValueResolver) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_logger = logger;
		_runtimeEntitySchemaReader = runtimeEntitySchemaReader;
		_lookupDisplayValueResolver = lookupDisplayValueResolver;
	}

	/// <summary>
	/// Resolves the entity's Classic UI page-role graph. Returns <c>true</c> with a populated
	/// <paramref name="response"/> on success; <c>false</c> with <see cref="ListEntityClientSchemasResponse.Error"/>
	/// set when the entity cannot be resolved or a DataService call fails.
	/// </summary>
	/// <param name="options">The command options carrying the entity name and environment.</param>
	/// <param name="response">The resolved page-role graph, or a failure envelope.</param>
	/// <returns><c>true</c> when the graph was resolved; otherwise <c>false</c>.</returns>
	public virtual bool TryResolve(ListEntityClientSchemasOptions options, out ListEntityClientSchemasResponse response) {
		try {
			if (string.IsNullOrWhiteSpace(options.EntityName)) {
				response = new ListEntityClientSchemasResponse { Success = false, Error = "entity-name is required" };
				return false;
			}
			var warnings = new List<string>();
			JArray entityRows = Select(ClassicEntitySchemaQuery.BuildSelectEntity(options.EntityName));
			if (entityRows.Count == ClassicEntitySchemaQuery.EntityRowCount) {
				warnings.Add(
					$"Entity schema lookup reached the rowCount cap ({ClassicEntitySchemaQuery.EntityRowCount}); verify the entity result before using it.");
			}
			(string entityUId, string entityError) = ClassicEntitySchemaQuery.ResolveEntityUId(options.EntityName, entityRows);
			if (entityUId == null) {
				response = new ListEntityClientSchemasResponse {
					Success = false, Error = entityError };
				return false;
			}

			JArray moduleRows = Select(BuildSelectSections(entityUId));
			JArray editRows = Select(BuildSelectEditPages(entityUId));
			if (moduleRows.Count == SectionRowCount) {
				warnings.Add(
					$"Section lookup reached the rowCount cap ({SectionRowCount}); the section list may be truncated.");
			}
			if (editRows.Count == EditPageRowCount) {
				warnings.Add(
					$"Edit-page lookup reached the rowCount cap ({EditPageRowCount}); the edit-page list may be truncated.");
			}

			IReadOnlyDictionary<string, (string name, string template)> schemaMetas = ResolveSchemaMetaBatch(
				CollectSchemaUIds(moduleRows, editRows));
			(string name, string template) Meta(string uId) {
				if (string.IsNullOrWhiteSpace(uId) || uId == EmptyGuid) return (null, null);
				return schemaMetas.TryGetValue(uId, out var m) ? m : (null, null);
			}

			var sections = moduleRows.Select(r => {
				string sectionUId = r[SectionSchemaUIdColumn]?.ToString();
				string cardUId = r[CardSchemaUIdColumn]?.ToString();
				string typeColUId = r[TypeColumnUIdColumn]?.ToString();
				(string cardName, string template) = Meta(cardUId);
				return new MigrationSectionInfo {
					Caption = r["Caption"]?.ToString(),
					Code = r["Code"]?.ToString(),
					SectionSchema = Meta(sectionUId).name,
					CardSchema = cardName,
					CardSchemaUId = cardUId,
					Template = template,
					Kind = ClassifyKind(template),
					IsTyped = !string.IsNullOrWhiteSpace(typeColUId) && typeColUId != EmptyGuid
				};
			}).ToList();

			// Each edit page is bundled with its own row's TypeColumnUId here (where the SysModuleEdit row is in scope),
			// so the type-name enrichment never has to re-index the raw rows in parallel with the pages (ENG-96553).
			var editPageEntries = editRows.Select(r => {
				string cardUId = r[CardSchemaUIdColumn]?.ToString();
				string miniUId = r[MiniPageSchemaUIdColumn]?.ToString();
				(string cardName, string template) = Meta(cardUId);
				(string miniName, string miniTemplate) = Meta(miniUId);
				return (
					page: new MigrationEditPageInfo {
						TypeColumnValue = r["TypeColumnValue"]?.ToString(),
						CardSchema = cardName,
						CardSchemaUId = cardUId,
						Template = template,
						Kind = ClassifyKind(template),
						MiniPageSchema = miniName,
						MiniPageSchemaUId = miniUId,
						MiniPageTemplate = miniTemplate,
						MiniPageKind = ClassifyKind(miniTemplate),
						MiniPageModes = r["MiniPageModes"]?.ToString()
					},
					typeColumnUId: r[TypeColumnUIdColumn]?.ToString());
			}).ToList();
			List<MigrationEditPageInfo> editPages = editPageEntries.Select(entry => entry.page).ToList();

			// ENG-96553: for a typed entity, resolve each per-type edit page's Type-lookup GUID to its display name so
			// the migration plan can name the type instead of the raw GUID. The deterministic GUID->caption join is done
			// here because the migration engine is a pure offline function over the manifest and cannot query the stand.
			EnrichTypeDisplayNames(options.EntityName, editPageEntries);

			bool empty = sections.Count == 0 && editPages.Count == 0;
			response = new ListEntityClientSchemasResponse {
				Success = true,
				Entity = options.EntityName,
				EntityUId = entityUId,
				Sections = sections,
				EditPages = editPages,
				Warnings = warnings.Count > 0 ? warnings : null,
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

	/// <summary>Classifies a parent template name as <c>freedom</c>, <c>classic</c>, or <c>unknown</c>.</summary>
	internal static string ClassifyKind(string template) {
		if (string.IsNullOrWhiteSpace(template)) return KindUnknown;
		template = template.Trim();
		if (FreedomTemplates.Contains(template))
			return KindFreedom;
		if (ClassicTemplates.Contains(template))
			return KindClassic;
		return KindUnknown;
	}

	/// <summary>
	/// Best-effort enrichment: sets <see cref="MigrationEditPageInfo.TypeColumnDisplayValue"/> for each per-type edit
	/// page by resolving its Type-lookup GUID against the entity's Type-column reference lookup. Fail-soft — any gap
	/// (non-typed entity, unresolvable type column, denied read) leaves the page GUID-only and never fails the resolve.
	/// </summary>
	private void EnrichTypeDisplayNames(
		string entityName, IReadOnlyList<(MigrationEditPageInfo page, string typeColumnUId)> editPageEntries) {
		try {
			Dictionary<string, HashSet<Guid>> valuesByTypeColumn = GroupTypeValuesByColumn(editPageEntries);
			if (valuesByTypeColumn.Count == 0) {
				return;
			}
			IReadOnlyDictionary<string, IReadOnlyDictionary<Guid, string>> captionsByTypeColumn =
				ResolveCaptionsByTypeColumn(entityName, valuesByTypeColumn);
			int assigned = AssignTypeDisplayNames(editPageEntries, captionsByTypeColumn);

			// Diagnosability (ENG-96553): a resolved-but-null TypeColumnDisplayValue is byte-identical to the legitimate
			// non-typed null, and the catches within only fire on a THROWN read. So if a typed entity produced per-type
			// GUID values yet NONE resolved to a caption, that is a systemic signal (an unreadable Type lookup, or a
			// GUID string-form / TypeColumnUId-to-column mismatch) rather than the usual one-off deleted record —
			// surface it once so a silent full degradation to GUID-only is not mistaken for "not typed".
			if (assigned == 0) {
				_logger.WriteWarning(
					$"Resolved no type display names for typed entity '{entityName}' although {valuesByTypeColumn.Count} Type column(s) " +
					"carried per-type values; the plan will show raw GUIDs. Verify the Type lookup is readable and the values match its records.");
			}
		} catch (Exception ex) {
			// Type-name enrichment is a readability aid only: the engine renders a GUID + "resolve the type name
			// on-stand" fallback when it is absent (ENG-96327), so a failure here must never turn a successful
			// page-role resolve into an error.
			_logger.WriteWarning($"Could not resolve type display names for entity '{entityName}'. {ex.Message}");
		}
	}

	/// <summary>
	/// Groups each page's GUID-parseable per-type value under its OWN row's Type column UId. Every entry already
	/// carries its own TypeColumnUId (bundled at projection time — no positional re-indexing), so an entity registered
	/// through more than one SysModuleEntity row with different type columns keeps each page under its own column. The
	/// default page (empty value) and any non-GUID registration contribute nothing, so a non-typed entity yields none.
	/// </summary>
	private static Dictionary<string, HashSet<Guid>> GroupTypeValuesByColumn(
		IReadOnlyList<(MigrationEditPageInfo page, string typeColumnUId)> editPageEntries) {
		var valuesByTypeColumn = new Dictionary<string, HashSet<Guid>>(StringComparer.OrdinalIgnoreCase);
		foreach ((MigrationEditPageInfo page, string typeColumnUId) in editPageEntries) {
			if (!Guid.TryParse(page.TypeColumnValue, out Guid typeValue)
				|| string.IsNullOrWhiteSpace(typeColumnUId) || typeColumnUId == EmptyGuid) {
				continue;
			}
			if (!valuesByTypeColumn.TryGetValue(typeColumnUId, out HashSet<Guid> values)) {
				values = [];
				valuesByTypeColumn[typeColumnUId] = values;
			}
			values.Add(typeValue);
		}
		return valuesByTypeColumn;
	}

	/// <summary>
	/// Maps each Type column UId to its reference (lookup) schema from the entity's runtime schema (one read), then
	/// batch-resolves that column's distinct GUIDs to captions through the shared <see cref="ILookupDefaultDisplayValueResolver"/>
	/// (one chunked IN query per reference schema). A per-column failure is isolated so it cannot discard captions
	/// already resolvable for the entity's other type columns.
	/// </summary>
	private IReadOnlyDictionary<string, IReadOnlyDictionary<Guid, string>> ResolveCaptionsByTypeColumn(
		string entityName, Dictionary<string, HashSet<Guid>> valuesByTypeColumn) {
		IReadOnlyList<RuntimeEntitySchemaColumnResult> columns = _runtimeEntitySchemaReader.GetByName(entityName).Columns;
		// The resolver reads via the injected environment-bound client; only TimeOut is taken from the options object,
		// so a default RemoteCommandOptions is sufficient (this command carries no timeout option of its own).
		var resolverOptions = new RemoteCommandOptions();
		var captionsByTypeColumn = new Dictionary<string, IReadOnlyDictionary<Guid, string>>(StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<string, HashSet<Guid>> group in valuesByTypeColumn) {
			if (!Guid.TryParse(group.Key, out Guid typeColumnGuid)) {
				continue;
			}
			string referenceSchemaName = columns
				.FirstOrDefault(column => column.UId == typeColumnGuid)?.ReferenceSchemaName;
			if (string.IsNullOrWhiteSpace(referenceSchemaName)) {
				continue;
			}
			try {
				IReadOnlyDictionary<Guid, LookupDefaultResolution> resolutions =
					_lookupDisplayValueResolver.ResolveMany(referenceSchemaName, group.Value, resolverOptions);
				var captions = new Dictionary<Guid, string>();
				foreach (KeyValuePair<Guid, LookupDefaultResolution> resolution in resolutions) {
					if (!string.IsNullOrWhiteSpace(resolution.Value?.DisplayValue)) {
						captions[resolution.Key] = resolution.Value.DisplayValue;
					}
				}
				captionsByTypeColumn[group.Key] = captions;
			} catch (Exception ex) {
				// ResolveMany is fail-soft, but keep the per-column guard as defence in depth so an unexpected fault in
				// one reference schema cannot discard captions already resolvable for the entity's OTHER type columns.
				_logger.WriteWarning(
					$"Could not resolve type display names for reference schema '{referenceSchemaName}' on entity '{entityName}'. {ex.Message}");
			}
		}
		return captionsByTypeColumn;
	}

	/// <summary>
	/// Assigns each per-type page the caption resolved for ITS OWN type column + value and returns how many were set.
	/// </summary>
	private static int AssignTypeDisplayNames(
		IReadOnlyList<(MigrationEditPageInfo page, string typeColumnUId)> editPageEntries,
		IReadOnlyDictionary<string, IReadOnlyDictionary<Guid, string>> captionsByTypeColumn) {
		int assigned = 0;
		foreach ((MigrationEditPageInfo page, string typeColumnUId) in editPageEntries) {
			// Look up by the parsed Guid so the page's TypeColumnValue and the resolved Id match regardless of lexical
			// form (braces / casing); the default page (empty value) simply fails Guid.TryParse and is skipped.
			if (!string.IsNullOrWhiteSpace(typeColumnUId)
				&& Guid.TryParse(page.TypeColumnValue, out Guid typeValueGuid)
				&& captionsByTypeColumn.TryGetValue(typeColumnUId, out IReadOnlyDictionary<Guid, string> captions)
				&& captions.TryGetValue(typeValueGuid, out string caption)) {
				page.TypeColumnDisplayValue = caption;
				assigned++;
			}
		}
		return assigned;
	}

	private IReadOnlyDictionary<string, (string name, string template)> ResolveSchemaMetaBatch(IEnumerable<string> uIds) {
		string[] distinctUIds = uIds
			.Where(uId => !string.IsNullOrWhiteSpace(uId) && uId != EmptyGuid)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		var result = new Dictionary<string, (string name, string template)>(StringComparer.OrdinalIgnoreCase);
		if (distinctUIds.Length == 0) {
			return result;
		}
		JArray rows = Select(BuildSelectSchemasByUId(distinctUIds));
		foreach (JToken row in rows) {
			string uId = row["UId"]?.ToString();
			if (!string.IsNullOrWhiteSpace(uId)) {
				result[uId] = (row["Name"]?.ToString(), row["ParentName"]?.ToString());
			}
		}
		return result;
	}

	private JArray Select(JObject query) =>
		ClassicEntitySchemaQuery.Select(_applicationClient, _serviceUrlBuilder, query);

	/// <inheritdoc />
	public override int Execute(ListEntityClientSchemasOptions options) {
		bool success = TryResolve(options, out ListEntityClientSchemasResponse response);
		_logger.WriteInfo(System.Text.Json.JsonSerializer.Serialize(response));
		return success ? 0 : 1;
	}

	// ---- ESQ builders ----
	private static IEnumerable<string> CollectSchemaUIds(JArray moduleRows, JArray editRows) {
		foreach (JToken row in moduleRows) {
			yield return row[SectionSchemaUIdColumn]?.ToString();
			yield return row[CardSchemaUIdColumn]?.ToString();
		}
		foreach (JToken row in editRows) {
			yield return row[CardSchemaUIdColumn]?.ToString();
			yield return row[MiniPageSchemaUIdColumn]?.ToString();
		}
	}

	// Column-set-specific selects (kept local); the DSL + entity resolution are single-sourced in
	// ClassicEntitySchemaQuery (imported statically) so this command and ClassicSectionSchemaResolver
	// cannot drift on the base-row rule or a filter's dataValueType.
	private static JObject BuildSelectSchemasByUId(IReadOnlyCollection<string> uIds) => Query("SysSchema",
		new JObject { ["UId"] = Column("UId"), ["Name"] = Column("Name"), ["ParentName"] = Column("Parent.Name") },
		Group(("byUId", InFilter("UId", uIds, 0))), uIds.Count);

	private static JObject BuildSelectSections(string entityUId) => Query("SysModule",
		new JObject {
			["Caption"] = Column("Caption"), ["Code"] = Column("Code"),
			[SectionSchemaUIdColumn] = Column(SectionSchemaUIdColumn), [CardSchemaUIdColumn] = Column(CardSchemaUIdColumn),
			[TypeColumnUIdColumn] = Column($"SysModuleEntity.{TypeColumnUIdColumn}")
		},
		Group(("byEntity", Eq("SysModuleEntity.SysEntitySchemaUId", entityUId, 0))), SectionRowCount);

	private static JObject BuildSelectEditPages(string entityUId) => Query("SysModuleEdit",
		new JObject {
			["TypeColumnValue"] = Column("TypeColumnValue"), [CardSchemaUIdColumn] = Column(CardSchemaUIdColumn),
			[MiniPageSchemaUIdColumn] = Column(MiniPageSchemaUIdColumn), ["MiniPageModes"] = Column("MiniPageModes"),
			// The Type column UId of each row's SysModuleEntity — read and resolved PER ROW (not assumed identical
			// across rows) so an entity registered through several SysModuleEntity rows with different type columns
			// maps each page to its own reference lookup (ENG-96553).
			[TypeColumnUIdColumn] = Column($"SysModuleEntity.{TypeColumnUIdColumn}")
		},
		Group(("byEntity", Eq("SysModuleEntity.SysEntitySchemaUId", entityUId, 0))), EditPageRowCount);
}
