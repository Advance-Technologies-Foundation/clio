using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;
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
	[Description("Git transport discovers the remote default branch without mutating settings and retrieves a ready artifact without checkout or build execution.")]
	public void Retrieve_ShouldReturnDefaultBranchWithoutMutatingSettings_WhenReferenceIsOmitted() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		IProcessExecutor processExecutor = Substitute.For<IProcessExecutor>();
		const string commit = "0123456789abcdef0123456789abcdef01234567";
		byte[] expectedBundle = [0x50, 0x4B, 0x03, 0x04];
		processExecutor.ExecuteAndCaptureAsync(Arg.Any<ProcessExecutionOptions>()).Returns(call => {
			ProcessExecutionOptions options = call.Arg<ProcessExecutionOptions>();
			if (options.Arguments.StartsWith("ls-remote", StringComparison.Ordinal)) {
				return Task.FromResult(Success("ref: refs/heads/main\tHEAD\n"));
			}
			if (options.Arguments.Contains(" rev-parse ", StringComparison.Ordinal)) {
				return Task.FromResult(Success(commit + "\n"));
			}
			if (options.Arguments.Contains(" ls-tree ", StringComparison.Ordinal)) {
				return Task.FromResult(Success($"100644 blob {commit}\tknowledge-bundle.zip\n"));
			}
			if (options.Arguments.Contains(" archive ", StringComparison.Ordinal)) {
				string archivePath = ReadQuotedValue(options.Arguments, "--output=");
				using Stream output = fileSystem.File.Create(archivePath);
				using ZipArchive archive = new(output, ZipArchiveMode.Create);
				ZipArchiveEntry entry = archive.CreateEntry("knowledge-bundle.zip");
				using Stream entryOutput = entry.Open();
				entryOutput.Write(expectedBundle);
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
		KnowledgeTransportResult result = transport.Retrieve(request);

		// Assert
		result.Status.Should().Be(KnowledgeTransportStatus.Downloaded,
			because: "a safe ready artifact at the resolved commit is a downloadable candidate");
		result.ResolvedBranch.Should().Be("main",
			because: "an omitted reference must resolve through the advertised remote default branch");
		result.ResolvedCommit.Should().Be(commit,
			because: "the installed candidate must retain its immutable Git provenance");
		source.Branch.Should().BeNull(
			because: "read-only retrieval must return discovery metadata without mutating source configuration");
		fileSystem.File.ReadAllBytes(result.CandidatePath!).Should().Equal(expectedBundle,
			because: "transport extraction must preserve the declared bundle artifact bytes");
		ProcessExecutionOptions[] invocations = processExecutor.ReceivedCalls()
			.Select(call => call.GetArguments().FirstOrDefault())
			.OfType<ProcessExecutionOptions>()
			.ToArray();
		invocations.Should().OnlyContain(invocation => invocation.Program == "git",
			because: "the transport must never invoke repository-provided tools or build scripts");
		invocations.Should().NotContain(invocation => invocation.Arguments.Contains("checkout", StringComparison.Ordinal),
			because: "archive-based retrieval avoids checkout hooks, filters, and live working-tree content");
		invocations.Where(invocation => invocation.Arguments.Contains(" -c ", StringComparison.Ordinal))
			.Should().OnlyContain(invocation => invocation.Arguments.Contains("core.hooksPath", StringComparison.Ordinal),
				because: "every repository-scoped Git operation must explicitly disable hooks");
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
		invocations.Should().OnlyContain(invocation => invocation.MaximumCapturedOutputCharacters == 64 * 1024,
			because: "untrusted Git output must remain bounded while each process runs");
		invocations.Should().OnlyContain(invocation =>
			invocation.MaximumMonitoredDirectoryBytes == 64L * 1024 * 1024
			&& invocation.MonitoredDirectory!.StartsWith(stagingRoot, StringComparison.Ordinal),
			because: "each Git process must enforce the staging boundary while it is running");
	}

	[Test]
	[Description("Git repair retrieves the exact installed commit without resolving or following the current branch head.")]
	public void Retrieve_ShouldFetchExactCommit_WhenRepairRevisionIsSpecified() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		IProcessExecutor processExecutor = Substitute.For<IProcessExecutor>();
		const string commit = "0123456789abcdef0123456789abcdef01234567";
		processExecutor.ExecuteAndCaptureAsync(Arg.Any<ProcessExecutionOptions>()).Returns(call => {
			ProcessExecutionOptions options = call.Arg<ProcessExecutionOptions>();
			if (options.Arguments.Contains(" rev-parse ", StringComparison.Ordinal)) {
				return Task.FromResult(Success(commit + "\n"));
			}
			if (options.Arguments.Contains(" ls-tree ", StringComparison.Ordinal)) {
				return Task.FromResult(Success($"100644 blob {commit}\tknowledge-bundle.zip\n"));
			}
			if (options.Arguments.Contains(" archive ", StringComparison.Ordinal)) {
				string archivePath = ReadQuotedValue(options.Arguments, "--output=");
				using Stream output = fileSystem.File.Create(archivePath);
				using ZipArchive archive = new(output, ZipArchiveMode.Create);
				using Stream entry = archive.CreateEntry("knowledge-bundle.zip").Open();
				entry.Write([0x50, 0x4B, 0x03, 0x04]);
			}
			return Task.FromResult(Success());
		});
		KnowledgeGitTransport transport = new(processExecutor, fileSystem);
		KnowledgeTransportRequest request = new(
			"partner", GitSource(), new HashSet<string>(StringComparer.OrdinalIgnoreCase),
			null, null, null, null, TestFileSystem.GetRootedPath("clio", "repair-staging"),
			ExactRevision: commit);

		// Act
		KnowledgeTransportResult result = transport.Retrieve(request);

		// Assert
		result.ResolvedCommit.Should().Be(commit,
			because: "repair must reproduce the immutable revision recorded by the active generation");
		result.ResolvedBranch.Should().BeNull(
			because: "an exact repair must neither discover nor persist a moving branch");
		ProcessExecutionOptions[] calls = processExecutor.ReceivedCalls()
			.Select(call => call.GetArguments().FirstOrDefault())
			.OfType<ProcessExecutionOptions>()
			.ToArray();
		calls.Should().NotContain(call => call.Arguments.StartsWith("ls-remote", StringComparison.Ordinal),
			because: "the recorded commit is sufficient and branch discovery could repair from a different generation");
		calls.Should().Contain(call => call.Arguments.Contains($"fetch --no-tags --depth=1 --filter=blob:none \"https://example.invalid/knowledge.git\" \"{commit}\"", StringComparison.Ordinal),
			because: "the transport must fetch the exact recorded commit rather than the current branch head");
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
		KnowledgeTransportResult result = transport.Retrieve(request);

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

	[Test]
	[Description("Git tree validation rejects submodules before reading the declared bundle artifact.")]
	public void ValidateTree_ShouldRejectRepository_WhenSubmoduleExists() {
		// Arrange
		const string tree =
			"160000 commit 0123456789abcdef0123456789abcdef01234567\tvendor/knowledge\n" +
			"100644 blob 0123456789abcdef0123456789abcdef01234567\tknowledge-bundle.zip\n";

		// Act
		Action action = () => KnowledgeGitTransport.ValidateTree(tree, "knowledge-bundle.zip");

		// Assert
		action.Should().Throw<InvalidDataException>()
			.WithMessage("*submodules*",
				because: "submodule URLs and checkout behavior are outside the trusted source contract");
	}

	[Test]
	[Description("Git tree validation rejects symbolic links even when the link is unrelated to the artifact.")]
	public void ValidateTree_ShouldRejectRepository_WhenSymbolicLinkExists() {
		// Arrange
		const string tree =
			"120000 blob 0123456789abcdef0123456789abcdef01234567\tdocs-link\n" +
			"100644 blob 0123456789abcdef0123456789abcdef01234567\tknowledge-bundle.zip\n";

		// Act
		Action action = () => KnowledgeGitTransport.ValidateTree(tree, "knowledge-bundle.zip");

		// Assert
		action.Should().Throw<InvalidDataException>()
			.WithMessage("*symbolic links*",
				because: "a repository symlink could escape the bounded staging and trust boundary");
	}

	private static KnowledgeSourceConfiguration GitSource() => new() {
		LibraryId = "com.example.partner",
		Type = KnowledgeSourceType.Git,
		Location = "https://example.invalid/knowledge.git",
		TrustedKeyId = "test-signing-key",
		TrustedPublicKeyPath = TestFileSystem.GetRootedPath("keys", "test-public.pem"),
		Enabled = true,
		Participation = KnowledgeSourceParticipation.Supplement
	};

	private static ProcessExecutionResult Success(string output = "") => new() {
		Started = true,
		ExitCode = 0,
		StandardOutput = output
	};

	private static string ReadQuotedValue(string arguments, string prefix) {
		int start = arguments.IndexOf(prefix + "\"", StringComparison.Ordinal) + prefix.Length + 1;
		int end = arguments.IndexOf('"', start);
		return arguments[start..end];
	}
}
