using System.Text;

namespace Clio.Mcp.E2E.Support.Results;

/// <summary>
/// Where a parse-failure payload dump ended up, or why it could not be written.
/// </summary>
/// <param name="Path">The written file's full path, or <c>null</c> when the write failed.</param>
/// <param name="FailureReason">
/// Why the write failed, or <c>null</c> on success. Never both <c>null</c>: a sink either produces a path
/// or explains itself, because a caller that gets neither has no way to tell a silent no-op from a
/// successful dump.
/// </param>
internal sealed record McpPayloadDumpResult(string? Path, string? FailureReason) {
	/// <summary>Whether the payload reached a file.</summary>
	public bool Succeeded => Path is not null;
}

/// <summary>
/// Writes the raw payload of an MCP tool result that no parser could read, so the bytes that actually
/// arrived survive the test run instead of being squeezed into an exception message (GitHub issue #1537).
/// </summary>
/// <remarks>
/// Behind an interface so tests can observe what would be written without touching the filesystem — the
/// suite runs two NUnit workers in parallel and a per-test disk write would be both slow and racy.
/// </remarks>
internal interface IMcpPayloadDumpSink {
	/// <summary>
	/// Writes <paramref name="rawPayload"/> verbatim and returns where it landed.
	/// </summary>
	/// <remarks>
	/// Implementations must not throw. This runs on the path whose whole job is to report someone else's
	/// failure, so an exception here would replace the parse failure the caller is trying to explain with
	/// an unrelated one; a write that cannot happen is reported through
	/// <see cref="McpPayloadDumpResult.FailureReason"/> instead.
	/// </remarks>
	/// <param name="label">The caller's own description of the failure, used to name the file.</param>
	/// <param name="rawPayload">The payload exactly as it arrived. Never modified, never redacted.</param>
	McpPayloadDumpResult Write(string label, string rawPayload);
}

/// <summary>
/// Writes payload dumps into the repository's <c>TestResults</c> directory.
/// </summary>
/// <remarks>
/// That directory is the one place a dump is reachable from BOTH ends of this suite's life. Locally it is
/// covered by <c>.gitignore</c>'s <c>[Tt]est[Rr]esult*/</c> rule, so dumps never dirty a working tree; on
/// the <c>Team_Atf_ClioMcpE2eTests</c> agent it is already a published build artifact (verified on build
/// 16026618, where it is published carrying only its <c>.gitkeep</c>), so a dump is downloadable from the
/// failed build with no TeamCity configuration change and no <c>##teamcity[publishArtifacts]</c> service
/// message.
/// <para>
/// Falling back to the temp directory keeps a local run useful, but it is NOT equivalent: a dump in temp on
/// a CI agent is published nowhere and is gone with the agent's cleanup. The fallback directory therefore
/// names itself so the reader can tell the two outcomes apart rather than hunting an artifact that was
/// never produced.
/// </para>
/// </remarks>
internal sealed class TestResultsPayloadDumpSink : IMcpPayloadDumpSink {
	/// <summary>
	/// Both markers must be present for a directory to count as the repository root. <c>clio.slnx</c>
	/// alone is not enough — a checkout laid out differently, or a nested sample repository created by an
	/// e2e fixture, can carry a solution file without being the checkout whose <c>TestResults</c> the CI
	/// job publishes.
	/// </summary>
	private const string SolutionMarker = "clio.slnx";

	private const string TestResultsDirectoryName = "TestResults";

	private const string DumpDirectoryName = "mcp-payloads";

	/// <summary>
	/// Longest slug taken from a caller's label. The Windows agent this suite runs on enforces a path
	/// length, and the label is free-form prose composed at the call site rather than a short identifier.
	/// </summary>
	private const int MaxSlugLength = 60;

	/// <summary>Characters of the GUID kept to make a file name unique.</summary>
	private const int UniqueSuffixLength = 8;

	/// <inheritdoc />
	public McpPayloadDumpResult Write(string label, string rawPayload) {
		try {
			string directory = ResolveDumpDirectory();

			// Created on every write rather than once: a run that cleans its working directory midway
			// would otherwise turn every later dump into a write failure for no reason.
			Directory.CreateDirectory(directory);

			// The unique part is a GUID fragment, not a counter. Two NUnit workers run in parallel
			// (NumberOfTestWorkers=2 in clio.mcp.e2e.runsettings), so a shared counter would be mutable
			// state across threads, and a UTC stamp alone repeats within the same second.
			string uniqueSuffix = Guid.NewGuid().ToString("N")[..UniqueSuffixLength];
			string fileName = $"{DateTime.UtcNow:yyyyMMdd'T'HHmmss'Z'}-{Slugify(label)}-{uniqueSuffix}.json";
			string path = Path.Combine(directory, fileName);

			File.WriteAllText(path, rawPayload, Encoding.UTF8);
			return new McpPayloadDumpResult(path, null);
		}
		catch (Exception exception) {
			return new McpPayloadDumpResult(null, $"{exception.GetType().Name}: {exception.Message}");
		}
	}

	/// <summary>
	/// Finds the repository's <c>TestResults/mcp-payloads</c>, or a temp-directory stand-in when this
	/// assembly is not running from inside a checkout.
	/// </summary>
	private static string ResolveDumpDirectory() {
		string? repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
		return repositoryRoot is null
			? Path.Combine(Path.GetTempPath(), "clio-mcp-e2e-payloads-not-published", DumpDirectoryName)
			: Path.Combine(repositoryRoot, TestResultsDirectoryName, DumpDirectoryName);
	}

	/// <summary>
	/// Walks up from the test assembly's own directory looking for the checkout root — the directory
	/// carrying BOTH the solution and the <c>TestResults</c> the CI job publishes.
	/// </summary>
	private static string? FindRepositoryRoot(string startDirectory) {
		DirectoryInfo? candidate = new(startDirectory);
		while (candidate is not null) {
			if (File.Exists(Path.Combine(candidate.FullName, SolutionMarker)) &&
				Directory.Exists(Path.Combine(candidate.FullName, TestResultsDirectoryName))) {
				return candidate.FullName;
			}

			candidate = candidate.Parent;
		}

		return null;
	}

	/// <summary>
	/// Reduces a caller's free-form label to a bounded, filesystem-safe fragment.
	/// </summary>
	/// <remarks>
	/// Whitelisting is deliberate: the label is a sentence the call site wrote ("Could not parse list-apps
	/// MCP result: "), not a validated identifier, and it is going into a path. Parsing the tool name out
	/// of the prose would assume a sentence shape every future call site has to keep; a slug of the whole
	/// label assumes nothing.
	/// </remarks>
	private static string Slugify(string label) {
		StringBuilder builder = new(label.Length);
		bool lastWasSeparator = true;
		foreach (char character in label) {
			if (char.IsAsciiLetterOrDigit(character)) {
				builder.Append(char.ToLowerInvariant(character));
				lastWasSeparator = false;
				continue;
			}

			if (!lastWasSeparator && builder.Length > 0) {
				builder.Append('-');
				lastWasSeparator = true;
			}
		}

		string slug = builder.ToString().Trim('-');
		if (slug.Length > MaxSlugLength) {
			slug = slug[..MaxSlugLength].TrimEnd('-');
		}

		return slug.Length == 0 ? "mcp-result" : slug;
	}
}
