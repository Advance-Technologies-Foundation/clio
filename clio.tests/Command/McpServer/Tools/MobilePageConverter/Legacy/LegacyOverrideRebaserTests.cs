using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Command.McpServer.Tools.MobilePageConverter;
using Clio.Command.McpServer.Tools.MobilePageConverter.Legacy;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer.Tools.MobilePageConverter.Legacy;

/// <summary>
/// Unit tests for the override REBASE (ENG-95733). Every case is taken from an override actually shipped in a
/// partner package (<c>C:\builds\vetoquinol\Pkg</c>), because the point of the story is that real customisations
/// survive conversion — or are reported honestly when they cannot.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class LegacyOverrideRebaserTests {

	private static readonly MobileLegacyTemplateRule Template =
		LegacyMobileListAnalysisService.ResolveGridPageTemplate(WebToMobilePageConversionRulesCatalog.LoadBundled());

	private static readonly MobileLegacyRuntimeNameSet NameSet =
		WebToMobilePageConversionRulesCatalog.LoadBundled().MobileLegacyRuntimeNames?.GridPage;

	private static LegacyGridPageSettings Parse(string entity, string items = "", string subtitles = "", string groups = "") =>
		LegacyGridPageSettingsParser.Parse(JObject.Parse($$"""
			{
			  "name": "settings", "settingsType": "GridPage", "entitySchemaName": "{{entity}}",
			  "items": [{{items}}], "subtitleItems": [{{subtitles}}], "groupItems": [{{groups}}]
			}
			"""));

	private static string Column(string columnName, int row = 0) => Caption(columnName, columnName, row);

	/// <summary>A wizard column whose caption differs from its name, so caption propagation is observable.</summary>
	private static string Caption(string columnName, string caption, int row = 0) =>
		$$"""{ "name": "id-{{columnName}}", "row": {{row}}, "content": "{{caption}}", "columnName": "{{columnName}}", "dataValueType": 1 }""";

	private static LegacyOverrideSection Section(string name, string operationsJson) {
		JArray ops = JArray.Parse(operationsJson);
		return new LegacyOverrideSection(name, ops.Count, LegacyMobileSettingsClassifier.OverridesTicket, true, null, ops);
	}

	private static LegacyOverrideRebaseResult Rebase(LegacyGridPageSettings settings, params LegacyOverrideSection[] sections) =>
		LegacyOverrideRebaser.Rebase(settings, sections,
			LegacyRuntimeNameOracle.Build(settings, NameSet), Template, NameSet);

	private static string Json(JsonNode node) => node.ToJsonString(new JsonSerializerOptions { WriteIndented = false });

	[Test]
	[Description("REAL: `remove ViewConfig properties:[floatAction]` (3 of the 4 shipped grid overrides) becomes a removal of the template's CreateRecordButton — the runtime keeps the floating action as a property of its screen root, the designer as a named element.")]
	public void Rebase_ShouldTurnAFloatActionPropertyRemoval_IntoRemovingTheTemplatesCreateRecordButton() {
		// Arrange
		LegacyGridPageSettings settings = Parse("GlbTerritoryManagerRel", items: Column("GlbManager"));
		LegacyOverrideSection section = Section("viewConfigDiff",
			"""[{"operation":"remove","name":"ViewConfig","properties":["floatAction"]}]""");

		// Act
		LegacyOverrideRebaseResult result = Rebase(settings, section);

		// Assert
		result.ViewConfigOperations.Should().ContainSingle(because: "one property removal becomes one element removal");
		Json(result.ViewConfigOperations[0]).Should().Be("""{"operation":"remove","name":"CreateRecordButton"}""",
			because: "CreateRecordButton is what BaseMobileListTemplate calls the floating action");
		result.Outcomes.Should().ContainSingle(o => o.Lane == LegacyOverrideLanes.TargetDelta,
			because: "the removal exists only in the target dialect, so it is a target delta");
		result.Settings.Items.Should().HaveCount(1, because: "the wizard buckets are untouched by a chrome removal");
	}

	[Test]
	[Description("REAL: the shipped Contact override moves column Account from the row body into the subtitle slot. A converted row has ONE slot and already shows the column, so nothing changes — the removal is NOT applied on its own — and a warning says so.")]
	public void Rebase_ShouldChangeNothingButWarn_WhenAnOverrideMovesAColumnBetweenRowSlots() {
		// Arrange
		LegacyGridPageSettings settings = Parse("Contact", items: Column("Name"), groups: Column("Account"));
		LegacyOverrideSection section = Section("viewConfigDiff", """
			[{"operation":"remove","name":"Contact_ListItem_Body_Account"},
			 {"operation":"insert","name":"Contact_ListItem_Subtitle_Account","values":{"value":"$Account","label":{"visible":true}},"parentName":"Contact_ListItem","propertyName":"subtitles","index":0}]
			""");

		// Act
		LegacyOverrideRebaseResult result = Rebase(settings, section);

		// Assert
		result.Settings.GroupItems.Should().ContainSingle(c => c.ColumnName == "Account",
			because: "the column stays exactly where it is: applying only the removal would delete it instead of moving it");
		result.Settings.SubtitleItems.Should().BeEmpty(because: "no second copy of the column is added");
		result.ViewConfigOperations.Should().BeEmpty(because: "the move needs no operation on the converted page");
		result.Warnings.Should().ContainSingle(w => w.Contains("moves column 'Account'") && w.Contains("nothing was changed"),
			because: "the user must learn the move was a no-op rather than assume the slot was honoured");
		result.Outcomes.Should().HaveCount(2, because: "both halves of the pair are accounted for");
	}

	[Test]
	[Description("A lone insert into the subtitle slot ADDS the column to the list row as an ordinary column — its value is shown — with a warning that the separate subtitle placement is not reproduced.")]
	public void Rebase_ShouldAddTheColumn_WhenAnOverrideInsertsOneIntoTheSubtitleSlot() {
		// Arrange — the source does not carry Account at all.
		LegacyGridPageSettings settings = Parse("Contact", items: Column("Name"));
		LegacyOverrideSection section = Section("viewConfigDiff",
			"""[{"operation":"insert","name":"Contact_ListItem_Subtitle_Account","values":{"value":"$Account","label":{"visible":true}},"parentName":"Contact_ListItem","propertyName":"subtitles","index":0}]""");

		// Act
		LegacyOverrideRebaseResult result = Rebase(settings, section);

		// Assert
		result.Settings.SubtitleItems.Should().ContainSingle(c => c.ColumnName == "Account",
			because: "the column the override asked for is added to the wizard model, so the analyzer renders it");
		result.Outcomes.Single().Lane.Should().Be(LegacyOverrideLanes.SourceEdit,
			because: "the addition is expressed in the wizard model, not as a target operation");
		result.Warnings.Should().ContainSingle(w => w.Contains("added to the row body"),
			because: "the value is shown but the separate subtitle placement is not reproduced");
	}

	[Test]
	[Description("A subtitle insert for a column the page already shows changes nothing and does not duplicate it; the warning says the column is already there.")]
	public void Rebase_ShouldNotDuplicateAColumn_WhenTheSubtitleInsertTargetsOneAlreadyShown() {
		// Arrange
		LegacyGridPageSettings settings = Parse("Contact", items: Column("Name"), groups: Column("Account"));
		LegacyOverrideSection section = Section("viewConfigDiff",
			"""[{"operation":"insert","name":"Contact_ListItem_Subtitle_Account","values":{"value":"$Account"},"parentName":"Contact_ListItem","propertyName":"subtitles","index":0}]""");

		// Act
		LegacyOverrideRebaseResult result = Rebase(settings, section);

		// Assert
		result.Settings.SubtitleItems.Should().BeEmpty(because: "the column is already shown, so adding it again would duplicate it");
		result.Settings.GroupItems.Should().ContainSingle(c => c.ColumnName == "Account", because: "it stays where it was");
		result.Warnings.Should().ContainSingle(w => w.Contains("already shows that column"),
			because: "the user learns the override needed no action");
	}

	[Test]
	[Description("A lone removal of a wizard column is said in the wizard's own language — the column leaves its bucket and the analyzer re-derives the page, so bindings are recomputed rather than copied from the override.")]
	public void Rebase_ShouldDropTheColumnFromItsBucket_WhenAColumnElementIsRemovedOnItsOwn() {
		// Arrange
		LegacyGridPageSettings settings = Parse("Contact",
			items: Column("Name"), groups: Column("Account") + ", " + Column("Job", 1));
		LegacyOverrideSection section = Section("viewConfigDiff",
			"""[{"operation":"remove","name":"Contact_ListItem_Body_Account"}]""");

		// Act
		LegacyOverrideRebaseResult result = Rebase(settings, section);

		// Assert
		result.Settings.GroupItems.Select(c => c.ColumnName).Should().Equal(["Job"],
			because: "the removed column leaves the bucket and the rest keep their order");
		result.Outcomes.Should().ContainSingle(o => o.Lane == LegacyOverrideLanes.SourceEdit,
			because: "a column removal is expressible in the wizard model itself");
		result.ViewConfigOperations.Should().BeEmpty(because: "a source edit adds no operation to the page");
	}

	[Test]
	[Description("REAL: `merge Attribute_Items_ModelConfig` (a cache sync rule) is re-pointed onto the converted page's attributes/Items/modelConfig path, because the target dialect addresses the data sections by path rather than by name.")]
	public void Rebase_ShouldRepointAnItemsModelConfigMerge_OntoTheTargetPath() {
		// Arrange
		LegacyGridPageSettings settings = Parse("GlbTerritoryManagerRel", items: Column("GlbManager"));
		LegacyOverrideSection section = Section("viewModelConfigDiff", """
			[{"operation":"merge","name":"Attribute_Items_ModelConfig","values":{"cacheConfig":{"syncRuleName":"GlbTerritoryManagerRelStandardDetail"}}}]
			""");

		// Act
		LegacyOverrideRebaseResult result = Rebase(settings, section);

		// Assert
		result.ViewModelConfigOperations.Should().ContainSingle(because: "the merge has an addressable counterpart");
		Json(result.ViewModelConfigOperations[0]).Should().Be(
			"""{"operation":"merge","path":["attributes","Items","modelConfig"],"values":{"cacheConfig":{"syncRuleName":"GlbTerritoryManagerRelStandardDetail"}}}""",
			because: "the runtime name maps to a path and the values are carried through unchanged");
		result.Outcomes.Should().ContainSingle(o => o.Lane == LegacyOverrideLanes.TargetDelta,
			because: "the operation lands on the converted page, not on the wizard source");
	}

	[Test]
	[Description("REAL: `remove GlbContactStatusActiveFilter` targets an attribute this source never generates, so it is reported with an explanation instead of being applied to something that merely looks similar.")]
	public void Rebase_ShouldReportAnOperation_WhoseTargetThisSourceNeverGenerates() {
		// Arrange
		LegacyGridPageSettings settings = Parse("Contact", items: Column("Name"));
		LegacyOverrideSection section = Section("viewModelConfigDiff",
			"""[{"operation":"remove","name":"GlbContactStatusActiveFilter"}]""");

		// Act
		LegacyOverrideRebaseResult result = Rebase(settings, section);

		// Assert
		result.ViewModelConfigOperations.Should().BeEmpty(because: "an unknown target is never guessed at");
		LegacyOverrideOutcome outcome = result.Outcomes.Single();
		outcome.Lane.Should().Be(LegacyOverrideLanes.Reported, because: "the operation could not be carried");
		outcome.Reason.Should().Contain("GlbContactStatusActiveFilter", because: "the report names the target");
		outcome.Reason.Should().Contain("hand-written", because: "the report explains where such a target comes from");
	}

	[Test]
	[Description("A merge on the list row WINS over the converted values, key by key: it is the later and more specific customisation of the same element, so a key the converter also writes is taken from the override.")]
	public void Rebase_ShouldLetAnOverrideWin_OnEveryKeyItNamesOnTheListRow() {
		// Arrange
		LegacyGridPageSettings settings = Parse("Contact", items: Column("Name"));
		LegacyOverrideSection section = Section("viewConfigDiff",
			"""[{"operation":"merge","name":"Contact_ListItem","values":{"showEmptyValues":false,"title":"$JobTitle"}}]""");

		// Act
		LegacyOverrideRebaseResult result = Rebase(settings, section);

		// Assert
		Json(result.ElementValueOverrides["ListItem"]).Should().Be("""{"showEmptyValues":false,"title":"$PDS_JobTitle"}""",
			because: "every key the override names is carried, and a row binding is re-derived into the PDS_ convention");
		result.RequiredColumns.Should().Equal(["JobTitle"],
			because: "the overriding title binds a column the wizard buckets did not declare");
		result.Outcomes.Single().Lane.Should().Be(LegacyOverrideLanes.TargetDelta,
			because: "the override reaches the converted page instead of being reported");
	}

	[Test]
	[Description("REAL: the default-sort override shipped in MobileCaseGridPageSettingsPortal / MobileFUIAccountGridPageSettingsDefaultWorkplace arrives as an INSERT into Attribute_Items_ModelConfig.sortingConfig, and is re-pointed onto attributes/Items/modelConfig/sortingConfig — the path the shipped designer page really carries, where the template already supplies attributeName.")]
	public void Rebase_ShouldCarryTheDefaultSort_FromAnInsertIntoTheSortingConfig() {
		// Arrange
		LegacyGridPageSettings settings = Parse("Case", items: Column("Number"));
		LegacyOverrideSection section = Section("viewModelConfigDiff", """
			[{"operation":"insert","name":"Attribute_Items_SortingConfig","parentName":"Attribute_Items_ModelConfig","propertyName":"sortingConfig","values":{"default":[{"columnName":"RegisteredOn","direction":"desc"}]}}]
			""");

		// Act
		LegacyOverrideRebaseResult result = Rebase(settings, section);

		// Assert
		result.ViewModelConfigOperations.Should().ContainSingle(because: "the sort default has an addressable counterpart");
		Json(result.ViewModelConfigOperations[0]).Should().Be(
			"""{"operation":"merge","path":["attributes","Items","modelConfig","sortingConfig"],"values":{"default":[{"columnName":"RegisteredOn","direction":"desc"}]}}""",
			because: "only 'default' is set; the template already provides attributeName on that node");
		result.Outcomes.Single().Lane.Should().Be(LegacyOverrideLanes.TargetDelta,
			because: "an insert that SETS content is re-pointed just like a merge");
	}

	[Test]
	[Description("REAL: the row-icon override shipped in MobileFUIContactGridPageSettingsDefaultWorkplace wins over the converted value, its binding is re-derived into the converted page's PDS_ convention, and the column it references is requested for both data sections so the binding is not dead.")]
	public void Rebase_ShouldCarryTheRowIcon_AndRequestTheColumnItsBindingNeeds() {
		// Arrange
		LegacyGridPageSettings settings = Parse("Contact", items: Column("Name"));
		LegacyOverrideSection section = Section("viewConfigDiff",
			"""[{"operation":"merge","name":"Contact_ListItem","values":{"icon":"$Photo"}}]""");

		// Act
		LegacyOverrideRebaseResult result = Rebase(settings, section);

		// Assert
		result.ElementValueOverrides.Should().ContainKey("ListItem", because: "the row is the template element the override targets");
		Json(result.ElementValueOverrides["ListItem"]).Should().Be("""{"icon":"$PDS_Photo"}""",
			because: "$Photo would resolve to nothing on the converted page, which declares PDS_Photo");
		result.RequiredColumns.Should().Equal(["Photo"],
			because: "the icon column is not a wizard column, so it must still be declared like one");
		result.Outcomes.Single().Lane.Should().Be(LegacyOverrideLanes.TargetDelta,
			because: "the icon is carried onto the converted page");
	}

	[Test]
	[Description("REAL: `merge Case_ListItem_Body_Symptoms {label:{visible:false}}` cannot be honoured — a converted row shows a label for every body column and offers no per-column switch — so it is reported and the report says the label stays visible rather than implying the column was lost.")]
	public void Rebase_ShouldReportThatALabelCouldNotBeHidden() {
		// Arrange
		LegacyGridPageSettings settings = Parse("Case", items: Column("Number"), groups: Column("Symptoms"));
		LegacyOverrideSection section = Section("viewConfigDiff",
			"""[{"operation":"merge","name":"Case_ListItem_Body_Symptoms","values":{"label":{"visible":false}}}]""");

		// Act
		LegacyOverrideRebaseResult result = Rebase(settings, section);

		// Assert
		LegacyOverrideOutcome outcome = result.Outcomes.Single();
		outcome.Lane.Should().Be(LegacyOverrideLanes.Reported, because: "the target row has no per-column label switch");
		outcome.Reason.Should().Contain("stays visible",
			because: "the user must learn the label is still shown, not that the column disappeared");
		result.Warnings.Should().ContainSingle(w => w.Contains("does not support controlling a list-row label"),
			because: "the warning reaches guide.constraints, which the caller cannot skip");
		result.Settings.GroupItems.Should().ContainSingle(c => c.ColumnName == "Symptoms",
			because: "an unhonoured label setting must not cost the column itself");
	}

	[Test]
	[Description("REAL: the shipped Case override offers a column in the sort tool. The runtime inserts it into ListSortToolItem.sortOptions; the designer carries the same thing as a SortButton.sortItems entry shaped { attributeName, caption } with the RAW column name — the shape a designer-authored page really uses.")]
	public void Rebase_ShouldCarryASortOption_IntoSortButtonSortItems() {
		// Arrange — Symptoms IS a page column (so its caption is known); RegisteredOn is not.
		LegacyGridPageSettings settings = Parse("Case", items: Column("Number"), groups: Caption("Symptoms", "Description"));
		LegacyOverrideSection section = Section("viewConfigDiff", """
			[{"operation":"insert","name":"Case_SortOptions_RegisteredOn","propertyName":"sortOptions","parentName":"Case_ListScreen_Tools_ListSortToolItem","values":{"property":"RegisteredOn"}},
			 {"operation":"insert","name":"Case_SortOptions_Symptoms","propertyName":"sortOptions","parentName":"Case_ListScreen_Tools_ListSortToolItem","values":{"property":"Symptoms"}}]
			""");

		// Act
		LegacyOverrideRebaseResult result = Rebase(settings, section);

		// Assert
		Json(result.ElementValueOverrides["SortButton"]).Should().Be(
			"""{"sortItems":[{"attributeName":"RegisteredOn"},{"attributeName":"Symptoms","caption":"Description"}]}""",
			because: "both options accumulate into ONE merge, attributeName is the raw column name — no PDS_ prefix — and an unresolved caption is OMITTED rather than filled with the machine name");
		result.Outcomes.Should().OnlyContain(o => o.Lane == LegacyOverrideLanes.TargetDelta,
			because: "both are carried onto the converted page");
		result.RequiredColumns.Should().Contain("RegisteredOn",
			because: "a column offered for sorting must be declared in the data sections or the sort cannot load it");
		result.Warnings.Should().ContainSingle(w => w.Contains("RegisteredOn") && w.Contains("no caption could be resolved"),
			because: "only the column with no resolvable caption needs a warning");
	}

	[Test]
	[Description("When the page does not carry the sorted column, its caption is taken from the OBJECT — the real display label — and no warning is needed.")]
	public void Rebase_ShouldTakeASortCaptionFromTheObject_WhenThePageDoesNotCarryTheColumn() {
		// Arrange
		LegacyGridPageSettings settings = Parse("Case", items: Column("Number"));
		LegacyOverrideSection section = Section("viewConfigDiff",
			"""[{"operation":"insert","name":"Case_SortOptions_RegisteredOn","propertyName":"sortOptions","parentName":"Case_ListScreen_Tools_ListSortToolItem","values":{"property":"RegisteredOn"}}]""");
		var captions = new Dictionary<string, string> { ["RegisteredOn"] = "Registered on" };

		// Act
		LegacyOverrideRebaseResult result = LegacyOverrideRebaser.Rebase(settings, [section],
			LegacyRuntimeNameOracle.Build(settings, NameSet), Template, NameSet, captions);

		// Assert
		Json(result.ElementValueOverrides["SortButton"]).Should().Be(
			"""{"sortItems":[{"attributeName":"RegisteredOn","caption":"Registered on"}]}""",
			because: "the object's own column caption is the real display label for the sort menu");
		result.Warnings.Should().BeEmpty(because: "nothing had to be guessed at, so there is nothing to warn about");
	}

	[Test]
	[Description("A designer target the TARGET TEMPLATE does not declare is reported, not authored: a merge onto a name the template does not carry writes nothing and no metadata validation catches it.")]
	public void Rebase_ShouldReportADesignerTarget_TheTemplateDoesNotDeclare() {
		// Arrange — a template whose element inventory lacks the floating action.
		LegacyGridPageSettings settings = Parse("Case", items: Column("Number"));
		LegacyOverrideSection section = Section("viewConfigDiff",
			"""[{"operation":"remove","name":"ViewConfig","properties":["floatAction"]}]""");
		var withoutFloatAction = new HashSet<string>(["Scaffold", "List", "ListItem", "FolderTreeActions"]);

		// Act
		LegacyOverrideRebaseResult result = LegacyOverrideRebaser.Rebase(settings, [section],
			LegacyRuntimeNameOracle.Build(settings, NameSet), Template, NameSet, null, withoutFloatAction);

		// Assert
		result.ViewConfigOperations.Should().BeEmpty(because: "authoring a removal of a name the template lacks does nothing");
		result.Outcomes.Single().Lane.Should().Be(LegacyOverrideLanes.Reported, because: "the target does not exist");
		result.Warnings.Should().ContainSingle(w => w.Contains("does not declare an element named 'CreateRecordButton'"),
			because: "the report must name the element the template is missing");
	}

	[Test]
	[Description("When the template DOES declare the target the operation is carried exactly as before, so the verification only ever removes wrong output — it never blocks correct output.")]
	public void Rebase_ShouldCarryTheOperation_WhenTheTemplateDeclaresTheTarget() {
		// Arrange
		LegacyGridPageSettings settings = Parse("Case", items: Column("Number"));
		LegacyOverrideSection section = Section("viewConfigDiff",
			"""[{"operation":"remove","name":"ViewConfig","properties":["floatAction"]}]""");
		var withFloatAction = new HashSet<string>(["Scaffold", "ListItem", "FolderTreeActions", "CreateRecordButton"]);

		// Act
		LegacyOverrideRebaseResult result = LegacyOverrideRebaser.Rebase(settings, [section],
			LegacyRuntimeNameOracle.Build(settings, NameSet), Template, NameSet, null, withFloatAction);

		// Assert
		Json(result.ViewConfigOperations.Single()).Should().Be("""{"operation":"remove","name":"CreateRecordButton"}""",
			because: "a verified target behaves exactly as it did before the check existed");
		result.Warnings.Should().BeEmpty(because: "nothing was wrong, so nothing is warned about");
	}

	[Test]
	[Description("With no template inventory (the stand could not be read) the names go UNVERIFIED and operations are still carried — an unreadable template must not silently strip a customisation.")]
	public void Rebase_ShouldStillCarryOperations_WhenTheTemplateCouldNotBeRead() {
		// Arrange
		LegacyGridPageSettings settings = Parse("Case", items: Column("Number"));
		LegacyOverrideSection section = Section("viewConfigDiff",
			"""[{"operation":"remove","name":"ViewConfig","properties":["floatAction"]}]""");

		// Act — templateElements omitted, exactly as when the probe fails.
		LegacyOverrideRebaseResult result = Rebase(settings, section);

		// Assert
		result.ViewConfigOperations.Should().ContainSingle(
			because: "an unreadable template degrades to trusting the table, it does not drop the override");
	}

	[Test]
	[Description("With no supported sections nothing is rebased and the wizard settings come back untouched, so a plain source is unaffected by the override pass existing.")]
	public void Rebase_ShouldBeANoOp_WhenThereAreNoSupportedSections() {
		// Arrange
		LegacyGridPageSettings settings = Parse("Contact", items: Column("Name"), groups: Column("Account"));
		var unsupported = new LegacyOverrideSection("diffV2", 1, null, false, "not supported");

		// Act
		LegacyOverrideRebaseResult empty = Rebase(settings);
		LegacyOverrideRebaseResult refused = Rebase(settings, unsupported);

		// Assert
		empty.Settings.Should().BeSameAs(settings, because: "nothing was edited");
		empty.Outcomes.Should().BeEmpty(because: "there was nothing to report on");
		refused.ViewConfigOperations.Should().BeEmpty(because: "an unsupported section is never processed");
		refused.Outcomes.Should().BeEmpty(because: "the classifier already reported it; the rebaser does not double-report");
	}

	[Test]
	[Description("Rebasing the same source and overrides twice produces the same edits, target operations and outcomes, so re-running the conversion is safe.")]
	public void Rebase_ShouldBeDeterministic_AcrossRepeatedRuns() {
		// Arrange
		LegacyGridPageSettings settings = Parse("Contact", items: Column("Name"), groups: Column("Account"));
		string ops = """
			[{"operation":"remove","name":"ViewConfig","properties":["floatAction"]},
			 {"operation":"remove","name":"Contact_ListItem_Body_Account"}]
			""";

		// Act
		LegacyOverrideRebaseResult first = Rebase(settings, Section("viewConfigDiff", ops));
		LegacyOverrideRebaseResult second = Rebase(settings, Section("viewConfigDiff", ops));

		// Assert
		first.ViewConfigOperations.Select(Json).Should().Equal(second.ViewConfigOperations.Select(Json),
			because: "the same source and overrides must produce the same page");
		first.Outcomes.Should().Equal(second.Outcomes, because: "the report must be reproducible too");
		first.Settings.GroupItems.Should().BeEmpty(because: "the column removal applied on both runs alike");
	}
}
