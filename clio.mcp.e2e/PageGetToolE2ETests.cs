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
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature("get-page")]
[NonParallelizable]
public sealed class PageGetToolE2ETests : McpContractFixtureBase {
	private const string ToolName = PageGetTool.ToolName;
	private const string ApplicationCode = "AutoTestClioMcp";
	private const string SavePage = "ClioMcp_BlankPageToSave";

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
	[Description("Reads the seeded Freedom UI page ClioMcp_BlankPageToSave via get-page and verifies update-page dry-run accepts the same body unchanged.")]
	[AllureTag(ToolName)]
	[AllureName("get-page raw body round-trips through update-page dry-run")]
	[AllureDescription("Uses the real clio MCP server to call get-page for the seeded page ClioMcp_BlankPageToSave in AutoTestClioMcp, reads the materialized body from disk, sends it back through update-page in dry-run mode, and verifies that the dry-run accepts the unchanged body so the read and write formats stay in sync.")]
	public async Task PageGetTool_Should_Return_Bundle_And_Support_DryRun_RoundTrip() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act 1: get-page reads the seeded page's body to disk
		CallToolResult getResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = SavePage,
					["environment-name"] = arrangeContext.EnvironmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		PageGetResponse getResponse = EntitySchemaStructuredResultParser.Extract<PageGetResponse>(getResult);

		// Assert get-page succeeded and materialized a non-empty body
		getResult.IsError.Should().NotBeTrue(
			because: $"get-page should return a structured payload for the seeded page '{SavePage}' before the round-trip can run");
		getResponse.Success.Should().BeTrue(
			because: $"get-page must succeed for the seeded page '{SavePage}' in '{ApplicationCode}'. Error: {getResponse.Error}");
		getResponse.Files.Should().NotBeNull(
			because: "successful get-page calls must return the materialized file paths");
		getResponse.Files.BodyFile.Should().NotBeNullOrWhiteSpace(
			because: "the round-trip needs a body-file path to read the raw body from disk");
		File.Exists(getResponse.Files.BodyFile).Should().BeTrue(
			because: "get-page must materialize body.js on disk for update-page reuse");
		string body = await File.ReadAllTextAsync(getResponse.Files.BodyFile);
		body.Should().NotBeNullOrWhiteSpace(
			because: "the round-trip is only meaningful if get-page returns a non-empty body");

		// Act 2: update-page dry-run consumes the same body unchanged
		CallToolResult updateResult = await arrangeContext.Session.CallToolAsync(
			PageUpdateTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = SavePage,
					["body"] = body,
					["dry-run"] = true,
					["environment-name"] = arrangeContext.EnvironmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		PageUpdateResponse updateResponse = EntitySchemaStructuredResultParser.Extract<PageUpdateResponse>(updateResult);

		// Assert update-page dry-run accepted the unchanged body
		updateResult.IsError.Should().NotBeTrue(
			because: "the round-trip body should produce a structured update-page response instead of a transport-level error");
		updateResponse.Success.Should().BeTrue(
			because: $"update-page dry-run must accept the body get-page produced for '{SavePage}', confirming get-page output is valid update-page input. Error: {updateResponse.Error}");
		updateResponse.Error.Should().BeNullOrWhiteSpace(
			because: "a successful dry-run should not include an error payload");
	}

	[Test]
	[Description("Starts the real clio MCP server, resolves the seeded installed application AutoTestClioMcp and one of its pages, and verifies the structured get-page metadata contract for that page.")]
	[AllureTag(ToolName)]
	[AllureName("get-page returns stable metadata contract for a real page")]
	[AllureDescription("Uses the real clio MCP server to look up the seeded installed application AutoTestClioMcp and the first page in that application, and verifies the stable get-page metadata and raw-body contract for the seeded page.")]
	public async Task PageGetTool_Should_Return_Stable_Metadata_Contract_For_Real_Page() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));
		PageDiscoveryCandidate candidate = await ResolveSeededPageCandidateOrIgnoreAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			arrangeContext.EnvironmentName,
			ApplicationCode);

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
			because: "the seeded page should return a structured get-page payload instead of a transport-level error");
		response.Success.Should().BeTrue(
			because: "get-page should succeed for a seeded page in the seeded installed application");
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
		response.Error.Should().Be("Rename: 'app-code' -> 'code'",
			because: "the MCP tool should direct callers to the canonical selector field using the centralized alias-rename format");
	}

	[Test]
	[Description("Starts the real clio MCP server, resolves the seeded installed application AutoTestClioMcp, and verifies that list-pages returns structured page summaries for that application.")]
	[AllureFeature(PageListTool.ToolName)]
	[AllureTag(PageListTool.ToolName)]
	[AllureName("list-pages returns structured page summaries")]
	[AllureDescription("Uses the real clio MCP server to look up the seeded installed application AutoTestClioMcp and verifies the structured list-pages summary envelope for that application.")]
	public async Task PageListTool_Should_Return_Structured_PageSummaries() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));
		ApplicationListItemEnvelope installedApplication = await SeededApplicationResolver.ResolveOrIgnoreAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			arrangeContext.EnvironmentName,
			ApplicationCode);

		// Act
		PageListResponse response = await CallPageListAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			arrangeContext.EnvironmentName,
			installedApplication.Code);

		// Assert
		response.Success.Should().BeTrue(
			because: $"list-pages should succeed for the seeded installed application. Error: {response.Error}");
		response.Pages.Should().NotBeNullOrEmpty(
			because: "the seeded application must expose at least one Freedom UI page so list-pages has something meaningful to verify");
		response.Count.Should().Be(response.Pages.Count,
			because: "list-pages should keep the explicit count aligned with the returned page collection");
		response.Pages.Should().OnlyContain(page =>
				!string.IsNullOrWhiteSpace(page.SchemaName)
				&& !string.IsNullOrWhiteSpace(page.PackageName),
			because: "every returned page summary should expose stable schema and package selectors");
	}

	private async Task<ArrangeContext> ArrangeAsync(McpE2ESettings settings, TimeSpan timeout) {
		CancellationTokenSource cancellationTokenSource = new(timeout);
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		McpServerSession session = Session;
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

	private static async Task<PageDiscoveryCandidate> ResolveSeededPageCandidateOrIgnoreAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		string environmentName,
		string applicationCode) {
		ApplicationListItemEnvelope installedApplication = await SeededApplicationResolver.ResolveOrIgnoreAsync(
			session,
			cancellationToken,
			environmentName,
			applicationCode);
		PageListResponse pageList = await CallPageListAsync(
			session,
			cancellationToken,
			environmentName,
			installedApplication.Code);
		pageList.Success.Should().BeTrue(
			because: $"list-pages must succeed before a seeded page can be resolved; treating an MCP-level failure as 'no pages' would hide real runtime regressions. Error: {pageList.Error}");
		PageListItem? seededPage = pageList.Pages?.FirstOrDefault();
		if (seededPage is not null) {
			return new PageDiscoveryCandidate(installedApplication, seededPage);
		}

		Assert.Ignore(
			$"Seeded application '{installedApplication.Code}' has no Freedom UI pages on environment '{environmentName}'. Add at least one page to the seed application.");
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

	private new sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource,
		string EnvironmentName) : IAsyncDisposable {
		public ValueTask DisposeAsync() {
			CancellationTokenSource.Dispose();
			return ValueTask.CompletedTask;
		}
	}

	private sealed record PageDiscoveryCandidate(
		ApplicationListItemEnvelope Application,
		PageListItem Page);

}
