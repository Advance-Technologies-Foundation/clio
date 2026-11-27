using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Common.McpWorker;
using Clio.Common.Skills;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.Skills;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class ToolkitReleaseProbeTests {
	private const string Commit = "6b296e3929701ac438af71302681bfa96002e78e";

	[Test]
	[Description("The release commit is taken from the single line whose ref is exactly refs/heads/release.")]
	public void ParseReleaseCommit_ShouldReturnCommit_WhenExactlyOneReleaseLineExists() {
		// Arrange
		string output = $"1111111111111111111111111111111111111111\trefs/heads/feature/release\n{Commit.ToUpperInvariant()}\trefs/heads/release\n";

		// Act
		string commit = ToolkitReleaseProbe.ParseReleaseCommit(output);

		// Assert
		commit.Should().Be(Commit, because: "a branch merely ending in 'release' is not the release branch");
	}

	[TestCase("")]
	[TestCase("6b296e3\trefs/heads/release\n")]
	[TestCase("1111111111111111111111111111111111111111\trefs/heads/main\n")]
	[TestCase("1111111111111111111111111111111111111111\trefs/heads/release\n2222222222222222222222222222222222222222\trefs/heads/release\n")]
	[Description("Anything other than exactly one well-formed release line is unknown, never a commit to compare.")]
	public void ParseReleaseCommit_ShouldReturnNull_WhenOutputIsNotExactlyOneReleaseCommit(string output) {
		// Arrange / Act
		string commit = ToolkitReleaseProbe.ParseReleaseCommit(output);

		// Assert
		commit.Should().BeNull(because: "an unknown release commit must make the caller update, not skip");
	}

	[Test]
	[Description("The probe asks git for the exact release ref of the default toolkit repository, bounded and without prompts.")]
	public void ReadReleaseCommit_ShouldQueryExactReleaseRef_WithTimeoutAndNoPrompt() {
		// Arrange
		IProcessExecutor executor = Substitute.For<IProcessExecutor>();
		ProcessExecutionOptions captured = null;
		executor.ExecuteAndCaptureAsync(Arg.Do<ProcessExecutionOptions>(options => captured = options))
			.Returns(Task.FromResult(new ProcessExecutionResult {
				Started = true, ExitCode = 0, StandardOutput = $"{Commit}\trefs/heads/release\n"
			}));
		ToolkitReleaseProbe sut = new(executor);

		// Act
		string commit = sut.ReadReleaseCommit();

		// Assert
		commit.Should().Be(Commit, because: "a successful query returns the published release commit");
		captured.ArgumentList.Should().Equal(["ls-remote", ToolkitDistribution.MarketplaceGitUrl, "refs/heads/release"],
			because: "the query must name the fully qualified release ref of the default source");
		captured.Timeout.Should().Be(ToolkitReleaseProbe.QueryTimeout, because: "a hung remote must not stall the update");
		captured.EnvironmentVariables.Should().Contain("GIT_TERMINAL_PROMPT", "0",
			because: "a credential prompt would wait forever under a detached or MCP caller");
	}

	[Test]
	[Description("A failed, timed-out, or unlaunchable git call yields an unknown release commit instead of an error.")]
	public void ReadReleaseCommit_ShouldReturnNull_WhenGitFails() {
		// Arrange
		IProcessExecutor failing = Substitute.For<IProcessExecutor>();
		failing.ExecuteAndCaptureAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(Task.FromResult(new ProcessExecutionResult { Started = true, ExitCode = 128 }));
		IProcessExecutor timingOut = Substitute.For<IProcessExecutor>();
		timingOut.ExecuteAndCaptureAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(Task.FromResult(new ProcessExecutionResult {
				Started = true, ExitCode = 0, TimedOut = true, StandardOutput = $"{Commit}\trefs/heads/release\n"
			}));
		IProcessExecutor missingGit = Substitute.For<IProcessExecutor>();
		missingGit.ExecuteAndCaptureAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns<Task<ProcessExecutionResult>>(_ => throw new FileNotFoundException("git"));

		// Act
		string[] commits = [
			new ToolkitReleaseProbe(failing).ReadReleaseCommit(),
			new ToolkitReleaseProbe(timingOut).ReadReleaseCommit(),
			new ToolkitReleaseProbe(missingGit).ReadReleaseCommit()
		];

		// Assert
		commits.Should().OnlyContain(commit => commit == null,
			because: "every failure mode must fall back to updating, and none may fail the command");
	}
}

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
[NonParallelizable]
public sealed class ToolkitOperationLockTests {
	private string _clioDir;
	private ToolkitOperationLock _sut;

	[SetUp]
	public void SetUp() {
		_clioDir = Path.Combine(Path.GetTempPath(), "clio-toolkit-lock-" + Guid.NewGuid().ToString("N"));
		IUserHomeProvider home = Substitute.For<IUserHomeProvider>();
		home.GetClioDir().Returns(_clioDir);
		_sut = new ToolkitOperationLock(new System.IO.Abstractions.FileSystem(), home);
	}

	[TearDown]
	public void TearDown() {
		if (Directory.Exists(_clioDir)) {
			Directory.Delete(_clioDir, recursive: true);
		}
	}

	[Test]
	[Description("A second toolkit operation is refused while the first holds the lock, and admitted once it is released.")]
	public void TryAcquire_ShouldRefuseSecondHolder_UntilFirstIsReleased() {
		// Arrange
		IDisposable first = _sut.TryAcquire();

		// Act
		IDisposable whileHeld = _sut.TryAcquire();
		first?.Dispose();
		using IDisposable afterRelease = _sut.TryAcquire();

		// Assert
		first.Should().NotBeNull(because: "the first operation takes a free lock");
		whileHeld.Should().BeNull(because: "two toolkit operations must never interleave their agent CLI sequences");
		afterRelease.Should().NotBeNull(because: "releasing the handle frees the lock for the next operation");
	}

	[Test]
	[Description("When the lock file cannot be created, toolkit operations run unguarded instead of being refused as 'in progress'.")]
	public void TryAcquire_ShouldProceedUnguarded_WhenLockFileCannotBeCreated() {
		// Arrange
		Directory.CreateDirectory(Path.Combine(_clioDir, ToolkitOperationLock.LockFileName));

		// Act
		using IDisposable handle = _sut.TryAcquire();
		using IDisposable second = _sut.TryAcquire();

		// Assert
		handle.Should().NotBeNull(because: "a broken lock location is not another running operation");
		second.Should().NotBeNull(because: "an unusable lock must not block every later toolkit operation either");
	}
}

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class ToolkitAutoUpdateLauncherTests {
	[Test]
	[Description("The launcher starts this clio build as a detached update-toolkit child with its output discarded.")]
	public void Launch_ShouldStartDetachedUpdateToolkitChild_WithOutputDiscarded() {
		// Arrange
		IClioExecutablePathProvider pathProvider = Substitute.For<IClioExecutablePathProvider>();
		pathProvider.Resolve(Arg.Any<string[]>()).Returns(call => new ClioWorkerLaunchDescriptor(
			"dotnet", ["clio.dll", .. call.Arg<string[]>()], "work"));
		IProcessExecutor executor = Substitute.For<IProcessExecutor>();
		ProcessExecutionOptions captured = null;
		executor.FireAndForgetAsync(Arg.Do<ProcessExecutionOptions>(options => captured = options))
			.Returns(Task.FromResult(new ProcessLaunchResult { Started = true }));
		ToolkitAutoUpdateLauncher sut = new(pathProvider, executor);

		// Act
		bool started = sut.Launch();

		// Assert
		started.Should().BeTrue(because: "the launcher reports whether the child started");
		captured.Program.Should().Be("dotnet", because: "the running clio build is re-launched, not whatever 'clio' is on PATH");
		captured.ArgumentList.Should().Equal(["clio.dll", "update-toolkit"],
			because: "the child runs the update-toolkit command of the same build");
		captured.DiscardOutput.Should().BeTrue(
			because: "the child outlives its parent and must not write into the terminal or a JSON-RPC stream");
		captured.WorkingDirectory.Should().Be(Path.GetFullPath(Clio.SettingsRepository.AppSettingsFolderPath),
			because: "the child must not hold the tool's install folder, which a later dotnet tool update replaces");
		executor.DidNotReceive().ExecuteAndCaptureAsync(Arg.Any<ProcessExecutionOptions>());
	}

	[Test]
	[Description("A child that fails to start is reported as not launched.")]
	public void Launch_ShouldReturnFalse_WhenChildDoesNotStart() {
		// Arrange
		IClioExecutablePathProvider pathProvider = Substitute.For<IClioExecutablePathProvider>();
		pathProvider.Resolve(Arg.Any<string[]>()).Returns(new ClioWorkerLaunchDescriptor("clio", [], "work"));
		IProcessExecutor executor = Substitute.For<IProcessExecutor>();
		executor.FireAndForgetAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(Task.FromResult(new ProcessLaunchResult { Started = false, ErrorMessage = "denied" }));

		// Act
		bool started = new ToolkitAutoUpdateLauncher(pathProvider, executor).Launch();

		// Assert
		started.Should().BeFalse(because: "a launch failure must not be reported as a started update");
	}
}
