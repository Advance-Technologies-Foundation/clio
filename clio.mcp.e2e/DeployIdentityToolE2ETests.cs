using Allure.NUnit;
using Allure.NUnit.Attributes;
using Allure.Net.Commons;
using Clio.Common;
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
		FieldDescription(contract, "overwrite").Should().Contain("target directory",
			because: "agents should know overwrite replaces an existing IdentityService deployment directory");
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
				"overwrite",
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

	[Test]
	[Description("Passes the published environment-name and overwrite fields through the real MCP server into deploy-identity resolution.")]
	[AllureTag(ToolName)]
	[AllureName("Deploy identity binds published environment and overwrite fields")]
	[AllureDescription("Calls the destructive long-tail tool with its curated contract shape and verifies the requested missing environment reaches resolution instead of being silently dropped during MCP binding.")]
	public async Task DeployIdentity_Should_Bind_Published_Environment_And_Overwrite_Fields()
	{
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		string clioHome = CreateClioHomeWithDeployIdentityEnabled();
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = clioHome;
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		string missingEnvironment = $"missing-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await AllureApi.Step("Call deploy-identity through the real MCP server", () =>
			session.CallDestructiveAsync(
				ToolName,
				new Dictionary<string, object?> {
					["environment-name"] = missingEnvironment,
					["overwrite"] = true
				},
				cancellationTokenSource.Token));
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		AllureApi.Step("Assert the call returns a structured command result", () =>
			callResult.IsError.Should().NotBeTrue(
				because: "a valid contract-shaped call should return a structured command result"));
		AllureApi.Step("Assert the missing environment prevents deployment", () =>
			execution.ExitCode.Should().Be(1,
				because: "the deliberately missing registered environment cannot be deployed"));
		AllureApi.Step("Assert the published environment reaches resolution", () =>
			execution.Output.Should().Contain(message => message.Value != null
				&& message.Value.Contains(missingEnvironment, StringComparison.Ordinal),
				because: "the MCP binder must preserve the requested environment-name instead of falling back to the active environment"));
		AllureApi.Step("Assert the command failure is classified as error output", () =>
			execution.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Error,
				because: "the rejected environment should remain an actionable command error rather than a transport failure"));
	}

	[Test]
	[Description("Rejects the obsolete environmentName field without falling back to the active environment.")]
	[AllureTag(ToolName)]
	[AllureName("Deploy identity rejects obsolete environment alias")]
	[AllureDescription("Calls deploy-identity with the obsolete camel-case environment field and verifies the real MCP server refuses it before active-environment fallback.")]
	public async Task DeployIdentity_Should_Reject_Obsolete_EnvironmentName_Field()
	{
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		string clioHome = CreateClioHomeWithDeployIdentityEnabled();
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = clioHome;
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);

		// Act
		CallToolResult callResult = await AllureApi.Step("Call deploy-identity with the obsolete field", () =>
			session.CallDestructiveAsync(
				ToolName,
				new Dictionary<string, object?> { ["environmentName"] = "obsolete-target" },
				cancellationTokenSource.Token));
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		AllureApi.Step("Assert the stale call returns a structured result", () =>
			callResult.IsError.Should().NotBeTrue(
				because: "an obsolete argument is a caller-correctable validation error, not an MCP transport failure"));
		AllureApi.Step("Assert the stale contract call is refused", () =>
			execution.ExitCode.Should().Be(1,
				because: "the obsolete field must not allow deploy-identity to use the active environment"));
		AllureApi.Step("Assert the correction names the published field", () =>
			execution.Output.Should().Contain(message => message.Value != null
				&& message.Value.Contains("environment-name", StringComparison.Ordinal),
				because: "the caller needs the canonical field name to correct the request"));
		AllureApi.Step("Assert the stale argument is classified as error output", () =>
			execution.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Error,
				because: "the rejected alias should remain an actionable command error"));
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
