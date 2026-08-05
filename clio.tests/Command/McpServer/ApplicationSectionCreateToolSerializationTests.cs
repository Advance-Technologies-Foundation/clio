using System;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// ENG-94418 (R2/R3): regression proof that two concurrent SAME-tenant <c>create-app-section</c>
/// executions never overlap — the destructive create runs under the per-tenant MCP execution lock
/// (<see cref="McpToolExecutionLock"/> / <see cref="TenantExecutionLockProvider"/>) that
/// <see cref="BaseTool{T}"/> takes via <c>ExecuteWithCleanLog</c>. Concurrent same-tenant creates are
/// exactly the driver of the schema-manager cache poisoning: if clio issued two racing section creates,
/// one could be abandoned mid-flight and leave the server cache holding a phantom. This test pins the
/// serialization so it cannot silently regress.
/// <para>
/// Deterministic by construction: a fake <see cref="IApplicationSectionCreateService"/> records the number
/// of executions active at once and coordinates with latches / a barrier — never sleeps-as-synchronization
/// (the bounded waits are correctness bounds, not pacing). Modelled on
/// <see cref="TenantLockConcurrencyTests"/> but drives the REAL create-app-section tool path, so it fails
/// if the tenant lock around the create path is ever removed.
/// </para>
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class ApplicationSectionCreateToolSerializationTests {

	// Correctness bound for latch/barrier waits — generous so a slow CI worker never flakes; it is NOT a
	// pacing sleep (the events fire the instant the condition holds).
	private static readonly TimeSpan Generous = TimeSpan.FromSeconds(10);

	// Window within which a lock-blocked second caller must NOT have entered the critical section while the
	// first still holds the lock. Mirrors TenantLockConcurrencyTests' 500 ms same-tenant negative check.
	private static readonly TimeSpan BlockedWindow = TimeSpan.FromMilliseconds(500);

	private static ApplicationSectionCreateArgs CreateArgs(string environmentName) =>
		new(ApplicationCode: "UsrOrdersApp", Caption: "Orders", EnvironmentName: environmentName);

	private static IToolCommandResolver ResolverReturning(EnvironmentSettings settings) {
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<EnvironmentSettings>(Arg.Any<EnvironmentOptions>()).Returns(settings);
		return resolver;
	}

	[Test]
	[Category("Unit")]
	[Description("R2/R3: two concurrent create-app-section calls for the SAME tenant serialize — while the first holds the per-tenant execution lock the second cannot enter the create service, so at most one create is ever active; removing the lock would let both run at once and fail this test.")]
	public async Task ApplicationSectionCreate_ShouldSerializeSameTenant_WhenTwoConcurrentCreatesShareTenantKey() {
		// Arrange — both calls resolve to the SAME per-tenant lock key, so they must serialize. A GUID
		// suffix isolates this test from the process-global TenantExecutionLockProvider.Shared used elsewhere.
		string sharedTenantKey = "eng94418-serialize-" + Guid.NewGuid();
		IToolCommandResolver commandResolver = ResolverReturning(new EnvironmentSettings { Uri = "https://tenant.example.com" });
		commandResolver.GetTenantKey(Arg.Any<EnvironmentOptions>()).Returns(sharedTenantKey);

		using ManualResetEventSlim firstInside = new(false);
		using ManualResetEventSlim firstMayRelease = new(false);
		using ManualResetEventSlim secondInside = new(false);
		int entries = 0;
		var probe = new ConcurrencyProbeSectionCreateService(() => {
			// Runs while the caller holds the per-tenant lock. The first entrant parks here (still holding
			// the lock) so a second concurrent entrant WOULD overlap if the lock were absent.
			if (Interlocked.Increment(ref entries) == 1) {
				firstInside.Set();
				firstMayRelease.Wait(Generous);
			} else {
				secondInside.Set();
			}
		});
		var tool = new ApplicationSectionCreateTool(Substitute.For<ILogger>(), commandResolver, probe);

		// Act
		Task<ApplicationSectionContextResponse> first = tool.ApplicationSectionCreate(CreateArgs("sandbox"), server: null);
		firstInside.Wait(Generous).Should().BeTrue(
			because: "the first create must enter the create service (under the tenant lock) before the second contends");

		Task<ApplicationSectionContextResponse> second = tool.ApplicationSectionCreate(CreateArgs("sandbox"), server: null);
		bool secondEnteredWhileFirstHeld = secondInside.Wait(BlockedWindow);

		// Assert — the second cannot enter while the first holds the SAME-tenant lock.
		secondEnteredWhileFirstHeld.Should().BeFalse(
			because: "the per-tenant lock must serialize same-tenant create-app-section — the second create cannot start while the first is still running");

		// Release the first; the second must then proceed, and the two never overlapped.
		firstMayRelease.Set();
		(await Task.WhenAll(first, second)).Should().OnlyContain(response => response.Success,
			because: "both serialized creates complete successfully once the lock is released in turn");
		secondInside.IsSet.Should().BeTrue(
			because: "once the first releases the tenant lock the second acquires it and runs");
		probe.MaxConcurrentExecutions.Should().Be(1,
			because: "the per-tenant lock guarantees at most one same-tenant create is ever active at once (R2/R3 regression boundary)");
	}

	[Test]
	[Category("Unit")]
	[Description("Per-tenant, not global: two concurrent create-app-section calls for DIFFERENT tenants run at the same time — both are inside the create service concurrently. This proves the active-execution counter genuinely detects overlap, so the same-tenant serialization result above is not vacuous.")]
	public async Task ApplicationSectionCreate_ShouldRunConcurrently_WhenTwoCreatesUseDifferentTenantKeys() {
		// Arrange — distinct tenant keys per call, so the per-tenant lock does NOT serialize them.
		string tenantKeyA = "eng94418-concurrent-A-" + Guid.NewGuid();
		string tenantKeyB = "eng94418-concurrent-B-" + Guid.NewGuid();
		IToolCommandResolver commandResolver = ResolverReturning(new EnvironmentSettings { Uri = "https://tenant.example.com" });
		commandResolver.GetTenantKey(Arg.Is<EnvironmentOptions>(o => o.Environment == "tenantA")).Returns(tenantKeyA);
		commandResolver.GetTenantKey(Arg.Is<EnvironmentOptions>(o => o.Environment == "tenantB")).Returns(tenantKeyB);

		using Barrier bothInside = new(2);
		bool firstReachedRendezvous = false;
		bool secondReachedRendezvous = false;
		int entries = 0;
		var probe = new ConcurrencyProbeSectionCreateService(() => {
			// Each entrant rendezvous with the other while inside the create service. If the two locks
			// serialized, only one could be inside at a time and the barrier would time out.
			bool reached = bothInside.SignalAndWait(Generous);
			if (Interlocked.Increment(ref entries) == 1) {
				firstReachedRendezvous = reached;
			} else {
				secondReachedRendezvous = reached;
			}
		});
		var tool = new ApplicationSectionCreateTool(Substitute.For<ILogger>(), commandResolver, probe);

		// Act — launch both concurrently.
		Task<ApplicationSectionContextResponse> a = tool.ApplicationSectionCreate(CreateArgs("tenantA"), server: null);
		Task<ApplicationSectionContextResponse> b = tool.ApplicationSectionCreate(CreateArgs("tenantB"), server: null);
		await Task.WhenAll(a, b);

		// Assert
		firstReachedRendezvous.Should().BeTrue(
			because: "tenant A runs under its own lock and is not blocked by tenant B");
		secondReachedRendezvous.Should().BeTrue(
			because: "tenant B runs under its own lock and is not blocked by tenant A");
		probe.MaxConcurrentExecutions.Should().Be(2,
			because: "different tenants are NOT serialized, so both creates are active at once — proving the counter detects real overlap");
	}

	/// <summary>
	/// Fake <see cref="IApplicationSectionCreateService"/> that records the maximum number of concurrent
	/// executions and runs a caller-supplied coordination hook while "inside" the create (i.e. while the
	/// tool holds the per-tenant lock). Only the settings-based overload — the one the MCP tool calls — is
	/// implemented; the name-based overload is never reached from the tool path.
	/// </summary>
	private sealed class ConcurrencyProbeSectionCreateService(Action onEnterCritical) : IApplicationSectionCreateService {
		private int _active;
		private int _maxActive;

		internal int MaxConcurrentExecutions => Volatile.Read(ref _maxActive);

		public ApplicationSectionCreateResult CreateSection(EnvironmentSettings environmentSettings,
			ApplicationSectionCreateRequest request, int? insertTimeoutMsOverride = null,
			int? readbackTimeoutMsOverride = null, bool enableContentionRetry = false, Action<string>? reportStage = null) {
			int active = Interlocked.Increment(ref _active);
			RecordMax(active);
			try {
				onEnterCritical();
				return BuildResult();
			} finally {
				Interlocked.Decrement(ref _active);
			}
		}

		public ApplicationSectionCreateResult CreateSection(string environmentName,
			ApplicationSectionCreateRequest request, int? insertTimeoutMsOverride = null,
			int? readbackTimeoutMsOverride = null, bool enableContentionRetry = false, Action<string>? reportStage = null) =>
			throw new NotSupportedException("The create-app-section MCP tool uses the settings-based overload only.");

		private void RecordMax(int candidate) {
			int previous;
			do {
				previous = Volatile.Read(ref _maxActive);
				if (candidate <= previous) {
					return;
				}
			} while (Interlocked.CompareExchange(ref _maxActive, candidate, previous) != previous);
		}

		private static ApplicationSectionCreateResult BuildResult() =>
			new(
				PackageUId: "pkg-uid",
				PackageName: "UsrOrdersApp",
				ApplicationId: "app-id",
				ApplicationName: "Orders App",
				ApplicationCode: "UsrOrdersApp",
				ApplicationVersion: "8.4.0",
				Section: new ApplicationSectionInfoResult(
					Id: "section-id",
					Code: "UsrOrders",
					Caption: "Orders",
					Description: null,
					EntitySchemaName: "UsrOrders",
					PackageId: "pkg-uid",
					SectionSchemaUId: null,
					IconId: null,
					IconBackground: null,
					ClientTypeId: null),
				Entity: null,
				Pages: Array.Empty<PageListItem>());
	}
}
