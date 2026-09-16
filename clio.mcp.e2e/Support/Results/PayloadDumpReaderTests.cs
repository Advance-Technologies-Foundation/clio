using FluentAssertions;

namespace Clio.Mcp.E2E.Support.Results;

/// <summary>
/// Unit tests for <see cref="PayloadDumpReader"/> (GitHub issue #1537) — the only way a test reads back
/// what a tool actually returned, and the entire mechanism keeping dumps from PASSING tests out of the
/// published <c>TestResults</c> artifact.
/// </summary>
/// <remarks>
/// The messages are composed here rather than obtained from <see cref="McpResultDiagnostics"/>, so a
/// change to the message format fails these tests loudly instead of leaving the reader silently returning
/// <c>null</c> for every dump. Real files are written into the sink's own directory, because path
/// confinement is one of the behaviours under test and a synthetic path would not exercise it.
/// </remarks>
[TestFixture]
[Category("Unit")]
[Category("McpE2E.NoEnvironment")]
[Property("Module", "McpServer")]
public sealed class PayloadDumpReaderTests {
	private readonly List<string> _writtenPaths = [];

	[TearDown]
	public void TearDown() {
		foreach (string path in _writtenPaths.Where(File.Exists)) {
			File.Delete(path);
		}

		_writtenPaths.Clear();
	}

	[Test]
	[Description("Extracts the dump path from the successful-write message, which is the ordinary case every reading test depends on.")]
	public void ExtractPath_ShouldReturnThePath_WhenTheMessageNamesADump() {
		// Arrange
		string dumpPath = WriteDump("{}");
		string message = $"IsError=True StructuredContent=(none) Content=1 block(s) Payload=\"{dumpPath}\"";

		// Act
		string? extracted = PayloadDumpReader.ExtractPath(message);

		// Assert
		extracted.Should().Be(dumpPath,
			because: "following the pointer the message leaves is the only way a test asserts on what the tool returned");
	}

	[Test]
	[Description("Still finds the path when the caller appended its own text after the description, as the ApplicationToolE2ETests re-throw does.")]
	public void ExtractPath_ShouldReturnThePath_WhenTextFollowsTheDescription() {
		// Arrange: the wrapped form "{message} Raw result: {description}" the poll loop re-throws. Its
		// trailing JSON contains no Word= token, so a terminator that ran to end-of-string swallowed it
		// and the cleanup call silently no-opped, leaving one dump per poll in the artifact.
		string dumpPath = WriteDump("{}");
		string message =
			$"Could not parse list-apps MCP result: IsError=True Content=1 block(s) Payload=\"{dumpPath}\""
			+ " Raw result: {\"IsError\":true,\"Content\":[{\"text\":\"anything at all\"}]}";

		// Act
		string? extracted = PayloadDumpReader.ExtractPath(message);

		// Assert
		extracted.Should().Be(dumpPath,
			because: "the path is delimited by its own quotes, so whatever a caller appends after the description cannot extend it");
	}

	[Test]
	[Description("Returns null for the failed-write message form, which names a reason rather than a file.")]
	public void ExtractPath_ShouldReturnNull_WhenTheDumpCouldNotBeWritten() {
		// Arrange
		const string message =
			"IsError=True Content=1 block(s) Payload=(dump failed: IOException: No space left on device.) "
			+ "PayloadExcerpt=\"the server reported an authentication failure\"";

		// Act
		string? extracted = PayloadDumpReader.ExtractPath(message);

		// Assert
		extracted.Should().BeNull(
			because: "a reason is not a path; handing '(dump failed: …)' back as one sends the reader hunting a file that was never written");
	}

	[Test]
	[Description("Refuses a path planted in the server-supplied part of the message, which redaction does not neutralize when it is relative.")]
	public void ExtractPath_ShouldReturnNull_WhenThePathComesFromServerSuppliedText() {
		// Arrange: SensitiveErrorTextRedactor neutralizes absolute paths and URIs, but a RELATIVE token
		// survives into the excerpt, so a tool result could otherwise choose which file gets deleted.
		const string message =
			"IsError=True LastJsonError=\"unexpected token near Payload=\\\"obj/project.assets.json\\\"\" "
			+ "Content=1 block(s) Payload=(dump failed: IOException: No space left on device.) "
			+ "PayloadExcerpt=\"Payload=\\\"../../clio.slnx\\\"\"";

		// Act
		string? extracted = PayloadDumpReader.ExtractPath(message);

		// Assert
		extracted.Should().BeNull(
			because: "only a path resolving inside the sink's own dump directory may be acted on; the worst a planted token can then name is another dump, never a file the suite depends on");
	}

	[Test]
	[Description("Deletes the dump a message names, which is what keeps a passing test's payload out of the published artifact.")]
	public void DeleteIfPresent_ShouldRemoveTheNamedFile() {
		// Arrange
		string dumpPath = WriteDump("{\"kept\":false}");
		string message = $"IsError=True Content=1 block(s) Payload=\"{dumpPath}\"";

		// Act
		PayloadDumpReader.DeleteIfPresent(message);

		// Assert
		File.Exists(dumpPath).Should().BeFalse(
			because: "dumps from tests that PASSED are exactly what makes the one dump belonging to a real failure hard to find");
	}

	[Test]
	[Description("Does nothing when the message names no dump, since every call site sits in a catch block on a passing test's path.")]
	public void DeleteIfPresent_ShouldNotThrow_WhenTheMessageNamesNoDump() {
		// Arrange
		const string message = "IsError=True Content=(none) Payload=(dump failed: IOException: nope)";

		// Act
		Action act = () => PayloadDumpReader.DeleteIfPresent(message);

		// Assert
		act.Should().NotThrow(
			because: "an exception here would replace a passing test's outcome with an unrelated IO error");
	}

	[Test]
	[Description("Does not throw when the named file is already gone, the race a transient lock or a parallel sweep produces on the Windows agent.")]
	public void DeleteIfPresent_ShouldNotThrow_WhenTheNamedFileIsAlreadyGone() {
		// Arrange
		string dumpPath = WriteDump("{}");
		File.Delete(dumpPath);
		string message = $"IsError=True Content=1 block(s) Payload=\"{dumpPath}\"";

		// Act
		Action act = () => PayloadDumpReader.DeleteIfPresent(message);

		// Assert
		act.Should().NotThrow(
			because: "a leftover or missing dump is harmless; a hijacked test result is not");
	}

	[Test]
	[Description("Returns the dump's content and removes the file, so one run's artifacts cannot be mistaken for the next run's evidence.")]
	public void ReadAndDelete_ShouldReturnTheContentAndRemoveTheFile() {
		// Arrange
		const string payload = "{\"password\":\"hunter2\"}";
		string dumpPath = WriteDump(payload);
		string message = $"IsError=True Content=1 block(s) Payload=\"{dumpPath}\"";

		// Act
		string content = PayloadDumpReader.ReadAndDelete(message);

		// Assert
		content.Should().Be(payload,
			because: "the dump is the full-fidelity record the message deliberately no longer carries");
		File.Exists(dumpPath).Should().BeFalse(
			because: "a read dump has served its purpose, and leaving it behind pollutes the artifact the same way a passing test's would");
	}

	[Test]
	[Description("Throws a named failure rather than returning empty when the message points at no dump at all.")]
	public void ReadAndDelete_ShouldThrow_WhenTheMessageNamesNoDump() {
		// Arrange
		const string message = "IsError=True Content=(none) Payload=(dump failed: IOException: nope)";

		// Act
		Action act = () => PayloadDumpReader.ReadAndDelete(message);

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "a test asserting on the payload must fail loudly when there is none, not silently compare against an empty string")
			.WithMessage("*names no payload dump*");
	}

	[Test]
	[Description("Throws when the message names a dump whose file is absent, which separates 'never written' from 'written and swept'.")]
	public void ReadAndDelete_ShouldThrow_WhenTheNamedFileIsAbsent() {
		// Arrange
		string dumpPath = WriteDump("{}");
		File.Delete(dumpPath);
		string message = $"IsError=True Content=1 block(s) Payload=\"{dumpPath}\"";

		// Act
		Action act = () => PayloadDumpReader.ReadAndDelete(message);

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "a named file that is not there is a different defect from a message that named none, and the reader must say which happened")
			.WithMessage("*but no file is there*");
	}

	/// <summary>
	/// Writes a real dump through the real sink and registers it for cleanup.
	/// </summary>
	private string WriteDump(string payload) {
		TestResultsPayloadDumpSink sink = new();
		McpPayloadDumpResult result = sink.Write("payload dump reader", payload);
		result.Succeeded.Should().BeTrue(
			because: $"the arrange step needs a real dump inside the sink's own directory, and the write reported: {result.FailureReason}");
		_writtenPaths.Add(result.Path!);
		return result.Path!;
	}
}
