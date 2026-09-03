using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Pipes;
using System.Linq;
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
/// <b>Why the child inherits exactly three handles and not "whatever was open".</b>
/// <c>bInheritHandles: true</c> on its own hands the child EVERY inheritable handle the parent holds at
/// that instant — not only the three named in <c>STARTUPINFO</c>. With workers launched concurrently that
/// means one child keeps a SIBLING's stdout/stderr pipe WRITE end alive. The relay treats the pipe
/// closing as the completion signal (<c>RunReadLoopAsync</c> ends on EOF and fails the pending calls), so
/// a retained write end means the sibling's reader never sees EOF and the parent waits on a worker that
/// is already dead until an unrelated child happens to exit — a hang whose cause points at the wrong
/// process. The fix is <c>STARTUPINFOEX</c> with a <c>PROC_THREAD_ATTRIBUTE_HANDLE_LIST</c> naming the
/// three pipe client handles: <c>bInheritHandles</c> stays <see langword="true"/> (the list only applies
/// when it is), and the list narrows the inheritance to exactly that set.
/// </para>
/// <para>
/// <b>Why a failed attribute list fails the spawn instead of falling back.</b> Falling back to
/// unrestricted inheritance would silently reintroduce the cross-worker retention above, which is the
/// hang class this whole feature exists to remove — and it would do so on the rare path nobody is
/// watching. <c>InitializeProcThreadAttributeList</c> has shipped in every supported Windows version, so
/// the only realistic trigger is an allocation failure, and a loud, correctly attributed spawn error is
/// strictly better than a wait that blames an unrelated process. The decision is therefore: no fallback.
/// </para>
/// <para>
/// <b>Verification status.</b> The sequence implemented here is the one measured green in ADR §2.4;
/// this code path cannot be executed on macOS or Linux, so its end-to-end test declares a Windows
/// requirement and skips elsewhere with an explicit reason rather than passing silently. Requirement
/// R-8b closes on a Windows run, not on a green Unix suite. The handle-list narrowing is in the same
/// position: its composition is asserted everywhere (see <see cref="WindowsWorkerStartup"/> and the
/// substituted attribute-list lifecycle), but that the kernel actually withholds the sibling handles is
/// a Windows-only observation.
/// </para>
/// </remarks>
public sealed class WindowsJobObjectContainment : IProcessContainment {

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

		[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters",
			Justification = "Every parameter is a handle or identity fact the OS gave back when the job/process " +
				"pair was created (job handle, process handle, process id, start time, executable path) plus " +
				"the three standard-stream pipe ends the caller already opened; grouping them into a DTO " +
				"would just move the same eight OS-owned values one level of indirection away from this " +
				"private construction seam.")]
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
			// Server ends stay private to this process; only the client ends are inheritable. That is
			// necessary but NOT sufficient on its own: at this instant the parent may also hold another
			// concurrently launching worker's inheritable client handles, and bInheritHandles would hand
			// this child those too. The PROC_THREAD_ATTRIBUTE_HANDLE_LIST built below is what makes
			// "exactly three handles and no more" actually true.
			AnonymousPipeServerStream input = new(PipeDirection.Out, HandleInheritability.Inheritable);
			AnonymousPipeServerStream output = new(PipeDirection.In, HandleInheritability.Inheritable);
			AnonymousPipeServerStream error = new(PipeDirection.In, HandleInheritability.Inheritable);
			SafeKernelHandle job = null;
			SafeKernelHandle processHandle = null;
			IntPtr environmentBlock = IntPtr.Zero;
			ProcessInformation processInformation = default;
			try {
				job = CreateKillOnCloseJob();
				// Sonar S3869 is suppressed for exactly these three reads: STARTUPINFO is a native struct whose
				// hStdInput/hStdOutput/hStdError fields ARE raw HANDLEs, so CreateProcessW cannot be given a
				// SafeHandle here, and the inherited-handle list is likewise an array of raw HANDLEs. The
				// lifetime is not dangerous in practice: the three AnonymousPipeServerStream instances are
				// locals owned by this method and their client handles are only released by
				// DisposeLocalCopyOfClientHandle AFTER the process has been created, so no handle can be
				// closed while CreateProcessW is reading the struct or the attribute list.
#pragma warning disable S3869
				IntPtr standardInputHandle = input.ClientSafePipeHandle.DangerousGetHandle();
				IntPtr standardOutputHandle = output.ClientSafePipeHandle.DangerousGetHandle();
				IntPtr standardErrorHandle = error.ClientSafePipeHandle.DangerousGetHandle();
#pragma warning restore S3869
				environmentBlock = BuildEnvironmentBlock(request);
				string commandLine = WindowsCommandLine.Build(request.Executable, request.Arguments);

				// Every handle named here is inheritable because all three pipes were created with
				// HandleInheritability.Inheritable above. That is a hard dependency, not a nicety: a handle in
				// the list that is NOT inheritable, or a handle named in STARTUPINFO that is missing from the
				// list, makes CreateProcessW fail with ERROR_INVALID_PARAMETER (87).
				IntPtr[] inheritedHandles = WindowsWorkerStartup.BuildInheritedHandleList(
					standardInputHandle, standardOutputHandle, standardErrorHandle);
				bool created;
				int createError;
				using (ProcThreadAttributeList attributeList =
					ProcThreadAttributeList.CreateForInheritedHandles(inheritedHandles)) {
					StartupInformationEx startupInformation = WindowsWorkerStartup.BuildStartupInformation(
						attributeList.Handle, standardInputHandle, standardOutputHandle, standardErrorHandle);

					// bInheritHandles stays true ON PURPOSE: the handle list narrows inheritance only while
					// inheritance is enabled at all. With it false the child would receive nothing, including
					// its own three pipes.
					created = NativeMethods.CreateProcessW(
						lpApplicationName: request.Executable,
						lpCommandLine: commandLine,
						lpProcessAttributes: IntPtr.Zero,
						lpThreadAttributes: IntPtr.Zero,
						bInheritHandles: true,
						dwCreationFlags: WindowsWorkerStartup.CreationFlags,
						lpEnvironment: environmentBlock,
						lpCurrentDirectory: request.WorkingDirectory,
						lpStartupInfo: ref startupInformation,
						lpProcessInformation: out processInformation);
					// Read before the attribute list is disposed: its teardown issues native calls of its own,
					// which would overwrite the thread's last-error value.
					createError = Marshal.GetLastWin32Error();
				}
				if (!created) {
					throw new System.ComponentModel.Win32Exception(createError,
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
			// SORTED, case-insensitively by name, before the block is marshalled. A Dictionary preserves no
			// order worth relying on — here it would emit the inherited allowlist first and the appended
			// CLIO_* delta last — and Windows documents the environment block as sorted. This is not a
			// speculative reading of the contract: .NET's own System.Diagnostics.Process builds its block
			// with exactly this comparer, so a worker launched through this containment path now gets the
			// same ordering as one launched through ProcessStartInfo, which is the path every other clio
			// process takes. Matching the platform costs one sort and removes a difference nobody would
			// think to look for when a child's environment lookup misbehaves.
			StringBuilder builder = new();
			foreach (KeyValuePair<string, string> pair in variables.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)) {
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

/// <summary>
/// Pure composition of what <c>CreateProcessW</c> is told about a worker: the creation flags, the
/// extended startup structure, and the exact set of handles the child is allowed to inherit.
/// </summary>
/// <remarks>
/// Separated from <see cref="WindowsJobObjectContainment"/> precisely so it can be asserted on macOS and
/// Linux, where not one line of the surrounding native path can execute. Every constant carries the
/// header expression it comes from so a reviewer can re-derive it rather than trust it.
/// </remarks>
internal static class WindowsWorkerStartup {

	/// <summary><c>CREATE_SUSPENDED</c> — the child is in the job before its first instruction (ADR §2.4).</summary>
	internal const uint CreateSuspended = 0x00000004;

	/// <summary><c>CREATE_UNICODE_ENVIRONMENT</c> — the environment block built here is UTF-16.</summary>
	internal const uint CreateUnicodeEnvironment = 0x00000400;

	/// <summary><c>CREATE_NO_WINDOW</c> — a worker is a console application with no console.</summary>
	internal const uint CreateNoWindow = 0x08000000;

	/// <summary><c>EXTENDED_STARTUPINFO_PRESENT</c> — <c>lpStartupInfo</c> is a <c>STARTUPINFOEX</c>.</summary>
	internal const uint ExtendedStartupInfoPresent = 0x00080000;

	/// <summary><c>STARTF_USESTDHANDLES</c> — the three <c>hStd*</c> fields are meaningful.</summary>
	internal const uint StartFlagUseStdHandles = 0x00000100;

	/// <summary>
	/// <c>PROC_THREAD_ATTRIBUTE_HANDLE_LIST</c>, that is
	/// <c>ProcThreadAttributeValue(ProcThreadAttributeHandleList = 2, thread: FALSE, input: TRUE,
	/// additive: FALSE)</c> = <c>2 | PROC_THREAD_ATTRIBUTE_INPUT (0x00020000)</c>.
	/// </summary>
	internal const int ProcThreadAttributeHandleList = 0x00020002;

	/// <summary>The creation flags every worker is created with.</summary>
	/// <remarks>
	/// <c>CREATE_SUSPENDED</c> is load-bearing for containment (ADR §2.4) and
	/// <c>EXTENDED_STARTUPINFO_PRESENT</c> is load-bearing for handle narrowing; dropping either while
	/// editing the other is the realistic regression, which is why a test asserts both bits.
	/// </remarks>
	internal const uint CreationFlags =
		CreateSuspended | CreateUnicodeEnvironment | CreateNoWindow | ExtendedStartupInfoPresent;

	private static readonly IntPtr InvalidHandleValue = new(-1);

	/// <summary>
	/// Builds the set of handles the child is permitted to inherit — the three standard streams and
	/// nothing else.
	/// </summary>
	/// <param name="standardInput">Inheritable client handle of the child's standard input pipe.</param>
	/// <param name="standardOutput">Inheritable client handle of the child's standard output pipe.</param>
	/// <param name="standardError">Inheritable client handle of the child's standard error pipe.</param>
	/// <returns>Distinct handles in standard-stream order.</returns>
	/// <remarks>
	/// A duplicate is collapsed because one list entry covers however many <c>STARTUPINFO</c> slots point
	/// at it, while a DISTINCT handle is never dropped: a handle named in <c>STARTUPINFO</c> but absent
	/// from the list fails the spawn with <c>ERROR_INVALID_PARAMETER</c>.
	/// </remarks>
	/// <exception cref="ArgumentException">A handle is null or <c>INVALID_HANDLE_VALUE</c>.</exception>
	internal static IntPtr[] BuildInheritedHandleList(IntPtr standardInput, IntPtr standardOutput,
		IntPtr standardError) {
		List<IntPtr> handles = new(3);
		AppendHandle(handles, standardInput, "standard input");
		AppendHandle(handles, standardOutput, "standard output");
		AppendHandle(handles, standardError, "standard error");
		return handles.ToArray();
	}

	/// <summary>Builds the extended startup structure for a worker.</summary>
	/// <param name="attributeList">The initialized <c>PROC_THREAD_ATTRIBUTE_LIST</c>.</param>
	/// <param name="standardInput">Handle placed in <c>hStdInput</c>.</param>
	/// <param name="standardOutput">Handle placed in <c>hStdOutput</c>.</param>
	/// <param name="standardError">Handle placed in <c>hStdError</c>.</param>
	/// <returns>A <c>STARTUPINFOEX</c> ready to hand to <c>CreateProcessW</c>.</returns>
	internal static StartupInformationEx BuildStartupInformation(IntPtr attributeList, IntPtr standardInput,
		IntPtr standardOutput, IntPtr standardError) {
		return new StartupInformationEx {
			StartupInfo = new StartupInformation {
				// With EXTENDED_STARTUPINFO_PRESENT, cb describes the EXTENDED structure. Leaving it at
				// sizeof(STARTUPINFO) fails EVERY spawn with ERROR_INVALID_PARAMETER (87).
				cb = Marshal.SizeOf<StartupInformationEx>(),
				dwFlags = StartFlagUseStdHandles,
				hStdInput = standardInput,
				hStdOutput = standardOutput,
				hStdError = standardError
			},
			lpAttributeList = attributeList
		};
	}

	private static void AppendHandle(List<IntPtr> handles, IntPtr handle, string streamName) {
		if (handle == IntPtr.Zero || handle == InvalidHandleValue) {
			throw new ArgumentException(
				$"The worker's {streamName} handle is not a valid handle, so the child's inherited handle list cannot be built.",
				nameof(handle));
		}
		if (!handles.Contains(handle)) {
			handles.Add(handle);
		}
	}
}

/// <summary>
/// The native operations a <c>PROC_THREAD_ATTRIBUTE_LIST</c> needs, plus the unmanaged allocation that
/// holds it.
/// </summary>
/// <remarks>
/// A seam rather than direct calls to <c>kernel32</c>, because the property worth testing here is a
/// lifecycle — buffer sized by the deliberately failing first call, destroyed before it is freed, freed
/// on every failure path — and that lifecycle is identical on a host with no <c>kernel32</c> at all.
/// Deliberately NOT a DI service: it is private plumbing of one containment implementation and has no
/// consumer outside this file.
/// </remarks>
internal interface IProcThreadAttributeListNative {

	/// <summary>Gets the calling thread's last native error code.</summary>
	int LastError { get; }

	/// <summary>Wraps <c>InitializeProcThreadAttributeList</c>.</summary>
	/// <param name="attributeList">The buffer, or <see cref="IntPtr.Zero"/> to query the required size.</param>
	/// <param name="attributeCount">Number of attributes the list must hold.</param>
	/// <param name="size">In/out required size in bytes.</param>
	/// <returns><see langword="true"/> on success; the sizing call returns <see langword="false"/> by design.</returns>
	bool Initialize(IntPtr attributeList, int attributeCount, ref IntPtr size);

	/// <summary>Wraps <c>UpdateProcThreadAttribute</c>.</summary>
	/// <param name="attributeList">An initialized list.</param>
	/// <param name="attribute">The attribute identifier.</param>
	/// <param name="value">Pointer to the attribute value; it is stored, not copied.</param>
	/// <param name="valueSize">Size of the value in BYTES.</param>
	/// <returns><see langword="true"/> on success.</returns>
	bool Update(IntPtr attributeList, IntPtr attribute, IntPtr value, IntPtr valueSize);

	/// <summary>Wraps <c>DeleteProcThreadAttributeList</c>; must precede <see cref="Free"/>.</summary>
	/// <param name="attributeList">An initialized list.</param>
	void Delete(IntPtr attributeList);

	/// <summary>Allocates unmanaged memory for the list.</summary>
	/// <param name="byteCount">Size reported by the sizing call.</param>
	/// <returns>The allocated buffer.</returns>
	IntPtr Allocate(int byteCount);

	/// <summary>Releases memory obtained from <see cref="Allocate"/>.</summary>
	/// <param name="buffer">The buffer to release.</param>
	void Free(IntPtr buffer);
}

/// <summary>The production <see cref="IProcThreadAttributeListNative"/>, backed by <c>kernel32</c>.</summary>
internal sealed class Kernel32ProcThreadAttributeListNative : IProcThreadAttributeListNative {

	/// <summary>The single instance; the type is stateless.</summary>
	internal static readonly IProcThreadAttributeListNative Instance = new Kernel32ProcThreadAttributeListNative();

	private Kernel32ProcThreadAttributeListNative() { }

	/// <inheritdoc />
	public int LastError => Marshal.GetLastWin32Error();

	/// <inheritdoc />
	public bool Initialize(IntPtr attributeList, int attributeCount, ref IntPtr size) =>
		NativeMethods.InitializeProcThreadAttributeList(attributeList, attributeCount, 0, ref size);

	/// <inheritdoc />
	public bool Update(IntPtr attributeList, IntPtr attribute, IntPtr value, IntPtr valueSize) =>
		NativeMethods.UpdateProcThreadAttribute(attributeList, 0, attribute, value, valueSize, IntPtr.Zero,
			IntPtr.Zero);

	/// <inheritdoc />
	public void Delete(IntPtr attributeList) => NativeMethods.DeleteProcThreadAttributeList(attributeList);

	/// <inheritdoc />
	public IntPtr Allocate(int byteCount) => Marshal.AllocHGlobal(byteCount);

	/// <inheritdoc />
	public void Free(IntPtr buffer) => Marshal.FreeHGlobal(buffer);
}

/// <summary>
/// A <c>PROC_THREAD_ATTRIBUTE_LIST</c> carrying one <c>PROC_THREAD_ATTRIBUTE_HANDLE_LIST</c> attribute:
/// the exhaustive set of handles a child created with <c>bInheritHandles: true</c> may inherit.
/// </summary>
/// <remarks>
/// <para>
/// The lifecycle is unmanaged on three counts and each one has an ordering rule:
/// </para>
/// <list type="number">
/// <item><description>
/// The buffer is sized by a call that DELIBERATELY FAILS — <c>InitializeProcThreadAttributeList</c> with
/// a null list returns <see langword="false"/> and sets <c>ERROR_INSUFFICIENT_BUFFER</c> while reporting
/// the size through its out parameter. The reported size, not the boolean, is the success signal; do not
/// "fix" this by testing the return value.
/// </description></item>
/// <item><description>
/// <c>UpdateProcThreadAttribute</c> stores a POINTER to the handle array rather than copying it, so the
/// array is pinned and stays pinned until after the list is destroyed — which is strictly later than the
/// <c>CreateProcess</c> call that reads it.
/// </description></item>
/// <item><description>
/// Teardown is <c>Delete</c> then <c>Free</c>, never the reverse, and it runs on every path including an
/// exception thrown between the two initialization calls — where <c>Delete</c> must be SKIPPED, because
/// destroying a list that was never initialized is undefined.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class ProcThreadAttributeList : IDisposable {

	private const int AttributeCount = 1;

	private readonly IProcThreadAttributeListNative _native;
	private readonly int _handleCount;
	private GCHandle _pinnedHandles;
	private IntPtr _buffer;
	private bool _initialized;
	private int _disposed;

	private ProcThreadAttributeList(IProcThreadAttributeListNative native, IntPtr[] handles) {
		_native = native;
		_handleCount = handles.Length;
		_pinnedHandles = GCHandle.Alloc(handles, GCHandleType.Pinned);
	}

	/// <summary>Gets the initialized list, for <c>STARTUPINFOEX.lpAttributeList</c>.</summary>
	internal IntPtr Handle => _buffer;

	/// <summary>Gets a value indicating whether the handle array is still pinned.</summary>
	/// <remarks>Exists so a test can observe that the pin outlives the list and is then released.</remarks>
	internal bool HandlesPinned => _pinnedHandles.IsAllocated;

	/// <summary>Builds a list restricting inheritance to <paramref name="handles"/>.</summary>
	/// <param name="handles">The handles the child may inherit; each must already be inheritable.</param>
	/// <returns>An initialized list the caller owns and must dispose.</returns>
	internal static ProcThreadAttributeList CreateForInheritedHandles(IntPtr[] handles) =>
		CreateForInheritedHandles(handles, Kernel32ProcThreadAttributeListNative.Instance);

	/// <summary>Builds a list restricting inheritance to <paramref name="handles"/>.</summary>
	/// <param name="handles">The handles the child may inherit; each must already be inheritable.</param>
	/// <param name="native">The native operations to use; substituted in tests.</param>
	/// <returns>An initialized list the caller owns and must dispose.</returns>
	/// <exception cref="System.ComponentModel.Win32Exception">The list could not be built. The spawn is
	/// failed rather than retried without a handle list — see <see cref="WindowsJobObjectContainment"/>.</exception>
	internal static ProcThreadAttributeList CreateForInheritedHandles(IntPtr[] handles,
		IProcThreadAttributeListNative native) {
		ArgumentNullException.ThrowIfNull(handles);
		ArgumentNullException.ThrowIfNull(native);
		if (handles.Length == 0) {
			throw new ArgumentException(
				"A child created with handle inheritance needs at least one inheritable handle; an empty list would deny it its own standard streams.",
				nameof(handles));
		}
		ProcThreadAttributeList attributeList = new(native, handles);
		try {
			attributeList.Build();
		} catch {
			attributeList.Dispose();
			throw;
		}
		return attributeList;
	}

	/// <inheritdoc />
	public void Dispose() {
		if (Interlocked.Exchange(ref _disposed, 1) != 0) {
			return;
		}
		// Order is the whole point: destroy the list, then release the memory that held it, then release
		// the pin on the array the list pointed at.
		if (_initialized) {
			_native.Delete(_buffer);
			_initialized = false;
		}
		if (_buffer != IntPtr.Zero) {
			_native.Free(_buffer);
			_buffer = IntPtr.Zero;
		}
		if (_pinnedHandles.IsAllocated) {
			_pinnedHandles.Free();
		}
	}

	private void Build() {
		IntPtr requiredSize = IntPtr.Zero;
		// Expected to return false with ERROR_INSUFFICIENT_BUFFER: this call exists to report the size.
		_native.Initialize(IntPtr.Zero, AttributeCount, ref requiredSize);
		long byteCount = requiredSize.ToInt64();
		if (byteCount <= 0) {
			throw new System.ComponentModel.Win32Exception(_native.LastError,
				"Unable to determine the size of the worker's inherited-handle attribute list.");
		}
		_buffer = _native.Allocate(checked((int)byteCount));
		if (!_native.Initialize(_buffer, AttributeCount, ref requiredSize)) {
			throw new System.ComponentModel.Win32Exception(_native.LastError,
				"Unable to initialize the worker's inherited-handle attribute list.");
		}
		_initialized = true;
		if (!_native.Update(_buffer, WindowsWorkerStartup.ProcThreadAttributeHandleList,
				_pinnedHandles.AddrOfPinnedObject(), (IntPtr)(_handleCount * IntPtr.Size))) {
			throw new System.ComponentModel.Win32Exception(_native.LastError,
				"Unable to restrict the worker's inherited handles to its own standard streams.");
		}
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

/// <summary>
/// <c>STARTUPINFOEX</c>: a <c>STARTUPINFO</c> followed by the attribute list pointer. Required whenever
/// <c>EXTENDED_STARTUPINFO_PRESENT</c> is passed, and the inner <c>cb</c> must describe THIS structure.
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct StartupInformationEx {
	internal StartupInformation StartupInfo;
	internal IntPtr lpAttributeList;
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
		string lpCurrentDirectory, ref StartupInformationEx lpStartupInfo,
		out ProcessInformation lpProcessInformation);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList,
		int dwAttributeCount, int dwFlags, ref IntPtr lpSize);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static extern bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags,
		IntPtr attribute, IntPtr lpValue, IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);

	[DllImport("kernel32.dll")]
	internal static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

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
