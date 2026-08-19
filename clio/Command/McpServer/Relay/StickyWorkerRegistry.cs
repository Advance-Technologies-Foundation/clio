using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Common.McpWorker;

namespace Clio.Command.McpServer.Relay;

/// <summary>
/// Identifies the ONE sticky worker a call of a long-running family must reach.
/// </summary>
/// <param name="Family">
/// The operation family, so a compile and a restart on one environment are two workers rather than one
/// process serving both.
/// </param>
/// <param name="TenantKey">
/// The key from <see cref="Tools.IToolCommandResolver.GetTenantKey"/>: principal + normalised target +
/// credential fingerprint (R-5). This is the STATUS-REPORTING cardinality — "whose operation is this" —
/// and is deliberately NOT the cardinality of
/// <see cref="ISharedResourceReservation"/>, which excludes per TARGET. Sharing a worker by environment
/// alone would be a cross-client boundary violation: status tools are credential-scoped (ADR rule 3).
/// </param>
/// <remarks>A data-only carrier, so it is a <see langword="record"/> per the DI policy.</remarks>
public sealed record StickyWorkerKey(McpToolOperationFamily Family, string TenantKey);

/// <summary>
/// How long a sticky worker may live, and the composition point where the credential's own validity
/// shortens that (threat model T-8, story 7 AC-04).
/// </summary>
/// <remarks>
/// <para>
/// <b>The threat is CREATED by stickiness.</b> A per-call worker cannot outlive the credential it
/// authenticated with, because it does not outlive the call. A sticky worker can: it holds a session
/// established with a token that may since have expired or been revoked, and work would continue under
/// revoked authority. That is why stickiness stays confined to the four long-running families instead of
/// being used as a general performance optimisation.
/// </para>
/// <para>
/// <b>On stdio the explicit maximum is the WHOLE bound, and that is a fact about the transport rather
/// than a shortcut.</b> No credential crosses the boundary at all: the child reads
/// <c>appsettings.json</c> itself and receives only the environment NAME, so the parent has no token
/// whose expiry it could read. T-8 anticipates exactly this — "where validity is unknown … an explicit
/// maximum sticky lifetime applies". The credential term is therefore a PARAMETER of
/// <see cref="Resolve"/> rather than an absent feature: when <c>mcp-http</c> returns (OQ-9) its
/// passthrough context supplies it at the call site, the same way AC-00 composed the other two
/// components of R-5's key at the call site rather than inside the normaliser.
/// </para>
/// <para>
/// <b>The maximum is DERIVED from the reservation ceiling rather than picked.</b> A sticky worker of the
/// configuration-build family holds a <see cref="ISharedResourceReservation"/> for its whole life. If it
/// could outlive that reservation's reclaim ceiling, a second caller would reclaim the reservation and
/// start a configuration build while the first worker was still running one — the exact corruption the
/// reservation exists to prevent, arrived at by way of the bound meant to contain it. Equal, not merely
/// less: the dispatcher sweeps expired workers BEFORE it reserves, so a worker at exactly the bound is
/// reaped on the same call that would otherwise reclaim its reservation.
/// </para>
/// </remarks>
public static class StickyWorkerLifetimeBound {

	/// <summary>
	/// The explicit maximum lifetime of a sticky worker, whatever the credential says.
	/// </summary>
	public static readonly TimeSpan ExplicitMaximum = SharedResourceReservation.DefaultReclaimCeiling;

	/// <summary>
	/// Resolves the moment a sticky worker must be gone by: the earlier of the explicit maximum and the
	/// credential's own validity.
	/// </summary>
	/// <param name="spawnedAtUtc">When the worker was spawned.</param>
	/// <param name="credentialValidUntilUtc">
	/// When the credential that authenticated this worker stops being valid, or <see langword="null"/>
	/// when it is unknown — which is every stdio call, because no credential crosses the boundary.
	/// </param>
	/// <returns>The expiry instant.</returns>
	/// <remarks>
	/// A credential that has ALREADY expired yields an expiry no later than the spawn instant, so the
	/// worker is reaped on its first sweep rather than treated as freshly valid. Fail-closed is the only
	/// safe reading of "the credential is not valid": the alternative silently grants a worker the full
	/// maximum on the strength of an expired token.
	/// </remarks>
	public static DateTimeOffset Resolve(DateTimeOffset spawnedAtUtc, DateTimeOffset? credentialValidUntilUtc) {
		DateTimeOffset maximum = spawnedAtUtc + ExplicitMaximum;
		if (credentialValidUntilUtc is null) {
			return maximum;
		}
		DateTimeOffset credentialBound = credentialValidUntilUtc.Value;
		return credentialBound < maximum ? credentialBound : maximum;
	}
}

/// <summary>
/// The parent's record of the sticky workers it is currently supervising, and the only place they are
/// reaped.
/// </summary>
/// <remarks>
/// <para>
/// <b>The registry owns the lease; the dispatcher does not.</b> An ordinary relayed call disposes its
/// lease in a <c>finally</c> on every path, which kills the worker and returns its admission slot. A
/// sticky call must not: the whole point is that the worker outlives the response so a later status poll
/// can reach it. Ownership therefore transfers HERE at registration, and disposal happens at exactly one
/// place — <see cref="ReapAsync"/> — which is also the only place the slot comes back.
/// </para>
/// <para>
/// <b>Reaping is driven by a PRIVATE completion signal, not by a terminal status</b> (ADR rule 5). Only
/// two operation registries exist, compile and restart; <c>install-process-builder</c> and
/// <c>create-app-section</c> have none and <c>restart-by-credentials</c> is deliberately unreportable, so
/// three of the four long-running families have no terminal status a supervisor could reap on. See
/// <see cref="WorkerOperationSignalContract"/>.
/// </para>
/// </remarks>
public interface IStickyWorkerRegistry {

	/// <summary>
	/// Takes ownership of a freshly spawned sticky worker.
	/// </summary>
	/// <param name="key">The key later calls of this family reach it by.</param>
	/// <param name="entry">The lease, the open relay session and the standard-error drain.</param>
	/// <returns>
	/// <see langword="true"/> when the entry was registered; <see langword="false"/> when a live worker
	/// already held that key, in which case the caller owns the entry it passed and must reap it itself.
	/// </returns>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	bool TryRegister(StickyWorkerKey key, StickyWorkerEntry entry);

	/// <summary>
	/// Returns the live sticky worker for <paramref name="key"/>, taking NO admission slot.
	/// </summary>
	/// <param name="key">The key to reach.</param>
	/// <param name="entry">The live entry, or <see langword="null"/>.</param>
	/// <returns><see langword="true"/> when a live worker was found.</returns>
	/// <remarks>
	/// An entry whose worker has EXITED, or which is past its
	/// <see cref="StickyWorkerEntry.ExpiresAtUtc"/>, is not returned: it is removed and reaped in the
	/// background, and the caller is answered as though no worker existed. Reaching is not an aliveness
	/// assertion — the worker may exit between the lookup and the send, which is why
	/// <see cref="IWorkerChannel.HasExited"/> exists rather than a reach that throws.
	/// </remarks>
	bool TryReach(StickyWorkerKey key, out StickyWorkerEntry entry);

	/// <summary>
	/// Ends one sticky worker: closes its relay session, stops its standard-error drain, releases any
	/// shared-resource reservation it held and disposes its lease, which kills the process and returns
	/// its admission slot.
	/// </summary>
	/// <param name="key">The key to reap.</param>
	/// <param name="entry">The entry the caller means to end.</param>
	/// <returns>A task that completes when the worker is gone.</returns>
	/// <remarks>
	/// <b>Scoped to the ENTRY, not to the key, and that is the whole signature.</b> A key outlives the
	/// worker registered under it: a finished worker lingers so its status poll can be answered and is
	/// then superseded by the next operation of the same family on the same target. A reap that removed
	/// "whatever is under this key" would let a caller holding the FINISHED worker kill its SUCCESSOR —
	/// an operation that had just started, ended by a poll for the one before it. The passed entry is
	/// always released (its release is idempotent, and an entry nobody holds must not leak a process);
	/// only an entry that is still the registered one is removed from the registry.
	/// </remarks>
	ValueTask ReapAsync(StickyWorkerKey key, StickyWorkerEntry entry);

	/// <summary>
	/// Records that a worker's long operation has finished: releases the shared resource it was holding
	/// AT ONCE, and shortens its lifetime to a short linger window.
	/// </summary>
	/// <param name="key">The key the reporting worker is registered under.</param>
	/// <param name="entry">
	/// The worker that reported completion. The signal only takes effect when THIS entry is the one
	/// registered under <paramref name="key"/>.
	/// </param>
	/// <param name="linger">
	/// How much longer the worker stays reachable after saying it has finished. See
	/// <see cref="StickyWorkerEntry.MarkCompleted"/> for why this is not zero.
	/// </param>
	/// <returns><see langword="true"/> when a live worker was marked.</returns>
	/// <remarks>
	/// <b>The identity check is a correctness requirement, not a guard against the impossible.</b> The
	/// signal arrives on one worker's own read loop, and the key it is keyed by may by then hold a
	/// DIFFERENT worker — the successor of a completed one. Marking by key alone would let a worker's
	/// completion release the successor's shared-resource reservation and shorten its lifetime to a
	/// linger window while its operation was still running.
	/// </remarks>
	bool SignalCompleted(StickyWorkerKey key, StickyWorkerEntry entry, TimeSpan linger);

	/// <summary>
	/// Reaps every entry that has exited or is past its lifetime bound.
	/// </summary>
	/// <returns>How many entries were reaped.</returns>
	/// <remarks>
	/// <para>
	/// <b>The registry sweeps itself AT THE DEADLINE; this is the second caller, not the only one.</b> An
	/// earlier version was reached from the dispatch head alone, which made the lifetime bound conditional
	/// on more sticky traffic arriving: with none, a worker that had finished — or one that simply hung —
	/// outlived both its linger and its hard bound indefinitely, holding its process, its authenticated
	/// Creatio session and its admission slot. On a host whose sticky ceiling is one or two, one idle
	/// expired worker then denied every later long operation. A bound that only takes effect if somebody
	/// happens to call again is a hint.
	/// </para>
	/// <para>
	/// <b>AWAITED here, not fire-and-forget.</b> The dispatch-path caller is about to ask for the very
	/// admission slot this sweep returns, and a release that ran in the background would race it: the call
	/// that just freed capacity would be refused for want of it, intermittently, on a loaded host. It is
	/// kept beside the timer rather than replaced by it for exactly that reason — the timer removes the
	/// stranding, the dispatch-head call removes the race. Reaping from the READ LOOP is the case that
	/// must stay off-thread, and that one goes through <see cref="SignalCompleted"/> instead.
	/// </para>
	/// </remarks>
	ValueTask<int> ReapExpiredAsync();

	/// <summary>Gets the number of sticky workers currently supervised.</summary>
	int Count { get; }
}

/// <summary>
/// One supervised sticky worker: everything the parent must own for its whole life, and everything that
/// must be released when it ends.
/// </summary>
/// <remarks>
/// <b>Not a DTO.</b> It owns three disposable resources and releases them in a fixed order, so it is a
/// class with behaviour rather than a <see langword="record"/>. It carries no <c>I&lt;Name&gt;</c>
/// interface and is never resolved from a container: it is per-worker runtime state built from a live
/// lease, exactly like <see cref="WorkerStandardErrorDrain"/>.
/// </remarks>
public sealed class StickyWorkerEntry {

	private readonly ILogger _logger;
	private readonly ISharedResourceReservation _reservations;
	private readonly object _expiryGate = new();
	private DateTimeOffset _expiresAtUtc;
	private int _completed;
	private int _reaped;
	private int _unprovenAfterCancellation;

	/// <summary>
	/// Initializes a new instance of the <see cref="StickyWorkerEntry"/> class.
	/// </summary>
	/// <param name="lease">The lease the registry takes ownership of.</param>
	/// <param name="session">The OPEN relay session over that worker; the only consumer of its transport.</param>
	/// <param name="standardError">The running standard-error drain for that worker.</param>
	/// <param name="expiresAtUtc">When the worker must be gone by (AC-04).</param>
	/// <param name="reservation">The shared-resource reservation this worker holds, or null.</param>
	/// <param name="reservations">The reservation registry the token is released into.</param>
	/// <param name="logger">Host logger.</param>
	/// <exception cref="ArgumentNullException">A required argument is missing.</exception>
	public StickyWorkerEntry(IWorkerLease lease, WorkerRelaySession session,
		WorkerStandardErrorDrain standardError, DateTimeOffset expiresAtUtc,
		SharedResourceReservationToken reservation, ISharedResourceReservation reservations, ILogger logger) {
		Lease = lease ?? throw new ArgumentNullException(nameof(lease));
		Session = session ?? throw new ArgumentNullException(nameof(session));
		StandardError = standardError ?? throw new ArgumentNullException(nameof(standardError));
		_reservations = reservations ?? throw new ArgumentNullException(nameof(reservations));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_expiresAtUtc = expiresAtUtc;
		Reservation = reservation;
	}

	/// <summary>Gets the lease this entry owns.</summary>
	public IWorkerLease Lease { get; }

	/// <summary>
	/// Gets the OPEN relay session. Later calls of the family send over THIS session; a second transport
	/// built over the same streams would steal messages from it, because
	/// <c>ITransport.MessageReader</c> is a channel reader with one consumer.
	/// </summary>
	public WorkerRelaySession Session { get; }

	/// <summary>Gets the running standard-error drain (ADR §3.4: draining is liveness, not diagnostics).</summary>
	public WorkerStandardErrorDrain StandardError { get; }

	/// <summary>Gets the moment this worker must be gone by.</summary>
	/// <remarks>
	/// Moves only DOWN, and only through <see cref="MarkCompleted"/>: a bound that could be extended
	/// would stop being a bound, and T-8's whole point is that a sticky worker cannot outlive the
	/// authority it was started with however long its operation runs.
	/// </remarks>
	public DateTimeOffset ExpiresAtUtc {
		get {
			lock (_expiryGate) {
				return _expiresAtUtc;
			}
		}
	}

	/// <summary>
	/// Records that the worker's long operation has finished.
	/// </summary>
	/// <param name="linger">How much longer the worker stays reachable.</param>
	/// <returns><see langword="true"/> when this call moved the bound.</returns>
	/// <remarks>
	/// <para>
	/// <b>The reservation goes at once; the worker goes after a linger, and the asymmetry is the
	/// point.</b> A finished configuration build must stop denying its environment immediately — that is
	/// the whole reason the reservation exists and the reason its ceiling is a backstop rather than a
	/// schedule. The PROCESS, though, is still the only place the operation record lives: on stdio the
	/// compile and restart registries are DI singletons inside the worker, so reaping the instant the
	/// work ends would answer the status poll that follows it with "no such operation" for an operation
	/// that had just completed — the precise symptom this story exists to remove, produced by the fix
	/// for it.
	/// </para>
	/// <para>
	/// The linger is therefore what "the sticky worker serves both calls" (cross-call-state §3, P-1/P-2)
	/// costs when the second call arrives after the first has finished. It is bounded because a finished
	/// worker still holds an admission slot, and it is short because the only consumer is a poll the
	/// caller was explicitly told to make.
	/// </para>
	/// </remarks>
	public bool MarkCompleted(TimeSpan linger) {
		DateTimeOffset lingerUntil = DateTimeOffset.UtcNow + (linger > TimeSpan.Zero ? linger : TimeSpan.Zero);
		lock (_expiryGate) {
			// CLAMPED, never extended. The earlier version RETURNED here when the linger reached or passed
			// the hard bound — and returning meant the worker was never marked completed and, worse, never
			// released its shared-resource reservation. An operation finishing inside the last linger-width
			// of its lifetime therefore went on refusing every new build for that target, and went on
			// holding an admission slot, until hard expiry — for a build that had already finished. The
			// bound is a ceiling on how long the worker may LIVE; it was never a reason to keep the
			// environment locked after the work was done.
			if (lingerUntil < _expiresAtUtc) {
				_expiresAtUtc = lingerUntil;
			}
		}
		Volatile.Write(ref _completed, 1);
		_reservations.Release(Reservation);
		return true;
	}

	/// <summary>Gets the shared-resource reservation this worker holds, or <see langword="null"/>.</summary>
	public SharedResourceReservationToken Reservation { get; }

	/// <summary>
	/// Gets a value indicating whether this worker has reported that its long operation finished.
	/// </summary>
	/// <remarks>
	/// Read by the poll path, which reaps a completed worker as soon as it has answered: the poll is the
	/// only consumer a lingering process has, so once it has its answer the linger has nothing left to
	/// protect and the slot should not be held for the rest of the window.
	/// </remarks>
	public bool IsCompleted => Volatile.Read(ref _completed) == 1;

	/// <summary>
	/// Gets a value indicating whether this worker must be PROVED alive before its session carries
	/// another call, because the call before it was abandoned by its caller after the request had been
	/// written.
	/// </summary>
	/// <remarks>
	/// <b>Not the same question as <see cref="WorkerRelaySession.IsRetired"/>, and the difference is the
	/// whole of ADR §3.2a.</b> A send that did NOT complete may have left half a frame on the child's
	/// stdin, so its session is retired and the worker goes. A send that DID complete left the transport
	/// whole and the worker was told through <c>notifications/cancelled</c> — the session is reusable,
	/// but a worker that ignores that notification is still busy with the call nobody is waiting for, and
	/// that is what the bounded probe asks about.
	/// </remarks>
	public bool RequiresLivenessProof => Volatile.Read(ref _unprovenAfterCancellation) == 1;

	/// <summary>
	/// Records that a call over this worker's session was abandoned by its caller AFTER its request had
	/// been written, so the next call must prove the worker before reusing it.
	/// </summary>
	public void MarkCallAbandoned() => Volatile.Write(ref _unprovenAfterCancellation, 1);

	/// <summary>
	/// Records that the worker answered a bounded liveness probe, so the next call needs no further
	/// proof.
	/// </summary>
	public void MarkProvedAlive() => Volatile.Write(ref _unprovenAfterCancellation, 0);

	/// <summary>
	/// Gets a value indicating whether this worker is still usable: its session has not been retired, it
	/// has not exited and it has not passed its lifetime bound.
	/// </summary>
	/// <param name="utcNow">The instant to judge expiry against.</param>
	/// <returns><see langword="true"/> when the worker may serve another call.</returns>
	/// <remarks>
	/// <b>The session term is load-bearing, not defensive.</b> Process lifetime alone answers "is there
	/// still a worker there", never "can anything still be said to it". A session retired by a send that
	/// did not complete (ADR §3.2a) belongs to a process that is alive and permanently unreachable, and
	/// reporting it live would hand the next poll a transport that may hold half a JSON-RPC frame while
	/// its admission slot and its shared-resource reservation stayed held until expiry.
	/// </remarks>
	public bool IsLive(DateTimeOffset utcNow) =>
		!Lease.HasExited && !Session.IsRetired && utcNow < ExpiresAtUtc;

	/// <summary>
	/// Releases everything this worker held, once. Safe to call twice — the second call does nothing.
	/// </summary>
	/// <returns>A task that completes when the worker is gone.</returns>
	/// <remarks>
	/// ORDER IS LOAD-BEARING. The session is closed first so the read loop stops before the pipes are
	/// torn down; the drain is stopped next, while the process still exists, so the pump ends on an EOF
	/// rather than on an exception; the reservation is released before the lease so a target is never
	/// denied by a worker that is already gone; the lease is disposed last, because that is the ONLY
	/// thing that returns the admission slot and it must run even if an earlier step threw.
	/// </remarks>
	public async ValueTask ReleaseAsync() {
		if (Interlocked.Exchange(ref _reaped, 1) == 1) {
			return;
		}
		try {
			await Session.DisposeAsync().ConfigureAwait(false);
		}
		catch (Exception exception) {
			_logger.WriteWarning(
				"Closing a sticky MCP worker's relay session failed: "
				+ SensitiveErrorTextRedactor.Redact(exception.Message));
		}
		try {
			await StandardError.StopAsync().ConfigureAwait(false);
		}
		catch (Exception exception) {
			_logger.WriteWarning(
				"Stopping a sticky MCP worker's standard-error drain failed: "
				+ SensitiveErrorTextRedactor.Redact(exception.Message));
		}
		_reservations.Release(Reservation);
		Lease.Dispose();
	}
}

/// <inheritdoc cref="IStickyWorkerRegistry"/>
public sealed class StickyWorkerRegistry : IStickyWorkerRegistry, IDisposable, IAsyncDisposable {

	private readonly Dictionary<StickyWorkerKey, StickyWorkerEntry> _entries = [];
	private readonly object _gate = new();
	private readonly ILogger _logger;
	private readonly TimeProvider _time;
	private readonly ITimer _deadline;

	/// <summary>
	/// The sweeps this registry has scheduled, CHAINED rather than run in parallel, so disposal has one
	/// task to await and a deadline arriving mid-sweep is queued behind it instead of dropped.
	/// </summary>
	private Task _sweeps = Task.CompletedTask;

	private bool _disposed;

	/// <summary>
	/// Initializes a new instance of the <see cref="StickyWorkerRegistry"/> class.
	/// </summary>
	/// <param name="logger">Host logger; reap diagnostics go here, never to standard output.</param>
	/// <param name="timeProvider">
	/// The clock expiry is judged against and the deadline is scheduled on. Optional so the container
	/// supplies the registered <see cref="TimeProvider"/> (BindingsModule) and a test can supply a
	/// controllable one; <see cref="TimeProvider.System"/> when absent.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="logger"/> is <see langword="null"/>.</exception>
	public StickyWorkerRegistry(ILogger logger, TimeProvider timeProvider = null) {
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_time = timeProvider ?? TimeProvider.System;
		// Created DISARMED. The callback closes over an instance this constructor has not finished
		// building, and an infinite due time is what makes it unreachable until something is registered
		// rather than merely unlikely to be reached.
		_deadline = _time.CreateTimer(OnDeadlineReached, state: null,
			Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
	}

	/// <inheritdoc/>
	public int Count {
		get {
			lock (_gate) {
				return _entries.Count;
			}
		}
	}

	/// <inheritdoc/>
	public bool TryRegister(StickyWorkerKey key, StickyWorkerEntry entry) {
		ArgumentNullException.ThrowIfNull(key);
		ArgumentNullException.ThrowIfNull(entry);
		lock (_gate) {
			if (_entries.ContainsKey(key)) {
				return false;
			}
			_entries[key] = entry;
			RearmLocked();
			return true;
		}
	}

	/// <inheritdoc/>
	public bool TryReach(StickyWorkerKey key, out StickyWorkerEntry entry) {
		ArgumentNullException.ThrowIfNull(key);
		StickyWorkerEntry dead = null;
		lock (_gate) {
			if (_entries.TryGetValue(key, out StickyWorkerEntry found)) {
				if (found.IsLive(_time.GetUtcNow())) {
					entry = found;
					return true;
				}
				_entries.Remove(key);
				RearmLocked();
				dead = found;
			}
		}
		ReleaseInBackground(dead, key);
		entry = null;
		return false;
	}

	/// <inheritdoc/>
	public async ValueTask ReapAsync(StickyWorkerKey key, StickyWorkerEntry entry) {
		ArgumentNullException.ThrowIfNull(key);
		ArgumentNullException.ThrowIfNull(entry);
		lock (_gate) {
			if (_entries.TryGetValue(key, out StickyWorkerEntry registered) && ReferenceEquals(registered, entry)) {
				_entries.Remove(key);
				RearmLocked();
			}
		}
		// Released even when the key now holds somebody else: this entry is then owned by nobody, and the
		// alternative to releasing it is a process, an admission slot and a reservation that nothing ever
		// returns. Release is idempotent, so reaping an already-reaped entry costs nothing.
		await entry.ReleaseAsync().ConfigureAwait(false);
	}

	/// <inheritdoc/>
	public bool SignalCompleted(StickyWorkerKey key, StickyWorkerEntry entry, TimeSpan linger) {
		ArgumentNullException.ThrowIfNull(key);
		if (entry is null) {
			// A signal that arrived before the parent had built the entry it belongs to. There is nothing
			// to mark and nothing to guess at: marking whatever the key held would be the very confusion
			// this parameter exists to remove.
			return false;
		}
		lock (_gate) {
			if (!_entries.TryGetValue(key, out StickyWorkerEntry registered)
				|| !ReferenceEquals(registered, entry)
				|| !registered.MarkCompleted(linger)) {
				return false;
			}
			// Completion moved this worker's expiry DOWN, past the deadline the registry is currently
			// armed for. Re-arming here is what makes the linger a window the registry acts on rather
			// than a number written into an entry: without it the worker keeps its admission slot until
			// the hard bound — half an hour after a build that finished in five minutes.
			RearmLocked();
			return true;
		}
	}

	/// <inheritdoc/>
	public async ValueTask<int> ReapExpiredAsync() {
		List<(StickyWorkerKey Key, StickyWorkerEntry Entry)> expired = [];
		DateTimeOffset utcNow = _time.GetUtcNow();
		lock (_gate) {
			foreach (KeyValuePair<StickyWorkerKey, StickyWorkerEntry> pair in _entries) {
				if (!pair.Value.IsLive(utcNow)) {
					expired.Add((pair.Key, pair.Value));
				}
			}
			foreach ((StickyWorkerKey key, StickyWorkerEntry entry) in expired) {
				// Entry-scoped, like ReapAsync. Collection and removal share this lock today, so no
				// successor can slip in between them; the check states the invariant locally so that
				// splitting the two later cannot quietly turn this into a key-scoped reap that ends the
				// operation which just replaced the expired one.
				if (_entries.TryGetValue(key, out StickyWorkerEntry registered)
					&& ReferenceEquals(registered, entry)) {
					_entries.Remove(key);
				}
			}
			RearmLocked();
		}
		foreach ((StickyWorkerKey key, StickyWorkerEntry entry) in expired) {
			try {
				await entry.ReleaseAsync().ConfigureAwait(false);
			}
			catch (Exception exception) {
				_logger.WriteWarning(string.Format(CultureInfo.InvariantCulture,
					"Reaping the sticky MCP worker for operation family '{0}' failed: {1}",
					key.Family, SensitiveErrorTextRedactor.Redact(exception.Message)));
			}
		}
		return expired.Count;
	}

	/// <summary>
	/// Stops the registry's own scheduling.
	/// </summary>
	/// <remarks>
	/// <b>The flag goes up BEFORE the timer comes down, and that order is the guarantee.</b> Both the
	/// callback and the sweep it starts test the flag under <see cref="_gate"/>, so a callback that had
	/// already begun when disposal ran does nothing — the promise does not rest on what a particular
	/// <see cref="ITimer"/> implementation does about in-flight callbacks. Disposing the timer is what
	/// stops a new one from starting.
	/// </remarks>
	public void Dispose() {
		lock (_gate) {
			if (_disposed) {
				return;
			}
			_disposed = true;
		}
		_deadline.Dispose();
	}

	/// <summary>
	/// Stops the registry's own scheduling and awaits the sweep that was already running.
	/// </summary>
	/// <returns>A task that completes when no sweep this registry scheduled is still running.</returns>
	/// <remarks>
	/// The chain is read AFTER the timer has been disposed, because the timer callback is the only thing
	/// that ever appends to it: reading it earlier could miss a sweep queued by a callback that was
	/// running at the time. The entries themselves are deliberately NOT reaped here — their leases are
	/// owned for the life of the operation, and ending live long operations because a container is being
	/// torn down is a decision for the shutdown path, not for a disposal that only stops a timer.
	/// </remarks>
	public async ValueTask DisposeAsync() {
		bool alreadyDisposed;
		lock (_gate) {
			alreadyDisposed = _disposed;
			_disposed = true;
		}
		if (!alreadyDisposed) {
			await _deadline.DisposeAsync().ConfigureAwait(false);
		}
		Task sweeps;
		lock (_gate) {
			sweeps = _sweeps;
		}
		try {
			await sweeps.ConfigureAwait(false);
		}
		catch (Exception exception) {
			// A faulted sweep must not throw out of container disposal, where the exception would be
			// attributed to whatever the host was shutting down.
			_logger.WriteWarning(
				"A sticky MCP worker sweep failed while the registry was being disposed: "
				+ SensitiveErrorTextRedactor.Redact(exception.Message));
		}
	}

	/// <summary>
	/// Re-arms the lifetime deadline against the EARLIEST outstanding expiry.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Deadline-driven rather than polled, because the expiries are known exactly.</b> Every entry
	/// carries the instant it must be gone by, so a fixed interval would be a choice between reaping late
	/// (a slot held for up to one period past the bound) and waking a host that is idle for hours purely
	/// to look at a dictionary. Scheduling against the earliest expiry costs one timer, wakes only when
	/// something is actually due, and re-arms on the four events that can change what "earliest" means:
	/// an entry added, an entry superseded or reaped, a completion moving an expiry DOWN, and the sweep
	/// itself.
	/// </para>
	/// <para>
	/// The one honest caveat: a wall-clock deadline can fire LATE — a suspended host, a loaded thread
	/// pool. That only delays a reap, never causes an early one, because the sweep re-tests
	/// <see cref="StickyWorkerEntry.IsLive"/> against the clock rather than trusting the timer.
	/// </para>
	/// <para>Callers hold <see cref="_gate"/>.</para>
	/// </remarks>
	private void RearmLocked() {
		if (_disposed) {
			return;
		}
		DateTimeOffset? earliest = null;
		foreach (StickyWorkerEntry entry in _entries.Values) {
			DateTimeOffset expiry = entry.ExpiresAtUtc;
			if (earliest is null || expiry < earliest.Value) {
				earliest = expiry;
			}
		}
		if (earliest is null) {
			_deadline.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
			return;
		}
		TimeSpan delay = earliest.Value - _time.GetUtcNow();
		_deadline.Change(delay > TimeSpan.Zero ? delay : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
	}

	/// <summary>
	/// Runs when the earliest outstanding lifetime bound is reached.
	/// </summary>
	/// <param name="state">Unused.</param>
	/// <remarks>
	/// <b>Nothing may escape this method.</b> It runs on a timer thread with no caller above it, so an
	/// exception here is an unhandled exception on a pool thread — it ends the MCP host, which is a far
	/// worse outcome than the stale worker the sweep was going to reap.
	/// </remarks>
	private void OnDeadlineReached(object state) {
		try {
			lock (_gate) {
				if (_disposed) {
					return;
				}
				_sweeps = _sweeps.ContinueWith(_ => SweepExpiredAsync(), CancellationToken.None,
					TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
			}
		}
		catch (Exception exception) {
			_logger.WriteWarning(
				"Scheduling a sticky MCP worker lifetime sweep failed: "
				+ SensitiveErrorTextRedactor.Redact(exception.Message));
		}
	}

	/// <summary>
	/// Reaps whatever the deadline was armed for, off the timer thread.
	/// </summary>
	/// <returns>A task that completes when the sweep is done.</returns>
	/// <remarks>
	/// Off the timer thread because a reap closes the worker's relay session, which joins its read loop:
	/// blocking a pool thread on that is exactly what the drain and the session were built to avoid.
	/// </remarks>
	private async Task SweepExpiredAsync() {
		bool disposed;
		lock (_gate) {
			disposed = _disposed;
		}
		if (disposed) {
			return;
		}
		try {
			await ReapExpiredAsync().ConfigureAwait(false);
		}
		catch (Exception exception) {
			_logger.WriteWarning(
				"Sweeping expired sticky MCP workers failed: "
				+ SensitiveErrorTextRedactor.Redact(exception.Message));
		}
	}

	/// <summary>
	/// Releases an already-removed entry off the caller's thread.
	/// </summary>
	/// <param name="entry">The entry, or null.</param>
	/// <param name="key">The key it was registered under, for the log line.</param>
	/// <remarks>
	/// <b>Off-thread on purpose, and it is not a style choice.</b> Reaping closes the worker's relay
	/// session, and the session's disposal joins its own read loop. The two callers that reap — the
	/// completion-signal tap, which runs INSIDE that read loop, and a lookup on the dispatch path — would
	/// otherwise either deadlock on themselves or pay a teardown on a call that has nothing to do with
	/// the worker being reaped.
	/// </remarks>
	private void ReleaseInBackground(StickyWorkerEntry entry, StickyWorkerKey key) {
		if (entry is null) {
			return;
		}
		_ = Task.Run(async () => {
			try {
				await entry.ReleaseAsync().ConfigureAwait(false);
			}
			catch (Exception exception) {
				_logger.WriteWarning(string.Format(CultureInfo.InvariantCulture,
					"Reaping the sticky MCP worker for operation family '{0}' failed: {1}",
					key.Family, SensitiveErrorTextRedactor.Redact(exception.Message)));
			}
		});
	}
}
