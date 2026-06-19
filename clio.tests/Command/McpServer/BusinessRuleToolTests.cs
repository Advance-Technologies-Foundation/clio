using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
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
	[Ignore("ENG-90312 Phase 2: tool folded into clio-run; safety flags now reflected on clio-run itself. Polymorphic registry validated by Z7 schema-discovery test.")]
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
		CommandExecutionResult result = tool.BusinessRuleCreate(new CreateEntityBusinessRuleRunArgs {
			EnvironmentName = "dev",
			PackageName = "UsrPkg",
			EntitySchemaName = "UsrOrder",
			Rule = rule
		});

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
		CommandExecutionResult result = tool.BusinessRuleCreate(new CreateEntityBusinessRuleRunArgs {
			EnvironmentName = "dev",
			PackageName = "UsrPkg",
			EntitySchemaName = "UsrOrder",
			Rule = rule
		});

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
	[Description("Marks create-entity-business-rule as a single required args wrapper so runtime calls use the same shape as other clio MCP tools.")]
	public void BusinessRuleCreate_Should_Expose_Required_Args_Wrapper() {
		// Arrange
		ParameterInfo parameter = typeof(CreateEntityBusinessRuleTool)
			.GetMethod(nameof(CreateEntityBusinessRuleTool.BusinessRuleCreate))!
			.GetParameters()
			.Single();

		// Act
		object[] requiredAttributes = parameter.GetCustomAttributes(typeof(RequiredAttribute), inherit: false);

		// Assert
		parameter.Name.Should().Be("args",
			because: "the runtime MCP schema should require the standard args wrapper instead of flat top-level fields");
		parameter.ParameterType.Should().Be(typeof(CreateEntityBusinessRuleRunArgs),
			because: "the wrapper type should own the business-rule input fields and their JSON names");
		requiredAttributes.Should().ContainSingle(
			because: "the MCP tool should require the args wrapper");
	}

	[Test]
	[Category("Unit")]
	[Description("Exposes kebab-case required fields inside the create-entity-business-rule args wrapper.")]
	[TestCase(nameof(CreateEntityBusinessRuleRunArgs.EnvironmentName), "environment-name")]
	[TestCase(nameof(CreateEntityBusinessRuleRunArgs.PackageName), "package-name")]
	[TestCase(nameof(CreateEntityBusinessRuleRunArgs.EntitySchemaName), "entity-schema-name")]
	[TestCase(nameof(CreateEntityBusinessRuleRunArgs.Rule), "rule")]
	public void BusinessRuleCreateArgs_Should_Expose_Required_Kebab_Case_Fields(
		string propertyName,
		string jsonName) {
		// Arrange
		PropertyInfo property = typeof(CreateEntityBusinessRuleRunArgs).GetProperty(propertyName)!;

		// Act
		string actualJsonName = property.GetCustomAttributes(typeof(JsonPropertyNameAttribute), inherit: false)
			.Cast<JsonPropertyNameAttribute>()
			.Single()
			.Name;

		// Assert
		actualJsonName.Should().Be(jsonName,
			because: "business-rule tool args should follow the same kebab-case MCP field convention as other clio tools");
		property.GetCustomAttributes(typeof(RequiredAttribute), inherit: false).Should().ContainSingle(
			because: "all entity business-rule creation fields are mandatory inside the args wrapper");
	}

	[Test]
	[Category("Unit")]
	[Description("Deserializes the args-wrapper business-rule payload and preserves lookup constants as string GUID values.")]
	public void BusinessRuleCreate_Should_Deserialize_Args_Wrapper_Payload_With_Lookup_Value_Expression() {
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
		CreateEntityBusinessRuleRunArgs? payloadArgs = JsonSerializer.Deserialize<CreateEntityBusinessRuleRunArgs>(payload);

		// Assert
		payloadArgs.Should().NotBeNull(
			because: "the args-wrapper business-rule payload should deserialize from JSON");
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
		CreateEntityBusinessRuleRunArgs? payloadArgs = JsonSerializer.Deserialize<CreateEntityBusinessRuleRunArgs>(payload);

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
		CreateEntityBusinessRuleRunArgs? payloadArgs = JsonSerializer.Deserialize<CreateEntityBusinessRuleRunArgs>(payload);

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
	[Description("Deserializes set-values actions with AttributeValue source payloads and preserves direct and forward source paths.")]
	public void BusinessRuleCreate_Should_Deserialize_SetValues_Action_With_Attribute_Value_Item() {
		// Arrange
		string payload = """
		{
		  "environment-name": "dev",
		  "package-name": "UsrPkg",
		  "entity-schema-name": "UsrOrder",
		  "rule": {
		    "caption": "Copy values",
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
		              "path": "UsrCreatorAge"
		            },
		            "value": {
		              "type": "AttributeValue",
		              "path": "CreatedBy.Age"
		            }
		          }
		        ]
		      }
		    ]
		  }
		}
		""";

		// Act
		CreateEntityBusinessRuleRunArgs? payloadArgs = JsonSerializer.Deserialize<CreateEntityBusinessRuleRunArgs>(payload);

		// Assert
		payloadArgs.Should().NotBeNull(
			because: "set-values attribute-source business-rule payloads should deserialize from the MCP JSON shape");
		BusinessRuleSetValueItem item = payloadArgs!.Rule.ToBusinessRule().Actions.Single().SetValueItems.Single();
		item.Expression.Path.Should().Be("UsrCreatorAge",
			because: "the target expression path should be preserved");
		item.Value.Type.Should().Be("AttributeValue",
			because: "the attribute-source discriminator should be preserved for downstream metadata conversion");
		item.Value.Path.Should().Be("CreatedBy.Age",
			because: "forward reference source paths should survive MCP payload binding");
	}

	[Test]
	[Category("Unit")]
	[Description("Deserializes a condition with a system-variable right expression and preserves the SysValue type and sysValueName.")]
	public void BusinessRuleCreate_Should_Deserialize_Condition_With_SysValue_Right_Expression() {
		// Arrange
		string payload = """
		{
		  "environment-name": "dev",
		  "package-name": "UsrPkg",
		  "entity-schema-name": "UsrOrder",
		  "rule": {
		    "caption": "Require status when owner is the current user",
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
		            "type": "SysValue",
		            "sysValueName": "CurrentUserContact"
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
		CreateEntityBusinessRuleRunArgs? payloadArgs = JsonSerializer.Deserialize<CreateEntityBusinessRuleRunArgs>(payload);

		// Assert
		payloadArgs.Should().NotBeNull(
			because: "system-variable business-rule payloads should deserialize from the MCP JSON shape");
		BusinessRuleExpression rightExpression = payloadArgs!.Rule.ToBusinessRule().Condition.Conditions[0].RightExpression!;
		rightExpression.Type.Should().Be("SysValue",
			because: "the SysValue discriminator should be preserved through MCP payload binding");
		rightExpression.SysValueName.Should().Be("CurrentUserContact",
			because: "the selected system variable name should survive MCP payload binding for downstream conversion");
		rightExpression.Value.Should().BeNull(
			because: "system-variable right expressions carry no static constant value");
	}

	[Test]
	[Category("Unit")]
	[Description("Deserializes a role-gate condition with a system variable on the left and a contain comparison.")]
	public void BusinessRuleCreate_Should_Deserialize_Condition_With_SysValue_Left_And_Contain() {
		// Arrange
		string payload = """
		{
		  "environment-name": "dev",
		  "package-name": "UsrPkg",
		  "entity-schema-name": "UsrOrder",
		  "rule": {
		    "caption": "Require status for administrators",
		    "condition": {
		      "logicalOperation": "AND",
		      "conditions": [
		        {
		          "leftExpression": {
		            "type": "SysValue",
		            "sysValueName": "CurrentUserRoles"
		          },
		          "comparisonType": "contain",
		          "rightExpression": {
		            "type": "Const",
		            "value": "83a43ebc-f36b-1410-298d-001e8c82bcad"
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
		CreateEntityBusinessRuleRunArgs? payloadArgs = JsonSerializer.Deserialize<CreateEntityBusinessRuleRunArgs>(payload);

		// Assert
		payloadArgs.Should().NotBeNull(
			because: "role-gate business-rule payloads should deserialize from the MCP JSON shape");
		BusinessRuleCondition condition = payloadArgs!.Rule.ToBusinessRule().Condition.Conditions[0];
		condition.ComparisonType.Should().Be("contain",
			because: "the contain comparison should survive MCP payload binding");
		condition.LeftExpression.Type.Should().Be("SysValue",
			because: "a system variable on the left should bind as a SysValue expression");
		condition.LeftExpression.SysValueName.Should().Be("CurrentUserRoles",
			because: "the role-collection system variable name should survive binding");
		condition.RightExpression!.Type.Should().Be("Const",
			because: "the compared role should bind as a constant");
		condition.RightExpression.Value!.Value.ValueKind.Should().Be(JsonValueKind.String,
			because: "the role record id is a GUID string");
	}

	[Test]
	[Category("Unit")]
	[Description("Deserializes apply-filter actions and preserves target/source lookup settings for downstream validation and conversion.")]
	public void BusinessRuleCreate_Should_Deserialize_ApplyFilter_Action() {
		// Arrange
		string payload = """
		{
		  "environment-name": "dev",
		  "package-name": "UsrPkg",
		  "entity-schema-name": "UsrOrder",
		  "rule": {
		    "caption": "Filter city by country",
		    "condition": {
		      "logicalOperation": "AND",
		      "conditions": []
		    },
		    "actions": [
		      {
		        "type": "apply-filter",
		        "target": "City",
		        "targetFilterPath": "Country",
		        "source": "Country",
		        "sourceFilterPath": "TimeZone",
		        "clearValue": true,
		        "populateValue": true
		      }
		    ]
		  }
		}
		""";

		// Act
		CreateEntityBusinessRuleRunArgs? payloadArgs = JsonSerializer.Deserialize<CreateEntityBusinessRuleRunArgs>(payload);

		// Assert
		payloadArgs.Should().NotBeNull(
			because: "apply-filter payloads should deserialize from the MCP JSON shape");
		ApplyFilterBusinessRuleAction action = payloadArgs!.Rule.ToBusinessRule().Actions.Single()
			.Should().BeOfType<ApplyFilterBusinessRuleAction>(
				because: "the MCP action discriminator should map apply-filter payloads to the shared model").Subject;
		action.Target.Should().Be("City",
			because: "the target lookup root should be preserved");
		action.TargetFilterPath.Should().Be("Country",
			because: "the target-side filter path should be preserved");
		action.Source.Should().Be("Country",
			because: "the source lookup root should be preserved");
		action.SourceFilterPath.Should().Be("TimeZone",
			because: "the optional source filter path should survive MCP payload binding when provided");
		action.ClearValue.Should().BeTrue(
			because: "clearValue should survive MCP payload binding");
		action.PopulateValue.Should().BeTrue(
			because: "populateValue should survive MCP payload binding");
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
		CreateEntityBusinessRuleRunArgs? payloadArgs = JsonSerializer.Deserialize<CreateEntityBusinessRuleRunArgs>(payload);

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
		CommandExecutionResult result = tool.BusinessRuleCreate(new CreateEntityBusinessRuleRunArgs {
			EnvironmentName = "dev",
			PackageName = "UsrPkg",
			EntitySchemaName = "UsrOrder",
			Rule = null!
		});

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
	[Ignore("ENG-90312 Phase 2: tool folded into clio-run; safety flags now reflected on clio-run itself. Polymorphic registry validated by Z7 schema-discovery test.")]
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
		CommandExecutionResult result = tool.BusinessRuleCreate(new CreatePageBusinessRuleRunArgs {
			EnvironmentName = "dev",
			PackageName = "UsrPkg",
			PageSchemaName = "UsrCase_FormPage",
			Rule = rule
		});

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
		CommandExecutionResult result = tool.BusinessRuleCreate(new CreatePageBusinessRuleRunArgs {
			EnvironmentName = "dev",
			PackageName = "UsrPkg",
			PageSchemaName = "UsrCase_FormPage",
			Rule = rule
		});

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
		CommandExecutionResult result = tool.BusinessRuleCreate(new CreatePageBusinessRuleRunArgs {
			EnvironmentName = "dev",
			PackageName = "UsrPkg",
			PageSchemaName = "UsrCase_FormPage",
			Rule = rule
		});

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
	[Description("Marks create-page-business-rule as a single required args wrapper so runtime calls use the same shape as other clio MCP tools.")]
	public void PageBusinessRuleCreate_Should_Expose_Required_Args_Wrapper() {
		// Arrange
		ParameterInfo parameter = typeof(CreatePageBusinessRuleTool)
			.GetMethod(nameof(CreatePageBusinessRuleTool.BusinessRuleCreate))!
			.GetParameters()
			.Single();

		// Act
		object[] requiredAttributes = parameter.GetCustomAttributes(typeof(RequiredAttribute), inherit: false);

		// Assert
		parameter.Name.Should().Be("args",
			because: "the runtime MCP schema should require the standard args wrapper instead of flat top-level fields");
		parameter.ParameterType.Should().Be(typeof(CreatePageBusinessRuleRunArgs),
			because: "the wrapper type should own the page business-rule input fields and their JSON names");
		requiredAttributes.Should().ContainSingle(
			because: "the MCP tool should require the args wrapper");
	}

	[Test]
	[Category("Unit")]
	[Description("Exposes kebab-case required fields inside the create-page-business-rule args wrapper.")]
	[TestCase(nameof(CreatePageBusinessRuleRunArgs.EnvironmentName), "environment-name")]
	[TestCase(nameof(CreatePageBusinessRuleRunArgs.PackageName), "package-name")]
	[TestCase(nameof(CreatePageBusinessRuleRunArgs.PageSchemaName), "page-schema-name")]
	[TestCase(nameof(CreatePageBusinessRuleRunArgs.Rule), "rule")]
	public void PageBusinessRuleCreateArgs_Should_Expose_Required_Kebab_Case_Fields(
		string propertyName,
		string jsonName) {
		// Arrange
		PropertyInfo property = typeof(CreatePageBusinessRuleRunArgs).GetProperty(propertyName)!;

		// Act
		string actualJsonName = property.GetCustomAttributes(typeof(JsonPropertyNameAttribute), inherit: false)
			.Cast<JsonPropertyNameAttribute>()
			.Single()
			.Name;

		// Assert
		actualJsonName.Should().Be(jsonName,
			because: "page business-rule tool args should follow the same kebab-case MCP field convention as other clio tools");
		property.GetCustomAttributes(typeof(RequiredAttribute), inherit: false).Should().ContainSingle(
			because: "all page business-rule creation fields are mandatory inside the args wrapper");
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
		CreatePageBusinessRuleRunArgs? payloadArgs = JsonSerializer.Deserialize<CreatePageBusinessRuleRunArgs>(payload);

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
		CommandExecutionResult result = tool.BusinessRuleCreate(new CreatePageBusinessRuleRunArgs {
			EnvironmentName = "dev",
			PackageName = "UsrPkg",
			PageSchemaName = "UsrCase_FormPage",
			Rule = null!
		});

		// Assert
		result.ExitCode.Should().Be(1,
			because: "a null page rule should be handled by command validation instead of throwing in the MCP adapter");
		result.Output.Select(message => (string)message.Value).Should().Contain("rule is required.",
			because: "callers should receive the standard command-layer validation error");
		service.DidNotReceiveWithAnyArgs().Create(default!);
	}

}

