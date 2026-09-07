using System;
using System.Linq;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Unit coverage for <see cref="SetActiveProcessVersionCommand.Execute"/>.
/// </summary>
/// <remarks>
/// The body had no fixture at all: the tool tests substitute the command and the service tests bypass it, so
/// nothing executed the identity gate, the load-bearing instance-pinning advisory, the warning relay or the
/// exit-code mapping. On the more dangerous of the two version operations — a rollback whose CLI output is
/// the operator's only feedback — that left the output itself unasserted. Modelled on
/// <c>ModifyBusinessProcessCommandTests</c>, the sibling of the same command family.
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class SetActiveProcessVersionCommandTests {

	private const string VersionName = "UsrSampleProcessCustom2";
	private const string VersionUid = "5c58c4c4-134b-4744-9c67-96d9c69c9d55";

	private ISetActiveProcessVersionService _service;
	private ILogger _logger;
	private SetActiveProcessVersionCommand _command;

	[SetUp]
	public void Setup() {
		_service = Substitute.For<ISetActiveProcessVersionService>();
		_logger = Substitute.For<ILogger>();
		_command = new SetActiveProcessVersionCommand(_service, _logger);
	}

	[TearDown]
	public void TearDown() {
		_service.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

	private static SetActiveProcessVersionOptions ByName() =>
		new() { Environment = "sandbox", VersionName = VersionName };

	private int WarningCount() => _logger.ReceivedCalls()
		.Count(call => call.GetMethodInfo().Name == nameof(ILogger.WriteWarning));

	[Test]
	[Description("Forwards the version identity and reports the version the environment READ BACK, plus the advisory that activation reaches new instances only — the half of 'activated' an agent otherwise assumes wrongly and then reports as a completed rollback.")]
	public void Execute_ShouldReportTheReadBackVersionAndTheInstancePinning_OnSuccess() {
		// Arrange
		_service.SetActiveVersion("sandbox", Arg.Any<SetActiveProcessVersionRequest>())
			.Returns(new SetActiveProcessVersionResult(VersionName, VersionUid, 0));

		// Act
		int result = _command.Execute(ByName());

		// Assert
		result.Should().Be(0, because: "a clean activation is the ordinary success");
		_service.Received(1).SetActiveVersion("sandbox",
			Arg.Is<SetActiveProcessVersionRequest>(request => request.VersionName == VersionName));
		_logger.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains(VersionName) && message.Contains(VersionUid)));
		_logger.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains("Instances already running stay")));
		WarningCount().Should().Be(0,
			because: "nothing about this activation is partial, and a warning invented from a clean result "
				+ "would train an operator to ignore the channel that carries the ones that are not");
	}

	[Test]
	[Description("Warns when the platform left siblings flagged active. The platform logs and SWALLOWS a sibling deactivation failure, so an unqualified success over a two-members-active family is exactly the state where package order — not this call — decides what the environment runs.")]
	public void Execute_ShouldWarn_WhenSiblingsRemainFlaggedActive() {
		// Arrange
		_service.SetActiveVersion("sandbox", Arg.Any<SetActiveProcessVersionRequest>())
			.Returns(new SetActiveProcessVersionResult(VersionName, VersionUid, 1));

		// Act
		int result = _command.Execute(ByName());

		// Assert
		result.Should().Be(0,
			because: "the write DID take effect, so exit 1 would tell the operator to retry something that "
				+ "already happened");
		_logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains("1 sibling(s)") && message.Contains("package order")));
	}

	[Test]
	[Description("Warns when the read-back names a DIFFERENT version than the request. The result is read from the environment rather than echoed precisely so this is visible, and printing 'Version X is now the actual one' for some other X with exit 0 hides it.")]
	public void Execute_ShouldWarn_WhenTheReadBackNamesAnotherVersion() {
		// Arrange
		_service.SetActiveVersion("sandbox", Arg.Any<SetActiveProcessVersionRequest>())
			.Returns(new SetActiveProcessVersionResult("UsrSampleProcessCustom1", VersionUid, 0));

		// Act
		int result = _command.Execute(ByName());

		// Assert
		result.Should().Be(0, because: "the environment answered about its own state, which is not a failure");
		_logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains("UsrSampleProcessCustom1") && message.Contains("NOT the version this call asked")));
	}

	[Test]
	[Description("A read-back UId rendered in a different case is not a mismatch: the server is free to format a GUID however it likes, and a warning there would cry wolf on every uid-addressed activation.")]
	public void Execute_ShouldNotWarn_WhenTheReadBackUidDiffersOnlyInCase() {
		// Arrange
		_service.SetActiveVersion("sandbox", Arg.Any<SetActiveProcessVersionRequest>())
			.Returns(new SetActiveProcessVersionResult(VersionName, VersionUid.ToUpperInvariant(), 0));

		// Act
		int result = _command.Execute(new SetActiveProcessVersionOptions {
			Environment = "sandbox",
			VersionUid = VersionUid
		});

		// Assert
		result.Should().Be(0, because: "the requested version IS the one the environment reports");
		WarningCount().Should().Be(0,
			because: "GUID rendering is not identity, and a false mismatch warning would make the real one "
				+ "unreadable");
	}

	[Test]
	[Description("Writes every server warning out as a WARNING — notably that activation re-saved every member of the family, a write nobody asked for by name and one describe cannot show afterwards.")]
	public void Execute_ShouldWriteServerWarnings_WhenTheServerReportsThem() {
		// Arrange
		_service.SetActiveVersion("sandbox", Arg.Any<SetActiveProcessVersionRequest>())
			.Returns(new SetActiveProcessVersionResult(VersionName, VersionUid, 0,
				["Activation re-saved all 3 members of the family."]));

		// Act
		int result = _command.Execute(ByName());

		// Assert
		result.Should().Be(0, because: "a warning is a caveat on a successful activation, not a failure");
		_logger.Received(1).WriteWarning(Arg.Is<string>(message => message.Contains("re-saved")));
	}

	[Test]
	[Description("Returns a failure exit code and names the rule when neither --name nor --uid is given.")]
	public void Execute_ShouldFail_WhenNoIdentityProvided() {
		// Act
		int result = _command.Execute(new SetActiveProcessVersionOptions { Environment = "sandbox" });

		// Assert
		result.Should().Be(1, because: "there is no version to activate");
		_service.DidNotReceiveWithAnyArgs().SetActiveVersion(default!, default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("--name") && message.Contains("--uid")));
	}

	[Test]
	[Description("Returns a failure exit code and says which rule was broken when BOTH --name and --uid are given, rather than silently resolving one way.")]
	public void Execute_ShouldFail_WhenBothNameAndUidProvided() {
		// Act
		int result = _command.Execute(new SetActiveProcessVersionOptions {
			Environment = "sandbox",
			VersionName = VersionName,
			VersionUid = VersionUid
		});

		// Assert
		result.Should().Be(1, because: "the version identity must be unambiguous");
		_service.DidNotReceiveWithAnyArgs().SetActiveVersion(default!, default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("not both")));
	}

	[Test]
	[Description("Returns a failure exit code and a readable error when the call omits environment-name.")]
	public void Execute_ShouldFail_WhenEnvironmentIsMissing() {
		// Act
		int result = _command.Execute(new SetActiveProcessVersionOptions { VersionName = VersionName });

		// Assert
		result.Should().Be(1, because: "the command must fail fast rather than reach an unnamed environment");
		_service.DidNotReceiveWithAnyArgs().SetActiveVersion(default!, default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("Environment name is required")));
	}

	[Test]
	[Description("Returns a failure exit code carrying the service's message when activation throws — the message is the only place a caller learns which version the environment reports as actual after a failed switch.")]
	public void Execute_ShouldFail_WhenServiceThrows() {
		// Arrange
		_service.SetActiveVersion(Arg.Any<string>(), Arg.Any<SetActiveProcessVersionRequest>())
			.Returns<SetActiveProcessVersionResult>(_ => throw new InvalidOperationException(
				"The version the environment reports as actual is 'UsrSampleProcessCustom1'."));

		// Act
		int result = _command.Execute(ByName());

		// Assert
		result.Should().Be(1, because: "a service-level failure is a non-zero exit code");
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("UsrSampleProcessCustom1")));
	}

}
