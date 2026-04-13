using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class UpdateAppSectionCommandTests {
	[Test]
	[Description("Maps CLI options to the section-update service request and writes the structured result to the logger on success.")]
	public void Execute_Should_Map_Options_To_Service_Request() {
		// Arrange
		IApplicationSectionUpdateService applicationSectionUpdateService = Substitute.For<IApplicationSectionUpdateService>();
		ILogger logger = Substitute.For<ILogger>();
		UpdateAppSectionCommand command = new(applicationSectionUpdateService, logger);
		UpdateAppSectionOptions options = new() {
			Environment = "sandbox",
			ApplicationCode = "UsrOrdersApp",
			SectionCode = "UsrOrders",
			Caption = "Orders",
			Description = "Order workspace",
			IconId = "11111111-1111-1111-1111-111111111111",
			IconBackground = "#123456"
		};
		applicationSectionUpdateService.UpdateSection("sandbox", Arg.Any<ApplicationSectionUpdateRequest>())
			.Returns(new ApplicationSectionUpdateResult(
				"pkg-uid",
				"UsrOrdersApp",
				"app-id",
				"Orders App",
				"UsrOrdersApp",
				"8.3.0",
				new ApplicationSectionInfoResult("section-id", "UsrOrders", "{\"en-US\":\"Orders\"}", "Old", "UsrOrder", "pkg-uid", null, "icon-old", "#111111", null),
				new ApplicationSectionInfoResult("section-id", "UsrOrders", "Orders", "Order workspace", "UsrOrder", "pkg-uid", null, "11111111-1111-1111-1111-111111111111", "#123456", null)));

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "successful section updates should return the standard success exit code");
		applicationSectionUpdateService.Received(1).UpdateSection(
			"sandbox",
			Arg.Is<ApplicationSectionUpdateRequest>(request =>
				request.ApplicationCode == "UsrOrdersApp" &&
				request.SectionCode == "UsrOrders" &&
				request.Caption == "Orders" &&
				request.Description == "Order workspace" &&
				request.IconId == "11111111-1111-1111-1111-111111111111" &&
				request.IconBackground == "#123456"));
		logger.Received(1).WriteInfo(Arg.Is<string>(message => message.Contains("\"ApplicationId\":\"app-id\"")));
	}

	[Test]
	[Description("Returns a failure exit code and logs a readable error when the CLI call omits environment-name.")]
	public void Execute_Should_Fail_When_Environment_Is_Missing() {
		// Arrange
		IApplicationSectionUpdateService applicationSectionUpdateService = Substitute.For<IApplicationSectionUpdateService>();
		ILogger logger = Substitute.For<ILogger>();
		UpdateAppSectionCommand command = new(applicationSectionUpdateService, logger);

		// Act
		int result = command.Execute(new UpdateAppSectionOptions {
			ApplicationCode = "UsrOrdersApp",
			SectionCode = "UsrOrders",
			Caption = "Orders"
		});

		// Assert
		result.Should().Be(1,
			because: "the command should fail fast when environment resolution input is missing");
		applicationSectionUpdateService.DidNotReceiveWithAnyArgs().UpdateSection(default!, default!);
		logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("Environment name is required")));
	}
}
