using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Unit tests for <see cref="PageLayoutCompositionDetector"/>: the body-shape detector that drives
/// the write-path layout-guidance gate. "Adds or lays out components" means at least one
/// <c>operation:"insert"</c> entry in <c>viewConfigDiff</c> whose <c>values.type</c> is a
/// <c>crt.*</c> view element.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class PageLayoutCompositionDetectorTests {

	private static string WrapViewConfigDiff(string viewConfigDiffJson) =>
		"define('TestPage', /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, "
		+ "function(/**SCHEMA_ARGS*//**SCHEMA_ARGS*/) { return { "
		+ "viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/" + viewConfigDiffJson + "/**SCHEMA_VIEW_CONFIG_DIFF*/, "
		+ "viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, "
		+ "modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, "
		+ "handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, "
		+ "converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, "
		+ "validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	[Test]
	[Category("Unit")]
	[Description("Returns true when the body inserts a crt.* view component.")]
	public void BodyAddsOrLaysOutComponents_Should_Return_True_When_Inserting_Crt_Component() {
		// Arrange
		var detector = new PageLayoutCompositionDetector();
		string body = WrapViewConfigDiff(
			"[{\"operation\":\"insert\",\"name\":\"UsrName\",\"values\":{\"type\":\"crt.Input\"}}]");

		// Act
		bool result = detector.BodyAddsOrLaysOutComponents(body);

		// Assert
		result.Should().BeTrue(
			because: "an insert of a crt.* visual component is a layout-composing change the gate must catch");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns true when a crt.* container component is inserted.")]
	public void BodyAddsOrLaysOutComponents_Should_Return_True_When_Inserting_Crt_Container() {
		// Arrange
		var detector = new PageLayoutCompositionDetector();
		string body = WrapViewConfigDiff(
			"[{\"operation\":\"insert\",\"name\":\"MainTab\",\"values\":{\"type\":\"crt.Tab\"}}]");

		// Act
		bool result = detector.BodyAddsOrLaysOutComponents(body);

		// Assert
		result.Should().BeTrue(
			because: "inserting a crt.* container (tab/group/panel) is laying out the page UI");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns false for a merge-only viewConfigDiff (no insert).")]
	public void BodyAddsOrLaysOutComponents_Should_Return_False_When_Merge_Only() {
		// Arrange
		var detector = new PageLayoutCompositionDetector();
		string body = WrapViewConfigDiff(
			"[{\"operation\":\"merge\",\"name\":\"UsrName\",\"values\":{\"label\":\"X\"}}]");

		// Act
		bool result = detector.BodyAddsOrLaysOutComponents(body);

		// Assert
		result.Should().BeFalse(
			because: "a merge edits an existing component and does not add or lay out a new one");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns false for a remove-only viewConfigDiff.")]
	public void BodyAddsOrLaysOutComponents_Should_Return_False_When_Remove_Only() {
		// Arrange
		var detector = new PageLayoutCompositionDetector();
		string body = WrapViewConfigDiff(
			"[{\"operation\":\"remove\",\"name\":\"UsrName\"}]");

		// Act
		bool result = detector.BodyAddsOrLaysOutComponents(body);

		// Assert
		result.Should().BeFalse(
			because: "removing a component is not adding or laying one out");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns false for a move-only viewConfigDiff.")]
	public void BodyAddsOrLaysOutComponents_Should_Return_False_When_Move_Only() {
		// Arrange
		var detector = new PageLayoutCompositionDetector();
		string body = WrapViewConfigDiff(
			"[{\"operation\":\"move\",\"name\":\"UsrName\",\"index\":2}]");

		// Act
		bool result = detector.BodyAddsOrLaysOutComponents(body);

		// Assert
		result.Should().BeFalse(
			because: "reordering an existing component is not adding or laying one out");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns false when an insert's values.type is not a crt.* element.")]
	public void BodyAddsOrLaysOutComponents_Should_Return_False_When_Insert_Is_Not_Crt_Type() {
		// Arrange
		var detector = new PageLayoutCompositionDetector();
		string body = WrapViewConfigDiff(
			"[{\"operation\":\"insert\",\"name\":\"X\",\"values\":{\"type\":\"usr.CustomThing\"}}]");

		// Act
		bool result = detector.BodyAddsOrLaysOutComponents(body);

		// Assert
		result.Should().BeFalse(
			because: "only a crt.* view-element insert counts as composing the page layout");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns false for a handler-only body (empty viewConfigDiff, handlers present).")]
	public void BodyAddsOrLaysOutComponents_Should_Return_False_When_Handler_Only() {
		// Arrange
		var detector = new PageLayoutCompositionDetector();
		string body = "define('TestPage', /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, "
			+ "function(/**SCHEMA_ARGS*//**SCHEMA_ARGS*/) { return { "
			+ "viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, "
			+ "handlers: /**SCHEMA_HANDLERS*/[{\"request\":\"usr.DoThing\",\"handler\":\"async()=>{}\"}]/**SCHEMA_HANDLERS*/, "
			+ "converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, "
			+ "validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

		// Act
		bool result = detector.BodyAddsOrLaysOutComponents(body);

		// Assert
		result.Should().BeFalse(
			because: "a handler-only body changes behavior, not the page layout");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns false for a converter-only body.")]
	public void BodyAddsOrLaysOutComponents_Should_Return_False_When_Converter_Only() {
		// Arrange
		var detector = new PageLayoutCompositionDetector();
		string body = "define('TestPage', /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, "
			+ "function(/**SCHEMA_ARGS*//**SCHEMA_ARGS*/) { return { "
			+ "viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, "
			+ "handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, "
			+ "converters: /**SCHEMA_CONVERTERS*/{\"Usr.ToUpper\":\"v=>v.toUpperCase()\"}/**SCHEMA_CONVERTERS*/, "
			+ "validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

		// Act
		bool result = detector.BodyAddsOrLaysOutComponents(body);

		// Assert
		result.Should().BeFalse(
			because: "a converter-only body changes value transformation, not the page layout");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns false for a validator-only body.")]
	public void BodyAddsOrLaysOutComponents_Should_Return_False_When_Validator_Only() {
		// Arrange
		var detector = new PageLayoutCompositionDetector();
		string body = "define('TestPage', /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, "
			+ "function(/**SCHEMA_ARGS*//**SCHEMA_ARGS*/) { return { "
			+ "viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, "
			+ "handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, "
			+ "converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, "
			+ "validators: /**SCHEMA_VALIDATORS*/{\"Usr.Req\":{}}/**SCHEMA_VALIDATORS*/ }; });";

		// Act
		bool result = detector.BodyAddsOrLaysOutComponents(body);

		// Assert
		result.Should().BeFalse(
			because: "a validator-only body changes validation, not the page layout");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns false for an empty viewConfigDiff array.")]
	public void BodyAddsOrLaysOutComponents_Should_Return_False_When_ViewConfigDiff_Is_Empty() {
		// Arrange
		var detector = new PageLayoutCompositionDetector();
		string body = WrapViewConfigDiff("[]");

		// Act
		bool result = detector.BodyAddsOrLaysOutComponents(body);

		// Assert
		result.Should().BeFalse(
			because: "an empty viewConfigDiff adds no components");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns false when the body has no viewConfigDiff marker section at all.")]
	public void BodyAddsOrLaysOutComponents_Should_Return_False_When_No_ViewConfigDiff_Marker() {
		// Arrange
		var detector = new PageLayoutCompositionDetector();
		string body = "define('TestPage', [], function(){ return {}; });";

		// Act
		bool result = detector.BodyAddsOrLaysOutComponents(body);

		// Assert
		result.Should().BeFalse(
			because: "a body with no viewConfigDiff section cannot be detected as composing layout");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns false for a null or whitespace body.")]
	public void BodyAddsOrLaysOutComponents_Should_Return_False_When_Body_Is_Null_Or_Blank() {
		// Arrange
		var detector = new PageLayoutCompositionDetector();

		// Act
		bool nullResult = detector.BodyAddsOrLaysOutComponents(null);
		bool blankResult = detector.BodyAddsOrLaysOutComponents("   ");

		// Assert
		nullResult.Should().BeFalse(because: "a null body adds nothing");
		blankResult.Should().BeFalse(because: "a blank body adds nothing");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns false for a mobile-style plain-JSON body with a crt.* insert because it has no SCHEMA_VIEW_CONFIG_DIFF markers.")]
	public void BodyAddsOrLaysOutComponents_Should_Return_False_When_Mobile_Plain_Json() {
		// Arrange — a mobile body is plain JSON with no marker-delimited viewConfigDiff section.
		var detector = new PageLayoutCompositionDetector();
		string body = "{\"viewConfigDiff\":[{\"operation\":\"insert\",\"name\":\"UsrName\",\"values\":{\"type\":\"crt.Input\"}}]}";

		// Act
		bool result = detector.BodyAddsOrLaysOutComponents(body);

		// Assert
		result.Should().BeFalse(
			because: "the detector is web-only and reads the SCHEMA_VIEW_CONFIG_DIFF marker section; a mobile body has no markers, so per the fail-open contract it is never blocked by the layout gate");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns false when an insert has values but no type property.")]
	public void BodyAddsOrLaysOutComponents_Should_Return_False_When_Insert_Has_Values_Without_Type() {
		// Arrange
		var detector = new PageLayoutCompositionDetector();
		string body = WrapViewConfigDiff(
			"[{\"operation\":\"insert\",\"name\":\"UsrName\",\"values\":{\"label\":\"X\"}}]");

		// Act
		bool result = detector.BodyAddsOrLaysOutComponents(body);

		// Assert
		result.Should().BeFalse(
			because: "an insert whose values omit a type cannot be classified as a crt.* component, so the fail-open contract returns false rather than blocking");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns false when the viewConfigDiff marker section content is malformed, unparseable JSON.")]
	public void BodyAddsOrLaysOutComponents_Should_Return_False_When_ViewConfigDiff_Json_Is_Malformed() {
		// Arrange — the marker section is present but its content is not parseable JSON.
		var detector = new PageLayoutCompositionDetector();
		string body = WrapViewConfigDiff("[{\"operation\": insert, ");

		// Act
		bool result = detector.BodyAddsOrLaysOutComponents(body);

		// Assert
		result.Should().BeFalse(
			because: "an unparseable viewConfigDiff is handled by the syntax/content validators that run before this gate; the detector follows the fail-open contract and never blocks on a parse failure");
	}
}
