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
				new MakeRequiredBusinessRuleAction([" Owner ", "Amount", "Owner"]),
				new MakeReadOnlyBusinessRuleAction(["Status"])
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
				&& request.Rule.Actions[0].ActionType == "make-required"
				&& request.Rule.Actions[0].FieldSelectionItems.Count == 3
				&& request.Rule.Actions[0].FieldSelectionItems[0] == " Owner "
				&& request.Rule.Actions[0].FieldSelectionItems[1] == "Amount"
				&& request.Rule.Actions[0].FieldSelectionItems[2] == "Owner"
				&& request.Rule.Actions[1].FieldSelectionItems.Count == 1
				&& request.Rule.Actions[1].FieldSelectionItems[0] == "Status"));
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
				new MakeRequiredBusinessRuleAction(["Owner"])
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
		payloadArgs.Rule.Actions.Single().FieldSelectionItems.Should().Equal(["Status"],
			because: "field-state action items should deserialize as the string-array branch of the action items union");
	}

	[Test]
	[Category("Unit")]
	[Description("Deserializes set-values actions with constant text number boolean and DateTime payloads.")]
	public void BusinessRuleCreate_Should_Deserialize_SetValues_Action_With_Constant_Items() {
		// Arrange
		string payload = """
		{
		  "environment-name": "dev",
		  "package-name": "UsrPkg",
		  "entity-schema-name": "UsrOrder",
		  "rule": {
		    "caption": "Populate defaults",
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
		        "type": "set-values",
		        "items": [
		          {
		            "expression": {
		              "type": "AttributeValue",
		              "path": "UsrTextResult"
		            },
		            "value": {
		              "type": "Const",
		              "value": "Ready"
		            }
		          },
		          {
		            "expression": {
		              "type": "AttributeValue",
		              "path": "UsrScore"
		            },
		            "value": {
		              "type": "Const",
		              "value": 42
		            }
		          },
		          {
		            "expression": {
		              "type": "AttributeValue",
		              "path": "UsrCompleted"
		            },
		            "value": {
		              "type": "Const",
		              "value": true
		            }
		          },
		          {
		            "expression": {
		              "type": "AttributeValue",
		              "path": "UsrPlannedOn"
		            },
		            "value": {
		              "type": "Const",
		              "value": "2025-01-01T00:00:00Z"
		            }
		          }
		        ]
		      }
		    ]
		  }
		}
		""";

		// Act
		BusinessRuleToolPayload? payloadArgs = JsonSerializer.Deserialize<BusinessRuleToolPayload>(payload);

		// Assert
		payloadArgs.Should().NotBeNull(
			because: "set-values business-rule payloads should deserialize from the MCP JSON shape");
		BusinessRuleAction action = payloadArgs!.Rule.Actions.Single();
		action.ActionType.Should().Be("set-values",
			because: "the action discriminator should be preserved");
		action.SetValueItems.Should().HaveCount(4,
			because: "each set-values assignment item should be bound separately");
		action.SetValueItems[0].Expression.Path.Should().Be("UsrTextResult",
			because: "the target expression path should be preserved");
		action.SetValueItems[0].Value.Value!.Value.ValueKind.Should().Be(JsonValueKind.String,
			because: "text constants should stay JSON strings");
		action.SetValueItems[1].Value.Value!.Value.ValueKind.Should().Be(JsonValueKind.Number,
			because: "number constants should stay JSON numbers");
		action.SetValueItems[2].Value.Value!.Value.ValueKind.Should().Be(JsonValueKind.True,
			because: "boolean constants should stay JSON booleans");
		action.SetValueItems[3].Value.Value!.Value.GetString().Should().Be("2025-01-01T00:00:00Z",
			because: "DateTime constants should preserve their raw timezone-aware string for validation");
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

