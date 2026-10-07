using System.Collections.Concurrent;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for ENG-102487: <c>clio mcp-server --fail-on-error</c> must still start, complete the
/// MCP <c>initialize</c> handshake and serve tools, while the ignored flag is reported on standard error only.
/// </summary>
/// <remarks>
/// The unit tests pin the warning text and that the logger receives it. Only a real stdio process proves that
/// the warning does not reach standard output, which is the JSON-RPC channel: a stray line there would break
/// the handshake of every MCP client configured with the flag.
/// </remarks>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("mcp-server")]
[NonParallelizable]
public sealed class McpServerIgnoredFailOnFlagsE2ETests {
	private const string McpServerVerb = "mcp-server";
	private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(2);

	[Test]
	[Description("clio mcp-server --fail-on-error completes the MCP handshake and lists tools, and the warning that the flag is ignored arrives on standard error (ENG-102487).")]
	[AllureTag(McpServerVerb)]
	[AllureName("mcp-server starts with an ignored --fail-on-error flag")]
	public async Task McpServer_ShouldCompleteHandshakeAndWarnOnStandardError_WhenStartedWithFailOnError() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		ClioProcessDescriptor descriptor = ClioExecutableResolver.Resolve(settings, McpServerVerb, "--fail-on-error");
		ConcurrentQueue<string> standardErrorLines = new();
		using CancellationTokenSource timeoutSource = new(StartupTimeout);
		StdioClientTransport transport = new(new StdioClientTransportOptions {
			Command = descriptor.Command,
			Arguments = [.. descriptor.Arguments],
			WorkingDirectory = descriptor.WorkingDirectory,
			EnvironmentVariables = settings.ProcessEnvironmentVariables,
			Name = "clio-mcp-e2e-ignored-fail-on",
			ShutdownTimeout = TimeSpan.FromMilliseconds(500),
			StandardErrorLines = standardErrorLines.Enqueue
		}, NullLoggerFactory.Instance);

		// Act
		await using McpClient client = await McpClient.CreateAsync(
			transport,
			new McpClientOptions(),
			NullLoggerFactory.Instance,
			timeoutSource.Token);
		IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: timeoutSource.Token);

		// Assert
		tools.Should().NotBeEmpty(
			because: "a server started with an ignored flag must complete initialize and serve its tools, so nothing it wrote broke the JSON-RPC stream on standard output");
		standardErrorLines.Should().Contain(line => line.Contains("--fail-on-error is not supported by the MCP server"),
			because: "the operator learns from standard error that the configured flag has no effect");
	}
}
