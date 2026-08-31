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

	/// <summary>The mobile tab strip. Only <c>crt.TabContainer</c> children of it are ever rendered.</summary>
	private const string MobileTabsPanel = "Tabs";

	/// <summary>Substring identifying the tab-strip loss report among the guide's constraints.</summary>
	private const string TabStripLossReport = "INVISIBLE in Mobile Designer";

	/// <summary>Page-authored content the web page places directly inside the template-owned general tab.</summary>
	private static readonly string[] GeneralTabContent = [
		"ServiceTeamMemberExpansionPanel", "ServicePactExpansionPanel"
	];

	/// <summary>
	/// Leaf content nested INSIDE that page-authored content. The reported defect was total loss of the tab,
	/// so a conversion that re-parents the panels correctly but drops what they hold is still the bug.
	/// </summary>
	private static readonly string[] GeneralTabLeafContent = ["ServiceTeamMemberList", "ServicePactList"];

	[Test]
	[Description("ENG-94951: content the web page puts inside the template-owned GeneralInfoTab is converted into the mobile general tab's content container, not emitted as a bare child of the mobile Tabs panel, and the content nested inside it survives too.")]
	public void Analyze_ShouldPlaceGeneralInfoTabContent_IntoTheMobileGeneralTabContainer() {
		// Arrange
		JsonObject fixture = LoadFixture();

		// Act
		MobilePageConversionGuide guide = Convert(fixture);

		// Assert
		RequireReproductionShape(fixture, guide);
		foreach (string name in GeneralTabContent) {
			ElementMapEntry entry = Element(guide, name);
			entry.Operation.Should().Be("insert",
				because: $"'{name}' is page-authored content and must reach the mobile page");
			entry.ParentName.Should().Be(MobileGeneralTabContainer,
				because: $"'{name}' lives in the web General-information tab, so it belongs in the mobile "
					+ "template's general tab content container; parenting it to the Tabs panel puts a "
					+ "non-tab child inside a crt.TabPanel, which renders nothing and is exactly ENG-94951");
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
		RequireReproductionShape(fixture, guide);
		IReadOnlyList<ElementMapEntry> offenders = NonTabChildrenOfTabStrips(guide);
		offenders.Should().BeEmpty(
			because: "a mobile tab strip is a crt.TabPanel: anything but a crt.TabContainer inserted into it is "
				+ "invisible in the mobile designer and lost from the converted page, which is how the "
				+ "General-information content disappeared. Offending entries: "
				+ string.Join(", ", offenders.Select(e => $"{e.WebName}({e.MobileType})->{e.ParentName}")));
	}

	[Test]
	[Description("ENG-94951: a web tab the PAGE authored still converts into its own mobile tab under the strip — the fix must not collapse every tab onto the general one.")]
	public void Analyze_ShouldConvertAPageAuthoredTab_IntoItsOwnMobileTab() {
		// Arrange
		JsonObject fixture = LoadFixture();

		// Act
		MobilePageConversionGuide guide = Convert(fixture);

		// Assert
		RequireReproductionShape(fixture, guide);
		ElementMapEntry tab = Element(guide, "CaseHistoryTab");
		tab.Operation.Should().Be("insert",
			because: "a tab the page added has no mobile counterpart, so it is created rather than merged");
		tab.MobileType.Should().Be("crt.TabContainer",
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
		RequireReproductionShape(fixture, guide);
		guide.ElementMap.Should().NotContain(
			e => e.Operation == "insert" && string.Equals(e.MobileName, "GeneralInfoTab", StringComparison.OrdinalIgnoreCase),
			because: "the mobile template already provides the general tab; inserting a second one under Tabs "
				+ "would duplicate it");
		guide.ElementMap.Should().NotContain(
			e => e.Operation == "insert"
				&& string.Equals(e.MobileName, MobileGeneralTabContainer, StringComparison.OrdinalIgnoreCase),
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
				because: $"'{name}' sits one level deeper here, but the extra container is inherited web-template "
					+ "chrome: it is subtracted and its children hoisted into the mapped tab, which resolves to "
					+ "the same mobile grid");
		}
		NonTabChildrenOfTabStrips(guide).Should().BeEmpty(
			because: "the shape a page keeps by default must not reintroduce the loss the ticket is about");
		guide.ElementMap
			.Count(e => string.Equals(e.MobileName, MobileGeneralTabContainer, StringComparison.OrdinalIgnoreCase)
				&& string.Equals(e.WebName, "GeneralInfoTabContainer", StringComparison.OrdinalIgnoreCase))
			.Should().Be(0,
				because: "mapping GeneralInfoTabContainer onto the same mobile name as GeneralInfoTab would add a "
					+ "second twin for one mobile element and make every by-MobileName lookup ambiguous — the "
					+ "single GeneralInfoTab entry already covers this shape");
	}

	[Test]
	[Description("ENG-94951 A/B negative half: with the SHIPPED rules the tab-strip loss report must NOT appear — a false positive tells the model to relocate correct entries and report a defect that does not exist.")]
	public void Analyze_ShouldNotReportTabStripLoss_WithTheShippedRules() {
		// Arrange
		JsonObject fixture = LoadFixture();

		// Act
		MobilePageConversionGuide guide = Convert(fixture);

		// Assert
		guide.TabStripPlacementLosses.Should().BeNull(
			because: "the shipped rules place the general tab's content correctly, so reporting a lost subtree "
				+ "would be guidance the model acts on — corrupting a conversion that was already right");
		guide.Constraints.Should().NotContain(c => c.Contains(TabStripLossReport),
			because: "the rendered sentence must follow the typed field, never outlive it");
	}

	[Test]
	[Description("ENG-94951 guard: when the rules file carries NO containers entry for the web general-information tab, the loss is no longer silent — the guide reports the non-tab child of the mobile tab strip by name so the defect is visible in the report alone.")]
	public void Analyze_ShouldReportContentInsertedStraightIntoTheMobileTabStrip_WhenTheRulesLackTheGeneralTab() {
		// Arrange
		JsonObject fixture = LoadFixture();

		// Act
		MobilePageConversionGuide guide = Convert(fixture, RulesWithoutTheGeneralTabEntry());

		// Assert
		guide.TabStripPlacementLosses.Should().ContainSingle(l => l.Name == "ServiceTeamMemberExpansionPanel"
				&& l.MobileType == "crt.ExpansionPanel" && l.ParentName == MobileTabsPanel,
			because: "the loss is reported as data the caller can act on, not only as prose it must parse");
		string report = guide.Constraints.Should().ContainSingle(c => c.Contains(TabStripLossReport),
			because: "a subtree the mobile designer renders as nothing must be named in the report; without this "
				+ "line a lost general-information tab is indistinguishable from a page that never had one")
			.Subject;
		report.Should().Contain("ServiceTeamMemberExpansionPanel",
			because: "the report names the elements that are lost, so the defect is actionable without a debugger");
		JsonSerializer.Deserialize<MobilePageConversionGuide>(JsonSerializer.Serialize(guide))!
			.Constraints.Should().Contain(c => c.Contains(TabStripLossReport),
				because: "the constraint reaches the model over the wire, so it must survive serialization intact "
					+ "rather than only existing as an in-memory string");
	}

	[Test]
	[Description("ENG-94951 guard degradation: the tab-strip loss report still fires when the mobile template could not be read — that degraded run is exactly the one where a runtime-fetched rules file missing the entry would otherwise go unnoticed.")]
	public void Analyze_ShouldReportTabStripLoss_WhenTheMobileTemplateIsUnavailable() {
		// Arrange — LoadMobileTemplateProbe's failure shape: an EMPTY mobile type map. Chrome subtraction is
		// driven by the web baseline and the rules alone, so the loss still happens without the mobile template.
		JsonObject fixture = LoadFixture();

		// Act
		MobilePageConversionGuide guide = Convert(
			fixture, RulesWithoutTheGeneralTabEntry(), mobileTemplateAvailable: false);

		// Assert
		guide.ElementMap.Should().Contain(
			e => e.Operation == "insert" && string.Equals(e.ParentName, MobileTabsPanel, StringComparison.OrdinalIgnoreCase),
			because: "the precondition of this test is that the loss still occurs without the mobile template");
		guide.TabStripPlacementLosses.Should().NotBeNullOrEmpty(
			because: "the mobile-template probe is best-effort; a report that disappears together with it is no "
				+ "guard at all, and this is the compound failure the guard exists for");
	}

	[Test]
	[Description("ENG-94951 is not specific to the tabbed template: a page that AUTHORS its own crt.TabPanel and puts a non-tab child under it is reported too, through the insert side of the tab-strip detection rather than the mobile template's type map.")]
	public void Analyze_ShouldReportNonTabChild_OfAPageAuthoredTabPanel() {
		// Arrange — no template baseline and no mobile template: only the page's own tab strip is in play. Every
		// container carries real content on purpose: an empty one is removed by the empty-container pass before
		// the report runs, which would make this test pass for the wrong reason.
		var bundle = new PageBundleInfo {
			ViewConfig = JsonNode.Parse("""
				[ { "name": "MainContainer", "type": "crt.GridContainer", "items": [
					{ "name": "UsrTabs", "type": "crt.TabPanel", "items": [
						{ "name": "UsrTab", "type": "crt.TabContainer", "items": [
							{ "name": "UsrTabLabel", "type": "crt.Label", "caption": "Tab" } ] },
						{ "name": "UsrStrayPanel", "type": "crt.ExpansionPanel", "items": [
							{ "name": "UsrStrayLabel", "type": "crt.Label", "caption": "Stray" } ] } ] } ] } ]
				""")!.AsArray(),
			ViewModelConfig = new JsonObject(), ModelConfig = new JsonObject(),
			Resources = new PageResourceInfo { Strings = new JsonObject() }
		};

		// Act
		MobilePageConversionGuide guide = WebToMobileAnalysisService.Analyze(
			bundle, MobileTypes(), new HashSet<string>(StringComparer.OrdinalIgnoreCase),
			webByType: new Dictionary<string, ComponentRegistryEntry>(StringComparer.OrdinalIgnoreCase),
			mobileByType: null, WebToMobilePageConversionRulesCatalog.LoadBundled(), templateRule: null,
			sourcePage: "Usr_FormPage", sourceTemplate: "BlankPageTemplate",
			suggestedTarget: "Usr_MobileFormPage",
			containerNameMap: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

		// Assert
		guide.TabStripPlacementLosses.Should().ContainSingle(l => l.Name == "UsrStrayPanel",
			because: "the invariant is a property of crt.TabPanel itself, not of the tabbed template — a strip "
				+ "the page authored rejects a non-tab child exactly the same way");
		NonTabChildrenOfTabStrips(guide).Should().ContainSingle(e => e.WebName == "UsrStrayPanel",
			because: "the expansion panel is the non-tab child; the sibling crt.TabContainer is legitimate");
	}

	[Test]
	[Description("A container twin the mobile template provides is a SIBLING of the content this fix re-homes beside it: a mobile crt.GridContainer places children by layoutConfig alone, so the twin must be placed too, contiguously and exactly once — an unplaced twin among placed siblings is not rendered at all.")]
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
			["AreaProfileContainer", "TermsContainer", "ServiceTeamMemberExpansionPanel", "ServicePactExpansionPanel"],
			because: "the template's profile card is the general tab grid's first child and the re-homed content "
				+ "follows it; a gap or a repeat means a phantom child took a row");
		grid.Items.Select(i => i.LayoutConfigAdaptive!["small"]!["row"]!.GetValue<int>())
			.Should().Equal([1, 2, 3, 4],
				because: "rows must be contiguous — the mobile grid does not auto-place, so a skipped row is a "
					+ "child that was counted but never rendered");
		Element(guide, "SideAreaProfileContainer").MobileValues!.AsObject()
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
		Element(guide, "SideAreaProfileContainer").MobileValues!.AsObject().Should().ContainKey("layoutConfig",
			because: "the mobile-parent map the placement reads is a property of the mobile TEMPLATE, not of the "
				+ "positional rules — gating one on the other is what made this dead for five of six families");
	}

	// ── helpers ──────────────────────────────────────────────────────────────────────────────────

	/// <summary>
	/// Asserts the pinned capture still has the shape the reproduction depends on. Every test calls it, because
	/// a refreshed fixture with a different shape would leave most assertions here trivially true rather than
	/// failing — silent vacuity is the one outcome a regression suite must not have.
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
		guide.ElementMap.Should().NotBeEmpty(because: "an empty element map would make every assertion vacuous");
	}

	/// <summary>
	/// Inserts parented to a tab strip that are not tabs themselves. The strip set is derived from the guide
	/// alone — any parent at least one <c>crt.TabContainer</c> insert targets IS a strip — deliberately NOT by
	/// calling the converter's own pass, so this re-states the invariant instead of re-running the implementation.
	/// </summary>
	private static IReadOnlyList<ElementMapEntry> NonTabChildrenOfTabStrips(MobilePageConversionGuide guide) {
		HashSet<string> strips = new(StringComparer.OrdinalIgnoreCase) { MobileTabsPanel };
		strips.UnionWith(guide.ElementMap
			.Where(e => e.Operation == "insert"
				&& string.Equals(e.MobileType, "crt.TabContainer", StringComparison.OrdinalIgnoreCase)
				&& !string.IsNullOrEmpty(e.ParentName))
			.Select(e => e.ParentName));
		return [.. guide.ElementMap.Where(e => e.Operation == "insert"
			&& !string.IsNullOrEmpty(e.ParentName)
			&& strips.Contains(e.ParentName)
			&& !string.Equals(e.MobileType, "crt.TabContainer", StringComparison.OrdinalIgnoreCase))];
	}

	private static JsonObject LoadFixture() {
		string path = Path.Combine(
			TestContext.CurrentContext.TestDirectory, "Command", "McpServer", "Fixtures", FixtureName);
		return JsonNode.Parse(File.ReadAllText(path))!.AsObject();
	}

	/// <summary>
	/// The shipped rules with the general-tab <c>containers</c> entry removed — the rules file that produced the
	/// reported page, and the shape a CDN-published file can reintroduce at runtime. Re-parsed from the bundled
	/// JSON rather than hand-copied onto a new <see cref="TemplateMappingRule"/>: a hand-copied baseline silently
	/// loses any property added to that class later, and the A/B would then differ in more than the section
	/// under test while staying green.
	/// </summary>
	private static WebToMobilePageConversionRules RulesWithoutTheGeneralTabEntry() {
		JsonObject rules = JsonNode.Parse(BundledRulesJson())!.AsObject();
		JsonArray containers = rules["templates"]!.AsArray()
			.Single(t => t!["web"]!.ToString() == "PageWithTabsFreedomTemplate")!["containers"]!.AsArray();
		JsonNode generalTab = containers.Single(c => c!["web"]!.ToString() == "GeneralInfoTab");
		containers.Remove(generalTab);
		using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(rules.ToJsonString()));
		return WebToMobilePageConversionRulesCatalog.ParseStream(stream);
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
		bool withPositionalPlacements = true) {
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

		return WebToMobileAnalysisService.Analyze(
			bundle, MobileTypes(), new HashSet<string>(StringComparer.OrdinalIgnoreCase),
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

	private static ElementMapEntry Element(MobilePageConversionGuide guide, string webName) {
		IReadOnlyList<ElementMapEntry> matches = [.. guide.ElementMap
			.Where(e => string.Equals(e.WebName, webName, StringComparison.OrdinalIgnoreCase))];
		matches.Should().ContainSingle(
			because: $"'{webName}' must appear in the element map exactly once; found "
				+ (matches.Count == 0
					? "none"
					: string.Join(", ", matches.Select(m => $"{m.Operation}->{m.MobileName}"))));
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
