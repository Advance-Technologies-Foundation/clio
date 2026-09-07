using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Common.McpWorker;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using FakeContainment = Clio.Tests.Command.McpServer.WorkerProcessSupervisorTests.FakeContainment;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// ENG-95262 Stage 7 foundation: the admission-capacity model that sticky supervision is built on —
/// reaching an existing worker without admission, the two partitioned pools, the derived sticky cap,
/// the immediate named refusal at that cap, and the operator override that closes threat-model gap G-1.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every behaviour here is asserted with a pool SATURATED, and that is the point of the fixture.</b>
/// A poll issued on an idle host reaches its worker under the correct implementation and under the
/// deadlocking one alike, so a test that only polls an idle supervisor proves nothing. The condition
/// that separates them is capacity that is already fully held — by the very worker the caller is trying
/// to reach (ADR §3.2c).
/// </para>
/// <para>
/// Caps are always stated explicitly through the test-only constructor. Reading the host's processor
/// count would make the arithmetic under test differ between a two-core build agent and a sixteen-core
/// laptop, which is how a partition bug ships green.
/// </para>
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class WorkerAdmissionCapacityTests {

	// Short enough that a call which QUEUED instead of being refused is reported as a failure rather
	// than as a slow test, and long enough that an ordinary admission on a loaded agent is never
	// mistaken for saturation.
	private static readonly TimeSpan ShortQueueWaitBound = TimeSpan.FromSeconds(2);

	private ILogger _logger;
	private IProcessExecutor _processExecutor;
	private IClioExecutablePathProvider _pathProvider;

	[SetUp]
	public void SetUp() {
		_logger = Substitute.For<ILogger>();
		_processExecutor = Substitute.For<IProcessExecutor>();
		_pathProvider = Substitute.For<IClioExecutablePathProvider>();
		// The test host's own executable: an absolute path that exists on every platform. FakeContainment
		// never runs it.
		_pathProvider.Resolve(Arg.Any<string[]>())
			.Returns(new ClioWorkerLaunchDescriptor(Environment.ProcessPath, Array.Empty<string>(),
				Path.GetTempPath()));
	}

	[TearDown]
	public void TearDown() {
		_logger.ClearReceivedCalls();
		_processExecutor.ClearReceivedCalls();
		_pathProvider.ClearReceivedCalls();
	}

	[Test]
	[Description("Reaching a worker that already exists takes no admission slot and succeeds even when every slot of every pool is held — including the sticky slot held by the worker being reached, which is the hold-and-wait cycle this seam removes.")]
	public async Task ReachExisting_ShouldTakeNoAdmissionSlot_WhenEveryPoolIsSaturated() {
		// Arrange — a total of two partitions into one sticky slot and one per-call slot, and both are
		// then held. Under an implementation that routed reaching through admission, there is nothing
		// left for the poll to acquire.
		FakeContainment containment = new();
		IStaleWorkerRegistry registry = Substitute.For<IStaleWorkerRegistry>();
		WorkerProcessSupervisor sut = new(_logger, _processExecutor, containment, _pathProvider, registry,
			concurrencyCap: 2, queueWaitBound: ShortQueueWaitBound);
		using IWorkerLease sticky = await sut.SpawnContainedAsync(
			new WorkerSpawnRequest { Lifetime = WorkerLifetime.Sticky, Budget = TimeSpan.FromHours(1) },
			CancellationToken.None);
		using IWorkerLease perCall = await sut.SpawnContainedAsync(
			new WorkerSpawnRequest { Budget = TimeSpan.FromMinutes(5) }, CancellationToken.None);
		WorkerSupervisorSnapshot saturated = sut.GetSnapshot();

		// Act
		long startedAt = Stopwatch.GetTimestamp();
		IWorkerChannel channel = sut.ReachExisting(sticky);
		TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt);
		WorkerSupervisorSnapshot afterReaching = sut.GetSnapshot();

		// Assert
		saturated.ActiveStickyWorkers.Should().Be(saturated.StickyConcurrencyCap,
			because: "the discriminating condition is a SATURATED sticky pool: on an idle host a poll reaches its worker under the deadlocking implementation too, so this arrangement is what the assertion rests on");
		saturated.QueuedRequests.Should().Be(0,
			because: "both pools are held and nobody is waiting yet, so any queueing observed below was caused by the reach itself");
		channel.Should().NotBeNull(
			because: "admission governs CREATING a worker, never TALKING to one that already exists (ADR §3.2c) — the caller must get its channel with every slot held");
		channel.ProcessId.Should().Be(sticky.ProcessId,
			because: "reaching must resolve to the SAME worker: a poll that reached a different process would report on a compile nobody is running");
		elapsed.Should().BeLessThan(ShortQueueWaitBound,
			because: "a reach that queued for admission could not return before the queue-wait bound, and the prototype measured 0.00-0.02 s poll latency precisely because reaching costs no admission");
		afterReaching.ActiveWorkers.Should().Be(saturated.ActiveWorkers,
			because: "reaching must not have created anything: it is a conversation with a worker that already exists");
		containment.LaunchCount.Should().Be(2,
			because: "only the two spawns launched a process — a reach that launched a third would be the spawn path wearing a different name");
		afterReaching.ActiveStickyWorkers.Should().Be(saturated.ActiveStickyWorkers,
			because: "occupancy is unchanged by reaching, which is what 'takes no slot' means in the only terms the pool can be observed in");
	}

	[Test]
	[Description("A channel handed to a caller that merely reached an existing worker cannot end that worker: it is neither a lease nor disposable, so the poll path cannot terminate the operation it is only observing.")]
	public async Task ReachExisting_ShouldReturnAChannelThatCannotEndTheWorker() {
		// Arrange
		FakeContainment containment = new();
		IStaleWorkerRegistry registry = Substitute.For<IStaleWorkerRegistry>();
		WorkerProcessSupervisor sut = new(_logger, _processExecutor, containment, _pathProvider, registry,
			concurrencyCap: 2, queueWaitBound: ShortQueueWaitBound);
		using IWorkerLease sticky = await sut.SpawnContainedAsync(
			new WorkerSpawnRequest { Lifetime = WorkerLifetime.Sticky, Budget = TimeSpan.FromHours(1) },
			CancellationToken.None);

		// Act
		IWorkerChannel channel = sut.ReachExisting(sticky);

		// Assert
		channel.Should().NotBeAssignableTo<IDisposable>(
			because: "a single 'using' on the poll path would otherwise kill the worker whose compile the poll was watching");
		channel.Should().NotBeAssignableTo<IWorkerLease>(
			because: "handing back the lease under a narrower static type would leave the kill switch one cast away, and a static type is a suggestion rather than a guarantee");
		channel.HasExited.Should().BeFalse(
			because: "the worker is still running, and HasExited is how a caller that reached an existing worker learns otherwise");
		sut.GetSnapshot().ActiveStickyWorkers.Should().Be(1,
			because: "obtaining and using a channel must leave the operation exactly as it was");
	}

	[Test]
	[Description("A lease from somewhere else is rejected rather than reached, because a channel to a process this supervisor never admitted would let a caller converse with a worker nobody is accounting for.")]
	public void ReachExisting_ShouldRejectALeaseItDidNotIssue() {
		// Arrange
		FakeContainment containment = new();
		IStaleWorkerRegistry registry = Substitute.For<IStaleWorkerRegistry>();
		WorkerProcessSupervisor sut = new(_logger, _processExecutor, containment, _pathProvider, registry,
			concurrencyCap: 4);
		IWorkerLease foreign = Substitute.For<IWorkerLease>();

		// Act
		Action reachForeign = () => sut.ReachExisting(foreign);
		Action reachNothing = () => sut.ReachExisting(null);

		// Assert
		reachForeign.Should().Throw<ArgumentException>(
			because: "this supervisor can only vouch for workers it admitted itself, exactly as KillContained already refuses a foreign lease");
		reachNothing.Should().Throw<ArgumentNullException>(
			because: "a null lease is a caller defect and must be named as one rather than producing a channel to nothing");
	}

	[Test]
	[Description("The reach contract exposes no way to create a worker, so a component injected with it instead of the whole supervisor cannot route a status poll through admission even by mistake — the deadlock stops being a mistake somebody can make.")]
	public void IWorkerReach_ShouldExposeNoWayToCreateAWorker() {
		// Arrange
		Type reachContract = typeof(IWorkerReach);

		// Act
		MethodInfo[] methods = reachContract.GetMethods();
		string[] names = methods.Select(method => method.Name).ToArray();

		// Assert
		names.Should().ContainSingle(
			because: "the contract exists to be narrow: one member, and it is the admission-free one");
		names.Should().NotContain(name => name.Contains("Spawn", StringComparison.Ordinal),
			because: "an implementer who routes a poll through SpawnContainedAsync satisfies every word of 'the poll reaches the same worker' and ships the deadlock, so the spawn path must be absent from the type the poll path depends on");
		typeof(IWorkerProcessSupervisor).Should().BeAssignableTo<IWorkerReach>(
			because: "the supervisor issues the leases, so it must implement the reach contract — the narrowing is for the consumer side");
	}

	[Test]
	[Description("With every sticky slot held for a long operation, an ordinary per-call worker is still admitted promptly under a tight queue-wait bound: the per-call floor is capacity sticky work can never take, which is the property the separate pools exist to provide.")]
	public async Task SpawnContainedAsync_ShouldStillAdmitPerCallWork_WhenTheStickyPoolIsSaturated() {
		// Arrange — a total of four gives two sticky slots and two per-call ones; both sticky slots are
		// held by hour-long operations before any ordinary call is made.
		FakeContainment containment = new();
		IStaleWorkerRegistry registry = Substitute.For<IStaleWorkerRegistry>();
		WorkerProcessSupervisor sut = new(_logger, _processExecutor, containment, _pathProvider, registry,
			concurrencyCap: 4, queueWaitBound: ShortQueueWaitBound);
		WorkerSpawnRequest longOperation = new()
			{ Lifetime = WorkerLifetime.Sticky, Budget = TimeSpan.FromHours(1) };
		using IWorkerLease firstLongOperation =
			await sut.SpawnContainedAsync(longOperation, CancellationToken.None);
		using IWorkerLease secondLongOperation =
			await sut.SpawnContainedAsync(longOperation, CancellationToken.None);
		WorkerSupervisorSnapshot saturated = sut.GetSnapshot();

		// Act
		long startedAt = Stopwatch.GetTimestamp();
		using IWorkerLease firstRead = await sut.SpawnContainedAsync(
			new WorkerSpawnRequest { Budget = TimeSpan.FromMinutes(5) }, CancellationToken.None);
		using IWorkerLease secondRead = await sut.SpawnContainedAsync(
			new WorkerSpawnRequest { Budget = TimeSpan.FromMinutes(5) }, CancellationToken.None);
		TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt);

		// Assert
		saturated.ActiveStickyWorkers.Should().Be(2,
			because: "the ordinary reads below have to be issued against a FULLY saturated sticky pool, or they would pass under a single shared pool as well");
		firstRead.Should().NotBeNull(
			because: "capacity reserved for per-call work must be capacity long operations can never take (ADR §3.2c)");
		secondRead.Should().NotBeNull(
			because: "the floor is the whole per-call cap, not one courtesy slot: both reserved slots must be reachable while every sticky slot is held");
		elapsed.Should().BeLessThan(ShortQueueWaitBound,
			because: "a per-call caller that had to wait behind an hour-long sticky worker would be refused at this bound instead of admitted, which is the exhaustion the partition removes");
		sut.GetSnapshot().ActiveWorkers.Should().Be(4,
			because: "two long operations and two ordinary reads is exactly the total capacity, which is what the partition adds up to");
		sut.GetSnapshot().QueuedRequests.Should().Be(0,
			because: "no per-call caller ever queued, so nothing should be reported as waiting");
	}

	[Test]
	[Description("The sticky cap is strictly less than the total for every total, leaves the per-call pool a floor of at least one slot, and is never the larger side of the split — derived in one place rather than stated twice.")]
	public void DeriveStickyConcurrencyCap_ShouldStayStrictlyBelowTheTotalAndLeaveAPerCallFloor() {
		// Arrange — literals rather than the host's processor count, so the arithmetic is the same
		// assertion on a two-core agent and on a sixteen-core laptop.
		(int Total, int ExpectedSticky)[] cases = [
			(1, 0), (2, 1), (3, 1), (4, 2), (5, 2), (8, 4),
			(WorkerProcessSupervisor.MaximumConfigurableConcurrencyCap,
				WorkerProcessSupervisor.MaximumConfigurableConcurrencyCap / 2)
		];

		// Act & Assert
		foreach ((int total, int expectedSticky) in cases) {
			int sticky = WorkerProcessSupervisor.DeriveStickyConcurrencyCap(total);
			sticky.Should().Be(expectedSticky,
				because: $"halving is the stated derivation, and a total of {total} must therefore support {expectedSticky} concurrent long operation(s)");
		}
		for (int total = 1; total <= WorkerProcessSupervisor.MaximumConfigurableConcurrencyCap; total++) {
			int sticky = WorkerProcessSupervisor.DeriveStickyConcurrencyCap(total);
			sticky.Should().BeLessThan(total,
				because: $"two pools whose caps merely used up the total would relabel the exhaustion rather than remove it, so the sticky cap must be STRICTLY below the total (total {total})");
			(total - sticky).Should().BeGreaterThanOrEqualTo(1,
				because: $"ordinary reads need a guaranteed floor that long operations can never take, and a floor of zero is not a floor (total {total})");
			(total - sticky).Should().BeGreaterThanOrEqualTo(sticky,
				because: $"the side of the split that answers ordinary calls must never be the smaller one (total {total})");
		}
	}

	[Test]
	[Description("The supervisor's two pool caps come from the single derivation applied to its total, add up to that total exactly, and are reported on the snapshot — so the strict inequality cannot drift out of step with the floor it produces.")]
	public void ConcurrencyCaps_ShouldPartitionTheTotal_RatherThanBeingStatedTwice() {
		// Arrange
		FakeContainment containment = new();
		IStaleWorkerRegistry registry = Substitute.For<IStaleWorkerRegistry>();
		const int total = 5;

		// Act
		WorkerProcessSupervisor sut = new(_logger, _processExecutor, containment, _pathProvider, registry,
			concurrencyCap: total);
		WorkerSupervisorSnapshot snapshot = sut.GetSnapshot();

		// Assert
		sut.ConcurrencyCap.Should().Be(total,
			because: "the published total is what the operator configured and what the two pools partition");
		sut.StickyConcurrencyCap.Should().Be(WorkerProcessSupervisor.DeriveStickyConcurrencyCap(total),
			because: "the sticky cap must come from the derivation rather than from a second literal that can drift away from it");
		(sut.StickyConcurrencyCap + sut.PerCallConcurrencyCap).Should().Be(total,
			because: "the pools PARTITION the measured ceiling: an additional pool on top of it would let the host run more workers than ADR §2.4 says it can usefully run");
		sut.StickyConcurrencyCap.Should().BeLessThan(sut.ConcurrencyCap,
			because: "the whole point of the split is that long operations can never take the last slot ordinary calls need");
		snapshot.StickyConcurrencyCap.Should().Be(sut.StickyConcurrencyCap,
			because: "the snapshot is how a caller and an operator observe capacity, and a snapshot that hid the sticky pool would report a host as idle while it refuses every long operation");
		snapshot.PerCallConcurrencyCap.Should().Be(sut.PerCallConcurrencyCap,
			because: "the reserved floor has to be observable too, or nobody can tell a saturated host from a mis-partitioned one");
		snapshot.ActiveStickyWorkers.Should().Be(0,
			because: "nothing has been spawned yet, so sticky occupancy must start empty");
	}

	[Test]
	[Description("Saturating the sticky pool refuses the next long operation IMMEDIATELY with a named error carrying the limit, rather than queueing for the wait bound and then refusing generically.")]
	public async Task SpawnContainedAsync_ShouldRefuseTheNextLongOperationImmediately_WhenTheStickyPoolIsSaturated() {
		// Arrange
		FakeContainment containment = new();
		IStaleWorkerRegistry registry = Substitute.For<IStaleWorkerRegistry>();
		WorkerProcessSupervisor sut = new(_logger, _processExecutor, containment, _pathProvider, registry,
			concurrencyCap: 4, queueWaitBound: ShortQueueWaitBound);
		WorkerSpawnRequest longOperation = new()
			{ Lifetime = WorkerLifetime.Sticky, Budget = TimeSpan.FromHours(1) };
		using IWorkerLease first = await sut.SpawnContainedAsync(longOperation, CancellationToken.None);
		using IWorkerLease second = await sut.SpawnContainedAsync(longOperation, CancellationToken.None);

		// Act
		long startedAt = Stopwatch.GetTimestamp();
		WorkerStickyCapacityExceededException refusal = null;
		try {
			using IWorkerLease third = await sut.SpawnContainedAsync(longOperation, CancellationToken.None);
		}
		catch (WorkerStickyCapacityExceededException exception) {
			refusal = exception;
		}
		TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt);

		// Assert
		refusal.Should().NotBeNull(
			because: "the sticky cap IS the number of concurrent long operations the host supports, so the next one is refused by a name that says that rather than by the queue-wait expiry, which would claim a wait that never happened");
		elapsed.Should().BeLessThan(ShortQueueWaitBound / 2,
			because: "a holder keeps its slot for minutes to an hour, so queueing could only spend the caller's patience on the way to the same answer — the refusal must arrive at once");
		refusal.StickyConcurrencyCap.Should().Be(2,
			because: "the refusal must carry the LIMIT, which is the number the caller needs in order to know how many long operations this host runs");
		refusal.TotalConcurrencyCap.Should().Be(4,
			because: "the total is the knob that moves the limit, so naming it turns the message into something an operator can act on");
		refusal.Message.Should().Contain("2",
			because: "the limit has to be in the text as well as on the exception: an agent reads the message, not the properties");
		refusal.Message.Should().Contain(WorkerProcessSupervisor.ConcurrencyCapOverrideEnvVar,
			because: "the remedy belongs in the refusal — an operator told only that the host is full has nowhere to go");
		containment.LaunchCount.Should().Be(2,
			because: "a refused long operation must not have spawned anything: nothing ran and no request reached Creatio");
		sut.GetSnapshot().QueuedRequests.Should().Be(0,
			because: "the refused caller never queued, and counting it would report a phantom depth to the next caller that reads the snapshot");
	}

	[Test]
	[Description("A host whose total capacity is one supports no long operation at all — it is told so by name, pointed at the override that fixes it, and still serves ordinary per-call work.")]
	public async Task SpawnContainedAsync_ShouldRefuseEveryLongOperation_WhenTheTotalLeavesNoStickyCapacity() {
		// Arrange — the degenerate single-slot host: one slot cannot both carry an hour-long operation
		// and leave ordinary calls a floor.
		FakeContainment containment = new();
		IStaleWorkerRegistry registry = Substitute.For<IStaleWorkerRegistry>();
		WorkerProcessSupervisor sut = new(_logger, _processExecutor, containment, _pathProvider, registry,
			concurrencyCap: 1, queueWaitBound: ShortQueueWaitBound);

		// Act
		WorkerStickyCapacityExceededException refusal = null;
		try {
			using IWorkerLease sticky = await sut.SpawnContainedAsync(
				new WorkerSpawnRequest { Lifetime = WorkerLifetime.Sticky, Budget = TimeSpan.FromHours(1) },
				CancellationToken.None);
		}
		catch (WorkerStickyCapacityExceededException exception) {
			refusal = exception;
		}
		using IWorkerLease ordinaryRead = await sut.SpawnContainedAsync(
			new WorkerSpawnRequest { Budget = TimeSpan.FromMinutes(5) }, CancellationToken.None);

		// Assert
		sut.StickyConcurrencyCap.Should().Be(0,
			because: "a total of one cannot be split into a long-operation slot and a per-call floor, and the arithmetic says so rather than papering over it by doubling the host's admitted concurrency");
		sut.PerCallConcurrencyCap.Should().Be(1,
			because: "the whole of a single-slot host stays available to ordinary calls, which is the behaviour every such host has today");
		refusal.Should().NotBeNull(
			because: "a host that cannot run a long operation must say so immediately rather than admitting one and deadlocking its own status polls");
		refusal.StickyConcurrencyCap.Should().Be(0,
			because: "'this host supports zero concurrent long operations' is the actionable statement, and it is only actionable if the zero is carried");
		refusal.Message.Should().Contain(WorkerProcessSupervisor.ConcurrencyCapOverrideEnvVar,
			because: "the operator's remedy on a single-slot host is to raise the total, so the refusal must name the variable that does it (threat model G-1)");
		ordinaryRead.Should().NotBeNull(
			because: "refusing long operations must not cost the host its ordinary per-call work");
	}

	[Test]
	[Description("The concurrency-cap override parses only a sane whole number of workers and falls back to the core-count default for everything else, so a malformed value can neither remove the cap nor fork hundreds of processes.")]
	public void ResolveConcurrencyCap_ShouldFallBackToTheDefault_WhenTheOverrideIsAbsentOrUnusable() {
		// Arrange — the fallback is the shipped derivation itself, restated here rather than pinned to a
		// number, because the number is the host's and the RULE is what is under test.
		int coreCountDefault = Math.Max(1, Environment.ProcessorCount);

		// Act
		int absent = WorkerProcessSupervisor.ResolveConcurrencyCap(null);
		int blank = WorkerProcessSupervisor.ResolveConcurrencyCap("   ");
		int notANumber = WorkerProcessSupervisor.ResolveConcurrencyCap("lots");
		int zero = WorkerProcessSupervisor.ResolveConcurrencyCap("0");
		int negative = WorkerProcessSupervisor.ResolveConcurrencyCap("-2");
		int tooLarge = WorkerProcessSupervisor.ResolveConcurrencyCap(
			(WorkerProcessSupervisor.MaximumConfigurableConcurrencyCap + 1).ToString());
		int fractional = WorkerProcessSupervisor.ResolveConcurrencyCap("2.5");
		int accepted = WorkerProcessSupervisor.ResolveConcurrencyCap("3");
		int atTheCeiling = WorkerProcessSupervisor.ResolveConcurrencyCap(
			WorkerProcessSupervisor.MaximumConfigurableConcurrencyCap.ToString());

		// Assert
		absent.Should().Be(coreCountDefault,
			because: "no override means the core-count default, which is the ordinary case on every host");
		blank.Should().Be(coreCountDefault,
			because: "an empty variable is how a shell spells 'unset', and it must not be read as a cap of zero");
		notANumber.Should().Be(coreCountDefault,
			because: "a typo must leave the measured default in force rather than removing admission control");
		zero.Should().Be(coreCountDefault,
			because: "a cap of zero would refuse every call on the host, which is a broken clio rather than a configured one");
		negative.Should().Be(coreCountDefault,
			because: "a negative cap is meaningless and must never reach a semaphore's constructor");
		tooLarge.Should().Be(coreCountDefault,
			because: "ADR §2.4 measured no gain above roughly the core count and 1073 MB at width 16, so a value past the documented ceiling is a mistyped number rather than an intention");
		fractional.Should().Be(coreCountDefault,
			because: "workers are whole: accepting '2.5' would silently mean something the operator did not write");
		accepted.Should().Be(3,
			because: "a legitimate override must actually take effect, or the escape hatch that closes gap G-1 does not exist");
		atTheCeiling.Should().Be(WorkerProcessSupervisor.MaximumConfigurableConcurrencyCap,
			because: "the documented range is inclusive at its upper end, so the largest accepted value must be accepted");
	}

	[Test]
	[Description("A sticky spawn blocked only because ORDINARY reads momentarily hold every slot must not be told the long-operation limit is exhausted: that condition is transient, a snapshot at that instant reports zero sticky workers, and 'wait for one to finish' would name nothing to wait for.")]
	public async Task SpawnContainedAsync_ShouldNotBlameTheStickyCeiling_WhenOnlyPerCallWorkHoldsTheSlots() {
		// Arrange — every slot taken by PER-CALL work, so the sticky ceiling is entirely unconsumed.
		FakeContainment containment = new();
		IStaleWorkerRegistry registry = Substitute.For<IStaleWorkerRegistry>();
		WorkerProcessSupervisor sut = new(_logger, _processExecutor, containment, _pathProvider, registry,
			concurrencyCap: 2, queueWaitBound: TimeSpan.FromMilliseconds(150));
		WorkerSpawnRequest perCall = new() { Budget = TimeSpan.FromMinutes(5) };
		IWorkerLease first = await sut.SpawnContainedAsync(perCall, CancellationToken.None);
		IWorkerLease second = await sut.SpawnContainedAsync(perCall, CancellationToken.None);
		sut.GetSnapshot().ActiveStickyWorkers.Should().Be(0,
			because: "the arrangement must leave the sticky ceiling untouched, or this test would not be about the shared pool at all");

		// Act
		Func<Task> stickySpawn = async () => await sut.SpawnContainedAsync(
			new WorkerSpawnRequest { Budget = TimeSpan.FromMinutes(5), Lifetime = WorkerLifetime.Sticky },
			CancellationToken.None);

		// Assert
		await stickySpawn.Should().ThrowAsync<WorkerQueueWaitExpiredException>(
			because: "the slots are held by ordinary reads that clear in seconds, so this is the ordinary saturation the queue-wait bound exists to report — not the long-operation ceiling, which is empty");
		sut.GetSnapshot().ActiveStickyWorkers.Should().Be(0,
			because: "a sticky reservation taken before the slot wait must be given back when that wait fails, or the ceiling would leak a place nobody holds and refuse forever");
		first.Dispose();
		second.Dispose();
	}
}
