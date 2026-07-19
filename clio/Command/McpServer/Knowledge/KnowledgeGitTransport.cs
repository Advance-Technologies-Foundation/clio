using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Clio.Common;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command.McpServer.Knowledge;

internal sealed class KnowledgeGitTransport : IKnowledgeTransport {
	private const int MaxBundleBytes = 40 * 1024 * 1024;
	private const long MaxStagingBytes = 64L * 1024 * 1024;
	private const int MaxCapturedOutputCharacters = 64 * 1024;
	private const int DefaultTransportDeadlineMilliseconds = 30_000;
	private static readonly IReadOnlyDictionary<string, string> GitEnvironment =
		new Dictionary<string, string>(StringComparer.Ordinal) {
			["GIT_TERMINAL_PROMPT"] = "0",
			["GIT_CONFIG_NOSYSTEM"] = "1",
			["GIT_CONFIG_COUNT"] = "0"
		};
	private static readonly IReadOnlyCollection<string> GitInheritedEnvironmentAllowlist = [
		"SystemRoot",
		"WINDIR",
		"COMSPEC",
		"TEMP",
		"TMP",
		"TMPDIR",
		"LANG",
		"LC_ALL",
		"LC_CTYPE",
		"HTTP_PROXY",
		"HTTPS_PROXY",
		"ALL_PROXY",
		"NO_PROXY",
		"http_proxy",
		"https_proxy",
		"all_proxy",
		"no_proxy",
		"SSL_CERT_FILE",
		"SSL_CERT_DIR",
		"CURL_CA_BUNDLE"
	];

	private readonly IProcessExecutor _processExecutor;
	private readonly IFileSystem _fileSystem;

	public KnowledgeGitTransport(
		IProcessExecutor processExecutor,
		IFileSystem fileSystem) {
		_processExecutor = processExecutor ?? throw new ArgumentNullException(nameof(processExecutor));
		_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
	}

	public KnowledgeSourceType Type => KnowledgeSourceType.Git;

	public KnowledgeTransportResult Retrieve(KnowledgeTransportRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		Stopwatch operation = Stopwatch.StartNew();
		TimeSpan deadline = TimeSpan.FromMilliseconds(request.TransportDeadlineMilliseconds is > 0
			? request.TransportDeadlineMilliseconds.Value
			: DefaultTransportDeadlineMilliseconds);
		KnowledgeSourceConfiguration source = KnowledgeSourceConfigurationValidator.ValidateAndClone(request.Source);
		if (source.Type != Type) {
			throw new ArgumentException("Git transport received a non-Git source.", nameof(request));
		}
		if (string.IsNullOrWhiteSpace(request.StagingDirectory)
				|| !_fileSystem.Path.IsPathFullyQualified(request.StagingDirectory)) {
			throw new ArgumentException("Git transport staging directory must be absolute.", nameof(request));
		}

		string operationDirectory = _fileSystem.Path.Combine(
			request.StagingDirectory,
			$"git-{Guid.NewGuid():N}");
		string repositoryPath = _fileSystem.Path.Combine(operationDirectory, "repository.git");
		string hooksPath = _fileSystem.Path.Combine(operationDirectory, "disabled-hooks");
		string archivePath = _fileSystem.Path.Combine(operationDirectory, "artifact.zip");
		string candidatePath = _fileSystem.Path.Combine(operationDirectory, "knowledge-bundle.zip");
		try {
			_fileSystem.Directory.CreateDirectory(operationDirectory);
			_fileSystem.Directory.CreateDirectory(hooksPath);
			string? exactCommit = request.ExactRevision;
			if (exactCommit is not null && !IsCompleteCommit(exactCommit)) {
				throw new InvalidDataException("The exact Git repair revision is not a complete commit ID.");
			}
			string? branch = source.Branch;
			if (source.Commit is null && source.Tag is null && branch is null && exactCommit is null) {
				ProcessExecutionResult defaultBranchResult = Execute(
					$"ls-remote --symref {Quote(source.Location)} HEAD",
					operationDirectory,
					GetRemainingTimeout(operation, deadline));
				branch = ParseDefaultBranch(defaultBranchResult.StandardOutput);
			}

			Execute($"init --bare {Quote(repositoryPath)}", operationDirectory,
				GetRemainingTimeout(operation, deadline));
			string fetchReference = exactCommit ?? GetFetchReference(source, branch);
			ExecuteGit(repositoryPath,
				$"fetch --no-tags --depth=1 --filter=blob:none {Quote(source.Location)} {Quote(fetchReference)}",
				GetRemainingTimeout(operation, deadline));
			EnsureStagingBound(operationDirectory);
			string revisionExpression = exactCommit is null ? GetRevisionExpression(source, branch) : "FETCH_HEAD";
			string commit = ExecuteGit(repositoryPath, $"rev-parse {Quote(revisionExpression)}^{{commit}}",
				GetRemainingTimeout(operation, deadline)).StandardOutput.Trim();
			if (!IsCompleteCommit(commit)) {
				throw new InvalidDataException("Git returned an invalid resolved commit ID.");
			}
			if (request.RejectedRevisions.Contains(commit)) {
				KnowledgeTransportResult rejected = Rejected(
					source,
					branch,
					commit,
					"The resolved Git commit was previously rejected.");
				TryDelete(operationDirectory);
				return rejected;
			}
			if (string.Equals(request.ActiveRevision, commit, StringComparison.OrdinalIgnoreCase)) {
				KnowledgeTransportResult noCandidate = NoCandidate(source, branch, commit);
				TryDelete(operationDirectory);
				return noCandidate;
			}

			string tree = ExecuteGit(repositoryPath,
				$"ls-tree {Quote(commit)} -- {Quote(source.ArtifactPath!)}",
				GetRemainingTimeout(operation, deadline)).StandardOutput;
			ValidateTree(tree, source.ArtifactPath!);
			ExecuteGit(repositoryPath,
				$"archive --format=zip --output={Quote(archivePath)} {Quote(commit)} -- {Quote(source.ArtifactPath!)}",
				GetRemainingTimeout(operation, deadline));
			EnsureStagingBound(operationDirectory);
			ExtractArtifact(archivePath, source.ArtifactPath!, candidatePath);
			return new KnowledgeTransportResult(
				KnowledgeTransportStatus.Downloaded,
				commit,
				null,
				candidatePath,
				ResolvedBranch: exactCommit is null ? branch : null,
				ResolvedTag: source.Tag,
				ResolvedCommit: commit);
		} catch (TimeoutException exception) {
			TryDelete(operationDirectory);
			return new KnowledgeTransportResult(
				KnowledgeTransportStatus.Failed,
				null,
				null,
				null,
				Diagnostic: exception.Message);
		} catch (InvalidOperationException exception) {
			TryDelete(operationDirectory);
			return new KnowledgeTransportResult(
				KnowledgeTransportStatus.Rejected,
				null,
				null,
				null,
				Diagnostic: exception.Message);
		} catch (Exception exception) when (exception is IOException
				or UnauthorizedAccessException
				or InvalidDataException
				or ArgumentException
				or NotSupportedException) {
			TryDelete(operationDirectory);
			return new KnowledgeTransportResult(
				KnowledgeTransportStatus.Rejected,
				null,
				null,
				null,
				Diagnostic: exception.Message);
		}
	}

	internal static string ParseDefaultBranch(string output) {
		string? branch = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
			.Select(line => line.Split('\t'))
			.Where(parts => parts.Length == 2 && string.Equals(parts[1], "HEAD", StringComparison.Ordinal))
			.Select(parts => parts[0])
			.Where(value => value.StartsWith("ref: refs/heads/", StringComparison.Ordinal))
			.Select(value => value["ref: refs/heads/".Length..])
			.FirstOrDefault();
		if (branch is null) {
			throw new InvalidDataException("Git remote did not advertise a default branch.");
		}
		KnowledgeSourceConfiguration probe = new() {
			LibraryId = "com.clio.validation",
			Type = KnowledgeSourceType.Git,
			Location = "https://localhost/repository.git",
			TrustedKeyId = "branch-validation",
			TrustedPublicKeyPath = Path.GetFullPath("branch-validation-public.pem"),
			Branch = branch
		};
		return KnowledgeSourceConfigurationValidator.ValidateAndClone(probe).Branch!;
	}

	internal static void ValidateTree(string output, string artifactPath) {
		bool artifactFound = false;
		foreach (string line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)) {
			int tab = line.IndexOf('\t');
			if (tab <= 0) {
				throw new InvalidDataException("Git tree output is malformed.");
			}
			string[] metadata = line[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (metadata.Length != 3) {
				throw new InvalidDataException("Git tree output is malformed.");
			}
			if (metadata[0] == "160000") {
				throw new InvalidDataException("Git knowledge repositories cannot contain submodules.");
			}
			if (metadata[0] == "120000") {
				throw new InvalidDataException("Git knowledge repositories cannot contain symbolic links.");
			}
			if (string.Equals(line[(tab + 1)..], artifactPath, StringComparison.Ordinal)) {
				artifactFound = metadata[0] is "100644" or "100755" && metadata[1] == "blob";
			}
		}
		if (!artifactFound) {
			throw new InvalidDataException($"Git knowledge artifact '{artifactPath}' was not found as a regular file.");
		}
	}

	private ProcessExecutionResult ExecuteGit(string repositoryPath, string arguments, TimeSpan timeout) =>
		Execute(
			$"-C {Quote(repositoryPath)} -c {Quote("core.hooksPath=" + _fileSystem.Path.Combine(
				_fileSystem.Path.GetDirectoryName(repositoryPath)!, "disabled-hooks"))} " + arguments,
			_fileSystem.Path.GetDirectoryName(repositoryPath)!,
			timeout);

	private ProcessExecutionResult Execute(string arguments, string workingDirectory, TimeSpan timeout) {
		Dictionary<string, string> environment = new(GitEnvironment, StringComparer.Ordinal) {
			["GIT_CONFIG_GLOBAL"] = _fileSystem.Path.Combine(workingDirectory, "disabled-global-gitconfig")
		};
		ProcessExecutionResult result = _processExecutor.ExecuteAndCaptureAsync(new ProcessExecutionOptions("git", arguments) {
			WorkingDirectory = workingDirectory,
			Timeout = timeout,
			SuppressErrors = true,
			ClearInheritedEnvironment = true,
			InheritedEnvironmentVariableAllowlist = GitInheritedEnvironmentAllowlist,
			EnvironmentVariables = environment,
			ResolveProgramPath = true,
			MaximumCapturedOutputCharacters = MaxCapturedOutputCharacters,
			MonitoredDirectory = workingDirectory,
			MaximumMonitoredDirectoryBytes = MaxStagingBytes
		}).GetAwaiter().GetResult();
		if (result.TimedOut) {
			throw new TimeoutException("The operation-wide Git knowledge retrieval deadline elapsed.");
		}
		if (!result.Started || result.ExitCode != 0 || result.Canceled || result.ResourceLimitExceeded) {
			throw new InvalidOperationException("Git knowledge retrieval failed.");
		}
		return result;
	}

	private static TimeSpan GetRemainingTimeout(Stopwatch operation, TimeSpan deadline) {
		TimeSpan remaining = deadline - operation.Elapsed;
		if (remaining <= TimeSpan.Zero) {
			throw new TimeoutException("The operation-wide Git knowledge retrieval deadline elapsed.");
		}
		return remaining;
	}

	private static string GetFetchReference(KnowledgeSourceConfiguration source, string? branch) {
		if (source.Commit is not null) {
			return source.Commit;
		}
		if (source.Tag is not null) {
			return $"refs/tags/{source.Tag}:refs/tags/{source.Tag}";
		}
		return $"refs/heads/{branch}:refs/remotes/origin/{branch}";
	}

	private static string GetRevisionExpression(KnowledgeSourceConfiguration source, string? branch) {
		if (source.Commit is not null) {
			return "FETCH_HEAD";
		}
		return source.Tag is not null ? $"refs/tags/{source.Tag}" : $"refs/remotes/origin/{branch}";
	}

	private static bool IsCompleteCommit(string commit) =>
		commit.Length is 40 or 64 && commit.All(Uri.IsHexDigit);

	private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

	private void EnsureStagingBound(string operationDirectory) {
		long bytes = _fileSystem.Directory.EnumerateFiles(operationDirectory, "*", SearchOption.AllDirectories)
			.Sum(file => _fileSystem.FileInfo.New(file).Length);
		if (bytes > MaxStagingBytes) {
			throw new InvalidDataException("Git knowledge staging area exceeded its size limit.");
		}
	}

	private void ExtractArtifact(string archivePath, string artifactPath, string candidatePath) {
		using Stream input = _fileSystem.File.OpenRead(archivePath);
		using ZipArchive archive = new(input, ZipArchiveMode.Read);
		ZipArchiveEntry[] entries = archive.Entries
			.Where(entry => string.Equals(entry.FullName, artifactPath, StringComparison.Ordinal))
			.ToArray();
		if (entries.Length != 1 || entries[0].Length <= 0 || entries[0].Length > MaxBundleBytes) {
			throw new InvalidDataException("Git archive did not contain exactly one bounded knowledge bundle artifact.");
		}
		using Stream source = entries[0].Open();
		using Stream destination = _fileSystem.File.Create(candidatePath);
		source.CopyTo(destination);
	}

	private static KnowledgeTransportResult NoCandidate(
		KnowledgeSourceConfiguration source,
		string? branch,
		string commit) => new(
		KnowledgeTransportStatus.NoCandidate,
		commit,
		null,
		null,
		ResolvedBranch: branch,
		ResolvedTag: source.Tag,
		ResolvedCommit: commit);

	private static KnowledgeTransportResult Rejected(
		KnowledgeSourceConfiguration source,
		string? branch,
		string commit,
		string diagnostic) => new(
		KnowledgeTransportStatus.Rejected,
		commit,
		null,
		null,
		ResolvedBranch: branch,
		ResolvedTag: source.Tag,
		ResolvedCommit: commit,
		Diagnostic: diagnostic);

	private void TryDelete(string operationDirectory) {
		try {
			if (_fileSystem.Directory.Exists(operationDirectory)) {
				_fileSystem.Directory.Delete(operationDirectory, recursive: true);
			}
		} catch (IOException) {
			// Best-effort cleanup; a later staging prune can remove a transiently locked directory.
		} catch (UnauthorizedAccessException) {
			// Best-effort cleanup; the rejection result remains authoritative.
		}
	}
}
