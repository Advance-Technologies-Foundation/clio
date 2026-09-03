using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.MobilePageConverter;
using Clio.Command.McpServer.Tools.MobilePageConverter.Legacy;
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
	[Description("Converts a real seeded Freedom UI page through the real clio MCP server and verifies that the returned modelConfigDiff / viewModelConfigDiff are SPLIT into focused targeted merges (no path-[] root merge remains), which is the split/union behavior fed by the mobile template probe, and that no element is dropped for being bound to a non-primary page data source.")]
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
		response.Guide!.ElementMap.Should().NotContain(
			e => e.Operation == "drop" && e.Reason != null && e.Reason.Contains("multi-data-source"),
			because: "a mobile page carries the same multi-data-source structure as web, so an element bound to a "
				+ "non-primary page data source must convert — the drop used to remove whole detail sections and, "
				+ "because emptiness cascades, their wrapper containers with them");
		AssertConvertedListsCarryTheirRow(response.Guide!);
		AssertHeaderActionsConvertToFab(response.Guide!);
	}

	[Test]
	[Description("Non-vacuous MainHeader->FAB guard (ENG-93152): converts real seeded pages until one yields a FloatingActionButton.menuItems entry, then asserts at least one real header-action conversion and its crt.MenuItem/denylist contract. When NO seeded page carries a header action it IGNORES with an explicit reason instead of passing silently, so a regression that stops MainHeader->FAB is caught on any header page and missing seed coverage is surfaced rather than hidden.")]
	[AllureTag(ToolName)]
	[AllureName("get-mobile-page-conversion-guide converts MainHeader actions into the floating action button")]
	[AllureDescription("Iterates the seeded application's pages, converts each through the real clio MCP server, and asserts the header-action -> FloatingActionButton.menuItems contract on the first page that produces one; a conversion failure fails the test, and no header-action page at all degrades to Ignore (never a vacuous pass).")]
	public async Task MobilePageConversionGuideTool_Should_Convert_MainHeaderActions_Into_Fab() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(5));
		await RequireConverterFeatureOrIgnoreAsync(context);
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		IReadOnlyList<string> candidates = await ResolveSeededTabbedPageCandidatesOrIgnoreAsync(
			context.Session, context.CancellationTokenSource.Token, environmentName);

		// Act — convert candidates until one yields a FAB conversion; a conversion FAILURE is a regression, not a seed gap.
		int fabEntryCount = 0;
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
			int fab = (response.Guide?.ElementMap ?? []).Count(e =>
				e.Operation == "insert" && e.ParentName == "FloatingActionButton" && e.PropertyName == "menuItems");
			if (fab > 0) {
				AssertHeaderActionsConvertToFab(response.Guide!);
				fabEntryCount = fab;
				convertedSchemaName = schemaName;
				break;
			}
		}

		// Assert
		if (fabEntryCount == 0) {
			if (failedCandidates.Count > 0) {
				Assert.Fail(
					$"{failedCandidates.Count} of {candidates.Count} seeded page(s) of '{ApplicationCode}' on environment "
					+ $"'{environmentName}' failed to convert; get-mobile-page-conversion-guide must succeed on every seeded "
					+ $"page, so this is a runtime regression, not missing seed data: {string.Join("; ", failedCandidates)}");
			}
			Assert.Ignore(
				$"None of the {candidates.Count} seeded page(s) of '{ApplicationCode}' on environment '{environmentName}' "
				+ "carries a MainHeader action, so MainHeader->FAB could not be exercised end to end. Add a seeded page with a "
				+ "header button (crt.Button under MainHeader) to guard this integration.");
		}
		fabEntryCount.Should().BeGreaterThan(0,
			because: $"the seeded page '{convertedSchemaName}' carries a MainHeader action that must convert into the FloatingActionButton");
	}

	[Test]
	[Description("Non-vacuous excludedComponents TRANSPORT guard (ENG-95081): converts real seeded pages until one carries a component of a bundled-rule-banned type, then asserts NO surviving insert of a banned type reaches the banned host through the banned slot on the entry graph — the regression where crt.SearchFilter survived inside crt.ExpansionPanel's tools because the pass searched only verbatim-carried values. Any candidate that fails to convert fails the test immediately (a runtime regression, never a seed gap); when no seeded page carries any banned type it IGNORES with an explicit reason instead of passing silently. The rule's own acceptance criterion does not depend on this test — WebToMobileRealPageRegressionTests enforces it hermetically on the pinned OOTB Leads_FormPage — so what this one adds is the verdict travelling through the real clio mcp-server process.")]
	[AllureTag(ToolName)]
	[AllureName("get-mobile-page-conversion-guide honors bundled excludedComponents rules on the entry graph")]
	[AllureDescription("Loads the bundled conversion rules' excludedComponents filters, iterates the seeded application's pages through the real clio MCP server, and on the first page whose element map mentions a banned type at all asserts that every surviving insert of a banned type has NO ancestor-entry chain reaching the banned host through the banned slot; a conversion failure fails the test, and a seed set with no banned type degrades to Ignore (never a vacuous pass).")]
	public async Task MobilePageConversionGuideTool_Should_Honor_Bundled_ExcludedComponents_Rules() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(5));
		await RequireConverterFeatureOrIgnoreAsync(context);
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		IReadOnlyList<string> candidates = await ResolveSeededTabbedPageCandidatesOrIgnoreAsync(
			context.Session, context.CancellationTokenSource.Token, environmentName);
		List<ExcludedComponentFilterRule> filters = WebToMobilePageConversionRulesCatalog.LoadBundled()
			.ExcludedComponents
			.SelectMany(g => g?.Filters ?? [])
			.Where(f => !string.IsNullOrWhiteSpace(f?.Type) && !string.IsNullOrWhiteSpace(f.ParentType))
			.ToList();
		filters.Should().NotBeEmpty(
			because: "the bundled conversion rules ship excludedComponents filters — with none, this guard no longer tests anything and must be revisited");

		// Act — convert candidates until one MENTIONS a banned type at all (as an insert OR a drop).
		// A conversion FAILURE fails the test right here — it is a runtime regression, never a seed gap,
		// and deferring it would let a later banned-type candidate mask it behind a green run.
		bool bannedTypeExercised = false;
		string convertedSchemaName = string.Empty;
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
			(callResult.IsError == true).Should().BeFalse(
				because: $"get-mobile-page-conversion-guide must succeed on every seeded page, and '{schemaName}' "
					+ "returned a transport-level error — a runtime regression, not missing seed data");
			MobilePageConversionGuideResponse response =
				EntitySchemaStructuredResultParser.Extract<MobilePageConversionGuideResponse>(callResult);
			response.Success.Should().BeTrue(
				because: $"get-mobile-page-conversion-guide must succeed on every seeded page, and '{schemaName}' "
					+ $"failed with: {response.Error} — a runtime regression, not missing seed data");
			MobilePageConversionGuide guide = response.Guide!;
			// A page only EXERCISES the rule when a banned type survived conversion as an insert. Accepting a
			// mere mention (including e.WebType, which a plain unsupported-type drop also satisfies) would let
			// the loop break on a page where the exclusion pass had nothing to decide, and
			// AssertExcludedComponentsHonored would then pass by construction — reporting a vacuous run as a
			// real one, which is the exact failure the Ignore branch below exists to prevent.
			bool exercisesBannedType = guide.ElementMap.Any(e =>
				string.Equals(e.Operation, "insert", StringComparison.OrdinalIgnoreCase)
				&& filters.Any(f => string.Equals(e.MobileType, f.Type, StringComparison.OrdinalIgnoreCase)));
			if (exercisesBannedType) {
				AssertExcludedComponentsHonored(guide, filters);
				bannedTypeExercised = true;
				convertedSchemaName = schemaName;
				break;
			}
		}

		// Assert
		if (!bannedTypeExercised) {
			Assert.Ignore(
				$"None of the {candidates.Count} seeded page(s) of '{ApplicationCode}' on environment '{environmentName}' "
				+ "converts a component of any excludedComponents-banned type into a surviving insert, so the TRANSPORT path could not be exercised "
				+ "end to end. This skip is not a coverage gap for the rule itself: the acceptance criterion is enforced "
				+ "hermetically on production-shaped metadata by WebToMobileRealPageRegressionTests (the pinned OOTB "
				+ "Leads_FormPage), which runs on every build. What is NOT covered while this skips is the rule reaching "
				+ "the same verdict through the real clio mcp-server process. To close that, add a seeded page with a "
				+ "crt.SearchFilter inside a crt.ExpansionPanel's tools to the seed application.");
		}
		TestContext.Out.WriteLine(
			$"excludedComponents invariant asserted against seeded page '{convertedSchemaName}'.");
	}

	/// <summary>
	/// The entry-graph invariant of the excludedComponents pass, re-derived independently of the product
	/// code: a SURVIVING insert of a banned <c>type</c> must have NO ancestor-entry chain (via
	/// <c>parentName</c>, over insert/merge entries) that reaches a host of the banned <c>parentType</c>
	/// through the banned slot — the edge entering the host must occupy <c>propertiesContainerName</c>
	/// (absent <c>propertyName</c> = <c>items</c>; a filter with no slot accepts any). A banned-type entry
	/// that appears only as a drop is the pass doing its job and passes this check by construction.
	/// </summary>
	private static void AssertExcludedComponentsHonored(
		MobilePageConversionGuide guide, List<ExcludedComponentFilterRule> filters) {
		Dictionary<string, ElementMapEntry> byMobileName = guide.ElementMap
			.Where(e => (e.Operation == "insert" || e.Operation == "merge") && !string.IsNullOrEmpty(e.MobileName))
			.GroupBy(e => e.MobileName!, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
		foreach (ElementMapEntry entry in guide.ElementMap) {
			if (entry.Operation != "insert" || string.IsNullOrEmpty(entry.MobileType)) {
				continue;
			}
			foreach (ExcludedComponentFilterRule filter in filters) {
				if (!string.Equals(entry.MobileType, filter.Type, StringComparison.OrdinalIgnoreCase)) {
					continue;
				}
				string? bannedHost = FindBannedHostOnAncestorPath(entry, filter, byMobileName);
				bannedHost.Should().BeNull(
					because: $"surviving insert '{entry.MobileName}' of banned type '{filter.Type}' reaches host "
						+ $"'{bannedHost}' of type '{filter.ParentType}'"
						+ (string.IsNullOrWhiteSpace(filter.PropertiesContainerName)
							? ""
							: $" through its '{filter.PropertiesContainerName}' slot")
						+ " — the excludedComponents pass must have dropped it (ENG-95081)");
			}
		}
	}

	/// <summary>The ancestor climb of <see cref="AssertExcludedComponentsHonored"/>: the banned host's
	/// mobile name, or null when the entry's chain never reaches one in scope. Bounded and cycle-guarded —
	/// the map arrives from a real environment.</summary>
	private static string? FindBannedHostOnAncestorPath(
		ElementMapEntry candidate, ExcludedComponentFilterRule filter,
		Dictionary<string, ElementMapEntry> byMobileName) {
		var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		ElementMapEntry current = candidate;
		for (int depth = 0; depth <= 32; depth++) {
			string? parentName = current.ParentName;
			if (string.IsNullOrEmpty(parentName) || !visited.Add(parentName)
				|| !byMobileName.TryGetValue(parentName, out ElementMapEntry? parent)) {
				return null;
			}
			bool slotMatches = string.IsNullOrWhiteSpace(filter.PropertiesContainerName)
				|| string.Equals(
					string.IsNullOrEmpty(current.PropertyName) ? "items" : current.PropertyName,
					filter.PropertiesContainerName, StringComparison.OrdinalIgnoreCase);
			if (string.Equals(parent.MobileType, filter.ParentType, StringComparison.OrdinalIgnoreCase) && slotMatches) {
				return parent.MobileName;
			}
			current = parent;
		}
		return null;
	}

	/// <summary>
	/// Any element retargeted into <c>FloatingActionButton.menuItems</c> — a converted MainHeader action
	/// (ENG-93152) — must be a <c>crt.MenuItem</c> insert carrying no visual properties (style/color/icon): the
	/// header-button → FAB denylist. A page with no header actions passes vacuously — the seeded page set is not
	/// guaranteed to carry a header button, so this asserts the contract only when one actually converted.
	/// </summary>
	private static void AssertHeaderActionsConvertToFab(MobilePageConversionGuide guide) {
		List<ElementMapEntry> fabEntries = guide.ElementMap.Where(e =>
			e.Operation == "insert" && e.ParentName == "FloatingActionButton" && e.PropertyName == "menuItems").ToList();
		foreach (ElementMapEntry entry in fabEntries) {
			entry.MobileType.Should().Be("crt.MenuItem",
				because: $"a header action retargeted into the FAB ('{entry.WebName}') becomes a mobile menu item");
			// ENG-93152: the recommended mobile record template provides the FloatingActionButton natively (in the
			// Scaffold's floatAction slot). Reaching this retarget therefore means the probe found the FAB, so the entry
			// MUST be flagged parentExistsOnTemplate:true — the caller inserts only the child and never re-declares the FAB.
			entry.ParentExistsOnTemplate.Should().BeTrue(
				because: $"the mobile template provides the FloatingActionButton natively, so the retargeted action "
					+ $"('{entry.WebName}') must be flagged parentExistsOnTemplate:true over the real MCP transport");
			if (entry.MobileValues is JsonObject values) {
				values.ContainsKey("style").Should().BeFalse(
					because: $"visual properties are denylisted on a converted FAB menu item ('{entry.WebName}')");
				values.ContainsKey("icon").Should().BeFalse(
					because: $"visual properties are denylisted on a converted FAB menu item ('{entry.WebName}')");
				values.ContainsKey("color").Should().BeFalse(
					because: $"visual properties are denylisted on a converted FAB menu item ('{entry.WebName}')");
			}
		}
		// AC 4.5: once header actions convert, the MainHeader scope container itself produces NO mobile element —
		// it is neither inserted nor merged (a non-converting scope emits nothing of its own). Asserted only when a
		// FAB conversion actually happened, so a page without a header still passes vacuously.
		if (fabEntries.Count > 0) {
			guide.ElementMap.Should().NotContain(
				e => e.WebName == "MainHeader" && (e.Operation == "insert" || e.Operation == "merge"),
				because: "a non-converting scope container (MainHeader) is never emitted as a mobile element (AC 4.5)");
			// ENG-93152 parentExistsOnTemplate contract: the FAB is template-provided, so the guide must NEVER emit an
			// insert/merge that re-declares it — authoring a second FloatingActionButton would override the native one.
			guide.ElementMap.Should().NotContain(
				e => (e.Operation == "insert" || e.Operation == "merge")
					&& (e.MobileName == "FloatingActionButton" || e.MobileType == "crt.FloatingActionButton"),
				because: "the FloatingActionButton is provided by the mobile template, so the guide must never emit an "
					+ "insert/merge that re-declares it (only the retargeted children are inserted)");
		}
	}

	/// <summary>
	/// Every inserted mobile list must arrive with its row PREBUILT (ENG-95046). The row is what makes the list
	/// render: a <c>crt.ListItem</c> under <c>itemLayout</c> whose <c>title</c> is a plain <c>$binding</c>
	/// STRING. Both failure shapes are asserted, because they look different in the designer and only one of
	/// them is obvious — a missing row leaves the whole list blank, while a title wrapped as
	/// <c>{ "value": … }</c> fills the body rows and leaves only the Title column empty. Also asserts the web
	/// grid's own properties do not ride along, since mobile <c>crt.List</c> has no equivalent for them.
	/// A page with no converted list passes vacuously — the seeded page set is not guaranteed to carry one.
	/// </summary>
	private static void AssertConvertedListsCarryTheirRow(MobilePageConversionGuide guide) {
		foreach (ElementMapEntry list in guide.ElementMap.Where(e =>
			e.Operation == "insert" && e.MobileType == "crt.List" && e.MobileValues is not null)) {
			JsonNode? row = list.MobileValues!["itemLayout"];
			row.Should().NotBeNull(
				because: $"'{list.WebName}' converts to a mobile list, whose row has no web counterpart to copy — "
					+ "the converter must build it from the grid's columns, and when that was left to the caller "
					+ "the list arrived with no title and no body");
			row!["type"]?.GetValue<string>().Should().Be("crt.ListItem",
				because: $"'{list.WebName}' must carry the mobile row element the list renders each record with");
			// The row leads with the FIRST column whatever its type (title-type selection was removed by
			// decision), so a title is present whenever the grid has any column at all — a title is absent only
			// for a column-less grid. The shape is asserted only when a title exists: asserting unconditionally
			// is what made this fail against a seeded page whose grid had no columns.
			if (row["title"] is { } title) {
				title.GetValueKind().Should().Be(JsonValueKind.String,
					because: $"the registry declares crt.ListItem.title as a string binding, and on '{list.WebName}' "
						+ "an object wrapper would render an empty Title column while the body rows still looked fine");
			}
			// Deliberately NOT asserted non-empty: a single-column grid legitimately yields a title and no body
			// rows, and this runs against whichever page the sandbox happens to seed.
			row["body"].Should().NotBeNull(
				because: $"the row on '{list.WebName}' must carry the body collection, even when the grid had only "
					+ "the one display column and it is therefore empty");
		}
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
		IReadOnlyList<string> candidates = await ResolveSeededTabbedPageCandidatesOrIgnoreAsync(
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
	[Description("ENG-96114 end to end: when the guide places content ABOVE a positional anchor, the anchor must not be left where the mobile template pinned it. Inserting at index 0 pushes the anchor down inside the parent's items, but a grid parent positions by layoutConfig — so the guide must also carry a merge moving the anchor's own row. Before this the converted progress bar rendered under the tab strip and nothing reported it.")]
	[AllureTag(ToolName)]
	[AllureName("get-mobile-page-conversion-guide frees the anchor's row for content placed above it")]
	[AllureDescription("Iterates the seeded application's pages through the real clio MCP server until one converts with indexed positional inserts, then asserts the guide carries exactly one merge for that anchor and that its layoutConfig.row is no longer the template's row 1 — the anchor made room for the content above it. No seeded page with content above the wrapper degrades to Ignore, never a vacuous pass; a conversion failure always fails the test.")]
	public async Task MobilePageConversionGuideTool_Should_Free_The_Anchor_Row_For_Content_Above_It() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(5));
		await RequireConverterFeatureOrIgnoreAsync(context);
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		IReadOnlyList<string> candidates = await ResolveSeededTabbedPageCandidatesOrIgnoreAsync(
			context.Session, context.CancellationTokenSource.Token, environmentName);

		// Act — convert candidates until one places content above an anchor. The converter's own reason text is
		// the trigger; a conversion FAILURE is a regression, not a seed gap.
		MobilePageConversionGuide guide = null;
		string convertedSchemaName = string.Empty;
		List<string> failedCandidates = [];
		foreach (string schemaName in candidates) {
			MobilePageConversionGuide candidate = await ConvertOrCollectFailureAsync(
				context.Session, context.CancellationTokenSource.Token, environmentName, schemaName, failedCandidates);
			if (PlacedAboveAnchor(candidate).Count > 0) {
				guide = candidate;
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
				+ "converted successfully, but none placed content above a positional anchor. Add a tabbed record "
				+ "page with an element above CardContentWrapper (e.g. a stage progress bar) to the seed application "
				+ "to exercise this surface.");
		}

		// Assert — the anchor made room. The exact arithmetic is unit-tested; what must hold end to end is that
		// the anchor no longer sits in the row the template pinned it to, and that it is re-placed exactly once.
		List<ElementMapEntry> above = PlacedAboveAnchor(guide);
		string anchorName = ResolveBundledPositionalAnchor();
		anchorName.Should().NotBeNullOrEmpty(
			because: "the bundled tabbed template rule must declare the mobile anchor its ':top' content is placed "
				+ $"around, so '{convertedSchemaName}' has something to be positioned against");

		List<ElementMapEntry> anchorMerges = [.. guide.ElementMap
			.Where(e => e.Operation == "merge" && string.Equals(e.MobileName, anchorName, StringComparison.OrdinalIgnoreCase))];
		anchorMerges.Should().ContainSingle(
			because: "the anchor is re-placed exactly once, whether the page produced a template twin for it or not");
		JsonNode row = anchorMerges[0].MobileValues?["layoutConfig"]?["row"];
		row.Should().NotBeNull(
			because: $"'{anchorName}' is positioned by layoutConfig, so the {above.Count} element(s) above it can "
				+ "only be made room for by moving its row");
		row!.GetValue<int>().Should().BeGreaterThan(1,
			because: "leaving the anchor in the template's first row is exactly the defect — the content above it "
				+ "would render below it");

		foreach (ElementMapEntry entry in above) {
			entry.MobileValues?["layoutConfig"].Should().NotBeNull(
				because: $"'{entry.MobileName}' must carry its own row too: freeing the anchor's row is not enough, "
					+ "the mobile runtime does not auto-place an unpositioned child into the free cell");
		}
	}

	/// <summary>
	/// Surviving inserts the converter routed ABOVE a positional anchor: they carry an index and say so in the
	/// reason the converter composed. Keyed on the reason because the anchor name is page/template data, not
	/// something this test may assume.
	/// </summary>
	private static List<ElementMapEntry> PlacedAboveAnchor(MobilePageConversionGuide guide) =>
		guide?.ElementMap is null
			? []
			: [.. guide.ElementMap.Where(e =>
				e.Operation == "insert" && e.Index is not null
				&& e.Reason is not null && e.Reason.Contains("placed above the mobile ", StringComparison.Ordinal))];

	/// <summary>
	/// The MOBILE anchor the bundled tabbed-template rule places its positional content around, read from the
	/// shipped rules file rather than parsed out of the converter's prose — a reason-wording change must not
	/// break this test, and the rule is the authoritative source anyway. Mirrors
	/// <see cref="ResolveBundledRemovableTypes"/>. Null when no rule declares a positional entry.
	/// </summary>
	private static string ResolveBundledPositionalAnchor() =>
		WebToMobilePageConversionRulesCatalog.LoadBundled().Templates
			.Where(t => t.Mobile == "MobilePageWithTabsFreedomTemplate")
			.SelectMany(t => t.Containers)
			.Where(c => c.Mobile is not null && c.Mobile.Contains(':'))
			.Select(c => c.Mobile.Split(':', 2)[0].Trim())
			.FirstOrDefault(anchor => anchor.Length > 0);

	[Test]
	[Description("Container child-slot regression guard: every surviving container insert that another surviving insert targets as parentName must physically declare THE SLOT IT IS TARGETED THROUGH ('items', 'tools', 'menuItems', …) in its mobileValues, and the viewConfigDiff assembled from the element map (in map order, exactly as the guide's nextSteps instruct the caller) must apply cleanly through the faithful differ clones. Before the converter declared those slots, the FIRST parent-child insert pair failed the Creatio differ with 'Item X is not a container for other items', so this is non-vacuous on any seeded page with nested containers. A conversion failure always fails the test, and so does a page that converted WITH tabAreaLayers yet produced no parent-targeting insert (the synthesized layers guarantee one by construction); Ignores only when the seed truly carries no nested structure at all.")]
	[AllureTag(ToolName)]
	[AllureName("get-mobile-page-conversion-guide returns an element map the Creatio differ applies cleanly")]
	[AllureDescription("Starts the real clio MCP server, converts every seeded page of the AutoTestClioMcp application, assembles each guide's insert entries into a mobile body viewConfigDiff and applies it through MobileDiffApplyValidator (the faithful JsonDiffApplier clone) — reproducing at the full MCP path the differ acceptance the unit tier covers in MobileDiffApplyValidatorTests, and asserting every parent-targeted insert declares the child slot its own children are inserted through.")]
	public async Task MobilePageConversionGuideTool_Should_Return_ElementMap_The_Differ_Applies_Cleanly() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(5));
		await RequireConverterFeatureOrIgnoreAsync(context);
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		IReadOnlyList<string> candidates = await ResolveSeededTabbedPageCandidatesOrIgnoreAsync(
			context.Session, context.CancellationTokenSource.Token, environmentName);

		// Act + Assert (per page) — convert EVERY seeded page; a conversion failure is a runtime
		// regression, never a seed gap, so failures are collected and fail the test outright.
		int pagesWithTargetedParents = 0;
		int pagesWithTabAreaLayers = 0;
		int pagesWithAConvertedTab = 0;
		List<string> failedCandidates = [];
		foreach (string schemaName in candidates) {
			MobilePageConversionGuide? guide = await ConvertOrCollectFailureAsync(
				context.Session, context.CancellationTokenSource.Token, environmentName, schemaName, failedCandidates);
			if (guide is null) {
				continue;
			}
			if (guide.TabAreaLayers is { Count: > 0 }) {
				pagesWithTabAreaLayers++;
			}
			AssertNoUnhostablePlacement(guide, schemaName);
			pagesWithAConvertedTab += guide.ElementMap.Any(e => e.Operation == "insert"
				&& string.Equals(e.MobileType, MobileTabComponentType, StringComparison.OrdinalIgnoreCase))
				? 1
				: 0;
			List<(ElementMapEntry Parent, string Slot)> targetedParents = ResolveTargetedParents(guide);
			if (targetedParents.Count == 0) {
				continue;
			}
			pagesWithTargetedParents++;
			foreach ((ElementMapEntry parent, string slot) in targetedParents) {
				(parent.MobileValues as JsonObject).Should().NotBeNull(
					because: $"a container insert ('{parent.MobileName}' on '{schemaName}') always carries a mobileValues object built by the converter");
				((JsonObject)parent.MobileValues!).ContainsKey(slot).Should().BeTrue(
					because: $"'{parent.MobileName}' on '{schemaName}' is targeted as a parent through '{slot}', so the converter must have declared that slot — the Creatio differ resolves the parent collection generically as itemInfo.Item[propertyName] and refuses the child insert with 'is not a container for other items' for ANY slot it cannot find there");
			}
			SchemaValidationResult applied = MobileDiffApplyValidator.Validate(AssembleViewConfigDiffBody(guide));
			applied.IsValid.Should().BeTrue(
				because: $"the viewConfigDiff assembled from the guide for '{schemaName}' must survive the Creatio differ clones; before the converter declared container child slots this failed with 'is not a container for other items'. Errors: {string.Join("; ", applied.Errors)}");
		}

		// Assert (aggregate) — never pass vacuously.
		if (failedCandidates.Count > 0) {
			Assert.Fail(
				$"{failedCandidates.Count} of {candidates.Count} seeded page(s) of '{ApplicationCode}' on environment "
				+ $"'{environmentName}' failed to convert; get-mobile-page-conversion-guide must succeed on every seeded "
				+ $"page, so this is a runtime regression, not missing seed data: {string.Join("; ", failedCandidates)}");
		}
		if (pagesWithTargetedParents == 0) {
			// A page that converted WITH tabAreaLayers always has a targeted parent by construction:
			// BuildTabAreaLayers synthesizes the tab-body grid and inserts the Area card into it through the
			// 'items' slot, and both layers are ordinary element-map inserts. Zero targeted parents on such a
			// page is therefore a converter regression (the synthesized layers or their parentName/propertyName
			// wiring went missing), NOT missing seed data — so it must fail in CI rather than skip.
			if (pagesWithTabAreaLayers > 0) {
				Assert.Fail(
					$"{pagesWithTabAreaLayers} of {candidates.Count} seeded page(s) of '{ApplicationCode}' on environment "
					+ $"'{environmentName}' converted WITH tabAreaLayers, so the synthesized tab-body/Area layers must "
					+ "appear in the element map as inserts that target a parent through a child slot — yet not one "
					+ "parent-targeting insert was found. That is a converter regression in BuildTabAreaLayers or in the "
					+ "element map's parentName/propertyName wiring, not a seed-data gap.");
			}
			Assert.Ignore(
				$"None of the {candidates.Count} seeded page(s) of '{ApplicationCode}' on environment '{environmentName}' "
				+ "produced a container insert with child inserts (and none carried tabs, whose synthesized layers would "
				+ "have produced one), so the container child-slot differ contract could not be exercised end to end. "
				+ "Add a seeded page with nested containers to guard this integration.");
		}
		pagesWithTargetedParents.Should().BeGreaterThan(0,
			because: "at least one seeded page with nested containers must have exercised the differ-apply gate");
		if (pagesWithAConvertedTab == 0) {
			// Not a failure: the tab-strip invariant is pinned hermetically on every build by
			// WebToMobileGeneralInfoTabRegressionTests against the OOTB Services_FormPage. What is NOT covered
			// while this is silent is the RUNTIME-FETCHED rules file reaching the same verdict through the real
			// clio mcp-server process — the one failure mode no hermetic unit test can see.
			Assert.Ignore(
				$"None of the {candidates.Count} seeded page(s) of '{ApplicationCode}' on environment "
				+ $"'{environmentName}' produced a converted tab, so the ENG-94951 placement invariant could not be "
				+ "exercised end to end. This is not a coverage gap for the rule itself \u2014 "
				+ "WebToMobileGeneralInfoTabRegressionTests pins it hermetically on the OOTB Services_FormPage and "
				+ "runs on every build. What is NOT covered while this skips is the RUNTIME-FETCHED rules file "
				+ "reaching the same verdict through the real clio mcp-server process. To close it, add a tabbed "
				+ "record page whose General information tab holds an expansion panel to the seed application.");
		}
	}

	/// <summary>
	/// ENG-94951: a parent that at least one <c>crt.TabContainer</c> insert targets IS a tab strip, and a
	/// <c>crt.TabPanel</c> renders only <c>crt.TabContainer</c> children — so any OTHER insert targeting that
	/// same parent is invisible in Mobile Designer and its whole subtree is lost from the converted page.
	/// <para>
	/// A pure STRUCTURAL statement: it reads the serialized guide and never calls the converter's own passes, so
	/// it cannot pass merely because the implementation agrees with itself. The strip set is SEEDED with the
	/// tabbed mobile template's own strip name, exactly as the unit-level statement of this invariant does —
	/// deriving it from converted tabs ALONE makes the check vacuous on the very page shape the ticket reported,
	/// where the only tab is the template's general one and no crt.TabContainer insert exists. Deriving it from converted tabs ALONE makes the check vacuous on the
	/// very page shape the ticket reported — a page whose only tab is the template's general one authors no
	/// <c>crt.TabContainer</c> insert, so the set would come back empty and the loss would pass unseen.
	/// </para>
	/// </summary>
	private static void AssertNoUnhostablePlacement(MobilePageConversionGuide guide, string schemaName) {
		HashSet<string> strips = new(StringComparer.OrdinalIgnoreCase) { MobileTabsElementName };
		strips.UnionWith(guide.ElementMap
			.Where(e => e.Operation == "insert"
				&& string.Equals(e.MobileType, MobileTabComponentType, StringComparison.OrdinalIgnoreCase)
				&& e.ParentName is { Length: > 0 })
			.Select(e => e.ParentName!));
		List<ElementMapEntry> offenders = [
			.. guide.ElementMap.Where(e => e.Operation == "insert"
				&& e.ParentName is { Length: > 0 }
				&& strips.Contains(e.ParentName!)
				&& !string.Equals(e.MobileType, MobileTabComponentType, StringComparison.OrdinalIgnoreCase))
		];
		offenders.Should().BeEmpty(
			because: $"on '{schemaName}' a crt.TabPanel accepts only crt.TabContainer children, so each of these is "
				+ "invisible in Mobile Designer and its whole subtree is lost from the converted page (ENG-94951): "
				+ string.Join(", ", offenders.Select(e => $"{e.MobileName}({e.MobileType})->{e.ParentName}")));
	}

	/// <summary>Mobile component type of a single tab; only this type may be a child of a crt.TabPanel.</summary>
	private const string MobileTabComponentType = "crt.TabContainer";

	/// <summary>The tabbed mobile template's own tab strip, which is a strip whether or not a tab converts.</summary>
	private const string MobileTabsElementName = "Tabs";

	[Test]
	[Description("Non-vacuous guard for container types OUTSIDE emptyContainerRemoval.removableTypes: converts seeded pages until one yields a surviving parent-targeted insert whose mobileType the empty-container-removal pass never lists (a crt.Button carrying menuItems children, a crt.Timeline, a crt.ButtonToggleGroup, …), then asserts the slot it is targeted through is declared and the differ applies cleanly — the slot declaration is keyed on 'used as parent through this slot', never on a type list, and a type-list-keyed regression would silently reintroduce exactly this case. The type-list independence itself is additionally pinned off-stand, on every unit run, by WebToMobileConversionServiceTests.Analyze_ContainerTypesOutsideEveryList_StillGetItemsSlot; this test is its full-MCP-path counterpart. When NO seeded page carries such a container it IGNORES with an explicit seed instruction instead of passing silently; a conversion failure always fails the test.")]
	[AllureTag(ToolName)]
	[AllureName("get-mobile-page-conversion-guide declares the child slot on containers outside the removable-type list")]
	[AllureDescription("Starts the real clio MCP server, converts seeded pages of AutoTestClioMcp until one produces a surviving parent-targeted container insert whose type is not in the bundled emptyContainerRemoval.removableTypes, and asserts the converter declared the child slot that insert is targeted through and that the assembled viewConfigDiff applies cleanly through the faithful differ clones — the type-list-independent half of the container child-slot contract, through the full MCP path.")]
	public async Task MobilePageConversionGuideTool_Should_Declare_ChildSlot_On_NonRemovableType_Container() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(5));
		await RequireConverterFeatureOrIgnoreAsync(context);
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		IReadOnlyList<string> candidates = await ResolveSeededTabbedPageCandidatesOrIgnoreAsync(
			context.Session, context.CancellationTokenSource.Token, environmentName);
		IReadOnlySet<string> removableTypes = ResolveBundledRemovableTypes();

		// Act — convert candidates until one yields a parent-targeted insert whose type the removal pass
		// never lists, through ANY child slot (a crt.Button targeted through menuItems qualifies exactly like
		// a crt.Timeline targeted through items); a conversion failure is a runtime regression, never a seed gap.
		MobilePageConversionGuide? matchedGuide = null;
		ElementMapEntry? matchedParent = null;
		string matchedSlot = string.Empty;
		string convertedSchemaName = string.Empty;
		List<string> failedCandidates = [];
		foreach (string schemaName in candidates) {
			MobilePageConversionGuide? guide = await ConvertOrCollectFailureAsync(
				context.Session, context.CancellationTokenSource.Token, environmentName, schemaName, failedCandidates);
			if (guide is null) {
				continue;
			}
			(ElementMapEntry Parent, string Slot) match = ResolveTargetedParents(guide).FirstOrDefault(p =>
				p.Parent.MobileType is { Length: > 0 } && !removableTypes.Contains(p.Parent.MobileType));
			if (match.Parent is not null) {
				matchedGuide = guide;
				matchedParent = match.Parent;
				matchedSlot = match.Slot;
				convertedSchemaName = schemaName;
				break;
			}
		}

		// Assert
		if (matchedGuide is null) {
			if (failedCandidates.Count > 0) {
				Assert.Fail(
					$"{failedCandidates.Count} of {candidates.Count} seeded page(s) of '{ApplicationCode}' on environment "
					+ $"'{environmentName}' failed to convert; get-mobile-page-conversion-guide must succeed on every seeded "
					+ $"page, so this is a runtime regression, not missing seed data: {string.Join("; ", failedCandidates)}");
			}
			Assert.Ignore(
				$"None of the {candidates.Count} seeded page(s) of '{ApplicationCode}' on environment '{environmentName}' "
				+ "produced a surviving parent-targeted insert whose type is outside emptyContainerRemoval.removableTypes "
				+ $"({string.Join(", ", removableTypes)}). The seed application is provisioned OUTSIDE this repository (the "
				+ "pipeline pushes the AutoTest/AutoTestClioMcp packages onto the stand), so this gap is closed by seeding, "
				+ "not by a code change: add a page holding children inside a registry-supported container of another type "
				+ "— a crt.Button with menuItems, a crt.Timeline, a crt.ButtonToggleGroup. Until then the type-list "
				+ "independence stays pinned off-stand by the unit test named in this test's Description.");
		}
		(matchedParent!.MobileValues as JsonObject).Should().NotBeNull(
			because: $"a container insert ('{matchedParent.MobileName}' on '{convertedSchemaName}') always carries a mobileValues object built by the converter");
		((JsonObject)matchedParent.MobileValues!).ContainsKey(matchedSlot).Should().BeTrue(
			because: $"'{matchedParent.MobileName}' ({matchedParent.MobileType}) on '{convertedSchemaName}' is outside the removable-type list and is targeted through '{matchedSlot}' — exactly the class of parent a type-list-keyed seeding would leave slotless for the differ to refuse");
		SchemaValidationResult applied = MobileDiffApplyValidator.Validate(AssembleViewConfigDiffBody(matchedGuide!));
		applied.IsValid.Should().BeTrue(
			because: $"the viewConfigDiff assembled from the guide for '{convertedSchemaName}' must survive the Creatio differ clones. Errors: {string.Join("; ", applied.Errors)}");
	}

	/// <summary>
	/// Converts one seeded page through the real MCP server. A transport-level error or an unsuccessful
	/// response is collected into <paramref name="failedCandidates"/> (the caller fails the test on any —
	/// a seeded page that stops converting is a runtime regression, never a seed gap) and yields null.
	/// </summary>
	private static async Task<MobilePageConversionGuide?> ConvertOrCollectFailureAsync(
		McpServerSession session, CancellationToken cancellationToken, string environmentName,
		string schemaName, List<string> failedCandidates) {
		CallToolResult callResult = await session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = schemaName,
					["environment-name"] = environmentName
				}
			},
			cancellationToken);
		if (callResult.IsError == true) {
			failedCandidates.Add($"'{schemaName}': transport-level error");
			return null;
		}
		MobilePageConversionGuideResponse response =
			EntitySchemaStructuredResultParser.Extract<MobilePageConversionGuideResponse>(callResult);
		if (!response.Success) {
			failedCandidates.Add($"'{schemaName}': {response.Error}");
			return null;
		}
		if (response.Guide is null) {
			failedCandidates.Add($"'{schemaName}': successful response carried no guide");
			return null;
		}
		return response.Guide;
	}

	/// <summary>
	/// Surviving inserts that at least one OTHER surviving insert targets as <c>parentName</c>, each paired
	/// with the slot it is targeted THROUGH — any slot, not just <c>items</c> (an absent propertyName defaults
	/// to <c>items</c>, mirroring the converter's own slot resolution). A parent targeted through two slots
	/// yields one pair per slot. Merge twins are excluded on both sides on purpose: a template-provided parent
	/// carries no converter-owned mobileValues, so it is not this contract's subject.
	/// </summary>
	private static List<(ElementMapEntry Parent, string Slot)> ResolveTargetedParents(MobilePageConversionGuide guide) {
		// (parentName, slot) pairs an insert actually targets. The slot is the child's own propertyName —
		// 'items' only as the documented default — because the differ resolves the parent collection as
		// itemInfo.Item[propertyName] and refuses ANY slot the parent does not declare, not just 'items'.
		Dictionary<string, HashSet<string>> targetedSlots = new(StringComparer.OrdinalIgnoreCase);
		foreach (ElementMapEntry entry in guide.ElementMap) {
			if (entry.Operation != "insert" || entry.ParentName is not { Length: > 0 }) {
				continue;
			}
			string slot = entry.PropertyName is { Length: > 0 } ? entry.PropertyName : "items";
			if (!targetedSlots.TryGetValue(entry.ParentName, out HashSet<string>? slots)) {
				// Ordinal, like the converter's own pass: the differ reads the slot as a case-SENSITIVE JSON
				// member, so a casing the parent does not declare verbatim is still refused.
				slots = new HashSet<string>(StringComparer.Ordinal);
				targetedSlots[entry.ParentName] = slots;
			}
			slots.Add(slot);
		}
		List<(ElementMapEntry Parent, string Slot)> parents = [];
		foreach (ElementMapEntry entry in guide.ElementMap) {
			if (entry.Operation != "insert" || entry.MobileName is not { Length: > 0 }
				|| !targetedSlots.TryGetValue(entry.MobileName, out HashSet<string>? slots)) {
				continue;
			}
			parents.AddRange(slots.Select(slot => (entry, slot)));
		}
		return parents;
	}

	/// <summary>
	/// Assembles the mobile body's <c>viewConfigDiff</c> from the element map's INSERT entries, in map
	/// order — the same mechanical assembly the guide's nextSteps instruct the caller to perform
	/// (parent-before-child order is the converter's own guarantee, asserted by the tabAreaLayers test
	/// above). Merge entries are left out on purpose: a merge twin layers onto a template-provided element
	/// the diff never declares, which the validator's seeded base already covers.
	/// </summary>
	private static string AssembleViewConfigDiffBody(MobilePageConversionGuide guide) {
		var viewConfigDiff = new JsonArray();
		foreach (ElementMapEntry entry in guide.ElementMap) {
			if (entry.Operation != "insert" || entry.MobileName is not { Length: > 0 }) {
				continue;
			}
			var operation = new JsonObject {
				["operation"] = "insert",
				["name"] = entry.MobileName,
				// A genuine converter insert always carries a JsonObject; the fallback only keeps a
				// hypothetical value-less entry from crashing the assembly instead of the differ gate.
				["values"] = entry.MobileValues?.DeepClone() ?? new JsonObject { ["type"] = entry.MobileType }
			};
			if (entry.ParentName is { Length: > 0 }) {
				operation["parentName"] = entry.ParentName;
				operation["propertyName"] = entry.PropertyName is { Length: > 0 } ? entry.PropertyName : "items";
			}
			viewConfigDiff.Add(operation);
		}
		return new JsonObject { ["viewConfigDiff"] = viewConfigDiff }.ToJsonString();
	}

	/// <summary>
	/// The bundled rules' <c>emptyContainerRemoval.removableTypes</c> list — read from the converter's own
	/// bundled catalog so the test never maintains a second copy. The registry may serve a NEWER rules
	/// version at runtime, but removableTypes is the converter's own baseline contract and the bundled file
	/// is its source of truth in this repository.
	/// </summary>
	private static IReadOnlySet<string> ResolveBundledRemovableTypes() {
		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.LoadBundled();
		return new HashSet<string>(
			rules.EmptyContainerRemoval?.RemovableTypes ?? [],
			StringComparer.OrdinalIgnoreCase);
	}

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
	/// OOTB legacy Mobile-wizard list settings schemas that ship with most Creatio products. The legacy sandbox test
	/// tries them in order and IGNORES (never passes vacuously) when none exists on the environment.
	/// </summary>
	private static readonly string[] LegacyGridPageSettingsCandidates = [
		"MobileActivityGridPageSettingsDefaultWorkplace",
		"MobileContactGridPageSettingsDefaultWorkplace",
		"MobileAccountGridPageSettingsDefaultWorkplace",
		"MobileCaseGridPageSettingsDefaultWorkplace",
		"MobileOrderGridPageSettingsDefaultWorkplace",
		"MobileLeadGridPageSettingsDefaultWorkplace",
		"MobileOpportunityGridPageSettingsDefaultWorkplace"
	];

	[Test]
	[Description("Legacy source (ENG-95730): converts a real classic Mobile-wizard GridPageSettings schema through the real clio MCP server and verifies the legacy mechanism ran (sourceType legacy-mobile-grid-page, conversionMechanism legacy-mobile-settings-converter), the guide recommends BaseMobileListTemplate, carries exactly ONE merge onto ListItem with a string title, declares PDS_Id, exposes no raw body in legacySource.layers, and is idempotent across two calls. Ignores when no OOTB legacy settings schema exists on the environment; a runtime failure on an existing schema fails the test.")]
	[AllureTag(ToolName)]
	[AllureName("get-mobile-page-conversion-guide converts a legacy mobile wizard list settings schema")]
	[AllureDescription("Tries the OOTB Mobile<Entity>GridPageSettingsDefaultWorkplace schemas against the sandbox; on the first one that exists asserts the legacy mechanism contract end to end, and calls the tool twice to prove the guide is deterministic.")]
	public async Task MobilePageConversionGuideTool_Should_Convert_Legacy_GridPageSettings_Schema() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(5));
		await RequireConverterFeatureOrIgnoreAsync(context);
		string environmentName = await ResolveReachableEnvironmentAsync(settings);

		// Act — the first existing legacy schema decides; a missing schema is a seed gap, any other failure a regression.
		MobilePageConversionGuideResponse? response = null;
		string convertedSchemaName = string.Empty;
		List<string> missing = [];
		foreach (string schemaName in LegacyGridPageSettingsCandidates) {
			MobilePageConversionGuideResponse candidate = await ConvertAsync(context, schemaName, environmentName);
			if (!candidate.Success && candidate.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true) {
				missing.Add(schemaName);
				continue;
			}
			response = candidate;
			convertedSchemaName = schemaName;
			break;
		}
		if (response is null) {
			Assert.Ignore(
				$"None of the OOTB legacy Mobile-wizard list settings schemas exist on environment '{environmentName}' "
				+ $"({string.Join(", ", missing)}). Seed one with the classic Mobile application wizard to guard the legacy path.");
		}

		// Assert
		response!.ConversionMechanism.Should().Be(LegacyMobileListAnalysisService.MechanismLegacySettingsConverter,
			because: "a GridPageSettings schema must be routed to the legacy mechanism, and the result must say so");
		response.SourceType.Should().Be(LegacyMobileListAnalysisService.SourceTypeLegacyGridPage,
			because: "the detected source type is reported on the response");
		response.Success.Should().BeTrue(
			because: $"an existing legacy list settings schema '{convertedSchemaName}' must convert (a custom-viewConfig refusal would name it). Error: {response.Error}");
		MobilePageConversionGuide guide = response.Guide!;
		guide.RecommendedMobileTemplate.Should().Be(LegacyMobileListAnalysisService.DefaultGridPageTemplate.TemplateName,
			because: "every converted legacy list page inherits the shipped mobile list template");
		guide.ElementMap.Should().HaveCount(2, because: "the legacy guide carries exactly the two merges the designer writes: FolderTreeActions and ListItem");
		guide.ElementMap.Should().OnlyContain(e => e.Operation == "merge", because: "every target element is template-provided");
		ElementMapEntry folder = guide.ElementMap.Single(e => e.MobileName == LegacyMobileListAnalysisService.DefaultGridPageTemplate.FolderTreeActionsName);
		folder.MobileValues!["rootSchemaName"]!.GetValue<string>().Should().Be(guide.LegacySource!.EntitySchemaName,
			because: "folder filtering is bound to the entity the wizard page was bound to");
		ElementMapEntry merge = guide.ElementMap.Single(e => e.MobileName == LegacyMobileListAnalysisService.DefaultGridPageTemplate.ListItemName);
		merge.MobileValues!["title"]!.GetValueKind().Should().Be(JsonValueKind.String,
			because: "a wizard list page always has a title column bound as a string attribute reference");
		guide.ViewModelConfigDiff!.AsArray()[0]!["values"]!.AsObject().ContainsKey("PDS_Id").Should().BeTrue(
			because: "PDS_Id is always declared so the row can open its record");
		guide.LegacySource.Should().NotBeNull(because: "the legacy facts are the per-element report");
		guide.LegacySource!.Layers.Should().NotBeEmpty(because: "at least one package layer contributed");
		JsonSerializer.Serialize(guide.LegacySource.Layers).Should().NotContain("\"operation\"",
			because: "no raw schema body may leak into the guide — layers carry package names and counts only");
		// ENG-95733: the designer names come from a data table, and the table is only as good as the template it
		// describes. Against a real stand the template IS readable, so neither the "unverified" fallback nor a
		// missing-element warning may appear — this is what proves the probe ran and the shipped names are right.
		guide.Constraints.Should().NotContain(c => c.Contains("UNVERIFIED"),
			because: "the template probe must have read the shipped template on a real environment");
		guide.Constraints.Should().NotContain(c => c.Contains("does not declare"),
			because: "every designer name the shipped rules table declares must exist in the real template");

		MobilePageConversionGuideResponse second = await ConvertAsync(context, convertedSchemaName, environmentName);
		second.Success.Should().BeTrue(because: "the second run must succeed exactly like the first");
		JsonSerializer.Serialize(second.Guide!.ElementMap.Single(e => e.MobileName == LegacyMobileListAnalysisService.DefaultGridPageTemplate.ListItemName).MobileValues).Should().Be(JsonSerializer.Serialize(merge.MobileValues),
			because: "the conversion is deterministic — running twice yields the same row values");
		JsonSerializer.Serialize(second.Guide.ViewModelConfigDiff).Should().Be(JsonSerializer.Serialize(guide.ViewModelConfigDiff),
			because: "the conversion is deterministic — running twice yields the same attribute declaration");
	}

	private static async Task<MobilePageConversionGuideResponse> ConvertAsync(ArrangeContext context, string schemaName, string environmentName) {
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = schemaName,
					["environment-name"] = environmentName
				}
			},
			context.CancellationTokenSource.Token);
		callResult.IsError.Should().NotBeTrue(because: $"converting '{schemaName}' must return a structured envelope, not a transport-level error");
		return EntitySchemaStructuredResultParser.Extract<MobilePageConversionGuideResponse>(callResult);
	}

	/// <summary>
	/// Seeded pages most likely to carry tabs first (form/record pages — tabs live on record pages, not
	/// list pages), then the rest as a fallback; ignores the test when the seed has no pages at all.
	/// </summary>
	private static async Task<IReadOnlyList<string>> ResolveSeededTabbedPageCandidatesOrIgnoreAsync(
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
				$"Seeded application '{installedApplication.Code}' has no Freedom UI pages on environment '{environmentName}'. Add at least one tabbed record page to the seed application.");
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
