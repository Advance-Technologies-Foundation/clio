using FluentAssertions;

namespace Clio.Mcp.E2E.Support.Results;

/// <summary>
/// Unit tests for <see cref="TestResultsPayloadDumpSink"/> (GitHub issue #1537) — the only part of the
/// parse-failure diagnostic that touches the filesystem.
/// </summary>
/// <remarks>
/// These write real files, because where the file lands is the entire point: a dump that is not under the
/// repository's published <c>TestResults</c> directory is invisible on CI, which is the failure mode this
/// design exists to remove. They clean up after themselves and use no OS-specific paths.
/// <para>
/// The write-failure branch is provoked without permission games: the sink is pointed at a directory whose
/// parent is an ordinary file, which makes <c>Directory.CreateDirectory</c> throw on every OS this suite
/// runs on. That drives the sink's OWN catch rather than a copy of it; how a failed dump degrades the diagnostic is covered from the
/// caller's side in
/// <c>McpResultDiagnosticsTests.Describe_ShouldFallBackToAnExcerpt_WhenTheDumpCannotBeWritten</c>.
/// </para>
/// </remarks>
[TestFixture]
[Category("Integration")]
[Category("McpE2E.NoEnvironment")]
[Property("Module", "McpServer")]
public sealed class TestResultsPayloadDumpSinkTests {
	private readonly List<string> _writtenPaths = [];
	private readonly List<string> _scratchDirectories = [];

	[TearDown]
	public void TearDown() {
		foreach (string path in _writtenPaths.Where(File.Exists)) {
			File.Delete(path);
		}

		foreach (string directory in _scratchDirectories.Where(Directory.Exists)) {
			Directory.Delete(directory, recursive: true);
		}

		_writtenPaths.Clear();
		_scratchDirectories.Clear();
	}

	[Test]
	[Description("Writes the payload byte-for-byte, so the dump is a full-fidelity record rather than a processed one.")]
	public void Write_ShouldPersistThePayloadVerbatim() {
		// Arrange
		TestResultsPayloadDumpSink sink = new();
		const string payload = "{\"password\":\"hunter2\",\"note\":\"ünicode \U0001F600 and /Users/alex/x.json\"}";

		// Act
		McpPayloadDumpResult result = sink.Write("verbatim payload", payload);

		// Assert
		result.Succeeded.Should().BeTrue(
			because: $"the write must succeed in a normal checkout, and it reported: {result.FailureReason}");
		Track(result);
		File.ReadAllBytes(result.Path!).Should().StartWith(payload.Take(1).Select(c => (byte)c),
			because: "the file must open as plain JSON: Encoding.UTF8 would prepend a byte-order mark that jq and JSON.parse both reject");
		File.ReadAllText(result.Path!).Should().Be(payload,
			because: "the dump is deliberately exempt from redaction and bounding; anything else makes it a worse record than the log it replaced");
	}

	[Test]
	[Description("Places the dump under the repository's TestResults directory, which the TeamCity job already publishes as a build artifact.")]
	public void Write_ShouldPlaceTheDumpUnderTestResults() {
		// Arrange
		TestResultsPayloadDumpSink sink = new();

		// Act
		McpPayloadDumpResult result = sink.Write("placement", "{}");

		// Assert
		Track(result);
		result.Path.Should().NotBeNull(because: "the write was expected to succeed");
		string directory = Path.GetDirectoryName(result.Path!)!;
		Path.GetFileName(directory).Should().Be("mcp-payloads",
			because: "dumps live in their own subdirectory so they are distinguishable from the test-run output that shares TestResults");
		Path.GetFileName(Path.GetDirectoryName(directory)!).Should().Be("TestResults",
			because: "that directory is what Team_Atf_ClioMcpE2eTests publishes, so landing anywhere else makes the dump unreachable from a failed build");
	}

	[Test]
	[Description("Derives a filesystem-safe, bounded name from the caller's free-form label, which is prose rather than an identifier.")]
	public void Write_ShouldSlugifyTheLabel_WhenItIsProseWithPunctuation() {
		// Arrange
		TestResultsPayloadDumpSink sink = new();

		// Act
		McpPayloadDumpResult result = sink.Write("Could not parse list-apps MCP result: ", "{}");

		// Assert
		Track(result);
		string fileName = Path.GetFileName(result.Path!);
		fileName.Should().Contain("could-not-parse-list-apps-mcp-result",
			because: "the label is what tells a reader which failure a dump belongs to without opening it");
		fileName.Should().NotContainAny([":", " ", "/", "\\"],
			because: "the label reaches a path, so every character outside the whitelist must have been replaced rather than trusted");
	}

	[Test]
	[Description("Produces a distinct file per write, so two parse failures in the same second cannot overwrite each other.")]
	public void Write_ShouldProduceDistinctFiles_WhenCalledRepeatedlyWithinTheSameSecond() {
		// Arrange
		TestResultsPayloadDumpSink sink = new();

		// Act
		List<McpPayloadDumpResult> results =
			[.. Enumerable.Range(0, 20).Select(index => sink.Write("collision", $"{{\"i\":{index}}}"))];

		// Assert
		foreach (McpPayloadDumpResult result in results) {
			Track(result);
		}

		results.Select(result => result.Path).Should().OnlyHaveUniqueItems(
			because: "the suite runs two NUnit workers, so a name built from a per-second stamp alone would let one failure's dump overwrite another's");
		results.Should().AllSatisfy(
			result => result.Succeeded.Should().BeTrue(
				because: "every write must land, not merely the first"),
			because: "a sink that silently stops writing after the first call would hide every later failure's payload");
	}

	[Test]
	[Description("Reports a write failure instead of throwing, so the diagnostic path never replaces the parse failure it exists to explain.")]
	public void Write_ShouldReportTheFailure_WhenItsDirectoryCannotBeCreated() {
		// Arrange: a FILE standing where the sink's directory has to be. Directory.CreateDirectory throws
		// on every OS this suite runs on, and it needs no permission state the runner may or may not have.
		string blocker = Path.Combine(Path.GetTempPath(), $"clio-dump-blocker-{Guid.NewGuid():N}");
		File.WriteAllText(blocker, "not a directory");
		try {
			TestResultsPayloadDumpSink sink = new(Path.Combine(blocker, "mcp-payloads"));

			// Act
			McpPayloadDumpResult result = sink.Write("failure branch", "{}");

			// Assert
			result.Succeeded.Should().BeFalse(
				because: "a write that cannot happen must be reported, not pretended");
			result.FailureReason.Should().NotBeNullOrWhiteSpace(
				because: "a caller that gets neither a path nor a reason cannot tell a silent no-op from a successful dump");
			result.Path.Should().BeNull(
				because: "naming a file that does not exist sends the reader hunting a missing artifact");
		}
		finally {
			File.Delete(blocker);
		}
	}

	[Test]
	[Description("Deletes only this sink's dumps older than the cutoff, so retention bounds the directory without touching recent dumps or foreign files (GitHub issue #1593).")]
	public void SweepExpiredDumps_ShouldDeleteOnlyDumpsOlderThanTheCutoff() {
		// Arrange
		string directory = CreateScratchDirectory();
		DateTime cutoff = DateTime.UtcNow - TimeSpan.FromDays(1);
		string expired = CreateFile(directory, "expired.json", cutoff - TimeSpan.FromHours(1));
		string recent = CreateFile(directory, "recent.json", cutoff + TimeSpan.FromHours(1));
		string foreign = CreateFile(directory, "notes.txt", cutoff - TimeSpan.FromHours(1));

		// Act
		TestResultsPayloadDumpSink.SweepExpiredDumps(directory, cutoff);

		// Assert
		File.Exists(expired).Should().BeFalse(
			because: "a dump older than the retention window is what makes the directory grow without bound");
		File.Exists(recent).Should().BeTrue(
			because: "a dump inside the retention window may still be the evidence someone is reading");
		File.Exists(foreign).Should().BeTrue(
			because: "the sweep owns only the sink's own *.json files, not whatever else shares the directory");
	}

	[Test]
	[Description("Treats a missing dump directory as nothing to sweep, because the sweep runs on the diagnostic path and must never throw.")]
	public void SweepExpiredDumps_ShouldNotThrow_WhenTheDirectoryIsMissing() {
		// Arrange
		string missing = Path.Combine(CreateScratchDirectory(), "absent");

		// Act
		Action act = () => TestResultsPayloadDumpSink.SweepExpiredDumps(missing, DateTime.UtcNow);

		// Assert
		act.Should().NotThrow(
			because: "an exception here would replace the parse failure the dump exists to explain");
	}

	[Test]
	[Description("Sweeps expired dumps on the first write while keeping the dump that write produced, because the cutoff is keyed on the run's start (GitHub issue #1593).")]
	public void Write_ShouldSweepExpiredDumpsAndKeepItsOwn_WhenItIsTheFirstWriteIntoTheDirectory() {
		// Arrange
		string directory = CreateScratchDirectory();
		string expired = CreateFile(directory, "expired.json",
			DateTime.UtcNow - TestResultsPayloadDumpSink.RetentionWindow - TimeSpan.FromDays(1));
		TestResultsPayloadDumpSink sink = new(directory);

		// Act
		McpPayloadDumpResult result = sink.Write("retention", "{}");

		// Assert
		result.Succeeded.Should().BeTrue(
			because: $"the write must succeed into an ordinary directory, and it reported: {result.FailureReason}");
		File.Exists(expired).Should().BeFalse(
			because: "the first write of a run is where retention is applied");
		File.Exists(result.Path!).Should().BeTrue(
			because: "retention must never delete a dump written by the run that is still executing");
		TestResultsPayloadDumpSink.RetentionCutoffUtc.Should().BeBefore(File.GetLastWriteTimeUtc(result.Path!),
			because: "every dump this run writes is newer than the cutoff, so no later sweep in this run can reach it");
	}

	[Test]
	[Description("Resolves the published TestResults directory inside a checkout without creating it, so resolution alone never leaves a directory behind.")]
	public void ResolveDumpDirectory_ShouldNameTestResults_WhenStartedInsideACheckout() {
		// Arrange
		string checkout = CreateScratchDirectory();
		File.WriteAllText(Path.Combine(checkout, "clio.slnx"), "<Solution />");
		string start = Directory.CreateDirectory(Path.Combine(checkout, "bin", "Debug")).FullName;

		// Act
		string resolved = TestResultsPayloadDumpSink.ResolveDumpDirectory(start, CreateScratchDirectory());

		// Assert
		resolved.Should().Be(Path.Combine(checkout, "TestResults", "mcp-payloads"),
			because: "the first ancestor carrying the solution file is the published checkout");
		Directory.Exists(resolved).Should().BeFalse(
			because: "the directory is created by a write, not by working out where a write would go");
	}

	[Test]
	[Description("Names a per-process fallback outside a checkout without creating it, so a run that never dumps leaves nothing in temp (GitHub issue #1593).")]
	public void ResolveDumpDirectory_ShouldNameButNotCreateTheFallback_WhenStartedOutsideACheckout() {
		// Arrange
		string outside = CreateScratchDirectory();
		string tempRoot = CreateScratchDirectory();

		// Act
		string resolved = TestResultsPayloadDumpSink.ResolveDumpDirectory(outside, tempRoot);

		// Assert
		Path.GetDirectoryName(resolved).Should().Be(tempRoot,
			because: "the fallback belongs in the temp root the caller named");
		Path.GetFileName(resolved).Should().StartWith(TestResultsPayloadDumpSink.FallbackDirectoryPrefix,
			because: "the name tells a reader the dump was published nowhere, and lets a later run's sweep recognise it");
		Directory.EnumerateFileSystemEntries(tempRoot).Should().BeEmpty(
			because: "creating the directory at resolution time is the leak: one empty directory per process, never removed");
	}

	[Test]
	[Description("Creates the fallback on the first write, owner-only on Unix, and sweeps earlier runs' expired fallback directories (GitHub issue #1593).")]
	public void Write_ShouldCreateTheFallbackAndSweepExpiredSiblings_WhenWritingOutsideACheckout() {
		// Arrange
		string tempRoot = CreateScratchDirectory();
		DateTime expiredTime = DateTime.UtcNow - TestResultsPayloadDumpSink.RetentionWindow - TimeSpan.FromDays(1);
		string expiredFallback = CreateDirectory(tempRoot, TestResultsPayloadDumpSink.FallbackDirectoryPrefix + "old",
			expiredTime);
		string unrelated = CreateDirectory(tempRoot, "some-other-tool-old", expiredTime);
		string fallback = TestResultsPayloadDumpSink.ResolveDumpDirectory(CreateScratchDirectory(), tempRoot);
		TestResultsPayloadDumpSink sink = new(fallback);

		// Act
		McpPayloadDumpResult result = sink.Write("fallback", "{}");

		// Assert
		result.Succeeded.Should().BeTrue(
			because: $"the first write must create the fallback directory, and it reported: {result.FailureReason}");
		Path.GetDirectoryName(result.Path!).Should().Be(fallback,
			because: "the dump lands in the directory the resolution named");
		Directory.Exists(expiredFallback).Should().BeFalse(
			because: "each run has its own fallback directory, so only a sweep over their parent ever removes an earlier one");
		Directory.Exists(unrelated).Should().BeTrue(
			because: "the sweep owns only directories carrying the fallback prefix");
		if (!OperatingSystem.IsWindows()) {
			File.GetUnixFileMode(fallback).Should().Be(
				UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
				because: "the temp root is world-readable on Unix agents, so the payloads must be readable by their owner only");
		}
	}

	private string CreateScratchDirectory() {
		string directory = Path.Combine(Path.GetTempPath(), $"clio-dump-retention-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directory);
		_scratchDirectories.Add(directory);
		return directory;
	}

	private static string CreateFile(string directory, string name, DateTime lastWriteTimeUtc) {
		string path = Path.Combine(directory, name);
		File.WriteAllText(path, "{}");
		File.SetLastWriteTimeUtc(path, lastWriteTimeUtc);
		return path;
	}

	private static string CreateDirectory(string parent, string name, DateTime lastWriteTimeUtc) {
		string path = Path.Combine(parent, name);
		Directory.CreateDirectory(path);
		File.WriteAllText(Path.Combine(path, "dump.json"), "{}");
		Directory.SetLastWriteTimeUtc(path, lastWriteTimeUtc);
		return path;
	}

	private void Track(McpPayloadDumpResult result) {
		if (result.Path is not null) {
			_writtenPaths.Add(result.Path);
		}
	}
}
