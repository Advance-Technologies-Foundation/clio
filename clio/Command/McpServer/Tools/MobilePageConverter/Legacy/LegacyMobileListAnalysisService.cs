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

	/// <summary>Default fallback when the environment's SchemaNamePrefix cannot be read.</summary>
	public const string DefaultSchemaNamePrefix = "Usr";

	/// <summary>
	/// Bundled fallback used when the rules file carries no <c>mobileLegacyTemplates.gridPage</c> group. The rules
	/// file is CDN-fetchable, so the file most likely to lack the group is an OLD one; degrading to the bundled
	/// defaults keeps the legacy branch behaving exactly as it did before the group existed.
	/// </summary>
	public static readonly MobileLegacyTemplateRule DefaultGridPageTemplate = new();

	/// <summary>
	/// Resolves the shipped mobile list template and the names of the template-provided elements a converted
	/// legacy list page merges onto, from the conversion rules; never null.
	/// </summary>
	/// <param name="rules">The loaded conversion rules, or null when they could not be fetched.</param>
	/// <returns>The grid-page template configuration, or <see cref="DefaultGridPageTemplate"/>.</returns>
	public static MobileLegacyTemplateRule ResolveGridPageTemplate(WebToMobilePageConversionRules rules) =>
		rules?.MobileLegacyTemplates?.GridPage ?? DefaultGridPageTemplate;

	private const string GuidanceArticleName = "freedom-page-web-to-mobile-conversion";
	private const string PrimaryDataSourceAlias = "PDS";
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
	/// <param name="template">
	/// Target template configuration from the conversion rules (<see cref="ResolveGridPageTemplate"/>); null
	/// degrades to <see cref="DefaultGridPageTemplate"/>.
	/// </param>
	/// <param name="columnCaptions">
	/// Entity column name → caption, read best-effort from the object. Used only where an override references a
	/// column the wizard never placed, so a carried sort option can show a real label instead of a machine name.
	/// </param>
	/// <param name="runtimeNames">
	/// The runtime-name table used to re-point embedded overrides (ENG-95733); null switches the override pass off
	/// and every embedded operation is reported instead.
	/// </param>
	/// <returns>The advisory guide.</returns>
	public static MobilePageConversionGuide Analyze(
		LegacyMobileSettingsReadResult read,
		LegacySettingsClassification classification,
		string sourcePage,
		string suggestedTarget,
		SectionRegistrationInfo sectionRegistration,
		MobileLegacyTemplateRule template,
		MobileLegacyRuntimeNameSet runtimeNames = null,
		IReadOnlyDictionary<string, string> columnCaptions = null) {
		ArgumentNullException.ThrowIfNull(read);
		ArgumentNullException.ThrowIfNull(classification);
		if (!read.Success || read.EffectiveSettings is null) {
			throw new InvalidOperationException("Analyze requires a successful legacy settings read.");
		}
		template ??= DefaultGridPageTemplate;
		LegacyGridPageSettings parsed = LegacyGridPageSettingsParser.Parse(read.EffectiveSettings);

		// Embedded overrides are re-pointed BEFORE conversion, so anything expressible in the wizard's own language
		// flows through the single emit path below and the page stays a pure function of one model.
		LegacyOverrideRebaseResult rebase = LegacyOverrideRebaser.Rebase(parsed, classification.OverrideSections,
			LegacyRuntimeNameOracle.Build(parsed, runtimeNames), template, runtimeNames, columnCaptions);
		LegacyGridPageSettings settings = rebase.Settings;
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
			bodyMappings.Add(Mapping(column, bucket, attribute, $"{template.ListItemName}.body[{bodyIndex}]"));
			Register(attributeOrder, attributeColumn, attribute, column.ColumnName, notes);
			bodyIndex++;
		}
		LegacyColumnMappingInfo titleMapping = null;
		if (titleColumn is not null) {
			string attribute = AttributeName(titleColumn.ColumnName);
			titleMapping = Mapping(titleColumn, BucketTitle, attribute, $"{template.ListItemName}.title");
			Register(attributeOrder, attributeColumn, attribute, titleColumn.ColumnName, notes);
		}
		// A carried override can bind a column the wizard never placed (a row icon, for example). Its attribute is
		// declared exactly like a wizard column — before PDS_Id, which stays last — or the binding would be dead.
		foreach (string column in rebase.RequiredColumns) {
			Register(attributeOrder, attributeColumn, AttributeName(column), column, notes);
		}

		// The template's ListItem merge (key order mirrors the designer's own output: body, title, icon).
		var mobileValues = new JsonObject { ["body"] = bodyRows };
		if (titleMapping is not null) {
			mobileValues["title"] = $"${titleMapping.Attribute}";
		}
		mobileValues["icon"] = null;
		// The override wins on every key it names: it is the later, more specific customisation of the same row.
		if (rebase.ElementValueOverrides.TryGetValue(template.ListItemName, out JsonObject rowOverrides)) {
			foreach (KeyValuePair<string, JsonNode> pair in rowOverrides) {
				mobileValues[pair.Key] = pair.Value?.DeepClone();
			}
		}

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
				["path"] = new JsonArray("attributes", template.ItemsAttributeName, "viewModelConfig", "attributes"),
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
		// Rebased overrides land AFTER the converted sections, so an override refines what the wizard produced
		// rather than being overwritten by it.
		foreach (JsonObject operation in rebase.ViewModelConfigOperations) {
			viewModelConfigDiff.Add(operation.DeepClone());
		}
		foreach (JsonObject operation in rebase.ModelConfigOperations) {
			modelConfigDiff.Add(operation.DeepClone());
		}

		// Recorded divergences from the mobile runtime's own converter (see the knowledge record
		// legacy-list-conversion-divergences-from-the-mobile-runtime): the target is the designer's vocabulary.
		notes.Add("Subtitle columns land in ListItem.body (together with group columns), not in ListItem.subtitles — this is what the Mobile Freedom UI designer itself generates for a list page; the runtime converter's separate subtitle slot is not reproduced.");
		notes.Add($"Search: {template.TemplateName} opens search through crt.OpenSearchListRequest over ${template.ItemsAttributeName}; this vocabulary has no per-page search-column list, the runtime searches the bound {template.ItemsAttributeName} attributes (the converted columns), so no searchFilter columns are emitted.");
		List<LegacyPropertyCoverageInfo> coverage = BuildCoverage(settings, decisions, template);
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
					Section = s.Section, OperationCount = s.OperationCount, Ticket = s.Ticket,
					Supported = s.Supported, Reason = s.Reason
				}).ToList()
				: null,
			Layers = read.Layers.Select(l => new LegacyMobileSettingsLayerInfo {
				SchemaName = l.SchemaName, PackageName = l.PackageName, OperationCount = l.OperationCount
			}).ToList(),
			TitleColumn = titleMapping,
			BodyColumns = bodyMappings,
			ColumnPropertyCoverage = coverage,
			OverrideOutcomes = rebase.Outcomes.Count > 0
				? rebase.Outcomes.Select(o => new LegacyOverrideOutcomeInfo {
					Section = o.Section, Index = o.Index, Operation = o.Operation, Target = o.Target,
					Lane = o.Lane, Effect = o.Effect, Reason = o.Reason
				}).ToList()
				: null,
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
			RecommendedMobileTemplate = template.TemplateName,
			TemplateNote = $"Shipped mobile list template '{template.TemplateName}': Scaffold + header (search, sort, folder tree, QuickFilterGroup) + '{template.ListContainerName}' with '{template.ListName}' bound to ${template.ItemsAttributeName} and a '{template.ListItemName}' row. The converter MERGES onto the template's {template.ListItemName} — it never re-declares any of these elements.",
			ContainerMap = [],
			ComponentSuggestions = [],
			ElementMap = BuildElementMap(settings, template, mobileValues, elementReason, rebase),
			MobileContracts = [],
			SectionRegistration = sectionRegistration,
			LegacySource = legacySource,
			Constraints = BuildConstraints(sourcePage, classification, dropped, titleMapping is null, notes, template, rebase),
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
	private static List<LegacyPropertyCoverageInfo> BuildCoverage(LegacyGridPageSettings settings, List<string> decisions, MobileLegacyTemplateRule template) {
		List<LegacyGridColumn> all = settings.Items.Concat(settings.SubtitleItems).Concat(settings.GroupItems).ToList();
		List<string> allNames = all.Select(c => c.ColumnName).ToList();
		var coverage = new List<LegacyPropertyCoverageInfo> {
			new() { Property = "columnName", Status = "transferred", Note = "Bound as $PDS_<column> on the ListItem row; a dotted path becomes a ForwardReference attribute.", Columns = allNames },
			new() { Property = "row", Status = "transferred", Note = "Wizard row order is kept: title first, then subtitle rows, then group rows.", Columns = allNames },
			new() { Property = "content", Status = "informational", Note = "Column caption. A converted list row shows the column's LABEL from the object, not this wizard caption, and the label cannot be switched off per column.", Columns = all.Where(c => !string.IsNullOrWhiteSpace(c.Caption)).Select(c => c.ColumnName).ToList() },
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
				Note = $"A {template.TemplateName} {template.ListItemName} body row carries only a value binding; this wizard column property has no counterpart and was not transferred.",
				Columns = extra.Value
			});
			decisions.Add($"Column property '{extra.Key}' (on {string.Join(", ", extra.Value)}) is not supported on a mobile {template.ListItemName} row and was dropped — confirm with the user or configure an alternative manually.");
		}
		return coverage;
	}

	/// <summary>
	/// The page's element map: the two merges the Mobile Freedom UI designer writes for a generated list page,
	/// followed by whatever an embedded override contributed that only the target dialect can express (ENG-95733).
	/// Built rather than literal, because the number of operations now depends on the source.
	/// </summary>
	private static List<ElementMapEntry> BuildElementMap(
		LegacyGridPageSettings settings, MobileLegacyTemplateRule template, JsonObject mobileValues,
		string elementReason, LegacyOverrideRebaseResult rebase) {
		var map = new List<ElementMapEntry> {
			// The folder tree bound to the entity (folder filtering keeps working), then the row.
			new() {
				WebName = LegacyGridPageSettingsParser.SettingsNodeName,
				WebType = "GridPageSettings",
				Operation = "merge",
				MobileName = template.FolderTreeActionsName,
				MobileType = template.FolderTreeActionsType,
				MobileValues = Fold(new JsonObject {
					["sourceSchemaName"] = template.FolderSourceSchemaName,
					["rootSchemaName"] = settings.EntitySchemaName
				}, rebase, template.FolderTreeActionsName),
				Reason = $"Folder filtering: the template-provided {template.FolderTreeActionsName} is bound to entity '{settings.EntitySchemaName}' (rootSchemaName) exactly as the Mobile designer does for a generated list page — merge by name, do not insert a duplicate."
			},
			new() {
				WebName = LegacyGridPageSettingsParser.SettingsNodeName,
				WebType = "GridPageSettings",
				Operation = "merge",
				MobileName = template.ListItemName,
				MobileType = template.ListItemType,
				MobileValues = mobileValues,
				Reason = elementReason
			}
		};
		// A template element the converter does not otherwise write, carrying override values, becomes its own merge.
		foreach (KeyValuePair<string, JsonObject> pair in rebase.ElementValueOverrides) {
			if (string.Equals(pair.Key, template.ListItemName, StringComparison.Ordinal)
				|| string.Equals(pair.Key, template.FolderTreeActionsName, StringComparison.Ordinal)) {
				continue;
			}
			LegacyOverrideOutcome source = rebase.Outcomes.FirstOrDefault(
				o => o.Effect is not null && o.Effect.Contains($"'{pair.Key}'", StringComparison.Ordinal));
			map.Add(new ElementMapEntry {
				WebName = source?.Target ?? LegacyGridPageSettingsParser.SettingsNodeName,
				WebType = "GridPageSettings",
				Operation = "merge",
				MobileName = pair.Key,
				MobileValues = pair.Value,
				Reason = source is null
					? $"Carried over from an embedded Freedom UI override in the source settings."
					: $"Embedded Freedom UI override ({source.Section}[{source.Index}] {source.Operation} '{source.Target}'): {source.Effect}"
			});
		}
		foreach (JsonObject operation in rebase.ViewConfigOperations) {
			string name = operation["name"]?.GetValue<string>();
			LegacyOverrideOutcome outcome = rebase.Outcomes.FirstOrDefault(
				o => o.Lane == LegacyOverrideLanes.TargetDelta && o.Effect is not null && o.Effect.Contains($"'{name}'", StringComparison.Ordinal));
			map.Add(new ElementMapEntry {
				WebName = outcome?.Target ?? LegacyGridPageSettingsParser.SettingsNodeName,
				WebType = "GridPageSettings",
				Operation = operation["operation"]?.GetValue<string>() ?? "remove",
				MobileName = name,
				MobileValues = operation["values"]?.DeepClone() as JsonObject,
				Reason = outcome?.Effect is null
					? $"Carried over from an embedded Freedom UI override in the source settings."
					: $"Embedded Freedom UI override ({outcome.Section}[{outcome.Index}] {outcome.Operation} '{outcome.Target}'): {outcome.Effect}"
			});
		}
		return map;
	}

	/// <summary>Folds an element's override values over the converter's own; the override wins on every key it names.</summary>
	private static JsonObject Fold(JsonObject own, LegacyOverrideRebaseResult rebase, string elementName) {
		if (!rebase.ElementValueOverrides.TryGetValue(elementName, out JsonObject overrides)) {
			return own;
		}
		foreach (KeyValuePair<string, JsonNode> pair in overrides) {
			own[pair.Key] = pair.Value?.DeepClone();
		}
		return own;
	}

	private static List<string> BuildConstraints(
		string sourcePage, LegacySettingsClassification classification, IReadOnlyList<string> droppedProperties,
		bool titleMissing, IReadOnlyList<string> notes, MobileLegacyTemplateRule template,
		LegacyOverrideRebaseResult rebase) {
		var constraints = new List<string> {
			"Mobile body is plain JSON with only viewConfigDiff / viewModelConfigDiff / modelConfigDiff — no AMD, no markers, no define() wrapper.",
			$"The mobile template ({template.TemplateName}) already provides the Scaffold root, the header (search, sort, folder tree, QuickFilterGroup), the '{template.ListName}' bound to ${template.ItemsAttributeName} inside '{template.ListContainerName}', and its '{template.ListItemName}' row — do NOT add a second Scaffold, {template.ListName}, {template.ListItemName} or QuickFilterGroup. The page only MERGES onto the template's {template.ListItemName}.",
			$"elementMap starts with TWO merges onto template-provided elements, in order: '{template.FolderTreeActionsName}' (sourceSchemaName + rootSchemaName, so folder filtering resolves the entity) and '{template.ListItemName}' (title / body / icon). Any FURTHER entry comes from an embedded Freedom UI override and carries its own operation. Emit every entry VERBATIM as {{ \"operation\": <operation>, \"name\": <mobileName>, \"values\": <mobileValues> }} in the given order, omitting \"values\" when the entry has none — do not rename attributes (PDS_<Column>), reorder or drop body rows, add properties, or move subtitle columns into a 'subtitles' slot.",
			"Paste the provided viewModelConfigDiff and modelConfigDiff VERBATIM as the page's viewModelConfigDiff / modelConfigDiff. Keep PDS_Id and every attribute path exactly as provided (a dotted column carries type ForwardReference); do NOT collapse them into a root merge, hand-build the data-source section, or copy it from another mobile body.",
			"Source is a legacy Mobile-wizard LIST settings schema (settingsType GridPage). Only the wizard buckets were converted: items -> ListItem.title, subtitleItems then groupItems (row order) -> ListItem.body. Read guide.legacySource for the column mapping, the contributing package layers and the columnPropertyCoverage table, and present them at the plan gate."
		};
		if (classification.Kind == LegacySettingsKind.FreedomUiOverrides) {
			// Two different verdicts, kept apart on purpose: a section this converter processes operation by
			// operation, versus one it will never process. Collapsing them would tell the user to wait for
			// something that is not coming.
			List<LegacyOverrideSection> processed = classification.OverrideSections.Where(s => s.Supported).ToList();
			List<LegacyOverrideSection> refused = classification.OverrideSections.Where(s => !s.Supported).ToList();
			if (processed.Count > 0) {
				int carried = rebase.Outcomes.Count(o => o.Lane != LegacyOverrideLanes.Reported);
				int reported = rebase.Outcomes.Count - carried;
				constraints.Add($"The settings schema ALSO carries Freedom UI override sections ({string.Join(", ", processed.Select(s => s.Section))}). Each operation was re-pointed individually: {carried} carried over, {reported} could not be. Read guide.legacySource.overrideOutcomes and present EVERY reported one at the plan gate — each names its source operation and the reason. Never carry a reported operation by hand — each was held back for the reason it states, and a partial re-application is worse than none.");
			}
			foreach (LegacyOverrideSection section in refused) {
				constraints.Add($"Override section '{section.Section}' ({section.OperationCount} operation(s)) is NOT supported by this converter and was not carried over. {section.Reason} Tell the user; never translate it by hand into the converted page.");
			}
		}
		if (droppedProperties.Count > 0) {
			constraints.Add($"Dropped column properties need a user decision before you build: {string.Join(", ", droppedProperties)}. Present them at the plan gate (Gate M); never invent a mobile equivalent.");
		}
		if (titleMissing) {
			constraints.Add("No title column was found in the wizard settings: ask the user which column becomes ListItem.title, then add \"title\": \"$PDS_<Column>\" to the ListItem merge and the matching PDS_<Column> attribute to both diffs.");
		}
		// Warnings about embedded overrides live HERE and nowhere else: constraints is the block the caller cannot
		// skip, and an override whose outcome differs from what it asked for must not be discoverable only by
		// reading a report section.
		constraints.AddRange(rebase.Warnings);
		foreach (string note in notes.Where(n => n.Contains("NOT part of the resolved hierarchy", StringComparison.Ordinal))) {
			constraints.Add(note);
		}
		constraints.Add($"The classic settings schema '{sourcePage}' is left untouched — nothing is written to it — and re-running this tool yields the same guide (idempotent).");
		return constraints;
	}

}
