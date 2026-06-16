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
public sealed class ModifyBusinessProcessCommandTests : BaseCommandTests<ModifyBusinessProcessOptions> {
	private const string SampleOperations =
		"[{\"op\":\"removeElement\",\"elementId\":\"StartEvent1\"}]";

	private IModifyBusinessProcessService _modifyBusinessProcessService;
	private ILogger _logger;
	private ModifyBusinessProcessCommand _command;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_modifyBusinessProcessService = Substitute.For<IModifyBusinessProcessService>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddSingleton(_modifyBusinessProcessService);
		containerBuilder.AddSingleton(_logger);
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<ModifyBusinessProcessCommand>();
	}

	[TearDown]
	public override void TearDown() {
		_modifyBusinessProcessService.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

	private static ModifyBusinessProcessResult BuildResult() =>
		new("UsrSampleProcess", "5c58c4c4-134b-4744-9c67-96d9c69c9d55", 1);

	[Test]
	[Description("Forwards the process identity and inline operations to the modify service and logs the result on success.")]
	public void Execute_ShouldMapInlineOperationsToService_WhenOperationsJsonProvided() {
		// Arrange
		ModifyBusinessProcessOptions options = new() {
			Environment = "sandbox",
			ProcessName = "UsrSampleProcess",
			OperationsJson = SampleOperations
		};
		_modifyBusinessProcessService.ModifyProcess("sandbox", Arg.Any<ModifyBusinessProcessRequest>())
			.Returns(BuildResult());

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "a successful edit should return the standard success exit code");
		_modifyBusinessProcessService.Received(1).ModifyProcess(
			"sandbox",
			Arg.Is<ModifyBusinessProcessRequest>(request =>
				request.ProcessName == "UsrSampleProcess" &&
				request.OperationsJson == SampleOperations));
		_logger.Received(1).WriteInfo(Arg.Is<string>(message => message.Contains("UsrSampleProcess")));
	}

	[Test]
	[Description("Reads the operations file content and forwards it to the modify service when --operations is provided.")]
	public void Execute_ShouldReadOperationsFile_WhenOperationsPathProvided() {
		// Arrange
		string operationsPath = Path.Combine(Path.GetTempPath(), $"clio-ops-test-{Guid.NewGuid():N}.json");
		File.WriteAllText(operationsPath, SampleOperations);
		try {
			ModifyBusinessProcessOptions options = new() {
				Environment = "sandbox",
				ProcessUid = "5c58c4c4-134b-4744-9c67-96d9c69c9d55",
				OperationsPath = operationsPath
			};
			_modifyBusinessProcessService.ModifyProcess("sandbox", Arg.Any<ModifyBusinessProcessRequest>())
				.Returns(BuildResult());

			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(0,
				because: "a successful edit from an operations file should return the standard success exit code");
			_modifyBusinessProcessService.Received(1).ModifyProcess(
				"sandbox",
				Arg.Is<ModifyBusinessProcessRequest>(request =>
					request.ProcessUid == "5c58c4c4-134b-4744-9c67-96d9c69c9d55" &&
					request.OperationsJson == SampleOperations));
		}
		finally {
			File.Delete(operationsPath);
		}
	}

	[Test]
	[Description("Returns a failure exit code and logs guidance when neither --name nor --uid is provided.")]
	public void Execute_ShouldFail_WhenNoIdentityProvided() {
		// Act
		int result = _command.Execute(new ModifyBusinessProcessOptions {
			Environment = "sandbox",
			OperationsJson = SampleOperations
		});

		// Assert
		result.Should().Be(1,
			because: "the command needs a process identity to edit");
		_modifyBusinessProcessService.DidNotReceiveWithAnyArgs().ModifyProcess(default!, default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("--name") && message.Contains("--uid")));
	}

	[Test]
	[Description("Returns a failure exit code and logs guidance when neither --operations nor --operations-json is provided.")]
	public void Execute_ShouldFail_WhenNoOperationsProvided() {
		// Act
		int result = _command.Execute(new ModifyBusinessProcessOptions {
			Environment = "sandbox",
			ProcessName = "UsrSampleProcess"
		});

		// Assert
		result.Should().Be(1,
			because: "the command requires an operations source");
		_modifyBusinessProcessService.DidNotReceiveWithAnyArgs().ModifyProcess(default!, default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("--operations") && message.Contains("--operations-json")));
	}

	[Test]
	[Description("Returns a failure exit code and logs a readable error when the CLI call omits environment-name.")]
	public void Execute_ShouldFail_WhenEnvironmentIsMissing() {
		// Act
		int result = _command.Execute(new ModifyBusinessProcessOptions {
			ProcessName = "UsrSampleProcess",
			OperationsJson = SampleOperations
		});

		// Assert
		result.Should().Be(1,
			because: "the command should fail fast when the environment is missing");
		_modifyBusinessProcessService.DidNotReceiveWithAnyArgs().ModifyProcess(default!, default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("Environment name is required")));
	}

	[Test]
	[Description("Returns a failure exit code and logs the service exception message when the modify service throws.")]
	public void Execute_ShouldFail_WhenServiceThrows() {
		// Arrange
		ModifyBusinessProcessOptions options = new() {
			Environment = "sandbox",
			ProcessName = "UsrSampleProcess",
			OperationsJson = SampleOperations
		};
		_modifyBusinessProcessService.ModifyProcess(Arg.Any<string>(), Arg.Any<ModifyBusinessProcessRequest>())
			.Returns<ModifyBusinessProcessResult>(_ =>
				throw new InvalidOperationException("Element 'StartEvent1' was not found in the process."));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1,
			because: "the command should propagate service-level failures as a non-zero exit code");
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("StartEvent1")));
	}
}
