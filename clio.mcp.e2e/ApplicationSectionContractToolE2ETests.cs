using System.Text.Json;
using System.Text.RegularExpressions;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Stand-free end-to-end contract tests for application section create/update MCP tools.
/// </summary>
[TestFixture]
[AllureNUnit]
[Category("McpE2E.NoEnvironment")]
[Parallelizable(ParallelScope.Self)]
public sealed class ApplicationSectionContractToolE2ETests : McpContractFixtureBase {
	private const string SectionCreateToolName = ApplicationSectionCreateTool.ApplicationSectionCreateToolName;
	private const string SectionUpdateToolName = ApplicationSectionUpdateTool.ApplicationSectionUpdateToolName;

	[Test]
	[Description("Exposes create-app-section via the get-tool-contract compact index so callers can discover the existing-app section creation tool on the lazy surface.")]
	[AllureFeature(SectionCreateToolName)]
	[AllureTag(SectionCreateToolName)]
	[AllureName("Application section create tool is discoverable on the lazy surface")]
	[AllureDescription("Starts the real clio MCP server and verifies that create-app-section is discoverable via the get-tool-contract compact index.")]
	public async Task ApplicationSectionCreate_Should_Be_Listed_By_Mcp_Server() {
		// Arrange
		await using ArrangeContext arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames =
			await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(SectionCreateToolName,
			because: $"the {SectionCreateToolName} MCP tool must be discoverable on the lazy surface (get-tool-contract compact index) even though it is not resident in tools/list, so MCP callers can discover the existing-app section creation tool");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes create-app-section with an invalid environment, and verifies that the failure remains human-readable.")]
	[AllureFeature(SectionCreateToolName)]
	[AllureTag(SectionCreateToolName)]
	[AllureName("Application section create reports invalid environment failures")]
	[AllureDescription("Uses the real clio MCP server to call create-app-section with an unknown environment name and verifies that the tool returns a structured readable error envelope.")]
	public async Task ApplicationSectionCreate_Should_Report_Invalid_Environment_Failure() {
		// Arrange
		await using ArrangeContext arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-section-create-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			SectionCreateToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["application-code"] = "UsrMissingApp",
					["caption"] = "Orders"
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		ApplicationSectionContextResponseEnvelope response = ApplicationResultParser.ExtractSectionCreate(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: $"structured create-app-section failures should be returned in the payload instead of as MCP invocation errors. Actual result: {DescribeCallResult(callResult)}");
		response.Success.Should().BeFalse(
			because: "create-app-section should fail when the requested environment does not exist");
		response.Error.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found)",
			because: "the failure should explain that the requested environment is missing");
	}

	[Test]
	[Description("Exposes update-app-section via the get-tool-contract compact index so callers can discover the existing-section update tool on the lazy surface.")]
	[AllureFeature(SectionUpdateToolName)]
	[AllureTag(SectionUpdateToolName)]
	[AllureName("Application section update tool is discoverable on the lazy surface")]
	[AllureDescription("Starts the real clio MCP server and verifies that update-app-section is discoverable via the get-tool-contract compact index.")]
	public async Task ApplicationSectionUpdate_Should_Be_Listed_By_Mcp_Server() {
		// Arrange
		await using ArrangeContext arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames =
			await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(SectionUpdateToolName,
			because: $"the {SectionUpdateToolName} MCP tool must be discoverable on the lazy surface (get-tool-contract compact index) even though it is not resident in tools/list, so MCP callers can discover the existing-section update tool");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes update-app-section with an invalid environment, and verifies that the failure remains human-readable.")]
	[AllureFeature(SectionUpdateToolName)]
	[AllureTag(SectionUpdateToolName)]
	[AllureName("Application section update reports invalid environment failures")]
	[AllureDescription("Uses the real clio MCP server to call update-app-section with an unknown environment name and verifies that the tool returns a structured readable error envelope.")]
	public async Task ApplicationSectionUpdate_Should_Report_Invalid_Environment_Failure() {
		// Arrange
		await using ArrangeContext arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-section-update-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			SectionUpdateToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["application-code"] = "UsrMissingApp",
					["section-code"] = "UsrMissingSection",
					["caption"] = "Orders"
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		ApplicationSectionUpdateContextResponseEnvelope response = ApplicationResultParser.ExtractSectionUpdate(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: $"structured update-app-section failures should be returned in the payload instead of as MCP invocation errors. Actual result: {DescribeCallResult(callResult)}");
		response.Success.Should().BeFalse(
			because: "update-app-section should fail when the requested environment does not exist");
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
