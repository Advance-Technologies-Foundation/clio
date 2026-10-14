using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace Clio.Common.BrowserSession;

/// <summary>
/// Starts a browser with posix_spawn so that its file descriptors 3 and 4 are the two ends of a private DevTools pipe,
/// and 0-2 are /dev/null (under the MCP server, clio's own stdout is the JSON-RPC stream).
/// </summary>
internal static class UnixPipeSpawn {

	internal const int BrowserReadFd = 3;
	internal const int BrowserWriteFd = 4;
	// posix_spawn_file_actions_t and posix_spawnattr_t are opaque: a pointer on macOS, 80 and 336 bytes in glibc on
	// 64-bit. libc initializes whatever buffer it is given, so a generous one fits every layout.
	private const int OpaqueBufferBytes = 1024;
	private const int ORdOnly = 0;
	private const int OWrOnly = 1;
	private const int WNoHang = 1;
	private const int EIntr = 4;
	private const int SigKill = 9;
	// Darwin's posix_spawn extension: every descriptor not named by a file action is closed in the child.
	private const short PosixSpawnCloexecDefault = 0x4000;
	private const int ReapPollMs = 50;

	[SupportedOSPlatform("linux")]
	[SupportedOSPlatform("macos")]
	internal static PipeBrowserProcess Start(string executablePath, IReadOnlyList<string> arguments) {
		// HandleInheritability.None: both ends are close-on-exec in clio, so no other child clio starts ever holds them.
		// dup2 in the file actions below gives the browser its own copies on 3 and 4, which dup2 makes inheritable.
		AnonymousPipeServerStream toBrowser = new(PipeDirection.Out, HandleInheritability.None);
		AnonymousPipeServerStream fromBrowser = new(PipeDirection.In, HandleInheritability.None);
		try {
			int browserRead = (int)toBrowser.ClientSafePipeHandle.DangerousGetHandle();
			int browserWrite = (int)fromBrowser.ClientSafePipeHandle.DangerousGetHandle();
			int pid = Spawn(executablePath, arguments, browserRead, browserWrite);
			toBrowser.DisposeLocalCopyOfClientHandle();
			fromBrowser.DisposeLocalCopyOfClientHandle();
			return new PipeBrowserProcess(pid, ProcessStartTime.Read(pid), fromBrowser, toBrowser, new ChildLifetime(pid));
		}
		catch {
			toBrowser.Dispose();
			fromBrowser.Dispose();
			throw;
		}
	}

	private static int Spawn(string executablePath, IReadOnlyList<string> arguments, int browserRead, int browserWrite) {
		using NativeStrings path = new([executablePath]);
		using NativeStrings argv = new([executablePath, .. arguments]);
		using NativeStrings envp = new(CurrentEnvironment());
		using NativeStrings devNull = new(["/dev/null"]);
		IntPtr actions = Marshal.AllocHGlobal(OpaqueBufferBytes);
		IntPtr attributes = IntPtr.Zero;
		bool actionsReady = false;
		bool attributesReady = false;
		try {
			Check(Interop.FileActionsInit(actions), "posix_spawn_file_actions_init");
			actionsReady = true;
			AddFileActions(actions, devNull.Pointers[0], browserRead, browserWrite);
			if (OperatingSystem.IsMacOS()) {
				attributes = Marshal.AllocHGlobal(OpaqueBufferBytes);
				Check(Interop.AttrInit(attributes), "posix_spawnattr_init");
				attributesReady = true;
				Check(Interop.AttrSetFlags(attributes, PosixSpawnCloexecDefault), "posix_spawnattr_setflags");
			}
			Check(Interop.PosixSpawn(out int pid, path.Pointers[0], actions, attributes, argv.Array, envp.Array),
				"posix_spawn");
			return pid;
		}
		finally {
			if (attributesReady) {
				Interop.AttrDestroy(attributes);
			}
			if (attributes != IntPtr.Zero) {
				Marshal.FreeHGlobal(attributes);
			}
			if (actionsReady) {
				Interop.FileActionsDestroy(actions);
			}
			Marshal.FreeHGlobal(actions);
		}
	}

	// The pipe ends are first copied to two scratch descriptors, so that neither can be overwritten by the other's dup2
	// when the kernel happened to hand one of them out as 3 or 4. dup2 onto a different number also clears close-on-exec,
	// which a dup2 onto the same number would not.
	private static void AddFileActions(IntPtr actions, IntPtr devNull, int browserRead, int browserWrite) {
		(int scratchRead, int scratchWrite) = ScratchDescriptors(browserRead, browserWrite);
		Check(Interop.FileActionsAddDup2(actions, browserRead, scratchRead), "posix_spawn_file_actions_adddup2");
		Check(Interop.FileActionsAddDup2(actions, browserWrite, scratchWrite), "posix_spawn_file_actions_adddup2");
		Check(Interop.FileActionsAddOpen(actions, 0, devNull, ORdOnly, 0), "posix_spawn_file_actions_addopen");
		Check(Interop.FileActionsAddOpen(actions, 1, devNull, OWrOnly, 0), "posix_spawn_file_actions_addopen");
		Check(Interop.FileActionsAddOpen(actions, 2, devNull, OWrOnly, 0), "posix_spawn_file_actions_addopen");
		Check(Interop.FileActionsAddDup2(actions, scratchRead, BrowserReadFd), "posix_spawn_file_actions_adddup2");
		Check(Interop.FileActionsAddDup2(actions, scratchWrite, BrowserWriteFd), "posix_spawn_file_actions_adddup2");
		Check(Interop.FileActionsAddClose(actions, scratchRead), "posix_spawn_file_actions_addclose");
		Check(Interop.FileActionsAddClose(actions, scratchWrite), "posix_spawn_file_actions_addclose");
		if (OperatingSystem.IsLinux()) {
			TryCloseFrom(actions, BrowserWriteFd + 1);
		}
	}

	/// <summary>Two descriptor numbers above 4 that are neither pipe end, to stage the ends through in the child.</summary>
	internal static (int Read, int Write) ScratchDescriptors(int browserRead, int browserWrite) {
		int first = NextFree(BrowserWriteFd + 1, browserRead, browserWrite);
		int second = NextFree(first + 1, browserRead, browserWrite);
		return (first, second);
	}

	private static int NextFree(int from, int browserRead, int browserWrite) {
		int candidate = from;
		while (candidate == browserRead || candidate == browserWrite) {
			candidate++;
		}
		return candidate;
	}

	// glibc 2.34+: close every other descriptor in the child, as macOS does with POSIX_SPAWN_CLOEXEC_DEFAULT. Older glibc
	// and musl lack it; .NET opens its own descriptors close-on-exec, so only an fd clio itself inherited could leak.
	private static void TryCloseFrom(IntPtr actions, int lowest) {
		try {
			Interop.FileActionsAddCloseFrom(actions, lowest);
		}
		catch (EntryPointNotFoundException) {
			// Not available in this libc.
		}
	}

	private static IEnumerable<string> CurrentEnvironment() {
		foreach (DictionaryEntry variable in Environment.GetEnvironmentVariables()) {
			if (variable.Key is string key && variable.Value is string value) {
				yield return key + "=" + value;
			}
		}
	}

	/// <summary>
	/// The browser as clio's own child. Its id cannot be reused until it is reaped here, which is what makes killing it by
	/// id safe; .NET reaps only the children System.Diagnostics.Process started, so a child left unreaped is a zombie.
	/// </summary>
	private sealed class ChildLifetime(int pid) : IPipeProcessLifetime {

		private int _reaped;

		public bool WaitForExit(int milliseconds) {
			long deadline = Environment.TickCount64 + milliseconds;
			while (!TryReap()) {
				if (Environment.TickCount64 >= deadline) {
					return false;
				}
				Thread.Sleep(ReapPollMs);
			}
			return true;
		}

		public void Terminate() {
			if (Volatile.Read(ref _reaped) == 0) {
				Interop.Kill(pid, SigKill);
			}
		}

		public void Release() {
			if (TryReap()) {
				return;
			}
			// Still running (it ignored the closed pipe and the kill has not landed yet): reap it whenever it does exit,
			// rather than leave a zombie for the life of a long-running MCP server.
			Thread reaper = new(() => {
				int result;
				do {
					result = Interop.WaitPid(pid, out _, 0);
				} while (result == -1 && Marshal.GetLastPInvokeError() == EIntr);
				Volatile.Write(ref _reaped, 1);
			}) { IsBackground = true, Name = "clio-browser-reaper" };
			reaper.Start();
		}

		private bool TryReap() {
			while (Volatile.Read(ref _reaped) == 0) {
				int result = Interop.WaitPid(pid, out _, WNoHang);
				if (result == 0) {
					return false;
				}
				if (result == -1 && Marshal.GetLastPInvokeError() == EIntr) {
					continue;
				}
				// Reaped now, or (ECHILD) by a runtime that reaps every child; either way there is nothing left to wait for.
				Volatile.Write(ref _reaped, 1);
			}
			return true;
		}
	}

	private static void Check(int result, string function) {
		if (result != 0) {
			throw new Win32Exception(result, $"{function} failed: {new Win32Exception(result).Message}");
		}
	}

	/// <summary>UTF-8 copies of strings in native memory, and a NULL-terminated array of pointers to them.</summary>
	private sealed class NativeStrings : IDisposable {

		internal NativeStrings(IEnumerable<string> values) {
			List<IntPtr> pointers = [];
			foreach (string value in values) {
				pointers.Add(Marshal.StringToCoTaskMemUTF8(value));
			}
			Pointers = pointers;
			Array = Marshal.AllocHGlobal(IntPtr.Size * (pointers.Count + 1));
			for (int index = 0; index < pointers.Count; index++) {
				Marshal.WriteIntPtr(Array, index * IntPtr.Size, pointers[index]);
			}
			Marshal.WriteIntPtr(Array, pointers.Count * IntPtr.Size, IntPtr.Zero);
		}

		internal IReadOnlyList<IntPtr> Pointers { get; }

		internal IntPtr Array { get; }

		public void Dispose() {
			Marshal.FreeHGlobal(Array);
			foreach (IntPtr pointer in Pointers) {
				Marshal.FreeCoTaskMem(pointer);
			}
		}
	}

	private static class Interop {

		// None of these is variadic, so every argument is passed exactly as declared on every ABI (see the variadic
		// open/openat note in UnixConfinedFileAccess). mode_t is 16-bit on Darwin and 32-bit on Linux; 0 fits both.
		[DllImport("libc", EntryPoint = "posix_spawn")]
		internal static extern int PosixSpawn(out int pid, IntPtr path, IntPtr fileActions, IntPtr attributes,
			IntPtr argv, IntPtr envp);

		[DllImport("libc", EntryPoint = "posix_spawn_file_actions_init")]
		internal static extern int FileActionsInit(IntPtr fileActions);

		[DllImport("libc", EntryPoint = "posix_spawn_file_actions_destroy")]
		internal static extern int FileActionsDestroy(IntPtr fileActions);

		[DllImport("libc", EntryPoint = "posix_spawn_file_actions_adddup2")]
		internal static extern int FileActionsAddDup2(IntPtr fileActions, int fd, int newFd);

		[DllImport("libc", EntryPoint = "posix_spawn_file_actions_addclose")]
		internal static extern int FileActionsAddClose(IntPtr fileActions, int fd);

		[DllImport("libc", EntryPoint = "posix_spawn_file_actions_addopen")]
		internal static extern int FileActionsAddOpen(IntPtr fileActions, int fd, IntPtr path, int flags, int mode);

		[DllImport("libc", EntryPoint = "posix_spawn_file_actions_addclosefrom_np")]
		internal static extern int FileActionsAddCloseFrom(IntPtr fileActions, int lowestFd);

		[DllImport("libc", EntryPoint = "posix_spawnattr_init")]
		internal static extern int AttrInit(IntPtr attributes);

		[DllImport("libc", EntryPoint = "posix_spawnattr_destroy")]
		internal static extern int AttrDestroy(IntPtr attributes);

		[DllImport("libc", EntryPoint = "posix_spawnattr_setflags")]
		internal static extern int AttrSetFlags(IntPtr attributes, short flags);

		[DllImport("libc", EntryPoint = "kill", SetLastError = true)]
		internal static extern int Kill(int pid, int signal);

		[DllImport("libc", EntryPoint = "waitpid", SetLastError = true)]
		internal static extern int WaitPid(int pid, out int status, int options);
	}
}
