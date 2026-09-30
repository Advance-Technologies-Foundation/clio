using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// ENG-99970: clio-run redacts a failure envelope AFTER the tool serialized it, and the MCP result encoder now
/// spells a quote nested in a string as a short escape rather than the six-character one the redactor's rules
/// were pinned against. These pin that the redaction still removes the secret AND leaves JSON the caller can
/// parse, on text written exactly the way a tool result is.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class ClioRunFailureRedactionEncodingTests {

	private static readonly JsonSerializerOptions ResultTextOptions = Clio.BindingsModule.CreateMcpSerializerOptions();

	[TestCase("Could not open file \"C:\\Temp\\secret.txt\" for reading", "C:\\Temp", TestName = "a quoted Windows path")]
	[TestCase("Could not open file \"/home/user/secret.txt\" for reading", "/home/user", TestName = "a quoted POSIX path")]
	[TestCase("Login failed: password=\"s3cr3t\" was rejected", "s3cr3t", TestName = "a quoted password pair")]
	[Category("Unit")]
	[Description("A success:false envelope written in the MCP result encoding comes out of the clio-run redaction with the sensitive value gone and the JSON still parseable - the three rules that stop at a quote used to swallow the backslash of a short escape, break the JSON and leave a quoted password in the clear.")]
	public void RedactFailureContent_Should_RedactAndKeepJson_ForTextInTheResultEncoding(string error, string secret) {
		// Arrange
		string text = JsonSerializer.Serialize(
			new Dictionary<string, object> { ["success"] = false, ["error"] = error }, ResultTextOptions);
		CallToolResult result = new() { Content = [new TextContentBlock { Text = text }] };

		// Act
		ClioRunExecutor.RedactFailureContent(result);
		string redacted = result.Content.OfType<TextContentBlock>().Single().Text;

		// Assert
		redacted.Should().NotContain(secret, because: "the sensitive value must not reach the agent");
		System.Action parse = () => JsonDocument.Parse(redacted);
		parse.Should().NotThrow(because: "a redacted envelope the caller cannot parse is a broken tool response");
		JsonDocument.Parse(redacted).RootElement.GetProperty("success").GetBoolean().Should().BeFalse(
			because: "redaction replaces the value, never the envelope around it");
	}

	[Test]
	[Category("Unit")]
	[Description("A redacted envelope is written back in the MCP result encoding, not in the spelling the redactor works on, so redaction does not undo the result-size saving.")]
	public void RedactFailureContent_Should_WriteTheRedactedEnvelope_InTheResultEncoding() {
		// Arrange
		string text = JsonSerializer.Serialize(new Dictionary<string, object> {
			["success"] = false,
			["error"] = "Request to \"https://user:pass@host.example/api\" failed"
		}, ResultTextOptions);
		CallToolResult result = new() { Content = [new TextContentBlock { Text = text }] };

		// Act
		ClioRunExecutor.RedactFailureContent(result);
		string redacted = result.Content.OfType<TextContentBlock>().Single().Text;

		// Assert
		redacted.Should().NotContain("user:pass", because: "credentials in a URI are redacted");
		redacted.Should().Contain("\\\"", because: "the nested quotes are written back with the short escape");
	}

	[Test]
	[Category("Unit")]
	[Description("A failure whose text block is not JSON is still redacted as the plain text it is.")]
	public void RedactFailureContent_Should_RedactPlainText_WhenTheBlockIsNotJson() {
		// Arrange
		CallToolResult result = new() {
			IsError = true,
			Content = [new TextContentBlock { Text = "Error: could not read C:\\Users\\someone\\secret.txt" }]
		};

		// Act
		ClioRunExecutor.RedactFailureContent(result);

		// Assert
		result.Content.OfType<TextContentBlock>().Single().Text.Should().NotContain("someone",
			because: "a plain-text failure is redacted exactly as before");
	}

	[Test]
	[Category("Unit")]
	[Description("A successful envelope is left byte-identical: only failures are scrubbed, and a success can carry legitimate paths.")]
	public void RedactFailureContent_Should_LeaveASuccess_ByteIdentical() {
		// Arrange
		string text = JsonSerializer.Serialize(
			new Dictionary<string, object> { ["success"] = true, ["path"] = "C:\\Temp\\output.txt" }, ResultTextOptions);
		CallToolResult result = new() { Content = [new TextContentBlock { Text = text }] };

		// Act
		ClioRunExecutor.RedactFailureContent(result);

		// Assert
		result.Content.OfType<TextContentBlock>().Single().Text.Should().Be(text,
			because: "a success is never scrubbed or re-serialized");
	}
}
