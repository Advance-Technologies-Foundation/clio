using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// ENG-99970: the text a tool result is serialized into is what the agent reads and pays for, so it is
/// written with the relaxed encoder - no HTML escaping of quotes, apostrophes, backticks, dashes or
/// non-ASCII - while control characters stay escaped.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class McpResultEncodingTests {

	[Test]
	[Category("Unit")]
	[Description("The MCP serializer writes a quote, an apostrophe, a backtick, a dash and a Cyrillic letter as themselves, not as six-character \\u escapes: a tool result is read over JSON-RPC, never embedded in HTML, so that escaping only inflated every result.")]
	public void McpSerializerOptions_Should_NotHtmlEscape_TheTextOfAResult() {
		// Arrange
		JsonSerializerOptions options = Clio.BindingsModule.CreateMcpSerializerOptions();
		const string text = "the descriptor's \"name\" - `code` — Контакт";

		// Act
		string json = JsonSerializer.Serialize(new { value = text }, options);

		// Assert
		json.Should().Contain("descriptor's", because: "an apostrophe needs no escaping in JSON");
		json.Should().Contain("\\\"name\\\"", because: "a quote inside a string is escaped the short JSON way");
		json.Should().Contain("`code`", because: "a backtick needs no escaping in JSON");
		json.Should().Contain("—", because: "a dash is written as itself, not as a six-character escape");
		json.Should().Contain("Контакт",
			because: "a non-English caption is written as its letters");
		json.Should().NotContain("\\u00", because: "none of these characters is escaped as a \\u sequence any more");
	}

	[Test]
	[Category("Unit")]
	[Description("The relaxed MCP serializer still escapes control characters, so an ESC sequence in data a tool returns cannot reach an agent CLI's terminal raw.")]
	public void McpSerializerOptions_Should_StillEscape_ControlCharacters() {
		// Arrange
		JsonSerializerOptions options = Clio.BindingsModule.CreateMcpSerializerOptions();

		// Act
		string json = JsonSerializer.Serialize(new { value = "Ocean\u001b[31m forged" }, options);

		// Assert
		json.Should().NotContain("\u001b", because: "a raw ESC in a result would be a terminal-injection vector");
		json.Should().ContainEquivalentOf("\\u001b", because: "the control character is escaped, not dropped");
	}

	[Test]
	[Category("Unit")]
	[Description("A describe-business-process result - the graph as a STRING inside the command result - carries no escaped quote once serialized for the agent: indented and escaped, a ten-element graph was 53-62 thousand characters and was spilled to a file on every describe of a measured run.")]
	public void DescribeResult_Should_NotCarryEscapedQuotes_OverTheWire() {
		// Arrange
		string graph = JsonSerializer.Serialize(
			new { name = "UsrContactHandling", caption = "Контакт", elements = new[] { new { name = "Start1" } } },
			DescribeProcessCommand.OutputOptions);
		CommandExecutionResult result = new(0, [new InfoMessage(graph)]);

		// Act
		string wire = JsonSerializer.Serialize(result, Clio.BindingsModule.CreateMcpSerializerOptions());

		// Assert
		graph.Should().NotContain("\n", because: "the graph is compact - indentation is paid twice once it is a string");
		wire.Should().NotContain("\\u0022", because: "each quote of the embedded graph costs two characters, not six");
		wire.Should().Contain("Контакт",
			because: "a non-English caption is not turned into \\uXXXX and then doubled by the outer encoding");
	}
}
