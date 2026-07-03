using Clio.Command.McpServer;
using CommandLine;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public class McpHttpServerCommandTests {

	[Test]
	[Description("Default option values are applied when no arguments are provided")]
	public void Parse_ShouldApplyDefaults_WhenNoArgumentsProvided() {
		// Arrange & Act
		ParserResult<McpHttpServerCommandOptions> result =
			Parser.Default.ParseArguments<McpHttpServerCommandOptions>(System.Array.Empty<string>());

		// Assert
		McpHttpServerCommandOptions opts = null;
		result.WithParsed(o => opts = o);
		result.Tag.Should().Be(ParserResultType.Parsed,
			because: "mcp-http with no arguments should parse successfully using defaults");
		opts.Should().NotBeNull();
		opts!.Port.Should().Be(8005,
			because: "the default port for the HTTP MCP server is 8005");
		opts.Host.Should().Be("127.0.0.1",
			because: "the default host binds to localhost only for security");
		opts.Path.Should().Be("/mcp",
			because: "the default MCP endpoint path is /mcp");
	}

	[Test]
	[Description("Custom port, host, and path values are parsed correctly")]
	public void Parse_ShouldUseProvidedValues_WhenAllOptionsSpecified() {
		// Arrange
		string[] args = ["--port", "9000", "--host", "0.0.0.0", "--path", "/api/mcp"];

		// Act
		ParserResult<McpHttpServerCommandOptions> result =
			Parser.Default.ParseArguments<McpHttpServerCommandOptions>(args);

		// Assert
		McpHttpServerCommandOptions opts = null;
		result.WithParsed(o => opts = o);
		result.Tag.Should().Be(ParserResultType.Parsed,
			because: "all provided options should be accepted by the parser");
		opts.Should().NotBeNull();
		opts!.Port.Should().Be(9000,
			because: "the --port value should override the default");
		opts.Host.Should().Be("0.0.0.0",
			because: "the --host value should override the default");
		opts.Path.Should().Be("/api/mcp",
			because: "the --path value should override the default");
	}
}
