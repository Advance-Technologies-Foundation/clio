using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the server-to-server OAuth configuration MCP tools. These tools share the
/// <c>deploy-identity</c> feature toggle AND live on the hidden long tail of the lazy tool surface,
/// so the real clio MCP server never lists them in <c>tools/list</c>; when the feature is enabled
/// they are discoverable through the <c>get-tool-contract</c> compact index and callable through the
/// <c>clio-run</c> executor. The tests start the real <c>clio mcp-server</c> process, enable the
/// feature in an isolated CLIO_HOME, and assert lazy-surface discovery, safety metadata, the approved
/// argument schemas, and secret-handling guidance.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("deploy-identity")]
[Parallelizable(ParallelScope.Self)]
public sealed class OAuthConfigurationToolsE2ETests : McpContractFixtureBase
{
	private static readonly string[] ExpectedToolNames = [
		GetIdentityServiceConfigTool.GetIdentityServiceConfigToolName,
		ResolveOAuthSystemUserTool.ResolveOAuthSystemUserToolName,
		CreateOAuthTechnicalUserTool.CreateOAuthTechnicalUserToolName,
		CreateServerToServerOAuthAppTool.CreateServerToServerOAuthAppToolName,
		VerifyOAuthAppTool.VerifyOAuthAppToolName
	];

	[Test]
	[Description("Starts the real clio MCP server with the deploy-identity feature enabled and verifies all five server-to-server OAuth configuration tools are discoverable via the get-tool-contract compact index.")]
	[AllureTag("oauth-mcp-tools")]
	[AllureName("Server-to-server OAuth configuration tools are discoverable on the lazy surface")]
	[AllureDescription("Starts the real clio MCP server with the deploy-identity feature enabled and verifies all five OAuth configuration tools are discoverable via the get-tool-contract compact index.")]
	public async Task OAuthConfigTools_Should_Be_Advertised_When_FeatureEnabled()
	{
		// Arrange
		await using var arrangeContext = Arrange();

		// Act
		IReadOnlyCollection<string> toolNames =
			await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ExpectedToolNames,
			because: "all five server-to-server OAuth configuration tools must be discoverable on the lazy surface (get-tool-contract compact index) when the deploy-identity feature is enabled");
	}

	[Test]
	[Description("Verifies the create-* OAuth tools carry the destructive flag in the get-tool-contract compact index, the read-only tools do not, and the full contracts keep the secret-handling guidance.")]
	[AllureTag("oauth-mcp-tools")]
	[AllureName("OAuth configuration tools expose correct safety metadata on the lazy surface")]
	[AllureDescription("Verifies destructive vs read-only safety flags via the get-tool-contract compact index and the secret-handling guidance via the full tool contracts of the five OAuth configuration tools.")]
	public async Task OAuthConfigTools_Should_Advertise_Correct_SafetyMetadata_When_FeatureEnabled()
	{
		// Arrange
		await using var arrangeContext = Arrange();

		// Act
		IReadOnlyList<ToolContractIndexEntry> index =
			await arrangeContext.Session.GetToolContractIndexAsync(arrangeContext.CancellationTokenSource.Token);
		IReadOnlyList<ToolContractDefinition> contracts = await FetchContractsAsync(
			arrangeContext,
			CreateOAuthTechnicalUserTool.CreateOAuthTechnicalUserToolName,
			CreateServerToServerOAuthAppTool.CreateServerToServerOAuthAppToolName,
			VerifyOAuthAppTool.VerifyOAuthAppToolName);

		// Assert
		// The destructive flag of a hidden tool now travels on the compact discovery index; the index only
		// carries a non-null flag when the invoker registry registered the tool, i.e. the feature is enabled.
		IndexEntryOf(index, GetIdentityServiceConfigTool.GetIdentityServiceConfigToolName).Destructive.Should().NotBe(true,
			because: "reading the identity service config is read-only");

		IndexEntryOf(index, ResolveOAuthSystemUserTool.ResolveOAuthSystemUserToolName).Destructive.Should().NotBe(true,
			because: "resolving a system user is read-only");

		IndexEntryOf(index, CreateOAuthTechnicalUserTool.CreateOAuthTechnicalUserToolName).Destructive.Should().BeTrue(
			because: "creating a technical user mutates Creatio");
		ContractOf(contracts, CreateOAuthTechnicalUserTool.CreateOAuthTechnicalUserToolName).Description
			.Should().Contain("ROLE GRANT IS DEFERRED",
				because: "agents must be told the REST-only path does not grant a Creatio role");

		IndexEntryOf(index, CreateServerToServerOAuthAppTool.CreateServerToServerOAuthAppToolName).Destructive.Should().BeTrue(
			because: "creating an OAuth app mutates Creatio");
		ContractOf(contracts, CreateServerToServerOAuthAppTool.CreateServerToServerOAuthAppToolName).Description
			.Should().Contain("never written to logs",
				because: "agents must be told the client secret is surfaced only in the structured result");

		IndexEntryOf(index, VerifyOAuthAppTool.VerifyOAuthAppToolName).Destructive.Should().NotBe(true,
			because: "verifying an OAuth app is read-only");
		ContractOf(contracts, VerifyOAuthAppTool.VerifyOAuthAppToolName).Description
			.Should().Contain("never returned or logged",
				because: "agents must be told the access token text is never surfaced");
	}

	[Test]
	[Description("Verifies the approved argument schema of the OAuth configuration tools via the full get-tool-contract payload on the lazy surface.")]
	[AllureTag("oauth-mcp-tools")]
	[AllureName("OAuth configuration tools expose the approved argument schema through get-tool-contract")]
	[AllureDescription("Verifies required arguments of the OAuth configuration tools via the full tool contracts returned by get-tool-contract.")]
	public async Task OAuthConfigTools_Should_Advertise_Approved_ArgumentSchema_When_FeatureEnabled()
	{
		// Arrange
		await using var arrangeContext = Arrange();

		// Act
		IReadOnlyList<ToolContractDefinition> contracts = await FetchContractsAsync(
			arrangeContext,
			GetIdentityServiceConfigTool.GetIdentityServiceConfigToolName,
			ResolveOAuthSystemUserTool.ResolveOAuthSystemUserToolName,
			VerifyOAuthAppTool.VerifyOAuthAppToolName);

		// Assert
		ContractOf(contracts, GetIdentityServiceConfigTool.GetIdentityServiceConfigToolName).InputSchema.Required
			.Should().BeEquivalentTo(["environment-name"],
				because: "get-identity-service-config requires only the environment name");

		ContractOf(contracts, ResolveOAuthSystemUserTool.ResolveOAuthSystemUserToolName).InputSchema.Required
			.Should().BeEquivalentTo(["environment-name"],
				because: "resolve-oauth-system-user requires only the environment name; name and id are optional");

		ContractOf(contracts, VerifyOAuthAppTool.VerifyOAuthAppToolName).InputSchema.Required
			.Should().BeEquivalentTo(["environment-name", "client-id", "client-secret"],
				because: "verify-oauth-app requires the environment plus the client credentials to verify");
	}

	private static async Task<IReadOnlyList<ToolContractDefinition>> FetchContractsAsync(
		ArrangeContext arrangeContext,
		params string[] toolNames)
	{
		CallToolResult contractResult = await arrangeContext.Session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> { ["tool-names"] = toolNames }
			},
			arrangeContext.CancellationTokenSource.Token);
		ToolContractGetResponse contracts =
			EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(contractResult);
		contracts.Tools.Should().NotBeNull(
			because: "get-tool-contract must expand the requested OAuth tool names into full contracts when the deploy-identity feature is enabled");
		return contracts.Tools!;
	}

	private static ToolContractIndexEntry IndexEntryOf(IReadOnlyList<ToolContractIndexEntry> index, string toolName)
	{
		return index.Should().ContainSingle(entry => entry.Name == toolName,
			because: $"the {toolName} MCP tool must be discoverable via the get-tool-contract compact index when the deploy-identity feature is enabled")
			.Which;
	}

	private static ToolContractDefinition ContractOf(IReadOnlyList<ToolContractDefinition> contracts, string toolName)
	{
		return contracts.Should().ContainSingle(contract => contract.Name == toolName,
			because: $"get-tool-contract must return the full contract of {toolName} on the lazy surface")
			.Which;
	}

	/// <inheritdoc />
	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings)
	{
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = CreateIsolatedClioHome(
			"""
			{
			  "ActiveEnvironmentKey": "dev",
			  "Autoupdate": false,
			  "Features": {
			    "deploy-identity": true
			  },
			  "Environments": {
			    "dev": {
			      "Uri": "http://localhost",
			      "Login": "Supervisor",
			      "Password": "Supervisor",
			      "IsNetCore": true
			    }
			  }
			}
			""",
			"deploy-identity");
	}
}
