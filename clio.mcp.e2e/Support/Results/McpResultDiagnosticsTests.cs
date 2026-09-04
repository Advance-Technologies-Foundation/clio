using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Results;

/// <summary>
/// Unit tests for <see cref="McpResultDiagnostics"/> (GitHub issue #1384), the payload-describing helper
/// shared by <see cref="EntitySchemaStructuredResultParser"/> and every sibling result parser in this
/// folder. They construct <see cref="CallToolResult"/> instances in-memory (no MCP server, no stand, no
/// network I/O), so they are categorized <c>Unit</c> rather than <c>McpE2E.Sandbox</c>.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class McpResultDiagnosticsTests {
	[Test]
	[Description("Describes a result with neither StructuredContent nor Content as IsError plus '(none)' for both payload fields.")]
	public void Describe_ShouldReportNonePayloads_WhenResultIsEmpty() {
		// Arrange
		CallToolResult callResult = new() {
			IsError = false,
			Content = []
		};

		// Act
		string description = McpResultDiagnostics.Describe(callResult);

		// Assert
		description.Should().Contain("IsError=False",
			because: "the call did not report an error");
		description.Should().Contain("StructuredContent=(none)",
			because: "no StructuredContent was present on the result");
		description.Should().Contain("Content=(none)",
			because: "an empty Content array carries no items to describe");
	}

	[Test]
	[Description("Describes a text Content item by rendering its type and text verbatim.")]
	public void Describe_ShouldRenderContentItem_WhenResultCarriesTextPayload() {
		// Arrange
		const string payloadText = "plain diagnostic text";
		CallToolResult callResult = new() {
			IsError = true,
			Content = [new TextContentBlock { Text = payloadText }]
		};

		// Act
		string description = McpResultDiagnostics.Describe(callResult);

		// Assert
		description.Should().Contain("IsError=True",
			because: "the call reported an error");
		description.Should().Contain($"{{type=text, text=\"{payloadText}\"}}",
			because: "the single text content item's type and text must both be rendered");
	}

	[Test]
	[Description("Describes StructuredContent by dumping its raw serialized JSON.")]
	public void Describe_ShouldRenderStructuredContent_WhenResultCarriesStructuredPayload() {
		// Arrange
		CallToolResult callResult = new() {
			IsError = false,
			Content = [],
			StructuredContent = JsonSerializer.SerializeToElement(new { code = 7 })
		};

		// Act
		string description = McpResultDiagnostics.Describe(callResult);

		// Assert
		description.Should().Contain("StructuredContent={\"code\":7}",
			because: "the structured payload's raw JSON must be embedded so the actual shape mismatch is visible");
	}

	[Test]
	[Description("Redacts an absolute file path embedded in a Content item's text.")]
	public void Describe_ShouldRedactSensitiveText_WhenContentCarriesAnAbsolutePath() {
		// Arrange
		const string sensitiveText = "Failed reading /Users/alex/secrets/credentials.json: invalid format";
		CallToolResult callResult = new() {
			IsError = true,
			Content = [new TextContentBlock { Text = sensitiveText }]
		};

		// Act
		string description = McpResultDiagnostics.Describe(callResult);

		// Assert
		description.Should().NotContain("/Users/alex/secrets/credentials.json",
			because: "an absolute path must be redacted before it reaches the diagnostic text");
		description.Should().Contain("[redacted-path]",
			because: "the redactor replaces an absolute path with its stable placeholder rather than dropping the whole message");
	}

	[Test]
	[Description("Includes the last JsonException's redacted message and it is only appended when the caller supplies one.")]
	public void Describe_ShouldIncludeLastJsonError_WhenCallerSuppliesOne() {
		// Arrange
		CallToolResult callResult = new() { IsError = false, Content = [] };
		JsonException lastJsonException;
		try {
			JsonSerializer.Deserialize<int>("not-json");
			throw new InvalidOperationException("Expected a JsonException to be thrown by the arrange step.");
		}
		catch (JsonException exception) {
			lastJsonException = exception;
		}

		// Act
		string descriptionWithException = McpResultDiagnostics.Describe(callResult, lastJsonException);
		string descriptionWithoutException = McpResultDiagnostics.Describe(callResult);

		// Assert
		descriptionWithException.Should().Contain("LastJsonError=",
			because: "the caller supplied a JsonException, so its message must appear in the description");
		descriptionWithoutException.Should().NotContain("LastJsonError=",
			because: "no JsonException was supplied, so the description must not fabricate one");
	}

	[Test]
	[Description("Truncates text longer than the documented cap and reports the total original length, instead of flooding the CI log unbounded.")]
	public void Truncate_ShouldCapAndReportTotalLength_WhenTextExceedsTheLimit() {
		// Arrange
		string hugeText = new string('a', McpResultDiagnostics.PayloadDiagnosticLimit + 5_000);

		// Act
		string truncated = McpResultDiagnostics.Truncate(hugeText);

		// Assert
		truncated.Length.Should().BeLessThan(hugeText.Length,
			because: "the truncated text must be capped rather than embedding the full original payload");
		truncated.Should().MatchRegex(@"truncated, \d+ characters total",
			because: "the cap must be explicit and report the total original character count, not silently cut");
	}

	[Test]
	[Description("Leaves text at or under the documented cap unchanged.")]
	public void Truncate_ShouldReturnTextUnchanged_WhenTextIsAtOrUnderTheLimit() {
		// Arrange
		string shortText = new string('a', McpResultDiagnostics.PayloadDiagnosticLimit);

		// Act
		string result = McpResultDiagnostics.Truncate(shortText);

		// Assert
		result.Should().Be(shortText,
			because: "text at exactly the cap must not be marked as truncated");
	}
}
