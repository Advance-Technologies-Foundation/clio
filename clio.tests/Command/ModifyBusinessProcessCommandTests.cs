using System;
using System.Linq;
using Clio.Command;
using Clio.Command.ProcessModel;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ModifyBusinessProcessCommandTests {
	private const string SampleOperations =
		"[{\"op\":\"removeElement\",\"elementName\":\"StartEvent1\"}]";

	private IModifyBusinessProcessService _modifyBusinessProcessService;
	private IProcessDescriber _processDescriber;
	private ILogger _logger;
	private ModifyBusinessProcessCommand _command;

	[SetUp]
	public void Setup() {
		_modifyBusinessProcessService = Substitute.For<IModifyBusinessProcessService>();
		// The command reads the process back to catch a server that discarded an email block while answering
		// success. These operations carry no email block, so the substitute is never consulted.
		_processDescriber = Substitute.For<IProcessDescriber>();
		_logger = Substitute.For<ILogger>();
		_command = new ModifyBusinessProcessCommand(_modifyBusinessProcessService, _processDescriber, _logger);
	}

	[TearDown]
	public void TearDown() {
		_modifyBusinessProcessService.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

	private static ModifyBusinessProcessResult BuildResult() =>
		new("UsrSampleProcess", "5c58c4c4-134b-4744-9c67-96d9c69c9d55", 1);

	[Test]
	[Description("Writes every server warning out as a WARNING, not merely parsing it: the two outcomes it carries (a connection on an unregistered column, a cleared binding) are invisible in describe afterwards, so a warning that is deserialized and then dropped is the same defect as one never sent.")]
	public void Execute_ShouldWriteWarnings_WhenTheServerReportsThem() {
		// Arrange
		ModifyBusinessProcessOptions options = new() {
			Environment = "sandbox",
			ProcessName = "UsrSampleProcess",
			OperationsJson = SampleOperations
		};
		_modifyBusinessProcessService.ModifyProcess("sandbox", Arg.Any<ModifyBusinessProcessRequest>())
			.Returns(new ModifyBusinessProcessResult("UsrSampleProcess", "5c58c4c4-134b-4744-9c67-96d9c69c9d55", 1,
				new[] { "Connection 'OmniChat' is not registered", "Connection 'Account' was CLEARED" }));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "a warning is a caveat on a SUCCESSFUL edit, not a failure");
		_logger.Received(1).WriteWarning(Arg.Is<string>(text => text.Contains("OmniChat")));
		_logger.Received(1).WriteWarning(Arg.Is<string>(text => text.Contains("CLEARED")));
	}

	[Test]
	[Description("Writes no warning when the server reported none, so an empty channel cannot train a reader to ignore it.")]
	public void Execute_ShouldNotWriteAnyWarning_WhenTheServerReportsNone() {
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

		// Assert — stated as an assertion with a reason rather than a bare DidNotReceive, so the intent is
		// legible and the emptiness is what the test actually claims.
		_logger.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(ILogger.WriteWarning))
			.Should().Be(0,
				because: "the server reported nothing, and a warning invented from an absent member would train a "
					+ "reader to ignore the channel that carries the two outcomes describe cannot show afterwards");
		result.Should().Be(0, because: "no warnings is the ordinary successful edit");
	}

	[Test]
	[Category("Unit")]
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
	[Category("Unit")]
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
	[Category("Unit")]
	[Description("Returns a failure exit code and rejects the edit when both --name and --uid are provided.")]
	public void Execute_ShouldFail_WhenBothNameAndUidProvided() {
		// Act
		int result = _command.Execute(new ModifyBusinessProcessOptions {
			Environment = "sandbox",
			ProcessName = "UsrSampleProcess",
			ProcessUid = "5c58c4c4-134b-4744-9c67-96d9c69c9d55",
			OperationsJson = SampleOperations
		});

		// Assert
		result.Should().Be(1,
			because: "the process identity must be unambiguous — exactly one of --name or --uid is allowed");
		_modifyBusinessProcessService.DidNotReceiveWithAnyArgs().ModifyProcess(default!, default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("not both")));
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a failure exit code and logs guidance when no inline operations array is provided.")]
	public void Execute_ShouldFail_WhenNoOperationsProvided() {
		// Act
		int result = _command.Execute(new ModifyBusinessProcessOptions {
			Environment = "sandbox",
			ProcessName = "UsrSampleProcess"
		});

		// Assert
		result.Should().Be(1,
			because: "the command requires an inline operations array");
		_modifyBusinessProcessService.DidNotReceiveWithAnyArgs().ModifyProcess(default!, default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("An operations array is required.")));
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a failure exit code and logs a readable error when the call omits environment-name.")]
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
	[Category("Unit")]
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
