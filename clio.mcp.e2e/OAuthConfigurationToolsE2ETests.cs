using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Client;
using System.Text.Json;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the server-to-server OAuth configuration MCP tools. These tools share the
/// <c>deploy-identity</c> feature toggle, so the real clio MCP server only advertises them when that
/// feature is enabled in the active <c>appsettings.json</c>. The tests start the real
/// <c>clio mcp-server</c> process, enable the feature in an isolated CLIO_HOME, and assert tool
/// discovery, safety metadata, the approved argument schemas, and secret-handling guidance.
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
	[Description("Starts the real clio MCP server with the deploy-identity feature enabled and verifies all five server-to-server OAuth configuration tools are advertised.")]
	[AllureTag("oauth-mcp-tools")]
	[AllureName("Server-to-server OAuth configuration tools are advertised by the real server")]
	[AllureDescription("Starts the real clio MCP server with the deploy-identity feature enabled and verifies all five OAuth configuration tools are advertised.")]
	public async Task OAuthConfigTools_Should_Be_Advertised_When_FeatureEnabled()
	{
		// Arrange
		await using var arrangeContext = Arrange();

		// Act
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);

		// Assert
		tools.Select(tool => tool.Name).Should().Contain(ExpectedToolNames,
			because: "all five server-to-server OAuth configuration tools must be advertised when the deploy-identity feature is enabled");
	}

	[Test]
	[Description("Verifies the create-* OAuth tools advertise destructive metadata and the read-only tools advertise non-destructive metadata via the real MCP server.")]
	[AllureTag("oauth-mcp-tools")]
	[AllureName("OAuth configuration tools advertise correct safety metadata")]
	[AllureDescription("Verifies destructive vs read-only safety annotations for the five OAuth configuration tools via the real MCP server.")]
	public async Task OAuthConfigTools_Should_Advertise_Correct_SafetyMetadata_When_FeatureEnabled()
	{
		// Arrange
		await using var arrangeContext = Arrange();

		// Act
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);

		// Assert
		McpClientTool getConfig = tools.Single(tool => tool.Name == GetIdentityServiceConfigTool.GetIdentityServiceConfigToolName);
		getConfig.ProtocolTool.Annotations!.DestructiveHint.Should().NotBe(true,
			because: "reading the identity service config is read-only");

		McpClientTool resolveUser = tools.Single(tool => tool.Name == ResolveOAuthSystemUserTool.ResolveOAuthSystemUserToolName);
		resolveUser.ProtocolTool.Annotations!.DestructiveHint.Should().NotBe(true,
			because: "resolving a system user is read-only");

		McpClientTool createUser = tools.Single(tool => tool.Name == CreateOAuthTechnicalUserTool.CreateOAuthTechnicalUserToolName);
		createUser.ProtocolTool.Annotations!.DestructiveHint.Should().BeTrue(
			because: "creating a technical user mutates Creatio");
		createUser.Description.Should().Contain("ROLE GRANT IS DEFERRED",
			because: "agents must be told the REST-only path does not grant a Creatio role");

		McpClientTool createApp = tools.Single(tool => tool.Name == CreateServerToServerOAuthAppTool.CreateServerToServerOAuthAppToolName);
		createApp.ProtocolTool.Annotations!.DestructiveHint.Should().BeTrue(
			because: "creating an OAuth app mutates Creatio");
		createApp.Description.Should().Contain("never written to logs",
			because: "agents must be told the client secret is surfaced only in the structured result");

		McpClientTool verifyApp = tools.Single(tool => tool.Name == VerifyOAuthAppTool.VerifyOAuthAppToolName);
		verifyApp.ProtocolTool.Annotations!.DestructiveHint.Should().NotBe(true,
			because: "verifying an OAuth app is read-only");
		verifyApp.Description.Should().Contain("never returned or logged",
			because: "agents must be told the access token text is never surfaced");
	}

	[Test]
	[Description("Verifies the approved argument schema of the five OAuth configuration tools via the real MCP server discovery payload.")]
	[AllureTag("oauth-mcp-tools")]
	[AllureName("OAuth configuration tools advertise the approved argument schema")]
	[AllureDescription("Verifies required and optional arguments of the five OAuth configuration tools via the real MCP server.")]
	public async Task OAuthConfigTools_Should_Advertise_Approved_ArgumentSchema_When_FeatureEnabled()
	{
		// Arrange
		await using var arrangeContext = Arrange();

		// Act
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);

		// Assert
		RequiredArgsOf(tools, GetIdentityServiceConfigTool.GetIdentityServiceConfigToolName)
			.Should().BeEquivalentTo(["environment-name"],
				because: "get-identity-service-config requires only the environment name");

		RequiredArgsOf(tools, ResolveOAuthSystemUserTool.ResolveOAuthSystemUserToolName)
			.Should().BeEquivalentTo(["environment-name"],
				because: "resolve-oauth-system-user requires only the environment name; name and id are optional");

		RequiredArgsOf(tools, VerifyOAuthAppTool.VerifyOAuthAppToolName)
			.Should().BeEquivalentTo(["environment-name", "client-id", "client-secret"],
				because: "verify-oauth-app requires the environment plus the client credentials to verify");
	}

	private static IEnumerable<string?> RequiredArgsOf(IList<McpClientTool> tools, string toolName)
	{
		McpClientTool tool = tools.Single(item => item.Name == toolName);
		JsonElement inputSchema = JsonSerializer.SerializeToElement(tool.ProtocolTool.InputSchema);
		JsonElement argsSchema = inputSchema.GetProperty("properties").GetProperty("args");
		return argsSchema.GetProperty("required").EnumerateArray().Select(item => item.GetString());
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
