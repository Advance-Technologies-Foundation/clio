using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.ComponentModel.DataAnnotations;
using Clio;
using Clio.Command;
using Clio.Command.BusinessRules;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public sealed class BusinessRuleToolTests {
	[Test]
	[Category("Unit")]
	[Description("Advertises a stable MCP tool name for create-entity-business-rule.")]
	public void BusinessRuleCreate_Should_Advertise_Stable_Tool_Name() {
		// Arrange

		// Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(CreateEntityBusinessRuleTool)
			.GetMethod(nameof(CreateEntityBusinessRuleTool.BusinessRuleCreate))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be(CreateEntityBusinessRuleTool.BusinessRuleCreateToolName,
			because: "the MCP tool name must stay stable for callers and tests");
		attribute.ReadOnly.Should().BeFalse(
			because: "creating business rules mutates remote Creatio metadata");
		attribute.Destructive.Should().BeTrue(
			because: "the tool changes the target entity add-on state");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps the MCP payload into the business-rule create service request without normalizing action items and returns a success exit code.")]
	public void BusinessRuleCreate_Should_Map_Arguments_And_Return_Success_Exit_Code() {
		// Arrange
		IBusinessRuleService service = Substitute.For<IBusinessRuleService>();
		CreateEntityBusinessRuleCommand command = new(service, ConsoleLogger.Instance);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntityBusinessRuleCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);
		service.Create(Arg.Any<BusinessRuleCreateRequest>())
			.Returns(new BusinessRuleCreateResult("BusinessRule_1234567"));
		CreateEntityBusinessRuleTool tool = new(command, ConsoleLogger.Instance, commandResolver);
		BusinessRule rule = new(
			"Require owner for drafts",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "Status", null),
						"equal",
						new BusinessRuleExpression("Const", null,
							JsonSerializer.Deserialize<JsonElement>("\"Draft\"")))
				]),
			[
				new BusinessRuleAction("make-required", [" Owner ", "Amount", "Owner"]),
				new BusinessRuleAction("make-read-only", ["Status"])
			]);

		// Act
		CommandExecutionResult result = tool.BusinessRuleCreate("dev", "UsrPkg", "UsrOrder", rule);

		// Assert
		result.ExitCode.Should().Be(0,
			because: "a successful service call should return the standard success exit code");
		result.Output.Select(message => (string)message.Value).Should().Contain("Rule name: BusinessRule_1234567",
			because: "the MCP tool should preserve the generated internal rule name in execution logs");
		commandResolver.Received(1).Resolve<CreateEntityBusinessRuleCommand>(Arg.Is<EnvironmentOptions>(options =>
			options.Environment == "dev"));
		service.Received(1).Create(
			Arg.Is<BusinessRuleCreateRequest>(request =>
				request.PackageName == "UsrPkg"
				&& request.EntitySchemaName == "UsrOrder"
				&& request.Rule.Caption == "Require owner for drafts"
				&& request.Rule.Condition.LogicalOperation == "AND"
				&& request.Rule.Actions.Count == 2
				&& request.Rule.Actions[0].Type == "make-required"
				&& request.Rule.Actions[0].Items.Count == 3
				&& request.Rule.Actions[0].Items[0] == " Owner "
				&& request.Rule.Actions[0].Items[1] == "Amount"
				&& request.Rule.Actions[0].Items[2] == "Owner"
				&& request.Rule.Actions[1].Items.Count == 1
				&& request.Rule.Actions[1].Items[0] == "Status"));
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves the business-rule command from the payload environment so nested dependencies use the requested MCP target.")]
	public void BusinessRuleCreate_Should_Resolve_Command_From_Requested_Environment() {
		// Arrange
		IBusinessRuleService service = Substitute.For<IBusinessRuleService>();
		CreateEntityBusinessRuleCommand command = new(service, ConsoleLogger.Instance);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntityBusinessRuleCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);
		service.Create(Arg.Any<BusinessRuleCreateRequest>())
			.Returns(new BusinessRuleCreateResult("BusinessRule_1234567"));
		CreateEntityBusinessRuleTool tool = new(command, ConsoleLogger.Instance, commandResolver);
		BusinessRule rule = new(
			"Require owner for drafts",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "Status", null),
						"equal",
						new BusinessRuleExpression("Const", null,
							JsonSerializer.Deserialize<JsonElement>("\"Draft\"")))
				]),
			[
				new BusinessRuleAction("make-required", ["Owner"])
			]);

		// Act
		CommandExecutionResult result = tool.BusinessRuleCreate("dev", "UsrPkg", "UsrOrder", rule);

		// Assert
		result.ExitCode.Should().Be(0,
			because: "the environment-aware resolver should return a command instance for the requested MCP environment");
		result.Output.Select(message => (string)message.Value).Should().Contain("Rule name: BusinessRule_1234567",
			because: "the generated internal rule name should be available through the standard execution log stream");
		commandResolver.Received(1).Resolve<CreateEntityBusinessRuleCommand>(Arg.Is<EnvironmentOptions>(options =>
			options is CreateEntityBusinessRuleOptions
			&& options.Environment == "dev"
			&& string.IsNullOrWhiteSpace(options.Uri)));
	}

	[Test]
	[Category("Unit")]
	[Description("Marks each top-level create-entity-business-rule MCP parameter as required so the MCP contract advertises the direct tool shape clearly.")]
	[TestCase("environmentName")]
	[TestCase("packageName")]
	[TestCase("entitySchemaName")]
	[TestCase("rule")]
	public void BusinessRuleCreate_Should_Expose_Required_Top_Level_Parameters(string parameterName) {
		// Arrange
		ParameterInfo parameter = typeof(CreateEntityBusinessRuleTool)
			.GetMethod(nameof(CreateEntityBusinessRuleTool.BusinessRuleCreate))!
			.GetParameters()
			.Single(candidate => candidate.Name == parameterName);

		// Act
		object[] requiredAttributes = parameter.GetCustomAttributes(typeof(RequiredAttribute), inherit: false);

		// Assert
		requiredAttributes.Should().ContainSingle(
			because: "the MCP tool should expose direct required parameters instead of a single outer args envelope");
	}

	[Test]
	[Category("Unit")]
	[Description("Deserializes the direct-parameter business-rule payload and preserves lookup constants as string GUID values.")]
	public void BusinessRuleCreate_Should_Deserialize_Direct_Parameter_Payload_With_Lookup_Value_Expression() {
		// Arrange
		string payload = """
		{
		  "environment-name": "dev",
		  "package-name": "UsrPkg",
		  "entity-schema-name": "UsrOrder",
		  "rule": {
		    "caption": "Require status for owner",
		    "condition": {
		      "logicalOperation": "AND",
		      "conditions": [
		        {
		          "leftExpression": {
		            "type": "AttributeValue",
		            "path": "Owner"
		          },
		          "comparisonType": "equal",
		          "rightExpression": {
		            "type": "Const",
		            "value": "11111111-1111-1111-1111-111111111111"
		          }
		        }
		      ]
		    },
		    "actions": [
		      {
		        "type": "make-required",
		        "items": [ "Status" ]
		      }
		    ]
		  }
		}
		""";

		// Act
		BusinessRuleToolPayload? payloadArgs = JsonSerializer.Deserialize<BusinessRuleToolPayload>(payload);

		// Assert
		payloadArgs.Should().NotBeNull(
			because: "the direct-parameter business-rule payload should deserialize from JSON");
		payloadArgs!.Rule.Condition.Conditions[0].LeftExpression.Type.Should().Be("AttributeValue",
			because: "path-based operands should preserve the AttributeValue type");
		payloadArgs.Rule.Condition.Conditions[0].LeftExpression.Path.Should().Be("Owner",
			because: "the attribute path should be preserved after deserialization");
		payloadArgs.Rule.Condition.Conditions[0].RightExpression.Type.Should().Be("Const",
			because: "constant operands should preserve the Const type");
		payloadArgs.Rule.Condition.Conditions[0].RightExpression.Value.HasValue.Should().BeTrue(
			because: "lookup constant payloads should preserve the GUID string value for downstream validation");
		payloadArgs.Rule.Condition.Conditions[0].RightExpression.Value!.Value.ValueKind.Should().Be(JsonValueKind.String,
			because: "lookup constants are only supported as string GUIDs on the MCP surface");
	}

	[Test]
	[Category("Unit")]
	[Description("Deserializes unary business-rule payloads that omit rightExpression for filled-state comparisons.")]
	public void BusinessRuleCreate_Should_Deserialize_Unary_Payload_Without_Right_Expression() {
		// Arrange
		string payload = """
		{
		  "environment-name": "dev",
		  "package-name": "UsrPkg",
		  "entity-schema-name": "UsrOrder",
		  "rule": {
		    "caption": "Lock status when name is filled",
		    "condition": {
		      "logicalOperation": "AND",
		      "conditions": [
		        {
		          "leftExpression": {
		            "type": "AttributeValue",
		            "path": "Name"
		          },
		          "comparisonType": "is-filled-in"
		        }
		      ]
		    },
		    "actions": [
		      {
		        "type": "make-read-only",
		        "items": [ "Status" ]
		      }
		    ]
		  }
		}
		""";

		// Act
		BusinessRuleToolPayload? payloadArgs = JsonSerializer.Deserialize<BusinessRuleToolPayload>(payload);

		// Assert
		payloadArgs.Should().NotBeNull(
			because: "unary business-rule payloads should deserialize from JSON without a rightExpression");
		payloadArgs!.Rule.Condition.Conditions[0].ComparisonType.Should().Be("is-filled-in",
			because: "the filled-state comparison should preserve its wire value");
		payloadArgs.Rule.Condition.Conditions[0].RightExpression.Should().BeNull(
			because: "unary comparisons should omit the rightExpression payload");
	}

	private sealed record BusinessRuleToolPayload(
		[property: System.Text.Json.Serialization.JsonPropertyName("environment-name")] string EnvironmentName,
		[property: System.Text.Json.Serialization.JsonPropertyName("package-name")] string PackageName,
		[property: System.Text.Json.Serialization.JsonPropertyName("entity-schema-name")] string EntitySchemaName,
		[property: System.Text.Json.Serialization.JsonPropertyName("rule")] BusinessRule Rule);
}
