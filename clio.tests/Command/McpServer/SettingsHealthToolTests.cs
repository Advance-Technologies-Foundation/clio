using System.Linq;
using Clio.Command.McpServer.Tools;
using Clio.UserEnvironment;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class SettingsHealthToolTests {
	[Test]
	[Category("Unit")]
	[Description("Advertises a stable MCP tool name for settings-health.")]
	public void SettingsHealth_Should_Advertise_Stable_Tool_Name() {
		// Arrange

		// Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(SettingsHealthTool)
			.GetMethod(nameof(SettingsHealthTool.GetSettingsHealth))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be(SettingsHealthTool.ToolName,
			because: "the settings-health tool name must stay stable for bootstrap diagnostics");
	}

	[Test]
	[Category("Unit")]
	[Description("Projects the bootstrap report into the structured settings-health MCP payload.")]
	public void SettingsHealth_Should_Return_Structured_Bootstrap_Report() {
		// Arrange
		ISettingsBootstrapService settingsBootstrapService = Substitute.For<ISettingsBootstrapService>();
		settingsBootstrapService.GetReport().Returns(new SettingsBootstrapReport(
			"repaired",
			"/tmp/appsettings.json",
			"wrong-dev",
			"dev",
			1,
			[new SettingsIssue("invalid-active-environment", "ActiveEnvironmentKey is invalid.")],
			[new SettingsRepair("set-active-environment", "Selected 'dev' as the active environment.")],
			true,
			true));
		SettingsHealthTool tool = new(settingsBootstrapService);

		// Act
		SettingsHealthResult result = tool.GetSettingsHealth();

		// Assert
		result.Status.Should().Be("repaired",
			because: "the MCP projection should preserve the bootstrap status");
		result.SettingsFilePath.Should().Be("/tmp/appsettings.json",
			because: "the caller needs the physical appsettings path for remediation");
		result.ActiveEnvironmentKey.Should().Be("wrong-dev",
			because: "the tool should report the original configured active environment key");
		result.ResolvedActiveEnvironmentKey.Should().Be("dev",
			because: "the tool should report the resolved environment after repair");
		result.Issues.Should().ContainSingle(issue => issue.Code == "invalid-active-environment",
			because: "the payload should expose structured issue diagnostics");
		result.RepairsApplied.Should().ContainSingle(repair => repair.Code == "set-active-environment",
			because: "the payload should expose structured repair diagnostics");
		result.CanStartBootstrapTools.Should().BeTrue(
			because: "bootstrap-safe tools should remain available after a safe repair");
		result.CanExecuteEnvTools.Should().BeTrue(
			because: "named-environment execution should become available again after repair");
	}
}
