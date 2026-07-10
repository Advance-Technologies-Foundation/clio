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
	[AllureName("advise-theme-palette is discoverable on the lazy surface and adapts a low-contrast primary")]
	[Description("Starts the real clio MCP server, verifies advise-theme-palette is discoverable via the get-tool-contract compact index as non-destructive with the guidance pointer in its contract, and invokes the adapt-primary operation on a low-contrast colour.")]
	public async Task ThemePaletteAdvisor_Should_Be_Discoverable_And_AdaptPrimary() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		// advise-theme-palette is hidden from tools/list on the lazy surface, so its discovery metadata comes
		// from the get-tool-contract compact index (destructive flag) and full contract (description) instead
		// of tools/list annotations.
		IReadOnlyList<ToolContractIndexEntry> index =
			await context.Session.GetToolContractIndexAsync(context.CancellationTokenSource.Token);
		CallToolResult contractResult = await context.Session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["tool-names"] = new[] { ToolName }
				}
			},
			context.CancellationTokenSource.Token);
		ToolContractGetResponse contracts =
			EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(contractResult);
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["operation"] = "adapt-primary",
					["primary"] = "#cccccc"
				}
			},
			context.CancellationTokenSource.Token);
		ThemePaletteAdvisorResult result = EntitySchemaStructuredResultParser.Extract<ThemePaletteAdvisorResult>(callResult);

		// Assert
		ToolContractIndexEntry indexEntry = index.Should().ContainSingle(entry => entry.Name == ToolName,
			because: "the advise-theme-palette MCP tool must be discoverable on the lazy surface (get-tool-contract compact index) even though it is not resident in tools/list")
			.Which;
		indexEntry.Destructive.Should().BeFalse(
			because: "the advisor is pure compute that never writes or touches an environment");
		ToolContractDefinition contract = contracts.Tools!.Single(definition => definition.Name == ToolName);
		contract.Description.Should().Contain("get-guidance theming",
			because: "the contract routes agents to the theme workflow guidance");
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
	public async Task ThemePaletteAdvisor_Should_Triage_BrandColours() {
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
		ThemePaletteAdvisorResult result = EntitySchemaStructuredResultParser.Extract<ThemePaletteAdvisorResult>(callResult);

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
	public async Task ThemePaletteAdvisor_Should_Preview_Base500_ByDefault() {
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
		ThemePaletteAdvisorResult result = EntitySchemaStructuredResultParser.Extract<ThemePaletteAdvisorResult>(callResult);

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
