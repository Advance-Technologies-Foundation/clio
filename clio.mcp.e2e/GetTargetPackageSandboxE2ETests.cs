using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Live coverage for get-target-package with no package named: the path that reads the environment's
/// CurrentPackageId setting. The hermetic fixture cannot reach it, and a unit fake cannot tell which SysPackage
/// column the setting's value matches - which is how a lookup by Id instead of UId shipped unnoticed.
/// </summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature("get-target-package")]
[NonParallelizable]
public sealed class GetTargetPackageSandboxE2ETests : McpContractFixtureBase {

	[Test]
	[AllureTag(GetTargetPackageTool.ToolName)]
	[AllureName("get-target-package resolves the environment's current package")]
	[Description("Calls get-target-package with no package on a reachable environment and verifies it resolves the package the CurrentPackageId setting names, which the platform stores as a package UId.")]
	public async Task GetTargetPackage_Should_Resolve_The_CurrentPackageId_Package_When_No_Package_Is_Named() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(5));
		McpE2ESettings settings = TestConfiguration.Load();
		string environmentName = await ReachableSandboxEnvironment.ResolveOrIgnoreAsync(
			settings,
			$"get-target-package MCP E2E requires a reachable environment. Configured sandbox environment '{settings.Sandbox.EnvironmentName}' was not reachable, and fallback environment '{ReachableSandboxEnvironment.FallbackEnvironmentName}' was also unavailable.");

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			GetTargetPackageTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName
				}
			},
			context.CancellationTokenSource.Token);
		GetTargetPackageResponse result =
			EntitySchemaStructuredResultParser.Extract<GetTargetPackageResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a resolution is a structured tool result, never an MCP protocol error");
		if (!result.Success && result.Error?.Contains("is locked", StringComparison.Ordinal) == true) {
			Assert.Ignore($"The stand's current package is locked, which this test does not cover: {result.Error}");
		}
		result.Success.Should().BeTrue(
			because: $"a stand ships with CurrentPackageId naming its editable Custom package by UId, so the no-package path must resolve it. Error: {result.Error}");
		result.PackageName.Should().NotBeNullOrWhiteSpace(
			because: "the caller passes the resolved name on to the write, and states it to the user");
	}
}
