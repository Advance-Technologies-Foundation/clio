using System.Linq;
using Clio.Command.McpServer.Tools.MobilePageConverter;
using Clio.Command.McpServer.Tools.MobilePageConverter.Legacy;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer.Tools.MobilePageConverter.Legacy;

/// <summary>
/// Unit tests for the runtime NAME ORACLE (ENG-95733). The expectations are taken from the mobile runtime's own
/// converter — <c>GridPageConverter.java</c> / <c>BaseScreenConverter.java</c> — which is the reference for the
/// dialect embedded <c>viewConfigDiff</c> operations are written against:
/// <list type="bullet">
///   <item>names are joined with <c>_</c> (<c>concatProperties</c>), except the row name which is
///   <c>&lt;Entity&gt;_List</c> + <c>Item</c> with NO separator;</item>
///   <item>the <c>subtitleItems</c> bucket produces <c>&lt;Entity&gt;_ListItem_Subtitle_&lt;Column&gt;</c> in the
///   <c>subtitles</c> slot, the <c>groupItems</c> bucket produces <c>&lt;Entity&gt;_ListItem_Body_&lt;Column&gt;</c>
///   in the <c>body</c> slot, and the <c>items</c> (title) bucket produces NO element of its own;</item>
///   <item>a dotted column keeps its dots in the NAME (the binding is what replaces them with <c>_</c>).</item>
/// </list>
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class LegacyRuntimeNameOracleTests {

	/// <summary>The shipped runtime-name table — the same data the tool reads at run time.</summary>
	private static readonly MobileLegacyRuntimeNameSet Shipped =
		WebToMobilePageConversionRulesCatalog.LoadBundled().MobileLegacyRuntimeNames?.GridPage;

	/// <summary>Builds a merged settings node the way the reader hands it over (wizard values hoisted onto the item).</summary>
	private static LegacyGridPageSettings Parse(string entity, string items = "", string subtitles = "", string groups = "") =>
		LegacyGridPageSettingsParser.Parse(JObject.Parse($$"""
			{
			  "name": "settings", "settingsType": "GridPage", "entitySchemaName": "{{entity}}",
			  "items": [{{items}}], "subtitleItems": [{{subtitles}}], "groupItems": [{{groups}}]
			}
			"""));

	private static string Column(string columnName, int row = 0) =>
		$$"""{ "name": "id-{{columnName}}", "row": {{row}}, "content": "{{columnName}}", "columnName": "{{columnName}}", "dataValueType": 1 }""";

	private static LegacyRuntimeNameInventory Build(LegacyGridPageSettings settings) =>
		LegacyRuntimeNameOracle.Build(settings, Shipped);

	[Test]
	[Description("The shipped conversion rules carry a runtime-name table for GridPage; without it the override pass has no addressing space to work in.")]
	public void ShippedRules_ShouldCarryRuntimeNameTable_ForGridPage() {
		// Arrange & Act
		MobileLegacyRuntimeNameSet table = Shipped;

		// Assert
		table.Should().NotBeNull(because: "embedded overrides are re-pointed through a data table, not through code");
		table!.Anchors.Should().NotBeEmpty(because: "an empty table would silently switch the override pass off");
		table.Anchors.Should().OnlyContain(a => !string.IsNullOrWhiteSpace(a.Role) && !string.IsNullOrWhiteSpace(a.Pattern),
			because: "an anchor without a role or a pattern cannot resolve anything");
	}

	[Test]
	[Description("The fixed scaffold names the runtime generates for any list source are all enumerated, spelled exactly as GridPageConverter builds them (note <Entity>_ListItem has no separator before 'Item').")]
	public void Build_ShouldEnumerateTheFixedScaffold_AsTheRuntimeSpellsIt() {
		// Arrange
		LegacyGridPageSettings settings = Parse("Contact");

		// Act
		LegacyRuntimeNameInventory inventory = Build(settings);

		// Assert
		inventory.Names.Should().Contain([
			"ViewConfig", "Contact_List", "Contact_ListItem", "Contact_ListItem_Body_Column",
			"Contact_Action_StartSearch_Button", "PDS_SearchFilter", "Contact_FloatActionButton",
			"Contact_ListScreen_Tools_FilterGroupButton", "Contact_ListScreen_Tools_ListSortToolItem",
			"Contact_ListScreen_Tools_ListFolderFilter", "QuickFilterGroup"
		], because: "these are the viewConfig elements GridPageConverter emits for every list page");
		inventory.Names.Should().Contain([
			"Attribute_Items", "Attribute_Items_ModelConfig", "Attribute_Items_ViewModelConfig",
			"Attribute_Items_ViewModelConfig_Attributes", "Attribute_ItemsSorting",
			"ModelConfig", "DataSources", "PDS", "PDS_Config", "PDS_Attributes"
		], because: "the data sections carry their own runtime names and overrides address those too");
	}

	[Test]
	[Description("A subtitleItems column becomes <Entity>_ListItem_Subtitle_<Column> in the subtitles slot; a groupItems column becomes <Entity>_ListItem_Body_<Column> in the body slot; the title column produces no element of its own.")]
	public void Build_ShouldMapEachWizardBucket_ToTheRuntimeElementItGenerates() {
		// Arrange
		LegacyGridPageSettings settings = Parse("Contact",
			items: Column("Name"), subtitles: Column("Job"), groups: Column("Account"));

		// Act
		LegacyRuntimeNameInventory inventory = Build(settings);
		LegacyRuntimeAnchor subtitle = inventory.Resolve("Contact_ListItem_Subtitle_Job");
		LegacyRuntimeAnchor body = inventory.Resolve("Contact_ListItem_Body_Account");

		// Assert
		subtitle.Should().NotBeNull(because: "a subtitleItems column is addressable by the name the runtime gave it");
		subtitle!.Role.Should().Be(LegacyRuntimeRoles.SubtitleField, because: "the bucket decides the role");
		subtitle.Bucket.Should().Be("subtitleItems", because: "the anchor carries the bucket the column came from");
		subtitle.Slot.Should().Be("subtitles", because: "the runtime inserts it into the row's subtitles slot");
		subtitle.ColumnPath.Should().Be("Job", because: "the column path is recovered from the name");
		subtitle.FromInventory.Should().BeTrue(because: "the column really is in this source");

		body!.Role.Should().Be(LegacyRuntimeRoles.BodyField, because: "groupItems render in the row body");
		body.Bucket.Should().Be("groupItems", because: "the anchor carries the bucket the column came from");
		body.Slot.Should().Be("body", because: "the runtime inserts it into the row's body slot");
		body.ColumnPath.Should().Be("Account", because: "the column path is recovered from the name");

		inventory.Names.Should().NotContain(n => n.Contains("_Name", System.StringComparison.Ordinal),
			because: "the title column is merged onto the row as a 'title' value and gets no element name of its own");
	}

	[Test]
	[Description("The literal body-column marker resolves as the marker, not as a body field for a column that happens to be called 'Column' — literal templates are matched before templates carrying {column}.")]
	public void Resolve_ShouldPreferTheLiteralMarker_OverAColumnTemplateThatCouldAlsoMatch() {
		// Arrange
		LegacyGridPageSettings settings = Parse("Contact", groups: Column("Account"));

		// Act
		LegacyRuntimeAnchor marker = Build(settings).Resolve("Contact_ListItem_Body_Column");

		// Assert
		marker.Should().NotBeNull(because: "the runtime emits this marker when the last group column goes");
		marker!.Role.Should().Be(LegacyRuntimeRoles.BodyColumnMarker,
			because: "an exact template wins over a parameterised one that would read 'Column' as a column name");
		marker.ColumnPath.Should().BeNull(because: "the marker is not column-bound");
	}

	[Test]
	[Description("A dotted column path keeps its dots in the generated NAME (GridPageConverter only underscores the BINDING), so an override addressing a related column resolves back to the full path.")]
	public void Resolve_ShouldKeepDots_InTheNameOfARelatedColumn() {
		// Arrange
		LegacyGridPageSettings settings = Parse("Contact", groups: Column("Account.CreatedBy.Name"));

		// Act
		LegacyRuntimeAnchor anchor = Build(settings).Resolve("Contact_ListItem_Body_Account.CreatedBy.Name");

		// Assert
		anchor.Should().NotBeNull(because: "the runtime writes the dotted path into the element name verbatim");
		anchor!.ColumnPath.Should().Be("Account.CreatedBy.Name",
			because: "the whole dotted path is the column, not just its last segment");
		anchor.FromInventory.Should().BeTrue(because: "the column is present in this source's groupItems");
	}

	[Test]
	[Description("A name that matches a template but names a column this source does not carry still resolves — with FromInventory false, so it can be reported as pointing at something the conversion did not produce instead of being applied.")]
	public void Resolve_ShouldFlagAnchorsOutsideTheSource_RatherThanTreatingThemAsConversionInput() {
		// Arrange
		LegacyGridPageSettings settings = Parse("Contact", groups: Column("Account"));

		// Act
		LegacyRuntimeAnchor stranger = Build(settings).Resolve("Contact_ListItem_Body_SomethingElse");

		// Assert
		stranger.Should().NotBeNull(because: "the template explains what the name MEANS even when the column is absent");
		stranger!.Role.Should().Be(LegacyRuntimeRoles.BodyField, because: "the meaning comes from the template");
		stranger.ColumnPath.Should().Be("SomethingElse", because: "the column is parsed out of the name");
		stranger.FromInventory.Should().BeFalse(
			because: "this source produced no such element, and applying an override for it would be a guess");
	}

	[Test]
	[Description("A name no template explains resolves to null, so the operation is reported rather than half-applied.")]
	public void Resolve_ShouldReturnNull_WhenNoTemplateExplainsTheName() {
		// Arrange
		LegacyGridPageSettings settings = Parse("Contact", groups: Column("Account"));

		// Act & Assert
		Build(settings).Resolve("SomeHandWrittenElement").Should().BeNull(
			because: "an unexplained target must never be guessed at");
		Build(settings).Resolve("Account_ListItem").Should().BeNull(
			because: "the entity prefix is part of the template — another entity's element is not ours");
	}

	[Test]
	[Description("REAL CORPUS: the override shipped in GlbMobileFUIContactGridPageSettingsFieldForceWorkplace decodes into 'move column Account from groupItems into subtitleItems', which is a wizard-model edit rather than a JSON patch.")]
	public void Resolve_ShouldDecodeARealShippedOverride_IntoWizardBucketMoves() {
		// Arrange — the source carries Account as a group column, and the override re-points it into subtitles.
		LegacyGridPageSettings settings = Parse("Contact", items: Column("Name"), groups: Column("Account"));
		LegacyRuntimeNameInventory inventory = Build(settings);

		// Act
		LegacyRuntimeAnchor removed = inventory.Resolve("Contact_ListItem_Body_Account");
		LegacyRuntimeAnchor inserted = inventory.Resolve("Contact_ListItem_Subtitle_Account");
		LegacyRuntimeAnchor parent = inventory.Resolve("Contact_ListItem");
		LegacyRuntimeAnchor root = inventory.Resolve("ViewConfig");

		// Assert
		removed!.Bucket.Should().Be("groupItems", because: "the removed element is the group-bucket rendering of Account");
		removed.FromInventory.Should().BeTrue(because: "this conversion really did produce that element");
		inserted!.Bucket.Should().Be("subtitleItems", because: "the inserted element is the subtitle-bucket rendering");
		inserted.ColumnPath.Should().Be(removed.ColumnPath, because: "the pair is one column moving between buckets");
		parent!.Role.Should().Be(LegacyRuntimeRoles.ListRow, because: "the operation's parentName is the row");
		root!.Role.Should().Be(LegacyRuntimeRoles.ScreenRoot,
			because: "the other shipped override removes ViewConfig.floatAction, so the root must resolve too");
	}

	[Test]
	[Description("Without a runtime-name table the oracle yields an empty inventory that resolves nothing, so the override pass switches off by data rather than by code.")]
	public void Build_ShouldYieldAnEmptyInventory_WhenTheTableIsAbsent() {
		// Arrange
		LegacyGridPageSettings settings = Parse("Contact", groups: Column("Account"));

		// Act
		LegacyRuntimeNameInventory fromNull = LegacyRuntimeNameOracle.Build(settings, null);
		LegacyRuntimeNameInventory fromEmpty = LegacyRuntimeNameOracle.Build(settings, new MobileLegacyRuntimeNameSet());

		// Assert
		fromNull.IsEmpty.Should().BeTrue(because: "no table means no addressing space");
		fromEmpty.IsEmpty.Should().BeTrue(because: "an empty anchor list is the same as no table");
		fromNull.Resolve("Contact_ListItem").Should().BeNull(because: "nothing can be resolved without templates");
	}

	[Test]
	[Description("The inventory is a deterministic function of the parsed source: the same settings produce the same names in the same order.")]
	public void Build_ShouldBeDeterministic_ForTheSameSource() {
		// Arrange
		LegacyGridPageSettings first = Parse("Contact", items: Column("Name"), subtitles: Column("Job"), groups: Column("Account"));
		LegacyGridPageSettings second = Parse("Contact", items: Column("Name"), subtitles: Column("Job"), groups: Column("Account"));

		// Act
		string[] a = [.. Build(first).Names];
		string[] b = [.. Build(second).Names];

		// Assert
		a.Should().Equal(b, because: "re-running the conversion on the same source must produce the same result");
	}
}
