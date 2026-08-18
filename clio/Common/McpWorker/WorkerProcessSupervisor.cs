using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Common.McpWorker;

/// <inheritdoc />
public sealed class WorkerProcessSupervisor : IWorkerProcessSupervisor, IWorkerProcessInspector {

	/// <summary>
	/// Ambient variables copied into a worker's cleared environment. Everything else is dropped, so a
	/// stray variable in the parent's environment cannot contradict the frozen payload the worker is
	/// launched with (ADR rule 11).
	/// </summary>
	/// <remarks>
	/// <para>
	/// The <c>DOTNET_ROOT*</c> family is not decoration. It is how a .NET apphost — which is what a
	/// published clio and the test fixture both are — finds the shared runtime when that runtime is not
	/// at the machine's default location, and the variable that carries it is ARCHITECTURE-SPECIFIC:
	/// measured on an arm64 macOS host, only <c>DOTNET_ROOT_ARM64</c> was set, and dropping it made a
	/// spawned worker fail at startup with "You must install or update .NET" before executing a line of
	/// clio. A frozen environment that omits these turns a working host into one where every worker dies
	/// instantly, so the allowlist carries every spelling the host may have used.
	/// </para>
	/// <para>
	/// The proxy family is here for the same reason and under the same "every spelling" rule. Where
	/// egress goes through a mandated inspecting proxy, the parent honours <c>HTTPS_PROXY</c> and a
	/// child that does not inherit it either cannot reach Creatio at all or reaches it around the
	/// policy — and both present to the user as "the environment is broken". The lowercase spellings
	/// are NOT duplicates of the uppercase ones: on Unix they are the conventional spelling, plenty of
	/// stacks read them case-sensitively, and a host may have set only those.
	/// </para>
	/// </remarks>
	public static readonly IReadOnlyCollection<string> DefaultInheritedEnvironmentVariableAllowlist = [
		"PATH",
		"HOME",
		"USERPROFILE",
		"LOCALAPPDATA",
		"APPDATA",
		"SystemRoot",
		"SystemDrive",
		"windir",
		"COMSPEC",
		"TEMP",
		"TMP",
		"TMPDIR",
		"DOTNET_ROOT",
		"DOTNET_ROOT_ARM64",
		"DOTNET_ROOT_X64",
		"DOTNET_ROOT_X86",
		"DOTNET_ROOT(x86)",
		"DOTNET_HOST_PATH",
		"CLIO_HOME",
		"LANG",
		"LC_ALL",
		"HTTP_PROXY",
		"HTTPS_PROXY",
		"NO_PROXY",
		"http_proxy",
		"https_proxy",
		"no_proxy"
	];

	/// <summary>
	/// Environment variable overriding <see cref="DefaultQueueWaitBound"/>, in seconds (invariant
	/// culture, accepted range 0 &lt; n ≤ 3600).
	/// </summary>
	/// <remarks>
	/// Separate from <c>CLIO_MCP_WORKER_BUDGET_SECONDS</c>, which bounds a worker that is RUNNING. The
	/// two answer different questions and a caller has to be able to tell which one it hit, so they are
	/// configured separately as well as reported separately.
	/// </remarks>
	internal const string QueueWaitOverrideEnvVar = "CLIO_MCP_WORKER_QUEUE_WAIT_SECONDS";

	/// <summary>
	/// How long a call may wait for a concurrency slot before it is refused with
	/// <see cref="WorkerQueueWaitExpiredException"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>60 s, from the measurements in ADR §2.4 rather than from taste.</b> The bound has to clear the
	/// worst HEALTHY queue wait ever measured: at concurrency width 16 on the four-core Windows stand a
	/// perfectly healthy call waited <b>16.9 s</b> just to reach <c>initialize</c> — four times
	/// oversubscribed, with a responsive backend. A bound anywhere near that would refuse calls for
	/// being busy, which is the failure mode the spawn-anchored budget already exists to avoid. 60 s is
	/// roughly 3.5× it, so an ordinarily busy host queues and succeeds.
	/// </para>
	/// <para>
	/// The upper end is set by the client, not by us: 60 s of queueing plus the 120 s default response
	/// budget is 180 s, which is about the hard ceiling an MCP client gives a single call before it
	/// abandons it. Anything larger and clio's own answer arrives after the client has stopped
	/// listening — the caller learns nothing, which is the condition this bound exists to end.
	/// </para>
	/// <para>
	/// <b>Read this together with <see cref="ConcurrencyCap"/>.</b> The cap is a shared, HELD resource:
	/// a slot is taken at spawn and returned at lease dispose, so any worker that lives longer than the
	/// answer it produced occupies capacity for its whole life. With a four-slot cap, four such holders
	/// are enough to send every other call into this queue — bounded, named and reported here rather
	/// than silently waiting, but still queued.
	/// </para>
	/// </remarks>
	public static readonly TimeSpan DefaultQueueWaitBound = TimeSpan.FromSeconds(60);

	private static readonly TimeSpan TerminationConfirmationTimeout = TimeSpan.FromSeconds(5);

	private readonly ILogger _logger;
	private readonly IProcessExecutor _processExecutor;
	private readonly IProcessContainment _containment;
	private readonly IClioExecutablePathProvider _executablePathProvider;
	private readonly IStaleWorkerRegistry _registry;
	private readonly WorkerSlotPool _perCallPool;
	private readonly ProcessIdentitySnapshot _ownerIdentity;

	private int _activeWorkers;
	private int _peakActiveWorkers;
	private long _totalSpawned;
	private long _totalTerminated;
	private long _totalStaleReaped;

	/// <summary>
	/// Initializes a new instance of the <see cref="WorkerProcessSupervisor"/> class with a concurrency
	/// cap derived from the machine's processor count.
	/// </summary>
	/// <param name="logger">Logger for containment and cleanup diagnostics.</param>
	/// <param name="processExecutor">
	/// The ordinary process executor, which serves the four inherited <see cref="IProcessExecutor"/>
	/// members. Worker spawning never goes through it — see the interface remarks for why it cannot.
	/// </param>
	/// <param name="containment">Platform containment for spawned workers.</param>
	/// <param name="executablePathProvider">Resolves how to re-launch this clio build.</param>
	/// <param name="registry">On-disk record of live workers, used for stale cleanup.</param>
	public WorkerProcessSupervisor(ILogger logger, IProcessExecutor processExecutor,
		IProcessContainment containment, IClioExecutablePathProvider executablePathProvider,
		IStaleWorkerRegistry registry)
		: this(logger, processExecutor, containment, executablePathProvider, registry, null,
			ResolveQueueWaitBound(System.Environment.GetEnvironmentVariable(QueueWaitOverrideEnvVar))) {
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="WorkerProcessSupervisor"/> class with an explicit
	/// concurrency cap. Used by tests, which must observe queueing without spawning one worker per core.
	/// </summary>
	/// <param name="logger">Logger for containment and cleanup diagnostics.</param>
	/// <param name="processExecutor">Executor serving the inherited members.</param>
	/// <param name="containment">Platform containment for spawned workers.</param>
	/// <param name="executablePathProvider">Resolves how to re-launch this clio build.</param>
	/// <param name="registry">On-disk record of live workers.</param>
	/// <param name="concurrencyCap">Explicit cap; processor count when null.</param>
	/// <param name="queueWaitBound">
	/// Explicit queue-wait bound; <see cref="DefaultQueueWaitBound"/> when null. Stated rather than read
	/// from the environment so a test can bound a queued call without mutating process-wide state.
	/// </param>
	internal WorkerProcessSupervisor(ILogger logger, IProcessExecutor processExecutor,
		IProcessContainment containment, IClioExecutablePathProvider executablePathProvider,
		IStaleWorkerRegistry registry, int? concurrencyCap, TimeSpan? queueWaitBound = null) {
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_processExecutor = processExecutor ?? throw new ArgumentNullException(nameof(processExecutor));
		_containment = containment ?? throw new ArgumentNullException(nameof(containment));
		_executablePathProvider = executablePathProvider
			?? throw new ArgumentNullException(nameof(executablePathProvider));
		_registry = registry ?? throw new ArgumentNullException(nameof(registry));
		// The cap is core-count derived because wall time grows linearly past the core count: a wider
		// cap buys no throughput and only inflates per-call latency (ADR section 2.4). Memory is not the
		// binding constraint — CPU is.
		ConcurrencyCap = Math.Max(1, concurrencyCap ?? System.Environment.ProcessorCount);
		QueueWaitBound = queueWaitBound ?? DefaultQueueWaitBound;
		_perCallPool = new WorkerSlotPool(ConcurrencyCap);
		_ownerIdentity = CaptureCurrentProcessIdentity();
	}

	/// <inheritdoc />
	public int ConcurrencyCap { get; }

	/// <summary>
	/// Gets how long a call may wait for a slot before it is refused with
	/// <see cref="WorkerQueueWaitExpiredException"/>. See <see cref="DefaultQueueWaitBound"/> for the
	/// measurements behind the default and for why the wait is bounded at all.
	/// </summary>
	public TimeSpan QueueWaitBound { get; }

	/// <summary>
	/// Parses a raw seconds override into a queue-wait bound, falling back to
	/// <see cref="DefaultQueueWaitBound"/> for null / empty / non-numeric / out-of-range values. Pure, so
	/// the parse rules are testable without touching process state.
	/// </summary>
	/// <param name="rawValue">The raw override value.</param>
	/// <returns>The resolved bound.</returns>
	internal static TimeSpan ResolveQueueWaitBound(string rawValue) {
		if (!string.IsNullOrWhiteSpace(rawValue)
			&& double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
			&& seconds > 0 && seconds <= 3600) {
			return TimeSpan.FromSeconds(seconds);
		}
		return DefaultQueueWaitBound;
	}

	#region Methods: worker lifecycle

	/// <inheritdoc />
	public async Task<IWorkerLease> SpawnContainedAsync(WorkerSpawnRequest request,
		CancellationToken cancellationToken) {
		ArgumentNullException.ThrowIfNull(request);
		// Queued, never dropped (AC-01) — but BOUNDED. A call is admitted the moment a slot frees, and
		// only a wait that outlasts the bound is refused, with a named exception carrying the numbers a
		// caller needs. An unbounded wait here would return nothing, issue zero requests to Creatio and
		// do so for an arbitrarily long time, which is the wedge this feature removes wearing a
		// different hat. Which POOL the slot came from is recorded on the lease, so a second pool with
		// its own cap is an addition here rather than a rewrite of the release path.
		WorkerSlotPool pool = _perCallPool;
		await pool.AcquireAsync(QueueWaitBound, cancellationToken).ConfigureAwait(false);

		bool slotHandedOver = false;
		try {
			WorkerLaunchRequest launchRequest = BuildLaunchRequest(request);
			IContainedWorker worker = _containment.OwnsProcessCreation
				? _containment.Launch(launchRequest)
				: _containment.Adopt(StartRedirectedProcess(launchRequest));
			// The budget clock starts HERE — after the slot was granted and the process exists — never at
			// admission. See IWorkerLease.BudgetExpiresAtUtc for the measurement behind that.
			DateTimeOffset spawnedAtUtc = DateTimeOffset.UtcNow;
			RegisterWorker(worker);
			int active = Interlocked.Increment(ref _activeWorkers);
			UpdatePeak(active);
			Interlocked.Increment(ref _totalSpawned);
			slotHandedOver = true;
			return new SupervisedWorkerLease(this, worker, pool, spawnedAtUtc, request.Budget);
		} finally {
			if (!slotHandedOver) {
				pool.Release();
			}
		}
	}

	/// <inheritdoc />
	public async Task<WorkerRunResult> WaitWithinBudgetAsync(IWorkerLease lease,
		CancellationToken cancellationToken) {
		ArgumentNullException.ThrowIfNull(lease);
		TimeSpan remaining = lease.BudgetExpiresAtUtc - DateTimeOffset.UtcNow;
		if (remaining <= TimeSpan.Zero) {
			return await TerminateForBudgetAsync(lease, WorkerRunStatus.BudgetExpired).ConfigureAwait(false);
		}

		using CancellationTokenSource budgetSource = new(remaining);
		using CancellationTokenSource linkedSource =
			CancellationTokenSource.CreateLinkedTokenSource(budgetSource.Token, cancellationToken);
		try {
			await lease.WaitForExitAsync(linkedSource.Token).ConfigureAwait(false);
			return new WorkerRunResult(WorkerRunStatus.Completed, lease.ExitCode,
				DateTimeOffset.UtcNow - lease.SpawnedAtUtc, null);
		} catch (OperationCanceledException) {
			// Kill regardless of which token fired: an abandoned child is exactly the wedge this feature
			// removes. Which of the two fired only decides how the outcome is reported.
			WorkerRunStatus status = cancellationToken.IsCancellationRequested
				? WorkerRunStatus.Canceled
				: WorkerRunStatus.BudgetExpired;
			return await TerminateForBudgetAsync(lease, status).ConfigureAwait(false);
		}
	}

	/// <inheritdoc />
	public WorkerTerminationOutcome KillContained(IWorkerLease lease) {
		ArgumentNullException.ThrowIfNull(lease);
		if (lease is not SupervisedWorkerLease ownLease) {
			throw new ArgumentException("The lease was not issued by this supervisor.", nameof(lease));
		}
		return ownLease.Terminate();
	}

	/// <inheritdoc />
	public StaleWorkerReapReport ReapStaleWorkers() {
		StaleWorkerReapReport report = _registry.Reap(this);
		Interlocked.Add(ref _totalStaleReaped, report.Terminated);
		foreach (string warning in report.Warnings) {
			_logger.WriteWarning(warning);
		}
		if (report.Terminated > 0) {
			_logger.WriteInfo(
				$"Terminated {report.Terminated} MCP worker process(es) left behind by a previous clio process.");
		}
		return report;
	}

	/// <inheritdoc />
	public WorkerSupervisorSnapshot GetSnapshot() {
		return new WorkerSupervisorSnapshot(
			ConcurrencyCap,
			Volatile.Read(ref _activeWorkers),
			_perCallPool.QueuedRequests,
			Volatile.Read(ref _peakActiveWorkers),
			Interlocked.Read(ref _totalSpawned),
			Interlocked.Read(ref _totalTerminated),
			Interlocked.Read(ref _totalStaleReaped));
	}

	#endregion

	#region Methods: IWorkerProcessInspector

	/// <inheritdoc />
	public ProcessIdentitySnapshot TryCaptureIdentity(int processId) {
		if (processId <= 0) {
			return null;
		}
		Process process = null;
		try {
			process = Process.GetProcessById(processId);
			if (process.HasExited) {
				return null;
			}
			return new ProcessIdentitySnapshot(process.Id, process.StartTime.ToUniversalTime().Ticks,
				ReadExecutablePath(process));
		} catch (Exception exception) when (IsProcessInspectionFailure(exception)) {
			return null;
		} finally {
			process?.Dispose();
		}
	}

	/// <inheritdoc />
	public WorkerTerminationOutcome TerminateStaleWorker(WorkerRegistrationEntry entry) {
		ArgumentNullException.ThrowIfNull(entry);
		Process process = null;
		try {
			process = Process.GetProcessById(entry.ProcessId);
			using IWorkerProcessHandle handle = CreateHandle(process, entry.ExecutablePath, null, null, null);
			return _containment.TerminateOrphan(handle);
		} catch (Exception exception) when (IsProcessInspectionFailure(exception)) {
			return WorkerTerminationOutcome.AlreadyExited;
		} finally {
			process?.Dispose();
		}
	}

	#endregion

	#region Methods: inherited IProcessExecutor surface

	/// <inheritdoc />
	public string Execute(string program, string arguments, bool waitForExit, string workingDirectory = null,
		bool showOutput = false, bool suppressErrors = false) =>
		_processExecutor.Execute(program, arguments, waitForExit, workingDirectory, showOutput, suppressErrors);

	/// <inheritdoc />
	public Task<ProcessLaunchResult> FireAndForgetAsync(ProcessExecutionOptions options) =>
		_processExecutor.FireAndForgetAsync(options);

	/// <inheritdoc />
	public Task<ProcessExecutionResult> ExecuteAndCaptureAsync(ProcessExecutionOptions options) =>
		_processExecutor.ExecuteAndCaptureAsync(options);

	/// <inheritdoc />
	public Task<ProcessExecutionResult> ExecuteWithRealtimeOutputAsync(ProcessExecutionOptions options) =>
		_processExecutor.ExecuteWithRealtimeOutputAsync(options);

	#endregion

	#region Methods: Private

	private async Task<WorkerRunResult> TerminateForBudgetAsync(IWorkerLease lease, WorkerRunStatus status) {
		WorkerTerminationOutcome outcome = KillContained(lease);
		await WaitForTerminationConfirmationAsync(lease).ConfigureAwait(false);
		return new WorkerRunResult(status, lease.ExitCode, DateTimeOffset.UtcNow - lease.SpawnedAtUtc,
			outcome);
	}

	// Waited for, rather than assumed: a caller that is told the worker was killed must not have to
	// discover later that it is still holding a file or a socket.
	private async Task WaitForTerminationConfirmationAsync(IWorkerLease lease) {
		try {
			using CancellationTokenSource confirmation = new(TerminationConfirmationTimeout);
			await lease.WaitForExitAsync(confirmation.Token).ConfigureAwait(false);
		} catch (OperationCanceledException) {
			_logger.WriteWarning(
				$"MCP worker {lease.ProcessId} did not exit within {TerminationConfirmationTimeout.TotalSeconds:0} s of being terminated.");
		} catch (Exception exception) when (IsProcessInspectionFailure(exception)) {
			// The process is gone; nothing left to confirm.
		}
	}

	private WorkerLaunchRequest BuildLaunchRequest(WorkerSpawnRequest request) {
		ClioWorkerLaunchDescriptor descriptor = request.LaunchOverride
			?? _executablePathProvider.Resolve([.. request.Arguments]);
		string executable = ProcessExecutor.ResolveExecutablePath(descriptor.Executable);
		return new WorkerLaunchRequest(
			executable,
			descriptor.Arguments,
			request.WorkingDirectory ?? descriptor.WorkingDirectory ?? System.Environment.CurrentDirectory,
			BuildEnvironment(request),
			request.ClearInheritedEnvironment);
	}

	private static IReadOnlyDictionary<string, string> BuildEnvironment(WorkerSpawnRequest request) {
		Dictionary<string, string> environment = new(StringComparer.Ordinal);
		if (request.ClearInheritedEnvironment) {
			IReadOnlyCollection<string> allowlist = request.InheritedEnvironmentVariableAllowlist
				?? DefaultInheritedEnvironmentVariableAllowlist;
			foreach (string name in allowlist) {
				string value = System.Environment.GetEnvironmentVariable(name);
				if (value is not null) {
					environment[name] = value;
				}
			}
		}
		if (request.EnvironmentVariables is not null) {
			foreach (KeyValuePair<string, string> pair in request.EnvironmentVariables) {
				environment[pair.Key] = pair.Value;
			}
		}
		return environment;
	}

	private IWorkerProcessHandle StartRedirectedProcess(WorkerLaunchRequest request) {
		ProcessStartInfo startInfo = new() {
			FileName = request.Executable,
			WorkingDirectory = request.WorkingDirectory,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};
		foreach (string argument in request.Arguments) {
			startInfo.ArgumentList.Add(argument);
		}
		if (request.ClearInheritedEnvironment) {
			startInfo.Environment.Clear();
		}
		foreach (KeyValuePair<string, string> pair in request.Environment) {
			startInfo.Environment[pair.Key] = pair.Value;
		}

		Process process = new() { StartInfo = startInfo };
		try {
			if (!process.Start()) {
				throw new InvalidOperationException(
					$"The MCP worker process '{request.Executable}' did not start.");
			}
			return CreateHandle(process, request.Executable,
				process.StandardInput.BaseStream,
				process.StandardOutput.BaseStream,
				process.StandardError.BaseStream);
		} catch {
			process.Dispose();
			throw;
		}
	}

	// Every operation on the System.Diagnostics.Process object is captured here, inside the one class
	// this feature allows to name that type, and handed out as delegates. The containment
	// implementations and the registry therefore work with a plain interface and stay free of it.
	private static IWorkerProcessHandle CreateHandle(Process process, string fallbackExecutablePath,
		Stream standardInput, Stream standardOutput, Stream standardError) {
		int processId = process.Id;
		DateTime startTimeUtc = ReadStartTimeUtc(process);
		string executablePath = ReadExecutablePath(process) ?? fallbackExecutablePath;
		return new DelegatedWorkerProcessHandle(
			processId,
			startTimeUtc,
			executablePath,
			standardInput,
			standardOutput,
			standardError,
			hasExited: () => {
				try {
					return process.HasExited;
				} catch (Exception exception) when (IsProcessInspectionFailure(exception)) {
					return true;
				}
			},
			exitCode: () => {
				try {
					return process.HasExited ? process.ExitCode : null;
				} catch (Exception exception) when (IsProcessInspectionFailure(exception)) {
					return null;
				}
			},
			waitForExitAsync: token => process.WaitForExitAsync(token),
			killProcessTree: () => process.Kill(entireProcessTree: true),
			dispose: process.Dispose);
	}

	private static DateTime ReadStartTimeUtc(Process process) {
		try {
			return process.StartTime.ToUniversalTime();
		} catch (Exception exception) when (IsProcessInspectionFailure(exception)) {
			return DateTime.UtcNow;
		}
	}

	private static string ReadExecutablePath(Process process) {
		try {
			return process.MainModule?.FileName;
		} catch (Exception exception) when (IsProcessInspectionFailure(exception)) {
			return null;
		}
	}

	private static ProcessIdentitySnapshot CaptureCurrentProcessIdentity() {
		using Process current = Process.GetCurrentProcess();
		return new ProcessIdentitySnapshot(current.Id, ReadStartTimeUtc(current).Ticks,
			ReadExecutablePath(current) ?? System.Environment.ProcessPath);
	}

	private static bool IsProcessInspectionFailure(Exception exception) {
		return exception is ArgumentException
			or InvalidOperationException
			or NotSupportedException
			or System.ComponentModel.Win32Exception
			or IOException
			or UnauthorizedAccessException;
	}

	private void RegisterWorker(IContainedWorker worker) {
		try {
			_registry.Record(new WorkerRegistrationEntry(
				worker.ProcessId,
				worker.StartTimeUtc.Ticks,
				worker.ExecutablePath,
				_ownerIdentity.ProcessId,
				_ownerIdentity.StartTimeUtcTicks,
				_ownerIdentity.ExecutablePath,
				DateTimeOffset.UtcNow));
		} catch (Exception exception) when (exception is TimeoutException or IOException
				or UnauthorizedAccessException) {
			// Failing to record a worker costs a possible orphan after an abrupt parent death; failing the
			// tool call costs the user their answer. The containment layers are the primary guarantee, so
			// the warning is surfaced and the call proceeds.
			_logger.WriteWarning(
				$"Unable to record MCP worker {worker.ProcessId} for stale cleanup: {exception.Message}");
		}
	}

	private void UnregisterWorker(IContainedWorker worker) {
		try {
			_registry.Remove(worker.ProcessId, worker.StartTimeUtc.Ticks);
		} catch (Exception exception) when (exception is TimeoutException or IOException
				or UnauthorizedAccessException) {
			_logger.WriteWarning(
				$"Unable to remove MCP worker {worker.ProcessId} from the stale-cleanup registry: {exception.Message}");
		}
	}

	private void UpdatePeak(int active) {
		int observed = Volatile.Read(ref _peakActiveWorkers);
		while (active > observed) {
			int previous = Interlocked.CompareExchange(ref _peakActiveWorkers, active, observed);
			if (previous == observed) {
				return;
			}
			observed = previous;
		}
	}

	// The slot goes back to the pool it came from, named on the lease — not to "the" pool. A caller that
	// waits on a different pool therefore releases into that one without this method changing.
	private void ReleaseLease(IContainedWorker worker, WorkerSlotPool pool) {
		Interlocked.Decrement(ref _activeWorkers);
		UnregisterWorker(worker);
		worker.Dispose();
		pool.Release();
	}

	private void CountTermination() => Interlocked.Increment(ref _totalTerminated);

	#endregion

	/// <summary>A started process reduced to delegates, so its owner keeps the only reference to it.</summary>
	private sealed class DelegatedWorkerProcessHandle : IWorkerProcessHandle {

		private readonly Func<bool> _hasExited;
		private readonly Func<int?> _exitCode;
		private readonly Func<CancellationToken, Task> _waitForExitAsync;
		private readonly Action _killProcessTree;
		private readonly Action _dispose;

		public DelegatedWorkerProcessHandle(int processId, DateTime startTimeUtc, string executablePath,
			Stream standardInput, Stream standardOutput, Stream standardError, Func<bool> hasExited,
			Func<int?> exitCode, Func<CancellationToken, Task> waitForExitAsync, Action killProcessTree,
			Action dispose) {
			ProcessId = processId;
			StartTimeUtc = startTimeUtc;
			ExecutablePath = executablePath;
			StandardInput = standardInput;
			StandardOutput = standardOutput;
			StandardError = standardError;
			_hasExited = hasExited;
			_exitCode = exitCode;
			_waitForExitAsync = waitForExitAsync;
			_killProcessTree = killProcessTree;
			_dispose = dispose;
		}

		public int ProcessId { get; }

		public DateTime StartTimeUtc { get; }

		public string ExecutablePath { get; }

		public Stream StandardInput { get; }

		public Stream StandardOutput { get; }

		public Stream StandardError { get; }

		public bool HasExited => _hasExited();

		public int? ExitCode => _exitCode();

		public Task WaitForExitAsync(CancellationToken cancellationToken) => _waitForExitAsync(cancellationToken);

		public void KillProcessTree() => _killProcessTree();

		public void Dispose() => _dispose();
	}

	/// <summary>One held worker: a concurrency slot, a contained process and a registry entry.</summary>
	private sealed class SupervisedWorkerLease : IWorkerLease {

		private readonly WorkerProcessSupervisor _supervisor;
		private readonly IContainedWorker _worker;
		private readonly WorkerSlotPool _pool;
		private int _disposed;

		public SupervisedWorkerLease(WorkerProcessSupervisor supervisor, IContainedWorker worker,
			WorkerSlotPool pool, DateTimeOffset spawnedAtUtc, TimeSpan budget) {
			_supervisor = supervisor;
			_worker = worker;
			// Recorded rather than assumed: the lease is what returns the slot, so it must know WHICH
			// pool granted it. With one pool this is bookkeeping; with two it is correctness.
			_pool = pool;
			SpawnedAtUtc = spawnedAtUtc;
			Budget = budget;
		}

		public int ProcessId => _worker.ProcessId;

		public DateTimeOffset SpawnedAtUtc { get; }

		public TimeSpan Budget { get; }

		public DateTimeOffset BudgetExpiresAtUtc => SpawnedAtUtc + Budget;

		public Stream StandardInput => _worker.StandardInput;

		public Stream StandardOutput => _worker.StandardOutput;

		public Stream StandardError => _worker.StandardError;

		public bool HasExited => _worker.HasExited;

		public int? ExitCode => _worker.ExitCode;

		public Task WaitForExitAsync(CancellationToken cancellationToken) =>
			_worker.WaitForExitAsync(cancellationToken);

		public WorkerTerminationOutcome Terminate() {
			WorkerTerminationOutcome outcome = _worker.Kill();
			if (outcome != WorkerTerminationOutcome.AlreadyExited) {
				_supervisor.CountTermination();
			}
			return outcome;
		}

		public void Dispose() {
			if (Interlocked.Exchange(ref _disposed, 1) != 0) {
				return;
			}
			if (!_worker.HasExited) {
				Terminate();
			}
			_supervisor.ReleaseLease(_worker, _pool);
		}
	}

	/// <summary>
	/// One pool of concurrency slots: a cap, the semaphore that enforces it, and a count of the callers
	/// currently waiting on it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A type rather than three loose fields because the cap, the queue depth and the wait belong
	/// together in the refusal: <see cref="WorkerQueueWaitExpiredException"/> reports all three, and it
	/// must report them for the pool the caller actually waited on. One pool exists today — the per-call
	/// pool every ordinary tool call takes a slot from. A second one with its own cap, for workers whose
	/// lifetime outlives a single answer and which must therefore not queue behind (or ahead of)
	/// ordinary per-call work, is then an added field and an added <c>AcquireAsync</c> call site rather
	/// than a rewrite of the release path: the lease already names the pool it must release into.
	/// </para>
	/// <para>
	/// Not disposed: the semaphore lives as long as the supervisor, and disposing it while a caller is
	/// queued is the one thing that turns a bounded wait back into an unbounded failure.
	/// </para>
	/// </remarks>
	private sealed class WorkerSlotPool {

		private readonly SemaphoreSlim _slots;
		private int _queuedRequests;

		internal WorkerSlotPool(int cap) {
			Cap = cap;
			_slots = new SemaphoreSlim(cap, cap);
		}

		/// <summary>Gets the maximum number of slots this pool hands out at once.</summary>
		internal int Cap { get; }

		/// <summary>Gets the callers waiting for a slot on this pool right now.</summary>
		internal int QueuedRequests => Volatile.Read(ref _queuedRequests);

		/// <summary>
		/// Waits for a slot, for at most <paramref name="queueWaitBound"/>.
		/// </summary>
		/// <param name="queueWaitBound">How long the caller may wait before it is refused.</param>
		/// <param name="cancellationToken">Ends the wait early on the caller's behalf.</param>
		/// <exception cref="WorkerQueueWaitExpiredException">The bound elapsed with no slot free.</exception>
		internal async Task AcquireAsync(TimeSpan queueWaitBound, CancellationToken cancellationToken) {
			long startedAt = Stopwatch.GetTimestamp();
			Interlocked.Increment(ref _queuedRequests);
			try {
				if (await _slots.WaitAsync(queueWaitBound, cancellationToken).ConfigureAwait(false)) {
					return;
				}
				// Depth is read BEFORE this caller leaves the queue, so the number includes the call being
				// refused: "4 running, 9 queued" is what a caller needs to tell a burst from saturation.
				throw new WorkerQueueWaitExpiredException(Stopwatch.GetElapsedTime(startedAt),
					queueWaitBound, Cap, QueuedRequests);
			} finally {
				Interlocked.Decrement(ref _queuedRequests);
			}
		}

		/// <summary>Returns one slot to this pool.</summary>
		internal void Release() => _slots.Release();
	}
}
