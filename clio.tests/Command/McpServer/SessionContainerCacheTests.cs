using System;
using Clio.Command.McpServer;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Unit coverage for <see cref="SessionContainerCache"/> (Story 8, FR-08): idle-TTL eviction,
/// LRU capacity eviction, the in-flight guard (AC-06), provider disposal on eviction (AC-05), and
/// per-key reuse. Uses a deterministic injected clock — no <c>Thread.Sleep</c> / no wall-clock.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class SessionContainerCacheTests {

	// Records disposal so a test can assert an evicted provider was disposed (AC-05).
	private sealed class SpyServiceProvider : IServiceProvider, IDisposable {
		public bool Disposed { get; private set; }

		public object GetService(Type serviceType) => null;

		public void Dispose() => Disposed = true;
	}

	private DateTime _now;

	private SessionContainerCache CreateCache(TimeSpan idleTtl, int maxSessions) =>
		new(idleTtl, maxSessions, () => _now);

	[SetUp]
	public void SetUp() {
		// Fixed, explicit UTC start so every advance is deterministic.
		_now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
	}

	[Test]
	[Description("Acquire returns the same provider for the same key and never re-invokes the factory.")]
	public void Acquire_ShouldReturnSameProviderAndNotRebuild_WhenSameKey() {
		// Arrange
		SessionContainerCache cache = CreateCache(TimeSpan.FromHours(1), 50);
		SpyServiceProvider first = new();
		int factoryCalls = 0;

		// Act
		IServiceProvider initial = cache.Acquire("key-a", () => { factoryCalls++; return first; });
		IServiceProvider reused = cache.Acquire("key-a",
			() => throw new InvalidOperationException("factory must not run for a cached key"));

		// Assert
		reused.Should().BeSameAs(initial,
			because: "a second Acquire for the same key must return the cached provider");
		factoryCalls.Should().Be(1,
			because: "the factory is invoked only on the first Acquire of a key");
	}

	[Test]
	[Description("Acquire returns distinct providers for distinct keys.")]
	public void Acquire_ShouldReturnDistinctProviders_WhenDistinctKeys() {
		// Arrange
		SessionContainerCache cache = CreateCache(TimeSpan.FromHours(1), 50);
		SpyServiceProvider providerA = new();
		SpyServiceProvider providerB = new();

		// Act
		IServiceProvider a = cache.Acquire("key-a", () => providerA);
		IServiceProvider b = cache.Acquire("key-b", () => providerB);

		// Assert
		a.Should().BeSameAs(providerA, because: "each distinct key owns its own container");
		b.Should().BeSameAs(providerB, because: "each distinct key owns its own container");
		a.Should().NotBeSameAs(b, because: "two tenants keyed differently must never share a container");
	}

	[Test]
	[Description("An entry idle past the TTL is evicted and its provider disposed when a later Acquire sweeps.")]
	public void Acquire_ShouldEvictAndDisposeIdleEntry_WhenIdlePastTtl() {
		// Arrange
		SessionContainerCache cache = CreateCache(TimeSpan.FromMinutes(5), 50);
		SpyServiceProvider idle = new();
		cache.Acquire("idle", () => idle);

		// Act
		_now = _now.AddMinutes(6); // past the 5-minute TTL
		cache.Acquire("fresh", () => new SpyServiceProvider()); // triggers the opportunistic sweep
		int rebuildCalls = 0;
		cache.Acquire("idle", () => { rebuildCalls++; return new SpyServiceProvider(); });

		// Assert
		idle.Disposed.Should().BeTrue(
			because: "an entry idle beyond the TTL is disposed when the next Acquire sweeps");
		rebuildCalls.Should().Be(1,
			because: "the evicted key must be rebuilt on its next Acquire, proving it was removed");
	}

	[Test]
	[Description("When capacity is exceeded, the least-recently-used entry is evicted and disposed.")]
	public void Acquire_ShouldEvictAndDisposeLruEntry_WhenCapacityExceeded() {
		// Arrange
		SessionContainerCache cache = CreateCache(TimeSpan.FromHours(1), 2);
		SpyServiceProvider oldest = new();
		cache.Acquire("a", () => oldest);
		_now = _now.AddSeconds(1);
		cache.Acquire("b", () => new SpyServiceProvider());
		_now = _now.AddSeconds(1);

		// Act
		cache.Acquire("c", () => new SpyServiceProvider()); // count 3 > cap 2 → evict LRU (a)
		int rebuildA = 0;
		cache.Acquire("a", () => { rebuildA++; return new SpyServiceProvider(); });

		// Assert
		oldest.Disposed.Should().BeTrue(
			because: "the least-recently-used container (a) is evicted and disposed on overflow");
		rebuildA.Should().Be(1,
			because: "the evicted LRU key must be rebuilt on its next Acquire, proving it was removed");
	}

	[Test]
	[Description("An in-use entry is never evicted for capacity even when it is the only eviction candidate (AC-06).")]
	public void Acquire_ShouldNotEvictInUseEntry_WhenItIsTheOnlyCapacityVictim() {
		// Arrange
		SessionContainerCache cache = CreateCache(TimeSpan.FromHours(1), 1);
		SpyServiceProvider inUse = new();
		cache.Acquire("in-use", () => inUse);
		cache.MarkInUse("in-use");
		_now = _now.AddSeconds(1);

		// Act
		cache.Acquire("newcomer", () => new SpyServiceProvider()); // count 2 > cap 1, but "in-use" is in use

		// Assert
		inUse.Disposed.Should().BeFalse(
			because: "eviction must never dispose an entry with an in-flight call, even over capacity");
		cache.Acquire("in-use",
			() => throw new InvalidOperationException("in-use entry must have survived eviction"))
			.Should().BeSameAs(inUse,
				because: "the in-use container survived the over-capacity sweep as a temporary overshoot");
	}

	[Test]
	[Description("An in-use entry is never evicted for idle-TTL even when it is idle past the TTL (AC-06).")]
	public void Acquire_ShouldNotEvictInUseEntry_WhenIdlePastTtl() {
		// Arrange
		SessionContainerCache cache = CreateCache(TimeSpan.FromMinutes(5), 50);
		SpyServiceProvider inUse = new();
		cache.Acquire("in-use", () => inUse);
		cache.MarkInUse("in-use");

		// Act
		_now = _now.AddMinutes(10); // well past the TTL
		cache.Acquire("fresh", () => new SpyServiceProvider()); // triggers the idle sweep

		// Assert
		inUse.Disposed.Should().BeFalse(
			because: "an entry with an in-flight call is never idle-evicted mid-call");
	}

	[Test]
	[Description("After MarkAvailable balances MarkInUse, the entry becomes evictable again.")]
	public void Acquire_ShouldEvictReleasedEntry_WhenMarkAvailableClearsInUse() {
		// Arrange
		SessionContainerCache cache = CreateCache(TimeSpan.FromMinutes(5), 50);
		SpyServiceProvider released = new();
		cache.Acquire("released", () => released);
		cache.MarkInUse("released");
		cache.MarkAvailable("released"); // in-use count back to 0

		// Act
		_now = _now.AddMinutes(6); // past the TTL
		cache.Acquire("fresh", () => new SpyServiceProvider()); // triggers the idle sweep

		// Assert
		released.Disposed.Should().BeTrue(
			because: "once the in-use marker is cleared the entry is idle-evictable again");
	}

	[Test]
	[Description("Constructing the cache with a non-positive idle-TTL is rejected.")]
	public void Constructor_ShouldThrow_WhenIdleTtlIsNonPositive() {
		// Arrange
		Action act = () => _ = new SessionContainerCache(TimeSpan.Zero, 50);

		// Act & Assert
		act.Should().Throw<ArgumentOutOfRangeException>(
			because: "a zero or negative idle-TTL would evict every entry immediately and is invalid");
	}

	[Test]
	[Description("Constructing the cache with a non-positive capacity is rejected.")]
	public void Constructor_ShouldThrow_WhenMaxSessionsIsNonPositive() {
		// Arrange
		Action act = () => _ = new SessionContainerCache(TimeSpan.FromMinutes(5), 0);

		// Act & Assert
		act.Should().Throw<ArgumentOutOfRangeException>(
			because: "a zero or negative capacity would evict every entry on add and is invalid");
	}

	[Test]
	[Description("ResolveIdleTtl parses suffixed durations (s/m/h/d).")]
	[TestCase("90s", 90)]
	[TestCase("5m", 300)]
	[TestCase("1h", 3600)]
	[TestCase("1d", 86400)]
	public void ResolveIdleTtl_ShouldParseSuffixedDuration_WhenSuffixPresent(string raw, int expectedSeconds) {
		// Act
		TimeSpan result = SessionContainerCacheDefaults.ResolveIdleTtl(raw);

		// Assert
		result.Should().Be(TimeSpan.FromSeconds(expectedSeconds),
			because: "a suffixed duration maps its unit onto the numeric magnitude");
	}

	[Test]
	[Description("ResolveIdleTtl treats a bare number as seconds and parses a TimeSpan string.")]
	public void ResolveIdleTtl_ShouldParseBareSecondsAndTimeSpan_WhenNoSuffix() {
		// Act
		TimeSpan bare = SessionContainerCacheDefaults.ResolveIdleTtl("300");
		TimeSpan timeSpan = SessionContainerCacheDefaults.ResolveIdleTtl("00:05:00");

		// Assert
		bare.Should().Be(TimeSpan.FromSeconds(300),
			because: "a bare number is interpreted as a number of seconds");
		timeSpan.Should().Be(TimeSpan.FromMinutes(5),
			because: "a TimeSpan-formatted string is parsed as a TimeSpan");
	}

	[Test]
	[Description("ResolveIdleTtl falls back to the default for a null, blank, unparseable, or non-positive value.")]
	[TestCase(null)]
	[TestCase("")]
	[TestCase("   ")]
	[TestCase("garbage")]
	[TestCase("0s")]
	[TestCase("-5m")]
	public void ResolveIdleTtl_ShouldFallBackToDefault_WhenValueIsInvalid(string raw) {
		// Act
		TimeSpan result = SessionContainerCacheDefaults.ResolveIdleTtl(raw);

		// Assert
		result.Should().Be(SessionContainerCacheDefaults.IdleTtl,
			because: "an absent or invalid duration must not disable eviction; it uses the 5-minute default");
	}
}
