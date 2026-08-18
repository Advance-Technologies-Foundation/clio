using System;
using System.Collections.Generic;
using System.Diagnostics;
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
	/// The <c>DOTNET_ROOT*</c> family is not decoration. It is how a .NET apphost — which is what a
	/// published clio and the test fixture both are — finds the shared runtime when that runtime is not
	/// at the machine's default location, and the variable that carries it is ARCHITECTURE-SPECIFIC:
	/// measured on an arm64 macOS host, only <c>DOTNET_ROOT_ARM64</c> was set, and dropping it made a
	/// spawned worker fail at startup with "You must install or update .NET" before executing a line of
	/// clio. A frozen environment that omits these turns a working host into one where every worker dies
	/// instantly, so the allowlist carries every spelling the host may have used.
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
		"LC_ALL"
	];

	private static readonly TimeSpan TerminationConfirmationTimeout = TimeSpan.FromSeconds(5);

	private readonly ILogger _logger;
	private readonly IProcessExecutor _processExecutor;
	private readonly IProcessContainment _containment;
	private readonly IClioExecutablePathProvider _executablePathProvider;
	private readonly IStaleWorkerRegistry _registry;
	private readonly SemaphoreSlim _slots;
	private readonly ProcessIdentitySnapshot _ownerIdentity;

	private int _activeWorkers;
	private int _queuedRequests;
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
		: this(logger, processExecutor, containment, executablePathProvider, registry, null) {
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
	internal WorkerProcessSupervisor(ILogger logger, IProcessExecutor processExecutor,
		IProcessContainment containment, IClioExecutablePathProvider executablePathProvider,
		IStaleWorkerRegistry registry, int? concurrencyCap) {
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
		_slots = new SemaphoreSlim(ConcurrencyCap, ConcurrencyCap);
		_ownerIdentity = CaptureCurrentProcessIdentity();
	}

	/// <inheritdoc />
	public int ConcurrencyCap { get; }

	#region Methods: worker lifecycle

	/// <inheritdoc />
	public async Task<IWorkerLease> SpawnContainedAsync(WorkerSpawnRequest request,
		CancellationToken cancellationToken) {
		ArgumentNullException.ThrowIfNull(request);
		Interlocked.Increment(ref _queuedRequests);
		try {
			// No queue timeout, by design: the caller's own cancellation is the only thing that ends this
			// wait. A call that has to wait for a slot is queued, never dropped (AC-01).
			await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
		} finally {
			Interlocked.Decrement(ref _queuedRequests);
		}

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
			return new SupervisedWorkerLease(this, worker, spawnedAtUtc, request.Budget);
		} finally {
			if (!slotHandedOver) {
				_slots.Release();
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
			Volatile.Read(ref _queuedRequests),
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

	private void ReleaseLease(IContainedWorker worker) {
		Interlocked.Decrement(ref _activeWorkers);
		UnregisterWorker(worker);
		worker.Dispose();
		_slots.Release();
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
		private int _disposed;

		public SupervisedWorkerLease(WorkerProcessSupervisor supervisor, IContainedWorker worker,
			DateTimeOffset spawnedAtUtc, TimeSpan budget) {
			_supervisor = supervisor;
			_worker = worker;
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
			_supervisor.ReleaseLease(_worker);
		}
	}
}
