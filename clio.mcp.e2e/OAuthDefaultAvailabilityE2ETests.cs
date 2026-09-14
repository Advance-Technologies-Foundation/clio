using Allure.Net.Commons;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;

namespace Clio.Mcp.E2E;

/// <summary>Proves OAuth workflow discovery with the deployment feature absent or disabled.</summary>
[TestFixture(false)]
[TestFixture(true)]
[Category("McpE2E.NoEnvironment")]
[NonParallelizable]
public sealed class OAuthDefaultAvailabilityE2ETests(bool explicitDisabled) : McpContractFixtureBase {
	/// <inheritdoc />
	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = CreateIsolatedClioHome(
			explicitDisabled ? "{\"Autoupdate\":false,\"Features\":{\"deploy-identity\":false}}" : "{\"Autoupdate\":false}",
			"oauth-defaults");
	}

	[Test]
	[Description("Discovers all four OAuth workflow contracts without enabling IdentityService deployment.")]
	[AllureTag(ToolContractGetTool.ToolName)]
	[AllureName("OAuth workflow is available by default")]
	[AllureDescription("Starts an isolated real MCP server with the deployment flag absent or false and checks named contracts and catalog visibility.")]
	public async Task GetToolContracts_ShouldExposeOAuthWorkflow_WhenDeploymentFeatureIsDisabled() {
		// Arrange
		await using var context = Arrange();
		string[] names = [GetIdentityServiceConfigTool.GetIdentityServiceConfigToolName,
			ResolveOAuthSystemUserTool.ResolveOAuthSystemUserToolName,
			CreateServerToServerOAuthAppTool.CreateServerToServerOAuthAppToolName,
			VerifyOAuthAppTool.VerifyOAuthAppToolName];

		// Act
		var call = await context.Session.CallToolAsync(ToolContractGetTool.ToolName,
			new Dictionary<string, object?> { ["args"] = new Dictionary<string, object?> { ["tool-names"] = names } },
			context.CancellationTokenSource.Token);
		var response = EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(call);
		var index = await context.Session.GetToolContractIndexAsync(context.CancellationTokenSource.Token);

		// Assert
		AllureApi.Step("Assert a normal MCP response", () =>
			call.IsError.Should().NotBeTrue(because: "discovery must work without a feature opt-in"));
		AllureApi.Step("Assert discovery succeeds", () =>
			response.Success.Should().BeTrue(because: "the documented OAuth workflow is public"));
		AllureApi.Step("Assert all four contracts resolve", () =>
			response.Tools!.Select(tool => tool.Name).Should().Equal(names,
				because: "every documented OAuth workflow step needs a contract"));
		AllureApi.Step("Assert there are no misses", () =>
			response.NotFound.Should().BeNull(because: "none of the four OAuth steps remains gated"));
		AllureApi.Step("Assert technical-user creation stays hidden", () => {
			index.Select(tool => tool.Name).Should().NotContain(CreateOAuthTechnicalUserTool.CreateOAuthTechnicalUserToolName,
				because: "technical-user creation remains experimental");
		});
	}
}
