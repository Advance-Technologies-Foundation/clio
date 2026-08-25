using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
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
[AllureFeature("Knowledge feedback policy")]
[NonParallelizable]
public sealed class KnowledgeFeedbackPolicyToolE2ETests : McpContractFixtureBase {

	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		string clioHome = CreateIsolatedClioHome("{}", "knowledge-feedback");
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = clioHome;
		settings.SuppressCuratedKnowledgeBootstrap = true;
	}

	[Test]
	[Category("E2E")]
	[Description("Discovers both non-resident policy tools and persists an off-mode update through clio-run over the real MCP server.")]
	[AllureTag(KnowledgeFeedbackPolicyTools.GetToolName)]
	[AllureTag(KnowledgeFeedbackPolicyTools.ConfigureToolName)]
	[AllureName("Knowledge-feedback policy tools are discoverable and callable through clio-run")]
	public async Task PolicyTools_ShouldRoundTripOffMode_WhenCalledThroughLongTail() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));
		IReadOnlyList<ToolContractIndexEntry> index = await context.Session
			.GetToolContractIndexAsync(context.CancellationTokenSource.Token);

		// Act
		CallToolResult configureCall = await context.Session.CallToolAsync(
			KnowledgeFeedbackPolicyTools.ConfigureToolName,
			new Dictionary<string, object?> {
				["mode"] = "off"
			},
			context.CancellationTokenSource.Token);
		KnowledgeFeedbackConfigureResponse configure =
			EntitySchemaStructuredResultParser.Extract<KnowledgeFeedbackConfigureResponse>(configureCall);
		CallToolResult getCall = await context.Session.CallToolAsync(
			KnowledgeFeedbackPolicyTools.GetToolName,
			new Dictionary<string, object?> { ["args"] = new Dictionary<string, object?>() },
			context.CancellationTokenSource.Token);
		KnowledgeFeedbackPolicy policy =
			EntitySchemaStructuredResultParser.Extract<KnowledgeFeedbackPolicy>(getCall);

		// Assert
		index.Should().Contain(entry => entry.Name == KnowledgeFeedbackPolicyTools.GetToolName && !entry.Resident,
			because: "inspection must be discoverable without consuming resident schema budget");
		index.Should().Contain(entry => entry.Name == KnowledgeFeedbackPolicyTools.ConfigureToolName && !entry.Resident,
			because: "configuration must be discoverable without consuming resident schema budget");
		index.Should().Contain(entry => entry.Name == KnowledgeFeedbackPolicyTools.ConfigureToolName && entry.Destructive == true,
			because: "standing-approval changes must be classified for host-level consent gating");
		configure.Success.Should().BeTrue(
			because: "a reversible off-mode update should execute through clio-run");
		policy.EffectiveMode.Should().Be("off",
			because: "the subsequent read must observe the persisted policy update");
	}
}
