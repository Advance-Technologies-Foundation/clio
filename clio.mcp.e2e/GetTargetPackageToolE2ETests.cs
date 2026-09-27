using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
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
/// kebab-case field. The list-packages and create-business-process description checks live here, not in those
/// tools' own fixtures, because those fixtures are Sandbox / process-designer tier and CI's hermetic run
/// excludes them; the live no-package path is in <see cref="GetTargetPackageSandboxE2ETests"/>.
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

	[Test]
	[AllureTag(GetPkgListTool.GetPkgListToolName)]
	[AllureName("list-packages sends a write-target question to get-target-package")]
	[Description("Reads list-packages both ways an agent can - as tools/list advertises it and as get-tool-contract serves its curated contract - and verifies each says the list cannot tell whether a package accepts changes and names get-target-package for that question - agents otherwise guess a target package from package names, 2-7 calls per process build in the ENG-99970 runs.")]
	public async Task GetPkgList_Should_Route_The_Write_Target_Question_To_GetTargetPackage() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IList<McpClientTool> tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);
		string description = tools.Single(tool => tool.Name == GetPkgListTool.GetPkgListToolName).Description;
		CallToolResult contractResult = await context.Session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["tool-names"] = new[] { GetPkgListTool.GetPkgListToolName }
				}
			},
			context.CancellationTokenSource.Token);
		ToolContractGetResponse contracts =
			EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(contractResult);

		// Assert
		description.Should().Contain("does not say whether a package accepts changes",
			because: "the rows carry name, version, maintainer and uId only, so an agent that looks for a writable package there can only guess");
		description.Should().Contain(GetTargetPackageTool.ToolName,
			because: "the description must name the tool that answers the question, since list-packages is resident and get-target-package is not");
		contracts.Success.Should().BeTrue(
			because: $"the list-packages contract must be served before its description can be read. Error: {contracts.Error?.Message}");
		contracts.Tools!.Single(definition => definition.Name == GetPkgListTool.GetPkgListToolName).Description
			.Should().Contain(GetPkgListTool.WriteTargetNote,
				because: "the curated contract, not the attribute, is what get-tool-contract readers see, so the routing note must be in both");
	}

	[Test]
	[AllureTag(CreateBusinessProcessTool.CreateBusinessProcessToolName)]
	[AllureName("create-business-process names get-target-package for the descriptor's packageName")]
	[Description("Reads create-business-process through get-tool-contract with no detail, the way a lazy-surface caller does by default, and verifies the contract names get-target-package for packageName - also when the reply is fitted and the contract comes back in its short form, which keeps only the opening of the description.")]
	public async Task CreateBusinessProcess_Should_Name_GetTargetPackage_For_The_PackageName() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));
		const string toolName = CreateBusinessProcessTool.CreateBusinessProcessToolName;

		// Act
		CallToolResult contractResult = await context.Session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["tool-names"] = new[] { toolName }
				}
			},
			context.CancellationTokenSource.Token);
		ToolContractGetResponse contracts =
			EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(contractResult);

		// Assert
		contracts.Success.Should().BeTrue(
			because: $"the contract must be served before its description can be read. Error: {contracts.Error?.Message}");
		string description = contracts.Tools!.Single(definition => definition.Name == toolName).Description;
		description.Should().Contain("Take packageName from get-target-package.",
			because: "the server refuses a missing or read-only packageName, and a default read may return the short form, which keeps only the description's opening - the sentence must stand there to survive it");
	}
}
