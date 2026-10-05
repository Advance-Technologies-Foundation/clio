using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio.Mcp.E2E;

/// <summary>
/// Stand-free end-to-end contract coverage for clio#1368 / clio#1300 - a Lookup on the schema registry holds the
/// schema UId, not a record id - read off the ADVERTISED contract through <c>get-tool-contract</c> over the real
/// MCP stdio transport. The behavioural half, which needs a stand carrying CrtProcessBuilder 1.6.6.64, is
/// <c>ModifyBusinessProcessToolE2ETests.ModifyBusinessProcess_Should_StoreTheSchemaUId_WhenAddMappingTargetsASchemaRegistryLookup</c>.
/// <para>What is asserted is the instruction surface the descriptions and the archive version separately: the
/// modify rule that names the exception to "a Lookup value is a bare record Guid", and the describe field that
/// is the only signal an element stored the old way carries.</para>
/// </summary>
[TestFixture]
[AllureNUnit]
[Category("McpE2E.NoEnvironment")]
[Parallelizable(ParallelScope.Self)]
public sealed class SchemaReferenceLookupContractToolE2ETests : McpContractFixtureBase {

	private const string ModifyToolName = ModifyBusinessProcessTool.ModifyBusinessProcessToolName;
	private const string DescribeToolName = DescribeProcessTool.ToolName;

	[Test]
	[Description("modify-business-process advertises the schema-registry exception to the Lookup-value rule: Add data EntitySchemaId and Modify data EntitySchemaUId hold the schema UId, and a registry row id is stored as that UId. Without it the 'bare record Guid' rule beside it reads as 'pass the view's row Id' - the value that used to fail at run time.")]
	[AllureFeature(ModifyToolName)]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process advertises the schema-registry Lookup rule")]
	public async Task ModifyBusinessProcess_Should_AdvertiseTheSchemaRegistryLookupRule() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		string description = await AdvertisedDescriptionAsync(context, ModifyToolName);

		// Assert
		description.Should().Contain("a Lookup on the schema registry (Add data EntitySchemaId",
			because: "naming the parameters is how a caller recognises the case before it writes one");
		description.Should().Contain("holds the schema UId, and a registry row id is stored as that UId",
			because: "both halves are the contract: the UId describe reports is accepted, and a row id is "
				+ "normalized rather than refused");
	}

	[Test]
	[Description("describe-business-process advertises objectWarning on the three data blocks and its two causes. An element whose object was stored as a schema-registry row id or a formula reads back with source null, exactly like an ordinary formula target, so the field is the only thing that tells a caller the designer shows it blank - and, for the row id, that it fails at run time.")]
	[AllureFeature(DescribeToolName)]
	[AllureTag(DescribeToolName)]
	[AllureName("describe-business-process advertises objectWarning on the data blocks")]
	public async Task DescribeBusinessProcess_Should_AdvertiseTheObjectWarning() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		string description = await AdvertisedDescriptionAsync(context, DescribeToolName);

		// Assert
		description.Should().Contain("(addData, changeData, deleteData) also carries objectWarning",
			because: "a caller has to know which blocks carry the field to look for it");
		description.Should().Contain("ItemNotFoundException",
			because: "a stored row id fails at run time, the half of the warning no designer view shows");
		description.Should().Contain("naming the setElement repair",
			because: "the warning is only useful if it says how to repair the element");
	}

	/// <summary>
	/// The tool's ADVERTISED description, fetched the way an agent would - the process-designer tools live on the
	/// lazy surface, so <c>get-tool-contract</c> is the route, as in <see cref="DeleteDataElementContractToolE2ETests"/>.
	/// </summary>
	private static async Task<string> AdvertisedDescriptionAsync(ArrangeContext context, string toolName) {
		IReadOnlyCollection<string> reachable =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);
		reachable.Should().Contain(toolName,
			because: $"the {toolName} MCP tool must be discoverable on the lazy surface before its advertised "
				+ "contract can be asserted");

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
		contract.Description.Should().NotBeNullOrWhiteSpace(
			because: $"the advertised contract for {toolName} must carry the description agents read");
		return contract.Description;
	}
}
