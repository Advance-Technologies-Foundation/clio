using System.Linq;
using System.Text.Json;
using Clio;
using Clio.Command;
using Clio.Command.BusinessRules;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.BusinessRules.Filters;

[TestFixture]
[Property("Module", "Command")]
public sealed class BusinessRuleToolApplyStaticFilterTests {

	[Test]
	[Category("Unit")]
	[Description("MCP tool maps an apply-static-filter payload into EntityBusinessRuleCreateRequest without mutation, propagates success exit code 0, and surfaces the generated rule name.")]
	public void BusinessRuleCreate_Should_Map_Apply_Static_Filter_Action_And_Return_Success_Exit_Code() {
		// Arrange
		IEntityBusinessRuleService service = Substitute.For<IEntityBusinessRuleService>();
		CreateEntityBusinessRuleCommand command = new(service, ConsoleLogger.Instance);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntityBusinessRuleCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);
		service.Create(Arg.Any<EntityBusinessRuleCreateRequest>())
			.Returns(new BusinessRuleCreateResult("BusinessRule_StaticFltr"));
		CreateEntityBusinessRuleTool tool = new(command, ConsoleLogger.Instance, commandResolver);
		JsonElement filter = JsonDocument.Parse(
			"{\"logicalOperation\":\"AND\",\"filters\":[{\"columnPath\":\"Name\",\"comparisonType\":\"EQUAL\",\"value\":\"Ukraine\"}],\"backwardReferenceFilters\":[]}")
			.RootElement.Clone();
		EntityBusinessRuleMcpContract rule = new(
			"Restrict City lookup by country",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "Name", null),
						"is-filled-in",
						null)
				]),
			[new EntityApplyStaticFilterBusinessRuleActionMcpContract("Country", filter)]);

		// Act
		CommandExecutionResult result = tool.BusinessRuleCreate("dev", "UsrPkg", "UsrCity", rule);

		// Assert
		result.ExitCode.Should().Be(0,
			because: "a successful service call must return exit code 0");
		result.Output.Select(message => (string)message.Value)
			.Should().Contain("Rule name: BusinessRule_StaticFltr",
				because: "the MCP tool must surface the generated rule name verbatim");
		service.Received(1).Create(Arg.Is<EntityBusinessRuleCreateRequest>(request =>
			request.PackageName == "UsrPkg"
			&& request.EntitySchemaName == "UsrCity"
			&& request.Rule.Actions.Count == 1
			&& ((ApplyStaticFilterBusinessRuleAction)request.Rule.Actions[0]).TargetAttribute == "Country"));
	}

	[Test]
	[Category("Unit")]
	[Description("Polymorphic deserialization of type=apply-static-filter binds to ApplyStaticFilterBusinessRuleAction with the targetAttribute and filter properties populated.")]
	public void Deserializing_Apply_Static_Filter_Wire_Payload_Should_Bind_To_Apply_Static_Filter_Subclass() {
		// Arrange
		string json = """
			{
			  "type": "apply-static-filter",
			  "targetAttribute": "Country",
			  "filter": {
			    "logicalOperation": "AND",
			    "filters": [
			      { "columnPath": "Name", "comparisonType": "EQUAL", "value": "Ukraine" }
			    ],
			    "backwardReferenceFilters": []
			  }
			}
			""";

		// Act
		BusinessRuleAction? action = JsonSerializer.Deserialize<BusinessRuleAction>(
			json, BusinessRuleConstants.JsonOptions);

		// Assert
		action.Should().BeOfType<ApplyStaticFilterBusinessRuleAction>(
			because: "the polymorphic discriminator type=apply-static-filter must bind to the new subclass");
		ApplyStaticFilterBusinessRuleAction setFilter = (ApplyStaticFilterBusinessRuleAction)action!;
		setFilter.TargetAttribute.Should().Be("Country",
			because: "targetAttribute must round-trip from the wire payload");
		setFilter.Filter.ValueKind.Should().Be(JsonValueKind.Object,
			because: "the filter JSON object must be preserved verbatim as a JsonElement for the validator to deserialize into StaticFilterGroup");
		setFilter.ExtensionData.Should().BeNull(
			because: "no unknown properties were sent on this happy-path payload");
	}

	[Test]
	[Category("Unit")]
	[Description("Page-level polymorphic deserialization accepts apply-static-filter so the page validator can reject it with a tailored 'use create-entity-business-rule instead' message instead of a generic JsonException.")]
	public void Page_Contract_Should_Deserialize_Apply_Static_Filter_For_Targeted_Rejection() {
		// Arrange
		string json = """
			{
			  "type": "apply-static-filter",
			  "targetAttribute": "UsrCity",
			  "filter": { "logicalOperation": "AND", "filters": [], "backwardReferenceFilters": [] }
			}
			""";

		// Act
		PageBusinessRuleActionMcpContract? action = JsonSerializer.Deserialize<PageBusinessRuleActionMcpContract>(
			json, BusinessRuleConstants.JsonOptions);

		// Assert
		action.Should().BeOfType<PageApplyStaticFilterBusinessRuleActionMcpContract>(
			because: "page contract registers the variant so deserialization succeeds and the page validator can return a clear scope-rejection message");
		BusinessRuleAction shared = action!.ToBusinessRuleAction();
		shared.Should().BeOfType<ApplyStaticFilterBusinessRuleAction>();
	}

	[Test]
	[Category("Unit")]
	[Description("Wire-level callers that send `items` alongside targetAttribute / filter on type=apply-static-filter have the items captured in ExtensionData so the validator can surface filter.items-not-allowed.")]
	public void Deserializing_Wire_Payload_With_Items_Should_Capture_Items_In_ExtensionData() {
		// Arrange
		string json = """
			{
			  "type": "apply-static-filter",
			  "targetAttribute": "Country",
			  "filter": { "logicalOperation": "AND", "filters": [], "backwardReferenceFilters": [] },
			  "items": ["Status"]
			}
			""";

		// Act
		BusinessRuleAction? action = JsonSerializer.Deserialize<BusinessRuleAction>(
			json, BusinessRuleConstants.JsonOptions);

		// Assert
		action.Should().BeOfType<ApplyStaticFilterBusinessRuleAction>();
		ApplyStaticFilterBusinessRuleAction setFilter = (ApplyStaticFilterBusinessRuleAction)action!;
		setFilter.ExtensionData.Should().NotBeNull(
			because: "JsonExtensionData must capture wire-level items so the validator can surface filter.items-not-allowed");
		setFilter.ExtensionData!.Should().ContainKey("items",
			because: "the catch-all dictionary closes the JSON-only loophole the type system cannot prevent");
	}
}
