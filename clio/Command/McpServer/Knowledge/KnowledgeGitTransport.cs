using System;
using System.Collections.Generic;
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
	private readonly TimeProvider _timeProvider;

	public KnowledgeSourceType Type => KnowledgeSourceType.Git;

	/// <summary>
	/// The operation-wide budget shared by every git command in one synchronization, measured
	/// against an injectable clock so a test can advance time instead of sleeping.
	/// </summary>
	private readonly record struct OperationDeadline(TimeProvider Clock, long StartTimestamp, TimeSpan Budget) {
		/// <summary>Time left, or a timeout when the operation-wide budget is spent.</summary>
		internal TimeSpan RequireRemaining() {
			TimeSpan remaining = Budget - Clock.GetElapsedTime(StartTimestamp);
			return remaining <= TimeSpan.Zero
				? throw new TimeoutException("The operation-wide Git knowledge synchronization deadline elapsed.")
				: remaining;
		}
	}

	public KnowledgeGitTransport(IProcessExecutor processExecutor, IFileSystem fileSystem, TimeProvider timeProvider) {
		_processExecutor = processExecutor ?? throw new ArgumentNullException(nameof(processExecutor));
		_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
		_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
	}

	public KnowledgeTransportResult Synchronize(KnowledgeTransportRequest request, string repositoryPath) {
		ArgumentNullException.ThrowIfNull(request);
		OperationDeadline deadline = new(_timeProvider, _timeProvider.GetTimestamp(), ResolveDeadline(request));
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
			string? branch;
			if (installed) {
				branch = UpdateInstalledCheckout(source, fullRepositoryPath, deadline);
			} else {
				branch = CreateCheckout(source, fullRepositoryPath, parent, deadline);
			}

			// Revalidated after the fetch/checkout/pull above, because those commands mutate the working tree.
			ValidateCheckout(fullRepositoryPath, deadline);
			string commit = ExecuteGit(fullRepositoryPath, "rev-parse HEAD",
				deadline.RequireRemaining()).StandardOutput.Trim();
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

	// A freshly cloned checkout is trusted as produced by the clone itself, so it intentionally skips the
	// filesystem, configuration and origin validations that an already installed checkout must pass.
	private string? CreateCheckout(
		KnowledgeSourceConfiguration source,
		string repositoryPath,
		string parent,
		OperationDeadline deadline) {
		Clone(source, repositoryPath, parent, deadline);
		if (source.Commit is not null) {
			CheckoutCommit(source.Commit, repositoryPath, deadline);
		}
		return source.Branch ?? (source.Tag is null && source.Commit is null
			? ReadCurrentBranch(repositoryPath, deadline)
			: null);
	}

	private string? UpdateInstalledCheckout(
		KnowledgeSourceConfiguration source,
		string repositoryPath,
		OperationDeadline deadline) {
		ValidateCheckoutFileSystem(repositoryPath, deadline);
		ValidateRepositoryConfiguration(repositoryPath);
		ValidateOrigin(repositoryPath, source, deadline);
		ValidateCheckout(repositoryPath, deadline);
		if (source.Commit is not null) {
			CheckoutCommit(source.Commit, repositoryPath, deadline);
			return source.Branch;
		}
		if (source.Tag is not null) {
			CheckoutTag(source.Tag, repositoryPath, deadline);
			return source.Branch;
		}
		string branch = source.Branch ?? ReadCurrentBranch(repositoryPath, deadline);
		ExecuteGit(repositoryPath, $"checkout {Quote(branch)}", deadline.RequireRemaining(),
			monitorDirectory: true);
		ExecuteGit(repositoryPath, $"pull --ff-only origin {Quote(branch)}",
			deadline.RequireRemaining(), monitorDirectory: true);
		return branch;
	}

	private void CheckoutCommit(string commit, string repositoryPath, OperationDeadline deadline) {
		ExecuteGit(repositoryPath,
			$"fetch --no-tags --depth=1 origin {Quote(commit)}",
			deadline.RequireRemaining(), monitorDirectory: true);
		ExecuteGit(repositoryPath, "checkout --detach FETCH_HEAD", deadline.RequireRemaining(),
			monitorDirectory: true);
	}

	private void CheckoutTag(string tag, string repositoryPath, OperationDeadline deadline) {
		ExecuteGit(repositoryPath,
			$"fetch --no-tags --depth=1 origin {Quote("refs/tags/" + tag)}",
			deadline.RequireRemaining(), monitorDirectory: true);
		ExecuteGit(repositoryPath, $"checkout --detach {Quote("FETCH_HEAD")}",
			deadline.RequireRemaining(), monitorDirectory: true);
	}

	public KnowledgeTransportResult CheckForUpdates(KnowledgeTransportRequest request, string repositoryPath) {
		ArgumentNullException.ThrowIfNull(request);
		OperationDeadline deadline = new(_timeProvider, _timeProvider.GetTimestamp(), ResolveDeadline(request));
		KnowledgeSourceConfiguration source = KnowledgeSourceConfigurationValidator.ValidateAndClone(request.Source);
		try {
			string fullRepositoryPath = RequireInstalledRepository(repositoryPath);
			ValidateOrigin(fullRepositoryPath, source, deadline);
			string current = GetCurrentRevision(fullRepositoryPath)
				?? throw new InvalidDataException("Installed Git knowledge checkout has no valid current revision.");
			string target = ResolveRemoteRevision(source, fullRepositoryPath, deadline);
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
		OperationDeadline deadline = new(
			_timeProvider,
			_timeProvider.GetTimestamp(),
			TimeSpan.FromMilliseconds(DefaultTransportDeadlineMilliseconds));
		string fullRepositoryPath = RequireInstalledRepository(repositoryPath);
		ValidateCheckoutFileSystem(fullRepositoryPath, deadline);
		ValidateRepositoryConfiguration(fullRepositoryPath);
		ValidateOrigin(fullRepositoryPath, validated, deadline);
		ValidateCheckout(fullRepositoryPath, deadline);
		if (enforceConfiguredReference) {
			ValidateConfiguredReference(fullRepositoryPath, validated, deadline);
		}
	}

	private void ValidateConfiguredReference(
		string repositoryPath,
		KnowledgeSourceConfiguration source,
		OperationDeadline deadline) {
		string head = ExecuteGit(repositoryPath, "rev-parse HEAD", deadline.RequireRemaining())
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
				deadline.RequireRemaining()).StandardOutput.Trim();
			if (!string.Equals(head, tagCommit, StringComparison.OrdinalIgnoreCase)) {
				throw new InvalidDataException("Installed Git knowledge checkout does not match the configured tag.");
			}
		}
		if (source.Branch is not null) {
			string branch = ReadCurrentBranch(repositoryPath, deadline);
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
			string? referencePath = ResolveContainedReferencePath(gitDirectory, reference);
			if (referencePath is null) {
				return null;
			}
			return _fileSystem.File.Exists(referencePath)
				? NormalizeCommit(ReadSmallText(referencePath).Trim())
				: ResolvePackedRevision(gitDirectory, reference);
		} catch (Exception) {
			return null;
		}
	}

	// Returns null when the HEAD symbolic reference would resolve outside the .git directory, so a crafted
	// checkout cannot make revision reading follow a path traversal out of the repository.
	private string? ResolveContainedReferencePath(string gitDirectory, string reference) {
		if (reference.Contains("..", StringComparison.Ordinal) || reference.Contains('\\')) {
			return null;
		}
		string referencePath = _fileSystem.Path.GetFullPath(_fileSystem.Path.Combine(gitDirectory, reference));
		string gitPrefix = _fileSystem.Path.GetFullPath(gitDirectory).TrimEnd(
			_fileSystem.Path.DirectorySeparatorChar, _fileSystem.Path.AltDirectorySeparatorChar)
			+ _fileSystem.Path.DirectorySeparatorChar;
		StringComparison pathComparison = OperatingSystem.IsWindows()
			? StringComparison.OrdinalIgnoreCase
			: StringComparison.Ordinal;
		return referencePath.StartsWith(gitPrefix, pathComparison) ? referencePath : null;
	}

	private string? ResolvePackedRevision(string gitDirectory, string reference) {
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
		return packed is null ? null : NormalizeCommit(packed);
	}

	private static string? NormalizeCommit(string revision) =>
		IsCompleteCommit(revision) ? revision.ToLowerInvariant() : null;

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
		OperationDeadline deadline) {
		string origin = ExecuteGit(repositoryPath, "remote get-url origin",
			deadline.RequireRemaining()).StandardOutput.Trim();
		if (!string.Equals(origin.TrimEnd('/'), source.Location.TrimEnd('/'), StringComparison.Ordinal)) {
			throw new InvalidDataException("Installed Git knowledge checkout origin does not match the configured source.");
		}
	}

	private string ResolveRemoteRevision(
		KnowledgeSourceConfiguration source,
		string repositoryPath,
		OperationDeadline deadline) {
		if (source.Commit is not null) {
			return source.Commit.ToLowerInvariant();
		}
		string reference;
		string arguments;
		if (source.Tag is not null) {
			reference = $"refs/tags/{source.Tag}";
			arguments = $"ls-remote --tags {Quote(source.Location)} {Quote(reference)} {Quote(reference + "^{}")}";
		} else {
			string branch = source.Branch ?? ReadCurrentBranch(repositoryPath, deadline);
			reference = $"refs/heads/{branch}";
			arguments = $"ls-remote --heads {Quote(source.Location)} {Quote(reference)}";
		}
		ProcessExecutionResult result = Execute(arguments, _fileSystem.Path.GetDirectoryName(repositoryPath),
			deadline.RequireRemaining(), monitoredDirectory: null);
		(string Revision, string Reference)[] candidates = result.StandardOutput
			.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
			.Select(line => line.Split('\t', 2))
			.Where(parts => parts.Length == 2 && IsCompleteCommit(parts[0]))
			.Select(parts => (parts[0].ToLowerInvariant(), parts[1]))
			.ToArray();
		// A tag is preferred through its peeled "^{}" entry so that annotated tags resolve to the commit they
		// point at, but lightweight tags only advertise the plain reference and must still be accepted.
		string? selected = source.Tag is not null
			? SelectRevision(candidates, reference + "^{}")
			: SelectRevision(candidates, reference);
		if (string.IsNullOrWhiteSpace(selected)) {
			selected = SelectRevision(candidates, reference);
		}
		return selected is not null && IsCompleteCommit(selected)
			? selected
			: throw new InvalidDataException("Git remote did not expose the configured reference.");
	}

	// The revision is projected before FirstOrDefault so that "not advertised" is a real null; taking the
	// default of the tuple itself would instead yield a (null, null) pair that reads as a found candidate.
	private static string? SelectRevision((string Revision, string Reference)[] candidates, string reference) =>
		candidates
			.Where(candidate => candidate.Reference == reference)
			.Select(candidate => candidate.Revision)
			.FirstOrDefault();

	private void ValidateCheckout(string repositoryPath, OperationDeadline deadline) {
		string status = ExecuteGit(repositoryPath, "status --porcelain --untracked-files=all",
			deadline.RequireRemaining()).StandardOutput;
		if (!string.IsNullOrWhiteSpace(status)) {
			throw new InvalidDataException("Git knowledge checkout contains modified or untracked files.");
		}
		string index = ExecuteGit(repositoryPath, "ls-files --stage",
			deadline.RequireRemaining()).StandardOutput;
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
		OperationDeadline deadline) {
		Stack<string> pending = new();
		int entryCount = 0;
		pending.Push(repositoryPath);
		while (pending.Count > 0) {
			_ = deadline.RequireRemaining();
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
			if (IsIgnorableConfigurationLine(line)) {
				continue;
			}
			string? declaredSection = TryReadSectionHeader(line);
			if (declaredSection is not null) {
				section = declaredSection;
				if (section is "include" or "includeif") {
					throw new InvalidDataException("Git knowledge checkout configuration cannot include external configuration.");
				}
				continue;
			}
			string key = line.Split(['=', ' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries)[0]
				.ToLowerInvariant();
			if (!IsAllowedConfigurationSetting(section, key)) {
				throw new InvalidDataException("Git knowledge checkout configuration contains unsupported settings.");
			}
		}
	}

	private static bool IsIgnorableConfigurationLine(string line) => line.Length == 0 || line[0] is '#' or ';';

	// Returns null for anything that is not a well-formed section header, so a malformed header keeps being
	// parsed as a key line under the section that preceded it.
	private static string? TryReadSectionHeader(string line) =>
		line.StartsWith('[') && line.EndsWith(']')
			? line[1..^1].Trim().Split([' ', '\t'], 2)[0].ToLowerInvariant()
			: null;

	private static bool IsAllowedConfigurationSetting(string section, string key) => section switch {
		"core" => key is "repositoryformatversion" or "filemode" or "bare" or "logallrefupdates"
			or "symlinks" or "ignorecase" or "precomposeunicode",
		"remote" => key is "url" or "fetch" or "promisor" or "partialclonefilter" or "tagopt",
		"branch" => key is "remote" or "merge",
		_ => false
	};

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
		OperationDeadline deadline) {
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
			deadline.RequireRemaining(),
			repositoryPath);
	}

	private string ReadCurrentBranch(string repositoryPath, OperationDeadline deadline) {
		string branch = ExecuteGit(repositoryPath, "branch --show-current",
			deadline.RequireRemaining()).StandardOutput.Trim();
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
		return KnowledgeSourceConfigurationValidator.ValidateAndClone(probe).Branch;
	}

	private ProcessExecutionResult ExecuteGit(
		string repositoryPath,
		string arguments,
		TimeSpan timeout,
		bool monitorDirectory = false) =>
		Execute(
			$"-C {Quote(repositoryPath)} -c {Quote("core.hooksPath=" + GetDisabledHooksPath())} " + arguments,
			_fileSystem.Path.GetDirectoryName(repositoryPath),
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
		string cleanupDiagnostic = result.DescendantTerminationUncertain
			? " Redirected streams were disconnected; termination of already reparented descendants is not guaranteed."
			: string.Empty;
		if (result.TimedOut) {
			throw new TimeoutException(
				$"The operation-wide Git knowledge synchronization deadline elapsed.{cleanupDiagnostic}");
		}
		if (!result.Started || result.ExitCode != 0 || result.Canceled || result.ResourceLimitExceeded) {
			// Four distinct causes used to collapse into one sentence, and git's own stderr - captured here in
			// full, because SuppressErrors only silences the logger - was discarded. The result was that a
			// clone which failed for ANY reason reported "Git knowledge synchronization failed" and nothing
			// else, so the only way to learn why was to reconstruct the command and run it by hand.
			// Neutralized rather than raw: git echoes remote branch names and server messages, so a
			// repository chooses part of this prose. RedactUntrustedOrNull is idempotent, so the boundary
			// that finally emits this does not wrap it twice.
			string cause = !result.Started
				? "git could not be started"
				: result.ResourceLimitExceeded
					? "the checkout exceeded its size limit"
					: result.Canceled
						? "the operation was canceled"
						: $"git exited with code {result.ExitCode}";
			string? reported = SensitiveErrorTextRedactor.RedactUntrustedOrNull(result.StandardError);
			throw new InvalidOperationException(
				$"Git knowledge synchronization failed: {cause}.{cleanupDiagnostic}"
				+ (reported is null ? string.Empty : $" {reported}"));
		}
		return result;
	}

	private static TimeSpan ResolveDeadline(KnowledgeTransportRequest request) =>
		TimeSpan.FromMilliseconds(request.TransportDeadlineMilliseconds is > 0
			? request.TransportDeadlineMilliseconds.Value
			: DefaultTransportDeadlineMilliseconds);


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
