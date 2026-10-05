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
/// MCP stdio transport. The behavioural half needs a stand carrying the bundled CrtProcessBuilder and lives in
/// <c>ModifyBusinessProcessToolE2ETests</c> (the two <c>...SchemaRegistryLookup</c> /
/// <c>...DataElementsObject</c> tests).
/// <para>What is asserted here is the instruction surface, which ships with clio and can drift from the archive
/// on its own: the modify rule that names the exception to "a Lookup value is a bare record Guid", and the route
/// that sets or changes the object.</para>
/// </summary>
[TestFixture]
[AllureNUnit]
[Category("McpE2E.NoEnvironment")]
[Parallelizable(ParallelScope.Self)]
public sealed class SchemaReferenceLookupContractToolE2ETests : McpContractFixtureBase {

	private const string ModifyToolName = ModifyBusinessProcessTool.ModifyBusinessProcessToolName;

	[Test]
	[Description("modify-business-process advertises the schema-registry exception to the Lookup-value rule: Add/Delete data EntitySchemaId and Modify data EntitySchemaUId hold the schema UId, never a row Id, and the object itself is set or changed only with setElement. Without it the 'bare record Guid' rule beside it reads as 'pass the view's row Id' - the value that used to fail at run time.")]
	[AllureFeature(ModifyToolName)]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process advertises the schema-registry Lookup rule")]
	public async Task ModifyBusinessProcess_Should_AdvertiseTheSchemaRegistryLookupRule() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		string description = await AdvertisedDescriptionAsync(context, ModifyToolName);

		// Assert
		description.Should().Contain("a schema-registry Lookup (Add/Delete data EntitySchemaId",
			because: "naming the parameters is how a caller recognises the case before it writes one");
		description.Should().Contain("holds the schema UId, never a row Id",
			because: "both halves are the contract: the UId describe reports is the value, and the view's row Id "
				+ "is not");
		description.Should().Contain("only setElement sets or changes that object",
			because: "addMapping refuses to set or change the object, so the contract has to name the route that does");
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
