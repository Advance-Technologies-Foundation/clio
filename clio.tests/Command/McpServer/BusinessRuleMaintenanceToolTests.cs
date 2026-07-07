using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
public sealed class BusinessRuleMaintenanceToolTests {

	private const string ActionUId = "aaaaaaaa-0000-0000-0000-000000000001";

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
	[Description("Maps read-entity-business-rules args into the entity read request and returns the read models with their count.")]
	public void EntityRead_Should_Map_Arguments_And_Return_Models() {
		// Arrange
		IEntityBusinessRuleService service = Substitute.For<IEntityBusinessRuleService>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IEntityBusinessRuleService>(Arg.Any<EnvironmentOptions>()).Returns(service);
		BusinessRuleReadModel model = new() { Name = "BusinessRule_1", Enabled = true, Convertible = true };
		service.Read(Arg.Any<EntityBusinessRulesReadRequest>()).Returns([model]);
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
			because: "the read models are passed through to the response unchanged");
		response.Error.Should().BeNull(because: "a successful read carries no request-level error");
		commandResolver.Received(1).Resolve<IEntityBusinessRuleService>(Arg.Is<EnvironmentOptions>(options =>
			options.Environment == "dev"));
		service.Received(1).Read(Arg.Is<EntityBusinessRulesReadRequest>(request =>
			request.PackageName == "UsrPkg"
			&& request.EntitySchemaName == "UsrOrder"));
	}

	[Test]
	[Category("Unit")]
	[Description("Maps read-page-business-rules args into the page read request and returns the read models with their count.")]
	public void PageRead_Should_Map_Arguments_And_Return_Models() {
		// Arrange
		IPageBusinessRuleService service = Substitute.For<IPageBusinessRuleService>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IPageBusinessRuleService>(Arg.Any<EnvironmentOptions>()).Returns(service);
		service.Read(Arg.Any<PageBusinessRulesReadRequest>())
			.Returns([new BusinessRuleReadModel { Name = "BusinessRule_pg", Enabled = true, Convertible = true }]);
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
			because: "the page read models are passed through to the response unchanged");
		service.Received(1).Read(Arg.Is<PageBusinessRulesReadRequest>(request =>
			request.PackageName == "UsrPkg"
			&& request.PageSchemaName == "UsrOrder_FormPage"));
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
		service.Read(Arg.Any<EntityBusinessRulesReadRequest>())
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
		service.Update(Arg.Any<EntityBusinessRulesBatchRequest>())
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
		BusinessRuleUpdateBatchResponse response = (BusinessRuleUpdateBatchResponse)tool.BusinessRulesUpdate(
			new UpdateEntityBusinessRulesArgs {
				EnvironmentName = "dev",
				PackageName = "UsrPkg",
				EntitySchemaName = "UsrOrder",
				Rules = [rule, missingRule]
			});

		// Assert
		response.Updated.Should().Be(1, because: "one rule of the batch was updated");
		response.Failed.Should().Be(1, because: "the unknown name fails only its own entry");
		response.Results.Should().HaveCount(2, because: "every input rule gets a result entry in input order");
		response.Results[1].Error.Should().Contain("BusinessRule_missing",
			because: "the per-rule failure must name the missing rule");
		commandResolver.Received(1).Resolve<IEntityBusinessRuleService>(Arg.Is<EnvironmentOptions>(options =>
			options.Environment == "dev"));
		service.Received(1).Update(Arg.Is<EntityBusinessRulesBatchRequest>(request =>
			request.PackageName == "UsrPkg"
			&& request.EntitySchemaName == "UsrOrder"
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
		BusinessRuleUpdateBatchResponse response = (BusinessRuleUpdateBatchResponse)tool.BusinessRulesUpdate(
			new UpdateEntityBusinessRulesArgs {
				EnvironmentName = "dev",
				PackageName = "UsrPkg",
				EntitySchemaName = "UsrOrder",
				Rules = []
			});

		// Assert
		response.Error.Should().Contain("rules is required",
			because: "an empty batch is rejected before any environment or remote work");
		response.Updated.Should().Be(0, because: "nothing was updated for a rejected request");
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
		service.Update(Arg.Any<EntityBusinessRulesBatchRequest>())
			.Returns(_ => throw new InvalidOperationException("entity-schema-name not found."));
		UpdateEntityBusinessRuleTool tool = new(commandResolver, ConsoleLogger.Instance);

		// Act
		BusinessRuleUpdateBatchResponse response = (BusinessRuleUpdateBatchResponse)tool.BusinessRulesUpdate(
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
		response.Updated.Should().Be(0, because: "nothing was updated when the whole request failed");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps update-page-business-rules args (including rule name and enabled) into the page batch update request.")]
	public void PageUpdate_Should_Map_Arguments_Into_Page_Batch_Request() {
		// Arrange
		IPageBusinessRuleService service = Substitute.For<IPageBusinessRuleService>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IPageBusinessRuleService>(Arg.Any<EnvironmentOptions>()).Returns(service);
		service.Update(Arg.Any<PageBusinessRulesBatchRequest>())
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
		BusinessRuleUpdateBatchResponse response = (BusinessRuleUpdateBatchResponse)tool.BusinessRulesUpdate(
			new UpdatePageBusinessRulesArgs {
				EnvironmentName = "dev",
				PackageName = "UsrPkg",
				PageSchemaName = "UsrOrder_FormPage",
				Rules = [rule]
			});

		// Assert
		response.Updated.Should().Be(1, because: "the single page rule was updated");
		response.Failed.Should().Be(0, because: "no page rule failed");
		service.Received(1).Update(Arg.Is<PageBusinessRulesBatchRequest>(request =>
			request.PackageName == "UsrPkg"
			&& request.PageSchemaName == "UsrOrder_FormPage"
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
		service.Delete(Arg.Any<EntityBusinessRulesDeleteRequest>())
			.Returns(new List<BusinessRuleBatchItemResult> {
				new("BusinessRule_1", true, "BusinessRule_1", null),
				new("BusinessRule_missing", false, null, "Business rule 'BusinessRule_missing' was not found.")
			});
		DeleteEntityBusinessRuleTool tool = new(commandResolver, ConsoleLogger.Instance);

		// Act
		BusinessRuleDeleteBatchResponse response = (BusinessRuleDeleteBatchResponse)tool.BusinessRulesDelete(
			new DeleteEntityBusinessRulesArgs {
				EnvironmentName = "dev",
				PackageName = "UsrPkg",
				EntitySchemaName = "UsrOrder",
				RuleNames = ["BusinessRule_1", "BusinessRule_missing"]
			});

		// Assert
		response.Deleted.Should().Be(1, because: "one rule of the batch was deleted");
		response.Failed.Should().Be(1, because: "the unknown name fails only its own entry");
		response.Results.Should().HaveCount(2, because: "every input name gets a result entry in input order");
		response.Results[1].Error.Should().Contain("BusinessRule_missing",
			because: "the per-name failure must name the missing rule");
		commandResolver.Received(1).Resolve<IEntityBusinessRuleService>(Arg.Is<EnvironmentOptions>(options =>
			options.Environment == "dev"));
		service.Received(1).Delete(Arg.Is<EntityBusinessRulesDeleteRequest>(request =>
			request.PackageName == "UsrPkg"
			&& request.EntitySchemaName == "UsrOrder"
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
		BusinessRuleDeleteBatchResponse response = (BusinessRuleDeleteBatchResponse)tool.BusinessRulesDelete(
			new DeleteEntityBusinessRulesArgs {
				EnvironmentName = "dev",
				PackageName = "UsrPkg",
				EntitySchemaName = "UsrOrder",
				RuleNames = []
			});

		// Assert
		response.Error.Should().Contain("rule-names is required",
			because: "an empty name list is rejected before any environment or remote work");
		response.Deleted.Should().Be(0, because: "nothing was deleted for a rejected request");
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
		service.Delete(Arg.Any<EntityBusinessRulesDeleteRequest>())
			.Returns(_ => throw new InvalidOperationException("Package 'UsrPkg' was not found."));
		DeleteEntityBusinessRuleTool tool = new(commandResolver, ConsoleLogger.Instance);

		// Act
		BusinessRuleDeleteBatchResponse response = (BusinessRuleDeleteBatchResponse)tool.BusinessRulesDelete(
			new DeleteEntityBusinessRulesArgs {
				EnvironmentName = "dev",
				PackageName = "UsrPkg",
				EntitySchemaName = "UsrOrder",
				RuleNames = ["BusinessRule_1"]
			});

		// Assert
		response.Error.Should().Be("Package 'UsrPkg' was not found.",
			because: "a request-level operation failure folds into the typed error response");
		response.Deleted.Should().Be(0, because: "nothing was deleted when the whole request failed");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps delete-page-business-rules args into the page delete request and returns per-name outcomes.")]
	public void PageDelete_Should_Map_Arguments_Into_Page_Delete_Request() {
		// Arrange
		IPageBusinessRuleService service = Substitute.For<IPageBusinessRuleService>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IPageBusinessRuleService>(Arg.Any<EnvironmentOptions>()).Returns(service);
		service.Delete(Arg.Any<PageBusinessRulesDeleteRequest>())
			.Returns(new List<BusinessRuleBatchItemResult> {
				new("BusinessRule_pg", true, "BusinessRule_pg", null)
			});
		DeletePageBusinessRuleTool tool = new(commandResolver, ConsoleLogger.Instance);

		// Act
		BusinessRuleDeleteBatchResponse response = (BusinessRuleDeleteBatchResponse)tool.BusinessRulesDelete(
			new DeletePageBusinessRulesArgs {
				EnvironmentName = "dev",
				PackageName = "UsrPkg",
				PageSchemaName = "UsrOrder_FormPage",
				RuleNames = ["BusinessRule_pg"]
			});

		// Assert
		response.Deleted.Should().Be(1, because: "the single page rule was deleted");
		response.Failed.Should().Be(0, because: "no page rule name failed");
		service.Received(1).Delete(Arg.Is<PageBusinessRulesDeleteRequest>(request =>
			request.PackageName == "UsrPkg"
			&& request.PageSchemaName == "UsrOrder_FormPage"
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
		BusinessRuleDeleteBatchResponse response = (BusinessRuleDeleteBatchResponse)tool.BusinessRulesDelete(
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
		BusinessRuleUpdateBatchResponse response = (BusinessRuleUpdateBatchResponse)tool.BusinessRulesUpdate(
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
