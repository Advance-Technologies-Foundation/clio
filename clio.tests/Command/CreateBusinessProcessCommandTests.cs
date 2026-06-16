using System;
using System.IO;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public sealed class CreateBusinessProcessCommandTests : BaseCommandTests<CreateBusinessProcessOptions> {
	private const string SampleDescriptor =
		"{\"name\":\"UsrSampleProcess\",\"packageName\":\"Custom\",\"elements\":[],\"flows\":[]}";

	private ICreateBusinessProcessService _createBusinessProcessService;
	private ILogger _logger;
	private CreateBusinessProcessCommand _command;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_createBusinessProcessService = Substitute.For<ICreateBusinessProcessService>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddSingleton(_createBusinessProcessService);
		containerBuilder.AddSingleton(_logger);
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<CreateBusinessProcessCommand>();
	}

	[TearDown]
	public override void TearDown() {
		_createBusinessProcessService.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

	private static CreateBusinessProcessResult BuildResult() =>
		new("UsrSampleProcess", "5c58c4c4-134b-4744-9c67-96d9c69c9d55");

	[Test]
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
	[Description("Reads the descriptor file content and forwards it to the build service when --descriptor is provided.")]
	public void Execute_ShouldReadDescriptorFile_WhenDescriptorPathProvided() {
		// Arrange
		string descriptorPath = Path.Combine(Path.GetTempPath(), $"clio-bp-test-{Guid.NewGuid():N}.json");
		File.WriteAllText(descriptorPath, SampleDescriptor);
		try {
			CreateBusinessProcessOptions options = new() {
				Environment = "sandbox",
				DescriptorPath = descriptorPath
			};
			_createBusinessProcessService.BuildProcess("sandbox", Arg.Any<CreateBusinessProcessRequest>())
				.Returns(BuildResult());

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(0,
				because: "a successful build from a descriptor file should return the standard success exit code");
			_createBusinessProcessService.Received(1).BuildProcess(
				"sandbox",
				Arg.Is<CreateBusinessProcessRequest>(request => request.DescriptorJson == SampleDescriptor));
		}
		finally {
			File.Delete(descriptorPath);
		}
	}

	[Test]
	[Description("Returns a failure exit code and logs guidance when neither --descriptor nor --descriptor-json is provided.")]
	public void Execute_ShouldFail_WhenNoDescriptorProvided() {
		// Act
		int result = _command.Execute(new CreateBusinessProcessOptions { Environment = "sandbox" });

		// Assert
		result.Should().Be(1,
			because: "the command requires a descriptor source to build a process");
		_createBusinessProcessService.DidNotReceiveWithAnyArgs().BuildProcess(default!, default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("--descriptor") && message.Contains("--descriptor-json")));
	}

	[Test]
	[Description("Returns a failure exit code and logs a readable error when the CLI call omits environment-name.")]
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
