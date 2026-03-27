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
		ProcessStartInfo startInfo = new() {
			FileName = options.Program,
			Arguments = options.Arguments,
			CreateNoWindow = true,
			UseShellExecute = false,
			WorkingDirectory = options.WorkingDirectory ?? Environment.CurrentDirectory,
			RedirectStandardInput = !string.IsNullOrEmpty(options.StandardInput),
			RedirectStandardOutput = redirectOutput,
			RedirectStandardError = redirectOutput
		};

		if (options.EnvironmentVariables is not null) {
			foreach ((string key, string value) in options.EnvironmentVariables) {
				startInfo.Environment[key] = value;
			}
		}

		return startInfo;
	}

	private async Task<ProcessExecutionResult> ExecuteInternalAsync(ProcessExecutionOptions options, bool enableRealtime) {
		ValidateOptions(options);

		StringBuilder stdout = new();
		StringBuilder stderr = new();
		DateTimeOffset startedAt = DateTimeOffset.UtcNow;

		try {
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

			Task stdoutTask = ReadStreamAsync(process.StandardOutput, ProcessOutputStream.StdOut, stdout, options,
				enableRealtime);
			Task stderrTask = ReadStreamAsync(process.StandardError, ProcessOutputStream.StdErr, stderr, options,
				enableRealtime);

			bool canceled = false;
			bool timedOut = false;

			using CancellationTokenSource linkedCts =
				CancellationTokenSource.CreateLinkedTokenSource(options.CancellationToken);
			if (options.Timeout is { } timeout && timeout > TimeSpan.Zero) {
				linkedCts.CancelAfter(timeout);
			}

			try {
				await process.WaitForExitAsync(linkedCts.Token);
			}
			catch (OperationCanceledException) {
				canceled = options.CancellationToken.IsCancellationRequested;
				timedOut = !canceled;
				TryKillProcess(process);
			}

			await Task.WhenAll(stdoutTask, stderrTask);

			return new ProcessExecutionResult {
				Started = true,
				ProcessId = process.Id,
				ExitCode = process.HasExited ? process.ExitCode : null,
				Canceled = canceled,
				TimedOut = timedOut,
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
		ProcessExecutionOptions options, bool enableRealtime) {
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
	}

	#endregion
}
