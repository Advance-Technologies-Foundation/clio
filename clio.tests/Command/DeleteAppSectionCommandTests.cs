using Clio.Command;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public sealed class DeleteAppSectionCommandTests : BaseCommandTests<DeleteAppSectionOptions> {
	private IApplicationSectionDeleteService _sectionDeleteService;
	private ILogger _logger;
	private DeleteAppSectionCommand _command;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_sectionDeleteService = Substitute.For<IApplicationSectionDeleteService>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddSingleton(_sectionDeleteService);
		containerBuilder.AddSingleton(_logger);
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<DeleteAppSectionCommand>();
	}

	[TearDown]
	public void TearDown() {
		_sectionDeleteService.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

	private static ApplicationSectionDeleteResult BuildResult() =>
		new(
			"pkg-uid",
			"UsrOrdersApp",
			"app-id",
			"Orders App",
			"UsrOrdersApp",
			"8.3.0",
			new ApplicationSectionInfoResult("section-id", "UsrOrders", "Orders", null, "UsrOrder", "pkg-uid", null, null, null, null));

	[Test]
	[Description("Maps CLI options to the section-delete service request and writes the structured result to the logger on success.")]
	public void Execute_Should_Map_Options_To_Service_Request() {
		// Arrange
		DeleteAppSectionOptions options = new() {
			Environment = "sandbox",
			ApplicationCode = "UsrOrdersApp",
			SectionCode = "UsrOrders"
		};
		_sectionDeleteService.DeleteSection("sandbox", Arg.Any<ApplicationSectionDeleteRequest>())
			.Returns(BuildResult());

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "successful section deletion should return the standard success exit code");
		_sectionDeleteService.Received(1).DeleteSection(
			"sandbox",
			Arg.Is<ApplicationSectionDeleteRequest>(request =>
				request.ApplicationCode == "UsrOrdersApp" &&
				request.SectionCode == "UsrOrders" &&
				request.DeleteEntitySchema == false));
		_logger.Received(1).WriteInfo(Arg.Is<string>(message => message.Contains("\"ApplicationId\":\"app-id\"")));
	}

	[Test]
	[Description("Returns a failure exit code and logs a readable error when the CLI call omits environment-name.")]
	public void Execute_Should_Fail_When_Environment_Is_Missing() {
		// Act
		int result = _command.Execute(new DeleteAppSectionOptions {
			ApplicationCode = "UsrOrdersApp",
			SectionCode = "UsrOrders"
		});

		// Assert
		result.Should().Be(1,
			because: "the command should fail fast when environment resolution input is missing");
		_sectionDeleteService.DidNotReceiveWithAnyArgs().DeleteSection(default!, default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("Environment name is required")));
	}

	[Test]
	[Description("Returns a failure exit code and logs the service exception message when the service throws.")]
	public void Execute_Should_Fail_When_Service_Throws() {
		// Arrange
		DeleteAppSectionOptions options = new() {
			Environment = "sandbox",
			ApplicationCode = "UsrOrdersApp",
			SectionCode = "UsrMissing"
		};
		_sectionDeleteService.DeleteSection(Arg.Any<string>(), Arg.Any<ApplicationSectionDeleteRequest>())
			.Returns<ApplicationSectionDeleteResult>(_ => throw new System.InvalidOperationException("Section 'UsrMissing' was not found in application 'app-id'."));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1,
			because: "the command should propagate service-level failures as a non-zero exit code");
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("UsrMissing")));
	}

	[Test]
	[Description("Passes delete-entity-schema=true to the service request when the CLI option is set.")]
	public void Execute_Should_Pass_DeleteEntitySchema_Flag_To_Service() {
		// Arrange
		DeleteAppSectionOptions options = new() {
			Environment = "sandbox",
			ApplicationCode = "UsrOrdersApp",
			SectionCode = "UsrOrders",
			DeleteEntitySchema = true
		};
		_sectionDeleteService.DeleteSection("sandbox", Arg.Any<ApplicationSectionDeleteRequest>())
			.Returns(BuildResult());

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "the command should succeed when all required options are provided");
		_sectionDeleteService.Received(1).DeleteSection(
			"sandbox",
			Arg.Is<ApplicationSectionDeleteRequest>(request => request.DeleteEntitySchema == true));
	}
}
