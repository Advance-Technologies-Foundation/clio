using System;
using System.Collections.Concurrent;
using System.Threading;
using Clio.Common;
using Clio.UserEnvironment;

namespace Clio.Command;

/// <summary>
/// Serializes the destructive <c>create-app-section</c> insert→readback span per
/// environment + application so that concurrent section creations against the same
/// application do not contend server-side into a spurious <c>InsertQuery failed</c> (ENG-93089).
/// </summary>
/// <remarks>
/// The repro is an AI agent that fires several <c>create-app-section</c> calls at once against one
/// application: each section insert takes ~90–100 s, so overlapping inserts collide. This guard makes
/// creations for the same <c>environment + application-code</c> run one at a time within a single
/// <c>clio</c> process, while creations for different applications or environments stay fully parallel.
/// It does NOT serialize across separate <c>clio</c> processes — that cross-process case is covered by the
/// reclassify + verify recovery in <see cref="ApplicationSectionCreateService"/> (and, on the MCP path only,
/// a bounded one-shot retry). Pure cross-process CLI-vs-CLI contention therefore gets reclassify + verify but
/// not the retry, because the retry leg is gated to the MCP/background caller (ENG-93089, Option B).
/// </remarks>
public interface ISectionCreateSerializationGuard {
	/// <summary>
	/// Runs <paramref name="work"/> while holding a process-wide mutex keyed by
	/// <paramref name="environmentKey"/> + <paramref name="applicationCode"/> (case-insensitive).
	/// The wait for the mutex is bounded by <paramref name="waitTimeout"/>; on timeout the work runs
	/// <b>unserialized</b> (best-effort) so a deep queue never becomes a hard failure — any resulting
	/// contention is recovered by the caller's retry/verify path. Additionally, two waiter bounds degrade
	/// an excess caller to best-effort <b>immediately</b> (fail-fast, without blocking on the mutex) so a
	/// deep fan-out cannot park a thread-pool worker per waiter and starve unrelated work in a long-lived
	/// server process: a <b>per-key</b> bound (<see cref="SectionCreateSerializationGuard.MaxConcurrentWaiters"/>),
	/// which caps a deep same-application queue, and a <b>process-wide</b> bound
	/// (<see cref="SectionCreateSerializationGuard.MaxTotalConcurrentWaiters"/>), which caps the total across
	/// ALL keys so a burst spread over many distinct hot keys — each individually under the per-key bound —
	/// still cannot park <c>per-key-bound × key-count</c> synchronous workers at once. A full asynchronous
	/// <c>WaitAsync</c> acquire (which would not hold a worker while queued) is the deferred alternative that
	/// would remove synchronous parking entirely. The mutex is always released when the work completes or
	/// throws.
	/// </summary>
	/// <typeparam name="T">Return type of the guarded work.</typeparam>
	/// <param name="environmentKey">
	/// The canonical, already-normalized environment identity that the caller must supply (for example the
	/// value produced by <see cref="SectionCreateSerializationGuard.BuildEnvironmentKey"/>) — NOT a registered
	/// clio environment name. It is used verbatim as the environment part of the mutex key, so passing the raw
	/// registered name here would reintroduce the divergent-gate bug (ENG-93089, #3594140065).
	/// </param>
	/// <param name="applicationCode">Installed application code (part of the mutex key).</param>
	/// <param name="waitTimeout">Maximum time to wait for the per-key mutex before degrading to best-effort.</param>
	/// <param name="work">The destructive create-section span (insert → readback) to serialize.</param>
	/// <returns>The value produced by <paramref name="work"/>.</returns>
	T Run<T>(string environmentKey, string applicationCode, TimeSpan waitTimeout, Func<T> work);
}

/// <summary>
/// Process-wide keyed-<see cref="SemaphoreSlim"/> implementation of <see cref="ISectionCreateSerializationGuard"/>.
/// Registered as a DI singleton so its registry is shared across the CLI verb and the MCP tool within one process.
/// </summary>
public sealed class SectionCreateSerializationGuard(ILogger logger) : ISectionCreateSerializationGuard {
	// Registry of per-key mutexes. A plain instance field is process-shared because the guard is a DI
	// singleton — no static field is needed (cleaner for CLIO005). KeyedSemaphore never evicts entries:
	// the count is bounded by the number of distinct environment+application-code pairs a process touches
	// (tens at most), and ref-counted removal would race Release against the next GetOrAdd (see its docs).
	private readonly KeyedSemaphore _sectionCreateLocks = new();

	// Per-key count of callers currently inside the guarded region (the holder plus every caller blocked
	// on Wait). It bounds how many thread-pool workers a single deep same-key fan-out can park. The
	// waiter-bound is layered HERE, over the shared KeyedSemaphore, on purpose: KeyedSemaphore is also used
	// by ComponentRegistryClient, so this create-section-specific back-pressure must not leak into it.
	private readonly ConcurrentDictionary<string, int> _inFlightPerKey = new(StringComparer.Ordinal);

	// Fail-fast bound on concurrently-parked same-key callers. The guard waits SYNCHRONOUSLY
	// (SemaphoreSlim.Wait up to waitTimeout, which can be ~120 s) on a thread-pool worker; under a deep
	// same-application fan-out that would otherwise park N-1 workers for the full wait, risking thread-pool
	// starvation of unrelated MCP tools in the long-lived server. Once this many callers are already
	// in-flight for a key, an excess caller runs best-effort (unserialized) immediately instead of parking
	// another worker — mirroring the existing wait-timeout degrade — and any residual contention is
	// recovered by the caller's verify/retry path. A small bound is enough: it caps parked workers while
	// still serializing the shallow bursts that are the actual repro. A full async WaitAsync acquire (which
	// would not hold a worker while queued) is the deferred alternative and is intentionally out of scope.
	internal const int MaxConcurrentWaiters = 8;

	// Process-wide companion to MaxConcurrentWaiters. The per-key bound alone caps only a deep SAME-key
	// queue; a burst spread across N distinct hot keys — each individually under MaxConcurrentWaiters — could
	// still park up to MaxConcurrentWaiters * N synchronous Wait workers at once and starve the pool. This
	// global bound caps the TOTAL number of callers concurrently inside the guarded region (holders plus
	// parked waiters) across ALL keys: once it is exceeded, an excess caller degrades to best-effort exactly
	// like the per-key path, no matter how shallow its own key's queue is. 32 comfortably admits the shallow
	// multi-application bursts that are the real workload while still bounding a pathological fan-out. As with
	// the per-key bound, a full async WaitAsync acquire is the deferred, non-parking alternative.
	internal const int MaxTotalConcurrentWaiters = 32;

	// Global count of callers currently inside the guarded region across EVERY key (the sum of every key's
	// in-flight count). Process-shared because the guard is a DI singleton. Interlocked-updated: incremented
	// before waiting and ALWAYS decremented in finally, mirroring _inFlightPerKey.
	private int _totalInFlight;

	/// <inheritdoc />
	public T Run<T>(string environmentKey, string applicationCode, TimeSpan waitTimeout, Func<T> work) {
		ArgumentNullException.ThrowIfNull(work);
		string key = BuildKey(environmentKey, applicationCode);
		// Reserve a slot BEFORE waiting so the bounds count this caller too; ALWAYS release BOTH in finally.
		// The per-key count bounds a deep same-application queue; the global count bounds the total across all
		// keys so a fan-out over many distinct hot keys cannot park MaxConcurrentWaiters workers per key.
		int inFlight = _inFlightPerKey.AddOrUpdate(key, 1, static (_, current) => current + 1);
		int totalInFlight = Interlocked.Increment(ref _totalInFlight);
		try {
			bool perKeyExceeded = inFlight > MaxConcurrentWaiters;
			bool totalExceeded = totalInFlight > MaxTotalConcurrentWaiters;
			if (perKeyExceeded || totalExceeded) {
				// Deep queue on EITHER bound: do NOT park another thread-pool worker on Wait. Degrade to
				// best-effort (unserialized) exactly like the wait-timeout path; residual contention is
				// recovered by the caller's verify/retry path. This fail-fast bounds parked workers so a deep
				// queue — same-key OR fanned across many keys — cannot exhaust the pool; an async acquire is
				// the deferred, non-parking alternative.
				string boundDescription = perKeyExceeded
					? $"the per-application bound for '{applicationCode}' ({inFlight} concurrent callers, "
						+ $"bound {MaxConcurrentWaiters})"
					: $"the process-wide bound ({totalInFlight} concurrent section-create callers across all "
						+ $"applications, bound {MaxTotalConcurrentWaiters})";
				logger.WriteWarning(
					$"Section-create queue exceeded {boundDescription}; proceeding without serialization "
					+ "(best-effort) to avoid parking a thread-pool worker. Any resulting contention is "
					+ "recovered automatically.");
				return work();
			}

			SemaphoreSlim gate = _sectionCreateLocks.GetOrAdd(key);
			bool acquired = gate.Wait(waitTimeout);
			if (!acquired) {
				logger.WriteWarning(
					$"Could not acquire the section-create lock for '{applicationCode}' within "
					+ $"{waitTimeout.TotalSeconds:0}s; proceeding without serialization (best-effort). "
					+ "Any resulting contention is recovered automatically.");
			}

			try {
				return work();
			} finally {
				if (acquired) {
					gate.Release();
				}
			}
		} finally {
			_inFlightPerKey.AddOrUpdate(key, 0, static (_, current) => current - 1);
			Interlocked.Decrement(ref _totalInFlight);
		}
	}

	/// <summary>
	/// Derives the canonical per-environment part of the guard key from resolved environment settings so
	/// that BOTH <c>create-app-section</c> overloads — the registered-name path and the settings-based
	/// MCP-HTTP passthrough path — map the same physical Creatio server onto the SAME serialization gate.
	/// Normalizes the connection identity from <see cref="EnvironmentSettings.Uri"/>: trims surrounding
	/// whitespace, lower-cases, and strips a single trailing <c>'/'</c>; a <c>null</c>/blank Uri falls back
	/// to <see cref="string.Empty"/>. Without a single canonical identity the two overloads keyed off
	/// different strings (registered name vs. raw Uri) and same-server creates via different overloads were
	/// not serialized against each other (ENG-93089, #3594140065).
	/// </summary>
	/// <param name="settings">Resolved environment settings; must not be <c>null</c>.</param>
	/// <returns>The normalized environment identity used as the first part of the guard key.</returns>
	internal static string BuildEnvironmentKey(EnvironmentSettings settings) {
		ArgumentNullException.ThrowIfNull(settings);
		string uri = settings.Uri?.Trim() ?? string.Empty;
		if (uri.Length == 0) {
			return string.Empty;
		}

		uri = uri.ToLowerInvariant();
		return uri.EndsWith('/') ? uri[..^1] : uri;
	}

	// The key joins the canonical environment identity (already normalized by the caller) and application
	// code so callers that differ only by case map to the same mutex (case-insensitive per ADR Q1).
	// The environment part arrives pre-normalized (e.g. from BuildEnvironmentKey); lower-casing it again here
	// is idempotent, and the application-code part is lower-cased here. The separator is the ASCII Unit
	// Separator control character U+001F, which does not occur in clio environment keys or Creatio
	// application codes, so distinct pairs can never collide onto one key.
	private static string BuildKey(string environmentKey, string applicationCode) =>
		$"{(environmentKey ?? string.Empty).ToLowerInvariant()}\u001F{(applicationCode ?? string.Empty).ToLowerInvariant()}";
}
