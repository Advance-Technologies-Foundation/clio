using System.Text.Json;
using System.Text.RegularExpressions;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the get-page MCP tool.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("get-page")]
[NonParallelizable]
public sealed class PageGetToolE2ETests {
	private const string ToolName = PageGetTool.ToolName;

	[Test]
	[Description("Advertises get-page MCP tool in the server tool list so callers can discover it.")]
	[AllureTag(ToolName)]
	[AllureName("get-page tool is advertised by the MCP server")]
	[AllureDescription("Verifies that get-page appears in the MCP server tool manifest.")]
	public async Task PageGetTool_Should_Be_Listed_By_MCP_Server() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);
		IEnumerable<string> toolNames = tools.Select(tool => tool.Name);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: "get-page must be advertised so MCP callers can discover the bundle reader");
	}

	[Test]
	[Description("Returns page metadata, merged bundle, raw body, and supports dry-run update-page roundtrip for a real sandbox page.")]
	[AllureTag(ToolName)]
	[AllureName("get-page returns bundle and raw body for a sandbox form page")]
	[AllureDescription("Discovers a Freedom UI form page through list-pages, reads it with get-page, asserts bundle and raw content, then passes raw.body into update-page dry-run.")]
	public async Task PageGetTool_Should_Return_Bundle_And_Support_DryRun_RoundTrip() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string? environmentName = settings.Sandbox.EnvironmentName;
		if (string.IsNullOrWhiteSpace(environmentName)) {
			Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName to run get-page success E2E.");
		}

		if (!await CanReachEnvironmentAsync(settings, environmentName!)) {
			Assert.Ignore($"get-page success E2E requires a reachable sandbox environment. '{environmentName}' was not reachable.");
		}

		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(5));
		PageListResponse pageListResponse = await CallPageListAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			environmentName!,
			searchPattern: "FormPage",
			limit: 20);
		if (!pageListResponse.Success || pageListResponse.Pages is null || pageListResponse.Pages.Count == 0) {
			Assert.Ignore($"get-page success E2E requires at least one discoverable Freedom UI form page in '{environmentName}'.");
		}
		pageListResponse.Pages.Should().Contain(page => !string.IsNullOrWhiteSpace(page.ParentSchemaName),
			because: "list-pages should now expose parent schema context so callers can choose a page before get-page");

		PageGetSuccessCandidate? candidate = await FindCandidateWithBundleAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			environmentName!,
			pageListResponse.Pages);
		if (candidate is null) {
			Assert.Ignore($"get-page success E2E requires at least one Freedom UI page with non-empty bundle.viewConfig in '{environmentName}'.");
		}

		// Act
		PageUpdateResponse roundTripResponse = await CallPageUpdateAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			environmentName!,
			candidate.Response.Page.SchemaName,
			candidate.Response.Raw.Body,
			dryRun: true);

		// Assert
		candidate.Response.Success.Should().BeTrue(
			because: "get-page should succeed for a real sandbox page");
		candidate.Response.Page.Should().NotBeNull(
			because: "get-page should return nested page metadata");
		candidate.Response.Bundle.Should().NotBeNull(
			because: "get-page should return the merged bundle block");
		candidate.Response.Bundle.ViewConfig.Should().NotBeNull(
			because: "the selected sandbox page should expose the merged layout array");
		candidate.Response.Bundle!.ViewConfig.Count.Should().BeGreaterThan(0,
			because: "the selected sandbox page should expose a non-empty inherited layout");
		candidate.Response.Raw.Should().NotBeNull(
			because: "get-page should return the raw editable payload");
		candidate.Response.Raw.Body.Should().NotBeNullOrWhiteSpace(
			because: "raw.body should be present for update-page round-trips");
		roundTripResponse.Success.Should().BeTrue(
			because: "update-page dry-run should accept raw.body returned by get-page");
		roundTripResponse.DryRun.Should().BeTrue(
			because: "the round-trip regression must stay non-destructive");
	}

	[Test]
	[Description("Reports readable failures when get-page is called with an invalid environment name.")]
	[AllureTag(ToolName)]
	[AllureName("get-page reports invalid environment failures")]
	[AllureDescription("Starts the real clio MCP server, invokes get-page with an unknown environment name, and verifies that the failure remains human-readable.")]
	public async Task PageGetTool_Should_Report_Invalid_Environment_Failure() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-get-page-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = "UsrMissing_FormPage",
					["environment-name"] = invalidEnvironmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		bool structuredFailure = TryExtractFailure(callResult, out PageGetResponse? response)
			&& response is not null
			&& !response.Success;
		string serializedCallResult = JsonSerializer.Serialize(new {
			callResult.IsError,
			StructuredContent = callResult.StructuredContent,
			Content = callResult.Content
		});

		// Assert
		(callResult.IsError == true || structuredFailure).Should().BeTrue(
			because: "get-page should fail when the requested environment does not exist");
		serializedCallResult.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found|error occurred invoking)",
			because: "the failure should explain that the requested environment is missing");
	}

	[Test]
	[Description("Rejects malformed resources JSON when update-page is invoked through the MCP server.")]
	[AllureFeature(PageUpdateTool.ToolName)]
	[AllureTag(PageUpdateTool.ToolName)]
	[AllureName("update-page rejects malformed resources JSON in dry-run mode")]
	[AllureDescription("Discovers a real sandbox page, reuses its raw body in update-page dry-run mode, and verifies that malformed resources JSON is rejected with a readable validation error.")]
	public async Task PageUpdateTool_Should_Reject_Invalid_Resources_Json() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string? environmentName = settings.Sandbox.EnvironmentName;
		if (string.IsNullOrWhiteSpace(environmentName)) {
			Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName to run update-page invalid resources E2E.");
		}
		if (!await CanReachEnvironmentAsync(settings, environmentName!)) {
			Assert.Ignore($"update-page invalid resources E2E requires a reachable sandbox environment. '{environmentName}' was not reachable.");
		}
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(5));
		PageListResponse pageListResponse = await CallPageListAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			environmentName!,
			searchPattern: "FormPage",
			limit: 20);
		if (!pageListResponse.Success || pageListResponse.Pages is null || pageListResponse.Pages.Count == 0) {
			Assert.Ignore($"update-page invalid resources E2E requires at least one discoverable Freedom UI form page in '{environmentName}'.");
		}
		PageGetSuccessCandidate? candidate = await FindCandidateWithBundleAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			environmentName!,
			pageListResponse.Pages);
		if (candidate is null) {
			Assert.Ignore($"update-page invalid resources E2E requires at least one Freedom UI page with non-empty bundle.viewConfig in '{environmentName}'.");
		}
		PageUpdateResponse response = await CallPageUpdateAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			environmentName!,
			candidate.Response.Page.SchemaName,
			candidate.Response.Raw.Body,
			dryRun: true,
			resources: "{\"UsrTitle\":");
		response.Success.Should().BeFalse(
			because: "malformed resources JSON should be rejected before update-page continues");
		response.Error.Should().Contain("resources must be a valid JSON object string",
			because: "the MCP tool should return a human-readable validation error for malformed resources");
	}

	[Test]
	[Description("Rejects legacy list-pages selector aliases before the server attempts unscoped page discovery.")]
	[AllureFeature(PageListTool.ToolName)]
	[AllureTag(PageListTool.ToolName)]
	[AllureName("list-pages rejects legacy app-code alias")]
	[AllureDescription("Starts the real clio MCP server, invokes list-pages with the deprecated app-code selector, and verifies that the tool returns a readable alias error instead of running an unscoped query.")]
	public async Task PageListTool_Should_Reject_Legacy_AppCode_Alias() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			PageListTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["app-code"] = "UsrTodoApp"
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		PageListResponse response = EntitySchemaStructuredResultParser.Extract<PageListResponse>(callResult);

		response.Success.Should().BeFalse(
			because: "legacy aliases should be rejected before list-pages can fall back to an unscoped query");
		response.Error.Should().Be("Use 'code' instead of 'app-code'.",
			because: "the MCP tool should direct callers to the canonical selector field");
	}

	private static async Task<ArrangeContext> ArrangeAsync(McpE2ESettings settings, TimeSpan timeout) {
		CancellationTokenSource cancellationTokenSource = new(timeout);
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(session, cancellationTokenSource);
	}

	private static async Task<bool> CanReachEnvironmentAsync(McpE2ESettings settings, string environmentName) {
		ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
			settings,
			["ping-app", "-e", environmentName]);
		return result.ExitCode == 0;
	}

	private static async Task<PageListResponse> CallPageListAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		string environmentName,
		string searchPattern,
		int limit) {
		CallToolResult callResult = await session.CallToolAsync(
			PageListTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["search-pattern"] = searchPattern,
					["limit"] = limit
				}
			},
			cancellationToken);
		return EntitySchemaStructuredResultParser.Extract<PageListResponse>(callResult);
	}

	private static async Task<PageGetSuccessCandidate?> FindCandidateWithBundleAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		string environmentName,
		IReadOnlyList<PageListItem> pages) {
		foreach (PageListItem page in pages.Where(item => !string.IsNullOrWhiteSpace(item.SchemaName))) {
			PageGetResponse response = await CallPageGetAsync(session, cancellationToken, environmentName, page.SchemaName);
			if (!response.Success || response.Bundle?.ViewConfig is null || response.Bundle.ViewConfig.Count == 0) {
				continue;
			}

			return new PageGetSuccessCandidate(page.SchemaName, response);
		}

		return null;
	}

	private static async Task<PageGetResponse> CallPageGetAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		string environmentName,
		string schemaName) {
		CallToolResult callResult = await session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = schemaName,
					["environment-name"] = environmentName
				}
			},
			cancellationToken);
		return EntitySchemaStructuredResultParser.Extract<PageGetResponse>(callResult);
	}

	private static async Task<PageUpdateResponse> CallPageUpdateAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		string environmentName,
		string schemaName,
		string body,
		bool dryRun,
		string? resources = null) {
		Dictionary<string, object?> args = new() {
			["schema-name"] = schemaName,
			["body"] = body,
			["dry-run"] = dryRun,
			["environment-name"] = environmentName
		};
		if (!string.IsNullOrWhiteSpace(resources)) {
			args["resources"] = resources;
		}
		CallToolResult callResult = await session.CallToolAsync(
			PageUpdateTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = args
			},
			cancellationToken);
		return EntitySchemaStructuredResultParser.Extract<PageUpdateResponse>(callResult);
	}

	private static bool TryExtractFailure(CallToolResult callResult, out PageGetResponse? response) {
		try {
			response = EntitySchemaStructuredResultParser.Extract<PageGetResponse>(callResult);
			return true;
		}
		catch (InvalidOperationException) {
			response = null;
			return false;
		}
	}

	private sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}

	private sealed record PageGetSuccessCandidate(
		string SchemaName,
		PageGetResponse Response);
}
