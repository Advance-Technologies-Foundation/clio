using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Common.McpWorker;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// ENG-95262 Stage 2: unit coverage for the MCP worker process supervisor — the concurrency cap
/// (AC-01), the identity-checked stale reap (AC-02), the spawn-anchored budget clock (AC-07), the
/// core-count-derived cap (AC-06), and the Unix process-group barrier that keeps a budget kill from
/// addressing the parent's own group.
/// </summary>
/// <remarks>
/// The containment seam is substituted in most tests on purpose: what is under test there is the
/// supervisor's bookkeeping, and spawning one real operating-system process per assertion would make
/// the suite slow without making it stricter. The two properties that CANNOT be faked — process-group
/// leadership and what a real kill reaches — are exercised against the real process fixture instead.
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class WorkerProcessSupervisorTests {

	private static readonly TimeSpan QueueObservationWindow = TimeSpan.FromMilliseconds(400);

	private ILogger _logger;
	private IProcessExecutor _processExecutor;
	private IClioExecutablePathProvider _pathProvider;
	private string _registryRoot;

	[SetUp]
	public void SetUp() {
		_logger = Substitute.For<ILogger>();
		_processExecutor = Substitute.For<IProcessExecutor>();
		_pathProvider = Substitute.For<IClioExecutablePathProvider>();
		// The test host's own executable: an ABSOLUTE path that exists and is executable on every
		// platform. A bare name such as "clio" would be resolved from PATH by the supervisor and would
		// pass only on a developer machine that happens to have clio installed — green here, red on any
		// CI agent, for a reason that has nothing to do with what these tests assert. FakeContainment
		// never runs it.
		_pathProvider.Resolve(Arg.Any<string[]>())
			.Returns(new ClioWorkerLaunchDescriptor(Environment.ProcessPath, Array.Empty<string>(),
				Path.GetTempPath()));
		_registryRoot = Path.Combine(Path.GetTempPath(), $"clio-worker-registry-{Guid.NewGuid():N}");
	}

	[TearDown]
	public void TearDown() {
		_logger.ClearReceivedCalls();
		_processExecutor.ClearReceivedCalls();
		_pathProvider.ClearReceivedCalls();
		if (Directory.Exists(_registryRoot)) {
			Directory.Delete(_registryRoot, recursive: true);
		}
	}

	[Test]
	[Description("TC-U-201: the concurrency cap admits exactly N workers, queues the N+1st instead of dropping or failing it, and admits it as soon as a slot is released.")]
	public async Task SpawnContainedAsync_ShouldQueueWithoutDropping_WhenTheCapIsReached() {
		// Arrange
		FakeContainment containment = new();
		IStaleWorkerRegistry registry = Substitute.For<IStaleWorkerRegistry>();
		WorkerProcessSupervisor sut = new(_logger, _processExecutor, containment, _pathProvider, registry,
			concurrencyCap: 2);
		WorkerSpawnRequest request = new() { Budget = TimeSpan.FromMinutes(5) };

		// Act
		IWorkerLease first = await sut.SpawnContainedAsync(request, CancellationToken.None);
		IWorkerLease second = await sut.SpawnContainedAsync(request, CancellationToken.None);
		Task<IWorkerLease> queued = sut.SpawnContainedAsync(request, CancellationToken.None);
		Task completedBeforeRelease = await Task.WhenAny(queued, Task.Delay(QueueObservationWindow));
		WorkerSupervisorSnapshot atCap = sut.GetSnapshot();
		first.Dispose();
		IWorkerLease third = await queued;

		// Assert
		completedBeforeRelease.Should().NotBeSameAs(queued,
			because: "a third spawn must WAIT while the cap of two is fully used, not run beside them");
		atCap.ActiveWorkers.Should().Be(2,
			because: "exactly the cap may run at once, which is what bounds the machine's CPU");
		atCap.QueuedRequests.Should().Be(1,
			because: "the third caller must be visible as queued rather than silently discarded");
		containment.LaunchCount.Should().Be(3,
			because: "every admitted call must eventually get its own worker: queueing delays a call, it never drops one (AC-01)");
		third.Should().NotBeNull(
			because: "the queued caller must receive a real lease once a slot is free");
		sut.GetSnapshot().PeakActiveWorkers.Should().Be(2,
			because: "resource accounting must report the highest concurrency actually reached");

		second.Dispose();
		third.Dispose();
	}

	[Test]
	[Description("TC-U-201b: a call queued behind the cap is cancelled only by its own caller, never by a queue timeout, and cancelling it releases nothing that another caller holds.")]
	public async Task SpawnContainedAsync_ShouldHonorOnlyCallerCancellation_WhileQueued() {
		// Arrange
		FakeContainment containment = new();
		IStaleWorkerRegistry registry = Substitute.For<IStaleWorkerRegistry>();
		WorkerProcessSupervisor sut = new(_logger, _processExecutor, containment, _pathProvider, registry,
			concurrencyCap: 1);
		WorkerSpawnRequest request = new() { Budget = TimeSpan.FromMinutes(5) };
		using CancellationTokenSource callerCancellation = new();

		// Act
		IWorkerLease held = await sut.SpawnContainedAsync(request, CancellationToken.None);
		Task<IWorkerLease> queued = sut.SpawnContainedAsync(request, callerCancellation.Token);
		await Task.Delay(QueueObservationWindow);
		bool completedOnItsOwn = queued.IsCompleted;
		callerCancellation.Cancel();
		Func<Task> awaitCancelled = async () => await queued;

		// Assert
		completedOnItsOwn.Should().BeFalse(
			because: "no queue timeout may exist: a busy supervisor makes callers wait, it does not fail them");
		await awaitCancelled.Should().ThrowAsync<OperationCanceledException>(
			because: "the caller's own token is the only thing that ends the wait for a slot");
		containment.LaunchCount.Should().Be(1,
			because: "a cancelled caller must not have consumed a worker");
		held.Dispose();
		sut.GetSnapshot().ActiveWorkers.Should().Be(0,
			because: "releasing the only lease must return the slot even after a queued caller gave up");
	}

	[Test]
	[Description("TC-U-202: a recorded worker whose process id has been reused by a stranger is never killed, and its entry is dropped so the reused identifier does not stay in the registry for ever.")]
	public void ReapStaleWorkers_ShouldNotKillAStranger_WhenTheRecordedProcessIdWasReused() {
		// Arrange
		IStaleWorkerRegistry registry = CreateRealRegistry();
		WorkerRegistrationEntry entry = new(ProcessId: 4242, StartTimeUtcTicks: 1_000,
			ExecutablePath: "/opt/clio/clio", OwnerProcessId: 4241, OwnerStartTimeUtcTicks: 900,
			OwnerExecutablePath: "/opt/clio/clio", RecordedAtUtc: DateTimeOffset.UtcNow);
		registry.Record(entry);
		IWorkerProcessInspector inspector = Substitute.For<IWorkerProcessInspector>();
		inspector.TryCaptureIdentity(4241).Returns((ProcessIdentitySnapshot)null);
		// Same process id, different process: a text editor that started later and lives elsewhere.
		inspector.TryCaptureIdentity(4242)
			.Returns(new ProcessIdentitySnapshot(4242, 9_999, "/usr/local/bin/nvim"));

		// Act
		StaleWorkerReapReport report = registry.Reap(inspector);

		// Assert
		inspector.DidNotReceive().TerminateStaleWorker(Arg.Any<WorkerRegistrationEntry>());
		report.StrangersSkipped.Should().Be(1,
			because: "process ids are reused, so a mismatched identity must be reported as a stranger and left alone");
		report.Terminated.Should().Be(0,
			because: "killing a process that merely reuses the recorded number is a defect in its own right");
		registry.Read().Should().BeEmpty(
			because: "an entry whose identifier now belongs to somebody else is dead weight and must be dropped, or it is re-examined for ever");
	}

	[Test]
	[Description("TC-U-202b: a recorded worker whose full identity triple still matches is killed and removed, proving the identity gate rejects strangers rather than rejecting everything.")]
	public void ReapStaleWorkers_ShouldKillAndForget_WhenTheRecordedIdentityStillMatches() {
		// Arrange
		IStaleWorkerRegistry registry = CreateRealRegistry();
		WorkerRegistrationEntry entry = new(ProcessId: 4242, StartTimeUtcTicks: 1_000,
			ExecutablePath: "/opt/clio/clio", OwnerProcessId: 4241, OwnerStartTimeUtcTicks: 900,
			OwnerExecutablePath: "/opt/clio/clio", RecordedAtUtc: DateTimeOffset.UtcNow);
		registry.Record(entry);
		IWorkerProcessInspector inspector = Substitute.For<IWorkerProcessInspector>();
		inspector.TryCaptureIdentity(4241).Returns((ProcessIdentitySnapshot)null);
		inspector.TryCaptureIdentity(4242)
			.Returns(new ProcessIdentitySnapshot(4242, 1_000, "/opt/clio/clio"));
		inspector.TerminateStaleWorker(Arg.Any<WorkerRegistrationEntry>())
			.Returns(WorkerTerminationOutcome.ContainedGroupKilled);

		// Act
		StaleWorkerReapReport report = registry.Reap(inspector);

		// Assert
		inspector.Received(1).TerminateStaleWorker(entry);
		report.Terminated.Should().Be(1,
			because: "a worker left behind by a dead parent whose identity still matches is exactly what the reap exists for");
		registry.Read().Should().BeEmpty(
			because: "a terminated worker must not be revisited by the next parent to start");
	}

	[Test]
	[Description("TC-U-206: workers recorded by another clio parent that is still running are left untouched, because reaping a healthy neighbour's live workers would recreate the very failure this feature removes.")]
	public void ReapStaleWorkers_ShouldLeaveAliveOwnersAlone_WhenAnotherParentIsStillRunning() {
		// Arrange
		IStaleWorkerRegistry registry = CreateRealRegistry();
		WorkerRegistrationEntry entry = new(ProcessId: 4242, StartTimeUtcTicks: 1_000,
			ExecutablePath: "/opt/clio/clio", OwnerProcessId: 4241, OwnerStartTimeUtcTicks: 900,
			OwnerExecutablePath: "/opt/clio/clio", RecordedAtUtc: DateTimeOffset.UtcNow);
		registry.Record(entry);
		IWorkerProcessInspector inspector = Substitute.For<IWorkerProcessInspector>();
		inspector.TryCaptureIdentity(4241)
			.Returns(new ProcessIdentitySnapshot(4241, 900, "/opt/clio/clio"));

		// Act
		StaleWorkerReapReport report = registry.Reap(inspector);

		// Assert
		inspector.DidNotReceive().TerminateStaleWorker(Arg.Any<WorkerRegistrationEntry>());
		inspector.DidNotReceive().TryCaptureIdentity(4242);
		report.LiveOwnersSkipped.Should().Be(1,
			because: "a live owner's workers are that parent's business and must not be inspected, let alone killed");
		registry.Read().Should().ContainSingle(
			because: "the entry still describes a live worker and must survive another parent's startup");
	}

	[Test]
	[Description("TC-U-204: the budget clock starts when the worker is spawned, so a call that waited behind the concurrency cap still receives its full budget (AC-07).")]
	public async Task SpawnContainedAsync_ShouldAnchorTheBudgetOnSpawn_NotOnAdmission() {
		// Arrange
		FakeContainment containment = new();
		IStaleWorkerRegistry registry = Substitute.For<IStaleWorkerRegistry>();
		WorkerProcessSupervisor sut = new(_logger, _processExecutor, containment, _pathProvider, registry,
			concurrencyCap: 1);
		TimeSpan budget = TimeSpan.FromSeconds(3);
		WorkerSpawnRequest request = new() { Budget = budget };
		IWorkerLease blocking = await sut.SpawnContainedAsync(request, CancellationToken.None);
		DateTimeOffset enqueuedAtUtc = DateTimeOffset.UtcNow;

		// Act
		Task<IWorkerLease> queued = sut.SpawnContainedAsync(request, CancellationToken.None);
		// Deliberately longer than a third of the budget: with an admission-anchored clock the queued
		// call would arrive at its worker having already burnt this much of it.
		await Task.Delay(TimeSpan.FromSeconds(1.2));
		blocking.Dispose();
		IWorkerLease afterQueueing = await queued;

		// Assert
		afterQueueing.SpawnedAtUtc.Should().BeAfter(enqueuedAtUtc.AddSeconds(1),
			because: "the worker genuinely was created only after the slot was released, which is what makes the distinction observable");
		(afterQueueing.BudgetExpiresAtUtc - afterQueueing.SpawnedAtUtc).Should().Be(budget,
			because: "the deadline is spawn plus budget: a call must never be killed for having been queued (ADR section 2.4 measured a healthy call waiting 16.9 s for a slot)");
		afterQueueing.BudgetExpiresAtUtc.Should().BeAfter(enqueuedAtUtc + budget,
			because: "an admission-anchored deadline would already have passed here, which is the failure mode this anchoring removes");
		afterQueueing.Dispose();
	}

	[Test]
	[Description("TC-U-208: the default concurrency cap is derived from the machine's processor count rather than a constant (AC-06).")]
	public void ConcurrencyCap_ShouldFollowProcessorCount_WhenNoExplicitCapIsGiven() {
		// Arrange
		FakeContainment containment = new();
		IStaleWorkerRegistry registry = Substitute.For<IStaleWorkerRegistry>();

		// Act
		WorkerProcessSupervisor sut = new(_logger, _processExecutor, containment, _pathProvider, registry);

		// Assert
		sut.ConcurrencyCap.Should().Be(Math.Max(1, Environment.ProcessorCount),
			because: "wall time grows linearly past the core count, so a constant cap would either waste cores or inflate per-call latency (ADR section 2.4)");
	}

	[Test]
	[Description("TC-U-209: the frozen worker environment keeps every architecture-specific DOTNET_ROOT spelling, because dropping the one the host actually set makes an apphost worker fail to start before it runs any clio code.")]
	public void DefaultInheritedEnvironmentVariableAllowlist_ShouldKeepRuntimeLocatingVariables() {
		// Arrange
		string[] required = [
			"PATH", "DOTNET_ROOT", "DOTNET_ROOT_ARM64", "DOTNET_ROOT_X64", "DOTNET_ROOT_X86",
			"DOTNET_ROOT(x86)", "DOTNET_HOST_PATH"
		];

		// Act
		IReadOnlyCollection<string> allowlist =
			WorkerProcessSupervisor.DefaultInheritedEnvironmentVariableAllowlist;

		// Assert
		allowlist.Should().Contain(required,
			because: "the variable that tells an apphost where the shared runtime lives is architecture-specific — measured on arm64 macOS only DOTNET_ROOT_ARM64 was set — and a frozen environment missing it makes every worker die at startup with \"You must install or update .NET\"");
	}

	[Test]
	[Description("TC-U-203: on Unix a group kill is issued only for a worker that has promoted itself to process-group leader; an unpromoted worker falls back to a tree kill, because its group is the parent clio's own.")]
	public void UnixContainment_ShouldRefuseAGroupKill_WhenTheWorkerIsNotItsOwnGroupLeader() {
		// Arrange
		if (OperatingSystem.IsWindows()) {
			Assert.Ignore("Process-group containment is a Unix mechanism; the Windows path is the job object.");
		}
		UnixProcessGroupContainment sut = new();
		using Process unpromoted = StartFixture("--hold-inherited-pipes");
		using FixtureProcessHandle handle = new(unpromoted);
		int ownGroupBeforeKill = UnixNativeMethods.getpgid(0);

		// Act
		WorkerTerminationOutcome outcome = sut.TerminateOrphan(handle);
		bool exited = unpromoted.WaitForExit(5_000);

		// Assert
		outcome.Should().Be(WorkerTerminationOutcome.FallbackTreeKilled,
			because: "an unpromoted child shares the LAUNCHING process's group, so a group kill would address the parent clio, the agent host and the user's shell");
		handle.TreeKillCount.Should().Be(1,
			because: "the fallback must still terminate the worker: refusing the group kill is not refusing to kill");
		exited.Should().BeTrue(
			because: "the worker must actually be gone after the fallback, not merely signalled");
		UnixNativeMethods.getpgid(0).Should().Be(ownGroupBeforeKill,
			because: "this test process is still alive and in its own group, which is the observable proof that no signal was sent to the shared group");
	}

	[Test]
	[Description("TC-U-205: the Windows command line is built with C-runtime quoting, so an argument containing spaces, quotes or trailing backslashes survives the round trip through CreateProcessW.")]
	public void WindowsCommandLine_ShouldQuoteArguments_SoTheChildSplitsThemBack() {
		// Arrange
		string executable = @"C:\Program Files\clio\clio.exe";
		IReadOnlyList<string> arguments = ["mcp-server", @"C:\repo dir\", "say \"hi\"", "plain"];

		// Act
		string commandLine = WindowsCommandLine.Build(executable, arguments);

		// Assert
		commandLine.Should().Be(
			"\"C:\\Program Files\\clio\\clio.exe\" mcp-server \"C:\\repo dir\\\\\" \"say \\\"hi\\\"\" plain",
			because: "joining arguments with spaces corrupts every ordinary Windows path and every quoted value, which the child would then read as different arguments");
	}

	[Test]
	[Description("TC-U-207: the worker launch descriptor never asks the dotnet muxer to run a command verb, which is what a naive Environment.ProcessPath spawn would do when clio runs as `dotnet clio.dll`.")]
	public void ClioExecutablePathProvider_ShouldPassTheAssemblyToTheMuxer_WhenClioRunsThroughIt() {
		// Arrange
		IFileSystem fileSystem = new System.IO.Abstractions.FileSystem();
		ClioExecutablePathProvider sut = new(fileSystem);

		// Act
		ClioWorkerLaunchDescriptor descriptor = sut.Resolve("mcp-server");

		// Assert
		descriptor.Executable.Should().NotBeNullOrWhiteSpace(
			because: "a worker cannot be spawned without something to run");
		descriptor.Arguments.Should().Contain("mcp-server",
			because: "the command verb must reach the child whichever host shape clio is running in");
		string executableName = Path.GetFileNameWithoutExtension(descriptor.Executable);
		if (string.Equals(executableName, "dotnet", StringComparison.OrdinalIgnoreCase)) {
			descriptor.Arguments[0].Should().EndWith(".dll",
				because: "the muxer takes an assembly as its first argument; handing it a command verb is exactly the failure this provider exists to prevent");
		} else {
			descriptor.Arguments[0].Should().Be("mcp-server",
				because: "an apphost IS clio, so the command verb stands alone as its first argument");
		}
	}

	private IStaleWorkerRegistry CreateRealRegistry() {
		// The real registry over a real temporary directory: what is under test is the read-modify-write
		// and the identity gate around it, and an in-memory file system would not exercise either.
		IFileSystem fileSystem = new System.IO.Abstractions.FileSystem();
		return new StaleWorkerRegistry(fileSystem, new InterprocessFileGate(fileSystem), _registryRoot);
	}

	private static Process StartFixture(params string[] arguments) {
		ProcessStartInfo startInfo = new() {
			FileName = ResolveFixtureExecutable(),
			UseShellExecute = false,
			CreateNoWindow = true
		};
		foreach (string argument in arguments) {
			startInfo.ArgumentList.Add(argument);
		}
		return Process.Start(startInfo)
			?? throw new InvalidOperationException("The process fixture did not start.");
	}

	// clio.tests has no project reference to clio.process.fixture, so the fixture is located by the same
	// build-output convention ProcessExecutorIntegrationTests uses, and a missing build fails loudly
	// instead of silently skipping the assertion.
	private static string ResolveFixtureExecutable() {
		DirectoryInfo testDirectory = new(TestContext.CurrentContext.TestDirectory);
		string targetFramework = testDirectory.Name;
		string configuration = testDirectory.Parent?.Name
			?? throw new InvalidOperationException("The test configuration directory could not be resolved.");
		string repositoryRoot = Path.GetFullPath(Path.Combine(testDirectory.FullName, "..", "..", "..", ".."));
		string executableName = OperatingSystem.IsWindows() ? "git.exe" : "git";
		string fixtureExecutable = Path.Combine(repositoryRoot, "clio.process.fixture", "bin", configuration,
			targetFramework, executableName);
		return File.Exists(fixtureExecutable)
			? fixtureExecutable
			: throw new FileNotFoundException("The process integration fixture was not built.", fixtureExecutable);
	}

	/// <summary>A real process handed to containment, recording whether the tree-kill fallback was used.</summary>
	private sealed class FixtureProcessHandle : IWorkerProcessHandle {

		private readonly Process _process;

		public FixtureProcessHandle(Process process) {
			_process = process;
			ProcessId = process.Id;
			StartTimeUtc = process.StartTime.ToUniversalTime();
			ExecutablePath = process.MainModule?.FileName ?? string.Empty;
		}

		public int TreeKillCount { get; private set; }

		public int ProcessId { get; }

		public DateTime StartTimeUtc { get; }

		public string ExecutablePath { get; }

		public Stream StandardInput => Stream.Null;

		public Stream StandardOutput => Stream.Null;

		public Stream StandardError => Stream.Null;

		public bool HasExited => _process.HasExited;

		public int? ExitCode => _process.HasExited ? _process.ExitCode : null;

		public Task WaitForExitAsync(CancellationToken cancellationToken) =>
			_process.WaitForExitAsync(cancellationToken);

		public void KillProcessTree() {
			TreeKillCount++;
			_process.Kill(entireProcessTree: true);
		}

		public void Dispose() {
			if (!_process.HasExited) {
				_process.Kill(entireProcessTree: true);
			}
		}
	}

	/// <summary>Containment that creates bookkeeping-only workers, so cap and budget tests spawn nothing.</summary>
	private sealed class FakeContainment : IProcessContainment {

		private int _nextProcessId = 10_000;

		public int LaunchCount { get; private set; }

		public bool OwnsProcessCreation => true;

		public IContainedWorker Launch(WorkerLaunchRequest request) {
			LaunchCount++;
			return new FakeContainedWorker(Interlocked.Increment(ref _nextProcessId));
		}

		public IContainedWorker Adopt(IWorkerProcessHandle startedProcess) =>
			throw new NotSupportedException();

		public WorkerTerminationOutcome TerminateOrphan(IWorkerProcessHandle orphan) =>
			WorkerTerminationOutcome.FallbackTreeKilled;
	}

	private sealed class FakeContainedWorker : IContainedWorker {

		private readonly TaskCompletionSource<bool> _exited =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public FakeContainedWorker(int processId) {
			ProcessId = processId;
			StartTimeUtc = DateTime.UtcNow;
		}

		public int ProcessId { get; }

		public DateTime StartTimeUtc { get; }

		public string ExecutablePath => "/fake/clio";

		public Stream StandardInput => Stream.Null;

		public Stream StandardOutput => Stream.Null;

		public Stream StandardError => Stream.Null;

		public bool HasExited => _exited.Task.IsCompleted;

		public int? ExitCode => HasExited ? 0 : null;

		public async Task WaitForExitAsync(CancellationToken cancellationToken) {
			await _exited.Task.WaitAsync(cancellationToken);
		}

		public WorkerTerminationOutcome Kill() {
			_exited.TrySetResult(true);
			return WorkerTerminationOutcome.ContainedJobTerminated;
		}

		public void Dispose() => _exited.TrySetResult(true);
	}
}
