using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common.McpWorker;
using ModelContextProtocol.Protocol;

namespace Clio.Command.McpServer.Relay;

/// <summary>
/// The STICKY half of <see cref="McpWorkerCallDispatcher"/>: the four long-running families whose worker
/// outlives the response that started it, so a later status poll reaches the process holding the
/// operation (ENG-95262 story 7).
/// </summary>
public sealed partial class McpWorkerCallDispatcher {

	/// <summary>
	/// The tool-argument name every environment-bound clio MCP tool uses for its target.
	/// </summary>
	/// <remarks>
	/// The parent reads this off the raw call rather than resolving the tool's own arguments record,
	/// because it has no instance of that record and no binder for it. One convention covers the whole
	/// catalog — see <see cref="TryReadStringArgument"/> for the two shapes it appears in.
	/// </remarks>
	internal const string EnvironmentNameArgument = "environment-name";

	/// <summary>
	/// The tool-argument name the credentials-started members of a long-running family use instead of
	/// <see cref="EnvironmentNameArgument"/>.
	/// </summary>
	/// <remarks>
	/// <c>restart-by-credentials</c> takes <c>url</c> / <c>userName</c> / <c>password</c> and names no
	/// registered environment at all. Reading only the environment name would leave EVERY
	/// credentials-started restart, against every stand, sharing one unresolved sticky key — so the
	/// second one would collide with the first and quietly degrade to a per-call worker whose readiness
	/// wait nothing can reach. Only the url is read; the credentials themselves are never touched here.
	/// </remarks>
	internal const string UrlArgument = "url";

	/// <summary>The <c>clio-run</c> executor's own argument naming the tool it is to run.</summary>
	/// <remarks>
	/// Read rather than assumed because the executor's params are relayed to the worker VERBATIM, so the
	/// envelope the client sent is the envelope this parent has to derive a key from.
	/// </remarks>
	internal const string ExecutorCommandArgument = "command";

	/// <summary>The <c>clio-run</c> executor's own argument carrying the inner call's arguments.</summary>
	internal const string ExecutorArgumentsArgument = "args";

	/// <summary>Machine-readable error class emitted when a shared, target-wide resource is already held.</summary>
	/// <remarks>
	/// NOT <see cref="BudgetExpiredErrorClass"/> and not <see cref="RelayFailureErrorClass"/>. The call was
	/// refused for a reason that is neither a slow backend nor a clio defect: another configuration build
	/// owns the environment. Telling an agent "the relay failed" would send it to look for a bug, and
	/// "creatio-timeout" would send it into a retry loop against a target that is busy for minutes.
	/// </remarks>
	internal const string SharedResourceBusyErrorClass = "clio-configuration-build-in-progress";

	/// <summary>Machine-readable error class emitted when the host is running all the long operations it can.</summary>
	/// <remarks>
	/// The one condition whose remedy is neither retry nor investigation but an operator changing a
	/// number, so it carries its own class. Mapping it onto the relay-failure class — which is what an
	/// unhandled <see cref="WorkerStickyCapacityExceededException"/> would have done, since the spawn
	/// site's catch-all produces that envelope — would report a correctly working saturation guard as a
	/// clio bug.
	/// </remarks>
	internal const string StickyCapacityErrorClass = "clio-worker-capacity";

	/// <summary>
	/// Machine-readable error class emitted when this family already has a live sticky worker for the
	/// target, so the call would have been a SECOND operation of the same kind against one environment.
	/// </summary>
	/// <remarks>
	/// Neither a capacity refusal nor a relay failure: the host has room and nothing is broken — the
	/// operation the caller asked for is already running, and the remedy is to poll it rather than to
	/// retry, wait for capacity, or look for a clio bug. It is the same statement
	/// <see cref="SharedResourceBusyErrorClass"/> makes for the families a configuration-build
	/// reservation already serialises; this one covers the families that have no shared resource to
	/// reserve — <c>restart-*</c> above all — and are therefore the only ones that could ever reach the
	/// spawn path twice for one key.
	/// </remarks>
	internal const string LongOperationInProgressErrorClass = "clio-long-operation-in-progress";

	/// <summary>
	/// How much longer than the child's response deadline the parent waits before giving up on one sticky
	/// call.
	/// </summary>
	/// <remarks>
	/// <para>
	/// It covers spawn plus <c>initialize</c> (p50 2.763 s on Windows Server 2022, ADR §2.4) plus the
	/// child's own in-progress envelope being written, framed, read off a pipe and forwarded — a minute is
	/// generous for all of it. It is a MARGIN on top of a deadline, not a timeout of its own, which is why
	/// it is expressed as headroom rather than as a second absolute number that could quietly fall below
	/// the first.
	/// </para>
	/// <para>
	/// <b>Declared BEFORE <see cref="DefaultStickyCallBudget"/>, and that is not cosmetic.</b> Static field
	/// initialisers run in TEXTUAL order, so a headroom declared after the budget that consumes it is still
	/// <see cref="TimeSpan.Zero"/> when the budget is computed — the parent bound would silently equal the
	/// child's deadline exactly, turning the ordering this exists to guarantee into a scheduling race whose
	/// loser is a killed long operation. Caught by
	/// <c>DefaultStickyCallBudget_ShouldBeDerivedFromTheChildsResponseDeadline</c> rather than in
	/// production, which is the only reason it is written down here.
	/// </remarks>
	internal static readonly TimeSpan StickyCallBudgetHeadroom = TimeSpan.FromSeconds(60);

	/// <summary>
	/// Wall-clock bound on ONE call to a sticky worker — not on the operation the worker is running.
	/// </summary>
	/// <remarks>
	/// <para>
	/// It must stay above the child's own MCP response deadline, because on this family it is the child's
	/// in-progress envelope that RETURNS the call (ADR rule 11 — which is also why a sticky worker KEEPS
	/// <c>CLIO_MCP_RESPONSE_DEADLINE_SECONDS</c> while an ordinary one is denied it). A parent bound below
	/// that deadline would kill every long operation a fraction before it answered, and the symptom would
	/// be a compile that always "fails" and always turns out to have run.
	/// </para>
	/// <para>
	/// <b>DERIVED from that deadline rather than stated beside it</b>, because the deadline is
	/// operator-configurable up to 600 s: a fixed 300 s here would hold the invariant on the default and
	/// break it the moment somebody raised <c>CLIO_MCP_RESPONSE_DEADLINE_SECONDS</c> past it — the
	/// invariant asserted by a comment rather than enforced by the code. Both values are resolved at type
	/// load from the same environment, so the parent and the child cannot disagree about which deadline is
	/// in force. On the shipped default this is 150 s + 60 s = 210 s.
	/// </para>
	/// </remarks>
	internal static readonly TimeSpan DefaultStickyCallBudget =
		Tools.McpProgressHeartbeat.DefaultResponseDeadline + StickyCallBudgetHeadroom;


	/// <summary>
	/// How long a sticky worker stays reachable AFTER it has reported that its long operation finished.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Not zero, and the reason is a shipped contract rather than caution.</b> On stdio the compile and
	/// restart operation registries are DI singletons INSIDE the worker, so the process is the only place
	/// the operation record lives. Reaping the instant the work ends would answer the status poll the
	/// caller was explicitly told to make with "no such operation" for an operation that had just
	/// finished — the exact symptom this story exists to remove, produced by its own fix. This window is
	/// what "the sticky worker serves both calls" (cross-call-state §3, P-1/P-2) costs when the second
	/// call arrives after the first has completed; it disappears if and when the registries themselves
	/// move to the parent.
	/// </para>
	/// <para>
	/// The shared configuration-build reservation is NOT held for it: that is released the moment the
	/// signal arrives, because a finished build must stop denying its environment at once. Only the
	/// admission slot is held, and it is held for minutes rather than for the lifetime bound's half hour.
	/// </para>
	/// </remarks>
	internal static readonly TimeSpan DefaultStickyCompletionLinger = TimeSpan.FromMinutes(5);

	/// <summary>
	/// The keys a sticky worker is being created for RIGHT NOW: spawned, handshaking, or not yet
	/// registered.
	/// </summary>
	/// <remarks>
	/// The window between spawn and registration is the whole defect this closes. Only starters ever
	/// touch it, and an entry lives in it for spawn plus handshake, never for the operation.
	/// </remarks>
	private readonly HashSet<StickyWorkerKey> _startsInFlight = [];

	/// <summary>Guards <see cref="_startsInFlight"/>; separate from the registry's own lock.</summary>
	private readonly object _startGateLock = new();

	/// <summary>
	/// Runs one call of a long-running family: reach the family's live sticky worker if there is one,
	/// otherwise start it (when this tool is the family's starter) or fall back to an ordinary per-call
	/// worker (when it is a poller with nothing to poll).
	/// </summary>
	/// <param name="toolName">The canonical tool name.</param>
	/// <param name="metadata">The declared execution metadata for that tool.</param>
	/// <param name="parameters">The caller's params.</param>
	/// <param name="parentSession">The parent leg the child's traffic is relayed to.</param>
	/// <param name="cancellationToken">Caller cancellation.</param>
	/// <returns>The worker's result, or a named error envelope.</returns>
	private async ValueTask<CallToolResult> DispatchStickyAsync(
		string toolName,
		McpToolExecutionMetadata metadata,
		CallToolRequestParams parameters,
		IParentMcpSession parentSession,
		CancellationToken cancellationToken) {
		EnvironmentOptions options = ReadTargetOptions(parameters, toolName);
		string environmentName = options.Environment ?? options.Uri;

		// FIRST, before any reservation is taken or reached: retire sticky workers that have exited or
		// passed their lifetime bound (AC-04). The order is load-bearing rather than tidy — a worker at
		// exactly StickyWorkerLifetimeBound.ExplicitMaximum still holds the reservation whose reclaim
		// ceiling that bound equals, so sweeping after reserving would let a reclaim and a live holder
		// coexist for one call.
		await _stickyWorkers.ReapExpiredAsync().ConfigureAwait(false);

		StickyWorkerKey key = new(metadata.OperationFamily, SafeTenantKey(options));
		CallToolRequestParams childParameters = WithoutParentSessionMetadata(parameters);

		// Only an OBSERVER reaches an existing worker. A starter that reused the family's live worker
		// would multiplex a SECOND operation onto the process running the first — an install-process-builder
		// answered by the worker holding somebody's compile — which is not what the reservation refusing it
		// looks like and is not what any caller asked for. A starter always creates, and what stops two of
		// them is the reservation below, not the reach above.
		if (!metadata.StartsOperation) {
			// Reaching takes NO admission slot — the binding half of ADR §3.2c. The slot a poll would
			// otherwise wait for is held by the very worker it is reaching, which is hold-and-wait and does
			// not resolve under load. _stickyPoll cannot spawn: that is what its dependency says.
			CallToolResult reached = await _stickyPoll
				.ReachAndCallAsync(key, childParameters, _stickyCallBudget, cancellationToken)
				.ConfigureAwait(false);
			return reached ?? await DispatchPerCallAsync(toolName, parameters, parentSession,
				cancellationToken).ConfigureAwait(false);
		}

		// STARTING IS SINGLE-FLIGHT PER KEY, and the gate is held across spawn, handshake and
		// registration — not across the operation, which is why it is taken here and dropped the moment
		// the registry has the entry. Without it two starters of one family both spawn, one loses
		// TryRegister, and the loser is released the instant its response is composed: on
		// restart-by-environment-name that response is IN-PROGRESS and the readiness wait continues
		// INSIDE the worker, so the release destroys the operation and its state. It is taken with ZERO
		// wait: queueing behind a starter that may sit in handshake for the whole sticky call budget
		// would spend minutes of the caller's patience to arrive at the same refusal with less
		// information.
		// THE RESERVATION IS TAKEN FIRST — before the per-key start gate below, not after it. The order is
		// load-bearing and was corrected on 2026-08-19 after a review pointed at the race it decides.
		//
		// The reservation is keyed by TARGET, so it excludes across principals AND across families (a
		// compile excludes an install-process-builder); the start gate is keyed by the sticky key, which is
		// narrower. When two same-target configuration-build starters race, whichever check runs first is
		// the one that answers — so with the gate first, the second caller was told
		// 'clio-long-operation-in-progress' (your own family is busy) when the true and more useful answer
		// is 'clio-configuration-build-in-progress' (this ENVIRONMENT is rebuilding, by anyone). Taking the
		// broader claim first makes the refusal say the broader thing.
		//
		// A caller that then loses the start gate releases the reservation on that path, so nothing is
		// stranded by winning the wrong one of the two.
		SharedResourceReservationToken reservation = null;
		if (metadata.SharedFileResource == McpToolSharedFileResource.ConfigurationBuild) {
			// Keyed by the NORMALISED TARGET and the resource, never by the tenant key: Creatio's
			// configuration build is server-wide, so two principals on one environment must exclude each
			// other. This is the parent's dictionary, so it also excludes across worker PROCESSES — which
			// the tool-side reservation, living in whichever child ran the tool, structurally cannot.
			if (!_reservations.TryReserve(McpToolSharedFileResource.ConfigurationBuild,
					SafeTargetKey(options), out reservation)) {
				return SharedResourceBusyResult(toolName, environmentName);
			}
		}

		using IDisposable startGate = TryEnterStartGate(key);
		if (startGate is null) {
			// Losing the gate after winning the reservation must give the reservation back, or one refused
			// racer would hold this target's configuration build until the reclaim ceiling.
			_reservations.Release(reservation);
			return LongOperationInProgressResult(toolName, key.Family, environmentName);
		}


		if (_stickyWorkers.TryReach(key, out StickyWorkerEntry existing)) {
			if (!existing.IsCompleted) {
				// Refused BEFORE anything is created. This is the answer for the families that have no
				// shared resource to reserve — restart-* above all — which are exactly the ones that could
				// otherwise reach the spawn path twice for one key.
				_reservations.Release(reservation);
				return LongOperationInProgressResult(toolName, key.Family, environmentName);
			}
			// A worker that has REPORTED COMPLETION is live only for its linger window, and the only thing
			// that window protects is a status poll for work that has already ended. A new starter
			// supersedes it: treating a lingering worker as "already in progress" would deny this target
			// its next long operation for minutes after the last one finished.
			await _stickyWorkers.ReapAsync(key, existing).ConfigureAwait(false);
		}

		return await StartStickyWorkerAsync(toolName, key, environmentName, reservation, parameters,
			childParameters, parentSession, startGate, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Takes the per-key start gate, or answers that another starter of this family already holds it.
	/// </summary>
	/// <param name="key">The sticky key a worker is about to be created for.</param>
	/// <returns>
	/// A lease to be disposed once the worker is registered, or <see langword="null"/> when a start for
	/// that key is already in flight.
	/// </returns>
	/// <remarks>
	/// A set of keys under a lock rather than a semaphore per key, because the gate NEVER WAITS: with no
	/// waiters there is no queue to model, nothing to time out and no per-key synchronisation primitive
	/// to reference-count and dispose. Held only for spawn, handshake and registration — the operation
	/// itself runs outside it, since the registration that makes the worker reachable happens before the
	/// starting call is sent.
	/// </remarks>
	private IDisposable TryEnterStartGate(StickyWorkerKey key) {
		lock (_startGateLock) {
			if (!_startsInFlight.Add(key)) {
				return null;
			}
		}
		return new StickyStartGateLease(this, key);
	}

	/// <summary>
	/// Frees the per-key start gate so the next starter of that family may create a worker.
	/// </summary>
	/// <param name="key">The key to release.</param>
	private void ExitStartGate(StickyWorkerKey key) {
		lock (_startGateLock) {
			_startsInFlight.Remove(key);
		}
	}

	/// <summary>
	/// Spawns the family's sticky worker, hands it to the registry and runs the starting call on it.
	/// </summary>
	/// <param name="toolName">The canonical tool name.</param>
	/// <param name="key">The key the worker is registered and later reached under.</param>
	/// <param name="environmentName">The target the call named, for the refusal envelope.</param>
	/// <param name="reservation">The shared-resource reservation this worker will hold, or null.</param>
	/// <param name="parameters">The caller's params, for the per-call fallback.</param>
	/// <param name="childParameters">The params to send to the worker.</param>
	/// <param name="parentSession">The parent leg.</param>
	/// <param name="startGate">
	/// The per-key start gate, dropped as soon as the registry owns the worker. The caller keeps its own
	/// <c>using</c> on it, so every failure path releases it too; the lease is idempotent.
	/// </param>
	/// <param name="cancellationToken">Caller cancellation.</param>
	/// <returns>The worker's result, or a named error envelope.</returns>
	private async ValueTask<CallToolResult> StartStickyWorkerAsync(
		string toolName,
		StickyWorkerKey key,
		string environmentName,
		SharedResourceReservationToken reservation,
		CallToolRequestParams parameters,
		CallToolRequestParams childParameters,
		IParentMcpSession parentSession,
		IDisposable startGate,
		CancellationToken cancellationToken) {
		// A sticky worker KEEPS clio's own response-deadline override (ADR rule 11): its in-progress
		// envelope is what returns the call, and stripping it turned a 25 s backend call into a 77 s block
		// in the prototype.
		IReadOnlyDictionary<string, string> childEnvironment = McpWorkerEnvironment.ComposeChildEnvironment(
			ReadFrozenFeatures(), McpWorkerLifetime.Sticky);
		// The lease's own budget IS the sticky lifetime bound, so the supervisor's view of how long this
		// worker may live and the registry's view cannot drift apart.
		WorkerSpawnRequest spawnRequest =
			ComposeSpawnRequest(childEnvironment, StickyWorkerLifetimeBound.ExplicitMaximum) with {
				Lifetime = WorkerLifetime.Sticky
			};

		IWorkerLease lease;
		try {
			lease = await _supervisor.SpawnContainedAsync(spawnRequest, cancellationToken).ConfigureAwait(false);
		}
		catch (WorkerStickyCapacityExceededException exception) {
			_reservations.Release(reservation);
			_logger.WriteWarning($"MCP worker for '{toolName}' was refused: {exception.Message}");
			return StickyCapacityResult(toolName, exception);
		}
		catch (OperationCanceledException) {
			_reservations.Release(reservation);
			throw;
		}
		catch (WorkerQueueWaitExpiredException exception) {
			// Saturation of the SHARED pool, which is a different thing from the sticky ceiling: this
			// starter already holds a place under the ceiling, and ordinary per-call reads are holding the
			// slots. That clears in seconds and is worth retrying, so it must not arrive as a relay
			// failure. The reservation goes back first — the call is not going to happen.
			_reservations.Release(reservation);
			_logger.WriteWarning($"Sticky MCP worker for '{toolName}' was not started: {exception.Message}");
			return WorkerSaturationResult(toolName, exception);
		}
		catch (Exception exception) {
			_reservations.Release(reservation);
			_logger.WriteWarning(
				$"Sticky MCP worker for '{toolName}' could not be started: "
				+ SensitiveErrorTextRedactor.Redact(exception.Message));
			return RelayFailureResult(toolName, "the worker process could not be started", exception.Message, null);
		}

		// Every lease consumer must drain (ADR §3.4). A sticky worker's drain lives as long as the worker,
		// so it is owned by the registry entry rather than by this call.
		WorkerStandardErrorDrain standardError = new(lease.StandardError, StandardErrorTailLimit);
		standardError.Start();

		StickyWorkerEntry entry = null;
		bool ownershipTransferred = false;
		try {
			using CancellationTokenSource handshakeSource =
				CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			handshakeSource.CancelAfter(_stickyCallBudget);
			ITransport childTransport = await _transportOwner
				.ConnectAsync(lease.StandardInput, lease.StandardOutput, handshakeSource.Token)
				.ConfigureAwait(false);
			// The tap is scoped to THIS ENTRY, never to the key: the key outlives the worker registered
			// under it (a finished worker lingers and is then superseded), so a signal keyed by key alone
			// could release a SUCCESSOR's reservation and shorten its lifetime while its operation was
			// still running. The box is written after the session opens and read on the relay's read
			// loop, so both sides go through Volatile; a signal that arrives before the entry exists is
			// consumed and does nothing, which is what the key-scoped tap did at that moment anyway.
			StrongBox<StickyWorkerEntry> tapEntry = new(null);
			WorkerRelayOptions relayOptions = new() {
				NotificationTap = notification =>
					TapCompletionSignal(notification, key, Volatile.Read(ref tapEntry.Value))
			};
			WorkerRelaySession session = await _relay
				.OpenAsync(childTransport, parentSession, relayOptions, handshakeSource.Token)
				.ConfigureAwait(false);
			entry = new StickyWorkerEntry(lease, session, standardError,
				StickyWorkerLifetimeBound.Resolve(lease.SpawnedAtUtc, credentialValidUntilUtc: null),
				reservation, _reservations, _logger);
			Volatile.Write(ref tapEntry.Value, entry);
			ownershipTransferred = _stickyWorkers.TryRegister(key, entry);
			if (!ownershipTransferred) {
				// Unreachable while the start gate holds — and handled anyway, because the alternative is
				// the defect this replaced: a worker that runs its call and is then released, which on a
				// family whose operation OUTLIVES its response kills the operation the caller was just told
				// to poll for. Nothing is invoked on an unowned worker; it ends here and the caller is told
				// the truth, that the family's operation for this target is already running.
				await entry.ReleaseAsync().ConfigureAwait(false);
				return LongOperationInProgressResult(toolName, key.Family, environmentName);
			}
			// The window the gate exists for is over: the worker is reachable, so the next starter of this
			// family will be refused by the registry lookup rather than by the gate. Holding it across the
			// starting call would block that starter for as long as the sticky call budget.
			startGate?.Dispose();
			using CancellationTokenSource callSource =
				CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			callSource.CancelAfter(_stickyCallBudget);
			CallToolResult result = await session.CallToolAsync(childParameters, callSource.Token)
				.ConfigureAwait(false);
			if (result is null) {
				_logger.WriteWarning($"Sticky MCP worker for '{toolName}' returned a null tool result.");
				await ReleaseStartedWorkerAsync(key, entry, ownershipTransferred).ConfigureAwait(false);
				return RelayFailureResult(toolName, "the worker returned a null tool result",
					detail: null, standardError.Tail());
			}
			return result;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
			await ReleaseStartedWorkerAsync(key, entry, ownershipTransferred).ConfigureAwait(false);
			await ReleaseUnregisteredAsync(entry, lease, standardError, reservation).ConfigureAwait(false);
			throw;
		}
		catch (Exception exception) {
			// Including the sticky call budget expiring. The worker is reaped rather than left running:
			// its response deadline is well inside this bound, so a worker that did not answer here is not
			// a slow operation but an unreachable process, and leaving it would hold both an admission
			// slot and the target's configuration-build reservation with nobody able to observe it.
			_logger.WriteWarning(
				$"Sticky MCP worker relay for '{toolName}' failed: "
				+ SensitiveErrorTextRedactor.Redact(exception.Message));
			await ReleaseStartedWorkerAsync(key, entry, ownershipTransferred).ConfigureAwait(false);
			await ReleaseUnregisteredAsync(entry, lease, standardError, reservation).ConfigureAwait(false);
			return RelayFailureResult(toolName, "the worker relay failed", exception.Message,
				standardError.Tail());
		}
		finally {
			if (!ownershipTransferred && entry is not null) {
				// Belt and braces for an entry the registry never took. The branch above already released
				// it before invoking anything — a worker nobody owns must not run a second operation of a
				// family whose work outlives its response — and release is idempotent, so this only ever
				// matters if a future path leaves an unowned entry behind.
				await entry.ReleaseAsync().ConfigureAwait(false);
			}
		}
	}

	/// <summary>
	/// Reaps a worker that WAS registered, so the registry never keeps a key whose worker has just failed.
	/// </summary>
	/// <param name="key">The key.</param>
	/// <param name="entry">The entry, or null when the session never opened.</param>
	/// <param name="ownershipTransferred">Whether the registry took the entry.</param>
	/// <returns>A task that completes when the entry is gone.</returns>
	private async ValueTask ReleaseStartedWorkerAsync(StickyWorkerKey key, StickyWorkerEntry entry,
		bool ownershipTransferred) {
		if (entry is not null && ownershipTransferred) {
			await _stickyWorkers.ReapAsync(key, entry).ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Releases a worker whose session never opened, so nothing else can.
	/// </summary>
	/// <param name="entry">The entry when one was built; null when the failure preceded it.</param>
	/// <param name="lease">The lease.</param>
	/// <param name="standardError">The running drain.</param>
	/// <param name="reservation">The reservation the worker would have held.</param>
	/// <returns>A task that completes when everything is released.</returns>
	/// <remarks>
	/// The entry's own release covers all four resources in the right order, so this only handles the
	/// window BEFORE an entry exists — a transport or handshake failure — where the lease and the drain
	/// are held by locals and the reservation by nobody.
	/// </remarks>
	private async ValueTask ReleaseUnregisteredAsync(StickyWorkerEntry entry, IWorkerLease lease,
		WorkerStandardErrorDrain standardError, SharedResourceReservationToken reservation) {
		if (entry is not null) {
			return;
		}
		await standardError.StopAsync().ConfigureAwait(false);
		_reservations.Release(reservation);
		lease.Dispose();
	}

	/// <summary>
	/// The read loop's serial observation point: consumes the worker's PRIVATE completion signal and
	/// reaps the worker it came from.
	/// </summary>
	/// <param name="notification">A notification taken off the worker's pipe.</param>
	/// <param name="key">The key the sending worker is registered under.</param>
	/// <param name="entry">
	/// The sending worker itself, or <see langword="null"/> when the signal arrived before the parent had
	/// finished building it. The signal only completes THIS entry, never whatever the key holds now.
	/// </param>
	/// <returns><see langword="false"/> for the signal (consume it); <see langword="true"/> otherwise.</returns>
	/// <remarks>
	/// <para>
	/// <b>Consumed, never forwarded.</b> Rule 5 calls this a PRIVATE signal, and it is: forwarding clio's
	/// own process plumbing into the real client's notification stream would be a contract change no
	/// client asked for. Everything else rides through untouched, which is what ADR rule 1 requires.
	/// </para>
	/// <para>
	/// <b>The reap runs OFF this thread, and that is not a style choice.</b> This runs inside the relay's
	/// read loop; reaping disposes the session, and the session's disposal joins that same loop. Reaping
	/// inline would have the read loop waiting for itself.
	/// </para>
	/// </remarks>
	private bool TapCompletionSignal(JsonRpcNotification notification, StickyWorkerKey key,
		StickyWorkerEntry entry) {
		if (!WorkerOperationSignalContract.TryRead(notification, out McpToolOperationFamily family,
				out int? exitCode)) {
			return true;
		}
		_logger.WriteInfo(string.Format(CultureInfo.InvariantCulture,
			"Sticky MCP worker for operation family '{0}' reported completion (exit code {1}); reaping it.",
			family == McpToolOperationFamily.Unspecified ? key.Family : family,
			exitCode?.ToString(CultureInfo.InvariantCulture) ?? "unknown"));
		// Releases the target's configuration-build reservation AT ONCE and shortens the worker's lifetime
		// to the linger window; the sweep at the head of the next sticky dispatch is what actually returns
		// the admission slot. Doing it this way rather than reaping inline is also what keeps this off the
		// read loop, which reaping would otherwise make wait for itself.
		_stickyWorkers.SignalCompleted(key, entry, _stickyCompletionLinger);
		return false;
	}

	/// <summary>
	/// Reads the target a raw tool call names: its registered environment name, or failing that its
	/// explicit url.
	/// </summary>
	/// <param name="parameters">The caller's params, exactly as the client sent them.</param>
	/// <param name="dispatchedToolName">
	/// The tool the ROUTE resolved to. For a call that arrived through <c>clio-run</c> this is the INNER
	/// tool, while <see cref="CallToolRequestParams.Name"/> is still the executor — which is one of the
	/// two signals <see cref="TryReadEffectiveArguments"/> unwraps on.
	/// </param>
	/// <returns>
	/// Options naming the target. Both fields are null when the call names neither, which the key
	/// derivations answer with a stable unresolved key rather than by throwing.
	/// </returns>
	/// <remarks>
	/// The name is preferred over the url because <see cref="Tools.IToolCommandResolver.GetTargetKey"/>
	/// folds a registered name and an explicit url onto ONE key anyway (AC-00), so reading whichever is
	/// present costs nothing and reading neither would put every credentials-started restart in one
	/// bucket.
	/// </remarks>
	internal static EnvironmentOptions ReadTargetOptions(
		CallToolRequestParams parameters, string dispatchedToolName) {
		IDictionary<string, JsonElement> arguments = ReadEffectiveArguments(parameters, dispatchedToolName);
		string environmentName = TryReadStringArgument(arguments, EnvironmentNameArgument);
		return environmentName is null
			? new EnvironmentOptions { Uri = TryReadStringArgument(arguments, UrlArgument) }
			: new EnvironmentOptions { Environment = environmentName };
	}

	/// <summary>
	/// Returns the arguments the TARGET tool was called with, unwrapping the <c>clio-run</c> envelope
	/// when the call arrived through the executor.
	/// </summary>
	/// <param name="parameters">The caller's params.</param>
	/// <param name="dispatchedToolName">The tool the route resolved to.</param>
	/// <returns>The target's own arguments, or the caller's arguments when there was nothing to unwrap.</returns>
	/// <remarks>
	/// <b>This is not an optimisation; without it the sticky key is derived from the WRAPPER.</b> Every
	/// long-running tool is NON-RESIDENT, so the live caller reaches it through <c>clio-run</c> — and the
	/// executor's params are relayed to the worker verbatim (see <see cref="IMcpWorkerCallDispatcher"/>),
	/// which is right for the relay and wrong for a key read off them. In the wrapped shape the target
	/// sits two object levels below <c>Arguments</c>, so an un-unwrapped read finds nothing: the compile
	/// and its own <c>compile-status</c> poll file under one UNRESOLVED key, and — worse, now that a
	/// colliding starter is refused — two different environments collide on that same key, so one
	/// environment's compile refuses another's.
	/// </remarks>
	private static IDictionary<string, JsonElement> ReadEffectiveArguments(
		CallToolRequestParams parameters, string dispatchedToolName) {
		IDictionary<string, JsonElement> arguments = parameters?.Arguments;
		if (arguments is null) {
			return null;
		}
		return TryReadEffectiveArguments(parameters, arguments, dispatchedToolName,
			out IDictionary<string, JsonElement> inner)
			? inner
			: arguments;
	}

	/// <summary>
	/// Recovers the inner call's arguments from an executor envelope.
	/// </summary>
	/// <param name="parameters">The caller's params, for the name the client dialled.</param>
	/// <param name="arguments">Those params' arguments.</param>
	/// <param name="dispatchedToolName">The tool the route resolved to.</param>
	/// <param name="inner">The target tool's own arguments when this was an executor call.</param>
	/// <returns><see langword="true"/> when an envelope was unwrapped.</returns>
	/// <remarks>
	/// <para>
	/// <b>Two signals, because neither alone is safe.</b> The name is the primary one — the executor's
	/// own <c>clio-run</c> / <c>clio-run-destructive</c>, matched the way the invoker registry matches
	/// (trimmed, case-insensitive). The second is that the route resolved to a DIFFERENT tool than the
	/// client named, which is true of an executor call whatever the wrapper is called, and false of every
	/// direct call. Both are then confirmed by the payload actually carrying a <c>command</c> string, so
	/// an ordinary tool that happens to take an argument called <c>args</c> is never unwrapped.
	/// </para>
	/// <para>
	/// <b>The recovery mirrors <c>ClioRunExecutor.RecoverWrappedCall</c> exactly</b> — inner <c>args</c>
	/// wins when present, otherwise the wrapper minus its <c>command</c> key is the flat target args —
	/// because a second convention for the same envelope is a second thing to keep in step, and the one
	/// that drifts is the one nothing dispatches through.
	/// </para>
	/// </remarks>
	internal static bool TryReadEffectiveArguments(
		CallToolRequestParams parameters,
		IDictionary<string, JsonElement> arguments,
		string dispatchedToolName,
		out IDictionary<string, JsonElement> inner) {
		inner = null;
		if (arguments is null || !LooksLikeExecutorCall(parameters, dispatchedToolName)) {
			return false;
		}
		// clio-run's OWN shape: its two declared parameters bind as {"command":"<tool>","args":{…}}.
		if (TryReadString(arguments, ExecutorCommandArgument) is not null) {
			inner = TryReadObject(arguments, ExecutorArgumentsArgument, out JsonElement targetArguments)
				? ToArguments(targetArguments)
				: WithoutCommandKey(arguments);
			return true;
		}
		// The WRAPPED shape an agent habituated to the single-args-record convention sends:
		// {"args":{"command":"<tool>", …}} — the target is one level deeper again.
		if (!TryReadObject(arguments, ExecutorArgumentsArgument, out JsonElement wrapper)
			|| !TryReadStringProperty(wrapper, ExecutorCommandArgument, out string _)) {
			return false;
		}
		inner = TryReadObjectProperty(wrapper, ExecutorArgumentsArgument, out JsonElement wrappedTarget)
			? ToArguments(wrappedTarget)
			: WithoutCommandKey(ToArguments(wrapper));
		return true;
	}

	// The name the client dialled is an executor's, or the route resolved somewhere else entirely — both
	// mean the params on hand belong to a wrapper rather than to the tool being dispatched. The name check
	// matches McpToolInvokerRegistry's own resolution (trimmed, case-insensitive) so a differently-cased
	// alias cannot slip past it.
	private static bool LooksLikeExecutorCall(CallToolRequestParams parameters, string dispatchedToolName) {
		string callName = parameters?.Name?.Trim();
		if (string.Equals(callName, Tools.ClioRunTool.ToolName, StringComparison.OrdinalIgnoreCase)
			|| string.Equals(callName, Tools.ClioRunDestructiveTool.ToolName, StringComparison.OrdinalIgnoreCase)) {
			return true;
		}
		return callName is not null && dispatchedToolName is not null
			&& !string.Equals(callName, dispatchedToolName, StringComparison.OrdinalIgnoreCase);
	}

	private static IDictionary<string, JsonElement> ToArguments(JsonElement element) {
		Dictionary<string, JsonElement> arguments = new(StringComparer.Ordinal);
		foreach (JsonProperty property in element.EnumerateObject()) {
			arguments[property.Name] = property.Value;
		}
		return arguments;
	}

	private static IDictionary<string, JsonElement> WithoutCommandKey(
		IDictionary<string, JsonElement> arguments) {
		Dictionary<string, JsonElement> stripped = new(StringComparer.Ordinal);
		foreach (KeyValuePair<string, JsonElement> argument in arguments) {
			if (!string.Equals(argument.Key, ExecutorCommandArgument, StringComparison.OrdinalIgnoreCase)) {
				stripped[argument.Key] = argument.Value;
			}
		}
		return stripped;
	}

	private static string TryReadString(IDictionary<string, JsonElement> arguments, string name) {
		foreach (KeyValuePair<string, JsonElement> argument in arguments) {
			if (string.Equals(argument.Key, name, StringComparison.OrdinalIgnoreCase)
				&& argument.Value.ValueKind == JsonValueKind.String) {
				return argument.Value.GetString();
			}
		}
		return null;
	}

	private static bool TryReadObject(
		IDictionary<string, JsonElement> arguments, string name, out JsonElement value) {
		foreach (KeyValuePair<string, JsonElement> argument in arguments) {
			if (string.Equals(argument.Key, name, StringComparison.OrdinalIgnoreCase)
				&& argument.Value.ValueKind == JsonValueKind.Object) {
				value = argument.Value;
				return true;
			}
		}
		value = default;
		return false;
	}

	private static bool TryReadStringProperty(JsonElement element, string name, out string value) {
		foreach (JsonProperty property in element.EnumerateObject()) {
			if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
				&& property.Value.ValueKind == JsonValueKind.String) {
				value = property.Value.GetString();
				return true;
			}
		}
		value = null;
		return false;
	}

	private static bool TryReadObjectProperty(JsonElement element, string name, out JsonElement value) {
		foreach (JsonProperty property in element.EnumerateObject()) {
			if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
				&& property.Value.ValueKind == JsonValueKind.Object) {
				value = property.Value;
				return true;
			}
		}
		value = default;
		return false;
	}

	/// <summary>
	/// Reads one string argument out of a tool call's arguments.
	/// </summary>
	/// <param name="arguments">
	/// The TARGET tool's arguments — for an executor call, the ones
	/// <see cref="TryReadEffectiveArguments"/> recovered, never the wrapper's.
	/// </param>
	/// <param name="argumentName">The argument to read.</param>
	/// <returns>The value, or <see langword="null"/> when the call carries none.</returns>
	/// <remarks>
	/// Two shapes, because the SDK binds arguments by PARAMETER name: a tool whose single parameter is a
	/// complex args record receives one argument (<c>args</c>) holding the whole object, while a
	/// scalar-parameter tool receives each key at the top level. Both are searched, top level first, and
	/// the search goes exactly ONE level deep — deeper would start matching an unrelated nested
	/// <c>environment-name</c> inside some other payload.
	/// </remarks>
	internal static string TryReadStringArgument(
		IDictionary<string, JsonElement> arguments, string argumentName) {
		if (arguments is null) {
			return null;
		}
		foreach (KeyValuePair<string, JsonElement> argument in arguments) {
			if (string.Equals(argument.Key, argumentName, StringComparison.OrdinalIgnoreCase)
				&& argument.Value.ValueKind == JsonValueKind.String) {
				return argument.Value.GetString();
			}
		}
		foreach (KeyValuePair<string, JsonElement> argument in arguments) {
			if (argument.Value.ValueKind == JsonValueKind.Object
				&& argument.Value.TryGetProperty(argumentName, out JsonElement nested)
				&& nested.ValueKind == JsonValueKind.String) {
				return nested.GetString();
			}
		}
		return null;
	}

	/// <summary>
	/// Resolves the tenant key without letting a resolution failure fail the dispatch.
	/// </summary>
	/// <param name="options">The environment options built from the call.</param>
	/// <returns>The tenant key.</returns>
	/// <remarks>
	/// <see cref="Tools.IToolCommandResolver.GetTenantKey"/> already contracts never to throw for a bad
	/// environment, and this is belt and braces for the unexpected: a key is only a dictionary key here,
	/// and failing the CALL because the key could not be computed would turn a resolver defect into an
	/// outage. The worker itself resolves the environment again and reports the real failure.
	/// </remarks>
	private string SafeTenantKey(EnvironmentOptions options) {
		try {
			return _commandResolver.GetTenantKey(options);
		}
		catch (Exception exception) {
			_logger.WriteWarning(
				"The tenant key for a sticky MCP worker could not be resolved: "
				+ SensitiveErrorTextRedactor.Redact(exception.Message));
			return $"unresolved-tenant:{options.Environment ?? options.Uri}";
		}
	}

	/// <summary>
	/// Resolves the normalised target key without letting a resolution failure fail the dispatch.
	/// </summary>
	/// <param name="options">The environment options built from the call.</param>
	/// <returns>The target key.</returns>
	private string SafeTargetKey(EnvironmentOptions options) {
		try {
			return _commandResolver.GetTargetKey(options);
		}
		catch (Exception exception) {
			_logger.WriteWarning(
				"The target key for a configuration-build reservation could not be resolved: "
				+ SensitiveErrorTextRedactor.Redact(exception.Message));
			// Derived from the requested identity rather than a constant: a constant would put every
			// unresolvable target in one bucket and let one of them deny all the others.
			return $"unresolved-target:{options.Environment ?? options.Uri}";
		}
	}

	/// <summary>
	/// Builds the envelope returned when the target's configuration build is already reserved.
	/// </summary>
	/// <param name="toolName">The refused tool.</param>
	/// <param name="environmentName">The environment the call named.</param>
	/// <returns>The error result.</returns>
	internal static CallToolResult SharedResourceBusyResult(string toolName, string environmentName) {
		string text = SharedResourceReservation.BuildAlreadyReservedMessage(toolName, environmentName);
		JsonObject payload = new() {
			["success"] = false,
			["tool"] = toolName,
			["error-class"] = SharedResourceBusyErrorClass,
			["configuration-build-in-progress"] = true,
			["message"] = text
		};
		return new CallToolResult {
			IsError = true,
			Content = [new TextContentBlock { Text = text }],
			StructuredContent = JsonSerializer.SerializeToElement(payload)
		};
	}

	/// <summary>
	/// Builds the envelope returned when this family's operation is already running for the target.
	/// </summary>
	/// <param name="toolName">The refused tool.</param>
	/// <param name="family">The operation family that is already running.</param>
	/// <param name="environmentName">The environment the call named, when it named one.</param>
	/// <returns>The error result.</returns>
	internal static CallToolResult LongOperationInProgressResult(string toolName,
		McpToolOperationFamily family, string environmentName) {
		string target = string.IsNullOrWhiteSpace(environmentName)
			? "the requested environment"
			: $"'{environmentName}'";
		string text = string.Format(CultureInfo.InvariantCulture,
			"'{0}' was not started: an operation of the '{1}' family is already running for {2} in this "
			+ "clio MCP host. Poll that operation's status tool for its result, or wait for it to finish "
			+ "before starting another.", toolName, family, target);
		JsonObject payload = new() {
			["success"] = false,
			["tool"] = toolName,
			["error-class"] = LongOperationInProgressErrorClass,
			["long-operation-in-progress"] = true,
			["operation-family"] = family.ToString(),
			["message"] = text
		};
		return new CallToolResult {
			IsError = true,
			Content = [new TextContentBlock { Text = text }],
			StructuredContent = JsonSerializer.SerializeToElement(payload)
		};
	}

	/// <summary>
	/// Holds the per-key start gate for one starter and frees it exactly once.
	/// </summary>
	/// <remarks>
	/// Per-call runtime state rather than a service — like <c>WorkerStandardErrorDrain</c> — so it is a
	/// private nested class with no interface and is never resolved from a container. Release is
	/// idempotent because the starter drops the gate as soon as the registry owns the worker while its
	/// caller still holds a <c>using</c> on it for the failure paths.
	/// </remarks>
	private sealed class StickyStartGateLease : IDisposable {

		private readonly McpWorkerCallDispatcher _dispatcher;
		private readonly StickyWorkerKey _key;
		private int _released;

		internal StickyStartGateLease(McpWorkerCallDispatcher dispatcher, StickyWorkerKey key) {
			_dispatcher = dispatcher;
			_key = key;
		}

		public void Dispose() {
			if (Interlocked.Exchange(ref _released, 1) == 0) {
				_dispatcher.ExitStartGate(_key);
			}
		}
	}

	/// <summary>
	/// Builds the envelope returned when the host already runs as many long operations as it supports.
	/// </summary>
	/// <param name="toolName">The refused tool.</param>
	/// <param name="exception">The refusal, whose message already names the limit and the knob.</param>
	/// <returns>The error result.</returns>
	internal static CallToolResult StickyCapacityResult(string toolName,
		WorkerStickyCapacityExceededException exception) {
		string text = $"'{toolName}' was not started. {exception.Message}";
		JsonObject payload = new() {
			["success"] = false,
			["tool"] = toolName,
			["error-class"] = StickyCapacityErrorClass,
			["long-operation-capacity"] = exception.StickyConcurrencyCap,
			["worker-concurrency"] = exception.TotalConcurrencyCap,
			["message"] = text
		};
		return new CallToolResult {
			IsError = true,
			Content = [new TextContentBlock { Text = text }],
			StructuredContent = JsonSerializer.SerializeToElement(payload)
		};
	}
}
