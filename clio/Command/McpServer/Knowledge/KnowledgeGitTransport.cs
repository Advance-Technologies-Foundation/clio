using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Clio.Common;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command.McpServer.Knowledge;

internal sealed class KnowledgeGitTransport : IKnowledgeRepositoryTransport {
	private const long MaxCheckoutBytes = 256L * 1024 * 1024;
	private const int MaxCheckoutEntries = 100_000;
	private const int MaxCapturedOutputCharacters = 2 * 1024 * 1024;
	private const int DefaultTransportDeadlineMilliseconds = 30_000;
	private static readonly IReadOnlyDictionary<string, string> GitEnvironment =
		new Dictionary<string, string>(StringComparer.Ordinal) {
			["GIT_TERMINAL_PROMPT"] = "0",
			["GIT_CONFIG_NOSYSTEM"] = "1",
			["GIT_CONFIG_COUNT"] = "0"
		};
	private static readonly IReadOnlyCollection<string> GitInheritedEnvironmentAllowlist = [
		"SystemRoot", "WINDIR", "COMSPEC", "TEMP", "TMP", "TMPDIR", "LANG", "LC_ALL", "LC_CTYPE",
		"HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY", "http_proxy", "https_proxy", "all_proxy",
		"no_proxy", "SSL_CERT_FILE", "SSL_CERT_DIR", "CURL_CA_BUNDLE"
	];

	private readonly IProcessExecutor _processExecutor;
	private readonly IFileSystem _fileSystem;

	public KnowledgeSourceType Type => KnowledgeSourceType.Git;

	public KnowledgeGitTransport(IProcessExecutor processExecutor, IFileSystem fileSystem) {
		_processExecutor = processExecutor ?? throw new ArgumentNullException(nameof(processExecutor));
		_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
	}

	public KnowledgeTransportResult Synchronize(KnowledgeTransportRequest request, string repositoryPath) {
		ArgumentNullException.ThrowIfNull(request);
		Stopwatch operation = Stopwatch.StartNew();
		TimeSpan deadline = TimeSpan.FromMilliseconds(request.TransportDeadlineMilliseconds is > 0
			? request.TransportDeadlineMilliseconds.Value
			: DefaultTransportDeadlineMilliseconds);
		KnowledgeSourceConfiguration source = KnowledgeSourceConfigurationValidator.ValidateAndClone(request.Source);
		if (source.Type != KnowledgeSourceType.Git) {
			throw new ArgumentException("Git transport received a non-Git source.", nameof(request));
		}
		if (string.IsNullOrWhiteSpace(repositoryPath) || !_fileSystem.Path.IsPathFullyQualified(repositoryPath)) {
			throw new ArgumentException("Git knowledge repository path must be absolute.", nameof(repositoryPath));
		}

		string fullRepositoryPath = _fileSystem.Path.GetFullPath(repositoryPath);
		string parent = _fileSystem.Path.GetDirectoryName(fullRepositoryPath)
			?? throw new InvalidDataException("Git knowledge repository path has no parent directory.");
		try {
			_fileSystem.Directory.CreateDirectory(parent);
			bool installed = _fileSystem.Directory.Exists(_fileSystem.Path.Combine(fullRepositoryPath, ".git"));
			string? branch = source.Branch;
			if (!installed) {
				Clone(source, fullRepositoryPath, parent, operation, deadline);
				if (source.Commit is not null) {
					ExecuteGit(fullRepositoryPath,
						$"fetch --no-tags --depth=1 origin {Quote(source.Commit)}",
						GetRemainingTimeout(operation, deadline), monitorDirectory: true);
					ExecuteGit(fullRepositoryPath, "checkout --detach FETCH_HEAD", GetRemainingTimeout(operation, deadline),
						monitorDirectory: true);
				}
				branch ??= source.Tag is null && source.Commit is null ? ReadCurrentBranch(fullRepositoryPath, operation, deadline) : null;
			} else {
				ValidateCheckoutFileSystem(fullRepositoryPath, operation, deadline);
				ValidateRepositoryConfiguration(fullRepositoryPath);
				ValidateOrigin(fullRepositoryPath, source, operation, deadline);
				ValidateCheckout(fullRepositoryPath, operation, deadline);
				if (source.Commit is not null) {
					ExecuteGit(fullRepositoryPath,
						$"fetch --no-tags --depth=1 origin {Quote(source.Commit)}",
						GetRemainingTimeout(operation, deadline), monitorDirectory: true);
					ExecuteGit(fullRepositoryPath, "checkout --detach FETCH_HEAD", GetRemainingTimeout(operation, deadline),
						monitorDirectory: true);
				} else if (source.Tag is not null) {
					ExecuteGit(fullRepositoryPath,
						$"fetch --no-tags --depth=1 origin {Quote("refs/tags/" + source.Tag)}",
						GetRemainingTimeout(operation, deadline), monitorDirectory: true);
					ExecuteGit(fullRepositoryPath, $"checkout --detach {Quote("FETCH_HEAD")}",
						GetRemainingTimeout(operation, deadline), monitorDirectory: true);
				} else {
					branch ??= ReadCurrentBranch(fullRepositoryPath, operation, deadline);
					ExecuteGit(fullRepositoryPath, $"checkout {Quote(branch)}", GetRemainingTimeout(operation, deadline),
						monitorDirectory: true);
					ExecuteGit(fullRepositoryPath, $"pull --ff-only origin {Quote(branch)}",
						GetRemainingTimeout(operation, deadline), monitorDirectory: true);
				}
			}

			ValidateCheckout(fullRepositoryPath, operation, deadline);
			string commit = ExecuteGit(fullRepositoryPath, "rev-parse HEAD",
				GetRemainingTimeout(operation, deadline)).StandardOutput.Trim();
			if (!IsCompleteCommit(commit)) {
				throw new InvalidDataException("Git returned an invalid resolved commit ID.");
			}
			if (request.RejectedRevisions.Contains(commit)) {
				return Rejected(source, branch, commit, "The resolved Git commit was previously rejected.");
			}
			KnowledgeTransportStatus status = string.Equals(request.ActiveRevision, commit, StringComparison.OrdinalIgnoreCase)
				? KnowledgeTransportStatus.NoCandidate
				: KnowledgeTransportStatus.Downloaded;
			return new KnowledgeTransportResult(
				status,
				commit,
				null,
				fullRepositoryPath,
				ResolvedBranch: branch,
				ResolvedTag: source.Tag,
				ResolvedCommit: commit);
		} catch (TimeoutException exception) {
			return Failed(exception.Message);
		} catch (InvalidOperationException exception) {
			return Failed(exception.Message);
		} catch (Exception exception) when (exception is IOException
				or UnauthorizedAccessException
				or InvalidDataException
				or ArgumentException
				or NotSupportedException) {
			return Rejected(source, branch: null, commit: null, exception.Message);
		}
	}

	public KnowledgeTransportResult CheckForUpdates(KnowledgeTransportRequest request, string repositoryPath) {
		ArgumentNullException.ThrowIfNull(request);
		Stopwatch operation = Stopwatch.StartNew();
		TimeSpan deadline = TimeSpan.FromMilliseconds(request.TransportDeadlineMilliseconds is > 0
			? request.TransportDeadlineMilliseconds.Value
			: DefaultTransportDeadlineMilliseconds);
		KnowledgeSourceConfiguration source = KnowledgeSourceConfigurationValidator.ValidateAndClone(request.Source);
		try {
			string fullRepositoryPath = RequireInstalledRepository(repositoryPath);
			ValidateOrigin(fullRepositoryPath, source, operation, deadline);
			string current = GetCurrentRevision(fullRepositoryPath)
				?? throw new InvalidDataException("Installed Git knowledge checkout has no valid current revision.");
			string target = ResolveRemoteRevision(source, fullRepositoryPath, operation, deadline);
			return new KnowledgeTransportResult(
				string.Equals(current, target, StringComparison.OrdinalIgnoreCase)
					? KnowledgeTransportStatus.NoCandidate
					: KnowledgeTransportStatus.Downloaded,
				target,
				null,
				null,
				ResolvedBranch: source.Branch,
				ResolvedTag: source.Tag,
				ResolvedCommit: target);
		} catch (TimeoutException exception) {
			return Failed(exception.Message);
		} catch (InvalidOperationException exception) {
			return Failed(exception.Message);
		} catch (Exception exception) when (exception is IOException
				or UnauthorizedAccessException
				or InvalidDataException
				or ArgumentException
				or NotSupportedException) {
			return Rejected(source, source.Branch, null, exception.Message);
		}
	}

	public void ValidateInstalledCheckout(KnowledgeSourceConfiguration source, string repositoryPath) {
		ValidateCheckoutCore(source, repositoryPath, enforceConfiguredReference: true);
	}

	public void ValidateCheckoutForSynchronization(KnowledgeSourceConfiguration source, string repositoryPath) {
		ValidateCheckoutCore(source, repositoryPath, enforceConfiguredReference: false);
	}

	private void ValidateCheckoutCore(
		KnowledgeSourceConfiguration source,
		string repositoryPath,
		bool enforceConfiguredReference) {
		KnowledgeSourceConfiguration validated = KnowledgeSourceConfigurationValidator.ValidateAndClone(source);
		if (validated.Type != KnowledgeSourceType.Git) {
			throw new ArgumentException("Git checkout validation received a non-Git source.", nameof(source));
		}
		Stopwatch operation = Stopwatch.StartNew();
		TimeSpan deadline = TimeSpan.FromMilliseconds(DefaultTransportDeadlineMilliseconds);
		string fullRepositoryPath = RequireInstalledRepository(repositoryPath);
		ValidateCheckoutFileSystem(fullRepositoryPath, operation, deadline);
		ValidateRepositoryConfiguration(fullRepositoryPath);
		ValidateOrigin(fullRepositoryPath, validated, operation, deadline);
		ValidateCheckout(fullRepositoryPath, operation, deadline);
		if (enforceConfiguredReference) {
			ValidateConfiguredReference(fullRepositoryPath, validated, operation, deadline);
		}
	}

	private void ValidateConfiguredReference(
		string repositoryPath,
		KnowledgeSourceConfiguration source,
		Stopwatch operation,
		TimeSpan deadline) {
		string head = ExecuteGit(repositoryPath, "rev-parse HEAD", GetRemainingTimeout(operation, deadline))
			.StandardOutput.Trim();
		if (!IsCompleteCommit(head)) {
			throw new InvalidDataException("Installed Git knowledge checkout has no valid current revision.");
		}
		if (source.Commit is not null
				&& !string.Equals(head, source.Commit, StringComparison.OrdinalIgnoreCase)) {
			throw new InvalidDataException("Installed Git knowledge checkout does not match the configured commit.");
		}
		if (source.Tag is not null) {
			string tagCommit = ExecuteGit(
				repositoryPath,
				$"rev-parse {Quote($"refs/tags/{source.Tag}^{{commit}}")}",
				GetRemainingTimeout(operation, deadline)).StandardOutput.Trim();
			if (!string.Equals(head, tagCommit, StringComparison.OrdinalIgnoreCase)) {
				throw new InvalidDataException("Installed Git knowledge checkout does not match the configured tag.");
			}
		}
		if (source.Branch is not null) {
			string branch = ReadCurrentBranch(repositoryPath, operation, deadline);
			if (!string.Equals(branch, source.Branch, StringComparison.Ordinal)) {
				throw new InvalidDataException("Installed Git knowledge checkout does not match the configured branch.");
			}
		}
	}

	public string? GetCurrentRevision(string repositoryPath) {
		try {
			string gitDirectory = _fileSystem.Path.Combine(repositoryPath, ".git");
			string headPath = _fileSystem.Path.Combine(gitDirectory, "HEAD");
			if (!_fileSystem.File.Exists(headPath)) {
				return null;
			}
			string head = ReadSmallText(headPath).Trim();
			if (IsCompleteCommit(head)) {
				return head.ToLowerInvariant();
			}
			const string prefix = "ref: ";
			if (!head.StartsWith(prefix, StringComparison.Ordinal)) {
				return null;
			}
			string reference = head[prefix.Length..];
			if (reference.Contains("..", StringComparison.Ordinal) || reference.Contains('\\')) {
				return null;
			}
			string referencePath = _fileSystem.Path.GetFullPath(_fileSystem.Path.Combine(gitDirectory, reference));
			string gitPrefix = _fileSystem.Path.GetFullPath(gitDirectory).TrimEnd(
				_fileSystem.Path.DirectorySeparatorChar, _fileSystem.Path.AltDirectorySeparatorChar)
				+ _fileSystem.Path.DirectorySeparatorChar;
			if (!referencePath.StartsWith(gitPrefix, OperatingSystem.IsWindows()
					? StringComparison.OrdinalIgnoreCase
					: StringComparison.Ordinal)) {
				return null;
			}
			if (_fileSystem.File.Exists(referencePath)) {
				string revision = ReadSmallText(referencePath).Trim();
				return IsCompleteCommit(revision) ? revision.ToLowerInvariant() : null;
			}
			string packedRefs = _fileSystem.Path.Combine(gitDirectory, "packed-refs");
			if (!_fileSystem.File.Exists(packedRefs)) {
				return null;
			}
			string? packed = ReadSmallText(packedRefs).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
				.Where(line => !line.StartsWith('#') && !line.StartsWith('^'))
				.Select(line => line.Split(' ', 2))
				.Where(parts => parts.Length == 2 && string.Equals(parts[1], reference, StringComparison.Ordinal))
				.Select(parts => parts[0])
				.FirstOrDefault();
			return packed is not null && IsCompleteCommit(packed) ? packed.ToLowerInvariant() : null;
		} catch (Exception) {
			return null;
		}
	}

	public void Restore(string repositoryPath, string revision) {
		if (!IsCompleteCommit(revision)) {
			throw new ArgumentException("Git restore revision must be a complete commit ID.", nameof(revision));
		}
		ExecuteGit(repositoryPath, $"reset --hard {Quote(revision)}", TimeSpan.FromSeconds(15), monitorDirectory: true);
	}

	private string RequireInstalledRepository(string repositoryPath) {
		if (string.IsNullOrWhiteSpace(repositoryPath) || !_fileSystem.Path.IsPathFullyQualified(repositoryPath)) {
			throw new ArgumentException("Git knowledge repository path must be absolute.", nameof(repositoryPath));
		}
		string fullPath = _fileSystem.Path.GetFullPath(repositoryPath);
		if (!_fileSystem.Directory.Exists(_fileSystem.Path.Combine(fullPath, ".git"))) {
			throw new InvalidDataException("Git knowledge source is not installed.");
		}
		return fullPath;
	}

	private void ValidateOrigin(
		string repositoryPath,
		KnowledgeSourceConfiguration source,
		Stopwatch operation,
		TimeSpan deadline) {
		string origin = ExecuteGit(repositoryPath, "remote get-url origin",
			GetRemainingTimeout(operation, deadline)).StandardOutput.Trim();
		if (!string.Equals(origin.TrimEnd('/'), source.Location.TrimEnd('/'), StringComparison.Ordinal)) {
			throw new InvalidDataException("Installed Git knowledge checkout origin does not match the configured source.");
		}
	}

	private string ResolveRemoteRevision(
		KnowledgeSourceConfiguration source,
		string repositoryPath,
		Stopwatch operation,
		TimeSpan deadline) {
		if (source.Commit is not null) {
			return source.Commit.ToLowerInvariant();
		}
		string reference;
		string arguments;
		if (source.Tag is not null) {
			reference = $"refs/tags/{source.Tag}";
			arguments = $"ls-remote --tags {Quote(source.Location)} {Quote(reference)} {Quote(reference + "^{}")}";
		} else {
			string branch = source.Branch ?? ReadCurrentBranch(repositoryPath, operation, deadline);
			reference = $"refs/heads/{branch}";
			arguments = $"ls-remote --heads {Quote(source.Location)} {Quote(reference)}";
		}
		ProcessExecutionResult result = Execute(arguments, _fileSystem.Path.GetDirectoryName(repositoryPath)!,
			GetRemainingTimeout(operation, deadline), monitoredDirectory: null);
		(string Revision, string Reference)[] candidates = result.StandardOutput
			.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
			.Select(line => line.Split('\t', 2))
			.Where(parts => parts.Length == 2 && IsCompleteCommit(parts[0]))
			.Select(parts => (parts[0].ToLowerInvariant(), parts[1]))
			.ToArray();
		(string Revision, string Reference)? selected = source.Tag is not null
			? candidates.FirstOrDefault(candidate => candidate.Reference == reference + "^{}")
			: candidates.FirstOrDefault(candidate => candidate.Reference == reference);
		if (selected is null || string.IsNullOrWhiteSpace(selected.Value.Revision)) {
			selected = candidates.FirstOrDefault(candidate => candidate.Reference == reference);
		}
		return selected is not null && IsCompleteCommit(selected.Value.Revision)
			? selected.Value.Revision
			: throw new InvalidDataException("Git remote did not expose the configured reference.");
	}

	private void ValidateCheckout(string repositoryPath, Stopwatch operation, TimeSpan deadline) {
		string status = ExecuteGit(repositoryPath, "status --porcelain --untracked-files=no",
			GetRemainingTimeout(operation, deadline)).StandardOutput;
		if (!string.IsNullOrWhiteSpace(status)) {
			throw new InvalidDataException("Git knowledge checkout contains modified tracked files.");
		}
		string index = ExecuteGit(repositoryPath, "ls-files --stage",
			GetRemainingTimeout(operation, deadline)).StandardOutput;
		foreach (string line in index.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)) {
			if (line.StartsWith("120000 ", StringComparison.Ordinal)) {
				throw new InvalidDataException("Git knowledge repositories cannot contain symbolic links.");
			}
			if (line.StartsWith("160000 ", StringComparison.Ordinal)) {
				throw new InvalidDataException("Git knowledge repositories cannot contain submodules.");
			}
		}
	}

	private void ValidateCheckoutFileSystem(
		string repositoryPath,
		Stopwatch operation,
		TimeSpan deadline) {
		Stack<string> pending = new();
		int entryCount = 0;
		pending.Push(repositoryPath);
		while (pending.Count > 0) {
			_ = GetRemainingTimeout(operation, deadline);
			string directory = pending.Pop();
			RejectReparsePoint(directory);
			foreach (string entry in _fileSystem.Directory.EnumerateFileSystemEntries(directory)) {
				if (++entryCount > MaxCheckoutEntries) {
					throw new InvalidDataException($"Git knowledge checkout exceeds {MaxCheckoutEntries} filesystem entries.");
				}
				FileAttributes attributes = _fileSystem.File.GetAttributes(entry);
				if ((attributes & FileAttributes.ReparsePoint) != 0) {
					throw new InvalidDataException("Git knowledge checkouts cannot contain filesystem links or junctions.");
				}
				if ((attributes & FileAttributes.Directory) != 0) {
					pending.Push(entry);
				}
			}
		}
	}

	private void ValidateRepositoryConfiguration(string repositoryPath) {
		string configPath = _fileSystem.Path.Combine(repositoryPath, ".git", "config");
		if (!_fileSystem.File.Exists(configPath)) {
			throw new InvalidDataException("Git knowledge checkout has no local configuration.");
		}
		string section = string.Empty;
		foreach (string rawLine in ReadSmallText(configPath).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)) {
			string line = rawLine.Trim();
			if (line.Length == 0 || line[0] is '#' or ';') {
				continue;
			}
			if (line.StartsWith('[') && line.EndsWith(']')) {
				section = line[1..^1].Trim().Split([' ', '\t'], 2)[0].ToLowerInvariant();
				if (section is "include" or "includeif") {
					throw new InvalidDataException("Git knowledge checkout configuration cannot include external configuration.");
				}
				continue;
			}
			string key = line.Split(['=', ' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries)[0]
				.ToLowerInvariant();
			bool allowed = section switch {
				"core" => key is "repositoryformatversion" or "filemode" or "bare" or "logallrefupdates"
					or "symlinks" or "ignorecase" or "precomposeunicode",
				"remote" => key is "url" or "fetch" or "promisor" or "partialclonefilter" or "tagopt",
				"branch" => key is "remote" or "merge",
				_ => false
			};
			if (!allowed) {
				throw new InvalidDataException("Git knowledge checkout configuration contains unsupported settings.");
			}
		}
	}

	private void RejectReparsePoint(string path) {
		if ((_fileSystem.File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) {
			throw new InvalidDataException("Git knowledge checkouts cannot contain filesystem links or junctions.");
		}
	}

	private string ReadSmallText(string path) {
		using Stream stream = _fileSystem.File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		if (stream.Length <= 0 || stream.Length > MaxCapturedOutputCharacters) {
			throw new InvalidDataException("Git metadata is outside the supported size bounds.");
		}
		using StreamReader reader = new(stream);
		return reader.ReadToEnd();
	}

	private void Clone(
		KnowledgeSourceConfiguration source,
		string repositoryPath,
		string parent,
		Stopwatch operation,
		TimeSpan deadline) {
		string reference = source.Tag ?? source.Branch ?? string.Empty;
		string referenceArguments = string.IsNullOrEmpty(reference)
			? string.Empty
			: $" --branch {Quote(reference)} --single-branch";
		string noCheckout = source.Commit is null ? string.Empty : " --no-checkout";
		string shallow = source.Commit is null ? " --depth=1" : string.Empty;
		_fileSystem.Directory.CreateDirectory(repositoryPath);
		Execute(
			$"-c {Quote("core.hooksPath=" + GetDisabledHooksPath())} clone --filter=blob:none --no-recurse-submodules{shallow}{noCheckout}{referenceArguments} "
			+ $"{Quote(source.Location)} {Quote(repositoryPath)}",
			parent,
			GetRemainingTimeout(operation, deadline),
			repositoryPath);
	}

	private string ReadCurrentBranch(string repositoryPath, Stopwatch operation, TimeSpan deadline) {
		string branch = ExecuteGit(repositoryPath, "branch --show-current",
			GetRemainingTimeout(operation, deadline)).StandardOutput.Trim();
		if (string.IsNullOrWhiteSpace(branch)) {
			throw new InvalidDataException("Git knowledge checkout has no current branch.");
		}
		return ValidateBranch(branch);
	}

	internal static string ParseDefaultBranch(string output) {
		string? branch = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
			.Select(line => line.Split('\t'))
			.Where(parts => parts.Length == 2 && string.Equals(parts[1], "HEAD", StringComparison.Ordinal))
			.Select(parts => parts[0])
			.Where(value => value.StartsWith("ref: refs/heads/", StringComparison.Ordinal))
			.Select(value => value["ref: refs/heads/".Length..])
			.FirstOrDefault();
		return branch is null ? throw new InvalidDataException("Git remote did not advertise a default branch.") : ValidateBranch(branch);
	}

	private static string ValidateBranch(string branch) {
		KnowledgeSourceConfiguration probe = new() {
			LibraryId = "com.clio.validation",
			Type = KnowledgeSourceType.Git,
			Location = "https://localhost/repository.git",
			Branch = branch
		};
		return KnowledgeSourceConfigurationValidator.ValidateAndClone(probe).Branch!;
	}

	private ProcessExecutionResult ExecuteGit(
		string repositoryPath,
		string arguments,
		TimeSpan timeout,
		bool monitorDirectory = false) =>
		Execute(
			$"-C {Quote(repositoryPath)} -c {Quote("core.hooksPath=" + GetDisabledHooksPath())} " + arguments,
			_fileSystem.Path.GetDirectoryName(repositoryPath)!,
			timeout,
			monitorDirectory ? repositoryPath : null);

	private static string GetDisabledHooksPath() => OperatingSystem.IsWindows() ? "NUL" : "/dev/null";

	private ProcessExecutionResult Execute(
		string arguments,
		string workingDirectory,
		TimeSpan timeout,
		string? monitoredDirectory) {
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
			MonitoredDirectory = monitoredDirectory,
			MaximumMonitoredDirectoryBytes = monitoredDirectory is null ? null : MaxCheckoutBytes,
			ResourceMonitorInterval = monitoredDirectory is null ? null : TimeSpan.FromSeconds(1)
		}).GetAwaiter().GetResult();
		if (result.TimedOut) {
			throw new TimeoutException("The operation-wide Git knowledge synchronization deadline elapsed.");
		}
		if (!result.Started || result.ExitCode != 0 || result.Canceled || result.ResourceLimitExceeded) {
			throw new InvalidOperationException("Git knowledge synchronization failed.");
		}
		return result;
	}

	private static TimeSpan GetRemainingTimeout(Stopwatch operation, TimeSpan deadline) {
		TimeSpan remaining = deadline - operation.Elapsed;
		return remaining <= TimeSpan.Zero
			? throw new TimeoutException("The operation-wide Git knowledge synchronization deadline elapsed.")
			: remaining;
	}

	private static bool IsCompleteCommit(string commit) => commit.Length is 40 or 64 && commit.All(Uri.IsHexDigit);

	private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

	private static KnowledgeTransportResult Failed(string diagnostic) => new(
		KnowledgeTransportStatus.Failed, null, null, null, Diagnostic: diagnostic);

	private static KnowledgeTransportResult Rejected(
		KnowledgeSourceConfiguration source,
		string? branch,
		string? commit,
		string diagnostic) => new(
		KnowledgeTransportStatus.Rejected,
		commit,
		null,
		null,
		ResolvedBranch: branch,
		ResolvedTag: source.Tag,
		ResolvedCommit: commit,
		Diagnostic: diagnostic);
}
