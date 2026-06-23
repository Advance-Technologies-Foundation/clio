using System.Reflection;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using ModelContextProtocol.Server;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class DeployIdentityToolTests
{
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
