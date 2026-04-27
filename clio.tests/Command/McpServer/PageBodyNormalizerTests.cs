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
	[Description("Proxy binding (e.g. $UsrStatus) on a standard field is rewritten to its canonical $PDS_* form.")]
	public void NormalizeProxyBindings_ProxyBinding_RewrittenToCanonicalPdsForm() {
		// Arrange
		string body = CreatePageBody(
			viewConfigDiff: """[{"operation":"insert","name":"UsrStatus","values":{"type":"crt.ComboBox","label":"$Resources.Strings.PDS_UsrStatus","control":"$UsrStatus"}}]""",
			viewModelConfigDiff: """[{"operation":"merge","values":{"UsrStatus":{"modelConfig":{"path":"PDS.UsrStatus"}}}}]""");

		// Act
		string result = PageBodyNormalizer.NormalizeProxyBindings(body);

		// Assert
		result.Should().Contain("\"$PDS_UsrStatus\"",
			because: "proxy binding to a declared attribute with a PDS path should be rewritten to its canonical $PDS_* form");
		result.Should().NotContain("\"$UsrStatus\"",
			because: "the proxy binding must be replaced with the canonical form");
	}

	[Test]
	[Description("Proxy binding to a declared attribute with path PDS.Name is rewritten to the canonical $Name form.")]
	public void NormalizeProxyBindings_ProxyBindingForNamePath_RewrittenToCanonicalNameForm() {
		// Arrange
		string body = CreatePageBody(
			viewConfigDiff: """[{"operation":"insert","name":"UsrName","values":{"type":"crt.Input","label":"$Resources.Strings.PDS_Name","control":"$UsrName"}}]""",
			viewModelConfigDiff: """[{"operation":"merge","values":{"UsrName":{"modelConfig":{"path":"PDS.Name"}}}}]""");

		// Act
		string result = PageBodyNormalizer.NormalizeProxyBindings(body);

		// Assert
		result.Should().Contain("\"$Name\"",
			because: "a proxy binding to an attribute with path PDS.Name should be rewritten to the canonical $Name form");
		result.Should().NotContain("\"$UsrName\"",
			because: "the proxy binding must be replaced with the canonical form");
	}

	[Test]
	[Description("A canonical $PDS_* binding is left unchanged.")]
	public void NormalizeProxyBindings_CanonicalPdsBinding_Unchanged() {
		// Arrange
		string body = CreatePageBody(
			viewConfigDiff: """[{"operation":"insert","name":"UsrStatus","values":{"type":"crt.ComboBox","control":"$PDS_UsrStatus"}}]""",
			viewModelConfigDiff: """[{"operation":"merge","values":{"UsrStatus":{"modelConfig":{"path":"PDS.UsrStatus"}}}}]""");

		// Act
		string result = PageBodyNormalizer.NormalizeProxyBindings(body);

		// Assert
		result.Should().Contain("$PDS_UsrStatus",
			because: "canonical $PDS_* binding must not be touched");
	}

	[Test]
	[Description("A canonical $Name binding is left unchanged.")]
	public void NormalizeProxyBindings_CanonicalNameBinding_Unchanged() {
		// Arrange
		string body = CreatePageBody(
			viewConfigDiff: """[{"operation":"insert","name":"UsrName","values":{"type":"crt.Input","control":"$Name"}}]""",
			viewModelConfigDiff: """[{"operation":"merge","values":{"UsrName":{"modelConfig":{"path":"PDS.Name"}}}}]""");

		// Act
		string result = PageBodyNormalizer.NormalizeProxyBindings(body);

		// Assert
		result.Should().Contain("\"$Name\"",
			because: "canonical $Name binding must not be touched");
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
	[Description("Multiple proxy bindings in one body are all rewritten to canonical form in a single call.")]
	public void NormalizeProxyBindings_MultipleProxyBindings_AllRewrittenToCanonicalForm() {
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
			because: "UsrStatus proxy binding must be rewritten to its canonical $PDS_* form");
		result.Should().Contain("$PDS_UsrDueDate",
			because: "UsrDueDate proxy binding must be rewritten to its canonical $PDS_* form");
		result.Should().Contain("\"$Name\"",
			because: "UsrName proxy binding targeting PDS.Name must be rewritten to the canonical $Name form");
		result.Should().NotContain("\"$UsrStatus\"",
			because: "proxy bindings must be replaced with canonical forms");
		result.Should().NotContain("\"$UsrDueDate\"",
			because: "proxy bindings must be replaced with canonical forms");
		result.Should().NotContain("\"$UsrName\"",
			because: "proxy bindings must be replaced with canonical forms");
	}

	[Test]
	[Description("A proxy binding expressed via the 'value' property (not 'control') is also rewritten to canonical form.")]
	public void NormalizeProxyBindings_ValueProperty_Rewritten() {
		// Arrange
		string body = CreatePageBody(
			viewConfigDiff: """[{"operation":"insert","name":"UsrPhoto","values":{"type":"crt.ImageInput","value":"$UsrPhoto"}}]""",
			viewModelConfigDiff: """[{"operation":"merge","values":{"UsrPhoto":{"modelConfig":{"path":"PDS.UsrPhoto"}}}}]""");

		// Act
		string result = PageBodyNormalizer.NormalizeProxyBindings(body);

		// Assert
		result.Should().Contain("$PDS_UsrPhoto",
			because: "'value' proxy bindings are also rewritten to canonical $PDS_* form");
		result.Should().NotContain("\"$UsrPhoto\"",
			because: "the proxy 'value' binding must be replaced with the canonical form");
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
			because: "flat-shape components without a 'values' wrapper must also have their proxy binding rewritten to canonical form");
	}

	[Test]
	[Description("A body that uses the SCHEMA_DIFF alias instead of SCHEMA_VIEW_CONFIG_DIFF is also normalized.")]
	public void NormalizeProxyBindings_SchemaDiffAlias_RewritesProxyBinding() {
		// Arrange
		string body = CreatePageBodyWithSchemaDiffMarker(
			viewConfigDiff: """[{"operation":"insert","name":"UsrStatus","values":{"type":"crt.ComboBox","control":"$UsrStatus"}}]""",
			viewModelConfigDiff: """[{"operation":"merge","values":{"UsrStatus":{"modelConfig":{"path":"PDS.UsrStatus"}}}}]""");

		// Act
		string result = PageBodyNormalizer.NormalizeProxyBindings(body);

		// Assert
		result.Should().Contain("$PDS_UsrStatus",
			because: "SCHEMA_DIFF is a supported alias for SCHEMA_VIEW_CONFIG_DIFF and must trigger proxy binding rewrite");
		result.Should().NotContain("\"$UsrStatus\"",
			because: "the proxy binding must be replaced with canonical form even when the body uses the SCHEMA_DIFF marker");
	}
}
