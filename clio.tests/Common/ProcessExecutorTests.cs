using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
