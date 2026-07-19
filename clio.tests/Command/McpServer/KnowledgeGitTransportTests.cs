using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Knowledge;
using Clio.Common;
using Clio.Tests.Infrastructure;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class KnowledgeGitTransportTests {
	[Test]
	[Description("Git transport clones the remote default branch without mutating source settings and returns its resolved commit.")]
	public void Synchronize_ShouldReturnDefaultBranchWithoutMutatingSettings_WhenReferenceIsOmitted() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		IProcessExecutor processExecutor = Substitute.For<IProcessExecutor>();
		const string commit = "0123456789abcdef0123456789abcdef01234567";
		processExecutor.ExecuteAndCaptureAsync(Arg.Any<ProcessExecutionOptions>()).Returns(call => {
			ProcessExecutionOptions options = call.Arg<ProcessExecutionOptions>();
			if (options.Arguments.Contains(" branch --show-current", StringComparison.Ordinal)) {
				return Task.FromResult(Success("main\n"));
			}
			if (options.Arguments.Contains(" rev-parse ", StringComparison.Ordinal)) {
				return Task.FromResult(Success(commit + "\n"));
			}
			return Task.FromResult(Success());
		});
		KnowledgeGitTransport transport = new(processExecutor, fileSystem);
		KnowledgeSourceConfiguration source = GitSource();
		string stagingRoot = TestFileSystem.GetRootedPath("clio", "knowledge-staging");
		KnowledgeTransportRequest request = new(
			"partner",
			source,
			new HashSet<string>(StringComparer.OrdinalIgnoreCase),
			null,
			null,
			null,
			null,
			stagingRoot);

		// Act
		string repositoryPath = fileSystem.Path.Combine(stagingRoot, "repository");
		KnowledgeTransportResult result = transport.Synchronize(request, repositoryPath);

		// Assert
		result.Status.Should().Be(KnowledgeTransportStatus.Downloaded,
			because: "a safe ready artifact at the resolved commit is a downloadable candidate");
		result.ResolvedBranch.Should().Be("main",
			because: "an omitted reference must resolve through the advertised remote default branch");
		result.ResolvedCommit.Should().Be(commit,
			because: "the installed candidate must retain its immutable Git provenance");
		source.Branch.Should().BeNull(
			because: "read-only retrieval must return discovery metadata without mutating source configuration");
		result.CandidatePath.Should().Be(repositoryPath,
			because: "direct Git knowledge is consumed from the cloned checkout rather than a committed ZIP artifact");
		ProcessExecutionOptions[] invocations = processExecutor.ReceivedCalls()
			.Select(call => call.GetArguments().FirstOrDefault())
			.OfType<ProcessExecutionOptions>()
			.ToArray();
		invocations.Should().OnlyContain(invocation => invocation.Program == "git",
			because: "the transport must never invoke repository-provided tools or build scripts");
		invocations.Where(invocation => invocation.Arguments.Contains("clone", StringComparison.Ordinal)
				|| invocation.Arguments.Contains(" -C ", StringComparison.Ordinal))
			.Should().OnlyContain(invocation => invocation.Arguments.Contains("core.hooksPath", StringComparison.Ordinal),
				because: "clone and repository-scoped Git operations must explicitly disable hooks");
		invocations.Should().OnlyContain(invocation => invocation.EnvironmentVariables!["GIT_TERMINAL_PROMPT"] == "0",
			because: "transport retrieval must not prompt for or expose repository credentials");
		invocations.Should().OnlyContain(invocation => invocation.ClearInheritedEnvironment,
			because: "ambient Git configuration and executable-helper variables must not cross the process boundary");
		invocations.Should().OnlyContain(invocation => invocation.ResolveProgramPath,
			because: "Git must be pinned to an absolute executable before the untrusted staging directory is used");
		invocations.Should().OnlyContain(invocation =>
			invocation.InheritedEnvironmentVariableAllowlist.All(variable =>
				!variable.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)),
			because: "no inherited Git variable may redirect helpers, inject configuration, or weaken transport behavior");
		invocations.Should().OnlyContain(invocation =>
			invocation.EnvironmentVariables!["GIT_CONFIG_NOSYSTEM"] == "1"
			&& invocation.EnvironmentVariables["GIT_CONFIG_COUNT"] == "0"
			&& invocation.EnvironmentVariables.ContainsKey("GIT_CONFIG_GLOBAL"),
			because: "the child must use only the transport's explicit disabled Git configuration");
		invocations.Should().OnlyContain(invocation => invocation.MaximumCapturedOutputCharacters == 2 * 1024 * 1024,
			because: "untrusted Git output must remain bounded while each process runs");
		invocations.Where(invocation => invocation.MonitoredDirectory is not null).Should().OnlyContain(invocation =>
			invocation.MaximumMonitoredDirectoryBytes == 256L * 1024 * 1024
			&& invocation.MonitoredDirectory!.StartsWith(stagingRoot, StringComparison.Ordinal),
			because: "mutating Git processes must enforce the checkout boundary while they run");
	}

	[Test]
	[Description("Git synchronization fetches the exact configured commit without following a moving branch.")]
	public void Synchronize_ShouldFetchExactCommit_WhenCommitIsConfigured() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		IProcessExecutor processExecutor = Substitute.For<IProcessExecutor>();
		const string commit = "0123456789abcdef0123456789abcdef01234567";
		processExecutor.ExecuteAndCaptureAsync(Arg.Any<ProcessExecutionOptions>()).Returns(call => {
			ProcessExecutionOptions options = call.Arg<ProcessExecutionOptions>();
			if (options.Arguments.Contains(" rev-parse ", StringComparison.Ordinal)) {
				return Task.FromResult(Success(commit + "\n"));
			}
			return Task.FromResult(Success());
		});
		KnowledgeGitTransport transport = new(processExecutor, fileSystem);
		KnowledgeSourceConfiguration source = GitSource();
		source.Commit = commit;
		string repositoryPath = TestFileSystem.GetRootedPath("clio", "repair-staging", "repository");
		KnowledgeTransportRequest request = new(
			"partner", source, new HashSet<string>(StringComparer.OrdinalIgnoreCase),
			null, null, null, null, TestFileSystem.GetRootedPath("clio", "repair-staging"));

		// Act
		KnowledgeTransportResult result = transport.Synchronize(request, repositoryPath);

		// Assert
		result.ResolvedCommit.Should().Be(commit,
			because: "repair must reproduce the immutable revision recorded by the active generation");
		result.ResolvedBranch.Should().BeNull(
			because: "an exact repair must neither discover nor persist a moving branch");
		ProcessExecutionOptions[] calls = processExecutor.ReceivedCalls()
			.Select(call => call.GetArguments().FirstOrDefault())
			.OfType<ProcessExecutionOptions>()
			.ToArray();
		calls.Should().NotContain(call => call.Arguments.Contains("pull --ff-only", StringComparison.Ordinal),
			because: "an immutable commit source must not follow a moving branch");
		calls.Should().Contain(call => call.Arguments.Contains($"fetch --no-tags --depth=1 origin \"{commit}\"", StringComparison.Ordinal),
			because: "the transport must fetch the configured commit rather than the current branch head");
	}

	[Test]
	[Description("Installed Git checkout validation rejects a clean checkout that differs from its configured immutable commit.")]
	public void ValidateInstalledCheckout_ShouldRejectHead_WhenConfiguredCommitDiffers() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		IProcessExecutor processExecutor = Substitute.For<IProcessExecutor>();
		KnowledgeSourceConfiguration source = GitSource();
		source.Commit = "1111111111111111111111111111111111111111";
		string repositoryPath = TestFileSystem.GetRootedPath("clio", "pinned", "repository");
		AddInstalledRepository(fileSystem, repositoryPath);
		processExecutor.ExecuteAndCaptureAsync(Arg.Any<ProcessExecutionOptions>()).Returns(call => {
			ProcessExecutionOptions options = call.Arg<ProcessExecutionOptions>();
			if (options.Arguments.Contains("remote get-url origin", StringComparison.Ordinal)) {
				return Task.FromResult(Success(source.Location + "\n"));
			}
			if (options.Arguments.Contains("rev-parse HEAD", StringComparison.Ordinal)) {
				return Task.FromResult(Success("2222222222222222222222222222222222222222\n"));
			}
			return Task.FromResult(Success());
		});
		KnowledgeGitTransport transport = new(processExecutor, fileSystem);

		// Act
		Action act = () => transport.ValidateInstalledCheckout(source, repositoryPath);

		// Assert
		act.Should().Throw<InvalidDataException>()
			.WithMessage("*configured commit*",
				because: "an immutable source pin must govern offline activation as well as network synchronization");
	}

	[Test]
	[Description("Installed Git checkout validation rejects locally modified tracked knowledge files.")]
	public void ValidateInstalledCheckout_ShouldRejectModifiedTrackedFiles() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		IProcessExecutor processExecutor = Substitute.For<IProcessExecutor>();
		KnowledgeSourceConfiguration source = GitSource();
		string repositoryPath = TestFileSystem.GetRootedPath("clio", "installed", "repository");
		AddInstalledRepository(fileSystem, repositoryPath);
		processExecutor.ExecuteAndCaptureAsync(Arg.Any<ProcessExecutionOptions>()).Returns(call => {
			ProcessExecutionOptions options = call.Arg<ProcessExecutionOptions>();
			if (options.Arguments.Contains("remote get-url origin", StringComparison.Ordinal)) {
				return Task.FromResult(Success(source.Location + "\n"));
			}
			if (options.Arguments.Contains("status --porcelain", StringComparison.Ordinal)) {
				return Task.FromResult(Success(" M bundle-source.json\n"));
			}
			return Task.FromResult(Success());
		});
		KnowledgeGitTransport transport = new(processExecutor, fileSystem);

		// Act
		Action act = () => transport.ValidateInstalledCheckout(source, repositoryPath);

		// Assert
		act.Should().Throw<InvalidDataException>(
			because: "mutable working-tree content must never be attributed to the checkout's HEAD revision");
	}

	[Test]
	[Description("Git synchronization rejects filesystem links before invoking Git in an existing checkout.")]
	public void ValidateCheckoutForSynchronization_ShouldRejectReparsePoint_BeforeInvokingGit() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		IProcessExecutor processExecutor = Substitute.For<IProcessExecutor>();
		string repositoryPath = TestFileSystem.GetRootedPath("clio", "linked", "repository");
		AddInstalledRepository(fileSystem, repositoryPath);
		string guidancePath = fileSystem.Path.Combine(repositoryPath, "guidance");
		fileSystem.AddDirectory(guidancePath);
		fileSystem.File.SetAttributes(guidancePath, FileAttributes.Directory | FileAttributes.ReparsePoint);
		KnowledgeGitTransport transport = new(processExecutor, fileSystem);

		// Act
		Action act = () => transport.ValidateCheckoutForSynchronization(GitSource(), repositoryPath);

		// Assert
		act.Should().Throw<InvalidDataException>().WithMessage("*links or junctions*",
			because: "Git must never mutate through a tracked directory redirected outside the managed checkout");
		processExecutor.DidNotReceive().ExecuteAndCaptureAsync(Arg.Any<ProcessExecutionOptions>());
	}

	[Test]
	[Description("Git synchronization rejects executable repository-local configuration before invoking Git.")]
	public void ValidateCheckoutForSynchronization_ShouldRejectExecutableLocalConfiguration_BeforeInvokingGit() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		IProcessExecutor processExecutor = Substitute.For<IProcessExecutor>();
		string repositoryPath = TestFileSystem.GetRootedPath("clio", "configured", "repository");
		AddInstalledRepository(fileSystem, repositoryPath,
			"[core]\n\trepositoryformatversion = 0\n\tfsmonitor = malicious-command\n");
		KnowledgeGitTransport transport = new(processExecutor, fileSystem);

		// Act
		Action act = () => transport.ValidateCheckoutForSynchronization(GitSource(), repositoryPath);

		// Assert
		act.Should().Throw<InvalidDataException>().WithMessage("*unsupported settings*",
			because: "repository-local Git settings must not execute helpers before checkout validation");
		processExecutor.DidNotReceive().ExecuteAndCaptureAsync(Arg.Any<ProcessExecutionOptions>());
	}

	[Test]
	[Description("Git transport stops before starting another process when the operation-wide retrieval deadline has elapsed.")]
	public void Retrieve_ShouldFailBeforeNextProcess_WhenOperationDeadlineElapsed() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		IProcessExecutor processExecutor = Substitute.For<IProcessExecutor>();
		processExecutor.ExecuteAndCaptureAsync(Arg.Any<ProcessExecutionOptions>()).Returns(call => {
			Thread.Sleep(150);
			return Task.FromResult(Success("ref: refs/heads/main\tHEAD\n"));
		});
		KnowledgeGitTransport transport = new(processExecutor, fileSystem);
		KnowledgeTransportRequest request = new(
			"partner",
			GitSource(),
			new HashSet<string>(StringComparer.OrdinalIgnoreCase),
			null,
			null,
			null,
			null,
			TestFileSystem.GetRootedPath("clio", "deadline-staging"),
			TransportDeadlineMilliseconds: 100);

		// Act
		KnowledgeTransportResult result = transport.Synchronize(
			request,
			fileSystem.Path.Combine(request.StagingDirectory, "repository"));

		// Assert
		result.Status.Should().Be(KnowledgeTransportStatus.Failed,
			because: "elapsed operation time must not be reset for each sequential Git command");
		result.Diagnostic.Should().Contain("operation-wide",
			because: "the caller must be able to distinguish shared-deadline exhaustion from candidate rejection");
		processExecutor.ReceivedCalls().Should().ContainSingle(
			because: "the transport must refuse to start the next Git process after the shared budget expires");
		ProcessExecutionOptions invocation = processExecutor.ReceivedCalls().Single()
			.GetArguments().OfType<ProcessExecutionOptions>().Single();
		invocation.Timeout.Should().NotBeNull(
			because: "every launched Git process needs an explicit remaining timeout");
		invocation.Timeout!.Value.Should().BeGreaterThan(TimeSpan.Zero,
			because: "every launched Git process needs a positive remaining timeout");
		invocation.Timeout.Value.Should().BeLessThanOrEqualTo(TimeSpan.FromMilliseconds(100),
			because: "a subprocess may receive only the operation's remaining budget");
	}

	private static KnowledgeSourceConfiguration GitSource() => new() {
		LibraryId = "com.example.partner",
		Type = KnowledgeSourceType.Git,
		Location = "https://example.invalid/knowledge.git",
		Enabled = true,
		Participation = KnowledgeSourceParticipation.Supplement
	};

	private static void AddInstalledRepository(
		MockFileSystem fileSystem,
		string repositoryPath,
		string config = "[core]\n\trepositoryformatversion = 0\n") {
		string gitPath = fileSystem.Path.Combine(repositoryPath, ".git");
		fileSystem.AddDirectory(gitPath);
		fileSystem.AddFile(fileSystem.Path.Combine(gitPath, "config"), config);
	}

	private static ProcessExecutionResult Success(string output = "") => new() {
		Started = true,
		ExitCode = 0,
		StandardOutput = output
	};

}
