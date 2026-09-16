using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Results;

/// <summary>
/// Unit tests for <see cref="EntitySchemaStructuredResultParser.Extract{T}"/>'s parse-failure diagnostics
/// (GitHub issue #1384). They construct <see cref="CallToolResult"/> instances in-memory (no MCP server, no
/// stand, no network I/O), so they are categorized <c>Unit</c> rather than <c>McpE2E.Sandbox</c>.
/// </summary>
[TestFixture]
[Category("Unit")]
[Category("McpE2E.NoEnvironment")]
[Property("Module", "McpServer")]
public sealed class EntitySchemaStructuredResultParserTests {
	/// <summary>
	/// Messages whose dump must be removed however the test ends.
	/// </summary>
	/// <remarks>
	/// Cleanup lives in <c>[TearDown]</c>, not at the end of the Assert block. FluentAssertions throws on
	/// the first failed assertion, so a delete placed after the assertions is skipped on exactly the runs
	/// that matter — the failing ones, whose artifact has to be readable — and the leftovers are then
	/// mistaken for the dump belonging to the failure under investigation.
	/// </remarks>
	private readonly List<string> _messagesWithDumps = [];

	[TearDown]
	public void TearDown() {
		foreach (string message in _messagesWithDumps) {
			PayloadDumpReader.DeleteIfPresent(message);
		}

		_messagesWithDumps.Clear();
	}

	/// <summary>Registers a failure message so its dump is removed even if an assertion above fails.</summary>
	private string TrackDump(string message) {
		_messagesWithDumps.Add(message);
		return message;
	}

	/// <summary>A DTO shape that a plain JSON object with a non-numeric "code" cannot satisfy.</summary>
	private sealed record SampleEnvelope(int Code);

	[Test]
	[Description("Reports 'no structured content and no text content at all' when the tool result carries neither StructuredContent nor Content.")]
	public void Extract_ShouldThrowWithNoPayloadShape_WhenResultIsEmpty() {
		// Arrange
		CallToolResult callResult = new() {
			IsError = false,
			Content = []
		};

		// Act
		Action act = () => EntitySchemaStructuredResultParser.Extract<SampleEnvelope>(callResult);

		// Assert
		InvalidOperationException exception = act.Should().Throw<InvalidOperationException>(
				because: "there is no structured content and no text content to parse")
			.Which;
		TrackDump(exception.Message);
		exception.Message.Should().Match("*no structured content and no text content at all*",
			because: "the message must name the exact shape mismatch observed");
	}

	[Test]
	[Description("Reports 'text content present but not JSON' and preserves the JsonException message when the tool result's text content is not JSON, e.g. an HTML login page.")]
	public void Extract_ShouldThrowWithTextNotJsonShape_WhenContentIsHtmlLoginPage() {
		// Arrange
		const string htmlLoginPage = "<html><body>Please <a href=\"/login\">sign in</a> to continue.</body></html>";
		CallToolResult callResult = new() {
			IsError = true,
			Content = [new TextContentBlock { Text = htmlLoginPage }]
		};

		// Act
		Action act = () => EntitySchemaStructuredResultParser.Extract<SampleEnvelope>(callResult);

		// Assert
		InvalidOperationException exception = act.Should().Throw<InvalidOperationException>(
				because: "the HTML page is not JSON and cannot be parsed as SampleEnvelope")
			.Which;
		TrackDump(exception.Message);
		exception.Message.Should().Contain("text content present but not JSON",
			because: "text was found but never parsed as JSON");
		exception.Message.Should().Contain("IsError=True",
			because: "an authentication rejection is exactly the kind of failure IsError should surface");
		exception.InnerException.Should().BeOfType<JsonException>(
			because: "the JsonException raised while trying to parse the HTML as JSON must be preserved, not discarded");
	}

	[Test]
	[Description("Reports 'JSON present but not shaped like the expected type' and preserves the JsonException message when the payload is valid JSON but does not match the DTO shape.")]
	public void Extract_ShouldThrowWithJsonShapeMismatch_WhenPayloadDoesNotMatchDto() {
		// Arrange
		const string mismatchedJson = /*lang=json,strict*/ "{\"Code\":\"not-a-number\"}";
		CallToolResult callResult = new() {
			IsError = false,
			Content = [new TextContentBlock { Text = mismatchedJson }]
		};

		// Act
		Action act = () => EntitySchemaStructuredResultParser.Extract<SampleEnvelope>(callResult);

		// Assert
		InvalidOperationException exception = act.Should().Throw<InvalidOperationException>(
				because: "\"not-a-number\" cannot be deserialized into the DTO's int Code property")
			.Which;
		TrackDump(exception.Message);
		exception.Message.Should().Contain("JSON present but not shaped like the expected type",
			because: "valid JSON was parsed but its shape does not match SampleEnvelope");
		exception.InnerException.Should().BeOfType<JsonException>(
			because: "the underlying deserialize failure must be threaded through as the inner exception");
		exception.Message.Should().Contain("LastJsonError=",
			because: "the last JsonException's message must also appear in the text, not only as an inner exception");
		exception.Message.Should().Contain("$.Code",
			because: "the kept JsonException names the offending JSON path, which is what turns a shape mismatch into an actionable report");
		exception.Message.Should().MatchRegex(@"LineNumber: \d+",
			because: "the kept JsonException also names where in the payload the mismatch is, rather than only that one happened");
	}

	[Test]
	[Description("Dumps the actual payload text (an unhandled-exception message forwarded verbatim) to the file the thrown message names, so the failure is self-explaining without re-running the call.")]
	public void Extract_ShouldDumpPayloadText_WhenContentCarriesAnErrorMessage() {
		// Arrange
		const string serverErrorText = "System.NullReferenceException: Object reference not set to an instance of an object.";
		CallToolResult callResult = new() {
			IsError = true,
			Content = [new TextContentBlock { Text = serverErrorText }]
		};

		// Act
		Action act = () => EntitySchemaStructuredResultParser.Extract<SampleEnvelope>(callResult);

		// Assert
		InvalidOperationException exception = act.Should().Throw<InvalidOperationException>(
				because: "the server's unhandled-exception text is not JSON")
			.Which;
		TrackDump(exception.Message);
		PayloadDumpReader.ReadAndDelete(exception.Message).Should().Contain("NullReferenceException",
			because: "the whole point of this diagnostic is that the payload's own error text survives the failure; since #1537 it survives in the dump the message names rather than in the message itself");
	}

	[Test]
	[Description("Keeps a very long payload out of the thrown message entirely, dumping it whole instead of flooding the CI log or cutting it down to a fragment.")]
	public void Extract_ShouldKeepTheMessageSmall_WhenTextContentIsVeryLong() {
		// Arrange
		string hugeText = new string('a', 10_000);
		CallToolResult callResult = new() {
			IsError = false,
			Content = [new TextContentBlock { Text = hugeText }]
		};

		// Act
		Action act = () => EntitySchemaStructuredResultParser.Extract<SampleEnvelope>(callResult);

		// Assert
		InvalidOperationException exception = act.Should().Throw<InvalidOperationException>(
				because: "the 10,000-character payload is not JSON")
			.Which;
		TrackDump(exception.Message);
		exception.Message.Length.Should().BeLessThan(1_000,
			because: "the message carries metadata and a path now, so its length no longer scales with the payload's at all");
		PayloadDumpReader.ReadAndDelete(exception.Message).Should().Contain(hugeText,
			because: "the dump holds the payload WHOLE - the cap that used to keep only its beginning is gone, and with it the redaction pass whose one-second budget the cap existed to protect (#1537)");
	}

	[Test]
	[Description("Does not redact the payload on its way to the dump, which is a file rather than the build log.")]
	public void Extract_ShouldNotRedactTheDumpedPayload_WhenPayloadCarriesAnAbsolutePath() {
		// Arrange
		const string sensitiveText = "Failed reading /Users/alex/secrets/credentials.json: invalid format";
		CallToolResult callResult = new() {
			IsError = true,
			Content = [new TextContentBlock { Text = sensitiveText }]
		};

		// Act
		Action act = () => EntitySchemaStructuredResultParser.Extract<SampleEnvelope>(callResult);

		// Assert
		InvalidOperationException exception = act.Should().Throw<InvalidOperationException>(
				because: "the payload is not JSON")
			.Which;
		TrackDump(exception.Message);
		exception.Message.Should().NotContain("/Users/alex/secrets/credentials.json",
			because: "the message reaches the build log and carries no payload text at all any more");
		PayloadDumpReader.ReadAndDelete(exception.Message).Should().Contain(sensitiveText,
			because: "the dump is a full-fidelity record of what the tool returned; redacting it is what the design deliberately dropped (#1537)");
	}

	[Test]
	[Description("Still suppresses the doomed wrapper exception for an object-shaped T, so the array-shaped fix does not reintroduce blaming the content-item wrapper.")]
	public void Extract_ShouldSuppressTheWrapperJsonError_WhenTheExpectedTypeIsObjectShaped() {
		// Arrange
		CallToolResult callResult = new() {
			IsError = false,
			StructuredContent = JsonDocument.Parse("[{\"Code\":\"not-a-number\"}]").RootElement.Clone()
		};

		// Act
		Action act = () => EntitySchemaStructuredResultParser.Extract<SampleEnvelope>(callResult);

		// Assert
		InvalidOperationException exception = act.Should().Throw<InvalidOperationException>(
				because: "an array cannot satisfy an object-shaped envelope")
			.Which;
		TrackDump(exception.Message);
		exception.Message.Should().NotContain("LastJsonError=",
			because: "the array here is the MCP content-item wrapper falling through, and its always-doomed exception must not be blamed for a mismatch the real payload caused");
		exception.Message.Should().Contain("JSON present but not shaped like the expected type",
			because: "suppressing the blame must not make the parser claim there was no JSON beside a dump of that very array");
	}
}
