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
	/// honest on both. WHERE the children go is a separate answer the rules give with `childrenTo`, which
	/// sends the tab's own children into the content grid — the same place a page that kept the template's
	/// grid puts them. Both source shapes therefore converge on one mobile tree.
	/// </summary>
	private const string MobileGeneralTab = "GeneralInfoTab";

	/// <summary>The mobile tab strip. Only <c>crt.TabContainer</c> children of it are ever rendered.</summary>
	private const string MobileTabsPanel = "Tabs";

	/// <summary>Substring identifying the tab-strip loss report among the guide's constraints.</summary>
	private const string PlacementLossReport = "INVISIBLE in Mobile Designer";

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
	[Description("ENG-94951: content the web page puts directly inside the template-owned GeneralInfoTab is converted into the mobile general tab's CONTENT CONTAINER - the ticket's acceptance criterion - rather than being emitted as a bare child of the mobile Tabs panel, and the content nested inside it survives too.")]
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
				because: $"'{name}' sits directly under the web general tab, whose containers entry sends its "
					+ "children into the mobile general tab's content grid; parenting it to the Tabs panel "
					+ "instead puts a non-tab child inside a crt.TabPanel, which renders nothing and is exactly "
					+ "ENG-94951");
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
				because: $"'{name}' sits inside the template's content grid here, and that grid is a type-aligned "
					+ "twin of the mobile general tab's grid, so the content lands in the grid rather than in the "
					+ "tab body");
		}
		NonTabChildrenOfTabStrips(guide).Should().BeEmpty(
			because: "the shape a page keeps by default must not reintroduce the loss the ticket is about");
		Element(guide, "GeneralInfoTabContainer").Operation.Should().Be("merge",
			because: "the web content grid and the mobile one are the same element under two names, so the page "
				+ "reuses the template's grid instead of inserting a second one");
	}

	[Test]
	[Description("ENG-94951 A/B negative half: with the SHIPPED rules the tab-strip loss report must NOT appear — a false positive tells the model to relocate correct entries and report a defect that does not exist.")]
	public void Analyze_ShouldNotReportTabStripLoss_WithTheShippedRules() {
		// Arrange
		JsonObject fixture = LoadFixture();

		// Act
		MobilePageConversionGuide guide = Convert(fixture);

		// Assert
		guide.PlacementLosses.Should().BeNull(
			because: "the shipped rules place the general tab's content correctly, so reporting a lost subtree "
				+ "would be guidance the model acts on — corrupting a conversion that was already right");
		guide.Constraints.Should().NotContain(c => c.Contains(PlacementLossReport),
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
		guide.PlacementLosses.Should().ContainSingle(l => l.Name == "ServiceTeamMemberExpansionPanel"
				&& l.MobileType == "crt.ExpansionPanel" && l.ParentName == MobileTabsPanel,
			because: "the loss is reported as data the caller can act on, not only as prose it must parse");
		string report = guide.Constraints.Should().ContainSingle(c => c.Contains(PlacementLossReport),
			because: "a subtree the mobile designer renders as nothing must be named in the report; without this "
				+ "line a lost general-information tab is indistinguishable from a page that never had one")
			.Subject;
		report.Should().Contain("ServiceTeamMemberExpansionPanel",
			because: "the report names the elements that are lost, so the defect is actionable without a debugger");
		JsonSerializer.Deserialize<MobilePageConversionGuide>(JsonSerializer.Serialize(guide))!
			.Constraints.Should().Contain(c => c.Contains(PlacementLossReport),
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
		guide.PlacementLosses.Should().NotBeNullOrEmpty(
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
		guide.PlacementLosses.Should().ContainSingle(l => l.Name == "UsrStrayPanel",
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
			because: "the template's profile card is the general tab grid's first child and the re-homed tab "
				+ "content follows it; a gap or a repeat means a phantom child took a row");
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

	[Test]
	[Description("Component types are DATA, not code: which receivers can host arbitrary children is the rules' contentContainerTypes accept-list, and which child is a tab is tabAreaLayers.tabComponentType. Renaming the tab type in the rules and in the page must move both decisions with it — a constant in the analyser would report a correctly converted tab as a loss. The rules are fetched at runtime while the assembly is not.")]
	public void Analyze_ShouldTakeTheReceiverAndTabTypes_FromTheRulesRatherThanFromCode() {
		// Arrange — the shipped rules with BOTH tab types renamed to values no code could know, and the fixture
		// retyped to match. Nothing else changes: the same page, the same missing general-tab containers entry.
		JsonObject fixture = LoadFixture();
		RetypeComponents(fixture, "crt.TabPanel", "usr.RenamedStrip");
		RetypeComponents(fixture, "crt.TabContainer", "usr.RenamedTab");
		WebToMobilePageConversionRules rules = RulesWithoutTheGeneralTabEntry(
			renameTabTypeTo: "usr.RenamedTab", renameAcceptedTabContainerTypeTo: "usr.RenamedTab",
			renameKnownContainerTypes: ("crt.TabPanel", "usr.RenamedStrip"));

		// Act
		MobilePageConversionGuide guide = Convert(
			fixture, rules, mobileTypes: MobileTypesWith("usr.RenamedStrip", "usr.RenamedTab"));

		// Assert
		guide.PlacementLosses.Should().NotBeNullOrEmpty(
			because: "the arrangement must still reproduce the loss after the rename, or the assertions below "
				+ "would be judging an empty list");
		// The proof. CaseHistoryTab is a converted tab of the RENAMED tab type sitting in the renamed strip; it
		// is legitimate there, so it must NOT be reported. Both halves of that judgement are data: the strip is
		// a non-hosting receiver because its type is absent from contentContainerTypes, and the child is exempt
		// because its type IS tabAreaLayers.tabComponentType. Constants would get both wrong.
		guide.ElementMap.Should().Contain(e => e.WebName == "CaseHistoryTab" && e.MobileType == "usr.RenamedTab",
			because: "the proof is vacuous unless a renamed tab actually reached the renamed strip");
		guide.PlacementLosses.Should().OnlyContain(l => l.MobileType != "usr.RenamedTab",
			because: "a tab is legitimate inside a strip whatever the platform calls it — the exemption reads "
				+ "the tab type from the rules, and a constant would turn every converted tab into a false loss");
	}

	[Test]
	[Description("The placement check is NOT about tabs: a receiver is non-hosting when the rules recognise its type as a layout container (emptyContainerRemoval.removableTypes) but do NOT list it as able to hold arbitrary content (contentContainerTypes). Proven with no tab anywhere in the page — a plain crt.GridContainer becomes non-hosting purely by being dropped from the accept-list, which a tab-shaped check could never detect.")]
	public void Analyze_ShouldReportAPlacementLoss_WhenTheReceiverTypeIsNotInTheAcceptList() {
		// Arrange — a page with no tab strip and no tab at all. The only lever is the accept-list.
		var bundle = new PageBundleInfo {
			ViewConfig = JsonNode.Parse("""
				[ { "name": "MainContainer", "type": "crt.GridContainer", "items": [
					{ "name": "UsrHost", "type": "crt.GridContainer", "items": [
						{ "name": "UsrField", "type": "crt.Input", "label": "F" } ] } ] } ]
				""")!.AsArray(),
			ViewModelConfig = new JsonObject(), ModelConfig = new JsonObject(),
			Resources = new PageResourceInfo { Strings = new JsonObject() }
		};

		// Act — twice over the SAME page: once with the shipped accept-list, once with crt.GridContainer
		// removed from it. Nothing else differs, so the accept-list alone decides the verdict.
		MobilePageConversionGuide accepted = AnalyzeBlank(bundle, WebToMobilePageConversionRulesCatalog.LoadBundled());
		MobilePageConversionGuide rejected = AnalyzeBlank(bundle, RulesWithoutAcceptedType("crt.GridContainer"));

		// Assert
		accepted.PlacementLosses.Should().BeNullOrEmpty(
			because: "a crt.GridContainer is on the shipped accept-list, so it hosts its child and nothing is lost");
		rejected.PlacementLosses.Should().ContainSingle(l => l.Name == "UsrField" && l.ParentName == "UsrHost",
			because: "with its type off the accept-list the very same container can no longer host the very same "
				+ "child — the decision is the rules' data, not a component type named in the analyser");
	}

	[Test]
	[Description("No false positive on a host the rules simply do not name. contentContainerTypes lists four types while the mobile registry ships many more with an items slot (crt.Scaffold, crt.Gallery, crt.Timeline, a partner's own usr.* container), so treating \"absent from the accept-list\" as \"cannot host\" would report a confident loss on every one of them - and this report tells the caller to STOP, so a false positive halts a correct conversion. Only a type the rules RECOGNISE as a layout container and do not list as content-hosting is reported.")]
	public void Analyze_ShouldNotReportAPlacementLoss_ForAHostTheRulesDoNotName() {
		// Arrange - two receivers neither list names: a partner container and a registry type with an items
		// slot that is not in contentContainerTypes.
		var bundle = new PageBundleInfo {
			ViewConfig = JsonNode.Parse("""
				[ { "name": "MainContainer", "type": "crt.GridContainer", "items": [
					{ "name": "UsrPartnerHost", "type": "usr.PartnerContainer", "items": [
						{ "name": "UsrPartnerField", "type": "crt.Input", "label": "P" } ] },
					{ "name": "Gal", "type": "crt.Gallery", "items": [
						{ "name": "GalField", "type": "crt.Input", "label": "G" } ] } ] } ]
				""")!.AsArray(),
			ViewModelConfig = new JsonObject(), ModelConfig = new JsonObject(),
			Resources = new PageResourceInfo { Strings = new JsonObject() }
		};
		var types = new HashSet<string>(MobileTypes(), StringComparer.OrdinalIgnoreCase)
			{ "usr.PartnerContainer", "crt.Gallery" };

		// Act
		MobilePageConversionGuide guide = AnalyzeBlank(
			bundle, WebToMobilePageConversionRulesCatalog.LoadBundled(), types);

		// Assert
		guide.PlacementLosses.Should().BeNullOrEmpty(
			because: "neither receiver is a type the rules recognise as a layout container, so the converter has "
				+ "no basis to claim it cannot hold its child; guessing here would stop a correct conversion");
	}

	[Test]
	[Description("Two web pages that render identically must produce the SAME mobile tree: whether the page kept the web template's content grid or removed it and put its content straight under the tab, the converted content lands in one place. Without childrenTo the two shapes diverge, because a type-aligned tab twin sends its children into the tab body while the grid twin sends them into the grid.")]
	public void Analyze_ShouldConvergeBothSourceShapes_OnTheSameMobileContainer() {
		// Arrange - the pinned page (grid REMOVED) and the same page with the template's grid put back.
		JsonObject removedGrid = LoadFixture();
		JsonObject keptGrid = WithTemplateContentGridRestored(LoadFixture());

		// Act
		MobilePageConversionGuide fromRemoved = Convert(removedGrid);
		MobilePageConversionGuide fromKept = Convert(keptGrid);

		// Assert
		foreach (string name in GeneralTabContent) {
			Element(fromRemoved, name).ParentName.Should().Be(
				Element(fromKept, name).ParentName,
				because: $"'{name}' renders in the same place on both web pages, so a reader of the converted "
					+ "mobile page must not be able to tell which source shape it came from");
			Element(fromRemoved, name).ParentName.Should().Be(MobileGeneralTabContainer,
				because: "and the place they converge on is the one the acceptance criterion names");
		}
	}

	[Test]
	[Description("childrenTo is DATA: it is what sends the tab's children into the content grid, and dropping it from the rules moves them back into the tab body. Pins that the convergence above is a rules decision the analyser reads, not behaviour hardcoded around the general tab's name.")]
	public void Analyze_ShouldFollowChildrenTo_FromTheRulesRatherThanTheTabName() {
		// Arrange
		JsonObject fixture = LoadFixture();

		// Act
		MobilePageConversionGuide withRule = Convert(fixture);
		MobilePageConversionGuide withoutRule = Convert(fixture, RulesWithoutChildrenTo("GeneralInfoTab"));

		// Assert
		Element(withRule, GeneralTabContent[0]).ParentName.Should().Be(MobileGeneralTabContainer,
			because: "the rules declare childrenTo for the general tab");
		Element(withoutRule, GeneralTabContent[0]).ParentName.Should().Be(MobileGeneralTab,
			because: "with the declaration gone the children fall back to the twin itself, which proves the "
				+ "placement follows the rules and is not keyed on the element's name in code");
	}

	// ── helpers ──────────────────────────────────────────────────────────────────────────────────

	/// <summary>
	/// Asserts the pinned capture still has the shape the reproduction depends on. <see cref="Convert"/> calls
	/// it on every conversion, so every test that goes through the fixture is covered without having to
	/// remember; a hand-built bundle is skipped. It matters because
	/// a refreshed fixture with a different shape would leave most assertions here trivially true rather than
	/// failing — silent vacuity is the one outcome a regression suite must not have.
	/// </summary>
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
		guide.ElementMap.Should().NotBeEmpty(because: "an empty element map would make every assertion vacuous");
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

	/// <summary>Rewrites every <c>type</c> equal to <paramref name="from"/> across the whole fixture.</summary>
	private static void RetypeComponents(JsonNode node, string from, string to) {
		switch (node) {
			case JsonArray array:
				foreach (JsonNode item in array.Where(i => i is not null)) {
					RetypeComponents(item!, from, to);
				}
				break;
			case JsonObject obj:
				if (string.Equals(obj["type"]?.ToString(), from, StringComparison.OrdinalIgnoreCase)) {
					obj["type"] = to;
				}
				foreach (KeyValuePair<string, JsonNode> pair in obj.ToList()) {
					if (pair.Value is JsonArray or JsonObject) {
						RetypeComponents(pair.Value!, from, to);
					}
				}
				break;
		}
	}

	/// <summary>The pinned mobile registry plus the extra types a renamed-platform test needs.</summary>
	private static IReadOnlySet<string> MobileTypesWith(params string[] extra) {
		var types = new HashSet<string>(MobileTypes(), StringComparer.OrdinalIgnoreCase);
		types.UnionWith(extra);
		return types;
	}

	/// <summary>The shipped rules with one containers entry's <c>childrenTo</c> removed.</summary>
	private static WebToMobilePageConversionRules RulesWithoutChildrenTo(string web) {
		JsonObject rules = JsonNode.Parse(BundledRulesJson())!.AsObject();
		JsonObject entry = rules["templates"]!.AsArray()
			.Single(t => t!["web"]!.ToString() == "PageWithTabsFreedomTemplate")!["containers"]!.AsArray()
			.Single(c => c!["web"]!.ToString() == web)!.AsObject();
		entry.Remove("childrenTo");
		using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(rules.ToJsonString()));
		return WebToMobilePageConversionRulesCatalog.ParseStream(stream);
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
	private static WebToMobilePageConversionRules RulesWithoutTheGeneralTabEntry(
		string renameTabTypeTo = null, string renameAcceptedTabContainerTypeTo = null,
		(string From, string To)? renameKnownContainerTypes = null) {
		JsonObject rules = JsonNode.Parse(BundledRulesJson())!.AsObject();
		if (renameTabTypeTo is not null) {
			rules["tabAreaLayers"]!["tabComponentType"] = renameTabTypeTo;
		}
		if (renameAcceptedTabContainerTypeTo is not null) {
			JsonArray accepted = rules["contentContainerTypes"]!.AsArray();
			for (int i = 0; i < accepted.Count; i++) {
				if (string.Equals(accepted[i]!.ToString(), "crt.TabContainer", StringComparison.OrdinalIgnoreCase)) {
					accepted[i] = renameAcceptedTabContainerTypeTo;
				}
			}
		}
		JsonArray containers = rules["templates"]!.AsArray()
			.Single(t => t!["web"]!.ToString() == "PageWithTabsFreedomTemplate")!["containers"]!.AsArray();
		JsonNode generalTab = containers.Single(c => c!["web"]!.ToString() == "GeneralInfoTab");
		containers.Remove(generalTab);
		if (renameKnownContainerTypes is { } rename) {
			JsonArray known = rules["emptyContainerRemoval"]!["removableTypes"]!.AsArray();
			for (int i = 0; i < known.Count; i++) {
				if (string.Equals(known[i]!.ToString(), rename.From, StringComparison.OrdinalIgnoreCase)) {
					known[i] = rename.To;
				}
			}
		}
		using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(rules.ToJsonString()));
		return WebToMobilePageConversionRulesCatalog.ParseStream(stream);
	}

	/// <summary>The shipped rules with one type removed from the content-container accept-list.</summary>
	private static WebToMobilePageConversionRules RulesWithoutAcceptedType(string type) {
		JsonObject rules = JsonNode.Parse(BundledRulesJson())!.AsObject();
		JsonArray accepted = rules["contentContainerTypes"]!.AsArray();
		JsonNode entry = accepted.Single(t => string.Equals(t!.ToString(), type, StringComparison.OrdinalIgnoreCase));
		accepted.Remove(entry);
		using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(rules.ToJsonString()));
		return WebToMobilePageConversionRulesCatalog.ParseStream(stream);
	}

	/// <summary>Converts a hand-built page with no template pair — no chrome subtraction, no twins.</summary>
	private static MobilePageConversionGuide AnalyzeBlank(
		PageBundleInfo bundle, WebToMobilePageConversionRules rules, IReadOnlySet<string> mobileTypes = null) =>
		WebToMobileAnalysisService.Analyze(
			new PageBundleInfo {
				ViewConfig = bundle.ViewConfig!.DeepClone().AsArray(),
				ViewModelConfig = new JsonObject(), ModelConfig = new JsonObject(),
				Resources = new PageResourceInfo { Strings = new JsonObject() }
			},
			mobileTypes ?? MobileTypes(), new HashSet<string>(StringComparer.OrdinalIgnoreCase),
			webByType: new Dictionary<string, ComponentRegistryEntry>(StringComparer.OrdinalIgnoreCase),
			mobileByType: null, rules, templateRule: null,
			sourcePage: "Usr_FormPage", sourceTemplate: "BlankPageTemplate",
			suggestedTarget: "Usr_MobileFormPage",
			containerNameMap: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

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
			containerChildrenTargets: MobilePageConversionGuideTool.BuildContainerChildrenTargetMap(templateRule),
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
