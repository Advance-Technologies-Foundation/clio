using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public class PageBodyNormalizerTests {

	private static string CreatePageBody(
		string viewConfigDiff = "[]",
		string viewModelConfigDiff = "[]",
		string viewModelConfig = "{}") {
		return $$"""
			define("TestPage", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ {
				return {
					viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/{{viewConfigDiff}}/**SCHEMA_VIEW_CONFIG_DIFF*/,
					viewModelConfig: /**SCHEMA_VIEW_MODEL_CONFIG*/{{viewModelConfig}}/**SCHEMA_VIEW_MODEL_CONFIG*/,
					viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/{{viewModelConfigDiff}}/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
					modelConfig: /**SCHEMA_MODEL_CONFIG*/{}/**SCHEMA_MODEL_CONFIG*/,
					modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
					handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,
					converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
					validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
				};
			});
			""";
	}

	private static string CreatePageBodyWithSchemaDiffMarker(
		string viewConfigDiff = "[]",
		string viewModelConfigDiff = "[]") {
		return $$"""
			define("TestPage", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ {
				return {
					viewConfigDiff: /**SCHEMA_DIFF*/{{viewConfigDiff}}/**SCHEMA_DIFF*/,
					viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/{{viewModelConfigDiff}}/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/
				};
			});
			""";
	}

	[Test]
	[Description("Legacy direct datasource binding on a standard field is rewritten to the declared view-model attribute.")]
	public void NormalizeProxyBindings_DirectDatasourceBinding_RewritesToDeclaredAttribute() {
		// Arrange
		string body = CreatePageBody(
			viewConfigDiff: """[{"operation":"insert","name":"UsrStatus","values":{"type":"crt.ComboBox","label":"$Resources.Strings.PDS_UsrStatus","control":"$PDS_UsrStatus"}}]""",
			viewModelConfigDiff: """[{"operation":"merge","values":{"UsrStatus":{"modelConfig":{"path":"PDS.UsrStatus"}}}}]""");

		// Act
		string result = PageBodyNormalizer.NormalizeProxyBindings(body);

		// Assert
		result.Should().Contain("$UsrStatus",
			because: "legacy direct datasource binding should be rewritten to the declared view-model attribute");
		result.Should().NotContain("\"$PDS_UsrStatus\"",
			because: "the direct datasource binding must be replaced");
	}

	[Test]
	[Description("Legacy direct datasource binding is rewritten to whichever declared attribute targets the same model path.")]
	public void NormalizeProxyBindings_NameField_RewritesToDeclaredAttributeForSamePath() {
		// Arrange
		string body = CreatePageBody(
			viewConfigDiff: """[{"operation":"insert","name":"UsrName","values":{"type":"crt.Input","label":"$Resources.Strings.PDS_Name","control":"$PDS_Name"}}]""",
			viewModelConfigDiff: """[{"operation":"merge","values":{"UsrName":{"modelConfig":{"path":"PDS.Name"}}}}]""");

		// Act
		string result = PageBodyNormalizer.NormalizeProxyBindings(body);

		// Assert
		result.Should().Contain("\"$UsrName\"",
			because: "direct datasource binding should be rewritten to the declared attribute that targets PDS.Name");
		result.Should().NotContain("\"$PDS_Name\"",
			because: "the direct datasource binding must be replaced");
	}

	[Test]
	[Description("A binding already in canonical $PDS_* form is left unchanged.")]
	public void NormalizeProxyBindings_CanonicalBinding_Unchanged() {
		// Arrange
		string body = CreatePageBody(
			viewConfigDiff: """[{"operation":"insert","name":"UsrStatus","values":{"type":"crt.ComboBox","control":"$PDS_UsrStatus"}}]""",
			viewModelConfigDiff: """[{"operation":"merge","values":{"PDS_UsrStatus":{"modelConfig":{"path":"PDS.UsrStatus"}}}}]""");

		// Act
		string result = PageBodyNormalizer.NormalizeProxyBindings(body);

		// Assert
		result.Should().Contain("$PDS_UsrStatus",
			because: "canonical binding should not be touched");
	}

	[Test]
	[Description("A component type not in the standard set is not normalized.")]
	public void NormalizeProxyBindings_NonStandardComponentType_Unchanged() {
		// Arrange
		string body = CreatePageBody(
			viewConfigDiff: """[{"operation":"insert","name":"UsrStatus","values":{"type":"crt.Button","control":"$UsrStatus"}}]""",
			viewModelConfigDiff: """[{"operation":"merge","values":{"UsrStatus":{"modelConfig":{"path":"PDS.UsrStatus"}}}}]""");

		// Act
		string result = PageBodyNormalizer.NormalizeProxyBindings(body);

		// Assert
		result.Should().Contain("\"$UsrStatus\"",
			because: "normalization only applies to standard field component types");
	}

	[Test]
	[Description("A binding whose attribute has no entry in viewModelConfigDiff is left unchanged even when other attributes are present.")]
	public void NormalizeProxyBindings_OrphanAttribute_Unchanged() {
		// Arrange
		string body = CreatePageBody(
			viewConfigDiff: """[{"operation":"insert","name":"UsrOrphan","values":{"type":"crt.Input","control":"$UsrOrphan"}}]""",
			viewModelConfigDiff: """[{"operation":"merge","values":{"OtherField":{"modelConfig":{"path":"PDS.OtherField"}}}}]""");

		// Act
		string result = PageBodyNormalizer.NormalizeProxyBindings(body);

		// Assert
		result.Should().Contain("\"$UsrOrphan\"",
			because: "an attribute absent from modelConfig cannot be resolved to a PDS path and must stay unchanged");
	}

	[Test]
	[Description("Malformed JSON in SCHEMA_VIEW_CONFIG_DIFF returns the original body without throwing.")]
	public void NormalizeProxyBindings_MalformedViewConfigDiff_ReturnsOriginalBody() {
		// Arrange
		string body = CreatePageBody(viewConfigDiff: "{[invalid json");

		// Act
		string result = PageBodyNormalizer.NormalizeProxyBindings(body);

		// Assert
		result.Should().Be(body,
			because: "normalization must be a no-op when the body cannot be parsed");
	}

	[Test]
	[Description("Null or empty body returns without throwing.")]
	[TestCase(null)]
	[TestCase("")]
	public void NormalizeProxyBindings_NullOrEmpty_ReturnsUnchanged(string body) {
		// Act
		string result = PageBodyNormalizer.NormalizeProxyBindings(body);

		// Assert
		result.Should().Be(body,
			because: "null/empty input must pass through unchanged");
	}

	[Test]
	[Description("Multiple legacy direct datasource bindings in one body are all rewritten in a single call.")]
	public void NormalizeProxyBindings_MultipleDirectDatasourceBindings_AllRewritten() {
		// Arrange
		string body = CreatePageBody(
			viewConfigDiff: """
				[
					{"operation":"insert","name":"UsrStatus","values":{"type":"crt.ComboBox","control":"$PDS_UsrStatus"}},
					{"operation":"insert","name":"UsrDueDate","values":{"type":"crt.DateTimePicker","control":"$PDS_UsrDueDate"}},
					{"operation":"insert","name":"UsrName","values":{"type":"crt.Input","control":"$PDS_Name"}}
				]
				""",
			viewModelConfigDiff: """
				[{"operation":"merge","values":{
					"UsrStatus":{"modelConfig":{"path":"PDS.UsrStatus"}},
					"UsrDueDate":{"modelConfig":{"path":"PDS.UsrDueDate"}},
					"UsrName":{"modelConfig":{"path":"PDS.Name"}}
				}}]
				""");

		// Act
		string result = PageBodyNormalizer.NormalizeProxyBindings(body);

		// Assert
		result.Should().Contain("$UsrStatus",
			because: "UsrStatus direct datasource binding must be rewritten to the declared attribute");
		result.Should().Contain("$UsrDueDate",
			because: "UsrDueDate direct datasource binding must be rewritten to the declared attribute");
		result.Should().Contain("\"$UsrName\"",
			because: "PDS.Name should be rebound to the declared attribute that targets the same path");
		result.Should().NotContain("\"$PDS_UsrStatus\"",
			because: "legacy direct datasource bindings should not remain after normalization");
		result.Should().NotContain("\"$PDS_UsrDueDate\"",
			because: "legacy direct datasource bindings should not remain after normalization");
		result.Should().NotContain("\"$PDS_Name\"",
			because: "legacy direct datasource bindings should not remain after normalization");
	}

	[Test]
	[Description("A legacy direct datasource binding expressed via 'value' property (not 'control') is also rewritten.")]
	public void NormalizeProxyBindings_ValueProperty_Rewritten() {
		// Arrange
		string body = CreatePageBody(
			viewConfigDiff: """[{"operation":"insert","name":"UsrPhoto","values":{"type":"crt.ImageInput","value":"$PDS_UsrPhoto"}}]""",
			viewModelConfigDiff: """[{"operation":"merge","values":{"UsrPhoto":{"modelConfig":{"path":"PDS.UsrPhoto"}}}}]""");

		// Act
		string result = PageBodyNormalizer.NormalizeProxyBindings(body);

		// Assert
		result.Should().Contain("$UsrPhoto",
			because: "'value' bindings are also checked for legacy direct datasource patterns");
		result.Should().NotContain("\"$PDS_UsrPhoto\"",
			because: "the direct datasource 'value' binding must be replaced");
	}

	[Test]
	[Description("A component at the top level of the viewConfigDiff array (flat shape, no 'values' wrapper) is normalized.")]
	public void NormalizeProxyBindings_FlatShapeComponent_Rewritten() {
		// Arrange
		string body = CreatePageBody(
			viewConfigDiff: """[{"type":"crt.Input","control":"$PDS_UsrStatus"}]""",
			viewModelConfigDiff: """[{"operation":"merge","values":{"UsrStatus":{"modelConfig":{"path":"PDS.UsrStatus"}}}}]""");

		// Act
		string result = PageBodyNormalizer.NormalizeProxyBindings(body);

		// Assert
		result.Should().Contain("$UsrStatus",
			because: "flat-shape components without a 'values' wrapper must also be rebound to the declared attribute");
	}

	[Test]
	[Description("A body that uses the SCHEMA_DIFF alias instead of SCHEMA_VIEW_CONFIG_DIFF is also normalized.")]
	public void NormalizeProxyBindings_SchemaDiffAlias_RewritesDirectDatasourceBinding() {
		// Arrange
		string body = CreatePageBodyWithSchemaDiffMarker(
			viewConfigDiff: """[{"operation":"insert","name":"UsrStatus","values":{"type":"crt.ComboBox","control":"$PDS_UsrStatus"}}]""",
			viewModelConfigDiff: """[{"operation":"merge","values":{"UsrStatus":{"modelConfig":{"path":"PDS.UsrStatus"}}}}]""");

		// Act
		string result = PageBodyNormalizer.NormalizeProxyBindings(body);

		// Assert
		result.Should().Contain("$UsrStatus",
			because: "SCHEMA_DIFF is a supported alias for SCHEMA_VIEW_CONFIG_DIFF and must trigger direct datasource binding rewrite");
		result.Should().NotContain("\"$PDS_UsrStatus\"",
			because: "the direct datasource binding must be replaced even when the body uses the SCHEMA_DIFF marker");
	}
}
