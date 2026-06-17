using System.Linq;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using ModelContextProtocol.Server;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class ExperimentalToolTests {

	[Test]
	[Category("Unit")]
	[Description("Advertises the stable experimental MCP tool name for client reuse.")]
	public void ToolName_ShouldBeStable_WhenAdvertisedToMcpClients() {
		// Arrange

		// Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(ExperimentalTool)
			.GetMethod(nameof(ExperimentalTool.Experimental))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be(ExperimentalTool.ToolName,
			because: "the experimental tool name must stay stable for MCP clients");
	}

	[Test]
	[Category("Unit")]
	[Description("Marks the experimental MCP tool as non-destructive and non-environment because it only mutates local settings.")]
	public void Tool_ShouldBeNonDestructiveAndLocalOnly_WhenInspected() {
		// Arrange

		// Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(ExperimentalTool)
			.GetMethod(nameof(ExperimentalTool.Experimental))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Destructive.Should().BeFalse(
			because: "toggling a feature flag is reversible and must not be flagged destructive");
		attribute.OpenWorld.Should().BeFalse(
			because: "the tool operates only on local clio settings and contacts no external system");
	}
}
