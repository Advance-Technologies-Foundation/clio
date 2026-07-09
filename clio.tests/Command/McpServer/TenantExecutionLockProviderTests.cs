using System;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// FR-05 (ENG-93208): <see cref="ITenantExecutionLockProvider"/> hands out one stable lock per cache
/// key and distinct locks for distinct keys, so the same tenant serializes and different tenants do
/// not. Keys are per-test GUID-suffixed because the provider under test is the process-wide shared
/// instance.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class TenantExecutionLockProviderTests {

	[Test]
	[Category("Unit")]
	[Description("Returns the same lock object for repeated calls with the same cache key.")]
	public void GetLock_ShouldReturnSameLock_WhenCalledTwiceWithSameKey() {
		// Arrange
		ITenantExecutionLockProvider provider = TenantExecutionLockProvider.Shared;
		string key = "tenant-" + Guid.NewGuid();

		// Act
		object first = provider.GetLock(key);
		object second = provider.GetLock(key);

		// Assert
		second.Should().BeSameAs(first,
			because: "the same cache key must always resolve to the same lock so same-tenant calls serialize");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns distinct lock objects for distinct cache keys.")]
	public void GetLock_ShouldReturnDistinctLocks_WhenKeysDiffer() {
		// Arrange
		ITenantExecutionLockProvider provider = TenantExecutionLockProvider.Shared;
		string keyA = "tenant-a-" + Guid.NewGuid();
		string keyB = "tenant-b-" + Guid.NewGuid();

		// Act
		object lockA = provider.GetLock(keyA);
		object lockB = provider.GetLock(keyB);

		// Assert
		lockB.Should().NotBeSameAs(lockA,
			because: "different cache keys must resolve to different locks so different tenants run concurrently");
	}

	[Test]
	[Category("Unit")]
	[Description("Treats cache keys case-insensitively so a single tenant identity yields one lock regardless of casing.")]
	public void GetLock_ShouldReturnSameLock_WhenKeysDifferOnlyByCase() {
		// Arrange
		ITenantExecutionLockProvider provider = TenantExecutionLockProvider.Shared;
		string suffix = Guid.NewGuid().ToString();
		string lowerKey = "tenant-" + suffix;
		string upperKey = "TENANT-" + suffix.ToUpperInvariant();

		// Act
		object lowerLock = provider.GetLock(lowerKey);
		object upperLock = provider.GetLock(upperKey);

		// Assert
		upperLock.Should().BeSameAs(lowerLock,
			because: "cache keys are compared case-insensitively, matching the session-container cache");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a null or blank cache key rather than handing back a shared surrogate lock.")]
	public void GetLock_ShouldThrow_WhenKeyIsNullOrWhitespace() {
		// Arrange
		ITenantExecutionLockProvider provider = TenantExecutionLockProvider.Shared;

		// Act
		Action nullKey = () => provider.GetLock(null);
		Action blankKey = () => provider.GetLock("   ");

		// Assert
		nullKey.Should().Throw<ArgumentException>(
			because: "a null key would silently collapse distinct tenants onto one lock");
		blankKey.Should().Throw<ArgumentException>(
			because: "a blank key would silently collapse distinct tenants onto one lock");
	}

	// M1 (ENG-93208): the lock map is bounded (idle-TTL + LRU-over-capacity) so a long-lived edge that
	// mints a fresh key per rotating passthrough token cannot grow forever. Eviction never drops a
	// mapping whose lock is currently held (would break mutual exclusion). These use the internal
	// clock/capacity seam so the sweeps are deterministic (no sleeps, isolated from the Shared instance).

	private sealed class MutableClock {
		public DateTime Now { get; set; } = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		public DateTime Read() => Now;
	}

	[Test]
	[Category("Unit")]
	[Description("Over capacity, the least-recently-used UNHELD mapping is evicted (bounded growth): re-acquiring it mints a new lock object.")]
	public void GetLock_ShouldEvictLruUnheldMapping_WhenOverCapacity() {
		// Arrange — capacity 2, advancing clock so last-access ordering is well-defined.
		MutableClock clock = new();
		var provider = new TenantExecutionLockProvider(TimeSpan.FromMinutes(30), maxEntries: 2, clock.Read);
		object lockA = provider.GetLock("A");
		clock.Now = clock.Now.AddSeconds(1);
		provider.GetLock("B");
		clock.Now = clock.Now.AddSeconds(1);

		// Act — adding a third key exceeds capacity and evicts the oldest unheld mapping (A).
		provider.GetLock("C");
		object lockAReacquired = provider.GetLock("A");

		// Assert
		lockAReacquired.Should().NotBeSameAs(lockA,
			because: "the oldest unheld mapping is evicted over capacity, so re-acquiring its key mints a new lock — the map is bounded");
	}

	[Test]
	[Category("Unit")]
	[Description("An in-use mapping is never evicted over capacity even when it is the least-recently-used: re-acquiring it returns the SAME lock object.")]
	public void GetLock_ShouldNotEvictInUseMapping_WhenOverCapacity() {
		// Arrange — A is the oldest but marked in-use; capacity 2.
		MutableClock clock = new();
		var provider = new TenantExecutionLockProvider(TimeSpan.FromMinutes(30), maxEntries: 2, clock.Read);
		object lockA = provider.GetLock("A");
		provider.MarkInUse("A");
		clock.Now = clock.Now.AddSeconds(1);
		object lockB = provider.GetLock("B");
		clock.Now = clock.Now.AddSeconds(1);

		// Act — adding C exceeds capacity; the only evictable (unheld, non-just-added) mapping is B.
		provider.GetLock("C");
		object lockAReacquired = provider.GetLock("A");
		object lockBReacquired = provider.GetLock("B");

		// Assert
		lockAReacquired.Should().BeSameAs(lockA,
			because: "a held lock must never be evicted — minting a new object while a thread still holds the old one would break mutual exclusion");
		lockBReacquired.Should().NotBeSameAs(lockB,
			because: "the unheld B was the eligible LRU victim, so re-acquiring its key mints a new lock");
	}

	[Test]
	[Category("Unit")]
	[Description("An unheld mapping idle past the TTL is evicted; a held mapping idle past the TTL survives.")]
	public void GetLock_ShouldEvictIdleUnheldButKeepIdleHeld_WhenIdlePastTtl() {
		// Arrange — held H and unheld U, both created at t0; capacity is generous so only idle-TTL applies.
		MutableClock clock = new();
		var provider = new TenantExecutionLockProvider(TimeSpan.FromMinutes(5), maxEntries: 50, clock.Read);
		object lockHeld = provider.GetLock("held");
		provider.MarkInUse("held");
		object lockUnheld = provider.GetLock("unheld");

		// Act — advance well past the TTL, then any GetLock runs the idle sweep.
		clock.Now = clock.Now.AddMinutes(10);
		provider.GetLock("trigger");
		object lockHeldReacquired = provider.GetLock("held");
		object lockUnheldReacquired = provider.GetLock("unheld");

		// Assert
		lockUnheldReacquired.Should().NotBeSameAs(lockUnheld,
			because: "an unheld mapping idle past the TTL is evicted, so its key mints a new lock");
		lockHeldReacquired.Should().BeSameAs(lockHeld,
			because: "a held mapping is never evicted for idle-TTL — the holder keeps serializing on the same object");
	}
}
