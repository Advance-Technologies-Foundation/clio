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
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("theming-version-floor")]
[NonParallelizable]
public sealed class ThemingVersionFloorContractE2ETests : McpContractFixtureBase {

	private static readonly IReadOnlyList<string> GatedThemingToolNames = [
		CreateThemeTool.ToolName,
		UpdateThemeTool.ToolName,
		DeleteThemeTool.ToolName,
		ListThemesTool.ToolName,
		ClearThemesCacheTool.ToolName,
		CheckThemingAccessTool.ToolName
	];

	[Test]
	[AllureTag("theming")]
	[AllureName("every ThemeService-backed tool contract advertises the Creatio version floor")]
	[Description("Starts the real clio MCP server, expands the full contracts of all six ThemeService-backed tools via get-tool-contract, and verifies each description advertises the Creatio version floor pinned by ThemeServiceRequirement.MinVersion.")]
	public async Task ThemingToolContracts_Should_Advertise_CreatioVersionFloor() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));
		string advertisedFloor = $"Requires Creatio {ThemeServiceRequirement.MinVersion} or later";

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
			contract.Description.Should().Contain(advertisedFloor,
				because: $"the {toolName} contract must advertise the ThemeService version floor so agents can refuse a pre-{ThemeServiceRequirement.MinVersion} target up front");
		}
	}
}
