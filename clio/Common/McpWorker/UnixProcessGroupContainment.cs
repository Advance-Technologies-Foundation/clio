using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Common.McpWorker;

/// <summary>
/// Unix containment: a deliberate kill addresses the worker's own process GROUP, so every descendant
/// it spawned goes with it in one signal.
/// </summary>
/// <remarks>
/// <para>
/// <b>The measured trap this class exists to avoid.</b> .NET's <c>Process.Start</c> calls neither
/// <c>setsid</c> nor <c>setpgid</c>, so a spawned child inherits the LAUNCHING shell's process group.
/// Measured on a development host: an orphaned .NET descendant had process group 17401 while the
/// launching shell was itself process 17401. Deriving a kill target from the worker's current group
/// would therefore address the parent clio, the agent host and the user's interactive shell — the
/// worst possible outcome of a routine budget expiry. The kill target is never derived from the
/// worker's group id.
/// </para>
/// <para>
/// <b>The promotion barrier.</b> A group kill is issued only once the worker is a process-group
/// LEADER, i.e. <c>getpgid(pid) == pid</c>, which is exactly the state <c>setpgid(0, 0)</c> in the
/// worker establishes and no inherited group can imitate. Leadership is the promotion proof; being
/// merely different from the parent's group is not. Until the worker has promoted itself the kill
/// falls back to the best-effort process-tree kill, which is sufficient in that window because a
/// worker that has not yet answered <c>initialize</c> has spawned nothing.
/// </para>
/// <para>
/// <b>What this class does NOT do.</b> Parent-death signalling is armed inside the worker, not here:
/// a parent that is <c>SIGKILL</c>ed executes no code, so nothing the parent installs can help. The
/// worker arms <c>SIGTERM</c>-on-parent-death and its handler kills its own group — SIGTERM plus a
/// handler rather than SIGKILL precisely because a <c>SIGKILL</c>ed worker could not pass the signal
/// on to its own children, and "both disappear" is the requirement.
/// </para>
/// </remarks>
public sealed class UnixProcessGroupContainment : IProcessContainment {

	private const int SignalKill = 9;
	private const int ErrorNoSuchProcess = 3;

	/// <inheritdoc />
	/// <remarks>
	/// Always <see langword="false"/>: promotion happens inside the child, so the parent has nothing to
	/// arrange at creation time and the ordinary redirected <c>Process.Start</c> is sufficient.
	/// </remarks>
	public bool OwnsProcessCreation => false;

	/// <inheritdoc />
	public IContainedWorker Launch(WorkerLaunchRequest request) {
		throw new NotSupportedException(
			"Unix containment does not create processes; start the process and call Adopt instead.");
	}

	/// <inheritdoc />
	public IContainedWorker Adopt(IWorkerProcessHandle startedProcess) {
		ArgumentNullException.ThrowIfNull(startedProcess);
		return new UnixContainedWorker(startedProcess);
	}

	/// <inheritdoc />
	public WorkerTerminationOutcome TerminateOrphan(IWorkerProcessHandle orphan) {
		ArgumentNullException.ThrowIfNull(orphan);
		return Terminate(orphan);
	}

	internal static WorkerTerminationOutcome Terminate(IWorkerProcessHandle handle) {
		if (handle.HasExited) {
			return WorkerTerminationOutcome.AlreadyExited;
		}
		if (!TryReadProcessGroupId(handle.ProcessId, out int processGroupId)) {
			return KillTree(handle);
		}
		if (processGroupId != handle.ProcessId) {
			// Not a group leader: the worker has not promoted itself, so its group is somebody else's —
			// possibly this process's own. Never signal it.
			return KillTree(handle);
		}
		if (!TryKillProcessGroup(handle.ProcessId, out int errorCode)) {
			return errorCode == ErrorNoSuchProcess
				? WorkerTerminationOutcome.AlreadyExited
				: KillTree(handle);
		}
		return WorkerTerminationOutcome.ContainedGroupKilled;
	}

	private static WorkerTerminationOutcome KillTree(IWorkerProcessHandle handle) {
		try {
			handle.KillProcessTree();
			return WorkerTerminationOutcome.FallbackTreeKilled;
		} catch (InvalidOperationException) {
			// The process exited between the check and the kill.
			return WorkerTerminationOutcome.AlreadyExited;
		} catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
				or NotSupportedException or AggregateException) {
			return WorkerTerminationOutcome.Failed;
		}
	}

	private static bool TryReadProcessGroupId(int processId, out int processGroupId) {
		processGroupId = -1;
		try {
			int result = UnixNativeMethods.getpgid(processId);
			if (result < 0) {
				return false;
			}
			processGroupId = result;
			return true;
		} catch (Exception exception) when (IsNativeCallUnavailable(exception)) {
			return false;
		}
	}

	private static bool TryKillProcessGroup(int groupLeaderProcessId, out int errorCode) {
		errorCode = 0;
		try {
			if (UnixNativeMethods.kill(-groupLeaderProcessId, SignalKill) == 0) {
				return true;
			}
			errorCode = Marshal.GetLastWin32Error();
			return false;
		} catch (Exception exception) when (IsNativeCallUnavailable(exception)) {
			return false;
		}
	}

	// The C library is always present on a Unix host, but its SONAME is not uniform and a resolver can
	// still fail on an unusual distribution or in a trimmed container. Treat that as "containment
	// unavailable" and fall back rather than failing a tool call.
	private static bool IsNativeCallUnavailable(Exception exception) {
		return exception is DllNotFoundException or EntryPointNotFoundException or TypeInitializationException;
	}

	/// <summary>The single contained worker on Unix: a started process plus group-addressed kill.</summary>
	private sealed class UnixContainedWorker : IContainedWorker {

		private readonly IWorkerProcessHandle _handle;

		public UnixContainedWorker(IWorkerProcessHandle handle) {
			_handle = handle;
		}

		public int ProcessId => _handle.ProcessId;

		public DateTime StartTimeUtc => _handle.StartTimeUtc;

		public string ExecutablePath => _handle.ExecutablePath;

		public System.IO.Stream StandardInput => _handle.StandardInput;

		public System.IO.Stream StandardOutput => _handle.StandardOutput;

		public System.IO.Stream StandardError => _handle.StandardError;

		public bool HasExited => _handle.HasExited;

		public int? ExitCode => _handle.ExitCode;

		public Task WaitForExitAsync(CancellationToken cancellationToken) =>
			_handle.WaitForExitAsync(cancellationToken);

		public WorkerTerminationOutcome Kill() => Terminate(_handle);

		public void Dispose() => _handle.Dispose();
	}
}

/// <summary>
/// The two C-library calls Unix containment needs.
/// </summary>
/// <remarks>
/// The import name is resolved explicitly because <c>libc</c> has no single portable file name:
/// <c>libSystem.dylib</c> on macOS, <c>libc.so.6</c> on glibc, <c>libc.so</c> elsewhere — and the
/// default probing rules find none of them reliably.
/// </remarks>
internal static class UnixNativeMethods {

	private const string LibraryName = "libc";

	static UnixNativeMethods() {
		NativeLibrary.SetDllImportResolver(typeof(UnixNativeMethods).Assembly, ResolveLibrary);
	}

	/// <summary>Returns the process group identifier of <paramref name="pid"/>, or -1 on failure.</summary>
	/// <param name="pid">Process identifier.</param>
	/// <returns>The process group identifier, or -1.</returns>
	[DllImport(LibraryName, SetLastError = true)]
	internal static extern int getpgid(int pid);

	/// <summary>
	/// Sends <paramref name="sig"/> to <paramref name="pid"/>; a negative <paramref name="pid"/>
	/// addresses the process group whose identifier is its absolute value.
	/// </summary>
	/// <param name="pid">Process identifier, or the negated group identifier.</param>
	/// <param name="sig">Signal number.</param>
	/// <returns>Zero on success, -1 on failure.</returns>
	[DllImport(LibraryName, SetLastError = true)]
	internal static extern int kill(int pid, int sig);

	private static IntPtr ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath) {
		if (!string.Equals(libraryName, LibraryName, StringComparison.Ordinal)) {
			return IntPtr.Zero;
		}
		foreach (string candidate in Candidates()) {
			if (NativeLibrary.TryLoad(candidate, out IntPtr handle)) {
				return handle;
			}
		}
		return IntPtr.Zero;
	}

	private static string[] Candidates() {
		if (OperatingSystem.IsMacOS()) {
			return ["libSystem.dylib", "libc"];
		}
		return ["libc.so.6", "libc.so", "libc"];
	}
}
