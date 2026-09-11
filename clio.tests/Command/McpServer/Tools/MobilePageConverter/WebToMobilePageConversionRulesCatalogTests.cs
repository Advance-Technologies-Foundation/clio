using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.MobilePageConverter;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer.Tools.MobilePageConverter;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class WebToMobilePageConversionRulesCatalogTests {

	/// <summary>True when the rule's filters select the given mobile component type.</summary>
	private static bool Targets(ComponentPropertyOverrideRule rule, string type) =>
		rule.Filters.Any(f => string.Equals(f.Type, type, StringComparison.OrdinalIgnoreCase));


	private static Stream JsonStream(string json) => new MemoryStream(Encoding.UTF8.GetBytes(json));

	[Test]
	[Description("The bundled rules resource parses into the seeded template and component groups.")]
	public void LoadBundled_ReturnsSeededTemplatesAndComponents() {
		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.LoadBundled();

		rules.Should().NotBeNull();
		rules.Version.Should().Be("latest");
		rules.Templates.Should().Contain(t => t.Web == "PageWithTabsFreedomTemplate" && t.Mobile == "MobilePageWithTabsFreedomTemplate");
		rules.Components.Should().Contain(c =>
			c.Filters.Any(f => f.Type == "crt.DataGrid") && c.ViewConfigTemplates.Count > 0,
			because: "the grid mapping now lives in components as a filters + viewConfigTemplates group "
				+ "(the root viewConfigTemplates section was folded in)");
	}

	[Test]
	[Description("Each bundled template rule declares isFormPage matching whether it is an edit/form (record) template or a list/section/blank one — the authoritative source MobilePageConversionGuideTool.IsFormPage consults before falling back to its hardcoded template-name heuristic.")]
	public void LoadBundled_TemplatesDeclareIsFormPage() {
		// Arrange & Act
		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.LoadBundled();

		// Assert
		rules.Templates.Single(t => t.Web == "BasePageFreedomTemplate").IsFormPage.Should().BeTrue(
			because: "the base non-tabbed record page is a form page");
		rules.Templates.Single(t => t.Web == "PageWithTabsFreedomTemplate").IsFormPage.Should().BeTrue(
			because: "the tabbed record page is a form page");
		rules.Templates.Single(t => t.Web == "PageWithRightAreaAndTabsFreedomTemplate").IsFormPage.Should().BeTrue(
			because: "the right-area tabbed record page is a form page, matching the hardcoded fallback's intent "
				+ "even though it was never in the old 3-name allowlist");
		rules.Templates.Single(t => t.Web == "BasePageTemplate").IsFormPage.Should().BeTrue(
			because: "the classic base page template also maps to a mobile record page");
		rules.Templates.Single(t => t.Web == "ListPageV3Template").IsFormPage.Should().BeFalse(
			because: "a section/list template is not a form page and omits the key, defaulting to false");
		rules.Templates.Single(t => t.Web == "ListPageV2Template").IsFormPage.Should().BeFalse(
			because: "a section/list template is not a form page and omits the key, defaulting to false");
		rules.Templates.Single(t => t.Web == "BlankPageTemplate").IsFormPage.Should().BeFalse(
			because: "a blank page is not a form page and omits the key, defaulting to false");
		rules.Templates.Single(t => t.Web == "BaseTemplate").IsFormPage.Should().BeFalse(
			because: "the structural root template is not itself a form page and omits the key, defaulting to false");
	}

	[Test]
	[Description("ENG-95046: the grid mapping is a components entry carrying filters + viewConfigTemplates (the root viewConfigTemplates section was folded in). It carries NO web/mobile pair: the filters identify the source and the template's own value.type declares the target — which is also what the converter derives the element's mobile type from.")]
	public void LoadBundled_GridEntryCarriesFiltersAndViewConfigTemplate() {
		// Arrange & Act
		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.LoadBundled();

		// Assert
		ComponentEquivalenceRule grid = rules.Components.Single(c => c.Filters.Any(f => f.Type == "crt.DataGrid"));
		grid.Filters.Select(f => f.Type).Should().BeEquivalentTo(new[] { "crt.DataGrid", "crt.DataTable" },
			because: "a filter naming only crt.DataGrid would leave a crt.DataTable list without a row");
		grid.Web.Should().BeEmpty(
			because: "a template group carries no web/mobile pair — its target is the template's value.type");
		grid.Mobile.Should().BeEmpty(
			because: "the mobile type is derived from viewConfigTemplates[].value.type, not a mobile list");
	}

	[Test]
	[Description("The bundled type-swap components (crt.Checkbox → crt.Toggle, crt.HtmlEditor → crt.RichTextEditor) use the same filter format as the grid, with preserveSourceProperties: filters identify the source, value.type declares the target, and every source property except the named type is copied across (so the caller rebuilds nothing).")]
	public void LoadBundled_TypeSwapComponentsPreserveSourceAndRetype() {
		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.LoadBundled();

		void AssertTypeSwap(string webType, string mobileType) {
			ComponentEquivalenceRule entry = rules.Components.Single(c => c.Filters.Any(f => f.Type == webType));
			entry.Web.Should().BeEmpty(because: $"{webType} is now a filter-based template entry, not a web/mobile pair");
			ViewConfigTemplateRule template = entry.ViewConfigTemplates.Should().ContainSingle().Subject;
			template.PreserveSourceProperties.Should().BeTrue(
				because: "a like-for-like field conversion keeps its source properties and only retypes");
			template.Value!.Value.GetProperty("type").GetString().Should().Be(mobileType,
				because: "the template's value.type is what the converter resolves the element to");
		}

		AssertTypeSwap("crt.Checkbox", "crt.Toggle");
		AssertTypeSwap("crt.HtmlEditor", "crt.RichTextEditor");
	}

	[Test]
	[Description("ENG-95046: the bundled view-config template produces the list row as DATA. It carries no web/mobile pair — the filters identify the source and the template's own value.type declares the target, which is also what gates it.")]
	public void LoadBundled_ViewConfigTemplateBuildsTheListRow() {
		// Arrange & Act
		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.LoadBundled();

		// Assert
		ComponentEquivalenceRule grid = rules.Components.Single(c => c.Filters.Any(f => f.Type == "crt.DataGrid"));
		grid.Filters.Select(f => f.Type).Should().BeEquivalentTo(new[] { "crt.DataGrid", "crt.DataTable" },
			because: "a filter naming only crt.DataGrid would leave a crt.DataTable list without a row");
		ViewConfigTemplateRule template = grid.ViewConfigTemplates.Single();
		template.ParentName.Should().Be("{{ diff.parentName }}",
			because: "the list row stays where the walk places it, so the template ECHOES the computed parent — "
				+ "echoing keeps the walked placement (only a DIFFERENT value would retarget, as the FAB rule does)");
		template.PropertyName.Should().Be("{{ diff.propertyName }}",
			because: "the slot is echoed for the same reason — the row is not retargeted");
		string skeleton = template.Value!.Value.GetRawText();
		skeleton.Should().Contain("\"type\": \"crt.List\"",
			because: "the template's own declared type is what gates it against the element's resolved mobile type — "
				+ "no second declaration ties the two together");
		skeleton.Should().Contain("crt.ListItem",
			because: "the row element the mobile list expects inside itemLayout is a crt.ListItem");
		skeleton.Should().Contain("\"title\": \"${{ source.columns[0].code }}\"",
			because: "the registry declares crt.ListItem.title a plain string binding — the { value } BODY shape "
				+ "there renders an empty Title column while the body rows still look correct");
		skeleton.Should().Contain("\"$each\": \"source.columns[1:]\"",
			because: "every column after the leading one becomes its own body entry");
	}

	[Test]
	[Description("The bundled MainHeader -> FAB rule is a path-scoped, placement-driving template: path scopes it to MainHeader, filters match crt.Button/crt.MenuItem, and the template retargets into FloatingActionButton.menuItems, retyping to crt.MenuItem and naming only caption/visible/clicked (a visual denylist via an authoritative template).")]
	public void LoadBundled_HeaderToFabRule_ScopedAndRetargeting() {
		// Arrange & Act
		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.LoadBundled();

		// Assert
		ComponentEquivalenceRule fab = rules.Components.Single(c => c.Path.Contains("MainHeader"));
		fab.Filters.Select(f => f.Type).Should().BeEquivalentTo(new[] { "crt.Button", "crt.MenuItem" },
			because: "the whole header action subtree — buttons and their menu items — is converted");
		ViewConfigTemplateRule template = fab.ViewConfigTemplates.Should().ContainSingle().Subject;
		template.ParentName.Should().Be("FloatingActionButton",
			because: "the template DRIVES placement into the FAB rather than echoing the walked position");
		template.PropertyName.Should().Be("menuItems",
			because: "converted actions land in the FAB's menuItems slot");
		template.PreserveSourceProperties.Should().BeFalse(
			because: "an authoritative template carries only the properties it names — that IS the visual denylist");
		JsonElement value = template.Value!.Value;
		value.GetProperty("type").GetString().Should().Be("crt.MenuItem",
			because: "the header action becomes a mobile menu item");
		string skeleton = value.GetRawText();
		skeleton.Should().Contain("caption").And.Contain("visible").And.Contain("clicked",
			because: "only caption/visible/clicked are carried onto the menu item");
		skeleton.Should().NotContain("style").And.NotContain("icon").And.NotContain("color",
			because: "visual properties are denylisted — an authoritative template never enumerates them");
	}

	[Test]
	[Description("ENG-94230: the bundled rules carry the metric style override — extra-small text and a hidden border nested under config, merging — using the registry's real property paths, not the ticket's prose (there is no top-level size/hideBorder input).")]
	public void LoadBundled_ReturnsSeededMetricStyleOverride() {
		// Arrange & Act
		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.LoadBundled();

		// Assert
		ComponentPropertyOverrideRule metric = rules.ComponentPropertyOverrides
			.Single(o => Targets(o, "crt.IndicatorWidget"));
		metric.MergeNestedObjects.Should().BeTrue(
			because: "the rule targets nested leaves — replacing config wholesale would destroy the aggregation subtree");
		JsonElement config = metric.Values["config"];
		config.GetProperty("text").GetProperty("fontSizeMode").GetString().Should().Be("extra-small",
			because: "the registry's fontSizeMode enum spells XS as 'extra-small'");
		config.GetProperty("layout").GetProperty("border").GetProperty("hidden").GetBoolean().Should().BeTrue(
			because: "hide-border lives at layout.border.hidden (WidgetBorderConfig)");
		config.TryGetProperty("theme", out _).Should().BeFalse(
			because: "the theme is a deliberate non-goal — the default 'without-fill' already gives the plain white look");
	}

	[Test]
	[Description("Every override rule carries ONLY data: a component type, the values to stamp and whether they merge. No rule may carry caller-facing prose, because the rules file is resolved at runtime and the guide's constraints/nextSteps are the caller's instruction channel.")]
	public void LoadBundled_OverridesCarryDataOnly() {
		// Arrange & Act
		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.LoadBundled();

		// Assert
		rules.ComponentPropertyOverrides.Should().OnlyContain(o => o.Values.Count > 0,
			because: "a rule without values cannot stamp anything");
		rules.ComponentPropertyOverrides.Should().OnlyContain(o => o.Filters != null && o.Filters.Count > 0,
			because: "filters are the rule's ONLY selector — an ABSENT list makes the pass skip the rule "
				+ "outright, and an EMPTY one would stamp onto every insert of every type; no standard wants "
				+ "either, so the bundled file must always name what it targets");
		rules.ComponentPropertyOverrides
			.SelectMany(o => o.Filters ?? [])
			.Should().OnlyContain(f => !string.IsNullOrWhiteSpace(f.Type),
				because: "a bundled filter that names no type would widen its standard across component types");
	}

	[Test]
	[Description("The pre-existing spacing overrides keep replace semantics: their promise that the web gap is discarded wholesale is only delivered by replacing, never by merging.")]
	public void LoadBundled_SpacingOverridesKeepReplaceSemantics() {
		// Arrange & Act
		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.LoadBundled();

		// Assert
		rules.ComponentPropertyOverrides
			.Where(o => Targets(o, "crt.GridContainer") || Targets(o, "crt.FlexContainer"))
			.Should().HaveCount(2)
			.And.OnlyContain(o => !o.MergeNestedObjects,
				because: "the spacing rules promise the web gap is discarded wholesale");
	}

	[Test]
	[Description("ListPageV2Template (the older section list template) mirrors ListPageV3Template's rule verbatim: same mobile target, same container correspondence, and the same DataTable->List / FolderTree->FolderTreeActions (with its carryProperties whitelist) components, so a section built on the legacy template converts identically to one on the current template.")]
	public void LoadBundled_ListPageV2TemplateMirrorsListPageV3() {
		// Arrange & Act
		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.LoadBundled();

		// Assert
		TemplateMappingRule v3 = rules.Templates.Single(t => t.Web == "ListPageV3Template");
		TemplateMappingRule v2 = rules.Templates.Single(t => t.Web == "ListPageV2Template");

		v2.Mobile.Should().Be(v3.Mobile, because: "both list-page generations target the same mobile list template");
		v2.Containers.Should().BeEquivalentTo(v3.Containers,
			because: "the two web templates lay out the section list identically, so their container correspondence must match");

		ComponentMappingRule v2FolderTree = v2.Components.Single(c => c.Web == "FolderTree");
		ComponentMappingRule v3FolderTree = v3.Components.Single(c => c.Web == "FolderTree");
		v2FolderTree.Mobile.Should().Be(v3FolderTree.Mobile);
		v2FolderTree.MobileType.Should().Be(v3FolderTree.MobileType);
		v2FolderTree.CarryProperties.Should().BeEquivalentTo(v3FolderTree.CarryProperties,
			because: "the folder-tree schema binding (sourceSchemaName/rootSchemaName) must be carried for either template generation");

		ComponentMappingRule v2DataTable = v2.Components.Single(c => c.Web == "DataTable");
		ComponentMappingRule v3DataTable = v3.Components.Single(c => c.Web == "DataTable");
		v2DataTable.Mobile.Should().Be(v3DataTable.Mobile);
	}

	[Test]
	[Description("The bundled rules carry a components template group, scoped to a crt.TabPanel literally named 'Tabs' (the converter-created strip), that stamps scrollable: true and bodyBackgroundColor: transparent while preserving every other source property — so the converted tab strip scrolls and does not paint an opaque backdrop over the Area cards its converted tabs wrap, without touching a differently-named crt.TabPanel.")]
	public void LoadBundled_TabPanelTemplateStampsScrollableAndTransparentBackground() {
		// Arrange & Act
		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.LoadBundled();

		// Assert
		ComponentEquivalenceRule tabPanel = rules.Components.Single(c =>
			c.Filters.Any(f => f.Type == "crt.TabPanel"));
		ElementFilterRule filter = tabPanel.Filters.Should().ContainSingle().Subject;
		filter.Type.Should().Be("crt.TabPanel", because: "only the converted tab strip's own component type is targeted");
		filter.Values["name"].GetString().Should().Be("Tabs",
			because: "the rule must match the converter-created strip by name, not every crt.TabPanel on the page");
		ViewConfigTemplateRule template = tabPanel.ViewConfigTemplates.Should().ContainSingle().Subject;
		template.PreserveSourceProperties.Should().BeTrue(
			because: "the rule only stamps two properties and must keep everything else the converter already put on the strip");
		JsonElement value = template.Value!.Value;
		value.GetProperty("type").GetString().Should().Be("crt.TabPanel", because: "the template targets the tab-strip type itself, not a retype");
		value.GetProperty("scrollable").GetBoolean().Should().BeTrue(because: "a converted tab strip must scroll on mobile");
		value.GetProperty("bodyBackgroundColor").GetString().Should().Be("transparent",
			because: "the converted tab strip must not paint an opaque background over the Area cards it wraps");
	}

	[Test]
	[Description("The bundled rules store only SUPPORTED requests (web→mobile); unsupported web requests are intentionally absent (a request not in the map is flagged at conversion time).")]
	public void LoadBundled_ReturnsSeededRequests() {
		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.LoadBundled();

		rules.Requests.Should().NotBeEmpty();
		rules.Requests.Should().Contain(r =>
			r.Web == "crt.SaveRecordRequest" && r.Mobile == "crt.SaveRecordRequest" && r.Category == "DirectMapping");
		rules.Requests.Should().OnlyContain(r => !string.IsNullOrEmpty(r.Mobile),
			because: "the map lists only requests supported on mobile; unsupported ones are simply not stored");
	}

	[TestCase("crt.OpenPageRequest", "schemaName", "web-page",
		TestName = "LoadBundled_OpenPageRequest_DeclaresItsPageTarget")]
	[TestCase("crt.CreateRecordRequest", "entityName", "entity-default-mobile-page",
		TestName = "LoadBundled_CreateRecordRequest_DeclaresItsObjectTarget")]
	[TestCase("crt.UpdateRecordRequest", "entityName", "entity-default-mobile-page",
		TestName = "LoadBundled_UpdateRecordRequest_DeclaresItsObjectTarget")]
	[Description("The bundled rules declare which params key carries each navigation target and what it names, so target verification is data-driven rather than hardcoded (ENG-94839).")]
	public void LoadBundled_NavigatingRequests_DeclareTheirTarget(
		string webRequest, string expectedParam, string expectedKind) {
		// Arrange & Act
		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.LoadBundled();

		// Assert
		RequestMappingRule rule = rules.Requests.Single(r => r.Web == webRequest);
		rule.TargetParam.Should().Be(expectedParam,
			because: "the probe reads the target out of that params key and nowhere else");
		rule.TargetKind.Should().Be(expectedKind,
			because: "the kind decides HOW clio verifies the target exists on mobile");
	}

	[Test]
	[Description("A request with no navigation target declares neither field, so it is never target-checked.")]
	public void LoadBundled_NonNavigatingRequest_DeclaresNoTarget() {
		// Arrange & Act
		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.LoadBundled();

		// Assert
		RequestMappingRule rule = rules.Requests.Single(r => r.Web == "crt.SaveRecordRequest");
		rule.TargetParam.Should().BeNull(because: "saving a record navigates nowhere");
		rule.TargetKind.Should().BeNull(because: "an undeclared target must never be verified");
	}

	[Test]
	[Description("Target declaration is all-or-nothing: a rule that names a params key must also say what it names, or the check cannot run.")]
	public void LoadBundled_TargetDeclarations_ArePaired() {
		// Arrange & Act
		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.LoadBundled();

		// Assert
		rules.Requests.Should().OnlyContain(
			r => string.IsNullOrWhiteSpace(r.TargetParam) == string.IsNullOrWhiteSpace(r.TargetKind),
			because: "half a declaration silently disables the check it looks like it enabled");
	}

	[Test]
	[Description("Bundled tabbed template carries container-name correspondence: the two type-aligned general-tab pairs GeneralInfoTab->GeneralInfoTab and GeneralInfoTabContainer->GeneralTabContainer (ENG-94951), CardContentWrapper->GeneralTabContainer for general non-tab content, SideAreaProfileContainer->AreaProfileContainer for the profile island (its children go INSIDE the profile Area card, never directly into the general tab's grid), and positional CardContentWrapper:top/:bottom -> Tabs:top/:bottom entries.")]
	public void LoadBundled_TemplatesCarryContainerCorrespondence() {
		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.LoadBundled();

		TemplateMappingRule tabbed = rules.Templates.First(t =>
			t.Web == "PageWithTabsFreedomTemplate" && t.Mobile == "MobilePageWithTabsFreedomTemplate");
		tabbed.Containers.Should().Contain(c => c.Web == "Tabs" && c.Mobile == "Tabs");
		tabbed.Containers.Should().Contain(c => c.Web == "FeedTabContainer" && c.Mobile == "FeedContainer");
		tabbed.Containers.Should().Contain(c => c.Web == "CardContentWrapper" && c.Mobile == "GeneralTabContainer",
			because: "the wrapper's general non-tab content fills the mobile general tab's grid");
		tabbed.Containers.Should().Contain(c => c.Web == "SideAreaProfileContainer" && c.Mobile == "AreaProfileContainer",
			because: "the web profile island merges into the template's profile Area card — its children " +
				"land inside AreaProfileContainer, not directly in GeneralTabContainer, so the Area is never left empty");
		tabbed.Containers.Should().Contain(c => c.Web == "GeneralInfoTab" && c.Mobile == "GeneralInfoTab",
			because: "without it the general-information tab is subtracted as inherited chrome and its content is "
				+ "hoisted straight into the crt.TabPanel, which renders only tabs — the whole tab is lost (ENG-94951)");
		tabbed.Containers.Should().Contain(c => c.Web == "GeneralInfoTabContainer" && c.Mobile == "GeneralTabContainer",
			because: "the tab's content grid is the second half of the pair: a page that KEEPS the template's grid "
				+ "must reuse the mobile one rather than have the grid subtracted as inherited chrome");
		tabbed.Containers.Should().Contain(c => c.Web == "CardContentWrapper:top" && c.Mobile == "Tabs:top");
		tabbed.Containers.Should().Contain(c => c.Web == "CardContentWrapper:bottom" && c.Mobile == "Tabs:bottom");
	}

	[Test]
	[Description("Bundled right-area template (PageWithRightAreaAndTabsFreedomTemplate -> BaseMobilePageTemplate) converts the web tab strip by pairing Tabs -> Tabs and GeneralInfoTab -> GeneralInfoTab against a mobile template that has neither, and declares ONE declared tab, RightPanelTab, under the converted Tabs at index 1 with a caption resource; the right profile area maps onto that declared tab. No positional entries: the template's only anchor candidate carries no row.")]
	public void LoadBundled_RightAreaTemplateDeclaresExtraTabForTheRightPanel() {
		// Arrange
		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.LoadBundled();

		// Act
		TemplateMappingRule rightArea = rules.Templates.Single(t => t.Web == "PageWithRightAreaAndTabsFreedomTemplate");

		// Assert
		rightArea.Mobile.Should().Be("BaseMobilePageTemplate",
			because: "the web template has no Feed/Attachments, so it targets the base mobile record page, not the tabbed one");
		rightArea.Containers.Should().Contain(c => c.Web == "Tabs" && c.Mobile == "Tabs",
			because: "the mobile template has no Tabs: the pair makes the converter CREATE the strip from the web element");
		rightArea.Containers.Should().Contain(c => c.Web == "GeneralInfoTab" && c.Mobile == "GeneralInfoTab",
			because: "the general tab is converted as a tab of its own under the created strip");
		rightArea.Containers.Should().Contain(c => c.Web == "RightAreaProfileContainer" && c.Mobile == "RightPanelTab",
			because: "the right profile area walks its content into the declared tab");
		rightArea.Containers.Should().NotContain(c => c.Web.Contains(':'),
			because: "no mobile anchor with a layoutConfig row exists on BaseMobilePageTemplate, so positional entries would be dead");
		DeclaredElementRule extra = rightArea.DeclaredElements.Should().ContainSingle(
			because: "exactly one container is declared on top of the mobile template").Subject;
		extra.Name.Should().Be("RightPanelTab", because: "the declared tab is the containers pair's mobile side");
		extra.Type.Should().Be("crt.TabContainer", because: "a tab is a crt.TabContainer");
		extra.ParentName.Should().Be("Tabs", because: "the declared tab lives in the converted strip");
		extra.PropertyName.Should().Be("items", because: "a tab strip holds its tabs in items");
		extra.Index.Should().Be(1, because: "the declared tab follows General information and precedes page-authored tabs");
		extra.CaptionResource.Should().NotBeNull(because: "a tab needs a caption");
		extra.CaptionResource.Key.Should().Be("RightPanelTab_caption", because: "the caption is a page resource keyed by the tab name");
		extra.CaptionResource.Value.Should().NotBeNullOrWhiteSpace(because: "the resource must carry its text");
	}

	[Test]
	[Description("Bundled tabbed template maps the attachments detail as a name-only, same-component twin: web AttachmentList -> mobile AttachmentFileList (both crt.FileList) with no carryProperties whitelist, so the page's delta over the web-template baseline — recordColumnName included — merges onto the template-provided element instead of being pruned as chrome.")]
	public void LoadBundled_TabbedTemplateMapsAttachmentListTwin() {
		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.LoadBundled();

		TemplateMappingRule tabbed = rules.Templates.First(t =>
			t.Web == "PageWithTabsFreedomTemplate" && t.Mobile == "MobilePageWithTabsFreedomTemplate");
		ComponentMappingRule attachments = tabbed.Components.Single(c => c.Web == "AttachmentList");
		attachments.Mobile.Should().Be("AttachmentFileList",
			because: "the web AttachmentList maps to the mobile AttachmentFileList element");
		attachments.CarryProperties.Should().BeEmpty(
			because: "it is the same component on both sides (crt.FileList) — a name-only twin carries the page's delta over the web-template baseline, no whitelist needed");
	}

	[Test]
	[Description("The bundled rules carry the empty-container removal allowlist: the CLOSED set of five layout container types removable when empty. The set is a deliberate decision pinned here — widening it must be an explicit change with its own review, never a drive-by edit or registry inference.")]
	public void LoadBundled_EmptyContainerRemoval_CarriesClosedAllowlist() {
		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.LoadBundled();

		rules.EmptyContainerRemoval.Should().NotBeNull();
		rules.EmptyContainerRemoval.RemovableTypes.Should().BeEquivalentTo(
			["crt.FlexContainer", "crt.GridContainer", "crt.TabPanel", "crt.TabContainer", "crt.ExpansionPanel"],
			because: "the removable set is a closed allowlist of disposable layout scaffolding — content-bearing " +
				"containers (crt.List, crt.Tabs) must never appear here");
	}

	[Test]
	[Description("A rules file without the excludedComponents section parses to an empty list — the pass is then a no-op (data-switched feature), matching how emptyContainerRemoval/tabAreaLayers degrade when absent.")]
	public void ParseStream_WithoutExcludedComponents_ParsesToEmptyList() {
		const string json = """{ "version": "8.3.3", "templates": [], "components": [] }""";

		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.ParseStream(JsonStream(json));

		rules.ExcludedComponents.Should().BeEmpty(
			because: "an absent section must switch the removal pass off rather than throw or default to null");
	}

	[Test]
	[Description("ParseStream parses a excludedComponents group into the typed filter rule: type, parentType and the optional propertiesContainerName all round-trip.")]
	public void ParseStream_WithExcludedComponents_ParsesTypedFilterRule() {
		const string json = """
			{
			  "version": "8.3.3",
			  "excludedComponents": [
			    {
			      "filters": [
			        { "type": "crt.SearchFilter", "parentType": "crt.ExpansionPanel", "propertiesContainerName": "tools" }
			      ]
			    }
			  ]
			}
			""";

		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.ParseStream(JsonStream(json));

		ExcludedComponentFilterRule filter = rules.ExcludedComponents.Single().Filters.Single();
		filter.Type.Should().Be("crt.SearchFilter",
			because: "the banned type must survive parsing verbatim — the pass matches on this string");
		filter.ParentType.Should().Be("crt.ExpansionPanel",
			because: "the host type is what scopes the search; losing it would widen the ban to the whole page");
		filter.PropertiesContainerName.Should().Be("tools",
			because: "the optional slot must bind when present — dropping it would widen the ban to the host's other properties");
	}

	[Test]
	[Description("propertiesContainerName is genuinely optional on a excludedComponents filter — it parses to null when omitted, so the runtime pass can fall back to searching the whole host subtree instead of one named property.")]
	public void ParseStream_ExcludedComponentsWithoutPropertiesContainerName_ParsesToNull() {
		const string json = """
			{
			  "version": "8.3.3",
			  "excludedComponents": [
			    { "filters": [ { "type": "usr.Foo", "parentType": "usr.Bar" } ] }
			  ]
			}
			""";

		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.ParseStream(JsonStream(json));

		rules.ExcludedComponents.Single().Filters.Single().PropertiesContainerName.Should().BeNull(
			because: "an absent propertiesContainerName means 'search the whole host subtree', not 'match nothing'");
	}

	[Test]
	[Description("The bundled rules carry the excludedComponents entry for crt.SearchFilter inside crt.ExpansionPanel.tools: the search field does not fit the panel's compact icon-only header strip, so it is stripped from tools specifically (not banned everywhere on the page).")]
	public void LoadBundled_ExcludedComponents_CarriesSearchFilterInsideExpansionPanelToolsRule() {
		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.LoadBundled();

		rules.ExcludedComponents.Should().NotBeEmpty(
			because: "the bundled rules ship the crt.SearchFilter / crt.ExpansionPanel.tools exclusion");
		ExcludedComponentFilterRule filter = rules.ExcludedComponents
			.SelectMany(g => g.Filters)
			.Single(f => f.Type == "crt.SearchFilter");
		filter.ParentType.Should().Be("crt.ExpansionPanel",
			because: "the defect is positional — crt.SearchFilter does not fit THIS host's tools strip, not unsupported everywhere");
		filter.PropertiesContainerName.Should().Be("tools",
			because: "the search is scoped to the panel's tools property, not its whole mobileValues subtree");
		filter.Note.Should().NotBeNullOrWhiteSpace(
			because: "the rules file is where the next rule author looks for WHY an exclusion exists — the drop "
				+ "reason deliberately carries only the mechanical fact, so the motivation has to live here");
	}

	[Test]
	[Description("The bundled rules carry the designer's 2-layer tab body (tab-body grid nesting the Area card) for converter-created tabs.")]
	public void LoadBundled_TabAreaLayers_CarryDesignerTabBodyProps() {
		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.LoadBundled();

		rules.TabAreaLayers.Should().NotBeNull();
		rules.TabAreaLayers.TabComponentType.Should().Be("crt.TabContainer",
			because: "which element gets the layers is data, not a hardcoded type in the engine");
		SynthesizedContainerRule main = rules.TabAreaLayers.MainTabContainer;
		main.NamePrefix.Should().Be("MainTabContainer_");
		main.Values["type"].GetString().Should().Be("crt.GridContainer");
		main.Values["padding"].GetProperty("bottom").GetString().Should().Be("medium");
		SynthesizedContainerRule area = main.AreaContainer;
		area.Should().NotBeNull(because: "the Area card rule nests inside the tab-body rule, mirroring the DOM");
		area.NamePrefix.Should().Be("GridContainer_");
		area.Values["type"].GetString().Should().Be("crt.GridContainer");
		area.Values["color"].GetString().Should().Be("primary");
		area.Values["borderRadius"].GetString().Should().Be("medium");
		area.AreaContainer.Should().BeNull(because: "the Area card is the innermost container — it nests nothing");
	}

	[Test]
	[Description("A rules file without the tabAreaLayers group parses to null — the tab-area pass is then a no-op (data-switched feature).")]
	public void ParseStream_WithoutTabAreaLayers_ParsesToNull() {
		const string json = """{ "version": "8.3.3", "templates": [], "components": [] }""";

		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.ParseStream(JsonStream(json));

		rules.TabAreaLayers.Should().BeNull();
	}

	[Test]
	[Description("ParseStream parses the tabAreaLayers group into the typed rule (nested tab-body → Area chain, prefixes + verbatim values).")]
	public void ParseStream_WithTabAreaLayers_ParsesTypedRule() {
		const string json = """
			{
			  "version": "8.3.3",
			  "tabAreaLayers": {
			    "note": "n",
			    "tabComponentType": "usr.CustomTab",
			    "mainTabContainer": {
			      "namePrefix": "MainTabContainer_",
			      "values": { "type": "crt.GridContainer", "alignItems": "stretch" },
			      "areaContainer": { "namePrefix": "GridContainer_", "values": { "type": "crt.GridContainer", "color": "primary" } }
			    }
			  }
			}
			""";

		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.ParseStream(JsonStream(json));

		rules.TabAreaLayers.Should().NotBeNull();
		rules.TabAreaLayers.Note.Should().Be("n");
		rules.TabAreaLayers.TabComponentType.Should().Be("usr.CustomTab");
		rules.TabAreaLayers.MainTabContainer.NamePrefix.Should().Be("MainTabContainer_");
		rules.TabAreaLayers.MainTabContainer.Values["alignItems"].GetString().Should().Be("stretch");
		rules.TabAreaLayers.MainTabContainer.AreaContainer.NamePrefix.Should().Be("GridContainer_");
		rules.TabAreaLayers.MainTabContainer.AreaContainer.Values["color"].GetString().Should().Be("primary");
	}

	[Test]
	[Description("A tabAreaLayers group that omits tabComponentType falls back to the platform's own tab type, so an older rules file keeps working.")]
	public void ParseStream_TabAreaLayersWithoutTabComponentType_FallsBackToPlatformTabType() {
		const string json = """
			{
			  "version": "8.3.3",
			  "tabAreaLayers": {
			    "mainTabContainer": {
			      "namePrefix": "MainTabContainer_",
			      "values": { "type": "crt.GridContainer" },
			      "areaContainer": { "namePrefix": "GridContainer_", "values": { "type": "crt.GridContainer" } }
			    }
			  }
			}
			""";

		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.ParseStream(JsonStream(json));

		rules.TabAreaLayers.TabComponentType.Should().Be("crt.TabContainer");
	}

	[Test]
	[Description("ParseStream supports many-to-many component equivalence rules (lists on both sides).")]
	public void ParseStream_SupportsManyToManyComponentRule() {
		const string json = """
			{
			  "version": "8.3.3",
			  "templates": [],
			  "components": [
			    { "web": ["crt.A", "crt.B"], "mobile": ["crt.X", "crt.Y"], "category": "WithAdaptation", "note": "n" }
			  ]
			}
			""";

		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.ParseStream(JsonStream(json));

		rules.Version.Should().Be("8.3.3");
		ComponentEquivalenceRule rule = rules.Components.Single();
		rule.Web.Should().BeEquivalentTo("crt.A", "crt.B");
		rule.Mobile.Should().BeEquivalentTo("crt.X", "crt.Y");
		rule.Category.Should().Be("WithAdaptation");
	}

	[Test]
	[Description("GetRulesAsync falls back to the bundled rules when the registry client cannot serve them (CDN not published yet).")]
	public async Task GetRulesAsync_WhenClientUnavailable_FallsBackToBundled() {
		var client = Substitute.For<IWebToMobilePageConversionRulesRegistryClient>();
		client.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromException<ComponentRegistryFetchResult>(
				new ComponentRegistryUnavailableException("latest", "https://cdn.example")));
		var catalog = new WebToMobilePageConversionRulesCatalog(client);

		WebToMobilePageConversionRules rules = await catalog.GetRulesAsync("latest");

		rules.Should().NotBeNull();
		rules.Components.Should().Contain(c => c.Filters.Any(f => f.Type == "crt.DataGrid"),
			because: "the bundled rules are the fallback source today");
	}

	[Test]
	[Description("GetRulesAsync returns the rules served by the registry client when available (CDN/cache/local override).")]
	public async Task GetRulesAsync_WhenClientServesRules_ReturnsThem() {
		const string json = """{ "version": "9.9.9", "templates": [], "components": [] }""";
		var client = Substitute.For<IWebToMobilePageConversionRulesRegistryClient>();
		client.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(new ComponentRegistryFetchResult(
				JsonStream(json), "9.9.9", ComponentRegistrySource.Cdn)));
		var catalog = new WebToMobilePageConversionRulesCatalog(client);

		WebToMobilePageConversionRules rules = await catalog.GetRulesAsync("9.9.9");

		rules.Version.Should().Be("9.9.9",
			because: "when the client serves rules, the catalog must use them rather than the bundled fallback");
	}

	[Test]
	[Description("The JSONPath index and slice the mandated template format relies on (source.columns[0].code and source.columns[1:]) must be supported by the JSON library already in use — the format cannot be implemented as written otherwise.")]
	public void JsonPathIndexAndSlice_AreSupportedByTheJsonLibraryInUse() {
		// Arrange
		JObject node = JObject.Parse("""
			{ "items": "$Grid", "columns": [ { "code": "A" }, { "code": "B" }, { "code": "C" } ] }
			""");

		// Act
		JToken lead = node.SelectToken("columns[0].code");
		List<JToken> rest = node.SelectTokens("columns[1:]").ToList();
		JToken items = node.SelectToken("items");

		// Assert
		lead?.ToString().Should().Be("A",
			because: "the template addresses the row's leading value by index");
		items?.ToString().Should().Be("$Grid",
			because: "a plain property path must keep working alongside the indexed ones");
		rest.Should().HaveCount(2,
			because: "the slice must yield every entry after the first");
		rest.Select(t => t["code"]?.ToString()).Should().ContainInOrder(new[] { "B", "C" },
			because: "the slice must preserve source order, which is what the row's body depends on");
	}
}
