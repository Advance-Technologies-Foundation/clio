using System.Text.Json;
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
/// End-to-end tests for the get-page-hierarchy MCP tool (ENG-93727). The tool is long-tail (not in
/// tools/list): it is discovered via get-tool-contract and invoked through clio-run. The sandbox test
/// exercises the real MCP server + a live Creatio to prove the ordered replacing-schema chain is
/// returned with bodies in one round-trip.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(PageHierarchyGetTool.ToolName)]
[NonParallelizable]
public sealed class PageHierarchyGetToolE2ETests : McpContractFixtureBase {

	private const string ToolName = PageHierarchyGetTool.ToolName;
	private const string ApplicationCode = "AutoTestClioMcp";

	[Test]
	[Category("McpE2E.NoEnvironment")]
	[Category("E2E")]
	[Description("get-page-hierarchy is discoverable via get-tool-contract as a non-resident (clio-run) tool with the expected input contract (ENG-93727).")]
	[AllureTag(ToolName)]
	[AllureName("get-page-hierarchy is discoverable through get-tool-contract")]
	public async Task GetPageHierarchy_Should_Be_Discoverable_Through_ToolContract() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act — compact index (no tool-names) then the full contract for get-page-hierarchy.
		ToolContractGetResponse index = await CallToolContractAsync(
			context.Session, context.CancellationTokenSource.Token, new Dictionary<string, object?>());
		ToolContractGetResponse contract = await CallToolContractAsync(
			context.Session, context.CancellationTokenSource.Token,
			new Dictionary<string, object?> { ["tool-names"] = new[] { ToolName } });

		// Assert — index lists it as a non-resident, non-destructive tool.
		index.Success.Should().BeTrue(because: "the compact index must resolve");
		ToolContractIndexEntry entry = index.Index!.Single(e => e.Name == ToolName);
		entry.Resident.Should().BeFalse(
			because: "get-page-hierarchy is a long-tail tool reached via clio-run, not present in tools/list");
		entry.Destructive.Should().NotBe(true,
			because: "reading a schema chain never mutates data");

		// Assert — full contract exposes the documented input shape.
		contract.Success.Should().BeTrue(because: "the tool has a reachable contract");
		ToolContractDefinition definition = contract.Tools!.Single(t => t.Name == ToolName);
		definition.InputSchema.Required.Should().Contain("schema-name",
			because: "schema-name is the one required argument");
		definition.InputSchema.Properties.Select(p => p.Name).Should().Contain(
			new[] { "schema-name", "metadata-only", "offset", "limit" },
			because: "the paging + metadata-only levers must be advertised so callers can bound large chains");
	}

	[Test]
	[Category("McpE2E.Sandbox")]
	[Category("E2E")]
	[Description("get-page-hierarchy (via clio-run) returns the full replacing-schema chain ordered root-first with each schema's raw body in one call for a seeded page (ENG-93727).")]
	[AllureTag(ToolName)]
	[AllureName("get-page-hierarchy returns the ordered chain with bodies in one round-trip")]
	[AllureDescription("Resolves a seeded page in AutoTestClioMcp on a reachable environment, invokes get-page-hierarchy through clio-run, and verifies the response is a hierarchy-level-ordered chain (root first) where each entry carries its own body length and the effective schema is present.")]
	public async Task GetPageHierarchy_Should_Return_Ordered_Chain_With_Bodies() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(3));
		string environmentName = await ResolveReachableEnvironmentOrIgnoreAsync(settings);
		string schemaName = await ResolveSeededPageSchemaOrIgnoreAsync(Session, cts.Token, environmentName);

		// Act — long-tail tool: dispatch through clio-run.
		CallToolResult callResult = await Session.CallToolAsync(
			ClioRunTool.ToolName,
			new Dictionary<string, object?> {
				["command"] = ToolName,
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = schemaName,
					["environment-name"] = environmentName
				}
			},
			cts.Token);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: $"get-page-hierarchy should return a structured payload for the seeded page '{schemaName}' instead of a transport error");
		GetPageHierarchyResponse response = ExtractHierarchy(callResult);
		response.Success.Should().BeTrue(
			because: $"get-page-hierarchy must resolve the chain for seeded page '{schemaName}'. Error: {response.Error}");
		response.Schemas.Should().NotBeNullOrEmpty(
			because: "a resolvable page has at least its own schema in the chain");
		response.TotalCount.Should().Be(response.Schemas.Count,
			because: "with no paging window the whole chain is returned");
		response.Schemas.Select(s => s.HierarchyLevel).Should().BeInAscendingOrder(
			because: "entries must be ordered by hierarchy level, root first — the order the deterministic merge consumes");
		response.Schemas[0].HierarchyLevel.Should().Be(0,
			because: "the first entry is the root (base) schema at level 0");
		response.RootSchemaName.Should().Be(response.Schemas[0].SchemaName,
			because: "the reported root name matches the first (level 0) entry");
		response.Schemas.Should().Contain(s => s.HasBody && s.BodyLength > 0,
			because: "at least one schema in a real page chain carries an editable body");
		response.Schemas.Where(s => s.HasBody).Should().OnlyContain(s => !string.IsNullOrEmpty(s.Body),
			because: "the default (non-metadata-only) response inlines each body-bearing schema's raw body");
	}

	private static async Task<ToolContractGetResponse> CallToolContractAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		IReadOnlyDictionary<string, object?> arguments) {
		CallToolResult callResult = await session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?> { ["args"] = arguments },
			cancellationToken);
		return EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(callResult);
	}

	// clio-run passes the target tool's structured result through; parse it leniently from either the
	// structured content or the serialized content blocks so the assertion is decoupled from the
	// clio-run envelope shape.
	private static GetPageHierarchyResponse ExtractHierarchy(CallToolResult callResult) {
		try {
			return EntitySchemaStructuredResultParser.Extract<GetPageHierarchyResponse>(callResult);
		} catch (InvalidOperationException) {
			string json = JsonSerializer.Serialize(callResult.StructuredContent);
			return JsonSerializer.Deserialize<GetPageHierarchyResponse>(json)
				?? new GetPageHierarchyResponse { Success = false, Error = "could not parse get-page-hierarchy response" };
		}
	}

	private async Task<string> ResolveReachableEnvironmentOrIgnoreAsync(McpE2ESettings settings) {
		string? configured = settings.Sandbox.EnvironmentName;
		if (!string.IsNullOrWhiteSpace(configured) && await CanReachEnvironmentAsync(settings, configured)) {
			return configured;
		}
		const string fallback = "d2";
		if (await CanReachEnvironmentAsync(settings, fallback)) {
			return fallback;
		}
		Assert.Ignore(
			$"get-page-hierarchy E2E requires a reachable environment. Configured sandbox '{configured}' and fallback '{fallback}' were both unavailable.");
		return string.Empty;
	}

	private static async Task<bool> CanReachEnvironmentAsync(McpE2ESettings settings, string environmentName) {
		using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
		try {
			ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
				settings, ["ping-app", "-e", environmentName], cancellationToken: cts.Token);
			return result.ExitCode == 0;
		} catch (OperationCanceledException) {
			return false;
		}
	}

	private static async Task<string> ResolveSeededPageSchemaOrIgnoreAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		string environmentName) {
		ApplicationListItemEnvelope installedApplication = await SeededApplicationResolver.ResolveOrIgnoreAsync(
			session, cancellationToken, environmentName, ApplicationCode);
		CallToolResult listResult = await session.CallToolAsync(
			PageListTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["code"] = installedApplication.Code
				}
			},
			cancellationToken);
		PageListResponse pageList = EntitySchemaStructuredResultParser.Extract<PageListResponse>(listResult);
		pageList.Success.Should().BeTrue(
			because: $"list-pages must succeed before a seeded page can be resolved. Error: {pageList.Error}");
		string? schemaName = pageList.Pages?.FirstOrDefault()?.SchemaName;
		if (string.IsNullOrWhiteSpace(schemaName)) {
			Assert.Ignore(
				$"Seeded application '{installedApplication.Code}' has no Freedom UI pages on '{environmentName}'.");
		}
		return schemaName!;
	}
}
