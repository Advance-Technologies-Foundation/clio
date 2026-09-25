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
/// page puts inside it, must land inside a mobile tab — never as bare children of the mobile <c>Tabs</c> panel.
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
/// A later change gave the web General-information tab a mobile tab of its own: the rules declare
/// <c>AdditionalInfoTab</c> (<c>declaredElements</c>, index 1 — right after the template's own Details tab)
/// and pair BOTH <c>GeneralInfoTab</c> and <c>GeneralInfoTabContainer</c> onto it, so the page's
/// general-tab content lands there whichever of the two shapes the page has, stacked into the tab's
/// synthesized Area card. The template's Details tab (<c>GeneralInfoTab</c> / <c>GeneralTabContainer</c>)
/// is unchanged: it keeps the side column — the profile island in <c>AreaProfileContainer</c> and the
/// wrapper's other content (<c>TermsContainer</c>). A kept content grid's web columns are NOT carried onto
/// the declared tab: adaptive columns belong to a mobile <c>crt.GridContainer</c> only.
/// </para>
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class WebToMobileGeneralInfoTabRegressionTests {

	/// <summary>No mobile request registry: support comes from the versioned rules alone, which is
	/// what these component-level fixtures are about. Passed explicitly because the parameter is
	/// required — an omitted registry must be a visible decision, not a silent empty default.</summary>
	private static readonly IReadOnlySet<string> NoRequestRegistry =
		new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private const string FixtureName = "ServicesFormPageTabbed.live-snapshot.json";

	/// <summary>The mobile general tab's content grid — the "Details tab content container" of the ticket.</summary>
	private const string MobileGeneralTabContainer = "GeneralTabContainer";

	/// <summary>
	/// The mobile profile Area card inside <see cref="MobileGeneralTabContainer"/> — the merge target of the web
	/// profile island (<c>SideAreaProfileContainer</c>), so the island's fields land inside it.
	/// </summary>
	private const string MobileProfileContainer = "AreaProfileContainer";

	/// <summary>
	/// The mobile template's own general ("Details") TAB. No web element pairs onto it any more — the web
	/// general tab pairs onto <see cref="DeclaredAdditionalInfoTab"/> — so it keeps only what reaches its
	/// content grid: the web side column.
	/// </summary>
	private const string MobileGeneralTab = "GeneralInfoTab";

	/// <summary>The mobile tab strip. Only <c>crt.TabContainer</c> children of it are ever rendered.</summary>
	private const string MobileTabsPanel = "Tabs";

	/// <summary>
	/// The tab the rules DECLARE for the web general-information tab. Both <c>GeneralInfoTab</c> and
	/// <c>GeneralInfoTabContainer</c> pair onto it, so the published <c>nameMap</c> may map two source names to it.
	/// </summary>
	private const string DeclaredAdditionalInfoTab = "AdditionalInfoTab";

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

	/// <summary>Page-authored content the web page places directly inside the template-owned general tab.</summary>
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
	/// They convert into the mobile profile Area card (<see cref="MobileProfileContainer"/>).
	/// </summary>
	private static readonly string[] SideProfileContent = [
		"GeneralInfoLabel", "Name", "Status", "Calendar", "Category", "CaseCategory", "Owner"
	];

	[Test]
	[Description("ENG-94951: content the web page puts directly inside the template-owned GeneralInfoTab is converted into the declared AdditionalInfoTab — a crt.TabContainer, which hosts items — rather than being emitted as a bare child of the mobile Tabs panel, and the content nested inside it survives too. The content sits in the tab's synthesized Area card (the white area of the mobile tab body), not directly in the tab, and it does not land in the template's own Details tab, which keeps the side column.")]
	public void Analyze_ShouldPlaceGeneralInfoTabContent_IntoTheDeclaredAdditionalInfoTab() {
		// Arrange
		JsonObject fixture = LoadFixture();

		// Act
		MobilePageConversionGuide guide = Convert(fixture);

		// Assert
		RequireReproductionShape(fixture, guide);
		TabAreaLayerGroup layers = guide.TabAreaLayers.Should().ContainSingle(
				g => g.TabName == DeclaredAdditionalInfoTab,
				because: "the declared tab is created by the converter, so it gets the mandatory two-layer tab body")
			.Subject;
		layers.AreaName.Should().NotBeNullOrEmpty(
			because: "the tab has content, so its Area card — the white area the content must sit in — is created");
		guide.ViewConfigDiff.Should().ContainSingle(
				o => o.Name == layers.MainTabContainerName && o.ParentName == DeclaredAdditionalInfoTab,
				because: "the tab body grid is the declared tab's direct child");
		guide.ViewConfigDiff.Should().ContainSingle(
				o => o.Name == layers.AreaName && o.ParentName == layers.MainTabContainerName,
				because: "the Area card sits inside the tab body grid");
		layers.MovedChildren.Should().Contain(GeneralTabContent,
			because: "the web general tab's top-level content is moved into the Area card");
		foreach (string name in GeneralTabContent) {
			Element(guide, name).ParentName.Should().Be(layers.AreaName,
				because: $"'{name}' must sit in the Area card; placed directly in the tab it renders without the white "
					+ "area");
		}
		foreach (string name in GeneralTabContent) {
			ViewConfigDiffOperation entry = Element(guide, name);
			entry.Operation.Should().Be("insert",
				because: $"'{name}' is page-authored content and must reach the mobile page");
			IsNestedIn(guide, entry.Name, DeclaredAdditionalInfoTab).Should().BeTrue(
				because: $"'{name}' sits directly under the web general tab, which pairs onto the declared tab, so it "
					+ "lands inside that tab (its synthesized Area card); parenting it to the Tabs panel instead puts a "
					+ $"non-tab child inside a crt.TabPanel, which renders nothing and is exactly ENG-94951 — found "
					+ $"parent '{entry.ParentName}'");
			IsNestedIn(guide, entry.Name, MobileGeneralTab).Should().BeFalse(
				because: $"'{name}' is general-tab content, and the template's Details tab keeps only the side column");
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
	[Description("ENG-94951 over-correction guard: the template's Details tab and its content grid are provided by the mobile template, so the converter never re-declares either one under Tabs; the web general tab's content gets the DECLARED AdditionalInfoTab instead, captioned by its own declared resource, while the template's own 'Details' caption stands untouched. This does NOT fail on the unfixed code — there the tab was dropped — it pins the shape of the fix.")]
	public void Analyze_ShouldMergeTheGeneralTab_RatherThanRecreateIt() {
		// Arrange
		JsonObject fixture = LoadFixture();

		// Act
		MobilePageConversionGuide guide = Convert(fixture);

		// Assert
		guide.ViewConfigDiff.Should().NotContain(
			e => e.Operation == "insert" && string.Equals(e.Name, MobileGeneralTab, StringComparison.OrdinalIgnoreCase),
			because: "the mobile template already provides the Details tab; inserting a second one under Tabs "
				+ "would duplicate it");
		guide.ViewConfigDiff.Should().NotContain(
			e => e.Operation == "insert"
				&& string.Equals(e.Name, MobileGeneralTabContainer, StringComparison.OrdinalIgnoreCase),
			because: "the mobile template already provides the Details tab's content grid — it is a merge "
				+ "target, never an insert");
		guide.ResourceStrings.Should().NotContainKey("GeneralInfoTab_caption",
			because: "no web caption is carried onto the template's tab, so the mobile template's own 'Details' "
				+ "must stand instead of the web page's caption overwriting it");
		ViewConfigDiffOperation declaredTab = Element(guide, "GeneralInfoTab");
		declaredTab.Values!["caption"]!.GetValue<string>().Should().Be("#ResourceString(AdditionalInfoTab_caption)#",
			because: "the declared tab is captioned by the resource its declaration names");
		guide.ResourceStrings.Should().ContainKey("AdditionalInfoTab_caption").WhoseValue.Should().Be(
			"General information",
			because: "the declared caption resource is registered with the page so the tab renders its name");
	}

	[Test]
	[Description("A page that KEEPS the web template's GeneralInfoTabContainer converts its content into the same declared AdditionalInfoTab: the grid pairs onto the tab exactly as the web tab itself does, so the tab is inserted once and the grid never reaches the mobile page under its own name.")]
	public void Analyze_ShouldPlaceGeneralInfoTabContent_WhenThePageKeepsTheTemplateContentGrid() {
		// Arrange — the fixture's own shape with the template's content grid put back around the page's content,
		// which is what an ordinary tabbed page (one that did not remove it) looks like.
		JsonObject fixture = WithTemplateContentGridRestored(LoadFixture());

		// Act
		MobilePageConversionGuide guide = Convert(fixture);

		// Assert
		foreach (string name in GeneralTabContent) {
			IsNestedIn(guide, Element(guide, name).Name, DeclaredAdditionalInfoTab).Should().BeTrue(
				because: $"'{name}' sits inside the template's content grid here, and that grid pairs onto the "
					+ "declared tab, so the content lands in that tab");
		}
		NonTabChildrenOfTabStrips(guide).Should().BeEmpty(
			because: "the shape a page keeps by default must not reintroduce the loss the ticket is about");
		guide.ViewConfigDiff.Should().ContainSingle(o => o.Name == DeclaredAdditionalInfoTab,
			because: "the web tab and its grid both merge onto the declared tab, which is inserted exactly once — "
				+ "a second operation on that name would be the double-merge a many-to-one pair must not produce");
		guide.ViewConfigDiff.Should().NotContain(o => o.Name == "GeneralInfoTabContainer",
			because: "the web grid is merged onto the declared tab, never carried to the mobile page as its own element");
	}

	[Test]
	[Description("A container twin the mobile template provides is a SIBLING of the inserts placed into its grid: a mobile crt.GridContainer places children by layoutConfig alone, so the twin must be placed too, contiguously and exactly once — an unplaced twin among placed siblings is not rendered at all. The profile island's own fields land inside the twin.")]
	public void Analyze_ShouldPlaceTheTemplateTwin_BesideTheContentReHomedIntoItsGrid() {
		// Arrange
		JsonObject fixture = LoadFixture();

		// Act
		MobilePageConversionGuide guide = Convert(fixture);

		// Assert
		AdaptiveLayoutGroup grid = guide.AdaptiveLayout
			.Single(g => g.Items.Any(i => i.Name == MobileProfileContainer));
		IReadOnlyList<string> placed = [.. grid.Items.Select(i => i.Name)];
		placed.Should().Equal(
			[MobileProfileContainer, "TermsContainer"],
			because: "the template's profile card is the general tab grid's first child and the wrapper's other "
				+ "non-tab content follows it; a gap or a repeat means a phantom child took a row. The general "
				+ "tab's own content is NOT here — it converts into the declared AdditionalInfoTab");
		grid.Items.Select(i => i.LayoutConfigAdaptive!["small"]!["row"]!.GetValue<int>())
			.Should().Equal([1, 2],
				because: "rows must be contiguous — the mobile grid does not auto-place, so a skipped row is a "
					+ "child that was counted but never rendered");
		Element(guide, "SideAreaProfileContainer").Values!.AsObject()
			.Should().ContainKey("layoutConfig",
				because: "the twin is a merge, but without a layoutConfig it is the one unplaced child of a grid "
					+ "whose every other child got a cell, and the mobile designer renders nothing for it");
		foreach (string name in SideProfileContent) {
			Element(guide, name).ParentName.Should().Be(MobileProfileContainer,
				because: $"'{name}' sits inside the web profile island, so on mobile it lands inside the profile "
					+ "Area card the island merges onto");
		}
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
	[Description("Twin placement and the declared tab must not depend on the template declaring positional (:top/:bottom) entries: only the tabbed template has any, so a mobile-parent map or a declaration supplied only alongside them would leave the fix dead for every other template family.")]
	public void Analyze_ShouldPlaceTheTemplateTwin_EvenWithNoPositionalPlacements() {
		// Arrange
		JsonObject fixture = LoadFixture();

		// Act
		MobilePageConversionGuide guide = Convert(fixture, withPositionalPlacements: false);

		// Assert
		Element(guide, "SideAreaProfileContainer").Values!.AsObject().Should().ContainKey("layoutConfig",
			because: "the mobile-parent map the placement reads is a property of the mobile TEMPLATE, not of the "
				+ "positional rules — gating one on the other is what made this dead for five of six families");
		ViewConfigDiffOperation declaredTab = Element(guide, "GeneralInfoTab");
		declaredTab.Operation.Should().Be("insert",
			because: "declaredElements creates the tab from the rule's own declaration, independent of "
				+ "positionalPlacements");
		declaredTab.ParentName.Should().Be(MobileTabsPanel,
			because: "the declared tab's parent comes from the declaration itself, not from positionalPlacements");
	}

	[Test]
	[Description("Both source shapes of the web general tab — the page that REMOVED the template's content grid and the page that KEPT it — convert into the same place: the shipped rule pairs both the tab and its grid onto the declared AdditionalInfoTab, whose synthesized Area card stacks the content in a single column, so the two shapes produce one mobile tree. For both, the viewConfigDiff that creates the tab — its insert, the web twins merged onto it and its tab body and Area card — applies cleanly through the faithful differ clone.")]
	public void Analyze_ShouldPlaceContentInTheDeclaredTab_ForBothSourceShapes() {
		// Arrange - the pinned page (grid REMOVED) and the same page with the template's grid put back.
		JsonObject removedGrid = LoadFixture();
		JsonObject keptGrid = WithTemplateContentGridRestored(LoadFixture());

		// Act
		MobilePageConversionGuide fromRemoved = Convert(removedGrid);
		MobilePageConversionGuide fromKept = Convert(keptGrid);

		// Assert
		foreach (string name in GeneralTabContent) {
			ViewConfigDiffOperation removed = Element(fromRemoved, name);
			IsNestedIn(fromRemoved, removed.Name, DeclaredAdditionalInfoTab).Should().BeTrue(
				because: $"the page put '{name}' straight in the web tab, which pairs onto the declared tab");
			Element(fromKept, name).ParentName.Should().Be(removed.ParentName,
				because: $"with the grid present '{name}' is its child, and the grid pairs onto the same declared "
					+ "tab, so it lands in the same Area card as in the removed-grid shape");
		}
		NonTabChildrenOfTabStrips(fromRemoved).Should().BeEmpty(
			because: "neither shape may leave a non-tab child in the strip — that is the invariant ENG-94951 broke");
		NonTabChildrenOfTabStrips(fromKept).Should().BeEmpty(
			because: "and it holds for the ordinary shape too");
		foreach ((string shape, MobilePageConversionGuide guide) in
			new[] { ("removed-grid", fromRemoved), ("kept-grid", fromKept) }) {
			SchemaValidationResult applied = MobileDiffApplyValidator.Validate(new JsonObject {
				["viewConfigDiff"] = JsonSerializer.SerializeToNode(guide.ViewConfigDiff)
			}.ToJsonString());
			applied.IsValid.Should().BeTrue(
				because: $"the {shape} guide's viewConfigDiff is pasted as shipped, so the differ must accept the declared "
					+ $"tab's insert, the twins merged onto it and its synthesized layers. Errors: "
					+ string.Join("; ", applied.Errors));
		}
	}

	[Test]
	[Description("The DEFAULT resolution, with no rules entry involved at all: strip both general-tab containers entries and the page still converts into the mobile general tab's grid, while the declared AdditionalInfoTab — which nothing reaches any more — is removed as empty. This is the class of the defect rather than its instance -- the rules file is CDN-fetched, so a published file that loses an entry must not be able to reproduce ENG-94951 on a user's machine.")]
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
		guide.ViewConfigDiff.Should().NotContain(o => o.Operation == "insert" && o.Name == DeclaredAdditionalInfoTab,
			because: "with its pairs gone nothing lands in the declared tab, and an empty converter-created container "
				+ "is removed rather than shipped as an empty tab");
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
	[Description("The template's Details tab keeps position 0 (a merge twin never moves), the declared AdditionalInfoTab takes index 1 as declared, and a page-authored tab is numbered after it (index 2) — the numbering pass skips the index the declaration claims.")]
	public void Analyze_ShouldPlaceTheDeclaredTabAfterDetails_AndNumberPageTabsAfterIt() {
		// Arrange
		JsonObject fixture = LoadFixture();

		// Act
		MobilePageConversionGuide guide = Convert(fixture);

		// Assert
		ViewConfigDiffOperation declaredTab = Element(guide, "GeneralInfoTab");
		declaredTab.Name.Should().Be(DeclaredAdditionalInfoTab,
			because: "the web general tab converts into the tab the rules declare for it");
		declaredTab.Operation.Should().Be("insert",
			because: "the mobile template has no such tab; the declaration creates it");
		TypeOf(declaredTab).Should().Be("crt.TabContainer",
			because: "only a crt.TabContainer may be a child of the crt.TabPanel it is inserted into");
		declaredTab.ParentName.Should().Be(MobileTabsPanel,
			because: "the declared tab is a tab of the template's strip, beside the Details tab");
		declaredTab.PropertyName.Should().Be("items",
			because: "a tab strip holds its tabs in items");
		declaredTab.Index.Should().Be(1,
			because: "the declared tab sits right after the template's Details tab, which keeps position 0");
		Element(guide, "CaseHistoryTab").Index.Should().Be(2,
			because: "position 0 is the Details tab and position 1 is the declared tab, so the first page tab "
				+ "follows both");
	}

	[Test]
	[Description("A page whose web general tab holds nothing gets no empty mobile tab: the declared AdditionalInfoTab is removed as empty, the web tab's pairs are dropped with it, and page-authored tabs are numbered right after the Details tab (index 1) since the declared index is no longer claimed.")]
	public void Analyze_ShouldDropTheDeclaredTab_WhenTheWebGeneralTabIsEmpty() {
		// Arrange
		JsonObject fixture = WithGeneralInfoTabContentMovedToCaseHistoryTab(LoadFixture());

		// Act
		MobilePageConversionGuide guide = Convert(fixture);

		// Assert
		guide.ViewConfigDiff.Should().NotContain(o => o.Name == DeclaredAdditionalInfoTab,
			because: "an empty converter-created tab is removed rather than shipped as an empty tab");
		IsNestedIn(guide, Element(guide, GeneralTabContent[0]).Name, "CaseHistoryTab").Should().BeTrue(
			because: "the moved content is still converted, inside the page tab that now holds it");
		Element(guide, "CaseHistoryTab").Index.Should().Be(1,
			because: "with the declared tab gone, index 1 is free and the first page tab follows the Details tab");
	}

	[Test]
	[Description("A page that KEEPS a multi-column web GeneralInfoTabContainer does not carry its columns onto the declared AdditionalInfoTab: a crt.TabContainer has no columns input, so no adaptive group is built for the tab, and the content is stacked single-column in the tab's Area card instead of keeping an adaptive two-column placement that no later pass would overwrite.")]
	public void Analyze_ShouldNotCarryTheKeptContentGridColumns_OntoTheDeclaredTab() {
		// Arrange
		JsonObject fixture = WithTemplateContentGridRestored(LoadFixture(), columns: 2);

		// Act
		MobilePageConversionGuide guide = Convert(fixture);

		// Assert
		guide.AdaptiveLayout.Should().NotContain(g => g.ContainerName == DeclaredAdditionalInfoTab,
			because: "adaptive per-breakpoint columns is a property of crt.GridContainer only; the web grid's columns "
				+ "must not be attached to a tab");
		foreach (string name in GeneralTabContent) {
			JsonNode layoutConfig = Element(guide, name).Values?["layoutConfig"];
			layoutConfig.Should().NotBeNull(because: $"'{name}' is stacked into the tab's Area card by row");
			layoutConfig!["adaptive"].Should().BeNull(
				because: $"'{name}' does not sit in a multi-column mobile grid, so it must not keep the web grid's "
					+ "adaptive placement");
			layoutConfig["column"]!.GetValue<int>().Should().Be(1,
				because: $"the Area card is a single-column grid, so '{name}' is placed in its only column");
		}
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
	/// the ordinary shape, which the pinned page (the reported one) removed. With <paramref name="columns"/> above
	/// one the grid declares that many web columns and its children fill them left to right, row by row — the
	/// multi-column layout the web template's own grid has.
	/// </summary>
	private static JsonObject WithTemplateContentGridRestored(JsonObject fixture, int columns = 0) {
		JsonObject generalTab = FindNode(fixture["page"]!["viewConfig"]!, "GeneralInfoTab")
			?? throw new AssertionException("fixture no longer carries GeneralInfoTab");
		JsonArray tabItems = generalTab["items"]!.AsArray();
		var restored = new JsonArray();
		foreach (JsonNode child in tabItems.ToList()) {
			tabItems.Remove(child);
			restored.Add(child);
		}
		var grid = new JsonObject {
			["name"] = "GeneralInfoTabContainer", ["type"] = "crt.GridContainer", ["items"] = restored
		};
		if (columns > 1) {
			grid["columns"] = new JsonArray([.. Enumerable.Repeat<JsonNode>("minmax(64px, 1fr)", columns)
				.Select(column => column.DeepClone())]);
			for (int i = 0; i < restored.Count; i++) {
				restored[i]!["layoutConfig"] = new JsonObject {
					["column"] = i % columns + 1, ["row"] = i / columns + 1, ["colSpan"] = 1, ["rowSpan"] = 1
				};
			}
		}
		tabItems.Add(grid);
		return fixture;
	}

	/// <summary>
	/// Moves the page's general-tab content out of the template-owned <c>GeneralInfoTab</c> into the
	/// page-authored <c>CaseHistoryTab</c>, leaving the web general tab empty while keeping every name the
	/// fixture's usability floor requires.
	/// </summary>
	private static JsonObject WithGeneralInfoTabContentMovedToCaseHistoryTab(JsonObject fixture) {
		JsonNode viewConfig = fixture["page"]!["viewConfig"]!;
		JsonArray generalTabItems = (FindNode(viewConfig, "GeneralInfoTab")
			?? throw new AssertionException("fixture no longer carries GeneralInfoTab"))["items"]!.AsArray();
		JsonArray historyTabItems = (FindNode(viewConfig, "CaseHistoryTab")
			?? throw new AssertionException("fixture no longer carries CaseHistoryTab"))["items"]!.AsArray();
		foreach (JsonNode child in generalTabItems.ToList()) {
			generalTabItems.Remove(child);
			historyTabItems.Add(child);
		}
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
			webTemplateBaselineNodes: webBaselineNodes,
			mobileRequestTypes: NoRequestRegistry);
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
