using System;
using System.Text.Json;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Relay;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Common.McpWorker;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// The private completion signal (ADR rule 5) and the choke point that guarantees it.
/// </summary>
/// <remarks>
/// NonParallelizable for two process-global reasons, both of which would produce a red with no bug
/// present: this fixture flips <see cref="McpWorkerEnvironment.IsWorkerProcess"/>, which every tool in the
/// process reads, and it takes / clears <c>McpToolExecutionLock</c> configuration-build reservations,
/// whose reset is an UNKEYED clear (the hazard the CompileCreatioToolTests header describes).
/// </remarks>
[NonParallelizable]
[TestFixture]
[Property("Module", "McpServer")]
public sealed class WorkerOperationCompletionSignalTests {

	// Long enough for a fire-and-forget notification to land on a loaded CI agent, short enough that a
	// genuine "never sent" does not stall the suite.
	private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(10);

	// How long a NEGATIVE assertion waits before concluding nothing was sent. A signal that is going to be
	// sent at all is sent from the same thread that ended the call, so this only has to outlast scheduling.
	private static readonly TimeSpan SilenceWindow = TimeSpan.FromMilliseconds(300);

	private static readonly IMcpToolExecutionMetadataReader MetadataReader =
		new McpToolExecutionMetadataReader(new McpToolCompatibilityCatalog());

	private bool _originalWorkerProcess;

	[SetUp]
	public void SetUp() {
		_originalWorkerProcess = McpWorkerEnvironment.IsWorkerProcess;
		McpWorkerEnvironment.IsWorkerProcess = true;
		ConsoleLogger.Instance.ClearMessages();
	}

	[TearDown]
	public void TearDown() {
		McpWorkerEnvironment.IsWorkerProcess = _originalWorkerProcess;
		// A deadline-branch test detaches its compile, whose reservation is released on a continuation that
		// can outlive the test method; clear the process-global reservations so a leaked one cannot fast-fail
		// the next test.
		McpToolExecutionLock.ResetConfigurationBuildReservationsForTests();
		ConsoleLogger.Instance.ClearMessages();
	}

	// ---------------------------------------------------------------------------------------------------
	// The choke point's own contract.
	// ---------------------------------------------------------------------------------------------------

	[Test]
	[Category("Unit")]
	[Description("A sticky operation-starting call that returns without ever starting an operation is over, so the choke point signals completion — this is the shape every early return in the four families has.")]
	public async Task RunToolCallAsync_ShouldSignalCompletion_WhenTheCallStartedNoOperation() {
		// Arrange
		RecordingWorkerSession session = new();
		McpToolExecutionMetadata metadata = MetadataFor(CompileCreatioTool.CompileCreatioToolName);

		// Act
		CommandExecutionResult result = await WorkerOperationCompletionSignal.RunToolCallAsync(
			session.Server, metadata,
			() => Task.FromResult(new CommandExecutionResult(1, [new ErrorMessage("refused")])));

		// Assert
		result.ExitCode.Should().Be(1,
			because: "the choke point must hand the caller's own result back untouched");
		session.WaitForSignals(1).Should().Be(1,
			because: "a call that never started an operation is over the moment it returns, and the parent "
				+ "must be told so it can reap the sticky worker instead of holding its admission slot and "
				+ "the target's configuration-build reservation for the whole hard lifetime");
	}

	[Test]
	[Category("Unit")]
	[Description("A sticky operation-starting call that throws is also over, so the choke point signals completion and rethrows.")]
	public async Task RunToolCallAsync_ShouldSignalCompletionAndRethrow_WhenTheCallThrows() {
		// Arrange
		RecordingWorkerSession session = new();
		McpToolExecutionMetadata metadata = MetadataFor(CompileCreatioTool.CompileCreatioToolName);
		InvalidOperationException failure = new("environment not found");

		// Act
		Func<Task> call = () => WorkerOperationCompletionSignal.RunToolCallAsync<CommandExecutionResult>(
			session.Server, metadata, () => throw failure);

		// Assert
		await call.Should().ThrowAsync<InvalidOperationException>().WithMessage("environment not found",
			because: "the choke point must not change how a call fails");
		session.WaitForSignals(1).Should().Be(1,
			because: "an exception ends the call as surely as a result does, and an unsignalled worker is "
				+ "stranded either way");
	}

	[Test]
	[Category("Unit")]
	[Description("A call that answered in-progress while its operation keeps running detached must NOT signal on return, and must signal exactly once when the operation really ends.")]
	public async Task RunToolCallAsync_ShouldDeferTheSignalUntilTheOperationEnds_WhenTheCallLeftItRunning() {
		// Arrange
		RecordingWorkerSession session = new();
		McpToolExecutionMetadata metadata = MetadataFor(CompileCreatioTool.CompileCreatioToolName);
		using ManualResetEventSlim operationGate = new(false);
		Task detachedOperation = null;

		// Act — the call leases an operation the way McpProgressHeartbeat does, then answers without it.
		await WorkerOperationCompletionSignal.RunToolCallAsync(session.Server, metadata, () => {
			WorkerOperationCompletionSignal.WorkerOperationLease lease =
				WorkerOperationCompletionSignal.BeginOperation();
			detachedOperation = Task.Run(() => lease.Run(() => {
				operationGate.Wait(SignalTimeout);
				return new CommandExecutionResult(0, []);
			}));
			return Task.FromResult(CommandExecutionResult.FromInfo("still running server-side"));
		});

		// Assert — nothing yet: the operation is the whole reason this worker is sticky.
		session.CountAfterSilence().Should().Be(0,
			because: "signalling here would reap the worker in the middle of the very operation it exists "
				+ "to keep alive, which is worse than the strand this choke point removes");

		operationGate.Set();
		await detachedOperation!;

		session.WaitForSignals(1).Should().Be(1,
			because: "the operation ending on the detached continuation is where the work really ends");
		session.CountAfterSilence().Should().Be(1,
			because: "the signal must be sent exactly once per operation, never once per ending");
	}

	[Test]
	[Category("Unit")]
	[Description("An operation that finishes inside the call still produces exactly one signal, sent when the call ends.")]
	public async Task RunToolCallAsync_ShouldSignalCompletionOnce_WhenTheOperationFinishedInsideTheCall() {
		// Arrange
		RecordingWorkerSession session = new();
		McpToolExecutionMetadata metadata = MetadataFor(CompileCreatioTool.CompileCreatioToolName);

		// Act
		await WorkerOperationCompletionSignal.RunToolCallAsync(session.Server, metadata, () => {
			WorkerOperationCompletionSignal.WorkerOperationLease lease =
				WorkerOperationCompletionSignal.BeginOperation();
			CommandExecutionResult operationResult = lease.Run(() => new CommandExecutionResult(7, []));
			return Task.FromResult(operationResult);
		});

		// Assert
		session.WaitForSignals(1).Should().Be(1,
			because: "the fast path — work finishing before the response deadline — must signal too");
		session.CountAfterSilence().Should().Be(1,
			because: "the operation ending and the call ending are two events but one completion");
		session.LastExitCode().Should().Be(7,
			because: "the exit code the parent logs must be the OPERATION's, derived from its result rather "
				+ "than hand-carried by each tool");
	}

	[Test]
	[Category("Unit")]
	[Description("A status poll observes an operation instead of starting one, so it must never signal completion — reaping the worker mid-compile would defeat the stickiness the poll depends on.")]
	public async Task RunToolCallAsync_ShouldNotSignalCompletion_WhenTheToolOnlyObservesTheOperation() {
		// Arrange
		RecordingWorkerSession session = new();
		McpToolExecutionMetadata pollMetadata = MetadataFor(CompileStatusTool.CompileStatusToolName);

		// Act
		await WorkerOperationCompletionSignal.RunToolCallAsync(
			session.Server, pollMetadata, () => Task.FromResult(new CommandExecutionResult(0, [])));

		// Assert
		pollMetadata.Lifetime.Should().Be(McpToolExecutionLifetime.Sticky,
			because: "a status poll is sticky precisely so it reaches the worker holding the operation — "
				+ "which is why a predicate keyed on Sticky alone would silently reap that worker");
		pollMetadata.StartsOperation.Should().BeFalse(
			because: "compile-status observes the compile, it does not start one");
		session.CountAfterSilence().Should().Be(0,
			because: "a poll that reported completion would reap the worker running the compile it was "
				+ "sent to observe");
	}

	[Test]
	[Category("Unit")]
	[Description("Outside a worker process there is no parent to tell and no admission slot to return, so the choke point opens no ledger and sends nothing.")]
	public async Task RunToolCallAsync_ShouldSendNothing_WhenTheProcessIsNotAWorker() {
		// Arrange
		RecordingWorkerSession session = new();
		McpToolExecutionMetadata metadata = MetadataFor(CompileCreatioTool.CompileCreatioToolName);
		McpWorkerEnvironment.IsWorkerProcess = false;

		// Act
		CommandExecutionResult result = await WorkerOperationCompletionSignal.RunToolCallAsync(
			session.Server, metadata, () => Task.FromResult(new CommandExecutionResult(0, [])));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "the ordinary in-process host must see the call behave exactly as it did before");
		session.CountAfterSilence().Should().Be(0,
			because: "the signal is private plumbing between a worker and its parent; an ordinary host has "
				+ "neither");
	}

	[Test]
	[Category("Unit")]
	[Description("Names the tools the completion signal is owed for, so a fifth long-running family cannot be added without this list moving.")]
	public void RequiresCompletionSignal_ShouldSelectExactlyTheOperationStarters_WhenAppliedToTheDeclaredCatalog() {
		// Arrange
		IReadOnlyDictionary<string, McpToolExecutionMetadata> declared = MetadataReader.DeclaredMetadataByToolName;

		// Act
		string[] sticky = [.. declared
			.Where(pair => pair.Value.Lifetime == McpToolExecutionLifetime.Sticky)
			.Select(pair => pair.Key)
			.OrderBy(name => name, StringComparer.Ordinal)];
		string[] owedASignal = [.. declared
			.Where(pair => WorkerOperationCompletionSignal.RequiresCompletionSignal(pair.Value))
			.Select(pair => pair.Key)
			.OrderBy(name => name, StringComparer.Ordinal)];

		// Assert
		sticky.Should().BeEquivalentTo(
			["compile-creatio", "compile-status", "create-app-section", "install-process-builder",
				"restart-by-credentials", "restart-by-environment-name", "restart-status"],
			because: "these are the tools whose worker outlives the response, and the sweep this fix rests "
				+ "on covered exactly them");
		owedASignal.Should().BeEquivalentTo(
			["compile-creatio", "create-app-section", "install-process-builder", "restart-by-credentials",
				"restart-by-environment-name"],
			because: "only a tool that STARTS an operation owns a worker to reap; the two status polls are "
				+ "sticky but must never signal");
	}

	// ---------------------------------------------------------------------------------------------------
	// The four families, driven through the production choke point with their own declared metadata.
	// Each case is a real early return that used to leave the sticky worker unsignalled.
	// ---------------------------------------------------------------------------------------------------

	[Test]
	[Category("Unit")]
	[Description("compile-creatio refuses a comma-separated package name before it reserves anything, and that refusal must still tell the parent the worker is free.")]
	public async Task CompileCreatio_ShouldSignalCompletion_WhenThePackageNameIsRejected() {
		// Arrange
		RecordingWorkerSession session = new();
		IToolCommandResolver commandResolver = ResolverFor("signal-tenant", "signal-target");
		CompileCreatioTool tool = new(ConsoleLogger.Instance, commandResolver, new CompileOperationRegistry());

		// Act
		CommandExecutionResult result = await RunThroughChokePoint(
			session, CompileCreatioTool.CompileCreatioToolName,
			() => tool.CompileCreatio(new CompileCreatioArgs("sandbox", "PkgA,PkgB"), session.Server));

		// Assert
		result.ExitCode.Should().Be(1,
			because: "a comma-separated package list is refused, which is the behaviour under test's premise");
		session.WaitForSignals(1).Should().Be(1,
			because: "the refusal returns before the operation registry and before the heartbeat, so nothing "
				+ "downstream will ever signal — the sticky worker would idle for its whole hard lifetime "
				+ "while every later compile for that target is refused");
		session.LastFamily().Should().Be(McpToolOperationFamily.ConfigurationBuild,
			because: "the parent cross-checks the family against the worker it registered");
	}

	[Test]
	[Category("Unit")]
	[Description("compile-creatio refuses when a configuration build is already reserved for the target, and that refusal must signal too.")]
	public async Task CompileCreatio_ShouldSignalCompletion_WhenTheConfigurationBuildIsAlreadyReserved() {
		// Arrange
		RecordingWorkerSession session = new();
		IToolCommandResolver commandResolver = ResolverFor("signal-tenant", "reserved-target");
		CompileCreatioTool tool = new(ConsoleLogger.Instance, commandResolver, new CompileOperationRegistry());
		McpToolExecutionLock.TryReserveConfigurationBuild("reserved-target", out McpToolExecutionLock.BuildReservation held)
			.Should().BeTrue(because: "the test must own the reservation before the tool asks for it");

		try {
			// Act
			CommandExecutionResult result = await RunThroughChokePoint(
				session, CompileCreatioTool.CompileCreatioToolName,
				() => tool.CompileCreatio(new CompileCreatioArgs("sandbox", null), session.Server));

			// Assert
			result.ExitCode.Should().Be(1,
				because: "a second concurrent compile for one target is refused, not queued");
			session.WaitForSignals(1).Should().Be(1,
				because: "this refusal is the likeliest early exit of all — it is what a duplicate compile "
					+ "gets — and it returns before the heartbeat that would otherwise report completion");
		} finally {
			McpToolExecutionLock.ReleaseConfigurationBuild("reserved-target", held);
		}
	}

	[Test]
	[Category("Unit")]
	[Description("restart-by-environment-name with waitReady=false runs the restart synchronously and returns without ever entering the readiness wait, which was the only place that signalled.")]
	public async Task RestartInstanceByName_ShouldSignalCompletion_WhenWaitReadyIsFalse() {
		// Arrange
		RecordingWorkerSession session = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<RestartCommand>(Arg.Any<RestartOptions>()).Returns(new FakeRestartCommand());
		RestartTool tool = new(new FakeRestartCommand(), ConsoleLogger.Instance, commandResolver,
			new RestartOperationRegistry());

		// Act
		CommandExecutionResult result = await RunThroughChokePoint(
			session, RestartTool.RestartByEnvironmentNameToolName,
			() => tool.RestartInstanceByName("sandbox", waitReady: false, server: session.Server));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "the request-only restart succeeded, which is what makes this the silent case");
		session.WaitForSignals(1).Should().Be(1,
			because: "waitReady=false returns straight out of ExecuteWithReadinessWait, so RunReadinessWait "
				+ "— the one place the signal used to be sent — never runs at all");
		session.LastFamily().Should().Be(McpToolOperationFamily.Restart,
			because: "the reaped worker belongs to the restart family");
	}

	[Test]
	[Category("Unit")]
	[Description("restart-by-environment-name gives up when the restart request itself fails, before the readiness wait that used to carry the signal.")]
	public async Task RestartInstanceByName_ShouldSignalCompletion_WhenTheRestartRequestFails() {
		// Arrange
		RecordingWorkerSession session = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<RestartCommand>(Arg.Any<RestartOptions>())
			.Returns(new FakeRestartCommand { ExitCodeToReturn = 1 });
		RestartTool tool = new(new FakeRestartCommand { ExitCodeToReturn = 1 }, ConsoleLogger.Instance,
			commandResolver, new RestartOperationRegistry());

		// Act
		CommandExecutionResult result = await RunThroughChokePoint(
			session, RestartTool.RestartByEnvironmentNameToolName,
			() => tool.RestartInstanceByName("sandbox", waitReady: true, server: session.Server));

		// Assert
		result.ExitCode.Should().Be(1,
			because: "a failed restart request is surfaced as-is; there is nothing to wait on");
		session.WaitForSignals(1).Should().Be(1,
			because: "phase 1 failing returns before registry.Begin and before the heartbeat, so the worker "
				+ "the parent already registered would never hear that the call was over");
	}

	[Test]
	[Category("Unit")]
	[Description("restart-by-environment-name refuses a blank environment name before anything else runs, and must still release its worker.")]
	public async Task RestartInstanceByName_ShouldSignalCompletion_WhenTheEnvironmentNameIsBlank() {
		// Arrange
		RecordingWorkerSession session = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		RestartTool tool = new(new FakeRestartCommand(), ConsoleLogger.Instance, commandResolver,
			new RestartOperationRegistry());

		// Act
		CommandExecutionResult result = await RunThroughChokePoint(
			session, RestartTool.RestartByEnvironmentNameToolName,
			() => tool.RestartInstanceByName(string.Empty, server: session.Server));

		// Assert
		result.ExitCode.Should().Be(1,
			because: "environment-name is required");
		session.WaitForSignals(1).Should().Be(1,
			because: "an argument refusal is still a call that is over, and the parent took the target's "
				+ "reservation before the tool method was ever entered");
	}

	[Test]
	[Category("Unit")]
	[Description("restart-by-credentials refuses incomplete credentials before the readiness wait — and it is the family member with no status tool, so the signal is the parent's only way to learn anything.")]
	public async Task RestartInstanceByCredentials_ShouldSignalCompletion_WhenCredentialsAreIncomplete() {
		// Arrange
		RecordingWorkerSession session = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		RestartTool tool = new(new FakeRestartCommand(), ConsoleLogger.Instance, commandResolver,
			new RestartOperationRegistry());

		// Act
		CommandExecutionResult result = await RunThroughChokePoint(
			session, RestartTool.RestartByCredentialsToolName,
			() => tool.RestartInstanceByCredentials(
				url: "https://example.creatio.com", userName: string.Empty, password: string.Empty,
				server: session.Server));

		// Assert
		result.ExitCode.Should().Be(1,
			because: "the credential validation refuses the call");
		session.WaitForSignals(1).Should().Be(1,
			because: "restart-by-credentials is deliberately unreportable through restart-status, so with no "
				+ "signal there is no terminal state a parent could ever observe for it");
	}

	[Test]
	[Category("Unit")]
	[Description("install-process-builder refuses when a configuration build is already running on the target, returning from inside the work delegate before the release-and-signal block.")]
	public async Task InstallProcessBuilder_ShouldSignalCompletion_WhenTheConfigurationBuildIsAlreadyReserved() {
		// Arrange
		RecordingWorkerSession session = new();
		IToolCommandResolver commandResolver = ResolverFor("install-tenant", "install-target");
		InstallProcessBuilderTool tool = new(ConsoleLogger.Instance, commandResolver);
		McpToolExecutionLock.TryReserveConfigurationBuild("install-target", out McpToolExecutionLock.BuildReservation held)
			.Should().BeTrue(because: "the test must own the reservation before the tool asks for it");

		try {
			// Act
			CommandExecutionResult result = await RunThroughChokePoint(
				session, InstallProcessBuilderTool.InstallProcessBuilderToolName,
				() => tool.InstallProcessBuilder(new InstallProcessBuilderArgs("sandbox"), session.Server));

			// Assert
			result.ExitCode.Should().Be(1,
				because: "a duplicate install is refused rather than queued");
			session.WaitForSignals(1).Should().Be(1,
				because: "install-process-builder has NO operation registry at all, so the private signal is "
					+ "the only thing that can ever reap its worker");
		} finally {
			McpToolExecutionLock.ReleaseConfigurationBuild("install-target", held);
		}
	}

	[Test]
	[Category("Unit")]
	[Description("create-app-section rejects its arguments before the heartbeat delegate that used to carry the signal, and must still release its worker.")]
	public async Task ApplicationSectionCreate_ShouldSignalCompletion_WhenArgumentsAreRejected() {
		// Arrange
		RecordingWorkerSession session = new();
		IToolCommandResolver commandResolver = ResolverFor("section-tenant", "section-target");
		ApplicationSectionCreateTool tool = new(ConsoleLogger.Instance, commandResolver,
			Substitute.For<IApplicationSectionCreateService>());

		// Act
		ApplicationSectionContextResponse response = await RunThroughChokePoint(
			session, ApplicationSectionCreateTool.ApplicationSectionCreateToolName,
			() => tool.ApplicationSectionCreate(
				new ApplicationSectionCreateArgs(ApplicationCode: string.Empty, Caption: "Orders",
					EnvironmentName: "sandbox"),
				session.Server, requestContext: null, cancellationToken: default));

		// Assert
		response.Success.Should().BeFalse(
			because: "application-code is required, which is the refusal this case is about");
		session.WaitForSignals(1).Should().Be(1,
			because: "the validation throws before RunWithProgressAndDeadlineAsync is ever reached, and "
				+ "create-app-section has no registry either — nothing else could tell the parent");
		session.LastFamily().Should().Be(McpToolOperationFamily.AppSectionCreate,
			because: "the reaped worker belongs to the app-section-create family");
	}

	[Test]
	[Category("Unit")]
	[Description("A compile that outruns the MCP response deadline answers in-progress WITHOUT signalling, then signals exactly once when the detached compile really ends.")]
	public async Task CompileCreatio_ShouldDeferTheSignalUntilTheCompileEnds_WhenTheResponseDeadlineIsReached() {
		// Arrange
		RecordingWorkerSession session = new();
		IToolCommandResolver commandResolver = ResolverFor("deadline-tenant", "deadline-target");
		using ManualResetEventSlim compileGate = new(false);
		commandResolver.Resolve<CompileConfigurationCommand>(Arg.Any<CompileConfigurationOptions>())
			.Returns(new FakeCompileConfigurationCommand { ExecuteGate = compileGate });
		ICompileOperationRegistry registry = new CompileOperationRegistry();
		CompileCreatioTool tool = new(ConsoleLogger.Instance, commandResolver, registry) {
			ResponseDeadlineOverride = TimeSpan.FromMilliseconds(50)
		};

		try {
			// Act
			CommandExecutionResult result = await RunThroughChokePoint(
				session, CompileCreatioTool.CompileCreatioToolName,
				() => tool.CompileCreatio(new CompileCreatioArgs("sandbox", null), session.Server));

			// Assert — the call is over, the compile is not.
			result.ExitCode.Should().Be(0,
				because: "an over-deadline compile answers with a non-error in-progress envelope");
			session.CountAfterSilence().Should().Be(0,
				because: "signalling here would reap the worker while the compile it holds is still running, "
					+ "and compile-status polls would then reach nothing");

			compileGate.Set();

			session.WaitForSignals(1).Should().Be(1,
				because: "the detached compile finishing is where the work really ends");
			session.CountAfterSilence().Should().Be(1,
				because: "one operation must produce exactly one completion signal");
		} finally {
			compileGate.Set();
		}
	}

	[Test]
	[Category("Unit")]
	[Description("The heartbeat leases the operation BEFORE it schedules it, so a deadline that fires while the work is still queued does not read the call as having started nothing.")]
	public async Task RunWithProgressAndDeadlineAsync_ShouldLeaseTheOperationBeforeSchedulingIt_WhenTheDeadlineFiresFirst() {
		// Arrange — a zero deadline makes Task.Delay already-completed, so the race is decided before the
		// thread pool has had any chance to run the work delegate. That is the window a lease taken INSIDE
		// the delegate would leave open, and it is reachable without blocking the pool (which would deadlock
		// the WhenAny continuation itself).
		RecordingWorkerSession session = new();
		McpToolExecutionMetadata metadata = MetadataFor(CompileCreatioTool.CompileCreatioToolName);
		using ManualResetEventSlim workGate = new(false);

		try {
			// Act
			CommandExecutionResult result = await WorkerOperationCompletionSignal.RunToolCallAsync(
				session.Server, metadata, async () => {
					try {
						return await McpProgressHeartbeat.RunWithProgressAndDeadlineAsync<CommandExecutionResult>(
							(McpProgressHeartbeat.ProgressChannel)null,
							CompileCreatioTool.CompileCreatioToolName,
							(Action<string> _) => {
								workGate.Wait(SignalTimeout);
								return new CommandExecutionResult(0, []);
							},
							deadline: TimeSpan.Zero).ConfigureAwait(false);
					} catch (McpResponseDeadlineExceededException) {
						return CommandExecutionResult.FromInfo("still running server-side");
					}
				});

			// Assert
			result.ExitCode.Should().Be(0,
				because: "the deadline branch answers with the in-progress envelope, which is the premise here");
			session.CountAfterSilence().Should().Be(0,
				because: "the operation was leased before it was scheduled, so a call that ended before the "
					+ "work delegate even started still counts as having one outstanding — leasing inside the "
					+ "delegate would leave that window open and reap the worker before its compile began");
		} finally {
			workGate.Set();
		}

		session.WaitForSignals(1).Should().Be(1,
			because: "the operation still has to signal once it really ends");
	}

	[Test]
	[Category("Unit")]
	[Description("A past-deadline operation that FAILS still signals exactly once, with a failure exit code, and its exception still reaches the background diagnostic that exists to observe it.")]
	public async Task RunToolCallAsync_ShouldSignalFailureOnce_WhenThePastDeadlineOperationThrows() {
		// Arrange
		RecordingWorkerSession session = new();
		McpToolExecutionMetadata metadata = MetadataFor(CompileCreatioTool.CompileCreatioToolName);
		using ManualResetEventSlim workGate = new(false);
		StringWriter capturedStandardError = new();
		TextWriter originalStandardError = Console.Error;
		Console.SetError(capturedStandardError);

		try {
			// Act
			await WorkerOperationCompletionSignal.RunToolCallAsync(session.Server, metadata, async () => {
				try {
					return await McpProgressHeartbeat.RunWithProgressAndDeadlineAsync<CommandExecutionResult>(
						(McpProgressHeartbeat.ProgressChannel)null,
						CompileCreatioTool.CompileCreatioToolName,
						(Action<string> _) => throw ThrowAfter(workGate),
						deadline: TimeSpan.Zero).ConfigureAwait(false);
				} catch (McpResponseDeadlineExceededException) {
					return CommandExecutionResult.FromInfo("still running server-side");
				}
			});
			workGate.Set();

			// Assert
			session.WaitForSignals(1).Should().Be(1,
				because: "a failed operation ends the worker's work as surely as a successful one; leaving it "
					+ "unsignalled would strand the worker on precisely the path nobody is watching");
			session.LastExitCode().Should().Be(1,
				because: "the lease reports a throw as a failure exit code without any tool having to carry it");
			session.CountAfterSilence().Should().Be(1,
				because: "the lease's finally must not turn one ending into two signals");
			WaitUntil(() => capturedStandardError.ToString().Contains("background operation faulted"))
				.Should().BeTrue(
					because: "the lease wraps the work, so its finally must not swallow or replace the fault "
						+ "the post-deadline diagnostic exists to surface");
		} finally {
			workGate.Set();
			Console.SetError(originalStandardError);
			capturedStandardError.Dispose();
		}
	}

	// ---------------------------------------------------------------------------------------------------
	// The wiring: the choke point is installed in the call-tool filter, so no tool can be reached around it.
	// ---------------------------------------------------------------------------------------------------

	[Test]
	[Category("Unit")]
	[Description("The MCP call-tool filter itself opens the completion scope, so a sticky starter that returns through the pipeline signals without any tool-side call.")]
	public async Task HandleCallToolErrors_ShouldSignalCompletion_WhenAStickyStarterReturnsThroughTheFilter() {
		// Arrange
		RecordingWorkerSession session = new();
		McpRequestHandler<CallToolRequestParams, CallToolResult> handler =
			McpToolErrorFilter.HandleCallToolErrors(
				(_, _) => new ValueTask<CallToolResult>(new CallToolResult { Content = [] }));
		RequestContext<CallToolRequestParams> context = McpRequestContextTestFactory.CreateCallToolContext(
			CompileCreatioTool.CompileCreatioToolName);
		context.Server = session.Server;
		context.Services = McpRequestContextTestFactory.CreateExecutionMetadataServices();

		// Act
		await handler(context, CancellationToken.None);

		// Assert
		session.WaitForSignals(1).Should().Be(1,
			because: "the guarantee is that a sticky starter cannot return WITHOUT the signal, and that only "
				+ "holds if the scope is opened by the pipeline rather than by each tool remembering to");
	}

	[Test]
	[Category("Unit")]
	[Description("The filter's pre-execution refusals — an argument-binding diagnostic answered before any tool runs — are calls too, and must release the worker the parent already registered.")]
	public async Task HandleCallToolErrors_ShouldSignalCompletion_WhenTheCallIsRefusedBeforeAnyToolRuns() {
		// Arrange
		RecordingWorkerSession session = new();
		bool innerHandlerRan = false;
		McpRequestHandler<CallToolRequestParams, CallToolResult> handler =
			McpToolErrorFilter.HandleCallToolErrors((_, _) => {
				innerHandlerRan = true;
				return new ValueTask<CallToolResult>(new CallToolResult { Content = [] });
			});
		RequestContext<CallToolRequestParams> context = McpRequestContextTestFactory.CreateCallToolContext(
			CompileCreatioTool.CompileCreatioToolName);
		context.Server = session.Server;
		// No IMcpExecutionRouter registered: the matched dispatch site is fail-closed, so it refuses inside
		// the filter — the cheapest reproduction of "answered before any tool ran".
		context.Services = McpRequestContextTestFactory.CreateExecutionMetadataServices();
		context.MatchedPrimitive = McpServerTool.Create(
			typeof(StubStickyTool).GetMethod(nameof(StubStickyTool.Execute))!, new StubStickyTool());

		// Act
		CallToolResult result = await handler(context, CancellationToken.None);

		// Assert
		innerHandlerRan.Should().BeFalse(
			because: "the routing refusal answers the call before execution, which is the premise here");
		result.Should().NotBeNull(because: "the refusal is still an MCP answer");
		session.WaitForSignals(1).Should().Be(1,
			because: "the parent registered the sticky worker and took the target's reservation before this "
				+ "call was relayed, so a refusal that answers before the tool runs strands it exactly as a "
				+ "validation refusal inside the tool would");
	}

	// ---------------------------------------------------------------------------------------------------
	// Helpers
	// ---------------------------------------------------------------------------------------------------

	// Blocks until the gate opens, then produces the failure the caller throws. Written as a helper because
	// a lambda whose body is `wait; throw;` cannot have its return type inferred.
	private static Exception ThrowAfter(ManualResetEventSlim gate) {
		gate.Wait(SignalTimeout);
		return new InvalidOperationException("the backend refused the build");
	}

	private static bool WaitUntil(Func<bool> condition) {
		Stopwatch stopwatch = Stopwatch.StartNew();
		while (!condition() && stopwatch.Elapsed < SignalTimeout) {
			Thread.Sleep(10);
		}
		return condition();
	}

	private static Task<TResult> RunThroughChokePoint<TResult>(
		RecordingWorkerSession session, string toolName, Func<Task<TResult>> invoke) =>
		WorkerOperationCompletionSignal.RunToolCallAsync(session.Server, MetadataFor(toolName), invoke);

	private static McpToolExecutionMetadata MetadataFor(string toolName) {
		MetadataReader.TryGetMetadata(toolName, innerCommand: null, out McpToolExecutionMetadata metadata)
			.Should().BeTrue(because: $"'{toolName}' must carry declared execution metadata");
		return metadata;
	}

	private static IToolCommandResolver ResolverFor(string tenantKey, string targetKey) {
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.GetTenantKey(Arg.Any<EnvironmentOptions>()).Returns(tenantKey);
		resolver.GetTargetKey(Arg.Any<EnvironmentOptions>()).Returns(targetKey);
		return resolver;
	}

	/// <summary>
	/// A substituted MCP server that records the private completion notifications a worker sends it, read
	/// back through the parent's own <see cref="WorkerOperationSignalContract"/> parser so the assertions
	/// are about the WIRE the parent reads, not about an internal call.
	/// </summary>
	private sealed class RecordingWorkerSession {

		private readonly List<(McpToolOperationFamily Family, int? ExitCode)> _signals = [];

		internal RecordingWorkerSession() {
			Server = Substitute.For<global::ModelContextProtocol.Server.McpServer>();
			Server.SendMessageAsync(Arg.Any<JsonRpcMessage>(), Arg.Any<CancellationToken>())
				.Returns(call => {
					if (call.Arg<JsonRpcMessage>() is JsonRpcNotification notification
						&& WorkerOperationSignalContract.TryRead(
							notification, out McpToolOperationFamily family, out int? exitCode)) {
						lock (_signals) {
							_signals.Add((family, exitCode));
						}
					}
					return Task.CompletedTask;
				});
		}

		internal global::ModelContextProtocol.Server.McpServer Server { get; }

		internal int Count {
			get {
				lock (_signals) {
					return _signals.Count;
				}
			}
		}

		/// <summary>Waits up to <see cref="SignalTimeout"/> for <paramref name="expected"/> signals.</summary>
		internal int WaitForSignals(int expected) {
			Stopwatch stopwatch = Stopwatch.StartNew();
			while (Count < expected && stopwatch.Elapsed < SignalTimeout) {
				Thread.Sleep(10);
			}
			return Count;
		}

		/// <summary>Waits out <see cref="SilenceWindow"/> and reports how many signals exist by then.</summary>
		internal int CountAfterSilence() {
			Thread.Sleep(SilenceWindow);
			return Count;
		}

		internal McpToolOperationFamily LastFamily() {
			lock (_signals) {
				return _signals[^1].Family;
			}
		}

		internal int? LastExitCode() {
			lock (_signals) {
				return _signals[^1].ExitCode;
			}
		}
	}

	private sealed class FakeRestartCommand : RestartCommand {

		internal int ExitCodeToReturn { get; init; }

		internal FakeRestartCommand()
			: base(Substitute.For<IApplicationClient>(), new EnvironmentSettings(),
				Substitute.For<IServerReadinessWaiter>()) {
		}

		public override int Execute(RestartOptions options) => ExitCodeToReturn;

		public override bool WaitForReadiness(RestartOptions options) => true;
	}

	private sealed class FakeCompileConfigurationCommand : CompileConfigurationCommand {

		internal ManualResetEventSlim ExecuteGate { get; init; }

		internal FakeCompileConfigurationCommand()
			: base(Substitute.For<IApplicationClient>(), new EnvironmentSettings(),
				Substitute.For<IServiceUrlBuilder>(), Substitute.For<ICompilationHistoryPoller>(),
				Substitute.For<ILogger>(), Substitute.For<IInteractiveConsole>()) {
		}

		public override int Execute(CompileConfigurationOptions options) {
			ExecuteGate?.Wait(SignalTimeout);
			return 0;
		}
	}

	/// <summary>
	/// A stand-in matched primitive: the filter only needs a tool whose protocol name is the sticky
	/// starter's, so the routing refusal it triggers is attributed to that name.
	/// </summary>
	public sealed class StubStickyTool {

		[McpServerTool(Name = CompileCreatioTool.CompileCreatioToolName, ReadOnly = false, Destructive = true)]
		public string Execute() => "unused";
	}

	[Test]
	[Description("A sticky starter invoked through clio-run — which is how every non-resident sticky tool is actually reached — must open the completion ledger from the INNER command, or the choke point covers every path except the one real callers use.")]
	public void ResolveExecutionMetadata_ShouldSeeTheStickyInnerCommand_WhenTheCallArrivesThroughClioRun() {
		// Arrange — the executor's wrapped shape: the target sits under "args", two levels down.
		IMcpToolExecutionMetadataReader reader =
			new McpToolExecutionMetadataReader(new McpToolCompatibilityCatalog());

		// Act — the dialled name is the executor's; the inner command is what decides stickiness.
		bool wrapped = reader.TryGetMetadata("clio-run", innerCommand: "compile-creatio",
			out McpToolExecutionMetadata metadataForInner);
		bool executorOnly = reader.TryGetMetadata("clio-run", innerCommand: null,
			out McpToolExecutionMetadata metadataForExecutor);

		// Assert
		wrapped.Should().BeTrue(because: "the reader must resolve the inner command when one is supplied");
		metadataForInner.Lifetime.Should().Be(McpToolExecutionLifetime.Sticky,
			because: "compile-creatio is the sticky starter, and it is its metadata — not the executor's — that decides whether a completion ledger opens");
		metadataForInner.StartsOperation.Should().BeTrue(
			because: "the ledger predicate is Sticky AND StartsOperation, so both halves have to survive the unwrap");
		if (executorOnly) {
			metadataForExecutor.Lifetime.Should().NotBe(McpToolExecutionLifetime.Sticky,
				because: "the executor's own metadata is not sticky — which is exactly why reading only the dialled name left every wrapped starter without a ledger");
		}
	}

	[Test]
	[Category("Unit")]
	[Description("The live path: every sticky tool is NON-RESIDENT, so the worker is dialled as clio-run and the inner command is what makes the call sticky. The filter must unwrap it, or the choke point covers every path except the one real callers use.")]
	public async Task HandleCallToolErrors_ShouldSignalCompletion_WhenAStickyStarterArrivesWrappedInClioRun() {
		// Arrange — the executor's wrapped shape, with the target two object levels down.
		RecordingWorkerSession session = new();
		McpRequestHandler<CallToolRequestParams, CallToolResult> handler =
			McpToolErrorFilter.HandleCallToolErrors(
				(_, _) => new ValueTask<CallToolResult>(new CallToolResult { Content = [] }));
		Dictionary<string, System.Text.Json.JsonElement> wrapped = new() {
			["args"] = JsonSerializer.SerializeToElement(new {
				command = CompileCreatioTool.CompileCreatioToolName,
				args = new { environmentName = "sandbox" }
			})
		};
		RequestContext<CallToolRequestParams> context =
			McpRequestContextTestFactory.CreateCallToolContext(ClioRunTool.ToolName, wrapped);
		context.Server = session.Server;
		context.Services = McpRequestContextTestFactory.CreateExecutionMetadataServices();

		// Act
		await handler(context, CancellationToken.None);

		// Assert
		session.WaitForSignals(1).Should().Be(1,
			because: "reading only the dialled name yields clio-run's own non-sticky metadata, so no ledger opens and the worker keeps its admission slot and the target's configuration-build reservation until the thirty-minute bound — on exactly the path every real caller takes");
	}

	[Test]
	[Category("Unit")]
	[Description("An ordinary tool that happens to carry a `command` argument must NOT be re-read as an executor wrapper: the unwrap is keyed on the executor names, not on the presence of a property.")]
	public async Task HandleCallToolErrors_ShouldNotUnwrap_WhenAnOrdinaryToolCarriesACommandArgument() {
		// Arrange — a non-sticky tool whose arguments innocently include "command".
		RecordingWorkerSession session = new();
		McpRequestHandler<CallToolRequestParams, CallToolResult> handler =
			McpToolErrorFilter.HandleCallToolErrors(
				(_, _) => new ValueTask<CallToolResult>(new CallToolResult { Content = [] }));
		Dictionary<string, System.Text.Json.JsonElement> arguments = new() {
			["command"] = JsonSerializer.SerializeToElement(CompileCreatioTool.CompileCreatioToolName)
		};
		RequestContext<CallToolRequestParams> context =
			McpRequestContextTestFactory.CreateCallToolContext("get-page", arguments);
		context.Server = session.Server;
		context.Services = McpRequestContextTestFactory.CreateExecutionMetadataServices();

		// Act
		await handler(context, CancellationToken.None);

		// Assert
		session.Count.Should().Be(0,
			because: "get-page is not a sticky starter, and treating any tool with a `command` argument as a clio-run wrapper would make an ordinary read report completion for an operation that never existed");
	}

	[Test]
	[Category("Unit")]
	[Description("A payload carrying a command at BOTH levels must resolve the same one the executor dispatches — the top-level. Two readers of one payload disagreeing is how a sticky compile ends up with no completion ledger.")]
	public async Task HandleCallToolErrors_ShouldPreferTheTopLevelCommand_WhenThePayloadCarriesBoth() {
		// Arrange — a mixed shape: a nested non-sticky command and a top-level sticky one. ClioRunExecutor
		// reads the top level, so the ledger must key off that.
		RecordingWorkerSession session = new();
		McpRequestHandler<CallToolRequestParams, CallToolResult> handler =
			McpToolErrorFilter.HandleCallToolErrors(
				(_, _) => new ValueTask<CallToolResult>(new CallToolResult { Content = [] }));
		Dictionary<string, System.Text.Json.JsonElement> mixed = new() {
			["args"] = JsonSerializer.SerializeToElement(new { command = "get-page" }),
			["command"] = JsonSerializer.SerializeToElement(CompileCreatioTool.CompileCreatioToolName)
		};
		RequestContext<CallToolRequestParams> context =
			McpRequestContextTestFactory.CreateCallToolContext(ClioRunTool.ToolName, mixed);
		context.Server = session.Server;
		context.Services = McpRequestContextTestFactory.CreateExecutionMetadataServices();

		// Act
		await handler(context, CancellationToken.None);

		// Assert
		session.WaitForSignals(1).Should().Be(1,
			because: "the executor dispatches the top-level compile-creatio, so scanning nested objects first would load get-page's non-sticky metadata and leave the sticky compile holding its worker, its admission slot and the target's configuration-build reservation until the thirty-minute bound");
	}
}
