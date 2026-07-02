using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Command.Theming;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for the theme-color-advisor MCP tool. The advisor is stateless, offline pure compute,
/// so the real clio MCP server can run its operations without a live Creatio environment. Its tools are
/// behind the <c>theming</c> feature toggle, so the fixture skip-gates when the feature is disabled (the
/// default) — like the other theming E2E fixtures.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("theme-color-advisor")]
[NonParallelizable]
public sealed class ThemeColorAdvisorToolE2ETests {
	private const string ToolName = ThemeColorAdvisorTool.ToolName;

	[Test]
	[AllureTag(ToolName)]
	[AllureName("theme-color-advisor advertises read-only metadata and adapts a low-contrast primary")]
	[Description("Starts the real clio MCP server, verifies theme-color-advisor is advertised as read-only with the guidance pointer, and invokes the adapt-primary operation on a low-contrast colour. Skips when the theming feature is disabled.")]
	public async Task ThemeColorAdvisor_Should_Advertise_And_AdaptPrimary() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);

		// Act
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationTokenSource.Token);
		McpClientTool tool = tools.Single(t => t.Name == ToolName);
		CallToolResult callResult = await session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> { ["operation"] = "adapt-primary", ["primary"] = "#cccccc" },
			cancellationTokenSource.Token);
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
	[AllureName("theme-color-advisor triage sorts brand colours and identifies the primary candidate")]
	[Description("Starts the real clio MCP server and invokes the triage operation on a mix of valid and invalid colours; verifies the accepted/passing counts and the highest-contrast candidate. Skips when the theming feature is disabled.")]
	public async Task ThemeColorAdvisor_Should_Triage_BrandColours() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);

		// Act
		CallToolResult callResult = await session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["operation"] = "triage",
				["colors"] = new object?[] { "#004fd6", "not-a-color", "#cccccc" }
			},
			cancellationTokenSource.Token);
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
}
