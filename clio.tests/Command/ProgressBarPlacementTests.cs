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
	[Description("The fragment is the application wizard's two inserts: a flex container at index 0 of MainContainer, then the stage bar inside it with the literal entity name, saveOnChange true and askUserToChangeSchema false.")]
	public void BuildViewConfigDiff_ShouldCarryTheWizardValues_WhenNoSuffix() {
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
		barValues["saveOnChange"]!.GetValue<bool>().Should().BeTrue(because: "a click persists the stage at once");
		barValues["askUserToChangeSchema"]!.GetValue<bool>().Should().BeFalse(because: "these are the wizard's values");
		barValues.Select(pair => pair.Key).Should().BeEquivalentTo(
			["type", "entityName", "saveOnChange", "askUserToChangeSchema"],
			because: "nothing else is written on the bar");
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

	internal static List<string> NamesIn(string fragment) {
		int start = fragment.IndexOf("/**SCHEMA_VIEW_CONFIG_DIFF*/", System.StringComparison.Ordinal)
			+ "/**SCHEMA_VIEW_CONFIG_DIFF*/".Length;
		int end = fragment.IndexOf("/**SCHEMA_VIEW_CONFIG_DIFF*/", start, System.StringComparison.Ordinal);
		return JsonNode.Parse(fragment[start..end])!.AsArray()
			.Select(node => node!["name"]!.GetValue<string>())
			.ToList();
	}
}
