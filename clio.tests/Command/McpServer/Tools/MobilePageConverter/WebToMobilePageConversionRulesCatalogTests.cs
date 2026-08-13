using System;
using System.IO;
using System.Linq;
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
[Property("Module", "Command")]
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
			c.Web.Contains("crt.Checkbox") && c.Mobile.Contains("crt.Toggle") && c.Category == "AlternativeAvailable");
		rules.Components.Should().Contain(c =>
			c.Web.Contains("crt.DataGrid") && c.Mobile.Contains("crt.List") && c.Category == "AlternativeAvailable");
	}

	[Test]
	[Description("ENG-95046: the bundled grid rule declares the row synthesis and the grid-only properties to drop, so a converted list's row is data rather than an instruction the caller has to carry out.")]
	public void LoadBundled_GridRuleDeclaresRowLayoutAndDropProperties() {
		// Arrange & Act
		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.LoadBundled();

		// Assert
		ComponentEquivalenceRule grid = rules.Components.Single(c => c.Web.Contains("crt.DataGrid"));
		grid.RowLayout.Should().NotBeNull(
			because: "the crt.ListItem row has no web counterpart to copy — it must be built from the grid's "
				+ "columns, and leaving that to the caller produced lists with no row at all");
		grid.RowLayout.SourceProperty.Should().Be("columns",
			because: "the row is built from the web grid's column array — nothing else in the node describes the row");
		grid.RowLayout.TargetProperty.Should().Be("itemLayout",
			because: "itemLayout is the input the mobile list renders each record with");
		grid.RowLayout.TargetType.Should().Be("crt.ListItem",
			because: "the row element the mobile list expects inside itemLayout is a crt.ListItem");
		grid.RowLayout.BindingFrom.Should().Be("code",
			because: "a column's code is its bound attribute name, which is what the $binding refers to");
		grid.RowLayout.ValueTypeFrom.Should().Be("dataValueType",
			because: "the title may bind only a text column, and dataValueType is where a column says what it is");
		grid.RowLayout.TitleValueTypes.Should().BeEquivalentTo(new[] { 1, 19, 27, 28, 29, 30, 42, 44, 45 },
			because: "a row title accepts text columns, and this is the DISPLAY-text subset of "
				+ "CreatioDataValueKind.Text — leaving out PhoneText/WebText/EmailText would give a contacts "
				+ "detail no title AND a note claiming the source had no acceptable column, which would be false");
		grid.DropProperties.Should().BeEquivalentTo(
			new[] { "columns", "primaryColumnName", "selectionState", "_selectionOptions", "features", "fitContent" },
			because: "these are the web grid's own properties, and mobile crt.List has no equivalent for any of them");
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
	[Description("The bundled grid → list component rule maps a web grid to [crt.List, crt.ListItem] and its note explains the crt.ListItem goes into the crt.List itemLayout.")]
	public void LoadBundled_GridRuleMapsToListAndListItem() {
		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.LoadBundled();

		ComponentEquivalenceRule grid = rules.Components.First(c => c.Web.Contains("crt.DataGrid"));
		grid.Mobile.Should().Contain("crt.List");
		grid.Mobile.Should().Contain("crt.ListItem");
		grid.Note.Should().Contain("itemLayout");
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
		rules.Components.Should().Contain(c => c.Web.Contains("crt.Checkbox"),
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
}
