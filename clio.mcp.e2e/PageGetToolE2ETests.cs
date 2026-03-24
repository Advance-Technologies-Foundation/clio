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
/// End-to-end tests for the page-get MCP tool.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("page-get")]
[NonParallelizable]
public sealed class PageGetToolE2ETests {
	private const string ToolName = PageGetTool.ToolName;

	[Test]
	[Description("Advertises page-get MCP tool in the server tool list so callers can discover it.")]
	[AllureTag(ToolName)]
	[AllureName("page-get tool is advertised by the MCP server")]
	[AllureDescription("Verifies that page-get appears in the MCP server tool manifest.")]
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
			because: "page-get must be advertised so MCP callers can discover the bundle reader");
	}

	[Test]
	[Description("Returns page metadata, merged bundle, raw body, and supports dry-run page-update roundtrip for a real sandbox page.")]
	[AllureTag(ToolName)]
	[AllureName("page-get returns bundle and raw body for a sandbox form page")]
	[AllureDescription("Discovers a Freedom UI form page through page-list, reads it with page-get, asserts bundle and raw content, then passes raw.body into page-update dry-run.")]
	public async Task PageGetTool_Should_Return_Bundle_And_Support_DryRun_RoundTrip() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string? environmentName = settings.Sandbox.EnvironmentName;
		if (string.IsNullOrWhiteSpace(environmentName)) {
			Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName to run page-get success E2E.");
		}

		if (!await CanReachEnvironmentAsync(settings, environmentName!)) {
			Assert.Ignore($"page-get success E2E requires a reachable sandbox environment. '{environmentName}' was not reachable.");
		}

		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(5));
		PageListResponse pageListResponse = await CallPageListAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			environmentName!,
			searchPattern: "FormPage",
			limit: 20);
		if (!pageListResponse.Success || pageListResponse.Pages is null || pageListResponse.Pages.Count == 0) {
			Assert.Ignore($"page-get success E2E requires at least one discoverable Freedom UI form page in '{environmentName}'.");
		}

		PageGetSuccessCandidate? candidate = await FindCandidateWithBundleAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			environmentName!,
			pageListResponse.Pages);
		if (candidate is null) {
			Assert.Ignore($"page-get success E2E requires at least one Freedom UI page with non-empty bundle.viewConfig in '{environmentName}'.");
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
			because: "page-get should succeed for a real sandbox page");
		candidate.Response.Page.Should().NotBeNull(
			because: "page-get should return nested page metadata");
		candidate.Response.Bundle.Should().NotBeNull(
			because: "page-get should return the merged bundle block");
		candidate.Response.Bundle.ViewConfig.Should().NotBeNull(
			because: "the selected sandbox page should expose the merged layout array");
		candidate.Response.Bundle!.ViewConfig.Count.Should().BeGreaterThan(0,
			because: "the selected sandbox page should expose a non-empty inherited layout");
		candidate.Response.Raw.Should().NotBeNull(
			because: "page-get should return the raw editable payload");
		candidate.Response.Raw.Body.Should().NotBeNullOrWhiteSpace(
			because: "raw.body should be present for page-update round-trips");
		roundTripResponse.Success.Should().BeTrue(
			because: "page-update dry-run should accept raw.body returned by page-get");
		roundTripResponse.DryRun.Should().BeTrue(
			because: "the round-trip regression must stay non-destructive");
	}

	[Test]
	[Description("Reports readable failures when page-get is called with an invalid environment name.")]
	[AllureTag(ToolName)]
	[AllureName("page-get reports invalid environment failures")]
	[AllureDescription("Starts the real clio MCP server, invokes page-get with an unknown environment name, and verifies that the failure remains human-readable.")]
	public async Task PageGetTool_Should_Report_Invalid_Environment_Failure() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-page-get-env-{Guid.NewGuid():N}";

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
			because: "page-get should fail when the requested environment does not exist");
		serializedCallResult.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found|error occurred invoking)",
			because: "the failure should explain that the requested environment is missing");
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
		foreach (PageListItem page in pages.Where(item => !string.IsNullOrWhiteSpace(item.Name))) {
			PageGetResponse response = await CallPageGetAsync(session, cancellationToken, environmentName, page.Name);
			if (!response.Success || response.Bundle?.ViewConfig is null || response.Bundle.ViewConfig.Count == 0) {
				continue;
			}

			return new PageGetSuccessCandidate(page.Name, response);
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
		bool dryRun) {
		CallToolResult callResult = await session.CallToolAsync(
			PageUpdateTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = schemaName,
					["body"] = body,
					["dry-run"] = dryRun,
					["environment-name"] = environmentName
				}
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
