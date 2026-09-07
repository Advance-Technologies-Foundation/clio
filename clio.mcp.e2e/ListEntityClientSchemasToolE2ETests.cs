using Allure.NUnit;
using Allure.NUnit.Attributes;
using System;
using System.Linq;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the list-entity-client-schemas MCP tool (entity page-role graph).
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(ListEntityClientSchemasTool.ToolName)]
[NonParallelizable]
public sealed class ListEntityClientSchemasToolE2ETests : McpContractFixtureBase {

	[Test]
	[Description("Exposes list-entity-client-schemas as a discoverable, non-destructive tool via the get-tool-contract compact index on the lazy MCP surface.")]
	[AllureTag(ListEntityClientSchemasTool.ToolName)]
	[AllureName("list-entity-client-schemas MCP tool is discoverable on the lazy surface")]
	public async Task ListEntityClientSchemas_Should_Be_Discoverable_On_Lazy_Surface() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames = await arrangeContext.Session.ListReachableToolNamesAsync(
			arrangeContext.CancellationTokenSource.Token);
		IReadOnlyList<ToolContractIndexEntry> index = await arrangeContext.Session.GetToolContractIndexAsync(
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ListEntityClientSchemasTool.ToolName,
			because: $"the {ListEntityClientSchemasTool.ToolName} MCP tool must be discoverable on the lazy surface (get-tool-contract compact index)");
		ToolContractIndexEntry entry = index.Should()
			.ContainSingle(entry => entry.Name == ListEntityClientSchemasTool.ToolName,
				because: "the compact discovery index must carry exactly one entry for list-entity-client-schemas")
			.Which;
		entry.Destructive.Should().NotBe(true,
			because: "list-entity-client-schemas is a read-only tool and must not be flagged destructive in the discovery index");
	}

	[Test]
	[Description("Binds list-entity-client-schemas arguments through the real MCP server and returns a structured failure for an unknown environment.")]
	[AllureTag(ListEntityClientSchemasTool.ToolName)]
	[AllureName("list-entity-client-schemas MCP tool binds arguments")]
	public async Task ListEntityClientSchemas_Should_Bind_Arguments_And_Report_Invalid_Environment() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-unit-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ListEntityClientSchemasTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["entity-name"] = "Contract",
					["environment-name"] = invalidEnvironmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		ListEntityClientSchemasResponse response = EntitySchemaStructuredResultParser.Extract<ListEntityClientSchemasResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "valid list-entity-client-schemas payloads should bind and return a structured tool response");
		response.Success.Should().BeFalse(
			because: "an unknown registered environment should fail inside tool execution");
		response.Error.Should().Contain(invalidEnvironmentName,
			because: "the structured failure should identify the missing environment name");
	}

	[Test]
	[Category("McpE2E.Sandbox")]
	[Description("Against a live stand, returns a resolved typeColumnDisplayValue for each per-type edit page of a typed entity, exercising the GUID->caption join through the real MCP server (ENG-96553).")]
	[AllureTag(ListEntityClientSchemasTool.ToolName)]
	[AllureName("list-entity-client-schemas MCP tool resolves type display names on a typed entity")]
	public async Task ListEntityClientSchemas_Should_Resolve_Type_Display_Names_For_Typed_Entity_On_Stand() {
		// Arrange - needs a registered sandbox stand; skip gracefully when none is configured so the NoEnvironment
		// run stays green. The typed entity defaults to the OOTB Case (typed with per-type edit pages) and can be
		// overridden for a stand without it.
		McpE2ESettings settings = TestConfiguration.Load();
		if (string.IsNullOrWhiteSpace(settings.Sandbox.EnvironmentName)) {
			Assert.Ignore("Set McpE2E__Sandbox__EnvironmentName to a registered stand to run the typed-entity display-name check.");
		}
		string entityName = Environment.GetEnvironmentVariable("MCP_E2E_TYPED_ENTITY") ?? "Case";
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ListEntityClientSchemasTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["entity-name"] = entityName,
					["environment-name"] = settings.Sandbox.EnvironmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		ListEntityClientSchemasResponse response = EntitySchemaStructuredResultParser.Extract<ListEntityClientSchemasResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(because: "a typed entity on a reachable stand binds and returns a structured response");
		response.Success.Should().BeTrue(because: $"the typed entity '{entityName}' should resolve on the stand");
		response.EditPages.Should().NotBeNullOrEmpty(because: $"a typed entity like '{entityName}' registers per-type edit pages");
		bool anyTypeNameResolved = response.EditPages.Any(
			page => Guid.TryParse(page.TypeColumnValue, out _) && !string.IsNullOrWhiteSpace(page.TypeColumnDisplayValue));
		anyTypeNameResolved.Should().BeTrue(
			because: "the tool must resolve at least one per-type edit page's Type GUID to a display name through the real MCP server");
	}
}
