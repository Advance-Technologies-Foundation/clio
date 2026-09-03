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
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for the Creatio version floor advertised by the ThemeService-backed theming tools.
/// Enforcing the floor needs a pre-10 Creatio environment, so the hermetic CI-safe assertion targets the
/// advertised contract instead: the full contract of every gated theming tool (via get-tool-contract on the
/// real clio MCP server) must state the floor pinned by <see cref="ThemeServiceRequirement.MinVersion"/>.
/// <c>check-theming-access</c> is deliberately NOT part of that set since issue #1303 C5 — it reads only the
/// generic RightsService/LicenseService endpoints, runs on any Creatio version, and reports the ThemeService
/// floor back as <c>themeServiceMinVersion</c> instead of refusing. Its separate test below pins that
/// contract so nobody re-adds the gate by copying the loop.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("theming-version-floor")]
[NonParallelizable]
public sealed class ThemingVersionFloorContractE2ETests : McpContractFixtureBase {

	/// <summary>
	/// The theming tools that call the native <c>ThemeService</c> and therefore stay version-gated.
	/// <see cref="CheckThemingAccessTool"/> is intentionally absent — see the class remarks.
	/// </summary>
	private static readonly IReadOnlyList<string> GatedThemingToolNames = [
		CreateThemeTool.ToolName,
		UpdateThemeTool.ToolName,
		DeleteThemeTool.ToolName,
		ListThemesTool.ToolName,
		ClearThemesCacheTool.ToolName
	];

	private static readonly string AdvertisedFloor =
		$"Requires Creatio {ThemeServiceRequirement.MinVersion} or later";

	[Test]
	[AllureTag("theming")]
	[AllureName("every ThemeService-backed tool contract advertises the Creatio version floor")]
	[Description("Starts the real clio MCP server, expands the full contracts of every ThemeService-backed WRITE/read tool via get-tool-contract, and verifies each description advertises the Creatio version floor pinned by ThemeServiceRequirement.MinVersion.")]
	public async Task ThemingToolContracts_Should_Advertise_CreatioVersionFloor() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult contractResult = await context.Session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["tool-names"] = GatedThemingToolNames.ToArray()
				}
			},
			context.CancellationTokenSource.Token);
		ToolContractGetResponse contracts =
			EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(contractResult);

		// Assert
		contractResult.IsError.Should().NotBeTrue(
			because: "get-tool-contract must expand the theming tool contracts without a protocol error");
		contracts.Success.Should().BeTrue(
			because: "every requested theming tool must resolve a full contract");
		contracts.Tools.Should().NotBeNull(
			because: "a successful named expansion carries the full contract list");
		foreach (string toolName in GatedThemingToolNames) {
			ToolContractDefinition contract = contracts.Tools!.Should().ContainSingle(
					definition => definition.Name == toolName,
					because: $"the {toolName} tool must expose exactly one full contract")
				.Which;
			contract.Description.Should().Contain(AdvertisedFloor,
				because: $"the {toolName} contract must advertise the ThemeService version floor so agents can refuse a pre-{ThemeServiceRequirement.MinVersion} target up front");
		}
	}

	[Test]
	[AllureTag(CheckThemingAccessTool.ToolName)]
	[AllureName("check-theming-access is NOT version-gated and reports the ThemeService floor instead")]
	[AllureDescription("Expands the full check-theming-access contract through get-tool-contract on the real clio MCP server and verifies it no longer advertises the gating phrase, states that it runs on any Creatio version, and promises themeServiceMinVersion in its result shape.")]
	[Description("check-theming-access lost its [RequiresCreatioVersion] gate (issue #1303 C5): the advisory access probe must advertise that it runs on any Creatio version and reports the ThemeService floor back as themeServiceMinVersion, rather than refusing a pre-10 target up front.")]
	public async Task CheckThemingAccessContract_Should_Not_Advertise_VersionFloor_And_Should_Report_ThemeServiceMinVersion() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult contractResult = await context.Session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["tool-names"] = new[] { CheckThemingAccessTool.ToolName }
				}
			},
			context.CancellationTokenSource.Token);
		ToolContractGetResponse contracts =
			EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(contractResult);

		// Assert
		contractResult.IsError.Should().NotBeTrue(
			because: "get-tool-contract must expand the check-theming-access contract without a protocol error");
		contracts.Success.Should().BeTrue(
			because: "check-theming-access must resolve a full contract on the lazy surface");
		ToolContractDefinition contract = contracts.Tools!.Should().ContainSingle(
				definition => definition.Name == CheckThemingAccessTool.ToolName,
				because: "the check-theming-access tool must expose exactly one full contract")
			.Which;
		contract.Description.Should().NotContain(AdvertisedFloor,
			because: "check-theming-access is no longer version-gated, so it must not advertise the gating phrase the ThemeService-backed tools use");
		contract.Description.Should().Contain("ANY Creatio version",
			because: "the contract must tell an agent the access probe is safe as the first step of the branding flow on any core");
		contract.Description.Should().Contain("themeServiceMinVersion",
			because: "the advisory answer reports the write-command floor as a field, so the contract must name it in the result shape");
		contract.Description.Should().Contain(ThemeServiceRequirement.MinVersion,
			because: "the contract must still tell the caller which floor the theme WRITE commands need, so nobody reads 'runs anywhere' as 'custom themes work anywhere'");
	}
}
