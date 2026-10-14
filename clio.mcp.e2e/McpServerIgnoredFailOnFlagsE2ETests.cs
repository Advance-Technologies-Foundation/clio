using System.Collections.Concurrent;
using Allure.Net.Commons;
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
/// <para>
/// The transport is built here rather than through <c>McpServerSession.StartAsync</c>, which always starts
/// <c>mcp-server</c> without extra arguments. Like every fixture in this assembly it runs under the shared
/// isolated home of <c>McpSharedHomeSetUpFixture</c>, where the curated-knowledge source is already disabled.
/// </para>
/// </remarks>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("mcp-server")]
[NonParallelizable]
public sealed class McpServerIgnoredFailOnFlagsE2ETests {
	private const string McpServerVerb = "mcp-server";
	private const string IgnoredFlagWarning = "--fail-on-error is not supported by the MCP server";
	private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(2);
	// The warning is written before the handshake, but the SDK reads standard error on its own reader, so the
	// line can reach the callback after ListToolsAsync returns. Waiting for it keeps the test from racing.
	private static readonly TimeSpan StandardErrorDeadline = TimeSpan.FromSeconds(10);

	[Test]
	[Description("clio mcp-server --fail-on-error completes the MCP handshake and lists tools, and the warning that the flag is ignored arrives on standard error (ENG-102487).")]
	[AllureTag(McpServerVerb)]
	[AllureName("mcp-server starts with an ignored --fail-on-error flag")]
	[AllureDescription("Starts the real clio mcp-server with --fail-on-error, completes the MCP initialize handshake, lists tools, and verifies that the warning about the ignored flag arrives on standard error rather than on the JSON-RPC stdout.")]
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

		bool warningSeen = await WaitForStandardErrorLineAsync(standardErrorLines, IgnoredFlagWarning);

		// Assert
		AllureApi.Step("Assert the MCP handshake completed and tools are listed", () =>
			tools.Should().NotBeEmpty(
				because: "a server started with an ignored flag must complete initialize and serve its tools, so nothing it wrote broke the JSON-RPC stream on standard output"));
		AllureApi.Step("Assert standard error names the ignored flag", () =>
			warningSeen.Should().BeTrue(
				because: $"the operator learns from standard error that the configured flag has no effect; stderr was: {string.Join(" | ", standardErrorLines)}"));
	}

	private static async Task<bool> WaitForStandardErrorLineAsync(ConcurrentQueue<string> lines, string fragment) {
		DateTime deadline = DateTime.UtcNow + StandardErrorDeadline;
		while (DateTime.UtcNow < deadline) {
			if (lines.Any(line => line.Contains(fragment, StringComparison.Ordinal))) {
				return true;
			}
			await Task.Delay(TimeSpan.FromMilliseconds(100));
		}
		return lines.Any(line => line.Contains(fragment, StringComparison.Ordinal));
	}
}
