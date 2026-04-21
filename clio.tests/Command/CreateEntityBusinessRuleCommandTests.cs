using Clio.Command;
using Clio.Command.BusinessRules;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public sealed class CreateEntityBusinessRuleCommandTests {
	[Test]
	[Description("Maps environment-scoped options into the business-rule service request and returns success when creation completes.")]
	public void Execute_Should_Map_Options_To_Service_Request() {
		// Arrange
		IBusinessRuleService businessRuleService = Substitute.For<IBusinessRuleService>();
		ILogger logger = Substitute.For<ILogger>();
		CreateEntityBusinessRuleCommand command = new(businessRuleService, logger);
		CreateEntityBusinessRuleOptions options = new() {
			EnvironmentName = "dev",
			PackageName = "UsrPkg",
			EntitySchemaName = "UsrOrder",
			Rule = new BusinessRule(
				"Require owner for drafts",
				new BusinessRuleConditionGroup(
					"AND",
					[
						new BusinessRuleCondition(
							new BusinessRuleExpression("AttributeValue", "Status", null),
							"equal",
							new BusinessRuleExpression("Const", null, System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("\"Draft\"")))
					]),
				[
					new BusinessRuleAction("make-required", ["Owner"])
				])
		};
		businessRuleService.Create(Arg.Any<BusinessRuleCreateRequest>())
			.Returns(new BusinessRuleCreateResult("BusinessRule_1234567"));

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "successful business-rule creation should return the standard success exit code");
		businessRuleService.Received(1).Create(
			Arg.Is<BusinessRuleCreateRequest>(request =>
				request.PackageName == "UsrPkg"
				&& request.EntitySchemaName == "UsrOrder"
				&& request.Rule.Caption == "Require owner for drafts"
				&& request.Rule.Actions.Count == 1
				&& request.Rule.Actions[0].Items.Count == 1
				&& request.Rule.Actions[0].Items[0] == "Owner"));
		logger.Received(1).WriteInfo("Done");
	}

	[Test]
	[Description("Returns a failure exit code and logs a readable error when the command omits environment-name.")]
	public void Execute_Should_Fail_When_Environment_Is_Missing() {
		// Arrange
		IBusinessRuleService businessRuleService = Substitute.For<IBusinessRuleService>();
		ILogger logger = Substitute.For<ILogger>();
		CreateEntityBusinessRuleCommand command = new(businessRuleService, logger);

		// Act
		int result = command.Execute(new CreateEntityBusinessRuleOptions {
			PackageName = "UsrPkg",
			EntitySchemaName = "UsrOrder",
			Rule = new BusinessRule(
				"Require owner for drafts",
				new BusinessRuleConditionGroup("AND", []),
				[])
		});

		// Assert
		result.Should().Be(1,
			because: "the command should fail fast when environment resolution input is missing");
		businessRuleService.DidNotReceiveWithAnyArgs().Create(default!);
		logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("environment-name is required.")));
	}
}
