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

	[Test]
	[Description("Loopback bind allows the bound host plus its loopback aliases")]
	public void BuildAllowedHosts_ShouldIncludeLoopbackAliases_WhenBoundToLoopback() {
		// Arrange & Act
		System.Collections.Generic.List<string> allowed =
			McpHttpServerCommand.BuildAllowedHosts("127.0.0.1");

		// Assert
		allowed.Should().Contain("127.0.0.1",
			because: "the bound loopback host must be allowed");
		allowed.Should().Contain("localhost",
			because: "localhost resolves to the loopback bind and must be allowed");
		allowed.Should().NotContain("*",
			because: "a loopback bind must not allow arbitrary hosts");
	}

	[Test]
	[Description("Wildcard bind cannot restrict the Host header to a single value")]
	public void BuildAllowedHosts_ShouldAllowAnyHost_WhenBoundToWildcard() {
		// Arrange & Act
		System.Collections.Generic.List<string> allowed =
			McpHttpServerCommand.BuildAllowedHosts("0.0.0.0");

		// Assert
		allowed.Should().ContainSingle().Which.Should().Be("*",
			because: "a 0.0.0.0 bind has no single legitimate Host value to restrict to");
	}

	[Test]
	[Description("A cross-origin browser request (DNS rebinding) is rejected on a loopback bind")]
	public void IsAllowedOrigin_ShouldReject_WhenOriginIsForeignHost() {
		// Arrange & Act
		bool allowed = McpHttpServerCommand.IsAllowedOrigin("http://evil.com", "127.0.0.1");

		// Assert
		allowed.Should().BeFalse(
			because: "a foreign Origin re-resolving to loopback is the DNS-rebinding attack the check blocks");
	}

	[Test]
	[Description("A loopback Origin is accepted on a loopback bind")]
	public void IsAllowedOrigin_ShouldAllow_WhenOriginIsLoopback() {
		// Arrange & Act
		bool allowed = McpHttpServerCommand.IsAllowedOrigin("http://localhost:8005", "127.0.0.1");

		// Assert
		allowed.Should().BeTrue(
			because: "a same-machine loopback origin is a legitimate caller");
	}

	[Test]
	[Description("A malformed Origin header is rejected rather than trusted")]
	public void IsAllowedOrigin_ShouldReject_WhenOriginIsNotAbsoluteUri() {
		// Arrange & Act
		bool allowed = McpHttpServerCommand.IsAllowedOrigin("not-a-uri", "127.0.0.1");

		// Assert
		allowed.Should().BeFalse(
			because: "an unparseable Origin cannot be validated and must fail closed");
	}
}
