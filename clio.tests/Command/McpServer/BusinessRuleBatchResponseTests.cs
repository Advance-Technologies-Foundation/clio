using System.Text.Json;
using Clio.Command.BusinessRules;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class BusinessRuleBatchResponseTests {

	[Test]
	[Description("ENG-95270: a static-filter rule's match count and warning reach the MCP wire; plain rules keep the old shape.")]
	public void From_Should_Carry_Static_Filter_Outcome_Only_When_Present() {
		// Arrange
		BusinessRuleBatchItemResult[] items = [
			new("Filtered", true, "BusinessRule_1", null, FilterMatchCount: 4),
			new("Unverified", true, "BusinessRule_2", null, Warning: "saved without running its filter"),
			new("Plain", true, "BusinessRule_3", null)
		];

		// Act
		string json = JsonSerializer.Serialize(BusinessRuleBatchResponse.From(items));

		// Assert
		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement results = document.RootElement.GetProperty("results");
		results[0].GetProperty("filterMatchCount").GetInt32().Should().Be(4, because: "the agent reads the count from the tool result");
		results[1].GetProperty("warning").GetString().Should().Contain("without running", because: "an unverified filter is flagged");
		results[2].TryGetProperty("filterMatchCount", out _).Should().BeFalse(because: "rules without a static filter keep the previous result shape");
		results[2].TryGetProperty("warning", out _).Should().BeFalse(because: "absent fields are not written as null");
	}
}
