using System;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class CreateBusinessProcessCommandTests {
	private const string SampleDescriptor =
		"{\"name\":\"UsrSampleProcess\",\"packageName\":\"Custom\",\"elements\":[],\"flows\":[]}";

	private ICreateBusinessProcessService _createBusinessProcessService;
	private ILogger _logger;
	private CreateBusinessProcessCommand _command;

	[SetUp]
	public void Setup() {
		_createBusinessProcessService = Substitute.For<ICreateBusinessProcessService>();
		_logger = Substitute.For<ILogger>();
		_command = new CreateBusinessProcessCommand(_createBusinessProcessService, _logger);
	}

	[TearDown]
	public void TearDown() {
		_createBusinessProcessService.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

	private static CreateBusinessProcessResult BuildResult() =>
		new("UsrSampleProcess", "5c58c4c4-134b-4744-9c67-96d9c69c9d55");

	[Test]
	[Category("Unit")]
	[Description("Forwards the inline descriptor JSON and package override to the build service and logs the created schema on success.")]
	public void Execute_ShouldMapInlineDescriptorToService_WhenDescriptorJsonProvided() {
		// Arrange
		CreateBusinessProcessOptions options = new() {
			Environment = "sandbox",
			DescriptorJson = SampleDescriptor,
			PackageName = "MyApp"
		};
		_createBusinessProcessService.BuildProcess("sandbox", Arg.Any<CreateBusinessProcessRequest>())
			.Returns(BuildResult());

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "a successful build should return the standard success exit code");
		_createBusinessProcessService.Received(1).BuildProcess(
			"sandbox",
			Arg.Is<CreateBusinessProcessRequest>(request =>
				request.DescriptorJson == SampleDescriptor &&
				request.PackageNameOverride == "MyApp"));
		_logger.Received(1).WriteInfo(Arg.Is<string>(message => message.Contains("UsrSampleProcess")));
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a failure exit code and logs guidance when no inline descriptor JSON is provided.")]
	public void Execute_ShouldFail_WhenNoDescriptorProvided() {
		// Act
		int result = _command.Execute(new CreateBusinessProcessOptions { Environment = "sandbox" });

		// Assert
		result.Should().Be(1,
			because: "the command requires an inline descriptor to build a process");
		_createBusinessProcessService.DidNotReceiveWithAnyArgs().BuildProcess(default!, default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("A process descriptor is required.")));
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a failure exit code and logs a readable error when the call omits environment-name.")]
	public void Execute_ShouldFail_WhenEnvironmentIsMissing() {
		// Act
		int result = _command.Execute(new CreateBusinessProcessOptions { DescriptorJson = SampleDescriptor });

		// Assert
		result.Should().Be(1,
			because: "the command should fail fast when the environment is missing");
		_createBusinessProcessService.DidNotReceiveWithAnyArgs().BuildProcess(default!, default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("Environment name is required")));
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a failure exit code and logs the service exception message when the build service throws.")]
	public void Execute_ShouldFail_WhenServiceThrows() {
		// Arrange
		CreateBusinessProcessOptions options = new() {
			Environment = "sandbox",
			DescriptorJson = SampleDescriptor
		};
		_createBusinessProcessService.BuildProcess(Arg.Any<string>(), Arg.Any<CreateBusinessProcessRequest>())
			.Returns<CreateBusinessProcessResult>(_ =>
				throw new InvalidOperationException("Package 'Custom' was not found."));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1,
			because: "the command should propagate service-level failures as a non-zero exit code");
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("Package 'Custom' was not found.")));
	}
}
