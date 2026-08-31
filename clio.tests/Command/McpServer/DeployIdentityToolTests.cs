using System.Linq;
using System.Reflection;
using System.Text.Json;
using Clio.Common;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class DeployIdentityToolTests
{
	[Test]
	[Description("Keeps the curated deploy-identity contract aligned with its required environment and supported overwrite arguments.")]
	public void DeployIdentityContract_Should_Expose_Environment_And_Overwrite_Fields()
	{
		// Arrange
		string[] toolNames = [DeployIdentityTool.DeployIdentityToolName];

		// Act
		ToolContractGetResponse response = ToolContractCatalog.GetContracts(toolNames);
		ToolContractDefinition contract = response.Tools!.Single();

		// Assert
		contract.InputSchema.Properties.Select(property => property.Name).Should().Contain(
			["environment-name", "overwrite"],
			because: "the curated contract must advertise the fields that deploy-identity binds and executes");
		contract.InputSchema.Required.Should().BeEquivalentTo(
			["environment-name"],
			because: "the target environment is required while overwrite remains optional");
	}

	[Test]
	[Description("Binds the published deploy-identity environment and overwrite fields through the production MCP serializer.")]
	public void DeployIdentityArgs_Should_Bind_Published_Environment_And_Overwrite_Fields()
	{
		// Arrange
		JsonSerializerOptions options = Clio.BindingsModule.CreateMcpSerializerOptions();
		const string json = """
			{"environment-name":"bank","overwrite":true}
			""";

		// Act
		DeployIdentityArgs? args = JsonSerializer.Deserialize<DeployIdentityArgs>(json, options);

		// Assert
		args.Should().NotBeNull(
			because: "the published deploy-identity contract must deserialize through the production MCP serializer");
		args!.EnvironmentName.Should().Be("bank",
			because: "environment-name is the required field advertised by get-tool-contract");
		args.Overwrite.Should().BeTrue(
			because: "the published overwrite flag must reach deploy-identity execution");
	}

	[Test]
	[Description("Rejects a missing MCP environment instead of allowing deploy-identity to fall back to the active environment.")]
	public void DeployIdentity_Should_Fail_Closed_When_Environment_Is_Missing()
	{
		// Arrange
		DeployIdentityTool tool = new(
			Substitute.For<ILogger>(),
			Substitute.For<IToolCommandResolver>());
		DeployIdentityArgs args = new(null!);

		// Act
		CommandExecutionResult result = tool.DeployIdentity(args);

		// Assert
		result.ExitCode.Should().Be(1,
			because: "a missing MCP target is a caller-actionable validation error");
		result.Output.Should().Contain(message => message.Value.ToString()!.Contains("environment-name is required"),
			because: "deploy-identity must fail before the command can select the active environment");
	}

	[Test]
	[Description("Advertises the stable deploy-identity MCP tool name so agents can target the deployment contract without drift.")]
	public void DeployIdentity_Should_Advertise_Stable_Tool_Name()
	{
		// Arrange

		// Act
		string toolName = DeployIdentityTool.DeployIdentityToolName;

		// Assert
		toolName.Should().Be("deploy-identity",
			because: "the MCP contract should keep a stable deploy-identity tool name");
	}

	[Test]
	[Description("Marks deploy-identity as destructive and documents automatic archive and port defaults plus secret handling in the MCP description.")]
	public void DeployIdentity_Should_Expose_Destructive_Metadata_And_Secret_Guidance()
	{
		// Arrange
		MethodInfo method = typeof(DeployIdentityTool).GetMethod(nameof(DeployIdentityTool.DeployIdentity))!;
		McpServerToolAttribute attribute = method.GetCustomAttribute<McpServerToolAttribute>()!;
		System.ComponentModel.DescriptionAttribute description =
			method.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()!;

		// Act
		bool destructive = attribute.Destructive;
		string text = description.Description;

		// Assert
		destructive.Should().BeTrue(
			because: "deploy-identity mutates IIS, Creatio sys-settings, and local clio settings");
		text.Should().Contain("EnvironmentPath",
			because: "agents should know zipFile can be omitted when IdentityService.zip is under the registered environment");
		text.Should().Contain("40001-40100",
			because: "agents should know identitySitePort can be omitted and auto-selected from the default range");
		text.Should().Contain("noApp",
			because: "agents should know they can intentionally skip OAuth app creation");
		text.Should().Contain("createTechUser",
			because: "agents should know technical user creation is opt-in");
		text.Should().Contain("Secret values are written only to clio settings",
			because: "the tool description should prevent public secret disclosure");
	}
}
