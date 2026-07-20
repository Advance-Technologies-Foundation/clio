using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Clio.Common;

/// <summary>
/// A small, reusable registry of per-key <see cref="SemaphoreSlim"/> gates. Each distinct key maps to
/// its own binary (<c>1,1</c>) semaphore, so callers can serialize work per key while different keys run
/// fully in parallel.
/// </summary>
/// <remarks>
/// <para>
/// <b>CLIO001 exemption (AGENTS.md lightweight-primitive rule).</b> This type is a lightweight,
/// stateless-policy concurrency <i>primitive</i> — the same category as <see cref="SemaphoreSlim"/> and
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> — not a behaviour-bearing service, handler, or
/// validator. Each consumer deliberately owns its OWN independent key space (for example the
/// section-create serialization guard's per-application locks vs. the component-registry background-refresh
/// gates), so every consumer instantiates its own <c>new KeyedSemaphore()</c> for its own registry. A
/// shared DI singleton would be <i>incorrect</i> here: it would conflate two unrelated lock registries
/// onto one instance. Constructing it per consumer with <c>new()</c> is therefore intentional and is the
/// AGENTS.md lightweight-primitive exemption to CLIO001 (favour DI for behaviour classes), not a defect.
/// </para>
/// Entries are <b>never evicted</b>. The key cardinality is bounded by design (tens of distinct keys per
/// process at most, each semaphore is a few dozen bytes), and ref-counted removal would introduce a
/// TOCTOU race between <see cref="SemaphoreSlim.Release()"/> and the next <see cref="GetOrAdd"/> — a
/// caller could remove an entry another thread is about to acquire. Never-remove is the standard, correct
/// keyed-lock pattern here. The backing dictionary uses <see cref="StringComparer.Ordinal"/>; callers that
/// need case-insensitive keys must normalize the key before calling.
/// </remarks>
public sealed class KeyedSemaphore {
	private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

	/// <summary>
	/// Returns the per-key binary semaphore for <paramref name="key"/>, creating it on first use.
	/// The same instance is returned for every subsequent call with the same key.
	/// </summary>
	/// <param name="key">The gate key. Callers own any normalization (e.g. lower-casing).</param>
	/// <returns>The shared <see cref="SemaphoreSlim"/> for the key.</returns>
	public SemaphoreSlim GetOrAdd(string key) {
		ArgumentNullException.ThrowIfNull(key);
		return _gates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
	}
}
