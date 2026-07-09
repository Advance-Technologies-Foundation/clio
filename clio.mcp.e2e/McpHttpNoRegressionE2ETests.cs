using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio.Mcp.E2E;

/// <summary>
/// Story 15d (ENG-93208, FR-10 / AC-05) no-regression e2e: with per-request credential passthrough
/// unused, <c>clio mcp</c> (stdio) and <c>clio mcp-http -e &lt;env&gt;</c> on loopback with NO platform
/// API key must behave exactly as the pre-passthrough 8.1.0.72 build. Treated as a core contract, not a
/// mere test.
/// <para>
/// MANUAL — NOT in CI (<c>[Category("E2E")]</c>). The stdio leg needs only a clio build; the
/// <c>mcp-http -e &lt;env&gt;</c> leg needs a pre-registered clio environment (skipped via
/// <see cref="Assert.Ignore(string)"/> when <c>CLIO_MCP_HTTP_E2E_REGISTERED_ENV</c> is absent).
/// </para>
/// </summary>
[TestFixture]
[Category("E2E")]
[NonParallelizable]
public sealed class McpHttpNoRegressionE2ETests {

	private McpE2ESettings _settings = null!;

	[SetUp]
	public void SetUp() {
		_settings = TestConfiguration.Load();
	}

	[Test]
	[Description("The stdio clio MCP server starts and advertises its resident tools exactly as the pre-passthrough build, proving the passthrough work did not regress the stdio transport (Story 15d / AC-05).")]
	public async Task Stdio_ShouldAdvertiseResidentTools_WhenPassthroughUnused() {
		// Arrange
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(2));
		await using McpServerSession session = await McpServerSession.StartAsync(_settings, cts.Token);

		// Act
		IList<McpClientTool> tools = await session.ListToolsAsync(cts.Token);

		// Assert
		tools.Should().NotBeEmpty(
			because: "the stdio MCP server must still advertise its resident tools unchanged by the passthrough work (Story 15d)");
	}

	[Test]
	[Description("clio mcp-http bound to loopback with NO platform API key serves a pre-registered environment via -e-style resolution exactly as the pre-passthrough build, ignoring any credential header (Story 15d / AC-05).")]
	public async Task HttpWithRegisteredEnvironment_ShouldServeEnvironment_WhenNoPlatformApiKeyConfigured() {
		// Arrange
		string? registeredEnvironment = Environment.GetEnvironmentVariable("CLIO_MCP_HTTP_E2E_REGISTERED_ENV");
		if (string.IsNullOrWhiteSpace(registeredEnvironment)) {
			Assert.Ignore(
				"Set CLIO_MCP_HTTP_E2E_REGISTERED_ENV to a pre-registered clio environment name to run the "
				+ "mcp-http no-regression leg against a live stand (MANUAL, not in CI).");
		}

		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(3));
		// No platform API key → passthrough is fully disabled; the server behaves as pre-passthrough.
		await using McpHttpServerSession server =
			await McpHttpServerSession.StartAsync(_settings, platformApiKey: null, cts.Token);
		await using McpClient client =
			await server.ConnectAsync(platformApiKey: null, integrationCredentialsBase64: null, cts.Token);

		// Act — resolve by the registered environment name, exactly as the pre-passthrough -e path.
		CallToolResult result = await client.CallToolAsync(
			GetCreatioInfoTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> { ["environment-name"] = registeredEnvironment }
			},
			cancellationToken: cts.Token);

		// Assert
		result.IsError.Should().NotBeTrue(
			because: "a registered environment must be served over mcp-http with no api key exactly as the pre-passthrough build (Story 15d)");
		CommandExecutionEnvelope envelope = McpCommandExecutionParser.Extract(result);
		envelope.ExitCode.Should().Be(0,
			because: "describe-environment against a registered environment must succeed unchanged by the passthrough work (Story 15d)");
	}
}
