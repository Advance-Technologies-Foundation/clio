using System;
using System.Collections.Generic;
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

	private static (string Program, string Arguments) GetDotNetCommand(string arguments) {
		return ("dotnet", arguments);
	}

}
