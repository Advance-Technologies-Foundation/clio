using System.Diagnostics;
using System.Text.Json;

namespace Clio.Mcp.E2E.Support.Mcp;

/// <summary>
/// One worker child, as the MCP host recorded it on disk at spawn time.
/// </summary>
/// <param name="ProcessId">The worker's operating-system process identifier.</param>
/// <param name="StartTimeUtcTicks">
/// The worker's start time in ticks. Half of the identity: process identifiers are reused, so
/// "is pid 4711 still alive" is not the same question as "is the worker that was pid 4711 still alive".
/// </param>
internal sealed record ObservedWorker(int ProcessId, long StartTimeUtcTicks) {

	/// <summary>
	/// Determines whether this exact worker is still running — identity checked, not just the identifier.
	/// </summary>
	/// <returns><see langword="true"/> only when a live process carries BOTH the recorded id and start time.</returns>
	internal bool IsStillRunning() {
		try {
			using Process process = Process.GetProcessById(ProcessId);
			if (process.HasExited) {
				return false;
			}
			// A reused identifier belongs to a stranger, and a stranger is not "A's child still running".
			// Comparing to the second absorbs the tick-precision difference between what the supervisor
			// recorded and what this process reads back.
			long recordedSeconds = StartTimeUtcTicks / TimeSpan.TicksPerSecond;
			long observedSeconds = process.StartTime.ToUniversalTime().Ticks / TimeSpan.TicksPerSecond;
			return recordedSeconds == observedSeconds;
		} catch (ArgumentException) {
			// No process carries that identifier any more.
			return false;
		} catch (InvalidOperationException) {
			// The process exited between the lookup and the read.
			return false;
		}
	}
}

/// <summary>
/// What <see cref="WorkerSpawnObserver.WaitUntilWorkersAreReleased"/> saw when it stopped waiting.
/// </summary>
/// <param name="RegistryRead">
/// Whether the final registry read actually observed the file. FALSE means the instrument failed, which
/// is a different statement from "no worker is recorded" and must never be asserted as that one.
/// </param>
/// <param name="StillRecorded">Entries the final read found in the registry.</param>
/// <param name="StillRunning">Observed identities whose process was still alive at the final check.</param>
/// <param name="Waited">How long the wait actually took, for the assertion diagnostics.</param>
internal sealed record WorkerReleaseObservation(
	bool RegistryRead,
	IReadOnlyList<ObservedWorker> StillRecorded,
	IReadOnlyList<ObservedWorker> StillRunning,
	TimeSpan Waited);

/// <summary>
/// Watches the MCP host's on-disk worker registry so a test can OBSERVE that child processes were
/// spawned, rather than infer it from a call having succeeded.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this and not a process-table scan.</b> "D succeeded quickly" is consistent with a worker having
/// run AND with the host having quietly executed the call itself, so the acceptance criterion explicitly
/// requires observing the child. The registry is the host's own record of every worker it starts
/// (<c>{CLIO_HOME}/mcp-workers/workers.json</c>, written before the child runs a single instruction and
/// removed when its lease is disposed), so watching it needs no per-operating-system command-line reader
/// and works identically on macOS, Linux and Windows.
/// </para>
/// <para>
/// <b>Polling, because the record is transient by design.</b> An entry exists only while its worker does,
/// which is exactly what makes the file usable for the cleanup half of the assertion — but it also means
/// a snapshot taken after the run would legitimately be empty. The observer therefore samples
/// continuously and accumulates every distinct identity it ever saw.
/// </para>
/// </remarks>
internal sealed class WorkerSpawnObserver : IAsyncDisposable {

	private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

	private readonly string _registryPath;
	private readonly CancellationTokenSource _cancellation = new();
	private readonly HashSet<ObservedWorker> _observed = [];
	private readonly object _sync = new();
	private readonly List<string> _readFailures = [];
	private Task? _pollLoop;

	private WorkerSpawnObserver(string registryPath) => _registryPath = registryPath;

	/// <summary>Starts watching the worker registry under a clio home directory.</summary>
	/// <param name="clioHome">The <c>CLIO_HOME</c> the MCP host under test was given.</param>
	/// <returns>The running observer.</returns>
	internal static WorkerSpawnObserver Start(string clioHome) {
		WorkerSpawnObserver observer = new(Path.Combine(clioHome, "mcp-workers", "workers.json"));
		observer._pollLoop = Task.Run(observer.PollAsync);
		return observer;
	}

	/// <summary>Gets every distinct worker identity seen in the registry since the observer started.</summary>
	internal IReadOnlyList<ObservedWorker> Observed {
		get {
			lock (_sync) {
				return [.. _observed];
			}
		}
	}

	/// <summary>
	/// Gets the registry entries present RIGHT NOW. An empty list after a run means every worker's lease
	/// was disposed, which is the cleanup half of TC-E-601b.
	/// </summary>
	internal IReadOnlyList<ObservedWorker> ReadCurrent() => ReadRegistry(out _);

	/// <summary>
	/// Polls until every worker seen during the run is released — absent from the on-disk registry AND
	/// no longer running — or until <paramref name="timeout"/> elapses. Returns what was still there.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Both halves are waited for, because neither is implied by the call answering and neither
	/// implies the other.</b> A tool whose declared lifetime is <c>PerCall</c> — which is every caller of
	/// this method today — is served by <c>McpWorkerCallDispatcher.DispatchPerCallAsync</c>, whose
	/// <c>finally</c> disposes the lease inline, before the result reaches the client. So the removal is
	/// ATTEMPTED before the answer. The sticky path (<c>StickyWorkerRegistry</c> and its unawaited
	/// <c>ReleaseInBackground</c>) serves only <c>Sticky</c>-lifetime tools and is NOT involved; do not
	/// reason about a per-call worker from it.
	/// </para>
	/// <para>
	/// <b>Attempted is not achieved, and that is the whole reason for the wait.</b>
	/// <c>WorkerProcessSupervisor.UnregisterWorker</c> routes the removal through the
	/// <c>workers.lock</c> interprocess gate and SWALLOWS <c>TimeoutException</c>, <c>IOException</c> and
	/// <c>UnauthorizedAccessException</c> with a warning, leaving the entry in <c>workers.json</c> while
	/// the call answers normally. A contended gate can also hold the attempt for the gate's own timeout
	/// before it succeeds. On a loaded agent — several workers, two of them overlapping, sharing one lock
	/// file — that is how a worker stays RECORDED after its call returned.
	/// </para>
	/// <para>
	/// <b>The registry is NOT the stronger condition.</b> <c>SupervisedWorkerLease.Dispose</c> calls
	/// <c>ReleaseLease</c> — and so <c>UnregisterWorker</c> — as soon as <c>Terminate()</c> returns
	/// anything but <c>Failed</c>, and <c>Terminate()</c> is <c>TerminateJobObject</c> on Windows and
	/// <c>kill(2)</c> on Unix: a request, not a wait. Only the failed-kill path
	/// (<c>ReleaseWhenExitConfirmed</c>) waits for exit first, and that path deliberately RETAINS the
	/// entry. So a drained registry means the kill was ISSUED, never that the process is gone — which is
	/// why process liveness is polled here rather than inferred from the registry.
	/// </para>
	/// <para>
	/// The timeout is what still tells "slow to release" apart from "leaked": nothing retries a swallowed
	/// removal, so an entry left behind by a failed unregister never drains, the wait expires and the
	/// assertion fails — which is the case TC-E-601b exists to catch.
	/// </para>
	/// </remarks>
	/// <param name="timeout">How long to keep waiting before reporting what survived.</param>
	/// <returns>The final observation, including whether the registry was actually read.</returns>
	internal WorkerReleaseObservation WaitUntilWorkersAreReleased(TimeSpan timeout) {
		Stopwatch elapsed = Stopwatch.StartNew();
		while (true) {
			// ReadRegistry, not ReadCurrent: the `read` flag is the difference between "the registry holds
			// no worker" and "the registry could not be read", and only the first may end this wait. Losing
			// that distinction would let a single unlucky read during the host's own atomic replace of
			// workers.json certify a leak as a clean release — the loop would poll until it FOUND an empty
			// answer rather than until the registry was empty.
			IReadOnlyList<ObservedWorker> recorded = ReadRegistry(out bool read);
			IReadOnlyList<ObservedWorker> running = [.. Observed.Where(worker => worker.IsStillRunning())];
			if ((read && recorded.Count == 0 && running.Count == 0) || elapsed.Elapsed >= timeout) {
				return new WorkerReleaseObservation(read, recorded, running, elapsed.Elapsed);
			}
			Thread.Sleep(PollInterval);
		}
	}

	/// <summary>
	/// Gets registry reads that failed for a reason other than the file being absent or half-written. A
	/// broken reader would make every observation empty — the same shape as "no worker ever ran" — so the
	/// instrument reports its own failures rather than letting them read as a result.
	/// </summary>
	internal IReadOnlyList<string> ReadFailures {
		get {
			lock (_sync) {
				return [.. _readFailures];
			}
		}
	}

	/// <summary>A single-line summary of what was observed, for assertion diagnostics.</summary>
	internal string Describe() {
		IReadOnlyList<ObservedWorker> observed = Observed;
		IReadOnlyList<ObservedWorker> current = ReadCurrent();
		string failures = ReadFailures.Count == 0 ? string.Empty : $", read-failures=[{string.Join(" | ", ReadFailures)}]";
		return $"registry={_registryPath}, workers-seen={observed.Count} "
			+ $"[{string.Join(", ", observed.Select(worker => worker.ProcessId))}], "
			+ $"still-recorded={current.Count} [{string.Join(", ", current.Select(worker => worker.ProcessId))}]"
			+ failures;
	}

	/// <inheritdoc/>
	public async ValueTask DisposeAsync() {
		await _cancellation.CancelAsync();
		if (_pollLoop is not null) {
			try {
				await _pollLoop;
			} catch (OperationCanceledException) {
				// Expected teardown.
			}
		}
		_cancellation.Dispose();
	}

	private async Task PollAsync() {
		while (!_cancellation.IsCancellationRequested) {
			foreach (ObservedWorker worker in ReadRegistry(out _)) {
				lock (_sync) {
					_observed.Add(worker);
				}
			}
			try {
				await Task.Delay(PollInterval, _cancellation.Token);
			} catch (OperationCanceledException) {
				return;
			}
		}
	}

	private IReadOnlyList<ObservedWorker> ReadRegistry(out bool read) {
		read = false;
		try {
			if (!File.Exists(_registryPath)) {
				// An ABSENT registry is an authoritative observation of "no worker recorded", not a failed
				// read: the host creates the file on the first registration and WriteUnguarded persists an
				// empty array rather than deleting it, so absence here means nothing ever registered — a
				// state AssertWorkersWereSpawned has already ruled out by the time anything waits on this.
				read = true;
				return [];
			}
			// Shared read: the host rewrites this file under its own interprocess gate, and a test must
			// never be the reason a spawn fails.
			using FileStream stream = new(_registryPath, FileMode.Open, FileAccess.Read,
				FileShare.ReadWrite | FileShare.Delete);
			using JsonDocument document = JsonDocument.Parse(stream);
			read = true;
			if (document.RootElement.ValueKind != JsonValueKind.Array) {
				return [];
			}
			List<ObservedWorker> workers = [];
			foreach (JsonElement entry in document.RootElement.EnumerateArray()) {
				if (entry.TryGetProperty("ProcessId", out JsonElement processId)
					&& entry.TryGetProperty("StartTimeUtcTicks", out JsonElement startTicks)) {
					workers.Add(new ObservedWorker(processId.GetInt32(), startTicks.GetInt64()));
				}
			}
			return workers;
		} catch (FileNotFoundException) {
			return [];
		} catch (DirectoryNotFoundException) {
			return [];
		} catch (IOException) {
			// A concurrent rewrite; the next poll sees the finished file.
			return [];
		} catch (JsonException) {
			// Same: a half-written file is a timing artefact of polling, not a defect.
			return [];
		} catch (Exception exception) {
			lock (_sync) {
				_readFailures.Add($"{exception.GetType().Name}: {exception.Message}");
			}
			return [];
		}
	}
}
