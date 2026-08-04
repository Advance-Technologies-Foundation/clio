using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

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
	[Description("Bundled tabbed template carries container-name correspondence, the CardContentWrapper->AreaProfileContainer leftover mapping (non-tab content goes INSIDE the profile Area, never directly into the general tab's grid), and positional CardContentWrapper:top/:bottom -> Tabs:top/:bottom entries.")]
	public void LoadBundled_TemplatesCarryContainerCorrespondence() {
		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.LoadBundled();

		TemplateMappingRule tabbed = rules.Templates.First(t =>
			t.Web == "PageWithTabsFreedomTemplate" && t.Mobile == "MobilePageWithTabsFreedomTemplate");
		tabbed.Containers.Should().Contain(c => c.Web == "Tabs" && c.Mobile == "Tabs");
		tabbed.Containers.Should().Contain(c => c.Web == "FeedTabContainer" && c.Mobile == "FeedContainer");
		tabbed.Containers.Should().Contain(c => c.Web == "CardContentWrapper" && c.Mobile == "AreaProfileContainer",
			because: "the wrapper's non-tab (side/profile) content lands inside the profile Area card, " +
				"not directly in GeneralTabContainer — the Area must not be left empty");
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
	[Description("The bundled rules carry the empty-container removal allowlist (ENG-91228): the CLOSED set of five layout container types removable when empty. The set is a deliberate decision pinned here — widening it must be an explicit change with its own review, never a drive-by edit or registry inference.")]
	public void LoadBundled_EmptyContainerRemoval_CarriesClosedAllowlist() {
		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.LoadBundled();

		rules.EmptyContainerRemoval.Should().NotBeNull();
		rules.EmptyContainerRemoval.RemovableTypes.Should().BeEquivalentTo(
			["crt.FlexContainer", "crt.GridContainer", "crt.TabPanel", "crt.TabContainer", "crt.ExpansionPanel"],
			because: "the removable set is a closed allowlist of disposable layout scaffolding — content-bearing " +
				"containers (crt.List, crt.Tabs) must never appear here");
	}

	[Test]
	[Description("The bundled rules carry the converted-tab placement section: converted web tabs are indexed under the mobile Tabs starting right after the template's general tab (firstIndex 1), so the template's Feed/Attachments tabs stay last deterministically instead of by guidance prose.")]
	public void LoadBundled_ConvertedTabPlacement_CarriesTabsIndexing() {
		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.LoadBundled();

		rules.ConvertedTabPlacement.Should().NotBeNull();
		rules.ConvertedTabPlacement.TabsElementName.Should().Be("Tabs");
		rules.ConvertedTabPlacement.TabComponentType.Should().Be("crt.TabContainer");
		rules.ConvertedTabPlacement.FirstIndex.Should().Be(1,
			because: "position 0 belongs to the template's general tab — the first converted web tab goes right after it");
	}

	[Test]
	[Description("The bundled rules carry the designer's 2-layer tab body (tab-body grid + Area card) for converter-created tabs (ENG-94188).")]
	public void LoadBundled_TabAreaLayers_CarryDesignerTabBodyProps() {
		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.LoadBundled();

		rules.TabAreaLayers.Should().NotBeNull();
		rules.TabAreaLayers.TabComponentType.Should().Be("crt.TabContainer",
			because: "which element gets the layers is data, not a hardcoded type in the engine");
		rules.TabAreaLayers.MainTabContainer.NamePrefix.Should().Be("MainTabContainer_");
		rules.TabAreaLayers.MainTabContainer.Values["type"].GetString().Should().Be("crt.GridContainer");
		rules.TabAreaLayers.MainTabContainer.Values["padding"].GetProperty("bottom").GetString().Should().Be("medium");
		rules.TabAreaLayers.AreaContainer.NamePrefix.Should().Be("GridContainer_");
		rules.TabAreaLayers.AreaContainer.Values["type"].GetString().Should().Be("crt.GridContainer");
		rules.TabAreaLayers.AreaContainer.Values["color"].GetString().Should().Be("primary");
		rules.TabAreaLayers.AreaContainer.Values["borderRadius"].GetString().Should().Be("medium");
		rules.TabAreaLayers.DetailComponentTypes.Should().Equal(new[] { "crt.ExpansionPanel" },
			because: "an expansion panel is carried out of the shared Area into its own detail Area card (ENG-94188 AC#4)");
	}

	[Test]
	[Description("A rules file without the tabAreaLayers group parses to null — the tab-area pass is then a no-op (data-switched feature).")]
	public void ParseStream_WithoutTabAreaLayers_ParsesToNull() {
		const string json = """{ "version": "8.3.3", "templates": [], "components": [] }""";

		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.ParseStream(JsonStream(json));

		rules.TabAreaLayers.Should().BeNull();
	}

	[Test]
	[Description("ParseStream parses the tabAreaLayers group into the typed rule (prefixes + verbatim values).")]
	public void ParseStream_WithTabAreaLayers_ParsesTypedRule() {
		const string json = """
			{
			  "version": "8.3.3",
			  "tabAreaLayers": {
			    "note": "n",
			    "tabComponentType": "usr.CustomTab",
			    "mainTabContainer": { "namePrefix": "MainTabContainer_", "values": { "type": "crt.GridContainer", "alignItems": "stretch" } },
			    "areaContainer": { "namePrefix": "GridContainer_", "values": { "type": "crt.GridContainer", "color": "primary" } }
			  }
			}
			""";

		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.ParseStream(JsonStream(json));

		rules.TabAreaLayers.Should().NotBeNull();
		rules.TabAreaLayers.Note.Should().Be("n");
		rules.TabAreaLayers.TabComponentType.Should().Be("usr.CustomTab");
		rules.TabAreaLayers.MainTabContainer.NamePrefix.Should().Be("MainTabContainer_");
		rules.TabAreaLayers.MainTabContainer.Values["alignItems"].GetString().Should().Be("stretch");
		rules.TabAreaLayers.AreaContainer.NamePrefix.Should().Be("GridContainer_");
		rules.TabAreaLayers.AreaContainer.Values["color"].GetString().Should().Be("primary");
	}

	[Test]
	[Description("ParseStream parses the tabAreaLayers detailComponentTypes list; a group that omits it parses to an empty list, so an older rules file keeps the pre-detail behavior (every child goes into the shared Area).")]
	public void ParseStream_TabAreaLayersDetailComponentTypes_ParsesListAndDefaultsToEmpty() {
		const string withDetails = """
			{
			  "version": "8.3.3",
			  "tabAreaLayers": {
			    "tabComponentType": "crt.TabContainer",
			    "detailComponentTypes": ["crt.ExpansionPanel", "usr.CustomDetail"],
			    "mainTabContainer": { "namePrefix": "MainTabContainer_", "values": { "type": "crt.GridContainer" } },
			    "areaContainer": { "namePrefix": "GridContainer_", "values": { "type": "crt.GridContainer" } }
			  }
			}
			""";
		const string withoutDetails = """
			{
			  "version": "8.3.3",
			  "tabAreaLayers": {
			    "tabComponentType": "crt.TabContainer",
			    "mainTabContainer": { "namePrefix": "MainTabContainer_", "values": { "type": "crt.GridContainer" } },
			    "areaContainer": { "namePrefix": "GridContainer_", "values": { "type": "crt.GridContainer" } }
			  }
			}
			""";

		WebToMobilePageConversionRules parsedWith = WebToMobilePageConversionRulesCatalog.ParseStream(JsonStream(withDetails));
		WebToMobilePageConversionRules parsedWithout = WebToMobilePageConversionRulesCatalog.ParseStream(JsonStream(withoutDetails));

		parsedWith.TabAreaLayers.DetailComponentTypes.Should().Equal(new[] { "crt.ExpansionPanel", "usr.CustomDetail" },
			because: "the list is data — future detail-like types must join without an engine change");
		parsedWithout.TabAreaLayers.DetailComponentTypes.Should().BeEmpty(
			because: "an absent list must keep the pre-detail behavior instead of failing or wrapping anything");
	}

	[Test]
	[Description("A tabAreaLayers group that omits tabComponentType falls back to the platform's own tab type, so an older rules file keeps working.")]
	public void ParseStream_TabAreaLayersWithoutTabComponentType_FallsBackToPlatformTabType() {
		const string json = """
			{
			  "version": "8.3.3",
			  "tabAreaLayers": {
			    "mainTabContainer": { "namePrefix": "MainTabContainer_", "values": { "type": "crt.GridContainer" } },
			    "areaContainer": { "namePrefix": "GridContainer_", "values": { "type": "crt.GridContainer" } }
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
