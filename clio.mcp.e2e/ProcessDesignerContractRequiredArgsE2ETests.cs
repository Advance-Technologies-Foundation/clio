using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio.Mcp.E2E;

/// <summary>
/// Stand-free end-to-end guard on what the REAL clio MCP server advertises as required for the
/// process-designer tools. The three tools below identify their target with one of several alternative
/// arguments, so none of those alternatives may appear in the advertised <c>required</c> list.
/// </summary>
/// <remarks>
/// These tools are non-resident, so their schema never appears in <c>tools/list</c>: the list a strict client
/// or an agent actually reads is <c>get-tool-contract</c>'s <c>input-schema.required</c>, which is derived
/// from the registered tool's emitted schema. That derivation is what regressed once already — the record
/// declared both identities as non-nullable positional parameters, so both were advertised as required while
/// the tool itself refused a payload carrying both, leaving no sendable call at all.
/// <para>
/// Not in CI: process-designer fixtures carry <see cref="ProcessDesignerE2EGate.CategoryName"/> and CI lanes
/// exclude that category, because the feature ships with the CrtProcessBuilder package rather than with the
/// default stand. The in-CI guard over the same contract is
/// <c>clio.tests/Command/McpServer/ProcessDesignerEmittedSchemaTests.cs</c>; this fixture proves the same
/// facts survive the real server process and the real serializer.
/// </para>
/// </remarks>
[TestFixture]
[AllureNUnit]
[AllureFeature("process-designer")]
[Category(ProcessDesignerE2EGate.CategoryName)]
[Parallelizable(ParallelScope.Self)]
public sealed class ProcessDesignerContractRequiredArgsE2ETests : McpContractFixtureBase {

	private static readonly string[] DescribeIdentities = ["process-name", "process-uid", "process-caption"];

	[Test]
	[Description("Verifies the real clio MCP server does not advertise either mutually exclusive process identity of modify-business-process as required, while environment-name and operations stay required.")]
	[AllureTag(ModifyBusinessProcessTool.ModifyBusinessProcessToolName)]
	[AllureName("modify-business-process advertises neither process identity as required")]
	public async Task ModifyBusinessProcess_Should_NotAdvertiseEitherIdentityAsRequired() {
		// Arrange
		SkipIfProcessDesignerDisabled();
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyList<string> required = await RequiredArgumentsAsync(
			context, ModifyBusinessProcessTool.ModifyBusinessProcessToolName);

		// Assert
		required.Should().NotContain("process-name",
			because: "the tool refuses a payload that carries both identities, so advertising this one as " +
				"required leaves a caller no sendable combination at all");
		required.Should().NotContain("process-uid",
			because: "the same holds mirrored for the other accepted identity");
		required.Should().Contain("environment-name",
			because: "environment-name is genuinely mandatory - asserting it proves this required list is " +
				"populated rather than empty, which is what would make the two negatives above vacuous");
		required.Should().Contain("operations",
			because: "the operations array is the edit itself, so the tool cannot run without it");
	}

	[Test]
	[Description("Verifies the real clio MCP server advertises none of describe-business-process's three alternative identities, nor its optional culture, as required.")]
	[AllureTag(DescribeProcessTool.ToolName)]
	[AllureName("describe-business-process advertises no single identity as required")]
	public async Task DescribeBusinessProcess_Should_NotAdvertiseAnyIdentityAsRequired() {
		// Arrange
		SkipIfProcessDesignerDisabled();
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyList<string> required = await RequiredArgumentsAsync(context, DescribeProcessTool.ToolName);

		// Assert
		foreach (string identity in DescribeIdentities) {
			required.Should().NotContain(identity,
				because: $"'{identity}' is one of three accepted identities, so a caller sending exactly one of " +
					"the other two must not be rejected before the call reaches clio");
		}

		required.Should().NotContain("culture",
			because: "the tool defaults culture to en-US and its own description calls it optional");
		required.Should().Contain("environment-name",
			because: "environment-name stays mandatory, which keeps the negatives above non-vacuous");
	}

	[Test]
	[Description("Verifies the real clio MCP server does not advertise create-business-process's optional package-name override as required, while environment-name and descriptor stay required.")]
	[AllureTag(CreateBusinessProcessTool.CreateBusinessProcessToolName)]
	[AllureName("create-business-process advertises package-name as optional")]
	public async Task CreateBusinessProcess_Should_NotAdvertisePackageNameAsRequired() {
		// Arrange
		SkipIfProcessDesignerDisabled();
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyList<string> required = await RequiredArgumentsAsync(
			context, CreateBusinessProcessTool.CreateBusinessProcessToolName);

		// Assert
		required.Should().NotContain("package-name",
			because: "package-name only overrides the descriptor's own packageName, so a descriptor that names " +
				"its package is already a complete payload");
		required.Should().Contain("environment-name",
			because: "environment-name stays mandatory, which keeps the negative above non-vacuous");
		required.Should().Contain("descriptor",
			because: "the descriptor is the process definition, so the tool cannot run without it");
	}

	/// <summary>
	/// Skips when <c>process-designer</c> is off in the appsettings the server process loads. The gate resolves
	/// that file FROM <see cref="McpE2ESettings.ClioProcessPath" />, so the path has to be filled in exactly as
	/// the shared fixture fills it - a bare <c>TestConfiguration.Load()</c> would look in the wrong place, read
	/// no feature map, and fail closed into a permanent skip that looks like a passing suite.
	/// </summary>
	private static void SkipIfProcessDesignerDisabled() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		ProcessDesignerE2EGate.SkipIfFeatureDisabled(settings);
	}

	/// <summary>
	/// Reads one tool's advertised <c>input-schema.required</c> list from the live server through
	/// <c>get-tool-contract</c> - the surface a client sees for a non-resident tool.
	/// </summary>
	private static async Task<IReadOnlyList<string>> RequiredArgumentsAsync(
		ArrangeContext context,
		string toolName) {
		IReadOnlyCollection<string> reachable =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);
		reachable.Should().Contain(toolName,
			because: $"the {toolName} MCP tool must be discoverable on the lazy surface before its advertised " +
				"contract can be asserted");

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
		ToolContractDefinition contract = contracts.Tools!.Single(definition => definition.Name == toolName);
		contract.InputSchema.Should().NotBeNull(
			because: $"the advertised contract for {toolName} must carry an input schema");
		return contract.InputSchema.Required ?? [];
	}
}
