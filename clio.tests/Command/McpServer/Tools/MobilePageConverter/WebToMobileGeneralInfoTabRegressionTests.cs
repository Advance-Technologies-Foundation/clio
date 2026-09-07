using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.MobilePageConverter;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer.Tools.MobilePageConverter;

/// <summary>
/// ENG-94951 regression: the General-information tab of a tabbed Freedom UI record page, and everything the
/// page puts inside it, must land in the mobile template's general ("Details") tab content container — never
/// as bare children of the mobile <c>Tabs</c> panel.
/// <para>
/// The pinned page is the OOTB <c>Services_FormPage</c> (package <c>CrtCaseManagementApp</c>), captured with
/// <c>clio get-page</c> together with the merged view configs of its web template
/// (<c>PageWithTabsFreedomTemplate</c>) and of the recommended mobile template
/// (<c>MobilePageWithTabsFreedomTemplate</c>). It is the reported shape: the page removes the template's
/// <c>GeneralInfoTabContainer</c> and inserts its own content straight under the template-owned
/// <c>GeneralInfoTab</c>. Both templates are REAL fixtures rather than hand-written stubs because the defect
/// only exists in the presence of the web-template baseline — a hand-written bundle carries no inherited
/// chrome, so chrome subtraction never runs and the bug is unreproducible.
/// </para>
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class WebToMobileGeneralInfoTabRegressionTests {

	private const string FixtureName = "ServicesFormPageTabbed.live-snapshot.json";

	/// <summary>The mobile general tab's content grid — the "Details tab content container" of the ticket.</summary>
	private const string MobileGeneralTabContainer = "GeneralTabContainer";

	/// <summary>
	/// The mobile general TAB. It and its content grid are BOTH type-aligned twins of their web counterparts
	/// (GeneralInfoTab to GeneralInfoTab, GeneralInfoTabContainer to GeneralTabContainer), so identity is
	/// honest on both, and children go into the twin the web page actually put them in. A page that KEPT the
	/// template's content grid resolves its content through that grid's own pair; a page that REMOVED it has
	/// content directly in the tab, which is a crt.TabContainer and hosts items. The two shapes differ on web
	/// too — the removed grid was a two-column layout with its own gap — so the conversion carries the
	/// difference rather than normalising it into a container the page deleted.
	/// </summary>
	private const string MobileGeneralTab = "GeneralInfoTab";

	/// <summary>The mobile tab strip. Only <c>crt.TabContainer</c> children of it are ever rendered.</summary>
	private const string MobileTabsPanel = "Tabs";

	/// <summary>Page-authored content the web page places directly inside the template-owned general tab.</summary>
	/// <summary>The mobile component type an insert declares — it lives in <c>values.type</c>.</summary>
	private static string TypeOf(ViewConfigDiffOperation operation) =>
		operation?.Values?["type"]?.GetValue<string>();

	/// <summary>
	/// Every name the SOURCE page had: <c>sourceStructure</c> plus the <c>nameMap</c> source keys. A
	/// viewConfigDiff name in neither was synthesized by the converter.
	/// </summary>
	private static string[] SourceNames(MobilePageConversionGuide guide) =>
		[.. (guide.SourceStructure ?? []).Select(entry => entry.Name)
			.Concat((guide.NameMap ?? new Dictionary<string, string>()).Keys)
			.Where(name => !string.IsNullOrEmpty(name))];

	/// <summary>
	/// The SOURCE element name behind an operation, by reversing the published <c>nameMap</c>; the
	/// operation's own name when nothing renamed it. Null for a converter-synthesized operation.
	/// </summary>
	private static string SourceNameOf(MobilePageConversionGuide guide, ViewConfigDiffOperation operation) {
		if (guide?.NameMap is not null) {
			foreach (KeyValuePair<string, string> rename in guide.NameMap) {
				if (string.Equals(rename.Value, operation?.Name, StringComparison.Ordinal)) {
					return rename.Key;
				}
			}
		}
		// This fixture never asserts on a synthesized element, so an un-renamed operation answers with its
		// own name rather than being filtered against sourceStructure — the reverse nameMap above is the
		// part that matters here, and it is what a caller uses to find a RENAMED element.
		return operation?.Name;
	}

	private static readonly string[] GeneralTabContent = [
		"ServiceTeamMemberExpansionPanel", "ServicePactExpansionPanel"
	];

	/// <summary>
	/// Leaf content nested INSIDE that page-authored content. The reported defect was total loss of the tab,
	/// so a conversion that re-parents the panels correctly but drops what they hold is still the bug.
	/// </summary>
	private static readonly string[] GeneralTabLeafContent = ["ServiceTeamMemberList", "ServicePactList"];

	[Test]
	[Description("ENG-94951: content the web page puts directly inside the template-owned GeneralInfoTab is converted into the mobile general TAB - a crt.TabContainer, which hosts items - rather than being emitted as a bare child of the mobile Tabs panel, and the content nested inside it survives too.")]
	public void Analyze_ShouldPlaceGeneralInfoTabContent_IntoTheMobileGeneralTab() {
		// Arrange
		JsonObject fixture = LoadFixture();

		// Act
		MobilePageConversionGuide guide = Convert(fixture);

		// Assert
		RequireReproductionShape(fixture, guide);
		foreach (string name in GeneralTabContent) {
			ViewConfigDiffOperation entry = Element(guide, name);
			entry.Operation.Should().Be("insert",
				because: $"'{name}' is page-authored content and must reach the mobile page");
			entry.ParentName.Should().Be(MobileGeneralTab,
				because: $"'{name}' sits directly under the web general tab on this page, and that tab is a "
					+ "type-aligned twin that hosts items, so its children stay in it; parenting it to the Tabs "
					+ "panel instead puts a non-tab child inside a crt.TabPanel, which renders nothing and is "
					+ "exactly ENG-94951");
		}
		foreach (string name in GeneralTabLeafContent) {
			Element(guide, name).Operation.Should().Be("insert",
				because: $"the tab is not recovered if its panels arrive empty — '{name}' is the content the "
					+ "user actually came for, so the leaf, not only its wrapper, must reach the mobile page");
		}
	}

	[Test]
	[Description("ENG-94951: no converted element is ever parented straight to a mobile tab strip unless it is itself a tab — a crt.TabPanel accepts only crt.TabContainer children.")]
	public void Analyze_ShouldParentOnlyTabs_ToEveryMobileTabStrip() {
		// Arrange
		JsonObject fixture = LoadFixture();

		// Act
		MobilePageConversionGuide guide = Convert(fixture);

		// Assert
		IReadOnlyList<ViewConfigDiffOperation> offenders = NonTabChildrenOfTabStrips(guide);
		offenders.Should().BeEmpty(
			because: "a mobile tab strip is a crt.TabPanel: anything but a crt.TabContainer inserted into it is "
				+ "invisible in the mobile designer and lost from the converted page, which is how the "
				+ "General-information content disappeared. Offending entries: "
				+ string.Join(", ", offenders.Select(e => $"{SourceNameOf(guide, e)}({TypeOf(e)})->{e.ParentName}")));
	}

	[Test]
	[Description("ENG-94951: a web tab the PAGE authored still converts into its own mobile tab under the strip — the fix must not collapse every tab onto the general one.")]
	public void Analyze_ShouldConvertAPageAuthoredTab_IntoItsOwnMobileTab() {
		// Arrange
		JsonObject fixture = LoadFixture();

		// Act
		MobilePageConversionGuide guide = Convert(fixture);

		// Assert
		ViewConfigDiffOperation tab = Element(guide, "CaseHistoryTab");
		tab.Operation.Should().Be("insert",
			because: "a tab the page added has no mobile counterpart, so it is created rather than merged");
		TypeOf(tab).Should().Be("crt.TabContainer",
			because: "only a crt.TabContainer may be a child of the crt.TabPanel it is inserted into");
		tab.ParentName.Should().Be(MobileTabsPanel,
			because: "a converted web tab becomes a new tab of the mobile strip, beside the template's own tabs");
	}

	[Test]
	[Description("ENG-94951 over-correction guard: the mobile general tab and its content container are template twins, so the converter merges onto them; a fix that re-declared either one under Tabs would duplicate what the template already provides. This does NOT fail on the unfixed code — there the tab was dropped — it pins the shape of the fix.")]
	public void Analyze_ShouldMergeTheGeneralTab_RatherThanRecreateIt() {
		// Arrange
		JsonObject fixture = LoadFixture();

		// Act
		MobilePageConversionGuide guide = Convert(fixture);

		// Assert
		guide.ViewConfigDiff.Should().NotContain(
			e => e.Operation == "insert" && string.Equals(e.Name, "GeneralInfoTab", StringComparison.OrdinalIgnoreCase),
			because: "the mobile template already provides the general tab; inserting a second one under Tabs "
				+ "would duplicate it");
		guide.ViewConfigDiff.Should().NotContain(
			e => e.Operation == "insert"
				&& string.Equals(e.Name, MobileGeneralTabContainer, StringComparison.OrdinalIgnoreCase),
			because: "the mobile template already provides the general tab's content grid — it is a merge "
				+ "target, never an insert");
		guide.ResourceStrings.Should().NotContainKey("GeneralInfoTab_caption",
			because: "the container twin carries no caption, so the mobile template's own 'Details' must stand "
				+ "instead of the web page's caption overwriting it");
	}

	[Test]
	[Description("ENG-94951: a page that KEEPS the web template's GeneralInfoTabContainer converts identically — the container is chrome-subtracted and its children are hoisted into the mapped tab, so ONE containers entry covers both page shapes without a second web name on the same mobile name.")]
	public void Analyze_ShouldPlaceGeneralInfoTabContent_WhenThePageKeepsTheTemplateContentGrid() {
		// Arrange — the fixture's own shape with the template's content grid put back around the page's content,
		// which is what an ordinary tabbed page (one that did not remove it) looks like.
		JsonObject fixture = LoadFixture();
		JsonObject generalTab = FindNode(fixture["page"]!["viewConfig"]!, "GeneralInfoTab")
			?? throw new AssertionException("fixture no longer carries GeneralInfoTab");
		JsonArray tabItems = generalTab["items"]!.AsArray();
		var restored = new JsonArray();
		foreach (JsonNode child in tabItems.ToList()) {
			tabItems.Remove(child);
			restored.Add(child);
		}
		tabItems.Add(new JsonObject {
			["name"] = "GeneralInfoTabContainer", ["type"] = "crt.GridContainer", ["items"] = restored
		});

		// Act
		MobilePageConversionGuide guide = Convert(fixture);

		// Assert
		foreach (string name in GeneralTabContent) {
			Element(guide, name).ParentName.Should().Be(MobileGeneralTabContainer,
				because: $"'{name}' sits inside the template's content grid here, and that grid is a type-aligned "
					+ "twin of the mobile general tab's grid, so the content lands in the grid rather than in the "
					+ "tab body");
		}
		NonTabChildrenOfTabStrips(guide).Should().BeEmpty(
			because: "the shape a page keeps by default must not reintroduce the loss the ticket is about");
		// Asserted on the TARGET name: the page's own content grid and the template's are the same element
		// under two names, and viewConfigDiff addresses it by the mobile one. NOTE the count is not pinned
		// here — this fixture produces TWO merges onto GeneralTabContainer (a container-map twin and the
		// general-tab twin), which the old elementMap told apart by webName but a caller would have applied
		// twice either way. That duplicate is pre-existing and out of scope here; it is reported separately.
		guide.ViewConfigDiff.Should().Contain(o => o.Operation == "merge" && o.Name == MobileGeneralTabContainer,
			because: "the page reuses the template's grid, so it merges onto it");
		guide.ViewConfigDiff.Should().NotContain(o => o.Operation == "insert" && o.Name == MobileGeneralTabContainer,
			because: "reusing the template's grid means never inserting a second one — that is the whole point");
	}

	[Test]
	[Description("A container twin the mobile template provides is a SIBLING of the inserts placed into its grid: a mobile crt.GridContainer places children by layoutConfig alone, so the twin must be placed too, contiguously and exactly once — an unplaced twin among placed siblings is not rendered at all.")]
	public void Analyze_ShouldPlaceTheTemplateTwin_BesideTheContentReHomedIntoItsGrid() {
		// Arrange
		JsonObject fixture = LoadFixture();

		// Act
		MobilePageConversionGuide guide = Convert(fixture);

		// Assert
		AdaptiveLayoutGroup grid = guide.AdaptiveLayout
			.Single(g => g.Items.Any(i => i.Name == "AreaProfileContainer"));
		IReadOnlyList<string> placed = [.. grid.Items.Select(i => i.Name)];
		placed.Should().Equal(
			["AreaProfileContainer", "TermsContainer"],
			because: "the template's profile card is the general tab grid's first child and the wrapper's other "
				+ "non-tab content follows it; a gap or a repeat means a phantom child took a row. The general "
				+ "tab's own content is NOT here on this page — it removed the template's content grid, so it "
				+ "stays in the tab body");
		grid.Items.Select(i => i.LayoutConfigAdaptive!["small"]!["row"]!.GetValue<int>())
			.Should().Equal([1, 2],
				because: "rows must be contiguous — the mobile grid does not auto-place, so a skipped row is a "
					+ "child that was counted but never rendered");
		Element(guide, "SideAreaProfileContainer").Values!.AsObject()
			.Should().ContainKey("layoutConfig",
				because: "the twin is a merge, but without a layoutConfig it is the one unplaced child of a grid "
					+ "whose every other child got a cell, and the mobile designer renders nothing for it");
	}

	[Test]
	[Description("A twin is placed only where the MOBILE template holds it: web Tabs sits inside CardContentWrapper (mapped to GeneralTabContainer) while on mobile GeneralTabContainer sits inside Tabs, so trusting the web nesting would place the tab strip inside its own descendant — and twice, since two web twins share the mobile name.")]
	public void Analyze_ShouldNotPlaceATwin_WhereOnlyTheWebTreeNestsIt() {
		// Arrange
		JsonObject fixture = LoadFixture();

		// Act
		MobilePageConversionGuide guide = Convert(fixture);

		// Assert
		foreach (AdaptiveLayoutGroup group in guide.AdaptiveLayout) {
			group.Items.Should().NotContain(i => i.Name == MobileTabsPanel,
				because: "the mobile tab strip contains the general tab's grid, not the other way round");
			group.Items.Select(i => i.Name).Should().OnlyHaveUniqueItems(
				because: "two web twins may share one mobile name; placing both would give the same element two cells");
		}
	}

	[Test]
	[Description("Twin placement must not depend on the template declaring positional (:top/:bottom) entries: only the tabbed template has any, so a mobile-parent map supplied only alongside them would leave the fix dead for every other template family.")]
	public void Analyze_ShouldPlaceTheTemplateTwin_EvenWithNoPositionalPlacements() {
		// Arrange
		JsonObject fixture = LoadFixture();

		// Act
		MobilePageConversionGuide guide = Convert(fixture, withPositionalPlacements: false);

		// Assert
		Element(guide, "SideAreaProfileContainer").Values!.AsObject().Should().ContainKey("layoutConfig",
			because: "the mobile-parent map the placement reads is a property of the mobile TEMPLATE, not of the "
				+ "positional rules — gating one on the other is what made this dead for five of six families");
	}

	[Test]
	[Description("Each source shape converts to where the WEB page put its content, and the two shapes differ on purpose: a page that KEPT the template's content grid resolves through that grid's own containers pair, and a page that REMOVED it keeps its content in the tab body. The removed grid is a two-column layout with its own gap, so the web pages differ too -- normalising them into one mobile tree would override a layout decision the developer made.")]
	public void Analyze_ShouldPlaceContentWhereTheWebPagePutIt_ForBothSourceShapes() {
		// Arrange - the pinned page (grid REMOVED) and the same page with the template's grid put back.
		JsonObject removedGrid = LoadFixture();
		JsonObject keptGrid = WithTemplateContentGridRestored(LoadFixture());

		// Act
		MobilePageConversionGuide fromRemoved = Convert(removedGrid);
		MobilePageConversionGuide fromKept = Convert(keptGrid);

		// Assert
		foreach (string name in GeneralTabContent) {
			Element(fromRemoved, name).ParentName.Should().Be(MobileGeneralTab,
				because: $"the page removed the template's content grid and put '{name}' straight in the tab, "
					+ "so the tab is where it belongs on mobile as well");
			Element(fromKept, name).ParentName.Should().Be(MobileGeneralTabContainer,
				because: $"with the grid present '{name}' is its child, and that grid has a containers pair of "
					+ "its own — no separate redirect on the tab is involved");
		}
		NonTabChildrenOfTabStrips(fromRemoved).Should().BeEmpty(
			because: "neither shape may leave a non-tab child in the strip — that is the invariant ENG-94951 broke");
		NonTabChildrenOfTabStrips(fromKept).Should().BeEmpty(
			because: "and it holds for the ordinary shape too");
	}

	[Test]
	[Description("The DEFAULT resolution, with no rules entry involved at all: strip both general-tab containers entries and the page still converts into the mobile general tab's grid. This is the class of the defect rather than its instance -- the rules file is CDN-fetched, so a published file that loses an entry must not be able to reproduce ENG-94951 on a user's machine.")]
	public void Analyze_ShouldReHomeTabStripChildren_WhenNoContainersEntryMapsTheGeneralTab() {
		// Arrange
		JsonObject fixture = LoadFixture();

		// Act
		MobilePageConversionGuide guide = Convert(fixture, RulesWithoutTheGeneralTabEntries());

		// Assert
		foreach (string name in GeneralTabContent) {
			Element(guide, name).ParentName.Should().Be(MobileGeneralTabContainer,
				because: $"'{name}' would otherwise be hoisted into the tab strip and render as nothing; the walk "
					+ "carries the nearest ancestor that can hold arbitrary children, and for this template that "
					+ "is the general tab's grid");
		}
		NonTabChildrenOfTabStrips(guide).Should().BeEmpty(
			because: "the invariant must hold for ANY rules file, not only for one whose containers list "
				+ "happens to be complete");
	}

	[Test]
	[Description("A tab keeps its strip even though a crt.TabPanel is absent from contentContainerTypes. The exemption is what stops the re-homing rule from dismantling every converted tab, and it is read from the rules' tabAreaLayers.tabComponentType rather than from a constant in the analyser.")]
	public void Analyze_ShouldExemptTabs_FromTheReHomingRule() {
		// Arrange
		JsonObject fixture = LoadFixture();

		// Act
		MobilePageConversionGuide guide = Convert(fixture, RulesWithoutTheGeneralTabEntries());

		// Assert
		guide.ViewConfigDiff.Where(e => e.Operation == "insert" && TypeOf(e) == "crt.TabContainer")
			.Should().OnlyContain(e => e.ParentName == MobileTabsPanel,
				because: "a tab belongs to a strip -- the one receiver outside the accept-list that is correct");
	}

	// ── helpers ──────────────────────────────────────────────────────────────────────────────────

	/// <summary>
	/// The anti-vacuity floor, asserted by <see cref="Convert"/> on EVERY conversion of the pinned capture,
	/// including the variants some tests build from it (the template grid put back, the component types
	/// renamed). It states only what every variant keeps: the page has a tab strip, the general tab inside it,
	/// and the two named panels somewhere. A refreshed capture that lost any of them would otherwise leave most
	/// assertions in this fixture trivially true instead of failing. A hand-built bundle is skipped — there is
	/// no pinned shape to guard.
	/// </summary>
	private static void RequireFixtureIsUsable(JsonObject fixture, MobilePageConversionGuide guide) {
		if (fixture["page"]?["viewConfig"] is null || FindNode(fixture["page"]!["viewConfig"]!, "Tabs") is null) {
			return;
		}
		IReadOnlyDictionary<string, string> parents = WebParents(fixture);
		parents.Should().ContainKey("GeneralInfoTab",
			because: "every variant of this capture keeps the template-owned general tab; without it the whole "
				+ "fixture stops reproducing anything and its assertions go quietly true");
		foreach (string name in GeneralTabContent) {
			parents.Should().ContainKey(name,
				because: $"'{name}' is the content whose loss this suite is about — a capture without it cannot "
					+ "fail any of these tests for the right reason");
		}
		guide.ViewConfigDiff.Should().NotBeEmpty(because: "an empty element map would make every assertion vacuous");
	}

	/// <summary>
	/// The STRICT guard: the capture is still the REPORTED shape — the page removed the web template's own
	/// content grid and put its content directly under the tab. Opt-in, because several tests deliberately
	/// build a different variant from the same capture and would fail this by design.
	/// </summary>
	private static void RequireReproductionShape(JsonObject fixture, MobilePageConversionGuide guide) {
		IReadOnlyDictionary<string, string> parents = WebParents(fixture);
		parents.Should().ContainKey("GeneralInfoTab",
			because: "the reproduction is a page whose template-owned general tab is present in the merged tree");
		parents["GeneralInfoTab"].Should().Be(MobileTabsPanel,
			because: "the general tab must sit inside the tab strip for the hoist to land in a crt.TabPanel");
		parents.Should().NotContainKey("GeneralInfoTabContainer",
			because: "this fixture is the reported shape — the page REMOVED the template's content grid and put "
				+ "its content directly under the tab; the kept-grid shape has its own test");
		foreach (string name in GeneralTabContent) {
			parents.Should().ContainKey(name)
				.WhoseValue.Should().Be("GeneralInfoTab",
					because: $"'{name}' must still sit directly under the template-owned general tab, otherwise "
						+ "this capture no longer reproduces the report and the expectations need re-deriving");
		}
		MobileTemplateTypes(fixture).Should().Contain(
			kv => kv.Key == MobileTabsPanel && kv.Value == "crt.TabPanel",
			because: "the whole suite depends on the mobile template declaring Tabs as a tab strip");
		guide.ViewConfigDiff.Should().NotBeEmpty(because: "an empty element map would make every assertion vacuous");
	}

	/// <summary>
	/// Inserts parented to a tab strip that are not tabs themselves. The strip set is derived from the guide
	/// alone — any parent at least one <c>crt.TabContainer</c> insert targets IS a strip — deliberately NOT by
	/// calling the converter's own pass, so this re-states the invariant instead of re-running the implementation.
	/// </summary>
	private static IReadOnlyList<ViewConfigDiffOperation> NonTabChildrenOfTabStrips(MobilePageConversionGuide guide) {
		HashSet<string> strips = new(StringComparer.OrdinalIgnoreCase) { MobileTabsPanel };
		strips.UnionWith(guide.ViewConfigDiff
			.Where(e => e.Operation == "insert"
				&& string.Equals(TypeOf(e), "crt.TabContainer", StringComparison.OrdinalIgnoreCase)
				&& !string.IsNullOrEmpty(e.ParentName))
			.Select(e => e.ParentName));
		return [.. guide.ViewConfigDiff.Where(e => e.Operation == "insert"
			&& !string.IsNullOrEmpty(e.ParentName)
			&& strips.Contains(e.ParentName)
			&& !string.Equals(TypeOf(e), "crt.TabContainer", StringComparison.OrdinalIgnoreCase))];
	}

	/// <summary>
	/// Puts the web template's own <c>GeneralInfoTabContainer</c> back around the page's general-tab content —
	/// the ordinary shape, which the pinned page (the reported one) removed.
	/// </summary>
	private static JsonObject WithTemplateContentGridRestored(JsonObject fixture) {
		JsonObject generalTab = FindNode(fixture["page"]!["viewConfig"]!, "GeneralInfoTab")
			?? throw new AssertionException("fixture no longer carries GeneralInfoTab");
		JsonArray tabItems = generalTab["items"]!.AsArray();
		var restored = new JsonArray();
		foreach (JsonNode child in tabItems.ToList()) {
			tabItems.Remove(child);
			restored.Add(child);
		}
		tabItems.Add(new JsonObject {
			["name"] = "GeneralInfoTabContainer", ["type"] = "crt.GridContainer", ["items"] = restored
		});
		return fixture;
	}

	/// <summary>
	/// The shipped rules with BOTH general-tab containers entries removed — the tabbed template exactly as it
	/// was before this branch, which is also the shape a published rules file would have if it lost them.
	/// </summary>
	private static WebToMobilePageConversionRules RulesWithoutTheGeneralTabEntries() {
		JsonObject rules = JsonNode.Parse(BundledRulesJson())!.AsObject();
		JsonArray containers = rules["templates"]!.AsArray()
			.Single(t => t!["web"]!.ToString() == "PageWithTabsFreedomTemplate")!["containers"]!.AsArray();
		foreach (JsonNode entry in containers.ToList()) {
			string web = entry!["web"]!.ToString();
			if (web is "GeneralInfoTab" or "GeneralInfoTabContainer") {
				containers.Remove(entry);
			}
		}
		using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(rules.ToJsonString()));
		return WebToMobilePageConversionRulesCatalog.ParseStream(stream);
	}

	private static JsonObject LoadFixture() {
		string path = Path.Combine(
			TestContext.CurrentContext.TestDirectory, "Command", "McpServer", "Fixtures", FixtureName);
		return JsonNode.Parse(File.ReadAllText(path))!.AsObject();
	}

	private static string BundledRulesJson() {
		using Stream stream = typeof(WebToMobilePageConversionRulesCatalog).Assembly
			.GetManifestResourceStream(WebToMobilePageConversionRulesCatalog.BundledResourceName)!;
		using var reader = new StreamReader(stream);
		return reader.ReadToEnd();
	}

	/// <summary>
	/// Runs the conversion over the three inputs the defect depends on: the rules, the web template's baseline
	/// names/nodes (chrome subtraction) and the mobile template's component types. The component REGISTRIES
	/// (<c>webByType</c> / <c>mobileByType</c>) and the mobile container-parent map used for positional
	/// placement are deliberately stubbed — they play no part in this defect — so this is not a full
	/// reproduction of <see cref="MobilePageConversionGuideTool"/>'s call. The mobile template's PARENT map is
	/// supplied, because the adaptive pass reads it to decide where a container twin may be placed.
	/// </summary>
	private static MobilePageConversionGuide Convert(
		JsonObject fixture,
		WebToMobilePageConversionRules overrideRules = null,
		bool mobileTemplateAvailable = true,
		bool withPositionalPlacements = true,
		IReadOnlySet<string> mobileTypes = null) {
		JsonObject page = fixture["page"]!.AsObject();
		JsonArray webTemplateViewConfig = fixture["webTemplate"]!["viewConfig"]!.DeepClone().AsArray();

		// The analysis mutates the bundle it is given, so each run gets its own copy of the pinned page.
		var bundle = new PageBundleInfo {
			ViewConfig = page["viewConfig"]!.DeepClone().AsArray(),
			ViewModelConfig = page["viewModelConfig"]?.DeepClone().AsObject() ?? new JsonObject(),
			ModelConfig = page["modelConfig"]?.DeepClone().AsObject() ?? new JsonObject(),
			Resources = new PageResourceInfo {
				Strings = page["resources"]?["strings"]?.DeepClone().AsObject() ?? new JsonObject()
			}
		};

		WebToMobilePageConversionRules rules = overrideRules ?? WebToMobilePageConversionRulesCatalog.LoadBundled();
		TemplateMappingRule templateRule = rules.Templates!
			.Single(t => string.Equals(t.Web, "PageWithTabsFreedomTemplate", StringComparison.OrdinalIgnoreCase));
		Dictionary<string, JObject> webBaselineNodes =
			WebToMobileAnalysisService.CollectComponentNodesByName(webTemplateViewConfig);

		MobilePageConversionGuide guide = WebToMobileAnalysisService.Analyze(
			bundle, mobileTypes ?? MobileTypes(), new HashSet<string>(StringComparer.OrdinalIgnoreCase),
			webByType: new Dictionary<string, ComponentRegistryEntry>(StringComparer.OrdinalIgnoreCase),
			mobileByType: null, rules, templateRule,
			sourcePage: "Services_FormPage", sourceTemplate: "PageWithTabsFreedomTemplate",
			suggestedTarget: "Services_MobileFormPage",
			containerNameMap: MobilePageConversionGuideTool.BuildContainerNameMap(templateRule),
			componentNameMap: MobilePageConversionGuideTool.BuildComponentNameMap(templateRule),
			positionalPlacements: withPositionalPlacements
				? MobilePageConversionGuideTool.BuildPositionalPlacements(templateRule)
				: null,
			templateComponentNames: new HashSet<string>(webBaselineNodes.Keys, StringComparer.OrdinalIgnoreCase),
			mobileContainerParents: mobileTemplateAvailable
				? WebToMobileAnalysisService.CollectParentByName(
					fixture["mobileTemplate"]!["viewConfig"]!.DeepClone().AsArray())
				: null,
			mobileTemplateTypesByName: mobileTemplateAvailable ? MobileTemplateTypes(fixture) : null,
			webTemplateBaselineNodes: webBaselineNodes);
		RequireFixtureIsUsable(fixture, guide);
		return guide;
	}

	/// <summary>Component name → type of the recommended mobile template, as the tool's probe supplies it.</summary>
	private static Dictionary<string, string> MobileTemplateTypes(JsonObject fixture) =>
		WebToMobileAnalysisService.CollectComponentTypesByName(
			fixture["mobileTemplate"]!["viewConfig"]!.DeepClone().AsArray());

	/// <summary>
	/// The mobile component types the conversion resolves against, read from the pinned live registry snapshot
	/// so the test states what the platform actually supports rather than a set curated to make it pass.
	/// </summary>
	private static IReadOnlySet<string> MobileTypes() {
		string path = Path.Combine(
			TestContext.CurrentContext.TestDirectory, "Command", "McpServer", "Fixtures",
			"MobileComponentRegistry.live-snapshot.json");
		JsonObject registry = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
		var types = new HashSet<string>(
			registry["components"]!.AsArray()
				.Select(c => c!["componentType"]?.ToString())
				.Where(t => !string.IsNullOrWhiteSpace(t))!,
			StringComparer.OrdinalIgnoreCase);
		types.Contains("crt.ExpansionPanel").Should().BeTrue(
			because: "the pinned general-tab content is expansion panels — with the type unsupported they "
				+ "would drop for an unrelated reason and every assertion here would be vacuous");
		return types;
	}

	private static ViewConfigDiffOperation Element(MobilePageConversionGuide guide, string webName) {
		IReadOnlyList<ViewConfigDiffOperation> matches = [.. guide.ViewConfigDiff
			.Where(e => string.Equals(SourceNameOf(guide, e), webName, StringComparison.OrdinalIgnoreCase))];
		matches.Should().ContainSingle(
			because: $"'{webName}' must appear in viewConfigDiff exactly once; found "
				+ (matches.Count == 0
					? "none. Operations: "
						+ string.Join(", ", guide.ViewConfigDiff.Select(o => $"{o.Operation}->{o.Name}"))
						+ ". nameMap: "
						+ (guide.NameMap is null
							? "(null)"
							: string.Join(", ", guide.NameMap.Select(kv => $"{kv.Key}=>{kv.Value}")))
					: string.Join(", ", matches.Select(m => $"{m.Operation}->{m.Name}"))));
		return matches[0];
	}

	/// <summary>The web parent each named component sits under in the pinned page's merged view config.</summary>
	private static IReadOnlyDictionary<string, string> WebParents(JsonObject fixture) {
		var parents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		CollectParents(fixture["page"]!["viewConfig"]!, null, parents);
		return parents;
	}

	private static JsonObject FindNode(JsonNode node, string name) {
		switch (node) {
			case JsonArray array:
				return array.Where(i => i is not null).Select(i => FindNode(i!, name)).FirstOrDefault(f => f is not null);
			case JsonObject obj:
				if (string.Equals(obj["name"]?.ToString(), name, StringComparison.OrdinalIgnoreCase)) {
					return obj;
				}
				return obj.Where(p => p.Value is JsonArray)
					.Select(p => FindNode(p.Value!, name)).FirstOrDefault(f => f is not null);
			default:
				return null;
		}
	}

	private static void CollectParents(JsonNode node, string parent, IDictionary<string, string> parents) {
		switch (node) {
			case JsonArray array:
				foreach (JsonNode item in array.Where(i => i is not null)) {
					CollectParents(item!, parent, parents);
				}
				break;
			case JsonObject obj: {
				string name = obj["name"]?.ToString();
				string childParent = string.IsNullOrWhiteSpace(name) ? parent : name;
				if (!string.IsNullOrWhiteSpace(name)) {
					parents[name] = parent;
				}
				foreach (KeyValuePair<string, JsonNode> pair in obj.Where(p => p.Value is JsonArray)) {
					CollectParents(pair.Value!, childParent, parents);
				}
				break;
			}
		}
	}
}
