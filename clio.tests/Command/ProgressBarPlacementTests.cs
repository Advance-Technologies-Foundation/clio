using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ProgressBarPlacementTests {

	private const string Entity = "UsrOrder";

	internal static PageBundleInfo Bundle(JsonArray viewConfig, string primaryEntity = Entity,
		string template = "PageWithTabsFreedomTemplate") =>
		new() {
			Name = "UsrOrders_FormPage",
			ViewConfig = viewConfig,
			ModelConfig = primaryEntity is null
				? new JsonObject()
				: new JsonObject {
					["primaryDataSourceName"] = "PDS",
					["dataSources"] = new JsonObject {
						["PDS"] = new JsonObject {
							["type"] = "crt.EntityDataSource",
							["config"] = new JsonObject { ["entitySchemaName"] = primaryEntity }
						}
					}
				},
			Schemas = [
				new PageSchemaChainEntry { SchemaName = "UsrOrders_FormPage" },
				new PageSchemaChainEntry { SchemaName = template },
				new PageSchemaChainEntry { SchemaName = "BasePageTemplate" }
			]
		};

	internal static JsonArray TabbedView(params JsonObject[] extraItems) {
		JsonArray items = [];
		foreach (JsonObject item in extraItems) {
			items.Add(item);
		}
		return [
			new JsonObject {
				["name"] = "MainContainer",
				["type"] = "crt.FlexContainer",
				["items"] = items
			}
		];
	}

	[Test]
	[Description("The fragment is the application wizard's two inserts: a flex container at index 0 of MainContainer, then the stage bar inside it with the literal entity name and the bar's own defaults: saveOnChange false (a click only sets the field) and askUserToChangeSchema true.")]
	public void BuildViewConfigDiff_ShouldCarryTheWizardLayoutAndTheBarDefaults_WhenNoSuffix() {
		// Arrange
		const string suffix = "";

		// Act
		JsonArray diff = ProgressBarFragment.BuildViewConfigDiff(Entity, suffix);

		// Assert
		diff.Should().HaveCount(2, because: "the bar needs exactly its container and itself");
		JsonObject container = diff[0]!.AsObject();
		container["operation"]!.GetValue<string>().Should().Be("insert", because: "both elements are new");
		container["name"]!.GetValue<string>().Should().Be("ProgressBarContainer", because: "the wizard's name is kept when free");
		container["parentName"]!.GetValue<string>().Should().Be("MainContainer", because: "the bar sits at the top of the main container");
		container["propertyName"]!.GetValue<string>().Should().Be("items", because: "an insert without a slot cannot render");
		container["index"]!.GetValue<int>().Should().Be(0, because: "the bar is the first thing on the page");
		JsonObject containerValues = container["values"]!.AsObject();
		containerValues["type"]!.GetValue<string>().Should().Be("crt.FlexContainer", because: "the wizard writes a flex container");
		containerValues["direction"]!.GetValue<string>().Should().Be("column", because: "these are the wizard's values");
		containerValues["fitContent"]!.GetValue<bool>().Should().BeTrue(because: "these are the wizard's values");
		containerValues["gap"]!.GetValue<string>().Should().Be("small", because: "these are the wizard's values");
		containerValues["wrap"]!.GetValue<string>().Should().Be("nowrap", because: "these are the wizard's values");
		JsonObject bar = diff[1]!.AsObject();
		bar["name"]!.GetValue<string>().Should().Be("ProgressBar", because: "the wizard's name is kept when free");
		bar["parentName"]!.GetValue<string>().Should().Be("ProgressBarContainer", because: "the bar goes inside the new container");
		JsonObject barValues = bar["values"]!.AsObject();
		barValues["type"]!.GetValue<string>().Should().Be("crt.EntityStageProgressBar", because: "that is the stage bar component");
		barValues["entityName"]!.GetValue<string>().Should().Be(Entity, because: "the bar finds its case by the object name");
		barValues["saveOnChange"]!.GetValue<bool>().Should().BeFalse(
			because: "the bar's default, kept by the page template and most product pages: the user saves");
		barValues["askUserToChangeSchema"]!.GetValue<bool>().Should().BeTrue(because: "the bar's default");
		barValues.Select(pair => pair.Key).Should().BeEquivalentTo(
			["type", "entityName", "saveOnChange", "askUserToChangeSchema"],
			because: "nothing else is written on the bar");
	}

	[Test]
	[Description("Asked for a save on click, the bar carries saveOnChange true in both the diff and the append body; nothing else changes.")]
	public void BuildViewConfigDiff_ShouldSaveOnClick_WhenAskedTo() {
		// Arrange
		const bool saveOnChange = true;

		// Act
		JsonArray diff = ProgressBarFragment.BuildViewConfigDiff(Entity, string.Empty, saveOnChange);
		string body = ProgressBarFragment.BuildBody("UsrOrders_FormPage", Entity, string.Empty, saveOnChange);

		// Assert
		JsonObject barValues = diff[1]!["values"]!.AsObject();
		barValues["saveOnChange"]!.GetValue<bool>().Should().BeTrue(because: "the caller asked for a save on click");
		barValues["askUserToChangeSchema"]!.GetValue<bool>().Should().BeTrue(because: "only the save mode is the caller's");
		body.Should().Contain("\"saveOnChange\":true", because: "the append body is built from the same diff");
	}

	[Test]
	[Description("No key the runtime preprocessor creates and overwrites on every open is written anywhere in the fragment.")]
	public void BuildBody_ShouldNotCarryAnyForbiddenKey() {
		// Arrange
		string body = ProgressBarFragment.BuildBody("UsrOrders_FormPage", Entity, string.Empty);

		// Act
		string[] present = ProgressBarFragment.ForbiddenKeys
			.Where(key => body.Contains($"\"{key}\"", System.StringComparison.Ordinal))
			.ToArray();

		// Assert
		ProgressBarFragment.ForbiddenKeys.Should().Contain(
			["recordId", "value", "stages", "loadData", "stageChanged", "setAllowedStages",
				"changeToAppropriateEntityStageSchema"],
			because: "the forbidden set is the agreed one");
		present.Should().BeEmpty(because: "the runtime wires these itself and a written one would be overwritten or fight it");
	}

	[Test]
	[Description("The fragment is a marker-wrapped append body for the page itself: update-page append refuses a bare array, and the view, model and handler sections stay empty.")]
	public void BuildBody_ShouldBeAMarkerWrappedDefineBody_WithOnlyTheViewDiff() {
		// Arrange
		const string page = "UsrOrders_FormPage";

		// Act
		string body = ProgressBarFragment.BuildBody(page, Entity, "_0a1b2c3d");

		// Assert
		body.Should().StartWith($"define(\"{page}\"", because: "an append body is an AMD module of the page");
		body.Should().Contain("/**SCHEMA_VIEW_CONFIG_DIFF*/[", because: "the inserts sit between the view-config markers");
		body.Should().Contain("/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/",
			because: "the view model is untouched");
		body.Should().Contain("/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/",
			because: "the model is untouched");
		body.Should().Contain("/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/", because: "no handler is added");
		body.Should().Contain("\"ProgressBarContainer_0a1b2c3d\"", because: "the suffix renames the container");
		body.Should().Contain("\"ProgressBar_0a1b2c3d\"", because: "the suffix renames the bar too");
		body.Should().Contain("\"parentName\":\"ProgressBarContainer_0a1b2c3d\"",
			because: "the bar must point at the renamed container");
	}

	[Test]
	[Description("A tabbed record page of the case object with a flex MainContainer and no bar is ready: no refusal, no existing bar, no name collision, and the template is read from the chain.")]
	public void Check_ShouldAllowTheBar_WhenTheTabbedPageIsForTheObject() {
		// Arrange
		PageBundleInfo bundle = Bundle(TabbedView());

		// Act
		ProgressBarPlacementCheck check = ProgressBarPlacement.Check(bundle, Entity);

		// Assert
		check.ErrorCode.Should().BeNull(because: "nothing about the page refuses the bar");
		check.BarAlreadyPresent.Should().BeFalse(because: "the page has no bar");
		check.NamesTaken.Should().BeFalse(because: "the page does not use the fragment's names");
		check.Template.Should().Be("PageWithTabsFreedomTemplate", because: "the nearest template in the chain is reported");
		ProgressBarPlacement.IsTabbedTemplate(check.Template).Should().BeTrue(because: "that template is a tabbed one");
	}

	[Test]
	[Description("A page that already carries a stage bar is reported as such, so nothing is written to it - pages on PageWithTabsAndProgressBarTemplate land here.")]
	public void Check_ShouldReportTheBar_WhenThePageAlreadyHasOne() {
		// Arrange
		PageBundleInfo bundle = Bundle(TabbedView(new JsonObject {
			["name"] = "StageBar", ["type"] = "crt.EntityStageProgressBar"
		}));

		// Act
		ProgressBarPlacementCheck check = ProgressBarPlacement.Check(bundle, Entity);

		// Assert
		check.BarAlreadyPresent.Should().BeTrue(because: "a second bar would show the same stages twice");
		check.ErrorCode.Should().BeNull(because: "a present bar is not a refusal");
	}

	[Test]
	[Description("A mini page keeps its fields in a grid container, so the bar has no MainContainer flex container to go into: placement-unsupported.")]
	public void Check_ShouldRefusePlacement_WhenMainContainerIsNotAFlexContainer() {
		// Arrange
		JsonArray mini = [new JsonObject { ["name"] = "MainContainer", ["type"] = "crt.GridContainer" }];

		// Act
		ProgressBarPlacementCheck check = ProgressBarPlacement.Check(Bundle(mini), Entity);

		// Assert
		check.ErrorCode.Should().Be(DynamicCaseErrorCodes.PlacementUnsupported,
			because: "inserting into a grid container would land the bar in a cell");
		check.ErrorMessage.Should().Contain("crt.GridContainer", because: "the refusal names what it found");
	}

	[Test]
	[Description("A page without any MainContainer is refused the same way.")]
	public void Check_ShouldRefusePlacement_WhenThereIsNoMainContainer() {
		// Arrange
		JsonArray view = [new JsonObject { ["name"] = "Root", ["type"] = "crt.FlexContainer" }];

		// Act
		ProgressBarPlacementCheck check = ProgressBarPlacement.Check(Bundle(view), Entity);

		// Assert
		check.ErrorCode.Should().Be(DynamicCaseErrorCodes.PlacementUnsupported,
			because: "the insert's parent would not resolve");
	}

	[Test]
	[Description("A page whose primary data source is another object is refused: the bar would show one record's case and write another record's column.")]
	public void Check_ShouldRefuse_WhenThePrimaryDataSourceIsAnotherObject() {
		// Arrange
		PageBundleInfo bundle = Bundle(TabbedView(), primaryEntity: "Account");

		// Act
		ProgressBarPlacementCheck check = ProgressBarPlacement.Check(bundle, Entity);

		// Assert
		check.ErrorCode.Should().Be(DynamicCaseErrorCodes.PageEntityMismatch,
			because: "entityName must be the page's primary data source entity");
		check.ErrorMessage.Should().Contain("Account", because: "the refusal names the object the page is for");
	}

	[Test]
	[Description("A page that declares no primary data source cannot be confirmed to be the object's record page, so it is refused too.")]
	public void Check_ShouldRefuse_WhenThePageDeclaresNoPrimaryDataSource() {
		// Arrange
		PageBundleInfo bundle = Bundle(TabbedView(), primaryEntity: null);

		// Act
		ProgressBarPlacementCheck check = ProgressBarPlacement.Check(bundle, Entity);

		// Assert
		check.ErrorCode.Should().Be(DynamicCaseErrorCodes.PageEntityMismatch,
			because: "a list or blank page is not the object's record page");
		check.ErrorMessage.Should().Contain("declares no primary data source", because: "the refusal says why");
	}

	[Test]
	[Description("A page that already uses one of the fragment's element names is flagged, because append REPLACES an entry with the same operation and name.")]
	public void Check_ShouldFlagTheNames_WhenThePageAlreadyUsesThem() {
		// Arrange
		PageBundleInfo bundle = Bundle(TabbedView(new JsonObject {
			["name"] = "ProgressBarContainer", ["type"] = "crt.FlexContainer"
		}));

		// Act
		ProgressBarPlacementCheck check = ProgressBarPlacement.Check(bundle, Entity);

		// Assert
		check.NamesTaken.Should().BeTrue(because: "reusing the name would overwrite the page's own element");
		check.ErrorCode.Should().BeNull(because: "a taken name is solved by a suffix, not refused");
	}

	[TestCase("PageWithTabsFreedomTemplate", true)]
	[TestCase("PageWithTabsAndProgressBarTemplate", true)]
	[TestCase("BaseMiniPageTemplate", false)]
	[TestCase(null, false)]
	[Description("Only the tabbed template family counts as one the product adds the bar to.")]
	public void IsTabbedTemplate_ShouldRecogniseTheTabbedFamily(string template, bool expected) {
		// Arrange & Act
		bool tabbed = ProgressBarPlacement.IsTabbedTemplate(template);

		// Assert
		tabbed.Should().Be(expected, because: "the note about the layout is raised for any other template");
	}

	[Test]
	[Description("A body stored in the full form (create-app's pages) gets the two inserts appended to its viewConfigDiff, and everything outside that marker pair, the full viewModelConfig and modelConfig included, is kept exactly as stored.")]
	public void TryInsertIntoFullBody_ShouldAppendTheTwoInserts_AndKeepTheRestOfTheBody() {
		// Arrange
		string body = FullFormBody(Entity);

		// Act
		bool inserted = ProgressBarFragment.TryInsertIntoFullBody(body, Entity, string.Empty, out string updated,
			out string error);

		// Assert
		inserted.Should().BeTrue(because: "the body has a viewConfigDiff array to insert into");
		error.Should().BeNull(because: "nothing went wrong");
		NamesIn(updated).Should().Equal(["UsrName", "ProgressBarContainer", "ProgressBar"],
			because: "the stored items stay first and the two inserts follow, as append would place them");
		JsonNode.Parse(SectionOf(updated, "SCHEMA_VIEW_CONFIG_DIFF"))!.AsArray().Skip(1).Select(node => node!.ToJsonString())
			.Should().Equal(ProgressBarFragment.BuildViewConfigDiff(Entity, string.Empty).Select(node => node!.ToJsonString()),
				because: "the inserts are exactly the fragment's, so page.fragment names what was written");
		WithoutSection(updated, "SCHEMA_VIEW_CONFIG_DIFF").Should().Be(WithoutSection(body, "SCHEMA_VIEW_CONFIG_DIFF"),
			because: "a replace must not change anything the bar does not need");
	}

	[Test]
	[Description("A suffixed pair is inserted under the suffixed names, the same ones the fragment carries.")]
	public void TryInsertIntoFullBody_ShouldUseTheSuffixedNames_WhenASuffixIsGiven() {
		// Arrange
		string body = FullFormBody(Entity);

		// Act
		ProgressBarFragment.TryInsertIntoFullBody(body, Entity, "_0a1b2c3d", out string updated, out _);

		// Assert
		NamesIn(updated).Should().Equal(["UsrName", "ProgressBarContainer_0a1b2c3d", "ProgressBar_0a1b2c3d"],
			because: "both names carry the suffix the plan chose");
	}

	[Test]
	[Description("A body with no viewConfigDiff marker pair, or one whose viewConfigDiff is not a JSON array, is not touched and the reason is returned.")]
	[TestCase("define(\"P\", function() { return { viewModelConfig: /**SCHEMA_VIEW_MODEL_CONFIG*/{}/**SCHEMA_VIEW_MODEL_CONFIG*/ }; });", "no viewConfigDiff")]
	[TestCase("define(\"P\", function() { return { viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/{ \"a\": 1 }/**SCHEMA_VIEW_CONFIG_DIFF*/ }; });", "not a JSON array")]
	public void TryInsertIntoFullBody_ShouldRefuse_WhenTheViewConfigDiffCannotTakeTheInserts(string body, string reason) {
		// Arrange & Act
		bool inserted = ProgressBarFragment.TryInsertIntoFullBody(body, Entity, string.Empty, out string updated,
			out string error);

		// Assert
		inserted.Should().BeFalse(because: "there is no array to insert into");
		updated.Should().BeNull(because: "no body is offered for a write");
		error.Should().Contain(reason, because: "the refusal names what is wrong with the stored body");
	}

	/// <summary>A record page as create-app stores it: a viewConfigDiff array next to a FULL viewModelConfig and
	/// modelConfig, the form append cannot merge into (measured on dcm-nf, UsrProjFx_FormPage, 2026-11-12).</summary>
	internal static string FullFormBody(string entity) =>
		"define(\"UsrOrders_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ {\n"
		+ "\treturn {\n"
		+ "\t\tviewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[\n"
		+ "\t\t\t{\n\t\t\t\t\"operation\": \"insert\",\n\t\t\t\t\"name\": \"UsrName\",\n"
		+ "\t\t\t\t\"values\": { \"type\": \"crt.Input\", \"control\": \"$UsrName\" },\n"
		+ "\t\t\t\t\"parentName\": \"SideAreaProfileContainer\",\n\t\t\t\t\"propertyName\": \"items\",\n\t\t\t\t\"index\": 0\n\t\t\t}\n"
		+ "\t\t]/**SCHEMA_VIEW_CONFIG_DIFF*/,\n"
		+ "\t\tviewModelConfig: /**SCHEMA_VIEW_MODEL_CONFIG*/{\n"
		+ "\t\t\t\"attributes\": { \"UsrName\": { \"modelConfig\": { \"path\": \"PDS.UsrName\" } } }\n"
		+ "\t\t}/**SCHEMA_VIEW_MODEL_CONFIG*/,\n"
		+ "\t\tmodelConfig: /**SCHEMA_MODEL_CONFIG*/{\n"
		+ "\t\t\t\"dataSources\": { \"PDS\": { \"type\": \"crt.EntityDataSource\", \"config\": { \"entitySchemaName\": \""
		+ entity + "\" }, \"scope\": \"page\" } },\n"
		+ "\t\t\t\"primaryDataSourceName\": \"PDS\"\n"
		+ "\t\t}/**SCHEMA_MODEL_CONFIG*/,\n"
		+ "\t\thandlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,\n"
		+ "\t\tconverters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,\n"
		+ "\t\tvalidators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/\n"
		+ "\t};\n"
		+ "});";

	internal static List<string> NamesIn(string fragment) =>
		JsonNode.Parse(SectionOf(fragment, "SCHEMA_VIEW_CONFIG_DIFF"))!.AsArray()
			.Select(node => node!["name"]!.GetValue<string>())
			.ToList();

	internal static string SectionOf(string body, string marker) {
		string pair = "/**" + marker + "*/";
		int start = body.IndexOf(pair, System.StringComparison.Ordinal) + pair.Length;
		int end = body.IndexOf(pair, start, System.StringComparison.Ordinal);
		return body[start..end];
	}

	internal static string WithoutSection(string body, string marker) =>
		body.Replace(SectionOf(body, marker), string.Empty, System.StringComparison.Ordinal);
}
