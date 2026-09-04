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
