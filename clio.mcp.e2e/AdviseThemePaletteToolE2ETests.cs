using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Command.Theming;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for the advise-theme-palette MCP tool. The advisor is stateless, offline pure compute,
/// so the real clio MCP server can run its operations without a live Creatio environment.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("advise-theme-palette")]
[NonParallelizable]
public sealed class AdviseThemePaletteToolE2ETests : McpContractFixtureBase {
	private const string ToolName = AdviseThemePaletteTool.ToolName;

	[Test]
	[AllureTag(ToolName)]
	[AllureName("advise-theme-palette advertises read-only metadata and adapts a low-contrast primary")]
	[Description("Starts the real clio MCP server, verifies advise-theme-palette is advertised as read-only with the guidance pointer, and invokes the adapt-primary operation on a low-contrast colour.")]
	public async Task ThemeColorAdvisor_Should_Advertise_And_AdaptPrimary() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IList<McpClientTool> tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);
		McpClientTool tool = tools.Single(t => t.Name == ToolName);
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["operation"] = "adapt-primary",
					["primary"] = "#cccccc"
				}
			},
			context.CancellationTokenSource.Token);
		ThemeColorAdvisorResult result = EntitySchemaStructuredResultParser.Extract<ThemeColorAdvisorResult>(callResult);

		// Assert
		tool.ProtocolTool.Annotations.Should().NotBeNull(
			because: "the MCP server should expose tool annotations for client-side safety policies");
		tool.ProtocolTool.Annotations!.ReadOnlyHint.Should().BeTrue(
			because: "the advisor is pure compute that never writes or touches an environment");
		tool.Description.Should().Contain("get-guidance theming",
			because: "the description routes agents to the theme workflow guidance");
		callResult.IsError.Should().NotBeTrue(
			because: "the advisor returns a structured result instead of a top-level MCP failure");
		result.Success.Should().BeTrue(
			because: "a valid primary produces an adaptation verdict");
		result.AdaptationState.Should().Be("adapted",
			because: "a low-contrast grey is below 3:1 but a darker compliant variant exists");
		result.Adapted500.Should().Be("#949494",
			because: "the advisor returns the calibrated darker variant");
	}

	[Test]
	[AllureTag(ToolName)]
	[AllureName("advise-theme-palette triage sorts brand colours and identifies the primary candidate")]
	[Description("Starts the real clio MCP server and invokes the triage operation on a mix of valid and invalid colours; verifies the accepted/passing counts and the highest-contrast candidate.")]
	public async Task ThemeColorAdvisor_Should_Triage_BrandColours() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["operation"] = "triage",
					["colors"] = new object?[] { "#004fd6", "not-a-color", "#cccccc" }
				}
			},
			context.CancellationTokenSource.Token);
		ThemeColorAdvisorResult result = EntitySchemaStructuredResultParser.Extract<ThemeColorAdvisorResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "triage completes even when some inputs are rejected");
		result.Success.Should().BeTrue(
			because: "triage returns a structured verdict packet");
		result.AcceptedCount.Should().Be(2,
			because: "two of the three inputs normalize");
		result.HighestContrastHex.Should().Be("#004fd6",
			because: "#004fd6 has the highest contrast among the accepted inputs");
	}

	[Test]
	[AllureTag(ToolName)]
	[AllureName("advise-theme-palette preview returns only the base -500 per role by default")]
	[Description("Starts the real clio MCP server and invokes the preview operation without full-stops; verifies each role's palette carries only the base -500 stop, not the full palette ramp.")]
	public async Task ThemeColorAdvisor_Should_Preview_Base500_ByDefault() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["operation"] = "preview",
					["primary"] = "#004fd6",
					["secondary"] = "#0d2e4e",
					["accent"] = "#f94e11"
				}
			},
			context.CancellationTokenSource.Token);
		ThemeColorAdvisorResult result = EntitySchemaStructuredResultParser.Extract<ThemeColorAdvisorResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "preview sources the system defaults offline and returns a structured result");
		result.Success.Should().BeTrue(
			because: "a valid preview with template-sourced system colours completes");
		result.Palettes.Should().ContainKeys(new[] { "primary", "secondary", "accent", "success", "error" },
			because: "every brand and system role is previewed");
		result.Palettes!["primary"].Should().ContainKey("500").And.HaveCount(1,
			because: "the default preview surfaces only the base -500 per role, not the palette stops");
	}
}
