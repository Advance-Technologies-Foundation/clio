using System.Text.Json;
using System.Text.RegularExpressions;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Stand-free end-to-end contract tests for application section maintenance MCP tools.
/// </summary>
[TestFixture]
[AllureNUnit]
[Category("McpE2E.NoEnvironment")]
[Parallelizable(ParallelScope.Self)]
public sealed class ApplicationSectionMaintenanceContractToolE2ETests : McpContractFixtureBase {
	private const string SectionListToolName = ApplicationSectionGetListTool.ApplicationSectionGetListToolName;
	private const string SectionDeleteToolName = ApplicationSectionDeleteTool.ApplicationSectionDeleteToolName;

	[Test]
	[Description("Advertises list-app-sections in the MCP tool list so callers can discover the installed-app section discovery tool.")]
	[AllureFeature(SectionListToolName)]
	[AllureTag(SectionListToolName)]
	[AllureName("Application section list tool is advertised by the MCP server")]
	[AllureDescription("Starts the real clio MCP server and verifies that list-app-sections appears in the advertised tool manifest.")]
	public async Task ApplicationSectionGetList_Should_Be_Listed_By_Mcp_Server() {
		// Arrange
		await using ArrangeContext arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);
		IEnumerable<string> toolNames = tools.Select(tool => tool.Name);

		// Assert
		toolNames.Should().Contain(SectionListToolName,
			because: "list-app-sections must be advertised so MCP callers can discover installed-app section discovery");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes list-app-sections with an invalid environment, and verifies that the failure remains human-readable.")]
	[AllureFeature(SectionListToolName)]
	[AllureTag(SectionListToolName)]
	[AllureName("Application section list reports invalid environment failures")]
	[AllureDescription("Uses the real clio MCP server to call list-app-sections with an unknown environment name and verifies that the tool returns a structured readable error envelope.")]
	public async Task ApplicationSectionGetList_Should_Report_Invalid_Environment_Failure() {
		// Arrange
		await using ArrangeContext arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-section-list-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			SectionListToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["application-code"] = "UsrMissingApp"
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		ApplicationSectionListContextResponseEnvelope response = ApplicationResultParser.ExtractSectionList(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: $"structured list-app-sections failures should be returned in the payload instead of as MCP invocation errors. Actual result: {DescribeCallResult(callResult)}");
		response.Success.Should().BeFalse(
			because: "list-app-sections should fail when the requested environment does not exist");
		response.Error.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found)",
			because: "the failure should explain that the requested environment is missing");
	}

	[Test]
	[Description("Exposes delete-app-section via the get-tool-contract compact index so callers can discover the installed-app section deletion tool on the lazy surface.")]
	[AllureFeature(SectionDeleteToolName)]
	[AllureTag(SectionDeleteToolName)]
	[AllureName("Application section delete tool is discoverable on the lazy surface")]
	[AllureDescription("Starts the real clio MCP server and verifies that delete-app-section is discoverable via the get-tool-contract compact index.")]
	public async Task ApplicationSectionDelete_Should_Be_Listed_By_Mcp_Server() {
		// Arrange
		await using ArrangeContext arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames =
			await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(SectionDeleteToolName,
			because: $"the {SectionDeleteToolName} MCP tool must be discoverable on the lazy surface (get-tool-contract compact index) even though it is not resident in tools/list, so MCP callers can discover installed-app section deletion");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes delete-app-section with an invalid environment, and verifies that the failure remains human-readable.")]
	[AllureFeature(SectionDeleteToolName)]
	[AllureTag(SectionDeleteToolName)]
	[AllureName("Application section delete reports invalid environment failures")]
	[AllureDescription("Uses the real clio MCP server to call delete-app-section with an unknown environment name and verifies that the tool returns a structured readable error envelope.")]
	public async Task ApplicationSectionDelete_Should_Report_Invalid_Environment_Failure() {
		// Arrange
		await using ArrangeContext arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-section-delete-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			SectionDeleteToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["application-code"] = "UsrMissingApp",
					["section-code"] = "UsrMissingSection"
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		ApplicationSectionDeleteContextResponseEnvelope response = ApplicationResultParser.ExtractSectionDelete(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: $"structured delete-app-section failures should be returned in the payload instead of as MCP invocation errors. Actual result: {DescribeCallResult(callResult)}");
		response.Success.Should().BeFalse(
			because: "delete-app-section should fail when the requested environment does not exist");
		response.Error.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found)",
			because: "the failure should explain that the requested environment is missing");
	}

	private static string DescribeCallResult(CallToolResult callResult) {
		return JsonSerializer.Serialize(new {
			callResult.IsError,
			callResult.StructuredContent,
			callResult.Content
		});
	}
}
