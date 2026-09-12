using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Results;

/// <summary>
/// Unit test proving that <see cref="ApplicationResultParser.ExtractList"/> — one representative sibling
/// of <see cref="EntitySchemaStructuredResultParser"/> (GitHub issue #1384) — now embeds the shared
/// <see cref="McpResultDiagnostics.Describe"/> payload description in its parse-failure message instead of
/// throwing the old bare "Could not parse list-apps MCP result." sentence, plus the two properties of the
/// last-<see cref="System.Text.Json.JsonException"/> threading that the siblings gained with it: a real
/// parse failure is named, and the doomed content-array attempt is not. Two sibling families are covered:
/// the remaining throw sites share the exact same call shape, so further copies would exercise the
/// identical code path in <see cref="McpResultDiagnostics"/> that <see cref="McpResultDiagnosticsTests"/>
/// already covers directly, without adding any new signal.
/// Constructs a <see cref="CallToolResult"/> in-memory (no MCP server, no stand, no network I/O), so it is
/// categorized <c>Unit</c> rather than <c>McpE2E.Sandbox</c>.
/// </summary>
[TestFixture]
[Category("Unit")]
[Category("McpE2E.NoEnvironment")]
[Property("Module", "McpServer")]
public sealed class ApplicationResultParserDiagnosticsTests {
	[Test]
	[Description("Includes the tool-specific prefix, IsError, and the payload's own text in the message thrown when list-apps returns an unparsable result.")]
	public void ExtractList_ShouldThrowWithPayloadDiagnostics_WhenResultIsNotAValidListEnvelope() {
		// Arrange
		const string serverErrorText = "System.NullReferenceException: Object reference not set to an instance of an object.";
		CallToolResult callResult = new() {
			IsError = true,
			Content = [new TextContentBlock { Text = serverErrorText }]
		};

		// Act
		Action act = () => ApplicationResultParser.ExtractList(callResult);

		// Assert
		InvalidOperationException exception = act.Should().Throw<InvalidOperationException>(
				because: "the server's unhandled-exception text does not deserialize into ApplicationListResponseEnvelope")
			.Which;
		exception.Message.Should().StartWith("Could not parse list-apps MCP result:",
			because: "the tool-specific prefix must be preserved unchanged");
		exception.Message.Should().Contain("IsError=True",
			because: "an unhandled server exception is exactly the kind of failure IsError should surface");
		exception.Message.Should().Contain(serverErrorText,
			because: "the payload's own error text must be visible in the thrown message, not discarded as it was before this diagnostic was added");
	}

	[Test]
	[Description("Names the last JsonException when a sibling parser's text payload is not JSON, instead of discarding it inside catch (JsonException) { }.")]
	public void ExtractResponse_ShouldNameLastJsonError_WhenTextPayloadIsNotJson() {
		// Arrange
		const string loginPage = "<!DOCTYPE html><html><body><h1>Sign in</h1></body></html>";
		CallToolResult callResult = new() {
			IsError = false,
			Content = [new TextContentBlock { Text = loginPage }]
		};

		// Act
		Action act = () => GetPkgListResultParser.ExtractResponse(callResult);

		// Assert
		InvalidOperationException exception = act.Should().Throw<InvalidOperationException>(
				because: "an HTML login page does not deserialize into GetPkgListResponseEnvelope")
			.Which;
		exception.Message.Should().Contain("LastJsonError=",
			because: "the sibling parsers now keep the JsonException their catch blocks used to swallow");
		exception.Message.Should().Contain("Sign in",
			because: "the page's own text must be visible too, so an authentication redirect is recognizable as one");
	}

	[Test]
	[Description("Does not report a LastJsonError when the only exception raised came from the doomed deserialize of the raw content-item array.")]
	public void ExtractResponse_ShouldNotNameLastJsonError_WhenOnlyTheContentArrayAttemptFailed() {
		// Arrange
		const string wellFormedButEmptyEnvelope = /*lang=json,strict*/ "{\"packages\":null}";
		CallToolResult callResult = new() {
			IsError = false,
			Content = [new TextContentBlock { Text = wellFormedButEmptyEnvelope }]
		};

		// Act
		Action act = () => GetPkgListResultParser.ExtractResponse(callResult);

		// Assert
		InvalidOperationException exception = act.Should().Throw<InvalidOperationException>(
				because: "an envelope with no packages array is rejected by the parser's own validity check")
			.Which;
		exception.Message.Should().NotContain("LastJsonError=",
			because: "deserializing the raw content-item array into an object always fails and says nothing about the real payload, so blaming it would misdirect the reader");
		exception.Message.Should().Contain("packages",
			because: "the payload itself must still be dumped, which is what actually explains this failure");
	}
}
