using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Client;
using System.Text.Json;

namespace Clio.Mcp.E2E;

[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("deploy-identity")]
public sealed class DeployIdentityToolE2ETests
{
	private const string ToolName = DeployIdentityTool.DeployIdentityToolName;

	[Test]
	[Description("Starts the real clio MCP server, discovers deploy-identity, and verifies destructive metadata, secret guidance, optional defaults, and the approved argument schema.")]
	[AllureTag(ToolName)]
	[AllureName("Deploy identity advertises destructive metadata and secret guidance")]
	[AllureDescription("Uses the real clio MCP server tool discovery payload to verify that deploy-identity is destructive, advertises automatic archive/port defaults, and does not expose a secret-return argument.")]
	public async Task DeployIdentity_Should_Advertise_Metadata_And_Argument_Schema()
	{
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		string clioHome = CreateClioHomeWithDeployIdentityEnabled();
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = clioHome;
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);

		// Act
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationTokenSource.Token);
		McpClientTool tool = tools.Single(tool => tool.Name == ToolName);

		// Assert
		tool.ProtocolTool.Annotations.Should().NotBeNull(
			because: "the MCP server should expose tool annotations for client-side safety policies");
		tool.ProtocolTool.Annotations!.DestructiveHint.Should().BeTrue(
			because: "deploy-identity mutates IIS, Creatio sys-settings, and local clio settings");
		tool.Description.Should().Contain("EnvironmentPath",
			because: "agents should know zipFile can be omitted when IdentityService.zip is under the registered environment");
		tool.Description.Should().Contain("40001-40100",
			because: "agents should know identitySitePort can be omitted and auto-selected from the default range");
		tool.Description.Should().Contain("noApp",
			because: "agents should know they can deploy and connect IdentityService without creating an OAuth app");
		tool.Description.Should().Contain("createTechUser",
			because: "agents should know technical user creation is opt-in");
		tool.Description.Should().Contain("Secret values are written only to clio settings",
			because: "the tool description should prevent public disclosure of generated OAuth secrets");

		JsonElement inputSchema = JsonSerializer.SerializeToElement(tool.ProtocolTool.InputSchema);
		JsonElement argsSchema = inputSchema.GetProperty("properties").GetProperty("args");
		argsSchema.GetProperty("properties").EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
			[
				"environmentName",
				"zipFile",
				"identitySitePort",
				"identityArchivePathInBundle",
				"identitySiteName",
				"identityPath",
				"overwrite",
				"configurationMode",
				"clientName",
				"clientApplicationUrl",
				"clientDescription",
				"noApp",
				"createTechUser",
				"user"
			],
			because: "the real MCP server should advertise only the supported deploy-identity arguments inside the args wrapper");
		argsSchema.GetProperty("required").EnumerateArray().Select(item => item.GetString()).Should().BeEquivalentTo(
			["environmentName"],
			because: "deploy-identity should allow zipFile and identitySitePort to default from EnvironmentPath and the IIS port scanner");
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
