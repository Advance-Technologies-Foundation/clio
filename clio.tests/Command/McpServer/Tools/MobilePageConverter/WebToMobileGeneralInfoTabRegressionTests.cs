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
/// <para>
/// A later change moved where TWO of the pairs in this same shape land: the web profile island
/// (<c>SideAreaProfileContainer</c>) now converts into its own dedicated <c>declaredElements</c> tab
/// (<c>GeneralInformationTab</c>, a sibling of the template's own general tab under <c>Tabs</c>) instead of
/// being squeezed into the general tab's content grid, and <c>GeneralInfoTabContainer</c> — the pair for a
/// page that KEEPS that grid — now merges onto the profile Area card (<c>AreaProfileContainer</c>) instead of
/// the general tab's whole grid (<c>GeneralTabContainer</c>). Both are exercised below.
/// </para>
/// <para>
/// The declared tab is now the FIRST tab (index 0, the one selected on open), so the template's own general
/// tab shifts to position 1 and page-authored tabs are numbered after it; and <c>CardContentWrapper</c> — the
/// web wrapper around the side column — pairs onto the declared tab too, so the side column's other content
/// (<c>TermsContainer</c>) shares that tab with the profile island. The wrapper's own web grid columns are NOT
/// carried onto the declared tab: adaptive columns belong to a mobile <c>crt.GridContainer</c> only.
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
	/// The mobile profile Area card. This is now the merge target for <c>GeneralInfoTabContainer</c>
	/// (a page that KEEPS the web template's content grid) — no longer <see cref="MobileGeneralTabContainer"/>.
	/// A page that REMOVED the grid has its content directly in <see cref="MobileGeneralTab"/> instead.
	/// </summary>
	private const string MobileProfileContainer = "AreaProfileContainer";

	/// <summary>
	/// The mobile general TAB. Its web counterpart is a type-aligned twin (GeneralInfoTab to GeneralInfoTab), so
	/// identity is honest, and children go into the twin the web page actually put them in. A page that KEPT the
	/// template's content grid resolves its content through <see cref="MobileProfileContainer"/> instead;
	/// a page that REMOVED it has content directly in the tab, which is a crt.TabContainer and hosts items.
	/// </summary>
	private const string MobileGeneralTab = "GeneralInfoTab";

	/// <summary>The mobile tab strip. Only <c>crt.TabContainer</c> children of it are ever rendered.</summary>
	private const string MobileTabsPanel = "Tabs";

	/// <summary>
	/// The tab the rules DECLARE for the web profile island. Both <c>SideAreaProfileContainer</c> and
	/// <c>CardContentWrapper</c> pair onto it, so the published <c>nameMap</c> maps two source names to it.
	/// </summary>
	private const string DeclaredGeneralInformationTab = "GeneralInformationTab";

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
	/// operation's own name when nothing renamed it. A many-to-one pair (two source names onto one mobile
	/// name) answers with the FIRST source name, so this is for diagnostics only — lookups by source name
	/// go through <see cref="Element"/>, which reads the map forward and is unambiguous.
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

	/// <summary>
	/// The profile-card fields the pinned page places directly inside its web <c>SideAreaProfileContainer</c>.
	/// This content now converts into the declared <c>GeneralInformationTab</c> instead of the mobile profile
	/// Area card.
	/// </summary>
	private static readonly string[] SideProfileContent = [
		"GeneralInfoLabel", "Name", "Status", "Calendar", "Category", "CaseCategory", "Owner"
	];

	/// <summary>
	/// Whether <paramref name="webName"/> converts to an element nested (at any depth) inside the mobile
	/// counterpart of <paramref name="ancestorWebName"/> — walking the synthesized wrapper chain a newly
	/// inserted container gets, rather than requiring a direct parent match.
	/// </summary>
	private static bool DescendsFrom(MobilePageConversionGuide guide, string webName, string ancestorWebName) {
		string ancestorMobileName = Element(guide, ancestorWebName).Name;
		Dictionary<string, string> parentByName = guide.ViewConfigDiff
			.Where(e => !string.IsNullOrEmpty(e.ParentName))
			.ToDictionary(e => e.Name, e => e.ParentName, StringComparer.OrdinalIgnoreCase);
		string current = Element(guide, webName).Name;
		for (int guard = 0; guard < 20 && parentByName.TryGetValue(current, out string parent); guard++) {
			if (string.Equals(parent, ancestorMobileName, StringComparison.OrdinalIgnoreCase)) {
				return true;
			}
			current = parent;
		}
		return false;
	}

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
	[Description("A page that KEEPS the web template's GeneralInfoTabContainer now converts its content onto the profile Area card (AreaProfileContainer), not the general tab's whole content grid (GeneralTabContainer) — the pair moved when SideAreaProfileContainer stopped targeting AreaProfileContainer and got its own declared tab instead.")]
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
			Element(guide, name).ParentName.Should().Be(MobileProfileContainer,
				because: $"'{name}' sits inside the template's content grid here, and that grid now pairs onto the "
					+ "profile Area card, so the content lands there rather than in the general tab's own grid or "
					+ "tab body");
		}
		NonTabChildrenOfTabStrips(guide).Should().BeEmpty(
			because: "the shape a page keeps by default must not reintroduce the loss the ticket is about");
		// Asserted on the TARGET name: the page's own content grid and the template's are the same element
		// under two names, and viewConfigDiff addresses it by the mobile one.
		guide.ViewConfigDiff.Should().ContainSingle(o => o.Operation == "merge" && o.Name == MobileProfileContainer,
			because: "the page reuses the template's grid, so it merges onto the profile Area card exactly once — "
				+ "the container-map twin (CardContentWrapper) and the general-tab twin no longer share this mobile "
				+ "name, so the previous double-merge onto GeneralTabContainer no longer applies here");
		guide.ViewConfigDiff.Should().NotContain(o => o.Operation == "insert" && o.Name == MobileProfileContainer,
			because: "reusing the template's grid means never inserting a second one — that is the whole point");
	}

	[Test]
	[Description("SideAreaProfileContainer no longer converts into a template-provided grid twin (AreaProfileContainer) placed by layoutConfig — it converts into the declared GeneralInformationTab tab instead, inserted exactly once under Tabs, with its own generated content grid hosting the profile fields. This supersedes the previous 'twin placed beside TermsContainer in the general tab's grid' shape.")]
	public void Analyze_ShouldPlaceTheProfileIslandTwin_AsItsOwnDeclaredTab() {
		// Arrange
		JsonObject fixture = LoadFixture();

		// Act
		MobilePageConversionGuide guide = Convert(fixture);

		// Assert
		ViewConfigDiffOperation tab = Element(guide, "SideAreaProfileContainer");
		tab.Operation.Should().Be("insert",
			because: "the mobile template has no native counterpart for this tab; declaredElements creates it");
		TypeOf(tab).Should().Be("crt.TabContainer",
			because: "only a crt.TabContainer may be a child of the crt.TabPanel it is inserted into");
		tab.ParentName.Should().Be(MobileTabsPanel,
			because: "the declared tab is a sibling of the template's own general tab, under the same strip");
		guide.ViewConfigDiff.Should().NotContain(o => o.Name == "AreaProfileContainer",
			because: "this fixture removed GeneralInfoTabContainer (the pair that now targets AreaProfileContainer), "
				+ "so nothing merges onto the profile Area card here — the profile content moved to the declared tab");
		foreach (string name in SideProfileContent) {
			ViewConfigDiffOperation content = Element(guide, name);
			content.Operation.Should().Be("insert",
				because: $"'{name}' is page-authored content on the profile island and must reach the mobile page");
			DescendsFrom(guide, name, "SideAreaProfileContainer").Should().BeTrue(
				because: $"'{name}' sits inside the web profile island, so on mobile it must land inside the tab "
					+ "that island now converts into — not scattered elsewhere or duplicated onto the general tab");
		}
	}

	[Test]
	[Description("A twin is placed only where the MOBILE template holds it: web Tabs sits inside CardContentWrapper (mapped to the declared GeneralInformationTab) while on mobile that tab sits inside Tabs, so trusting the web nesting would place the tab strip inside its own descendant — and twice, since two web twins share the mobile name.")]
	public void Analyze_ShouldNotPlaceATwin_WhereOnlyTheWebTreeNestsIt() {
		// Arrange
		JsonObject fixture = LoadFixture();

		// Act
		MobilePageConversionGuide guide = Convert(fixture);

		// Assert
		foreach (AdaptiveLayoutGroup group in guide.AdaptiveLayout) {
			group.Items.Should().NotContain(i => i.Name == MobileTabsPanel,
				because: "the mobile tab strip contains the declared tab CardContentWrapper maps onto, not the other way round");
			group.Items.Select(i => i.Name).Should().OnlyHaveUniqueItems(
				because: "two web twins may share one mobile name; placing both would give the same element two cells");
		}
	}

	[Test]
	[Description("Creation of the declared GeneralInformationTab must not depend on the template declaring positional (:top/:bottom) entries — only the tabbed template has any, so gating declaredElements on them would leave the tab dead for every other template family. This supersedes the previous twin-placement invariant, which was gated the same way for the same reason.")]
	public void Analyze_ShouldInsertTheDeclaredGeneralInformationTab_EvenWithNoPositionalPlacements() {
		// Arrange
		JsonObject fixture = LoadFixture();

		// Act
		MobilePageConversionGuide guide = Convert(fixture, withPositionalPlacements: false);

		// Assert
		ViewConfigDiffOperation tab = Element(guide, "SideAreaProfileContainer");
		tab.Operation.Should().Be("insert",
			because: "declaredElements creates the tab from the mobile template's own declaration, independent of "
				+ "positionalPlacements");
		tab.ParentName.Should().Be(MobileTabsPanel,
			because: "the declared tab's parent comes from the declaration itself, not from positionalPlacements");
	}

	[Test]
	[Description("Each source shape converts to where the WEB page put its content, and the two shapes differ on purpose: a page that KEPT the template's content grid resolves through that grid's own containers pair (onto the profile Area card), and a page that REMOVED it keeps its content in the tab body. The removed grid is a two-column layout with its own gap, so the web pages differ too -- normalising them into one mobile tree would override a layout decision the developer made.")]
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
			Element(fromKept, name).ParentName.Should().Be(MobileProfileContainer,
				because: $"with the grid present '{name}' is its child, and that grid now pairs onto the profile "
					+ "Area card — no separate redirect on the tab is involved");
		}
		NonTabChildrenOfTabStrips(fromRemoved).Should().BeEmpty(
			because: "neither shape may leave a non-tab child in the strip — that is the invariant ENG-94951 broke");
		NonTabChildrenOfTabStrips(fromKept).Should().BeEmpty(
			because: "and it holds for the ordinary shape too");
	}

	[Test]
	[Description("The DEFAULT resolution, with no rules entry involved at all: strip both general-tab containers entries and the page still converts into a content-capable mobile container instead of the tab strip. The nearest such ancestor is CardContentWrapper, which now pairs onto the declared GeneralInformationTab, so the content lands in that tab's Area card. This is the class of the defect rather than its instance -- the rules file is CDN-fetched, so a published file that loses an entry must not be able to reproduce ENG-94951 on a user's machine.")]
	public void Analyze_ShouldReHomeTabStripChildren_WhenNoContainersEntryMapsTheGeneralTab() {
		// Arrange
		JsonObject fixture = LoadFixture();

		// Act
		MobilePageConversionGuide guide = Convert(fixture, RulesWithoutTheGeneralTabEntries());

		// Assert
		foreach (string name in GeneralTabContent) {
			ViewConfigDiffOperation content = Element(guide, name);
			content.ParentName.Should().NotBe(MobileTabsPanel,
				because: $"'{name}' would otherwise be hoisted into the tab strip and render as nothing");
			IsNestedIn(guide, content.Name, DeclaredGeneralInformationTab).Should().BeTrue(
				because: $"the walk carries the nearest ancestor that can hold arbitrary children; for this template "
					+ "that is CardContentWrapper, whose mobile side is now the declared General information tab, so "
					+ $"'{name}' lands inside that tab (its synthesized Area card) — found parent '{content.ParentName}'");
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

	[Test]
	[Description("The declared GeneralInformationTab is the FIRST mobile tab (index 0, the one selected when the page opens). The template's own general tab is a merge twin and never moves, so it shifts to position 1, and a page-authored tab is numbered after the SHIFTED native tab (index 2) — not squeezed in between the declared tab and the native tab at index 1.")]
	public void Analyze_ShouldPutTheDeclaredTabFirst_AndNumberPageTabsAfterTheShiftedNativeTab() {
		// Arrange
		JsonObject fixture = LoadFixture();

		// Act
		MobilePageConversionGuide guide = Convert(fixture);

		// Assert
		ViewConfigDiffOperation declaredTab = Element(guide, "SideAreaProfileContainer");
		declaredTab.Name.Should().Be(DeclaredGeneralInformationTab,
			because: "the profile island converts into the tab the rules declare for it");
		declaredTab.Index.Should().Be(0,
			because: "the declared tab is the first tab, so it is the one selected when the mobile page opens");
		ViewConfigDiffOperation nativeTab = Element(guide, MobileGeneralTab);
		nativeTab.Operation.Should().Be("merge",
			because: "the template's own general tab is a twin — it is merged, never re-inserted or re-indexed");
		nativeTab.Index.Should().BeNull(
			because: "a merge carries no index; the insert at 0 in front of it is what shifts it to position 1");
		Element(guide, "CaseHistoryTab").Index.Should().Be(2,
			because: "position 0 is the declared tab and position 1 is the shifted native tab, so the first page "
				+ "tab follows both; index 1 would place it between the declared and the native tab");
	}

	[TestCase(0, 2)]
	[TestCase(1, 2)]
	[TestCase(2, 1)]
	[Description("Page-tab numbering under a template-owned Tabs is derived from where the native tab actually ends up, not from a fixed offset: a declared tab at the native tab's position (0) shifts the native tab right and page tabs start after it; a declared tab right after it (1) is skipped over; a declared tab further right (2) leaves index 1 free for the first page tab.")]
	public void Analyze_ShouldNumberPageTabsAfterTheNativeTab_WhereverTheDeclaredTabSits(
			int declaredIndex, int expectedPageTabIndex) {
		// Arrange
		JsonObject fixture = LoadFixture();
		WebToMobilePageConversionRules rules = RulesWithDeclaredGeneralInformationTabAt(declaredIndex);

		// Act
		MobilePageConversionGuide guide = Convert(fixture, rules);

		// Assert
		Element(guide, "SideAreaProfileContainer").Index.Should().Be(declaredIndex,
			because: "a declared tab keeps the index the rules declare for it, untouched by the numbering pass");
		Element(guide, "CaseHistoryTab").Index.Should().Be(expectedPageTabIndex,
			because: $"with the declared tab at {declaredIndex} and the native tab at the first position the "
				+ "declaration leaves free, the page tab takes the next free position after the native tab");
	}

	[Test]
	[Description("CardContentWrapper now pairs onto the declared GeneralInformationTab, so the side column's content other than the profile island (TermsContainer) lands in that tab together with the profile fields — not in the template's general tab, where the web page's own general-tab content stays.")]
	public void Analyze_ShouldPlaceTheSideColumnContent_IntoTheDeclaredGeneralInformationTab() {
		// Arrange
		JsonObject fixture = LoadFixture();

		// Act
		MobilePageConversionGuide guide = Convert(fixture);

		// Assert
		Element(guide, "CardContentWrapper").Name.Should().Be(DeclaredGeneralInformationTab,
			because: "the wrapper's containers pair names the declared tab as its mobile side");
		ViewConfigDiffOperation terms = Element(guide, "TermsContainer");
		terms.Operation.Should().Be("insert", because: "TermsContainer is page content and must reach the mobile page");
		IsNestedIn(guide, terms.Name, DeclaredGeneralInformationTab).Should().BeTrue(
			because: "TermsContainer sits in the wrapper's side column, and the wrapper now maps onto the declared tab");
		IsNestedIn(guide, terms.Name, MobileGeneralTab).Should().BeFalse(
			because: "the side column no longer fills the template's general tab — that tab keeps only the content "
				+ "the web page put in its own general tab");
		foreach (string name in new[] { "TermsLabel", "ResponseTimeUnit", "ResolutionTimeValue" }) {
			Element(guide, name).ParentName.Should().Be(terms.Name,
				because: $"'{name}' keeps its own container when the container moves to the declared tab");
		}
		foreach (string name in GeneralTabContent) {
			Element(guide, name).ParentName.Should().Be(MobileGeneralTab,
				because: $"moving the side column must not drag the general tab's own content ('{name}') with it");
		}
	}

	[Test]
	[Description("On the real page, CardContentWrapper is a two-column web crt.GridContainer that now pairs onto the declared GeneralInformationTab, a crt.TabContainer, which has no adaptive columns input: no adaptive group is built for the tab and none of its direct children is placed as if it sat in a multi-column grid, while a grid-to-grid conversion (TermsContainer) keeps its adaptive columns. This pins the end-to-end outcome on the pinned capture; the adaptive-pass guard itself is isolated by Analyze_MultiColumnGrid_RenamedOntoNonGrid_GetsNoAdaptive, because on this capture the outcome is the same with or without that guard.")]
	public void Analyze_ShouldNotCarryGridColumns_OntoAMobileElementThatIsNotAGrid() {
		// Arrange
		JsonObject fixture = LoadFixture();

		// Act
		MobilePageConversionGuide guide = Convert(fixture);

		// Assert
		JsonArray wrapperColumns = FindNode(fixture["page"]!["viewConfig"]!, "CardContentWrapper")?["columns"]?.AsArray();
		wrapperColumns.Should().NotBeNull(because: "the guard is only exercised if the web wrapper is a grid with columns");
		wrapperColumns!.Count.Should().BeGreaterThan(1,
			because: "a single-column grid would never get adaptive placement, so the guard would be vacuous");
		TypeOf(Element(guide, "CardContentWrapper")).Should().Be("crt.TabContainer",
			because: "the wrapper's mobile side is the declared tab, which is not a grid");
		guide.AdaptiveLayout.Should().NotContain(g => g.ContainerName == DeclaredGeneralInformationTab,
			because: "adaptive per-breakpoint columns is a property of crt.GridContainer only; the web wrapper's "
				+ "columns must not be attached to a tab");
		guide.ViewConfigDiff
			.Where(o => o.Operation == "insert"
				&& string.Equals(o.ParentName, DeclaredGeneralInformationTab, StringComparison.OrdinalIgnoreCase))
			.Should().OnlyContain(o => o.Values == null || o.Values["layoutConfig"] == null
					|| o.Values["layoutConfig"]!["adaptive"] == null,
				because: "a direct child of the tab must not be placed as if it sat in the wrapper's multi-column grid");
		guide.AdaptiveLayout.Should().Contain(g => g.ContainerName == "TermsContainer",
			because: "a web grid that converts to a mobile crt.GridContainer still gets its adaptive columns — the "
				+ "guard is about the mobile type, not about renaming");
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

	/// <summary>
	/// The single operation a SOURCE element converts to: its <c>nameMap</c> target when renamed, else its own
	/// name. Read forward, because a many-to-one pair makes the reverse map ambiguous.
	/// </summary>
	private static ViewConfigDiffOperation Element(MobilePageConversionGuide guide, string webName) {
		string mobileName = MobileNameOf(guide, webName);
		IReadOnlyList<ViewConfigDiffOperation> matches = [.. guide.ViewConfigDiff
			.Where(e => string.Equals(e.Name, mobileName, StringComparison.OrdinalIgnoreCase))];
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

	/// <summary>The mobile name a source element converts to — its <c>nameMap</c> target, else itself.</summary>
	private static string MobileNameOf(MobilePageConversionGuide guide, string webName) {
		if (guide?.NameMap is not null) {
			foreach (KeyValuePair<string, string> rename in guide.NameMap) {
				if (string.Equals(rename.Key, webName, StringComparison.OrdinalIgnoreCase)) {
					return rename.Value;
				}
			}
		}
		return webName;
	}

	/// <summary>
	/// Whether the operation named <paramref name="mobileName"/> is nested (at any depth) inside the mobile
	/// element <paramref name="ancestorMobileName"/>, walking the parentName chain of viewConfigDiff.
	/// </summary>
	private static bool IsNestedIn(MobilePageConversionGuide guide, string mobileName, string ancestorMobileName) {
		Dictionary<string, string> parentByName = guide.ViewConfigDiff
			.Where(e => !string.IsNullOrEmpty(e.ParentName))
			.ToDictionary(e => e.Name, e => e.ParentName, StringComparer.OrdinalIgnoreCase);
		string current = mobileName;
		for (int guard = 0; guard < 20 && parentByName.TryGetValue(current, out string parent); guard++) {
			if (string.Equals(parent, ancestorMobileName, StringComparison.OrdinalIgnoreCase)) {
				return true;
			}
			current = parent;
		}
		return false;
	}

	/// <summary>
	/// The shipped rules with the declared <c>GeneralInformationTab</c> moved to <paramref name="index"/> —
	/// the only knob the converted-tab numbering reads from a declaration under a template-owned Tabs.
	/// </summary>
	private static WebToMobilePageConversionRules RulesWithDeclaredGeneralInformationTabAt(int index) {
		JsonObject rules = JsonNode.Parse(BundledRulesJson())!.AsObject();
		JsonObject declared = rules["templates"]!.AsArray()
			.Single(t => t!["web"]!.ToString() == "PageWithTabsFreedomTemplate")!["declaredElements"]!.AsArray()
			.Single(d => d!["name"]!.ToString() == DeclaredGeneralInformationTab)!.AsObject();
		declared["index"] = index;
		using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(rules.ToJsonString()));
		return WebToMobilePageConversionRulesCatalog.ParseStream(stream);
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
