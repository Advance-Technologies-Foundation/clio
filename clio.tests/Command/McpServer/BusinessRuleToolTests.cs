using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;
using Clio;
using Clio.Command.BusinessRules;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class BusinessRuleToolTests {

	private const string ActionUId = "aaaaaaaa-0000-0000-0000-000000000001";

	[Test]
	[Category("Unit")]
	[Description("Advertises a stable MCP tool name for create-entity-business-rules.")]
	public void BusinessRuleCreate_Should_Advertise_Stable_Tool_Name() {
		// Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(CreateEntityBusinessRuleTool)
			.GetMethod(nameof(CreateEntityBusinessRuleTool.BusinessRuleCreate))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be(CreateEntityBusinessRuleTool.BusinessRuleCreateToolName,
			because: "the MCP tool name must stay stable for callers and tests");
		attribute.Name.Should().Be("create-entity-business-rules",
			because: "the batch entity tool is advertised under the plural name");
		attribute.ReadOnly.Should().BeFalse(
			because: "creating business rules mutates remote Creatio metadata");
		attribute.Destructive.Should().BeTrue(
			because: "the tool changes the target entity add-on state");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps the batch MCP payload into the business-rule service request and returns per-rule results.")]
	public void BusinessRuleCreate_Should_Map_Arguments_And_Return_Per_Rule_Results() {
		// Arrange
		IEntityBusinessRuleService service = Substitute.For<IEntityBusinessRuleService>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IEntityBusinessRuleService>(Arg.Any<EnvironmentOptions>()).Returns(service);
		service.Create(Arg.Any<BusinessRulesBatchRequest>())
			.Returns(new List<BusinessRuleBatchItemResult> {
				new("Require owner for drafts", true, "BusinessRule_1234567", null)
			});
		CreateEntityBusinessRuleTool tool = new(commandResolver, ConsoleLogger.Instance);
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
		BusinessRuleBatchResponse response = (BusinessRuleBatchResponse)tool.BusinessRuleCreate(new CreateEntityBusinessRulesArgs {
			EnvironmentName = "dev",
			PackageName = "UsrPkg",
			EntitySchemaName = "UsrOrder",
			Rules = [rule]
		});

		// Assert
		response.Succeeded.Should().Be(1, because: "the single rule was created");
		response.Failed.Should().Be(0);
		response.Results.Single().Name.Should().Be("Require owner for drafts");
		response.Results.Single().Success.Should().BeTrue();
		response.Results.Single().RuleName.Should().Be("BusinessRule_1234567",
			because: "the response should preserve the generated internal rule name");
		commandResolver.Received(1).Resolve<IEntityBusinessRuleService>(Arg.Is<EnvironmentOptions>(options =>
			options.Environment == "dev"));
		service.Received(1).Create(
			Arg.Is<BusinessRulesBatchRequest>(request =>
				request.PackageName == "UsrPkg"
				&& request.SchemaName == "UsrOrder"
				&& request.Rules.Count == 1
				&& request.Rules[0].Caption == "Require owner for drafts"
				&& request.Rules[0].Condition.LogicalOperation == "AND"
				&& request.Rules[0].Actions.Count == 2
				&& request.Rules[0].Actions[0].ActionType == "make-required"
				&& request.Rules[0].Actions[0].FieldSelectionItems.Count == 3
				&& request.Rules[0].Actions[0].FieldSelectionItems[0] == " Owner "
				&& request.Rules[0].Actions[1].FieldSelectionItems[0] == "Status"));
	}

	[Test]
	[Category("Unit")]
	[Description("Maps every rule of a multi-rule batch into a single service request.")]
	public void BusinessRuleCreate_Should_Map_Multiple_Rules_Into_One_Request() {
		// Arrange
		IEntityBusinessRuleService service = Substitute.For<IEntityBusinessRuleService>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IEntityBusinessRuleService>(Arg.Any<EnvironmentOptions>()).Returns(service);
		service.Create(Arg.Any<BusinessRulesBatchRequest>())
			.Returns(new List<BusinessRuleBatchItemResult> {
				new("Rule A", true, "BusinessRule_A", null),
				new("Rule B", true, "BusinessRule_B", null)
			});
		CreateEntityBusinessRuleTool tool = new(commandResolver, ConsoleLogger.Instance);

		// Act
		BusinessRuleBatchResponse response = (BusinessRuleBatchResponse)tool.BusinessRuleCreate(new CreateEntityBusinessRulesArgs {
			EnvironmentName = "dev",
			PackageName = "UsrPkg",
			EntitySchemaName = "UsrOrder",
			Rules = [
				new EntityBusinessRuleMcpContract("Rule A", new BusinessRuleConditionGroup("AND", []),
					[new EntityMakeReadOnlyBusinessRuleActionMcpContract(["Status"])]),
				new EntityBusinessRuleMcpContract("Rule B", new BusinessRuleConditionGroup("AND", []),
					[new EntityMakeRequiredBusinessRuleActionMcpContract(["Owner"])])
			]
		});

		// Assert
		response.Succeeded.Should().Be(2);
		service.Received(1).Create(Arg.Is<BusinessRulesBatchRequest>(request => request.Rules.Count == 2));
	}

	[Test]
	[Category("Unit")]
	[Description("Reports a request-level error without calling the service when the rules array is empty.")]
	public void BusinessRuleCreate_Should_Return_Request_Error_When_Rules_Empty() {
		// Arrange
		IEntityBusinessRuleService service = Substitute.For<IEntityBusinessRuleService>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IEntityBusinessRuleService>(Arg.Any<EnvironmentOptions>()).Returns(service);
		CreateEntityBusinessRuleTool tool = new(commandResolver, ConsoleLogger.Instance);

		// Act
		BusinessRuleBatchResponse response = (BusinessRuleBatchResponse)tool.BusinessRuleCreate(new CreateEntityBusinessRulesArgs {
			EnvironmentName = "dev",
			PackageName = "UsrPkg",
			EntitySchemaName = "UsrOrder",
			Rules = []
		});

		// Assert
		response.Error.Should().Contain("rules is required",
			because: "an empty batch should be rejected before any remote work");
		response.Succeeded.Should().Be(0);
		service.DidNotReceiveWithAnyArgs().Create(default(BusinessRulesBatchRequest)!);
	}

	[Test]
	[Category("Unit")]
	[Description("Surfaces per-rule failures from the service as a mixed created/failed summary without losing successful rules.")]
	public void BusinessRuleCreate_Should_Report_Mixed_Per_Rule_Outcomes() {
		// Arrange
		IEntityBusinessRuleService service = Substitute.For<IEntityBusinessRuleService>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IEntityBusinessRuleService>(Arg.Any<EnvironmentOptions>()).Returns(service);
		service.Create(Arg.Any<BusinessRulesBatchRequest>())
			.Returns(new List<BusinessRuleBatchItemResult> {
				new("Good rule", true, "BusinessRule_OK", null),
				new("Bad rule", false, null, "Unknown attribute")
			});
		CreateEntityBusinessRuleTool tool = new(commandResolver, ConsoleLogger.Instance);

		// Act
		BusinessRuleBatchResponse response = (BusinessRuleBatchResponse)tool.BusinessRuleCreate(new CreateEntityBusinessRulesArgs {
			EnvironmentName = "dev",
			PackageName = "UsrPkg",
			EntitySchemaName = "UsrOrder",
			Rules = [
				new EntityBusinessRuleMcpContract("Good rule", new BusinessRuleConditionGroup("AND", []),
					[new EntityMakeReadOnlyBusinessRuleActionMcpContract(["Status"])]),
				new EntityBusinessRuleMcpContract("Bad rule", new BusinessRuleConditionGroup("AND", []),
					[new EntityMakeReadOnlyBusinessRuleActionMcpContract(["Nope"])])
			]
		});

		// Assert
		response.Succeeded.Should().Be(1);
		response.Failed.Should().Be(1);
		response.Results.Should().Contain(result => result.Name == "Bad rule" && !result.Success && result.Error == "Unknown attribute");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps a request-level service exception into a per-rule failure response so callers still get structured output.")]
	public void BusinessRuleCreate_Should_Map_Request_Level_Exception_To_Failures() {
		// Arrange
		IEntityBusinessRuleService service = Substitute.For<IEntityBusinessRuleService>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IEntityBusinessRuleService>(Arg.Any<EnvironmentOptions>()).Returns(service);
		service.Create(Arg.Any<BusinessRulesBatchRequest>())
			.Returns(_ => throw new System.InvalidOperationException("entity-schema-name not found."));
		CreateEntityBusinessRuleTool tool = new(commandResolver, ConsoleLogger.Instance);

		// Act
		BusinessRuleBatchResponse response = (BusinessRuleBatchResponse)tool.BusinessRuleCreate(new CreateEntityBusinessRulesArgs {
			EnvironmentName = "dev",
			PackageName = "UsrPkg",
			EntitySchemaName = "Missing",
			Rules = [
				new EntityBusinessRuleMcpContract("Rule A", new BusinessRuleConditionGroup("AND", []),
					[new EntityMakeReadOnlyBusinessRuleActionMcpContract(["Status"])])
			]
		});

		// Assert
		response.Failed.Should().Be(1, because: "a request-level failure fails every rule of the call");
		response.Results.Single().Success.Should().BeFalse();
		response.Results.Single().Error.Should().Be("entity-schema-name not found.");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the standard command-execution envelope (exit code 1) referencing the requested environment when entity business-rule creation cannot resolve the environment, instead of folding the failure into an implicit-success batch response (ENG-91830 / ENG-91825).")]
	public void BusinessRuleCreate_Should_Return_Resolver_Envelope_When_Environment_Resolution_Fails_ForEntity() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IEntityBusinessRuleService>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new EnvironmentResolutionException("Environment with key 'missing-env' not found."));
		CreateEntityBusinessRuleTool tool = new(commandResolver, ConsoleLogger.Instance);

		// Act
		object result = tool.BusinessRuleCreate(new CreateEntityBusinessRulesArgs {
			EnvironmentName = "missing-env",
			PackageName = "UsrPkg",
			EntitySchemaName = "UsrOrder",
			Rules = [
				new EntityBusinessRuleMcpContract("Rule A", new BusinessRuleConditionGroup("AND", []),
					[new EntityMakeReadOnlyBusinessRuleActionMcpContract(["Status"])])
			]
		});

		// Assert
		result.Should().BeOfType<CommandExecutionResult>(
			because: "an unresolvable environment must surface as the standard command-execution envelope, not a batch response");
		CommandExecutionResult execution = (CommandExecutionResult)result;
		execution.ExitCode.Should().Be(1,
			because: "a missing environment is an expected, caller-actionable failure mapped to exit code 1");
		execution.Output.Select(message => message.Value?.ToString() ?? string.Empty).Should().Contain(value => value.Contains("missing-env"),
			because: "the failure must reference the requested environment so the caller can correct it");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the standard command-execution envelope (exit code 1) referencing the requested environment when page business-rule creation cannot resolve the environment, instead of folding the failure into an implicit-success batch response (ENG-91830 / ENG-91825).")]
	public void BusinessRuleCreate_Should_Return_Resolver_Envelope_When_Environment_Resolution_Fails_ForPage() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IPageBusinessRuleService>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new EnvironmentResolutionException("Environment with key 'missing-env' not found."));
		CreatePageBusinessRuleTool tool = new(commandResolver, ConsoleLogger.Instance);

		// Act
		object result = tool.BusinessRuleCreate(new CreatePageBusinessRulesArgs {
			EnvironmentName = "missing-env",
			PackageName = "UsrPkg",
			PageSchemaName = "UsrOrderFormPage",
			Rules = [
				new PageBusinessRuleMcpContract("Rule A", new BusinessRuleConditionGroup("AND", []),
					[new PageMakeReadOnlyBusinessRuleActionMcpContract(["Status"])])
			]
		});

		// Assert
		result.Should().BeOfType<CommandExecutionResult>(
			because: "an unresolvable environment must surface as the standard command-execution envelope, not a batch response");
		CommandExecutionResult execution = (CommandExecutionResult)result;
		execution.ExitCode.Should().Be(1,
			because: "a missing environment is an expected, caller-actionable failure mapped to exit code 1");
		execution.Output.Select(message => message.Value?.ToString() ?? string.Empty).Should().Contain(value => value.Contains("missing-env"),
			because: "the failure must reference the requested environment so the caller can correct it");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the unexpected-failure command-execution envelope (exit code -1) when entity business-rule environment resolution throws a NON-environment exception (e.g. a DI/bootstrap failure), instead of letting it escape to the MCP SDK generic error. Pins the deliberate two-class discrimination — EnvironmentResolutionException maps to exit 1, every other resolve failure maps to exit -1 — against future reordering of the catch arms (ENG-91830 / ENG-91825).")]
	public void BusinessRuleCreate_Should_Return_Unexpected_Failure_Envelope_When_Resolution_Throws_NonEnvironment_ForEntity() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IEntityBusinessRuleService>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new System.InvalidOperationException("BindingsModule.Register failed."));
		CreateEntityBusinessRuleTool tool = new(commandResolver, ConsoleLogger.Instance);

		// Act
		object result = tool.BusinessRuleCreate(new CreateEntityBusinessRulesArgs {
			EnvironmentName = "dev",
			PackageName = "UsrPkg",
			EntitySchemaName = "UsrOrder",
			Rules = [
				new EntityBusinessRuleMcpContract("Rule A", new BusinessRuleConditionGroup("AND", []),
					[new EntityMakeReadOnlyBusinessRuleActionMcpContract(["Status"])])
			]
		});

		// Assert
		result.Should().BeOfType<CommandExecutionResult>(
			because: "an unexpected (non-environment) resolve failure must surface as the standard command-execution envelope, not a batch response");
		CommandExecutionResult execution = (CommandExecutionResult)result;
		execution.ExitCode.Should().Be(-1,
			because: "an unexpected DI/bootstrap failure is an internal error mapped to exit code -1, mirroring BaseTool.InternalExecute, not the exit code 1 reserved for a caller-actionable environment error");
		execution.Output.Select(message => message.Value?.ToString() ?? string.Empty).Should().Contain(value => value.Contains("BindingsModule.Register failed."),
			because: "the unexpected-failure envelope must carry the underlying exception message for diagnostics");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the unexpected-failure command-execution envelope (exit code -1) when page business-rule environment resolution throws a NON-environment exception (e.g. a DI/bootstrap failure), instead of letting it escape to the MCP SDK generic error. Pins the deliberate two-class discrimination — EnvironmentResolutionException maps to exit 1, every other resolve failure maps to exit -1 — against future reordering of the catch arms (ENG-91830 / ENG-91825).")]
	public void BusinessRuleCreate_Should_Return_Unexpected_Failure_Envelope_When_Resolution_Throws_NonEnvironment_ForPage() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IPageBusinessRuleService>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new System.InvalidOperationException("BindingsModule.Register failed."));
		CreatePageBusinessRuleTool tool = new(commandResolver, ConsoleLogger.Instance);

		// Act
		object result = tool.BusinessRuleCreate(new CreatePageBusinessRulesArgs {
			EnvironmentName = "dev",
			PackageName = "UsrPkg",
			PageSchemaName = "UsrOrderFormPage",
			Rules = [
				new PageBusinessRuleMcpContract("Rule A", new BusinessRuleConditionGroup("AND", []),
					[new PageMakeReadOnlyBusinessRuleActionMcpContract(["Status"])])
			]
		});

		// Assert
		result.Should().BeOfType<CommandExecutionResult>(
			because: "an unexpected (non-environment) resolve failure must surface as the standard command-execution envelope, not a batch response");
		CommandExecutionResult execution = (CommandExecutionResult)result;
		execution.ExitCode.Should().Be(-1,
			because: "an unexpected DI/bootstrap failure is an internal error mapped to exit code -1, mirroring BaseTool.InternalExecute, not the exit code 1 reserved for a caller-actionable environment error");
		execution.Output.Select(message => message.Value?.ToString() ?? string.Empty).Should().Contain(value => value.Contains("BindingsModule.Register failed."),
			because: "the unexpected-failure envelope must carry the underlying exception message for diagnostics");
	}

	[Test]
	[Category("Unit")]
	[Description("Marks create-entity-business-rules as a single required args wrapper so runtime calls use the same shape as other clio MCP tools.")]
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
		parameter.ParameterType.Should().Be(typeof(CreateEntityBusinessRulesArgs),
			because: "the wrapper type should own the batch business-rule input fields and their JSON names");
		requiredAttributes.Should().ContainSingle(
			because: "the MCP tool should require the args wrapper");
	}

	[Test]
	[Category("Unit")]
	[Description("Exposes kebab-case required fields inside the create-entity-business-rules args wrapper.")]
	[TestCase(nameof(CreateEntityBusinessRulesArgs.EnvironmentName), "environment-name")]
	[TestCase(nameof(CreateEntityBusinessRulesArgs.PackageName), "package-name")]
	[TestCase(nameof(CreateEntityBusinessRulesArgs.EntitySchemaName), "entity-schema-name")]
	[TestCase(nameof(CreateEntityBusinessRulesArgs.Rules), "rules")]
	public void BusinessRuleCreateArgs_Should_Expose_Required_Kebab_Case_Fields(
		string propertyName,
		string jsonName) {
		// Arrange
		PropertyInfo property = typeof(CreateEntityBusinessRulesArgs).GetProperty(propertyName)!;

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
	[Description("Deserializes the batch args-wrapper business-rule payload and preserves lookup constants as string GUID values.")]
	public void BusinessRuleCreate_Should_Deserialize_Args_Wrapper_Payload_With_Lookup_Value_Expression() {
		// Arrange
		string payload = """
		{
		  "environment-name": "dev",
		  "package-name": "UsrPkg",
		  "entity-schema-name": "UsrOrder",
		  "rules": [
		    {
		      "caption": "Require status for owner",
		      "condition": {
		        "logicalOperation": "AND",
		        "conditions": [
		          {
		            "leftExpression": { "type": "AttributeValue", "path": "Owner" },
		            "comparisonType": "equal",
		            "rightExpression": { "type": "Const", "value": "11111111-1111-1111-1111-111111111111" }
		          }
		        ]
		      },
		      "actions": [ { "type": "make-required", "items": [ "Status" ] } ]
		    }
		  ]
		}
		""";

		// Act
		CreateEntityBusinessRulesArgs? payloadArgs = JsonSerializer.Deserialize<CreateEntityBusinessRulesArgs>(payload);

		// Assert
		payloadArgs.Should().NotBeNull(
			because: "the batch args-wrapper business-rule payload should deserialize from JSON");
		BusinessRule rule = payloadArgs!.Rules.Single().ToBusinessRule();
		rule.Condition.Conditions[0].LeftExpression.Type.Should().Be("AttributeValue");
		rule.Condition.Conditions[0].LeftExpression.Path.Should().Be("Owner");
		rule.Condition.Conditions[0].RightExpression!.Value!.Value.ValueKind.Should().Be(JsonValueKind.String,
			because: "lookup constants are only supported as string GUIDs on the MCP surface");
		rule.Actions.Single().FieldSelectionItems.Should().Equal(["Status"]);
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
		  "rules": [
		    {
		      "caption": "Populate defaults",
		      "condition": {
		        "logicalOperation": "AND",
		        "conditions": [ { "leftExpression": { "type": "AttributeValue", "path": "Name" }, "comparisonType": "is-filled-in" } ]
		      },
		      "actions": [
		        {
		          "type": "set-values",
		          "items": [
		            { "expression": { "type": "AttributeValue", "path": "UsrTextResult" }, "value": { "type": "Const", "value": "Ready" } },
		            { "expression": { "type": "AttributeValue", "path": "UsrScore" }, "value": { "type": "Const", "value": 42 } },
		            { "expression": { "type": "AttributeValue", "path": "UsrCompleted" }, "value": { "type": "Const", "value": true } }
		          ]
		        }
		      ]
		    }
		  ]
		}
		""";

		// Act
		CreateEntityBusinessRulesArgs? payloadArgs = JsonSerializer.Deserialize<CreateEntityBusinessRulesArgs>(payload);

		// Assert
		payloadArgs.Should().NotBeNull();
		BusinessRuleAction action = payloadArgs!.Rules.Single().ToBusinessRule().Actions.Single();
		action.ActionType.Should().Be("set-values");
		action.SetValueItems.Should().HaveCount(3);
		action.SetValueItems[0].Value.Value!.Value.ValueKind.Should().Be(JsonValueKind.String);
		action.SetValueItems[1].Value.Value!.Value.ValueKind.Should().Be(JsonValueKind.Number);
		action.SetValueItems[2].Value.Value!.Value.ValueKind.Should().Be(JsonValueKind.True);
	}

	[Test]
	[Category("Unit")]
	[Description("Deserializes apply-filter actions and preserves target/source lookup settings.")]
	public void BusinessRuleCreate_Should_Deserialize_ApplyFilter_Action() {
		// Arrange
		string payload = """
		{
		  "environment-name": "dev",
		  "package-name": "UsrPkg",
		  "entity-schema-name": "UsrOrder",
		  "rules": [
		    {
		      "caption": "Filter city by country",
		      "condition": { "logicalOperation": "AND", "conditions": [] },
		      "actions": [
		        { "type": "apply-filter", "target": "City", "targetFilterPath": "Country", "source": "Country", "sourceFilterPath": "TimeZone", "clearValue": true, "populateValue": true }
		      ]
		    }
		  ]
		}
		""";

		// Act
		CreateEntityBusinessRulesArgs? payloadArgs = JsonSerializer.Deserialize<CreateEntityBusinessRulesArgs>(payload);

		// Assert
		payloadArgs.Should().NotBeNull();
		ApplyFilterBusinessRuleAction action = payloadArgs!.Rules.Single().ToBusinessRule().Actions.Single()
			.Should().BeOfType<ApplyFilterBusinessRuleAction>().Subject;
		action.Target.Should().Be("City");
		action.Source.Should().Be("Country");
		action.PopulateValue.Should().BeTrue();
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
		result.Actions.Should().HaveCount(2);
		result.Actions[1].Should().BeNull();
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
		result.Actions.Single().SetValueItems.Should().BeEmpty();
	}

	[Test]
	[Category("Unit")]
	[Description("Advertises a stable MCP tool name for create-page-business-rules.")]
	public void PageBusinessRuleCreate_Should_Advertise_Stable_Tool_Name() {
		// Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(CreatePageBusinessRuleTool)
			.GetMethod(nameof(CreatePageBusinessRuleTool.BusinessRuleCreate))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be(CreatePageBusinessRuleTool.BusinessRuleCreateToolName);
		attribute.Name.Should().Be("create-page-business-rules",
			because: "the batch page tool is advertised under the plural name");
		attribute.ReadOnly.Should().BeFalse();
		attribute.Destructive.Should().BeTrue();
	}

	[Test]
	[Category("Unit")]
	[Description("Maps the batch create-page-business-rules payload into the page service request and returns per-rule results.")]
	public void PageBusinessRuleCreate_Should_Map_Arguments_And_Return_Per_Rule_Results() {
		// Arrange
		IPageBusinessRuleService service = Substitute.For<IPageBusinessRuleService>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IPageBusinessRuleService>(Arg.Any<EnvironmentOptions>()).Returns(service);
		service.Create(Arg.Any<BusinessRulesBatchRequest>())
			.Returns(new List<BusinessRuleBatchItemResult> {
				new("Show escalation when priority is high", true, "BusinessRule_7654321", null)
			});
		CreatePageBusinessRuleTool tool = new(commandResolver, ConsoleLogger.Instance);
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
		BusinessRuleBatchResponse response = (BusinessRuleBatchResponse)tool.BusinessRuleCreate(new CreatePageBusinessRulesArgs {
			EnvironmentName = "dev",
			PackageName = "UsrPkg",
			PageSchemaName = "UsrCase_FormPage",
			Rules = [rule]
		});

		// Assert
		response.Succeeded.Should().Be(1);
		response.Results.Single().RuleName.Should().Be("BusinessRule_7654321");
		commandResolver.Received(1).Resolve<IPageBusinessRuleService>(Arg.Is<EnvironmentOptions>(options =>
			options.Environment == "dev"));
		service.Received(1).Create(
			Arg.Is<BusinessRulesBatchRequest>(request =>
				request.PackageName == "UsrPkg"
				&& request.SchemaName == "UsrCase_FormPage"
				&& request.Rules.Count == 1
				&& request.Rules[0].Caption == "Show escalation when priority is high"
				&& request.Rules[0].Actions[0].ActionType == "show-element"
				&& request.Rules[0].Actions[0].FieldSelectionItems.Single() == "EscalateButton"));
	}

	[Test]
	[Category("Unit")]
	[Description("Reports a request-level error without calling the page service when the rules array is empty.")]
	public void PageBusinessRuleCreate_Should_Return_Request_Error_When_Rules_Empty() {
		// Arrange
		IPageBusinessRuleService service = Substitute.For<IPageBusinessRuleService>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IPageBusinessRuleService>(Arg.Any<EnvironmentOptions>()).Returns(service);
		CreatePageBusinessRuleTool tool = new(commandResolver, ConsoleLogger.Instance);

		// Act
		BusinessRuleBatchResponse response = (BusinessRuleBatchResponse)tool.BusinessRuleCreate(new CreatePageBusinessRulesArgs {
			EnvironmentName = "dev",
			PackageName = "UsrPkg",
			PageSchemaName = "UsrCase_FormPage",
			Rules = []
		});

		// Assert
		response.Error.Should().Contain("rules is required");
		service.DidNotReceiveWithAnyArgs().Create(default(BusinessRulesBatchRequest)!);
	}

	[Test]
	[Category("Unit")]
	[Description("Exposes kebab-case required fields inside the create-page-business-rules args wrapper.")]
	[TestCase(nameof(CreatePageBusinessRulesArgs.EnvironmentName), "environment-name")]
	[TestCase(nameof(CreatePageBusinessRulesArgs.PackageName), "package-name")]
	[TestCase(nameof(CreatePageBusinessRulesArgs.PageSchemaName), "page-schema-name")]
	[TestCase(nameof(CreatePageBusinessRulesArgs.Rules), "rules")]
	public void PageBusinessRuleCreateArgs_Should_Expose_Required_Kebab_Case_Fields(
		string propertyName,
		string jsonName) {
		// Arrange
		PropertyInfo property = typeof(CreatePageBusinessRulesArgs).GetProperty(propertyName)!;

		// Act
		string actualJsonName = property.GetCustomAttributes(typeof(JsonPropertyNameAttribute), inherit: false)
			.Cast<JsonPropertyNameAttribute>()
			.Single()
			.Name;

		// Assert
		actualJsonName.Should().Be(jsonName);
		property.GetCustomAttributes(typeof(RequiredAttribute), inherit: false).Should().ContainSingle();
	}

	[Test]
	[Category("Unit")]
	[Description("Marks create-page-business-rules as a single required args wrapper.")]
	public void PageBusinessRuleCreate_Should_Expose_Required_Args_Wrapper() {
		// Arrange
		ParameterInfo parameter = typeof(CreatePageBusinessRuleTool)
			.GetMethod(nameof(CreatePageBusinessRuleTool.BusinessRuleCreate))!
			.GetParameters()
			.Single();

		// Assert
		parameter.Name.Should().Be("args");
		parameter.ParameterType.Should().Be(typeof(CreatePageBusinessRulesArgs));
		parameter.GetCustomAttributes(typeof(RequiredAttribute), inherit: false).Should().ContainSingle();
	}

	[Test]
	[Category("Unit")]
	[Description("Deserializes create-page-business-rules page action payloads without accepting object-only action branches.")]
	public void PageBusinessRuleCreate_Should_Deserialize_Page_Action_Payload() {
		// Arrange
		string payload = """
		{
		  "environment-name": "dev",
		  "package-name": "UsrPkg",
		  "page-schema-name": "UsrCase_FormPage",
		  "rules": [
		    {
		      "caption": "Hide escalation for low priority",
		      "condition": {
		        "logicalOperation": "AND",
		        "conditions": [ { "leftExpression": { "type": "AttributeValue", "path": "PDS_Priority" }, "comparisonType": "equal", "rightExpression": { "type": "Const", "value": "Low" } } ]
		      },
		      "actions": [
		        { "type": "hide-element", "items": [ "EscalateButton" ] },
		        { "type": "make-read-only", "items": [ "AmountInput" ] }
		      ]
		    }
		  ]
		}
		""";

		// Act
		CreatePageBusinessRulesArgs? payloadArgs = JsonSerializer.Deserialize<CreatePageBusinessRulesArgs>(payload);

		// Assert
		payloadArgs.Should().NotBeNull();
		BusinessRule rule = payloadArgs!.Rules.Single().ToBusinessRule();
		rule.Actions.Select(action => action.ActionType).Should().Equal(["hide-element", "make-read-only"]);
		rule.Actions.SelectMany(action => action.FieldSelectionItems).Should().Equal(["EscalateButton", "AmountInput"]);
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
		result.Actions.Single().FieldSelectionItems.Should().BeEmpty();
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
		result.Actions.Should().BeEmpty();
	}

	[TestCase(typeof(ReadEntityBusinessRuleTool), nameof(ReadEntityBusinessRuleTool.BusinessRulesRead), "read-entity-business-rules", true, false)]
	[TestCase(typeof(ReadPageBusinessRuleTool), nameof(ReadPageBusinessRuleTool.BusinessRulesRead), "read-page-business-rules", true, false)]
	[TestCase(typeof(UpdateEntityBusinessRuleTool), nameof(UpdateEntityBusinessRuleTool.BusinessRulesUpdate), "update-entity-business-rules", false, true)]
	[TestCase(typeof(UpdatePageBusinessRuleTool), nameof(UpdatePageBusinessRuleTool.BusinessRulesUpdate), "update-page-business-rules", false, true)]
	[TestCase(typeof(DeleteEntityBusinessRuleTool), nameof(DeleteEntityBusinessRuleTool.BusinessRulesDelete), "delete-entity-business-rules", false, true)]
	[TestCase(typeof(DeletePageBusinessRuleTool), nameof(DeletePageBusinessRuleTool.BusinessRulesDelete), "delete-page-business-rules", false, true)]
	[Category("Unit")]
	[Description("Advertises stable MCP tool names with correct read-only/destructive safety flags for all six business-rule maintenance tools.")]
	public void MaintenanceTools_Should_Advertise_Stable_Names_And_Safety_Flags(
		Type toolType,
		string methodName,
		string expectedToolName,
		bool expectedReadOnly,
		bool expectedDestructive) {
		// Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)toolType
			.GetMethod(methodName)!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be(expectedToolName,
			because: "the MCP tool name must stay stable for callers and tests");
		attribute.ReadOnly.Should().Be(expectedReadOnly,
			because: "read tools never mutate remote state while update/delete tools do");
		attribute.Destructive.Should().Be(expectedDestructive,
			because: "the destructive flag must reflect whether the tool changes the target add-on state");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps read-entity-business-rules args into the entity read request and returns the rules with their count.")]
	public void EntityRead_Should_Map_Arguments_And_Return_Models() {
		// Arrange
		IEntityBusinessRuleService service = Substitute.For<IEntityBusinessRuleService>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IEntityBusinessRuleService>(Arg.Any<EnvironmentOptions>()).Returns(service);
		BusinessRule model = new("Entity rule", new BusinessRuleConditionGroup("AND", []), []) {
			Name = "BusinessRule_1",
			Enabled = true
		};
		service.Read(Arg.Any<BusinessRulesReadRequest>()).Returns([model]);
		ReadEntityBusinessRuleTool tool = new(commandResolver, ConsoleLogger.Instance);

		// Act
		BusinessRulesReadResponse response = (BusinessRulesReadResponse)tool.BusinessRulesRead(new ReadEntityBusinessRulesArgs {
			EnvironmentName = "dev",
			PackageName = "UsrPkg",
			EntitySchemaName = "UsrOrder"
		});

		// Assert
		response.Count.Should().Be(1, because: "one persisted rule was read");
		response.Rules.Single().Should().BeSameAs(model,
			because: "the rules are passed through to the response unchanged");
		response.Error.Should().BeNull(because: "a successful read carries no request-level error");
		commandResolver.Received(1).Resolve<IEntityBusinessRuleService>(Arg.Is<EnvironmentOptions>(options =>
			options.Environment == "dev"));
		service.Received(1).Read(Arg.Is<BusinessRulesReadRequest>(request =>
			request.PackageName == "UsrPkg"
			&& request.SchemaName == "UsrOrder"));
	}

	[Test]
	[Category("Unit")]
	[Description("Maps read-page-business-rules args into the page read request and returns the rules with their count.")]
	public void PageRead_Should_Map_Arguments_And_Return_Models() {
		// Arrange
		IPageBusinessRuleService service = Substitute.For<IPageBusinessRuleService>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IPageBusinessRuleService>(Arg.Any<EnvironmentOptions>()).Returns(service);
		service.Read(Arg.Any<BusinessRulesReadRequest>())
			.Returns([
				new BusinessRule("Page rule", new BusinessRuleConditionGroup("AND", []), []) {
					Name = "BusinessRule_pg",
					Enabled = true
				}
			]);
		ReadPageBusinessRuleTool tool = new(commandResolver, ConsoleLogger.Instance);

		// Act
		BusinessRulesReadResponse response = (BusinessRulesReadResponse)tool.BusinessRulesRead(new ReadPageBusinessRulesArgs {
			EnvironmentName = "dev",
			PackageName = "UsrPkg",
			PageSchemaName = "UsrOrder_FormPage"
		});

		// Assert
		response.Count.Should().Be(1, because: "one persisted page rule was read");
		response.Rules.Single().Name.Should().Be("BusinessRule_pg",
			because: "the page rules are passed through to the response unchanged");
		service.Received(1).Read(Arg.Is<BusinessRulesReadRequest>(request =>
			request.PackageName == "UsrPkg"
			&& request.SchemaName == "UsrOrder_FormPage"));
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the standard command-execution envelope (exit code 1) referencing the requested environment when read-entity-business-rules cannot resolve the environment.")]
	public void EntityRead_Should_Return_Resolver_Envelope_When_Environment_Resolution_Fails() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IEntityBusinessRuleService>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new EnvironmentResolutionException("Environment with key 'missing-env' not found."));
		ReadEntityBusinessRuleTool tool = new(commandResolver, ConsoleLogger.Instance);

		// Act
		object result = tool.BusinessRulesRead(new ReadEntityBusinessRulesArgs {
			EnvironmentName = "missing-env",
			PackageName = "UsrPkg",
			EntitySchemaName = "UsrOrder"
		});

		// Assert
		result.Should().BeOfType<CommandExecutionResult>(
			because: "an unresolvable environment must surface as the standard command-execution envelope, not a read response");
		CommandExecutionResult execution = (CommandExecutionResult)result;
		execution.ExitCode.Should().Be(1,
			because: "a missing environment is an expected, caller-actionable failure mapped to exit code 1");
		execution.Output.Select(message => message.Value?.ToString() ?? string.Empty)
			.Should().Contain(value => value.Contains("missing-env"),
				because: "the failure must reference the requested environment so the caller can correct it");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the unexpected-failure command-execution envelope (exit code -1) when read-entity-business-rules resolution throws a non-environment exception, mirroring BaseTool.InternalExecute.")]
	public void EntityRead_Should_Return_Unexpected_Failure_Envelope_When_Resolution_Throws_NonEnvironment() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IEntityBusinessRuleService>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new InvalidOperationException("BindingsModule.Register failed."));
		ReadEntityBusinessRuleTool tool = new(commandResolver, ConsoleLogger.Instance);

		// Act
		object result = tool.BusinessRulesRead(new ReadEntityBusinessRulesArgs {
			EnvironmentName = "dev",
			PackageName = "UsrPkg",
			EntitySchemaName = "UsrOrder"
		});

		// Assert
		result.Should().BeOfType<CommandExecutionResult>(
			because: "an unexpected resolve failure must surface as the standard command-execution envelope");
		((CommandExecutionResult)result).ExitCode.Should().Be(-1,
			because: "an unexpected DI/bootstrap failure is an internal error mapped to exit code -1, not the exit code 1 reserved for environment errors");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps a read-level service exception into the typed read error response so callers still get structured output.")]
	public void EntityRead_Should_Return_Typed_Error_When_Service_Throws() {
		// Arrange
		IEntityBusinessRuleService service = Substitute.For<IEntityBusinessRuleService>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IEntityBusinessRuleService>(Arg.Any<EnvironmentOptions>()).Returns(service);
		service.Read(Arg.Any<BusinessRulesReadRequest>())
			.Returns(_ => throw new InvalidOperationException("Package 'UsrPkg' was not found."));
		ReadEntityBusinessRuleTool tool = new(commandResolver, ConsoleLogger.Instance);

		// Act
		BusinessRulesReadResponse response = (BusinessRulesReadResponse)tool.BusinessRulesRead(new ReadEntityBusinessRulesArgs {
			EnvironmentName = "dev",
			PackageName = "UsrPkg",
			EntitySchemaName = "UsrOrder"
		});

		// Assert
		response.Error.Should().Be("Package 'UsrPkg' was not found.",
			because: "an operation failure folds into the typed error response instead of an MCP SDK generic error");
		response.Count.Should().Be(0, because: "no rules were read when the operation failed");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps update-entity-business-rules args (including rule name, enabled, and action uId) into the entity batch update request and aggregates mixed per-rule outcomes.")]
	public void EntityUpdate_Should_Map_Arguments_And_Aggregate_Mixed_Outcomes() {
		// Arrange
		IEntityBusinessRuleService service = Substitute.For<IEntityBusinessRuleService>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IEntityBusinessRuleService>(Arg.Any<EnvironmentOptions>()).Returns(service);
		service.Update(Arg.Any<BusinessRulesBatchRequest>())
			.Returns(new List<BusinessRuleBatchItemResult> {
				new("BusinessRule_1", true, "BusinessRule_1", null),
				new("BusinessRule_missing", false, null, "Business rule 'BusinessRule_missing' was not found.")
			});
		UpdateEntityBusinessRuleTool tool = new(commandResolver, ConsoleLogger.Instance);
		EntityBusinessRuleMcpContract rule = new(
			"Updated caption",
			new BusinessRuleConditionGroup("AND", []),
			[new EntityMakeReadOnlyBusinessRuleActionMcpContract(["Status"]) { UId = ActionUId }]) {
			Name = "BusinessRule_1",
			Enabled = false
		};
		EntityBusinessRuleMcpContract missingRule = new(
			"Missing caption",
			new BusinessRuleConditionGroup("AND", []),
			[new EntityMakeReadOnlyBusinessRuleActionMcpContract(["Status"])]) {
			Name = "BusinessRule_missing"
		};

		// Act
		BusinessRuleBatchResponse response = (BusinessRuleBatchResponse)tool.BusinessRulesUpdate(
			new UpdateEntityBusinessRulesArgs {
				EnvironmentName = "dev",
				PackageName = "UsrPkg",
				EntitySchemaName = "UsrOrder",
				Rules = [rule, missingRule]
			});

		// Assert
		response.Succeeded.Should().Be(1, because: "one rule of the batch was updated");
		response.Failed.Should().Be(1, because: "the unknown name fails only its own entry");
		response.Results.Should().HaveCount(2, because: "every input rule gets a result entry in input order");
		response.Results[1].Error.Should().Contain("BusinessRule_missing",
			because: "the per-rule failure must name the missing rule");
		commandResolver.Received(1).Resolve<IEntityBusinessRuleService>(Arg.Is<EnvironmentOptions>(options =>
			options.Environment == "dev"));
		service.Received(1).Update(Arg.Is<BusinessRulesBatchRequest>(request =>
			request.PackageName == "UsrPkg"
			&& request.SchemaName == "UsrOrder"
			&& request.Rules.Count == 2
			&& request.Rules[0].Name == "BusinessRule_1"
			&& request.Rules[0].Enabled == false
			&& request.Rules[0].Actions[0].UId == ActionUId
			&& request.Rules[1].Name == "BusinessRule_missing"
			&& request.Rules[1].Enabled == null));
	}

	[Test]
	[Category("Unit")]
	[Description("Reports a request-level error without resolving the environment or calling the service when the update rules array is empty.")]
	public void EntityUpdate_Should_Return_Request_Error_When_Rules_Empty() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		UpdateEntityBusinessRuleTool tool = new(commandResolver, ConsoleLogger.Instance);

		// Act
		BusinessRuleBatchResponse response = (BusinessRuleBatchResponse)tool.BusinessRulesUpdate(
			new UpdateEntityBusinessRulesArgs {
				EnvironmentName = "dev",
				PackageName = "UsrPkg",
				EntitySchemaName = "UsrOrder",
				Rules = []
			});

		// Assert
		response.Error.Should().Contain("rules is required",
			because: "an empty batch is rejected before any environment or remote work");
		response.Succeeded.Should().Be(0, because: "nothing was updated for a rejected request");
		commandResolver.DidNotReceive().Resolve<IEntityBusinessRuleService>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Category("Unit")]
	[Description("Maps a request-level service exception into the typed update error response so callers still get structured output.")]
	public void EntityUpdate_Should_Return_Typed_Error_When_Service_Throws() {
		// Arrange
		IEntityBusinessRuleService service = Substitute.For<IEntityBusinessRuleService>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IEntityBusinessRuleService>(Arg.Any<EnvironmentOptions>()).Returns(service);
		service.Update(Arg.Any<BusinessRulesBatchRequest>())
			.Returns(_ => throw new InvalidOperationException("entity-schema-name not found."));
		UpdateEntityBusinessRuleTool tool = new(commandResolver, ConsoleLogger.Instance);

		// Act
		BusinessRuleBatchResponse response = (BusinessRuleBatchResponse)tool.BusinessRulesUpdate(
			new UpdateEntityBusinessRulesArgs {
				EnvironmentName = "dev",
				PackageName = "UsrPkg",
				EntitySchemaName = "Missing",
				Rules = [
					new EntityBusinessRuleMcpContract("Rule", new BusinessRuleConditionGroup("AND", []),
						[new EntityMakeReadOnlyBusinessRuleActionMcpContract(["Status"])]) { Name = "BusinessRule_1" }
				]
			});

		// Assert
		response.Error.Should().Be("entity-schema-name not found.",
			because: "a request-level operation failure folds into the typed error response");
		response.Succeeded.Should().Be(0, because: "nothing was updated when the whole request failed");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps update-page-business-rules args (including rule name and enabled) into the page batch update request.")]
	public void PageUpdate_Should_Map_Arguments_Into_Page_Batch_Request() {
		// Arrange
		IPageBusinessRuleService service = Substitute.For<IPageBusinessRuleService>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IPageBusinessRuleService>(Arg.Any<EnvironmentOptions>()).Returns(service);
		service.Update(Arg.Any<BusinessRulesBatchRequest>())
			.Returns(new List<BusinessRuleBatchItemResult> {
				new("BusinessRule_pg", true, "BusinessRule_pg", null)
			});
		UpdatePageBusinessRuleTool tool = new(commandResolver, ConsoleLogger.Instance);
		PageBusinessRuleMcpContract rule = new(
			"Updated page rule",
			new BusinessRuleConditionGroup("AND", []),
			[new PageShowElementBusinessRuleActionMcpContract(["EscalateButton"]) { UId = ActionUId }]) {
			Name = "BusinessRule_pg",
			Enabled = true
		};

		// Act
		BusinessRuleBatchResponse response = (BusinessRuleBatchResponse)tool.BusinessRulesUpdate(
			new UpdatePageBusinessRulesArgs {
				EnvironmentName = "dev",
				PackageName = "UsrPkg",
				PageSchemaName = "UsrOrder_FormPage",
				Rules = [rule]
			});

		// Assert
		response.Succeeded.Should().Be(1, because: "the single page rule was updated");
		response.Failed.Should().Be(0, because: "no page rule failed");
		service.Received(1).Update(Arg.Is<BusinessRulesBatchRequest>(request =>
			request.PackageName == "UsrPkg"
			&& request.SchemaName == "UsrOrder_FormPage"
			&& request.Rules.Count == 1
			&& request.Rules[0].Name == "BusinessRule_pg"
			&& request.Rules[0].Enabled == true
			&& request.Rules[0].Actions[0].UId == ActionUId));
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the standard command-execution envelope (exit code 1) referencing the requested environment when update-page-business-rules cannot resolve the environment.")]
	public void PageUpdate_Should_Return_Resolver_Envelope_When_Environment_Resolution_Fails() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IPageBusinessRuleService>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new EnvironmentResolutionException("Environment with key 'missing-env' not found."));
		UpdatePageBusinessRuleTool tool = new(commandResolver, ConsoleLogger.Instance);

		// Act
		object result = tool.BusinessRulesUpdate(new UpdatePageBusinessRulesArgs {
			EnvironmentName = "missing-env",
			PackageName = "UsrPkg",
			PageSchemaName = "UsrOrder_FormPage",
			Rules = [
				new PageBusinessRuleMcpContract("Rule", new BusinessRuleConditionGroup("AND", []),
					[new PageShowElementBusinessRuleActionMcpContract(["EscalateButton"])]) { Name = "BusinessRule_pg" }
			]
		});

		// Assert
		result.Should().BeOfType<CommandExecutionResult>(
			because: "an unresolvable environment must surface as the standard command-execution envelope, not a batch response");
		CommandExecutionResult execution = (CommandExecutionResult)result;
		execution.ExitCode.Should().Be(1,
			because: "a missing environment is an expected, caller-actionable failure mapped to exit code 1");
		execution.Output.Select(message => message.Value?.ToString() ?? string.Empty)
			.Should().Contain(value => value.Contains("missing-env"),
				because: "the failure must reference the requested environment so the caller can correct it");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps delete-entity-business-rules args into the entity delete request and aggregates mixed per-name outcomes.")]
	public void EntityDelete_Should_Map_Arguments_And_Aggregate_Mixed_Outcomes() {
		// Arrange
		IEntityBusinessRuleService service = Substitute.For<IEntityBusinessRuleService>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IEntityBusinessRuleService>(Arg.Any<EnvironmentOptions>()).Returns(service);
		service.Delete(Arg.Any<BusinessRulesDeleteRequest>())
			.Returns(new List<BusinessRuleBatchItemResult> {
				new("BusinessRule_1", true, "BusinessRule_1", null),
				new("BusinessRule_missing", false, null, "Business rule 'BusinessRule_missing' was not found.")
			});
		DeleteEntityBusinessRuleTool tool = new(commandResolver, ConsoleLogger.Instance);

		// Act
		BusinessRuleBatchResponse response = (BusinessRuleBatchResponse)tool.BusinessRulesDelete(
			new DeleteEntityBusinessRulesArgs {
				EnvironmentName = "dev",
				PackageName = "UsrPkg",
				EntitySchemaName = "UsrOrder",
				RuleNames = ["BusinessRule_1", "BusinessRule_missing"]
			});

		// Assert
		response.Succeeded.Should().Be(1, because: "one rule of the batch was deleted");
		response.Failed.Should().Be(1, because: "the unknown name fails only its own entry");
		response.Results.Should().HaveCount(2, because: "every input name gets a result entry in input order");
		response.Results[1].Error.Should().Contain("BusinessRule_missing",
			because: "the per-name failure must name the missing rule");
		commandResolver.Received(1).Resolve<IEntityBusinessRuleService>(Arg.Is<EnvironmentOptions>(options =>
			options.Environment == "dev"));
		service.Received(1).Delete(Arg.Is<BusinessRulesDeleteRequest>(request =>
			request.PackageName == "UsrPkg"
			&& request.SchemaName == "UsrOrder"
			&& request.RuleNames.Count == 2
			&& request.RuleNames[0] == "BusinessRule_1"
			&& request.RuleNames[1] == "BusinessRule_missing"));
	}

	[Test]
	[Category("Unit")]
	[Description("Reports a request-level error without resolving the environment or calling the service when the delete rule-names array is empty.")]
	public void EntityDelete_Should_Return_Request_Error_When_RuleNames_Empty() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		DeleteEntityBusinessRuleTool tool = new(commandResolver, ConsoleLogger.Instance);

		// Act
		BusinessRuleBatchResponse response = (BusinessRuleBatchResponse)tool.BusinessRulesDelete(
			new DeleteEntityBusinessRulesArgs {
				EnvironmentName = "dev",
				PackageName = "UsrPkg",
				EntitySchemaName = "UsrOrder",
				RuleNames = []
			});

		// Assert
		response.Error.Should().Contain("rule-names is required",
			because: "an empty name list is rejected before any environment or remote work");
		response.Succeeded.Should().Be(0, because: "nothing was deleted for a rejected request");
		commandResolver.DidNotReceive().Resolve<IEntityBusinessRuleService>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the standard command-execution envelope (exit code 1) referencing the requested environment when delete-entity-business-rules cannot resolve the environment.")]
	public void EntityDelete_Should_Return_Resolver_Envelope_When_Environment_Resolution_Fails() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IEntityBusinessRuleService>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new EnvironmentResolutionException("Environment with key 'missing-env' not found."));
		DeleteEntityBusinessRuleTool tool = new(commandResolver, ConsoleLogger.Instance);

		// Act
		object result = tool.BusinessRulesDelete(new DeleteEntityBusinessRulesArgs {
			EnvironmentName = "missing-env",
			PackageName = "UsrPkg",
			EntitySchemaName = "UsrOrder",
			RuleNames = ["BusinessRule_1"]
		});

		// Assert
		result.Should().BeOfType<CommandExecutionResult>(
			because: "an unresolvable environment must surface as the standard command-execution envelope, not a delete response");
		((CommandExecutionResult)result).ExitCode.Should().Be(1,
			because: "a missing environment is an expected, caller-actionable failure mapped to exit code 1");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps a request-level service exception into the typed delete error response so callers still get structured output.")]
	public void EntityDelete_Should_Return_Typed_Error_When_Service_Throws() {
		// Arrange
		IEntityBusinessRuleService service = Substitute.For<IEntityBusinessRuleService>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IEntityBusinessRuleService>(Arg.Any<EnvironmentOptions>()).Returns(service);
		service.Delete(Arg.Any<BusinessRulesDeleteRequest>())
			.Returns(_ => throw new InvalidOperationException("Package 'UsrPkg' was not found."));
		DeleteEntityBusinessRuleTool tool = new(commandResolver, ConsoleLogger.Instance);

		// Act
		BusinessRuleBatchResponse response = (BusinessRuleBatchResponse)tool.BusinessRulesDelete(
			new DeleteEntityBusinessRulesArgs {
				EnvironmentName = "dev",
				PackageName = "UsrPkg",
				EntitySchemaName = "UsrOrder",
				RuleNames = ["BusinessRule_1"]
			});

		// Assert
		response.Error.Should().Be("Package 'UsrPkg' was not found.",
			because: "a request-level operation failure folds into the typed error response");
		response.Succeeded.Should().Be(0, because: "nothing was deleted when the whole request failed");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps delete-page-business-rules args into the page delete request and returns per-name outcomes.")]
	public void PageDelete_Should_Map_Arguments_Into_Page_Delete_Request() {
		// Arrange
		IPageBusinessRuleService service = Substitute.For<IPageBusinessRuleService>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IPageBusinessRuleService>(Arg.Any<EnvironmentOptions>()).Returns(service);
		service.Delete(Arg.Any<BusinessRulesDeleteRequest>())
			.Returns(new List<BusinessRuleBatchItemResult> {
				new("BusinessRule_pg", true, "BusinessRule_pg", null)
			});
		DeletePageBusinessRuleTool tool = new(commandResolver, ConsoleLogger.Instance);

		// Act
		BusinessRuleBatchResponse response = (BusinessRuleBatchResponse)tool.BusinessRulesDelete(
			new DeletePageBusinessRulesArgs {
				EnvironmentName = "dev",
				PackageName = "UsrPkg",
				PageSchemaName = "UsrOrder_FormPage",
				RuleNames = ["BusinessRule_pg"]
			});

		// Assert
		response.Succeeded.Should().Be(1, because: "the single page rule was deleted");
		response.Failed.Should().Be(0, because: "no page rule name failed");
		service.Received(1).Delete(Arg.Is<BusinessRulesDeleteRequest>(request =>
			request.PackageName == "UsrPkg"
			&& request.SchemaName == "UsrOrder_FormPage"
			&& request.RuleNames.Count == 1
			&& request.RuleNames[0] == "BusinessRule_pg"));
	}

	[Test]
	[Category("Unit")]
	[Description("Reports a request-level error without calling the page service when the delete rule-names array is empty.")]
	public void PageDelete_Should_Return_Request_Error_When_RuleNames_Empty() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		DeletePageBusinessRuleTool tool = new(commandResolver, ConsoleLogger.Instance);

		// Act
		BusinessRuleBatchResponse response = (BusinessRuleBatchResponse)tool.BusinessRulesDelete(
			new DeletePageBusinessRulesArgs {
				EnvironmentName = "dev",
				PackageName = "UsrPkg",
				PageSchemaName = "UsrOrder_FormPage",
				RuleNames = []
			});

		// Assert
		response.Error.Should().Contain("rule-names is required",
			because: "an empty name list is rejected before any environment or remote work");
		commandResolver.DidNotReceive().Resolve<IPageBusinessRuleService>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Category("Unit")]
	[Description("Reports a request-level error without calling the page service when the page update rules array is empty.")]
	public void PageUpdate_Should_Return_Request_Error_When_Rules_Empty() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		UpdatePageBusinessRuleTool tool = new(commandResolver, ConsoleLogger.Instance);

		// Act
		BusinessRuleBatchResponse response = (BusinessRuleBatchResponse)tool.BusinessRulesUpdate(
			new UpdatePageBusinessRulesArgs {
				EnvironmentName = "dev",
				PackageName = "UsrPkg",
				PageSchemaName = "UsrOrder_FormPage",
				Rules = []
			});

		// Assert
		response.Error.Should().Contain("rules is required",
			because: "an empty batch is rejected before any environment or remote work");
		commandResolver.DidNotReceive().Resolve<IPageBusinessRuleService>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Category("Unit")]
	[Description("Maps the entity MCP contract's name, enabled flag, and action uId onto the shared business-rule model used by the services.")]
	public void EntityContract_ToBusinessRule_Should_Map_Name_Enabled_And_Action_UId() {
		// Arrange
		EntityBusinessRuleMcpContract contract = new(
			"Contract caption",
			new BusinessRuleConditionGroup("AND", []),
			[new EntityMakeReadOnlyBusinessRuleActionMcpContract(["Status"]) { UId = ActionUId }]) {
			Name = "BusinessRule_1",
			Enabled = false
		};

		// Act
		BusinessRule rule = contract.ToBusinessRule();

		// Assert
		rule.Name.Should().Be("BusinessRule_1",
			because: "the update match key must survive contract conversion");
		rule.Enabled.Should().BeFalse(
			because: "the caller's explicit enabled intent must survive contract conversion");
		rule.Caption.Should().Be("Contract caption",
			because: "the caption must survive contract conversion");
		rule.Actions.Single().UId.Should().Be(ActionUId,
			because: "action block uIds must survive contract conversion so update can preserve identity");
	}
}
