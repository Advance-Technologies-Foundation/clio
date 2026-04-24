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
	[Description("Reads a discovered real page with get-page and reuses the written body.js in update-page dry-run without triggering proxy-binding validation errors.")]
	[AllureTag(ToolName)]
	[AllureName("get-page body.js supports update-page dry-run round-trip")]
	[AllureDescription("Uses the real clio MCP server to discover a page, reads its generated body.js, and verifies that update-page dry-run accepts that body as-is.")]
	public async Task PageGetTool_Should_Support_DryRun_RoundTrip_For_Discovered_Page() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));
		PageDiscoveryCandidate candidate = await ResolvePageCandidateOrIgnoreAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			arrangeContext.EnvironmentName);

		CallToolResult getCallResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = candidate.Page.SchemaName,
					["environment-name"] = arrangeContext.EnvironmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		PageGetResponse getResponse = EntitySchemaStructuredResultParser.Extract<PageGetResponse>(getCallResult);
		string body = await File.ReadAllTextAsync(getResponse.Files.BodyFile, arrangeContext.CancellationTokenSource.Token);

		CallToolResult updateCallResult = await arrangeContext.Session.CallToolAsync(
			PageUpdateTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = candidate.Page.SchemaName,
					["environment-name"] = arrangeContext.EnvironmentName,
					["body"] = body,
					["dry-run"] = true
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		PageUpdateResponse updateResponse = EntitySchemaStructuredResultParser.Extract<PageUpdateResponse>(updateCallResult);

		getResponse.Success.Should().BeTrue(
			because: "get-page should succeed for the discovered real page before the dry-run round-trip");
		updateCallResult.IsError.Should().NotBeTrue(
			because: "validation outcomes should stay in the structured update-page response");
		updateResponse.Success.Should().BeTrue(
			because: "body.js returned by get-page should already be normalized for reuse in update-page dry-run");
		updateResponse.Error.Should().BeNullOrWhiteSpace(
			because: "the round-trip should not surface proxy-binding validation errors");
	}

	[Test]
	[Description("Starts the real clio MCP server, discovers an installed application page when available, and verifies the structured get-page metadata contract for that page.")]
	[AllureTag(ToolName)]
	[AllureName("get-page returns stable metadata contract for a real page")]
	[AllureDescription("Uses the real clio MCP server to discover an installed application and one of its pages, ignores when no such data exists, and otherwise verifies the stable get-page metadata and raw-body contract for the discovered page.")]
	public async Task PageGetTool_Should_Return_Stable_Metadata_Contract_For_Real_Page() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));
		PageDiscoveryCandidate candidate = await ResolvePageCandidateOrIgnoreAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			arrangeContext.EnvironmentName);

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = candidate.Page.SchemaName,
					["environment-name"] = arrangeContext.EnvironmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		PageGetResponse response = EntitySchemaStructuredResultParser.Extract<PageGetResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a discovered page should return a structured get-page payload instead of a transport-level error");
		response.Success.Should().BeTrue(
			because: "get-page should succeed for a discovered page in a discovered installed application");
		response.Page.Should().NotBeNull(
			because: "successful get-page calls should include page metadata");
		response.Page.SchemaName.Should().Be(candidate.Page.SchemaName,
			because: "get-page metadata should identify the same page selected through list-pages");
		response.Page.PackageName.Should().Be(candidate.Page.PackageName,
			because: "get-page metadata should preserve the owning package from list-pages");
		response.Bundle.Should().BeNull(
			because: "the MCP wrapper compacts successful get-page responses to file paths instead of returning inline bundle data");
		response.Raw.Should().BeNull(
			because: "the MCP wrapper compacts successful get-page responses to file paths instead of returning inline raw body data");
		response.Files.Should().NotBeNull(
			because: "successful get-page calls should return the written file paths for MCP clients");
		response.Files.BodyFile.Should().EndWith("body.js",
			because: "get-page should write the editable page body to body.js");
		response.Files.BundleFile.Should().EndWith("bundle.json",
			because: "get-page should write the merged bundle to bundle.json");
		response.Files.MetaFile.Should().EndWith("meta.json",
			because: "get-page should write the page metadata to meta.json");
		File.Exists(response.Files.BodyFile).Should().BeTrue(
			because: "get-page should materialize the editable page body on disk for update-page reuse");
		File.Exists(response.Files.BundleFile).Should().BeTrue(
			because: "get-page should materialize the merged bundle on disk for inspection");
		File.Exists(response.Files.MetaFile).Should().BeTrue(
			because: "get-page should materialize the metadata payload on disk for inspection");
		(await File.ReadAllTextAsync(response.Files.BodyFile)).Should().NotBeNullOrWhiteSpace(
			because: "body.js should contain the raw editable page body");
		response.Error.Should().BeNullOrWhiteSpace(
			because: "successful get-page calls should not include an error payload");
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
	[Description("Rejects legacy list-pages selector aliasesbefore the server attempts unscoped page discovery.")]
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

	[Test]
	[Description("Starts the real clio MCP server, discovers an installed application when available, and verifies that list-pages returns structured page summaries for that application.")]
	[AllureFeature(PageListTool.ToolName)]
	[AllureTag(PageListTool.ToolName)]
	[AllureName("list-pages returns structured page summaries")]
	[AllureDescription("Uses the real clio MCP server to discover an installed application, ignores when no applications or pages exist, and otherwise verifies the structured list-pages summary envelope for the discovered application.")]
	public async Task PageListTool_Should_Return_Structured_PageSummaries() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));
		ApplicationListItemEnvelope installedApplication = await ResolveInstalledApplicationOrIgnoreAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			arrangeContext.EnvironmentName);

		// Act
		PageListResponse response = await CallPageListAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			arrangeContext.EnvironmentName,
			installedApplication.Code);
		if (response.Pages is null || response.Pages.Count == 0) {
			Assert.Ignore("TODO: ENG-88547 add predefined installed application/page data to the E2E environment.");
		}

		// Assert
		response.Success.Should().BeTrue(
			because: "list-pages should succeed for a discovered installed application");
		response.Pages.Should().NotBeNullOrEmpty(
			because: "this discovery-backed test should only continue when the discovered application exposes at least one page");
		response.Count.Should().Be(response.Pages.Count,
			because: "list-pages should keep the explicit count aligned with the returned page collection");
		response.Pages.Should().OnlyContain(page =>
				!string.IsNullOrWhiteSpace(page.SchemaName)
				&& !string.IsNullOrWhiteSpace(page.PackageName),
			because: "every returned page summary should expose stable schema and package selectors");
	}

	private static async Task<ArrangeContext> ArrangeAsync(McpE2ESettings settings, TimeSpan timeout) {
		CancellationTokenSource cancellationTokenSource = new(timeout);
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(session, cancellationTokenSource, environmentName);
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

	private static async Task<ApplicationListItemEnvelope> ResolveInstalledApplicationOrIgnoreAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		string environmentName) {
		CallToolResult callResult = await session.CallToolAsync(
			ApplicationGetListTool.ApplicationGetListToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName
				}
			},
			cancellationToken);
		ApplicationListResponseEnvelope response = ApplicationResultParser.ExtractList(callResult);
		ApplicationListItemEnvelope? installedApplication = response.Applications?.FirstOrDefault();
		if (installedApplication is not null) {
			return installedApplication;
		}

		Assert.Ignore("TODO: ENG-88547 add predefined installed application data to the E2E environment.");
		return null!;
	}

	private static async Task<PageDiscoveryCandidate> ResolvePageCandidateOrIgnoreAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		string environmentName) {
		ApplicationListItemEnvelope installedApplication = await ResolveInstalledApplicationOrIgnoreAsync(
			session,
			cancellationToken,
			environmentName);
		PageListResponse pageList = await CallPageListAsync(
			session,
			cancellationToken,
			environmentName,
			installedApplication.Code);
		PageListItem? discoveredPage = pageList.Pages.FirstOrDefault();
		if (discoveredPage is not null) {
			return new PageDiscoveryCandidate(installedApplication, discoveredPage);
		}

		Assert.Ignore("TODO: ENG-88547 add predefined installed application/page data to the E2E environment.");
		return null!;
	}

	private static async Task<PageListResponse> CallPageListAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		string environmentName,
		string applicationCode) {
		CallToolResult callResult = await session.CallToolAsync(
			PageListTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["code"] = applicationCode
				}
			},
			cancellationToken);
		return EntitySchemaStructuredResultParser.Extract<PageListResponse>(callResult);
	}

	private static async Task<string> ResolveReachableEnvironmentAsync(McpE2ESettings settings) {
		string? configuredEnvironmentName = settings.Sandbox.EnvironmentName;
		if (!string.IsNullOrWhiteSpace(configuredEnvironmentName) &&
			await CanReachEnvironmentAsync(settings, configuredEnvironmentName)) {
			return configuredEnvironmentName;
		}

		const string fallbackEnvironmentName = "d2";
		if (await CanReachEnvironmentAsync(settings, fallbackEnvironmentName)) {
			return fallbackEnvironmentName;
		}

		Assert.Ignore(
			$"page MCP E2E requires a reachable environment. Configured sandbox environment '{configuredEnvironmentName}' was not reachable, and fallback environment '{fallbackEnvironmentName}' was also unavailable.");
		return string.Empty;
	}

	private static async Task<bool> CanReachEnvironmentAsync(McpE2ESettings settings, string environmentName) {
		using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
		try {
			ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
				settings,
				["ping-app", "-e", environmentName],
				cancellationToken: cts.Token);
			return result.ExitCode == 0;
		} catch (OperationCanceledException) {
			return false;
		}
	}

	private sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource,
		string EnvironmentName) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}

	private sealed record PageDiscoveryCandidate(
		ApplicationListItemEnvelope Application,
		PageListItem Page);

}
