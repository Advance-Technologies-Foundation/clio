using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Clio.Common.McpWorker;

/// <summary>
/// Windows containment: the worker is created inside a job object whose limits include
/// <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c>, so the kernel destroys the whole subtree when the last
/// handle to the job closes — including when this parent is force-terminated and runs no cleanup.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this class creates the process itself instead of assigning one that <c>Process.Start</c>
/// already started.</b> It is a measurement, not a preference (ADR §2.4, Windows Server 2022). With
/// the child created running and assigned afterwards, the child WAS in the job, and a grandchild it
/// spawned before the assignment landed SURVIVED the parent's force-kill. With
/// <c>CREATE_SUSPENDED</c> → <c>AssignProcessToJobObject</c> → <c>ResumeThread</c>, the whole subtree
/// died. .NET's <c>Process.Start</c> cannot express <c>CREATE_SUSPENDED</c>, so process creation is
/// done here through <c>CreateProcessW</c>. "Start it, then assign it" is not an implementation
/// detail — it leaks.
/// </para>
/// <para>
/// <b>Why a job object and not a "process group".</b> A Windows process group
/// (<c>GenerateConsoleCtrlEvent</c>) is console-signal routing: an uncooperative child simply ignores
/// it. Only job membership is kernel-enforced and inherited by descendants, which is what ADR rule 6
/// asks for.
/// </para>
/// <para>
/// <b>Verification status.</b> The sequence implemented here is the one measured green in ADR §2.4;
/// this code path cannot be executed on macOS or Linux, so its end-to-end test declares a Windows
/// requirement and skips elsewhere with an explicit reason rather than passing silently. Requirement
/// R-8b closes on a Windows run, not on a green Unix suite.
/// </para>
/// </remarks>
public sealed class WindowsJobObjectContainment : IProcessContainment {

	private const uint CreateSuspended = 0x00000004;
	private const uint CreateUnicodeEnvironment = 0x00000400;
	private const uint CreateNoWindow = 0x08000000;
	private const uint StartFlagUseStdHandles = 0x00000100;
	private const int ExtendedLimitInformationClass = 9;
	private const uint LimitKillOnJobClose = 0x00002000;
	private const uint WaitObjectZero = 0x00000000;
	private const uint WaitTimeout = 0x00000102;
	private const int WorkerTerminationExitCode = 1;

	/// <inheritdoc />
	/// <remarks>
	/// Always <see langword="true"/>: the job assignment has to precede the child's first instruction,
	/// which only a suspended creation can guarantee.
	/// </remarks>
	public bool OwnsProcessCreation => true;

	/// <inheritdoc />
	public IContainedWorker Launch(WorkerLaunchRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		// Explicit runtime guard: CA1416 is suppressed repository-wide, so the platform attributes give
		// no compile-time protection here.
		if (!OperatingSystem.IsWindows()) {
			throw new PlatformNotSupportedException(
				"Job object containment is available on Windows only.");
		}
		return WindowsJobContainedWorker.Create(request);
	}

	/// <inheritdoc />
	public IContainedWorker Adopt(IWorkerProcessHandle startedProcess) {
		throw new NotSupportedException(
			"Windows containment must create the worker itself: assigning an already running process to the job leaves a window in which a grandchild escapes it (ADR section 2.4).");
	}

	/// <inheritdoc />
	/// <remarks>
	/// An orphan cannot normally exist on Windows — the job dies with its last handle, so a dead parent
	/// takes the subtree with it. This is the backstop for a worker recorded before its assignment
	/// completed, and it is a best-effort tree kill because the job handle died with the parent that
	/// owned it.
	/// </remarks>
	public WorkerTerminationOutcome TerminateOrphan(IWorkerProcessHandle orphan) {
		ArgumentNullException.ThrowIfNull(orphan);
		if (orphan.HasExited) {
			return WorkerTerminationOutcome.AlreadyExited;
		}
		try {
			orphan.KillProcessTree();
			return WorkerTerminationOutcome.FallbackTreeKilled;
		} catch (InvalidOperationException) {
			return WorkerTerminationOutcome.AlreadyExited;
		} catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
				or NotSupportedException or AggregateException) {
			return WorkerTerminationOutcome.Failed;
		}
	}

	/// <summary>A worker created suspended, assigned to a kill-on-close job, then resumed.</summary>
	private sealed class WindowsJobContainedWorker : IContainedWorker {

		private readonly SafeKernelHandle _jobHandle;
		private readonly SafeKernelHandle _processHandle;
		private readonly AnonymousPipeServerStream _standardInput;
		private readonly AnonymousPipeServerStream _standardOutput;
		private readonly AnonymousPipeServerStream _standardError;
		private int _disposed;

		private WindowsJobContainedWorker(SafeKernelHandle jobHandle, SafeKernelHandle processHandle,
			int processId, DateTime startTimeUtc, string executablePath,
			AnonymousPipeServerStream standardInput, AnonymousPipeServerStream standardOutput,
			AnonymousPipeServerStream standardError) {
			_jobHandle = jobHandle;
			_processHandle = processHandle;
			ProcessId = processId;
			StartTimeUtc = startTimeUtc;
			ExecutablePath = executablePath;
			_standardInput = standardInput;
			_standardOutput = standardOutput;
			_standardError = standardError;
		}

		public int ProcessId { get; }

		public DateTime StartTimeUtc { get; }

		public string ExecutablePath { get; }

		public Stream StandardInput => _standardInput;

		public Stream StandardOutput => _standardOutput;

		public Stream StandardError => _standardError;

		// Asked of the process HANDLE rather than of the exit code, because a process that legitimately
		// exits with 259 is indistinguishable from a running one by GetExitCodeProcess: 259 IS
		// STILL_ACTIVE. The handle's signalled state has no such ambiguity.
		public bool HasExited => NativeMethods.WaitForSingleObject(_processHandle, 0) == WaitObjectZero;

		public int? ExitCode => HasExited ? TryReadExitCode() : null;

		public static WindowsJobContainedWorker Create(WorkerLaunchRequest request) {
			// Server ends stay private to this process; only the client ends are inheritable, so the
			// child receives exactly three handles and no more.
			AnonymousPipeServerStream input = new(PipeDirection.Out, HandleInheritability.Inheritable);
			AnonymousPipeServerStream output = new(PipeDirection.In, HandleInheritability.Inheritable);
			AnonymousPipeServerStream error = new(PipeDirection.In, HandleInheritability.Inheritable);
			SafeKernelHandle job = null;
			SafeKernelHandle processHandle = null;
			IntPtr environmentBlock = IntPtr.Zero;
			ProcessInformation processInformation = default;
			try {
				job = CreateKillOnCloseJob();
				StartupInformation startupInformation = new() {
					cb = Marshal.SizeOf<StartupInformation>(),
					dwFlags = StartFlagUseStdHandles,
					hStdInput = input.ClientSafePipeHandle.DangerousGetHandle(),
					hStdOutput = output.ClientSafePipeHandle.DangerousGetHandle(),
					hStdError = error.ClientSafePipeHandle.DangerousGetHandle()
				};
				environmentBlock = BuildEnvironmentBlock(request);
				string commandLine = WindowsCommandLine.Build(request.Executable, request.Arguments);

				bool created = NativeMethods.CreateProcessW(
					lpApplicationName: request.Executable,
					lpCommandLine: commandLine,
					lpProcessAttributes: IntPtr.Zero,
					lpThreadAttributes: IntPtr.Zero,
					bInheritHandles: true,
					dwCreationFlags: CreateSuspended | CreateUnicodeEnvironment | CreateNoWindow,
					lpEnvironment: environmentBlock,
					lpCurrentDirectory: request.WorkingDirectory,
					lpStartupInfo: ref startupInformation,
					lpProcessInformation: out processInformation);
				if (!created) {
					throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),
						$"Unable to create the worker process '{request.Executable}'.");
				}
				// Owned from here on, so every failure path below closes it exactly once.
				processHandle = new SafeKernelHandle(processInformation.hProcess);

				// The whole point of the suspended creation: the child is in the job before it executes
				// its first instruction, so nothing it spawns can be outside the job.
				if (!NativeMethods.AssignProcessToJobObject(job, processHandle)) {
					int assignError = Marshal.GetLastWin32Error();
					NativeMethods.TerminateProcess(processHandle, WorkerTerminationExitCode);
					throw new System.ComponentModel.Win32Exception(assignError,
						"Unable to assign the suspended worker process to its job object.");
				}
				if (NativeMethods.ResumeThread(processInformation.hThread) == unchecked((uint)-1)) {
					int resumeError = Marshal.GetLastWin32Error();
					NativeMethods.TerminateJobObject(job, WorkerTerminationExitCode);
					throw new System.ComponentModel.Win32Exception(resumeError,
						"Unable to resume the contained worker process.");
				}

				DateTime startTimeUtc = ReadStartTimeUtc(processHandle);
				input.DisposeLocalCopyOfClientHandle();
				output.DisposeLocalCopyOfClientHandle();
				error.DisposeLocalCopyOfClientHandle();
				return new WindowsJobContainedWorker(job, processHandle, processInformation.dwProcessId,
					startTimeUtc, request.Executable, input, output, error);
			} catch {
				processHandle?.Dispose();
				job?.Dispose();
				input.Dispose();
				output.Dispose();
				error.Dispose();
				throw;
			} finally {
				if (processInformation.hThread != IntPtr.Zero) {
					NativeMethods.CloseHandle(processInformation.hThread);
				}
				if (environmentBlock != IntPtr.Zero) {
					Marshal.FreeHGlobal(environmentBlock);
				}
			}
		}

		public Task WaitForExitAsync(CancellationToken cancellationToken) {
			// Offloaded, and waited in short slices rather than one unbounded blocking wait, so that
			// cancellation is observed promptly and no caller thread is parked inside a native call.
			return Task.Run(() => {
				while (!cancellationToken.IsCancellationRequested) {
					uint waitResult = NativeMethods.WaitForSingleObject(_processHandle, 50);
					if (waitResult == WaitObjectZero) {
						return;
					}
					if (waitResult != WaitTimeout) {
						throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),
							"Waiting for the contained worker process failed.");
					}
				}
				cancellationToken.ThrowIfCancellationRequested();
			}, CancellationToken.None);
		}

		public WorkerTerminationOutcome Kill() {
			if (HasExited) {
				return WorkerTerminationOutcome.AlreadyExited;
			}
			return NativeMethods.TerminateJobObject(_jobHandle, WorkerTerminationExitCode)
				? WorkerTerminationOutcome.ContainedJobTerminated
				: WorkerTerminationOutcome.Failed;
		}

		public void Dispose() {
			if (Interlocked.Exchange(ref _disposed, 1) != 0) {
				return;
			}
			_standardInput.Dispose();
			_standardOutput.Dispose();
			_standardError.Dispose();
			// Closing the last job handle is itself lethal to the subtree: that is the kill-on-close
			// guarantee, and it is why a force-killed parent leaves nothing behind.
			_jobHandle.Dispose();
			_processHandle.Dispose();
		}

		private int? TryReadExitCode() {
			return NativeMethods.GetExitCodeProcess(_processHandle, out uint exitCode)
				? unchecked((int)exitCode)
				: null;
		}

		private static SafeKernelHandle CreateKillOnCloseJob() {
			IntPtr rawJob = NativeMethods.CreateJobObjectW(IntPtr.Zero, null);
			if (rawJob == IntPtr.Zero) {
				throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),
					"Unable to create the worker job object.");
			}
			SafeKernelHandle job = new(rawJob);
			JobObjectExtendedLimitInformation limits = new() {
				BasicLimitInformation = new JobObjectBasicLimitInformation {
					LimitFlags = LimitKillOnJobClose
				}
			};
			int size = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
			IntPtr buffer = Marshal.AllocHGlobal(size);
			try {
				Marshal.StructureToPtr(limits, buffer, false);
				if (!NativeMethods.SetInformationJobObject(job, ExtendedLimitInformationClass, buffer, (uint)size)) {
					throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),
						"Unable to set kill-on-close on the worker job object.");
				}
			} catch {
				job.Dispose();
				throw;
			} finally {
				Marshal.FreeHGlobal(buffer);
			}
			return job;
		}

		private static DateTime ReadStartTimeUtc(SafeKernelHandle processHandle) {
			if (NativeMethods.GetProcessTimes(processHandle, out long creation, out long _, out long _,
					out long _)) {
				try {
					return DateTime.FromFileTimeUtc(creation);
				} catch (ArgumentOutOfRangeException) {
					return DateTime.UtcNow;
				}
			}
			return DateTime.UtcNow;
		}

		private static IntPtr BuildEnvironmentBlock(WorkerLaunchRequest request) {
			Dictionary<string, string> variables = new(StringComparer.OrdinalIgnoreCase);
			if (!request.ClearInheritedEnvironment) {
				foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables()) {
					if (entry.Key is string key && entry.Value is string value) {
						variables[key] = value;
					}
				}
			}
			if (request.Environment is not null) {
				foreach (KeyValuePair<string, string> pair in request.Environment) {
					variables[pair.Key] = pair.Value;
				}
			}
			StringBuilder builder = new();
			foreach (KeyValuePair<string, string> pair in variables) {
				builder.Append(pair.Key).Append('=').Append(pair.Value).Append('\0');
			}
			builder.Append('\0');
			return Marshal.StringToHGlobalUni(builder.ToString());
		}
	}
}

/// <summary>
/// Builds a Windows command line from an argument vector.
/// </summary>
/// <remarks>
/// <c>CreateProcessW</c> takes one string, and the child's runtime splits it back into arguments using
/// the C runtime rules. Joining with spaces therefore corrupts any argument containing a space,
/// a quote, or a trailing backslash — which includes ordinary Windows paths. This is a pure function
/// precisely so it can be unit tested on every platform, including the ones that cannot run the rest
/// of the Windows containment path.
/// </remarks>
internal static class WindowsCommandLine {

	/// <summary>Builds the command line for an executable and its arguments.</summary>
	/// <param name="executable">Executable path; becomes argument zero.</param>
	/// <param name="arguments">Argument vector.</param>
	/// <returns>A command line the C runtime splits back into the same vector.</returns>
	internal static string Build(string executable, IReadOnlyList<string> arguments) {
		StringBuilder builder = new();
		AppendArgument(builder, executable);
		foreach (string argument in arguments ?? Array.Empty<string>()) {
			builder.Append(' ');
			AppendArgument(builder, argument ?? string.Empty);
		}
		return builder.ToString();
	}

	private static void AppendArgument(StringBuilder builder, string argument) {
		if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '"']) < 0) {
			builder.Append(argument);
			return;
		}
		builder.Append('"');
		int index = 0;
		while (index < argument.Length) {
			int backslashes = 0;
			while (index < argument.Length && argument[index] == '\\') {
				backslashes++;
				index++;
			}
			if (index == argument.Length) {
				// Trailing backslashes are doubled so the closing quote is not escaped by them.
				builder.Append('\\', backslashes * 2);
				break;
			}
			if (argument[index] == '"') {
				builder.Append('\\', backslashes * 2 + 1).Append('"');
			} else {
				builder.Append('\\', backslashes).Append(argument[index]);
			}
			index++;
		}
		builder.Append('"');
	}
}

/// <summary>A kernel handle closed with <c>CloseHandle</c>.</summary>
internal sealed class SafeKernelHandle : SafeHandleZeroOrMinusOneIsInvalid {

	/// <summary>Initializes a new instance of the <see cref="SafeKernelHandle"/> class.</summary>
	/// <param name="handle">The raw handle to own.</param>
	internal SafeKernelHandle(IntPtr handle) : base(true) {
		SetHandle(handle);
	}

	/// <inheritdoc />
	protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle);
}

[StructLayout(LayoutKind.Sequential)]
internal struct ProcessInformation {
	internal IntPtr hProcess;
	internal IntPtr hThread;
	internal int dwProcessId;
	internal int dwThreadId;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct StartupInformation {
	internal int cb;
	internal IntPtr lpReserved;
	internal IntPtr lpDesktop;
	internal IntPtr lpTitle;
	internal int dwX;
	internal int dwY;
	internal int dwXSize;
	internal int dwYSize;
	internal int dwXCountChars;
	internal int dwYCountChars;
	internal int dwFillAttribute;
	internal uint dwFlags;
	internal short wShowWindow;
	internal short cbReserved2;
	internal IntPtr lpReserved2;
	internal IntPtr hStdInput;
	internal IntPtr hStdOutput;
	internal IntPtr hStdError;
}

[StructLayout(LayoutKind.Sequential)]
internal struct JobObjectBasicLimitInformation {
	internal long PerProcessUserTimeLimit;
	internal long PerJobUserTimeLimit;
	internal uint LimitFlags;
	internal UIntPtr MinimumWorkingSetSize;
	internal UIntPtr MaximumWorkingSetSize;
	internal uint ActiveProcessLimit;
	internal UIntPtr Affinity;
	internal uint PriorityClass;
	internal uint SchedulingClass;
}

[StructLayout(LayoutKind.Sequential)]
internal struct IoCounters {
	internal ulong ReadOperationCount;
	internal ulong WriteOperationCount;
	internal ulong OtherOperationCount;
	internal ulong ReadTransferCount;
	internal ulong WriteTransferCount;
	internal ulong OtherTransferCount;
}

[StructLayout(LayoutKind.Sequential)]
internal struct JobObjectExtendedLimitInformation {
	internal JobObjectBasicLimitInformation BasicLimitInformation;
	internal IoCounters IoInfo;
	internal UIntPtr ProcessMemoryLimit;
	internal UIntPtr JobMemoryLimit;
	internal UIntPtr PeakProcessMemoryUsed;
	internal UIntPtr PeakJobMemoryUsed;
}

/// <summary>
/// The Windows entry points job-object containment needs. Written in the classic <c>DllImport</c> style
/// used elsewhere in clio core, deliberately not the source-generated <c>LibraryImport</c> style, which
/// this project's <c>net8.0</c> floor and analyzer configuration do not require.
/// </summary>
internal static class NativeMethods {

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static extern bool CreateProcessW(string lpApplicationName, string lpCommandLine,
		IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
		[MarshalAs(UnmanagedType.Bool)] bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment,
		string lpCurrentDirectory, ref StartupInformation lpStartupInfo,
		out ProcessInformation lpProcessInformation);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern IntPtr CreateJobObjectW(IntPtr lpJobAttributes, string lpName);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static extern bool SetInformationJobObject(SafeKernelHandle hJob,
		int jobObjectInformationClass, IntPtr lpJobObjectInformation, uint cbJobObjectInformationLength);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static extern bool AssignProcessToJobObject(SafeKernelHandle hJob, SafeKernelHandle hProcess);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static extern bool TerminateJobObject(SafeKernelHandle hJob, uint uExitCode);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static extern bool TerminateProcess(SafeKernelHandle hProcess, uint uExitCode);

	[DllImport("kernel32.dll", SetLastError = true)]
	internal static extern uint ResumeThread(IntPtr hThread);

	[DllImport("kernel32.dll", SetLastError = true)]
	internal static extern uint WaitForSingleObject(SafeKernelHandle hHandle, uint dwMilliseconds);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static extern bool GetExitCodeProcess(SafeKernelHandle hProcess, out uint lpExitCode);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static extern bool GetProcessTimes(SafeKernelHandle hProcess, out long lpCreationTime,
		out long lpExitTime, out long lpKernelTime, out long lpUserTime);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static extern bool CloseHandle(IntPtr hObject);
}
