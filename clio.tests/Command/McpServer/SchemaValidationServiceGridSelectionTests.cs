using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class SchemaValidationServiceGridSelectionTests {

	private static string Body(string viewConfigDiff) =>
		"define(\"UsrTest_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, "
		+ "function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { "
		+ "viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/" + viewConfigDiff + "/**SCHEMA_VIEW_CONFIG_DIFF*/, "
		+ "viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, "
		+ "modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, "
		+ "handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, "
		+ "converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, "
		+ "validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	[Test]
	[Description("Warns when an inserted grid binds selectionState but no operation writes the selection back through _selectionOptions.")]
	public void ValidateGridSelectionWriteBack_WhenInsertedGridHasNoSelectionOptions_ReturnsWarning() {
		// Arrange — the shape of the reviewed migration run: selection bound, write-back half missing.
		string body = Body("""
			[{"operation":"insert","name":"ProductsList","parentName":"Tab","propertyName":"items","index":0,
			  "values":{"type":"crt.DataGrid","features":{"rows":{"selection":{"enable":true,"multiple":true}}},
			            "selectionState":"$ProductsList_SelectionState","items":"$ProductsList"}}]
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateGridSelectionWriteBack(body);

		// Assert
		result.IsValid.Should().BeTrue(because: "the rule is a warning and never rejects a save");
		result.Warnings.Should().ContainSingle(because: "exactly one grid binds selection without the write-back half")
			.Which.Should().Contain("grid-selection-without-write-back")
			.And.Contain("ProductsList")
			.And.Contain("_selectionOptions")
			.And.Contain("ProductsList_SelectionState",
				because: "the warning names the attribute the write-back has to target");
	}

	[Test]
	[Description("Does not warn when the grid carries both selectionState and _selectionOptions.attribute.")]
	public void ValidateGridSelectionWriteBack_WhenBothHalvesPresent_ReturnsNoWarning() {
		// Arrange
		string body = Body("""
			[{"operation":"insert","name":"ProductsList","parentName":"Tab","propertyName":"items","index":0,
			  "values":{"type":"crt.DataGrid","selectionState":"$ProductsList_SelectionState",
			            "_selectionOptions":{"attribute":"ProductsList_SelectionState"}}}]
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateGridSelectionWriteBack(body);

		// Assert
		result.Warnings.Should().BeEmpty(because: "the selection is written back to the bound attribute");
	}

	[Test]
	[Description("Does not warn when _selectionOptions arrives in a later merge of the grid the same body inserts.")]
	public void ValidateGridSelectionWriteBack_WhenWriteBackArrivesInOwnMerge_ReturnsNoWarning() {
		// Arrange
		string body = Body("""
			[{"operation":"insert","name":"ProductsList","parentName":"Tab","propertyName":"items","index":0,
			  "values":{"type":"crt.DataGrid","selectionState":"$ProductsList_SelectionState"}},
			 {"operation":"merge","name":"ProductsList",
			  "values":{"_selectionOptions":{"attribute":"ProductsList_SelectionState"}}}]
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateGridSelectionWriteBack(body);

		// Assert
		result.Warnings.Should().BeEmpty(because: "any operation of the body on the same element may carry the write-back");
	}

	[Test]
	[Description("Warns when a merge of a grid the same body inserts adds selectionState without any write-back.")]
	public void ValidateGridSelectionWriteBack_WhenOwnMergeAddsSelectionWithoutWriteBack_ReturnsWarning() {
		// Arrange
		string body = Body("""
			[{"operation":"insert","name":"ProductsList","parentName":"Tab","propertyName":"items","index":0,
			  "values":{"type":"crt.DataGrid"}},
			 {"operation":"merge","name":"ProductsList","values":{"selectionState":"$ProductsList_SelectionState"}}]
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateGridSelectionWriteBack(body);

		// Assert
		result.Warnings.Should().ContainSingle(because: "the page owns the grid, so the missing write-back is its own");
	}

	[Test]
	[Description("Does not warn on a merge of a parent's grid: the parent may carry _selectionOptions that clio cannot see.")]
	public void ValidateGridSelectionWriteBack_WhenMergingParentGrid_ReturnsNoWarning() {
		// Arrange
		string body = Body("""
			[{"operation":"merge","name":"DataTable","values":{"selectionState":"$DataTable_SelectionState"}}]
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateGridSelectionWriteBack(body);

		// Assert
		result.Warnings.Should().BeEmpty(because: "the grid is inserted by a parent schema whose body is not checked here");
	}

	[Test]
	[Description("Warns when _selectionOptions is present but has no attribute to write the selection to.")]
	public void ValidateGridSelectionWriteBack_WhenSelectionOptionsHasNoAttribute_ReturnsWarning() {
		// Arrange
		string body = Body("""
			[{"operation":"insert","name":"ProductsList","parentName":"Tab","propertyName":"items","index":0,
			  "values":{"type":"crt.DataGrid","selectionState":"$ProductsList_SelectionState","_selectionOptions":{}}}]
			""");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateGridSelectionWriteBack(body);

		// Assert
		result.Warnings.Should().ContainSingle(because: "_selectionOptions without an attribute writes the selection nowhere");
	}

	[Test]
	[Description("Returns no warning for an empty body or a body without a viewConfigDiff section.")]
	public void ValidateGridSelectionWriteBack_WhenBodyEmpty_ReturnsNoWarning() {
		// Act
		SchemaValidationResult empty = SchemaValidationService.ValidateGridSelectionWriteBack(string.Empty);
		SchemaValidationResult noSection = SchemaValidationService.ValidateGridSelectionWriteBack("define(\"X\", [], function() { return {}; });");

		// Assert
		empty.Warnings.Should().BeEmpty(because: "there is nothing to check");
		noSection.Warnings.Should().BeEmpty(because: "a body without a viewConfigDiff section has no grids");
	}
}
