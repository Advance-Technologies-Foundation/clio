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
		IEntityBusinessRuleService service = Substitute.For<IEntityBusinessRuleService>();
		CreateEntityBusinessRuleCommand command = new(service, ConsoleLogger.Instance);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntityBusinessRuleCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);
		service.Create(Arg.Any<EntityBusinessRuleCreateRequest>())
			.Returns(new BusinessRuleCreateResult("BusinessRule_1234567"));
		CreateEntityBusinessRuleTool tool = new(command, ConsoleLogger.Instance, commandResolver);
		EntityBusinessRuleMcpContract rule = new(
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
				new EntityMakeRequiredBusinessRuleActionMcpContract([" Owner ", "Amount", "Owner"]),
				new EntityMakeReadOnlyBusinessRuleActionMcpContract(["Status"])
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
			Arg.Is<EntityBusinessRuleCreateRequest>(request =>
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
		IEntityBusinessRuleService service = Substitute.For<IEntityBusinessRuleService>();
		CreateEntityBusinessRuleCommand command = new(service, ConsoleLogger.Instance);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntityBusinessRuleCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);
		service.Create(Arg.Any<EntityBusinessRuleCreateRequest>())
			.Returns(new BusinessRuleCreateResult("BusinessRule_1234567"));
		CreateEntityBusinessRuleTool tool = new(command, ConsoleLogger.Instance, commandResolver);
		EntityBusinessRuleMcpContract rule = new(
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
				new EntityMakeRequiredBusinessRuleActionMcpContract(["Owner"])
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
		BusinessRule rule = payloadArgs!.Rule.ToBusinessRule();
		rule.Condition.Conditions[0].LeftExpression.Type.Should().Be("AttributeValue",
			because: "path-based operands should preserve the AttributeValue type");
		rule.Condition.Conditions[0].LeftExpression.Path.Should().Be("Owner",
			because: "the attribute path should be preserved after deserialization");
		rule.Condition.Conditions[0].RightExpression.Type.Should().Be("Const",
			because: "constant operands should preserve the Const type");
		rule.Condition.Conditions[0].RightExpression.Value.HasValue.Should().BeTrue(
			because: "lookup constant payloads should preserve the GUID string value for downstream validation");
		rule.Condition.Conditions[0].RightExpression.Value!.Value.ValueKind.Should().Be(JsonValueKind.String,
			because: "lookup constants are only supported as string GUIDs on the MCP surface");
		rule.Actions.Single().FieldSelectionItems.Should().Equal(["Status"],
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
		BusinessRuleAction action = payloadArgs!.Rule.ToBusinessRule().Actions.Single();
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
	[Description("Deserializes set-values actions with Formula payloads and preserves the agent-friendly formula text.")]
	public void BusinessRuleCreate_Should_Deserialize_SetValues_Action_With_Formula_Item() {
		// Arrange
		string payload = """
		{
		  "environment-name": "dev",
		  "package-name": "UsrPkg",
		  "entity-schema-name": "UsrOrder",
		  "rule": {
		    "caption": "Calculate totals",
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
		              "path": "UsrTotal"
		            },
		            "value": {
		              "type": "Formula",
		              "expression": "UsrAmount + UsrTax"
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
			because: "set-values formula business-rule payloads should deserialize from the MCP JSON shape");
		BusinessRuleSetValueItem item = payloadArgs!.Rule.ToBusinessRule().Actions.Single().SetValueItems.Single();
		item.Expression.Path.Should().Be("UsrTotal",
			because: "the target expression path should be preserved");
		item.Value.Type.Should().Be("Formula",
			because: "the formula discriminator should be preserved for downstream expression-schema translation");
		item.Value.Expression.Should().Be("UsrAmount + UsrTax",
			because: "agents should send simple formula text, not prebuilt BRF2 metadata");
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
		BusinessRule rule = payloadArgs!.Rule.ToBusinessRule();
		rule.Condition.Conditions[0].ComparisonType.Should().Be("is-filled-in",
			because: "the filled-state comparison should preserve its wire value");
		rule.Condition.Conditions[0].RightExpression.Should().BeNull(
			because: "unary comparisons should omit the rightExpression payload");
	}

	[Test]
	[Category("Unit")]
	[Description("Converts omitted entity actions to an empty model list so command validation returns the standard action error.")]
	public void BusinessRuleCreate_Should_Convert_Null_Entity_Actions_To_Empty_Model_List() {
		// Arrange
		EntityBusinessRuleMcpContract rule = new() {
			Caption = "Missing actions",
			Condition = new BusinessRuleConditionGroup("AND", []),
			Actions = null!
		};

		// Act
		BusinessRule result = rule.ToBusinessRule();

		// Assert
		result.Actions.Should().BeEmpty(
			because: "malformed MCP payloads should reach BusinessRuleValidator instead of failing with a null reference during contract conversion");
	}

	[Test]
	[Category("Unit")]
	[Description("Preserves null entity action entries so command validation rejects the malformed payload instead of dropping actions before mutation.")]
	public void BusinessRuleCreate_Should_Preserve_Null_Entity_Action_Entries_For_Validation() {
		// Arrange
		EntityBusinessRuleMcpContract rule = new(
			"Partially malformed actions",
			new BusinessRuleConditionGroup("AND", []),
			[
				new EntityMakeRequiredBusinessRuleActionMcpContract(["Owner"]),
				null!
			]);

		// Act
		BusinessRule result = rule.ToBusinessRule();

		// Assert
		result.Actions.Should().HaveCount(2,
			because: "the MCP adapter must not silently drop malformed action entries before command validation");
		result.Actions[1].Should().BeNull(
			because: "BusinessRuleValidator should reject the null action entry with the standard action type error");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the standard command validation error when create-entity-business-rule receives a null rule payload.")]
	public void BusinessRuleCreate_Should_Return_Command_Error_When_Entity_Rule_Is_Null() {
		// Arrange
		IEntityBusinessRuleService service = Substitute.For<IEntityBusinessRuleService>();
		CreateEntityBusinessRuleCommand command = new(service, ConsoleLogger.Instance);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntityBusinessRuleCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);
		CreateEntityBusinessRuleTool tool = new(command, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.BusinessRuleCreate("dev", "UsrPkg", "UsrOrder", null!);

		// Assert
		result.ExitCode.Should().Be(1,
			because: "a null rule should be handled by command validation instead of throwing in the MCP adapter");
		result.Output.Select(message => (string)message.Value).Should().Contain("rule is required.",
			because: "callers should receive the standard command-layer validation error");
		service.DidNotReceiveWithAnyArgs().Create(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("Advertises a stable MCP tool name for create-page-business-rule.")]
	public void PageBusinessRuleCreate_Should_Advertise_Stable_Tool_Name() {
		// Arrange

		// Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(CreatePageBusinessRuleTool)
			.GetMethod(nameof(CreatePageBusinessRuleTool.BusinessRuleCreate))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be(CreatePageBusinessRuleTool.BusinessRuleCreateToolName,
			because: "the MCP tool name must stay stable for callers and tests");
		attribute.ReadOnly.Should().BeFalse(
			because: "creating page business rules mutates remote Creatio metadata");
		attribute.Destructive.Should().BeTrue(
			because: "the tool changes the target page add-on state");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps the create-page-business-rule payload into the page business-rule service request and returns a success exit code.")]
	public void PageBusinessRuleCreate_Should_Map_Arguments_And_Return_Success_Exit_Code() {
		// Arrange
		IPageBusinessRuleService service = Substitute.For<IPageBusinessRuleService>();
		CreatePageBusinessRuleCommand command = new(service, ConsoleLogger.Instance);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreatePageBusinessRuleCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);
		service.Create(Arg.Any<PageBusinessRuleCreateRequest>())
			.Returns(new BusinessRuleCreateResult("BusinessRule_7654321"));
		CreatePageBusinessRuleTool tool = new(command, ConsoleLogger.Instance, commandResolver);
		PageBusinessRuleMcpContract rule = new(
			"Show escalation when priority is high",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "PDS_Priority", null),
						"equal",
						new BusinessRuleExpression("Const", null,
							JsonSerializer.Deserialize<JsonElement>("\"High\"")))
				]),
			[
				new PageShowElementBusinessRuleActionMcpContract(["EscalateButton"])
			]);

		// Act
		CommandExecutionResult result = tool.BusinessRuleCreate("dev", "UsrPkg", "UsrCase_FormPage", rule);

		// Assert
		result.ExitCode.Should().Be(0,
			because: "a successful page business-rule service call should return the standard success exit code");
		result.Output.Select(message => (string)message.Value).Should().Contain("Rule name: BusinessRule_7654321",
			because: "the MCP tool should preserve the generated internal rule name in execution logs");
		commandResolver.Received(1).Resolve<CreatePageBusinessRuleCommand>(Arg.Is<EnvironmentOptions>(options =>
			options.Environment == "dev"));
		service.Received(1).Create(
			Arg.Is<PageBusinessRuleCreateRequest>(request =>
				request.PackageName == "UsrPkg"
				&& request.PageSchemaName == "UsrCase_FormPage"
				&& request.Rule.Caption == "Show escalation when priority is high"
				&& request.Rule.Condition.LogicalOperation == "AND"
				&& request.Rule.Actions.Count == 1
				&& request.Rule.Actions[0].ActionType == "show-element"
				&& request.Rule.Actions[0].FieldSelectionItems.Single() == "EscalateButton"));
	}

	[Test]
	[Category("Unit")]
	[Description("Maps a page field-state action payload into the page business-rule service request without breaking page element items.")]
	public void PageBusinessRuleCreate_Should_Map_Field_State_Action_Arguments() {
		// Arrange
		IPageBusinessRuleService service = Substitute.For<IPageBusinessRuleService>();
		CreatePageBusinessRuleCommand command = new(service, ConsoleLogger.Instance);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreatePageBusinessRuleCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);
		service.Create(Arg.Any<PageBusinessRuleCreateRequest>())
			.Returns(new BusinessRuleCreateResult("BusinessRule_7654321"));
		CreatePageBusinessRuleTool tool = new(command, ConsoleLogger.Instance, commandResolver);
		PageBusinessRuleMcpContract rule = new(
			"Make escalation read-only when priority is high",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "PDS_Priority", null),
						"equal",
						new BusinessRuleExpression("Const", null,
							JsonSerializer.Deserialize<JsonElement>("\"High\"")))
				]),
			[
				new PageMakeReadOnlyBusinessRuleActionMcpContract(["EscalateButton"])
			]);

		// Act
		CommandExecutionResult result = tool.BusinessRuleCreate("dev", "UsrPkg", "UsrCase_FormPage", rule);

		// Assert
		result.ExitCode.Should().Be(0,
			because: "a successful page business-rule service call should return the standard success exit code");
		service.Received(1).Create(
			Arg.Is<PageBusinessRuleCreateRequest>(request =>
				request.PackageName == "UsrPkg"
				&& request.PageSchemaName == "UsrCase_FormPage"
				&& request.Rule.Actions.Count == 1
				&& request.Rule.Actions[0].ActionType == "make-read-only"
				&& request.Rule.Actions[0].FieldSelectionItems.Single() == "EscalateButton"));
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves the page business-rule command from the payload environment so nested dependencies use the requested MCP target.")]
	public void PageBusinessRuleCreate_Should_Resolve_Command_From_Requested_Environment() {
		// Arrange
		IPageBusinessRuleService service = Substitute.For<IPageBusinessRuleService>();
		CreatePageBusinessRuleCommand command = new(service, ConsoleLogger.Instance);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreatePageBusinessRuleCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);
		service.Create(Arg.Any<PageBusinessRuleCreateRequest>())
			.Returns(new BusinessRuleCreateResult("BusinessRule_7654321"));
		CreatePageBusinessRuleTool tool = new(command, ConsoleLogger.Instance, commandResolver);
		PageBusinessRuleMcpContract rule = new(
			"Hide escalation when priority is low",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "PDS_Priority", null),
						"equal",
						new BusinessRuleExpression("Const", null,
							JsonSerializer.Deserialize<JsonElement>("\"Low\"")))
				]),
			[
				new PageHideElementBusinessRuleActionMcpContract(["EscalateButton"])
			]);

		// Act
		CommandExecutionResult result = tool.BusinessRuleCreate("dev", "UsrPkg", "UsrCase_FormPage", rule);

		// Assert
		result.ExitCode.Should().Be(0,
			because: "the environment-aware resolver should return a command instance for the requested MCP environment");
		commandResolver.Received(1).Resolve<CreatePageBusinessRuleCommand>(Arg.Is<EnvironmentOptions>(options =>
			options is CreatePageBusinessRuleOptions
			&& options.Environment == "dev"
			&& string.IsNullOrWhiteSpace(options.Uri)));
	}

	[Test]
	[Category("Unit")]
	[Description("Marks each top-level create-page-business-rule MCP parameter as required so the MCP contract advertises the direct tool shape clearly.")]
	[TestCase("environmentName")]
	[TestCase("packageName")]
	[TestCase("pageSchemaName")]
	[TestCase("rule")]
	public void PageBusinessRuleCreate_Should_Expose_Required_Top_Level_Parameters(string parameterName) {
		// Arrange
		ParameterInfo parameter = typeof(CreatePageBusinessRuleTool)
			.GetMethod(nameof(CreatePageBusinessRuleTool.BusinessRuleCreate))!
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
	[Description("Deserializes create-page-business-rule page action payloads without accepting object-only action branches.")]
	public void PageBusinessRuleCreate_Should_Deserialize_Page_Action_Payload() {
		// Arrange
		string payload = """
		{
		  "environment-name": "dev",
		  "package-name": "UsrPkg",
		  "page-schema-name": "UsrCase_FormPage",
		  "rule": {
		    "caption": "Hide escalation for low priority",
		    "condition": {
		      "logicalOperation": "AND",
		      "conditions": [
		        {
		          "leftExpression": {
		            "type": "AttributeValue",
		            "path": "PDS_Priority"
		          },
		          "comparisonType": "equal",
		          "rightExpression": {
		            "type": "Const",
		            "value": "Low"
		          }
		        }
		      ]
		    },
		    "actions": [
		      {
		        "type": "hide-element",
		        "items": [ "EscalateButton" ]
		      },
		      {
		        "type": "make-editable",
		        "items": [ "PriorityInput" ]
		      },
		      {
		        "type": "make-read-only",
		        "items": [ "AmountInput" ]
		      },
		      {
		        "type": "make-required",
		        "items": [ "CloseDateInput" ]
		      },
		      {
		        "type": "make-optional",
		        "items": [ "CommentInput" ]
		      }
		    ]
		  }
		}
		""";

		// Act
		PageBusinessRuleToolPayload? payloadArgs = JsonSerializer.Deserialize<PageBusinessRuleToolPayload>(payload);

		// Assert
		payloadArgs.Should().NotBeNull(
			because: "page business-rule payloads should deserialize from the MCP JSON shape");
		BusinessRule rule = payloadArgs!.Rule.ToBusinessRule();
		rule.Actions.Select(action => action.ActionType).Should().Equal([
				"hide-element",
				"make-editable",
				"make-read-only",
				"make-required",
				"make-optional"
			],
			because: "the page action discriminators should be preserved");
		rule.Actions.SelectMany(action => action.FieldSelectionItems).Should().Equal([
				"EscalateButton",
				"PriorityInput",
				"AmountInput",
				"CloseDateInput",
				"CommentInput"
			],
			because: "page element action items should deserialize as target element names");
	}

	[Test]
	[Category("Unit")]
	[Description("Converts null page action items to an empty model list so command validation returns the standard item error.")]
	public void PageBusinessRuleCreate_Should_Convert_Null_Page_Action_Items_To_Empty_Model_List() {
		// Arrange
		PageBusinessRuleMcpContract rule = new(
			"Missing page items",
			new BusinessRuleConditionGroup("AND", []),
			[
				new PageHideElementBusinessRuleActionMcpContract {
					Items = null!
				}
			]);

		// Act
		BusinessRule result = rule.ToBusinessRule();

		// Assert
		result.Actions.Single().FieldSelectionItems.Should().BeEmpty(
			because: "malformed page MCP action items should reach BusinessRuleValidator instead of failing with a null reference during contract conversion");
	}

	[Test]
	[Category("Unit")]
	[Description("Converts null entity set-values items to an empty model list so command validation returns the standard item error.")]
	public void BusinessRuleCreate_Should_Convert_Null_SetValues_Items_To_Empty_Model_List() {
		// Arrange
		EntityBusinessRuleMcpContract rule = new(
			"Missing set-values items",
			new BusinessRuleConditionGroup("AND", []),
			[
				new EntitySetValuesBusinessRuleActionMcpContract {
					Items = null!
				}
			]);

		// Act
		BusinessRule result = rule.ToBusinessRule();

		// Assert
		result.Actions.Single().SetValueItems.Should().BeEmpty(
			because: "malformed set-values MCP action items should reach BusinessRuleValidator instead of failing with a null reference during contract conversion");
	}

	[Test]
	[Category("Unit")]
	[Description("Converts omitted page actions to an empty model list so command validation returns the standard action error.")]
	public void PageBusinessRuleCreate_Should_Convert_Null_Page_Actions_To_Empty_Model_List() {
		// Arrange
		PageBusinessRuleMcpContract rule = new() {
			Caption = "Missing actions",
			Condition = new BusinessRuleConditionGroup("AND", []),
			Actions = null!
		};

		// Act
		BusinessRule result = rule.ToBusinessRule();

		// Assert
		result.Actions.Should().BeEmpty(
			because: "malformed page MCP payloads should reach BusinessRuleValidator instead of failing with a null reference during contract conversion");
	}

	[Test]
	[Category("Unit")]
	[Description("Preserves null page action entries so command validation rejects the malformed payload instead of dropping actions before mutation.")]
	public void PageBusinessRuleCreate_Should_Preserve_Null_Page_Action_Entries_For_Validation() {
		// Arrange
		PageBusinessRuleMcpContract rule = new(
			"Partially malformed page actions",
			new BusinessRuleConditionGroup("AND", []),
			[
				new PageHideElementBusinessRuleActionMcpContract(["NameInput"]),
				null!
			]);

		// Act
		BusinessRule result = rule.ToBusinessRule();

		// Assert
		result.Actions.Should().HaveCount(2,
			because: "the MCP adapter must not silently drop malformed page action entries before command validation");
		result.Actions[1].Should().BeNull(
			because: "BusinessRuleValidator should reject the null action entry with the standard action type error");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the standard command validation error when create-page-business-rule receives a null rule payload.")]
	public void PageBusinessRuleCreate_Should_Return_Command_Error_When_Page_Rule_Is_Null() {
		// Arrange
		IPageBusinessRuleService service = Substitute.For<IPageBusinessRuleService>();
		CreatePageBusinessRuleCommand command = new(service, ConsoleLogger.Instance);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreatePageBusinessRuleCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);
		CreatePageBusinessRuleTool tool = new(command, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.BusinessRuleCreate("dev", "UsrPkg", "UsrCase_FormPage", null!);

		// Assert
		result.ExitCode.Should().Be(1,
			because: "a null page rule should be handled by command validation instead of throwing in the MCP adapter");
		result.Output.Select(message => (string)message.Value).Should().Contain("rule is required.",
			because: "callers should receive the standard command-layer validation error");
		service.DidNotReceiveWithAnyArgs().Create(default!);
	}

	private sealed record BusinessRuleToolPayload(
		[property: System.Text.Json.Serialization.JsonPropertyName("environment-name")] string EnvironmentName,
		[property: System.Text.Json.Serialization.JsonPropertyName("package-name")] string PackageName,
		[property: System.Text.Json.Serialization.JsonPropertyName("entity-schema-name")] string EntitySchemaName,
		[property: System.Text.Json.Serialization.JsonPropertyName("rule")] EntityBusinessRuleMcpContract Rule);

	private sealed record PageBusinessRuleToolPayload(
		[property: System.Text.Json.Serialization.JsonPropertyName("environment-name")] string EnvironmentName,
		[property: System.Text.Json.Serialization.JsonPropertyName("package-name")] string PackageName,
		[property: System.Text.Json.Serialization.JsonPropertyName("page-schema-name")] string PageSchemaName,
		[property: System.Text.Json.Serialization.JsonPropertyName("rule")] PageBusinessRuleMcpContract Rule);
}

