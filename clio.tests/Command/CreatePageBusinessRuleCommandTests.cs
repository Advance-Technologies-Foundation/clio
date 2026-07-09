using System;
using Clio.Command;
using Clio.Command.BusinessRules;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class CreatePageBusinessRuleCommandTests {
	[Test]
	[Category("Unit")]
	[Description("Maps environment-scoped options into the page business-rule service request and returns success when creation completes.")]
	public void Execute_Should_Map_Options_To_Service_Request() {
		// Arrange
		IPageBusinessRuleService pageBusinessRuleService = Substitute.For<IPageBusinessRuleService>();
		ILogger logger = Substitute.For<ILogger>();
		CreatePageBusinessRuleCommand command = new(pageBusinessRuleService, logger);
		CreatePageBusinessRuleOptions options = new() {
			EnvironmentName = "dev",
			PackageName = "UsrPkg",
			PageSchemaName = "UsrOrder_FormPage",
			Rule = CreateRule()
		};
		pageBusinessRuleService.Create(Arg.Any<BusinessRuleCreateRequest>())
			.Returns(new BusinessRuleCreateResult("BusinessRule_7654321"));

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "successful page business-rule creation should return the standard success exit code");
		pageBusinessRuleService.Received(1).Create(
			Arg.Is<BusinessRuleCreateRequest>(request =>
				request.PackageName == "UsrPkg"
				&& request.SchemaName == "UsrOrder_FormPage"
				&& request.Rule.Caption == "Hide warning when status is filled"
				&& request.Rule.Actions.Count == 1
				&& request.Rule.Actions[0].ActionType == "hide-element"
				&& request.Rule.Actions[0].FieldSelectionItems.Count == 1
				&& request.Rule.Actions[0].FieldSelectionItems[0] == "StatusWarningLabel"));
		logger.Received(1).WriteInfo("Rule name: BusinessRule_7654321");
		logger.Received(1).WriteInfo("Done");
	}

	[TestCase("", "UsrPkg", "UsrOrder_FormPage", true, "environment-name is required.")]
	[TestCase("dev", "", "UsrOrder_FormPage", true, "package-name is required.")]
	[TestCase("dev", "UsrPkg", "", true, "page-schema-name is required.")]
	[TestCase("dev", "UsrPkg", "UsrOrder_FormPage", false, "rule is required.")]
	[Category("Unit")]
	[Description("Returns a failure exit code and skips the page service when required command options are missing.")]
	public void Execute_Should_Fail_When_Required_Options_Are_Missing(
		string environmentName,
		string packageName,
		string pageSchemaName,
		bool includeRule,
		string expectedMessage) {
		// Arrange
		IPageBusinessRuleService pageBusinessRuleService = Substitute.For<IPageBusinessRuleService>();
		ILogger logger = Substitute.For<ILogger>();
		CreatePageBusinessRuleCommand command = new(pageBusinessRuleService, logger);
		CreatePageBusinessRuleOptions options = new() {
			EnvironmentName = environmentName,
			PackageName = packageName,
			PageSchemaName = pageSchemaName,
			Rule = includeRule ? CreateRule() : null!
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1,
			because: "the command should fail fast when required execution input is missing");
		pageBusinessRuleService.DidNotReceiveWithAnyArgs().Create(default(BusinessRuleCreateRequest)!);
		logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains(expectedMessage)));
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a failure exit code and logs the service error when page business-rule creation fails.")]
	public void Execute_Should_Return_Failure_When_Service_Throws() {
		// Arrange
		IPageBusinessRuleService pageBusinessRuleService = Substitute.For<IPageBusinessRuleService>();
		ILogger logger = Substitute.For<ILogger>();
		CreatePageBusinessRuleCommand command = new(pageBusinessRuleService, logger);
		pageBusinessRuleService.Create(Arg.Any<BusinessRuleCreateRequest>())
			.Returns(_ => throw new InvalidOperationException("Page schema 'UsrMissing_FormPage' was not found."));

		// Act
		int result = command.Execute(new CreatePageBusinessRuleOptions {
			EnvironmentName = "dev",
			PackageName = "UsrPkg",
			PageSchemaName = "UsrMissing_FormPage",
			Rule = CreateRule()
		});

		// Assert
		result.Should().Be(1,
			because: "service failures should be reported as command execution failures");
		logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("Page schema 'UsrMissing_FormPage' was not found.")));
		logger.DidNotReceive().WriteInfo("Done");
	}

	private static BusinessRule CreateRule() =>
		new(
			"Hide warning when status is filled",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "PDS_Status", null),
						"is-filled-in")
				]),
			[
				new HideElementBusinessRuleAction(["StatusWarningLabel"])
			]);
}
