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
[AllureFeature("deploy-identity")]
public sealed class DeployIdentityToolE2ETests
{
	private const string ToolName = DeployIdentityTool.DeployIdentityToolName;

	[Test]
	[Description("Starts the real clio MCP server with the deploy-identity feature enabled, discovers deploy-identity via the get-tool-contract compact index, and verifies destructive metadata, secret guidance, optional defaults, and the approved argument contract from the full tool contract.")]
	[AllureTag(ToolName)]
	[AllureName("Deploy identity is discoverable with destructive metadata and secret guidance on the lazy surface")]
	[AllureDescription("Uses the get-tool-contract compact index and full contract of the real clio MCP server to verify that deploy-identity is destructive, documents automatic archive/port defaults, and does not steer agents into disclosing generated OAuth secrets.")]
	public async Task DeployIdentity_Should_Expose_Metadata_And_Argument_Contract_On_Lazy_Surface()
	{
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		string clioHome = CreateClioHomeWithDeployIdentityEnabled();
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = clioHome;
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		CancellationToken token = cancellationTokenSource.Token;

		// Act
		// deploy-identity is feature-gated AND hidden from tools/list on the lazy tool surface, so its
		// discovery metadata comes from the get-tool-contract compact index (destructive flag) and the
		// full curated contract (description, argument schema) instead of tools/list annotations.
		IReadOnlyList<ToolContractIndexEntry> index = await session.GetToolContractIndexAsync(token);
		CallToolResult contractResult = await session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["tool-names"] = new[] { ToolName }
				}
			},
			token);
		ToolContractGetResponse contracts =
			EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(contractResult);

		// Assert
		ToolContractIndexEntry indexEntry = index.Should().ContainSingle(item => item.Name == ToolName,
			because: "deploy-identity must be discoverable via the get-tool-contract compact index when the deploy-identity feature is enabled")
			.Which;
		indexEntry.Destructive.Should().BeTrue(
			because: "deploy-identity mutates IIS, Creatio sys-settings, and local clio settings");
		indexEntry.ContractAvailable.Should().BeTrue(
			because: "agents must be able to expand deploy-identity into its full contract before calling it through clio-run");

		ToolContractDefinition contract = contracts.Tools!.Single(tool => tool.Name == ToolName);
		contract.Description.Should().Contain("Never echo the generated client secret",
			because: "the tool contract should prevent public disclosure of generated OAuth secrets");
		FieldDescription(contract, "zipFile").Should().Contain("EnvironmentPath",
			because: "agents should know zipFile can be omitted when IdentityService.zip is under the registered environment");
		FieldDescription(contract, "identitySitePort").Should().Contain("40001-40100",
			because: "agents should know identitySitePort can be omitted and auto-selected from the default range");
		FieldDescription(contract, "noApp").Should().Contain("without creating a clio OAuth app",
			because: "agents should know they can deploy and connect IdentityService without creating an OAuth app");
		FieldDescription(contract, "createTechUser").Should().Contain("technical user",
			because: "agents should know technical user creation is opt-in");

		contract.InputSchema.Properties.Select(property => property.Name).Should().BeEquivalentTo(
			[
				"environment-name",
				"zipFile",
				"identitySitePort",
				"identityArchivePathInBundle",
				"identitySiteName",
				"identityPath",
				"configurationMode",
				"clientName",
				"clientApplicationUrl",
				"clientDescription",
				"noApp",
				"createTechUser",
				"user"
			],
			because: "the deploy-identity contract exposed through get-tool-contract should document only the supported arguments");
		contract.InputSchema.Required.Should().BeEquivalentTo(
			["environment-name"],
			because: "deploy-identity should allow zipFile and identitySitePort to default from EnvironmentPath and the IIS port scanner");
	}

	private static string FieldDescription(ToolContractDefinition contract, string fieldName)
	{
		return contract.InputSchema.Properties.Single(property => property.Name == fieldName).Description;
	}

	private static string CreateClioHomeWithDeployIdentityEnabled()
	{
		string clioHome = Path.Combine(Path.GetTempPath(), $"clio-mcp-e2e-{Guid.NewGuid():N}");
		Directory.CreateDirectory(clioHome);
		File.WriteAllText(Path.Combine(clioHome, "appsettings.json"),
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
			""");
		return clioHome;
	}
}
