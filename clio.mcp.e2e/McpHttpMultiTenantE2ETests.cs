using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Story 15 AC-07 / SM-01 (ENG-93208) multi-tenant e2e. A SINGLE <c>clio mcp-http</c> process with ZERO
/// pre-registered environments serves tool calls against two distinct Creatio URLs/users in one run,
/// using ONLY per-request <c>X-Integration-Credentials</c> headers, and both succeed.
/// <para>
/// MANUAL — NOT in CI. Needs a live stand with two distinct tenants and a clio mcp-http process
/// started with <c>--platform-api-key</c> (the sole passthrough gate); skipped via
/// <see cref="McpHttpPassthroughStand.RequireOrIgnore"/> when the live-stand env vars are absent.
/// </para>
/// </summary>
[TestFixture]
[Category("E2E")]
[NonParallelizable]
public sealed class McpHttpMultiTenantE2ETests {

	private McpE2ESettings _settings = null!;

	[SetUp]
	public void SetUp() {
		_settings = TestConfiguration.Load();
	}

	[Test]
	[Description("One mcp-http process with no pre-registered environments serves two distinct tenants in a single run via only X-Integration-Credentials, and both calls succeed (SM-01 / AC-07).")]
	public async Task SingleProcess_ShouldServeTwoTenants_WhenOnlyPerRequestCredentialsAreUsed() {
		// Arrange
		McpHttpPassthroughStand stand = McpHttpPassthroughStand.RequireOrIgnore();
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(3));
		await using McpHttpServerSession server =
			await McpHttpServerSession.StartAsync(_settings, stand.PlatformApiKey, cts.Token);

		// Act — sequential calls in ONE run, each authenticating purely by header (no -e, no registered env).
		CallToolResult tenantOne = await CallDescribeEnvironmentAsync(
			server, stand.PlatformApiKey, stand.TenantOneUrl, stand.TenantOneToken, cts.Token);
		CallToolResult tenantTwo = await CallDescribeEnvironmentAsync(
			server, stand.PlatformApiKey, stand.TenantTwoUrl, stand.TenantTwoToken, cts.Token);

		// Assert
		tenantOne.IsError.Should().NotBeTrue(
			because: "the first tenant must be served from its per-request credentials with no pre-registered environment (SM-01)");
		tenantTwo.IsError.Should().NotBeTrue(
			because: "the second, distinct tenant must be served from its per-request credentials in the same process run (SM-01)");
		ExtractText(tenantOne).Should().Contain(new Uri(stand.TenantOneUrl).Host,
			because: "the first response must describe the first tenant's environment");
		ExtractText(tenantTwo).Should().Contain(new Uri(stand.TenantTwoUrl).Host,
			because: "the second response must describe the second tenant's environment");
	}

	private static async Task<CallToolResult> CallDescribeEnvironmentAsync(
		McpHttpServerSession server, string platformApiKey, string url, string token, CancellationToken cancellationToken) {
		await using McpClient client = await server.ConnectAsync(
			platformApiKey,
			McpHttpServerSession.EncodeBearerCredentials(url, token),
			cancellationToken);
		return await client.CallToolAsync(
			GetCreatioInfoTool.ToolName,
			new Dictionary<string, object?> { ["args"] = new Dictionary<string, object?>() },
			cancellationToken: cancellationToken);
	}

	private static string ExtractText(CallToolResult callResult) {
		CommandExecutionEnvelope envelope = McpCommandExecutionParser.Extract(callResult);
		return string.Join("\n", (envelope.Output ?? []).Select(message => message.Value ?? string.Empty));
	}
}
