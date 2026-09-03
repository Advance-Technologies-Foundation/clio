namespace Clio.Command.McpServer.Tools.MobilePageConverter.Legacy;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

// LEGACY Mobile-wizard LIST settings -> Freedom UI MOBILE list page conversion ANALYSIS (advisory-only, ENG-95730).
// Second source type of get-mobile-page-conversion-guide, beside the Freedom UI web analysis. Pure: no Creatio I/O.
// Unlike a web page with dozens of components, the target elements are known up front and all of them are
// supported, so the guide is deliberately LEAN: the two elementMap merges the designer itself writes for a generated
// list page (FolderTreeActions bound to the entity, ListItem row), the two data-section diffs, and the column facts in
// one place (legacySource).

/// <summary>
/// Deterministic analyzer for a classic Mobile-wizard list settings schema. Given the merged settings and their
/// classification it returns the same <see cref="MobilePageConversionGuide"/> shape the Freedom UI web analysis
/// returns, so the caller (skill) processes both source types through one contract.
/// </summary>
public static class LegacyMobileListAnalysisService {

	/// <summary>Source type of a legacy Mobile-wizard LIST settings schema.</summary>
	public const string SourceTypeLegacyGridPage = "legacy-mobile-grid-page";

	/// <summary>Source type of a legacy Mobile-wizard RECORD settings schema (detected, not converted — ENG-95731).</summary>
	public const string SourceTypeLegacyRecordPage = "legacy-mobile-record-page";

	/// <summary>Mechanism label reported for a Freedom UI web source.</summary>
	public const string MechanismFreedomWebAnalysis = "freedom-web-analysis";

	/// <summary>Mechanism label reported for a legacy Mobile-wizard settings source.</summary>
	public const string MechanismLegacySettingsConverter = "legacy-mobile-settings-converter";

	/// <summary>The shipped mobile list template every converted legacy list page inherits.</summary>
	public const string RecommendedTemplate = "BaseMobileListTemplate";

	/// <summary>The template's row element the guide merges onto.</summary>
	public const string ListItemName = "ListItem";

	/// <summary>The template's folder-tree action element the guide binds to the entity (folder filtering).</summary>
	public const string FolderTreeActionsName = "FolderTreeActions";

	/// <summary>The folder schema name the shipped template and the designer both put on FolderTreeActions.</summary>
	public const string FolderTreeSourceSchemaName = "FolderTree";

	/// <summary>Default fallback when the environment's SchemaNamePrefix cannot be read.</summary>
	public const string DefaultSchemaNamePrefix = "Usr";

	private const string GuidanceArticleName = "freedom-page-web-to-mobile-conversion";
	private const string PrimaryDataSourceAlias = "PDS";
	private const string ItemsAttribute = "Items";
	private const string ListItemType = "crt.ListItem";
	private const string FolderTreeActionsType = "crt.FolderTreeActions";
	private const string BucketTitle = "title";
	private const string BucketSubtitle = "subtitle";
	private const string BucketGroup = "group";

	private static readonly Regex SchemaNamePattern = new(
		"^Mobile(?<entity>.+?)(?<kind>GridPageSettings|RecordPageSettings)(?<workplace>.*)$",
		RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	/// <summary>Entity and workplace hints parsed from a legacy settings schema name.</summary>
	public sealed record LegacySchemaNameParts(string Entity, string Workplace, bool IsRecordPage);

	/// <summary>
	/// Parses <c>Mobile&lt;Entity&gt;GridPageSettings&lt;Workplace&gt;</c>; null when the name does not follow the pattern.
	/// </summary>
	public static LegacySchemaNameParts TryParseSchemaName(string schemaName) {
		if (string.IsNullOrWhiteSpace(schemaName)) {
			return null;
		}
		Match match = SchemaNamePattern.Match(schemaName.Trim());
		if (!match.Success) {
			return null;
		}
		string workplace = match.Groups["workplace"].Value;
		return new LegacySchemaNameParts(
			match.Groups["entity"].Value,
			string.IsNullOrWhiteSpace(workplace) ? null : workplace,
			match.Groups["kind"].Value.StartsWith("Record", StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Default target mobile page name: <c>&lt;Prefix&gt;&lt;Entity&gt;_MobileListPage</c>, without doubling a prefix
	/// the entity already carries (<c>UsrMK_Test</c> → <c>UsrMK_Test_MobileListPage</c>).
	/// </summary>
	public static string DeriveTargetSchemaName(string entitySchemaName, string schemaNamePrefix) {
		string entity = (entitySchemaName ?? string.Empty).Trim();
		string prefix = (schemaNamePrefix ?? string.Empty).Trim();
		if (entity.Length == 0) {
			return $"{prefix}Mobile_ListPage";
		}
		bool alreadyPrefixed = prefix.Length > 0 && entity.StartsWith(prefix, StringComparison.Ordinal);
		return $"{(alreadyPrefixed ? string.Empty : prefix)}{entity}_MobileListPage";
	}

	/// <summary>The view-model attribute a wizard column binds to: <c>PDS_&lt;path with '.' → '_'&gt;</c>.</summary>
	public static string AttributeName(string columnPath) =>
		$"{PrimaryDataSourceAlias}_{columnPath.Replace('.', '_')}";

	/// <summary>The attribute's model path: <c>PDS.&lt;path with '.' → '_'&gt;</c>.</summary>
	public static string ModelPath(string columnPath) =>
		$"{PrimaryDataSourceAlias}.{columnPath.Replace('.', '_')}";

	/// <summary>
	/// Builds the conversion guide for a legacy Mobile-wizard list settings source.
	/// </summary>
	/// <param name="read">The merged settings read (must be successful).</param>
	/// <param name="classification">Static classification of the merged settings.</param>
	/// <param name="sourcePage">The source schema name.</param>
	/// <param name="suggestedTarget">The target mobile page name.</param>
	/// <param name="sectionRegistration">Read-only section registration facts, or null when not probed.</param>
	/// <returns>The advisory guide.</returns>
	public static MobilePageConversionGuide Analyze(
		LegacyMobileSettingsReadResult read,
		LegacySettingsClassification classification,
		string sourcePage,
		string suggestedTarget,
		SectionRegistrationInfo sectionRegistration) {
		ArgumentNullException.ThrowIfNull(read);
		ArgumentNullException.ThrowIfNull(classification);
		if (!read.Success || read.EffectiveSettings is null) {
			throw new InvalidOperationException("Analyze requires a successful legacy settings read.");
		}
		LegacyGridPageSettings settings = LegacyGridPageSettingsParser.Parse(read.EffectiveSettings);
		var decisions = new List<string>();
		var notes = new List<string>(read.Notes ?? []);
		notes.AddRange(classification.Notes ?? []);

		LegacySchemaNameParts nameParts = TryParseSchemaName(sourcePage);
		if (nameParts is not null
			&& !string.Equals(nameParts.Entity, settings.EntitySchemaName, StringComparison.OrdinalIgnoreCase)) {
			notes.Add($"The schema name suggests entity '{nameParts.Entity}' but the settings bind '{settings.EntitySchemaName}'; the settings are authoritative.");
		}

		// Title: the wizard writes exactly one items column. Lowest row wins when several are present; the rest
		// join the body (reported as adapted) rather than being silently lost.
		LegacyGridColumn titleColumn = settings.Items.FirstOrDefault();
		var bodySource = new List<(LegacyGridColumn Column, string Bucket)>();
		foreach (LegacyGridColumn extra in settings.Items.Skip(1)) {
			bodySource.Add((extra, BucketTitle));
			decisions.Add($"Wizard 'items' bucket holds more than one column; '{extra.ColumnName}' was moved into ListItem.body (the title shows '{titleColumn.ColumnName}'). Confirm or pick another title.");
		}
		bodySource.AddRange(settings.SubtitleItems.Select(c => (c, BucketSubtitle)));
		bodySource.AddRange(settings.GroupItems.Select(c => (c, BucketGroup)));
		if (titleColumn is null) {
			decisions.Add("No title column ('items' bucket) was found in the wizard settings — choose the column that becomes ListItem.title.");
		}

		// Attributes in emission order: body columns, then the title, then PDS_Id (always). Duplicates once.
		var attributeOrder = new List<string>();
		var attributeColumn = new Dictionary<string, string>(StringComparer.Ordinal);
		var bodyMappings = new List<LegacyColumnMappingInfo>();
		var bodyRows = new JsonArray();
		int bodyIndex = 0;
		foreach ((LegacyGridColumn column, string bucket) in bodySource) {
			string attribute = AttributeName(column.ColumnName);
			bodyRows.Add(new JsonObject { ["value"] = $"${attribute}" });
			bodyMappings.Add(Mapping(column, bucket, attribute, $"{ListItemName}.body[{bodyIndex}]"));
			Register(attributeOrder, attributeColumn, attribute, column.ColumnName, notes);
			bodyIndex++;
		}
		LegacyColumnMappingInfo titleMapping = null;
		if (titleColumn is not null) {
			string attribute = AttributeName(titleColumn.ColumnName);
			titleMapping = Mapping(titleColumn, BucketTitle, attribute, $"{ListItemName}.title");
			Register(attributeOrder, attributeColumn, attribute, titleColumn.ColumnName, notes);
		}

		// The template's ListItem merge (key order mirrors the designer's own output: body, title, icon).
		var mobileValues = new JsonObject { ["body"] = bodyRows };
		if (titleMapping is not null) {
			mobileValues["title"] = $"${titleMapping.Attribute}";
		}
		mobileValues["icon"] = null;

		var viewModelAttributes = new JsonObject();
		var dataSourceAttributes = new JsonObject();
		foreach (string attribute in attributeOrder) {
			viewModelAttributes[attribute] = new JsonObject {
				["modelConfig"] = new JsonObject { ["path"] = ModelPath(attributeColumn[attribute]) }
			};
			string columnPath = attributeColumn[attribute];
			var dataSourceAttribute = new JsonObject { ["path"] = columnPath };
			if (columnPath.Contains('.')) {
				dataSourceAttribute["type"] = "ForwardReference";
			}
			dataSourceAttributes[columnPath.Replace('.', '_')] = dataSourceAttribute;
		}
		viewModelAttributes[AttributeName("Id")] = new JsonObject {
			["modelConfig"] = new JsonObject { ["path"] = ModelPath("Id") }
		};

		var viewModelConfigDiff = new JsonArray {
			new JsonObject {
				["operation"] = "merge",
				["path"] = new JsonArray("attributes", ItemsAttribute, "viewModelConfig", "attributes"),
				["values"] = viewModelAttributes
			}
		};
		var modelConfigDiff = new JsonArray {
			new JsonObject {
				["operation"] = "merge",
				["path"] = new JsonArray("dataSources", PrimaryDataSourceAlias, "config"),
				["values"] = new JsonObject {
					["attributes"] = dataSourceAttributes,
					["entitySchemaName"] = settings.EntitySchemaName
				}
			}
		};

		// Recorded divergences from the mobile runtime's own converter (see the knowledge record
		// legacy-list-conversion-divergences-from-the-mobile-runtime): the target is the designer's vocabulary.
		notes.Add("Subtitle columns land in ListItem.body (together with group columns), not in ListItem.subtitles — this is what the Mobile Freedom UI designer itself generates for a list page; the runtime converter's separate subtitle slot is not reproduced.");
		notes.Add("Search: BaseMobileListTemplate opens search through crt.OpenSearchListRequest over $Items; this vocabulary has no per-page search-column list, the runtime searches the bound Items attributes (the converted columns), so no searchFilter columns are emitted.");
		List<LegacyPropertyCoverageInfo> coverage = BuildCoverage(settings, decisions);
		if (!string.IsNullOrWhiteSpace(settings.GridType)) {
			notes.Add($"Classic grid layout settings (gridType '{settings.GridType}', rows, columns) describe the classic list only and have no mobile counterpart; the mobile ListItem row has one layout.");
		}
		foreach (KeyValuePair<string, Newtonsoft.Json.Linq.JToken> extra in settings.OtherSettingsProperties) {
			decisions.Add($"Settings property '{extra.Key}' has no counterpart on the mobile list page and was dropped.");
		}

		IReadOnlyList<string> dropped = coverage.Where(c => c.Status == "dropped").Select(c => c.Property).ToList();
		var legacySource = new LegacyMobileSourceInfo {
			SettingsType = settings.SettingsType,
			EntitySchemaName = settings.EntitySchemaName,
			Workplace = nameParts?.Workplace,
			Classification = classification.Label,
			OverrideSections = classification.OverrideSections.Count > 0
				? classification.OverrideSections.Select(s => new LegacyOverrideSectionInfo {
					Section = s.Section, OperationCount = s.OperationCount, Ticket = s.Ticket
				}).ToList()
				: null,
			Layers = read.Layers.Select(l => new LegacyMobileSettingsLayerInfo {
				SchemaName = l.SchemaName, PackageName = l.PackageName, OperationCount = l.OperationCount
			}).ToList(),
			TitleColumn = titleMapping,
			BodyColumns = bodyMappings,
			ColumnPropertyCoverage = coverage,
			Decisions = decisions,
			Notes = notes.Count > 0 ? notes : null
		};

		string elementReason = titleMapping is null
			? $"Legacy wizard list settings for '{settings.EntitySchemaName}': {bodyMappings.Count} body row(s) from subtitleItems/groupItems merge onto the template's ListItem; no title column was defined."
			: $"Legacy wizard list settings for '{settings.EntitySchemaName}': the items column '{titleMapping.ColumnName}' becomes ListItem.title and {bodyMappings.Count} subtitle/group column(s) become ListItem.body rows, in wizard row order — merged onto the template-provided ListItem (do not insert a duplicate).";

		return new MobilePageConversionGuide {
			SourcePage = sourcePage,
			SourceType = SourceTypeLegacyGridPage,
			SourceTemplate = null,
			SourceStructure = [],
			DataSources = [PrimaryDataSourceAlias],
			ModelConfigDiff = modelConfigDiff,
			ViewModelConfigDiff = viewModelConfigDiff,
			RecommendedMobileTemplate = RecommendedTemplate,
			TemplateNote = "Shipped mobile list template: Scaffold + header (search, sort, folder tree, QuickFilterGroup) + ListContainer with crt.List bound to $Items and a ListItem row. The converter MERGES onto the template's ListItem — it never re-declares any of these elements.",
			ContainerMap = [],
			ComponentSuggestions = [],
			ElementMap = [
				// Same two merges the Mobile Freedom UI designer writes for a generated list page, in its order:
				// the folder tree bound to the entity (folder filtering keeps working), then the row.
				new ElementMapEntry {
					WebName = LegacyGridPageSettingsParser.SettingsNodeName,
					WebType = "GridPageSettings",
					Operation = "merge",
					MobileName = FolderTreeActionsName,
					MobileType = FolderTreeActionsType,
					MobileValues = new JsonObject {
						["sourceSchemaName"] = FolderTreeSourceSchemaName,
						["rootSchemaName"] = settings.EntitySchemaName
					},
					Reason = $"Folder filtering: the template-provided FolderTreeActions is bound to entity '{settings.EntitySchemaName}' (rootSchemaName) exactly as the Mobile designer does for a generated list page — merge by name, do not insert a duplicate."
				},
				new ElementMapEntry {
					WebName = LegacyGridPageSettingsParser.SettingsNodeName,
					WebType = "GridPageSettings",
					Operation = "merge",
					MobileName = ListItemName,
					MobileType = ListItemType,
					MobileValues = mobileValues,
					Reason = elementReason
				}
			],
			MobileContracts = [],
			SectionRegistration = sectionRegistration,
			LegacySource = legacySource,
			Constraints = BuildConstraints(sourcePage, classification, dropped, titleMapping is null, notes),
			NextSteps = BuildNextSteps(suggestedTarget, settings.EntitySchemaName),
			GuidanceArticle = GuidanceArticleName,
			SuggestedTargetSchemaName = suggestedTarget
		};
	}

	private static LegacyColumnMappingInfo Mapping(LegacyGridColumn column, string bucket, string attribute, string target) =>
		new() {
			Bucket = bucket,
			Row = column.Row,
			ColumnName = column.ColumnName,
			Caption = column.Caption,
			DataValueType = column.DataValueType,
			Attribute = attribute,
			ModelPath = ModelPath(column.ColumnName),
			Target = target
		};

	private static void Register(List<string> order, Dictionary<string, string> byAttribute, string attribute, string columnPath, List<string> notes) {
		if (string.Equals(attribute, AttributeName("Id"), StringComparison.Ordinal)) {
			// PDS_Id is always declared LAST by the converter (the row's record key); a wizard column literally
			// named Id must not declare it a second time or move it.
			return;
		}
		if (byAttribute.ContainsKey(attribute)) {
			notes.Add($"Column '{columnPath}' appears in more than one wizard bucket; its attribute '{attribute}' is declared once.");
			return;
		}
		byAttribute[attribute] = columnPath;
		order.Add(attribute);
	}

	/// <summary>
	/// Coverage table over every wizard column property: <c>row</c> and <c>columnName</c> transfer (order and
	/// binding), <c>content</c> and <c>dataValueType</c> are informational (a mobile list row shows the bound
	/// value; its caption and type come from the entity), and anything else the wizard recorded (view types such as
	/// phone/email/url/map/preview, formats) has no counterpart on a template ListItem row and is dropped — each
	/// such property adds a decision for the user.
	/// </summary>
	private static List<LegacyPropertyCoverageInfo> BuildCoverage(LegacyGridPageSettings settings, List<string> decisions) {
		List<LegacyGridColumn> all = settings.Items.Concat(settings.SubtitleItems).Concat(settings.GroupItems).ToList();
		List<string> allNames = all.Select(c => c.ColumnName).ToList();
		var coverage = new List<LegacyPropertyCoverageInfo> {
			new() { Property = "columnName", Status = "transferred", Note = "Bound as $PDS_<column> on the ListItem row; a dotted path becomes a ForwardReference attribute.", Columns = allNames },
			new() { Property = "row", Status = "transferred", Note = "Wizard row order is kept: title first, then subtitle rows, then group rows.", Columns = allNames },
			new() { Property = "content", Status = "informational", Note = "Column caption. A mobile list row shows values only; captions are not rendered on ListItem rows.", Columns = all.Where(c => !string.IsNullOrWhiteSpace(c.Caption)).Select(c => c.ColumnName).ToList() },
			new() { Property = "dataValueType", Status = "informational", Note = "The mobile runtime formats the value from the entity column type; lookups display their primary display value.", Columns = all.Where(c => c.DataValueType is not null).Select(c => c.ColumnName).ToList() }
		};
		var extras = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
		foreach (LegacyGridColumn column in all) {
			foreach (string key in column.OtherProperties.Keys) {
				if (!extras.TryGetValue(key, out List<string> columns)) {
					columns = [];
					extras[key] = columns;
				}
				columns.Add(column.ColumnName);
			}
		}
		foreach (KeyValuePair<string, List<string>> extra in extras) {
			coverage.Add(new LegacyPropertyCoverageInfo {
				Property = extra.Key,
				Status = "dropped",
				Note = "A BaseMobileListTemplate ListItem body row carries only a value binding; this wizard column property has no counterpart and was not transferred.",
				Columns = extra.Value
			});
			decisions.Add($"Column property '{extra.Key}' (on {string.Join(", ", extra.Value)}) is not supported on a mobile ListItem row and was dropped — confirm with the user or configure an alternative manually.");
		}
		return coverage;
	}

	private static List<string> BuildConstraints(
		string sourcePage, LegacySettingsClassification classification, IReadOnlyList<string> droppedProperties,
		bool titleMissing, IReadOnlyList<string> notes) {
		var constraints = new List<string> {
			"Mobile body is plain JSON with only viewConfigDiff / viewModelConfigDiff / modelConfigDiff — no AMD, no markers, no define() wrapper.",
			"The mobile template (BaseMobileListTemplate) already provides the Scaffold root, the header (search, sort, folder tree, QuickFilterGroup), the crt.List bound to $Items and its ListItem row — do NOT add a second Scaffold, List, ListItem or QuickFilterGroup. The page only MERGES onto the template's ListItem.",
			"elementMap carries exactly TWO operations, both merges onto template-provided elements, in order: 'FolderTreeActions' (sourceSchemaName + rootSchemaName, so folder filtering resolves the entity) and 'ListItem' (title / body / icon). Emit each VERBATIM as { \"operation\": \"merge\", \"name\": <mobileName>, \"values\": <mobileValues> } — do not rename attributes (PDS_<Column>), reorder or drop body rows, add properties, or move subtitle columns into a 'subtitles' slot.",
			"Paste the provided viewModelConfigDiff and modelConfigDiff VERBATIM as the page's viewModelConfigDiff / modelConfigDiff. Keep PDS_Id and every attribute path exactly as provided (a dotted column carries type ForwardReference); do NOT collapse them into a root merge, hand-build the data-source section, or copy it from another mobile body.",
			"Source is a legacy Mobile-wizard LIST settings schema (settingsType GridPage). Only the wizard buckets were converted: items -> ListItem.title, subtitleItems then groupItems (row order) -> ListItem.body. Read guide.legacySource for the column mapping, the contributing package layers and the columnPropertyCoverage table, and present them at the plan gate."
		};
		if (classification.Kind == LegacySettingsKind.FreedomUiOverrides) {
			string sections = string.Join(", ", classification.OverrideSections.Select(s => s.Section));
			constraints.Add($"The settings schema ALSO carries Freedom UI override sections ({sections}) — they were RECOGNISED but NOT converted ({LegacyMobileSettingsClassifier.OverridesTicket}). Tell the user; do not merge them by hand.");
		}
		if (droppedProperties.Count > 0) {
			constraints.Add($"Dropped column properties need a user decision before you build: {string.Join(", ", droppedProperties)}. Present them at the plan gate (Gate M); never invent a mobile equivalent.");
		}
		if (titleMissing) {
			constraints.Add("No title column was found in the wizard settings: ask the user which column becomes ListItem.title, then add \"title\": \"$PDS_<Column>\" to the ListItem merge and the matching PDS_<Column> attribute to both diffs.");
		}
		foreach (string note in notes.Where(n => n.Contains("NOT part of the resolved hierarchy", StringComparison.Ordinal))) {
			constraints.Add(note);
		}
		constraints.Add($"The classic settings schema '{sourcePage}' is left untouched — nothing is written to it — and re-running this tool yields the same guide (idempotent).");
		return constraints;
	}

	private static List<string> BuildNextSteps(string suggestedTarget, string entitySchemaName) => [
		$"Read get-guidance with name \"{GuidanceArticleName}\".",
		"Present the plan from guide.legacySource: which page will be created, the title column and body rows that transfer, what was adapted, the dropped column properties and why, the open decisions, and the packages that contributed. Wait for explicit approval (Gate M) — nothing is written before it.",
		$"Create the target mobile page with create-page: schema-name={suggestedTarget}, template={RecommendedTemplate}, entity-schema-name={entitySchemaName}, in the user's target package.",
		"Build the body: viewConfigDiff = [ the elementMap merges in order — 'FolderTreeActions' then 'ListItem' — each with its mobileValues verbatim ]; viewModelConfigDiff and modelConfigDiff = the provided diffs pasted verbatim.",
		"Validate the body with validate-page; resolve findings only by asking the user — never by silently editing the pasted values.",
		"Persist with update-page (mode replace), then read the page back with get-page and confirm ListItem.title / body and the PDS_* attributes match guide.legacySource before reporting success.",
		"Register the section for mobile per guide.sectionRegistration after approval (Gate S), then open the result in Freedom UI Mobile Designer for final review."
	];
}
