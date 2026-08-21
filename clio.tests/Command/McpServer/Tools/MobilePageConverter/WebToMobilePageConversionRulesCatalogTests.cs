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
			.Single(o => o.Type == "crt.IndicatorWidget");
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
		rules.ComponentPropertyOverrides.Should().OnlyContain(
			o => !string.IsNullOrWhiteSpace(o.Type) && o.Values.Count > 0,
			because: "a rule without a type or values cannot stamp anything");
		rules.ComponentPropertyOverrides.Select(o => o.Type).Should().OnlyHaveUniqueItems(
			because: "the pass indexes by type and silently LAST-WINS, so a duplicate would ship a rule that "
				+ "never fires — cheap to catch here for the bundled file");
	}

	[Test]
	[Description("The pre-existing spacing overrides keep replace semantics: their promise that the web gap is discarded wholesale is only delivered by replacing, never by merging.")]
	public void LoadBundled_SpacingOverridesKeepReplaceSemantics() {
		// Arrange & Act
		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.LoadBundled();

		// Assert
		rules.ComponentPropertyOverrides
			.Where(o => o.Type is "crt.GridContainer" or "crt.FlexContainer")
			.Should().HaveCount(2)
			.And.OnlyContain(o => !o.MergeNestedObjects,
				because: "the spacing rules promise the web gap is discarded wholesale");
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

	[Test]
	[Description("Bundled tabbed template carries container-name correspondence: CardContentWrapper->GeneralTabContainer for general non-tab content, SideAreaProfileContainer->AreaProfileContainer for the profile island (its children go INSIDE the profile Area card, never directly into the general tab's grid), and positional CardContentWrapper:top/:bottom -> Tabs:top/:bottom entries.")]
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
		tabbed.Containers.Should().Contain(c => c.Web == "CardContentWrapper:top" && c.Mobile == "Tabs:top");
		tabbed.Containers.Should().Contain(c => c.Web == "CardContentWrapper:bottom" && c.Mobile == "Tabs:bottom");
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
		filter.Type.Should().Be("crt.SearchFilter");
		filter.ParentType.Should().Be("crt.ExpansionPanel");
		filter.PropertiesContainerName.Should().Be("tools");
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
