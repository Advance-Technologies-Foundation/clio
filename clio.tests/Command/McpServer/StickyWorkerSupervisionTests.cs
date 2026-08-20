using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Relay;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Common.McpWorker;
using Clio.UserEnvironment;
using FluentAssertions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// ENG-95262 story 7: sticky supervision — a long operation's worker outlives the response, its status
/// poll reaches that same worker WITHOUT taking an admission slot, a private completion signal reaps it,
/// and its lifetime is bounded whatever the operation does.
/// </summary>
/// <remarks>
/// <para>
/// <b>The supervisor, the relay, the dispatcher and the registry are all the PRODUCTION types.</b> Only
/// the child is scripted: <see cref="PipedContainment"/> hands the real supervisor a contained worker
/// whose three streams are ordinary pipes, and a scripted server speaks JSON-RPC on the other end. So
/// admission accounting, containment bookkeeping, the relay's read loop and the dispatcher's branching
/// are exercised together, and a message is serialised, framed, read off a pipe and forwarded exactly as
/// a worker's would be.
/// </para>
/// <para>
/// <b>Every admission assertion is made with the sticky pool SATURATED, and that is the whole design of
/// this fixture.</b> A poll on an idle host reaches its worker under the correct implementation and
/// under the deadlocking one alike (ADR §3.2c's own testing note), so an idle-host test proves nothing.
/// Caps are stated explicitly through the supervisor's test-only constructor rather than derived from
/// the host's processor count, so the arithmetic under test is the same on a two-core build agent and on
/// a sixteen-core laptop.
/// </para>
/// </remarks>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class StickyWorkerSupervisionTests {

	private const string CompileToolName = "compile-creatio";
	private const string CompileStatusToolName = "compile-status";
	private const string InstallProcessBuilderToolName = "install-process-builder";
	private const string RestartToolName = "restart-by-environment-name";
	private const string RestartStatusToolName = "restart-status";
	private const string EnvironmentName = "sandbox";

	/// <summary>
	/// The agent-observable error class a starter is refused with when its family already has a live
	/// sticky worker for the target.
	/// </summary>
	/// <remarks>
	/// Pinned here as a LITERAL rather than compared to the constant the dispatcher also reads, for the
	/// same reason <c>WorkerOperationSignalContract.NotificationMethod</c> is: it is a token shipped
	/// guidance and clients key on, so a rename must be a deliberate change rather than a comparison that
	/// silently agrees with itself.
	/// </remarks>
	private const string LongOperationInProgressErrorClass = "clio-long-operation-in-progress";

	/// <summary>
	/// Short enough that a call which QUEUED for admission instead of reaching its worker is reported as
	/// a failure rather than as a slow test.
	/// </summary>
	private static readonly TimeSpan ShortQueueWaitBound = TimeSpan.FromSeconds(2);

	/// <summary>Ceiling on every wait here; a scripted child answers in milliseconds.</summary>
	private static readonly TimeSpan AssertionTimeout = TimeSpan.FromSeconds(30);

	/// <summary>
	/// The supervision cadence the cases about a RETIRED session state for themselves.
	/// </summary>
	/// <remarks>
	/// Six hundred times shorter than <see cref="AssertionTimeout"/>, so a loaded agent that misses many
	/// passes still has hundreds left. Nothing asserts how fast a reap arrives — only that it arrives
	/// without a dispatch and without the half-hour lifetime bound — so this cannot become a timing test.
	/// The shipped cadence (<see cref="StickyWorkerRegistry.DefaultSupervisionInterval"/>) is deliberately
	/// not used here: waiting it out would make every one of these cases fifteen seconds long.
	/// </remarks>
	private static readonly TimeSpan FastSupervisionInterval = TimeSpan.FromMilliseconds(50);

	/// <summary>The JSON-RPC method a poll invokes on its worker.</summary>
	private const string CallToolMethodName = "tools/call";

	/// <summary>The JSON-RPC method the bounded liveness probe uses (never <c>ping</c>, ADR 3.1b).</summary>
	private const string ListToolsMethodName = "tools/list";

	/// <summary>The key the hermetic poll harness registers its single sticky worker under.</summary>
	private static readonly StickyWorkerKey PollKey =
		new(McpToolOperationFamily.ConfigurationBuild, $"tenant|{EnvironmentName}");

	/// <summary>
	/// The budget the hermetic polls are given: long enough that anything shorter than it which happens is
	/// a BOUND of its own rather than the budget expiring.
	/// </summary>
	private static readonly TimeSpan PollBudget = TimeSpan.FromSeconds(10);

	/// <summary>The liveness-probe bound the hermetic harness runs with.</summary>
	private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(300);

	/// <summary>
	/// The ceiling a probe-bounded reap must come in under. Well clear of <see cref="ProbeTimeout"/> so a
	/// loaded agent does not fail it, and far short of <see cref="PollBudget"/> so a poll that waited out
	/// its whole budget instead of its probe cannot pass.
	/// </summary>
	private static readonly TimeSpan ProbeBoundCeiling = TimeSpan.FromSeconds(3);

	private ILogger _logger;
	private IProcessExecutor _processExecutor;
	private IClioExecutablePathProvider _pathProvider;
	private ISettingsRepository _settingsRepository;
	private IToolCommandResolver _commandResolver;
	private IStaleWorkerRegistry _staleWorkers;

	[SetUp]
	public void SetUp() {
		_logger = Substitute.For<ILogger>();
		_processExecutor = Substitute.For<IProcessExecutor>();
		_pathProvider = Substitute.For<IClioExecutablePathProvider>();
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_staleWorkers = Substitute.For<IStaleWorkerRegistry>();
		_commandResolver = Substitute.For<IToolCommandResolver>();
		// Stated rather than left to NSubstitute's empty-string default: a blank key would put every
		// environment in one bucket, which is exactly the defect AC-03's cardinality assertions look for,
		// and a test that silently relied on it could not tell a correct key from no key at all.
		// Mirrors the real resolver's shape rather than only its name branch: BuildTargetIdentity folds a
		// registered name and an explicit uri onto ONE key, so a stub that read only Environment would make
		// every url-only call share one key and would hide exactly the defect the credentials-path test
		// looks for.
		_commandResolver.GetTenantKey(Arg.Any<EnvironmentOptions>())
			.Returns(call => $"tenant|{Target(call.Arg<EnvironmentOptions>())}");
		_commandResolver.GetTargetKey(Arg.Any<EnvironmentOptions>())
			.Returns(call => $"target|{Target(call.Arg<EnvironmentOptions>())}");
		_pathProvider.Resolve(Arg.Any<string[]>())
			.Returns(new ClioWorkerLaunchDescriptor(Environment.ProcessPath, Array.Empty<string>(),
				Path.GetTempPath()));
	}

	[TearDown]
	public void TearDown() {
		_logger.ClearReceivedCalls();
		_processExecutor.ClearReceivedCalls();
		_pathProvider.ClearReceivedCalls();
		_settingsRepository.ClearReceivedCalls();
		_commandResolver.ClearReceivedCalls();
		_staleWorkers.ClearReceivedCalls();
	}

	// ---------------------------------------------------------------------------------------------
	// AC-01 — the poll reaches the same worker, and takes no admission slot doing it
	// ---------------------------------------------------------------------------------------------

	[Test]
	[Category("Unit")]
	[Description("compile-creatio returns from a sticky worker and a subsequent compile-status poll is answered by that SAME worker process while the sticky pool is saturated — so the poll took no admission slot, which is the only arrangement that separates a correct implementation from the deadlocking one.")]
	public async Task DispatchAsync_ShouldAnswerAStatusPollFromTheSameStickyWorker_WhenTheStickyPoolIsSaturated() {
		// Arrange — a total of two admits exactly one sticky worker, so the compile below saturates the
		// sticky pool outright: the slot the poll would have to wait for is held by the very worker the
		// poll is trying to reach.
		using StickyFixture fixture = CreateFixture(concurrencyCap: 2);
		CallToolResult compile = await fixture.DispatchAsync(
			CompileToolName, StarterMetadata(McpToolOperationFamily.ConfigurationBuild,
				McpToolSharedFileResource.ConfigurationBuild), EnvironmentName);
		WorkerSupervisorSnapshot saturated = fixture.Supervisor.GetSnapshot();
		int launchesAfterCompile = fixture.Containment.LaunchCount;

		// Act
		long startedAt = Stopwatch.GetTimestamp();
		CallToolResult status = await fixture.DispatchAsync(
			CompileStatusToolName, PollerMetadata(McpToolOperationFamily.ConfigurationBuild,
				McpToolSharedFileResource.ConfigurationBuild), EnvironmentName);
		TimeSpan pollElapsed = Stopwatch.GetElapsedTime(startedAt);

		// Assert
		saturated.ActiveStickyWorkers.Should().Be(saturated.StickyConcurrencyCap,
			because: "the discriminating condition is a SATURATED sticky pool; on an idle host the poll is answered under the deadlocking implementation too, so this arrangement is what every assertion below rests on");
		compile.IsError.Should().NotBeTrue(
			because: "the scripted worker answered the starting call, so a failure here would mean the poll was measuring a dead worker rather than a live one");
		status.IsError.Should().NotBeTrue(
			because: "the poll must be ANSWERED, not refused — a poll refused for want of a slot is precisely the hold-and-wait outcome ADR §3.2c describes");
		fixture.Containment.LaunchCount.Should().Be(launchesAfterCompile,
			because: "reaching an existing worker must create nothing: a second launch would mean the poll went through the spawn path and answered from a process with an empty operation registry");
		fixture.Children.Should().ContainSingle(
			because: "one worker served both calls, which is the whole promise of a sticky lifetime");
		fixture.Children[0].CallCount.Should().Be(2,
			because: "the compile and the poll must both have been answered by the SAME child process — this, not the absence of a second launch, is what 'reaches the same worker' means");
		pollElapsed.Should().BeLessThan(ShortQueueWaitBound,
			because: "a poll that queued for admission could not return before the queue-wait bound expired, and the prototype measured 0.00-0.02 s poll latency precisely because reaching costs no admission");
	}

	[Test]
	[Category("Unit")]
	[Description("A status poll that finds no live sticky worker falls back to an ordinary per-call worker and does NOT take the target's configuration-build reservation — otherwise compile-status would refuse the very compile it was asked to report on.")]
	public async Task DispatchAsync_ShouldNotReserveTheConfigurationBuild_WhenAStatusPollFindsNoStickyWorker() {
		// Arrange
		using StickyFixture fixture = CreateFixture(concurrencyCap: 4);

		// Act
		CallToolResult status = await fixture.DispatchAsync(
			CompileStatusToolName, PollerMetadata(McpToolOperationFamily.ConfigurationBuild,
				McpToolSharedFileResource.ConfigurationBuild), EnvironmentName);

		// Assert
		status.IsError.Should().NotBeTrue(
			because: "with no sticky worker to reach, the poll must still be answered — from an empty registry, exactly as the in-process host answered it after a restart");
		fixture.Reservations.HeldCount.Should().Be(0,
			because: "a poller must never take the family's exclusion: a compile-status that reserved the configuration build would make the next compile-creatio refuse with 'a configuration build is already in progress' caused by its own status poll");
		fixture.StickyWorkers.Count.Should().Be(0,
			because: "the fallback is an ORDINARY per-call worker, so nothing may be left registered for a later poll to reach");
	}

	// ---------------------------------------------------------------------------------------------
	// AC-02 — the private completion signal, and the consequence of not having one
	// ---------------------------------------------------------------------------------------------

	[Test]
	[Category("Unit")]
	[Description("A sticky worker of a family with NO operation registry is reaped when it sends the private completion signal: the registry entry goes, the admission slot comes back, and the next long operation is admitted rather than refused for want of capacity.")]
	public async Task DispatchAsync_ShouldReapTheStickyWorkerAndReturnItsSlot_WhenTheWorkerSignalsCompletion() {
		// Arrange — capacity for exactly one long operation, so "the slot came back" is observable as the
		// next one being admitted rather than as a number nobody consumes. install-process-builder is the
		// family member chosen deliberately: it has no operation registry at all, so a terminal status is
		// not available to reap on even in principle.
		using StickyFixture fixture = CreateFixture(concurrencyCap: 2);
		CallToolResult started = await fixture.DispatchAsync(
			InstallProcessBuilderToolName, StarterMetadata(McpToolOperationFamily.ConfigurationBuild,
				McpToolSharedFileResource.ConfigurationBuild), EnvironmentName);
		fixture.StickyWorkers.Count.Should().Be(1,
			because: "the arrangement is only meaningful if the worker really was retained; a starter that reaped itself would make the assertions below vacuous");

		// Act — the worker says its detached work has finished, on the private channel.
		await fixture.Children[0].SendCompletionSignalAsync(McpToolOperationFamily.ConfigurationBuild,
			exitCode: 0);
		// Gated on the SLOT, not on the reservation, and the difference is a race rather than a
		// preference. The reservation is released the instant completion is recorded, while the process,
		// the session and the admission slot go on a sweep the registry schedules for itself — so a second
		// starter issued on the reservation's signal can ask for the sticky slot while that sweep is still
		// giving it back, and be refused for want of capacity by a worker that is already being reaped.
		// The wait is bounded like every other here, so a reap that never happens still fails the case.
		await WaitUntilAsync(() => fixture.Reservations.HeldCount == 0
			&& fixture.Supervisor.GetSnapshot().ActiveStickyWorkers == 0);
		CallToolResult second = await fixture.DispatchAsync(
			InstallProcessBuilderToolName, StarterMetadata(McpToolOperationFamily.ConfigurationBuild,
				McpToolSharedFileResource.ConfigurationBuild), EnvironmentName);

		// Assert
		started.IsError.Should().NotBeTrue(
			because: "the starting call must have been answered normally, or the reap below would be reaping a worker that failed for an unrelated reason");
		fixture.StickyWorkers.Count.Should().Be(1,
			because: "the second long operation registered its own worker, which is only possible if the first one's entry was really removed rather than merely marked");
		second.IsError.Should().NotBeTrue(
			because: "the slot the first worker held must have come back: without the reap this call is refused for want of long-operation capacity, and THAT is the failure the completion signal exists to prevent");
		fixture.Containment.LaunchCount.Should().Be(2,
			because: "the second call had to create a NEW worker — reaching a reaped one would mean the registry still had it");
	}

	[Test]
	[Category("Unit")]
	[Description("The private completion signal is consumed by the parent and never forwarded to the real MCP client: it is clio's own process plumbing, and putting it on a client's notification stream would be a contract change nobody asked for.")]
	public async Task DispatchAsync_ShouldNotForwardThePrivateCompletionSignalToTheClient() {
		// Arrange
		using StickyFixture fixture = CreateFixture(concurrencyCap: 2);
		await fixture.DispatchAsync(
			InstallProcessBuilderToolName, StarterMetadata(McpToolOperationFamily.ConfigurationBuild,
				McpToolSharedFileResource.ConfigurationBuild), EnvironmentName);

		// Act
		await fixture.Children[0].SendCompletionSignalAsync(McpToolOperationFamily.ConfigurationBuild,
			exitCode: 0);
		await WaitUntilAsync(() => fixture.Reservations.HeldCount == 0);

		// Assert
		fixture.Client.ForwardedMethods.Should().NotContain(
			WorkerOperationSignalContract.NotificationMethod,
			because: "rule 5 calls this a PRIVATE signal; a client that received clio's internal reap notifications would be reading plumbing it has no contract for");
	}

	[Test]
	[Category("Unit")]
	[Description("The wire contract of the private completion signal round-trips, and its method name and property names are pinned as literals so a rename is a deliberate change rather than a silently agreeing round trip.")]
	public void WorkerOperationSignalContract_ShouldRoundTripAndKeepItsPinnedWireNames() {
		// Arrange
		WorkerOperationCompletedParams built =
			WorkerOperationSignalContract.BuildParams(McpToolOperationFamily.Restart, exitCode: 3);
		JsonNode wire = JsonSerializer.SerializeToNode(built);
		JsonRpcNotification notification = new() {
			Method = WorkerOperationSignalContract.NotificationMethod,
			Params = wire
		};

		// Act
		bool read = WorkerOperationSignalContract.TryRead(notification,
			out McpToolOperationFamily family, out int? exitCode);
		bool readOther = WorkerOperationSignalContract.TryRead(
			new JsonRpcNotification { Method = "notifications/progress" }, out _, out _);

		// Assert
		JsonNode sdkWire = JsonSerializer.SerializeToNode(built,
			ModelContextProtocol.McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(WorkerOperationCompletedParams)));
		sdkWire.AsObject().Should().ContainKey("operation-family",
			because: "the worker sends this THROUGH the SDK, which serialises with its own options — a payload that only round-trips under System.Text.Json's defaults would go out unreadable and the send failure is swallowed, so the worker would never be reaped until its lifetime bound");
		sdkWire.AsObject().Should().ContainKey("exit-code",
			because: "same reason: the wire shape that matters is the one the SDK produces, not the one a test produced for itself");
		WorkerOperationSignalContract.NotificationMethod.Should()
			.Be("notifications/clio/worker-operation-completed",
				because: "the method name is a cross-process contract between a clio parent and a clio child of a possibly different build, so it is pinned here as a literal rather than compared to the constant the producer also reads");
		wire.AsObject().Should().ContainKey("operation-family",
			because: "the parent reads this property by name off a raw JsonNode, so a renamed JSON property would leave the signal unreadable while every C# call site still compiled");
		wire.AsObject().Should().ContainKey("exit-code",
			because: "same reason: the payload is parsed as JSON on the other side, not deserialised into this record");
		read.Should().BeTrue(because: "the notification the child builds must be the one the parent recognises");
		family.Should().Be(McpToolOperationFamily.Restart,
			because: "the family crosses as its enum NAME, so a reordered enum cannot change which family a signal reports");
		exitCode.Should().Be(3, because: "the exit code must survive the round trip for the parent's log line to be true");
		readOther.Should().BeFalse(
			because: "an ordinary progress notification must not be mistaken for a completion signal, or every streaming worker would reap itself mid-operation");
	}

	// ---------------------------------------------------------------------------------------------
	// AC-03 — the parent-owned configuration-build reservation
	// ---------------------------------------------------------------------------------------------

	[Test]
	[Category("Unit")]
	[Description("A running compile excludes install-process-builder on the same environment even though it runs in a different worker process, and the refusal names the shared configuration build rather than reading as a relay failure.")]
	public async Task DispatchAsync_ShouldRefuseInstallProcessBuilder_WhileACompileHoldsTheTargetsConfigurationBuild() {
		// Arrange — capacity for two long operations, so a refusal below cannot be admission capacity
		// wearing the reservation's clothes.
		using StickyFixture fixture = CreateFixture(concurrencyCap: 4);
		await fixture.DispatchAsync(
			CompileToolName, StarterMetadata(McpToolOperationFamily.ConfigurationBuild,
				McpToolSharedFileResource.ConfigurationBuild), EnvironmentName);

		// Act
		CallToolResult install = await fixture.DispatchAsync(
			InstallProcessBuilderToolName, StarterMetadata(McpToolOperationFamily.ConfigurationBuild,
				McpToolSharedFileResource.ConfigurationBuild), EnvironmentName);

		// Assert
		fixture.Supervisor.GetSnapshot().StickyConcurrencyCap.Should().BeGreaterThan(1,
			because: "the host must have room for a second long operation, or this test would pass on exhausted capacity and prove nothing about the reservation");
		install.IsError.Should().BeTrue(
			because: "two concurrent configuration builds on one environment corrupt each other's package compilation state, so the second must be refused rather than started");
		ReadErrorClass(install).Should().Be(McpWorkerCallDispatcher.SharedResourceBusyErrorClass,
			because: "the caller needs to know it was refused by a BUSY ENVIRONMENT: reporting a relay failure would send an agent hunting a clio bug, and a timeout class would send it into a retry loop");
		fixture.Containment.LaunchCount.Should().Be(1,
			because: "a refused call must issue nothing — no second worker, and therefore no second request to Creatio");
	}

	[Test]
	[Category("Unit")]
	[Description("Two DIFFERENT principals compiling ONE environment exclude each other through the dispatcher: the reservation is keyed off the normalised target, so a key that carried the principal — the tenant key — would admit both and let them corrupt each other's package compilation state.")]
	public async Task DispatchAsync_ShouldExcludeASecondPrincipal_WhenBothCompileTheSameTarget() {
		// Arrange — two callers whose TENANT keys differ (different credentials) but whose TARGET is the
		// same environment. That divergence is the whole point: it is what a reservation keyed off the
		// tenant key would not notice.
		_commandResolver.GetTenantKey(Arg.Any<EnvironmentOptions>())
			.Returns(call => $"tenant|{call.Arg<EnvironmentOptions>()?.Environment}");
		_commandResolver.GetTargetKey(Arg.Any<EnvironmentOptions>())
			.Returns("target|https://sandbox.creatio.com");
		using StickyFixture fixture = CreateFixture(concurrencyCap: 4);
		CallToolResult firstPrincipal = await fixture.DispatchAsync(
			CompileToolName, StarterMetadata(McpToolOperationFamily.ConfigurationBuild,
				McpToolSharedFileResource.ConfigurationBuild), "sandbox-as-supervisor");

		// Act
		CallToolResult secondPrincipal = await fixture.DispatchAsync(
			CompileToolName, StarterMetadata(McpToolOperationFamily.ConfigurationBuild,
				McpToolSharedFileResource.ConfigurationBuild), "sandbox-as-someone-else");

		// Assert
		firstPrincipal.IsError.Should().NotBeTrue(
			because: "the first compile must have been admitted, or the refusal below would be measuring something other than the exclusion");
		fixture.Supervisor.GetSnapshot().StickyConcurrencyCap.Should().BeGreaterThan(1,
			because: "the host must have capacity for a second long operation, or a capacity refusal would masquerade as the exclusion");
		secondPrincipal.IsError.Should().BeTrue(
			because: "Creatio's configuration build is SERVER-WIDE: keying the exclusion by the tenant key (principal + target + credential fingerprint) would give these two callers separate reservations and let both compile the same environment at once");
		ReadErrorClass(secondPrincipal).Should().Be(McpWorkerCallDispatcher.SharedResourceBusyErrorClass,
			because: "the second principal is refused BY THE ENVIRONMENT being busy, which is a different and more actionable statement than a relay failure or a timeout");
		fixture.Reservations.HeldCount.Should().Be(1,
			because: "exactly one reservation may exist for one target no matter how many principals ask for it");
	}

	[Test]
	[Category("Unit")]
	[Description("The configuration-build exclusion is keyed by normalised target and resource ONLY: two different principals on one environment exclude each other, while the same principal on two environments does not.")]
	public void TryReserve_ShouldExcludeAcrossPrincipalsAndOnlyWithinOneTarget() {
		// Arrange — the exclusion is handed target keys, never tenant keys. Two principals on one
		// environment produce ONE target key by construction; that is the property under test.
		SharedResourceReservation sut = new();
		const string environmentTarget = "https://sandbox.creatio.com";
		const string otherTarget = "https://other.creatio.com";

		// Act
		bool firstPrincipal = sut.TryReserve(McpToolSharedFileResource.ConfigurationBuild,
			environmentTarget, out SharedResourceReservationToken held);
		bool secondPrincipal = sut.TryReserve(McpToolSharedFileResource.ConfigurationBuild,
			environmentTarget, out SharedResourceReservationToken _);
		bool otherEnvironment = sut.TryReserve(McpToolSharedFileResource.ConfigurationBuild,
			otherTarget, out SharedResourceReservationToken _);
		bool otherResource = sut.TryReserve(McpToolSharedFileResource.ClioPages,
			environmentTarget, out SharedResourceReservationToken _);
		bool releasedByStranger = sut.Release(
			new SharedResourceReservationToken(held.ExclusionKey, held.Token + 1, held.StartedAtMs));
		bool releasedByOwner = sut.Release(held);
		bool afterRelease = sut.TryReserve(McpToolSharedFileResource.ConfigurationBuild,
			environmentTarget, out SharedResourceReservationToken _);

		// Assert
		firstPrincipal.Should().BeTrue(because: "an unheld target must admit the first configuration build");
		secondPrincipal.Should().BeFalse(
			because: "Creatio's configuration build is server-wide, so a key carrying the principal would let two callers compile one environment concurrently — the exact corruption this reservation exists to prevent");
		otherEnvironment.Should().BeTrue(
			because: "keying by target must not serialise UNRELATED environments: a global lock would make one slow compile deny every other stand");
		otherResource.Should().BeTrue(
			because: "the resource is part of the key, so two different shared resources on one target are independent rather than accidentally mutually exclusive");
		releasedByStranger.Should().BeFalse(
			because: "release is ownership-aware; without that, a superseded holder returning late would delete a reclaimer's live reservation and switch the guard off for that target");
		releasedByOwner.Should().BeTrue(because: "the holder's own token must free the key");
		afterRelease.Should().BeTrue(because: "a released target must admit the next configuration build");
	}

	[Test]
	[Category("Unit")]
	[Description("A reservation held past its reclaim ceiling is taken over by the next caller, so a holder that can never release cannot deny an environment for the life of the MCP host — and the ceiling is therefore the maximum time one stuck build can hold a target.")]
	public void TryReserve_ShouldReclaim_WhenTheHolderIsPastTheCeiling() {
		// Arrange — an explicit, tiny ceiling rather than a back-dated entry: the ceiling is constructor
		// state here, so nothing global is mutated and no test can leave it wrong for the next one.
		SharedResourceReservation sut = new(TimeSpan.FromMilliseconds(1));
		SharedResourceReservation neverReclaims = new(TimeSpan.FromHours(1));
		const string target = "https://sandbox.creatio.com";
		sut.TryReserve(McpToolSharedFileResource.ConfigurationBuild, target,
			out SharedResourceReservationToken stalled);
		neverReclaims.TryReserve(McpToolSharedFileResource.ConfigurationBuild, target,
			out SharedResourceReservationToken _);
		Thread.Sleep(20);

		// Act
		bool reclaimed = sut.TryReserve(McpToolSharedFileResource.ConfigurationBuild, target,
			out SharedResourceReservationToken reclaimer);
		bool thirdCaller = sut.TryReserve(McpToolSharedFileResource.ConfigurationBuild, target,
			out SharedResourceReservationToken _);
		bool stalledReleaseTookEffect = sut.Release(stalled);
		bool withinCeiling = neverReclaims.TryReserve(McpToolSharedFileResource.ConfigurationBuild,
			target, out SharedResourceReservationToken _);

		// Assert
		reclaimed.Should().BeTrue(
			because: "past the ceiling the slot must be reclaimable, or a holder whose release never runs — a target that accepted an unbounded install POST and then went silent — wedges that environment for the life of the host");
		reclaimer.Token.Should().NotBe(stalled.Token,
			because: "ownership must move to the reclaimer, and a monotonic counter is what makes that decidable — a clock-derived stamp would collide inside one Windows tick");
		thirdCaller.Should().BeFalse(
			because: "the reclaimer now holds the target: a reclaim must not leave the key open, or the reclaimed build would run alongside a third one");
		stalledReleaseTookEffect.Should().BeFalse(
			because: "the original holder returning late must NOT delete the reclaimer's reservation — an unconditional remove there switches the guard off for that target after a single reclaim");
		withinCeiling.Should().BeFalse(
			because: "a holder inside its ceiling is honoured; without this the reclaim above would prove only that the dictionary forgets, not that the ceiling decides");
	}

	// ---------------------------------------------------------------------------------------------
	// AC-04 — sticky lifetime bounded by credential validity, with an explicit maximum
	// ---------------------------------------------------------------------------------------------

	[Test]
	[Category("Unit")]
	[Description("A sticky worker's lifetime is the earlier of an explicit maximum and the credential's own validity, and that maximum never exceeds the configuration-build reclaim ceiling, so a worker can never outlive the reservation it holds.")]
	public void Resolve_ShouldBoundStickyLifetimeByTheEarlierOfTheMaximumAndTheCredential() {
		// Arrange
		DateTimeOffset spawnedAt = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

		// Act
		DateTimeOffset unknownCredential = StickyWorkerLifetimeBound.Resolve(spawnedAt, null);
		DateTimeOffset shortCredential =
			StickyWorkerLifetimeBound.Resolve(spawnedAt, spawnedAt.AddMinutes(5));
		DateTimeOffset longCredential =
			StickyWorkerLifetimeBound.Resolve(spawnedAt, spawnedAt.AddDays(1));
		DateTimeOffset expiredCredential =
			StickyWorkerLifetimeBound.Resolve(spawnedAt, spawnedAt.AddMinutes(-1));

		// Assert
		unknownCredential.Should().Be(spawnedAt + StickyWorkerLifetimeBound.ExplicitMaximum,
			because: "on stdio no credential crosses the boundary at all, so T-8's 'where validity is unknown, an explicit maximum applies' IS the whole bound rather than a fallback");
		shortCredential.Should().Be(spawnedAt.AddMinutes(5),
			because: "a credential that expires before the maximum must shorten the worker's life, or work would continue under revoked authority — the threat stickiness creates and per-call workers do not have");
		longCredential.Should().Be(spawnedAt + StickyWorkerLifetimeBound.ExplicitMaximum,
			because: "a long-lived credential must not extend a worker past the explicit maximum, or the maximum would be advisory");
		expiredCredential.Should().BeOnOrBefore(spawnedAt,
			because: "an already-expired credential must fail closed; granting the full maximum on the strength of an expired token is the same bug in a friendlier shape");
		StickyWorkerLifetimeBound.ExplicitMaximum.Should()
			.BeLessThanOrEqualTo(new SharedResourceReservation().ReclaimCeiling,
				because: "a sticky worker of the configuration-build family holds a reservation for its whole life, so a maximum ABOVE the reclaim ceiling would let a second caller reclaim that reservation and start a build alongside the first — the corruption the reservation exists to prevent, reached by way of the bound meant to contain it");
	}

	[Test]
	[Category("Unit")]
	[Description("A sticky worker past its lifetime bound is neither reachable nor retained: it is reaped, while a worker still inside its bound survives the same sweep — so the bound is a lifetime rather than a periodic cull.")]
	public async Task ReapExpiredAsync_ShouldRetireAStickyWorkerPastItsLifetimeBound() {
		// Arrange — two workers, each with its OWN process: the expired entry is built over the SECOND
		// worker's lease and session, so reaping it cannot take the first one down with it. The expiry is
		// stated on the entry rather than waited out; the real maximum is half an hour by derivation and
		// waiting for it would make this a timing test rather than a behaviour test.
		using StickyFixture fixture = CreateFixture(concurrencyCap: 8);
		await fixture.DispatchAsync(
			InstallProcessBuilderToolName, StarterMetadata(McpToolOperationFamily.ConfigurationBuild,
				McpToolSharedFileResource.ConfigurationBuild), EnvironmentName);
		await fixture.DispatchAsync(
			"restart-by-environment-name", StarterMetadata(McpToolOperationFamily.Restart,
				McpToolSharedFileResource.None), "doomed-environment");
		StickyWorkerKey survivorKey =
			new(McpToolOperationFamily.ConfigurationBuild, $"tenant|{EnvironmentName}");
		StickyWorkerKey doomedKey = new(McpToolOperationFamily.Restart, "tenant|doomed-environment");
		fixture.StickyWorkers.TryReach(survivorKey, out StickyWorkerEntry _).Should().BeTrue(
			because: "the arrangement must start from two reachable workers, or 'expired' and 'never registered' would be indistinguishable below");
		fixture.StickyWorkers.TryReach(doomedKey, out StickyWorkerEntry doomed).Should().BeTrue();
		StickyWorkerKey expiredKey = new(McpToolOperationFamily.AppSectionCreate, "tenant|expired");
		fixture.StickyWorkers.TryRegister(expiredKey, fixture.CreateExpiredEntryFrom(doomed))
			.Should().BeTrue();

		// Act
		int reaped = await fixture.StickyWorkers.ReapExpiredAsync();
		bool reachedAfterwards = fixture.StickyWorkers.TryReach(expiredKey, out StickyWorkerEntry _);

		// Assert
		reaped.Should().Be(1,
			because: "exactly the entry past its bound must be retired — reaping a worker inside its bound would end a running operation, and reaping none would leave a worker holding a slot and a reservation past the bound that exists to stop that");
		reachedAfterwards.Should().BeFalse(
			because: "a worker past its lifetime bound must be unreachable: answering a poll from it would be answering out of a session the bound declared over");
		fixture.StickyWorkers.TryReach(survivorKey, out StickyWorkerEntry _).Should().BeTrue(
			because: "a worker inside its bound must survive the sweep, or the bound would be a periodic cull rather than a lifetime");
	}

	// ---------------------------------------------------------------------------------------------
	// AC-04, the other half: the bound must take effect at the DEADLINE, with nobody calling
	// ---------------------------------------------------------------------------------------------

	[Test]
	[Category("Unit")]
	[Description("A sticky worker past its lifetime bound is reaped at the deadline with no further sticky dispatch of any kind, and the admission slot it was holding comes back — otherwise the bound only takes effect if more sticky traffic happens to arrive, and one idle expired worker denies every later long operation on a host whose sticky capacity is one.")]
	public async Task DeadlineTimer_ShouldReapAnExpiredStickyWorkerAndReturnItsSlot_WhenNoFurtherDispatchArrives() {
		// Arrange — a total cap of 2 puts the sticky ceiling at 1, so ONE long operation saturates the
		// sticky pool and the slot under test is the only one there is. The clock is offset-based: it
		// tracks real time and moves only when this test moves it, so nothing here waits out a bound that
		// is half an hour by derivation.
		ControllableClock clock = new();
		using StickyFixture fixture = CreateFixture(concurrencyCap: 2, clock: clock);
		await fixture.DispatchAsync(
			CompileToolName, StarterMetadata(McpToolOperationFamily.ConfigurationBuild,
				McpToolSharedFileResource.ConfigurationBuild), EnvironmentName);
		WorkerSupervisorSnapshot beforeDeadline = fixture.Supervisor.GetSnapshot();
		beforeDeadline.ActiveStickyWorkers.Should().Be(beforeDeadline.StickyConcurrencyCap,
			because: "the case only discriminates with the sticky pool SATURATED: on a host with a spare slot the next long operation succeeds whether or not the expired worker was ever reaped");

		// Act — the deadline passes and NOBODY calls. No poll, no second starter, no dispatch at all:
		// this is exactly the idle host the defect strands, and the only thing that can act on it is the
		// registry itself.
		clock.Advance(StickyWorkerLifetimeBound.ExplicitMaximum + TimeSpan.FromMinutes(1));
		await WaitUntilAsync(() => fixture.Supervisor.GetSnapshot().ActiveStickyWorkers == 0);
		WorkerSupervisorSnapshot afterDeadline = fixture.Supervisor.GetSnapshot();
		// Read BEFORE the proof below dispatches: that dispatch registers a worker of its own, so the
		// count is only about the reaped one while it is the only thing that ever registered.
		int supervisedAfterDeadline = fixture.StickyWorkers.Count;
		CallToolResult nextLongOperation = await fixture.DispatchAsync(
			CompileToolName, StarterMetadata(McpToolOperationFamily.ConfigurationBuild,
				McpToolSharedFileResource.ConfigurationBuild), "another-environment");

		// Assert
		afterDeadline.ActiveStickyWorkers.Should().Be(0,
			because: "the ADMISSION SLOT is the resource a stranded worker holds, so the slot coming back is the property under test — a dictionary entry that merely vanished would leave the process, its authenticated session and its slot exactly where they were");
		supervisedAfterDeadline.Should().Be(0,
			because: "the registry must also stop pointing at a worker it has ended, or a later poll would be answered out of a session the bound declared over");
		fixture.Children[0].HasExited.Should().BeTrue(
			because: "reaping must end the PROCESS: a worker that lives past its bound goes on holding an authenticated Creatio session, which is the threat T-8 names and the reason the bound exists");
		nextLongOperation.IsError.Should().NotBeTrue(
			because: "the reclaimed slot must be genuinely reusable — a later long operation refused for want of capacity is what an unreaped worker looks like from outside");
	}

	[Test]
	[Category("Unit")]
	[Description("A completion signal moves a sticky worker's expiry DOWN to the linger window, and the deadline is re-armed to it: the worker is reaped when the linger ends, without any further dispatch, rather than holding its slot until the far-off hard bound.")]
	public async Task DeadlineTimer_ShouldReapAfterTheCompletionLinger_WhenNoFurtherDispatchArrives() {
		// Arrange — the linger is five minutes and the hard bound is half an hour, so a deadline armed
		// once at registration and never re-armed cannot fire inside the window this case advances
		// through. That gap is what makes the case discriminate re-arming from mere scheduling.
		ControllableClock clock = new();
		using StickyFixture fixture = CreateFixture(concurrencyCap: 2,
			completionLinger: TimeSpan.FromMinutes(5), clock: clock);
		await fixture.DispatchAsync(
			CompileToolName, StarterMetadata(McpToolOperationFamily.ConfigurationBuild,
				McpToolSharedFileResource.ConfigurationBuild), EnvironmentName);

		// Act — completion FIRST, then the clock. MarkCompleted stamps the linger against the real
		// clock, so advancing before the signal arrived would put the offset ahead of the stamp and the
		// worker would be born expired, proving nothing about re-arming.
		await fixture.Children[0].SendCompletionSignalAsync(McpToolOperationFamily.ConfigurationBuild,
			exitCode: 0);
		await WaitUntilAsync(() => fixture.Reservations.HeldCount == 0);
		clock.Advance(TimeSpan.FromMinutes(6));
		await WaitUntilAsync(() => fixture.Supervisor.GetSnapshot().ActiveStickyWorkers == 0);

		// Assert
		fixture.Supervisor.GetSnapshot().ActiveStickyWorkers.Should().Be(0,
			because: "the linger is what the worker's remaining life IS once it has reported completion, so the deadline must have moved down with it — holding the slot for the rest of the half-hour bound is the same stranding the bound exists to prevent, just shorter");
		fixture.StickyWorkers.Count.Should().Be(0,
			because: "a lingering worker whose window has closed has nothing left to answer: the poll it was kept alive for either arrived or never will");
	}

	[Test]
	[Category("Unit")]
	[Description("The lifetime deadline is scheduled against the EARLIEST outstanding expiry: a worker registered with a nearer bound pulls the deadline in, and one registered with a later bound leaves it alone — so no entry waits behind another entry's expiry.")]
	public async Task TryRegister_ShouldArmTheDeadlineAgainstTheEarliestOutstandingExpiry() {
		// Arrange
		ControllableClock clock = new();
		SharedResourceReservation reservations = new();
		using StickyWorkerRegistry registry = new(_logger, clock);
		RegistryEntryProbe middle = await CreateRegistryEntryAsync(
			clock.GetUtcNow().AddMinutes(20), reservations);
		RegistryEntryProbe nearest = await CreateRegistryEntryAsync(
			clock.GetUtcNow().AddMinutes(5), reservations);
		RegistryEntryProbe furthest = await CreateRegistryEntryAsync(
			clock.GetUtcNow().AddMinutes(45), reservations);

		// Act
		registry.TryRegister(new StickyWorkerKey(McpToolOperationFamily.ConfigurationBuild, "tenant|a"),
			middle.Entry).Should().BeTrue();
		TimeSpan? armedForMiddle = clock.DeadlineTimer.ArmedDelay;
		registry.TryRegister(new StickyWorkerKey(McpToolOperationFamily.Restart, "tenant|b"),
			nearest.Entry).Should().BeTrue();
		TimeSpan? armedForNearest = clock.DeadlineTimer.ArmedDelay;
		registry.TryRegister(new StickyWorkerKey(McpToolOperationFamily.AppSectionCreate, "tenant|c"),
			furthest.Entry).Should().BeTrue();
		TimeSpan? armedForFurthest = clock.DeadlineTimer.ArmedDelay;

		// Assert
		armedForMiddle.Should().NotBeNull(
			because: "registering the first worker must arm the deadline at all: an unarmed registry reaps only when somebody happens to call, which is the defect");
		armedForMiddle!.Value.Should().BeCloseTo(TimeSpan.FromMinutes(20), TimeSpan.FromSeconds(5),
			because: "the only outstanding expiry is this worker's, so that is when the registry must next wake");
		armedForNearest!.Value.Should().BeCloseTo(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(5),
			because: "a nearer expiry must PULL the deadline in — a registry that kept the old one would leave the new worker holding its slot for the fifteen minutes between the two bounds");
		armedForFurthest!.Value.Should().BeCloseTo(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(5),
			because: "a later expiry must not push the deadline out, or one long-lived worker would postpone the reaping of every shorter-lived one behind it");

		// Cleanup — these entries were built for the schedule, not for a worker, so they are released
		// here rather than by a sweep.
		await middle.Entry.ReleaseAsync();
		await nearest.Entry.ReleaseAsync();
		await furthest.Entry.ReleaseAsync();
	}

	[TestCase(false)]
	[TestCase(true)]
	[Category("Unit")]
	[Description("A disposed registry runs no deadline callback: its timer is disposed and the deadline it was armed for passes with nothing reaped — a callback firing into a torn-down host is a crash on a thread with nobody above it to catch. Asserted on BOTH disposal paths, because which one the container takes is decided by how the host disposes its provider, not by this type.")]
	public async Task Dispose_ShouldStopTheDeadlineTimer_SoNoCallbackRunsAfterTheRegistryIsGone(
		bool disposeAsynchronously) {
		// Arrange
		ControllableClock clock = new();
		SharedResourceReservation reservations = new();
		StickyWorkerRegistry registry = new(_logger, clock);
		RegistryEntryProbe probe = await CreateRegistryEntryAsync(
			clock.GetUtcNow().AddMinutes(1), reservations);
		registry.TryRegister(new StickyWorkerKey(McpToolOperationFamily.ConfigurationBuild, "tenant|a"),
			probe.Entry).Should().BeTrue(
			because: "the deadline has to be armed before disposal, or 'nothing fired afterwards' would be true of a registry that never scheduled anything");
		clock.DeadlineTimer.ArmedDelay.Should().NotBeNull(
			because: "the same reason stated as an observation rather than an assumption");

		// Act
		if (disposeAsynchronously) {
			await registry.DisposeAsync();
		}
		else {
			registry.Dispose();
		}
		clock.Advance(TimeSpan.FromMinutes(2));
		// A callback that DID start would release on a background task, so settle briefly before
		// asserting a negative. The wait can only produce a false PASS, never a false failure, and the
		// structural assertion below is what carries the guarantee.
		await Task.Delay(100);

		// Assert
		clock.DeadlineTimer.IsDisposed.Should().BeTrue(
			because: "disposal must take the timer with it: that is what makes 'no callback after disposal' structural rather than a race the flag happens to win");
		probe.Disposals.Value.Should().Be(0,
			because: "a deadline that passes after the registry is gone must reap nothing — the entries belong to a host that is shutting down, and touching them there is work on a half-torn-down object graph");
		registry.Count.Should().Be(1,
			because: "the registry must be inert after disposal rather than quietly emptying itself, so what happened is decidable from outside");

		// Cleanup — nothing reaped this entry, by design, so this test owns its release.
		await probe.Entry.ReleaseAsync();
	}

	[Test]
	[Category("Unit")]
	[Description("Deadline-driven reaping stays scoped to the ENTRY: the sweep ends the worker past its bound and no other, and a stale reap for that same worker, arriving after a successor has taken its key, leaves the successor running.")]
	public async Task DeadlineTimer_ShouldReapOnlyTheExpiredEntry_WhenASuccessorTakesItsKey() {
		// Arrange
		ControllableClock clock = new();
		SharedResourceReservation reservations = new();
		using StickyWorkerRegistry registry = new(_logger, clock);
		StickyWorkerKey contestedKey =
			new(McpToolOperationFamily.ConfigurationBuild, $"tenant|{EnvironmentName}");
		StickyWorkerKey bystanderKey = new(McpToolOperationFamily.Restart, "tenant|another-environment");
		RegistryEntryProbe doomed = await CreateRegistryEntryAsync(
			clock.GetUtcNow().AddMinutes(1), reservations);
		RegistryEntryProbe bystander = await CreateRegistryEntryAsync(
			clock.GetUtcNow().AddMinutes(45), reservations);
		registry.TryRegister(contestedKey, doomed.Entry).Should().BeTrue();
		registry.TryRegister(bystanderKey, bystander.Entry).Should().BeTrue();

		// Act — the first worker's deadline passes with nobody calling, and the next operation on that
		// same target takes the key it vacated.
		clock.Advance(TimeSpan.FromMinutes(2));
		await WaitUntilAsync(() => doomed.Disposals.Value == 1);
		RegistryEntryProbe successor = await CreateRegistryEntryAsync(
			clock.GetUtcNow().AddMinutes(45), reservations);
		bool successorRegistered = registry.TryRegister(contestedKey, successor.Entry);
		// The stale reap a caller holding the FINISHED worker issues — a poll that decided about the old
		// entry before the sweep ran and acted on it afterwards.
		await registry.ReapAsync(contestedKey, doomed.Entry);

		// Assert
		successorRegistered.Should().BeTrue(
			because: "the sweep must free the KEY as well as the worker, or the next operation on that target would be refused by a registry still pointing at a worker it had already ended");
		registry.TryReach(contestedKey, out StickyWorkerEntry reached).Should().BeTrue(
			because: "the successor must remain reachable: a reap scoped to the key rather than to the entry would end whichever operation happened to hold it, which after a supersession is the NEW one");
		reached.Should().BeSameAs(successor.Entry,
			because: "and it must be the successor itself, so a later poll reaches the operation that is actually running");
		successor.Disposals.Value.Should().Be(0,
			because: "the damage a key-scoped reap does is not a dictionary edit but a killed operation, so the successor's PROCESS is the thing to state");
		bystander.Disposals.Value.Should().Be(0,
			because: "a worker inside its bound must survive a sweep aimed at another, or the deadline would be a periodic cull rather than a per-entry lifetime");
		doomed.Disposals.Value.Should().Be(1,
			because: "the expired worker must be released exactly once: the stale reap that followed must not double-release the entry it names");

		// Cleanup
		await successor.Entry.ReleaseAsync();
		await bystander.Entry.ReleaseAsync();
	}

	// ---------------------------------------------------------------------------------------------
	// Entry-scoped supervision: an unusable worker is reaped WHEN IT BECOMES UNUSABLE, not at its bound
	// ---------------------------------------------------------------------------------------------

	[Test]
	[Category("Unit")]
	[Description("A sticky worker whose PROCESS exits is reaped at once — with no further sticky dispatch and without waiting out its lifetime bound — and the admission slot it was holding comes back, so the next long operation on that host is admitted rather than refused for the sake of a worker that no longer exists.")]
	public async Task Supervision_ShouldReapAStickyWorkerAndReturnItsSlot_WhenItsProcessExits() {
		// Arrange — a total cap of 2 puts the sticky ceiling at 1, so ONE long operation saturates the
		// sticky pool and the slot under test is the only one there is. The clock is the REAL one on
		// purpose: this case must not be reachable by advancing time, because "reaped at its lifetime
		// bound" is exactly the behaviour it exists to distinguish itself from.
		using StickyFixture fixture = CreateFixture(concurrencyCap: 2);
		await fixture.DispatchAsync(
			CompileToolName, StarterMetadata(McpToolOperationFamily.ConfigurationBuild,
				McpToolSharedFileResource.ConfigurationBuild), EnvironmentName);
		WorkerSupervisorSnapshot beforeExit = fixture.Supervisor.GetSnapshot();
		beforeExit.ActiveStickyWorkers.Should().Be(beforeExit.StickyConcurrencyCap,
			because: "the case only discriminates with the sticky pool SATURATED: on a host with a spare slot the next long operation succeeds whether or not the dead worker was ever reaped");

		// Act — the worker's process ends and NOBODY calls: no poll, no second starter, no dispatch at
		// all, and no clock advanced anywhere. The pipes are deliberately left open, so the only thing
		// that changed is the PROCESS — a session torn down at the same moment would let this pass
		// through the retirement path instead.
		fixture.Children[0].SimulateProcessExit();
		await WaitUntilAsync(() => fixture.Supervisor.GetSnapshot().ActiveStickyWorkers == 0);
		WorkerSupervisorSnapshot afterExit = fixture.Supervisor.GetSnapshot();
		// Read BEFORE the proof below dispatches: that dispatch registers a worker of its own, so the
		// count is only about the reaped one while it is the only thing that ever registered.
		int supervisedAfterExit = fixture.StickyWorkers.Count;
		CallToolResult nextLongOperation = await fixture.DispatchAsync(
			CompileToolName, StarterMetadata(McpToolOperationFamily.ConfigurationBuild,
				McpToolSharedFileResource.ConfigurationBuild), "another-environment");

		// Assert
		afterExit.ActiveStickyWorkers.Should().Be(0,
			because: "the ADMISSION SLOT is the resource a dead worker goes on holding, so the slot coming back is the property under test — a dictionary entry that merely vanished would leave the shared semaphore consumed by a process that no longer exists");
		supervisedAfterExit.Should().Be(0,
			because: "the registry must stop pointing at a worker it has ended, or a later poll would be answered out of a session belonging to a dead process");
		nextLongOperation.IsError.Should().NotBeTrue(
			because: "the reclaimed slot must be genuinely reusable — a later long operation refused for want of capacity is what an unreaped dead worker looks like from outside, and on a host whose sticky ceiling is one that refusal lasts the whole half-hour bound");
	}

	[Test]
	[Category("Unit")]
	[Description("A sticky worker whose relay session is RETIRED while its process is still running is reaped by supervision alone, with no further dispatch and no lifetime deadline — a live process nothing can say anything to must not go on holding an admission slot and a shared-resource reservation.")]
	public async Task Supervision_ShouldReapAStickyWorker_WhenItsSessionIsRetiredWhileItsProcessRuns() {
		// Arrange — the cadence is STATED (50 ms) rather than inherited, so this case cannot depend on the
		// shipped fifteen-second interval. It cannot flake either: nothing here asserts how FAST the reap
		// happened, only that it happened without a dispatch and without the half-hour bound, and the wait
		// below is bounded by AssertionTimeout, six hundred times the cadence.
		using StickyFixture fixture = CreateFixture(concurrencyCap: 2,
			supervisionInterval: FastSupervisionInterval);
		await fixture.DispatchAsync(
			CompileToolName, StarterMetadata(McpToolOperationFamily.ConfigurationBuild,
				McpToolSharedFileResource.ConfigurationBuild), EnvironmentName);
		StickyWorkerKey key = new(McpToolOperationFamily.ConfigurationBuild, $"tenant|{EnvironmentName}");
		fixture.StickyWorkers.TryReach(key, out StickyWorkerEntry entry).Should().BeTrue(
			because: "the arrangement must start from a reachable worker, or 'unreachable afterwards' and 'never registered' would be indistinguishable");
		fixture.Reservations.HeldCount.Should().Be(1,
			because: "the running compile holds its target's reservation, which is the second thing an unreachable worker would go on denying the environment");

		// Act — the SESSION is retired and the process is left running. This is the state ADR 3.2a
		// describes: a worker that is alive and permanently unreachable, which no process-exit event can
		// ever report.
		// Read BEFORE the session is retired, never after: the reap this case waits for ends the process
		// itself, so a read taken afterwards would be racing the very supervision under test and would
		// fail on a correct implementation. Nothing else can have ended this child — the scripted worker
		// exits only through Kill, Dispose or SimulateProcessExit, none of which this case calls — so the
		// pre-read states exactly the property: alive at the moment its session was retired.
		bool processRunningWhenRetired = !fixture.Children[0].HasExited;
		await entry.Session.DisposeAsync();
		await WaitUntilAsync(() => fixture.Supervisor.GetSnapshot().ActiveStickyWorkers == 0);

		// Assert
		processRunningWhenRetired.Should().BeTrue(
			because: "the case is about a session RETIRED while its process ran on: if the process had already gone, the exit watcher would have carried this and the retirement half would be untested");
		fixture.Supervisor.GetSnapshot().ActiveStickyWorkers.Should().Be(0,
			because: "a worker nothing can be said to must give its admission slot back when it becomes unreachable, not half an hour later at its lifetime bound");
		fixture.StickyWorkers.Count.Should().Be(0,
			because: "reaching such a worker could only hand the next poll a transport that will never be written to again");
		fixture.Reservations.HeldCount.Should().Be(0,
			because: "the reservation is released as part of the reap, so an environment is not left denied by a worker that can no longer be talked to");
	}

	[Test]
	[Category("Unit")]
	[Description("An entry already reaped for another reason leaves no watcher behind: when that worker's process later ends, nothing is released a second time and the SUCCESSOR holding its key is untouched — a stray watcher would end the operation that replaced the one it was watching.")]
	public async Task Supervision_ShouldLeaveNoWatcherBehind_WhenTheEntryWasAlreadyReaped() {
		// Arrange — three entries, each over its own lease, and a clock the test moves. The cadence is the
		// SHIPPED one here: on a controllable clock a supervision pass happens only when this test says
		// so, which is what makes "nothing looked at the reaped worker" a fact rather than a race against
		// a real timer. The canary makes the negatives mean something: it is registered live and then made
		// dead, so waiting for IT to be reaped proves the pass this test triggered really ran.
		ControllableClock clock = new();
		SharedResourceReservation reservations = new();
		using StickyWorkerRegistry registry = new(_logger, clock);
		StickyWorkerKey contestedKey =
			new(McpToolOperationFamily.ConfigurationBuild, $"tenant|{EnvironmentName}");
		StickyWorkerKey canaryKey = new(McpToolOperationFamily.Restart, "tenant|canary-environment");
		RegistryEntryProbe doomed = await CreateRegistryEntryAsync(
			DateTimeOffset.UtcNow.AddMinutes(45), reservations);
		RegistryEntryProbe successor = await CreateRegistryEntryAsync(
			DateTimeOffset.UtcNow.AddMinutes(45), reservations);
		RegistryEntryProbe canary = await CreateRegistryEntryAsync(
			DateTimeOffset.UtcNow.AddMinutes(45), reservations);
		registry.TryRegister(contestedKey, doomed.Entry).Should().BeTrue();
		// PARKED before it is reaped, and that ordering is the whole arrangement. A watcher is started on a
		// pool thread; reaping before its first pass would leave it with no cadence timer at all, and the
		// stray pass this case looks for would then have happened BEFORE the count is taken. The clock
		// publishes a timer only once it is armed, so two timers — the lifetime deadline and this
		// watcher — means the watcher is waiting.
		await WaitUntilAsync(() => clock.TimerCount >= 2);
		registry.TryRegister(canaryKey, canary.Entry).Should().BeTrue();
		await WaitUntilAsync(() => clock.TimerCount >= 3);

		// Act — the first worker is reaped for a DIFFERENT reason (a poll ended it), its key is taken by
		// the next operation on the same target, and only then does the reaped worker's process end.
		await registry.ReapAsync(contestedKey, doomed.Entry);
		registry.TryRegister(contestedKey, successor.Entry).Should().BeTrue(
			because: "the reap must free the key, or the successor could not take it and the stray-watcher question would never arise");
		await WaitUntilAsync(() => clock.TimerCount >= 4);
		Volatile.Write(ref doomed.Exited.Value, true);
		Volatile.Write(ref canary.Exited.Value, true);
		// Read while NO pass can be in flight — the clock has not moved since the reap — so anything
		// counted beyond it belongs to the pass triggered below. That trace is what the case turns on:
		// reaping an entry that was already reaped is absorbed by an idempotent release and would prove
		// nothing either way.
		int doomedLooksBefore = doomed.LivenessReads.Value;
		clock.Advance(StickyWorkerRegistry.DefaultSupervisionInterval + TimeSpan.FromSeconds(1));
		// The canary is the gate rather than a sleep: it proves the pass happened at all, which a bare
		// delay on a loaded agent cannot. The settle that follows is only there so a stray watcher woken
		// by the SAME advance has finished its (much shorter) pass before the count is read.
		await WaitUntilAsync(() => canary.Disposals.Value == 1);
		await Task.Delay(250);

		// Assert
		canary.Disposals.Value.Should().Be(1,
			because: "the control has to fire, or every negative below would be true of a registry whose supervision never ran");
		doomed.LivenessReads.Value.Should().Be(doomedLooksBefore,
			because: "a watcher torn down with its entry stops looking; one left behind goes on reading the lease of a worker nobody owns for as long as the host lives, and that read is the only trace it leaves");
		doomed.Disposals.Value.Should().Be(1,
			because: "the entry was released once by the reap that ended it, and exactly once is what an idempotent release plus a torn-down watcher produce together");
		successor.Disposals.Value.Should().Be(0,
			because: "the damage a stray watcher does is not a dictionary edit but a killed operation — the successor holds the contested key, and a reap that was not entry-scoped would end the operation that had just started");
		registry.TryReach(contestedKey, out StickyWorkerEntry reached).Should().BeTrue(
			because: "the successor must still be reachable so a later poll answers out of the process actually running the operation");
		reached.Should().BeSameAs(successor.Entry,
			because: "and it must be the successor itself rather than a resurrected predecessor");

		// Cleanup — the successor was never reaped, by design, so this test owns its release.
		await successor.Entry.ReleaseAsync();
	}

	[Test]
	[Category("Unit")]
	[Description("A registry disposed while a watcher is pending reaps nothing when that worker later dies: the watchers are cancelled with the deadline timer and every pass re-tests the disposal flag, so no callback touches a half-torn-down object graph — while the same arrangement on a LIVE registry reaps at once, which is what makes this about disposal rather than about a watcher that never ran.")]
	public async Task Supervision_ShouldReapNothing_WhenTheRegistryWasDisposedWhileAWatcherWasPending() {
		// Arrange — two registries with the same arrangement, one disposed and one not. The live one is
		// the control: without it, a green negative would also be produced by supervision that was simply
		// not running.
		SharedResourceReservation reservations = new();
		StickyWorkerRegistry disposedRegistry = new(_logger, timeProvider: null,
			supervisionInterval: FastSupervisionInterval);
		using StickyWorkerRegistry liveRegistry = new(_logger, timeProvider: null,
			supervisionInterval: FastSupervisionInterval);
		StickyWorkerKey key = new(McpToolOperationFamily.ConfigurationBuild, $"tenant|{EnvironmentName}");
		RegistryEntryProbe orphaned = await CreateRegistryEntryAsync(
			DateTimeOffset.UtcNow.AddMinutes(45), reservations);
		RegistryEntryProbe control = await CreateRegistryEntryAsync(
			DateTimeOffset.UtcNow.AddMinutes(45), reservations);
		disposedRegistry.TryRegister(key, orphaned.Entry).Should().BeTrue(
			because: "a watcher has to be pending before disposal, or 'nothing fired afterwards' would be true of a registry that never watched anything");
		liveRegistry.TryRegister(key, control.Entry).Should().BeTrue();

		// Act — the host is torn down, and only then do both workers die.
		await disposedRegistry.DisposeAsync();
		Volatile.Write(ref orphaned.Exited.Value, true);
		Volatile.Write(ref control.Exited.Value, true);
		await WaitUntilAsync(() => control.Disposals.Value == 1);
		await Task.Delay(FastSupervisionInterval + FastSupervisionInterval);

		// Assert
		control.Disposals.Value.Should().Be(1,
			because: "the live registry must reap the same arrangement, or the negative below would say nothing about disposal");
		orphaned.Disposals.Value.Should().Be(0,
			because: "a watcher that acted after disposal would be working on an object graph the host has already torn down — the entries belong to a shutdown path, and reaping them is deliberately not what disposing a registry does");
		disposedRegistry.Count.Should().Be(1,
			because: "the disposed registry must be inert rather than quietly emptying itself, so what happened is decidable from outside");

		// Cleanup — nothing reaped the orphaned entry, by design, so this test owns its release.
		await orphaned.Entry.ReleaseAsync();
	}

	[Test]
	[Category("Unit")]
	[Description("A worker that reported completion stays reachable for the linger window, so the status poll the caller was told to make still reaches the process holding the operation record — while the target's configuration-build reservation is released at once, because a finished build must stop denying its environment immediately.")]
	public async Task DispatchAsync_ShouldKeepAnsweringStatusPolls_DuringTheCompletionLinger() {
		// Arrange — a linger long enough that the poll below falls inside it. On stdio the compile
		// registry is a DI singleton INSIDE the worker, so reaping at once would answer this poll with
		// "no such operation" for an operation that had just finished.
		using StickyFixture fixture = CreateFixture(concurrencyCap: 4,
			completionLinger: TimeSpan.FromMinutes(5));
		await fixture.DispatchAsync(
			CompileToolName, StarterMetadata(McpToolOperationFamily.ConfigurationBuild,
				McpToolSharedFileResource.ConfigurationBuild), EnvironmentName);
		fixture.Reservations.HeldCount.Should().Be(1,
			because: "the running compile must hold its target's reservation, or the release asserted below would be releasing nothing");

		// Act
		await fixture.Children[0].SendCompletionSignalAsync(McpToolOperationFamily.ConfigurationBuild,
			exitCode: 0);
		await WaitUntilAsync(() => fixture.Reservations.HeldCount == 0);
		CallToolResult status = await fixture.DispatchAsync(
			CompileStatusToolName, PollerMetadata(McpToolOperationFamily.ConfigurationBuild,
				McpToolSharedFileResource.ConfigurationBuild), EnvironmentName);

		// Assert
		fixture.Reservations.HeldCount.Should().Be(0,
			because: "the reservation goes the moment the build finishes — holding it through the linger would keep an environment denied for minutes after the work that justified it ended");
		status.IsError.Should().NotBeTrue(
			because: "the poll must still be answered during the linger");
		fixture.Children.Should().ContainSingle(
			because: "no new worker may be created for the poll: the point of the linger is that the process holding the operation record is still there to answer it");
		fixture.Children[0].CallCount.Should().Be(2,
			because: "the completed worker itself answered the status poll, which is what 'the sticky worker serves both calls' means when the second call arrives after the first has finished");
	}

	[Test]
	[Category("Unit")]
	[Description("A completed sticky worker's slot is reclaimed by the NEXT long operation superseding it — not by the poll that read its status, which may be repeated as often as the caller likes.")]
	public async Task DispatchAsync_ShouldReclaimTheSlotOfACompletedWorker_WhenTheNextOperationSupersedesIt() {
		// Arrange — INVERTED 2026-08-19, and the inversion is the point. This test used to assert that
		// answering one poll reaped the completed worker, and it passed while production was wrong: the
		// second of two adjacent polls then reached a fresh per-call worker with an empty operation
		// registry and answered "not-found" for an operation that HAD been recorded (e2e 15896920). The
		// slot concern was real; the mechanism was the wrong one. Supersession by the next starter returns
		// the capacity without destroying a record the caller is still entitled to poll, which is what this
		// now pins.
		//
		// The shipped linger, not a shortened one: the reclaim must come from the supersession, not from
		// the window elapsing. Total 2 means sticky capacity is exactly 1, so "the slot came back" is
		// observable as the next long operation being admitted.
		using StickyFixture fixture = CreateFixture(concurrencyCap: 2,
			completionLinger: TimeSpan.FromMinutes(5));
		await fixture.DispatchAsync(
			CompileToolName, StarterMetadata(McpToolOperationFamily.ConfigurationBuild,
				McpToolSharedFileResource.ConfigurationBuild), EnvironmentName);
		await fixture.Children[0].SendCompletionSignalAsync(McpToolOperationFamily.ConfigurationBuild,
			exitCode: 0);
		await WaitUntilAsync(() => fixture.Reservations.HeldCount == 0);

		// Act
		CallToolResult status = await fixture.DispatchAsync(
			CompileStatusToolName, PollerMetadata(McpToolOperationFamily.ConfigurationBuild,
				McpToolSharedFileResource.ConfigurationBuild), EnvironmentName);
		CallToolResult nextOperation = await fixture.DispatchAsync(
			CompileToolName, StarterMetadata(McpToolOperationFamily.ConfigurationBuild,
				McpToolSharedFileResource.ConfigurationBuild), "another-environment");

		// Assert
		status.IsError.Should().NotBeTrue(
			because: "the poll must still have been answered by the completed worker — reaping it BEFORE it answered would defeat the linger entirely");
		fixture.StickyWorkers.Count.Should().Be(1,
			because: "only the NEW long operation may be registered: the completed one is superseded when a starter arrives for its key, which is what returns the slot");
		nextOperation.IsError.Should().NotBeTrue(
			because: "sticky capacity here is exactly one, so a completed worker still holding its slot would refuse this call for the rest of the linger window — a host reporting itself full while nothing is running");
	}

	[Test]
	[Category("Unit")]
	[Description("REPRODUCTION of the ENG-95262 stage-7 end-to-end failure in TeamCity 15896920: two ADJACENT compile-status polls inside one linger window must both be answered by the worker that ran the compile. This is the unit-scale twin of CompileStatus_Should_AnswerRepeatedPolls_FromTheStickyWorkerThatRanTheCompile, and it cannot both pass and let DispatchAsync_ShouldReapACompletedWorker_OnceItsStatusPollHasBeenAnswered pass: one asserts the completed worker survives its first answered poll, the other asserts it is reaped by it.")]
	public async Task DispatchAsync_ShouldAnswerTwoAdjacentStatusPolls_FromTheSameCompletedWorker() {
		// Arrange — the shipped linger, and enough capacity that nothing here can be refused for a reason
		// other than the one under test. The compile is COMPLETED before the first poll, which is the
		// arrangement the stand produced on its own: the end-to-end test compiles an environment that does
		// not exist, so the operation is finalised as `failed` long before either poll is issued.
		using StickyFixture fixture = CreateFixture(concurrencyCap: 8,
			completionLinger: TimeSpan.FromMinutes(5));
		await fixture.DispatchAsync(
			CompileToolName, StarterMetadata(McpToolOperationFamily.ConfigurationBuild,
				McpToolSharedFileResource.ConfigurationBuild), EnvironmentName);
		await fixture.Children[0].SendCompletionSignalAsync(McpToolOperationFamily.ConfigurationBuild,
			exitCode: 1);
		await WaitUntilAsync(() => fixture.Reservations.HeldCount == 0);

		// Act — two polls, back to back, exactly as a caller told to "poll compile-status" issues them.
		CallToolResult firstPoll = await fixture.DispatchAsync(
			CompileStatusToolName, PollerMetadata(McpToolOperationFamily.ConfigurationBuild,
				McpToolSharedFileResource.ConfigurationBuild), EnvironmentName);
		CallToolResult secondPoll = await fixture.DispatchAsync(
			CompileStatusToolName, PollerMetadata(McpToolOperationFamily.ConfigurationBuild,
				McpToolSharedFileResource.ConfigurationBuild), EnvironmentName);

		// Assert
		firstPoll.IsError.Should().NotBeTrue(
			because: "the first poll falls inside the linger and must be answered, or the second assertion below would be about an arrangement that never worked at all");
		secondPoll.IsError.Should().NotBeTrue(
			because: "a second poll inside the same linger window is an ordinary thing for a caller to do — compile-status is declared ReadOnly and Idempotent, and the shipped workspace AGENTS.md tells the agent to poll it");
		fixture.Children.Should().ContainSingle(
			because: "BOTH polls must be answered by the process that holds the compile record; a second child here is the parent falling back to a per-call worker whose ICompileOperationRegistry is empty, which is exactly how a completed compile turns into 'not-found' between two adjacent polls");
		fixture.Children[0].CallCount.Should().Be(3,
			because: "one starting call plus two polls all served by the one worker is what 'the sticky worker serves both calls' means when the caller polls more than once");
	}

	[Test]
	[Category("Unit")]
	[Description("A long-running tool that names no registered environment — restart-by-credentials, whose arguments are url/userName/password — is keyed by its url, so two credentials-started restarts against DIFFERENT stands are two sticky workers rather than one shared unresolved key.")]
	public async Task DispatchAsync_ShouldKeyACredentialsStartedOperationByItsUrl() {
		// Arrange — a scalar-parameter tool: the SDK binds each key at the top level rather than under an
		// `args` object, which is the second of the two argument shapes the parent has to read.
		using StickyFixture fixture = CreateFixture(concurrencyCap: 8);

		// Act
		CallToolResult first = await fixture.DispatchWithArgumentsAsync(
			"restart-by-credentials", StarterMetadata(McpToolOperationFamily.Restart,
				McpToolSharedFileResource.None),
			new Dictionary<string, string> {
				["url"] = "https://first.creatio.com", ["userName"] = "Supervisor"
			});
		CallToolResult second = await fixture.DispatchWithArgumentsAsync(
			"restart-by-credentials", StarterMetadata(McpToolOperationFamily.Restart,
				McpToolSharedFileResource.None),
			new Dictionary<string, string> {
				["url"] = "https://second.creatio.com", ["userName"] = "Supervisor"
			});

		// Assert
		first.IsError.Should().NotBeTrue(because: "the first credentials-started restart must be admitted");
		second.IsError.Should().NotBeTrue(because: "the second must be admitted too — the host has capacity for both");
		fixture.StickyWorkers.Count.Should().Be(2,
			because: "two DIFFERENT stands are two operations: keyed off an absent environment-name alone they would share one unresolved key, the second would lose the registration race and its readiness wait would become unreachable to any poll");
	}

	[Test]
	[Category("Unit")]
	[Description("The parent's bound on one sticky call is DERIVED from the child's own MCP response deadline plus headroom, not stated beside it — because that deadline is operator-configurable up to 600 s and a fixed parent bound would hold the invariant on the default and silently break it the moment somebody raised the child's.")]
	public void DefaultStickyCallBudget_ShouldBeDerivedFromTheChildsResponseDeadline() {
		// Arrange & Act — both values are resolved at type load from the same environment, so the parent and
		// the child cannot disagree about which deadline is in force.
		TimeSpan childDeadline = McpProgressHeartbeat.DefaultResponseDeadline;
		TimeSpan parentBound = McpWorkerCallDispatcher.DefaultStickyCallBudget;

		// Assert
		parentBound.Should().Be(childDeadline + McpWorkerCallDispatcher.StickyCallBudgetHeadroom,
			because: "a sticky call is RETURNED by the child's own in-progress envelope (ADR rule 11), so the parent's bound has to move with that deadline; a constant here would kill every long operation a fraction before it answered on any host whose CLIO_MCP_RESPONSE_DEADLINE_SECONDS was raised, and the symptom is a compile that always fails and always turns out to have run");
		parentBound.Should().BeGreaterThan(childDeadline,
			because: "the whole ordering the derivation exists to guarantee is that the child answers before the parent gives up");
		McpWorkerCallDispatcher.StickyCallBudgetHeadroom.Should().BeGreaterThan(TimeSpan.Zero,
			because: "zero headroom makes the two bounds a race decided by scheduling, on a call whose loser is a killed long operation");
	}

	// ---------------------------------------------------------------------------------------------
	// The lost-race starter: starting is SINGLE-FLIGHT per key, and the second starter is refused
	// before it can create a worker whose continuation would be killed with it
	// ---------------------------------------------------------------------------------------------

	[Test]
	[Category("Unit")]
	[Description("Two starters of one family racing for one key create exactly ONE worker: the loser is refused by name before anything is launched, rather than starting a second worker that is released — killed — the instant it has composed its in-progress response, taking the readiness wait continuing inside it with it.")]
	public async Task DispatchAsync_ShouldCreateOneWorkerAndRefuseTheLoser_WhenTwoStartersRaceForOneKey() {
		// Arrange — the restart family, whose shared file resource is None. It is the family with NO
		// configuration-build reservation, which is precisely why two of its starters can reach the spawn
		// path together; compile and install-process-builder are already serialised before it. Capacity is
		// eight, so nothing here can be refused for want of a sticky slot. The child stalls its initialize
		// reply so both starters are inside the spawn-to-register window at the same time — without that,
		// the second may simply arrive after the first has registered and the race never happens.
		using StickyFixture fixture = CreateFixture(concurrencyCap: 8,
			handshakeDelay: TimeSpan.FromMilliseconds(150));

		// Act
		Task<CallToolResult> firstStarter = Task.Run(() => fixture.DispatchAsync(
			RestartToolName,
			StarterMetadata(McpToolOperationFamily.Restart, McpToolSharedFileResource.None),
			EnvironmentName));
		Task<CallToolResult> secondStarter = Task.Run(() => fixture.DispatchAsync(
			RestartToolName,
			StarterMetadata(McpToolOperationFamily.Restart, McpToolSharedFileResource.None),
			EnvironmentName));
		CallToolResult[] results = await Task.WhenAll(firstStarter, secondStarter);

		// Assert
		fixture.Containment.LaunchCount.Should().Be(1,
			because: "the spawn count on the supervisor's containment is the ONLY place a second worker is visible: a loser that is created and then released leaves no trace in the registry, answers its caller normally, and takes the operation running inside it down when it goes");
		results.Where(result => result.IsError != true).Should().ContainSingle(
			because: "exactly one starter may own the family's worker for this target, and it must be answered normally");
		results.Where(result => result.IsError == true).Should().ContainSingle(
			because: "the other starter must be REFUSED rather than served by a doomed worker — a restart returns in-progress and continues its readiness wait inside the process, so releasing that process the moment the response is composed destroys the operation the caller was told to poll for");
		ReadErrorClass(results.Single(result => result.IsError == true)).Should()
			.Be(LongOperationInProgressErrorClass,
				because: "the refusal must say WHY: an operation of this family is already running for this environment, which is neither a relay failure to investigate nor a timeout to retry but a state the caller resolves by polling the operation that is already running");
		fixture.StickyWorkers.Count.Should().Be(1,
			because: "one key holds one worker, and the survivor must be registered so the status poll the admitted caller was told to make can reach it");
		fixture.Children.Should().ContainSingle(
			because: "no second child process may exist at all; one that was created and killed is exactly the defect");
		fixture.Children[0].HasExited.Should().BeFalse(
			because: "the survivor must still be alive after the refusal — the refusal path must not touch the incumbent's worker");
	}

	[Test]
	[Category("Unit")]
	[Description("A second starter arriving while the family's sticky worker is still running is refused by name, and the incumbent keeps answering its status polls — the refusal must cost the running operation nothing.")]
	public async Task DispatchAsync_ShouldRefuseASecondStarter_WhileTheFamilysStickyWorkerIsStillRunning() {
		// Arrange
		using StickyFixture fixture = CreateFixture(concurrencyCap: 8);
		CallToolResult started = await fixture.DispatchAsync(
			RestartToolName,
			StarterMetadata(McpToolOperationFamily.Restart, McpToolSharedFileResource.None),
			EnvironmentName);

		// Act
		CallToolResult refused = await fixture.DispatchAsync(
			RestartToolName,
			StarterMetadata(McpToolOperationFamily.Restart, McpToolSharedFileResource.None),
			EnvironmentName);
		CallToolResult status = await fixture.DispatchAsync(
			RestartStatusToolName,
			PollerMetadata(McpToolOperationFamily.Restart, McpToolSharedFileResource.None),
			EnvironmentName);

		// Assert
		started.IsError.Should().NotBeTrue(
			because: "the incumbent must have been admitted, or the refusal below would be measuring an empty registry rather than a live worker");
		refused.IsError.Should().BeTrue(
			because: "one target runs one operation of one family at a time: serving this call from a second worker that is released afterwards is what kills a readiness wait that had not finished");
		ReadErrorClass(refused).Should().Be(LongOperationInProgressErrorClass,
			because: "an agent needs to be told the operation it asked for is ALREADY RUNNING, which is actionable — poll it — where a relay failure would send it hunting a clio bug");
		fixture.Containment.LaunchCount.Should().Be(1,
			because: "a refused starter must create nothing: no second process, and therefore no second restart request to Creatio");
		status.IsError.Should().NotBeTrue(
			because: "the incumbent must still be reachable after the refusal; a refusal that disturbed the running worker would be worse than the defect it replaces");
		fixture.Children[0].CallCount.Should().Be(2,
			because: "the one worker answered the starting call and the poll, so the refusal neither reached it nor started anything alongside it");
	}

	[Test]
	[Category("Unit")]
	[Description("A worker that has already reported completion and is only lingering to answer a status poll does NOT refuse the next long operation of its family: the new starter supersedes it, so the linger cannot become minutes of false 'already in progress' refusals.")]
	public async Task DispatchAsync_ShouldSupersedeALingeringCompletedWorker_WhenTheNextStarterArrives() {
		// Arrange — the shipped-shaped linger rather than zero, because zero would let the sweep at the
		// head of the next dispatch remove the entry and the successor would never meet it. A completed
		// worker is still LIVE for the whole window, so an unconditional "is there a worker for this key"
		// refusal would deny this environment its next restart for five minutes after the last one ended.
		using StickyFixture fixture = CreateFixture(concurrencyCap: 8,
			completionLinger: TimeSpan.FromMinutes(5));
		await fixture.DispatchAsync(
			RestartToolName,
			StarterMetadata(McpToolOperationFamily.Restart, McpToolSharedFileResource.None),
			EnvironmentName);
		StickyWorkerKey key = new(McpToolOperationFamily.Restart, $"tenant|{EnvironmentName}");
		await fixture.Children[0].SendCompletionSignalAsync(McpToolOperationFamily.Restart, exitCode: 0);
		await WaitUntilAsync(() =>
			fixture.StickyWorkers.TryReach(key, out StickyWorkerEntry lingering) && lingering.IsCompleted);

		// Act
		CallToolResult next = await fixture.DispatchAsync(
			RestartToolName,
			StarterMetadata(McpToolOperationFamily.Restart, McpToolSharedFileResource.None),
			EnvironmentName);

		// Assert
		next.IsError.Should().NotBeTrue(
			because: "the previous operation has FINISHED; refusing the next one because its worker is still lingering for a poll would invent a cooldown nobody asked for");
		fixture.Containment.LaunchCount.Should().Be(2,
			because: "the successor is a new operation and needs its own worker: reusing the finished one would multiplex a second restart onto the process that just reported its first as done");
		fixture.StickyWorkers.TryReach(key, out StickyWorkerEntry registered).Should().BeTrue(
			because: "the key must hold the RUNNING operation's worker so its status poll reaches it");
		registered.IsCompleted.Should().BeFalse(
			because: "the entry under this key must be the SUCCESSOR, not the finished worker it superseded — otherwise the new operation's poll is answered by the process that ran the previous one");
		fixture.Children[1].HasExited.Should().BeFalse(
			because: "the successor owns the key and must stay alive; a starter that registered nothing and released itself is precisely the lost-race worker this change removes");
	}

	[Test]
	[Category("Unit")]
	[Description("A completion signal carrying one worker completes THAT worker or nothing: aimed at a key another worker is registered under, it neither marks that worker complete nor releases the reservation it is holding for a build that is still running.")]
	public async Task SignalCompleted_ShouldNotCompleteTheRegisteredWorker_WhenTheSignalCameFromAnotherWorker() {
		// Arrange — two live sticky workers, each under its own key. The first holds its target's
		// configuration-build reservation, and a completion releases that AT ONCE, so a signal that took
		// effect on the wrong entry shows up as an environment that stopped being reserved while its
		// build was still running — not merely as a lifetime quietly shortened to a linger window.
		using StickyFixture fixture = CreateFixture(concurrencyCap: 8);
		await fixture.DispatchAsync(
			InstallProcessBuilderToolName,
			StarterMetadata(McpToolOperationFamily.ConfigurationBuild,
				McpToolSharedFileResource.ConfigurationBuild),
			EnvironmentName);
		await fixture.DispatchAsync(
			RestartToolName,
			StarterMetadata(McpToolOperationFamily.Restart, McpToolSharedFileResource.None),
			"another-environment");
		StickyWorkerKey buildKey =
			new(McpToolOperationFamily.ConfigurationBuild, $"tenant|{EnvironmentName}");
		StickyWorkerKey restartKey = new(McpToolOperationFamily.Restart, "tenant|another-environment");
		fixture.StickyWorkers.TryReach(buildKey, out StickyWorkerEntry build).Should().BeTrue(
			because: "the arrangement needs a live registered worker to aim a foreign signal at");
		fixture.StickyWorkers.TryReach(restartKey, out StickyWorkerEntry restart).Should().BeTrue(
			because: "and a second, DIFFERENT worker to be the one the signal actually came from");

		// Act — the second worker's entry, presented under the first worker's key.
		bool foreignSignal =
			fixture.StickyWorkers.SignalCompleted(buildKey, restart, TimeSpan.FromMinutes(1));
		bool completedAfterForeignSignal = build.IsCompleted;
		int reservationsAfterForeignSignal = fixture.Reservations.HeldCount;
		bool ownSignal = fixture.StickyWorkers.SignalCompleted(buildKey, build, TimeSpan.FromMinutes(1));

		// Assert
		foreignSignal.Should().BeFalse(
			because: "a signal only completes the worker that emitted it: the key it names may by then hold a DIFFERENT worker — a successor to one that finished — and completing that one releases a reservation and shortens a lifetime under an operation that is still running");
		completedAfterForeignSignal.Should().BeFalse(
			because: "the running build must not be marked finished by somebody else's completion");
		reservationsAfterForeignSignal.Should().Be(1,
			because: "the configuration-build reservation is released the moment a build reports completion, so a foreign signal that took effect would leave this environment open to a second concurrent build while the first was still compiling");
		restart.IsCompleted.Should().BeFalse(
			because: "the entry that was passed must not be completed either — it was not registered under the key the signal named, and the signal is not a licence to complete it wherever it lives");
		ownSignal.Should().BeTrue(
			because: "the worker's OWN signal must still take effect, or this fixture would be proving that signals never work rather than that they are scoped");
		fixture.Reservations.HeldCount.Should().Be(0,
			because: "that own signal must release the finished build's reservation at once, which is the same observation the foreign signal was required not to produce");
	}

	[Test]
	[Category("Unit")]
	[Description("Reaping is scoped to the entry as well: asked to reap a worker the key no longer holds, the registry leaves the registered worker alone — otherwise a poll finishing late would end the operation that had just superseded the one it was polling.")]
	public async Task ReapAsync_ShouldLeaveTheRegisteredWorkerAlone_WhenAskedToReapAnEntryThatKeyNoLongerHolds() {
		// Arrange
		using StickyFixture fixture = CreateFixture(concurrencyCap: 8);
		await fixture.DispatchAsync(
			InstallProcessBuilderToolName,
			StarterMetadata(McpToolOperationFamily.ConfigurationBuild,
				McpToolSharedFileResource.ConfigurationBuild),
			EnvironmentName);
		await fixture.DispatchAsync(
			RestartToolName,
			StarterMetadata(McpToolOperationFamily.Restart, McpToolSharedFileResource.None),
			"another-environment");
		StickyWorkerKey buildKey =
			new(McpToolOperationFamily.ConfigurationBuild, $"tenant|{EnvironmentName}");
		fixture.StickyWorkers.TryReach(buildKey, out StickyWorkerEntry incumbent).Should().BeTrue(
			because: "the incumbent has to be reachable before the reap, or 'still reachable after' would say nothing");
		fixture.StickyWorkers.TryReach(
			new StickyWorkerKey(McpToolOperationFamily.Restart, "tenant|another-environment"),
			out StickyWorkerEntry strangerToThisKey).Should().BeTrue(
			because: "the reap below has to be handed a real entry that is simply not the one this key holds");

		// Act
		await fixture.StickyWorkers.ReapAsync(buildKey, strangerToThisKey);

		// Assert
		fixture.StickyWorkers.TryReach(buildKey, out StickyWorkerEntry survivor).Should().BeTrue(
			because: "the worker registered under this key must survive a reap that named a different entry — a key-scoped reap would end whichever operation happened to hold the key, which after a supersession is the NEW one");
		survivor.Should().BeSameAs(incumbent,
			because: "the key must still resolve to the SAME entry, so a later poll reaches the operation that is actually running rather than a replacement built for it");
		fixture.Children[0].HasExited.Should().BeFalse(
			because: "the registered worker's PROCESS must be untouched: the damage a key-scoped reap does is not a dictionary edit but a killed operation");
		fixture.Children[1].HasExited.Should().BeTrue(
			because: "the entry that WAS handed in is released whatever key it was named under — an entry nobody owns must not leak a process, an admission slot and a reservation");
	}

	// ---------------------------------------------------------------------------------------------
	// Capacity refusal, mapped to its own envelope rather than to a relay failure
	// ---------------------------------------------------------------------------------------------

	[Test]
	[Category("Unit")]
	[Description("When every long-operation slot is held, the next long operation is refused immediately with a named capacity envelope carrying the limit and the knob — not with a relay-failure envelope, which would report a working saturation guard as a clio bug.")]
	public async Task DispatchAsync_ShouldRefuseWithACapacityEnvelope_WhenEveryStickySlotIsHeld() {
		// Arrange
		using StickyFixture fixture = CreateFixture(concurrencyCap: 2);
		await fixture.DispatchAsync(
			CompileToolName, StarterMetadata(McpToolOperationFamily.ConfigurationBuild,
				McpToolSharedFileResource.ConfigurationBuild), EnvironmentName);

		// Act — a DIFFERENT environment, so the configuration-build reservation cannot be what refuses it.
		long startedAt = Stopwatch.GetTimestamp();
		CallToolResult refused = await fixture.DispatchAsync(
			CompileToolName, StarterMetadata(McpToolOperationFamily.ConfigurationBuild,
				McpToolSharedFileResource.ConfigurationBuild), "another-environment");
		TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt);

		// Assert
		refused.IsError.Should().BeTrue(because: "the host cannot run another long operation, so the call must be refused rather than queued behind one that runs for minutes");
		ReadErrorClass(refused).Should().Be(McpWorkerCallDispatcher.StickyCapacityErrorClass,
			because: "this is neither a slow backend nor a clio defect: it is a host running as many long operations as it is configured to run, and its remedy is an operator changing a number");
		elapsed.Should().BeLessThan(ShortQueueWaitBound,
			because: "sticky admission does not queue — spending a minute of the caller's patience to arrive at the same refusal with less information is exactly what the immediate answer replaces");
		fixture.Reservations.HeldCount.Should().Be(1,
			because: "the refused call must have released the reservation it took on the way in; a counter that drifts here denies that environment until the ceiling reclaims it");
	}

	// ---------------------------------------------------------------------------------------------
	// The live call shape: every sticky tool is NON-RESIDENT, so the caller reaches it through clio-run
	// ---------------------------------------------------------------------------------------------

	[Test]
	[Category("Unit")]
	[Description("A sticky call wrapped by clio-run derives the SAME sticky key as the identical call named directly, so a compile started through the executor and a compile-status polled through it land in one bucket instead of an unresolved one.")]
	public void ReadTargetOptions_ShouldDeriveTheSameStickyKeyAsTheDirectCall_WhenTheCallArrivesThroughClioRun() {
		// Arrange — the three shapes one target can arrive in. The direct one is what a resident tool
		// sends; the other two are what clio-run sends, and every stage-7 tool is non-resident, so those
		// two ARE the live vector rather than an edge case.
		CallToolRequestParams direct = DirectCallParams(CompileToolName, EnvironmentName);
		CallToolRequestParams executor = ExecutorCallParams(CompileToolName, EnvironmentName);
		CallToolRequestParams wrappedExecutor = WrappedExecutorCallParams(CompileToolName, EnvironmentName);

		// Act
		StickyWorkerKey directKey = DeriveStickyKey(direct, CompileToolName);
		StickyWorkerKey executorKey = DeriveStickyKey(executor, CompileToolName);
		StickyWorkerKey wrappedKey = DeriveStickyKey(wrappedExecutor, CompileToolName);

		// Assert
		directKey.TenantKey.Should().Be($"tenant|{EnvironmentName}",
			because: "the direct shape must resolve the environment it names, or the two comparisons below would be agreeing on an unresolved key rather than on the right one");
		executorKey.Should().Be(directKey,
			because: "clio-run's own call shape puts the target one object deeper, and a key derived from the wrapper instead of from the target is what makes a status poll miss the worker holding the operation");
		wrappedKey.Should().Be(directKey,
			because: "the wrapped shape {\"args\":{\"command\":…,\"args\":{…}}} is what an agent habituated to the single-args-record convention actually sends, and clio-run itself accepts it — so the sticky key must recover the same target from it");
	}

	[Test]
	[Category("Unit")]
	[Description("Two clio-run calls naming DIFFERENT environments derive different sticky keys, so one environment's compile can never refuse another's by colliding on a single unresolved key.")]
	public void ReadTargetOptions_ShouldDeriveDistinctStickyKeys_WhenTwoClioRunCallsNameDifferentEnvironments() {
		// Arrange
		CallToolRequestParams first = WrappedExecutorCallParams(CompileToolName, EnvironmentName);
		CallToolRequestParams second = WrappedExecutorCallParams(CompileToolName, "another-environment");

		// Act
		StickyWorkerKey firstKey = DeriveStickyKey(first, CompileToolName);
		StickyWorkerKey secondKey = DeriveStickyKey(second, CompileToolName);

		// Assert
		firstKey.Should().NotBe(secondKey,
			because: "an unresolved target puts every wrapped call in one bucket, and now that a colliding starter is REFUSED that collision lets one environment's compile refuse another environment's");
		firstKey.TenantKey.Should().Be($"tenant|{EnvironmentName}",
			because: "each key must name the environment its call named — two keys can also differ by both being wrong, and this is what tells the two apart");
		secondKey.TenantKey.Should().Be("tenant|another-environment",
			because: "the second key must likewise name its own environment rather than a shared placeholder");
	}

	[Test]
	[Category("Unit")]
	[Description("A compile started through clio-run and a compile-status polled through clio-run are answered by the SAME sticky worker while the sticky pool is saturated — the live vector end to end, since both tools are non-resident and cannot be called by name.")]
	public async Task DispatchAsync_ShouldAnswerAStatusPollFromTheSameStickyWorker_WhenBothCallsArriveThroughClioRun() {
		// Arrange — a total of two admits exactly one sticky worker, so the compile saturates the sticky
		// pool and the poll cannot resolve by taking a slot of its own.
		using StickyFixture fixture = CreateFixture(concurrencyCap: 2);
		CallToolResult compile = await fixture.DispatchWithParamsAsync(
			CompileToolName, StarterMetadata(McpToolOperationFamily.ConfigurationBuild,
				McpToolSharedFileResource.ConfigurationBuild),
			WrappedExecutorCallParams(CompileToolName, EnvironmentName));
		int launchesAfterCompile = fixture.Containment.LaunchCount;

		// Act
		CallToolResult status = await fixture.DispatchWithParamsAsync(
			CompileStatusToolName, PollerMetadata(McpToolOperationFamily.ConfigurationBuild,
				McpToolSharedFileResource.ConfigurationBuild),
			WrappedExecutorCallParams(CompileStatusToolName, EnvironmentName));

		// Assert
		compile.IsError.Should().NotBeTrue(
			because: "the scripted worker answered the starting call, so a failure here would mean the poll was measuring a dead worker rather than a live one");
		status.IsError.Should().NotBeTrue(
			because: "the poll must be answered rather than refused: through clio-run it is the ONLY way this tool can be called at all");
		fixture.Containment.LaunchCount.Should().Be(launchesAfterCompile,
			because: "a poll whose key missed the compile's key falls back to an ordinary per-call worker, and that second launch is exactly what 'answered from an empty registry' looks like");
		fixture.Children.Should().ContainSingle(
			because: "one worker must serve both calls; two children means the poll reached a process that never saw the compile");
		fixture.Children[0].CallCount.Should().Be(2,
			because: "the compile and the poll must both have been answered by the SAME child process, which is what 'reaches the worker holding the operation' means");
	}

	[Test]
	[Category("Unit")]
	[Description("Two environments compiling concurrently through clio-run neither refuse each other nor share a worker, and each status poll is answered by its OWN environment's worker — the collision an unresolved key produces once a colliding starter is refused.")]
	public async Task DispatchAsync_ShouldKeepTwoClioRunEnvironmentsApart_WhenBothAreCompilingConcurrently() {
		// Arrange — a total of four admits two sticky workers, so capacity can never be what refuses the
		// second compile; only a shared key can be.
		using StickyFixture fixture = CreateFixture(concurrencyCap: 4);

		// Act
		CallToolResult firstCompile = await fixture.DispatchWithParamsAsync(
			CompileToolName, StarterMetadata(McpToolOperationFamily.ConfigurationBuild,
				McpToolSharedFileResource.ConfigurationBuild),
			WrappedExecutorCallParams(CompileToolName, EnvironmentName));
		CallToolResult secondCompile = await fixture.DispatchWithParamsAsync(
			CompileToolName, StarterMetadata(McpToolOperationFamily.ConfigurationBuild,
				McpToolSharedFileResource.ConfigurationBuild),
			WrappedExecutorCallParams(CompileToolName, "another-environment"));
		CallToolResult secondStatus = await fixture.DispatchWithParamsAsync(
			CompileStatusToolName, PollerMetadata(McpToolOperationFamily.ConfigurationBuild,
				McpToolSharedFileResource.ConfigurationBuild),
			WrappedExecutorCallParams(CompileStatusToolName, "another-environment"));

		// Assert
		firstCompile.IsError.Should().NotBeTrue(
			because: "the first compile has nothing to collide with, so a failure here would be a broken arrangement rather than the defect");
		secondCompile.IsError.Should().NotBeTrue(
			because: "these are two DIFFERENT environments: an unresolved key files both under one bucket, and the configuration-build reservation then refuses the second environment's compile because the first environment is compiling");
		fixture.Containment.LaunchCount.Should().Be(2,
			because: "each environment must get its own worker; one launch means the second compile was refused and never started");
		fixture.Reservations.HeldCount.Should().Be(2,
			because: "the configuration-build reservation is keyed by normalised TARGET, so two targets must hold two reservations — one held reservation is the shared-key collision stated as a number");
		fixture.Children[1].CallCount.Should().Be(2,
			because: "the second environment's poll must be answered by the second environment's worker: the compile plus the poll is two calls on that child");
		fixture.Children[0].CallCount.Should().Be(1,
			because: "the first environment's worker must have seen only its own compile — answering another environment's status poll is what a shared key does");
		secondStatus.IsError.Should().NotBeTrue(
			because: "the poll must be answered rather than refused, or the routing assertions above would be describing a call that never happened");
	}


	// ---------------------------------------------------------------------------------------------
	// ADR 3.2a - a cancelled poll decides the SESSION's fate, because the SDK's send lock guarantees a
	// completed send and not an atomic one
	// ---------------------------------------------------------------------------------------------

	[Test]
	[Category("Unit")]
	[Description("A poll whose caller cancelled while its request was still being written RETIRES the sticky worker: the session may hold half a JSON-RPC frame, so it is reaped rather than left registered for the next poll to append to.")]
	public async Task ReachAndCallAsync_ShouldRetireTheStickyWorker_WhenTheCallersCancellationInterruptedTheSend() {
		// Arrange - the send never completes, which is precisely the state the SDK's _sendLock does not
		// protect against: the caller's token is taken separately by the payload write, the newline write and
		// the flush, so a token firing between them releases the lock over an unterminated line.
		ScriptedPollTransport transport = new() { BlockToolCallSends = true };
		await using PollHarness harness = await CreatePollHarnessAsync(transport);
		using CancellationTokenSource caller = new();

		// Act
		Task<CallToolResult> poll = harness.Poll.ReachAndCallAsync(PollKey,
			DirectCallParams(CompileStatusToolName, EnvironmentName), PollBudget, caller.Token).AsTask();
		await WaitUntilAsync(() => transport.ToolCallSendsStarted == 1);
		await caller.CancelAsync();
		Func<Task> awaitingThePoll = async () => await poll;

		// Assert
		await awaitingThePoll.Should().ThrowAsync<OperationCanceledException>(
			because: "the caller must still see its own cancellation: retiring a SESSION and answering a CALLER are different things, and a poll that swallowed the cancellation would report a status nobody asked for any more");
		transport.ToolCallSendsCancelled.Should().Be(1,
			because: "the arrangement is only the one under test if the send itself was interrupted - a send that had completed would be the CLEAN cancellation, which has the opposite correct answer");
		harness.Registry.TryReach(PollKey, out StickyWorkerEntry _).Should().BeFalse(
			because: "ADR 3.2a is binding: a session whose send did not complete is retired and never reused, because the next writer's JSON would be appended to the dangling line and the worker would get one corrupt frame and answer nothing - one process wedged, which is the failure this whole boundary exists to remove");
		harness.LeaseDisposals.Should().Be(1,
			because: "retiring the ENTRY is what returns the worker's admission slot and its shared-resource reservation; leaving them held until expiry would refuse the next long operation on a host whose sticky capacity is one or two");
	}

	[Test]
	[Category("Unit")]
	[Description("A poll whose caller cancelled AFTER its request was written leaves the sticky worker registered, and the next poll proves the session before reusing it: the reuse is preceded by a bounded tools/list probe rather than by an assumption.")]
	public async Task ReachAndCallAsync_ShouldProveTheSessionWithABoundedProbe_WhenReusingItAfterACleanCancellation() {
		// Arrange - the send COMPLETES and the answer never comes, so the worker was told through
		// notifications/cancelled and the transport is provably whole. That session is reusable, but only
		// after the worker has been asked whether it is still answering.
		ScriptedPollTransport transport = new() { AnswerToolCalls = false };
		await using PollHarness harness = await CreatePollHarnessAsync(transport);
		using CancellationTokenSource caller = new();
		Task<CallToolResult> abandoned = harness.Poll.ReachAndCallAsync(PollKey,
			DirectCallParams(CompileStatusToolName, EnvironmentName), PollBudget, caller.Token).AsTask();
		await WaitUntilAsync(() => transport.ToolCallSendsStarted == 1);
		await caller.CancelAsync();
		Func<Task> awaitingTheAbandonedPoll = async () => await abandoned;
		await awaitingTheAbandonedPoll.Should().ThrowAsync<OperationCanceledException>(
			because: "the arrangement depends on the first poll genuinely being cancelled by its caller");
		bool retained = harness.Registry.TryReach(PollKey, out StickyWorkerEntry _);
		transport.AnswerToolCalls = true;

		// Act
		CallToolResult reused = await harness.Poll.ReachAndCallAsync(PollKey,
			DirectCallParams(CompileStatusToolName, EnvironmentName), PollBudget, CancellationToken.None);

		// Assert
		retained.Should().BeTrue(
			because: "a session whose request was WRITTEN carries no half frame, and killing the worker over it would end a compile that is still running - the operation the abandoned status poll was about");
		reused.Should().NotBeNull(
			because: "a retained worker must still be reachable: answering 'nothing was reached' would send the caller down the per-call path for a worker that was perfectly healthy");
		transport.SentMethods.Should().ContainInOrder(new[] { CallToolMethodName, ListToolsMethodName, CallToolMethodName },
			because: "the ADR allows reuse after a clean cancellation only behind a bounded liveness probe: the worker was told to stop, and a worker that ignores that notification is still busy with the abandoned call");
	}

	[Test]
	[Category("Unit")]
	[Description("The probe that precedes reuse is BOUNDED: a worker that stops answering after a clean cancellation is reaped in probe time rather than in the poll's whole budget, so an unreachable worker does not hold a caller for the length of its call budget.")]
	public async Task ReachAndCallAsync_ShouldReapInProbeTime_WhenTheWorkerStopsAnsweringAfterACleanCancellation() {
		// Arrange - a worker with an open pipe that answers nothing is exactly the state a probe exists to
		// catch, and an unbounded probe on it would be the same wedge wearing a different hat.
		ScriptedPollTransport transport = new() { AnswerToolCalls = false };
		await using PollHarness harness = await CreatePollHarnessAsync(transport);
		using CancellationTokenSource caller = new();
		Task<CallToolResult> abandoned = harness.Poll.ReachAndCallAsync(PollKey,
			DirectCallParams(CompileStatusToolName, EnvironmentName), PollBudget, caller.Token).AsTask();
		await WaitUntilAsync(() => transport.ToolCallSendsStarted == 1);
		await caller.CancelAsync();
		Func<Task> awaitingTheAbandonedPoll = async () => await abandoned;
		await awaitingTheAbandonedPoll.Should().ThrowAsync<OperationCanceledException>(
			because: "the arrangement depends on the first poll genuinely being cancelled by its caller");
		transport.AnswerToolsList = false;

		// Act
		long startedAt = Stopwatch.GetTimestamp();
		CallToolResult reused = await harness.Poll.ReachAndCallAsync(PollKey,
			DirectCallParams(CompileStatusToolName, EnvironmentName), PollBudget, CancellationToken.None);
		TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt);

		// Assert
		reused.Should().BeNull(
			because: "a worker that cannot be proved alive must be answered as 'nothing was reached', so the caller takes the ordinary per-call path exactly as it would have after a parent restart");
		elapsed.Should().BeLessThan(ProbeBoundCeiling,
			because: "the probe carries its own bound, so an unanswering worker costs the caller probe time rather than the poll's whole budget - a probe that waited out the budget would be the unbounded wait this fix exists to remove");
		harness.Registry.TryReach(PollKey, out StickyWorkerEntry _).Should().BeFalse(
			because: "a worker that failed its probe is retired then and there, or its admission slot and reservation stay held until expiry for a process nothing can talk to");
	}


	// ---------------------------------------------------------------------------------------------
	// Helpers
	// ---------------------------------------------------------------------------------------------

	private static string Target(EnvironmentOptions options) =>
		options?.Environment ?? options?.Uri ?? "default";

	/// <summary>
	/// Builds the params a RESIDENT tool is called with: the SDK binds a single complex args record under
	/// its parameter name, so the target sits one object below <c>Arguments</c>.
	/// </summary>
	/// <param name="toolName">The tool being called.</param>
	/// <param name="environmentName">The environment the call names.</param>
	/// <returns>The params.</returns>
	private static CallToolRequestParams DirectCallParams(string toolName, string environmentName) =>
		new() {
			Name = toolName,
			Arguments = new Dictionary<string, JsonElement> {
				["args"] = JsonSerializer.SerializeToElement(
					new Dictionary<string, string> { ["environment-name"] = environmentName })
			}
		};

	/// <summary>
	/// Builds clio-run's OWN call shape — <c>{"command":"&lt;tool&gt;","args":{…}}</c> — which is what
	/// the executor declares and what its two top-level parameters bind to.
	/// </summary>
	/// <param name="toolName">The inner tool.</param>
	/// <param name="environmentName">The environment the inner call names.</param>
	/// <returns>The params, named for the executor rather than for the inner tool.</returns>
	private static CallToolRequestParams ExecutorCallParams(string toolName, string environmentName) =>
		new() {
			Name = "clio-run",
			Arguments = new Dictionary<string, JsonElement> {
				["command"] = JsonSerializer.SerializeToElement(toolName),
				["args"] = JsonSerializer.SerializeToElement(
					new Dictionary<string, string> { ["environment-name"] = environmentName })
			}
		};

	/// <summary>
	/// Builds the WRAPPED clio-run shape — <c>{"args":{"command":"&lt;tool&gt;","args":{…}}}</c> — the one
	/// an agent habituated to the single-args-record convention sends and that
	/// <c>ClioRunExecutor.RecoverWrappedCall</c> accepts. Here the target is TWO object levels below
	/// <c>Arguments</c>.
	/// </summary>
	/// <param name="toolName">The inner tool.</param>
	/// <param name="environmentName">The environment the inner call names.</param>
	/// <returns>The params, named for the executor rather than for the inner tool.</returns>
	private static CallToolRequestParams WrappedExecutorCallParams(string toolName, string environmentName) =>
		new() {
			Name = "clio-run",
			Arguments = new Dictionary<string, JsonElement> {
				["args"] = JsonSerializer.SerializeToElement(new Dictionary<string, object> {
					["command"] = toolName,
					["args"] = new Dictionary<string, string> { ["environment-name"] = environmentName }
				})
			}
		};

	/// <summary>
	/// Derives the sticky key exactly as the dispatcher does: read the target off the raw call, then fold
	/// it through the resolver stub that stands in for <c>IToolCommandResolver.GetTenantKey</c>.
	/// </summary>
	/// <param name="parameters">The caller's params.</param>
	/// <param name="dispatchedToolName">The tool the route resolved to — the INNER tool for an executor call.</param>
	/// <returns>The key the dispatcher would file this call under.</returns>
	private static StickyWorkerKey DeriveStickyKey(CallToolRequestParams parameters, string dispatchedToolName) {
		EnvironmentOptions options =
			McpWorkerCallDispatcher.ReadTargetOptions(parameters, dispatchedToolName);
		return new StickyWorkerKey(McpToolOperationFamily.ConfigurationBuild, $"tenant|{Target(options)}");
	}

	private static McpToolExecutionMetadata StarterMetadata(McpToolOperationFamily family,
		McpToolSharedFileResource resource) =>
		new(McpToolExecutionLocation.Worker, McpToolExecutionLifetime.Sticky, family,
			McpToolBudgetPolicy.ParentKillExtended, McpToolClientRequests.Progress, resource,
			AliasOf: null, StartsOperation: true);

	private static McpToolExecutionMetadata PollerMetadata(McpToolOperationFamily family,
		McpToolSharedFileResource resource) =>
		new(McpToolExecutionLocation.Worker, McpToolExecutionLifetime.Sticky, family,
			McpToolBudgetPolicy.ParentKillExtended, McpToolClientRequests.None, resource,
			AliasOf: null, StartsOperation: false);

	private static string ReadErrorClass(CallToolResult result) =>
		result?.StructuredContent is { } structured
			? JsonNode.Parse(structured.GetRawText())?["error-class"]?.GetValue<string>()
			: null;

	private static async Task WaitUntilAsync(Func<bool> condition) {
		long startedAt = Stopwatch.GetTimestamp();
		while (!condition() && Stopwatch.GetElapsedTime(startedAt) < AssertionTimeout) {
			await Task.Delay(10).ConfigureAwait(false);
		}
	}

	private StickyFixture CreateFixture(int concurrencyCap, TimeSpan? completionLinger = null,
		TimeSpan? handshakeDelay = null, TimeProvider clock = null, TimeSpan? supervisionInterval = null) {
		// The delay is a STATED arrangement, not a sleep in a test: it holds a starter inside the
		// spawn-to-register window long enough for a second starter of the same key to be there too, which
		// is the only condition under which the lost race happens at all.
		PipedContainment containment = new(handshakeDelay ?? TimeSpan.Zero);
		WorkerProcessSupervisor supervisor = new(_logger, _processExecutor, containment, _pathProvider,
			_staleWorkers, concurrencyCap, ShortQueueWaitBound);
		// The clock is the registry's OWN seam: it judges expiry and schedules the lifetime deadline on
		// it. Left null here, every existing case keeps the real clock and the real deadline, so nothing
		// below is arranged by a fake unless it says so. The supervision interval is stated the same way:
		// only a case about noticing a RETIRED session pays for it, and it states its own cadence rather
		// than waiting out the shipped fifteen seconds.
		StickyWorkerRegistry stickyWorkers = new(_logger, clock, supervisionInterval);
		SharedResourceReservation reservations = new();
		StickyWorkerPoll poll = new(supervisor, stickyWorkers, _logger);
		McpWorkerCallDispatcher dispatcher = new(supervisor, new WorkerChildTransportOwner(),
			new WorkerMcpRelay(_logger), _settingsRepository, stickyWorkers, poll, reservations,
			_commandResolver, _logger, TimeSpan.FromSeconds(30), stageEventSilenceBound: null,
			postTerminalExitGrace: null, stickyCallBudget: null,
			// Zero by default here so a completed worker is reaped by the very next dispatch's sweep. The
			// shipped window is minutes, and waiting it out would make every reap assertion a timing test.
			stickyCompletionLinger: completionLinger ?? TimeSpan.Zero);
		return new StickyFixture(dispatcher, supervisor, containment, stickyWorkers, reservations, _logger);
	}

	/// <summary>
	/// Everything one sticky scenario needs, wired the way production wires it.
	/// </summary>
	private sealed class StickyFixture : IDisposable {

		private readonly McpWorkerCallDispatcher _dispatcher;
		private readonly ILogger _logger;

		internal StickyFixture(McpWorkerCallDispatcher dispatcher, WorkerProcessSupervisor supervisor,
			PipedContainment containment, StickyWorkerRegistry stickyWorkers,
			SharedResourceReservation reservations, ILogger logger) {
			_dispatcher = dispatcher;
			_logger = logger;
			Supervisor = supervisor;
			Containment = containment;
			StickyWorkers = stickyWorkers;
			Reservations = reservations;
			Client = new RecordingParentSession();
		}

		internal WorkerProcessSupervisor Supervisor { get; }

		internal PipedContainment Containment { get; }

		internal StickyWorkerRegistry StickyWorkers { get; }

		internal SharedResourceReservation Reservations { get; }

		internal RecordingParentSession Client { get; }

		internal IReadOnlyList<ScriptedChild> Children => Containment.Children;

		internal async Task<CallToolResult> DispatchAsync(string toolName,
			McpToolExecutionMetadata metadata, string environmentName) =>
			await _dispatcher.DispatchAsync(
				new McpExecutionRoute(toolName, McpToolExecutionLocation.Worker,
					McpExecutionDisposition.Worker, metadata),
				new CallToolRequestParams {
					Name = toolName,
					Arguments = new Dictionary<string, JsonElement> {
						["args"] = JsonSerializer.SerializeToElement(
							new Dictionary<string, string> { ["environment-name"] = environmentName })
					}
				},
				Client,
				CancellationToken.None);

		/// <summary>
		/// Dispatches a routed call with params the test built itself, so a call shape other than the
		/// resident one can be driven end to end.
		/// </summary>
		/// <param name="toolName">The tool the ROUTE resolved to (the inner tool for an executor call).</param>
		/// <param name="metadata">The declared execution metadata for that tool.</param>
		/// <param name="parameters">The params exactly as the client sent them.</param>
		/// <returns>The dispatch result.</returns>
		internal async Task<CallToolResult> DispatchWithParamsAsync(string toolName,
			McpToolExecutionMetadata metadata, CallToolRequestParams parameters) =>
			await _dispatcher.DispatchAsync(
				new McpExecutionRoute(toolName, McpToolExecutionLocation.Worker,
					McpExecutionDisposition.Worker, metadata),
				parameters,
				Client,
				CancellationToken.None);

		internal async Task<CallToolResult> DispatchWithArgumentsAsync(string toolName,
			McpToolExecutionMetadata metadata, IReadOnlyDictionary<string, string> arguments) =>
			await _dispatcher.DispatchAsync(
				new McpExecutionRoute(toolName, McpToolExecutionLocation.Worker,
					McpExecutionDisposition.Worker, metadata),
				new CallToolRequestParams {
					Name = toolName,
					Arguments = arguments.ToDictionary(
						pair => pair.Key, pair => JsonSerializer.SerializeToElement(pair.Value))
				},
				Client,
				CancellationToken.None);

		/// <summary>
		/// Builds an entry over the SAME live worker with an expiry already in the past, so the sweep can
		/// be exercised without waiting out a bound that is half an hour by derivation.
		/// </summary>
		/// <param name="live">A registered, live entry to borrow the worker from.</param>
		/// <returns>The expired entry.</returns>
		internal StickyWorkerEntry CreateExpiredEntryFrom(StickyWorkerEntry live) =>
			new(live.Lease, live.Session, live.StandardError, DateTimeOffset.UtcNow.AddMinutes(-1),
				reservation: null, Reservations, _logger);

		/// <summary>
		/// Ends this scenario: the registry's own scheduling first, the scripted children second.
		/// </summary>
		/// <remarks>
		/// <b>The order is what keeps one case out of the next one's way.</b> A registry left running owns
		/// a lifetime deadline and one supervision watcher per worker it registered; killing the children
		/// first would wake every one of those watchers at once, in the middle of whichever case runs
		/// afterwards, to reap workers whose scenario is over. Stopping the registry first cancels them,
		/// so a finished case contributes no background work to the ones behind it.
		/// </remarks>
		public void Dispose() {
			StickyWorkers.Dispose();
			Containment.Dispose();
		}
	}

	/// <summary>
	/// Containment that hands the REAL supervisor a contained worker whose three streams are ordinary
	/// pipes, with a scripted JSON-RPC server on the other end.
	/// </summary>
	/// <remarks>
	/// The point of paying for real pipes rather than substituting the supervisor is that the two things
	/// this story changes — admission accounting and what happens on the worker's own message stream —
	/// only interact when both are real. A substituted supervisor cannot saturate a pool, and a
	/// substituted session cannot deliver a completion signal through a read loop.
	/// </remarks>
	private sealed class PipedContainment : IProcessContainment, IDisposable {

		private readonly ConcurrentBag<ScriptedChild> _children = [];
		private readonly List<ScriptedChild> _ordered = [];
		private readonly object _gate = new();
		private readonly TimeSpan _handshakeDelay;
		private int _nextProcessId = 20_000;

		internal PipedContainment(TimeSpan handshakeDelay) => _handshakeDelay = handshakeDelay;

		public bool OwnsProcessCreation => true;

		internal int LaunchCount {
			get {
				lock (_gate) {
					return _ordered.Count;
				}
			}
		}

		internal IReadOnlyList<ScriptedChild> Children {
			get {
				lock (_gate) {
					return [.. _ordered];
				}
			}
		}

		public IContainedWorker Launch(WorkerLaunchRequest request) {
			ScriptedChild child = new(Interlocked.Increment(ref _nextProcessId), _handshakeDelay);
			_children.Add(child);
			lock (_gate) {
				_ordered.Add(child);
			}
			child.Start();
			return child;
		}

		public IContainedWorker Adopt(IWorkerProcessHandle startedProcess) =>
			throw new NotSupportedException();

		public WorkerTerminationOutcome TerminateOrphan(IWorkerProcessHandle orphan) =>
			WorkerTerminationOutcome.FallbackTreeKilled;

		public void Dispose() {
			foreach (ScriptedChild child in _children) {
				child.Dispose();
			}
		}
	}

	/// <summary>
	/// A worker that speaks just enough MCP to be relayed to, plus the one thing this story adds: it can
	/// send the private completion signal on demand.
	/// </summary>
	private sealed class ScriptedChild : IContainedWorker {

		private readonly AnonymousPipeServerStream _parentToChildReader;
		private readonly AnonymousPipeClientStream _parentToChildWriter;
		private readonly AnonymousPipeServerStream _childToParentWriter;
		private readonly AnonymousPipeClientStream _childToParentReader;
		private readonly TaskCompletionSource<bool> _exited =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly SemaphoreSlim _writeGate = new(1, 1);
		private readonly TimeSpan _handshakeDelay;
		private StreamWriter _toParent;
		private int _callCount;

		internal ScriptedChild(int processId, TimeSpan handshakeDelay) {
			_handshakeDelay = handshakeDelay;
			ProcessId = processId;
			StartTimeUtc = DateTime.UtcNow;
			_parentToChildReader = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
			_parentToChildWriter =
				new AnonymousPipeClientStream(PipeDirection.Out, _parentToChildReader.GetClientHandleAsString());
			_childToParentWriter = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None);
			_childToParentReader =
				new AnonymousPipeClientStream(PipeDirection.In, _childToParentWriter.GetClientHandleAsString());
		}

		public int ProcessId { get; }

		public DateTime StartTimeUtc { get; }

		public string ExecutablePath => "/scripted/clio";

		public Stream StandardInput => _parentToChildWriter;

		public Stream StandardOutput => _childToParentReader;

		// Stream.Null so the standard-error drain reaches end of stream at once; a stream that never
		// completed would cost every test here the drain's stop bound.
		public Stream StandardError => Stream.Null;

		public bool HasExited => _exited.Task.IsCompleted;

		public int? ExitCode => HasExited ? 0 : null;

		/// <summary>Gets how many <c>tools/call</c> requests this child answered.</summary>
		internal int CallCount => Volatile.Read(ref _callCount);

		internal void Start() => _ = Task.Run(RunAsync, CancellationToken.None);

		/// <summary>Sends the private completion signal, as a finished long operation would.</summary>
		/// <param name="family">The family whose work has ended.</param>
		/// <param name="exitCode">The exit code to report.</param>
		/// <returns>A task that completes when the signal has been written.</returns>
		internal async Task SendCompletionSignalAsync(McpToolOperationFamily family, int exitCode) {
			WorkerOperationCompletedParams payload =
				WorkerOperationSignalContract.BuildParams(family, exitCode);
			await WriteAsync(new JsonObject {
				["jsonrpc"] = "2.0",
				["method"] = WorkerOperationSignalContract.NotificationMethod,
				["params"] = JsonSerializer.SerializeToNode(payload)
			}).ConfigureAwait(false);
		}

		public Task WaitForExitAsync(CancellationToken cancellationToken) =>
			_exited.Task.WaitAsync(cancellationToken);

		/// <summary>
		/// Ends the process WITHOUT touching its pipes, as a worker that crashed does from the parent's
		/// point of view.
		/// </summary>
		/// <remarks>
		/// Deliberately not <see cref="Kill"/> and deliberately not <see cref="Dispose"/>: this must be the
		/// PROCESS ending and nothing else, so a case about noticing an exit cannot be satisfied by a
		/// session that was torn down at the same moment. Leaving the pipes open keeps the relay session
		/// whole, which is what separates the exit signal from retirement.
		/// </remarks>
		internal void SimulateProcessExit() => _exited.TrySetResult(true);

		public WorkerTerminationOutcome Kill() {
			_exited.TrySetResult(true);
			return WorkerTerminationOutcome.ContainedJobTerminated;
		}

		public void Dispose() {
			_exited.TrySetResult(true);
			_parentToChildWriter.Dispose();
			_parentToChildReader.Dispose();
			_childToParentReader.Dispose();
			_childToParentWriter.Dispose();
			_writeGate.Dispose();
		}

		private async Task RunAsync() {
			try {
				using StreamReader fromParent = new(_parentToChildReader);
				_toParent = new StreamWriter(_childToParentWriter) { AutoFlush = true, NewLine = "\n" };
				string line;
				while ((line = await fromParent.ReadLineAsync().ConfigureAwait(false)) is not null) {
					await AnswerAsync(line).ConfigureAwait(false);
				}
			}
			catch (Exception) {
				// The parent closing its end IS how a worker's standard input ends.
			}
		}

		private async Task AnswerAsync(string line) {
			JsonNode request = JsonNode.Parse(line);
			string method = request?["method"]?.GetValue<string>();
			if (method == "initialize") {
				if (_handshakeDelay > TimeSpan.Zero) {
					// A worker that takes a moment to come up: p50 spawn plus initialize measured 2.763 s on
					// Windows Server 2022 (ADR 2.4), so a starter really does sit in this window.
					await Task.Delay(_handshakeDelay).ConfigureAwait(false);
				}
				await WriteAsync(new JsonObject {
					["jsonrpc"] = "2.0",
					["id"] = request["id"]?.DeepClone(),
					["result"] = new JsonObject {
						["protocolVersion"] = WorkerRelayOptions.MeasuredProtocolVersion,
						["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
						["serverInfo"] = new JsonObject { ["name"] = "scripted-worker", ["version"] = "1" }
					}
				}).ConfigureAwait(false);
				return;
			}
			if (method != "tools/call") {
				return;
			}
			Interlocked.Increment(ref _callCount);
			await WriteAsync(new JsonObject {
				["jsonrpc"] = "2.0",
				["id"] = request["id"]?.DeepClone(),
				["result"] = new JsonObject {
					["content"] = new JsonArray(new JsonObject {
						["type"] = "text",
						["text"] = "{\"success\":true}"
					}),
					["isError"] = false
				}
			}).ConfigureAwait(false);
		}

		private async Task WriteAsync(JsonObject message) {
			await _writeGate.WaitAsync().ConfigureAwait(false);
			try {
				if (_toParent is not null) {
					await _toParent.WriteLineAsync(message.ToJsonString()).ConfigureAwait(false);
				}
			}
			catch (Exception) {
				// A torn-down pipe is how this child's life ends, not a failure of the test.
			}
			finally {
				_writeGate.Release();
			}
		}
	}

	/// <summary>
	/// The real client leg, recording what the relay forwarded so a PRIVATE signal that leaked can be
	/// seen.
	/// </summary>
	private sealed class RecordingParentSession : IParentMcpSession {

		private readonly ConcurrentQueue<string> _methods = new();

		public bool SupportsSampling => false;

		internal IReadOnlyCollection<string> ForwardedMethods => [.. _methods];

		public Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken) {
			if (message is JsonRpcNotification notification) {
				_methods.Enqueue(notification.Method);
			}
			return Task.CompletedTask;
		}

#pragma warning disable MCP9005
		public ValueTask<CreateMessageResult> SampleAsync(CreateMessageRequestParams requestParams,
			CancellationToken cancellationToken) =>
			throw new NotSupportedException();
#pragma warning restore MCP9005
	}

	/// <summary>
	/// Builds ONE registered sticky worker over a scripted transport, with the real relay, the real
	/// registry and the real poll around it.
	/// </summary>
	/// <param name="transport">The scripted child transport.</param>
	/// <returns>The harness.</returns>
	/// <remarks>
	/// Hermetic rather than piped on purpose. What these three cases turn on is a STATE - whether the send
	/// completed - and a transport that blocks <c>tools/call</c> on the caller's token produces it exactly.
	/// The byte-level half frame that state stands for is already pinned over real pipes by
	/// <c>WorkerMcpRelayTests.RequestAsync_ShouldRetireTheSession_WhenASendWasCancelledMidFrame</c>, and
	/// reproducing it here would buy a second copy of that proof rather than a second property.
	/// </remarks>
	private async Task<PollHarness> CreatePollHarnessAsync(ScriptedPollTransport transport) {
		WorkerMcpRelay relay = new(_logger);
		WorkerRelaySession session = await relay.OpenAsync(transport, new RecordingParentSession(),
			new WorkerRelayOptions { LivenessProbeTimeout = ProbeTimeout }, CancellationToken.None);
		IWorkerLease lease = Substitute.For<IWorkerLease>();
		lease.HasExited.Returns(false);
		StrongBox<int> disposals = new(0);
		lease.When(disposed => disposed.Dispose()).Do(_ => disposals.Value++);
		IWorkerReach reach = Substitute.For<IWorkerReach>();
		reach.ReachExisting(Arg.Any<IWorkerLease>()).Returns(lease);
		StickyWorkerRegistry registry = new(_logger);
		StickyWorkerEntry entry = new(lease, session, new WorkerStandardErrorDrain(null, 1024),
			DateTimeOffset.UtcNow.AddMinutes(30), reservation: null, new SharedResourceReservation(), _logger);
		registry.TryRegister(PollKey, entry).Should().BeTrue(
			because: "every case below starts from ONE registered sticky worker, and a harness that registered none would make them all vacuous");
		return new PollHarness(new StickyWorkerPoll(reach, registry, _logger), registry, entry, disposals);
	}

	/// <summary>One registered sticky worker and everything a case needs to state what happened to it.</summary>
	private sealed class PollHarness : IAsyncDisposable {

		private readonly StickyWorkerEntry _entry;
		private readonly StrongBox<int> _leaseDisposals;

		internal PollHarness(StickyWorkerPoll poll, StickyWorkerRegistry registry, StickyWorkerEntry entry,
			StrongBox<int> leaseDisposals) {
			Poll = poll;
			Registry = registry;
			_entry = entry;
			_leaseDisposals = leaseDisposals;
		}

		internal StickyWorkerPoll Poll { get; }

		internal StickyWorkerRegistry Registry { get; }

		/// <summary>Gets how often the worker's lease was disposed - the only thing that returns its slot.</summary>
		internal int LeaseDisposals => _leaseDisposals.Value;

		public async ValueTask DisposeAsync() => await _entry.ReleaseAsync().ConfigureAwait(false);
	}

	/// <summary>
	/// A scripted child transport whose ONE interesting property is whether a <c>tools/call</c> send is
	/// allowed to complete.
	/// </summary>
	/// <remarks>
	/// Blocking the send on the caller's own token reproduces the state ADR 3.2a is about without a pipe:
	/// the relay's <c>sent</c> flag stays false, so it retires the session before it rethrows, exactly as it
	/// does when a real newline write is abandoned mid-frame.
	/// </remarks>
	private sealed class ScriptedPollTransport : ITransport {

		private readonly Channel<JsonRpcMessage> _fromChild =
			Channel.CreateUnbounded<JsonRpcMessage>(new UnboundedChannelOptions { SingleReader = true });
		private readonly List<string> _sentMethods = [];
		private readonly object _sentLock = new();
		private int _toolCallSendsStarted;
		private int _toolCallSendsCancelled;

		/// <summary>Gets or sets a value indicating whether a <c>tools/call</c> send never completes.</summary>
		internal bool BlockToolCallSends { get; set; }

		/// <summary>Gets or sets a value indicating whether <c>tools/call</c> is answered.</summary>
		internal bool AnswerToolCalls { get; set; } = true;

		/// <summary>Gets or sets a value indicating whether <c>tools/list</c> is answered.</summary>
		internal bool AnswerToolsList { get; set; } = true;

		/// <summary>Gets how many <c>tools/call</c> sends were begun.</summary>
		internal int ToolCallSendsStarted => Volatile.Read(ref _toolCallSendsStarted);

		/// <summary>Gets how many <c>tools/call</c> sends were abandoned part-way.</summary>
		internal int ToolCallSendsCancelled => Volatile.Read(ref _toolCallSendsCancelled);

		/// <summary>Gets every method the relay has sent, in order.</summary>
		internal IReadOnlyList<string> SentMethods {
			get {
				lock (_sentLock) {
					return [.. _sentMethods];
				}
			}
		}

		public string SessionId => "sticky-poll-fake";

		public ChannelReader<JsonRpcMessage> MessageReader => _fromChild.Reader;

		public async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken) {
			string method = message switch {
				JsonRpcRequest request => request.Method,
				JsonRpcNotification notification => notification.Method,
				_ => null
			};
			if (method is not null) {
				lock (_sentLock) {
					_sentMethods.Add(method);
				}
			}
			if (message is not JsonRpcRequest sent) {
				return;
			}
			if (string.Equals(sent.Method, CallToolMethodName, StringComparison.Ordinal)) {
				Interlocked.Increment(ref _toolCallSendsStarted);
				if (BlockToolCallSends) {
					try {
						await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
					}
					catch (OperationCanceledException) {
						Interlocked.Increment(ref _toolCallSendsCancelled);
						throw;
					}
					return;
				}
			}
			Answer(sent);
		}

		public ValueTask DisposeAsync() {
			_fromChild.Writer.TryComplete();
			return default;
		}

		private void Answer(JsonRpcRequest request) {
			JsonNode result = request.Method switch {
				"initialize" => new JsonObject {
					["protocolVersion"] = WorkerRelayOptions.MeasuredProtocolVersion,
					["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
					["serverInfo"] = new JsonObject { ["name"] = "sticky-poll-fake", ["version"] = "1" }
				},
				ListToolsMethodName when AnswerToolsList =>
					JsonSerializer.SerializeToNode(new ListToolsResult { Tools = [] },
						McpJsonUtilities.DefaultOptions),
				CallToolMethodName when AnswerToolCalls =>
					JsonSerializer.SerializeToNode(new CallToolResult { Content = [] },
						McpJsonUtilities.DefaultOptions),
				_ => null
			};
			if (result is not null) {
				_fromChild.Writer.TryWrite(new JsonRpcResponse { Id = request.Id, Result = result });
			}
		}
	}

	// ---------------------------------------------------------------------------------------------
	// Deadline-scheduling test seams
	// ---------------------------------------------------------------------------------------------

	/// <summary>
	/// One hand-built registry entry and the only observation that says it was really ended.
	/// </summary>
	/// <param name="Entry">The entry.</param>
	/// <param name="Disposals">
	/// How often its lease was disposed — the ONE thing that kills the process and returns its admission
	/// slot, so it is what a reap is asserted by rather than the dictionary having forgotten it.
	/// </param>
	/// <param name="Exited">
	/// Whether the worker's process has ended. Writable, so a case can kill a worker at the moment it
	/// means to rather than at the moment a substitute was configured.
	/// </param>
	/// <param name="LivenessReads">
	/// How often anything has asked this lease whether its worker is still there. It is the only
	/// externally visible trace a supervision watcher leaves while it is running: a reap it performs on an
	/// entry already reaped is absorbed by an idempotent release and proves nothing, so "is somebody still
	/// looking at this worker" is the question a stray-watcher case has to ask.
	/// </param>
	private sealed record RegistryEntryProbe(StickyWorkerEntry Entry, StrongBox<int> Disposals,
		StrongBox<bool> Exited, StrongBox<int> LivenessReads);

	/// <summary>
	/// Builds a registered-shape sticky entry with a stated expiry, over a scripted transport and a
	/// substituted lease.
	/// </summary>
	/// <param name="expiresAtUtc">The lifetime bound this entry carries.</param>
	/// <param name="reservations">The reservation registry its release runs against.</param>
	/// <returns>The entry and its disposal counter.</returns>
	/// <remarks>
	/// The scheduling cases turn on WHEN the registry acts, not on what a worker says, so a scripted
	/// transport is the whole child they need. The piped fixture above is what proves the same behaviour
	/// against real admission accounting.
	/// </remarks>
	private async Task<RegistryEntryProbe> CreateRegistryEntryAsync(DateTimeOffset expiresAtUtc,
		ISharedResourceReservation reservations) {
		WorkerMcpRelay relay = new(_logger);
		WorkerRelaySession session = await relay.OpenAsync(new ScriptedPollTransport(),
			new RecordingParentSession(), new WorkerRelayOptions { LivenessProbeTimeout = ProbeTimeout },
			CancellationToken.None);
		IWorkerLease lease = Substitute.For<IWorkerLease>();
		StrongBox<bool> exited = new(false);
		StrongBox<int> livenessReads = new(0);
		// A counted, writable answer rather than a fixed one: a case needs to end this worker DURING the
		// act, and it needs to be able to state whether anything is still watching it afterwards.
		lease.HasExited.Returns(_ => {
			Interlocked.Increment(ref livenessReads.Value);
			return Volatile.Read(ref exited.Value);
		});
		StrongBox<int> disposals = new(0);
		lease.When(disposed => disposed.Dispose()).Do(_ => Interlocked.Increment(ref disposals.Value));
		return new RegistryEntryProbe(
			new StickyWorkerEntry(lease, session, new WorkerStandardErrorDrain(null, 1024), expiresAtUtc,
				reservation: null, reservations, _logger),
			disposals, exited, livenessReads);
	}

	/// <summary>
	/// A clock that tracks real time through an OFFSET the test moves, and whose timers fire only when it
	/// moves them.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Offset-based rather than frozen on purpose. The registry judges expiry against this clock, but
	/// <see cref="StickyWorkerEntry.MarkCompleted"/> stamps its linger against the real one, so a clock
	/// frozen at construction would place every completion in its own future and a case about lingers
	/// would pass for the wrong reason. Tracking real time keeps the two agreed to within the
	/// milliseconds a test takes, while <see cref="Advance"/> still moves half an hour in no time at all.
	/// </para>
	/// <para>
	/// Callbacks are invoked with NO lock of this type held, which is what keeps the fake out of the lock
	/// order: the registry arms the timer while holding its own gate, so a fake that fired callbacks
	/// under its own lock would deadlock the very code it is here to observe.
	/// </para>
	/// </remarks>
	private sealed class ControllableClock : TimeProvider {

		private readonly List<ControllableTimer> _timers = [];
		private readonly object _gate = new();
		private long _offsetTicks;

		public override DateTimeOffset GetUtcNow() =>
			DateTimeOffset.UtcNow + TimeSpan.FromTicks(Interlocked.Read(ref _offsetTicks));

		public override ITimer CreateTimer(TimerCallback callback, object state, TimeSpan dueTime,
			TimeSpan period) {
			ControllableTimer timer = new(this, callback, state);
			// ARMED BEFORE it is published, and the order is a flake fix rather than tidiness. A timer
			// created on a pool thread — every supervision cadence is — could otherwise be seen by an
			// Advance running between the two statements, be found unarmed, and then arm itself against the
			// ALREADY-ADVANCED clock, so a single Advance would silently fail to fire it. Publishing last
			// makes "registered" mean "armed".
			timer.Change(dueTime, period);
			lock (_gate) {
				_timers.Add(timer);
			}
			return timer;
		}

		/// <summary>
		/// Gets the registry's lifetime-deadline timer — the FIRST timer created on this clock.
		/// </summary>
		/// <remarks>
		/// It is the first because the registry creates it in its constructor, before any entry exists.
		/// The list is no longer a single timer: each supervised entry parks on a cadence delay that also
		/// runs on this clock, and <see cref="Advance"/> fires those too. That is intended — the cadence is
		/// how a RETIRED session is noticed — and it is the reason this accessor names the one timer these
		/// scheduling cases are about instead of asserting there is only one.
		/// </remarks>
		internal ControllableTimer DeadlineTimer {
			get {
				lock (_gate) {
					return _timers[0];
				}
			}
		}

		/// <summary>
		/// Gets how many timers have been created on this clock — and therefore ARMED, since a timer is
		/// published only after it has been.
		/// </summary>
		/// <remarks>
		/// Read by a case that must move the clock past a timer armed on a POOL thread: advancing before
		/// that timer exists fires nothing, and the case would then wait out its whole assertion timeout
		/// for a pass it prevented itself.
		/// </remarks>
		internal int TimerCount {
			get {
				lock (_gate) {
					return _timers.Count;
				}
			}
		}

		/// <summary>Moves the clock forward and fires every timer whose deadline that passed.</summary>
		/// <param name="delta">How far forward.</param>
		internal void Advance(TimeSpan delta) {
			Interlocked.Add(ref _offsetTicks, delta.Ticks);
			ControllableTimer[] timers;
			lock (_gate) {
				timers = [.. _timers];
			}
			DateTimeOffset now = GetUtcNow();
			foreach (ControllableTimer timer in timers) {
				timer.FireIfDue(now);
			}
		}
	}

	/// <summary>A one-shot timer that fires only when its clock is advanced past its deadline.</summary>
	private sealed class ControllableTimer : ITimer {

		private readonly ControllableClock _clock;
		private readonly TimerCallback _callback;
		private readonly object _state;
		private readonly object _gate = new();
		private DateTimeOffset? _dueAt;
		private TimeSpan? _armedDelay;
		private bool _disposed;

		internal ControllableTimer(ControllableClock clock, TimerCallback callback, object state) {
			_clock = clock;
			_callback = callback;
			_state = state;
		}

		/// <summary>Gets the delay this timer was last armed with, or null when it is disarmed.</summary>
		internal TimeSpan? ArmedDelay {
			get {
				lock (_gate) {
					return _armedDelay;
				}
			}
		}

		/// <summary>Gets a value indicating whether this timer has been disposed.</summary>
		internal bool IsDisposed {
			get {
				lock (_gate) {
					return _disposed;
				}
			}
		}

		public bool Change(TimeSpan dueTime, TimeSpan period) {
			DateTimeOffset now = _clock.GetUtcNow();
			lock (_gate) {
				if (_disposed) {
					return false;
				}
				if (dueTime == Timeout.InfiniteTimeSpan) {
					_dueAt = null;
					_armedDelay = null;
					return true;
				}
				_dueAt = now + dueTime;
				_armedDelay = dueTime;
				return true;
			}
		}

		public void Dispose() {
			lock (_gate) {
				_disposed = true;
				_dueAt = null;
				_armedDelay = null;
			}
		}

		public ValueTask DisposeAsync() {
			Dispose();
			return ValueTask.CompletedTask;
		}

		internal void FireIfDue(DateTimeOffset now) {
			lock (_gate) {
				if (_disposed || _dueAt is null || now < _dueAt.Value) {
					return;
				}
				_dueAt = null;
			}
			_callback(_state);
		}
	}

	[Test]
	[Description("restart-by-environment-name declares a bare `string environmentName` parameter, so the SDK reflects it camelCased — the starter must resolve the SAME sticky key its own restart-status poller does, or the poll answers 'not found' for a restart that is running.")]
	public void ReadTargetOptions_ShouldResolveTheTarget_WhenTheArgumentIsCamelCased() {
		// Arrange — the two spellings the catalog actually emits: a record with
		// [JsonPropertyName("environment-name")], and a bare method parameter reflected as camelCase.
		CallToolRequestParams kebab = new() {
			Name = "restart-status",
			Arguments = new Dictionary<string, JsonElement> {
				["environment-name"] = JsonSerializer.SerializeToElement("sandbox")
			}
		};
		CallToolRequestParams camel = new() {
			Name = "restart-by-environment-name",
			Arguments = new Dictionary<string, JsonElement> {
				["environmentName"] = JsonSerializer.SerializeToElement("sandbox")
			}
		};

		// Act
		EnvironmentOptions fromKebab =
			McpWorkerCallDispatcher.ReadTargetOptions(kebab, "restart-status");
		EnvironmentOptions fromCamel =
			McpWorkerCallDispatcher.ReadTargetOptions(camel, "restart-by-environment-name");

		// Assert
		fromCamel.Environment.Should().Be("sandbox",
			because: "the starter's own schema spells it camelCase, and failing to read it registers the worker under an unresolved key that its poller can never reach");
		fromCamel.Environment.Should().Be(fromKebab.Environment,
			because: "the starter and the poller of ONE family must derive the same target, or a running restart is reported as not found and two environments share one key");
	}
}
