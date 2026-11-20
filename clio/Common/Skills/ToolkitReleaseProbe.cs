using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Clio.Common.Skills;

/// <summary>
/// Reads the commit currently published on the toolkit's release branch.
/// </summary>
public interface IToolkitReleaseProbe {
	/// <summary>
	/// Returns the commit at <c>refs/heads/release</c> of the default toolkit repository, or <c>null</c> when
	/// it cannot be determined.
	/// </summary>
	/// <returns>
	/// A lower-case 40-character commit id, or <c>null</c> when <c>git</c> is unavailable, the remote is
	/// unreachable, the call times out, or the answer is not exactly one well-formed line. Callers must treat
	/// <c>null</c> as "unknown" and update, never as "up to date".
	/// </returns>
	string ReadReleaseCommit();
}

/// <inheritdoc />
public sealed class ToolkitReleaseProbe(IProcessExecutor processExecutor) : IToolkitReleaseProbe {
	/// <summary>Upper bound for the remote query; a hang must never stall an update for long.</summary>
	internal static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(20);

	/// <summary>The fully qualified release ref; a bare branch name would also match <c>*/release</c>.</summary>
	internal const string ReleaseRefName = "refs/heads/" + ToolkitDistribution.ReleaseRef;

	private const long MaximumCapturedCharacters = 64 * 1024;

	private static readonly char[] RefSeparators = ['\t', ' '];

	private static readonly IReadOnlyDictionary<string, string> GitEnvironment =
		new Dictionary<string, string>(StringComparer.Ordinal) {
			// A credential prompt would wait forever on a detached or MCP caller: no terminal prompt, and no
			// Git Credential Manager dialog either.
			["GIT_TERMINAL_PROMPT"] = "0",
			["GCM_INTERACTIVE"] = "never"
		};

	/// <inheritdoc />
	public string ReadReleaseCommit() {
		try {
			ProcessExecutionResult result = processExecutor.ExecuteAndCaptureAsync(new ProcessExecutionOptions("git", string.Empty) {
				ArgumentList = ["ls-remote", ToolkitDistribution.MarketplaceGitUrl, ReleaseRefName],
				// Never the caller's directory: a repository there would apply its own .git/config (for
				// example url.*.insteadOf) to this query.
				WorkingDirectory = Path.GetFullPath(SettingsRepository.AppSettingsFolderPath),
				Timeout = QueryTimeout,
				SuppressErrors = true,
				ResolveProgramPath = true,
				EnvironmentVariables = GitEnvironment,
				MaximumCapturedOutputCharacters = MaximumCapturedCharacters
			}).GetAwaiter().GetResult();
			bool succeeded = result.Started && result.ExitCode == 0 && !result.TimedOut && !result.Canceled
				&& !result.ResourceLimitExceeded;
			return succeeded ? ParseReleaseCommit(result.StandardOutput) : null;
		}
		catch (Exception exception) when (exception is not OutOfMemoryException) {
			// No git on PATH, or a launch failure: unknown, so the caller updates as before.
			return null;
		}
	}

	/// <summary>
	/// Extracts the release commit from <c>git ls-remote</c> output, requiring exactly one line whose ref is
	/// exactly <see cref="ReleaseRefName"/> and whose id is a full commit.
	/// </summary>
	internal static string ParseReleaseCommit(string output) {
		List<string> commits = (output ?? string.Empty)
			.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Select(line => line.Split(RefSeparators, StringSplitOptions.RemoveEmptyEntries))
			.Where(parts => parts.Length == 2 && string.Equals(parts[1], ReleaseRefName, StringComparison.Ordinal))
			.Select(parts => GitCommitId.Normalize(parts[0]))
			.ToList();
		return commits.Count == 1 ? commits[0] : null;
	}
}
