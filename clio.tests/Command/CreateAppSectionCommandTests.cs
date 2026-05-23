using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class CreateAppSectionCommandTests {
	[Test]
	[Description("Maps CLI options to the section-create service request and writes the structured result to the logger on success.")]
	public void Execute_Should_Map_Options_To_Service_Request() {
		// Arrange
		IApplicationSectionCreateService applicationSectionCreateService = Substitute.For<IApplicationSectionCreateService>();
		ILogger logger = Substitute.For<ILogger>();
		CreateAppSectionCommand command = new(applicationSectionCreateService, logger);
		CreateAppSectionOptions options = new() {
			Environment = "sandbox",
			ApplicationCode = "UsrOrdersApp",
			Caption = "Orders",
			Description = "Order workspace",
			EntitySchemaName = "UsrOrder",
			WithMobilePages = true
		};
		applicationSectionCreateService.CreateSection("sandbox", Arg.Any<ApplicationSectionCreateRequest>())
			.Returns(new ApplicationSectionCreateResult(
				"pkg-uid",
				"UsrOrdersApp",
				"app-id",
				"Orders App",
				"UsrOrdersApp",
				"8.3.0",
				new ApplicationSectionInfoResult("section-id", "UsrOrders", "Orders", null, "UsrOrder", "pkg-uid", null, null, "#123456", null),
				null,
				[]));

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "successful section creation should return the standard success exit code");
		applicationSectionCreateService.Received(1).CreateSection(
			"sandbox",
			Arg.Is<ApplicationSectionCreateRequest>(request =>
				request.ApplicationCode == "UsrOrdersApp" &&
				request.Caption == "Orders" &&
				request.Description == "Order workspace" &&
				request.EntitySchemaName == "UsrOrder" &&
				request.WithMobilePages));
		logger.Received(1).WriteInfo(Arg.Is<string>(message => message.Contains("\"ApplicationId\":\"app-id\"")));
	}

	[Test]
	[Description("Returns a failure exit code and logs a readable error when the CLI call omits environment-name.")]
	public void Execute_Should_Fail_When_Environment_Is_Missing() {
		// Arrange
		IApplicationSectionCreateService applicationSectionCreateService = Substitute.For<IApplicationSectionCreateService>();
		ILogger logger = Substitute.For<ILogger>();
		CreateAppSectionCommand command = new(applicationSectionCreateService, logger);

		// Act
		int result = command.Execute(new CreateAppSectionOptions {
			Caption = "Orders",
			ApplicationCode = "UsrOrdersApp"
		});

		// Assert
		result.Should().Be(1,
			because: "the command should fail fast when environment resolution input is missing");
		applicationSectionCreateService.DidNotReceiveWithAnyArgs().CreateSection(default!, default!);
		logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("Environment name is required")));
	}
}
