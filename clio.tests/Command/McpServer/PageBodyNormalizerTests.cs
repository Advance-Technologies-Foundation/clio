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

	[Test]
	[Description("Proxy binding on a standard field is rewritten to $PDS_* form.")]
	public void NormalizeProxyBindings_ProxyField_RewritesToPdsBinding() {
		// Arrange
		string body = CreatePageBody(
			viewConfigDiff: """[{"operation":"insert","name":"UsrStatus","values":{"type":"crt.ComboBox","label":"$Resources.Strings.PDS_UsrStatus","control":"$UsrStatus"}}]""",
			viewModelConfigDiff: """[{"operation":"merge","values":{"UsrStatus":{"modelConfig":{"path":"PDS.UsrStatus"}}}}]""");

		// Act
		string result = PageBodyNormalizer.NormalizeProxyBindings(body);

		// Assert
		result.Should().Contain("$PDS_UsrStatus",
			because: "proxy binding $UsrStatus pointing to PDS.UsrStatus should be rewritten to $PDS_UsrStatus");
		result.Should().NotContain("\"$UsrStatus\"",
			because: "the proxy binding must be replaced");
	}

	[Test]
	[Description("Proxy binding for the primary Name field is rewritten to $Name (special case).")]
	public void NormalizeProxyBindings_NameField_RewritesToDollarName() {
		// Arrange
		string body = CreatePageBody(
			viewConfigDiff: """[{"operation":"insert","name":"UsrName","values":{"type":"crt.Input","label":"$Resources.Strings.PDS_Name","control":"$UsrName"}}]""",
			viewModelConfigDiff: """[{"operation":"merge","values":{"UsrName":{"modelConfig":{"path":"PDS.Name"}}}}]""");

		// Act
		string result = PageBodyNormalizer.NormalizeProxyBindings(body);

		// Assert
		result.Should().Contain("\"$Name\"",
			because: "PDS.Name has the special-case expected binding $Name, not $PDS_Name");
		result.Should().NotContain("\"$UsrName\"",
			because: "the proxy binding must be replaced");
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
	[Description("Multiple proxy fields in one body are all rewritten in a single call.")]
	public void NormalizeProxyBindings_MultipleProxyFields_AllRewritten() {
		// Arrange
		string body = CreatePageBody(
			viewConfigDiff: """
				[
					{"operation":"insert","name":"UsrStatus","values":{"type":"crt.ComboBox","control":"$UsrStatus"}},
					{"operation":"insert","name":"UsrDueDate","values":{"type":"crt.DateTimePicker","control":"$UsrDueDate"}},
					{"operation":"insert","name":"UsrName","values":{"type":"crt.Input","control":"$UsrName"}}
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
		result.Should().Contain("$PDS_UsrStatus",
			because: "UsrStatus proxy binding must be rewritten");
		result.Should().Contain("$PDS_UsrDueDate",
			because: "UsrDueDate proxy binding must be rewritten");
		result.Should().Contain("\"$Name\"",
			because: "UsrName pointing to PDS.Name must use the special-case $Name binding");
		result.Should().NotMatchRegex("\"\\$Usr[A-Za-z]",
			because: "no proxy bindings should remain after normalization");
	}

	[Test]
	[Description("A proxy binding expressed via 'value' property (not 'control') is also rewritten.")]
	public void NormalizeProxyBindings_ValueProperty_Rewritten() {
		// Arrange
		string body = CreatePageBody(
			viewConfigDiff: """[{"operation":"insert","name":"UsrPhoto","values":{"type":"crt.ImageInput","value":"$UsrPhoto"}}]""",
			viewModelConfigDiff: """[{"operation":"merge","values":{"UsrPhoto":{"modelConfig":{"path":"PDS.UsrPhoto"}}}}]""");

		// Act
		string result = PageBodyNormalizer.NormalizeProxyBindings(body);

		// Assert
		result.Should().Contain("$PDS_UsrPhoto",
			because: "'value' bindings are also checked for proxy patterns");
		result.Should().NotContain("\"$UsrPhoto\"",
			because: "the proxy 'value' binding must be replaced");
	}

	[Test]
	[Description("A component at the top level of the viewConfigDiff array (flat shape, no 'values' wrapper) is normalized.")]
	public void NormalizeProxyBindings_FlatShapeComponent_Rewritten() {
		// Arrange
		string body = CreatePageBody(
			viewConfigDiff: """[{"type":"crt.Input","control":"$UsrStatus"}]""",
			viewModelConfigDiff: """[{"operation":"merge","values":{"UsrStatus":{"modelConfig":{"path":"PDS.UsrStatus"}}}}]""");

		// Act
		string result = PageBodyNormalizer.NormalizeProxyBindings(body);

		// Assert
		result.Should().Contain("$PDS_UsrStatus",
			because: "flat-shape components without a 'values' wrapper must also be normalized");
	}
}
