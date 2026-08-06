using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for the get-target-package MCP probe. Resolving a real package needs a live Creatio
/// environment, so the hermetic CI-safe assertions are that the real clio MCP server makes the probe reachable
/// on the lazy surface, and that its args wrapper binds to a structured validation error naming the missing
/// kebab-case field.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("get-target-package")]
[NonParallelizable]
public sealed class GetTargetPackageToolE2ETests : McpContractFixtureBase {

	[Test]
	[AllureTag(GetTargetPackageTool.ToolName)]
	[AllureName("get-target-package is discoverable on the lazy surface")]
	[Description("Starts the real clio MCP server and verifies get-target-package is discoverable via the get-tool-contract compact index on the lazy tool surface, which is how the branding guide reaches it.")]
	public async Task GetTargetPackage_Should_Be_Discoverable_On_Lazy_Surface() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(GetTargetPackageTool.ToolName,
			because: "the branding flow resolves the target package before it names it to the user, so the probe must be reachable on the lazy surface even though it is not resident in tools/list");
	}

	[Test]
	[AllureTag(GetTargetPackageTool.ToolName)]
	[AllureName("get-target-package binds the args wrapper and returns a structured validation failure")]
	[Description("Calls get-target-package through the real clio MCP server with an empty args object and verifies the structured kebab-case validation error names environment-name, proving the args wrapper binds without a live Creatio environment.")]
	public async Task GetTargetPackage_Should_Return_Structured_Validation_Failure_When_Args_Are_Empty() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			GetTargetPackageTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?>()
			},
			context.CancellationTokenSource.Token);
		GetTargetPackageResponse result =
			EntitySchemaStructuredResultParser.Extract<GetTargetPackageResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an argument mistake must surface as a structured in-tool failure, not an MCP protocol error");
		result.Success.Should().BeFalse(
			because: "resolving a target package without an environment name is invalid");
		result.Error.Should().Contain("environment-name",
			because: "the failure must name the exact kebab-case field the caller has to add");
		result.ResolutionFailed.Should().NotBeTrue(
			because: "an argument mistake is not a definitive answer about the environment's packages — flagging it as one would send the agent asking the user for another package");
	}
}
