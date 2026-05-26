using System.Text.Json;
using Clio.Command.BusinessRules;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "Command")]
public sealed class BusinessRuleToolApplyStaticFilterTests {

	[Test]
	[Category("Unit")]
	[Description("Deserializes an apply-static-filter action from the entity MCP contract and projects to the internal model.")]
	public void EntityBusinessRule_Should_Deserialize_ApplyStaticFilter() {
		string payload = """
		{
		  "caption": "Filter UsrCountry by Name starts with U",
		  "condition": { "logicalOperation": "AND", "conditions": [] },
		  "actions": [
		    {
		      "type": "apply-static-filter",
		      "targetAttribute": "UsrCountry",
		      "filter": {
		        "logicalOperation": "AND",
		        "filters": [
		          { "columnPath": "Name", "comparisonType": "START_WITH", "value": "U" }
		        ]
		      }
		    }
		  ]
		}
		""";

		EntityBusinessRuleMcpContract? contract = JsonSerializer.Deserialize<EntityBusinessRuleMcpContract>(payload);

		contract.Should().NotBeNull();
		BusinessRule rule = contract!.ToBusinessRule();
		ApplyStaticFilterBusinessRuleAction action = (ApplyStaticFilterBusinessRuleAction)rule.Actions[0];
		action.TargetAttribute.Should().Be("UsrCountry");
		action.Filter.GetProperty("logicalOperation").GetString().Should().Be("AND");
		action.Filter.GetProperty("filters")[0].GetProperty("columnPath").GetString().Should().Be("Name");
	}
}
