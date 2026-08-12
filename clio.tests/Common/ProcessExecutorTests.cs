using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Property("Module", "Common")]
[Category("Unit")]
public class ProcessExecutorTests {

	[Test]
	[Description("Verifies that fire-and-forget mode starts a process and returns launch metadata without waiting for completion.")]
	public async Task FireAndForgetAsync_StartsProcess() {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();
		ProcessExecutor sut = new(logger);
		(string program, string arguments) = GetDotNetCommand("--info");
		ProcessExecutionOptions options = new(program, arguments);

		// Act
		ProcessLaunchResult result = await sut.FireAndForgetAsync(options);

		// Assert
		result.Started.Should().BeTrue("because fire-and-forget execution must launch the process");
		result.ProcessId.Should().NotBeNull("because a started process should have an identifier");
	}

	[Test]
	[Description("Verifies that capture mode waits for process completion and returns standard output with exit metadata.")]
	public async Task ExecuteAndCaptureAsync_ReturnsOutput() {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();
		ProcessExecutor sut = new(logger);
		(string program, string arguments) = GetDotNetCommand("--version");
		ProcessExecutionOptions options = new(program, arguments);

		// Act
		ProcessExecutionResult result = await sut.ExecuteAndCaptureAsync(options);

		// Assert
		result.Started.Should().BeTrue("because the command is valid and should start");
		result.ExitCode.Should().Be(0, "because the command should finish successfully");
		result.StandardOutput.Should().NotBeNullOrWhiteSpace(
			"because capture mode should return standard output text");
		result.Canceled.Should().BeFalse("because the execution was not canceled");
		result.TimedOut.Should().BeFalse("because no timeout was configured");
	}

	[Test]
	[Description("Verifies that real-time mode publishes stdout lines and mirrors them to ILogger.")]
	public async Task ExecuteWithRealtimeOutputAsync_StreamsOutputAndMirrorsToLogger() {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();
		ProcessExecutor sut = new(logger);
		List<(string Line, ProcessOutputStream Stream)> receivedLines = new();
		(string program, string arguments) = GetDotNetCommand("--version");
		ProcessExecutionOptions options = new(program, arguments) {
			MirrorOutputToLogger = true,
			OnOutput = (line, stream) => receivedLines.Add((line, stream))
		};

		// Act
		ProcessExecutionResult result = await sut.ExecuteWithRealtimeOutputAsync(options);

		// Assert
		result.Started.Should().BeTrue("because the command is valid and should start");
		result.ExitCode.Should().Be(0, "because the command should finish successfully");
		receivedLines.Should().Contain((line) => !string.IsNullOrWhiteSpace(line.Line) && line.Stream == ProcessOutputStream.StdOut,
			"because stdout lines must be reported in real time");
		int writeInfoCalls = logger.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(ILogger.WriteInfo));
		writeInfoCalls.Should().BeGreaterThan(0, "because realtime mode with mirroring should publish stdout via logger");
	}

	[Test]
	[Description("Verifies that capture mode fails closed before launching a process when the monitored directory already exceeds its configured byte limit.")]
	public async Task ExecuteAndCaptureAsync_ShouldFailClosed_WhenMonitoredDirectoryExceedsLimit() {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();
		ProcessExecutor sut = new(logger);
		string directory = Path.Combine(Path.GetTempPath(), $"clio-process-limit-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directory);
		await File.WriteAllBytesAsync(Path.Combine(directory, "oversized.bin"), [0x01, 0x02]);
		(string program, string arguments) = GetDotNetCommand("--version");
		ProcessExecutionOptions options = new(program, arguments) {
			MonitoredDirectory = directory,
			MaximumMonitoredDirectoryBytes = 1
		};

		try {
			// Act
			ProcessExecutionResult result = await sut.ExecuteAndCaptureAsync(options);

			// Assert
			result.Started.Should().BeFalse(
				because: "a process must not start after its monitored staging area already exceeded the limit");
			result.ResourceLimitExceeded.Should().BeTrue(
				because: "callers need a fail-closed signal distinct from ordinary process failure");
		} finally {
			Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	[Description("Verifies that capture mode bounds untrusted output and reports a resource-limit failure.")]
	public async Task ExecuteAndCaptureAsync_ShouldTerminateCapture_WhenOutputExceedsLimit() {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();
		ProcessExecutor sut = new(logger);
		(string program, string arguments) = GetDotNetCommand("--info");
		ProcessExecutionOptions options = new(program, arguments) {
			MaximumCapturedOutputCharacters = 1
		};

		// Act
		ProcessExecutionResult result = await sut.ExecuteAndCaptureAsync(options);

		// Assert
		result.ResourceLimitExceeded.Should().BeTrue(
			because: "output beyond the configured cap must terminate capture instead of consuming unbounded memory");
		result.StandardOutput.Length.Should().BeLessThanOrEqualTo(1,
			because: "captured output must never exceed the configured character limit");
	}

	[Test]
	[NonParallelizable]
	[Description("Verifies that a cleared child environment retains only explicitly allowlisted and supplied variables.")]
	public async Task ExecuteAndCaptureAsync_ShouldClearInheritedEnvironment_WhenRequested() {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();
		ProcessExecutor sut = new(logger);
		string suffix = Guid.NewGuid().ToString("N").ToUpperInvariant();
		string inheritedName = $"CLIO_PROCESS_INHERITED_{suffix}";
		string allowlistedName = $"CLIO_PROCESS_ALLOWLISTED_{suffix}";
		string explicitName = $"CLIO_PROCESS_EXPLICIT_{suffix}";
		Environment.SetEnvironmentVariable(inheritedName, "ambient");
		Environment.SetEnvironmentVariable(allowlistedName, "allowed");
		(string program, string arguments) = GetEnvironmentProbeCommand(
			inheritedName,
			allowlistedName,
			explicitName);
		ProcessExecutionOptions options = new(program, arguments) {
			ClearInheritedEnvironment = true,
			InheritedEnvironmentVariableAllowlist = [allowlistedName],
			EnvironmentVariables = new Dictionary<string, string> {
				[explicitName] = "explicit"
			}
		};

		try {
			// Act
			ProcessExecutionResult result = await sut.ExecuteAndCaptureAsync(options);

			// Assert
			result.ExitCode.Should().Be(0,
				because: "the environment probe should run with the deliberately minimal environment");
			result.StandardOutput.Should().Contain("inherited=unset",
				because: "ambient variables outside the allowlist must not reach the child process");
			result.StandardOutput.Should().Contain("allowlisted=allowed",
				because: "the explicit inherited-variable allowlist must preserve required ambient values");
			result.StandardOutput.Should().Contain("explicit=explicit",
				because: "explicit child variables must be applied after the inherited environment is cleared");
		} finally {
			Environment.SetEnvironmentVariable(inheritedName, null);
			Environment.SetEnvironmentVariable(allowlistedName, null);
		}
	}

	[Test]
	[Description("Verifies that executable resolution pins a bare program name to an absolute executable path.")]
	public void ResolveExecutablePath_ShouldReturnAbsolutePath_WhenProgramNameIsBare() {
		// Arrange
		const string program = "dotnet";

		// Act
		string resolved = ProcessExecutor.ResolveExecutablePath(program);

		// Assert
		Path.IsPathFullyQualified(resolved).Should().BeTrue(
			because: "a resolved process must not depend on child working-directory or PATH lookup order");
		File.Exists(resolved).Should().BeTrue(
			because: "the pinned process path must identify an existing executable file");
	}

	[Test]
	[Description("Verifies that growth of the monitored directory while a child is running terminates it promptly and is reported as a resource-limit failure rather than a timeout.")]
	public async Task ExecuteAndCaptureAsync_ShouldTerminateRunningChild_WhenTheMonitoredDirectoryGrowsPastTheLimit() {
		// Arrange
		// The pre-launch check only covers a directory that is already oversized. Here the cap is
		// crossed while the child is alive, which is what an untrusted clone actually does. The
		// growth is driven from the first captured output line rather than by the child itself:
		// the guard polls the directory and does not care who wrote to it, and a portable shell
		// one-liner that reliably grows a directory on every supported OS does not exist.
		ILogger logger = Substitute.For<ILogger>();
		ProcessExecutor sut = new(logger);
		string directory = Path.Combine(Path.GetTempPath(), $"clio-process-growth-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directory);
		const long capBytes = 1024 * 1024;
		const string readyMarkerFileName = "ready.txt";
		string readyMarker = Path.Combine(directory, readyMarkerFileName);
		(string program, string arguments) = GetIdlingChildCommand(readyMarkerFileName);
		ProcessExecutionOptions options = new(program, arguments) {
			WorkingDirectory = directory,
			MonitoredDirectory = directory,
			MaximumMonitoredDirectoryBytes = capBytes,
			ResourceMonitorInterval = TimeSpan.FromMilliseconds(50),
			Timeout = TimeSpan.FromSeconds(25)
		};
		// The child announces itself with a tiny marker well under the cap, so the growth is known
		// to happen after launch rather than racing the pre-launch check.
		Task<bool> growth = Task.Run(async () => {
			for (int attempt = 0; attempt < 200 && !File.Exists(readyMarker); attempt++) {
				await Task.Delay(50);
			}
			if (!File.Exists(readyMarker)) {
				return false;
			}
			await File.WriteAllBytesAsync(Path.Combine(directory, "grown.bin"), new byte[capBytes * 2]);
			return true;
		});
		Stopwatch elapsed = Stopwatch.StartNew();

		try {
			// Act
			ProcessExecutionResult result = await sut.ExecuteAndCaptureAsync(options);
			elapsed.Stop();
			bool grown = await growth;

			// Assert
			result.Started.Should().BeTrue(
				because: "the child must actually launch for the running-process guard to be under test");
			grown.Should().BeTrue(
				because: "the directory must be grown after launch, or the already-oversized pre-launch check would be under test instead");
			result.ResourceLimitExceeded.Should().BeTrue(
				because: "growth past the cap during execution must be reported as a resource-limit failure");
			result.TimedOut.Should().BeFalse(
				because: "the guard must terminate the child on its own rather than leaving the timeout to do it");
			elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15),
				because: "the child idles far longer than this, so returning early proves it was killed rather than waited out");
			result.StandardOutput.Should().NotBeNullOrWhiteSpace(
				because: "the stream readers must complete with the output produced before termination, not hang on the killed child");
		} finally {
			TryDeleteDirectory(directory);
		}
	}

	/// <summary>
	/// Builds a child that writes one line to each stream, signals that it is running, and then
	/// idles well past the assertion window, so an early return can only mean the resource guard
	/// terminated it.
	/// </summary>
	/// <param name="readyMarkerFileName">
	/// The marker file the child creates once it is running. It is a bare file name, written into
	/// the child's working directory, so no path has to survive two levels of shell quoting.
	/// </param>
	/// <returns>The program and arguments.</returns>
	private static (string Program, string Arguments) GetIdlingChildCommand(string readyMarkerFileName) {
		// The launcher is OS-specific because there is no portable way to spawn a shell; the
		// behaviour under test - prompt termination and resource-limit classification - is not.
		if (OperatingSystem.IsWindows()) {
			string commandInterpreter = Environment.GetEnvironmentVariable("ComSpec")
				?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
			return (
				commandInterpreter,
				$"/d /c \"echo started & echo started 1>&2 & echo ready>{readyMarkerFileName} "
				+ "& ping -n 31 127.0.0.1 >nul\"");
		}
		return (
			"/bin/sh",
			$"-c \"echo started; echo started 1>&2; echo ready > {readyMarkerFileName}; sleep 30\"");
	}

	/// <summary>Removes a directory a killed child may still be holding entries in.</summary>
	/// <param name="directory">The directory to remove.</param>
	private static void TryDeleteDirectory(string directory) {
		try {
			Directory.Delete(directory, recursive: true);
		} catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
			// A terminated child can leave a handle open briefly on Windows; the temp directory is
			// disposable, so failing cleanup must not fail the assertion under test.
		}
	}

	private static (string Program, string Arguments) GetDotNetCommand(string arguments) {
		return ("dotnet", arguments);
	}

	private static (string Program, string Arguments) GetEnvironmentProbeCommand(
		string inheritedName,
		string allowlistedName,
		string explicitName) {
		if (OperatingSystem.IsWindows()) {
			string commandInterpreter = Environment.GetEnvironmentVariable("ComSpec")
				?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
			return (
				commandInterpreter,
				$"/d /c \"if defined {inheritedName} (echo inherited=%{inheritedName}%) else (echo inherited=unset)"
				+ $"&echo allowlisted=%{allowlistedName}%&echo explicit=%{explicitName}%\"");
		}
		string inheritedExpression = "${" + inheritedName + ":-unset}";
		string allowlistedExpression = "${" + allowlistedName + ":-unset}";
		string explicitExpression = "${" + explicitName + ":-unset}";
		return (
			"/bin/sh",
			$"-c \"printf 'inherited=%s\\nallowlisted=%s\\nexplicit=%s\\n' "
			+ $"'{inheritedExpression}' '{allowlistedExpression}' '{explicitExpression}'\"");
	}

}
