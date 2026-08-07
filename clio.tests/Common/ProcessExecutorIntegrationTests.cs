using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Property("Module", "Common")]
[Category("Integration")]
public class ProcessExecutorIntegrationTests {

	[TestCase(false, null)]
	[TestCase(true, 4096L)]
	[Description("Verifies that timeout and caller cancellation bound redirected stream draining after an immediate parent exits.")]
	public async Task ExecuteAndCaptureAsync_ShouldBoundDrain_WhenDescendantRetainsRedirectedHandles(
		bool cancelByCaller, long? maximumCapturedOutputCharacters) {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();
		ProcessExecutor sut = new(logger);
		string directory = Path.Combine(Path.GetTempPath(), $"clio-process-drain-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directory);
		string descendantPidPath = Path.Combine(directory, "descendant.pid");
		string fixtureExecutable = ResolveFixtureExecutable();
		TimeSpan operationBudget = TimeSpan.FromSeconds(2);
		using CancellationTokenSource cancellationSource = cancelByCaller
			? new CancellationTokenSource(operationBudget)
			: new CancellationTokenSource();
		ProcessExecutionOptions options = new(fixtureExecutable,
			$"--spawn-inherited-handle-descendant \"{descendantPidPath}\"") {
			CancellationToken = cancellationSource.Token,
			MaximumCapturedOutputCharacters = maximumCapturedOutputCharacters,
			Timeout = cancelByCaller ? null : operationBudget
		};
		Stopwatch elapsed = Stopwatch.StartNew();
		int? descendantPid = null;

		try {
			// Act
			ProcessExecutionResult result = await sut.ExecuteAndCaptureAsync(options);
			elapsed.Stop();
			descendantPid = ReadDescendantPid(descendantPidPath);

			// Assert
			result.Started.Should().BeTrue(
				because: "the purpose-built immediate parent must start to exercise post-exit stream draining");
			result.TimedOut.Should().Be(!cancelByCaller,
				because: "only the configured operation deadline should classify the stopped drain as timed out");
			result.Canceled.Should().Be(cancelByCaller,
				because: "caller cancellation and the configured timeout must retain distinct classifications");
			result.DescendantTerminationUncertain.Should().BeTrue(
				because: "closing redirected streams cannot prove that a silent reparented descendant was terminated");
			result.StandardOutput.Should().Be("parent-exited",
				because: "unterminated output captured before cancellation must remain available in full");
			elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
				because: "the silent thirty-second descendant must not extend the two-second operation budget");
		} finally {
			if (descendantPid is not null) {
				await TerminateProcessAsync(descendantPid.Value);
			}
			Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	[Description("Verifies that an output resource limit cancels both redirected readers when a descendant retains their handles.")]
	public async Task ExecuteAndCaptureAsync_ShouldBoundDrain_WhenOutputLimitIsExceeded() {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();
		ProcessExecutor sut = new(logger);
		string directory = Path.Combine(Path.GetTempPath(), $"clio-process-limit-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directory);
		string descendantPidPath = Path.Combine(directory, "descendant.pid");
		ProcessExecutionOptions options = new(ResolveFixtureExecutable(),
			$"--overflow-output-with-inherited-handle-descendant \"{descendantPidPath}\"") {
			MaximumCapturedOutputCharacters = 64
		};
		Stopwatch elapsed = Stopwatch.StartNew();
		int? descendantPid = null;

		try {
			// Act
			ProcessExecutionResult result = await sut.ExecuteAndCaptureAsync(options);
			elapsed.Stop();
			descendantPid = ReadDescendantPid(descendantPidPath);

			// Assert
			result.ResourceLimitExceeded.Should().BeTrue(
				because: "cross-stream capture beyond the configured maximum must terminate the operation");
			result.StandardOutput.Should().HaveLength(64,
				because: "captured output must be truncated exactly at the configured resource boundary");
			result.DescendantTerminationUncertain.Should().BeTrue(
				because: "resource cleanup cannot prove that a reparented pipe holder was terminated");
			elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3),
				because: "canceling the operation token must release the other redirected reader immediately");
		} finally {
			if (descendantPid is not null) {
				await TerminateProcessAsync(descendantPid.Value);
			}
			Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	[Description("Verifies that real-time callbacks preserve carriage-return line boundaries across asynchronous reads.")]
	public async Task ExecuteWithRealtimeOutputAsync_ShouldPublishLine_WhenOutputUsesCarriageReturn() {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();
		ProcessExecutor sut = new(logger);
		ConcurrentQueue<string> lines = new();
		TaskCompletionSource firstLineObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
		ProcessExecutionOptions options = new(ResolveFixtureExecutable(), "--write-carriage-return-output") {
			Timeout = TimeSpan.FromSeconds(5),
			OnOutput = (line, _) => {
				lines.Enqueue(line);
				if (string.Equals(line, "first", StringComparison.Ordinal)) {
					firstLineObserved.TrySetResult();
				}
			}
		};

		// Act
		Task<ProcessExecutionResult> execution = sut.ExecuteWithRealtimeOutputAsync(options);
		await firstLineObserved.Task.WaitAsync(TimeSpan.FromSeconds(3));
		bool completedBeforeSecondOutput = execution.IsCompleted;
		ProcessExecutionResult result = await execution.WaitAsync(TimeSpan.FromSeconds(5));

		// Assert
		completedBeforeSecondOutput.Should().BeFalse(
			because: "a carriage return must publish the first callback while the child is still running");
		lines.Should().Equal(["first", "second"],
			because: "carriage return and end-of-stream must remain separate logical line boundaries");
		result.StandardOutput.Should().Be("first\rsecond",
			because: "real-time parsing must not alter the captured output payload");
		result.ExitCode.Should().Be(0,
			because: "the deterministic carriage-return fixture should complete normally");
	}

	private static string ResolveFixtureExecutable() {
		DirectoryInfo testDirectory = new(TestContext.CurrentContext.TestDirectory);
		string targetFramework = testDirectory.Name;
		string configuration = testDirectory.Parent?.Name
			?? throw new InvalidOperationException("The test configuration directory could not be resolved.");
		string repositoryRoot = Path.GetFullPath(Path.Combine(testDirectory.FullName, "..", "..", "..", ".."));
		string executableName = OperatingSystem.IsWindows() ? "git.exe" : "git";
		string fixtureExecutable = Path.Combine(repositoryRoot, "clio.process.fixture", "bin", configuration,
			targetFramework, executableName);
		return File.Exists(fixtureExecutable)
			? fixtureExecutable
			: throw new FileNotFoundException("The process integration fixture was not built.", fixtureExecutable);
	}

	private static int ReadDescendantPid(string descendantPidPath) {
		File.Exists(descendantPidPath).Should().BeTrue(
			because: "the immediate parent must record the descendant that inherited its redirected handles");
		return int.Parse(File.ReadAllText(descendantPidPath), CultureInfo.InvariantCulture);
	}

	private static async Task TerminateProcessAsync(int processId) {
		using Process process = TryGetProcess(processId);
		if (process is null || process.HasExited) {
			return;
		}
		process.Kill(entireProcessTree: true);
		using CancellationTokenSource cleanupDeadline = new(TimeSpan.FromSeconds(5));
		await process.WaitForExitAsync(cleanupDeadline.Token);
	}

	private static Process TryGetProcess(int processId) {
		try {
			return Process.GetProcessById(processId);
		} catch (ArgumentException) {
			return null;
		}
	}

}
