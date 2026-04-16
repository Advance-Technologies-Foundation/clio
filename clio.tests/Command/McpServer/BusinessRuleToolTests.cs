using System.Linq;
using System.Text.Json;
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
	[Description("Maps the MCP payload into the business-rule create service request and returns the structured response envelope.")]
	public void BusinessRuleCreate_Should_Map_Arguments_And_Return_Structured_Response() {
		// Arrange
		IBusinessRuleService service = Substitute.For<IBusinessRuleService>();
		CreateEntityBusinessRuleCommand command = new(service, Substitute.For<ILogger>());
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntityBusinessRuleCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);
		service.Create("dev", Arg.Any<BusinessRuleCreateRequest>())
			.Returns(new BusinessRuleCreateResult("BusinessRule_1234567"));
		CreateEntityBusinessRuleTool tool = new(command, ConsoleLogger.Instance, commandResolver);
		BusinessRuleCreateArgs args = new(
			"dev",
			"UsrPkg",
			"UsrOrder",
			new BusinessRuleArgs(
				"Require owner for drafts",
				new BusinessRuleConditionGroupArgs(
					"AND",
					[
						new BusinessRuleConditionArgs(
							new BusinessRuleAttributeExpressionArgs { Path = "Status" },
							"equal",
							new BusinessRuleValueExpressionArgs {
								Value = JsonSerializer.Deserialize<JsonElement>("\"Draft\"")
						})
					]),
				[
					new BusinessRuleActionArgs("make-required", ["Owner", "Amount"]),
					new BusinessRuleActionArgs("make-read-only", ["Status"])
				]));

		// Act
		BusinessRuleCreateResponse result = tool.BusinessRuleCreate(args);

		// Assert
		result.Success.Should().BeTrue(
			because: "a successful service call should be wrapped into a structured success envelope");
		result.PackageName.Should().Be("UsrPkg",
			because: "the response should echo the package name from the request args");
		result.EntitySchemaName.Should().Be("UsrOrder",
			because: "the response should echo the entity schema name from the request args");
		result.RuleName.Should().Be("BusinessRule_1234567",
			because: "the response should report the generated rule name returned by the service");
		commandResolver.Received(1).Resolve<CreateEntityBusinessRuleCommand>(Arg.Is<EnvironmentOptions>(options =>
			options.Environment == "dev"));
		service.Received(1).Create(
			"dev",
			Arg.Is<BusinessRuleCreateRequest>(request =>
				request.PackageName == "UsrPkg"
				&& request.EntitySchemaName == "UsrOrder"
				&& request.Rule.Caption == "Require owner for drafts"
				&& request.Rule.Enabled == null
				&& request.Rule.ConditionGroup.Operator == "AND"
				&& request.Rule.Actions.Count == 2
				&& request.Rule.Actions[0].Action == "make-required"
				&& request.Rule.Actions[0].Targets.Count == 2
				&& request.Rule.Actions[0].Targets[0] == "Owner"
				&& request.Rule.Actions[0].Targets[1] == "Amount"
				&& request.Rule.Actions[1].Targets.Count == 1
				&& request.Rule.Actions[1].Targets[0] == "Status"));
	}

	[Test]
	[Category("Unit")]
	[Description("Preserves omitted optional enabled on the MCP surface so the service can apply its documented default.")]
	public void BusinessRuleCreate_Should_Preserve_Omitted_Enabled_Default() {
		// Arrange
		IBusinessRuleService service = Substitute.For<IBusinessRuleService>();
		CreateEntityBusinessRuleCommand command = new(service, Substitute.For<ILogger>());
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntityBusinessRuleCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);
		service.Create("dev", Arg.Any<BusinessRuleCreateRequest>())
			.Returns(new BusinessRuleCreateResult("BusinessRule_1234567"));
		CreateEntityBusinessRuleTool tool = new(command, ConsoleLogger.Instance, commandResolver);
		BusinessRuleCreateArgs args = new(
			"dev",
			"UsrPkg",
			"UsrOrder",
			new BusinessRuleArgs(
				"Require owner for drafts",
				new BusinessRuleConditionGroupArgs(
					"AND",
					[
						new BusinessRuleConditionArgs(
							new BusinessRuleAttributeExpressionArgs { Path = "Status" },
							"equal",
							new BusinessRuleValueExpressionArgs {
								Value = JsonSerializer.Deserialize<JsonElement>("\"Draft\"")
							})
					]),
				[
					new BusinessRuleActionArgs("make-required", ["Owner"])
				]));

		// Act
		BusinessRuleCreateResponse result = tool.BusinessRuleCreate(args);

		// Assert
		result.Success.Should().BeTrue(
			because: "omitting rule.enabled should still allow valid requests to succeed");
		commandResolver.Received(1).Resolve<CreateEntityBusinessRuleCommand>(Arg.Is<EnvironmentOptions>(options =>
			options.Environment == "dev"));
		service.Received(1).Create(
			"dev",
			Arg.Is<BusinessRuleCreateRequest>(request => request.Rule.Enabled == null));
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves the business-rule command from the payload environment so nested dependencies use the requested MCP target.")]
	public void BusinessRuleCreate_Should_Resolve_Command_From_Requested_Environment() {
		// Arrange
		IBusinessRuleService service = Substitute.For<IBusinessRuleService>();
		CreateEntityBusinessRuleCommand command = new(service, Substitute.For<ILogger>());
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntityBusinessRuleCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);
		service.Create("dev", Arg.Any<BusinessRuleCreateRequest>())
			.Returns(new BusinessRuleCreateResult("BusinessRule_1234567"));
		CreateEntityBusinessRuleTool tool = new(command, ConsoleLogger.Instance, commandResolver);
		BusinessRuleCreateArgs args = new(
			"dev",
			"UsrPkg",
			"UsrOrder",
			new BusinessRuleArgs(
				"Require owner for drafts",
				new BusinessRuleConditionGroupArgs(
					"AND",
					[
						new BusinessRuleConditionArgs(
							new BusinessRuleAttributeExpressionArgs { Path = "Status" },
							"equal",
							new BusinessRuleValueExpressionArgs {
								Value = JsonSerializer.Deserialize<JsonElement>("\"Draft\"")
							})
					]),
				[
					new BusinessRuleActionArgs("make-required", ["Owner"])
				]));

		// Act
		BusinessRuleCreateResponse result = tool.BusinessRuleCreate(args);

		// Assert
		result.Success.Should().BeTrue(
			because: "the environment-aware resolver should return a command instance for the requested MCP environment");
		commandResolver.Received(1).Resolve<CreateEntityBusinessRuleCommand>(Arg.Is<EnvironmentOptions>(options =>
			options is CreateEntityBusinessRuleOptions
			&& options.Environment == "dev"
			&& string.IsNullOrWhiteSpace(options.Uri)));
	}
	

	[Test]
	[Category("Unit")]
	[Description("Deserializes lookup constant expressions into the value-expression subclass while preserving a string GUID value.")]
	public void BusinessRuleCreate_Should_Deserialize_Lookup_Value_Expression_From_Json() {
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
		BusinessRuleCreateArgs? args = JsonSerializer.Deserialize<BusinessRuleCreateArgs>(payload);

		// Assert
		args.Should().NotBeNull(
			because: "the business-rule MCP payload should deserialize from JSON");
		args!.Rule.Condition.Conditions[0].LeftExpression.Should().BeOfType<BusinessRuleAttributeExpressionArgs>(
			because: "path-based operands should deserialize into the attribute-expression subclass");
		args.Rule.Condition.Conditions[0].RightExpression.Should().BeOfType<BusinessRuleValueExpressionArgs>(
			because: "lookup constants should deserialize into the value-expression subclass");
		((BusinessRuleValueExpressionArgs)args.Rule.Condition.Conditions[0].RightExpression).Value.HasValue.Should().BeTrue(
			because: "lookup constant payloads should preserve the GUID string value for downstream validation");
		((BusinessRuleValueExpressionArgs)args.Rule.Condition.Conditions[0].RightExpression).Value!.Value.ValueKind.Should().Be(JsonValueKind.String,
			because: "lookup constants are only supported as string GUIDs on the MCP surface");
	}
}
