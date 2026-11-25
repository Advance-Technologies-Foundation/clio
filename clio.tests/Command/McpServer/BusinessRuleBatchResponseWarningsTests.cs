using System.Text.Json;
using Clio.Command.BusinessRules;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class BusinessRuleBatchResponseWarningsTests {

	[Test]
	[Description("Carries a saved rule's advisory warnings into the batch response item.")]
	public void From_WhenItemHasWarnings_SerializesThem() {
		// Arrange
		BusinessRuleBatchItemResult item = new("Hide input", true, "BusinessRule_1", null,
			["inverse-redundant — rule 'Hide input' only undoes 'Show input'"]);

		// Act
		string json = JsonSerializer.Serialize(BusinessRuleBatchResponse.From([item]));

		// Assert
		json.Should().Contain("\"warnings\":[\"inverse-redundant",
			because: "the agent reads the finding from the item it concerns");
	}

	[Test]
	[Description("Omits the warnings field when a rule has none, so the existing response shape is unchanged.")]
	public void From_WhenItemHasNoWarnings_OmitsField() {
		// Arrange
		BusinessRuleBatchItemResult item = new("Show input", true, "BusinessRule_1", null);

		// Act
		string json = JsonSerializer.Serialize(BusinessRuleBatchResponse.From([item]));

		// Assert
		json.Should().NotContain("warnings", because: "the field is additive and absent when empty");
	}
}
