using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Relay;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Common.McpWorker;
using Clio.UserEnvironment;
using FluentAssertions;
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
	private const string EnvironmentName = "sandbox";

	/// <summary>
	/// Short enough that a call which QUEUED for admission instead of reaching its worker is reported as
	/// a failure rather than as a slow test.
	/// </summary>
	private static readonly TimeSpan ShortQueueWaitBound = TimeSpan.FromSeconds(2);

	/// <summary>Ceiling on every wait here; a scripted child answers in milliseconds.</summary>
	private static readonly TimeSpan AssertionTimeout = TimeSpan.FromSeconds(30);

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
		await WaitUntilAsync(() => fixture.Reservations.HeldCount == 0);
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
	[Description("A completed sticky worker is reaped as soon as the poll it was lingering for has been answered, so a finished long operation does not hold one of a small host's few sticky slots for the whole linger window.")]
	public async Task DispatchAsync_ShouldReapACompletedWorker_OnceItsStatusPollHasBeenAnswered() {
		// Arrange — the shipped linger, not a shortened one: the point is that the reap happens because the
		// poll consumed it, not because the window elapsed. Total 2 means sticky capacity is exactly 1, so
		// "the slot came back" is observable as the next long operation being admitted.
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
			because: "only the NEW long operation may be registered: the completed one had to go once its poll was served");
		nextOperation.IsError.Should().NotBeTrue(
			because: "sticky capacity here is exactly one, so a completed worker still holding its slot would refuse this call for the rest of the linger window — a host reporting itself full while nothing is running");
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
	// Helpers
	// ---------------------------------------------------------------------------------------------

	private static string Target(EnvironmentOptions options) =>
		options?.Environment ?? options?.Uri ?? "default";

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

	private StickyFixture CreateFixture(int concurrencyCap, TimeSpan? completionLinger = null) {
		PipedContainment containment = new();
		WorkerProcessSupervisor supervisor = new(_logger, _processExecutor, containment, _pathProvider,
			_staleWorkers, concurrencyCap, ShortQueueWaitBound);
		StickyWorkerRegistry stickyWorkers = new(_logger);
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

		public void Dispose() => Containment.Dispose();
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
		private int _nextProcessId = 20_000;

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
			ScriptedChild child = new(Interlocked.Increment(ref _nextProcessId));
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
		private StreamWriter _toParent;
		private int _callCount;

		internal ScriptedChild(int processId) {
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
}
