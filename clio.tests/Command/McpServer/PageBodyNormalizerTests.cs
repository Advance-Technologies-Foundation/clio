using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
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
	[Description("Proxy binding on a standard field is rewritten to the canonical $PDS_* binding.")]
	public void NormalizeProxyBindings_ProxyField_RewritesToPdsBinding() {
		string body = CreatePageBody(
			viewConfigDiff: """[{"operation":"insert","name":"UsrStatus","values":{"type":"crt.ComboBox","control":"$UsrStatus"}}]""",
			viewModelConfigDiff: """[{"operation":"merge","values":{"UsrStatus":{"modelConfig":{"path":"PDS.UsrStatus"}}}}]""");

		string result = PageBodyNormalizer.NormalizeProxyBindings(body);

		result.Should().Contain("$PDS_UsrStatus",
			because: "proxy binding $UsrStatus pointing to PDS.UsrStatus should be rewritten for round-trip safety");
		result.Should().NotContain("\"$UsrStatus\"",
			because: "the non-canonical proxy binding must not remain after normalization");
	}

	[Test]
	[Description("Proxy binding for the primary Name field is rewritten to the special-case $Name binding.")]
	public void NormalizeProxyBindings_NameField_RewritesToDollarName() {
		string body = CreatePageBody(
			viewConfigDiff: """[{"operation":"insert","name":"UsrName","values":{"type":"crt.Input","control":"$UsrName"}}]""",
			viewModelConfigDiff: """[{"operation":"merge","values":{"UsrName":{"modelConfig":{"path":"PDS.Name"}}}}]""");

		string result = PageBodyNormalizer.NormalizeProxyBindings(body);

		result.Should().Contain("\"$Name\"",
			because: "PDS.Name must normalize to the special-case $Name binding");
		result.Should().NotContain("\"$UsrName\"",
			because: "the proxy attribute binding should be replaced");
	}

	[Test]
	[Description("Canonical bindings remain unchanged when the body already uses the expected datasource form.")]
	public void NormalizeProxyBindings_CanonicalBinding_RemainsUnchanged() {
		string body = CreatePageBody(
			viewConfigDiff: """[{"operation":"insert","name":"UsrStatus","values":{"type":"crt.ComboBox","control":"$PDS_UsrStatus"}}]""",
			viewModelConfigDiff: """[{"operation":"merge","values":{"PDS_UsrStatus":{"modelConfig":{"path":"PDS.UsrStatus"}}}}]""");

		string result = PageBodyNormalizer.NormalizeProxyBindings(body);

		result.Should().Be(body,
			because: "already-canonical bindings should not be rewritten");
	}
}
