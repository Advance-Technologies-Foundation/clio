using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
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
	[Test]
	[Description("Verifies that caller cancellation during monitored-directory preflight is classified without launching the process.")]
	public async Task ExecuteAndCaptureAsync_ShouldReturnCanceledWithoutStarting_WhenPreflightIsAlreadyCanceled() {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();
		ProcessExecutor sut = new(logger);
		string directory = Path.Combine(Path.GetTempPath(), $"clio-process-preflight-cancel-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directory);
		string descendantIdentityPath = Path.Combine(directory, "descendant.identity");
		using CancellationTokenSource cancellationSource = new();
		cancellationSource.Cancel();
		ProcessExecutionOptions options = new(ResolveFixtureExecutable(),
			$"--spawn-inherited-handle-descendant \"{descendantIdentityPath}\"") {
			CancellationToken = cancellationSource.Token,
			MonitoredDirectory = directory,
			MaximumMonitoredDirectoryBytes = 1024
		};

		try {
			// Act
			ProcessExecutionResult result = await sut.ExecuteAndCaptureAsync(options);

			// Assert
			result.Started.Should().BeFalse(
				because: "an operation canceled before preflight must not launch the child process");
			result.Canceled.Should().BeTrue(
				because: "preflight cancellation must retain the same classification as in-process cancellation");
			result.TimedOut.Should().BeFalse(
				because: "the caller token, not an operation deadline, stopped this execution");
			File.Exists(descendantIdentityPath).Should().BeFalse(
				because: "the fixture identity marker proves whether the child process was launched");
		} finally {
			Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	[Description("Verifies that the operation timeout bounds traversal of a large pre-existing monitored directory.")]
	public async Task ExecuteAndCaptureAsync_ShouldBoundPreflightScan_WhenMonitoredDirectoryIsLarge() {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();
		ProcessExecutor sut = new(logger);
		string directory = Path.Combine(Path.GetTempPath(), $"clio-process-preflight-timeout-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directory);
		for (int index = 0; index < 1_000; index++) {
			string childDirectory = Path.Combine(directory, $"entry-{index:D4}");
			Directory.CreateDirectory(childDirectory);
			File.Create(Path.Combine(childDirectory, "content.tmp")).Dispose();
		}
		ProcessExecutionOptions options = new(ResolveFixtureExecutable(), "--write-carriage-return-output") {
			Timeout = TimeSpan.FromMilliseconds(10),
			MonitoredDirectory = directory,
			MaximumMonitoredDirectoryBytes = long.MaxValue
		};
		Stopwatch elapsed = Stopwatch.StartNew();

		try {
			// Act
			ProcessExecutionResult result = await sut.ExecuteAndCaptureAsync(options);
			elapsed.Stop();

			// Assert
			result.TimedOut.Should().BeTrue(
				because: "the configured deadline must include the monitored-directory preflight scan");
			result.Started.Should().BeFalse(
				because: "the deadline must stop preflight before the fixture process can launch");
			result.Canceled.Should().BeFalse(
				because: "the operation deadline, not caller cancellation, stopped this execution");
			elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(500),
				because: "an existing checkout must not postpone a ten-millisecond operation deadline");
		} finally {
			Directory.Delete(directory, recursive: true);
		}
	}

	[TestCase(false, null)]
	[TestCase(true, 4096L)]
	[Description("Verifies that timeout and caller cancellation bound redirected stream draining after an immediate parent exits.")]
	public async Task ExecuteWithRealtimeOutputAsync_ShouldBoundDrain_WhenDescendantRetainsRedirectedHandles(
		bool cancelByCaller, long? maximumCapturedOutputCharacters) {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();
		ProcessExecutor sut = new(logger);
		string directory = Path.Combine(Path.GetTempPath(), $"clio-process-drain-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directory);
		string descendantIdentityPath = Path.Combine(directory, "descendant.identity");
		string fixtureExecutable = ResolveFixtureExecutable();
		TimeSpan operationBudget = TimeSpan.FromSeconds(2);
		ConcurrentQueue<string> lines = new();
		using CancellationTokenSource cancellationSource = cancelByCaller
			? new CancellationTokenSource(operationBudget)
			: new CancellationTokenSource();
		ProcessExecutionOptions options = new(fixtureExecutable,
			$"--spawn-inherited-handle-descendant \"{descendantIdentityPath}\"") {
			CancellationToken = cancellationSource.Token,
			MaximumCapturedOutputCharacters = maximumCapturedOutputCharacters,
			Timeout = cancelByCaller ? null : operationBudget,
			OnOutput = (line, _) => lines.Enqueue(line)
		};
		Stopwatch elapsed = Stopwatch.StartNew();
		ProcessIdentity? descendantIdentity = null;

		try {
			// Act
			ProcessExecutionResult result = await sut.ExecuteWithRealtimeOutputAsync(options);
			elapsed.Stop();
			descendantIdentity = ReadDescendantIdentity(descendantIdentityPath);

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
			lines.Should().ContainSingle().Which.Should().Be("parent-exited",
				because: "cancellation must flush the pending unterminated fragment to real-time consumers");
			elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
				because: "the silent thirty-second descendant must not extend the two-second operation budget");
		} finally {
			if (descendantIdentity is not null) {
				await TerminateProcessAsync(descendantIdentity);
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
		string descendantIdentityPath = Path.Combine(directory, "descendant.identity");
		ProcessExecutionOptions options = new(ResolveFixtureExecutable(),
			$"--overflow-output-with-inherited-handle-descendant \"{descendantIdentityPath}\"") {
			MaximumCapturedOutputCharacters = 64
		};
		Stopwatch elapsed = Stopwatch.StartNew();
		ProcessIdentity? descendantIdentity = null;

		try {
			// Act
			ProcessExecutionResult result = await sut.ExecuteAndCaptureAsync(options);
			elapsed.Stop();
			descendantIdentity = ReadDescendantIdentity(descendantIdentityPath);

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
			if (descendantIdentity is not null) {
				await TerminateProcessAsync(descendantIdentity);
			}
			Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	[Description("Verifies that directory monitoring continues while a descendant retains redirected handles after the parent exits.")]
	public async Task ExecuteAndCaptureAsync_ShouldApplyDirectoryLimit_DuringPostExitDrain() {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();
		ProcessExecutor sut = new(logger);
		string directory = Path.Combine(Path.GetTempPath(), $"clio-process-directory-limit-{Guid.NewGuid():N}");
		string monitoredDirectory = Path.Combine(directory, "checkout");
		Directory.CreateDirectory(monitoredDirectory);
		string descendantIdentityPath = Path.Combine(directory, "descendant.identity");
		ProcessExecutionOptions options = new(ResolveFixtureExecutable(),
			$"--spawn-growing-inherited-handle-descendant \"{descendantIdentityPath}\" \"{monitoredDirectory}\"") {
			Timeout = TimeSpan.FromSeconds(5),
			MonitoredDirectory = monitoredDirectory,
			MaximumMonitoredDirectoryBytes = 1024,
			ResourceMonitorInterval = TimeSpan.FromMilliseconds(50)
		};
		Stopwatch elapsed = Stopwatch.StartNew();
		ProcessIdentity? descendantIdentity = null;

		try {
			// Act
			ProcessExecutionResult result = await sut.ExecuteAndCaptureAsync(options);
			elapsed.Stop();
			descendantIdentity = ReadDescendantIdentity(descendantIdentityPath);

			// Assert
			result.ResourceLimitExceeded.Should().BeTrue(
				because: "a descendant must remain subject to the checkout limit while stream draining is pending");
			result.TimedOut.Should().BeFalse(
				because: "late directory growth must retain resource-limit classification instead of waiting for timeout");
			result.StandardOutput.Should().Be("parent-exited",
				because: "output captured before the late resource violation must remain available");
			elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3),
				because: "the fifty-millisecond monitor must detect growth well before the five-second timeout");
		} finally {
			if (descendantIdentity is not null) {
				await TerminateProcessAsync(descendantIdentity);
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

	[Test]
	[Description("Verifies that a captured child reading standard input to completion sees end-of-file and exits.")]
	public async Task ExecuteAndCaptureAsync_ShouldGiveTheChildEndOfFileOnStandardInput_WhenNoInputIsSupplied() {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();
		ProcessExecutor sut = new(logger);
		string markerPath = Path.Combine(Path.GetTempPath(), $"clio-process-stdin-eof-{Guid.NewGuid():N}.marker");
		ProcessExecutionOptions options = new(ResolveFixtureExecutable(),
			$"--read-standard-input-to-end \"{markerPath}\"") {
			Timeout = TimeSpan.FromSeconds(20)
		};

		try {
			// Act
			ProcessExecutionResult result = await sut.ExecuteAndCaptureAsync(options);

			// Assert
			// NOTE: on a host whose own stdin is already at EOF - which is what dotnet test gives a CAPTURED
			// child on Windows - this passes with the fix reverted too. The invariant itself is pinned by
			// ProcessExecutorTests.CreateStartInfo_ShouldAlwaysRedirectStandardInput; this test covers the
			// end-to-end behaviour and the write/close path.
			result.TimedOut.Should().BeFalse(
				because: "a child that reads standard input to completion must reach end-of-file rather than "
					+ "block on a handle nobody will write to");
			result.ExitCode.Should().Be(0,
				because: "the child completes normally once its standard input reaches end-of-file");
			result.StandardOutput.Should().Be("stdin:0:",
				because: "a child launched without StandardInput must read an empty stream, never bytes from "
					+ "whatever handle this process happens to hold");
		} finally {
			TryDeleteFile(markerPath);
		}
	}

	[Test]
	[Description("Verifies that redirecting standard input for every captured child still delivers a supplied payload.")]
	public async Task ExecuteAndCaptureAsync_ShouldWriteAndCloseStandardInput_WhenInputIsSupplied() {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();
		ProcessExecutor sut = new(logger);
		string markerPath = Path.Combine(Path.GetTempPath(), $"clio-process-stdin-write-{Guid.NewGuid():N}.marker");
		ProcessExecutionOptions options = new(ResolveFixtureExecutable(),
			$"--read-standard-input-to-end \"{markerPath}\"") {
			StandardInput = "payload",
			Timeout = TimeSpan.FromSeconds(20)
		};

		try {
			// Act
			ProcessExecutionResult result = await sut.ExecuteAndCaptureAsync(options);

			// Assert
			result.TimedOut.Should().BeFalse(
				because: "writing the payload must be followed by a close so the child still reaches end-of-file");
			result.StandardOutput.Should().Be("stdin:7:payload",
				because: "the supplied payload must reach the child unchanged now that stdin is always redirected");
		} finally {
			TryDeleteFile(markerPath);
		}
	}

	[Test]
	[Description("Verifies that a detached child reading standard input to completion is released instead of holding the parent handle.")]
	public async Task FireAndForgetAsync_ShouldGiveTheDetachedChildEndOfFileOnStandardInput() {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();
		ProcessExecutor sut = new(logger);
		string markerPath = Path.Combine(Path.GetTempPath(), $"clio-process-stdin-detached-{Guid.NewGuid():N}.marker");
		ProcessExecutionOptions options = new(ResolveFixtureExecutable(),
			$"--read-standard-input-to-end \"{markerPath}\"");

		ProcessLaunchResult launch = null;
		try {
			// Act
			launch = await sut.FireAndForgetAsync(options);
			launch.Started.Should().BeTrue(
				because: "the fixture must launch before its standard input behaviour can be observed");
			string marker = await WaitForMarkerAsync(markerPath, TimeSpan.FromSeconds(20));

			// Assert
			marker.Should().NotBeNull(
				because: "a detached child outlives the launch call, so a standard input handle it inherits is "
					+ "held for the rest of the session - the worse case of the bug this rule exists to prevent");
			marker.Should().Be("0",
				because: "the detached child must read an empty stream rather than bytes from this process handle");
		} finally {
			// The subject of this test is a detached child that never exits. When it fails, that child is
			// still blocked on stdin and still holds the test host's handles, which turns a failed test into
			// a hung run. Identity-checked so a recycled PID is never killed.
			await TerminateFixtureAsync(launch?.ProcessId);
			TryDeleteFile(markerPath);
		}
	}

	[Test]
	[Description("Verifies the fixture itself blocks on an open standard input handle, so the end-of-file assertions above are not vacuous.")]
	public async Task ReadStandardInputFixture_ShouldBlock_UntilTheParentClosesStandardInput() {
		// Arrange
		string markerPath = Path.Combine(Path.GetTempPath(), $"clio-process-stdin-control-{Guid.NewGuid():N}.marker");
		ProcessStartInfo startInfo = new(ResolveFixtureExecutable()) {
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardInput = true
		};
		startInfo.ArgumentList.Add("--read-standard-input-to-end");
		startInfo.ArgumentList.Add(markerPath);
		using Process process = Process.Start(startInfo)
			?? throw new InvalidOperationException("The standard input fixture did not start.");

		try {
			// Act
			string whileHeld = await WaitForMarkerAsync(markerPath, TimeSpan.FromSeconds(2));
			process.StandardInput.Close();
			string afterClose = await WaitForMarkerAsync(markerPath, TimeSpan.FromSeconds(20));

			// Assert
			whileHeld.Should().BeNull(
				because: "an open standard input handle that is never written to and never closed must hold the "
					+ "child indefinitely - this is the negative control that keeps the tests above honest");
			afterClose.Should().Be("0",
				because: "closing the write end must deliver end-of-file and release the child");
		} finally {
			TryKill(process);
			TryDeleteFile(markerPath);
		}
	}

	// Waits for marker CONTENT, not for the file to exist: File.Exists goes true when the writer creates the
	// handle, so an existence poll races the byte and reads an empty string on a loaded agent - a flake that
	// reads exactly like the stdin bug coming back.
	private static async Task<string> WaitForMarkerAsync(string path, TimeSpan timeout) {
		Stopwatch elapsed = Stopwatch.StartNew();
		while (elapsed.Elapsed < timeout) {
			string content = TryReadMarker(path);
			if (content is not null) {
				return content;
			}
			await Task.Delay(50);
		}
		return TryReadMarker(path);
	}

	private static string TryReadMarker(string path) {
		try {
			string content = File.ReadAllText(path);
			return content.Length == 0 ? null : content;
		} catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
			return null;
		}
	}

	// Cleanup runs from a finally, where a throw would replace the assertion failure that matters.
	private static void TryDeleteFile(string path) {
		try {
			File.Delete(path);
		} catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
		}
	}

	private static void TryKill(Process process) {
		try {
			if (!process.HasExited) {
				process.Kill(entireProcessTree: true);
			}
		} catch (Exception exception) when (exception is InvalidOperationException
				or System.ComponentModel.Win32Exception
				or NotSupportedException) {
		}
	}

	private static async Task TerminateFixtureAsync(int? processId) {
		if (processId is not { } id) {
			return;
		}
		using Process process = TryGetProcess(id);
		if (process is null || process.HasExited) {
			return;
		}
		// Identity check before the kill: a bare PID races reuse, and the fixture masquerades as "git".
		string actual = TryGetExecutablePath(process);
		if (actual is null || !string.Equals(
				Path.GetFullPath(ResolveFixtureExecutable()),
				Path.GetFullPath(actual),
				OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) {
			return;
		}
		TryKill(process);
		using CancellationTokenSource cleanupDeadline = new(TimeSpan.FromSeconds(5));
		try {
			await process.WaitForExitAsync(cleanupDeadline.Token);
		} catch (OperationCanceledException) {
		}
	}

	private static string TryGetExecutablePath(Process process) {
		try {
			return process.MainModule?.FileName;
		} catch (Exception exception) when (exception is InvalidOperationException
				or System.ComponentModel.Win32Exception
				or NotSupportedException) {
			return null;
		}
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

	private static ProcessIdentity ReadDescendantIdentity(string descendantIdentityPath) {
		File.Exists(descendantIdentityPath).Should().BeTrue(
			because: "the immediate parent must record the descendant that inherited its redirected handles");
		return JsonSerializer.Deserialize<ProcessIdentity>(File.ReadAllText(descendantIdentityPath))
			?? throw new InvalidDataException("The fixture descendant identity is invalid.");
	}

	private static async Task TerminateProcessAsync(ProcessIdentity identity) {
		using Process process = TryGetProcess(identity.ProcessId);
		if (process is null || process.HasExited) {
			return;
		}
		if (!MatchesIdentity(process, identity)) {
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

	private static bool MatchesIdentity(Process process, ProcessIdentity identity) {
		try {
			StringComparison comparison = OperatingSystem.IsWindows()
				? StringComparison.OrdinalIgnoreCase
				: StringComparison.Ordinal;
			return process.StartTime.ToUniversalTime().Ticks == identity.StartUtcTicks
				&& string.Equals(Path.GetFullPath(process.MainModule!.FileName),
					Path.GetFullPath(identity.ExecutablePath), comparison);
		} catch (Exception exception) when (exception is InvalidOperationException
				or System.ComponentModel.Win32Exception
				or NotSupportedException) {
			return false;
		}
	}

	private sealed record ProcessIdentity(int ProcessId, long StartUtcTicks, string ExecutablePath);

}
