using System.IO;
using Clio.Command.ProcessModel;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.ProcessModel;

/// <summary>
/// Unit tests for <see cref="ProcessGraphExtractor"/> — projects parsed schema fixtures into the
/// structured elements/flows/parameters graph (Story 8). Reuses the existing ProcessSchema fixtures.
/// </summary>
[TestFixture]
[Property("Module", "ProcessModel")]
[Category("Unit")]
public sealed class ProcessGraphExtractorTests {
	private readonly ILogger _logger = Substitute.For<ILogger>();
	private readonly IProcessGraphExtractor _extractor = new ProcessGraphExtractor();

	private ProcessSchemaResponse Parse(string file) {
		string json = File.ReadAllText(Path.Join("Examples", "ProcessSchema", file));
		return ProcessSchemaResponse.FromJson(json, _logger).Value;
	}

	[Test]
	[Category("Unit")]
	[Description("Extract projects a parsed schema into code/uId, element and flow lists, and process parameters.")]
	[TestCase("ProcessSchemaResponse0.json")]
	[TestCase("ProcessSchemaResponse1.json")]
	[TestCase("ProcessSchemaResponse2.json")]
	public void Extract_ShouldProjectStructuredGraph_WhenSchemaParsed(string file) {
		// Arrange
		ProcessSchemaResponse schema = Parse(file);

		// Act
		ProcessDescription description = _extractor.Extract(schema, "en-US");

		// Assert
		description.Should().NotBeNull(because: "a parsed schema must yield a description");
		description.Code.Should().Be(schema.Schema.Name,
			because: "the description code mirrors the schema Name");
		description.UId.Should().Be(schema.Schema.UId.ToString(),
			because: "the description UId mirrors the schema UId");
		description.Parameters.Count.Should().Be(schema.Schema.MetaDataSchema?.Parameters?.Count ?? 0,
			because: "every process-level parameter is preserved (same data generate-process-model exposes)");
		description.Flows.Should().NotContain(f => f.Kind != "sequence" && f.Kind != "conditional" && f.Kind != "default",
			because: "every flow is classified into one of the three connection kinds");
		description.Elements.Should().NotContain(e => string.IsNullOrWhiteSpace(e.Type),
			because: "every projected element carries a resolved role");
	}

	[Test]
	[Category("Unit")]
	[Description("Extract emits structured sections (elements/flows/parameters), not the raw escaped metaData string.")]
	public void Extract_ShouldEmitStructuredSections_WhenProjectingRealProcess() {
		// Arrange
		ProcessSchemaResponse schema = Parse("ProcessSchemaResponse2.json");

		// Act
		ProcessDescription description = _extractor.Extract(schema, "en-US");

		// Assert
		(description.Elements.Count + description.Flows.Count).Should().BeGreaterThan(0,
			because: "a real process projects into elements and/or flows, not a raw metadata blob");
		description.Parameters.Count.Should().BeGreaterThan(0,
			because: "ProcessSchemaResponse2 carries process-level parameters");
	}
}
