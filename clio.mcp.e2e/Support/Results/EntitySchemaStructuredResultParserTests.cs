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
[Property("Module", "McpServer")]
public sealed class EntitySchemaStructuredResultParserTests {
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
		act.Should().Throw<InvalidOperationException>(
				because: "there is no structured content and no text content to parse")
			.WithMessage("*no structured content and no text content at all*",
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
		exception.Message.Should().Contain("JSON present but not shaped like the expected type",
			because: "valid JSON was parsed but its shape does not match SampleEnvelope");
		exception.InnerException.Should().BeOfType<JsonException>(
			because: "the underlying deserialize failure must be threaded through as the inner exception");
		exception.Message.Should().Contain("LastJsonError=",
			because: "the last JsonException's message must also appear in the text, not only as an inner exception");
	}

	[Test]
	[Description("Includes the actual payload text (an unhandled-exception message forwarded verbatim) in the thrown message so the failure is self-explaining without re-running the call.")]
	public void Extract_ShouldIncludePayloadText_WhenContentCarriesAnErrorMessage() {
		// Arrange
		const string serverErrorText = "System.NullReferenceException: Object reference not set to an instance of an object.";
		CallToolResult callResult = new() {
			IsError = true,
			Content = [new TextContentBlock { Text = serverErrorText }]
		};

		// Act
		Action act = () => EntitySchemaStructuredResultParser.Extract<SampleEnvelope>(callResult);

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "the server's unhandled-exception text is not JSON")
			.WithMessage("*NullReferenceException*",
				because: "the whole point of this diagnostic is that the payload's own error text must be visible in the thrown message, not discarded");
	}

	[Test]
	[Description("Truncates a very long payload to the documented cap and reports the total original length, instead of flooding the CI log unbounded.")]
	public void Extract_ShouldTruncatePayload_WhenTextContentIsVeryLong() {
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
		exception.Message.Length.Should().BeLessThan(hugeText.Length,
			because: "the composed message must be capped rather than embedding the full 10,000-character payload");
		exception.Message.Should().Contain("truncated",
			because: "the cap must be explicit, not a silent cut");
		exception.Message.Should().MatchRegex(@"truncated, \d+ characters total",
			because: "the note must report the total original character count, not just say 'truncated'");
	}

	[Test]
	[Description("Redacts an absolute file path embedded in the payload text before it is surfaced in the thrown message.")]
	public void Extract_ShouldRedactSensitiveText_WhenPayloadCarriesAnAbsolutePath() {
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
		exception.Message.Should().NotContain("/Users/alex/secrets/credentials.json",
			because: "an absolute path must be redacted before it reaches the thrown message, same as any other MCP-surfaced text");
		exception.Message.Should().Contain("[redacted-path]",
			because: "the redactor replaces an absolute path with its stable placeholder rather than dropping the whole message");
	}
}
