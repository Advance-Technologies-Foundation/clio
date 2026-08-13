using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.MobilePageConverter;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio.Mcp.E2E;

/// <summary>
/// Sandbox-tier end-to-end tests for the get-mobile-page-conversion-guide MCP tool. Unlike the
/// NoEnvironment fixture (discovery + graceful-failure only), these exercise the real happy path
/// against a stood-up Creatio: reading the source page AND the recommended mobile template's own
/// bundle, then feeding the template's native arrays / collection keys into the guide so the
/// modelConfig / viewModelConfig root merges are SPLIT into focused targeted merges (arrays unioned
/// with the template's natives). This is the only tier that drives
/// <see cref="WebToMobileAnalysisService.SplitRootMergeIntoTargetedMerges"/> /
/// <see cref="WebToMobileAnalysisService.SplitModelConfigRootMerge"/> and the
/// <c>LoadMobileTemplateProbe</c> template read through the real <c>clio mcp-server</c> process, so a
/// regression in that MCP surface (a crash in the template probe, or a diff that regresses to a single
/// root merge the mobile diff engine would array-replace) is caught here. Every test degrades to
/// <see cref="Assert.Ignore(string)"/> with an explicit reason when the feature flag, a reachable
/// environment, or a seeded page is missing — but a conversion failure on a seeded page always fails
/// the test: only missing preconditions may Ignore, never a runtime error.
/// </summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature(MobilePageConversionGuideTool.ToolName)]
[NonParallelizable]
public sealed class MobilePageConversionGuideSandboxE2ETests : McpContractFixtureBase {

	private const string ToolName = MobilePageConversionGuideTool.ToolName;
	private const string ApplicationCode = "AutoTestClioMcp";

	[Test]
	[Description("Converts a real seeded Freedom UI page through the real clio MCP server and verifies that the returned modelConfigDiff / viewModelConfigDiff are SPLIT into focused targeted merges (no path-[] root merge remains), which is the split/union behavior fed by the mobile template probe.")]
	[AllureTag(ToolName)]
	[AllureName("get-mobile-page-conversion-guide returns split-shaped data-section diffs for a real page")]
	[AllureDescription("Starts the real clio MCP server, resolves the seeded installed application AutoTestClioMcp and one of its pages, calls get-mobile-page-conversion-guide against a reachable environment, and asserts that every data-section diff is a set of targeted merges rather than a single path-[] root merge — exercising the LoadMobileTemplateProbe read and the SplitModelConfigRootMerge / SplitRootMergeIntoTargetedMerges wiring end to end.")]
	public async Task MobilePageConversionGuideTool_Should_Return_SplitShaped_Diffs_For_Real_Page() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));
		await RequireConverterFeatureOrIgnoreAsync(context);
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		string sourceSchemaName = await ResolveSeededPageSchemaNameOrIgnoreAsync(
			context.Session, context.CancellationTokenSource.Token, environmentName);

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = sourceSchemaName,
					["environment-name"] = environmentName
				}
			},
			context.CancellationTokenSource.Token);
		MobilePageConversionGuideResponse response =
			EntitySchemaStructuredResultParser.Extract<MobilePageConversionGuideResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "converting a seeded page should return a structured guide payload, not a transport-level error");
		response.Success.Should().BeTrue(
			because: $"get-mobile-page-conversion-guide should convert the seeded page '{sourceSchemaName}'. Error: {response.Error}");
		response.Guide.Should().NotBeNull(
			because: "a successful conversion must carry the guide inline so the caller can paste its diffs");
		AssertSplitShape(response.Guide!.ModelConfigDiff, "modelConfigDiff");
		AssertSplitShape(response.Guide!.ViewModelConfigDiff, "viewModelConfigDiff");
	}

	[Test]
	[Description("Converts a real seeded TABBED page through the real clio MCP server and verifies the mandatory tabAreaLayers contract end to end — the guide carries a tabAreaLayers group per converted tab; the tab body grid sits right after its tab with no webName; the Area follows it and holds ALL of the tab's top-level content (expansion panels included); and the constraints/nextSteps carry the MANDATORY (never offer to skip) wording.")]
	[AllureTag(ToolName)]
	[AllureName("get-mobile-page-conversion-guide returns the mandatory tabAreaLayers contract for a tabbed page")]
	[AllureDescription("Starts the real clio MCP server, resolves the seeded installed application AutoTestClioMcp, converts its pages until one yields tabAreaLayers, and asserts the full serialized contract: the tab body grid at tab+1 with no webName; the Area at tab+2 with movedChildren reparented onto it — exercising the bundled WebToMobilePageConversionRules.json tabAreaLayers section and the BuildTabAreaLayers pass through the real MCP surface.")]
	public async Task MobilePageConversionGuideTool_Should_Return_Mandatory_TabAreaLayers_For_Tabbed_Page() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(5));
		await RequireConverterFeatureOrIgnoreAsync(context);
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		IReadOnlyList<string> candidates = await ResolveSeededRecordPageCandidatesOrIgnoreAsync(
			context.Session, context.CancellationTokenSource.Token, environmentName);

		// Act — convert candidates (form pages first) until one synthesizes tab layers. A candidate that
		// fails to convert is a runtime regression, never a seed-data gap, so failures are collected and
		// fail the test outright instead of degrading to Ignore.
		MobilePageConversionGuide? guide = null;
		string convertedSchemaName = string.Empty;
		List<string> failedCandidates = [];
		foreach (string schemaName in candidates) {
			CallToolResult callResult = await context.Session.CallToolAsync(
				ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["schema-name"] = schemaName,
						["environment-name"] = environmentName
					}
				},
				context.CancellationTokenSource.Token);
			if (callResult.IsError == true) {
				failedCandidates.Add($"'{schemaName}': transport-level error");
				continue;
			}
			MobilePageConversionGuideResponse response =
				EntitySchemaStructuredResultParser.Extract<MobilePageConversionGuideResponse>(callResult);
			if (!response.Success) {
				failedCandidates.Add($"'{schemaName}': {response.Error}");
				continue;
			}
			if (response.Guide?.TabAreaLayers is { Count: > 0 }) {
				guide = response.Guide;
				convertedSchemaName = schemaName;
				break;
			}
		}
		if (guide is null) {
			if (failedCandidates.Count > 0) {
				Assert.Fail(
					$"{failedCandidates.Count} of {candidates.Count} seeded page(s) of '{ApplicationCode}' on environment "
					+ $"'{environmentName}' failed to convert; get-mobile-page-conversion-guide must succeed on every seeded "
					+ $"page, so this is a runtime regression, not missing seed data: {string.Join("; ", failedCandidates)}");
			}
			Assert.Ignore(
				$"All {candidates.Count} seeded page(s) of '{ApplicationCode}' on environment '{environmentName}' "
				+ "converted successfully, but none produced tabAreaLayers: the seed application has no Freedom UI page "
				+ "with a converter-created tab that has content. Add a tabbed record page to the seed application to "
				+ "exercise the tabAreaLayers surface.");
		}

		// Assert — the serialized guide honors the mandatory layer contract for EVERY converted tab:
		// the tab body grid always directly follows its tab; the Area follows the body and holds ALL of
		// the tab's top-level content (expansion panels included).
		foreach (TabAreaLayerGroup group in guide!.TabAreaLayers!) {
			int tabAt = IndexOfMobile(guide, group.TabName);
			tabAt.Should().BeGreaterThanOrEqualTo(0,
				because: $"the tab '{group.TabName}' the group describes must itself be in the element map of '{convertedSchemaName}'");

			ElementMapEntry mainEntry = guide.ElementMap[tabAt + 1];
			mainEntry.MobileName.Should().Be(group.MainTabContainerName,
				because: "the tab body grid must be the very next element-map entry after its tab, so applying inserts in order creates the parent first");
			mainEntry.Operation.Should().Be("insert",
				because: "a synthesized layer is applied exactly like any other insert");
			mainEntry.WebName.Should().BeNull(
				because: "a synthesized container has no web counterpart, so the serialized entry carries no webName");
			mainEntry.ParentName.Should().Be(group.TabName,
				because: "the tab body grid is the tab's direct child");

			if (group.AreaName is not null) {
				ElementMapEntry areaEntry = guide.ElementMap[tabAt + 2];
				areaEntry.MobileName.Should().Be(group.AreaName,
					because: "the Area card must directly follow the tab body it lives in");
				areaEntry.WebName.Should().BeNull(
					because: "a synthesized container has no web counterpart, so the serialized entry carries no webName");
				areaEntry.ParentName.Should().Be(group.MainTabContainerName,
					because: "the Area card sits inside the tab body grid, not in the tab");
			} else {
				group.MovedChildren.Should().BeEmpty(
					because: "with no Area synthesized there is nowhere for children to move (routing hints only)");
			}

			foreach (string movedChild in group.MovedChildren) {
				guide.ElementMap.Should().Contain(
					e => e.MobileName == movedChild && e.ParentName == group.AreaName,
					because: $"movedChildren are already reparented onto their own tab's Area ('{group.AreaName}'), never a sibling's");
			}
		}
		guide.Constraints.Should().Contain(c => c.Contains("tabAreaLayers is MANDATORY"),
			because: "the guide must forbid the caller from offering the two-layer body as a choice");
		guide.NextSteps.Should().Contain(s => s.Contains("tabAreaLayers") && s.Contains("MANDATORY"),
			because: "the ordered steps must tell the caller to state the structure as fact, not ask for approval");
	}

	[Test]
	[Description("Converts a real seeded RECORD page whose header carries a page-added button and verifies the mandatory fabConversion contract end to end — the guide carries fabConversion items; each one is a synthesized crt.MenuItem INSERT into the real mobile template's FAB menuItems (no webName, never a Scaffold floatAction merge, which the platform diff applier would silently drop); and the constraints/nextSteps carry the MANDATORY wording. This is the only tier that drives LoadMobileTemplateProbe -> TryExtractScaffoldFloatAction against a REAL mobile template bundle, the seam every unit test bypasses by injecting the floatAction directly.")]
	[AllureTag(ToolName)]
	[AllureName("get-mobile-page-conversion-guide returns the mandatory fabConversion contract for a page with header buttons")]
	[AllureDescription("Starts the real clio MCP server, resolves the seeded installed application AutoTestClioMcp, converts its record pages until one yields fabConversion, and asserts the serialized contract: one insert per converted item targeting the template FAB's menuItems with no webName, no Scaffold merge, and the MANDATORY constraint/next-step wording — exercising the bundled WebToMobilePageConversionRules.json fabConversion section, the real mobile-template FAB probe, and the BuildFabConversion pass through the real MCP surface.")]
	public async Task MobilePageConversionGuideTool_Should_Return_Mandatory_FabConversion_For_Page_With_Header_Button() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(5));
		await RequireConverterFeatureOrIgnoreAsync(context);
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		IReadOnlyList<string> candidates = await ResolveSeededRecordPageCandidatesOrIgnoreAsync(
			context.Session, context.CancellationTokenSource.Token, environmentName);

		// Act — convert candidates (record pages first) until one converts a header button into a FAB item.
		// A candidate that fails to convert is a runtime regression, never a seed-data gap, so failures are
		// collected and fail the test outright instead of degrading to Ignore.
		MobilePageConversionGuide? guide = null;
		string convertedSchemaName = string.Empty;
		List<string> failedCandidates = [];
		foreach (string schemaName in candidates) {
			CallToolResult callResult = await context.Session.CallToolAsync(
				ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["schema-name"] = schemaName,
						["environment-name"] = environmentName
					}
				},
				context.CancellationTokenSource.Token);
			if (callResult.IsError == true) {
				failedCandidates.Add($"'{schemaName}': transport-level error");
				continue;
			}
			MobilePageConversionGuideResponse response =
				EntitySchemaStructuredResultParser.Extract<MobilePageConversionGuideResponse>(callResult);
			if (!response.Success) {
				failedCandidates.Add($"'{schemaName}': {response.Error}");
				continue;
			}
			if (response.Guide?.FabConversion?.Items is { Count: > 0 }) {
				guide = response.Guide;
				convertedSchemaName = schemaName;
				break;
			}
		}
		if (guide is null) {
			if (failedCandidates.Count > 0) {
				Assert.Fail(
					$"{failedCandidates.Count} of {candidates.Count} seeded page(s) of '{ApplicationCode}' on environment "
					+ $"'{environmentName}' failed to convert; get-mobile-page-conversion-guide must succeed on every seeded "
					+ $"page, so this is a runtime regression, not missing seed data: {string.Join("; ", failedCandidates)}");
			}
			Assert.Ignore(
				$"All {candidates.Count} seeded page(s) of '{ApplicationCode}' on environment '{environmentName}' "
				+ "converted successfully, but none produced fabConversion items: the seed application has no Freedom UI "
				+ "record page whose header carries a PAGE-ADDED crt.Button with a mobile-supported clicked request. Add "
				+ "one to the seed application to exercise the fabConversion surface.");
		}

		// Assert — every reported item is backed by a synthesized insert into the template FAB's menuItems.
		// The merge shape is the failure this contract exists to prevent: when the mobile template already
		// owns floatAction, the platform diff applier silently drops a merged property and the converted
		// items never reach the compiled page.
		guide!.ElementMap.Should().NotContain(e => CarriesFloatAction(e),
			because: $"'{convertedSchemaName}' resolved a real mobile template, so its FAB must be reached by additive "
				+ "inserts — a floatAction merge is the shape the diff applier silently drops");
		string? fabParentName = null;
		foreach (FabMenuItemInfo item in guide.FabConversion!.Items) {
			ElementMapEntry insert = guide.ElementMap.Should().ContainSingle(
				e => e.MobileName == item.Name && e.Operation == "insert",
				because: $"the item '{item.Name}' reported for source button '{item.SourceButton}' must be backed by "
					+ "exactly one synthesized element-map entry — the advisory section alone applies nothing").Which;
			insert.WebName.Should().BeNull(
				because: "a synthesized menu item has no web counterpart, so the serialized entry carries no webName");
			insert.PropertyName.Should().Be("menuItems",
				because: "converted items are inserted into the FAB's menuItems property, not into a container's items");
			insert.ParentName.Should().NotBeNullOrWhiteSpace(
				because: "an insert needs the template FAB's own name as its target");
			insert.MobileValues.Should().NotBeNull(
				because: "the caller pastes mobileValues verbatim — an insert that lost them applies nothing");
			insert.MobileValues!.AsObject().Should().NotContainKey("name",
				because: "the insert operation carries the name itself; a duplicate inside values is noise");
			fabParentName ??= insert.ParentName;
			insert.ParentName.Should().Be(fabParentName,
				because: "every converted item targets the same template FAB");
		}
		guide.FabConversion.Note.Should().NotContain("could not be resolved",
			because: "the mobile template was read from a reachable environment, so its own FAB must have been "
				+ "resolved by the template probe rather than assumed — that probe seam is what this tier exists to cover");
		guide.Constraints.Should().Contain(c => c.Contains("fabConversion is MANDATORY"),
			because: "the guide must forbid the caller from treating the converted FAB items as a proposal");
		guide.NextSteps.Should().Contain(s => s.Contains("guide.fabConversion") && s.Contains("MANDATORY"),
			because: "the ordered steps must tell the caller to state the converted items as fact, not ask for approval");
	}

	/// <summary>True when an element-map entry is a merge whose values carry a <c>floatAction</c> — the shape
	/// the platform diff applier silently drops once the mobile template owns that property itself.</summary>
	private static bool CarriesFloatAction(ElementMapEntry entry) =>
		entry.Operation == "merge" && entry.MobileValues is JsonObject values && values.ContainsKey("floatAction");

	/// <summary>Position of an entry in the element map by its mobile name, -1 when absent.</summary>
	private static int IndexOfMobile(MobilePageConversionGuide guide, string mobileName) {
		for (int i = 0; i < guide.ElementMap.Count; i++) {
			if (guide.ElementMap[i].MobileName == mobileName) {
				return i;
			}
		}
		return -1;
	}

	/// <summary>
	/// Seeded pages most likely to carry record-page structure first — tabs and header action buttons live
	/// on form/record pages, not on list pages — then the rest as a fallback; ignores the test when the seed
	/// has no pages at all.
	/// </summary>
	private static async Task<IReadOnlyList<string>> ResolveSeededRecordPageCandidatesOrIgnoreAsync(
		McpServerSession session, CancellationToken cancellationToken, string environmentName) {
		ApplicationListItemEnvelope installedApplication = await SeededApplicationResolver.ResolveOrIgnoreAsync(
			session, cancellationToken, environmentName, ApplicationCode);
		CallToolResult callResult = await session.CallToolAsync(
			PageListTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["code"] = installedApplication.Code
				}
			},
			cancellationToken);
		PageListResponse pageList = EntitySchemaStructuredResultParser.Extract<PageListResponse>(callResult);
		pageList.Success.Should().BeTrue(
			because: $"list-pages must succeed before a seeded page can be converted; an MCP-level failure would hide real runtime regressions. Error: {pageList.Error}");

		List<string> candidates = (pageList.Pages ?? [])
			.Select(page => page.SchemaName)
			.Where(name => !string.IsNullOrWhiteSpace(name))
			.OrderByDescending(name => name!.EndsWith("FormPage", StringComparison.OrdinalIgnoreCase)
				|| name.Contains("RecordPage", StringComparison.OrdinalIgnoreCase))
			.ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
			.Select(name => name!)
			.ToList();
		if (candidates.Count == 0) {
			Assert.Ignore(
				$"Seeded application '{installedApplication.Code}' has no Freedom UI pages on environment '{environmentName}'. Add at least one record page to the seed application.");
		}
		return candidates;
	}

	/// <summary>
	/// A data-section diff (when present) must be split into FOCUSED targeted merges: no path-[] root merge
	/// may carry an ARRAY, because the mobile diff engine replaces arrays wholesale on a merge, so a path-[]
	/// array would silently drop the page's own entries. A scalar-only residual path-[] merge (a top-level
	/// modelConfig scalar that cannot be expressed as a nested-key merge, see
	/// <see cref="WebToMobileAnalysisService.SplitModelConfigRootMerge"/>) is expected-safe and must NOT fail
	/// this gate — it carries no array, so nothing is array-replaced. A null diff (the page has no data
	/// section) is vacuously valid: the call still exercised the template probe and Analyze wiring.
	/// </summary>
	private static void AssertSplitShape(JsonNode? diff, string diffName) {
		if (diff is null) {
			return;
		}
		JsonArray operations = diff.AsArray();
		foreach (JsonNode? operation in operations) {
			JsonObject op = operation!.AsObject();
			if (op["path"]!.AsArray().Count != 0) {
				continue;
			}
			ContainsArray(op["values"]).Should().BeFalse(
				because: $"{diffName} may keep a scalar-only residual root merge, but a path-[] merge that carries an array lets the mobile diff engine array-replace and drop the page's own entries");
		}
	}

	/// <summary>Recursively reports whether <paramref name="node"/> is, or contains anywhere, a JSON array.</summary>
	private static bool ContainsArray(JsonNode? node) => node switch {
		JsonArray => true,
		JsonObject obj => obj.Any(property => ContainsArray(property.Value)),
		_ => false
	};

	private async Task RequireConverterFeatureOrIgnoreAsync(ArrangeContext context) {
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);
		if (!toolNames.Contains(ToolName)) {
			Assert.Ignore(
				$"'{ToolName}' is not advertised: the 'mobile-page-converter' feature is not enabled in the active clio home. "
				+ "Enable it (Features.mobile-page-converter=true) to run this sandbox test.");
		}
	}

	private static async Task<string> ResolveSeededPageSchemaNameOrIgnoreAsync(
		McpServerSession session, CancellationToken cancellationToken, string environmentName) {
		ApplicationListItemEnvelope installedApplication = await SeededApplicationResolver.ResolveOrIgnoreAsync(
			session, cancellationToken, environmentName, ApplicationCode);
		CallToolResult callResult = await session.CallToolAsync(
			PageListTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["code"] = installedApplication.Code
				}
			},
			cancellationToken);
		PageListResponse pageList = EntitySchemaStructuredResultParser.Extract<PageListResponse>(callResult);
		pageList.Success.Should().BeTrue(
			because: $"list-pages must succeed before a seeded page can be converted; an MCP-level failure would hide real runtime regressions. Error: {pageList.Error}");

		// Prefer a list page — it carries the data-source arrays (quick filters / sorting) whose union with
		// the template's natives is the point of the split. Fall back to any seeded page otherwise.
		PageListItem? candidate = pageList.Pages?
				.FirstOrDefault(page => page.SchemaName?.EndsWith("ListPage", StringComparison.OrdinalIgnoreCase) == true)
			?? pageList.Pages?.FirstOrDefault();
		if (candidate is not null && !string.IsNullOrWhiteSpace(candidate.SchemaName)) {
			return candidate.SchemaName;
		}

		Assert.Ignore(
			$"Seeded application '{installedApplication.Code}' has no Freedom UI pages on environment '{environmentName}'. Add at least one page to the seed application.");
		return string.Empty;
	}

	private static async Task<string> ResolveReachableEnvironmentAsync(McpE2ESettings settings) {
		string? configuredEnvironmentName = settings.Sandbox.EnvironmentName;
		if (string.IsNullOrWhiteSpace(configuredEnvironmentName)) {
			Assert.Ignore(
				"mobile-page-conversion MCP E2E requires a configured sandbox environment: set Sandbox.EnvironmentName "
				+ "in the MCP E2E settings to a registered clio environment that hosts the seed application.");
		}
		if (!await CanReachEnvironmentAsync(settings, configuredEnvironmentName!)) {
			Assert.Ignore(
				$"mobile-page-conversion MCP E2E requires a reachable environment: configured sandbox environment "
				+ $"'{configuredEnvironmentName}' did not answer ping-app. Start it or point Sandbox.EnvironmentName at a reachable environment.");
		}
		return configuredEnvironmentName!;
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
}
