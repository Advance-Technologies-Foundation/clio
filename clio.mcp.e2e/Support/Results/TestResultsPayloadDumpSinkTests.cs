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

	[TearDown]
	public void TearDown() {
		foreach (string path in _writtenPaths.Where(File.Exists)) {
			File.Delete(path);
		}

		_writtenPaths.Clear();
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

	private void Track(McpPayloadDumpResult result) {
		if (result.Path is not null) {
			_writtenPaths.Add(result.Path);
		}
	}
}
