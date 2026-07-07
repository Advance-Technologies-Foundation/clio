using System;
using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class PageBodyBeforeSavePreprocessingPipelineTests {

	// Appends a fixed marker so chaining/order is observable in the output.
	private sealed class AppendPreprocessor(string suffix) : IPageBodyPreprocessor {
		public string Preprocess(string body) => body + suffix;
	}

	private sealed class ThrowingPreprocessor : IPageBodyPreprocessor {
		public string Preprocess(string body) => throw new InvalidOperationException("boom");
	}

	[Test]
	[Description("The pipeline applies every preprocessor in order, threading each result into the next.")]
	public void Preprocess_AppliesPreprocessorsInOrder() {
		IPageBodyPreprocessor[] preprocessors = [new AppendPreprocessor("-A"), new AppendPreprocessor("-B")];

		string result = PageBodyBeforeSavePreprocessingPipeline.Preprocess("body", preprocessors);

		result.Should().Be("body-A-B", "preprocessors run in registration order, each fed the previous result");
	}

	[Test]
	[Description("Fail-safe: a preprocessor that throws is skipped and the remaining preprocessors still run — a preprocessing step can never fail the save.")]
	public void Preprocess_SkipsThrowingPreprocessor() {
		IPageBodyPreprocessor[] preprocessors = [new ThrowingPreprocessor(), new AppendPreprocessor("-X")];

		string result = PageBodyBeforeSavePreprocessingPipeline.Preprocess("body", preprocessors);

		result.Should().Be("body-X", "the throwing preprocessor is skipped, the next one still applies");
	}

	[Test]
	[Description("An empty body is returned as-is without invoking preprocessors.")]
	public void Preprocess_EmptyBody_ReturnsUnchanged() {
		PageBodyBeforeSavePreprocessingPipeline.Preprocess("", [new AppendPreprocessor("-A")]).Should().Be("");
	}

	[Test]
	[Description("End-to-end: the production pipeline has the chart key-order preprocessor registered, so a crash-shaped chart body is normalized through the general mechanism (no direct call).")]
	public void Preprocess_ProductionPipeline_AppliesChartKeyOrderFix() {
		// config = { title, series, scales{..yAxis.name} } → scales last → crash-shaped.
		string vcd =
			"""[{"operation":"insert","name":"TestChart","parentName":"Main","propertyName":"items","index":0,"values":{"type":"crt.ChartWidget","config":{"title":"T","series":[{"type":"doughnut","label":"L","data":{"providing":{"schemaName":"Account"}}}],"scales":{"stacked":false,"xAxis":{"name":"Category"},"yAxis":{"name":"Revenue"}}}}}]""";
		string body = "/**SCHEMA_VIEW_CONFIG_DIFF*/" + vcd + "/**SCHEMA_VIEW_CONFIG_DIFF*/";

		string result = PageBodyBeforeSavePreprocessingPipeline.Preprocess(body);

		result.Should().NotBe(body, "the registered chart preprocessor must reorder the crash-shaped config");
		result.Should().Contain("\"Revenue\"", "the axis caption is preserved — only key order changes");
	}
}
