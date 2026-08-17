using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Clio.Common.McpWorker;

/// <summary>
/// How this process learns that its parent died.
/// </summary>
public enum ParentDeathSignallingMode {

	/// <summary>
	/// No in-child signalling is armed. Windows needs none — the parent's job object kills the whole tree
	/// when it closes — and a Unix host whose native calls are unavailable degrades to this.
	/// </summary>
	NotSupported,

	/// <summary>Linux: <c>prctl(PR_SET_PDEATHSIG, SIGTERM)</c> plus a SIGTERM handler.</summary>
	PrctlParentDeathSignal,

	/// <summary>macOS: a <c>kqueue</c> <c>EVFILT_PROC</c> / <c>NOTE_EXIT</c> watch on the parent process.</summary>
	KqueueProcessExit
}

/// <summary>
/// What arming produced. Every field is measured rather than assumed, because each one changes what a later
/// kill is allowed to address.
/// </summary>
/// <param name="ProcessGroupPromoted">
/// Whether this process is now a process-group LEADER. Until it is, a group-addressed kill would signal
/// somebody else's group — possibly the launching shell's — so the group kill is withheld.
/// </param>
/// <param name="Mode">How parent death is detected.</param>
/// <param name="ParentProcessId">The parent observed at arming time; <c>0</c> when not applicable.</param>
/// <param name="ParentAlreadyExited">
/// Whether the parent had already exited by the time arming completed. Detection cannot be left to the
/// signal: a parent that died BEFORE the watch was installed never triggers one.
/// </param>
public sealed record ParentDeathWatchResult(
	bool ProcessGroupPromoted,
	ParentDeathSignallingMode Mode,
	int ParentProcessId,
	bool ParentAlreadyExited);

/// <summary>
/// The worker-side half of containment: the child promotes itself to its own process group and arms
/// parent-death signalling, so a parent that is <c>SIGKILL</c>ed still takes the worker AND everything the
/// worker spawned with it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this cannot live in the parent.</b> A <c>SIGKILL</c>ed parent executes no code, so nothing it
/// installs can help. Only the child can arrange its own death.
/// </para>
/// <para>
/// <b>Why SIGTERM and not SIGKILL.</b> <c>PR_SET_PDEATHSIG</c> accepts any signal, and <c>SIGKILL</c> looks
/// like the safer choice — it is the worse one. A <c>SIGKILL</c>ed worker runs no handler, so it cannot pass
/// the signal on to its own descendants, and the orphaned grandchildren are exactly what containment exists to
/// prevent. SIGTERM plus a handler that kills the whole GROUP (including itself) is what makes "both disappear"
/// true.
/// </para>
/// <para>
/// <b>The promotion barrier, restated because getting it wrong is catastrophic.</b> <c>Process.Start</c> calls
/// neither <c>setsid</c> nor <c>setpgid</c>, so a spawned child inherits the LAUNCHING shell's process group.
/// A group kill issued before promotion would therefore address the parent clio, the agent host and the user's
/// interactive shell. <see cref="TerminateSelfAndDescendants"/> re-derives leadership
/// (<c>getpgid(0) == getpid()</c>) immediately before signalling and falls back to killing only itself when it
/// does not hold.
/// </para>
/// <para>
/// <b>macOS has no <c>prctl</c>.</b> Verified on this host: <c>/usr/include/sys/prctl.h</c> is absent, and so
/// is <c>setsid(1)</c>. The macOS path therefore watches the parent pid with <c>kqueue</c>
/// <c>EVFILT_PROC</c> / <c>NOTE_EXIT</c> from a dedicated background thread that invokes the same handler.
/// </para>
/// <para>
/// A stdin end-of-file watchdog may be added later as a supplementary DETECTOR inside the worker, but never as
/// the parent's kill mechanism: the parent kills through containment, not by closing a pipe.
/// </para>
/// </remarks>
public static class UnixParentDeathWatch {

	private const int SignalTerm = 15;
	private const int SignalKill = 9;
	private const int PrctlSetParentDeathSignal = 1;

	// The lowest pid that can be a real parent. Reparenting to init/launchd (pid 1) is how a Unix child
	// observes that its parent is gone.
	private const int InitProcessId = 1;

	// Kept for the process lifetime on purpose: disposing the registration would restore the default SIGTERM
	// disposition and silently disarm the watch.
	private static IDisposable _signalRegistration;
	private static Thread _kqueueWatcher;

	/// <summary>
	/// Promotes this process to its own process group and arms parent-death signalling.
	/// </summary>
	/// <remarks>
	/// The <c>getppid</c> re-check after arming closes the already-dead-parent race: between reading the
	/// parent pid and installing the watch the parent can exit, and no signal would ever arrive. When that is
	/// observed, <paramref name="onParentDeath"/> is invoked immediately.
	/// </remarks>
	/// <param name="onParentDeath">
	/// What to run when the parent dies; defaults to <see cref="TerminateSelfAndDescendants"/>. Injectable so
	/// the arming path is testable without a test process killing its own group.
	/// </param>
	/// <param name="promoteProcessGroup">
	/// Whether to call <c>setpgid(0, 0)</c>. Only a test passes <see langword="false"/>: a worker that skipped
	/// promotion could never be group-killed.
	/// </param>
	/// <returns>What arming produced.</returns>
	public static ParentDeathWatchResult Arm(Action onParentDeath = null, bool promoteProcessGroup = true) {
		Action handler = onParentDeath ?? TerminateSelfAndDescendants;
		if (OperatingSystem.IsWindows()) {
			// Windows containment is arranged entirely by the parent (CREATE_SUSPENDED, job-object assignment,
			// then resume), and JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE covers parent death including a hard kill.
			return new ParentDeathWatchResult(false, ParentDeathSignallingMode.NotSupported, 0, false);
		}

		bool promoted = promoteProcessGroup && TryPromoteToProcessGroupLeader();
		if (!TryGetParentProcessId(out int parentProcessId)) {
			return new ParentDeathWatchResult(promoted, ParentDeathSignallingMode.NotSupported, 0, false);
		}
		if (parentProcessId <= InitProcessId) {
			// Already reparented: the parent died before this call. No signal will ever arrive.
			handler();
			return new ParentDeathWatchResult(promoted, ParentDeathSignallingMode.NotSupported, parentProcessId, true);
		}

		ParentDeathSignallingMode mode = OperatingSystem.IsMacOS()
			? ArmKqueueWatch(parentProcessId, handler)
			: ArmPrctlParentDeathSignal(handler);

		// Re-read the parent AFTER arming. A parent that exited during the window above left the watch armed
		// against a pid that will never signal again.
		bool parentAlreadyExited = TryGetParentProcessId(out int parentAfterArming)
			&& (parentAfterArming <= InitProcessId || parentAfterArming != parentProcessId);
		if (parentAlreadyExited) {
			handler();
		}
		return new ParentDeathWatchResult(promoted, mode, parentProcessId, parentAlreadyExited);
	}

	/// <summary>
	/// The default parent-death handler: kill this process's whole group when it leads one, otherwise kill only
	/// this process.
	/// </summary>
	/// <remarks>
	/// Leadership is re-derived here rather than trusted from <see cref="Arm"/>, because the consequence of
	/// getting it wrong is signalling the agent host and the user's shell. Killing only itself is a strictly
	/// worse but SAFE outcome: a worker that never promoted has not yet answered <c>initialize</c> and has
	/// therefore spawned nothing.
	/// </remarks>
	public static void TerminateSelfAndDescendants() {
		int self = Environment.ProcessId;
		if (TryGetProcessGroupId(self, out int groupId) && groupId == self) {
			if (TryNativeCall(() => UnixNativeMethods.kill(-self, SignalKill))) {
				return;
			}
		}
		TryNativeCall(() => UnixNativeMethods.kill(self, SignalKill));
	}

	/// <summary>
	/// Resolves the signalling mechanism this platform supports, without arming anything.
	/// </summary>
	/// <returns>The mode <see cref="Arm"/> would use on this platform.</returns>
	public static ParentDeathSignallingMode ResolveSignallingMode() {
		if (OperatingSystem.IsWindows()) {
			return ParentDeathSignallingMode.NotSupported;
		}
		return OperatingSystem.IsMacOS()
			? ParentDeathSignallingMode.KqueueProcessExit
			: ParentDeathSignallingMode.PrctlParentDeathSignal;
	}

	private static bool TryPromoteToProcessGroupLeader() {
		if (!TryNativeCall(() => UnixParentDeathNativeMethods.setpgid(0, 0))) {
			// setpgid fails with EPERM when this process is already a session leader, which is itself a
			// group leader — so confirm by reading the group rather than reporting failure.
			return TryGetProcessGroupId(Environment.ProcessId, out int groupId) && groupId == Environment.ProcessId;
		}
		return true;
	}

	private static ParentDeathSignallingMode ArmPrctlParentDeathSignal(Action handler) {
		// PosixSignalRegistration rather than a sigaction P/Invoke: the runtime already owns signal
		// disposition, and installing a raw handler underneath it is how a .NET process loses SIGTERM
		// handling altogether.
		try {
			_signalRegistration = System.Runtime.InteropServices.PosixSignalRegistration.Create(
				PosixSignal.SIGTERM,
				context => {
					context.Cancel = true;
					handler();
				});
		} catch (Exception exception) when (exception is PlatformNotSupportedException or ArgumentException) {
			return ParentDeathSignallingMode.NotSupported;
		}
		if (!TryNativeCall(() => UnixParentDeathNativeMethods.prctl(
				PrctlSetParentDeathSignal, (ulong)SignalTerm, 0, 0, 0))) {
			return ParentDeathSignallingMode.NotSupported;
		}
		return ParentDeathSignallingMode.PrctlParentDeathSignal;
	}

	private static ParentDeathSignallingMode ArmKqueueWatch(int parentProcessId, Action handler) {
		int queue;
		try {
			queue = UnixParentDeathNativeMethods.kqueue();
		} catch (Exception exception) when (IsNativeCallUnavailable(exception)) {
			return ParentDeathSignallingMode.NotSupported;
		}
		if (queue < 0) {
			return ParentDeathSignallingMode.NotSupported;
		}

		KEvent registration = new() {
			Ident = (nuint)parentProcessId,
			Filter = UnixParentDeathNativeMethods.EvfiltProc,
			Flags = UnixParentDeathNativeMethods.EvAdd
				| UnixParentDeathNativeMethods.EvEnable
				| UnixParentDeathNativeMethods.EvOneShot,
			FilterFlags = UnixParentDeathNativeMethods.NoteExit,
			Data = 0,
			UserData = IntPtr.Zero
		};
		int registered;
		try {
			registered = UnixParentDeathNativeMethods.kevent(queue, [registration], 1, null, 0, IntPtr.Zero);
		} catch (Exception exception) when (IsNativeCallUnavailable(exception)) {
			UnixParentDeathNativeMethods.close(queue);
			return ParentDeathSignallingMode.NotSupported;
		}
		if (registered < 0) {
			// ESRCH here means the parent is already gone; the getppid re-check in Arm reports that, so the
			// only thing to do is stop watching.
			UnixParentDeathNativeMethods.close(queue);
			return ParentDeathSignallingMode.NotSupported;
		}

		// Background thread: it blocks in kevent for the process's whole life, and a foreground thread doing
		// that would keep the worker alive after its work finished.
		_kqueueWatcher = new Thread(() => WaitForParentExit(queue, handler)) {
			IsBackground = true,
			Name = "clio-mcp-worker-parent-death-watch"
		};
		_kqueueWatcher.Start();
		return ParentDeathSignallingMode.KqueueProcessExit;
	}

	private static void WaitForParentExit(int queue, Action handler) {
		try {
			KEvent[] events = new KEvent[1];
			// A null timeout blocks until the parent exits. EINTR is the one retryable failure.
			while (true) {
				int count = UnixParentDeathNativeMethods.kevent(queue, null, 0, events, 1, IntPtr.Zero);
				if (count > 0) {
					handler();
					return;
				}
				if (count == 0) {
					continue;
				}
				if (Marshal.GetLastWin32Error() == UnixParentDeathNativeMethods.Eintr) {
					continue;
				}
				return;
			}
		} catch (Exception exception) when (IsNativeCallUnavailable(exception)) {
			// Containment degrades to the parent-side kill, which is still in place.
		} finally {
			UnixParentDeathNativeMethods.close(queue);
		}
	}

	private static bool TryGetParentProcessId(out int parentProcessId) {
		parentProcessId = 0;
		try {
			parentProcessId = UnixParentDeathNativeMethods.getppid();
			return parentProcessId > 0;
		} catch (Exception exception) when (IsNativeCallUnavailable(exception)) {
			return false;
		}
	}

	private static bool TryGetProcessGroupId(int processId, out int groupId) {
		groupId = -1;
		try {
			int result = UnixNativeMethods.getpgid(processId);
			if (result < 0) {
				return false;
			}
			groupId = result;
			return true;
		} catch (Exception exception) when (IsNativeCallUnavailable(exception)) {
			return false;
		}
	}

	private static bool TryNativeCall(Func<int> call) {
		try {
			return call() == 0;
		} catch (Exception exception) when (IsNativeCallUnavailable(exception)) {
			return false;
		}
	}

	// The C library is always present on a Unix host, but its SONAME is not uniform and a resolver can still
	// fail on an unusual distribution or in a trimmed container. Treat that as "signalling unavailable" and
	// degrade rather than failing the worker at startup.
	private static bool IsNativeCallUnavailable(Exception exception) =>
		exception is DllNotFoundException or EntryPointNotFoundException or TypeInitializationException;
}

/// <summary>
/// macOS <c>struct kevent</c>. The layout is macOS-specific by construction — this type is only ever passed to
/// the macOS <c>kevent</c> entry point — and matches the 64-bit ABI: <c>uintptr_t</c>, <c>int16_t</c>,
/// <c>uint16_t</c>, <c>uint32_t</c>, <c>intptr_t</c>, <c>void*</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct KEvent {

	/// <summary>Identifier for this event; the watched process id for <c>EVFILT_PROC</c>.</summary>
	public nuint Ident;

	/// <summary>Filter for this event (<c>EVFILT_PROC</c>).</summary>
	public short Filter;

	/// <summary>General flags (<c>EV_ADD</c> / <c>EV_ENABLE</c> / <c>EV_ONESHOT</c>).</summary>
	public ushort Flags;

	/// <summary>Filter-specific flags (<c>NOTE_EXIT</c>).</summary>
	public uint FilterFlags;

	/// <summary>Filter-specific data (the exit status on a <c>NOTE_EXIT</c> report).</summary>
	public nint Data;

	/// <summary>Opaque user data; unused here.</summary>
	public IntPtr UserData;
}

/// <summary>
/// The C-library entry points the worker-side watch needs, beyond the two
/// <see cref="UnixNativeMethods"/> already declares.
/// </summary>
/// <remarks>
/// The static constructor forces <see cref="UnixNativeMethods"/>'s to run because that type installs the
/// assembly's single <c>libc</c> import resolver, and
/// <see cref="System.Runtime.InteropServices.NativeLibrary.SetDllImportResolver"/> throws when a second one is
/// registered for the same assembly. The resolver is per-assembly, so these imports go through it too.
/// </remarks>
internal static class UnixParentDeathNativeMethods {

	private const string LibraryName = "libc";

	/// <summary><c>EVFILT_PROC</c>: watch process state changes.</summary>
	internal const short EvfiltProc = -5;

	/// <summary><c>EV_ADD</c>.</summary>
	internal const ushort EvAdd = 0x0001;

	/// <summary><c>EV_ENABLE</c>.</summary>
	internal const ushort EvEnable = 0x0004;

	/// <summary><c>EV_ONESHOT</c>: report once, then drop the registration.</summary>
	internal const ushort EvOneShot = 0x0010;

	/// <summary><c>NOTE_EXIT</c>: the watched process exited.</summary>
	internal const uint NoteExit = 0x80000000;

	/// <summary><c>EINTR</c>: the call was interrupted and may be retried.</summary>
	internal const int Eintr = 4;

	static UnixParentDeathNativeMethods() {
		RuntimeHelpers.RunClassConstructor(typeof(UnixNativeMethods).TypeHandle);
	}

	/// <summary>Puts <paramref name="pid"/> into process group <paramref name="pgid"/>; zeros mean "self".</summary>
	/// <param name="pid">Process identifier, or 0 for the calling process.</param>
	/// <param name="pgid">Group identifier, or 0 to use <paramref name="pid"/>.</param>
	/// <returns>Zero on success, -1 on failure.</returns>
	[DllImport(LibraryName, SetLastError = true)]
	internal static extern int setpgid(int pid, int pgid);

	/// <summary>Returns the calling process's parent process identifier.</summary>
	/// <returns>The parent process identifier.</returns>
	[DllImport(LibraryName, SetLastError = true)]
	internal static extern int getppid();

	/// <summary>Linux process-control call; used only for <c>PR_SET_PDEATHSIG</c>.</summary>
	/// <param name="option">The <c>PR_*</c> option.</param>
	/// <param name="arg2">First option argument.</param>
	/// <param name="arg3">Second option argument.</param>
	/// <param name="arg4">Third option argument.</param>
	/// <param name="arg5">Fourth option argument.</param>
	/// <returns>Zero on success, -1 on failure.</returns>
	[DllImport(LibraryName, SetLastError = true)]
	internal static extern int prctl(int option, ulong arg2, ulong arg3, ulong arg4, ulong arg5);

	/// <summary>Creates a new kernel event queue.</summary>
	/// <returns>The queue descriptor, or -1 on failure.</returns>
	[DllImport(LibraryName, SetLastError = true)]
	internal static extern int kqueue();

	/// <summary>Registers events with, and reads events from, a kernel event queue.</summary>
	/// <param name="kq">The queue descriptor.</param>
	/// <param name="changelist">Events to register, or <see langword="null"/>.</param>
	/// <param name="nchanges">Number of entries in <paramref name="changelist"/>.</param>
	/// <param name="eventlist">Buffer receiving reported events, or <see langword="null"/>.</param>
	/// <param name="nevents">Capacity of <paramref name="eventlist"/>.</param>
	/// <param name="timeout">A <c>struct timespec*</c>; <see cref="IntPtr.Zero"/> blocks indefinitely.</param>
	/// <returns>The number of events placed in <paramref name="eventlist"/>, or -1 on failure.</returns>
	[DllImport(LibraryName, SetLastError = true)]
	internal static extern int kevent(
		int kq,
		[In] KEvent[] changelist,
		int nchanges,
		[Out] KEvent[] eventlist,
		int nevents,
		IntPtr timeout);

	/// <summary>Closes a file descriptor.</summary>
	/// <param name="fd">The descriptor to close.</param>
	/// <returns>Zero on success, -1 on failure.</returns>
	[DllImport(LibraryName, SetLastError = true)]
	internal static extern int close(int fd);
}
