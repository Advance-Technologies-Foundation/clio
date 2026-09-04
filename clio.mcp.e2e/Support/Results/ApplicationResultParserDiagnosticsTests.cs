using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Results;

/// <summary>
/// Unit test proving that <see cref="ApplicationResultParser.ExtractList"/> — one representative sibling
/// of <see cref="EntitySchemaStructuredResultParser"/> (GitHub issue #1384) — now embeds the shared
/// <see cref="McpResultDiagnostics.Describe"/> payload description in its parse-failure message instead of
/// throwing the old bare "Could not parse list-apps MCP result." sentence. Only one sibling family is
/// covered here: the other seven throw sites in this folder share the exact same one-line call shape
/// (<c>McpResultDiagnostics.Describe(callResult)</c> appended to an unchanged tool-specific prefix), so a
/// second or third test would exercise the identical code path in <see cref="McpResultDiagnostics"/> that
/// <see cref="McpResultDiagnosticsTests"/> already covers directly, without adding any new signal.
/// Constructs a <see cref="CallToolResult"/> in-memory (no MCP server, no stand, no network I/O), so it is
/// categorized <c>Unit</c> rather than <c>McpE2E.Sandbox</c>.
/// </summary>
[TestFixture]
[Category("Unit")]
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
}
