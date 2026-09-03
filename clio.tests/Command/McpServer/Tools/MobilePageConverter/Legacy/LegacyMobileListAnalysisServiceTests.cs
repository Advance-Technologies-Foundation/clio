using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Command;
using Clio.Command.McpServer.Tools.MobilePageConverter;
using Clio.Command.McpServer.Tools.MobilePageConverter.Legacy;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer.Tools.MobilePageConverter.Legacy;

/// <summary>
/// Unit tests for the pure legacy Mobile-wizard LIST settings analysis (ENG-95730): the merged settings of a
/// <c>Mobile&lt;Entity&gt;GridPageSettings&lt;Workplace&gt;</c> schema become ONE ListItem merge plus the two data-section
/// diffs, in the same guide contract the Freedom UI web analysis returns.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class LegacyMobileListAnalysisServiceTests {

	private const string SourceSchema = "MobileOrderGridPageSettingsDefaultWorkplace";
	private const string Target = "UsrOrder_MobileListPage";

	private static string FixturePath(string name) =>
		Path.Combine(TestContext.CurrentContext.TestDirectory, "Command", "McpServer", "Fixtures", "LegacyMobile", name);

	/// <summary>Merges one or more legacy diff arrays exactly like the reader does and wraps them as a successful read.</summary>
	private static LegacyMobileSettingsReadResult Read(params string[] layerBodies) {
		List<(JArray Operations, int SchemaVersion)> layers = layerBodies
			.Select(body => (JArray.Parse(LegacyMobileSettingsReader.Unescape(body)), 1))
			.ToList();
		JArray merged = LegacyMobileSettingsReader.Merge(layers, () => new JsonDiffApplier());
		JObject settings = merged.OfType<JObject>().Single(o => o.Value<string>("name") == "settings");
		List<LegacyMobileSettingsLayer> layerInfos = layers
			.Select((l, i) => new LegacyMobileSettingsLayer($"uid-{i}", SourceSchema, $"pkg-{i}", $"Package{i}", 1, l.Operations.Count))
			.ToList();
		return new LegacyMobileSettingsReadResult(true, null, SourceSchema, "uid-0", 0, layerInfos, settings,
			LegacyBodyShape.OperationArray, []);
	}

	/// <summary>The bundled target-template configuration the analyzer falls back to when the rules file has none.</summary>
	private static readonly MobileLegacyTemplateRule Template = LegacyMobileListAnalysisService.DefaultGridPageTemplate;

	/// <summary>The shipped runtime-name table, so the tests exercise the same data the tool loads.</summary>
	private static readonly MobileLegacyRuntimeNameSet RuntimeNames =
		WebToMobilePageConversionRulesCatalog.LoadBundled().MobileLegacyRuntimeNames?.GridPage;

	private static MobilePageConversionGuide Analyze(LegacyMobileSettingsReadResult read, SectionRegistrationInfo section = null,
		MobileLegacyTemplateRule template = null) =>
		LegacyMobileListAnalysisService.Analyze(
			read, LegacyMobileSettingsClassifier.Classify(read.EffectiveSettings), SourceSchema, Target, section,
			template ?? Template, RuntimeNames);

	private static string Settings(string entity, string items, string subtitles, string groups, string extraSettings = "") => $$"""
		[
		  { "operation": "insert", "name": "settings", "values": { "entitySchemaName": "{{entity}}", "items": [], "subtitleItems": [], "groupItems": [], "settingsType": "GridPage", "operation": "insert", "localizableStrings": {} {{extraSettings}} } }
		  {{items}}{{subtitles}}{{groups}}
		]
		""";

	private static string Column(string bucket, int row, string columnName, string caption, int dvt = 1, string extra = "") =>
		$$""", { "operation": "insert", "name": "{{Guid.NewGuid()}}", "values": { "row": {{row}}, "content": "{{caption}}", "columnName": "{{columnName}}", "dataValueType": {{dvt}}, "operation": "insert" {{extra}} }, "parentName": "settings", "propertyName": "{{bucket}}", "index": {{row}} }""";

	private static string Json(JsonNode node) => node.ToJsonString(new JsonSerializerOptions { WriteIndented = false });

	/// <summary>The ListItem merge of a legacy guide (the second of its two elementMap operations).</summary>
	private static ElementMapEntry ListItem(MobilePageConversionGuide guide) =>
		guide.ElementMap.Single(e => e.MobileName == Template.ListItemName);

	[Test]
	[Description("GOLDEN: the sample Order wizard settings produce exactly the expected ListItem merge values, viewModelConfigDiff and modelConfigDiff (title from items, body from subtitleItems then groupItems, PDS_Id appended, entitySchemaName Order).")]
	public void Analyze_ShouldMatchExpectedFixture_WhenGivenSampleOrderSettings() {
		// Arrange
		string source = File.ReadAllText(FixturePath("MobileOrderGridPageSettingsDefaultWorkplace.json"));
		JsonObject expected = JsonNode.Parse(File.ReadAllText(FixturePath("OrderMobileListPage.expected.json")))!.AsObject();

		// Act
		MobilePageConversionGuide guide = Analyze(Read(source));

		// Assert
		guide.ElementMap.Should().HaveCount(2, because: "a legacy list page needs exactly the two merges the designer writes: FolderTreeActions and ListItem");
		ElementMapEntry folder = guide.ElementMap[0];
		folder.Operation.Should().Be("merge", because: "FolderTreeActions is template-provided and is never re-inserted");
		folder.MobileName.Should().Be("FolderTreeActions", because: "folder filtering is bound on the template's element, first — as the designer orders it");
		Json(folder.MobileValues).Should().Be("""{"sourceSchemaName":"FolderTree","rootSchemaName":"Order"}""",
			because: "the designer-generated list page binds the folder tree with exactly these two values, rootSchemaName being the entity");
		ElementMapEntry merge = guide.ElementMap[1];
		merge.Operation.Should().Be("merge", because: "the ListItem is template-provided and is never re-inserted");
		merge.MobileName.Should().Be("ListItem", because: "the merge targets the BaseMobileListTemplate row element by name");
		merge.MobileType.Should().Be("crt.ListItem", because: "the caller learns the target type without a registry lookup");
		Json(merge.MobileValues).Should().Be(Json(expected["listItemValues"]),
			because: "the ListItem values must be byte-identical to the designer's own output: body rows in wizard order, title, icon null");
		Json(guide.ViewModelConfigDiff).Should().Be(Json(expected["viewModelConfigDiff"]),
			because: "the collection attributes are declared as one targeted merge under Items.viewModelConfig.attributes, PDS_Id last");
		Json(guide.ModelConfigDiff).Should().Be(Json(expected["modelConfigDiff"]),
			because: "the data source is bound to Order with one attribute per column as a targeted merge on dataSources.PDS.config");
	}

	[Test]
	[Description("The legacy guide is LEAN and uses the shared contract: sourceType legacy-mobile-grid-page, BaseMobileListTemplate recommended, PDS data source, no component suggestions / contracts / container map, no web-only sections, and legacySource populated.")]
	public void Analyze_ShouldFillSharedContractLeanly_WhenGivenSampleOrderSettings() {
		// Arrange
		string source = File.ReadAllText(FixturePath("MobileOrderGridPageSettingsDefaultWorkplace.json"));

		// Act
		MobilePageConversionGuide guide = Analyze(Read(source));

		// Assert
		guide.SourceType.Should().Be(LegacyMobileListAnalysisService.SourceTypeLegacyGridPage, because: "the guide names the detected source type");
		guide.SourcePage.Should().Be(SourceSchema, because: "the guide names the source schema");
		guide.RecommendedMobileTemplate.Should().Be("BaseMobileListTemplate", because: "every converted legacy list page inherits the shipped list template");
		guide.SuggestedTargetSchemaName.Should().Be(Target, because: "the suggested target is passed through");
		guide.DataSources.Should().Equal(new[] { "PDS" }, because: "the template's primary data source is the only one");
		guide.SourceTemplate.Should().BeNull(because: "a legacy settings schema has no web template");
		guide.SourceStructure.Should().BeEmpty(because: "the column facts live in legacySource, not in a web-style structure");
		guide.ComponentSuggestions.Should().BeEmpty(because: "the target elements are known up front and all supported — nothing to choose");
		guide.MobileContracts.Should().BeEmpty(because: "no registry lookup is needed for the template's own ListItem");
		guide.ContainerMap.Should().BeEmpty(because: "there are no web containers to map");
		guide.ModelConfig.Should().BeNull(because: "only the diffs are shipped for a legacy source");
		guide.ViewModelConfig.Should().BeNull(because: "only the diffs are shipped for a legacy source");
		guide.WebOnlySections.Should().BeNull(because: "a legacy settings body has no handlers/converters/validators");
		guide.PageBusinessRules.Should().BeNull(because: "no page-level business rule probe runs for a legacy source");
		guide.GuidanceArticle.Should().Be("freedom-page-web-to-mobile-conversion", because: "the same guidance article governs both source types");
		guide.LegacySource.Should().NotBeNull(because: "the legacy facts are the per-element report for this source type");
		guide.LegacySource.EntitySchemaName.Should().Be("Order", because: "the entity comes from settings.entitySchemaName");
		guide.LegacySource.SettingsType.Should().Be("GridPage", because: "the wizard list settings type is echoed");
		guide.LegacySource.Workplace.Should().Be("DefaultWorkplace", because: "the workplace suffix is parsed from the schema name");
		guide.LegacySource.Classification.Should().Be("plain", because: "the sample carries no Freedom UI overrides");
		guide.LegacySource.TitleColumn.ColumnName.Should().Be("Number", because: "the single items column is the title");
		guide.LegacySource.TitleColumn.Target.Should().Be("ListItem.title", because: "the title column's target is named for the report");
		guide.LegacySource.BodyColumns.Select(c => c.ColumnName).Should().Equal(new[] { "Account", "PaymentAmount" }, because: "subtitle columns come first, group columns second, each in row order");
		guide.LegacySource.BodyColumns.Select(c => c.Bucket).Should().Equal(new[] { "subtitle", "group" }, because: "each body row records the wizard bucket it came from");
		guide.LegacySource.Layers.Should().ContainSingle(l => l.OperationCount == 4, because: "one package layer with four operations contributed");
		guide.LegacySource.Decisions.Should().BeEmpty(because: "the sample needs no user decision");
		guide.Constraints.Should().Contain(c => c.Contains("starts with TWO merges"), because: "a source with no overrides yields exactly the two designer merges");
		guide.Constraints.Should().Contain(c => c.Contains("left untouched"), because: "idempotence and non-mutation of the classic schema are promised");
		guide.NextSteps.Should().BeEmpty(
			because: "the legacy guide states rules in constraints; the conversion FLOW is the skill's, not the guide's");
		guide.Constraints.Should().Contain(c => c.Contains("BaseMobileListTemplate"),
			because: "the next steps carry the exact create-page invocation");
		guide.Constraints.Should().Contain(c => c.Contains("left untouched") && c.Contains("idempotent"),
			because: "the guide states what it does NOT do to the source; the approval gate itself belongs to the skill");
	}

	[Test]
	[Description("A dotted column path binds as PDS_<A>_<B> with model path PDS.<A>_<B> and its data-source attribute carries the dotted path plus type ForwardReference; a lookup column binds directly.")]
	public void Analyze_ShouldEmitForwardReference_WhenColumnPathIsDotted() {
		// Arrange
		string source = Settings("Order",
			Column("items", 0, "Number", "Number"),
			Column("subtitleItems", 0, "Account.Type", "Account type", 10),
			Column("groupItems", 0, "Owner", "Owner", 10));

		// Act
		MobilePageConversionGuide guide = Analyze(Read(source));

		// Assert
		JsonObject values = ListItem(guide).MobileValues.AsObject();
		values["body"]!.AsArray().Select(r => r!["value"]!.GetValue<string>()).Should().Equal(new[] { "$PDS_Account_Type", "$PDS_Owner" }, because: "the dot becomes an underscore in the attribute name and a lookup binds directly");
		JsonObject vmAttributes = guide.ViewModelConfigDiff!.AsArray()[0]!["values"]!.AsObject();
		vmAttributes["PDS_Account_Type"]!["modelConfig"]!["path"]!.GetValue<string>().Should().Be("PDS.Account_Type",
			because: "the collection attribute path uses the underscored attribute name under the PDS alias");
		JsonObject dsAttributes = guide.ModelConfigDiff!.AsArray()[0]!["values"]!["attributes"]!.AsObject();
		dsAttributes["Account_Type"]!["path"]!.GetValue<string>().Should().Be("Account.Type", because: "the data source attribute keeps the real dotted entity path");
		dsAttributes["Account_Type"]!["type"]!.GetValue<string>().Should().Be("ForwardReference", because: "a related-column path must be declared as a ForwardReference for the mobile designer");
		dsAttributes["Owner"]!.AsObject().ContainsKey("type").Should().BeFalse(because: "a direct column carries no ForwardReference type");
		guide.LegacySource.BodyColumns[0].Attribute.Should().Be("PDS_Account_Type", because: "the report names the attribute the row binds to");
	}

	[Test]
	[Description("Several subtitle and group columns keep wizard row order: all subtitle rows first (by row), then all group rows (by row), regardless of the order the operations appear in the body.")]
	public void Analyze_ShouldKeepWizardRowOrder_WhenSeveralSubtitleAndGroupColumnsExist() {
		// Arrange — group column listed BEFORE subtitle columns, and subtitle rows out of order.
		string source = Settings("Contact",
			Column("items", 0, "Name", "Name"),
			Column("groupItems", 0, "Type", "Type") + Column("subtitleItems", 1, "Email", "Email") + Column("subtitleItems", 0, "Account", "Account"),
			Column("groupItems", 1, "Owner", "Owner"));

		// Act
		MobilePageConversionGuide guide = Analyze(Read(source));

		// Assert
		ListItem(guide).MobileValues["body"]!.AsArray().Select(r => r!["value"]!.GetValue<string>())
			.Should().Equal(new[] { "$PDS_Account", "$PDS_Email", "$PDS_Type", "$PDS_Owner" }, because: "subtitle rows by row then group rows by row is the order the wizard displayed them in");
		guide.ViewModelConfigDiff!.AsArray()[0]!["values"]!.AsObject().Select(kv => kv.Key)
			.Should().Equal(new[] { "PDS_Account", "PDS_Email", "PDS_Type", "PDS_Owner", "PDS_Name", "PDS_Id" }, because: "attributes are declared in body order, then the title, then PDS_Id — deterministic for byte-stable output");
	}

	[Test]
	[Description("A column present in two wizard buckets is bound twice on the row but declared once as an attribute, with a note explaining the deduplication.")]
	public void Analyze_ShouldDeclareAttributeOnce_WhenColumnAppearsInTwoBuckets() {
		// Arrange
		string source = Settings("Order",
			Column("items", 0, "Number", "Number"),
			Column("subtitleItems", 0, "Account", "Account", 10),
			Column("groupItems", 0, "Account", "Account", 10));

		// Act
		MobilePageConversionGuide guide = Analyze(Read(source));

		// Assert
		guide.ViewModelConfigDiff!.AsArray()[0]!["values"]!.AsObject().Count(kv => kv.Key == "PDS_Account").Should().Be(1,
			because: "a JSON object cannot declare the same attribute twice");
		ListItem(guide).MobileValues["body"]!.AsArray().Should().HaveCount(2, because: "both wizard rows are still rendered");
		guide.LegacySource.Notes.Should().Contain(n => n.Contains("more than one wizard bucket"), because: "the deduplication is reported, not silent");
	}

	[Test]
	[Description("When the wizard 'items' bucket is empty the ListItem merge carries no title key, a decision asks the user to choose one, and a constraint tells the caller how to add it.")]
	public void Analyze_ShouldOmitTitleAndAskForDecision_WhenNoTitleColumnExists() {
		// Arrange
		string source = Settings("Order", string.Empty, Column("subtitleItems", 0, "Account", "Account", 10), string.Empty);

		// Act
		MobilePageConversionGuide guide = Analyze(Read(source));

		// Assert
		ListItem(guide).MobileValues.AsObject().ContainsKey("title").Should().BeFalse(because: "there is no column to bind the title to");
		guide.LegacySource.TitleColumn.Should().BeNull(because: "the report shows no title column");
		guide.LegacySource.Decisions.Should().Contain(d => d.Contains("No title column"), because: "the user must choose the title");
		guide.Constraints.Should().Contain(c => c.Contains("No title column was found"), because: "the caller is told how to add the title after the decision");
		guide.ViewModelConfigDiff!.AsArray()[0]!["values"]!.AsObject().Select(kv => kv.Key).Should().Equal(new[] { "PDS_Account", "PDS_Id" }, because: "only the body column and PDS_Id are declared");
	}

	[Test]
	[Description("When the wizard 'items' bucket holds several columns the lowest row becomes the title and the others are moved to the front of the body, each reported as a decision (adapted, not silently lost).")]
	public void Analyze_ShouldUseLowestRowAsTitle_WhenSeveralTitleColumnsExist() {
		// Arrange
		string source = Settings("Order",
			Column("items", 1, "Account", "Account", 10) + Column("items", 0, "Number", "Number"),
			Column("subtitleItems", 0, "PaymentAmount", "Payment amount", 6),
			string.Empty);

		// Act
		MobilePageConversionGuide guide = Analyze(Read(source));

		// Assert
		ListItem(guide).MobileValues["title"]!.GetValue<string>().Should().Be("$PDS_Number", because: "row 0 wins the title");
		ListItem(guide).MobileValues["body"]!.AsArray().Select(r => r!["value"]!.GetValue<string>()).Should().Equal(new[] { "$PDS_Account", "$PDS_PaymentAmount" }, because: "the extra title column is prepended to the body so it is not lost");
		guide.LegacySource.BodyColumns[0].Bucket.Should().Be("title", because: "the report shows where the moved column came from");
		guide.LegacySource.Decisions.Should().Contain(d => d.Contains("more than one column") && d.Contains("Account"),
			because: "the adaptation is a decision for the user");
	}

	[Test]
	[Description("Wizard column properties without a counterpart on a ListItem row (view types such as phone/email/url, formats) are reported as dropped in the coverage table with the columns that carried them, added to decisions, and named in a constraint.")]
	public void Analyze_ShouldReportDroppedColumnProperties_WhenWizardRecordedViewTypes() {
		// Arrange
		string source = Settings("Contact",
			Column("items", 0, "Name", "Name"),
			Column("subtitleItems", 0, "Phone", "Phone", 1, ", \"viewType\": \"phone\"") + Column("subtitleItems", 1, "Email", "Email", 1, ", \"viewType\": \"email\", \"displayFormat\": \"short\""),
			string.Empty);

		// Act
		MobilePageConversionGuide guide = Analyze(Read(source));

		// Assert
		LegacyPropertyCoverageInfo viewType = guide.LegacySource.ColumnPropertyCoverage.Single(c => c.Property == "viewType");
		viewType.Status.Should().Be("dropped", because: "a template ListItem body row carries only a value binding");
		viewType.Columns.Should().Equal(new[] { "Phone", "Email" }, because: "the coverage row names the columns that carried the property");
		guide.LegacySource.ColumnPropertyCoverage.Should().Contain(c => c.Property == "displayFormat" && c.Status == "dropped",
			because: "every unknown column property is covered, not just the first");
		guide.LegacySource.ColumnPropertyCoverage.Should().Contain(c => c.Property == "columnName" && c.Status == "transferred",
			because: "the binding itself transfers");
		guide.LegacySource.ColumnPropertyCoverage.Should().Contain(c => c.Property == "content" && c.Status == "informational",
			because: "captions are not rendered on mobile list rows");
		guide.LegacySource.Decisions.Should().Contain(d => d.Contains("'viewType'"), because: "dropped properties need a user decision");
		guide.Constraints.Should().Contain(c => c.Contains("Dropped column properties") && c.Contains("viewType") && c.Contains("displayFormat"),
			because: "the caller is told to present the dropped properties at the plan gate");
	}

	[Test]
	[Description("The wizard's classic grid layout settings (gridType / rows / columns, present on every OOTB list settings schema) are informational — reported in notes, never as a user decision — while an unknown settings property still becomes a decision.")]
	public void Analyze_ShouldTreatGridLayoutSettingsAsInformational_AndUnknownSettingsAsDecisions() {
		// Arrange
		string source = Settings("Activity",
			Column("items", 0, "Title", "Title"), string.Empty, string.Empty,
			", \"gridType\": \"listed\", \"rows\": 1, \"columns\": 1, \"mysteryFlag\": true");

		// Act
		MobilePageConversionGuide guide = Analyze(Read(source));

		// Assert
		guide.LegacySource.Decisions.Should().NotContain(d => d.Contains("gridType") || d.Contains("'rows'") || d.Contains("'columns'"),
			because: "classic grid geometry is not something the user can decide about on a mobile list row");
		guide.LegacySource.Notes.Should().Contain(n => n.Contains("gridType 'listed'"), because: "the classic layout is still reported for transparency");
		guide.LegacySource.Decisions.Should().ContainSingle(d => d.Contains("'mysteryFlag'"), because: "a genuinely unknown settings property needs a decision");
	}

	[Test]
	[Description("Freedom UI override sections embedded in the settings are recognised and reported (classification freedom-ui-overrides, overrideSections with op counts, an ENG-95733 constraint) but the wizard buckets still convert and no override content leaks into the diffs.")]
	public void Analyze_ShouldReportOverrides_WhenSettingsCarryFreedomUiSections() {
		// Arrange — the wizard stores override sections as JSON-encoded strings.
		string source = Settings("Order",
			Column("items", 0, "Number", "Number"), string.Empty, string.Empty,
			", \"viewConfigDiff\": \"[{\\\"operation\\\":\\\"merge\\\",\\\"name\\\":\\\"X\\\",\\\"values\\\":{}}]\", \"modelConfigDiff\": []");

		// Act
		MobilePageConversionGuide guide = Analyze(Read(source));

		// Assert
		guide.LegacySource.Classification.Should().Be("freedom-ui-overrides", because: "override sections were present");
		guide.LegacySource.OverrideSections.Should().Contain(s => s.Section == "viewConfigDiff" && s.OperationCount == 1 && s.Supported && s.Ticket == null,
			because: "a string-encoded section is parsed, and this format is processed operation by operation rather than deferred to a ticket");
		guide.LegacySource.OverrideSections.Should().NotContain(s => s.Section == "modelConfigDiff",
			because: "an EMPTY placeholder section carries nothing to convert and must not be reported as a dropped override");
		guide.LegacySource.Notes.Should().Contain(n => n.Contains("'modelConfigDiff'") && n.Contains("empty"),
			because: "the empty placeholder is still mentioned for transparency");
		guide.Constraints.Should().Contain(c => c.Contains("re-pointed individually") && c.Contains("overrideOutcomes"),
			because: "the caller must present the per-operation outcome of every override");
		guide.LegacySource.OverrideOutcomes.Should().ContainSingle(o => o.Target == "X" && o.Lane == LegacyOverrideLanes.Reported,
			because: "'X' is not a name the runtime would generate for this source, so it is reported instead of guessed at");
		guide.ModelConfigDiff!.AsArray().Should().HaveCount(1, because: "an empty override section contributes no operation");
		ListItem(guide).MobileValues["title"]!.GetValue<string>().Should().Be("$PDS_Number", because: "the wizard buckets still convert");
	}

	[Test]
	[Description("Two package layers merge ROOT -> HEAD: the head layer's insert at index 0 lands first, its remove takes a subtitle column away, and its merge changes a caption — and every layer appears in legacySource.layers without a body.")]
	public void Analyze_ShouldReflectMergedLayers_WhenSettingsSpanTwoPackages() {
		// Arrange
		string subtitleName = Guid.NewGuid().ToString();
		string root = $$"""
			[
			  { "operation": "insert", "name": "settings", "values": { "entitySchemaName": "Order", "items": [], "subtitleItems": [], "groupItems": [], "settingsType": "GridPage", "operation": "insert", "localizableStrings": {} } },
			  { "operation": "insert", "name": "t1", "values": { "row": 0, "content": "Number", "columnName": "Number", "dataValueType": 1, "operation": "insert" }, "parentName": "settings", "propertyName": "items", "index": 0 },
			  { "operation": "insert", "name": "{{subtitleName}}", "values": { "row": 0, "content": "Account", "columnName": "Account", "dataValueType": 10, "operation": "insert" }, "parentName": "settings", "propertyName": "subtitleItems", "index": 0 },
			  { "operation": "insert", "name": "g1", "values": { "row": 1, "content": "Owner", "columnName": "Owner", "dataValueType": 10, "operation": "insert" }, "parentName": "settings", "propertyName": "groupItems", "index": 0 }
			]
			""";
		string head = $$"""
			[
			  { "operation": "remove", "name": "{{subtitleName}}" },
			  { "operation": "insert", "name": "g0", "values": { "row": 0, "content": "Status", "columnName": "Status", "dataValueType": 10, "operation": "insert" }, "parentName": "settings", "propertyName": "groupItems", "index": 0 },
			  { "operation": "merge", "name": "t1", "values": { "content": "Order number" } }
			]
			""";

		// Act
		MobilePageConversionGuide guide = Analyze(Read(root, head));

		// Assert
		ListItem(guide).MobileValues["body"]!.AsArray().Select(r => r!["value"]!.GetValue<string>()).Should().Equal(new[] { "$PDS_Status", "$PDS_Owner" }, because: "the head layer removed Account and inserted Status at row 0 before Owner");
		guide.LegacySource.TitleColumn.Caption.Should().Be("Order number", because: "the head layer's merge changed the title caption");
		guide.LegacySource.Layers.Should().HaveCount(2, because: "both package layers contributed");
		guide.LegacySource.Layers.Select(l => l.OperationCount).Should().Equal(new[] { 4, 3 }, because: "layers are reported ROOT -> HEAD with their operation counts");
		guide.LegacySource.Layers.Select(l => l.PackageName).Should().Equal(new[] { "Package0", "Package1" }, because: "the contributing packages are named for the plan");
	}

	[Test]
	[Description("A wizard column literally named Id does not declare PDS_Id twice or move it: PDS_Id stays the LAST attribute and the row still binds $PDS_Id.")]
	public void Analyze_ShouldKeepPdsIdLast_WhenAColumnIsNamedId() {
		// Arrange
		string source = Settings("Order",
			Column("items", 0, "Number", "Number"),
			Column("subtitleItems", 0, "Id", "Id", 0),
			string.Empty);

		// Act
		MobilePageConversionGuide guide = Analyze(Read(source));

		// Assert
		guide.ViewModelConfigDiff!.AsArray()[0]!["values"]!.AsObject().Select(kv => kv.Key).Should().Equal(new[] { "PDS_Number", "PDS_Id" },
			because: "PDS_Id is declared once, last, by the converter itself");
		ListItem(guide).MobileValues["body"]!.AsArray()[0]!["value"]!.GetValue<string>().Should().Be("$PDS_Id",
			because: "the row still binds the column the wizard asked for");
	}

	[Test]
	[Description("RECORDED DIVERGENCE from the mobile runtime converter: subtitle columns land in ListItem.body (as the Mobile designer generates), never in the template's 'subtitles' slot, and no search-column list is emitted (the template searches $Items through crt.OpenSearchListRequest); both are stated in legacySource.notes.")]
	public void Analyze_ShouldPutSubtitlesInBodyAndEmitNoSearchColumns_AsRecordedDivergences() {
		// Arrange
		string source = Settings("Order",
			Column("items", 0, "Number", "Number"),
			Column("subtitleItems", 0, "Account", "Account", 10),
			string.Empty);

		// Act
		MobilePageConversionGuide guide = Analyze(Read(source));

		// Assert
		JsonObject values = ListItem(guide).MobileValues.AsObject();
		values.ContainsKey("subtitles").Should().BeFalse(because: "the designer's own list page puts every extra column in body");
		values["body"]!.AsArray().Select(r => r!["value"]!.GetValue<string>()).Should().Equal(new[] { "$PDS_Account" }, because: "the subtitle column is a body row");
		Json(guide.ViewModelConfigDiff).Should().NotContain("search", because: "no per-page search-column configuration exists in the BaseMobileListTemplate vocabulary");
		guide.LegacySource.Notes.Should().Contain(n => n.Contains("ListItem.subtitles"), because: "the subtitle divergence is written down for the user");
		guide.LegacySource.Notes.Should().Contain(n => n.Contains("crt.OpenSearchListRequest"), because: "the search divergence is written down for the user");
	}

	[Test]
	[Description("Running the analysis twice on the same input yields byte-identical diffs and ListItem values (idempotent, deterministic key order).")]
	public void Analyze_ShouldBeDeterministic_WhenRunTwice() {
		// Arrange
		string source = File.ReadAllText(FixturePath("MobileOrderGridPageSettingsDefaultWorkplace.json"));

		// Act
		MobilePageConversionGuide first = Analyze(Read(source));
		MobilePageConversionGuide second = Analyze(Read(source));

		// Assert
		Json(second.ElementMap[0].MobileValues).Should().Be(Json(first.ElementMap[0].MobileValues), because: "the row values must not depend on run order");
		Json(second.ViewModelConfigDiff).Should().Be(Json(first.ViewModelConfigDiff), because: "the attribute declaration must be stable");
		Json(second.ModelConfigDiff).Should().Be(Json(first.ModelConfigDiff), because: "the data source declaration must be stable");
	}

	[Test]
	[Description("The provided section registration is passed through onto the guide so the caller can drive Gate S from one place.")]
	public void Analyze_ShouldCarrySectionRegistration_WhenProvided() {
		// Arrange
		string source = File.ReadAllText(FixturePath("MobileOrderGridPageSettingsDefaultWorkplace.json"));
		var section = new SectionRegistrationInfo { SourcePageIsSection = true, SectionCode = "Order", ProbeOk = true };

		// Act
		MobilePageConversionGuide guide = Analyze(Read(source), section);

		// Assert
		guide.SectionRegistration.Should().BeSameAs(section, because: "the probe result is surfaced verbatim");
	}

	[TestCase("Order", "Usr", ExpectedResult = "UsrOrder_MobileListPage")]
	[TestCase("UsrMK_Test", "Usr", ExpectedResult = "UsrMK_Test_MobileListPage")]
	[TestCase("Order", "", ExpectedResult = "Order_MobileListPage")]
	[TestCase("Order", null, ExpectedResult = "Order_MobileListPage")]
	[TestCase("Contact", "Glb", ExpectedResult = "GlbContact_MobileListPage")]
	[Description("The default target name is <Prefix><Entity>_MobileListPage, without doubling a prefix the entity already carries, and without a prefix when the environment declares none.")]
	public string DeriveTargetSchemaName_ShouldPrefixOnce(string entity, string prefix) =>
		LegacyMobileListAnalysisService.DeriveTargetSchemaName(entity, prefix);

	[TestCase("MobileOrderGridPageSettingsDefaultWorkplace", "Order", "DefaultWorkplace", false)]
	[TestCase("MobileCaseGridPageSettings", "Case", null, false)]
	[TestCase("MobileActivityRecordPageSettingsDefaultWorkplace", "Activity", "DefaultWorkplace", true)]
	[Description("The schema name pattern Mobile<Entity>(Grid|Record)PageSettings<Workplace> yields entity, optional workplace and the page kind.")]
	public void TryParseSchemaName_ShouldExtractParts(string name, string entity, string workplace, bool isRecord) {
		// Act
		LegacyMobileListAnalysisService.LegacySchemaNameParts parts = LegacyMobileListAnalysisService.TryParseSchemaName(name);

		// Assert
		parts.Should().NotBeNull(because: "the name follows the wizard pattern");
		parts.Entity.Should().Be(entity, because: "the entity sits between the Mobile prefix and the settings kind");
		parts.Workplace.Should().Be(workplace, because: "the workplace is the optional suffix");
		parts.IsRecordPage.Should().Be(isRecord, because: "the kind distinguishes list from record settings");
	}

	[Test]
	[Description("A name that does not follow the wizard pattern yields null instead of a guess.")]
	public void TryParseSchemaName_ShouldReturnNull_WhenNameDoesNotMatch() {
		LegacyMobileListAnalysisService.TryParseSchemaName("UsrOrder_ListPage").Should().BeNull(because: "a Freedom UI page name is not a wizard settings name");
	}

	[Test]
	[Description("The analysis refuses a failed read instead of producing an empty guide.")]
	public void Analyze_ShouldThrow_WhenReadFailed() {
		// Arrange
		LegacyMobileSettingsReadResult failed = LegacyMobileSettingsReadResult.Fail(SourceSchema, "boom");
		var classification = new LegacySettingsClassification(LegacySettingsKind.Plain, [], []);

		// Act
		Action act = () => LegacyMobileListAnalysisService.Analyze(failed, classification, SourceSchema, Target, null, Template);

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "a guide must never be built from nothing");
	}

	[Test]
	[Description("Settings without entitySchemaName cannot bind a mobile page and are rejected with a clear message.")]
	public void Parse_ShouldThrow_WhenEntitySchemaNameIsMissing() {
		// Arrange
		JObject settings = JObject.Parse("""{ "name": "settings", "settingsType": "GridPage", "items": [] }""");

		// Act
		Action act = () => LegacyGridPageSettingsParser.Parse(settings);

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*entitySchemaName*", because: "the missing binding is named");
	}

	[Test]
	[Description("END TO END over the real shipped override `remove ViewConfig properties:[floatAction]`: the guide gains a third elementMap entry removing the template's CreateRecordButton, the outcome is recorded as a target delta, and the wizard buckets convert unchanged alongside it.")]
	public void Analyze_ShouldCarryAShippedOverride_IntoTheElementMap() {
		// Arrange
		string source = Settings("Order",
			Column("items", 0, "Number", "Number"), string.Empty, Column("groupItems", 0, "Account", "Account"),
			", \"viewConfigDiff\": \"[{\\\"operation\\\":\\\"remove\\\",\\\"name\\\":\\\"ViewConfig\\\",\\\"properties\\\":[\\\"floatAction\\\"]}]\"");

		// Act
		MobilePageConversionGuide guide = Analyze(Read(source));

		// Assert
		guide.ElementMap.Should().HaveCount(3, because: "the two designer merges are joined by the override's own operation");
		ElementMapEntry carried = guide.ElementMap[2];
		carried.Operation.Should().Be("remove", because: "the override switches the floating action off");
		carried.MobileName.Should().Be("CreateRecordButton", because: "that is what the shipped template calls the floating action");
		carried.MobileValues.Should().BeNull(because: "a removal carries no values");
		carried.Reason.Should().Contain("viewConfigDiff[0]", because: "the reason points back at the exact source operation");
		guide.LegacySource.OverrideOutcomes.Should().ContainSingle(o => o.Lane == LegacyOverrideLanes.TargetDelta,
			because: "the operation was carried onto the converted page rather than reported");
		ListItem(guide).MobileValues["title"]!.GetValue<string>().Should().Be("$PDS_Number",
			because: "the wizard buckets convert exactly as they do without an override");
		Json(ListItem(guide).MobileValues["body"]).Should().Be("""[{"value":"$PDS_Account"}]""",
			because: "a chrome-level override must not disturb the converted columns");
	}

	[Test]
	[Description("END TO END over the two shipped OOTB overrides (row icon + default sort): the icon lands on the elementMap's ListItem entry with its binding re-derived, the column it needs is declared in BOTH data sections before PDS_Id, and the sort default becomes a targeted merge on the Items sortingConfig path.")]
	public void Analyze_ShouldCarryIconAndSort_IntoTheElementMapAndBothDiffs() {
		// Arrange — the shape MobileFUIContactGridPageSettingsDefaultWorkplace ships.
		string source = Settings("Contact",
			Column("items", 0, "Name", "Name"), string.Empty, string.Empty,
			", \"viewConfigDiff\": \"[{\\\"operation\\\":\\\"merge\\\",\\\"name\\\":\\\"Contact_ListItem\\\",\\\"values\\\":{\\\"icon\\\":\\\"$Photo\\\"}}]\""
			+ ", \"viewModelConfigDiff\": \"[{\\\"operation\\\":\\\"insert\\\",\\\"name\\\":\\\"Attribute_Items_SortingConfig\\\",\\\"parentName\\\":\\\"Attribute_Items_ModelConfig\\\",\\\"propertyName\\\":\\\"sortingConfig\\\",\\\"values\\\":{\\\"default\\\":[{\\\"columnName\\\":\\\"Name\\\",\\\"direction\\\":\\\"asc\\\"}]}}]\"");

		// Act
		MobilePageConversionGuide guide = Analyze(Read(source));

		// Assert — the icon is folded into the converter's own ListItem merge, not added as a rival entry.
		guide.ElementMap.Should().HaveCount(2, because: "the override refines the row the converter already writes");
		ListItem(guide).MobileValues["icon"]!.GetValue<string>().Should().Be("$PDS_Photo",
			because: "$Photo would resolve to nothing; the converted page declares PDS_Photo");

		// …and everything the carried binding needs is declared, with PDS_Id still last.
		JsonObject attributes = guide.ViewModelConfigDiff![0]!["values"]!.AsObject();
		attributes.Select(pair => pair.Key).Should().Equal(["PDS_Name", "PDS_Photo", "PDS_Id"],
			because: "the icon column is declared like a wizard column, and PDS_Id stays the last attribute");
		guide.ModelConfigDiff![0]!["values"]!["attributes"]!["Photo"]!["path"]!.GetValue<string>().Should().Be("Photo",
			because: "the data source must load the column the icon binds to");

		// …and the sort default is a targeted merge on the path the shipped designer page really carries.
		Json(guide.ViewModelConfigDiff!.AsArray()[1]).Should().Be(
			"""{"operation":"merge","path":["attributes","Items","modelConfig","sortingConfig"],"values":{"default":[{"columnName":"Name","direction":"asc"}]}}""",
			because: "the template already supplies attributeName on that node, so only 'default' is set");
		guide.LegacySource.OverrideOutcomes.Should().OnlyContain(o => o.Lane == LegacyOverrideLanes.TargetDelta,
			because: "both shipped overrides are carried, neither is reported");
	}

	[Test]
	[Description("Warnings about embedded overrides reach guide.constraints — the block a caller cannot skip — and nowhere else: an override whose outcome differs from what it asked for must not be discoverable only by reading a report section.")]
	public void Analyze_ShouldSurfaceOverrideWarnings_InTheConstraints() {
		// Arrange — the shipped Contact move plus an operation whose target this source never generates.
		string source = Settings("Contact",
			Column("items", 0, "Name", "Name"), string.Empty, Column("groupItems", 0, "Account", "Account"),
			", \"viewConfigDiff\": \"[{\\\"operation\\\":\\\"remove\\\",\\\"name\\\":\\\"Contact_ListItem_Body_Account\\\"},{\\\"operation\\\":\\\"insert\\\",\\\"name\\\":\\\"Contact_ListItem_Subtitle_Account\\\",\\\"parentName\\\":\\\"Contact_ListItem\\\",\\\"propertyName\\\":\\\"subtitles\\\",\\\"index\\\":0,\\\"values\\\":{\\\"value\\\":\\\"$Account\\\"}}]\""
			+ ", \"viewModelConfigDiff\": \"[{\\\"operation\\\":\\\"remove\\\",\\\"name\\\":\\\"GlbContactStatusActiveFilter\\\"}]\"");

		// Act
		MobilePageConversionGuide guide = Analyze(Read(source));

		// Assert
		guide.Constraints.Should().Contain(c => c.Contains("moves column 'Account'") && c.Contains("nothing was changed"),
			because: "a move between row slots is a no-op on the converted page and the caller must be told");
		guide.Constraints.Should().Contain(c => c.Contains("GlbContactStatusActiveFilter") && c.Contains("SKIPPED"),
			because: "an operation whose target this source never generates is skipped, and the caller must be told");
		ListItem(guide).MobileValues["body"]!.AsArray().Should().ContainSingle(
			because: "the column survives the attempted move exactly where it was");
	}

	[Test]
	[Description("The shipped conversion rules file carries a mobileLegacyTemplates.gridPage group, and it agrees with the bundled defaults so the two cannot drift apart silently.")]
	public void ShippedRules_ShouldCarryGridPageTemplate_MatchingTheBundledDefaults() {
		// Arrange
		// The rules file ships as an embedded resource, so the bundled loader is what the tool actually reads.
		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.LoadBundled();

		// Act
		MobileLegacyTemplateRule resolved = LegacyMobileListAnalysisService.ResolveGridPageTemplate(rules);

		// Assert
		rules!.MobileLegacyTemplates?.GridPage.Should().NotBeNull(
			because: "the legacy branch picks its target template from the rules file, not from a constant");
		resolved.TemplateName.Should().Be(Template.TemplateName, because: "the shipped group and the bundled fallback must agree");
		resolved.ListItemName.Should().Be(Template.ListItemName, because: "the shipped group and the bundled fallback must agree");
		resolved.ListName.Should().Be(Template.ListName, because: "the shipped group and the bundled fallback must agree");
		resolved.ListContainerName.Should().Be(Template.ListContainerName, because: "the shipped group and the bundled fallback must agree");
		resolved.FolderTreeActionsName.Should().Be(Template.FolderTreeActionsName, because: "the shipped group and the bundled fallback must agree");
		resolved.FolderSourceSchemaName.Should().Be(Template.FolderSourceSchemaName, because: "the shipped group and the bundled fallback must agree");
		resolved.ItemsAttributeName.Should().Be(Template.ItemsAttributeName, because: "the shipped group and the bundled fallback must agree");
	}

	[Test]
	[Description("The shipped grid-page group names the BaseMobileListTemplate elements exactly as the live template declares them (verified against DevMK) — CreateRecordButton in particular, because the runtime carries the floating action as a ViewConfig PROPERTY while the designer carries it as a NAMED element, so an invented name would add a second button instead of removing one.")]
	public void ShippedRules_ShouldNameTheTemplateElements_AsTheLiveTemplateDeclaresThem() {
		// Arrange
		MobileLegacyTemplateRule resolved =
			LegacyMobileListAnalysisService.ResolveGridPageTemplate(WebToMobilePageConversionRulesCatalog.LoadBundled());

		// Act
		(string Actual, string Expected)[] pairs = [
			(resolved.TemplateName, "BaseMobileListTemplate"),
			(resolved.ScaffoldName, "Scaffold"),
			(resolved.MainContainerName, "MainContainer"),
			(resolved.HeaderContainerName, "HeaderContainer"),
			(resolved.SearchButtonName, "SearchButton"),
			(resolved.FilterGroupButtonName, "FilterGroupButton"),
			(resolved.SortButtonName, "SortButton"),
			(resolved.FolderTreeActionsName, "FolderTreeActions"),
			(resolved.QuickFilterGroupName, "QuickFilterGroup"),
			(resolved.ListContainerName, "ListContainer"),
			(resolved.ListName, "List"),
			(resolved.ListItemName, "ListItem"),
			(resolved.CreateRecordButtonName, "CreateRecordButton")
		];

		// Assert
		pairs.Should().OnlyContain(p => p.Actual == p.Expected,
			because: "these are the twelve elements BaseMobileListTemplate declares; see the knowledge record base-mobile-list-template-element-inventory");
	}

	[Test]
	[Description("Rules without the mobileLegacyTemplates group degrade to the bundled defaults instead of failing, so an older CDN-served rules file keeps the legacy branch working.")]
	public void ResolveGridPageTemplate_ShouldFallBackToDefaults_WhenRulesCarryNoGroup() {
		// Arrange
		var empty = new WebToMobilePageConversionRules();

		// Act
		MobileLegacyTemplateRule fromEmpty = LegacyMobileListAnalysisService.ResolveGridPageTemplate(empty);
		MobileLegacyTemplateRule fromNull = LegacyMobileListAnalysisService.ResolveGridPageTemplate(null);

		// Assert
		fromEmpty.Should().BeSameAs(LegacyMobileListAnalysisService.DefaultGridPageTemplate,
			because: "a rules file that predates the group must not break the legacy branch");
		fromNull.Should().BeSameAs(LegacyMobileListAnalysisService.DefaultGridPageTemplate,
			because: "an unreachable rules file must not break the legacy branch either");
	}

	[Test]
	[Description("The target template and its element names come from the rules data, not from constants: a different mobileLegacyTemplates.gridPage group changes the recommended template, both elementMap targets and the next steps.")]
	public void Analyze_ShouldTakeTargetTemplateFromRules_WhenGroupOverridesTheDefaults() {
		// Arrange
		LegacyMobileSettingsReadResult read = Read(Settings("Order", Column("items", 0, "Number", "Number"), "", ""));
		var custom = new MobileLegacyTemplateRule {
			TemplateName = "UsrCustomMobileListTemplate",
			ListName = "UsrList",
			ListContainerName = "UsrListContainer",
			ListItemName = "UsrRow",
			ListItemType = "crt.ListItem",
			FolderTreeActionsName = "UsrFolders",
			FolderTreeActionsType = "crt.FolderTreeActions",
			FolderSourceSchemaName = "UsrFolderTree",
			ItemsAttributeName = "Records"
		};

		// Act
		MobilePageConversionGuide guide = Analyze(read, template: custom);

		// Assert
		guide.RecommendedMobileTemplate.Should().Be("UsrCustomMobileListTemplate",
			because: "the recommended template is read from the rules group, not hardcoded");
		guide.ElementMap.Select(e => e.MobileName).Should().Equal(["UsrFolders", "UsrRow"],
			because: "both merge targets are the template element names the rules group declares, in the designer's order");
		guide.ElementMap[0].MobileValues!["sourceSchemaName"]!.GetValue<string>().Should().Be("UsrFolderTree",
			because: "the folder schema binding comes from the rules group too");
		Json(guide.ViewModelConfigDiff![0]!["path"]!).Should().Contain("Records",
			because: "the collection attribute the list is bound to is named by the rules group");
		guide.Constraints.Should().Contain(c => c.Contains("UsrCustomMobileListTemplate"),
			because: "the constraints must name the template the rules group selected");
	}
}
