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

	private static ProcessStartInfo CreateStartInfo(ProcessExecutionOptions options, bool redirectOutput) {
		string program = options.ResolveProgramPath
			? ResolveExecutablePath(options.Program)
			: options.Program;
		ProcessStartInfo startInfo = new() {
			FileName = program,
			Arguments = options.Arguments,
			CreateNoWindow = true,
			UseShellExecute = false,
			WorkingDirectory = options.WorkingDirectory ?? Environment.CurrentDirectory,
			RedirectStandardInput = !string.IsNullOrEmpty(options.StandardInput),
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
			string directory = rawDirectory.Trim().Trim('"');
			if (!Path.IsPathFullyQualified(directory)) {
				continue;
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
		}
		throw new FileNotFoundException(
			$"Executable '{program}' was not found in any rooted PATH directory.",
			program);
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

		try {
			if (IsMonitoredDirectoryOverLimit(options)) {
				return ResourceLimitFailure(startedAt);
			}

			using Process process = new();
			process.StartInfo = CreateStartInfo(options, redirectOutput: true);
			process.EnableRaisingEvents = true;

			bool started = process.Start();
			if (!started) {
				return new ProcessExecutionResult {
					Started = false,
					StartedAtUtc = startedAt,
					FinishedAtUtc = DateTimeOffset.UtcNow
				};
			}

			if (!string.IsNullOrEmpty(options.StandardInput)) {
				await process.StandardInput.WriteAsync(options.StandardInput);
				await process.StandardInput.FlushAsync();
				process.StandardInput.Close();
			}

			ResourceLimitState resourceLimitState = new();
			Task stdoutTask = ReadStreamAsync(process.StandardOutput, ProcessOutputStream.StdOut, stdout, options,
				enableRealtime, resourceLimitState, process);
			Task stderrTask = ReadStreamAsync(process.StandardError, ProcessOutputStream.StdErr, stderr, options,
				enableRealtime, resourceLimitState, process);

			bool canceled = false;
			bool timedOut = false;

			using CancellationTokenSource linkedCts =
				CancellationTokenSource.CreateLinkedTokenSource(options.CancellationToken);
			if (options.Timeout is { } timeout && timeout > TimeSpan.Zero) {
				linkedCts.CancelAfter(timeout);
			}
			using CancellationTokenSource monitorCts = CancellationTokenSource.CreateLinkedTokenSource(linkedCts.Token);
			Task monitorTask = MonitorDirectoryAsync(process, options, resourceLimitState, monitorCts.Token);

			try {
				await process.WaitForExitAsync(linkedCts.Token);
			}
			catch (OperationCanceledException) {
				canceled = options.CancellationToken.IsCancellationRequested;
				timedOut = !canceled && !resourceLimitState.Exceeded;
				TryKillProcess(process);
			}

			if (IsMonitoredDirectoryOverLimit(options)) {
				resourceLimitState.MarkExceeded();
				TryKillProcess(process);
			}
			monitorCts.Cancel();
			await monitorTask;
			await Task.WhenAll(stdoutTask, stderrTask);

			return new ProcessExecutionResult {
				Started = true,
				ProcessId = process.Id,
				ExitCode = process.HasExited ? process.ExitCode : null,
				Canceled = canceled,
				TimedOut = timedOut,
				ResourceLimitExceeded = resourceLimitState.Exceeded,
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
		ProcessExecutionOptions options, bool enableRealtime, ResourceLimitState resourceLimitState, Process process) {
		if (options.MaximumCapturedOutputCharacters.HasValue) {
			await ReadBoundedStreamAsync(reader, stream, target, options, enableRealtime, resourceLimitState, process);
			return;
		}

		while (true) {
			string line = await reader.ReadLineAsync();
			if (line is null) {
				break;
			}

			target.AppendLine(line);
			if (enableRealtime) {
				PublishLine(line, stream, options);
			}
		}
	}

	private async Task ReadBoundedStreamAsync(StreamReader reader, ProcessOutputStream stream, StringBuilder target,
		ProcessExecutionOptions options, bool enableRealtime, ResourceLimitState resourceLimitState, Process process) {
		char[] buffer = new char[4096];
		StringBuilder realtimeLine = new();
		long maximum = options.MaximumCapturedOutputCharacters!.Value;
		while (true) {
			int read = await reader.ReadAsync(buffer.AsMemory());
			if (read == 0) {
				break;
			}

			long previous = Interlocked.Add(ref resourceLimitState.CapturedOutputCharacters, read) - read;
			int permitted = previous >= maximum ? 0 : (int)Math.Min(read, maximum - previous);
			if (permitted > 0) {
				target.Append(buffer, 0, permitted);
				if (enableRealtime) {
					PublishBoundedOutput(buffer.AsSpan(0, permitted), realtimeLine, stream, options);
				}
			}
			if (permitted < read) {
				resourceLimitState.MarkExceeded();
				TryKillProcess(process);
				break;
			}
		}
		if (enableRealtime && realtimeLine.Length > 0) {
			PublishLine(realtimeLine.ToString(), stream, options);
		}
	}

	private void PublishBoundedOutput(ReadOnlySpan<char> output, StringBuilder pendingLine,
		ProcessOutputStream stream, ProcessExecutionOptions options) {
		foreach (char character in output) {
			if (character == '\n') {
				PublishLine(pendingLine.ToString().TrimEnd('\r'), stream, options);
				pendingLine.Clear();
			} else {
				pendingLine.Append(character);
			}
		}
	}

	private static async Task MonitorDirectoryAsync(Process process, ProcessExecutionOptions options,
		ResourceLimitState resourceLimitState, CancellationToken cancellationToken) {
		if (string.IsNullOrWhiteSpace(options.MonitoredDirectory)
				|| options.MaximumMonitoredDirectoryBytes is not { } maximumBytes) {
			return;
		}

		TimeSpan interval = options.ResourceMonitorInterval is { } configured && configured > TimeSpan.Zero
			? configured
			: TimeSpan.FromMilliseconds(50);
		try {
			while (!process.HasExited && !cancellationToken.IsCancellationRequested) {
				if (GetDirectorySize(options.MonitoredDirectory) > maximumBytes) {
					resourceLimitState.MarkExceeded();
					TryKillProcess(process);
					return;
				}
				await Task.Delay(interval, cancellationToken);
			}
		} catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
			// Normal completion cancels the resource monitor.
		} catch (IOException) {
			resourceLimitState.MarkExceeded();
			TryKillProcess(process);
		} catch (UnauthorizedAccessException) {
			resourceLimitState.MarkExceeded();
			TryKillProcess(process);
		}
	}

	private static bool IsMonitoredDirectoryOverLimit(ProcessExecutionOptions options) {
		if (string.IsNullOrWhiteSpace(options.MonitoredDirectory)
				|| options.MaximumMonitoredDirectoryBytes is not { } maximumBytes) {
			return false;
		}
		try {
			return GetDirectorySize(options.MonitoredDirectory) > maximumBytes;
		} catch (IOException) {
			return true;
		} catch (UnauthorizedAccessException) {
			return true;
		}
	}

	private static long GetDirectorySize(string directory) {
		if (!Directory.Exists(directory)) {
			return 0;
		}

		long size = 0;
		foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)) {
			size = checked(size + new FileInfo(file).Length);
		}
		return size;
	}

	private static ProcessExecutionResult ResourceLimitFailure(DateTimeOffset startedAt) => new() {
		Started = false,
		ResourceLimitExceeded = true,
		StandardError = "Process resource limit was exceeded.",
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
			if (!process.HasExited) {
				process.Kill(entireProcessTree: true);
			}
		}
		catch {
			// Ignore termination failures and return partial result.
		}
	}

	private static void ValidateOptions(ProcessExecutionOptions options) {
		if (options is null) {
			throw new ArgumentNullException(nameof(options));
		}

		options.Program.CheckArgumentNullOrWhiteSpace(nameof(options.Program));
		options.Arguments.CheckArgumentNullOrWhiteSpace(nameof(options.Arguments));
		if (options.MaximumCapturedOutputCharacters is <= 0) {
			throw new ArgumentOutOfRangeException(nameof(options.MaximumCapturedOutputCharacters));
		}
		if (options.MaximumMonitoredDirectoryBytes is <= 0) {
			throw new ArgumentOutOfRangeException(nameof(options.MaximumMonitoredDirectoryBytes));
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

	#endregion
}
