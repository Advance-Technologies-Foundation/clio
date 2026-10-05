using System;
using System.Collections.Generic;
using System.Linq;
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
		_actualization.FindRunning(Arg.Any<CreatioRequestOptions>()).Returns(Array.Empty<RunningUpdate>());
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


	// Each poll interval passes for real time, so a wait that is never ended by a final status ends by the clock.
	private void RealDelay() =>
		_delay.When(d => d.Wait(Arg.Any<TimeSpan>())).Do(call => System.Threading.Thread.Sleep((TimeSpan)call[0]));

	[Test]
	[Description("When every status read fails, the result says the status could not be read — not 'still running' — and the reads go on until the wait ends instead of stopping at the first fault.")]
	public void Execute_ShouldReportStatusUnknown_WhenEveryReadFails() {
		// Arrange
		ObjectIs(true);
		RealDelay();
		_actualization.ReadStatus(ProcessId, Arg.Any<CreatioRequestOptions>())
			.Returns(_ => throw new InvalidOperationException("SelectQuery failed: denied"));

		// Act
		int exitCode = _command.Execute(Options(o => o.TimeoutSeconds = 3));

		// Assert
		exitCode.Should().Be(0, because: "the launch succeeded");
		_actualization.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IRecordRightsActualization.ReadStatus))
			.Should().BeGreaterThan(1, because: "one failed read does not end the wait");
		_warnings.Should().Contain(w => w.Contains("could not be read") && w.Contains("denied") && w.Contains(ProcessId.ToString()),
			because: "the unknown status and the process to check are named");
		_infos.Should().NotContain(i => i.Contains("still running"), because: "clio never saw the run going");
	}

	[Test]
	[Description("A run whose SysProcessLog row never appears is reported as status unknown, not as still running.")]
	public void Execute_ShouldReportStatusUnknown_WhenNoLogRow() {
		// Arrange
		ObjectIs(true);
		RealDelay();
		_actualization.ReadStatus(ProcessId, Arg.Any<CreatioRequestOptions>()).Returns((ProcessRunStatus)null);

		// Act
		int exitCode = _command.Execute(Options(o => o.TimeoutSeconds = 1));

		// Assert
		exitCode.Should().Be(0, because: "the launch succeeded");
		_warnings.Should().Contain(w => w.Contains("no SysProcessLog row was found"), because: "the reason is named");
		_infos.Should().NotContain(i => i.Contains("still running"), because: "clio never saw the run going");
	}

	[Test]
	[Description("A status read that times out is retried: the run's Completed status read afterwards is reported.")]
	public void Execute_ShouldReportCompleted_WhenAReadTimesOutFirst() {
		// Arrange
		ObjectIs(true);
		int reads = 0;
		_actualization.ReadStatus(ProcessId, Arg.Any<CreatioRequestOptions>()).Returns(_ => ++reads == 1
			? throw new TimeoutException("no answer")
			: new ProcessRunStatus(ProcessRunStatus.Completed, "Completed"));

		// Act
		int exitCode = _command.Execute(Options());

		// Assert
		exitCode.Should().Be(0, because: "the run completed");
		_infos.Should().Contain(i => i.Contains("completed"), because: "the second read saw the end");
	}

	[Test]
	[Description("On MCP the wait is cut to what is left of the call budget, so a run still going is reported long before --timeout-seconds and the worker is not killed after the launch.")]
	public void Execute_ShouldCutTheWaitToTheCallBudget() {
		// Arrange
		ObjectIs(true);
		RealDelay();
		_actualization.ReadStatus(ProcessId, Arg.Any<CreatioRequestOptions>())
			.Returns(new ProcessRunStatus(ProcessRunStatus.Running, "Running"));
		System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();

		// Act
		int exitCode = _command.Execute(Options(o => {
			o.TimeoutSeconds = 60;
			o.TimeOut = 500;
			o.CallBudget = TimeSpan.FromSeconds(2);
		}));

		// Assert
		watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), because: "the 2 s call budget, not the 60 s wait, ends it");
		exitCode.Should().Be(0, because: "a run still going is not a failure");
		_infos.Should().Contain(i => i.Contains("still running"), because: "the run was seen going");
	}

	[TestCase(0, TestName = "Execute_ShouldRefuseZeroTimeout")]
	[TestCase(-5, TestName = "Execute_ShouldRefuseNegativeTimeout")]
	[Description("--timeout-seconds must be positive; nothing is read or started otherwise.")]
	public void Execute_ShouldRefuseNonPositiveTimeout(int seconds) {
		// Act
		int exitCode = _command.Execute(Options(o => o.TimeoutSeconds = seconds));

		// Assert
		exitCode.Should().Be(1, because: "a wait of no time is meaningless");
		_errors.Should().Contain(e => e.Contains("--timeout-seconds must be a positive"), because: "the option is named");
		_reader.DidNotReceiveWithAnyArgs().GetObjectRights(default, default);
	}

	[Test]
	[Description("An object read without a schema UId starts nothing: the process needs the UId.")]
	public void Execute_ShouldRefuse_WhenSchemaUIdIsMissing() {
		// Arrange
		_reader.GetObjectRights(Arg.Any<string>(), Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(true, "UsrFoo", "Foo", true, Array.Empty<RoleOperationRights>(),
				AdministratedByRecords: true, RecordRules: Array.Empty<DefaultRecordRule>()));

		// Act
		int exitCode = _command.Execute(Options());

		// Assert
		exitCode.Should().Be(1, because: "the launch has no object to name");
		_errors.Should().Contain(e => e.Contains("schema UId"), because: "the reason is named");
		_actualization.DidNotReceiveWithAnyArgs().Start(default, default);
	}

	[Test]
	[Description("A launch the server answered with a fault (not a missing answer) says the run was NOT started — the opposite advice of a launch with no answer.")]
	public void Execute_ShouldSayNotStarted_WhenLaunchFailsDefinitely() {
		// Arrange
		ObjectIs(true);
		_actualization.Start(Arg.Any<Guid>(), Arg.Any<CreatioRequestOptions>())
			.Returns(_ => throw new InvalidOperationException("Unexpected response: an HTML page"));

		// Act
		int exitCode = _command.Execute(Options());

		// Assert
		exitCode.Should().Be(1, because: "nothing was started");
		_errors.Should().Contain(e => e.Contains("was not started"), because: "the server answered with a fault");
		_errors.Should().NotContain(e => e.Contains("MAY already be going"), because: "that advice is for a missing answer only");
	}

	[Test]
	[Description("A record count that fails before the confirmation is reported in the summary and does not stop the run.")]
	public void Execute_ShouldStart_WhenCountFails() {
		// Arrange
		ObjectIs(true);
		_counter.CountRecords(Arg.Any<string>(), Arg.Any<CreatioRequestOptions>())
			.Returns(_ => throw new InvalidOperationException("SelectQuery failed: denied"));
		_console.IsInteractive.Returns(true);
		_console.Prompt(Arg.Any<string>()).Returns(true);
		List<string> prompts = new();
		_logger.When(l => l.WriteWarning(Arg.Any<string>())).Do(call => prompts.Add((string)call[0]));
		_actualization.ReadStatus(ProcessId, Arg.Any<CreatioRequestOptions>())
			.Returns(new ProcessRunStatus(ProcessRunStatus.Completed, "Completed"));

		// Act
		int exitCode = _command.Execute(Options(o => o.Confirm = false));

		// Assert
		exitCode.Should().Be(0, because: "the count is a fact for the user, not part of the run");
		prompts.Should().Contain(w => w.Contains("existing records not counted"), because: "the prompt says the count failed");
		_actualization.Received(1).Start(Arg.Any<Guid>(), Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("When a record-rights update is already running on the environment, the call warns before the confirmation (naming the process) and still starts — the log cannot say which object the running update is for.")]
	public void Execute_ShouldWarn_WhenAnUpdateIsAlreadyRunning() {
		// Arrange
		ObjectIs(true);
		Guid running = Guid.Parse("11111111-2222-3333-4444-555555555555");
		_actualization.FindRunning(Arg.Any<CreatioRequestOptions>())
			.Returns(new[] { new RunningUpdate(running, "2026-10-05T10:00:00") });
		_actualization.ReadStatus(ProcessId, Arg.Any<CreatioRequestOptions>())
			.Returns(new ProcessRunStatus(ProcessRunStatus.Completed, "Completed"));

		// Act
		int exitCode = _command.Execute(Options());

		// Assert
		exitCode.Should().Be(0, because: "the warning does not block: the running update may be for another object");
		_warnings.Should().Contain(w => w.Contains("already running") && w.Contains(running.ToString()),
			because: "the running process is named so the user can check it");
		Received.InOrder(() => {
			_actualization.FindRunning(Arg.Any<CreatioRequestOptions>());
			_actualization.Start(Arg.Any<Guid>(), Arg.Any<CreatioRequestOptions>());
		});
	}

	[Test]
	[Description("A check for running updates that fails is reported and never stops the call.")]
	public void Execute_ShouldStart_WhenTheRunningCheckFails() {
		// Arrange
		ObjectIs(true);
		_actualization.FindRunning(Arg.Any<CreatioRequestOptions>())
			.Returns(_ => throw new InvalidOperationException("SelectQuery failed: denied"));
		_actualization.ReadStatus(ProcessId, Arg.Any<CreatioRequestOptions>())
			.Returns(new ProcessRunStatus(ProcessRunStatus.Completed, "Completed"));

		// Act
		int exitCode = _command.Execute(Options());

		// Assert
		exitCode.Should().Be(0, because: "the check is advisory");
		_warnings.Should().Contain(w => w.Contains("could not check whether a record-rights update is already running"),
			because: "the failed check is reported");
		_actualization.Received(1).Start(Arg.Any<Guid>(), Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("With --confirm (as on MCP) nobody reads the summary, so the records are not counted: the count would only take time from the wait.")]
	public void Execute_ShouldNotCount_WhenConfirmed() {
		// Arrange
		ObjectIs(true);
		_actualization.ReadStatus(ProcessId, Arg.Any<CreatioRequestOptions>())
			.Returns(new ProcessRunStatus(ProcessRunStatus.Completed, "Completed"));

		// Act
		int exitCode = _command.Execute(Options());

		// Assert
		exitCode.Should().Be(0, because: "the run completed");
		_counter.DidNotReceiveWithAnyArgs().CountRecords(default, default);
	}

	[Test]
	[Description("A launch answered with a body that could not be read (a proxy 502/504 page) may still have started the run: the failure says so and forbids starting it again.")]
	public void Execute_ShouldWarnRunMayBeGoing_WhenLaunchAnswerIsUnreadable() {
		// Arrange
		ObjectIs(true);
		_actualization.Start(Arg.Any<Guid>(), Arg.Any<CreatioRequestOptions>()).Returns(new RunProcessResponse {
			Status = RecordRightsActualizationClient.OutcomeUnknownStatus, Error = "RunProcess returned an empty response"
		});

		// Act
		int exitCode = _command.Execute(Options());

		// Assert
		exitCode.Should().Be(1, because: "the outcome is not known");
		_errors.Should().Contain(e => e.Contains("MAY already be going") && e.Contains("Do NOT start it again"),
			because: "a re-sent launch would start a second heavy run");
	}

	[Test]
	[Description("When the call's time limit is spent by the launch, no status is read at all, and the result says the wait ended before the status could be read — not that no log row was found.")]
	public void Execute_ShouldSayTheWaitEnded_WhenNoReadHappened() {
		// Arrange
		ObjectIs(true);
		_actualization.Start(Arg.Any<Guid>(), Arg.Any<CreatioRequestOptions>()).Returns(_ => {
			System.Threading.Thread.Sleep(300);
			return new RunProcessResponse { Status = "running", ProcessId = ProcessId.ToString() };
		});

		// Act
		int exitCode = _command.Execute(Options(o => {
			o.TimeOut = 50;
			o.CallBudget = TimeSpan.FromMilliseconds(200);
		}));

		// Assert
		exitCode.Should().Be(0, because: "the run was started");
		_actualization.DidNotReceiveWithAnyArgs().ReadStatus(default, default);
		_warnings.Should().Contain(w => w.Contains("the wait ended before the status could be read"),
			because: "the honest reason is given");
	}
}
