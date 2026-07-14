using System;
using System.Threading;
using Clio.Common;

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
/// It does NOT serialize across separate <c>clio</c> processes — that cross-process case is covered by
/// the reclassify/verify/retry recovery in <see cref="ApplicationSectionCreateService"/> (ENG-93089, Option B).
/// </remarks>
public interface ISectionCreateSerializationGuard {
	/// <summary>
	/// Runs <paramref name="work"/> while holding a process-wide mutex keyed by
	/// <paramref name="environmentName"/> + <paramref name="applicationCode"/> (case-insensitive).
	/// The wait for the mutex is bounded by <paramref name="waitTimeout"/>; on timeout the work runs
	/// <b>unserialized</b> (best-effort) so a deep queue never becomes a hard failure — any resulting
	/// contention is recovered by the caller's retry/verify path. The mutex is always released when the
	/// work completes or throws.
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

	/// <inheritdoc />
	public T Run<T>(string environmentName, string applicationCode, TimeSpan waitTimeout, Func<T> work) {
		ArgumentNullException.ThrowIfNull(work);
		string key = BuildKey(environmentName, applicationCode);
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
	}

	// The key joins the lower-cased environment name and application code so callers that differ only by
	// case map to the same mutex (case-insensitive per ADR Q1). The separator is the ASCII Unit Separator
	// control character U+001F, which does not occur in clio environment names or Creatio application
	// codes, so distinct pairs can never collide onto one key.
	private static string BuildKey(string environmentName, string applicationCode) =>
		$"{(environmentName ?? string.Empty).ToLowerInvariant()}\u001F{(applicationCode ?? string.Empty).ToLowerInvariant()}";
}
