using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public sealed class DeleteAppSectionCommandTests {

	[Test]
	[Description("Maps CLI options to the section-delete service request and writes the structured result to the logger on success.")]
	public void Execute_Should_Map_Options_To_Service_Request() {
		// Arrange
		IApplicationSectionDeleteService sectionDeleteService = Substitute.For<IApplicationSectionDeleteService>();
		ILogger logger = Substitute.For<ILogger>();
		DeleteAppSectionCommand command = new(sectionDeleteService, logger);
		DeleteAppSectionOptions options = new() {
			Environment = "sandbox",
			ApplicationCode = "UsrOrdersApp",
			SectionCode = "UsrOrders"
		};
		sectionDeleteService.DeleteSection("sandbox", Arg.Any<ApplicationSectionDeleteRequest>())
			.Returns(new ApplicationSectionDeleteResult(
				"pkg-uid",
				"UsrOrdersApp",
				"app-id",
				"Orders App",
				"UsrOrdersApp",
				"8.3.0",
				new ApplicationSectionInfoResult("section-id", "UsrOrders", "Orders", null, "UsrOrder", "pkg-uid", null, null, null, null)));

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "successful section deletion should return the standard success exit code");
		sectionDeleteService.Received(1).DeleteSection(
			"sandbox",
			Arg.Is<ApplicationSectionDeleteRequest>(request =>
				request.ApplicationCode == "UsrOrdersApp" &&
				request.SectionCode == "UsrOrders" &&
				request.DeleteEntitySchema == false));
		logger.Received(1).WriteInfo(Arg.Is<string>(message => message.Contains("\"ApplicationId\":\"app-id\"")));
	}

	[Test]
	[Description("Returns a failure exit code and logs a readable error when the CLI call omits environment-name.")]
	public void Execute_Should_Fail_When_Environment_Is_Missing() {
		// Arrange
		IApplicationSectionDeleteService sectionDeleteService = Substitute.For<IApplicationSectionDeleteService>();
		ILogger logger = Substitute.For<ILogger>();
		DeleteAppSectionCommand command = new(sectionDeleteService, logger);

		// Act
		int result = command.Execute(new DeleteAppSectionOptions {
			ApplicationCode = "UsrOrdersApp",
			SectionCode = "UsrOrders"
		});

		// Assert
		result.Should().Be(1,
			because: "the command should fail fast when environment resolution input is missing");
		sectionDeleteService.DidNotReceiveWithAnyArgs().DeleteSection(default!, default!);
		logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("Environment name is required")));
	}

	[Test]
	[Description("Returns a failure exit code and logs the service exception message when the service throws.")]
	public void Execute_Should_Fail_When_Service_Throws() {
		// Arrange
		IApplicationSectionDeleteService sectionDeleteService = Substitute.For<IApplicationSectionDeleteService>();
		ILogger logger = Substitute.For<ILogger>();
		DeleteAppSectionCommand command = new(sectionDeleteService, logger);
		DeleteAppSectionOptions options = new() {
			Environment = "sandbox",
			ApplicationCode = "UsrOrdersApp",
			SectionCode = "UsrMissing"
		};
		sectionDeleteService.DeleteSection(Arg.Any<string>(), Arg.Any<ApplicationSectionDeleteRequest>())
			.Returns<ApplicationSectionDeleteResult>(_ => throw new System.InvalidOperationException("Section 'UsrMissing' was not found in application 'app-id'."));

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1,
			because: "the command should propagate service-level failures as a non-zero exit code");
		logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("UsrMissing")));
	}

	[Test]
	[Description("Passes delete-entity-schema=true to the service request when the CLI option is set.")]
	public void Execute_Should_Pass_DeleteEntitySchema_Flag_To_Service() {
		// Arrange
		IApplicationSectionDeleteService sectionDeleteService = Substitute.For<IApplicationSectionDeleteService>();
		ILogger logger = Substitute.For<ILogger>();
		DeleteAppSectionCommand command = new(sectionDeleteService, logger);
		DeleteAppSectionOptions options = new() {
			Environment = "sandbox",
			ApplicationCode = "UsrOrdersApp",
			SectionCode = "UsrOrders",
			DeleteEntitySchema = true
		};
		sectionDeleteService.DeleteSection("sandbox", Arg.Any<ApplicationSectionDeleteRequest>())
			.Returns(new ApplicationSectionDeleteResult(
				null, null, "app-id", "Orders App", "UsrOrdersApp", "8.3.0",
				new ApplicationSectionInfoResult("section-id", "UsrOrders", "Orders", null, "UsrOrder", null, null, null, null, null)));

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "the command should succeed when all required options are provided");
		sectionDeleteService.Received(1).DeleteSection(
			"sandbox",
			Arg.Is<ApplicationSectionDeleteRequest>(request =>
				request.DeleteEntitySchema == true));
	}
}
