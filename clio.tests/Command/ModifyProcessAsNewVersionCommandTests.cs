using System;
using System.Linq;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Unit coverage for <see cref="ModifyProcessAsNewVersionCommand.Execute"/>.
/// </summary>
/// <remarks>
/// The body had no fixture: the tool tests substitute the command and the service tests bypass it, so the
/// identity gate, the load-bearing "what the environment runs did not change" line, the warning relay and the
/// exit-code mapping never ran. Modelled on <c>ModifyBusinessProcessCommandTests</c>, the sibling of the same
/// command family.
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ModifyProcessAsNewVersionCommandTests {

	private const string ProcessName = "UsrSampleProcess";
	private const string ProcessUid = "5c58c4c4-134b-4744-9c67-96d9c69c9d55";
	private const string VersionName = "UsrSampleProcessCustom1";
	private const string VersionUid = "11111111-2222-3333-4444-555555555555";
	private const string RootUid = "332eac25-1443-4e4e-a972-6c0e66cb9243";

	private IModifyProcessAsNewVersionService _service;
	private ILogger _logger;
	private ModifyProcessAsNewVersionCommand _command;

	[SetUp]
	public void Setup() {
		_service = Substitute.For<IModifyProcessAsNewVersionService>();
		_logger = Substitute.For<ILogger>();
		_command = new ModifyProcessAsNewVersionCommand(_service, _logger);
	}

	[TearDown]
	public void TearDown() {
		_service.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

	private static ModifyProcessAsNewVersionOptions ByName() =>
		new() { Environment = "sandbox", ProcessName = ProcessName, PackageName = "Custom" };

	private static ModifyProcessAsNewVersionResult Created(int? version = 1, bool? isActive = false) =>
		new(VersionName, VersionUid, version, isActive, RootUid, 2);

	private int WarningCount() => _logger.ReceivedCalls()
		.Count(call => call.GetMethodInfo().Name == nameof(ILogger.WriteWarning));

	[Test]
	[Description("Forwards the source identity and target package, and states on EVERY success that the source version is still what runs — the one thing that did not change, and the omission that makes an agent report the edit as live and stop.")]
	public void Execute_ShouldReportTheCreatedVersionAndThatTheSourceStillRuns_OnSuccess() {
		// Arrange
		_service.ModifyAsNewVersion("sandbox", Arg.Any<ModifyProcessAsNewVersionRequest>())
			.Returns(Created());

		// Act
		int result = _command.Execute(ByName());

		// Assert
		result.Should().Be(0, because: "a saved version is the ordinary success");
		_service.Received(1).ModifyAsNewVersion("sandbox",
			Arg.Is<ModifyProcessAsNewVersionRequest>(request =>
				request.ProcessName == ProcessName && request.PackageName == "Custom"));
		_logger.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains("Version 1") && message.Contains(VersionName) && message.Contains(RootUid)));
		_logger.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains("still the actual one")));
		WarningCount().Should().Be(0, because: "nothing about this create is a caveat");
	}

	[Test]
	[Description("A version the environment reports as ACTIVE is called out as unexpected rather than narrated as a normal create, because creating a version must not change what runs.")]
	public void Execute_ShouldFlagTheUnexpected_WhenTheCreatedVersionIsReportedActive() {
		// Arrange
		_service.ModifyAsNewVersion("sandbox", Arg.Any<ModifyProcessAsNewVersionRequest>())
			.Returns(Created(isActive: true));

		// Act
		int result = _command.Execute(ByName());

		// Assert
		result.Should().Be(0, because: "the version was created; the flag is a caveat on it");
		_logger.Received(1).WriteInfo(Arg.Is<string>(message => message.Contains("unexpected for a create")));
	}

	[Test]
	[Description("An UNREPORTED active-version flag gets a neutral sentence instead of the reassuring one. 'The source still runs' is a claim about what the environment executes, and the read half of this feature is built on absent meaning NOT ESTABLISHED rather than false.")]
	public void Execute_ShouldNotClaimTheSourceStillRuns_WhenTheFlagWasNotReported() {
		// Arrange
		_service.ModifyAsNewVersion("sandbox", Arg.Any<ModifyProcessAsNewVersionRequest>())
			.Returns(Created(isActive: null));

		// Act
		int result = _command.Execute(ByName());

		// Assert
		result.Should().Be(0, because: "the version was created either way");
		_logger.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains("did not report which version is actual")));
		_logger.DidNotReceive().WriteInfo(Arg.Is<string>(message => message.Contains("still the actual one")));
	}

	[Test]
	[Description("An UNREPORTED version number is not printed as 0. In this feature's own vocabulary 0 means the schema is a family ROOT, which is the opposite of what a new version is.")]
	public void Execute_ShouldNotPrintZero_WhenTheVersionNumberWasNotReported() {
		// Arrange
		_service.ModifyAsNewVersion("sandbox", Arg.Any<ModifyProcessAsNewVersionRequest>())
			.Returns(Created(version: null));

		// Act
		int result = _command.Execute(ByName());

		// Assert
		result.Should().Be(0, because: "the version exists; only its number was not reported");
		_logger.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains("A new version") && !message.Contains("Version 0")));
	}

	[Test]
	[Description("Writes every server warning out as a WARNING rather than deserializing and dropping it.")]
	public void Execute_ShouldWriteServerWarnings_WhenTheServerReportsThem() {
		// Arrange
		_service.ModifyAsNewVersion("sandbox", Arg.Any<ModifyProcessAsNewVersionRequest>())
			.Returns(new ModifyProcessAsNewVersionResult(VersionName, VersionUid, 1, false, RootUid, 2,
				["Connection 'OmniChat' is not registered"]));

		// Act
		int result = _command.Execute(ByName());

		// Assert
		result.Should().Be(0, because: "a warning is a caveat on a successful save, not a failure");
		_logger.Received(1).WriteWarning(Arg.Is<string>(message => message.Contains("OmniChat")));
	}

	[Test]
	[Description("Returns a failure exit code and names the rule when neither --name nor --uid is given.")]
	public void Execute_ShouldFail_WhenNoIdentityProvided() {
		// Act
		int result = _command.Execute(new ModifyProcessAsNewVersionOptions { Environment = "sandbox" });

		// Assert
		result.Should().Be(1, because: "there is no source process to clone");
		_service.DidNotReceiveWithAnyArgs().ModifyAsNewVersion(default!, default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("--name") && message.Contains("--uid")));
	}

	[Test]
	[Description("Returns a failure exit code and says which rule was broken when BOTH --name and --uid are given.")]
	public void Execute_ShouldFail_WhenBothNameAndUidProvided() {
		// Act
		int result = _command.Execute(new ModifyProcessAsNewVersionOptions {
			Environment = "sandbox",
			ProcessName = ProcessName,
			ProcessUid = ProcessUid
		});

		// Assert
		result.Should().Be(1, because: "the source identity must be unambiguous");
		_service.DidNotReceiveWithAnyArgs().ModifyAsNewVersion(default!, default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("not both")));
	}

	[Test]
	[Description("Returns a failure exit code and a readable error when the call omits environment-name.")]
	public void Execute_ShouldFail_WhenEnvironmentIsMissing() {
		// Act
		int result = _command.Execute(new ModifyProcessAsNewVersionOptions { ProcessName = ProcessName });

		// Assert
		result.Should().Be(1, because: "the command must fail fast rather than reach an unnamed environment");
		_service.DidNotReceiveWithAnyArgs().ModifyAsNewVersion(default!, default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("Environment name is required")));
	}

	[Test]
	[Description("Returns a failure exit code carrying the service's message when the save throws — a failure that still names a version is NOT a failed create, and the message is the only place a caller learns the version exists and cannot be deleted.")]
	public void Execute_ShouldFail_WhenServiceThrows() {
		// Arrange
		_service.ModifyAsNewVersion(Arg.Any<string>(), Arg.Any<ModifyProcessAsNewVersionRequest>())
			.Returns<ModifyProcessAsNewVersionResult>(_ => throw new InvalidOperationException(
				$"The version '{VersionName}' WAS created and still exists — a version cannot be deleted."));

		// Act
		int result = _command.Execute(ByName());

		// Assert
		result.Should().Be(1, because: "a service-level failure is a non-zero exit code");
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains(VersionName) && message.Contains("cannot be deleted")));
	}

}
