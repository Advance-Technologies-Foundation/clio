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
	[Description("Advertises a stable MCP tool name for check-settings-health.")]
	public void SettingsHealth_Should_Advertise_Stable_Tool_Name() {
		// Arrange

		// Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(SettingsHealthTool)
			.GetMethod(nameof(SettingsHealthTool.GetSettingsHealth))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be(SettingsHealthTool.ToolName,
			because: "the check-settings-health tool name must stay stable for bootstrap diagnostics");
	}

	[Test]
	[Category("Unit")]
	[Description("Projects the bootstrap report into the structured check-settings-health MCP payload.")]
	public void SettingsHealth_Should_Return_Structured_Bootstrap_Report() {
		// Arrange
		ISettingsBootstrapService settingsBootstrapService = Substitute.For<ISettingsBootstrapService>();
		settingsBootstrapService.GetReport().Returns(new SettingsBootstrapReport(
			"issues-detected",
			"/tmp/appsettings.json",
			"wrong-dev",
			null,
			1,
			[new SettingsIssue("invalid-active-environment", "ActiveEnvironmentKey is missing or does not point to a configured environment.")],
			[],
			true,
			false));
		SettingsHealthTool tool = new(settingsBootstrapService);

		// Act
		SettingsHealthResult result = tool.GetSettingsHealth();

		// Assert
		result.Status.Should().Be("issues-detected",
			because: "the MCP projection should preserve the bootstrap status");
		result.SettingsFilePath.Should().Be("/tmp/appsettings.json",
			because: "the caller needs the physical appsettings path for remediation");
		result.ActiveEnvironmentKey.Should().Be("wrong-dev",
			because: "the tool should report the original configured active environment key so the user knows which key is wrong");
		result.ResolvedActiveEnvironmentKey.Should().BeNull(
			because: "bootstrap no longer auto-selects a fallback environment");
		result.Issues.Should().ContainSingle(issue => issue.Code == "invalid-active-environment",
			because: "the payload should expose structured issue diagnostics");
		result.RepairsApplied.Should().BeEmpty(
			because: "no auto-repair is applied — the user must run set-active-environment explicitly");
		result.CanStartBootstrapTools.Should().BeTrue(
			because: "bootstrap-safe tools remain available even when the active environment is misconfigured");
		result.CanExecuteEnvTools.Should().BeFalse(
			because: "named-environment tools must stay blocked when no active environment is resolved");
	}
}
