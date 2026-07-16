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
	/// <paramref name="environmentName"/> + <paramref name="applicationCode"/> (case-insensitive).
	/// The wait for the mutex is bounded by <paramref name="waitTimeout"/>; on timeout the work runs
	/// <b>unserialized</b> (best-effort) so a deep queue never becomes a hard failure — any resulting
	/// contention is recovered by the caller's retry/verify path. Additionally, when too many callers
	/// are already queued on the same key, an excess caller degrades to best-effort <b>immediately</b>
	/// (fail-fast, without blocking on the mutex) so a deep same-key fan-out cannot park a thread-pool
	/// worker per waiter and starve unrelated work in a long-lived server process. The mutex is always
	/// released when the work completes or throws.
	/// </summary>
	/// <typeparam name="T">Return type of the guarded work.</typeparam>
	/// <param name="environmentName">Registered clio environment name (part of the mutex key).</param>
	/// <param name="applicationCode">Installed application code (part of the mutex key).</param>
	/// <param name="waitTimeout">Maximum time to wait for the per-key mutex before degrading to best-effort.</param>
	/// <param name="work">The destructive create-section span (insert → readback) to serialize.</param>
	/// <returns>The value produced by <paramref name="work"/>.</returns>
	T Run<T>(string environmentName, string applicationCode, TimeSpan waitTimeout, Func<T> work);
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

	/// <inheritdoc />
	public T Run<T>(string environmentName, string applicationCode, TimeSpan waitTimeout, Func<T> work) {
		ArgumentNullException.ThrowIfNull(work);
		string key = BuildKey(environmentName, applicationCode);
		// Reserve a slot BEFORE waiting so the bound counts this caller too; ALWAYS release it in finally.
		int inFlight = _inFlightPerKey.AddOrUpdate(key, 1, static (_, current) => current + 1);
		try {
			if (inFlight > MaxConcurrentWaiters) {
				// Deep same-key queue: do NOT park another thread-pool worker on Wait. Degrade to best-effort
				// (unserialized) exactly like the wait-timeout path; residual contention is recovered by the
				// caller's verify/retry path. This fail-fast bounds parked workers so a deep queue cannot
				// exhaust the pool; an async acquire is the deferred, non-parking alternative.
				logger.WriteWarning(
					$"Section-create queue for '{applicationCode}' is deep ({inFlight} concurrent callers, "
					+ $"bound {MaxConcurrentWaiters}); proceeding without serialization (best-effort) to avoid "
					+ "parking a thread-pool worker. Any resulting contention is recovered automatically.");
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

	// The key joins the lower-cased environment name and application code so callers that differ only by
	// case map to the same mutex (case-insensitive per ADR Q1). The separator is the ASCII Unit Separator
	// control character U+001F, which does not occur in clio environment names or Creatio application
	// codes, so distinct pairs can never collide onto one key.
	private static string BuildKey(string environmentName, string applicationCode) =>
		$"{(environmentName ?? string.Empty).ToLowerInvariant()}\u001F{(applicationCode ?? string.Empty).ToLowerInvariant()}";
}
