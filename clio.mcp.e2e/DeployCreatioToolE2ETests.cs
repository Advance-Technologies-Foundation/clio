using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("deploy-creatio")]
[Parallelizable(ParallelScope.Self)]
public sealed class DeployCreatioToolE2ETests : McpContractFixtureBase
{
	private const string ToolName = InstallerCommandTool.DeployCreatioToolName;
	private const string ScheduledMaintenanceMessage =
		"Infrastructure temporarily unavailable due to scheduled maintenance. Please try again later.";

	[Test]
	[Description("Starts the real clio MCP server, discovers deploy-creatio through the get-tool-contract lazy surface, and verifies the discovery metadata flags destructive behavior plus the required preflight guidance.")]
	[AllureTag(ToolName)]
	[AllureName("Deploy creatio exposes preflight guidance and destructive metadata on the lazy surface")]
	[AllureDescription("Uses the get-tool-contract compact index and full contract of the real clio MCP server to verify that deploy-creatio is destructive and instructs agents to run assert-infrastructure and show-passing-infrastructure first.")]
	public async Task DeployCreatio_Should_Advertise_Preflight_Guidance()
	{
		// Arrange
		await using var arrangeContext = Arrange();

		// Act
		// deploy-creatio is a hidden (non-resident) tool on the lazy surface: its safety flag comes from
		// the get-tool-contract compact index, and its description/input schema from the full contract.
		IReadOnlyList<ToolContractIndexEntry> index =
			await arrangeContext.Session.GetToolContractIndexAsync(arrangeContext.CancellationTokenSource.Token);
		CallToolResult contractResult = await arrangeContext.Session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> { ["tool-names"] = new[] { ToolName } }
			},
			arrangeContext.CancellationTokenSource.Token);
		ToolContractGetResponse contracts =
			EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(contractResult);
		ToolContractDefinition contract = contracts.Tools!.Single(tool => tool.Name == ToolName);

		// Assert
		ToolContractIndexEntry entry = index.Should()
			.ContainSingle(entry => entry.Name == ToolName,
				because: "the compact discovery index must carry exactly one entry for deploy-creatio")
			.Which;
		entry.Destructive.Should().BeTrue(
			because: "deploy-creatio mutates infrastructure and should be flagged destructive in the discovery index");
		contract.Description.Should().Contain("assert-infrastructure",
			because: "the deploy-creatio contract description should tell the agent to review full infrastructure first");
		contract.Description.Should().Contain("show-passing-infrastructure",
			because: "the deploy-creatio contract description should tell the agent to fetch deployable recommendations second");
		contract.Description.Should().Contain("existing forced-password-change state",
			because: "the real MCP contract should disclose the preserved database behavior used by Ring");
		contract.InputSchema.Properties.Select(property => property.Name).Should().BeEquivalentTo(
			["siteName", "zipFile", "sitePort", "dbServerName", "redisServerName", "useHttps"],
			because: "the full deploy-creatio contract should only expose the six approved arguments");
		contract.InputSchema.Properties.Single(property => property.Name == "useHttps").Description.Should()
			.Contain("falls back to HTTP",
				because: "agents need the non-failing HTTPS fallback contract before invoking deployment");
	}

	[Test]
	// NoEnvironment tier: the invalid-archive path fails inside the command before any Kubernetes
	// call, so the tool returns a structured command failure env-free. It was previously ignored as
	// "requires a reachable Kubernetes cluster", but the real blocker was the no-Kubernetes fallback
	// IKubernetes client throwing from Dispose during per-request DI-scope teardown (opaque
	// InternalError) — fixed under ENG-91830.
	[Description("Starts the real clio MCP server, invokes deploy-creatio with an invalid archive path, and verifies that the tool reaches the real command path instead of returning the removed scheduled-maintenance stub.")]
	[AllureTag(ToolName)]
	[AllureName("Deploy creatio reaches the real command path")]
	[AllureDescription("Uses the real clio MCP server to call deploy-creatio with an invalid zip path and verifies that the result is a real command failure rather than the removed scheduled-maintenance response.")]
	public async Task DeployCreatio_Should_Not_Return_Scheduled_Maintenance_Response()
	{
		// Arrange
		await using var arrangeContext = Arrange();
		string missingZipFile = Path.Combine(Path.GetTempPath(), $"missing-creatio-{Guid.NewGuid():N}.zip");

		// Act
		var callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?>
			{
				["args"] = new Dictionary<string, object?> {
					["siteName"] = $"e2e-{Guid.NewGuid():N}",
					["zipFile"] = missingZipFile,
					["sitePort"] = 5001,
					["useHttps"] = true
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);
		string combinedOutput = string.Join(
			Environment.NewLine,
			(execution.Output ?? []).Select(message => message.Value ?? string.Empty));

		// Assert
		(callResult.IsError == true || execution.ExitCode != 0).Should().BeTrue(
			because: "an invalid zip path should produce a real deployment failure");
		combinedOutput.Should().NotContain(ScheduledMaintenanceMessage,
			because: "the scheduled-maintenance stub has been removed and the MCP tool should now reach the real command path");
		combinedOutput.Should().NotBeNullOrWhiteSpace(
			because: "the real command path should produce human-readable diagnostics for an invalid archive path");
		execution.LogFilePath.Should().NotBeNullOrWhiteSpace(
			because: "deploy-creatio should return the temp database-operation log artifact path even when deployment fails early");
		File.Exists(execution.LogFilePath!).Should().BeTrue(
			because: "the returned deploy-creatio log-file-path should reference a created temp artifact");
	}
}
