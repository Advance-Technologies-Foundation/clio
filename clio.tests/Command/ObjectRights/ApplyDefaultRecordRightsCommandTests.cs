using System;
using System.Collections.Generic;
using Clio.Command;
using Clio.Command.ObjectRights;
using Clio.Common;
using Clio.Common.ObjectRights;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.ObjectRights;

/// <summary>
/// <c>apply-default-record-rights</c>: refused on an object whose record permissions are off, the run started exactly
/// once, and the outcome — completed, failed, still running at the deadline — read from the run's SysProcessLog status.
/// </summary>
[TestFixture]
[Property("Module", "Command")]
public class ApplyDefaultRecordRightsCommandTests : BaseCommandTests<ApplyDefaultRecordRightsOptions> {

	private static readonly Guid SchemaUId = Guid.Parse("35a9057f-800d-414b-acfb-c919c215857a");
	private static readonly Guid ProcessId = Guid.Parse("6e719418-f149-47a5-8fbe-9faa37080438");

	private ApplyDefaultRecordRightsCommand _command;
	private IObjectRightsReader _reader;
	private IRecordRightsActualization _actualization;
	private IObjectRecordCounter _counter;
	private IRetryDelay _delay;
	private IInteractiveConsole _console;
	private ILogger _logger;
	private List<string> _errors;
	private List<string> _infos;
	private List<string> _warnings;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<ApplyDefaultRecordRightsCommand>();
	}

	public override void TearDown() {
		_reader.ClearReceivedCalls();
		_actualization.ClearReceivedCalls();
		_counter.ClearReceivedCalls();
		_delay.ClearReceivedCalls();
		_console.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_reader = Substitute.For<IObjectRightsReader>();
		_actualization = Substitute.For<IRecordRightsActualization>();
		_actualization.Start(Arg.Any<Guid>(), Arg.Any<CreatioRequestOptions>())
			.Returns(new RunProcessResponse { Status = "running", ProcessId = ProcessId.ToString() });
		_counter = Substitute.For<IObjectRecordCounter>();
		_delay = Substitute.For<IRetryDelay>();
		_console = Substitute.For<IInteractiveConsole>();
		_logger = Substitute.For<ILogger>();
		_errors = new List<string>();
		_infos = new List<string>();
		_warnings = new List<string>();
		_logger.When(l => l.WriteWarning(Arg.Any<string>())).Do(call => _warnings.Add((string)call[0]));
		_logger.When(l => l.WriteError(Arg.Any<string>())).Do(call => _errors.Add((string)call[0]));
		_logger.When(l => l.WriteInfo(Arg.Any<string>())).Do(call => _infos.Add((string)call[0]));
		containerBuilder.AddTransient(_ => _reader);
		containerBuilder.AddTransient(_ => _actualization);
		containerBuilder.AddTransient(_ => _counter);
		containerBuilder.AddSingleton(_ => _delay);
		containerBuilder.AddTransient(_ => _console);
		containerBuilder.AddTransient(_ => _logger);
	}

	private void ObjectIs(bool recordsOn) =>
		_reader.GetObjectRights(Arg.Any<string>(), Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(true, "UsrFoo", "Foo", true, Array.Empty<RoleOperationRights>(),
				AdministratedByRecords: recordsOn, RecordRules: Array.Empty<DefaultRecordRule>(), SchemaUId: SchemaUId));

	private static ApplyDefaultRecordRightsOptions Options(Action<ApplyDefaultRecordRightsOptions> tweak = null) {
		ApplyDefaultRecordRightsOptions options = new() { EntitySchemaName = "UsrFoo", Confirm = true, TimeoutSeconds = 30 };
		tweak?.Invoke(options);
		return options;
	}

	private static ProcessRunStatus Status(Guid id, string name) => new(id, name);

	[Test]
	[Description("An object whose record permissions are OFF is refused and no run is started.")]
	public void Execute_ShouldRefuse_WhenRecordPermissionsAreOff() {
		// Arrange
		ObjectIs(false);

		// Act
		int exitCode = _command.Execute(Options());

		// Assert
		exitCode.Should().Be(1, because: "there is nothing to apply while record rights are not evaluated");
		_errors.Should().Contain(e => e.Contains("are OFF"), because: "the refusal says why");
		_actualization.DidNotReceiveWithAnyArgs().Start(default, default);
	}

	[Test]
	[Description("A run is started once with the object's schema UId and followed until it completes; exit 0.")]
	public void Execute_ShouldStartOnceAndReportCompleted() {
		// Arrange
		ObjectIs(true);
		_actualization.ReadStatus(ProcessId, Arg.Any<CreatioRequestOptions>())
			.Returns(Status(ProcessRunStatus.Running, "Running"), Status(ProcessRunStatus.Completed, "Completed"));

		// Act
		int exitCode = _command.Execute(Options());

		// Assert
		exitCode.Should().Be(0, because: "the run completed");
		_actualization.Received(1).Start(SchemaUId, Arg.Any<CreatioRequestOptions>());
		_delay.Received(1).Wait(Arg.Any<TimeSpan>());
		_infos.Should().Contain(i => i.Contains("completed") && i.Contains(ProcessId.ToString()),
			because: "the outcome names the process");
	}

	[TestCase("f942c08d-b6e2-df11-971b-001d60e938c6", "Error")]
	[TestCase("1be78f3e-234d-4d6a-869a-dc07253fd2f3", "Canceled")]
	[Description("A run that ends in error or is cancelled fails the call and names the status and the process.")]
	public void Execute_ShouldFail_WhenRunEndsBadly(string statusId, string statusName) {
		// Arrange
		ObjectIs(true);
		_actualization.ReadStatus(ProcessId, Arg.Any<CreatioRequestOptions>()).Returns(Status(Guid.Parse(statusId), statusName));

		// Act
		int exitCode = _command.Execute(Options());

		// Assert
		exitCode.Should().Be(1, because: "the run did not complete");
		_errors.Should().Contain(e => e.Contains(statusName) && e.Contains(ProcessId.ToString()),
			because: "the failure names the status and the process");
	}

	[Test]
	[Description("A run still going when the wait ends is reported as still running with its process id, exit 0 — not a failure and not a reason to start it again.")]
	public void Execute_ShouldReportStillRunning_WhenWaitEnds() {
		// Arrange
		ObjectIs(true);
		_actualization.ReadStatus(ProcessId, Arg.Any<CreatioRequestOptions>()).Returns(Status(ProcessRunStatus.Running, "Running"));
		// Each poll interval passes for real time: the wait of one second ends after a few polls.
		_delay.When(d => d.Wait(Arg.Any<TimeSpan>())).Do(call => System.Threading.Thread.Sleep((TimeSpan)call[0]));

		// Act
		int exitCode = _command.Execute(Options(o => o.TimeoutSeconds = 1));

		// Assert
		exitCode.Should().Be(0, because: "the run was started and is still going");
		_infos.Should().Contain(i => i.Contains("still running") && i.Contains("Do NOT start it again"),
			because: "the caller must not start a second run");
		_actualization.Received(1).Start(Arg.Any<Guid>(), Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("wait=false starts the run and returns its process id without polling.")]
	public void Execute_ShouldNotPoll_WhenWaitIsFalse() {
		// Arrange
		ObjectIs(true);

		// Act
		int exitCode = _command.Execute(Options(o => o.Wait = false));

		// Assert
		exitCode.Should().Be(0, because: "the run was started");
		_actualization.DidNotReceiveWithAnyArgs().ReadStatus(default, default);
		_infos.Should().Contain(i => i.Contains(ProcessId.ToString()), because: "the process id is returned");
	}

	[Test]
	[Description("A launch the platform refused fails the call with its reason.")]
	public void Execute_ShouldFail_WhenLaunchRefused() {
		// Arrange
		ObjectIs(true);
		_actualization.Start(Arg.Any<Guid>(), Arg.Any<CreatioRequestOptions>())
			.Returns(new RunProcessResponse { Status = "not-started", Error = "'X' was not started: denied." });

		// Act
		int exitCode = _command.Execute(Options());

		// Assert
		exitCode.Should().Be(1, because: "nothing was started");
		_errors.Should().Contain(e => e.Contains("denied"), because: "the platform's reason is passed on");
	}

	[Test]
	[Description("A non-interactive run without --confirm starts nothing.")]
	public void Execute_ShouldRefuse_WhenNotConfirmed() {
		// Arrange
		ObjectIs(true);
		_console.IsInteractive.Returns(false);

		// Act
		int exitCode = _command.Execute(Options(o => o.Confirm = false));

		// Assert
		exitCode.Should().Be(1, because: "a destructive run needs confirmation");
		_actualization.DidNotReceiveWithAnyArgs().Start(default, default);
		_errors.Should().Contain(e => e.Contains("--confirm") && !e.Contains("--preview"),
			because: "the refusal names only options this command has: it has no --preview");
	}

	[Test]
	[Description("A launch that got no answer may already have started the run: the failure says so and forbids starting it again.")]
	public void Execute_ShouldWarnRunMayBeGoing_WhenLaunchTimesOut() {
		// Arrange
		ObjectIs(true);
		_actualization.Start(Arg.Any<Guid>(), Arg.Any<CreatioRequestOptions>())
			.Returns(_ => throw new TimeoutException("no answer"));

		// Act
		int exitCode = _command.Execute(Options());

		// Assert
		exitCode.Should().Be(1, because: "the outcome is not known");
		_errors.Should().Contain(e => e.Contains("MAY already be going") && e.Contains("Do NOT start it again"),
			because: "a re-sent launch would start a second heavy run");
	}

	[Test]
	[Description("When the call's deadline no longer covers one full launch request, the launch is not sent and nothing starts.")]
	public void Execute_ShouldNotLaunch_WhenDeadlineLeavesNoTime() {
		// Arrange
		ObjectIs(true);

		// Act
		int exitCode = _command.Execute(Options(o => { o.TimeOut = 25_000; o.CallBudget = TimeSpan.FromMilliseconds(1); }));

		// Assert
		exitCode.Should().Be(1, because: "a launch cut short by the deadline would leave its outcome unknown");
		_actualization.DidNotReceiveWithAnyArgs().Start(default, default);
		_errors.Should().Contain(e => e.Contains("nothing was started"), because: "the refusal says nothing started");
	}

	[Test]
	[Description("An object that cannot be read fails and starts nothing.")]
	public void Execute_ShouldFail_WhenObjectCannotBeRead() {
		// Arrange
		_reader.GetObjectRights(Arg.Any<string>(), Arg.Any<CreatioRequestOptions>())
			.Returns(ObjectRightsInfo.ReadFailed("UsrFoo", "Request Error"));

		// Act
		int exitCode = _command.Execute(Options());

		// Assert
		exitCode.Should().Be(1, because: "nothing can be applied to an object that was not read");
		_errors.Should().Contain(e => e.Contains("Nothing was started"), because: "the failure says nothing started");
		_actualization.DidNotReceiveWithAnyArgs().Start(default, default);
	}

	[Test]
	[Description("A launch the platform queued without a process id is reported as queued, exit 0, with no polling.")]
	public void Execute_ShouldReportQueued_WhenNoProcessId() {
		// Arrange
		ObjectIs(true);
		_actualization.Start(Arg.Any<Guid>(), Arg.Any<CreatioRequestOptions>())
			.Returns(new RunProcessResponse { Status = "queued-background" });

		// Act
		int exitCode = _command.Execute(Options());

		// Assert
		exitCode.Should().Be(0, because: "for a queued run the launch is the outcome");
		_infos.Should().Contain(i => i.Contains("queued"), because: "the result says so");
		_actualization.DidNotReceiveWithAnyArgs().ReadStatus(default, default);
	}

	[Test]
	[Description("A status read that fails while waiting is a warning: the run was started, so it is reported as still running, exit 0.")]
	public void Execute_ShouldReportStillRunning_WhenStatusCannotBeRead() {
		// Arrange
		ObjectIs(true);
		_actualization.ReadStatus(ProcessId, Arg.Any<CreatioRequestOptions>())
			.Returns(_ => throw new InvalidOperationException("SelectQuery failed: denied"));

		// Act
		int exitCode = _command.Execute(Options());

		// Assert
		exitCode.Should().Be(0, because: "the launch succeeded");
		_warnings.Should().Contain(w => w.Contains("could not be read"), because: "the failed read is reported");
		_infos.Should().Contain(i => i.Contains("still running") && i.Contains(ProcessId.ToString()),
			because: "the process to check is named");
	}
}
