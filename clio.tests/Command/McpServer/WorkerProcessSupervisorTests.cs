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

	// Long enough that a bounded wait of 200 ms has certainly finished on a loaded agent, short enough
	// that an UNBOUNDED wait is reported as a failing test rather than as a hung run.
	private static readonly TimeSpan UnboundedWaitDetectionWindow = TimeSpan.FromSeconds(15);

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
	[Description("TC-U-201b: while it is INSIDE the queue-wait bound, a queued call is ended by its own caller's token and by nothing else, and cancelling it releases nothing that another caller holds.")]
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
			because: "a busy supervisor makes a caller WAIT rather than failing it immediately — this window (400 ms) is a fraction of the 60 s queue-wait bound, so nothing but the caller can end it here");
		await awaitCancelled.Should().ThrowAsync<OperationCanceledException>(
			because: "inside the bound the caller's own token is the only thing that ends the wait, and it must surface as cancellation rather than as the saturation refusal");
		containment.LaunchCount.Should().Be(1,
			because: "a cancelled caller must not have consumed a worker");
		held.Dispose();
		sut.GetSnapshot().ActiveWorkers.Should().Be(0,
			because: "releasing the only lease must return the slot even after a queued caller gave up");
	}

	[Test]
	[Description("A call that outlasts the queue-wait bound is refused with the named saturation exception carrying the bound, the cap and the queue depth — never left waiting for ever, and never reported as a caller cancellation or a backend timeout.")]
	public async Task SpawnContainedAsync_ShouldThrowQueueWaitExpired_WhenNoSlotFreesWithinTheBound() {
		// Arrange — every slot of a cap of one is held, so the next caller can only queue.
		FakeContainment containment = new();
		IStaleWorkerRegistry registry = Substitute.For<IStaleWorkerRegistry>();
		TimeSpan bound = TimeSpan.FromMilliseconds(200);
		WorkerProcessSupervisor sut = new(_logger, _processExecutor, containment, _pathProvider, registry,
			concurrencyCap: 1, queueWaitBound: bound);
		WorkerSpawnRequest request = new() { Budget = TimeSpan.FromMinutes(5) };
		using IWorkerLease held = await sut.SpawnContainedAsync(request, CancellationToken.None);

		// Act — the assertion itself is bounded: with no queue bound this call never completes, and a test
		// that HANGS reports nothing, so the failure has to be observable as a failure.
		Task<IWorkerLease> queued = sut.SpawnContainedAsync(request, CancellationToken.None);
		Task first = await Task.WhenAny(queued, Task.Delay(UnboundedWaitDetectionWindow));

		// Assert
		first.Should().BeSameAs(queued,
			because: "an unbounded queue wait is the wedge in another shape — a call that returns nothing, issues no request to Creatio, and does so for an arbitrarily long time");
		WorkerQueueWaitExpiredException refusal = null;
		try {
			await queued;
		}
		catch (WorkerQueueWaitExpiredException exception) {
			refusal = exception;
		}
		refusal.Should().NotBeNull(
			because: "the expiry must be a NAMED outcome: a TimeoutException would read as a slow Creatio (nothing was even asked of it) and an OperationCanceledException would read as the caller giving up");
		refusal.ConfiguredBound.Should().Be(bound,
			because: "a caller that is told to back off must be told what it was measured against, or it cannot tell a tight local bound from a saturated host");
		refusal.ConcurrencyCap.Should().Be(1,
			because: "the cap is the capacity that ran out and is what a caller needs in order to reason about how much concurrency is too much");
		refusal.QueueDepth.Should().Be(1,
			because: "the depth including this call separates 'briefly busy' from 'structurally saturated' — one caller against a full cap is a burst, many is a host that will not recover by waiting");
		refusal.WaitEndured.Should().BeGreaterThan(TimeSpan.Zero,
			because: "the wait actually endured must be reported, so the refusal describes what happened rather than only what was configured");
		containment.LaunchCount.Should().Be(1,
			because: "a refused caller must not have spawned anything: the whole point is that the call never reached a worker");
		sut.GetSnapshot().QueuedRequests.Should().Be(0,
			because: "a refused caller must leave the queue, or the depth reported to the next one is a lie");
	}

	[Test]
	[Description("The queue wait and the response budget are separate bounds: a call that spent real time queued still receives its FULL budget, measured from the instant its worker was spawned rather than from when it was admitted.")]
	public async Task SpawnContainedAsync_ShouldGrantTheFullBudget_WhenTheCallSpentTimeQueued() {
		// Arrange
		FakeContainment containment = new();
		IStaleWorkerRegistry registry = Substitute.For<IStaleWorkerRegistry>();
		// The bound is generous relative to the wait on purpose. What this test has to prove is that the
		// budget is measured from SPAWN — a wait of a couple of seconds against a 30 s budget shows that
		// unmistakably — and tightening the bound around the wait would only add a way for a loaded build
		// agent to fail the test for a reason that has nothing to do with the property.
		TimeSpan bound = TimeSpan.FromSeconds(15);
		TimeSpan budget = TimeSpan.FromSeconds(30);
		WorkerProcessSupervisor sut = new(_logger, _processExecutor, containment, _pathProvider, registry,
			concurrencyCap: 1, queueWaitBound: bound);
		WorkerSpawnRequest request = new() { Budget = budget };
		IWorkerLease blocking = await sut.SpawnContainedAsync(request, CancellationToken.None);
		DateTimeOffset enqueuedAtUtc = DateTimeOffset.UtcNow;

		// Act — queued for over half the bound, then admitted.
		Task<IWorkerLease> queued = sut.SpawnContainedAsync(request, CancellationToken.None);
		await Task.Delay(TimeSpan.FromSeconds(2.2));
		DateTimeOffset releasedAtUtc = DateTimeOffset.UtcNow;
		blocking.Dispose();
		using IWorkerLease admitted = await queued;

		// Assert
		(admitted.SpawnedAtUtc - enqueuedAtUtc).Should().BeGreaterThan(TimeSpan.FromSeconds(2),
			because: "the call must genuinely have spent time queued, or the claim that queueing does not shorten the budget is vacuous");
		admitted.SpawnedAtUtc.Should().BeOnOrAfter(releasedAtUtc,
			because: "the worker was created only once a slot came free, which is what makes the two bounds separately observable at all");
		(admitted.BudgetExpiresAtUtc - admitted.SpawnedAtUtc).Should().Be(budget,
			because: "queueing must not shorten the budget: the two bounds answer different questions and a call punished for waiting is a failure mode the fix would have invented");
		admitted.BudgetExpiresAtUtc.Should().BeAfter(enqueuedAtUtc + budget,
			because: "an admission-anchored deadline would already have burnt the wait, which is exactly the ADR §2.4 measurement (16.9 s queued on a healthy stand) that anchors the clock on spawn");
	}

	[Test]
	[Description("Bounding the queue must not turn queueing into failure: a call that waits briefly and then gets a slot is admitted and spawns its worker, even under a tight bound.")]
	public async Task SpawnContainedAsync_ShouldStillAdmit_WhenTheCallQueuesBrieflyUnderATightBound() {
		// Arrange
		FakeContainment containment = new();
		IStaleWorkerRegistry registry = Substitute.For<IStaleWorkerRegistry>();
		WorkerProcessSupervisor sut = new(_logger, _processExecutor, containment, _pathProvider, registry,
			concurrencyCap: 1, queueWaitBound: TimeSpan.FromSeconds(5));
		WorkerSpawnRequest request = new() { Budget = TimeSpan.FromMinutes(5) };
		IWorkerLease blocking = await sut.SpawnContainedAsync(request, CancellationToken.None);

		// Act — the slot frees immediately; the queued caller must be admitted rather than refused.
		Task<IWorkerLease> queued = sut.SpawnContainedAsync(request, CancellationToken.None);
		blocking.Dispose();
		using IWorkerLease admitted = await queued;

		// Assert
		admitted.Should().NotBeNull(
			because: "a queued call is delayed, never dropped (AC-01) — the bound refuses only a wait that outlasts it");
		containment.LaunchCount.Should().Be(2,
			because: "the admitted caller must have received a worker of its own, which is what proves the bound did not fire on an ordinary short wait");
		sut.GetSnapshot().QueuedRequests.Should().Be(0,
			because: "an admitted caller has left the queue and must not still be counted against it");
	}

	[Test]
	[Description("The queue-wait override parses only a sane positive number of seconds and falls back to the measured default for everything else, so a malformed value can never remove the bound.")]
	public void ResolveQueueWaitBound_ShouldFallBackToTheDefault_WhenTheOverrideIsAbsentOrUnusable() {
		// Arrange & Act
		TimeSpan absent = WorkerProcessSupervisor.ResolveQueueWaitBound(null);
		TimeSpan blank = WorkerProcessSupervisor.ResolveQueueWaitBound("   ");
		TimeSpan notANumber = WorkerProcessSupervisor.ResolveQueueWaitBound("soon");
		TimeSpan zero = WorkerProcessSupervisor.ResolveQueueWaitBound("0");
		TimeSpan negative = WorkerProcessSupervisor.ResolveQueueWaitBound("-30");
		TimeSpan tooLarge = WorkerProcessSupervisor.ResolveQueueWaitBound("3601");
		TimeSpan accepted = WorkerProcessSupervisor.ResolveQueueWaitBound("45");
		TimeSpan fractional = WorkerProcessSupervisor.ResolveQueueWaitBound("1.5");

		// Assert
		absent.Should().Be(WorkerProcessSupervisor.DefaultQueueWaitBound,
			because: "no override means the measured default, which is the ordinary case on every host");
		blank.Should().Be(WorkerProcessSupervisor.DefaultQueueWaitBound,
			because: "an empty variable is how a shell spells 'unset', and it must not be read as a zero bound");
		notANumber.Should().Be(WorkerProcessSupervisor.DefaultQueueWaitBound,
			because: "a typo must leave the bound in force rather than removing it, which would restore the unbounded wait");
		zero.Should().Be(WorkerProcessSupervisor.DefaultQueueWaitBound,
			because: "a zero bound would refuse every call that has to queue at all, turning a busy host into a failing one");
		negative.Should().Be(WorkerProcessSupervisor.DefaultQueueWaitBound,
			because: "a negative bound is meaningless and must not be handed to a semaphore wait");
		tooLarge.Should().Be(WorkerProcessSupervisor.DefaultQueueWaitBound,
			because: "an hour-long queue wait is indistinguishable from no bound at all from the caller's seat");
		accepted.Should().Be(TimeSpan.FromSeconds(45),
			because: "a legitimate override must actually take effect, or the escape hatch documented on the variable does not exist");
		fractional.Should().Be(TimeSpan.FromSeconds(1.5),
			because: "the value is parsed in invariant culture as seconds, so a fractional bound is expressible on every host locale");
	}

	[Test]
	[Description("The measured default queue-wait bound stays inside the window the ADR §2.4 measurements define: comfortably above the worst healthy queue wait, and small enough that the bound plus a full response budget still answers before an MCP client abandons the call.")]
	public void DefaultQueueWaitBound_ShouldStayWithinTheMeasuredWindow() {
		// Arrange — the two numbers the default is derived from, restated as literals so a change to the
		// default has to argue with the measurements rather than silently drift past them.
		TimeSpan worstMeasuredHealthyQueueWait = TimeSpan.FromSeconds(16.9);
		TimeSpan observedClientCeiling = TimeSpan.FromSeconds(180);
		TimeSpan defaultResponseBudget = TimeSpan.FromSeconds(120);

		// Act
		TimeSpan bound = WorkerProcessSupervisor.DefaultQueueWaitBound;

		// Assert
		bound.Should().BeGreaterThan(worstMeasuredHealthyQueueWait * 2,
			because: "at concurrency width 16 on the four-core Windows stand a HEALTHY call waited 16.9 s just to reach initialize (ADR §2.4); a bound near that would refuse calls for being busy");
		(bound + defaultResponseBudget).Should().BeLessThanOrEqualTo(observedClientCeiling,
			because: "queue wait plus response budget must still fit inside the ~180 s an MCP client gives one call, or clio's own answer arrives after the client stopped listening and the caller learns nothing");
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
	[Description("Each proxy variable the parent honours actually reaches the spawned worker, in the uppercase AND lowercase spelling, so a child behind a mandated inspecting proxy can still reach Creatio instead of failing as a broken environment.")]
	public async Task SpawnContainedAsync_ShouldHandEveryProxyVariableSpellingToTheWorker() {
		// Arrange — the six spellings are named one by one rather than read from the allowlist: a test that
		// iterated the allowlist would assert that the list contains what the list contains, and would stay
		// green if a spelling were deleted. They share ONE value so the assertion also holds on Windows,
		// where environment-variable names are case-insensitive and the two spellings are one variable.
		string[] spellings = ["HTTP_PROXY", "HTTPS_PROXY", "NO_PROXY", "http_proxy", "https_proxy", "no_proxy"];
		const string sentinel = "http://clio-worker-proxy-sentinel.invalid:8080";
		Dictionary<string, string> originals = new(StringComparer.Ordinal);
		foreach (string spelling in spellings) {
			originals[spelling] = Environment.GetEnvironmentVariable(spelling);
		}
		FakeContainment containment = new();
		IStaleWorkerRegistry registry = Substitute.For<IStaleWorkerRegistry>();
		WorkerProcessSupervisor sut = new(_logger, _processExecutor, containment, _pathProvider, registry,
			concurrencyCap: 1);

		// Act — the parent's environment is mutated for the duration of ONE spawn and restored immediately,
		// because these variables are process-wide and other fixtures run in parallel.
		try {
			foreach (string spelling in spellings) {
				Environment.SetEnvironmentVariable(spelling, sentinel);
			}
			using IWorkerLease lease = await sut.SpawnContainedAsync(
				new WorkerSpawnRequest { Budget = TimeSpan.FromMinutes(5) }, CancellationToken.None);
		}
		finally {
			foreach (KeyValuePair<string, string> original in originals) {
				Environment.SetEnvironmentVariable(original.Key, original.Value);
			}
		}
		Dictionary<string, string> childEnvironment =
			new(containment.LastRequest.Environment, StringComparer.Ordinal);

		// Assert
		childEnvironment.Should().Contain("HTTP_PROXY", sentinel,
			because: "a child that cannot see the proxy the parent uses either cannot reach Creatio at all or reaches it around a mandated inspecting proxy — and both present to the user as a broken environment");
		childEnvironment.Should().Contain("HTTPS_PROXY", sentinel,
			because: "Creatio is reached over HTTPS, so this is the spelling that decides whether the worker can talk to the stand at all");
		childEnvironment.Should().Contain("NO_PROXY", sentinel,
			because: "without the exclusion list an on-premise stand that must be reached DIRECTLY is sent through the proxy instead, which fails in a way that looks like the stand is down");
		childEnvironment.Should().Contain("http_proxy", sentinel,
			because: "on Unix the lowercase spelling is the conventional one and is read case-sensitively — it is not a duplicate of the uppercase entry");
		childEnvironment.Should().Contain("https_proxy", sentinel,
			because: "a host that set only the lowercase spelling must not silently hand the worker an unproxied environment");
		childEnvironment.Should().Contain("no_proxy", sentinel,
			because: "the exclusion list has the same two spellings as the proxy it excludes from, and dropping one of them re-proxies the excluded hosts");
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

		/// <summary>The launch request of the most recent worker, including the frozen child environment.</summary>
		public WorkerLaunchRequest LastRequest { get; private set; }

		public bool OwnsProcessCreation => true;

		public IContainedWorker Launch(WorkerLaunchRequest request) {
			LaunchCount++;
			LastRequest = request;
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
