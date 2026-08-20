using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace Clio.Command.McpServer.Relay;

/// <summary>
/// One held reservation on a shared, target-wide resource: which key it holds, who holds it, and since
/// when.
/// </summary>
/// <param name="ExclusionKey">
/// The composed <c>resource|normalised-target</c> key. Carried on the token so a release cannot be
/// misdirected to another key by a caller that kept the token and lost track of the key.
/// </param>
/// <param name="Token">
/// Ownership, from a monotonic counter. NOT derived from the clock: <see cref="Environment.TickCount64"/>
/// has a ~15.6 ms resolution on Windows, so two reservations taken inside one tick would share a stamp and
/// ownership would stop being decidable. Production could not hit that today — a reclaim needs an entry
/// older than the ceiling, so the two stamps are tens of minutes apart — but correctness that rests on the
/// clock being coarse enough is not correctness.
/// </param>
/// <param name="StartedAtMs">
/// When the reservation was taken, in monotonic ticks, for the ceiling comparison only.
/// </param>
/// <remarks>A data-only carrier, so it is a <see langword="record"/> per the DI policy.</remarks>
public sealed record SharedResourceReservationToken(string ExclusionKey, long Token, long StartedAtMs);

/// <summary>
/// The PARENT-owned mutual exclusion for shared resources that are a property of the TARGET rather than
/// of the caller — today exactly one: Creatio's server-wide configuration build
/// (<see cref="McpToolSharedFileResource.ConfigurationBuild"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it had to move out of the tool.</b> Until Stage 7 this lived in
/// <c>McpToolExecutionLock.TryReserveConfigurationBuild</c> — a <see langword="static"/> dictionary in
/// whichever process ran the tool. Once <c>compile-creatio</c> and <c>install-process-builder</c> execute
/// in short-lived children, that dictionary is the CHILD's: each worker holds a reservation nobody else
/// can see, and the exclusion the two tools depend on silently evaluates to nothing. The owner has to be
/// the one process that outlives every worker, which is the parent (cross-call-state inventory §3, P-3).
/// </para>
/// <para>
/// <b>Keyed by normalised target + resource, and deliberately NOT by the tenant key.</b> Creatio's
/// configuration build is server-wide: two principals compiling one environment corrupt each other's
/// package compilation state regardless of whose credentials started them. Putting the principal (or the
/// credential fingerprint the tenant key carries) into this key would let exactly that happen, so the key
/// comes from <see cref="Tools.IToolCommandResolver.GetTargetKey"/> and never from
/// <see cref="Tools.IToolCommandResolver.GetTenantKey"/>. The compile and restart STATUS registries keep
/// the opposite cardinality — they answer "whose operation is this" — and conflating the two fails in one
/// direction or the other.
/// </para>
/// <para>
/// <b>The 30-minute reclaim ceiling is the maximum hold time, not an incidental number.</b> Keyed by
/// target alone, one stuck holder denies the whole environment to every other principal, so the ceiling
/// IS the bound on that denial. It is not a timeout on the work: nothing is cancelled, and a build still
/// running past it keeps running. It only stops a reservation nobody will ever release from outliving the
/// work it was protecting.
/// </para>
/// <para>
/// <b>Scope, stated so it is not overclaimed.</b> This excludes every worker of ONE parent, and the
/// parent's own in-process callers. It does not, and never did, exclude a second clio on another machine;
/// what serialises that is the platform itself (<c>WorkspaceBuilder</c> rejects a concurrent compilation
/// on the node with <c>AnotherCompilationIsInProgress</c>).
/// </para>
/// </remarks>
public interface ISharedResourceReservation {

	/// <summary>
	/// Gets how long a reservation may be held before another caller may reclaim it — the maximum time
	/// one stuck holder can deny a target.
	/// </summary>
	TimeSpan ReclaimCeiling { get; }

	/// <summary>
	/// Attempts to reserve <paramref name="resource"/> on <paramref name="targetKey"/>.
	/// </summary>
	/// <param name="resource">The shared resource being reserved.</param>
	/// <param name="targetKey">
	/// The NORMALISED TARGET key from <see cref="Tools.IToolCommandResolver.GetTargetKey"/>. Never a
	/// tenant key: see the type remarks.
	/// </param>
	/// <param name="reservation">
	/// The ownership token when this call won, which must be handed back to <see cref="Release"/>;
	/// <see langword="null"/> otherwise.
	/// </param>
	/// <returns>
	/// <see langword="true"/> when nothing was in flight for that target and resource (or the holder was
	/// past <see cref="ReclaimCeiling"/> and was reclaimed); <see langword="false"/> when a live holder
	/// exists and the caller must fail fast.
	/// </returns>
	/// <exception cref="ArgumentException"><paramref name="targetKey"/> is null or blank.</exception>
	bool TryReserve(McpToolSharedFileResource resource, string targetKey,
		out SharedResourceReservationToken reservation);

	/// <summary>
	/// Releases a reservation, and only if it is still the holder.
	/// </summary>
	/// <param name="reservation">The token <see cref="TryReserve"/> handed out; null is a no-op.</param>
	/// <returns><see langword="true"/> when this token was the holder and the key is now free.</returns>
	/// <remarks>
	/// OWNERSHIP-AWARE, and it has to be once reclaiming exists. With a ceiling there can be two logical
	/// owners: the original, whose work is still out there, and the caller that reclaimed after the
	/// ceiling. An unconditional remove would let the ORIGINAL delete the RECLAIMER's live reservation,
	/// after which any third caller reserves successfully and starts a configuration build alongside the
	/// reclaimer's — the guard switching itself off for that target after a single reclaim.
	/// </remarks>
	bool Release(SharedResourceReservationToken reservation);

	/// <summary>Gets the number of reservations currently held, across every resource and target.</summary>
	int HeldCount { get; }
}

/// <inheritdoc cref="ISharedResourceReservation"/>
public sealed class SharedResourceReservation : ISharedResourceReservation {

	/// <summary>
	/// How long a reservation may be held before another caller may reclaim it.
	/// </summary>
	/// <remarks>
	/// Carried over UNCHANGED from <c>McpToolExecutionLock</c>, where it was designed for exactly the
	/// "holder may never release" case this move preserves: past the MCP response deadline the work runs
	/// detached, and the install POST it wraps goes out with <c>Timeout.Infinite</c>. A target that accepts
	/// the request and then never answers would otherwise leave the entry in place for the life of the MCP
	/// server process. It is also the bound <see cref="StickyWorkerLifetimeBound.ExplicitMaximum"/> is
	/// derived from, so a sticky worker can never outlive the reservation it holds.
	/// </remarks>
	public static readonly TimeSpan DefaultReclaimCeiling = TimeSpan.FromMinutes(30);

	private readonly ConcurrentDictionary<string, SharedResourceReservationToken> _held =
		new(StringComparer.Ordinal);

	private long _tokenSource;

	/// <summary>
	/// Initializes a new instance of the <see cref="SharedResourceReservation"/> class with the shipped
	/// <see cref="DefaultReclaimCeiling"/>. Used by DI.
	/// </summary>
	public SharedResourceReservation()
		: this(DefaultReclaimCeiling) {
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="SharedResourceReservation"/> class with an explicit
	/// ceiling, so a test can exercise reclaim without waiting half an hour and without making the shipped
	/// ceiling mutable process state that one test could leave wrong for the next.
	/// </summary>
	/// <param name="reclaimCeiling">The maximum hold time.</param>
	/// <exception cref="ArgumentOutOfRangeException">The ceiling is not positive.</exception>
	internal SharedResourceReservation(TimeSpan reclaimCeiling) {
		if (reclaimCeiling <= TimeSpan.Zero) {
			throw new ArgumentOutOfRangeException(nameof(reclaimCeiling),
				"A reclaim ceiling of zero or less would reclaim every reservation the moment it was taken.");
		}
		ReclaimCeiling = reclaimCeiling;
	}

	/// <inheritdoc/>
	public TimeSpan ReclaimCeiling { get; }

	/// <inheritdoc/>
	public int HeldCount => _held.Count;

	/// <summary>
	/// Composes the exclusion key from the resource and the normalised target.
	/// </summary>
	/// <param name="resource">The shared resource.</param>
	/// <param name="targetKey">The normalised target key.</param>
	/// <returns>The composed key.</returns>
	/// <remarks>
	/// The resource is part of the key rather than assumed, so two DIFFERENT shared resources on one
	/// target do not exclude each other. Exposed to tests so they can state the key without restating the
	/// composition — a test that rebuilt it by hand would agree with itself after a format change.
	/// </remarks>
	internal static string ComposeExclusionKey(McpToolSharedFileResource resource, string targetKey) =>
		string.Concat(resource.ToString(), "|", targetKey);

	/// <inheritdoc/>
	public bool TryReserve(McpToolSharedFileResource resource, string targetKey,
		out SharedResourceReservationToken reservation) {
		if (string.IsNullOrWhiteSpace(targetKey)) {
			throw new ArgumentException(
				"A shared-resource reservation must name the target it excludes. A blank key would put every "
				+ "environment in one bucket and make one compile deny all of them.", nameof(targetKey));
		}
		string exclusionKey = ComposeExclusionKey(resource, targetKey);
		// Environment.TickCount64, NOT DateTime.UtcNow: this is an ELAPSED-time measurement and the wall
		// clock is not monotonic. A forward step larger than the ceiling — an NTP correction, a VM snapshot
		// restore, a laptop resuming with a corrected RTC — would otherwise reclaim a reservation whose
		// build started seconds ago, which is the one thing the generous ceiling exists to prevent.
		long now = Environment.TickCount64;
		SharedResourceReservationToken candidate =
			new(exclusionKey, Interlocked.Increment(ref _tokenSource), now);
		if (_held.TryAdd(exclusionKey, candidate)) {
			reservation = candidate;
			return true;
		}
		if (_held.TryGetValue(exclusionKey, out SharedResourceReservationToken current)
			&& now - current.StartedAtMs > (long)ReclaimCeiling.TotalMilliseconds
			// TryUpdate, not Remove-then-Add: when two callers race here only one may take the reclaimed
			// slot, and the comparison value makes that decidable without a lock.
			&& _held.TryUpdate(exclusionKey, candidate, current)) {
			reservation = candidate;
			return true;
		}
		reservation = null;
		return false;
	}

	/// <inheritdoc/>
	public bool Release(SharedResourceReservationToken reservation) =>
		reservation is not null
		&& _held.TryRemove(
			new KeyValuePair<string, SharedResourceReservationToken>(reservation.ExclusionKey, reservation));

	/// <summary>
	/// Builds the message returned when a shared resource is already reserved for the target.
	/// </summary>
	/// <param name="toolName">The tool that was refused.</param>
	/// <param name="environmentName">The environment named by the call, for the reader.</param>
	/// <returns>The refusal text.</returns>
	/// <remarks>
	/// It names the OTHER tool family explicitly, because the whole point of this reservation is that
	/// <c>compile-creatio</c> and <c>install-process-builder</c> exclude EACH OTHER: a caller told only
	/// "a compilation is in progress" after calling <c>install-process-builder</c> would reasonably
	/// conclude the message was about somebody else's tool and retry.
	/// </remarks>
	internal static string BuildAlreadyReservedMessage(string toolName, string environmentName) =>
		string.Format(CultureInfo.InvariantCulture,
			"'{0}' was not started: a configuration build is already in progress for '{1}'. Creatio "
			+ "serialises configuration builds server-wide, so compile-creatio and install-process-builder "
			+ "exclude each other on one environment no matter which caller, principal or clio process "
			+ "started the running one. Poll compile-status and wait for it to finish. The call was not "
			+ "executed and issued no request to Creatio.",
			toolName, environmentName ?? "the requested environment");
}
