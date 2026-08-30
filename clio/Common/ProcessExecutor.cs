using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Common;

/// <summary>
/// Identifies the stream where a process output line was produced.
/// </summary>
public enum ProcessOutputStream {
	/// <summary>
	/// Standard output stream.
	/// </summary>
	StdOut,

	/// <summary>
	/// Standard error stream.
	/// </summary>
	StdErr
}

/// <summary>
/// Options that control process execution behavior.
/// </summary>
public sealed record ProcessExecutionOptions {
	/// <summary>
	/// Initializes a new instance of the <see cref="ProcessExecutionOptions"/> class.
	/// </summary>
	/// <param name="program">Executable name or path.</param>
	/// <param name="arguments">Command-line arguments.</param>
	public ProcessExecutionOptions(string program, string arguments) {
		Program = program;
		Arguments = arguments;
	}

	/// <summary>
	/// Gets the executable name or path.
	/// </summary>
	public string Program { get; init; }

	/// <summary>
	/// Gets the command-line arguments.
	/// </summary>
	public string Arguments { get; init; }

	/// <summary>
	/// Gets the optional working directory. Current directory is used when null.
	/// </summary>
	public string WorkingDirectory { get; init; }

	/// <summary>
	/// Gets a value indicating whether standard error lines should be suppressed from logger output.
	/// </summary>
	public bool SuppressErrors { get; init; }

	/// <summary>
	/// Gets a value indicating whether output should be mirrored to <see cref="ILogger"/>.
	/// </summary>
	public bool MirrorOutputToLogger { get; init; }

	/// <summary>
	/// Gets the optional execution timeout.
	/// </summary>
	public TimeSpan? Timeout { get; init; }

	/// <summary>
	/// Gets the cancellation token used to stop waiting for process completion.
	/// </summary>
	public CancellationToken CancellationToken { get; init; }

	/// <summary>
	/// Gets an optional callback invoked for each output line in real-time mode.
	/// </summary>
	public Action<string, ProcessOutputStream> OnOutput { get; init; }

	/// <summary>
	/// Gets optional text written to standard input immediately after process start.
	/// </summary>
	public string StandardInput { get; init; }

	/// <summary>
	/// Gets optional environment variables to add or override for the started process.
	/// </summary>
	public IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; }

	/// <summary>
	/// Gets a value indicating whether the inherited process environment must be cleared before
	/// applying <see cref="InheritedEnvironmentVariableAllowlist"/> and <see cref="EnvironmentVariables"/>.
	/// </summary>
	public bool ClearInheritedEnvironment { get; init; }

	/// <summary>
	/// Gets the names of ambient variables copied into a cleared child environment.
	/// Values are read from the current process immediately before launch.
	/// </summary>
	public IReadOnlyCollection<string> InheritedEnvironmentVariableAllowlist { get; init; } =
		Array.Empty<string>();

	/// <summary>
	/// Gets a value indicating whether a bare executable name must be resolved to an absolute file
	/// from rooted <c>PATH</c> entries before process launch.
	/// </summary>
	public bool ResolveProgramPath { get; init; }

	/// <summary>
	/// Gets the maximum number of characters retained across standard output and standard error.
	/// When the process produces more output, execution is terminated and reported as a resource-limit failure.
	/// </summary>
	public long? MaximumCapturedOutputCharacters { get; init; }

	/// <summary>
	/// Gets the optional directory whose aggregate file size is monitored while the process runs.
	/// </summary>
	public string MonitoredDirectory { get; init; }

	/// <summary>
	/// Gets the maximum aggregate size, in bytes, permitted under <see cref="MonitoredDirectory"/>.
	/// When the limit is exceeded, the process tree is terminated.
	/// </summary>
	public long? MaximumMonitoredDirectoryBytes { get; init; }

	/// <summary>
	/// Gets the interval used to poll <see cref="MonitoredDirectory"/> while the process runs.
	/// </summary>
	public TimeSpan? ResourceMonitorInterval { get; init; }
}

/// <summary>
/// Represents the outcome of an execution that can capture process output.
/// </summary>
public sealed record ProcessExecutionResult {
	/// <summary>
	/// Gets a value indicating whether the process was successfully started.
	/// </summary>
	public bool Started { get; init; }

	/// <summary>
	/// Gets the started process identifier when available.
	/// </summary>
	public int? ProcessId { get; init; }

	/// <summary>
	/// Gets the process exit code when available.
	/// </summary>
	public int? ExitCode { get; init; }

	/// <summary>
	/// Gets a value indicating whether execution was stopped due to timeout.
	/// </summary>
	public bool TimedOut { get; init; }

	/// <summary>
	/// Gets a value indicating whether execution was canceled.
	/// </summary>
	public bool Canceled { get; init; }

	/// <summary>
	/// Gets a value indicating whether execution was terminated because a configured resource limit was exceeded.
	/// </summary>
	public bool ResourceLimitExceeded { get; init; }

	/// <summary>
	/// Gets a value indicating that timeout, cancellation, or resource-limit cleanup disconnected redirected
	/// streams but could not guarantee that already reparented descendants were terminated.
	/// </summary>
	/// <remarks>
	/// The immediate process tree is terminated on a best-effort basis. Operating systems can reparent descendants
	/// after the immediate process exits, so callers must not interpret a stopped capture as proof that every
	/// independently running descendant has exited.
	/// </remarks>
	public bool DescendantTerminationUncertain { get; init; }

	/// <summary>
	/// Gets the captured standard output.
	/// </summary>
	public string StandardOutput { get; init; } = string.Empty;

	/// <summary>
	/// Gets the captured standard error.
	/// </summary>
	public string StandardError { get; init; } = string.Empty;

	/// <summary>
	/// Gets the UTC timestamp when execution started.
	/// </summary>
	public DateTimeOffset StartedAtUtc { get; init; }

	/// <summary>
	/// Gets the UTC timestamp when execution finished.
	/// </summary>
	public DateTimeOffset? FinishedAtUtc { get; init; }
}

/// <summary>
/// Represents the outcome of fire-and-forget process launch.
/// </summary>
public sealed record ProcessLaunchResult {
	/// <summary>
	/// Gets a value indicating whether the process was successfully started.
	/// </summary>
	public bool Started { get; init; }

	/// <summary>
	/// Gets the started process identifier when available.
	/// </summary>
	public int? ProcessId { get; init; }

	/// <summary>
	/// Gets the launch error message when process failed to start.
	/// </summary>
	public string ErrorMessage { get; init; }

	/// <summary>
	/// Gets the UTC timestamp when launch was attempted.
	/// </summary>
	public DateTimeOffset StartedAtUtc { get; init; }
}

/// <summary>
/// Provides process execution capabilities for CLI commands.
/// </summary>
public interface IProcessExecutor{
	#region Methods: Public

	/// <summary>
	/// Executes a process using a compatibility API.
	/// </summary>
	/// <param name="program">Executable name or path.</param>
	/// <param name="arguments">Command-line arguments.</param>
	/// <param name="waitForExit">If true, waits for completion and returns captured output.</param>
	/// <param name="workingDirectory">Optional working directory. Current directory is used when null.</param>
	/// <param name="showOutput">If true, output is streamed to logger in real time.</param>
	/// <param name="suppressErrors">If true, standard error lines are not logged in real time mode.</param>
	/// <returns>Combined standard output and standard error text for blocking execution; empty string for fire-and-forget.</returns>
	string Execute(string program, string arguments, bool waitForExit, string workingDirectory = null,
		bool showOutput = false, bool suppressErrors = false);

	/// <summary>
	/// Starts a process without waiting for completion.
	/// </summary>
	/// <param name="options">Process execution options.</param>
	/// <returns>Launch result containing process id when start succeeds.</returns>
	Task<ProcessLaunchResult> FireAndForgetAsync(ProcessExecutionOptions options);

	/// <summary>
	/// Starts a process, waits for completion, and returns captured output.
	/// </summary>
	/// <param name="options">Process execution options.</param>
	/// <returns>Execution result with captured output and exit metadata.</returns>
	Task<ProcessExecutionResult> ExecuteAndCaptureAsync(ProcessExecutionOptions options);

	/// <summary>
	/// Starts a process, streams output in real time, and returns captured output.
	/// </summary>
	/// <param name="options">Process execution options.</param>
	/// <returns>Execution result with captured output and exit metadata.</returns>
	Task<ProcessExecutionResult> ExecuteWithRealtimeOutputAsync(ProcessExecutionOptions options);

	#endregion
}

/// <summary>
/// Default implementation of <see cref="IProcessExecutor"/>.
/// </summary>
public class ProcessExecutor(ILogger logger) : IProcessExecutor{
	private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

	#region Methods: Public

	/// <inheritdoc />
	public string Execute(string program, string arguments, bool waitForExit, string workingDirectory = null,
		bool showOutput = false, bool suppressErrors = false) {
		program.CheckArgumentNullOrWhiteSpace(nameof(program));
		arguments.CheckArgumentNullOrWhiteSpace(nameof(arguments));

		ProcessExecutionOptions options = new(program, arguments) {
			WorkingDirectory = workingDirectory,
			MirrorOutputToLogger = showOutput,
			SuppressErrors = suppressErrors
		};

		if (!waitForExit) {
			_ = FireAndForgetAsync(options).GetAwaiter().GetResult();
			return string.Empty;
		}

		ProcessExecutionResult result = showOutput
			? ExecuteWithRealtimeOutputAsync(options).GetAwaiter().GetResult()
			: ExecuteAndCaptureAsync(options).GetAwaiter().GetResult();

		return JoinOutputs(result.StandardOutput, result.StandardError);
	}

	/// <inheritdoc />
	public Task<ProcessLaunchResult> FireAndForgetAsync(ProcessExecutionOptions options) {
		ValidateOptions(options);
		DateTimeOffset startedAt = DateTimeOffset.UtcNow;

		try {
			using Process process = new();
			process.StartInfo = CreateStartInfo(options, redirectOutput: false);

			bool started = process.Start();
			if (started) {
				// Closed explicitly rather than left to Process.Dispose, which only closes an unread
				// StandardInput as an undocumented side effect of Close(). A detached child must reach EOF the
				// moment it is launched: it outlives this method, and until the write end is closed it is
				// holding a handle to whatever stdin this process has - the MCP server's JSON-RPC pipe included.
				TryCloseStandardInput(process);
			}
			return Task.FromResult(new ProcessLaunchResult {
				Started = started,
				ProcessId = started ? process.Id : null,
				StartedAtUtc = startedAt
			});
		}
		catch (Exception ex) {
			return Task.FromResult(new ProcessLaunchResult {
				Started = false,
				ErrorMessage = ex.Message,
				StartedAtUtc = startedAt
			});
		}
	}

	/// <inheritdoc />
	public Task<ProcessExecutionResult> ExecuteAndCaptureAsync(ProcessExecutionOptions options) {
		return ExecuteInternalAsync(options, enableRealtime: false);
	}

	/// <inheritdoc />
	public Task<ProcessExecutionResult> ExecuteWithRealtimeOutputAsync(ProcessExecutionOptions options) {
		return ExecuteInternalAsync(options, enableRealtime: true);
	}

	#endregion

	#region Methods: Private

	// internal so the redirection invariants below can be pinned directly. Behavioural tests cannot pin them:
	// whether an inherited stdin blocks depends on what the TEST HOST's own stdin happens to be, which is a
	// live pipe under an interactive console and already at EOF under most CI runners.
	internal static ProcessStartInfo CreateStartInfo(ProcessExecutionOptions options, bool redirectOutput) {
		string program = options.ResolveProgramPath
			? ResolveExecutablePath(options.Program)
			: options.Program;
		ProcessStartInfo startInfo = new() {
			FileName = program,
			Arguments = options.Arguments,
			CreateNoWindow = true,
			UseShellExecute = false,
			WorkingDirectory = options.WorkingDirectory ?? Environment.CurrentDirectory,
			// ALWAYS redirected, and NOT only when there is input to write. No child clio launches is
			// interactive on stdin, so none has any business inheriting ours - and when the parent is the MCP
			// server, "ours" is the JSON-RPC pipe: a child holding it can block on it and could in principle
			// consume protocol bytes. A detached fire-and-forget child is the worse case of the two, because it
			// holds the handle for the rest of the session rather than for one command. The stream is closed
			// immediately after start when no input was supplied, so the child sees EOF rather than a handle
			// that never closes. Note this redirects INPUT only: stdout/stderr still follow redirectOutput, so
			// a detached child can never block on an output pipe nobody drains.
			RedirectStandardInput = true,
			RedirectStandardOutput = redirectOutput,
			RedirectStandardError = redirectOutput
		};

		if (options.ClearInheritedEnvironment) {
			startInfo.Environment.Clear();
			foreach (string variableName in options.InheritedEnvironmentVariableAllowlist
					?? Array.Empty<string>()) {
				string value = Environment.GetEnvironmentVariable(variableName);
				if (value is not null) {
					startInfo.Environment[variableName] = value;
				}
			}
		}

		if (options.EnvironmentVariables is not null) {
			foreach ((string key, string value) in options.EnvironmentVariables) {
				startInfo.Environment[key] = value;
			}
		}

		return startInfo;
	}

	internal static string ResolveExecutablePath(string program) {
		program.CheckArgumentNullOrWhiteSpace(nameof(program));
		if (Path.IsPathFullyQualified(program)) {
			return ValidateExecutablePath(program)
				?? throw new FileNotFoundException($"Executable '{program}' was not found or is not executable.", program);
		}
		if (program.IndexOf(Path.DirectorySeparatorChar) >= 0
				|| program.IndexOf(Path.AltDirectorySeparatorChar) >= 0) {
			throw new ArgumentException("Executable resolution accepts only a bare name or an absolute path.",
				nameof(program));
		}

		string pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
		string[] executableNames = OperatingSystem.IsWindows() && !Path.HasExtension(program)
			? [$"{program}.exe"]
			: [program];
		foreach (string rawDirectory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)) {
			string resolved = ResolveFromPathDirectory(rawDirectory, executableNames);
			if (resolved is not null) {
				return resolved;
			}
		}
		throw new FileNotFoundException(
			$"Executable '{program}' was not found in any rooted PATH directory.",
			program);
	}

	private static string ResolveFromPathDirectory(string rawDirectory, string[] executableNames) {
		string directory = rawDirectory.Trim().Trim('"');
		// Relative PATH entries resolve against the current directory and would let a caller-controlled
		// working directory decide which executable runs, so they are never searched.
		if (!Path.IsPathFullyQualified(directory)) {
			return null;
		}
		foreach (string executableName in executableNames) {
			string candidate;
			try {
				candidate = Path.Combine(directory, executableName);
			} catch (ArgumentException) {
				continue;
			}
			string resolved = ValidateExecutablePath(candidate);
			if (resolved is not null) {
				return resolved;
			}
		}
		return null;
	}

	private static string ValidateExecutablePath(string candidate) {
		try {
			string fullPath = Path.GetFullPath(candidate);
			FileInfo executable = new(fullPath);
			if (!executable.Exists || (executable.Attributes & FileAttributes.Directory) != 0) {
				return null;
			}
			if (executable.LinkTarget is not null) {
				FileSystemInfo resolvedTarget = executable.ResolveLinkTarget(returnFinalTarget: true)
					?? throw new IOException($"Executable link '{fullPath}' could not be resolved.");
				if ((resolvedTarget.Attributes & FileAttributes.Directory) != 0) {
					return null;
				}
				fullPath = resolvedTarget.FullName;
			}
			if (!OperatingSystem.IsWindows()) {
				UnixFileMode mode = File.GetUnixFileMode(fullPath);
				UnixFileMode executableBits = UnixFileMode.UserExecute
					| UnixFileMode.GroupExecute
					| UnixFileMode.OtherExecute;
				if ((mode & executableBits) == 0) {
					return null;
				}
			}
			return fullPath;
		} catch (Exception exception) when (exception is ArgumentException
				or IOException
				or NotSupportedException
				or UnauthorizedAccessException) {
			return null;
		}
	}

	private async Task<ProcessExecutionResult> ExecuteInternalAsync(ProcessExecutionOptions options, bool enableRealtime) {
		ValidateOptions(options);

		StringBuilder stdout = new();
		StringBuilder stderr = new();
		DateTimeOffset startedAt = DateTimeOffset.UtcNow;
		OperationStopState stopState = new();
		ResourceLimitState resourceLimitState = new();
		using CancellationTokenSource timeoutCts = new();
		using CancellationTokenRegistration callerCancellationRegistration = options.CancellationToken.Register(
			() => stopState.TrySet(OperationStopReason.CallerCancellation));
		using CancellationTokenRegistration timeoutRegistration = timeoutCts.Token.Register(
			() => stopState.TrySet(OperationStopReason.Timeout));
		using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
			options.CancellationToken, timeoutCts.Token);
		if (options.Timeout is { } timeout && timeout > TimeSpan.Zero) {
			timeoutCts.CancelAfter(timeout);
		}
		bool canceled = false;
		bool timedOut = false;

		try {
			ProcessExecutionResult? preflightResult = ExecutePreflight(options, startedAt, resourceLimitState,
				stopState, linkedCts, ref canceled, ref timedOut);
			if (preflightResult is not null) {
				return preflightResult;
			}

			using Process process = new();
			process.StartInfo = CreateStartInfo(options, redirectOutput: true);
			process.EnableRaisingEvents = true;
			if (linkedCts.IsCancellationRequested) {
				ClassifyCancellation(options, resourceLimitState, stopState, ref canceled, ref timedOut);
				return StoppedBeforeStart(startedAt, canceled, timedOut);
			}

			bool started = process.Start();
			if (!started) {
				return new ProcessExecutionResult {
					Started = false,
					StartedAtUtc = startedAt,
					FinishedAtUtc = DateTimeOffset.UtcNow
				};
			}
			ProcessOperationContext operationContext = new(options, enableRealtime, resourceLimitState, stopState,
				process, linkedCts);
			Task stdoutTask = ReadStreamAsync(process.StandardOutput, ProcessOutputStream.StdOut, stdout,
				operationContext);
			Task stderrTask = ReadStreamAsync(process.StandardError, ProcessOutputStream.StdErr, stderr,
				operationContext);

			using CancellationTokenSource monitorCts = CancellationTokenSource.CreateLinkedTokenSource(linkedCts.Token);
			Task monitorTask = MonitorDirectoryAsync(operationContext, monitorCts.Token);

			try {
				if (!string.IsNullOrEmpty(options.StandardInput)) {
					try {
						await process.StandardInput.WriteAsync(options.StandardInput.AsMemory(), linkedCts.Token);
						await process.StandardInput.FlushAsync(linkedCts.Token);
					} catch (IOException) {
						// The child read what it wanted and closed its end. That is its answer, not a launch
						// failure - and without this the IOException escapes the only catch filter here
						// (OperationCanceledException), so ExecuteAndCaptureAsync throws instead of returning
						// a result its callers can read the exit code from.
					}
					TryCloseStandardInput(process);
				} else {
					// No input to send: close at once so the child reads EOF instead of waiting on a handle
					// nobody will ever write to. Unconditional because CreateStartInfo always redirects stdin.
					// Through the swallowing helper: the only catch filter here is OperationCanceledException,
					// so a raw Close() would report an IOException as a LAUNCH failure for a running process.
					TryCloseStandardInput(process);
				}
				await process.WaitForExitAsync(linkedCts.Token);
			}
			catch (OperationCanceledException) when (linkedCts.IsCancellationRequested) {
				ClassifyCancellation(options, resourceLimitState, stopState, ref canceled, ref timedOut);
				TryKillProcess(process);
				TryCloseStandardInput(process);
				TryCloseRedirectedStreams(process);
			}

			try {
				await Task.WhenAll(stdoutTask, stderrTask);
			} catch (Exception) when (linkedCts.IsCancellationRequested) {
				ClassifyCancellation(options, resourceLimitState, stopState, ref canceled, ref timedOut);
				TryKillProcess(process);
				TryCloseRedirectedStreams(process);
			} finally {
				await monitorCts.CancelAsync();
				await monitorTask;
			}

			try {
				if (!linkedCts.IsCancellationRequested
						&& IsMonitoredDirectoryOverLimit(options, linkedCts.Token)) {
					await StopForResourceLimitAsync(operationContext);
					TryCloseRedirectedStreams(process);
				}
			} catch (OperationCanceledException) when (linkedCts.IsCancellationRequested) {
				ClassifyCancellation(options, resourceLimitState, stopState, ref canceled, ref timedOut);
				TryKillProcess(process);
				TryCloseRedirectedStreams(process);
			}
			timeoutCts.CancelAfter(Timeout.InfiniteTimeSpan);
			if (linkedCts.IsCancellationRequested) {
				ClassifyCancellation(options, resourceLimitState, stopState, ref canceled, ref timedOut);
			}

			return new ProcessExecutionResult {
				Started = true,
				ProcessId = process.Id,
				ExitCode = process.HasExited ? process.ExitCode : null,
				Canceled = canceled,
				TimedOut = timedOut,
				ResourceLimitExceeded = resourceLimitState.Exceeded,
				DescendantTerminationUncertain = canceled || timedOut || resourceLimitState.Exceeded,
				StandardOutput = NormalizeOutput(stdout),
				StandardError = NormalizeOutput(stderr),
				StartedAtUtc = startedAt,
				FinishedAtUtc = DateTimeOffset.UtcNow
			};
		}
		catch (Exception ex) {
			return new ProcessExecutionResult {
				Started = false,
				StandardError = ex.Message,
				StartedAtUtc = startedAt,
				FinishedAtUtc = DateTimeOffset.UtcNow
			};
		}
	}

	private async Task ReadStreamAsync(StreamReader reader, ProcessOutputStream stream, StringBuilder target,
		ProcessOperationContext context) {
		char[] buffer = new char[4096];
		RealtimeLineState realtimeLine = new();
		try {
			while (true) {
				// The operation-wide token also bounds post-exit draining when a descendant retains an
				// inherited pipe handle. Output already appended to the target remains available when the
				// pending read is canceled.
				int read = await reader.ReadAsync(buffer.AsMemory(), context.OperationCts.Token);
				if (read == 0) {
					break;
				}

				int permitted = CaptureOutput(buffer, read, target, realtimeLine, stream, context);
				if (permitted < read) {
					await StopForResourceLimitAsync(context);
					break;
				}
			}
		} finally {
			if (context.EnableRealtime && realtimeLine.Pending.Length > 0) {
				PublishLine(realtimeLine.Pending.ToString(), stream, context.Options);
				realtimeLine.Pending.Clear();
			}
		}
	}

	private int CaptureOutput(char[] buffer, int read, StringBuilder target, RealtimeLineState realtimeLine,
		ProcessOutputStream stream, ProcessOperationContext context) {
		int permitted = read;
		if (context.Options.MaximumCapturedOutputCharacters is { } maximum) {
			long previous = Interlocked.Add(ref context.ResourceLimitState.CapturedOutputCharacters, read) - read;
			permitted = previous >= maximum ? 0 : (int)Math.Min(read, maximum - previous);
		}
		if (permitted <= 0) {
			return permitted;
		}
		target.Append(buffer, 0, permitted);
		if (context.EnableRealtime) {
			PublishRealtimeOutput(buffer.AsSpan(0, permitted), realtimeLine, stream, context.Options);
		}
		return permitted;
	}

	private void PublishRealtimeOutput(ReadOnlySpan<char> output, RealtimeLineState lineState,
		ProcessOutputStream stream, ProcessExecutionOptions options) {
		foreach (char character in output) {
			if (lineState.SuppressLineFeedAfterCarriageReturn) {
				lineState.SuppressLineFeedAfterCarriageReturn = false;
				if (character == '\n') {
					continue;
				}
			}
			if (character is '\r' or '\n') {
				PublishLine(lineState.Pending.ToString(), stream, options);
				lineState.Pending.Clear();
				lineState.SuppressLineFeedAfterCarriageReturn = character == '\r';
				continue;
			}
			lineState.Pending.Append(character);
		}
	}

	private static async Task MonitorDirectoryAsync(ProcessOperationContext context,
		CancellationToken cancellationToken) {
		ProcessExecutionOptions options = context.Options;
		if (string.IsNullOrWhiteSpace(options.MonitoredDirectory)
				|| options.MaximumMonitoredDirectoryBytes is not { } maximumBytes) {
			return;
		}

		TimeSpan interval = options.ResourceMonitorInterval is { } configured && configured > TimeSpan.Zero
			? configured
			: TimeSpan.FromMilliseconds(50);
		try {
			while (!cancellationToken.IsCancellationRequested) {
				if (GetDirectorySize(options.MonitoredDirectory, cancellationToken) > maximumBytes) {
					await StopForResourceLimitAsync(context);
					return;
				}
				await Task.Delay(interval, cancellationToken);
			}
		} catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
			// Normal completion cancels the resource monitor.
		} catch (IOException) {
			await StopForResourceLimitAsync(context);
		} catch (UnauthorizedAccessException) {
			await StopForResourceLimitAsync(context);
		}
	}

	private static async Task StopForResourceLimitAsync(ProcessOperationContext context) {
		context.ResourceLimitState.MarkExceeded();
		context.StopState.TrySet(OperationStopReason.ResourceLimit);
		await context.OperationCts.CancelAsync();
		TryKillProcess(context.Process);
	}

	private static bool IsMonitoredDirectoryOverLimit(ProcessExecutionOptions options,
		CancellationToken cancellationToken) {
		if (string.IsNullOrWhiteSpace(options.MonitoredDirectory)
				|| options.MaximumMonitoredDirectoryBytes is not { } maximumBytes) {
			return false;
		}
		try {
			return GetDirectorySize(options.MonitoredDirectory, cancellationToken) > maximumBytes;
		} catch (IOException) {
			return true;
		} catch (UnauthorizedAccessException) {
			return true;
		}
	}

	private static long GetDirectorySize(string directory, CancellationToken cancellationToken) {
		if (!Directory.Exists(directory)) {
			return 0;
		}

		long size = 0;
		Stack<string> pending = new();
		pending.Push(directory);
		while (pending.Count > 0) {
			cancellationToken.ThrowIfCancellationRequested();
			string current = pending.Pop();
			try {
				size = checked(size + SumRegularFileSizes(current, cancellationToken));
				PushTraversableDirectories(current, pending, cancellationToken);
			} catch (DirectoryNotFoundException) {
				continue;
			}
		}
		return size;
	}

	private static long SumRegularFileSizes(string directory, CancellationToken cancellationToken) {
		long size = 0;
		foreach (string file in Directory.EnumerateFiles(directory)) {
			cancellationToken.ThrowIfCancellationRequested();
			try {
				FileInfo info = new(file);
				// Reparse points are skipped so a symlink into a large tree cannot inflate the measured size.
				if ((info.Attributes & FileAttributes.ReparsePoint) == 0) {
					size = checked(size + info.Length);
				}
			} catch (FileNotFoundException) {
				// Files can disappear while the monitored process atomically replaces them.
			} catch (DirectoryNotFoundException) {
				// A parent directory can disappear between enumeration and metadata access.
			}
		}
		return size;
	}

	private static void PushTraversableDirectories(string directory, Stack<string> pending,
		CancellationToken cancellationToken) {
		foreach (string child in Directory.EnumerateDirectories(directory)) {
			cancellationToken.ThrowIfCancellationRequested();
			try {
				// Reparse points are not followed so traversal cannot escape the monitored directory.
				if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) {
					pending.Push(child);
				}
			} catch (DirectoryNotFoundException) {
				// Git routinely renames and removes temporary directories during mutation.
			} catch (FileNotFoundException) {
				// The directory entry disappeared before its attributes were read.
			}
		}
	}

	private static ProcessExecutionResult? ExecutePreflight(ProcessExecutionOptions options,
		DateTimeOffset startedAt, ResourceLimitState resourceLimitState, OperationStopState stopState,
		CancellationTokenSource operationCts, ref bool canceled, ref bool timedOut) {
		try {
			operationCts.Token.ThrowIfCancellationRequested();
			if (IsMonitoredDirectoryOverLimit(options, operationCts.Token)) {
				return ResourceLimitFailure(startedAt);
			}
			operationCts.Token.ThrowIfCancellationRequested();
			return null;
		} catch (OperationCanceledException) when (operationCts.IsCancellationRequested) {
			ClassifyCancellation(options, resourceLimitState, stopState, ref canceled, ref timedOut);
			return StoppedBeforeStart(startedAt, canceled, timedOut);
		}
	}

	private static ProcessExecutionResult ResourceLimitFailure(DateTimeOffset startedAt) => new() {
		Started = false,
		ResourceLimitExceeded = true,
		StandardError = "Process resource limit was exceeded.",
		StartedAtUtc = startedAt,
		FinishedAtUtc = DateTimeOffset.UtcNow
	};

	private static ProcessExecutionResult StoppedBeforeStart(DateTimeOffset startedAt, bool canceled, bool timedOut) =>
		new() {
			Started = false,
			Canceled = canceled,
			TimedOut = timedOut,
			StartedAtUtc = startedAt,
			FinishedAtUtc = DateTimeOffset.UtcNow
		};

	private void PublishLine(string line, ProcessOutputStream stream, ProcessExecutionOptions options) {
		if (options.OnOutput is not null) {
			try {
				options.OnOutput(line, stream);
			}
			catch (Exception ex) {
				_logger.WriteError($"Process output callback failed: {ex.Message}");
			}
		}

		if (!options.MirrorOutputToLogger) {
			return;
		}

		if (stream == ProcessOutputStream.StdErr) {
			if (!options.SuppressErrors) {
				_logger.WriteError(line);
			}
			return;
		}

		_logger.WriteInfo(line);
	}

	private static string NormalizeOutput(StringBuilder output) {
		return output
			.ToString()
			.TrimEnd('\r', '\n');
	}

	private static string JoinOutputs(string stdout, string stderr) {
		if (string.IsNullOrEmpty(stdout)) {
			return stderr ?? string.Empty;
		}

		if (string.IsNullOrEmpty(stderr)) {
			return stdout;
		}

		return $"{stdout}{Environment.NewLine}{stderr}";
	}

	private static void TryKillProcess(Process process) {
		try {
			// Best-effort cleanup is still useful after the immediate process exits: Windows can traverse
			// descendants from the retained root process. Unix may already have reparented them, so the
			// authoritative cross-platform cleanup remains closing Clio's redirected stream ends.
			process.Kill(entireProcessTree: true);
		}
		catch {
			// Ignore termination failures and return partial result.
		}
	}

	private static void TryCloseRedirectedStreams(Process process) {
		try {
			process.StandardOutput.Close();
		} catch {
			// Ignore cleanup failures and return the timeout/cancellation result.
		}
		try {
			process.StandardError.Close();
		} catch {
			// Ignore cleanup failures and return the timeout/cancellation result.
		}
	}

	private static void TryCloseStandardInput(Process process) {
		try {
			process.StandardInput.Close();
		} catch {
			// Ignore cleanup failures and continue timeout/cancellation cleanup.
		}
	}

	private static void ClassifyCancellation(ProcessExecutionOptions options,
		ResourceLimitState resourceLimitState, OperationStopState stopState, ref bool canceled, ref bool timedOut) {
		if (canceled || timedOut) {
			return;
		}
		switch (stopState.Reason) {
			case OperationStopReason.CallerCancellation:
				canceled = true;
				break;
			case OperationStopReason.Timeout:
				timedOut = true;
				break;
			case OperationStopReason.ResourceLimit:
				break;
			default:
				canceled = options.CancellationToken.IsCancellationRequested;
				timedOut = !canceled && !resourceLimitState.Exceeded;
				break;
		}
	}

	private static void ValidateOptions(ProcessExecutionOptions options) {
		if (options is null) {
			throw new ArgumentNullException(nameof(options));
		}

		options.Program.CheckArgumentNullOrWhiteSpace(nameof(options.Program));
		options.Arguments.CheckArgumentNullOrWhiteSpace(nameof(options.Arguments));
		if (options.MaximumCapturedOutputCharacters is <= 0) {
			throw new ArgumentOutOfRangeException(nameof(options), options.MaximumCapturedOutputCharacters,
				$"{nameof(options.MaximumCapturedOutputCharacters)} must be greater than zero when configured.");
		}
		if (options.MaximumMonitoredDirectoryBytes is <= 0) {
			throw new ArgumentOutOfRangeException(nameof(options), options.MaximumMonitoredDirectoryBytes,
				$"{nameof(options.MaximumMonitoredDirectoryBytes)} must be greater than zero when configured.");
		}
		if (options.MaximumMonitoredDirectoryBytes.HasValue
				&& string.IsNullOrWhiteSpace(options.MonitoredDirectory)) {
			throw new ArgumentException("A monitored directory is required when a directory size limit is configured.",
				nameof(options));
		}
	}

	private sealed class ResourceLimitState {
		private int _exceeded;

		public long CapturedOutputCharacters;

		public bool Exceeded => Volatile.Read(ref _exceeded) != 0;

		public void MarkExceeded() => Interlocked.Exchange(ref _exceeded, 1);
	}

	private sealed record ProcessOperationContext(ProcessExecutionOptions Options, bool EnableRealtime,
		ResourceLimitState ResourceLimitState, OperationStopState StopState, Process Process,
		CancellationTokenSource OperationCts);

	private sealed class OperationStopState {
		private int _reason;

		public OperationStopReason Reason => (OperationStopReason)Volatile.Read(ref _reason);

		public void TrySet(OperationStopReason reason) =>
			Interlocked.CompareExchange(ref _reason, (int)reason, (int)OperationStopReason.None);
	}

	private enum OperationStopReason {
		None,
		CallerCancellation,
		Timeout,
		ResourceLimit
	}

	private sealed class RealtimeLineState {
		public StringBuilder Pending { get; } = new();

		public bool SuppressLineFeedAfterCarriageReturn { get; set; }
	}

	#endregion
}
